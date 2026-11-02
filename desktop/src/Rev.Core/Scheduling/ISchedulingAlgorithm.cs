namespace Rev.Core.Scheduling;

/// <summary>
/// The "plug" every scheduling algorithm must fit. FSRS and SM-2 both fit it,
/// so the rest of the app never needs to know which one is switched on.
/// </summary>
public interface ISchedulingAlgorithm
{
    /// <summary>The card's new schedule after you answer it with <paramref name="rating"/> at <paramref name="now"/>.</summary>
    SchedulingState Next(SchedulingState state, Rating rating, DateTime now);
}

/// <summary>The two algorithms you can choose in Settings.</summary>
public enum SchedulerKind
{
    Fsrs,
    Sm2,
}

/// <summary>Builds the algorithm chosen in Settings.</summary>
public static class SchedulerFactory
{
    /// <summary>The "target memory" setting: your chance of still remembering a card when it comes back.</summary>
    public const double DefaultRetention = 0.90;
    public const double MinimumRetention = 0.80;
    public const double MaximumRetention = 0.97;

    public static ISchedulingAlgorithm Create(SchedulerKind kind, double desiredRetention = DefaultRetention) =>
        kind switch
        {
            SchedulerKind.Sm2 => new Sm2Scheduler(),
            _ => new FsrsScheduler
            {
                DesiredRetention = Math.Clamp(desiredRetention, MinimumRetention, MaximumRetention),
            },
        };
}
