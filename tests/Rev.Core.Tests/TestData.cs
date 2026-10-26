using Rev.Core.Models;
using Rev.Core.Scheduling;

namespace Rev.Core.Tests;

/// <summary>Shared starting points for the tests.</summary>
internal static class TestData
{
    /// <summary>A fixed "now": Monday 26 October 2026, 3 pm.</summary>
    public static readonly DateTime Now = new(2026, 10, 26, 15, 0, 0, DateTimeKind.Local);

    /// <summary>4 am today, when today's study day began.</summary>
    public static readonly DateTime TodayStart = new(2026, 10, 26, 4, 0, 0, DateTimeKind.Local);

    public static SchedulingState NewState(long seed = 1) => new() { Due = Now, FuzzSeed = seed };

    /// <summary>A graduated card last seen <paramref name="interval"/> days ago, due today.</summary>
    public static SchedulingState ReviewState(double interval = 10, long seed = 1, int daysLate = 0,
                                              double? stability = 10, double? difficulty = 5) => new()
    {
        Phase = CardPhase.Review,
        IntervalDays = interval,
        Due = TodayStart.AddDays(-daysLate),
        LastReviewed = TodayStart.AddDays(-daysLate - interval).AddHours(10),
        Reps = 5,
        Stability = stability,
        Difficulty = difficulty,
        FuzzSeed = seed,
    };

    public static Card MakeCard(CardPhase phase, DateTime due, string? noteId = null, DateTime? createdAt = null,
                                bool suspended = false)
    {
        var card = new Card
        {
            Phase = phase,
            Due = due,
            IsSuspended = suspended,
            CreatedAt = createdAt ?? Now.AddDays(-30),
        };
        if (noteId != null) card.NoteId = noteId;
        return card;
    }
}
