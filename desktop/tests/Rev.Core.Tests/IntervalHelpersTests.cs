using Rev.Core.Scheduling;

namespace Rev.Core.Tests;

public class IntervalHelpersTests
{
    [Fact]
    public void RoundingSendsHalvesUpLikeTheIPhone()
    {
        Assert.Equal(3, IntervalHelpers.Round(2.5));
        Assert.Equal(6, IntervalHelpers.Round(5.5));
        Assert.Equal(2, IntervalHelpers.Round(2.49));
    }

    [Fact]
    public void FuzzFractionIsRepeatableAndBetween0And1()
    {
        for (long seed = -500; seed < 500; seed++)
        {
            double f = IntervalHelpers.FuzzFraction(seed);
            Assert.InRange(f, 0, 0.9999);
            Assert.Equal(f, IntervalHelpers.FuzzFraction(seed));
        }
    }

    [Fact]
    public void ShortIntervalsAreNotFuzzed()
    {
        Assert.Equal(1, IntervalHelpers.Fuzz(1, 0));
        Assert.Equal(2.4, IntervalHelpers.Fuzz(2.4, 1));
    }

    [Theory]
    [InlineData(4, 1.225)]          // 1 + 0.15 × 1.5
    [InlineData(10, 1.975)]         // 1 + 0.15 × 4.5 + 0.10 × 3
    [InlineData(30, 3.475)]         // 1 + 0.675 + 1.3 + 0.05 × 10
    public void FuzzStaysWithinAnkisSpread(double days, double spread)
    {
        Assert.Equal(days - spread, IntervalHelpers.Fuzz(days, 0), 6);
        Assert.Equal(days + spread, IntervalHelpers.Fuzz(days, 1), 6);
        Assert.Equal(days, IntervalHelpers.Fuzz(days, 0.5), 6);
    }

    [Fact]
    public void HardGoodEasyAreAlwaysInOrderEvenWhenTheyStartEqual()
    {
        var (hard, good, easy) = IntervalHelpers.ReviewIntervals(5, 5, 5, 0.5, 36_500);
        Assert.True(hard < good && good < easy);
    }

    [Fact]
    public void IntervalsNeverPassTheMaximum()
    {
        var (_, good, easy) = IntervalHelpers.ReviewIntervals(90, 100, 200, 0.9, 100);
        Assert.Equal(100, good);
        Assert.Equal(100, easy);
    }

    [Fact]
    public void HardOnTheFirstStepWaitsHalfwayBetweenSteps()
    {
        TimeSpan[] steps = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)];
        Assert.Equal(TimeSpan.FromMinutes(5.5), IntervalHelpers.HardDelay(steps, 0));
        Assert.Equal(TimeSpan.FromMinutes(10), IntervalHelpers.HardDelay(steps, 1));
        Assert.Equal(TimeSpan.FromMinutes(15), IntervalHelpers.HardDelay([TimeSpan.FromMinutes(10)], 0));
    }

    [Fact]
    public void GraduatedCardsAreDueAt4am()
    {
        var s = TestData.NewState();
        IntervalHelpers.Graduate(s, 3.4, TestData.Now, 36_500);
        Assert.Equal(CardPhase.Review, s.Phase);
        Assert.Equal(3, s.IntervalDays);
        Assert.Equal(TestData.TodayStart.AddDays(3), s.Due);
    }
}
