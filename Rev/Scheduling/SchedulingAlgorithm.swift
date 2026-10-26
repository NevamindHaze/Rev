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
    /// FSRS memory: how many days until recall drops to 90%.
    var stability: Double?
    /// FSRS memory: how hard the card is, from 1 (easy) to 10 (hard).
    var difficulty: Double?
    /// A per-card number used to spread intervals out a little ("fuzz").
    var fuzzSeed: Int = 0
}

/// The "plug" every scheduling algorithm must fit. SM-2 and FSRS both fit it.
protocol SchedulingAlgorithm {
    func next(after state: SchedulingState, rating: Rating, now: Date) -> SchedulingState
}

/// The two algorithms you can choose in Settings.
enum SchedulerKind: String, CaseIterable, Identifiable {
    case fsrs
    case sm2

    var id: String { rawValue }

    var title: String {
        switch self {
        case .fsrs: "FSRS (recommended)"
        case .sm2: "SM-2 (classic)"
        }
    }
}

/// The algorithm the app uses right now, based on Settings.
enum ActiveScheduler {
    static var current: any SchedulingAlgorithm {
        let defaults = UserDefaults.standard
        let kind = SchedulerKind(rawValue: defaults.string(forKey: "scheduler") ?? "") ?? .fsrs
        switch kind {
        case .sm2:
            return SM2Scheduler()
        case .fsrs:
            let retention = defaults.object(forKey: "desiredRetention") as? Double ?? 0.9
            return FSRSScheduler(desiredRetention: retention)
        }
    }
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
