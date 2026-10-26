using System.Globalization;
using Rev.Core.Models;
using Rev.Core.Scheduling;
using Rev.Core.Text;

namespace Rev.Core.Browser;

/// <summary>Which cards to show, by where they are in their life.</summary>
public enum CardStateFilter { All, DueToday, New, Learning, Review, Leeches, Suspended }

/// <summary>How to order the list.</summary>
public enum CardSort { Newest, Oldest, DueSoonest, AToZ, MostForgotten, Hardest }

/// <summary>Searching, filtering and sorting cards for the card browser.</summary>
public static class CardQuery
{
    public static string Title(this CardStateFilter filter) => filter switch
    {
        CardStateFilter.DueToday => "Due today",
        CardStateFilter.New => "New",
        CardStateFilter.Learning => "Learning",
        CardStateFilter.Review => "Review",
        CardStateFilter.Leeches => "Leeches",
        CardStateFilter.Suspended => "Suspended",
        _ => "All cards",
    };

    public static string Title(this CardSort sort) => sort switch
    {
        CardSort.Oldest => "Oldest first",
        CardSort.DueSoonest => "Due soonest",
        CardSort.AToZ => "A to Z",
        CardSort.MostForgotten => "Most forgotten",
        CardSort.Hardest => "Hardest",
        _ => "Newest first",
    };

    public static bool Matches(this CardStateFilter filter, Card card, DateTime now) => filter switch
    {
        CardStateFilter.DueToday => !card.IsSuspended && card.Phase != CardPhase.New && card.Due < now.EndOfStudyDay(),
        CardStateFilter.New => card.Phase == CardPhase.New,
        CardStateFilter.Learning => card.Phase is CardPhase.Learning or CardPhase.Relearning,
        CardStateFilter.Review => card.Phase == CardPhase.Review,
        CardStateFilter.Leeches => card.IsLeech,
        CardStateFilter.Suspended => card.IsSuspended,
        _ => true,
    };

    /// <summary>
    /// The cards to show. <paramref name="search"/> looks in the question, answer and tags (any capitals);
    /// <paramref name="tag"/> (or null for any) keeps only cards with that tag.
    /// </summary>
    public static List<Card> Apply(IEnumerable<Card> cards, string search, CardStateFilter state, string? tag,
                                   CardSort sort, DateTime now)
    {
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        bool Contains(string text) => compare.IndexOf(text, search, CompareOptions.IgnoreCase) >= 0;
        search = search.Trim();

        var visible = cards
            .Where(c => state.Matches(c, now))
            .Where(c => tag is null || Tags.Parse(c.TagsText).Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Where(c => search.Length == 0 || Contains(c.Front) || Contains(c.Back) || Contains(c.TagsText));

        // ThenBy(CreatedAt) keeps the order steady when two cards are equal.
        return (sort switch
        {
            CardSort.Oldest => visible.OrderBy(c => c.CreatedAt),
            CardSort.DueSoonest => visible.OrderBy(c => c.Due).ThenBy(c => c.CreatedAt),
            CardSort.AToZ => visible.OrderBy(c => CardText.Preview(c.Front).Replace("*", ""), StringComparer.CurrentCultureIgnoreCase)
                                    .ThenBy(c => c.CreatedAt),
            CardSort.MostForgotten => visible.OrderByDescending(c => c.Lapses).ThenBy(c => c.CreatedAt),
            CardSort.Hardest => visible.OrderByDescending(Hardness).ThenBy(c => c.CreatedAt),
            _ => visible.OrderByDescending(c => c.CreatedAt),
        }).ToList();
    }

    /// <summary>
    /// How hard a card is, 1–10: FSRS difficulty if the card has one, otherwise estimated from its
    /// SM-2 ease (2.5 → 5, 1.3 → 9.8, the same estimate FSRS itself uses).
    /// </summary>
    public static double Hardness(Card card) =>
        card.Difficulty ?? Math.Clamp(11 - (card.Ease - 1) * 4, 1, 10);

    /// <summary>Every tag used by these cards, A to Z (once each, whatever the capitals).</summary>
    public static List<string> AllTags(IEnumerable<Card> cards) =>
        cards.SelectMany(c => Tags.Parse(c.TagsText))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>The state badge on a card: "New", "Learning", "Due today", "Due in 3 days", …</summary>
    public static string StateText(Card card, DateTime now)
    {
        if (card.Phase == CardPhase.New) return "New";
        if (card.Phase is CardPhase.Learning or CardPhase.Relearning) return "Learning";
        int days = now.StudyDaysTo(card.Due);
        return days switch
        {
            0 => "Due today",
            1 => "Due tomorrow",
            -1 => "Due yesterday",
            > 1 => $"Due in {days.Counted("day")}",
            _ => $"Due {(-days).Counted("day")} ago",
        };
    }
}
