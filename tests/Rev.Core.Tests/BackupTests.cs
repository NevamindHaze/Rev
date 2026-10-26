using Rev.Core.Backup;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public sealed class BackupTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rev-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RevDatabase db;

    public BackupTests()
    {
        Directory.CreateDirectory(folder);
        db = new RevDatabase(Path.Combine(folder, "rev.db"));
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private string SafetyFolder => Path.Combine(folder, "Backups");

    /// <summary>A small collection: two decks, a reviewed card, a cloze pair, a suspended leech, reviews and settings.</summary>
    private void Fill()
    {
        var pharm = db.AddDeck("Pharmacology", Now.AddDays(-10));
        var para = db.AddDeck("Parasitology", Now.AddDays(-9));
        db.AddNote(pharm.Id, CardKind.Basic, "Class of **meloxicam**?", "NSAID", ["pharm"], Now.AddDays(-8));
        db.AddNote(para.Id, CardKind.Cloze, "{{c1::Fasciola}} lives in the {{c2::liver}}", "Trematode", ["paper_1"], Now.AddDays(-7));
        var reviewed = db.GetCards(pharm.Id).Single();
        reviewed.SchedulingState = ReviewState() with { Lapses = 8, LastReviewed = Now.AddDays(-10).AddMilliseconds(123) };
        reviewed.IsLeech = true;
        reviewed.IsSuspended = true;
        db.UpdateCard(reviewed);
        db.AddReviewLog(new ReviewLog { CardId = reviewed.Id, Date = Now.AddHours(-1).AddMilliseconds(456), Rating = Rating.Again });
        db.AddReviewLog(new ReviewLog { CardId = reviewed.Id, Date = Now.AddDays(-3), Rating = Rating.Good, WasNew = true, IntervalDays = 3 });
        db.SaveSettings(new AppSettings { UserName = "Sam", NewCardsPerDay = 35, Scheduler = SchedulerKind.Sm2, DesiredRetention = 0.93 });
    }

    [Fact]
    public void ABackupRestoresEverythingExactly()
    {
        Fill();
        var cardsBefore = db.GetCards().OrderBy(c => c.Id).ToList();
        var logsBefore = db.GetReviewLogs();
        var decksBefore = db.GetDecks();
        string path = Path.Combine(folder, "backup.json");
        BackupService.Save(BackupService.Make(db, Now), path);

        // Change everything, then restore.
        db.DeleteDeck(decksBefore[0].Id);
        db.AddDeck("Something new");
        db.SaveSettings(new AppSettings());
        BackupService.Restore(db, BackupService.Load(path), SafetyFolder, Now);

        Assert.Equal(decksBefore.Select(d => (d.Id, d.Name, d.CreatedAt)), db.GetDecks().Select(d => (d.Id, d.Name, d.CreatedAt)));
        var cardsAfter = db.GetCards().OrderBy(c => c.Id).ToList();
        Assert.Equal(cardsBefore.Count, cardsAfter.Count);
        for (int i = 0; i < cardsBefore.Count; i++)
        {
            var (a, b) = (cardsBefore[i], cardsAfter[i]);
            Assert.Equal((a.Id, a.DeckId, a.Front, a.Back, a.Kind, a.TagsText, a.IsSuspended, a.IsLeech),
                         (b.Id, b.DeckId, b.Front, b.Back, b.Kind, b.TagsText, b.IsSuspended, b.IsLeech));
            Assert.Equal((a.ClozeSource, a.ClozeNumber, a.ClozeExtra, a.SyncKey, a.FieldsReadOnly),
                         (b.ClozeSource, b.ClozeNumber, b.ClozeExtra, b.SyncKey, b.FieldsReadOnly));
            Assert.Equal((a.CreatedAt, a.EditedAt, a.SchedChangedAt), (b.CreatedAt, b.EditedAt, b.SchedChangedAt));
            Assert.Equal(a.SchedulingState, b.SchedulingState);
        }
        Assert.Equal(logsBefore.Select(l => (l.Id, l.CardId, l.Date, l.Rating, l.WasNew, l.IntervalDays)),
                     db.GetReviewLogs().Select(l => (l.Id, l.CardId, l.Date, l.Rating, l.WasNew, l.IntervalDays)));
        Assert.Equal(new AppSettings { UserName = "Sam", NewCardsPerDay = 35, Scheduler = SchedulerKind.Sm2, DesiredRetention = 0.93 },
                     db.GetSettings());
    }

    [Fact]
    public void RestoreReplacesEverythingAndKeepsASafetyCopy()
    {
        Fill();
        var empty = new BackupFile { ExportedAt = BackupService.Seconds(Now) };
        string safety = BackupService.Restore(db, empty, SafetyFolder, Now);

        Assert.Empty(db.GetDecks());
        Assert.Empty(db.GetCards());
        Assert.Empty(db.GetReviewLogs());

        // The safety copy brings it all back.
        Assert.True(File.Exists(safety));
        BackupService.Restore(db, BackupService.Load(safety), SafetyFolder, Now.AddMinutes(1));
        Assert.Equal(2, db.GetDecks().Count);
        Assert.Equal(3, db.GetCards().Count);
        Assert.Equal(2, db.GetReviewLogs().Count);
    }

    [Fact]
    public void AFileThatIsNotABackupChangesNothing()
    {
        Fill();
        string notJson = Path.Combine(folder, "notes.json");
        File.WriteAllText(notJson, "this is not a backup");
        Assert.Throws<InvalidDataException>(() => BackupService.Load(notJson));

        string future = Path.Combine(folder, "future.json");
        File.WriteAllText(future, """{ "formatVersion": 2, "exportedAt": 0, "decks": [], "reviewLogs": [] }""");
        Assert.Contains("newer version", Assert.Throws<InvalidDataException>(() => BackupService.Load(future)).Message);

        Assert.Equal(3, db.GetCards().Count);
    }

    [Fact]
    public void ARestoreThatFailsHalfwayLeavesEverythingAsItWas()
    {
        Fill();
        var backup = BackupService.Make(db, Now);
        // Damage it: the same card twice, so saving the second copy fails partway through.
        backup.Decks[0].Cards.Add(backup.Decks[0].Cards[0]);

        Assert.ThrowsAny<Exception>(() => BackupService.Restore(db, backup, SafetyFolder, Now));
        Assert.Equal(2, db.GetDecks().Count);
        Assert.Equal(3, db.GetCards().Count);
        Assert.Equal(2, db.GetReviewLogs().Count);
        Assert.Equal("Sam", db.GetSettings().UserName);
    }

    [Fact]
    public void ABackupFromRevForIPhoneRestores()
    {
        // Written the way Swift's JSONEncoder writes it: upper-case ids, seconds since 1970, sorted keys.
        // The second card is from an older Rev: no noteOrd, editedAt or kind. The third shares its note.
        const string iphone = """
        {
          "decks" : [ {
            "cards" : [ {
              "back" : "NSAID", "createdAt" : 1760000000.25, "difficulty" : 5.5, "due" : 1761350400,
              "ease" : 2.5, "front" : "Meloxicam?", "id" : "6F9619FF-8B86-D011-B42D-00C04FC964FF",
              "intervalDays" : 12, "isLeech" : false, "isSuspended" : false, "kind" : "basic",
              "lapses" : 1, "lastReviewed" : 1760350400.5, "noteID" : "A1B2C3", "noteOrd" : 0,
              "phase" : "review", "reps" : 4, "stability" : 11.25, "step" : 0, "tags" : "pharm"
            }, {
              "back" : "Liver", "createdAt" : 1760000001, "due" : 1760000001, "ease" : 2.5,
              "front" : "Where does Fasciola live?", "id" : "7F9619FF-8B86-D011-B42D-00C04FC964FF",
              "intervalDays" : 0, "lapses" : 0, "noteID" : "anki-guid-1", "phase" : "new", "reps" : 0, "step" : 0
            }, {
              "back" : "Fasciola", "createdAt" : 1760000002, "due" : 1760000002, "ease" : 2.5,
              "front" : "What lives in the liver?", "id" : "8F9619FF-8B86-D011-B42D-00C04FC964FF",
              "intervalDays" : 0, "lapses" : 0, "noteID" : "anki-guid-1", "phase" : "new", "reps" : 0, "step" : 0
            } ],
            "createdAt" : 1759999999, "id" : "1F9619FF-8B86-D011-B42D-00C04FC964FF", "name" : "Vet › Pharm"
          } ],
          "exportedAt" : 1761400000, "formatVersion" : 1, "newCardsPerDay" : 15,
          "reviewLogs" : [ {
            "cardID" : "6F9619FF-8B86-D011-B42D-00C04FC964FF", "date" : 1760350400.5, "intervalDays" : 12,
            "rating" : 3, "wasNew" : false
          } ],
          "userName" : "Sam"
        }
        """;
        string path = Path.Combine(folder, "iphone.json");
        File.WriteAllText(path, iphone);
        BackupService.Restore(db, BackupService.Load(path), SafetyFolder, Now);

        var deck = db.GetDecks().Single();
        Assert.Equal("Vet › Pharm", deck.Name);
        var cards = db.GetCards().OrderBy(c => c.CreatedAt).ToList();
        Assert.Equal(3, cards.Count);

        var meloxicam = cards[0];
        Assert.Equal(Guid.Parse("6F9619FF-8B86-D011-B42D-00C04FC964FF"), meloxicam.Id);
        Assert.Equal((CardPhase.Review, 12.0, 11.25, 5.5, "pharm"), (meloxicam.Phase, meloxicam.IntervalDays, meloxicam.Stability!.Value, meloxicam.Difficulty!.Value, meloxicam.TagsText));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1760350400500).LocalDateTime, meloxicam.LastReviewed);
        Assert.Equal("A1B2C3:0", meloxicam.SyncKey);

        // The two cards of one note without an ord still get different identities.
        Assert.Equal(["anki-guid-1:0", "anki-guid-1:1"], cards.Skip(1).Select(c => c.SyncKey));
        Assert.Equal(CardKind.Basic, cards[1].Kind);
        Assert.Equal(cards[1].CreatedAt, cards[1].EditedAt);

        var log = db.GetReviewLogs().Single();
        Assert.Equal(meloxicam.Id, log.CardId);
        Assert.Equal(Rating.Good, log.Rating);
        Assert.Equal(("Sam", 15), (db.GetSettings().UserName, db.GetSettings().NewCardsPerDay));
    }

    [Fact]
    public void BackupsUseTheIPhoneFieldNames()
    {
        Fill();
        string path = Path.Combine(folder, "backup.json");
        BackupService.Save(BackupService.Make(db, Now), path);
        string json = File.ReadAllText(path);
        foreach (var name in new[] { "\"formatVersion\"", "\"exportedAt\"", "\"reviewLogs\"", "\"cardID\"", "\"noteID\"",
                                     "\"noteOrd\"", "\"isSuspended\"", "\"isLeech\"", "\"schedChangedAt\"", "\"intervalDays\"" })
            Assert.Contains(name, json);
        Assert.Equal("Rev backup 2026-10-26 1500.json", BackupService.SuggestedFileName(Now));
    }
}
