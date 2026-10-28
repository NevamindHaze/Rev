using Rev.Core.Data;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Text;

namespace Rev.Core.Review;

/// <summary>The kinds of custom study, like Anki's "Custom study" dialog.</summary>
public enum CustomStudyKind
{
    /// <summary>New cards beyond today's limit, oldest first.</summary>
    MoreNew,
    /// <summary>Cards you pressed Again on in the last few days.</summary>
    Forgotten,
    /// <summary>Review cards that come due in the next few days, soonest first.</summary>
    ReviewAhead,
    /// <summary>Cards with one of the picked tags.</summary>
    ByTag,
}

/// <summary>Which cards "Study by tag" takes.</summary>
public enum TagCards { All, NewOnly, StudiedOnly }

/// <summary>
/// An extra study session outside the normal daily queue. Answers still count as reviews
/// and reschedule the cards as usual (and sync like any other answer).
/// </summary>
public sealed record CustomStudy
{
    public CustomStudyKind Kind { get; init; } = CustomStudyKind.MoreNew;
    /// <summary>How many cards at most (more new cards, study by tag).</summary>
    public int Limit { get; init; } = 20;
    /// <summary>How many days to look back (forgotten) or ahead (review ahead).</summary>
    public int Days { get; init; } = 1;
    /// <summary>Study by tag: cards with any of these tags. Empty means any card.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
    public TagCards TagCards { get; init; } = TagCards.All;
    public bool Shuffled { get; init; } = true;

    public static string Title(CustomStudyKind kind) => kind switch
    {
        CustomStudyKind.MoreNew => "More new cards",
        CustomStudyKind.Forgotten => "Forgotten cards",
        CustomStudyKind.ReviewAhead => "Review ahead",
        _ => "Study by tag",
    };

    public string Title() => Title(Kind);

    /// <summary>One line under the title, so it's clear what each choice picks.</summary>
    public static string Subtitle(CustomStudyKind kind) => kind switch
    {
        CustomStudyKind.MoreNew => "New cards beyond today's limit",
        CustomStudyKind.Forgotten => "Cards you pressed Again on lately",
        CustomStudyKind.ReviewAhead => "Cards that come due soon",
        _ => "Any cards, or only ones with a tag",
    };

    /// <summary>
    /// Why nothing was picked, in words, e.g. "The next card is due in 21 days."
    /// <paramref name="place"/> is "this deck" or "your decks".
    /// </summary>
    public string EmptyReason(IEnumerable<Card> cards, string place, DateTime now)
    {
        var available = cards.Where(c => !c.IsSuspended).ToList();
        switch (Kind)
        {
            case CustomStudyKind.MoreNew:
                return available.Any(c => c.Phase == CardPhase.New)
                    ? "Today's other new cards share a note with a card you studied today, so they wait until tomorrow."
                    : $"There are no new cards left in {place}. Try “Study by tag” to go over cards you've already learned.";
            case CustomStudyKind.Forgotten:
                return $"You haven't pressed Again on any card in the last {Days.Counted("day")}. Try “Study by tag” to go over everything.";
            case CustomStudyKind.ReviewAhead:
            {
                var later = available.Where(c => c.Phase == CardPhase.Review && c.Due >= now.EndOfStudyDay())
                    .Select(c => (DateTime?)c.Due).Min();
                if (later is not { } next) return $"Nothing in {place} is waiting for a later review yet.";
                int wait = Math.Max(1, now.StudyDaysTo(next));
                return $"Nothing comes due in the next {Days.Counted("day")}. The next card is due in {wait.Counted("day")}: " +
                       "press + to look further ahead, or try “Study by tag”.";
            }
            default:
                if (Tags.Count == 0) return $"There are no cards to study in {place}.";
                return TagCards == TagCards.All
                    ? "No cards have the picked tags."
                    : $"No {(TagCards == TagCards.NewOnly ? "new" : "studied")} cards have the picked tags. Try “All cards”.";
        }
    }

    /// <summary>The day counts the − and + buttons go through: 1–7 one day at a time, then a week at a time up to a year.</summary>
    public static int StepDays(int days, bool up) => up
        ? days < 7 ? days + 1 : Math.Min(365, days + 7)
        : days > 7 ? Math.Max(7, days - 7) : Math.Max(1, days - 1);

    /// <summary>The cards this session will show, in order. Suspended cards are never picked.</summary>
    public List<Card> PickCards(RevDatabase db, IEnumerable<Card> cards, DateTime now, Random? random = null)
    {
        random ??= Random.Shared;
        var available = cards.Where(c => !c.IsSuspended).ToList();
        switch (Kind)
        {
            case CustomStudyKind.MoreNew:
            {
                // Siblings of notes studied today still wait, so a cloze doesn't give away its partner.
                var buried = StudyQueue.BuriedNotes(db.GetCards(), db.CardIdsReviewedToday(now));
                return available.Where(c => c.Phase == CardPhase.New && !buried.Contains(c.NoteId))
                    .OrderBy(c => c.CreatedAt).Take(Math.Max(0, Limit)).ToList();
            }
            case CustomStudyKind.Forgotten:
            {
                var since = now.StartOfStudyDay().AddDays(1 - Math.Max(1, Days));
                var forgotten = db.GetReviewLogs(since).Where(l => l.Rating == Rating.Again).Select(l => l.CardId).ToHashSet();
                return Shuffle(available.Where(c => forgotten.Contains(c.Id) && c.Phase != CardPhase.New), random);
            }
            case CustomStudyKind.ReviewAhead:
            {
                var start = now.EndOfStudyDay();
                var end = start.AddDays(Math.Max(1, Days));
                return available.Where(c => c.Phase == CardPhase.Review && c.Due >= start && c.Due < end)
                    .OrderBy(c => c.Due).ThenBy(c => c.CreatedAt).ToList();
            }
            default:
            {
                var wanted = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase);
                var matching = available.Where(c =>
                    (wanted.Count == 0 || Models.Tags.Parse(c.TagsText).Any(wanted.Contains)) &&
                    TagCards switch
                    {
                        TagCards.NewOnly => c.Phase == CardPhase.New,
                        TagCards.StudiedOnly => c.Phase != CardPhase.New,
                        _ => true,
                    });
                var ordered = Shuffled ? Shuffle(matching, random) : matching.OrderBy(c => c.CreatedAt).ToList();
                return ordered.Take(Math.Max(0, Limit)).ToList();
            }
        }
    }

    private static List<Card> Shuffle(IEnumerable<Card> cards, Random random)
    {
        var list = cards.ToList();
        random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list));
        return list;
    }

    /// <summary>Every tag used by these cards, A to Z (for the "Study by tag" choices).</summary>
    public static List<string> AllTags(IEnumerable<Card> cards) => cards
        .SelectMany(c => Models.Tags.Parse(c.TagsText))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
