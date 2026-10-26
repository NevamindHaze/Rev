import Foundation
import SwiftData
import CryptoKit

/// The sync rules on Rev's side (the Anki add-on follows the same rules):
/// - A card is identified by "<note id>:<ord>" in both apps.
/// - For each card, text/tags/deck and schedule are merged separately: the newest change wins.
/// - Deletions travel as "tombstones"; a deletion loses if the card was changed after it.
/// - Reviews from both apps are kept (duplicates skipped), so stats and streaks include everything.
@MainActor
enum SyncMerger {
    struct Report {
        var added = 0
        var updated = 0
        var rescheduled = 0
        var deleted = 0
        var reviews = 0
        var decksRemoved = 0

        var summary: String {
            var parts: [String] = []
            if added > 0 { parts.append("\(added.counted("new card"))") }
            if updated > 0 { parts.append("\(updated.counted("card")) updated") }
            if rescheduled > 0 { parts.append("\(rescheduled.counted("schedule")) from Anki") }
            if deleted > 0 { parts.append("\(deleted.counted("card")) deleted") }
            if reviews > 0 { parts.append("\(reviews.counted("review")) from Anki") }
            if decksRemoved > 0 { parts.append("\(decksRemoved.counted("empty deck")) removed") }
            return parts.isEmpty ? "Everything is up to date." : parts.joined(separator: ", ") + "."
        }
    }

    private static let reviewDays: Double = 60
    private static let tombstoneDays: Double = 60

    // MARK: - Anki → Rev

    static func apply(_ snapshot: SyncSnapshot, context: ModelContext, state: inout SyncState) throws -> Report {
        var report = Report()
        let refreshText = (state.contentFormat ?? 1) < 2
        defer { state.contentFormat = 2 }
        let cards = try context.fetch(FetchDescriptor<Card>())
        link(cards, remote: snapshot.cards)

        var byKey: [String: Card] = [:]
        for card in cards {
            if let key = card.syncKey { byKey[key] = card }
        }
        var decksByName: [String: Deck] = [:]
        for deck in try context.fetch(FetchDescriptor<Deck>()) { decksByName[deck.name] = deck }
        func deck(named ankiName: String) -> Deck {
            let name = ankiName.replacingOccurrences(of: "::", with: " › ")
            if let existing = decksByName[name] { return existing }
            let created = Deck(name: name)
            context.insert(created)
            decksByName[name] = created
            return created
        }

        // Decks that lose cards in this sync (deleted, or moved to another deck). One left empty
        // afterwards was deleted or renamed in another app, so it goes too (see step 2b).
        var emptied: [PersistentIdentifier: Deck] = [:]
        var gone = Set<PersistentIdentifier>()

        // 1. Cards deleted in Anki (unless changed in Rev afterwards).
        for tombstone in snapshot.deleted {
            guard let card = byKey[tombstone.key], lastChange(card) < tombstone.at else { continue }
            if let old = card.deck { emptied[old.persistentModelID] = old }
            gone.insert(card.persistentModelID)
            context.delete(card)
            byKey[tombstone.key] = nil
            report.deleted += 1
        }

        // 2. Changed and new cards.
        let exported = Set(state.exportedKeys)
        for remote in snapshot.cards {
            if let card = byKey[remote.key] {
                // (A one-time refresh also re-reads cards whose text came from Anki unchanged,
                //  so they pick up bold/italic; cards you edited in Rev are newer and stay as they are.)
                let local = card.editedAt?.epoch ?? 0
                if remote.editedAt > local + 1 || (refreshText && abs(remote.editedAt - local) <= 1) {
                    if let old = card.deck { emptied[old.persistentModelID] = old }
                    applyContent(remote, to: card, deck: deck(named: remote.deck))
                    card.editedAt = Date(timeIntervalSince1970: remote.editedAt)
                    report.updated += 1
                }
                if remote.schedAt > schedTime(card) + 1 {
                    applySchedule(remote, to: card)
                    card.schedChangedAt = Date(timeIntervalSince1970: remote.schedAt)
                    report.rescheduled += 1
                }
            } else if !exported.contains(remote.key) {
                // (If Rev exported this key before, it was deleted in Rev; Anki will delete it too.)
                let card = Card(front: "", back: "")
                context.insert(card)
                card.noteID = remote.noteId
                card.noteOrd = remote.ord
                applyContent(remote, to: card, deck: deck(named: remote.deck))
                applySchedule(remote, to: card)
                card.editedAt = Date(timeIntervalSince1970: remote.editedAt)
                card.schedChangedAt = Date(timeIntervalSince1970: remote.schedAt)
                byKey[remote.key] = card
                report.added += 1
            }
        }

        // 2b. Remove the decks this sync left without cards. Decks you emptied or made empty yourself
        //     are never in `emptied`, so they stay. A deck that still has sub-decks also stays.
        let inUse = Set((cards + Array(byKey.values))
            .filter { !gone.contains($0.persistentModelID) }
            .compactMap { $0.deck?.persistentModelID })
        var names = Set(decksByName.keys)
        // Longest names first, so an emptied sub-deck goes before its parent is checked.
        for old in emptied.values.sorted(by: { $0.name.count > $1.name.count }) {
            let hasSubDecks = names.contains { $0.hasPrefix(old.name + " › ") }
            if hasSubDecks || inUse.contains(old.persistentModelID) { continue }
            names.remove(old.name)
            context.delete(old)
            report.decksRemoved += 1
        }

        // 3. Reviews done in Anki.
        let cutoff = Date().addingTimeInterval(-reviewDays * 86_400)
        let recent = try context.fetch(FetchDescriptor<ReviewLog>(predicate: #Predicate<ReviewLog> { $0.date >= cutoff }))
        var seen = Set(recent.map { "\($0.cardID)|\(milliseconds($0.date))" })
        for review in snapshot.reviews {
            guard let card = byKey[review.key] else { continue }
            let marker = "\(card.id)|\(review.at)"
            if seen.contains(marker) { continue }
            seen.insert(marker)
            context.insert(ReviewLog(cardID: card.id, date: Date(timeIntervalSince1970: Double(review.at) / 1000),
                                     rating: Rating(rawValue: review.rating) ?? .good, wasNew: false,
                                     intervalDays: review.interval))
            report.reviews += 1
        }
        return report
    }

    private static func applyContent(_ remote: SyncCard, to card: Card, deck: Deck) {
        card.front = AnkiText.plainText(fromHTML: remote.front)
        card.back = AnkiText.plainText(fromHTML: remote.back)
        card.tags = remote.tags
        card.isLeech = remote.leech
        switch remote.kind {
        case "cloze":
            card.kind = .cloze
            card.clozeSource = AnkiText.plainText(fromHTML: remote.clozeSource ?? "")
            card.clozeExtra = AnkiText.plainText(fromHTML: remote.clozeExtra ?? "")
            card.clozeNumber = remote.ord + 1
            card.fieldsReadOnly = false
        case "basic", "reversed", "typeIn":
            card.kind = CardKind(rawValue: remote.kind) ?? .basic
            card.fieldsReadOnly = false
        default:
            card.kind = .basic
            card.fieldsReadOnly = true
        }
        if card.deck?.persistentModelID != deck.persistentModelID { card.deck = deck }
    }

    private static func applySchedule(_ remote: SyncCard, to card: Card) {
        let s = remote.sched
        card.phase = CardPhase(rawValue: s.phase) ?? .new
        if card.phase != .new { card.due = Date(timeIntervalSince1970: s.due) }
        card.intervalDays = s.interval
        card.ease = s.ease
        card.reps = s.reps
        card.lapses = s.lapses
        card.step = 0
        if let stability = s.stability, let difficulty = s.difficulty {
            card.stability = stability
            card.difficulty = difficulty
        }
        card.isSuspended = s.suspended
        if let last = remote.lastReview { card.lastReviewed = Date(timeIntervalSince1970: last) }
    }

    // MARK: - Rev → snapshot

    static func makeSnapshot(context: ModelContext, state: inout SyncState) throws -> SyncSnapshot {
        let now = Date().epoch
        let cards = try context.fetch(FetchDescriptor<Card>())
        link(cards, remote: [])

        var out: [SyncCard] = []
        var keyOfCard: [UUID: String] = [:]
        for card in cards {
            guard let key = card.syncKey, let noteID = card.noteID, let ord = card.noteOrd,
                  let deck = card.deck else { continue }
            keyOfCard[card.id] = key
            out.append(SyncCard(
                key: key, noteId: noteID, ord: ord,
                deck: deck.name.replacingOccurrences(of: " › ", with: "::"),
                kind: card.fieldsReadOnly ? "other" : card.kind.rawValue,
                front: card.front, back: card.back, fields: fields(for: card),
                clozeSource: nil, clozeExtra: nil,
                tags: card.tags, leech: card.isLeech,
                editedAt: card.editedAt?.epoch ?? 0,
                sched: SyncSchedule(phase: card.phaseRaw, due: card.due.epoch, interval: card.intervalDays,
                                    ease: card.ease, reps: card.reps, lapses: card.lapses,
                                    stability: card.stability, difficulty: card.difficulty,
                                    suspended: card.isSuspended),
                schedAt: schedTime(card),
                lastReview: card.lastReviewed?.epoch))
        }
        out.sort { $0.key < $1.key }

        // Cards that were sent before but are gone now were deleted in Rev.
        let current = Set(keyOfCard.values)
        for key in state.exportedKeys where !current.contains(key) && state.tombstones[key] == nil {
            state.tombstones[key] = now
        }
        state.tombstones = state.tombstones.filter { !current.contains($0.key) && $0.value > now - tombstoneDays * 86_400 }
        state.exportedKeys = current.sorted()

        let cutoff = Date().addingTimeInterval(-reviewDays * 86_400)
        let logs = try context.fetch(FetchDescriptor<ReviewLog>(predicate: #Predicate<ReviewLog> { $0.date >= cutoff },
                                                               sortBy: [SortDescriptor(\ReviewLog.date)]))
        let reviews = logs.compactMap { log -> SyncReview? in
            guard let key = keyOfCard[log.cardID] else { return nil }
            return SyncReview(key: key, at: milliseconds(log.date), rating: log.ratingRaw, interval: log.intervalDays)
        }

        return SyncSnapshot(format: 1, writer: "rev", writtenAt: 0, cards: out,
                            deleted: state.tombstones.map { SyncTombstone(key: $0.key, at: $0.value) }.sorted { $0.key < $1.key },
                            reviews: reviews)
    }

    /// A fingerprint of the snapshot's content, to skip writing the file when nothing changed.
    static func digest(_ snapshot: SyncSnapshot) -> String {
        var copy = snapshot
        copy.writtenAt = 0
        let encoder = JSONEncoder()
        encoder.outputFormatting = .sortedKeys
        let data = (try? encoder.encode(copy)) ?? Data()
        return SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    /// The Anki fields for a card Rev can edit.
    private static func fields(for card: Card) -> [String: String]? {
        guard !card.fieldsReadOnly else { return nil }
        let html = AnkiText.html(fromPlain:)
        switch card.kind {
        case .cloze:
            return ["Text": html(card.clozeSource ?? card.front), "Back Extra": html(card.clozeExtra ?? "")]
        case .reversed where card.noteOrd == 1:
            return ["Front": html(card.back), "Back": html(card.front)]
        default:
            return ["Front": html(card.front), "Back": html(card.back)]
        }
    }

    // MARK: - Linking cards to their Anki identity

    /// Gives every card a note ord, so it has a sync key.
    /// Cards made in Rev get one straight away; cards imported from Anki are matched with
    /// Anki's own list of cards (by note and question), and wait until Anki's file is available.
    static func link(_ cards: [Card], remote: [SyncCard]) {
        let remoteByNote = Dictionary(grouping: remote, by: \.noteId)
        var used = Set(cards.compactMap(\.syncKey))
        let siblings = Dictionary(grouping: cards.filter { $0.noteID != nil }, by: { $0.noteID! })

        for card in cards where card.noteOrd == nil {
            guard let noteID = card.noteID else {
                card.noteID = UUID().uuidString   // an older card made in Rev
                card.noteOrd = 0
                continue
            }
            if UUID(uuidString: noteID) != nil {
                // Made in Rev: ord comes from the cloze number or the card's place in its note.
                if card.kind == .cloze, let number = card.clozeNumber {
                    card.noteOrd = number - 1
                } else {
                    let ordered = (siblings[noteID] ?? [card]).sorted { $0.createdAt < $1.createdAt }
                    card.noteOrd = ordered.firstIndex { $0.persistentModelID == card.persistentModelID } ?? 0
                }
                continue
            }
            // Imported from Anki: find the matching card in Anki's list.
            let candidates = (remoteByNote[noteID] ?? []).filter { !used.contains($0.key) }
            let question = BackupService.matchKey(card.front)
            let match = candidates.count == 1 ? candidates.first : candidates.first {
                BackupService.matchKey(AnkiText.plainText(fromHTML: $0.front)) == question
            }
            if let match {
                card.noteOrd = match.ord
                used.insert(match.key)
            }
        }
    }

    // MARK: - Helpers

    /// When the schedule last changed. Cards never reviewed or changed in Rev count as "long ago",
    /// so Anki's schedule wins for them.
    private static func schedTime(_ card: Card) -> Double {
        (card.schedChangedAt ?? card.lastReviewed)?.epoch ?? 0
    }

    private static func lastChange(_ card: Card) -> Double {
        max(card.editedAt?.epoch ?? 0, schedTime(card))
    }

    private static func milliseconds(_ date: Date) -> Int64 {
        Int64((date.timeIntervalSince1970 * 1000).rounded())
    }
}
