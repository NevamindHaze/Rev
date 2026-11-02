using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public class CardFactoryTests
{
    private static readonly Guid DeckId = Guid.NewGuid();

    [Fact]
    public void BasicAndTypeInMakeOneCard()
    {
        var basic = Assert.Single(CardFactory.Create(CardKind.Basic, "Q", "A", ["pharm"], DeckId, Now));
        Assert.Equal(("Q", "A", 0, "pharm"), (basic.Front, basic.Back, basic.NoteOrd, basic.TagsText));
        Assert.Equal(CardKind.TypeIn, Assert.Single(CardFactory.Create(CardKind.TypeIn, "Q", "A", [], DeckId, Now)).Kind);
    }

    [Fact]
    public void ReversedMakesTwoCardsOfOneNote()
    {
        var cards = CardFactory.Create(CardKind.Reversed, "Meloxicam", "NSAID", [], DeckId, Now);
        Assert.Equal(2, cards.Count);
        Assert.Equal(("Meloxicam", "NSAID", 0), (cards[0].Front, cards[0].Back, cards[0].NoteOrd));
        Assert.Equal(("NSAID", "Meloxicam", 1), (cards[1].Front, cards[1].Back, cards[1].NoteOrd));
        Assert.Equal(cards[0].NoteId, cards[1].NoteId);
        Assert.True(Guid.TryParse(cards[0].NoteId, out _));   // notes made in Rev get a UUID
        Assert.True(cards[0].CreatedAt < cards[1].CreatedAt);
    }

    [Fact]
    public void ClozeMakesOneCardPerNumberWithAnkisOrds()
    {
        var cards = CardFactory.Create(CardKind.Cloze, "{{c1::Fasciola}} lives in the {{c2::liver}}", "Trematode", [], DeckId, Now);
        Assert.Equal([0, 1], cards.Select(c => c.NoteOrd));
        Assert.Equal([1, 2], cards.Select(c => c.ClozeNumber!.Value));
        Assert.Equal("[...] lives in the liver", cards[0].Front);
        Assert.Equal("[Fasciola] lives in the liver\n\nTrematode", cards[0].Back);
        Assert.Equal("Fasciola lives in the [liver]\n\nTrematode", cards[1].Back);
        Assert.Single(cards.Select(c => c.NoteId).Distinct());
    }

    [Fact]
    public void ClozeWithoutHiddenWordsMakesNoCards() =>
        Assert.Empty(CardFactory.Create(CardKind.Cloze, "nothing hidden", "", [], DeckId, Now));
}

public sealed class NoteEditingTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rev-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RevDatabase db;
    private readonly Deck deck;

    public NoteEditingTests()
    {
        db = new RevDatabase(Path.Combine(folder, "rev.db"));
        deck = db.AddDeck("Parasitology");
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void AddingANoteSavesAllItsCards()
    {
        Assert.Equal(2, db.AddNote(deck.Id, CardKind.Reversed, "  Front ", "Back  ", ["a", "b"]));
        var cards = db.GetCards(deck.Id);
        Assert.Equal(2, cards.Count);
        Assert.All(cards, c => Assert.Equal("a b", c.TagsText));
        Assert.Equal("Front", cards[0].Front);
    }

    [Fact]
    public void PastedCardsKeepTheirOrder()
    {
        db.AddPasted(deck.Id, [("one", "1"), ("two", "2"), ("three", "3")], Now);
        Assert.Equal(["one", "two", "three"], db.GetCards(deck.Id).Select(c => c.Front));
    }

    [Fact]
    public void EditingAClozeKeepsProgressAddsNewNumbersAndRemovesOldOnes()
    {
        db.AddNote(deck.Id, CardKind.Cloze, "{{c1::Fasciola}} lives in the {{c2::liver}}", "", []);
        var c1 = db.GetCards(deck.Id).Single(c => c.ClozeNumber == 1);
        c1.SchedulingState = ReviewState();
        db.UpdateCard(c1);

        // c2 removed, c3 added, c1's text changed.
        db.SaveEdit(c1, "{{c1::Fasciola hepatica}} needs a {{c3::snail}}", "extra", ["trematodes"], Now);

        var cards = db.GetCards(deck.Id).OrderBy(c => c.ClozeNumber).ToList();
        Assert.Equal([1, 3], cards.Select(c => c.ClozeNumber!.Value));
        Assert.Equal([0, 2], cards.Select(c => c.NoteOrd));
        Assert.Equal("[...] needs a snail", cards[0].Front);
        Assert.Equal(CardPhase.Review, cards[0].Phase);          // progress kept
        Assert.Equal(CardPhase.New, cards[1].Phase);
        Assert.All(cards, c => Assert.Equal("trematodes", c.TagsText));
        Assert.All(cards, c => Assert.Equal(c1.NoteId, c.NoteId));
    }

    [Fact]
    public void EditingAReversedCardUpdatesItsTwin()
    {
        db.AddNote(deck.Id, CardKind.Reversed, "Meloxicam", "NSAID", []);
        var forward = db.GetCards(deck.Id).Single(c => c.NoteOrd == 0);
        db.SaveEdit(forward, "Carprofen", "NSAID (COX-2)", ["pharm"], Now);

        var twin = db.GetCards(deck.Id).Single(c => c.NoteOrd == 1);
        Assert.Equal(("NSAID (COX-2)", "Carprofen", "pharm"), (twin.Front, twin.Back, twin.TagsText));
    }

    [Fact]
    public void EditingMarksTheCardEditedButNotRescheduled()
    {
        db.AddNote(deck.Id, CardKind.Basic, "Q", "A", [], Now.AddDays(-2));
        var card = db.GetCards(deck.Id).Single();
        db.SaveEdit(card, "Q2", "A2", [], Now);

        var saved = db.GetCards(deck.Id).Single();
        Assert.Equal("Q2", saved.Front);
        Assert.True(saved.EditedAt > saved.SchedChangedAt);
    }

    [Fact]
    public void PreferencesAreRemembered()
    {
        Assert.Null(db.GetPreference("lastCardKind"));
        db.SetPreference("lastCardKind", "cloze");
        Assert.Equal("cloze", db.GetPreference("lastCardKind"));
    }
}
