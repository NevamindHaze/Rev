using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public class Sm2SchedulerTests
{
    private readonly Sm2Scheduler sm2 = new();

    private static SchedulingState Sm2Review(double interval = 10, double ease = 2.5, int daysLate = 0, long seed = 1) =>
        ReviewState(interval, seed, daysLate, stability: null, difficulty: null) with { Ease = ease };

    [Fact]
    public void NewCardsGoThroughTheLearningSteps()
    {
        var first = sm2.Next(NewState(), Rating.Good, Now);
        Assert.Equal(CardPhase.Learning, first.Phase);
        Assert.Equal(Now.AddMinutes(10), first.Due);
        Assert.Equal(2.5, first.Ease);

        var second = sm2.Next(first, Rating.Good, Now.AddMinutes(10));
        Assert.Equal(CardPhase.Review, second.Phase);
        Assert.Equal(1, second.IntervalDays);
        Assert.Equal(TodayStart.AddDays(1), second.Due);           // tomorrow at 4 am
    }

    [Fact]
    public void EasyOnANewCardGraduatesIn4Days()
    {
        var s = sm2.Next(NewState(), Rating.Easy, Now);
        Assert.Equal(CardPhase.Review, s.Phase);
        Assert.Equal(4, s.IntervalDays);
    }

    [Fact]
    public void GoodMultipliesTheIntervalByTheEase()
    {
        var s = sm2.Next(Sm2Review(), Rating.Good, Now);
        Assert.InRange(s.IntervalDays, 22, 28);                    // 10 × 2.5 = 25, fuzzed
        Assert.Equal(2.5, s.Ease);
    }

    [Fact]
    public void HardGrowsALittleAndLowersTheEase()
    {
        var s = sm2.Next(Sm2Review(), Rating.Hard, Now);
        Assert.InRange(s.IntervalDays, 10, 14);                    // 10 × 1.2 = 12, fuzzed
        Assert.Equal(2.35, s.Ease, 6);
    }

    [Fact]
    public void EasyRaisesTheEase()
    {
        var s = sm2.Next(Sm2Review(), Rating.Easy, Now);
        Assert.Equal(2.65, s.Ease, 6);
        Assert.InRange(s.IntervalDays, 29, 36);                    // 10 × 2.5 × 1.3 = 32.5, fuzzed
    }

    [Fact]
    public void AgainHalvesTheIntervalAndCountsALapse()
    {
        var s = sm2.Next(Sm2Review(), Rating.Again, Now);
        Assert.Equal(CardPhase.Relearning, s.Phase);
        Assert.Equal(1, s.Lapses);
        Assert.Equal(5, s.IntervalDays);
        Assert.Equal(2.3, s.Ease, 6);
        Assert.Equal(Now.AddMinutes(10), s.Due);

        var relearned = sm2.Next(s, Rating.Good, Now.AddMinutes(10));
        Assert.Equal(CardPhase.Review, relearned.Phase);
        Assert.Equal(5, relearned.IntervalDays);
    }

    [Fact]
    public void EaseNeverDropsBelow130Percent()
    {
        var s = sm2.Next(Sm2Review(ease: 1.35), Rating.Again, Now);
        Assert.Equal(1.3, s.Ease, 6);
    }

    [Fact]
    public void RememberingAnOverdueCardEarnsALongerInterval()
    {
        var late = sm2.Next(Sm2Review(daysLate: 10), Rating.Good, Now);
        Assert.InRange(late.IntervalDays, 34, 41);                 // (10 + 10 / 2) × 2.5 = 37.5, fuzzed
    }

    [Fact]
    public void HardIsShorterThanGoodWhichIsShorterThanEasyForEveryCard()
    {
        for (long seed = 0; seed < 2000; seed++)
        {
            var before = Sm2Review(interval: 1 + seed % 40, ease: 1.3 + seed % 15 / 10.0, seed: seed);
            double hard = sm2.Next(before, Rating.Hard, Now).IntervalDays;
            double good = sm2.Next(before, Rating.Good, Now).IntervalDays;
            double easy = sm2.Next(before, Rating.Easy, Now).IntervalDays;
            Assert.True(hard < good && good < easy, $"seed {seed}: {hard} / {good} / {easy}");
        }
    }
}
