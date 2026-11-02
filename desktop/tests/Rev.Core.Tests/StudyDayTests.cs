using Rev.Core.Scheduling;

namespace Rev.Core.Tests;

public class StudyDayTests
{
    private static DateTime At(int day, int hour, int minute = 0) =>
        new(2026, 10, day, hour, minute, 0, DateTimeKind.Local);

    [Fact]
    public void AfterMidnightButBefore4amStillCountsAsYesterday() =>
        Assert.Equal(At(26, 4), At(27, 2, 30).StartOfStudyDay());

    [Fact]
    public void FourAmStartsANewDay() =>
        Assert.Equal(At(27, 4), At(27, 4).StartOfStudyDay());

    [Fact]
    public void TheDayEndsAt4amTomorrow() =>
        Assert.Equal(At(27, 4), At(26, 15).EndOfStudyDay());

    [Theory]
    [InlineData(27, 3, 59, 0)]   // 11 pm → 3:59 am: same study day
    [InlineData(27, 4, 0, 1)]    // 11 pm → 4 am: next study day
    [InlineData(29, 12, 0, 3)]
    public void StudyDaysAreCountedFrom4am(int day, int hour, int minute, int expected) =>
        Assert.Equal(expected, At(26, 23).StudyDaysTo(At(day, hour, minute)));

    [Fact]
    public void StudyDaysCanBeNegative() =>
        Assert.Equal(-2, At(28, 12).StudyDaysTo(At(26, 12)));
}
