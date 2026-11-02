namespace Rev.Core.Scheduling;

/// <summary>Where a card is in its life.</summary>
public enum CardPhase
{
    /// <summary>Never studied.</summary>
    New,
    /// <summary>Being learned for the first time (short steps, minutes).</summary>
    Learning,
    /// <summary>Graduated; shown again after some days.</summary>
    Review,
    /// <summary>Forgotten after graduating; short steps again.</summary>
    Relearning,
}

/// <summary>
/// The phase as text ("new", "learning", "review", "relearning"), the same words Rev on iPhone
/// stores. Used in the database, backups and (later) sync.
/// </summary>
public static class CardPhaseText
{
    public static string ToText(this CardPhase phase) => phase switch
    {
        CardPhase.Learning => "learning",
        CardPhase.Review => "review",
        CardPhase.Relearning => "relearning",
        _ => "new",
    };

    public static CardPhase ParsePhase(string? text) => text switch
    {
        "learning" => CardPhase.Learning,
        "review" => CardPhase.Review,
        "relearning" => CardPhase.Relearning,
        _ => CardPhase.New,
    };
}
