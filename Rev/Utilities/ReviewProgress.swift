import Foundation

/// How to draw the Review progress bar in Signal.
enum ReviewProgress {
    /// Rounds down, so the bar is only full when the queue is empty.
    static func segments(reviewed: Int, left: Int, maxSegments: Int = 30) -> (filled: Int, total: Int) {
        let cards = reviewed + left
        let total = max(1, min(cards, maxSegments))
        return (cards == 0 ? 0 : reviewed * total / cards, total)
    }
}
