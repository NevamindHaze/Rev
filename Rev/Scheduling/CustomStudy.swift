import Foundation
import SwiftData

/// An extra study session outside the normal daily queue, like Anki's "Custom study".
/// Answers still count and reschedule cards as usual.
struct CustomStudy: Hashable, Identifiable {
    enum Kind: String, CaseIterable, Identifiable {
        case moreNew, forgotten, reviewAhead, byTag
        var id: String { rawValue }

        var title: String {
            switch self {
            case .moreNew: return "More new cards"
            case .forgotten: return "Forgotten cards"
            case .reviewAhead: return "Review ahead"
            case .byTag: return "Study by tag"
            }
        }

        /// One line under the title, so it's clear what each choice picks.
        var subtitle: String {
            switch self {
            case .moreNew: return "New cards beyond today's limit"
            case .forgotten: return "Cards you pressed Again on lately"
            case .reviewAhead: return "Cards that come due soon"
            case .byTag: return "Any cards, or only ones with a tag"
            }
        }

        var icon: String {
            switch self {
            case .moreNew: return "sparkles"
            case .forgotten: return "arrow.counterclockwise"
            case .reviewAhead: return "forward.fill"
            case .byTag: return "tag.fill"
            }
        }
    }

    /// Which cards "Study by tag" takes.
    enum TagCards: String, CaseIterable, Identifiable {
        case all = "All cards"
        case new = "New only"
        case studied = "Studied only"
        var id: String { rawValue }
    }

    var kind: Kind = .moreNew
    /// How many cards at most (more new cards, study by tag).
    var limit = 20
    /// How many days to look back (forgotten) or ahead (review ahead).
    var days = 1
    var tags: Set<String> = []
    var tagCards: TagCards = .all
    var shuffled = true

    /// Picked once when the session starts, so the list doesn't change under you.
    let id = UUID()

    var title: String { kind.title }

    /// The cards this session will show, in order.
    func pickCards(from cards: [Card], context: ModelContext, now: Date = Date()) -> [Card] {
        let available = cards.filter { !$0.isSuspended }
        switch kind {
        case .moreNew:
            // Siblings of notes studied today still wait, so a cloze doesn't give away its partner.
            let studiedToday = StatsCalculator.cardIDsReviewedToday(in: context, now: now)
            let buriedNotes = Set(cards.filter { studiedToday.contains($0.id) }.compactMap(\.noteID))
            let new = available
                .filter { card in
                    guard card.phase == .new else { return false }
                    guard let noteID = card.noteID else { return true }
                    return !buriedNotes.contains(noteID)
                }
                .sorted { $0.createdAt < $1.createdAt }
            return Array(new.prefix(max(0, limit)))

        case .forgotten:
            let since = Calendar.current.date(byAdding: .day, value: -max(1, days) + 1, to: now.startOfDay) ?? now
            let again = Rating.again.rawValue
            let descriptor = FetchDescriptor<ReviewLog>(
                predicate: #Predicate<ReviewLog> { $0.date >= since && $0.ratingRaw == again }
            )
            let forgottenIDs = Set(((try? context.fetch(descriptor)) ?? []).map(\.cardID))
            return available.filter { forgottenIDs.contains($0.id) && $0.phase != .new }.shuffled()

        case .reviewAhead:
            let end = Calendar.current.date(byAdding: .day, value: max(1, days), to: now.endOfDay) ?? now
            return available
                .filter { $0.phase == .review && $0.due >= now.endOfDay && $0.due < end }
                .sorted { $0.due < $1.due }

        case .byTag:
            let wanted = Set(tags.map { $0.lowercased() })
            var matching = available.filter { card in
                guard wanted.isEmpty || card.tags.contains(where: { wanted.contains($0.lowercased()) }) else {
                    return false
                }
                switch tagCards {
                case .all: return true
                case .new: return card.phase == .new
                case .studied: return card.phase != .new
                }
            }
            if shuffled {
                matching.shuffle()
            } else {
                matching.sort { $0.createdAt < $1.createdAt }
            }
            return Array(matching.prefix(max(0, limit)))
        }
    }

    /// Why nothing was picked, in words, e.g. "The next card is due in 21 days."
    /// `place` is "this deck" or "your decks".
    func emptyReason(from cards: [Card], place: String, now: Date = Date()) -> String {
        let available = cards.filter { !$0.isSuspended }
        switch kind {
        case .moreNew:
            return available.contains { $0.phase == .new }
                ? "Today's other new cards share a note with a card you studied today, so they wait until tomorrow."
                : "There are no new cards left in \(place). Try “Study by tag” to go over cards you've already learned."
        case .forgotten:
            return "You haven't pressed Again on any card in the last \(days.counted("day")). "
                + "Try “Study by tag” to go over everything."
        case .reviewAhead:
            let later = available.filter { $0.phase == .review && $0.due >= now.endOfDay }.map(\.due).min()
            guard let later else {
                return "Nothing in \(place) is waiting for a later review yet."
            }
            let wait = max(1, now.studyDays(to: later))
            return "Nothing comes due in the next \(days.counted("day")). The next card is due in \(wait.counted("day")): "
                + "press + to look further ahead, or try “Study by tag”."
        case .byTag:
            if tags.isEmpty { return "There are no cards to study in \(place)." }
            return tagCards == .all
                ? "No cards have the picked tags."
                : "No \(tagCards == .new ? "new" : "studied") cards have the picked tags. Try “All cards”."
        }
    }

    /// The day counts the stepper goes through: 1–7 one day at a time, then a week at a time up to a year.
    static func stepDays(_ days: Int, up: Bool) -> Int {
        if up { return days < 7 ? days + 1 : min(365, days + 7) }
        return days > 7 ? max(7, days - 7) : max(1, days - 1)
    }

    static func == (a: CustomStudy, b: CustomStudy) -> Bool { a.id == b.id }
    func hash(into hasher: inout Hasher) { hasher.combine(id) }
}
