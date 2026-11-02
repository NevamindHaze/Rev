namespace Rev.Core.Scheduling;

/// <summary>
/// An SM-2 style spaced-repetition algorithm, the same as Rev on iPhone (Rev/Scheduling/SM2Scheduler.swift).
///
/// In plain words:
/// - New cards go through short "learning steps" (1 min, then 10 min) before they graduate.
/// - Graduated cards come back after a number of days. "Good" multiplies the interval
///   by the card's ease (starts at 2.5). "Hard" grows it a little and lowers the ease.
///   "Easy" grows it more and raises the ease.
/// - If you review a card late and still remember it, you get credit for the extra days.
/// - Intervals are spread a little (fuzz) so cards don't bunch up on one day.
/// - "Again" on a graduated card counts as a lapse: the ease drops, the interval is halved,
///   and the card goes back to a short relearning step (10 min).
/// </summary>
public sealed class Sm2Scheduler : ISchedulingAlgorithm
{
    public IReadOnlyList<TimeSpan> LearningSteps { get; init; } = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)];
    public IReadOnlyList<TimeSpan> RelearningSteps { get; init; } = [TimeSpan.FromMinutes(10)];
    public double GraduatingDays { get; init; } = 1;
    public double EasyGraduatingDays { get; init; } = 4;
    public double StartingEase { get; init; } = 2.5;
    public double MinimumEase { get; init; } = 1.3;
    public double MaximumDays { get; init; } = 36_500;

    public SchedulingState Next(SchedulingState old, Rating rating, DateTime now)
    {
        var s = old with { Reps = old.Reps + 1, LastReviewed = now };

        switch (old.Phase)
        {
            case CardPhase.New:
            case CardPhase.Learning:
                if (old.Phase == CardPhase.New) s.Ease = StartingEase;
                s.Phase = CardPhase.Learning;
                AnswerStep(s, rating, LearningSteps, now, GraduatingDays, EasyGraduatingDays);
                break;
            case CardPhase.Relearning:
                AnswerStep(s, rating, RelearningSteps, now, s.IntervalDays, s.IntervalDays + 1);
                break;
            case CardPhase.Review:
                AnswerReview(s, old, rating, now);
                break;
        }
        return s;
    }

    /// <summary>Answering a card that is on short steps (learning or relearning).</summary>
    private void AnswerStep(SchedulingState s, Rating rating, IReadOnlyList<TimeSpan> steps,
                            DateTime now, double graduateDays, double easyDays)
    {
        switch (rating)
        {
            case Rating.Again:
                s.Step = 0;
                s.Due = now + steps[0];
                break;
            case Rating.Hard:
                s.Due = now + IntervalHelpers.HardDelay(steps, s.Step);
                break;
            case Rating.Good:
                s.Step += 1;
                if (s.Step >= steps.Count) IntervalHelpers.Graduate(s, graduateDays, now, MaximumDays);
                else s.Due = now + steps[s.Step];
                break;
            case Rating.Easy:
                IntervalHelpers.Graduate(s, easyDays, now, MaximumDays);
                break;
        }
    }

    /// <summary>Answering a graduated card.</summary>
    private void AnswerReview(SchedulingState s, SchedulingState old, Rating rating, DateTime now)
    {
        double current = Math.Max(1, old.IntervalDays);
        // Days past the due date. Remembering a card after a delay earns a longer interval.
        double daysLate = Math.Max(0, old.Due.StudyDaysTo(now));
        var options = IntervalHelpers.ReviewIntervals(
            hard: Math.Max(current + 1, current * 1.2),
            good: (current + daysLate / 2) * old.Ease,
            easy: (current + daysLate) * old.Ease * 1.3,
            fraction: IntervalHelpers.FuzzFraction(old.FuzzSeed),
            maximum: MaximumDays);

        switch (rating)
        {
            case Rating.Again:
                s.Lapses += 1;
                s.Ease = Math.Max(MinimumEase, old.Ease - 0.2);
                s.IntervalDays = Math.Max(1, IntervalHelpers.Round(current * 0.5));
                s.Phase = CardPhase.Relearning;
                s.Step = 0;
                s.Due = now + RelearningSteps[0];
                break;
            case Rating.Hard:
                s.Ease = Math.Max(MinimumEase, old.Ease - 0.15);
                IntervalHelpers.Graduate(s, options.Hard, now, MaximumDays);
                break;
            case Rating.Good:
                IntervalHelpers.Graduate(s, options.Good, now, MaximumDays);
                break;
            case Rating.Easy:
                s.Ease = old.Ease + 0.15;
                IntervalHelpers.Graduate(s, options.Easy, now, MaximumDays);
                break;
        }
    }
}
