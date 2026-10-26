namespace Rev.Core.Models;

/// <summary>Reading and writing tags. Tags are single words (like Anki), stored separated by spaces.</summary>
public static class Tags
{
    /// <summary>"Paper_1, parasites  paper_1" → ["Paper_1", "parasites"] (duplicates ignored, any capitals).</summary>
    public static List<string> Parse(string? text)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var tag in (text ?? "").Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
            if (seen.Add(tag)) result.Add(tag);
        return result;
    }

    public static string Format(IEnumerable<string> tags) => string.Join(" ", Parse(string.Join(" ", tags)));

    /// <summary>Your most-used tags that aren't in <paramref name="current"/> yet (for the suggestion chips).</summary>
    public static List<string> Suggestions(IEnumerable<Card> allCards, string current, int count = 12)
    {
        var have = new HashSet<string>(Parse(current), StringComparer.OrdinalIgnoreCase);
        return allCards
            .SelectMany(c => Parse(c.TagsText))
            .Where(tag => !have.Contains(tag))
            .GroupBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .Select(group => group.First())
            .ToList();
    }
}
