namespace Rev.Core.Scheduling;

/// <summary>
/// Everything an algorithm needs to know about a card's schedule.
/// It's a "record", so <c>old with { }</c> makes a copy that can be changed without touching the original.
/// </summary>
public sealed record SchedulingState
{
    public CardPhase Phase { get; set; } = CardPhase.New;
    /// <summary>When the card should be shown next.</summary>
    public DateTime Due { get; set; }
    /// <summary>Days until the next review (for cards in the review phase).</summary>
    public double IntervalDays { get; set; }
    /// <summary>How easy the card is for you (SM-2). Starts at 2.5; higher means intervals grow faster.</summary>
    public double Ease { get; set; } = 2.5;
    /// <summary>How many times you have answered this card.</summary>
    public int Reps { get; set; }
    /// <summary>How many times you forgot it after it had graduated.</summary>
    public int Lapses { get; set; }
    /// <summary>Which short learning step the card is on.</summary>
    public int Step { get; set; }
    public DateTime? LastReviewed { get; set; }
    /// <summary>FSRS memory: how many days until recall drops to 90%.</summary>
    public double? Stability { get; set; }
    /// <summary>FSRS memory: how hard the card is, from 1 (easy) to 10 (hard).</summary>
    public double? Difficulty { get; set; }
    /// <summary>A per-card number used to spread intervals out a little ("fuzz").</summary>
    public long FuzzSeed { get; set; }
}
