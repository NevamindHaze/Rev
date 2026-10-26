using System.Globalization;
using Rev.Core.Scheduling;

namespace Rev.Core.Data;

/// <summary>Your settings, with Rev's defaults.</summary>
public sealed record AppSettings
{
    /// <summary>Shown in the greeting on the Home screen ("Hi, Sam 👋").</summary>
    public string UserName { get; init; } = "";
    public int NewCardsPerDay { get; init; } = 20;
    public SchedulerKind Scheduler { get; init; } = SchedulerKind.Fsrs;
    /// <summary>FSRS target memory, 0.80 – 0.97.</summary>
    public double DesiredRetention { get; init; } = SchedulerFactory.DefaultRetention;

    /// <summary>The algorithm these settings ask for.</summary>
    public ISchedulingAlgorithm CreateScheduler() => SchedulerFactory.Create(Scheduler, DesiredRetention);
}

// Settings are stored as simple name → text pairs in the settings table.
public sealed partial class RevDatabase
{
    public AppSettings GetSettings()
    {
        var values = Query("SELECT key, value FROM settings", r => (r.GetString(0), r.GetString(1)))
            .ToDictionary(pair => pair.Item1, pair => pair.Item2);
        var defaults = new AppSettings();
        return new AppSettings
        {
            UserName = values.GetValueOrDefault("userName", defaults.UserName),
            NewCardsPerDay = int.TryParse(values.GetValueOrDefault("newCardsPerDay"), out int perDay)
                ? Math.Max(0, perDay) : defaults.NewCardsPerDay,
            Scheduler = values.GetValueOrDefault("scheduler") == "sm2" ? SchedulerKind.Sm2 : SchedulerKind.Fsrs,
            DesiredRetention = double.TryParse(values.GetValueOrDefault("desiredRetention"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double retention)
                ? Math.Clamp(retention, SchedulerFactory.MinimumRetention, SchedulerFactory.MaximumRetention)
                : defaults.DesiredRetention,
        };
    }

    public void SaveSettings(AppSettings settings)
    {
        InTransaction(() =>
        {
            SetValue("userName", settings.UserName.Trim());
            SetValue("newCardsPerDay", settings.NewCardsPerDay.ToString(CultureInfo.InvariantCulture));
            SetValue("scheduler", settings.Scheduler == SchedulerKind.Sm2 ? "sm2" : "fsrs");
            SetValue("desiredRetention", settings.DesiredRetention.ToString(CultureInfo.InvariantCulture));
        });
    }

    private void SetValue(string key, string value) =>
        Execute("INSERT INTO settings (key, value) VALUES ($key, $value) " +
                "ON CONFLICT (key) DO UPDATE SET value = excluded.value",
            ("$key", key), ("$value", value));
}
