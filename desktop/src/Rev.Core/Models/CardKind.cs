namespace Rev.Core.Models;

/// <summary>The kinds of card you can make, like Anki's note types.</summary>
public enum CardKind
{
    /// <summary>Question on the front, answer on the back.</summary>
    Basic,
    /// <summary>Makes two cards: front → back, and back → front.</summary>
    Reversed,
    /// <summary>Fill in the blank: hide parts of a sentence with {{c1::...}}. One card per number.</summary>
    Cloze,
    /// <summary>Like basic, but you type your answer before seeing the right one.</summary>
    TypeIn,
}

/// <summary>The kind as text ("basic", "reversed", "cloze", "typeIn"), the same words Rev on iPhone stores.</summary>
public static class CardKindText
{
    public static string ToText(this CardKind kind) => kind switch
    {
        CardKind.Reversed => "reversed",
        CardKind.Cloze => "cloze",
        CardKind.TypeIn => "typeIn",
        _ => "basic",
    };

    public static CardKind ParseKind(string? text) => text switch
    {
        "reversed" => CardKind.Reversed,
        "cloze" => CardKind.Cloze,
        "typeIn" => CardKind.TypeIn,
        _ => CardKind.Basic,
    };
}
