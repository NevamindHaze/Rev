using Rev.Core.Models;
using Rev.Core.Stats;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public class StatsTests
{
    /// <summary>One review on each of these days (0 = today, 1 = yesterday, …), at noon.</summary>
    private static List<ReviewLog> ReviewsOn(params int[] daysAgo) =>
        daysAgo.Select(d => new ReviewLog { CardId = Guid.NewGuid(), Date = TodayStart.AddDays(-d).AddHours(8) }).ToList();

    [Fact]
    public void StreakCountsDaysInARow()
    {
        Assert.Equal(3, StatsCalculator.Streak(ReviewsOn(0, 1, 2, 4), Now));
        Assert.Equal(0, StatsCalculator.Streak(ReviewsOn(), Now));
    }

    [Fact]
    public void StreakDoesNotResetBeforeYouStudyToday() =>
        Assert.Equal(2, StatsCalculator.Streak(ReviewsOn(1, 2), Now));

    [Fact]
    public void AMissedDayEndsTheStreak() =>
        Assert.Equal(0, StatsCalculator.Streak(ReviewsOn(2, 3), Now));

    [Fact]
    public void ReviewsAfterMidnightCountForTheDayBefore()
    {
        // 1 am today belongs to yesterday's study day (the day starts at 4 am).
        var lateNight = new ReviewLog { Date = TodayStart.AddHours(-3) };
        Assert.Equal(1, StatsCalculator.Streak([lateNight], Now));
        Assert.Equal(1, StatsCalculator.LastDays([lateNight], 7, Now)[5].Count);
    }

    [Fact]
    public void LastSevenDaysEndWithToday()
    {
        var days = StatsCalculator.LastDays(ReviewsOn(0, 0, 0, 1, 6, 7), 7, Now);
        Assert.Equal(7, days.Count);
        Assert.Equal([1, 0, 0, 0, 0, 1, 3], days.Select(d => d.Count));     // 7 days ago isn't shown
        Assert.True(days[6].IsToday);
        Assert.False(days[5].IsToday);
        Assert.Equal(TodayStart, days[6].Day);
        Assert.Equal(TodayStart.AddDays(-6), days[0].Day);
    }
}
