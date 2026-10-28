using System.Text.RegularExpressions;

namespace Rev.Core.Text;

/// <summary>One line of a card, ready to be laid out on screen.</summary>
public abstract record CardLine
{
    public sealed record Text(string Value) : CardLine;
    /// <summary>A list item: the marker is "•" or a number like "1.".</summary>
    public sealed record ListItem(string Marker, string Value) : CardLine;
    /// <summary>An image, by its file name in the media folder.</summary>
    public sealed record Image(string Name) : CardLine;
    public sealed record Blank : CardLine;
}

/// <summary>A piece of text with its formatting, e.g. ("Toxocara", Bold: false, Italic: true).</summary>
public sealed record TextSpan(string Value, bool Bold, bool Italic);

/// <summary>Where a ** or * marker is in a line.</summary>
public readonly record struct MarkerRange(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>A bold and/or italic stretch of a line, by position (without its markers).</summary>
public sealed record FormatRange(int Start, int Length, bool Bold, bool Italic);

/// <summary>The formatted stretches of a line and the ** / * markers around them (outer ones first).</summary>
public sealed record FormatLayout(List<FormatRange> Styled, List<MarkerRange> Markers);

/// <summary>
/// Understands the simple structure inside card text: list items, image markers ([img:heart.jpg]),
/// blank lines, and **bold** / *italic* markers.
/// </summary>
public static partial class CardText
{
    [GeneratedRegex(@"^\[img:(.+)\]$")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"^(•|\d{1,3}\.)\s+(.*)$")]
    private static partial Regex ListPattern();

    // The same rules Rev uses when sending formatting to Anki (AnkiText.html in Rev for iPhone).
    [GeneratedRegex(@"\*\*(.+?)\*\*|(?<![*\w])\*(?=\S)(.+?)(?<=\S)\*(?![*\w])")]
    private static partial Regex FormatPattern();

    [GeneratedRegex(@"\[img:[^\]]+\]")]
    private static partial Regex InlineImagePattern();

    public static List<CardLine> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(raw =>
        {
            string line = raw.Trim(' ', '\t');
            if (line.Length == 0) return (CardLine)new CardLine.Blank();
            if (ImagePattern().Match(line) is { Success: true } image) return new CardLine.Image(image.Groups[1].Value);
            if (ListPattern().Match(line) is { Success: true } item) return new CardLine.ListItem(item.Groups[1].Value, item.Groups[2].Value);
            return new CardLine.Text(line);
        }).ToList();

    /// <summary>Splits one line into plain, bold and italic pieces (bold and italic can be nested).</summary>
    public static List<TextSpan> Spans(string line, bool bold = false, bool italic = false)
    {
        var spans = new List<TextSpan>();
        int position = 0;
        foreach (Match match in FormatPattern().Matches(line))
        {
            if (match.Index > position) spans.Add(new TextSpan(line[position..match.Index], bold, italic));
            spans.AddRange(match.Groups[1].Success
                ? Spans(match.Groups[1].Value, true, italic)
                : Spans(match.Groups[2].Value, bold, true));
            position = match.Index + match.Length;
        }
        if (position < line.Length) spans.Add(new TextSpan(line[position..], bold, italic));
        return spans;
    }

    /// <summary>
    /// Where the formatting is in one line, by position: the bold and italic parts, and the ** / * markers
    /// around them. The editor uses this to show the parts formatted and hide the markers.
    /// </summary>
    public static FormatLayout Layout(string line)
    {
        var layout = new FormatLayout([], []);
        AddLayout(line, 0, false, false, layout);
        return layout;
    }

    private static void AddLayout(string line, int offset, bool bold, bool italic, FormatLayout layout)
    {
        foreach (Match match in FormatPattern().Matches(line))
        {
            bool isBold = match.Groups[1].Success;
            var inner = isBold ? match.Groups[1] : match.Groups[2];
            int size = isBold ? 2 : 1;
            layout.Markers.Add(new MarkerRange(offset + match.Index, size));
            layout.Markers.Add(new MarkerRange(offset + match.Index + match.Length - size, size));
            layout.Styled.Add(new FormatRange(offset + inner.Index, inner.Length, bold || isBold, italic || !isBold));
            AddLayout(inner.Value, offset + inner.Index, bold || isBold, italic || !isBold, layout);
        }
    }

    /// <summary>Short one-line text for lists, with image markers shown as a picture symbol.</summary>
    public static string Preview(string text) =>
        string.Join("  ", InlineImagePattern().Replace(text, "🖼").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()));

    /// <summary>Every image a card uses.</summary>
    public static List<string> ImageNames(string text) =>
        Lines(text).OfType<CardLine.Image>().Select(i => i.Name).ToList();
}
