import Foundation
import SwiftData

/// One flashcard: a front (question) and a back (answer),
/// plus the information the scheduler needs to decide when to show it next.
@Model
final class Card {
    var id: UUID = UUID()
    var front: String = ""
    var back: String = ""
    var createdAt: Date = Date()
    var deck: Deck?
    /// Cards made from the same Anki note (e.g. c1 and c2 of one cloze) share this.
    /// After you study one of them, the others wait until tomorrow ("burying").
    var noteID: String?
    /// Suspended cards are never shown in reviews until you unsuspend them.
    var isSuspended: Bool = false
    /// A "leech" is a card you keep forgetting (8 lapses). It usually needs rewriting.
    var isLeech: Bool = false

    // MARK: Scheduling information (updated every time you answer the card)

    /// "new", "learning", "review" or "relearning". Use `phase` instead of this directly.
    var phaseRaw: String = "new"
    /// When the card should be shown next.
    var due: Date = Date()
    /// Days until the next review (for cards in the "review" phase).
    var intervalDays: Double = 0
    /// How easy the card is for you. Starts at 2.5; higher means intervals grow faster.
    var ease: Double = 2.5
    /// How many times you have answered this card.
    var reps: Int = 0
    /// How many times you forgot it after it had graduated.
    var lapses: Int = 0
    /// Which short learning step the card is on.
    var step: Int = 0
    var lastReviewed: Date?

    // Used by the FSRS algorithm. Not used by SM-2.
    var stability: Double?
    var difficulty: Double?

    init(front: String, back: String) {
        self.front = front
        self.back = back
    }
}

extension Card {
    var phase: CardPhase {
        get { CardPhase(rawValue: phaseRaw) ?? .new }
        set { phaseRaw = newValue.rawValue }
    }

    /// All the scheduling fields bundled together, so the algorithm
    /// doesn't need to know about SwiftData at all.
    var schedulingState: SchedulingState {
        get {
            SchedulingState(phase: phase, due: due, intervalDays: intervalDays, ease: ease,
                            reps: reps, lapses: lapses, step: step, lastReviewed: lastReviewed,
                            stability: stability, difficulty: difficulty,
                            fuzzSeed: id.hashValue &+ reps)
        }
        set {
            phase = newValue.phase
            due = newValue.due
            intervalDays = newValue.intervalDays
            ease = newValue.ease
            reps = newValue.reps
            lapses = newValue.lapses
            step = newValue.step
            lastReviewed = newValue.lastReviewed
            stability = newValue.stability
            difficulty = newValue.difficulty
        }
    }

    /// Creates a new card, saves it into the database and puts it in a deck.
    @discardableResult
    static func create(front: String, back: String, in deck: Deck,
                       context: ModelContext, createdAt: Date = Date()) -> Card {
        let card = Card(front: front, back: back)
        card.createdAt = createdAt
        card.due = createdAt
        context.insert(card)
        card.deck = deck
        return card
    }
}
