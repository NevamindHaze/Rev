import Foundation
import SwiftData

/// Makes JSON backups, restores them, and adds imported decks to the database.
enum BackupService {
    struct ImportResult {
        var decks = 0
        var cards = 0
        var updated = 0
        var skipped = 0
    }

    // MARK: Backup

    static func makeBackup(context: ModelContext, newCardsPerDay: Int, userName: String) throws -> BackupFile {
        let decks = try context.fetch(FetchDescriptor<Deck>(sortBy: [SortDescriptor(\Deck.createdAt)]))
        let logs = try context.fetch(FetchDescriptor<ReviewLog>(sortBy: [SortDescriptor(\ReviewLog.date)]))
        return BackupFile(formatVersion: 1, exportedAt: Date(), newCardsPerDay: newCardsPerDay,
                          userName: userName, decks: decks.map { DeckDTO($0) },
                          reviewLogs: logs.map { ReviewLogDTO($0) })
    }

    static func encode(_ backup: BackupFile) throws -> Data {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .secondsSince1970   // keeps exact times, so card order survives
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try encoder.encode(backup)
    }

    static func decode(_ data: Data) throws -> BackupFile {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .secondsSince1970
        return try decoder.decode(BackupFile.self, from: data)
    }

    // MARK: Restore

    /// Deletes everything in the app, then loads the backup.
    static func restore(_ backup: BackupFile, into context: ModelContext) throws {
        for deck in try context.fetch(FetchDescriptor<Deck>()) {
            deck.deleteWithCards(in: context)
        }
        for card in try context.fetch(FetchDescriptor<Card>()) {
            context.delete(card)   // any cards that somehow lost their deck
        }
        for log in try context.fetch(FetchDescriptor<ReviewLog>()) {
            context.delete(log)
        }
        try context.save()

        try insert(backup.decks, into: context, mergeByName: false)
        for dto in backup.reviewLogs {
            let log = ReviewLog(cardID: dto.cardID, date: dto.date, rating: Rating(rawValue: dto.rating) ?? .good,
                                wasNew: dto.wasNew, intervalDays: dto.intervalDays)
            context.insert(log)
        }
        try context.save()
    }

    // MARK: Insert decks

    /// Adds decks and cards to the database.
    /// With `mergeByName`, cards go into an existing deck with the same name.
    /// A card whose question is already in that deck isn't added again: if its answer
    /// changed, the answer is updated (your study progress in Rev is kept); otherwise it's skipped.
    @discardableResult
    static func insert(_ decks: [DeckDTO], into context: ModelContext, mergeByName: Bool) throws -> ImportResult {
        var result = ImportResult()
        var decksByName: [String: Deck] = [:]
        if mergeByName {
            for deck in try context.fetch(FetchDescriptor<Deck>()) {
                decksByName[deck.name] = deck
            }
        }

        for dto in decks {
            let deck: Deck
            if mergeByName, let existing = decksByName[dto.name] {
                deck = existing
            } else {
                deck = Deck(name: dto.name)
                context.insert(deck)
                deck.id = dto.id
                deck.createdAt = dto.createdAt
                decksByName[dto.name] = deck
                result.decks += 1
            }

            // Cards already in this deck, looked up by their question.
            var existingByFront: [String: [Card]] = [:]
            for card in deck.cards {
                existingByFront[card.front, default: []].append(card)
            }

            for cardDTO in dto.cards {
                if mergeByName, var matches = existingByFront[cardDTO.front], !matches.isEmpty {
                    let existing = matches.removeFirst()
                    existingByFront[cardDTO.front] = matches
                    if existing.back == cardDTO.back {
                        result.skipped += 1
                    } else {
                        existing.back = cardDTO.back
                        result.updated += 1
                    }
                    continue
                }
                let card = Card(front: cardDTO.front, back: cardDTO.back)
                context.insert(card)
                card.apply(cardDTO)
                card.deck = deck
                result.cards += 1
            }
        }
        try context.save()
        return result
    }
}
