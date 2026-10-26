import Foundation

/// Small pieces shared by both algorithms.
enum IntervalHelpers {
    /// A number between 0 and 1 that stays the same for one card at one moment,
    /// so the times shown on the buttons match what really happens when you tap.
    static func fuzzFraction(seed: Int) -> Double {
        var x = UInt64(bitPattern: Int64(seed)) &+ 0x9E37_79B9_7F4A_7C15
        x = (x ^ (x >> 30)) &* 0xBF58_476D_1CE4_E5B9
        x = (x ^ (x >> 27)) &* 0x94D0_49BB_1331_11EB
        x ^= x >> 31
        return Double(x % 10_000) / 10_000
    }

    /// Moves an interval a few percent up or down, like Anki does,
    /// so cards learned together don't all come due on the same day.
    static func fuzz(_ days: Double, fraction: Double) -> Double {
        guard days >= 2.5 else { return days }
        var spread = 1.0
        spread += 0.15 * max(0, min(days, 7) - 2.5)
        spread += 0.10 * max(0, min(days, 20) - 7)
        spread += 0.05 * max(0, days - 20)
        return days + spread * (2 * fraction - 1)
    }

    /// Fuzzes and rounds the Hard / Good / Easy intervals, keeping Hard < Good < Easy.
    static func reviewIntervals(hard: Double, good: Double, easy: Double, fraction: Double,
                                maximum: Double) -> (hard: Double, good: Double, easy: Double) {
        func finish(_ days: Double) -> Double {
            min(maximum, max(1, fuzz(days, fraction: fraction).rounded()))
        }
        let hardDays = finish(hard)
        let goodDays = min(maximum, max(finish(good), hardDays + 1))
        let easyDays = min(maximum, max(finish(easy), goodDays + 1))
        return (hardDays, goodDays, easyDays)
    }

    /// Delay for "Hard" on a learning step: a bit longer than Again, never longer than Good.
    static func hardDelay(steps: [TimeInterval], step: Int) -> TimeInterval {
        if steps.count == 1 { return steps[0] * 1.5 }
        if step == 0 { return (steps[0] + steps[1]) / 2 }
        return steps[min(step, steps.count - 1)]
    }

    /// Puts a card in the "review" phase, due in a whole number of study days.
    static func graduate(_ s: inout SchedulingState, days: Double, now: Date, maximum: Double) {
        let whole = min(maximum, max(1, days.rounded()))
        s.phase = .review
        s.step = 0
        s.intervalDays = whole
        s.due = Calendar.current.date(byAdding: .day, value: Int(whole), to: now.startOfDay) ?? now
    }
}
