namespace Rev.Core.Scheduling;

/// <summary>
/// Like Anki, a "study day" starts at 4 am, not midnight,
/// so a late-night session after 12 still counts as the same day.
/// All times here are your computer's local time.
/// </summary>
public static class StudyDay
{
    public const int StartHour = 4;

    /// <summary>When the study day containing this moment began (4 am).</summary>
    public static DateTime StartOfStudyDay(this DateTime moment)
    {
        // Move back 4 hours, take that calendar day's midnight, then move forward 4 hours again.
        // e.g. 2:30 am on the 27th → 10:30 pm on the 26th → midnight 26th → 4 am on the 26th.
        var midnight = moment.AddHours(-StartHour).Date;
        return DateTime.SpecifyKind(midnight.AddHours(StartHour), moment.Kind);
    }

    /// <summary>When the next study day begins (4 am tomorrow).</summary>
    public static DateTime EndOfStudyDay(this DateTime moment) => moment.StartOfStudyDay().AddDays(1);

    /// <summary>Whole study days from this moment to <paramref name="other"/> (negative if other is earlier).</summary>
    public static int StudyDaysTo(this DateTime moment, DateTime other) =>
        (other.StartOfStudyDay().Date - moment.StartOfStudyDay().Date).Days;
}
