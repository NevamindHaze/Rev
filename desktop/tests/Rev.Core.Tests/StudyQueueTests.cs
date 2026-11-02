using Rev.Core.Models;
using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public class StudyQueueTests
{
    [Fact]
    public void LearningDueComesFirstThenReviewsThenNewThenLearningSoon()
    {
        var soon = MakeCard(CardPhase.Learning, Now.AddMinutes(5));
        var fresh = MakeCard(CardPhase.New, Now);
        var review = MakeCard(CardPhase.Review, TodayStart);
        var learning = MakeCard(CardPhase.Learning, Now.AddMinutes(-1));

        var cards = new List<Card> { soon, fresh, review, learning };
        Assert.Same(learning, new StudyQueue(cards, 20, Now).NextCard);
        cards.Remove(learning);
        Assert.Same(review, new StudyQueue(cards, 20, Now).NextCard);
        cards.Remove(review);
        Assert.Same(fresh, new StudyQueue(cards, 20, Now).NextCard);
        cards.Remove(fresh);
        Assert.Same(soon, new StudyQueue(cards, 20, Now).NextCard);
    }

    [Fact]
    public void ReviewsDueBefore4amTomorrowCountAsToday()
    {
        var tonight = MakeCard(CardPhase.Review, TodayStart.AddDays(1).AddMinutes(-1));
        var tomorrow = MakeCard(CardPhase.Review, TodayStart.AddDays(1));
        var queue = new StudyQueue([tonight, tomorrow], 20, Now);
        Assert.Equal([tonight], queue.ReviewDue);
    }

    [Fact]
    public void LearningCardsLaterTodayAreCountedButNotShown()
    {
        var later = MakeCard(CardPhase.Learning, Now.AddHours(2));
        var queue = new StudyQueue([later], 20, Now);
        Assert.Null(queue.NextCard);
        Assert.Equal(1, queue.LaterToday);
    }

    [Fact]
    public void NewCardsFollowTheDailyLimitInTheOrderTheyWereMade()
    {
        var cards = Enumerable.Range(0, 30)
            .Select(i => MakeCard(CardPhase.New, Now, createdAt: Now.AddMinutes(-i)))
            .ToList();
        var queue = new StudyQueue(cards, 20, Now);
        Assert.Equal(20, queue.NewCards.Count);
        Assert.Same(cards[29], queue.NewCards[0]);                 // the oldest first

        Assert.Empty(new StudyQueue(cards, 0, Now).NewCards);
        Assert.Empty(new StudyQueue(cards, -3, Now).NewCards);
    }

    [Fact]
    public void SuspendedCardsNeverAppear()
    {
        var cards = new[]
        {
            MakeCard(CardPhase.New, Now, suspended: true),
            MakeCard(CardPhase.Review, TodayStart, suspended: true),
            MakeCard(CardPhase.Learning, Now.AddMinutes(-5), suspended: true),
        };
        Assert.Null(new StudyQueue(cards, 20, Now).NextCard);
        Assert.Equal(0, cards.DueCount(Now));
        Assert.Equal(0, cards.NewCount());
    }

    [Fact]
    public void SiblingsOfACardStudiedTodayWaitUntilTomorrow()
    {
        var studied = MakeCard(CardPhase.Learning, Now.AddMinutes(-1), noteId: "note-1");
        var newSibling = MakeCard(CardPhase.New, Now, noteId: "note-1");
        var reviewSibling = MakeCard(CardPhase.Review, TodayStart, noteId: "note-1");
        var other = MakeCard(CardPhase.New, Now, noteId: "note-2");
        var all = new[] { studied, newSibling, reviewSibling, other };

        var buried = StudyQueue.BuriedNotes(all, new HashSet<Guid> { studied.Id });
        var queue = new StudyQueue(all, 20, Now, buried);

        Assert.Equal([studied], queue.LearningDue);                // learning cards are never buried
        Assert.Empty(queue.ReviewDue);
        Assert.Equal([other], queue.NewCards);
    }

    [Fact]
    public void HomeScreenCountsDueAndNewCards()
    {
        var cards = new[]
        {
            MakeCard(CardPhase.New, Now),
            MakeCard(CardPhase.New, Now),
            MakeCard(CardPhase.Review, TodayStart),
            MakeCard(CardPhase.Learning, Now.AddHours(3)),
            MakeCard(CardPhase.Review, TodayStart.AddDays(5)),
        };
        Assert.Equal(2, cards.DueCount(Now));
        Assert.Equal(2, cards.NewCount());
    }
}
