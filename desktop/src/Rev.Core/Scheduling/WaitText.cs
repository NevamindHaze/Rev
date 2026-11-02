using System.Globalization;

namespace Rev.Core.Scheduling;

/// <summary>Short text for the answer buttons, e.g. "10 min", "3 d", "1.5 mo".</summary>
public static class WaitText
{
    /// <summary>How long until this (already answered) state comes back, as button text.</summary>
    public static string WaitTextFrom(this SchedulingState state, DateTime now)
    {
        if (state.Phase == CardPhase.Review) return FormatDays(state.IntervalDays);
        int minutes = Math.Max(1, (int)IntervalHelpers.Round((state.Due - now).TotalMinutes));
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h";
    }

    public static string FormatDays(double days)
    {
        int whole = (int)IntervalHelpers.Round(days);
        if (whole < 31) return $"{whole} d";
        if (whole < 365) return (days / 30).ToString("0.0", CultureInfo.InvariantCulture) + " mo";
        return (days / 365).ToString("0.0", CultureInfo.InvariantCulture) + " y";
    }

    /// <summary>The text for all four buttons at once.</summary>
    public static IReadOnlyDictionary<Rating, string> Previews(
        this ISchedulingAlgorithm algorithm, SchedulingState state, DateTime now) =>
        Enum.GetValues<Rating>().ToDictionary(r => r, r => algorithm.Next(state, r, now).WaitTextFrom(now));
}
