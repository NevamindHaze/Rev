using System.Text.Json;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Sync;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

/// <summary>Rev for Windows' side of the sync, against hand-made anki.json files in a temporary folder.</summary>
public sealed class SyncTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "rev-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RevDatabase db;
    private readonly SyncService sync;
    private string Folder => Path.Combine(root, "Rev Sync");
    private string Media => Path.Combine(root, "Media");
    private readonly Deck deck;

    public SyncTests()
    {
        Directory.CreateDirectory(Folder);
        db = new RevDatabase(Path.Combine(root, "rev.db"));
        sync = new SyncService(db, Folder, Path.Combine(root, "sync-state.json"), Media);
        deck = db.AddDeck("Vet › Pharmacology", Now.AddDays(-30));
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(root, recursive: true);
    }

    private static double S(DateTime t) => SyncMerger.Seconds(t);

    private SyncSnapshot WindowsFile() =>
        JsonSerializer.Deserialize<SyncSnapshot>(File.ReadAllBytes(Path.Combine(Folder, "windows.json")))!;

    private void WriteAnki(double writtenAt, IEnumerable<SyncCard> cards, IEnumerable<SyncTombstone>? deleted = null,
                           IEnumerable<SyncReview>? reviews = null) =>
        File.WriteAllBytes(Path.Combine(Folder, "anki.json"), JsonSerializer.SerializeToUtf8Bytes(new SyncSnapshot
        {
            Writer = "anki", WrittenAt = writtenAt, Cards = cards.ToList(),
            Deleted = (deleted ?? []).ToList(), Reviews = (reviews ?? []).ToList(),
        }));

    /// <summary>A card as the add-on would write it in anki.json (Anki's text is HTML).</summary>
    private static SyncCard AnkiCard(string noteId, int ord, string front, string back, double editedAt, double schedAt,
                                     string deck = "Vet::Pharmacology", SyncSchedule? sched = null, string kind = "basic") => new()
    {
        Key = $"{noteId}:{ord}", NoteId = noteId, Ord = ord, Deck = deck, Kind = kind, Front = front, Back = back,
        Tags = ["pharm"], EditedAt = editedAt, SchedAt = schedAt, Sched = sched ?? new SyncSchedule(),
    };

    private Card AddCard(string front, string back, DateTime changed)
    {
        db.AddNote(deck.Id, CardKind.Basic, front, back, ["pharm"], changed);
        return db.GetCards().Single(c => c.Front == front);
    }

    [Fact]
    public void WindowsFileHasEveryCardInTheSharedFormat()
    {
        var card = AddCard("Class of **meloxicam**?", "NSAID", Now);
        db.AddNote(deck.Id, CardKind.Reversed, "Atipamezole", "Reverses medetomidine", [], Now);
        sync.Run(Now);

        var file = WindowsFile();
        Assert.Equal(("windows", 1, 3), (file.Writer, file.Format, file.Cards.Count));
        var sent = file.Cards.Single(c => c.Key == card.SyncKey);
        Assert.Equal(("Vet::Pharmacology", "basic", "Class of **meloxicam**?"), (sent.Deck, sent.Kind, sent.Front));
        Assert.Equal("Class of <b>meloxicam</b>?", sent.Fields!["Front"]);
        Assert.Equal(S(card.EditedAt), sent.EditedAt, 3);
        var reverse = file.Cards.Single(c => c.Front == "Reverses medetomidine");
        Assert.Equal(("reversed", "Atipamezole", "Reverses medetomidine"), (reverse.Kind, reverse.Fields!["Front"], reverse.Fields["Back"]));
    }

    [Fact]
    public void TheFileIsOnlyRewrittenWhenSomethingChanged()
    {
        var card = AddCard("Q", "A", Now);
        sync.Run(Now);
        double first = WindowsFile().WrittenAt;
        sync.Run(Now.AddMinutes(1));
        Assert.Equal(first, WindowsFile().WrittenAt);

        db.SaveEdit(card, "Q", "A2", [], Now.AddMinutes(2));
        sync.Run(Now.AddMinutes(2));
        Assert.True(WindowsFile().WrittenAt > first);
    }

    [Fact]
    public void NewerTextAndScheduleFromAnkiAreAppliedAndOlderOnesIgnored()
    {
        var newer = AddCard("Old question", "Old answer", Now.AddDays(-2));
        var older = AddCard("Kept question", "Kept answer", Now);
        var review = new SyncSchedule { Phase = "review", Due = S(TodayStart.AddDays(6)), Interval = 6, Ease = 2.4, Reps = 3, Stability = 6.5, Difficulty = 4.5 };
        WriteAnki(1000, [
            AnkiCard(newer.NoteId, 0, "New <b>question</b>", "<ul><li>one</li><li>two</li></ul>", S(Now.AddDays(-1)), S(Now.AddDays(-1)), sched: review),
            AnkiCard(older.NoteId, 0, "Anki's older text", "x", S(Now.AddDays(-3)), S(Now.AddDays(-3)), sched: review),
        ]);

        var report = sync.Run(Now);
        Assert.Equal((1, 1), (report.Updated, report.Rescheduled));
        var updated = db.GetCard(newer.Id)!;
        Assert.Equal(("New **question**", "• one\n• two"), (updated.Front, updated.Back));
        Assert.Equal((CardPhase.Review, 6.0, 6.5), (updated.Phase, updated.IntervalDays, updated.Stability!.Value));
        Assert.Equal(TodayStart.AddDays(6), updated.Due);
        Assert.Equal("Kept answer", db.GetCard(older.Id)!.Back);
        Assert.Equal(CardPhase.New, db.GetCard(older.Id)!.Phase);
    }

    [Fact]
    public void AnkiReportingWindowsOwnChangeBackChangesNothing()
    {
        var card = AddCard("Q", "A", Now);
        sync.Run(Now);
        var sent = WindowsFile().Cards.Single();
        // The add-on applied it and reports Windows' own time back (that's how it avoids echoes).
        WriteAnki(1000, [AnkiCard(card.NoteId, 0, "Q", "A", sent.EditedAt, sent.SchedAt)]);
        var report = sync.Run(Now.AddMinutes(1));
        Assert.False(report.Any);
    }

    [Fact]
    public void NewCardsAndDecksFromAnkiArrive()
    {
        WriteAnki(1000, [
            AnkiCard("anki-guid-1", 0, "{{c1::x}}", "y", S(Now), S(Now), deck: "Vet::Surgery", kind: "cloze"),
            AnkiCard("anki-guid-2", 0, "Custom", "Note type", S(Now), S(Now), deck: "Vet::Surgery", kind: "other"),
        ]);
        var report = sync.Run(Now);
        Assert.Equal(2, report.Added);
        var surgery = db.GetDecks().Single(d => d.Name == "Vet › Surgery");
        var cards = db.GetCards(surgery.Id);
        Assert.Equal(2, cards.Count);
        Assert.Equal(CardKind.Cloze, cards.Single(c => c.NoteId == "anki-guid-1").Kind);
        Assert.True(cards.Single(c => c.NoteId == "anki-guid-2").FieldsReadOnly);
        Assert.All(cards, c => Assert.Equal(S(Now), S(c.EditedAt), 3));
    }

    [Fact]
    public void ADeletionInAnkiLosesToANewerChangeInRev()
    {
        var keep = AddCard("Keep", "A", Now);
        var drop = AddCard("Drop", "A", Now.AddDays(-5));
        WriteAnki(1000, [], deleted: [
            new SyncTombstone { Key = keep.SyncKey, At = S(Now.AddMinutes(-5)) },
            new SyncTombstone { Key = drop.SyncKey, At = S(Now.AddMinutes(-5)) },
        ]);
        var report = sync.Run(Now);
        Assert.Equal(1, report.Deleted);
        Assert.NotNull(db.GetCard(keep.Id));
        Assert.Null(db.GetCard(drop.Id));
    }

    [Fact]
    public void ADeckEmptiedByDeletionsInAnkiIsRemovedButOtherEmptyDecksStay()
    {
        var gone = db.AddDeck("Nematodes", Now.AddDays(-3));
        db.AddNote(gone.Id, CardKind.Basic, "Ascaris host?", "Pig", [], Now.AddDays(-3));
        var mine = db.AddDeck("New and empty", Now);
        var half = db.AddDeck("Cestodes", Now.AddDays(-3));
        db.AddNote(half.Id, CardKind.Basic, "Taenia?", "Tapeworm", [], Now.AddDays(-3));
        db.AddNote(half.Id, CardKind.Basic, "Echinococcus?", "Hydatid", [], Now.AddDays(-3));
        var parent = db.AddDeck("Parasites", Now.AddDays(-3));
        var child = db.AddDeck("Parasites › Flukes", Now.AddDays(-3));
        db.AddNote(parent.Id, CardKind.Basic, "Parasite?", "Yes", [], Now.AddDays(-3));
        db.AddNote(child.Id, CardKind.Basic, "Fasciola?", "Liver fluke", [], Now.AddDays(-3));
        db.AddNote(deck.Id, CardKind.Basic, "Moved", "A", [], Now.AddDays(-3));
        var keyOf = db.GetCards().ToDictionary(c => c.Front, c => c.SyncKey);
        var moved = db.GetCards().Single(c => c.Front == "Moved");
        double at = S(Now.AddMinutes(-5));

        WriteAnki(1000, [AnkiCard(moved.NoteId, 0, "Moved", "A", at, S(moved.SchedChangedAt), deck: "Renamed")], deleted: [
            new SyncTombstone { Key = keyOf["Ascaris host?"], At = at },
            new SyncTombstone { Key = keyOf["Taenia?"], At = at },
            new SyncTombstone { Key = keyOf["Fasciola?"], At = at },
            new SyncTombstone { Key = keyOf["Parasite?"], At = at },
        ]);
        sync.Run(Now);

        var names = db.GetDecks().Select(d => d.Name).Order().ToList();
        // Nematodes, both Parasites decks and the deck "Moved" left are gone; the others stay.
        Assert.Equal(["Cestodes", "New and empty", "Renamed"], names);
    }

    [Fact]
    public void ACardDeletedInRevIsNotBroughtBackAndIsSentAsATombstone()
    {
        var card = AddCard("Q", "A", Now.AddDays(-1));
        sync.Run(Now.AddDays(-1));
        db.DeleteCard(card.Id);
        // Anki hasn't heard yet and still has it.
        WriteAnki(1000, [AnkiCard(card.NoteId, 0, "Q", "A", S(Now.AddDays(-1)), S(Now.AddDays(-1)))]);
        var report = sync.Run(Now);
        Assert.Equal(0, report.Added);
        Assert.Empty(db.GetCards());
        var tombstone = Assert.Single(WindowsFile().Deleted);
        Assert.Equal(card.SyncKey, tombstone.Key);
    }

    [Fact]
    public void ReviewsTravelBothWaysAndAreAddedOnce()
    {
        var card = AddCard("Q", "A", Now.AddDays(-1));
        db.AddReviewLog(new ReviewLog { CardId = card.Id, Date = Now.AddMinutes(-10).AddMilliseconds(5), Rating = Rating.Good });
        sync.Run(Now);
        var sent = Assert.Single(WindowsFile().Reviews);
        Assert.Equal((card.SyncKey, 3), (sent.Key, sent.Rating));

        long fromAnki = RevDatabase.ToMs(Now.AddMinutes(-2));
        WriteAnki(1000, [AnkiCard(card.NoteId, 0, "Q", "A", S(card.EditedAt), S(card.SchedChangedAt))],
                  reviews: [sent, new SyncReview { Key = card.SyncKey, At = fromAnki, Rating = 1, Interval = 0 }]);
        var report = sync.Run(Now);
        Assert.Equal(1, report.Reviews);   // Windows' own review came back and isn't added again
        Assert.Equal(2, db.GetReviewLogs().Count);
    }

    [Fact]
    public void PicturesAreCopiedBothWays()
    {
        Directory.CreateDirectory(Media);
        File.WriteAllText(Path.Combine(Media, "mine.png"), "png");
        db.AddNote(deck.Id, CardKind.Basic, "Mine", "[img:mine.png]", [], Now);
        Directory.CreateDirectory(Path.Combine(Folder, "media"));
        File.WriteAllText(Path.Combine(Folder, "media", "theirs.png"), "png");
        WriteAnki(1000, [AnkiCard("g", 0, "Theirs", "<img src=\"theirs.png\">", S(Now), S(Now))]);

        sync.Run(Now);
        Assert.True(File.Exists(Path.Combine(Folder, "media", "mine.png")));
        Assert.True(File.Exists(Path.Combine(Media, "theirs.png")));
    }

    [Fact]
    public void ThePreviewChangesNothing()
    {
        AddCard("Q", "A", Now.AddDays(-2));
        WriteAnki(1000, [AnkiCard("anki-guid-1", 0, "New", "card", S(Now), S(Now))]);
        var (fromAnki, toSend) = sync.Preview(Now);
        Assert.Equal((1, 1), (fromAnki.Added, toSend));
        Assert.Single(db.GetCards());
        Assert.False(File.Exists(Path.Combine(Folder, "windows.json")));
        Assert.Equal(1, sync.Run(Now).Added);   // and the real sync then does it
    }

    [Fact]
    public void SummaryReadsLikeTheIPhone()
    {
        Assert.Equal("Everything is up to date.", new SyncReport().Summary);
        Assert.Equal("1 new card, 2 cards updated, 3 reviews from Anki.", new SyncReport(Added: 1, Updated: 2, Reviews: 3).Summary);
    }
}
