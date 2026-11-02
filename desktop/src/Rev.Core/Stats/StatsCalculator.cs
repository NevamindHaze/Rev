using Rev.Core.Models;
using Rev.Core.Scheduling;

namespace Rev.Core.Stats;

/// <summary>One bar in the "last 7 days" chart.</summary>
public sealed record DayCount(DateTime Day, int Count, bool IsToday)
{
    /// <summary>Short weekday name in your computer's language, e.g. "Mon".</summary>
    public string Label => Day.ToString("ddd");
}

/// <summary>Counts things from the review history for the Stats screen. Days start at 4 am.</summary>
public static class StatsCalculator
{
    /// <summary>How many answers were given on each study day (keyed by that day's 4 am start).</summary>
    public static Dictionary<DateTime, int> CountsByDay(IEnumerable<ReviewLog> logs) =>
        logs.GroupBy(l => l.Date.StartOfStudyDay()).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Days in a row with at least one review. If you haven't studied yet today, the streak still
    /// counts up to yesterday, so it doesn't reset in the morning.
    /// </summary>
    public static int Streak(IEnumerable<ReviewLog> logs, DateTime now)
    {
        var counts = CountsByDay(logs);
        var day = now.StartOfStudyDay();
        if (!counts.ContainsKey(day)) day = day.AddDays(-1);
        int streak = 0;
        while (counts.ContainsKey(day))
        {
            streak += 1;
            day = day.AddDays(-1);
        }
        return streak;
    }

    /// <summary>Reviews per day for the last <paramref name="count"/> days, oldest first (today last).</summary>
    public static List<DayCount> LastDays(IEnumerable<ReviewLog> logs, int count, DateTime now)
    {
        var counts = CountsByDay(logs);
        var today = now.StartOfStudyDay();
        return Enumerable.Range(0, count)
            .Select(offset => today.AddDays(offset - (count - 1)))
            .Select(day => new DayCount(day, counts.GetValueOrDefault(day), day == today))
            .ToList();
    }
}
