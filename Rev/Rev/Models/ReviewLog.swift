import Foundation
import SwiftData

/// One row for every answer you give. Used for stats, the streak,
/// and the daily new-card limit. Later, FSRS can learn from this history.
@Model
final class ReviewLog {
    var id: UUID = UUID()
    var cardID: UUID = UUID()
    var date: Date = Date()
    /// 1 = Again, 2 = Hard, 3 = Good, 4 = Easy
    var ratingRaw: Int = 3
    /// True if this was the first time the card was ever studied.
    var wasNew: Bool = false
    /// The interval the card got after this answer.
    var intervalDays: Double = 0

    init(cardID: UUID, date: Date, rating: Rating, wasNew: Bool, intervalDays: Double) {
        self.cardID = cardID
        self.date = date
        self.ratingRaw = rating.rawValue
        self.wasNew = wasNew
        self.intervalDays = intervalDays
    }
}
