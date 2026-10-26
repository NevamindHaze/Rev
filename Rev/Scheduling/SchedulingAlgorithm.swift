import Foundation

/// Where a card is in its life.
enum CardPhase: String, Codable {
    case new          // never studied
    case learning     // being learned for the first time (short steps, minutes)
    case review       // graduated; shown again after some days
    case relearning   // forgotten after graduating; short steps again
}

/// Everything an algorithm needs to know about a card's schedule.
struct SchedulingState: Equatable {
    var phase: CardPhase
    var due: Date
    var intervalDays: Double
    var ease: Double
    var reps: Int
    var lapses: Int
    var step: Int
    var lastReviewed: Date?
}

/// The "plug" every scheduling algorithm must fit.
/// SM-2 fits it today. To switch to FSRS later: write `FSRSScheduler`
/// that follows this protocol, then change one line in `ActiveScheduler`.
protocol SchedulingAlgorithm {
    func next(after state: SchedulingState, rating: Rating, now: Date) -> SchedulingState
}

/// The algorithm the app uses right now.
enum ActiveScheduler {
    static var current: any SchedulingAlgorithm { SM2Scheduler() }
}

extension SchedulingState {
    /// Short text for the answer buttons, e.g. "10 min", "3 d", "1.5 mo".
    func waitText(from now: Date) -> String {
        if phase == .review {
            return Self.format(days: intervalDays)
        }
        let minutes = max(1, Int((due.timeIntervalSince(now) / 60).rounded()))
        return minutes < 60 ? "\(minutes) min" : "\(minutes / 60) h"
    }

    static func format(days: Double) -> String {
        let whole = Int(days.rounded())
        if whole < 31 { return "\(whole) d" }
        if whole < 365 { return String(format: "%.1f mo", days / 30) }
        return String(format: "%.1f y", days / 365)
    }
}
