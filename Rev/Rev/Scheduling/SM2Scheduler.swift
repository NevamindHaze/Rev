import Foundation

/// An SM-2 style spaced-repetition algorithm, written from scratch for Rev.
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
struct SM2Scheduler: SchedulingAlgorithm {
    var learningSteps: [TimeInterval] = [1 * 60, 10 * 60]
    var relearningSteps: [TimeInterval] = [10 * 60]
    var graduatingDays: Double = 1
    var easyGraduatingDays: Double = 4
    var startingEase: Double = 2.5
    var minimumEase: Double = 1.3
    var maximumDays: Double = 36_500

    func next(after old: SchedulingState, rating: Rating, now: Date) -> SchedulingState {
        var s = old
        s.reps += 1
        s.lastReviewed = now

        switch old.phase {
        case .new, .learning:
            if old.phase == .new { s.ease = startingEase }
            s.phase = .learning
            answerStep(&s, rating, steps: learningSteps, now: now,
                       graduateDays: graduatingDays, easyDays: easyGraduatingDays)
        case .relearning:
            answerStep(&s, rating, steps: relearningSteps, now: now,
                       graduateDays: s.intervalDays, easyDays: s.intervalDays + 1)
        case .review:
            answerReview(&s, old: old, rating, now: now)
        }
        return s
    }

    /// Answering a card that is on short steps (learning or relearning).
    private func answerStep(_ s: inout SchedulingState, _ rating: Rating, steps: [TimeInterval],
                            now: Date, graduateDays: Double, easyDays: Double) {
        switch rating {
        case .again:
            s.step = 0
            s.due = now.addingTimeInterval(steps[0])
        case .hard:
            s.due = now.addingTimeInterval(IntervalHelpers.hardDelay(steps: steps, step: s.step))
        case .good:
            s.step += 1
            if s.step >= steps.count {
                IntervalHelpers.graduate(&s, days: graduateDays, now: now, maximum: maximumDays)
            } else {
                s.due = now.addingTimeInterval(steps[s.step])
            }
        case .easy:
            IntervalHelpers.graduate(&s, days: easyDays, now: now, maximum: maximumDays)
        }
    }

    /// Answering a graduated card.
    private func answerReview(_ s: inout SchedulingState, old: SchedulingState, _ rating: Rating, now: Date) {
        let current = max(1, old.intervalDays)
        // Days past the due date. Remembering a card after a delay earns a longer interval.
        let daysLate = Double(max(0, old.due.studyDays(to: now)))
        let options = IntervalHelpers.reviewIntervals(
            hard: max(current + 1, current * 1.2),
            good: (current + daysLate / 2) * old.ease,
            easy: (current + daysLate) * old.ease * 1.3,
            fraction: IntervalHelpers.fuzzFraction(seed: old.fuzzSeed),
            maximum: maximumDays)

        switch rating {
        case .again:
            s.lapses += 1
            s.ease = max(minimumEase, old.ease - 0.2)
            s.intervalDays = max(1, (current * 0.5).rounded())
            s.phase = .relearning
            s.step = 0
            s.due = now.addingTimeInterval(relearningSteps[0])
        case .hard:
            s.ease = max(minimumEase, old.ease - 0.15)
            IntervalHelpers.graduate(&s, days: options.hard, now: now, maximum: maximumDays)
        case .good:
            IntervalHelpers.graduate(&s, days: options.good, now: now, maximum: maximumDays)
        case .easy:
            s.ease = old.ease + 0.15
            IntervalHelpers.graduate(&s, days: options.easy, now: now, maximum: maximumDays)
        }
    }
}
