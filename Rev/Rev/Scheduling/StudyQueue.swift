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
    /// Studied cards that are due by the end of today.
    func dueCount(now: Date = Date()) -> Int {
        let end = now.endOfDay
        return filter { $0.phase != .new && $0.due < end }.count
    }

    /// Cards that have never been studied.
    var newCount: Int { filter { $0.phase == .new }.count }
}
