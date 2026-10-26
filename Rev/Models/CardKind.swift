import Foundation

/// The kinds of card you can make, like Anki's note types.
enum CardKind: String, CaseIterable, Identifiable, Codable {
    /// Question on the front, answer on the back.
    case basic
    /// Makes two cards: front → back, and back → front.
    case reversed
    /// Fill in the blank: hide parts of a sentence with {{c1::...}}. One card per number.
    case cloze
    /// Like basic, but you type your answer before seeing the right one.
    case typeIn

    var id: String { rawValue }

    var title: String {
        switch self {
        case .basic: "Basic"
        case .reversed: "Basic + reversed"
        case .cloze: "Cloze (fill in the blank)"
        case .typeIn: "Type the answer"
        }
    }

    var shortTitle: String {
        switch self {
        case .basic: "Basic"
        case .reversed: "Reversed"
        case .cloze: "Cloze"
        case .typeIn: "Type-in"
        }
    }

    var explanation: String {
        switch self {
        case .basic: "One card: question on the front, answer on the back."
        case .reversed: "Two cards: one asks front → back, the other asks back → front. Great for drug ↔ class or term ↔ definition."
        case .cloze: "Hide words inside a sentence. Type the word in “Word to hide” and tap Hide, or write {{c1::word}} yourself. Each number (c1, c2…) becomes its own card; use the same number to hide several words together."
        case .typeIn: "You type your answer before revealing, then see if it matched. Good for spellings, doses and normal values."
        }
    }
}

/// Reading and writing tags. Tags are single words (like Anki), stored separated by spaces.
enum Tags {
    /// "Paper_1, parasites  paper_1" → ["Paper_1", "parasites"]
    static func parse(_ text: String) -> [String] {
        var seen = Set<String>()
        var result: [String] = []
        for piece in text.split(whereSeparator: { $0 == " " || $0 == "," || $0.isNewline }) {
            let tag = String(piece)
            if seen.insert(tag.lowercased()).inserted { result.append(tag) }
        }
        return result
    }

    static func format(_ tags: [String]) -> String {
        parse(tags.joined(separator: " ")).joined(separator: " ")
    }
}
