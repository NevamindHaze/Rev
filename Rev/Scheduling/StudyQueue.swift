import Foundation

/// Decides which card to show next.
///
/// Order: learning cards that are due now → review cards due today →
/// new cards (up to today's limit) → learning cards due in the next 20 minutes.
struct StudyQueue {
    var learningDue: [Card] = []
    var reviewDue: [Card] = []
    var newCards: [Card] = []
    var learningSoon: [Card] = []
    /// Learning cards that come back later today (more than 20 minutes from now).
    var laterToday = 0

    /// How far ahead we may show learning cards when nothing else is left.
    static let learnAhead: TimeInterval = 20 * 60

    init() {}

    /// `buriedNoteIDs`: notes you already studied a card from today. Their other new and
    /// review cards wait until tomorrow, so one cloze doesn't give away its sibling's answer.
    init(cards: [Card], newCardsAllowed: Int, now: Date, buriedNoteIDs: Set<String> = []) {
        let endOfToday = now.endOfDay
        var allNew: [Card] = []

        for card in cards {
            if card.isSuspended { continue }
            if card.phase == .new || card.phase == .review,
               let noteID = card.noteID, buriedNoteIDs.contains(noteID) {
                continue
            }
            switch card.phase {
            case .new:
                allNew.append(card)
            case .review:
                if card.due < endOfToday { reviewDue.append(card) }
            case .learning, .relearning:
                if card.due <= now {
                    learningDue.append(card)
                } else if card.due <= now.addingTimeInterval(Self.learnAhead) {
                    learningSoon.append(card)
                } else if card.due < endOfToday {
                    laterToday += 1
                }
            }
        }

        learningDue.sort { $0.due < $1.due }
        reviewDue.sort { $0.due < $1.due }
        learningSoon.sort { $0.due < $1.due }
        allNew.sort { $0.createdAt < $1.createdAt }
        newCards = Array(allNew.prefix(max(0, newCardsAllowed)))
    }

    var nextCard: Card? {
        learningDue.first ?? reviewDue.first ?? newCards.first ?? learningSoon.first
    }

    var learningCount: Int { learningDue.count + learningSoon.count }
}

extension Array where Element == Card {
    /// Cards waiting for you right now: review cards due today, plus learning cards whose
    /// wait is over. Answering a card takes it off this count straight away.
    func toReviewCount(now: Date = Date()) -> Int {
        let end = now.endOfDay
        return filter { card in
            guard !card.isSuspended else { return false }
            switch card.phase {
            case .new: return false
            case .review: return card.due < end
            case .learning, .relearning: return card.due <= now
            }
        }.count
    }

    /// Cards you pressed "Again" (or are still learning) that come back later today.
    func comingBack(now: Date = Date()) -> [Card] {
        let end = now.endOfDay
        return filter { !$0.isSuspended && ($0.phase == .learning || $0.phase == .relearning)
            && $0.due > now && $0.due < end }
    }

    /// E.g. "3 coming back · next in 8 min", or nil when none are waiting.
    func comingBackText(now: Date = Date()) -> String? {
        let waiting = comingBack(now: now)
        guard let next = waiting.map(\.due).min() else { return nil }
        let minutes = Swift.max(1, Int((next.timeIntervalSince(now) / 60).rounded(.up)))
        let wait = minutes < 60 ? "\(minutes) min" : "\(minutes / 60) h \(minutes % 60) min"
        return "\(waiting.count) coming back · next in \(wait)"
    }

    /// Cards that have never been studied (not counting suspended ones).
    var newCount: Int { filter { !$0.isSuspended && $0.phase == .new }.count }
}
