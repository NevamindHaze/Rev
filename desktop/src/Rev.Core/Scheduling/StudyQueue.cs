using Rev.Core.Models;

namespace Rev.Core.Scheduling;

/// <summary>
/// Decides which card to show next.
///
/// Order: learning cards that are due now → review cards due today →
/// new cards (up to today's limit) → learning cards due in the next 20 minutes.
/// Suspended cards are never shown.
/// </summary>
public sealed class StudyQueue
{
    /// <summary>How far ahead we may show learning cards when nothing else is left.</summary>
    public static readonly TimeSpan LearnAhead = TimeSpan.FromMinutes(20);

    public IReadOnlyList<Card> LearningDue { get; }
    public IReadOnlyList<Card> ReviewDue { get; }
    public IReadOnlyList<Card> NewCards { get; }
    public IReadOnlyList<Card> LearningSoon { get; }
    /// <summary>Learning cards that come back later today (more than 20 minutes from now).</summary>
    public int LaterToday { get; }

    /// <param name="newCardsAllowed">How many new cards may still be shown today (daily limit minus those already studied).</param>
    /// <param name="buriedNoteIds">
    /// Notes you already studied a card from today. Their other new and review cards wait until tomorrow,
    /// so one cloze doesn't give away its sibling's answer.
    /// </param>
    public StudyQueue(IEnumerable<Card> cards, int newCardsAllowed, DateTime now,
                      IReadOnlySet<string>? buriedNoteIds = null)
    {
        var endOfToday = now.EndOfStudyDay();
        var learningDue = new List<Card>();
        var reviewDue = new List<Card>();
        var allNew = new List<Card>();
        var learningSoon = new List<Card>();
        int laterToday = 0;

        foreach (var card in cards)
        {
            if (card.IsSuspended) continue;
            bool buried = buriedNoteIds?.Contains(card.NoteId) == true;
            if (buried && card.Phase is CardPhase.New or CardPhase.Review) continue;

            switch (card.Phase)
            {
                case CardPhase.New:
                    allNew.Add(card);
                    break;
                case CardPhase.Review:
                    if (card.Due < endOfToday) reviewDue.Add(card);
                    break;
                default:   // learning or relearning
                    if (card.Due <= now) learningDue.Add(card);
                    else if (card.Due <= now + LearnAhead) learningSoon.Add(card);
                    else if (card.Due < endOfToday) laterToday += 1;
                    break;
            }
        }

        LearningDue = learningDue.OrderBy(c => c.Due).ThenBy(c => c.CreatedAt).ToList();
        ReviewDue = reviewDue.OrderBy(c => c.Due).ThenBy(c => c.CreatedAt).ToList();
        LearningSoon = learningSoon.OrderBy(c => c.Due).ThenBy(c => c.CreatedAt).ToList();
        NewCards = allNew.OrderBy(c => c.CreatedAt).Take(Math.Max(0, newCardsAllowed)).ToList();
        LaterToday = laterToday;
    }

    /// <summary>
    /// A custom study session: the session's cards you haven't answered yet, in the order given,
    /// plus any you answered that came back as learning cards (like a normal session).
    /// </summary>
    public StudyQueue(IEnumerable<Card> sessionCards, IReadOnlySet<Guid> answered, DateTime now)
    {
        var endOfToday = now.EndOfStudyDay();
        var learningDue = new List<Card>();
        var reviewDue = new List<Card>();
        var newCards = new List<Card>();
        var learningSoon = new List<Card>();
        int laterToday = 0;

        foreach (var card in sessionCards)
        {
            if (card.IsSuspended) continue;
            bool learning = card.Phase is CardPhase.Learning or CardPhase.Relearning;
            if (!answered.Contains(card.Id))
            {
                if (card.Phase == CardPhase.New) newCards.Add(card);
                else if (learning) learningDue.Add(card);
                else reviewDue.Add(card);
            }
            else if (learning)
            {
                if (card.Due <= now) learningDue.Add(card);
                else if (card.Due <= now + LearnAhead) learningSoon.Add(card);
                else if (card.Due < endOfToday) laterToday += 1;
            }
        }

        LearningDue = learningDue.OrderBy(c => c.Due).ToList();
        ReviewDue = reviewDue;
        NewCards = newCards;
        LearningSoon = learningSoon.OrderBy(c => c.Due).ToList();
        LaterToday = laterToday;
    }

    /// <summary>The card to show now, or null when you're done for the moment.</summary>
    public Card? NextCard =>
        LearningDue.FirstOrDefault() ?? ReviewDue.FirstOrDefault() ?? NewCards.FirstOrDefault()
        ?? LearningSoon.FirstOrDefault();

    public int LearningCount => LearningDue.Count + LearningSoon.Count;

    /// <summary>The notes that have a card you answered today (see "buried" above).</summary>
    public static IReadOnlySet<string> BuriedNotes(IEnumerable<Card> allCards, IReadOnlySet<Guid> cardIdsReviewedToday) =>
        allCards.Where(c => cardIdsReviewedToday.Contains(c.Id)).Select(c => c.NoteId).ToHashSet();
}

/// <summary>Counts used on the Home screen and deck tiles.</summary>
public static class CardCounts
{
    /// <summary>Studied cards that are due by the end of today (not counting suspended ones).</summary>
    public static int DueCount(this IEnumerable<Card> cards, DateTime now)
    {
        var end = now.EndOfStudyDay();
        return cards.Count(c => !c.IsSuspended && c.Phase != CardPhase.New && c.Due < end);
    }

    /// <summary>Cards that have never been studied (not counting suspended ones).</summary>
    public static int NewCount(this IEnumerable<Card> cards) =>
        cards.Count(c => !c.IsSuspended && c.Phase == CardPhase.New);
}
