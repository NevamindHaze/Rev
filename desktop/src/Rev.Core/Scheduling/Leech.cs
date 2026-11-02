namespace Rev.Core.Scheduling;

/// <summary>
/// Like Anki: a card becomes a "leech" when you forget it for the 8th time after it had graduated,
/// and you're reminded again every 4 more (12th, 16th, …). Rev then offers to rewrite or suspend it.
/// </summary>
public static class Leech
{
    public const int Threshold = 8;
    public const int RemindEvery = 4;

    /// <summary>True if this answer just made the card a leech (or is a new leech reminder).</summary>
    public static bool JustBecameLeech(SchedulingState before, Rating rating, SchedulingState after) =>
        rating == Rating.Again
        && before.Phase == CardPhase.Review
        && after.Lapses >= Threshold
        && (after.Lapses - Threshold) % RemindEvery == 0;
}
