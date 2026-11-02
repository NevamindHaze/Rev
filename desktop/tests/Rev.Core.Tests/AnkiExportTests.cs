using Rev.Core.Anki;
using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

/// <summary>Exports a small collection to .apkg, then reads the file back with Rev's own Anki reader.</summary>
public sealed class AnkiExportTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rev-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RevDatabase db;
    private string Media => Path.Combine(folder, "Media");
    private string Package => Path.Combine(folder, "export.apkg");

    public AnkiExportTests()
    {
        db = new RevDatabase(Path.Combine(folder, "rev.db"));
        Directory.CreateDirectory(Media);
        File.WriteAllBytes(Path.Combine(Media, "heart.png"), File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "vet-legacy.apkg"))[..100]);
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private Deck Fill()
    {
        var deck = db.AddDeck("Vet › Pharmacology", Now.AddDays(-10));
        db.AddNote(deck.Id, CardKind.Basic, "Class of **meloxicam**?", "NSAID (*COX-2*)", ["pharm", "paper_1"], Now.AddDays(-9));
        db.AddNote(deck.Id, CardKind.Basic, "Signs?", "• vomiting\n• ulcers", [], Now.AddDays(-8));
        db.AddNote(deck.Id, CardKind.Basic, "What does this show?", "[img:heart.png]\nA heart & lungs <3", [], Now.AddDays(-7));
        db.AddNote(deck.Id, CardKind.Basic, "Reviewed card", "A", [], Now.AddDays(-6));
        var reviewed = db.GetCards(deck.Id).Single(c => c.Front == "Reviewed card");
        reviewed.SchedulingState = new SchedulingState
        {
            Phase = CardPhase.Review, IntervalDays = 12, Ease = 2.3, Reps = 6, Lapses = 2, Stability = 11.5, Difficulty = 6.25,
            Due = TodayStart.AddDays(5), LastReviewed = TodayStart.AddDays(-7),
        };
        reviewed.IsLeech = true;
        reviewed.IsSuspended = true;
        db.UpdateCard(reviewed);
        return deck;
    }

    private ApkgContents ExportAndReadBack()
    {
        var result = ApkgWriter.Write(Package, ApkgWriter.AllDecks(db), Media, Now);
        Assert.Equal((1, 4, 1), (result.Decks, result.Cards, result.Images));
        return ApkgReader.Read(Package, Path.Combine(folder, "Media2"), Now);
    }

    private static Card One(ApkgContents contents, string front) => contents.Decks.SelectMany(d => d.Cards).Single(c => c.Front == front);

    [Fact]
    public void DecksAndTextComeBackTheSame()
    {
        Fill();
        var back = ExportAndReadBack();
        Assert.Equal("Vet › Pharmacology", back.Decks.Single().Name);
        Assert.Equal("NSAID (*COX-2*)", One(back, "Class of **meloxicam**?").Back);
        Assert.Equal("• vomiting\n• ulcers", One(back, "Signs?").Back);
        Assert.Equal("[img:heart.png]\nA heart & lungs <3", One(back, "What does this show?").Back);
        Assert.Equal("pharm paper_1", One(back, "Class of **meloxicam**?").TagsText);
    }

    [Fact]
    public void ImagesAreInsideThePackage()
    {
        Fill();
        var back = ExportAndReadBack();
        Assert.Equal(1, back.ImagesSaved);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Media, "heart.png")), File.ReadAllBytes(Path.Combine(folder, "Media2", "heart.png")));
    }

    [Fact]
    public void ScheduleLeechAndSuspendedComeAcross()
    {
        Fill();
        var card = One(ExportAndReadBack(), "Reviewed card");
        Assert.Equal((CardPhase.Review, 12.0, 2.3, 6, 2), (card.Phase, card.IntervalDays, card.Ease, card.Reps, card.Lapses));
        Assert.Equal(TodayStart.AddDays(5).Date, card.Due.StartOfStudyDay().Date);
        Assert.Equal((11.5, 6.25), (card.Stability, card.Difficulty));
        Assert.True(card.IsLeech);
        Assert.True(card.IsSuspended);
        Assert.Equal("", card.TagsText);   // "leech" is a mark in Rev, not a tag
    }

    [Fact]
    public void NewCardsStayNewAndInOrder()
    {
        Fill();
        var cards = ExportAndReadBack().Decks.Single().Cards.Where(c => c.Phase == CardPhase.New).ToList();
        Assert.Equal(["Class of **meloxicam**?", "Signs?", "What does this show?"], cards.OrderBy(c => c.CreatedAt).Select(c => c.Front));
    }

    [Fact]
    public void ImportingTheExportBackIntoRevChangesNothing()
    {
        Fill();
        ApkgWriter.Write(Package, ApkgWriter.AllDecks(db), Media, Now);
        var result = AnkiImporter.Import(db, ApkgReader.Read(Package, Path.Combine(folder, "Media2"), Now), Now);
        Assert.Equal((0, 0, 0, 4), (result.DecksCreated, result.Added, result.Updated, result.Unchanged));
    }

    [Fact]
    public void ExportingAgainReplacesTheFileAndKeepsTheSameNoteIds()
    {
        Fill();
        ApkgWriter.Write(Package, ApkgWriter.AllDecks(db), Media, Now);
        var first = ApkgReader.Read(Package, Path.Combine(folder, "Media2"), Now).Decks.Single().Cards.Select(c => c.NoteId).Order().ToList();
        ApkgWriter.Write(Package, ApkgWriter.AllDecks(db), Media, Now.AddMinutes(5));
        var second = ApkgReader.Read(Package, Path.Combine(folder, "Media2"), Now).Decks.Single().Cards.Select(c => c.NoteId).Order().ToList();
        Assert.Equal(first, second);   // so Anki updates these notes instead of adding copies
        Assert.False(File.Exists(Package + ".part"));
    }
}
