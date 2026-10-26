using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;

namespace Rev.Core.Review;

/// <summary>
/// One study session: which card to show, what each answer button would do, answering, undo and suspend.
/// The review screen only draws what this says; all the rules live here, so they can be tested.
/// </summary>
public sealed class ReviewSession
{
    private readonly RevDatabase db;
    private readonly Guid? deckId;
    private readonly Func<DateTime> clock;

    /// <summary>Answers given in this session, newest last, so they can be undone.</summary>
    private readonly Stack<PastAnswer> history = new();

    private sealed record PastAnswer(Guid CardId, SchedulingState Before, bool WasLeech, bool WasSuspended, Guid LogId);

    /// <param name="deckId">The deck to study, or null for "Review all decks".</param>
    /// <param name="clock">Where "now" comes from (tests pass a fixed time).</param>
    public ReviewSession(RevDatabase db, Guid? deckId, Func<DateTime>? clock = null)
    {
        this.db = db;
        this.deckId = deckId;
        this.clock = clock ?? (() => DateTime.Now);
        Queue = new StudyQueue([], 0, this.clock());
        LoadNext();
    }

    public StudyQueue Queue { get; private set; }
    /// <summary>The card on screen, or null when there's nothing left to study right now.</summary>
    public Card? Current { get; private set; }
    public int ReviewedThisSession { get; private set; }
    public bool CanUndo => history.Count > 0;
    /// <summary>False when the deck (or every deck) has no cards at all.</summary>
    public bool HasAnyCards { get; private set; }

    /// <summary>The algorithm from Settings, read fresh each time so a change applies straight away.</summary>
    private ISchedulingAlgorithm Scheduler => db.GetSettings().CreateScheduler();

    /// <summary>Rebuilds the queue and picks the next card.</summary>
    public void LoadNext()
    {
        RebuildQueue();
        Current = Queue.NextCard;
    }

    private void RebuildQueue()
    {
        var now = clock();
        var allCards = db.GetCards();
        var cards = deckId is { } id ? allCards.Where(c => c.DeckId == id).ToList() : allCards;
        HasAnyCards = cards.Count > 0;
        int newLeft = Math.Max(0, db.GetSettings().NewCardsPerDay - db.NewCardsStudiedToday(now));
        // Notes you already studied a card from today: their sibling cards wait until tomorrow.
        var buried = StudyQueue.BuriedNotes(allCards, db.CardIdsReviewedToday(now));
        Queue = new StudyQueue(cards, newLeft, now, buried);
    }

    /// <summary>What each button would do for the current card, e.g. Again "1 min", Good "3 d".</summary>
    public IReadOnlyDictionary<Rating, string> Previews() =>
        Current is { } card ? Scheduler.Previews(card.SchedulingState, clock()) : new Dictionary<Rating, string>();

    /// <summary>Answers the current card. Returns true if it has just become a leech.</summary>
    public bool Rate(Rating rating)
    {
        if (Current is not { } card) return false;
        var now = clock();
        var before = card.SchedulingState;
        var after = Scheduler.Next(before, rating, now);
        bool wasLeech = card.IsLeech;

        card.SchedulingState = after;
        card.MarkScheduled(now);
        bool becameLeech = Leech.JustBecameLeech(before, rating, after);
        if (becameLeech)
        {
            card.IsLeech = true;
            card.MarkEdited(now);   // the leech tag is part of the card's content in Anki
        }

        var log = new ReviewLog
        {
            CardId = card.Id, Date = now, Rating = rating,
            WasNew = before.Phase == CardPhase.New, IntervalDays = after.IntervalDays,
        };
        db.InTransaction(() =>
        {
            db.UpdateCard(card);
            db.AddReviewLog(log);
        });

        history.Push(new PastAnswer(card.Id, before, wasLeech, card.IsSuspended, log.Id));
        ReviewedThisSession += 1;
        LoadNext();
        return becameLeech;
    }

    /// <summary>Puts the last answered card back exactly as it was (and removes its review) and shows it again.</summary>
    public void Undo()
    {
        if (!history.TryPop(out var last)) return;
        var now = clock();
        db.DeleteReviewLog(last.LogId);
        ReviewedThisSession = Math.Max(0, ReviewedThisSession - 1);

        if (db.GetCard(last.CardId) is not { } card)
        {
            LoadNext();   // the card was deleted meanwhile; nothing to put back
            return;
        }
        card.SchedulingState = last.Before;
        card.IsLeech = last.WasLeech;
        card.IsSuspended = last.WasSuspended;
        card.MarkScheduled(now);
        card.MarkEdited(now);
        db.UpdateCard(card);

        RebuildQueue();
        Current = card;
    }

    /// <summary>Hides a card from reviews until you unsuspend it (offered for leeches).</summary>
    public void Suspend(Guid cardId)
    {
        if (db.GetCard(cardId) is { } card)
        {
            card.IsSuspended = true;
            card.MarkScheduled(clock());
            db.UpdateCard(card);
        }
        LoadNext();
    }

    /// <summary>Reads the current card again after editing it (it may have changed, or even been deleted).</summary>
    public void ReloadCurrent()
    {
        if (Current is { } card && db.GetCard(card.Id) is { } fresh)
        {
            RebuildQueue();
            Current = fresh;
        }
        else LoadNext();
    }
}
