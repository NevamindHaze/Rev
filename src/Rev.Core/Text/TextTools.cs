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

        // Already this kind of formatting? Remove its markers, whether the selection is just the words
        // or takes in the markers too (easy to do when the editor hides them). Only markers of the same
        // kind count, so Italic on a bold word doesn't take a star from each side and make it italic.
        var markers = CardText.Layout(text).Markers;
        for (int i = 0; i + 1 < markers.Count; i += 2)
        {
            var (open, close) = (markers[i], markers[i + 1]);
            if (open.Length != size) continue;
            if (open.End == start && close.Start == end)
                return (text[..open.Start] + text[start..end] + text[close.End..], open.Start, length);
            if (open.Start == start && close.End == end)
                return (text[..start] + text[open.End..close.Start] + text[end..], start, length - 2 * size);
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
