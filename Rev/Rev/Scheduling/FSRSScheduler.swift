import Foundation

/// FSRS (Free Spaced Repetition Scheduler), version 5, with its published default parameters.
///
/// In plain words, FSRS keeps two numbers for every card:
/// - Stability (S): how many days until your chance of remembering drops to 90%.
/// - Difficulty (D): how hard the card is for you, from 1 to 10.
/// After each answer it updates S and D, then schedules the card for the day your
/// chance of remembering falls to your target (90% by default, changeable in Settings).
/// New and forgotten cards still use short steps in minutes first, like SM-2.
struct FSRSScheduler: SchedulingAlgorithm {
    var desiredRetention: Double = 0.9
    var learningSteps: [TimeInterval] = [1 * 60, 10 * 60]
    var relearningSteps: [TimeInterval] = [10 * 60]
    var maximumDays: Double = 36_500

    /// FSRS-5 default weights.
    private let w: [Double] = [
        0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575, 0.1192,
        1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621,
    ]
    private let decay = -0.5
    private let factor = 19.0 / 81.0

    func next(after old: SchedulingState, rating: Rating, now: Date) -> SchedulingState {
        var s = old
        s.reps += 1
        s.lastReviewed = now
        let fraction = IntervalHelpers.fuzzFraction(seed: old.fuzzSeed)

        // New card: memory starts from the first answer.
        guard old.phase != .new else {
            s.stability = w[rating.rawValue - 1]
            s.difficulty = initialDifficulty(rating)
            s.phase = .learning
            answerStep(&s, rating, steps: learningSteps, now: now, fraction: fraction)
            return s
        }

        let memory = currentMemory(of: old)
        let elapsedDays = Double(max(0, lastReview(of: old).studyDays(to: now)))
        let recall = retrievability(days: elapsedDays, stability: memory.stability)
        s.difficulty = nextDifficulty(memory.difficulty, rating)

        switch old.phase {
        case .new, .learning, .relearning:
            // Short steps (minutes). Seen again the same day: small stability change.
            s.stability = elapsedDays < 1
                ? shortTermStability(memory.stability, rating)
                : stabilityAfter(rating, difficulty: memory.difficulty, stability: memory.stability, recall: recall)
            let steps = old.phase == .relearning ? relearningSteps : learningSteps
            answerStep(&s, rating, steps: steps, now: now, fraction: fraction)

        case .review:
            if rating == .again {
                let newStability = stabilityAfter(.again, difficulty: memory.difficulty,
                                                  stability: memory.stability, recall: recall)
                s.stability = newStability
                s.lapses += 1
                s.intervalDays = max(1, interval(for: newStability).rounded())
                s.phase = .relearning
                s.step = 0
                s.due = now.addingTimeInterval(relearningSteps[0])
                return s
            }
            // Work out all three so Hard < Good < Easy always holds, then pick yours.
            let stabilities = [Rating.hard, .good, .easy].map {
                stabilityAfter($0, difficulty: memory.difficulty, stability: memory.stability, recall: recall)
            }
            let options = IntervalHelpers.reviewIntervals(
                hard: interval(for: stabilities[0]), good: interval(for: stabilities[1]),
                easy: interval(for: stabilities[2]), fraction: fraction, maximum: maximumDays)
            switch rating {
            case .hard:
                s.stability = stabilities[0]
                IntervalHelpers.graduate(&s, days: options.hard, now: now, maximum: maximumDays)
            case .good, .again:
                s.stability = stabilities[1]
                IntervalHelpers.graduate(&s, days: options.good, now: now, maximum: maximumDays)
            case .easy:
                s.stability = stabilities[2]
                IntervalHelpers.graduate(&s, days: options.easy, now: now, maximum: maximumDays)
            }
        }
        return s
    }

    // MARK: - Steps (minutes)

    private func answerStep(_ s: inout SchedulingState, _ rating: Rating, steps: [TimeInterval],
                            now: Date, fraction: Double) {
        let graduateDays = IntervalHelpers.fuzz(interval(for: s.stability ?? w[2]), fraction: fraction)
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
            IntervalHelpers.graduate(&s, days: graduateDays, now: now, maximum: maximumDays)
        }
    }

    // MARK: - Memory

    /// The card's stability and difficulty. Cards studied before FSRS was switched on
    /// (or imported without FSRS data) get an estimate from their interval and ease.
    private func currentMemory(of state: SchedulingState) -> (stability: Double, difficulty: Double) {
        if let stability = state.stability, let difficulty = state.difficulty {
            return (max(0.01, stability), clampDifficulty(difficulty))
        }
        let stability = state.intervalDays >= 1 ? state.intervalDays : w[2]
        let difficulty = clampDifficulty(11 - (state.ease - 1) * 4)   // ease 2.5 → 5, ease 1.3 → 9.8
        return (stability, difficulty)
    }

    /// When the card was last seen. If unknown (e.g. imported), estimated from its due date.
    private func lastReview(of state: SchedulingState) -> Date {
        if let last = state.lastReviewed { return last }
        return Calendar.current.date(byAdding: .day, value: -Int(state.intervalDays.rounded()), to: state.due)
            ?? state.due
    }

    // MARK: - FSRS-5 formulas

    /// Chance (0–1) of remembering after `days`, given stability.
    private func retrievability(days: Double, stability: Double) -> Double {
        pow(1 + factor * days / stability, decay)
    }

    /// Days until the chance of remembering falls to the target retention.
    private func interval(for stability: Double) -> Double {
        stability / factor * (pow(desiredRetention, 1 / decay) - 1)
    }

    private func initialDifficulty(_ rating: Rating) -> Double {
        clampDifficulty(rawInitialDifficulty(rating))
    }

    private func rawInitialDifficulty(_ rating: Rating) -> Double {
        w[4] - exp(w[5] * Double(rating.rawValue - 1)) + 1
    }

    private func nextDifficulty(_ difficulty: Double, _ rating: Rating) -> Double {
        let change = -w[6] * Double(rating.rawValue - 3)
        let damped = difficulty + change * (10 - difficulty) / 9
        // Drifts slowly back toward the "Easy" starting difficulty, so cards don't get stuck at 10.
        return clampDifficulty(w[7] * rawInitialDifficulty(.easy) + (1 - w[7]) * damped)
    }

    private func stabilityAfter(_ rating: Rating, difficulty: Double, stability: Double, recall: Double) -> Double {
        if rating == .again {
            let forgotten = w[11] * pow(difficulty, -w[12]) * (pow(stability + 1, w[13]) - 1)
                * exp(w[14] * (1 - recall))
            return max(0.01, min(forgotten, stability))
        }
        let hardPenalty = rating == .hard ? w[15] : 1
        let easyBonus = rating == .easy ? w[16] : 1
        let growth = exp(w[8]) * (11 - difficulty) * pow(stability, -w[9])
            * (exp(w[10] * (1 - recall)) - 1) * hardPenalty * easyBonus
        return stability * (growth + 1)
    }

    private func shortTermStability(_ stability: Double, _ rating: Rating) -> Double {
        max(0.01, stability * exp(w[17] * (Double(rating.rawValue) - 3 + w[18])))
    }

    private func clampDifficulty(_ value: Double) -> Double {
        min(10, max(1, value))
    }
}
