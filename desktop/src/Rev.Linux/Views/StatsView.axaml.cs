using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Rev.Core.Scheduling;
using Rev.Core.Stats;
using Rev.Linux.Controls;

namespace Rev.Linux.Views;

/// <summary>The Stats screen. Everything is worked out from your cards and review history.</summary>
public partial class StatsView : UserControl, IScreen
{
    /// <summary>The tallest bar, in pixels; the other bars are scaled to it.</summary>
    private const double MaxBarHeight = 150;

    public StatsView()
    {
        InitializeComponent();
        // Side by side when there's room, one above the other when narrow.
        Columns.SizeChanged += (_, e) =>
        {
            bool wide = e.NewSize.Width >= 900;
            Grid.SetColumn(WeekColumn, wide ? 2 : 0);
            Grid.SetRow(WeekColumn, wide ? 0 : 1);
            Grid.SetColumnSpan(WeekColumn, wide ? 1 : 3);
            Grid.SetColumnSpan(TodayColumn, wide ? 1 : 3);
            WeekColumn.Margin = new Thickness(0, wide ? 0 : 14, 0, 0);
        };
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var now = DateTime.Now;
        var db = App.Database;
        var cards = db.GetCards();
        // Two years of history is plenty for the streak; the chart only needs a week.
        var logs = db.GetReviewLogs(now.StartOfStudyDay().AddDays(-730));

        int due = cards.DueCount(now);
        DueNumber.Text = due.ToString();
        DueLabel.Text = due == 1 ? "card due today" : "cards due today";

        int streak = StatsCalculator.Streak(logs, now);
        StreakNumber.Text = streak.ToString();
        StreakLabel.Text = streak == 1 ? "day" : "days";

        ReviewedTile.Value = db.ReviewsToday(now).ToString();
        NewTile.Value = cards.NewCount().ToString();
        TotalTile.Value = cards.Count.ToString();

        DrawChart(StatsCalculator.LastDays(logs, 7, now));
    }

    /// <summary>One column per day: the count, a rounded bar, and the weekday. Today's bar is coral.</summary>
    private void DrawChart(List<DayCount> days)
    {
        Chart.Children.Clear();
        int most = Math.Max(1, days.Max(d => d.Count));
        foreach (var day in days)
        {
            var bar = new Border
            {
                Width = 42,
                // Days with no reviews still get a thin bar, so the chart never looks broken.
                Height = Math.Max(6, MaxBarHeight * day.Count / most),
                CornerRadius = new CornerRadius(10),
                VerticalAlignment = VerticalAlignment.Bottom,
                Opacity = day.Count == 0 ? 0.35 : 1,
            }.Colour(Border.BackgroundProperty, day.IsToday ? "CoralBrush" : "TealBrush");

            var count = new TextBlock
            {
                Text = day.Count.ToString(), FontSize = 14, FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6),
            }.Colour(TextBlock.ForegroundProperty, "InkBrush");

            var label = new TextBlock
            {
                Text = day.IsToday ? "Today" : day.Label, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0), FontWeight = day.IsToday ? FontWeight.Bold : FontWeight.Normal,
            }.Colour(TextBlock.ForegroundProperty, day.IsToday ? "InkBrush" : "MutedInkBrush");

            var column = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(label, Dock.Bottom);
            DockPanel.SetDock(bar, Dock.Bottom);
            DockPanel.SetDock(count, Dock.Bottom);
            column.Children.Add(label);
            column.Children.Add(bar);
            column.Children.Add(count);
            Chart.Children.Add(column);
        }
    }

}
