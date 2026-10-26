namespace Rev.Core.Text;

/// <summary>Small text helpers for the editor and the review screen.</summary>
public static class TextTools
{
    // ---- Bold and italic ----

    /// <summary>
    /// Wraps the selected words in a marker (** for bold, * for italic),
    /// or removes the marker if the words already have it. Cards show the markers as real formatting.
    /// Returns the new text and where the selection should be afterwards.
    /// </summary>
    public static (string Text, int Start, int Length) ToggleFormat(string text, string marker, int start, int length)
    {
        int end = start + length;
        int size = marker.Length;
        bool markedBefore = start >= size && text.Substring(start - size, size) == marker;
        bool markedAfter = end + size <= text.Length && text.Substring(end, size) == marker;

        if (markedBefore && markedAfter)
        {
            string removed = text[..(start - size)] + text[start..end] + text[(end + size)..];
            return (removed, start - size, length);
        }
        string added = text[..start] + marker + text[start..end] + marker + text[end..];
        return (added, start + size, length);
    }

    // ---- Type the answer ----

    /// <summary>
    /// True if what you typed matches the answer. Capital letters, extra spaces,
    /// formatting marks and a full stop at either end don't matter.
    /// </summary>
    public static bool AnswerMatches(string typed, string answer) => Normalize(typed) == Normalize(answer);

    private static string Normalize(string text) =>
        string.Join(" ", text.ToLowerInvariant().Replace("*", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim('.', ' ');

    // ---- Paste many ----

    /// <summary>
    /// Splits pasted text into (front, back) pairs: one card per line, with "|" (or a tab, as when
    /// copying from a spreadsheet) between front and back. Lines without a separator are skipped.
    /// </summary>
    public static List<(string Front, string Back)> ParsePasted(string text)
    {
        var pairs = new List<(string, string)>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            char separator = line.Contains('\t') ? '\t' : '|';
            int at = line.IndexOf(separator);
            if (at < 0) continue;
            string front = line[..at].Trim(' ', '\t');
            string back = line[(at + 1)..].Trim(' ', '\t');
            if (front.Length > 0) pairs.Add((front, back));
        }
        return pairs;
    }
}
