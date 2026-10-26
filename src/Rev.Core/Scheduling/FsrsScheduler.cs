namespace Rev.Core.Scheduling;

/// <summary>
/// FSRS (Free Spaced Repetition Scheduler), version 5, with its published default parameters.
/// The same rules as Rev on iPhone (Rev/Scheduling/FSRSScheduler.swift).
///
/// In plain words, FSRS keeps two numbers for every card:
/// - Stability (S): how many days until your chance of remembering drops to 90%.
/// - Difficulty (D): how hard the card is for you, from 1 to 10.
/// After each answer it updates S and D, then schedules the card for the day your
/// chance of remembering falls to your target (90% by default, changeable in Settings).
/// New and forgotten cards still use short steps in minutes first, like SM-2.
/// </summary>
public sealed class FsrsScheduler : ISchedulingAlgorithm
{
    public double DesiredRetention { get; init; } = 0.9;
    public IReadOnlyList<TimeSpan> LearningSteps { get; init; } = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)];
    public IReadOnlyList<TimeSpan> RelearningSteps { get; init; } = [TimeSpan.FromMinutes(10)];
    public double MaximumDays { get; init; } = 36_500;

    /// <summary>FSRS-5 default weights (w[0] … w[18]).</summary>
    private static readonly double[] W =
    [
        0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575, 0.1192,
        1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621,
    ];
    private const double Decay = -0.5;
    private const double Factor = 19.0 / 81.0;

    public SchedulingState Next(SchedulingState old, Rating rating, DateTime now)
    {
        var s = old with { Reps = old.Reps + 1, LastReviewed = now };
        double fraction = IntervalHelpers.FuzzFraction(old.FuzzSeed);

        // New card: memory starts from the first answer.
        if (old.Phase == CardPhase.New)
        {
            s.Stability = W[(int)rating - 1];
            s.Difficulty = InitialDifficulty(rating);
            s.Phase = CardPhase.Learning;
            AnswerStep(s, rating, LearningSteps, now, fraction);
            return s;
        }

        var (stability, difficulty) = CurrentMemory(old);
        double elapsedDays = Math.Max(0, LastReview(old).StudyDaysTo(now));
        double recall = Retrievability(elapsedDays, stability);
        s.Difficulty = NextDifficulty(difficulty, rating);

        if (old.Phase != CardPhase.Review)
        {
            // Short steps (minutes). Seen again the same day: small stability change.
            s.Stability = elapsedDays < 1
                ? ShortTermStability(stability, rating)
                : StabilityAfter(rating, difficulty, stability, recall);
            var steps = old.Phase == CardPhase.Relearning ? RelearningSteps : LearningSteps;
            AnswerStep(s, rating, steps, now, fraction);
            return s;
        }

        // A graduated card.
        if (rating == Rating.Again)
        {
            double forgotten = StabilityAfter(Rating.Again, difficulty, stability, recall);
            s.Stability = forgotten;
            s.Lapses += 1;
            s.IntervalDays = Math.Max(1, IntervalHelpers.Round(Interval(forgotten)));
            s.Phase = CardPhase.Relearning;
            s.Step = 0;
            s.Due = now + RelearningSteps[0];
            return s;
        }

        // Work out all three so Hard < Good < Easy always holds, then pick yours.
        double hardS = StabilityAfter(Rating.Hard, difficulty, stability, recall);
        double goodS = StabilityAfter(Rating.Good, difficulty, stability, recall);
        double easyS = StabilityAfter(Rating.Easy, difficulty, stability, recall);
        var options = IntervalHelpers.ReviewIntervals(
            Interval(hardS), Interval(goodS), Interval(easyS), fraction, MaximumDays);

        switch (rating)
        {
            case Rating.Hard:
                s.Stability = hardS;
                IntervalHelpers.Graduate(s, options.Hard, now, MaximumDays);
                break;
            case Rating.Easy:
                s.Stability = easyS;
                IntervalHelpers.Graduate(s, options.Easy, now, MaximumDays);
                break;
            default:
                s.Stability = goodS;
                IntervalHelpers.Graduate(s, options.Good, now, MaximumDays);
                break;
        }
        return s;
    }

    // ---- Steps (minutes) ----

    private void AnswerStep(SchedulingState s, Rating rating, IReadOnlyList<TimeSpan> steps,
                            DateTime now, double fraction)
    {
        double graduateDays = IntervalHelpers.Fuzz(Interval(s.Stability ?? W[2]), fraction);
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
                IntervalHelpers.Graduate(s, graduateDays, now, MaximumDays);
                break;
        }
    }

    // ---- Memory ----

    /// <summary>
    /// The card's stability and difficulty. Cards studied before FSRS was switched on
    /// (or imported without FSRS data) get an estimate from their interval and ease.
    /// </summary>
    private static (double Stability, double Difficulty) CurrentMemory(SchedulingState state)
    {
        if (state.Stability is double stability && state.Difficulty is double difficulty)
            return (Math.Max(0.01, stability), ClampDifficulty(difficulty));
        double estimated = state.IntervalDays >= 1 ? state.IntervalDays : W[2];
        return (estimated, ClampDifficulty(11 - (state.Ease - 1) * 4));   // ease 2.5 → 5, ease 1.3 → 9.8
    }

    /// <summary>When the card was last seen. If unknown (e.g. imported), estimated from its due date.</summary>
    private static DateTime LastReview(SchedulingState state) =>
        state.LastReviewed ?? state.Due.AddDays(-IntervalHelpers.Round(state.IntervalDays));

    // ---- FSRS-5 formulas ----

    /// <summary>Chance (0–1) of remembering after <paramref name="days"/>, given stability.</summary>
    private static double Retrievability(double days, double stability) =>
        Math.Pow(1 + Factor * days / stability, Decay);

    /// <summary>Days until the chance of remembering falls to the target retention.</summary>
    private double Interval(double stability) =>
        stability / Factor * (Math.Pow(DesiredRetention, 1 / Decay) - 1);

    private static double InitialDifficulty(Rating rating) => ClampDifficulty(RawInitialDifficulty(rating));

    private static double RawInitialDifficulty(Rating rating) =>
        W[4] - Math.Exp(W[5] * ((int)rating - 1)) + 1;

    private static double NextDifficulty(double difficulty, Rating rating)
    {
        double change = -W[6] * ((int)rating - 3);
        double damped = difficulty + change * (10 - difficulty) / 9;
        // Drifts slowly back toward the "Easy" starting difficulty, so cards don't get stuck at 10.
        return ClampDifficulty(W[7] * RawInitialDifficulty(Rating.Easy) + (1 - W[7]) * damped);
    }

    private static double StabilityAfter(Rating rating, double difficulty, double stability, double recall)
    {
        if (rating == Rating.Again)
        {
            double forgotten = W[11] * Math.Pow(difficulty, -W[12]) * (Math.Pow(stability + 1, W[13]) - 1)
                * Math.Exp(W[14] * (1 - recall));
            return Math.Max(0.01, Math.Min(forgotten, stability));
        }
        double hardPenalty = rating == Rating.Hard ? W[15] : 1;
        double easyBonus = rating == Rating.Easy ? W[16] : 1;
        double growth = Math.Exp(W[8]) * (11 - difficulty) * Math.Pow(stability, -W[9])
            * (Math.Exp(W[10] * (1 - recall)) - 1) * hardPenalty * easyBonus;
        return stability * (growth + 1);
    }

    private static double ShortTermStability(double stability, Rating rating) =>
        Math.Max(0.01, stability * Math.Exp(W[17] * ((int)rating - 3 + W[18])));

    private static double ClampDifficulty(double value) => Math.Min(10, Math.Max(1, value));
}
