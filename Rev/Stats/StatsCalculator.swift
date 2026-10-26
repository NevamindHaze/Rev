import Foundation
import SwiftData

/// One bar in the "last 7 days" chart.
struct DayCount: Identifiable {
    let date: Date
    let count: Int
    let isToday: Bool
    var id: Date { date }
    var label: String { date.formatted(.dateTime.weekday(.abbreviated)) }
}

/// Counts things from the review history for the Stats and Home screens.
enum StatsCalculator {
    static func reviewsToday(in context: ModelContext, now: Date = Date()) -> Int {
        let start = now.startOfDay
        let descriptor = FetchDescriptor<ReviewLog>(predicate: #Predicate<ReviewLog> { $0.date >= start })
        return (try? context.fetchCount(descriptor)) ?? 0
    }

    /// New cards studied today; used for the daily new-card limit.
    static func newCardsStudiedToday(in context: ModelContext, now: Date = Date()) -> Int {
        let start = now.startOfDay
        let descriptor = FetchDescriptor<ReviewLog>(
            predicate: #Predicate<ReviewLog> { $0.date >= start && $0.wasNew == true }
        )
        return (try? context.fetchCount(descriptor)) ?? 0
    }

    /// Number of reviews per day (keyed by midnight) since a given date.
    static func reviewCountsByDay(in context: ModelContext, since: Date) -> [Date: Int] {
        let descriptor = FetchDescriptor<ReviewLog>(predicate: #Predicate<ReviewLog> { $0.date >= since })
        let logs = (try? context.fetch(descriptor)) ?? []
        var counts: [Date: Int] = [:]
        for log in logs {
            counts[log.date.startOfDay, default: 0] += 1
        }
        return counts
    }

    /// Days in a row with at least one review. If you haven't studied yet today,
    /// the streak still counts up to yesterday, so it doesn't reset in the morning.
    static func streak(in context: ModelContext, now: Date = Date()) -> Int {
        let calendar = Calendar.current
        let since = calendar.date(byAdding: .day, value: -730, to: now) ?? now
        let counts = reviewCountsByDay(in: context, since: since)

        var day = now.startOfDay
        if counts[day] == nil {
            day = calendar.date(byAdding: .day, value: -1, to: day) ?? day
        }
        var streak = 0
        while counts[day] != nil {
            streak += 1
            guard let previous = calendar.date(byAdding: .day, value: -1, to: day) else { break }
            day = previous
        }
        return streak
    }

    /// Reviews per day for the last `count` days, oldest first.
    static func lastDays(_ count: Int, in context: ModelContext, now: Date = Date()) -> [DayCount] {
        let calendar = Calendar.current
        let today = now.startOfDay
        let start = calendar.date(byAdding: .day, value: -(count - 1), to: today) ?? today
        let counts = reviewCountsByDay(in: context, since: start)
        return (0..<count).compactMap { offset in
            guard let day = calendar.date(byAdding: .day, value: offset, to: start) else { return nil }
            return DayCount(date: day, count: counts[day] ?? 0, isToday: day == today)
        }
    }
}
