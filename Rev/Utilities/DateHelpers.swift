import Foundation

extension Date {
    /// Midnight at the start of this date's day.
    var startOfDay: Date { Calendar.current.startOfDay(for: self) }

    /// Midnight at the start of the next day.
    var endOfDay: Date {
        Calendar.current.date(byAdding: .day, value: 1, to: startOfDay) ?? self
    }
}
