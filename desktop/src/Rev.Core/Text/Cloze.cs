using System.Text.RegularExpressions;

namespace Rev.Core.Text;

/// <summary>
/// Cloze ("fill in the blank") text: words hidden with {{c1::answer}} or {{c1::answer::hint}}.
/// Each number (c1, c2, …) becomes its own card; the same number can hide several words together.
/// </summary>
public static partial class Cloze
{
    public enum Side { Front, Back }

    [GeneratedRegex(@"\{\{c(\d+)::", RegexOptions.IgnoreCase)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\{\{c(\d+)::(.*?)(?:::(.*?))?\}\}", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ClozePattern();

    /// <summary>The cloze numbers used in a text, e.g. "{{c1::a}} {{c3::b}} {{c1::c}}" → [1, 3].</summary>
    public static List<int> Numbers(string text) =>
        NumberPattern().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order().ToList();

    /// <summary>The next free cloze number, for the "Hide" button.</summary>
    public static int NextNumber(string text) => (Numbers(text).DefaultIfEmpty(0).Max()) + 1;

    /// <summary>
    /// One card's side. "{{c1::Dog::animal}}" → front "[animal]" (or "[...]"), back "[Dog]".
    /// Other cloze numbers on the same note are simply shown as normal text.
    /// </summary>
    public static string Render(string text, int number, Side side) =>
        ClozePattern().Replace(text, m =>
        {
            string answer = m.Groups[2].Value;
            if (int.Parse(m.Groups[1].Value) != number) return answer;
            if (side == Side.Back) return "[" + answer + "]";
            return m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? "[" + m.Groups[3].Value + "]" : "[...]";
        });

    /// <summary>
    /// Hides the first not-yet-hidden appearance of <paramref name="word"/> as cloze <paramref name="number"/>.
    /// Returns the new text, or null if the word isn't found (outside existing clozes).
    /// </summary>
    public static string? Hide(string text, string word, int number)
    {
        word = word.Trim();
        if (word.Length == 0) return null;
        // Skip text that's already inside a cloze.
        var pattern = new Regex(@"\{\{c\d+::.*?\}\}|" + Regex.Escape(word), RegexOptions.IgnoreCase);
        foreach (Match match in pattern.Matches(text))
        {
            if (match.Value.StartsWith("{{")) continue;
            return text[..match.Index] + $"{{{{c{number}::{match.Value}}}}}" + text[(match.Index + match.Length)..];
        }
        return null;
    }
}
