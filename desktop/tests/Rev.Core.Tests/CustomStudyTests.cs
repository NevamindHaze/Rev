using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Review;
using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public sealed class CustomStudyTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rev-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RevDatabase db;
    private readonly Deck deck;

    public CustomStudyTests()
    {
        db = new RevDatabase(Path.Combine(folder, "rev.db"));
        deck = db.AddDeck("Pharmacology");
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private ReviewSession Session(CustomStudy custom) => new(db, deck.Id, () => Now, custom);

    private Card AddCard(string front, SchedulingState state, string tags = "", bool suspended = false)
    {
        var card = new Card
        {
            DeckId = deck.Id, Front = front, Back = "A", TagsText = tags, IsSuspended = suspended,
            CreatedAt = Now.AddDays(-30).AddMinutes(db.GetCards().Count),
        };
        card.SchedulingState = state;
        db.AddCard(card);
        return card;
    }

    private Card AddNew(string front, string tags = "", bool suspended = false) =>
        AddCard(front, NewState(), tags, suspended);

    private List<string> Picked(CustomStudy custom) =>
        custom.PickCards(db, db.GetCards(deck.Id), Now, new Random(1)).Select(c => c.Front).ToList();

    [Fact]
    public void MoreNewCardsGoesPastTheDailyLimitOldestFirst()
    {
        db.SaveSettings(new AppSettings { NewCardsPerDay = 0 });
        AddNew("Q1");
        AddNew("Q2");
        AddNew("Q3");
        AddNew("Q4", suspended: true);

        Assert.Equal(["Q1", "Q2"], Picked(new CustomStudy { Kind = CustomStudyKind.MoreNew, Limit = 2 }));
        Assert.Null(new ReviewSession(db, deck.Id, () => Now).Current);   // the normal queue has none
        Assert.Equal("Q1", Session(new CustomStudy { Kind = CustomStudyKind.MoreNew, Limit = 2 }).Current!.Front);
    }

    [Fact]
    public void ForgottenCardsAreTheOnesYouPressedAgainOn()
    {
        var forgotten = AddCard("Forgot", ReviewState(seed: 1));
        var remembered = AddCard("Remembered", ReviewState(seed: 2));
        var longAgo = AddCard("Long ago", ReviewState(seed: 3));
        db.AddReviewLog(new ReviewLog { CardId = forgotten.Id, Date = Now.AddHours(-1), Rating = Rating.Again });
        db.AddReviewLog(new ReviewLog { CardId = remembered.Id, Date = Now.AddHours(-1), Rating = Rating.Good });
        db.AddReviewLog(new ReviewLog { CardId = longAgo.Id, Date = Now.AddDays(-5), Rating = Rating.Again });

        Assert.Equal(["Forgot"], Picked(new CustomStudy { Kind = CustomStudyKind.Forgotten, Days = 1 }));
        Assert.Equal(["Forgot", "Long ago"],
            Picked(new CustomStudy { Kind = CustomStudyKind.Forgotten, Days = 7 }).Order().ToList());
    }

    [Fact]
    public void ReviewAheadTakesCardsDueInTheNextFewDaysSoonestFirst()
    {
        AddCard("Today", ReviewState());
        AddCard("In 3 days", ReviewState() with { Due = TodayStart.AddDays(3) });
        AddCard("Tomorrow", ReviewState() with { Due = TodayStart.AddDays(1).AddHours(6) });
        AddCard("Next week", ReviewState() with { Due = TodayStart.AddDays(8) });
        AddNew("New");

        Assert.Equal(["Tomorrow"], Picked(new CustomStudy { Kind = CustomStudyKind.ReviewAhead, Days = 1 }));
        Assert.Equal(["Tomorrow", "In 3 days"], Picked(new CustomStudy { Kind = CustomStudyKind.ReviewAhead, Days = 3 }));
    }

    [Fact]
    public void StudyByTagFiltersByTagAndKindOfCard()
    {
        AddNew("New parasite", "parasitology");
        AddCard("Old parasite", ReviewState() with { Due = TodayStart.AddDays(10) }, "Parasitology paper_1");
        AddNew("Drug", "pharmacology");
        AddNew("Untagged");

        var byTag = new CustomStudy { Kind = CustomStudyKind.ByTag, Tags = ["PARASITOLOGY"], Shuffled = false };
        Assert.Equal(["New parasite", "Old parasite"], Picked(byTag));
        Assert.Equal(["New parasite"], Picked(byTag with { TagCards = TagCards.NewOnly }));
        Assert.Equal(["Old parasite"], Picked(byTag with { TagCards = TagCards.StudiedOnly }));
        Assert.Equal(["New parasite"], Picked(byTag with { Limit = 1 }));
        Assert.Equal(4, Picked(byTag with { Tags = [] }).Count);   // no tags picked: any card
        Assert.Equal(["paper_1", "parasitology", "pharmacology"],
            CustomStudy.AllTags(db.GetCards()).Select(t => t.ToLowerInvariant()).ToList());
    }

    [Fact]
    public void ASessionShowsOnlyItsCardsAndForgottenOnesComeBack()
    {
        AddNew("Q1", "exam");
        AddNew("Q2", "exam");
        AddNew("Other");
        var session = Session(new CustomStudy { Kind = CustomStudyKind.ByTag, Tags = ["exam"], Shuffled = false });
        Assert.Equal(2, session.Queue.NewCards.Count);

        session.Rate(Rating.Easy);    // Q1 graduates: done for this session
        session.Rate(Rating.Again);   // Q2 comes back in a minute, like a normal session
        Assert.Equal("Q2", session.Current!.Front);
        Assert.Empty(session.Queue.NewCards);

        session.Rate(Rating.Easy);
        Assert.Null(session.Current);
        Assert.True(session.HasAnyCards);
        Assert.Equal(3, db.GetReviewLogs().Count);   // answers count as normal reviews
    }

    [Fact]
    public void UndoInACustomSessionPutsTheCardBack()
    {
        AddNew("Q1");
        var session = Session(new CustomStudy { Kind = CustomStudyKind.MoreNew, Limit = 5 });
        session.Rate(Rating.Easy);
        Assert.Null(session.Current);

        session.Undo();
        Assert.Equal("Q1", session.Current!.Front);
        Assert.Single(session.Queue.NewCards);
    }

    [Fact]
    public void WhenNothingMatchesTheReasonIsExplained()
    {
        AddCard("Learned", ReviewState() with { Due = TodayStart.AddDays(21) });
        var cards = db.GetCards(deck.Id);

        Assert.Contains("no new cards left in this deck",
            new CustomStudy { Kind = CustomStudyKind.MoreNew }.EmptyReason(cards, "this deck", Now));
        Assert.Contains("The next card is due in 21 days",
            new CustomStudy { Kind = CustomStudyKind.ReviewAhead }.EmptyReason(cards, "this deck", Now));
        Assert.Contains("last 3 days",
            new CustomStudy { Kind = CustomStudyKind.Forgotten, Days = 3 }.EmptyReason(cards, "this deck", Now));
        // "Study by tag" with no tag picked takes every card, so it has something here.
        Assert.Equal(["Learned"], Picked(new CustomStudy { Kind = CustomStudyKind.ByTag }));
    }

    [Fact]
    public void DaysGoByOneForAWeekThenByWeeksUpToAYear()
    {
        Assert.Equal(2, CustomStudy.StepDays(1, up: true));
        Assert.Equal(14, CustomStudy.StepDays(7, up: true));
        Assert.Equal(365, CustomStudy.StepDays(364, up: true));
        Assert.Equal(7, CustomStudy.StepDays(14, up: false));
        Assert.Equal(6, CustomStudy.StepDays(7, up: false));
        Assert.Equal(1, CustomStudy.StepDays(1, up: false));
    }

    [Fact]
    public void NothingMatchingSaysSo()
    {
        AddNew("Q1");
        var session = Session(new CustomStudy { Kind = CustomStudyKind.ReviewAhead });
        Assert.Null(session.Current);
        Assert.False(session.HasAnyCards);
    }
}
