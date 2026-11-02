namespace Rev.Core.Scheduling;

/// <summary>Small pieces shared by both algorithms.</summary>
public static class IntervalHelpers
{
    /// <summary>
    /// Rounds .5 up (2.5 → 3), the way Rev on iPhone does.
    /// (C#'s normal Math.Round would give 2, which would make intervals differ from the iPhone.)
    /// </summary>
    public static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>
    /// A number between 0 and 1 that stays the same for one card at one moment,
    /// so the times shown on the buttons match what really happens when you answer.
    /// (A "splitmix" scramble: the same seed always gives the same number.)
    /// </summary>
    public static double FuzzFraction(long seed)
    {
        unchecked
        {
            ulong x = (ulong)seed + 0x9E37_79B9_7F4A_7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58_476D_1CE4_E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D0_49BB_1331_11EBUL;
            x ^= x >> 31;
            return (x % 10_000) / 10_000.0;
        }
    }

    /// <summary>
    /// Moves an interval a few percent up or down, like Anki does,
    /// so cards learned together don't all come due on the same day.
    /// <paramref name="fraction"/> 0 means "as early as allowed", 1 "as late as allowed".
    /// </summary>
    public static double Fuzz(double days, double fraction)
    {
        if (days < 2.5) return days;
        double spread = 1.0;
        spread += 0.15 * Math.Max(0, Math.Min(days, 7) - 2.5);
        spread += 0.10 * Math.Max(0, Math.Min(days, 20) - 7);
        spread += 0.05 * Math.Max(0, days - 20);
        return days + spread * (2 * fraction - 1);
    }

    /// <summary>Fuzzes and rounds the Hard / Good / Easy intervals, keeping Hard &lt; Good &lt; Easy.</summary>
    public static (double Hard, double Good, double Easy) ReviewIntervals(
        double hard, double good, double easy, double fraction, double maximum)
    {
        double Finish(double days) => Math.Min(maximum, Math.Max(1, Round(Fuzz(days, fraction))));
        double hardDays = Finish(hard);
        double goodDays = Math.Min(maximum, Math.Max(Finish(good), hardDays + 1));
        double easyDays = Math.Min(maximum, Math.Max(Finish(easy), goodDays + 1));
        return (hardDays, goodDays, easyDays);
    }

    /// <summary>Delay for "Hard" on a learning step: a bit longer than Again, never longer than Good.</summary>
    public static TimeSpan HardDelay(IReadOnlyList<TimeSpan> steps, int step)
    {
        if (steps.Count == 1) return steps[0] * 1.5;
        if (step == 0) return (steps[0] + steps[1]) / 2;
        return steps[Math.Min(step, steps.Count - 1)];
    }

    /// <summary>Puts a card in the "review" phase, due in a whole number of study days (at 4 am).</summary>
    public static void Graduate(SchedulingState s, double days, DateTime now, double maximum)
    {
        double whole = Math.Min(maximum, Math.Max(1, Round(days)));
        s.Phase = CardPhase.Review;
        s.Step = 0;
        s.IntervalDays = whole;
        s.Due = now.StartOfStudyDay().AddDays(whole);
    }
}
