using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Review;
using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public sealed class ReviewSessionTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "rev-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RevDatabase db;
    private readonly Deck deck;
    private DateTime now = Now;

    public ReviewSessionTests()
    {
        db = new RevDatabase(Path.Combine(folder, "rev.db"));
        deck = db.AddDeck("Pharmacology");
    }

    public void Dispose()
    {
        db.Dispose();
        Directory.Delete(folder, recursive: true);
    }

    private ReviewSession Session(Guid? deckId = null) => new(db, deckId ?? deck.Id, () => now);

    private void AddNew(int count, Guid? deckId = null)
    {
        for (int i = 0; i < count; i++)
            db.AddNote(deckId ?? deck.Id, CardKind.Basic, $"Q{i}", $"A{i}", [], Now.AddDays(-1).AddMinutes(i));
    }

    [Fact]
    public void AnsweringANewCardSavesItsScheduleAndAReview()
    {
        AddNew(1);
        var session = Session();
        var card = session.Current!;

        session.Rate(Rating.Good);

        var saved = db.GetCard(card.Id)!;
        Assert.Equal(CardPhase.Learning, saved.Phase);
        Assert.Equal(Now.AddMinutes(10), saved.Due);
        var log = Assert.Single(db.GetReviewLogs());
        Assert.Equal((card.Id, Rating.Good, true, Now), (log.CardId, log.Rating, log.WasNew, log.Date));
        Assert.Equal(1, session.ReviewedThisSession);
    }

    [Fact]
    public void ButtonPreviewsMatchWhatTheAnswerDoes()
    {
        AddNew(1);
        var session = Session();
        var card = session.Current!;
        string preview = session.Previews()[Rating.Hard];
        session.Rate(Rating.Hard);
        Assert.Equal(preview, db.GetCard(card.Id)!.SchedulingState.WaitTextFrom(Now));
    }

    [Fact]
    public void TheDailyNewCardLimitCountsCardsAlreadyStudiedToday()
    {
        db.SaveSettings(new AppSettings { NewCardsPerDay = 2 });
        AddNew(5);
        var session = Session();
        Assert.Equal(2, session.Queue.NewCards.Count);

        session.Rate(Rating.Easy);   // one new card studied (and graduated)
        Assert.Single(session.Queue.NewCards);

        // A new session later today still knows.
        Assert.Single(Session().Queue.NewCards);
    }

    [Fact]
    public void TheOtherCardOfANoteWaitsUntilTomorrow()
    {
        db.AddNote(deck.Id, CardKind.Reversed, "Meloxicam", "NSAID", [], Now.AddDays(-1));
        var session = Session();
        Assert.Equal(2, session.Queue.NewCards.Count);

        session.Rate(Rating.Easy);
        Assert.Null(session.Current);
        Assert.Empty(session.Queue.NewCards);

        now = Now.AddDays(1);   // tomorrow
        Assert.NotNull(Session().Current);
    }

    [Fact]
    public void UndoPutsTheCardBackAndRemovesTheReview()
    {
        AddNew(2);
        var session = Session();
        var first = session.Current!;
        var before = db.GetCard(first.Id)!.SchedulingState;

        session.Rate(Rating.Good);
        Assert.NotEqual(first.Id, session.Current!.Id);
        Assert.True(session.CanUndo);

        session.Undo();
        Assert.Equal(first.Id, session.Current!.Id);
        Assert.Equal(before, db.GetCard(first.Id)!.SchedulingState);
        Assert.Empty(db.GetReviewLogs());
        Assert.Equal(0, session.ReviewedThisSession);
        Assert.False(session.CanUndo);
        Assert.Equal(2, session.Queue.NewCards.Count);   // the new-card limit gets its card back too
    }

    [Fact]
    public void ForgettingACardForThe8thTimeMakesItALeechAndUndoTakesThatBack()
    {
        var card = new Card { DeckId = deck.Id, Front = "Q", Back = "A" };
        card.SchedulingState = ReviewState() with { Lapses = 7 };
        db.AddCard(card);
        var session = Session();

        Assert.True(session.Rate(Rating.Again));
        var leech = db.GetCard(card.Id)!;
        Assert.True(leech.IsLeech);
        Assert.Equal(8, leech.Lapses);

        session.Undo();
        var restored = db.GetCard(card.Id)!;
        Assert.False(restored.IsLeech);
        Assert.Equal(7, restored.Lapses);
    }

    [Fact]
    public void SuspendedCardsLeaveTheQueue()
    {
        AddNew(1);
        var session = Session();
        session.Suspend(session.Current!.Id);
        Assert.Null(session.Current);
        Assert.True(db.GetCards().Single().IsSuspended);
        Assert.True(session.HasAnyCards);
    }

    [Fact]
    public void ReviewAllDecksStudiesEveryDeck()
    {
        var other = db.AddDeck("Surgery");
        AddNew(1);
        AddNew(1, other.Id);
        Assert.Equal(2, new ReviewSession(db, null, () => now).Queue.NewCards.Count);
        Assert.Single(Session().Queue.NewCards);
    }

    [Fact]
    public void AnEmptyDeckSaysSo()
    {
        var session = Session();
        Assert.Null(session.Current);
        Assert.False(session.HasAnyCards);
    }

    [Fact]
    public void TheSchedulerFromSettingsIsUsed()
    {
        db.SaveSettings(new AppSettings { Scheduler = SchedulerKind.Sm2 });
        AddNew(1);
        var session = Session();
        var card = session.Current!;
        session.Rate(Rating.Easy);
        Assert.Equal(4, db.GetCard(card.Id)!.IntervalDays);   // SM-2's "easy" graduating interval
        Assert.Null(db.GetCard(card.Id)!.Stability);
    }

    [Fact]
    public void AfterEditingTheCurrentCardTheNewTextIsShown()
    {
        AddNew(1);
        var session = Session();
        var card = session.Current!;
        db.SaveEdit(card, "Fixed typo", "A", []);
        session.ReloadCurrent();
        Assert.Equal("Fixed typo", session.Current!.Front);

        db.DeleteCard(card.Id);
        session.ReloadCurrent();
        Assert.Null(session.Current);
    }
}
