import Foundation
import SwiftData

/// Turns what you type in the editor into one or more cards, depending on the card kind.
enum CardFactory {
    /// Creates the card(s) for one note. Returns how many cards were made.
    @discardableResult
    static func create(kind: CardKind, front: String, back: String, tags: [String],
                       in deck: Deck, context: ModelContext, createdAt: Date = Date()) -> Int {
        let noteID = UUID().uuidString
        switch kind {
        case .basic, .typeIn:
            make(front: front, back: back, kind: kind, noteID: noteID, tags: tags, deck: deck,
                 context: context, createdAt: createdAt)
            return 1
        case .reversed:
            make(front: front, back: back, kind: kind, noteID: noteID, tags: tags, deck: deck,
                 context: context, createdAt: createdAt)
            make(front: back, back: front, kind: kind, noteID: noteID, tags: tags, deck: deck,
                 context: context, createdAt: createdAt.addingTimeInterval(0.001))
            return 2
        case .cloze:
            let numbers = clozeNumbers(in: front)
            for (index, number) in numbers.enumerated() {
                let card = make(front: "", back: "", kind: .cloze, noteID: noteID, tags: tags, deck: deck,
                                context: context, createdAt: createdAt.addingTimeInterval(Double(index) * 0.001))
                fillCloze(card, source: front, extra: back, number: number)
            }
            return numbers.count
        }
    }

    /// Edits a cloze note: updates its cards, adds cards for new numbers, removes cards for numbers
    /// you deleted. Cards that stay keep their study progress.
    static func updateCloze(_ card: Card, source: String, extra: String, tags: [String], context: ModelContext) {
        let noteID = card.noteID ?? UUID().uuidString
        card.noteID = noteID
        let siblings = (card.deck?.cards ?? [card]).filter { $0.noteID == noteID && $0.kind == .cloze }
        let numbers = clozeNumbers(in: source)

        for sibling in siblings {
            if let number = sibling.clozeNumber, numbers.contains(number) {
                fillCloze(sibling, source: source, extra: extra, number: number)
                sibling.tags = tags
            } else {
                context.delete(sibling)
            }
        }
        let existing = Set(siblings.compactMap { $0.clozeNumber })
        if let deck = card.deck {
            for number in numbers where !existing.contains(number) {
                let newCard = make(front: "", back: "", kind: .cloze, noteID: noteID, tags: tags, deck: deck,
                                   context: context, createdAt: Date())
                fillCloze(newCard, source: source, extra: extra, number: number)
            }
        }
    }

    /// The cloze numbers used in a text, e.g. "{{c1::a}} {{c3::b}}" → [1, 3].
    static func clozeNumbers(in text: String) -> [Int] {
        guard let regex = try? NSRegularExpression(pattern: #"\{\{c(\d+)::"#, options: .caseInsensitive) else { return [] }
        let ns = text as NSString
        let numbers = regex.matches(in: text, range: NSRange(location: 0, length: ns.length)).compactMap {
            Int(ns.substring(with: $0.range(at: 1)))
        }
        return Array(Set(numbers)).sorted()
    }

    /// The next free cloze number, for the "Hide" button.
    static func nextClozeNumber(in text: String) -> Int {
        (clozeNumbers(in: text).max() ?? 0) + 1
    }

    // MARK: - Helpers

    @discardableResult
    private static func make(front: String, back: String, kind: CardKind, noteID: String, tags: [String],
                             deck: Deck, context: ModelContext, createdAt: Date) -> Card {
        let card = Card.create(front: front, back: back, in: deck, context: context, createdAt: createdAt)
        card.kind = kind
        card.noteID = noteID
        card.tags = tags
        return card
    }

    private static func fillCloze(_ card: Card, source: String, extra: String, number: Int) {
        card.clozeSource = source
        card.clozeExtra = extra
        card.clozeNumber = number
        card.front = AnkiText.cloze(source, number: number, side: .front)
        let answer = AnkiText.cloze(source, number: number, side: .back)
        card.back = extra.isEmpty ? answer : answer + "\n\n" + extra
    }
}
