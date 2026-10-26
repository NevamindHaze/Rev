import Foundation

extension Date {
    /// Like Anki, a "study day" starts at 4 am, not midnight,
    /// so a late-night session after 12 still counts as the same day.
    static let studyDayStartHour = 4

    /// When the study day containing this moment began (4 am).
    var startOfDay: Date {
        let calendar = Calendar.current
        let shifted = calendar.date(byAdding: .hour, value: -Self.studyDayStartHour, to: self) ?? self
        let midnight = calendar.startOfDay(for: shifted)
        return calendar.date(byAdding: .hour, value: Self.studyDayStartHour, to: midnight) ?? midnight
    }

    /// When the next study day begins (4 am tomorrow).
    var endOfDay: Date {
        Calendar.current.date(byAdding: .day, value: 1, to: startOfDay) ?? self
    }

    /// Whole study days from this date to `other` (negative if `other` is earlier).
    func studyDays(to other: Date) -> Int {
        Calendar.current.dateComponents([.day], from: startOfDay, to: other.startOfDay).day ?? 0
    }
}
