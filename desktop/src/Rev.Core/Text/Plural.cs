namespace Rev.Core.Text;

/// <summary>Writes counts with the right word: "1 card", "2 cards", "0 cards".</summary>
public static class Plural
{
    /// <example>3.Counted("card") → "3 cards"; 1.Counted("lapse") → "1 lapse"</example>
    public static string Counted(this int count, string singular, string? plural = null) =>
        $"{count} {(count == 1 ? singular : plural ?? singular + "s")}";
}
