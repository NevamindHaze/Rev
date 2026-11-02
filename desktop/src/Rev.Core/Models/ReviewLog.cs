using Rev.Core.Scheduling;

namespace Rev.Core.Models;

/// <summary>
/// One row for every answer you give. Used for stats, the streak and the daily new-card limit.
/// Times are kept to the millisecond (like Anki's review ids), so the same review is never counted twice.
/// </summary>
public sealed class ReviewLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CardId { get; set; }
    public DateTime Date { get; set; }
    public Rating Rating { get; set; } = Rating.Good;
    /// <summary>True if this was the first time the card was ever studied.</summary>
    public bool WasNew { get; set; }
    /// <summary>The interval the card got after this answer.</summary>
    public double IntervalDays { get; set; }
}
