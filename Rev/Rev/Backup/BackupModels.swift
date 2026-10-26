import Foundation

// The shape of a backup file. These are plain copies of the database objects
// ("DTO" = data transfer object), so they can be written to JSON and moved between threads.
// `formatVersion` lets future versions of Rev still read old backups.

struct BackupFile: Codable, Sendable {
    var formatVersion: Int
    var exportedAt: Date
    var newCardsPerDay: Int?
    var userName: String?
    var decks: [DeckDTO]
    var reviewLogs: [ReviewLogDTO]
}

struct DeckDTO: Codable, Sendable {
    var id: UUID
    var name: String
    var createdAt: Date
    var cards: [CardDTO]
}

struct CardDTO: Codable, Sendable {
    var id: UUID
    var front: String
    var back: String
    var createdAt: Date
    var phase: String
    var due: Date
    var intervalDays: Double
    var ease: Double
    var reps: Int
    var lapses: Int
    var step: Int
    var lastReviewed: Date?
    var stability: Double?
    var difficulty: Double?
    var noteID: String?
}

struct ReviewLogDTO: Codable, Sendable {
    var cardID: UUID
    var date: Date
    var rating: Int
    var wasNew: Bool
    var intervalDays: Double
}

// MARK: - Converting between database objects and DTOs

extension DeckDTO {
    init(_ deck: Deck) {
        self.init(id: deck.id, name: deck.name, createdAt: deck.createdAt,
                  cards: deck.cards.sorted { $0.createdAt < $1.createdAt }.map { CardDTO($0) })
    }
}

extension CardDTO {
    init(_ card: Card) {
        self.init(id: card.id, front: card.front, back: card.back, createdAt: card.createdAt,
                  phase: card.phaseRaw, due: card.due, intervalDays: card.intervalDays, ease: card.ease,
                  reps: card.reps, lapses: card.lapses, step: card.step, lastReviewed: card.lastReviewed,
                  stability: card.stability, difficulty: card.difficulty, noteID: card.noteID)
    }

    /// A brand-new, never-studied card.
    static func fresh(front: String, back: String, createdAt: Date) -> CardDTO {
        CardDTO(id: UUID(), front: front, back: back, createdAt: createdAt,
                phase: CardPhase.new.rawValue, due: createdAt, intervalDays: 0, ease: 2.5,
                reps: 0, lapses: 0, step: 0, lastReviewed: nil, stability: nil, difficulty: nil)
    }
}

extension ReviewLogDTO {
    init(_ log: ReviewLog) {
        self.init(cardID: log.cardID, date: log.date, rating: log.ratingRaw,
                  wasNew: log.wasNew, intervalDays: log.intervalDays)
    }
}

extension Card {
    /// Copies every field from a DTO onto this card.
    func apply(_ dto: CardDTO) {
        id = dto.id
        front = dto.front
        back = dto.back
        createdAt = dto.createdAt
        phaseRaw = dto.phase
        due = dto.due
        intervalDays = dto.intervalDays
        ease = dto.ease
        reps = dto.reps
        lapses = dto.lapses
        step = dto.step
        lastReviewed = dto.lastReviewed
        stability = dto.stability
        difficulty = dto.difficulty
        noteID = dto.noteID
    }
}
