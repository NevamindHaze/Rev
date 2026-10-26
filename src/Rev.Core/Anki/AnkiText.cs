using System.Text.RegularExpressions;
using Rev.Core.Media;
using Rev.Core.Text;

namespace Rev.Core.Anki;

/// <summary>
/// Converts Anki's card templates and HTML into Rev's plain text (with **bold**, *italic*, "•" lists and
/// [img:…] markers), and Rev's text back into simple HTML for exporting.
/// The same rules as Rev for iPhone (Rev/Anki/AnkiText.swift).
/// </summary>
public static class AnkiText
{
    /// <summary>All patterns ignore capitals and let "." match line breaks, like Rev for iPhone's.</summary>
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    private static string Replace(string text, string pattern, MatchEvaluator with) =>
        Regex.Replace(text, pattern, with, Options);

    private static string Replace(string text, string pattern, string with) =>
        Regex.Replace(text, pattern, with, Options);

    // ---- Templates ----

    /// <summary>
    /// Fills an Anki card template (like "{{Front}}") with a note's fields.
    /// <paramref name="clozeNumber"/> is used for cloze cards: 1 for c1, 2 for c2, and so on.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> fields, Cloze.Side side, int? clozeNumber)
    {
        // The answer side often repeats the question; Rev shows the question separately.
        // Anki marks where the answer starts with <hr id=answer>: keep only what follows it.
        string text = template.Replace("{{FrontSide}}", "");
        if (side == Cloze.Side.Back)
        {
            var divider = Regex.Match(text, @"<hr[^>]*\bid\s*=\s*[""']?answer[""']?[^>]*>", RegexOptions.IgnoreCase);
            if (divider.Success) text = text[(divider.Index + divider.Length)..];
        }

        // 1. Optional parts: {{#Field}} … {{/Field}} shows only if the field has text,
        //    {{^Field}} … {{/Field}} only if it's empty. (Sections can sit inside sections.)
        for (int i = 0; i < 10; i++)
        {
            string updated = Replace(text, @"\{\{([#^])\s*([^}]+?)\s*\}\}(.*?)\{\{/\s*\2\s*\}\}", m =>
            {
                bool empty = PlainText(fields.GetValueOrDefault(m.Groups[2].Value, "")).Length == 0;
                bool show = m.Groups[1].Value == "#" ? !empty : empty;
                return show ? m.Groups[3].Value : "";
            });
            if (updated == text) break;
            text = updated;
        }

        // 2. Field tags: {{Back}}, {{text:Back}}, {{cloze:Text}}, {{type:Back}} …
        return Replace(text, @"\{\{([^{}]+)\}\}", m =>
        {
            var parts = m.Groups[1].Value.Split(':').Select(p => p.Trim()).ToList();
            string name = parts[^1];
            var filters = parts.Take(parts.Count - 1).Select(f => f.ToLowerInvariant()).ToList();
            string value = fields.GetValueOrDefault(name, "");
            // {{type:Back}}: the typing box on the question side (Rev draws its own), the right answer on the back.
            if (filters.Contains("type")) return side == Cloze.Side.Back && !filters.Contains("cloze") ? value : "";
            if (filters.Contains("cloze")) return Cloze.Render(value, clozeNumber ?? 1, side);
            return value;
        });
    }

    // ---- HTML → plain text ----

    /// <summary>
    /// Works like a web browser: line breaks typed in the HTML source don't count, &lt;br&gt; is a line break,
    /// and the edges of blocks (paragraphs, list items…) start a new line, with several edges in a row
    /// counting as just one.
    /// </summary>
    public static string PlainText(string html)
    {
        const string edge = "\u0001";        // temporary marker for "a block starts or ends here"
        const string afterLabel = "\u0002";  // temporary marker right after "•" or "1."
        string s = html;
        s = Replace(s, @"<(style|script)[^>]*>.*?</\1>", "");
        s = Replace(s, @"\s+", " ");
        s = Replace(s, @"<br\s*/?>", "\n");

        // Lists: <ol> items get 1. 2. 3., <ul> items get •. Lists can sit inside lists.
        var openLists = new List<(bool Numbered, int Count)>();
        s = Replace(s, @"<(/?)(ol|ul|li)(\s[^>]*)?>", m =>
        {
            string tag = m.Groups[2].Value.ToLowerInvariant();
            bool closing = m.Groups[1].Value == "/";
            switch (tag, closing)
            {
                case ("ol", false): openLists.Add((true, 0)); break;
                case ("ul", false): openLists.Add((false, 0)); break;
                case ("ol", true) or ("ul", true):
                    if (openLists.Count > 0) openLists.RemoveAt(openLists.Count - 1);
                    break;
                case ("li", false):
                    if (openLists.Count == 0) return edge + "•" + afterLabel;
                    var list = openLists[^1];
                    list.Count += 1;
                    openLists[^1] = list;
                    return edge + (list.Numbered ? $"{list.Count}." : "•") + afterLabel;
            }
            return edge;   // </li>, and list starts and ends
        });

        s = Replace(s, @"</?(div|p|tr|table|blockquote|h[1-6])(\s[^>]*)?>", edge);
        s = Replace(s, @"<hr[^>]*>", edge);
        // Images become a marker on their own line, e.g. [img:heart.jpg].
        s = Replace(s, @"<img[^>]*?\ssrc\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>", m =>
        {
            string source = new[] { m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value }.FirstOrDefault(v => v.Length > 0) ?? "";
            string decoded = DecodeEntities(Uri.UnescapeDataString(source));
            return MediaStore.SafeName(decoded) is { } name ? edge + $"[img:{name}]" + edge : "";
        });
        s = Replace(s, @"<img[^>]*>", "");
        // Bold and italic become Rev's **bold** and *italic* markers.
        s = Replace(s, @"</?(b|strong)(\s[^>]*)?>", "**");
        s = Replace(s, @"</?(i|em)(\s[^>]*)?>", "*");
        s = Replace(s, @"\[sound:[^\]]*\]", "");
        s = Replace(s, @"<[^>]+>", "");
        s = DecodeEntities(s);

        // A list item whose text sits in its own <div> stays on the "•" or "1." line.
        s = Replace(s, @"\x02[ \x01]*", " ");
        // A run of block edges becomes one line break. If it also holds a <br>
        // (Anki's way of writing an empty line), keep one blank line.
        s = Replace(s, @"[ \n]*\x01[ \x01\n]*", m => m.Value.Contains('\n') ? "\n\n" : "\n");
        s = Replace(s, @"[ \t]+\n", "\n");
        s = Replace(s, @"\n[ \t]+", "\n");
        s = Replace(s, @"\n{3,}", "\n\n");
        // Two bold runs side by side ("</b><b>") leave an empty pair; remove it.
        s = s.Replace("****", "");
        return s.Trim();
    }

    /// <summary>Turns codes like "&amp;amp;" and "&amp;#176;" back into normal characters.</summary>
    public static string DecodeEntities(string text)
    {
        string s = text;
        foreach (var (code, character) in new[] { ("&nbsp;", " "), ("&lt;", "<"), ("&gt;", ">"), ("&quot;", "\""), ("&#39;", "'"), ("&apos;", "'") })
            s = Regex.Replace(s, Regex.Escape(code), character, RegexOptions.IgnoreCase);
        s = Replace(s, @"&#x([0-9a-f]+);", m => CharFor(Convert.ToInt32(m.Groups[1].Value, 16)));
        s = Replace(s, @"&#(\d+);", m => int.TryParse(m.Groups[1].Value, out int n) ? CharFor(n) : "");
        return Regex.Replace(s, "&amp;", "&", RegexOptions.IgnoreCase);
    }

    private static string CharFor(int code) =>
        code is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32(code) : "";

    // ---- Plain text → HTML (for export) ----

    public static string HtmlFromPlain(string text)
    {
        string escaped = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;").Replace("\r\n", "\n").Replace("\n", "<br>");
        // Image markers become real images again, and **bold** / *italic* become HTML.
        string s = Regex.Replace(escaped, @"\[img:([^\]]+)\]", m => $"<img src=\"{m.Groups[1].Value}\">");
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<b>$1</b>");
        s = Regex.Replace(s, @"(?<![*\w])\*(?=\S)(.+?)(?<=\S)\*(?![*\w])", "<i>$1</i>");
        return s;
    }
}
