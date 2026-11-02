using Rev.Core.Scheduling;
using static Rev.Core.Tests.TestData;

namespace Rev.Core.Tests;

public class FsrsSchedulerTests
{
    private readonly FsrsScheduler fsrs = new();

    [Fact]
    public void FirstGoodStartsMemoryAndGoesToThe10MinuteStep()
    {
        var s = fsrs.Next(NewState(), Rating.Good, Now);
        Assert.Equal(CardPhase.Learning, s.Phase);
        Assert.Equal(1, s.Step);
        Assert.Equal(Now.AddMinutes(10), s.Due);
        Assert.Equal(3.173, s.Stability!.Value, 6);                // w[2]
        Assert.Equal(5.28243, s.Difficulty!.Value, 4);             // w[4] − e^(2·w[5]) + 1
        Assert.Equal(1, s.Reps);
        Assert.Equal(Now, s.LastReviewed);
    }

    [Fact]
    public void FirstAgainAndHardUseTheShortSteps()
    {
        Assert.Equal(Now.AddMinutes(1), fsrs.Next(NewState(), Rating.Again, Now).Due);
        Assert.Equal(Now.AddMinutes(5.5), fsrs.Next(NewState(), Rating.Hard, Now).Due);
    }

    [Fact]
    public void FirstEasyGraduatesStraightAway()
    {
        var s = fsrs.Next(NewState(), Rating.Easy, Now);
        Assert.Equal(CardPhase.Review, s.Phase);
        Assert.Equal(15.69105, s.Stability!.Value, 5);             // w[3]
        Assert.InRange(s.IntervalDays, 13, 18);                    // 15.7 days, fuzzed
        Assert.Equal(TodayStart.AddDays(s.IntervalDays), s.Due);
    }

    [Fact]
    public void TwoGoodsGraduateWithAShortTermStabilityBoost()
    {
        var first = fsrs.Next(NewState(), Rating.Good, Now);
        var second = fsrs.Next(first, Rating.Good, Now.AddMinutes(10));
        Assert.Equal(CardPhase.Review, second.Phase);
        Assert.Equal(4.4669, second.Stability!.Value, 3);          // 3.173 × e^(w[17] × w[18])
        Assert.InRange(second.IntervalDays, 3, 6);
    }

    [Fact]
    public void AgainOnAGraduatedCardIsALapse()
    {
        var before = ReviewState();
        var s = fsrs.Next(before, Rating.Again, Now);
        Assert.Equal(CardPhase.Relearning, s.Phase);
        Assert.Equal(before.Lapses + 1, s.Lapses);
        Assert.Equal(Now.AddMinutes(10), s.Due);
        Assert.True(s.Stability < before.Stability);
        Assert.True(s.IntervalDays >= 1);
    }

    [Fact]
    public void HardIsShorterThanGoodWhichIsShorterThanEasyForEveryCard()
    {
        for (long seed = 0; seed < 2000; seed++)
        {
            var before = ReviewState(interval: 1 + seed % 60, seed: seed, stability: 1 + seed % 60);
            double hard = fsrs.Next(before, Rating.Hard, Now).IntervalDays;
            double good = fsrs.Next(before, Rating.Good, Now).IntervalDays;
            double easy = fsrs.Next(before, Rating.Easy, Now).IntervalDays;
            Assert.True(hard < good && good < easy, $"seed {seed}: {hard} / {good} / {easy}");
        }
    }

    [Fact]
    public void RememberingAnOverdueCardEarnsALongerInterval()
    {
        var onTime = fsrs.Next(ReviewState(daysLate: 0), Rating.Good, Now);
        var late = fsrs.Next(ReviewState(daysLate: 20), Rating.Good, Now);
        Assert.True(late.Stability > onTime.Stability);
        Assert.True(late.IntervalDays > onTime.IntervalDays);
    }

    [Fact]
    public void AHigherMemoryTargetGivesShorterIntervals()
    {
        var relaxed = new FsrsScheduler { DesiredRetention = 0.80 }.Next(ReviewState(), Rating.Good, Now);
        var strict = new FsrsScheduler { DesiredRetention = 0.97 }.Next(ReviewState(), Rating.Good, Now);
        Assert.True(strict.IntervalDays < relaxed.IntervalDays);
    }

    [Fact]
    public void MemoryTargetIsKeptBetween80And97Percent()
    {
        var low = (FsrsScheduler)SchedulerFactory.Create(SchedulerKind.Fsrs, 0.5);
        var high = (FsrsScheduler)SchedulerFactory.Create(SchedulerKind.Fsrs, 0.99);
        Assert.Equal(0.80, low.DesiredRetention);
        Assert.Equal(0.97, high.DesiredRetention);
    }

    [Fact]
    public void CardsWithoutFsrsMemoryGetAnEstimateFromIntervalAndEase()
    {
        // e.g. imported from Anki with SM-2 progress only.
        var before = ReviewState(interval: 20, stability: null, difficulty: null) with { Ease = 2.5 };
        var s = fsrs.Next(before, Rating.Good, Now);
        Assert.NotNull(s.Stability);
        Assert.True(s.Stability > 20);
        Assert.InRange(s.Difficulty!.Value, 1, 10);
    }

    [Fact]
    public void DifficultyStaysBetween1And10()
    {
        var s = ReviewState(difficulty: 9.9);
        for (int i = 0; i < 20; i++) s = fsrs.Next(s with { Phase = CardPhase.Review }, Rating.Again, Now);
        Assert.InRange(s.Difficulty!.Value, 1, 10);

        s = ReviewState(difficulty: 1.1);
        for (int i = 0; i < 20; i++) s = fsrs.Next(s, Rating.Easy, s.Due);
        Assert.InRange(s.Difficulty!.Value, 1, 10);
    }

    [Fact]
    public void RelearningGoodGoesBackToReview()
    {
        var lapsed = fsrs.Next(ReviewState(), Rating.Again, Now);
        var s = fsrs.Next(lapsed, Rating.Good, Now.AddMinutes(10));
        Assert.Equal(CardPhase.Review, s.Phase);
        Assert.True(s.IntervalDays >= 1);
    }

    [Fact]
    public void ButtonPreviewsMatchWhatHappens()
    {
        var before = ReviewState();
        var previews = fsrs.Previews(before, Now);
        foreach (var rating in Enum.GetValues<Rating>())
            Assert.Equal(previews[rating], fsrs.Next(before, rating, Now).WaitTextFrom(Now));
    }
}
