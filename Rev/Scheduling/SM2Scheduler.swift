import Foundation

/// An SM-2 style spaced-repetition algorithm, written from scratch for Rev.
///
/// In plain words:
/// - New cards go through short "learning steps" (1 min, then 10 min) before they graduate.
/// - Graduated cards come back after a number of days. "Good" multiplies the interval
///   by the card's ease (starts at 2.5). "Hard" grows it a little and lowers the ease.
///   "Easy" grows it more and raises the ease.
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
            answerReview(&s, rating, now: now)
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
            s.due = now.addingTimeInterval(hardDelay(steps: steps, step: s.step))
        case .good:
            s.step += 1
            if s.step >= steps.count {
                graduate(&s, days: graduateDays, now: now)
            } else {
                s.due = now.addingTimeInterval(steps[s.step])
            }
        case .easy:
            graduate(&s, days: easyDays, now: now)
        }
    }

    /// Answering a graduated card.
    private func answerReview(_ s: inout SchedulingState, _ rating: Rating, now: Date) {
        let current = max(1, s.intervalDays)
        // Work out the three "remembered" intervals so that Hard < Good < Easy always holds.
        let hardDays = max(current + 1, current * 1.2)
        let goodDays = max(hardDays + 1, current * s.ease)
        let easyDays = max(goodDays + 1, current * s.ease * 1.3)

        switch rating {
        case .again:
            s.lapses += 1
            s.ease = max(minimumEase, s.ease - 0.2)
            s.intervalDays = max(1, (current * 0.5).rounded())
            s.phase = .relearning
            s.step = 0
            s.due = now.addingTimeInterval(relearningSteps[0])
        case .hard:
            s.ease = max(minimumEase, s.ease - 0.15)
            graduate(&s, days: hardDays, now: now)
        case .good:
            graduate(&s, days: goodDays, now: now)
        case .easy:
            s.ease += 0.15
            graduate(&s, days: easyDays, now: now)
        }
    }

    /// Moves a card to the "review" phase, due in a whole number of days.
    private func graduate(_ s: inout SchedulingState, days: Double, now: Date) {
        let whole = min(maximumDays, max(1, days.rounded()))
        s.phase = .review
        s.step = 0
        s.intervalDays = whole
        s.due = Calendar.current.date(byAdding: .day, value: Int(whole), to: now.startOfDay) ?? now
    }

    /// "Hard" on a learning step: a bit longer than Again, never longer than Good.
    private func hardDelay(steps: [TimeInterval], step: Int) -> TimeInterval {
        if steps.count == 1 { return steps[0] * 1.5 }
        if step == 0 { return (steps[0] + steps[1]) / 2 }
        return steps[min(step, steps.count - 1)]
    }
}
