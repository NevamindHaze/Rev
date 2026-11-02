#if DEBUG
import Foundation
import SwiftData
import UIKit

/// Sample decks for the screenshot tests. The app opens these instead of your real cards
/// when it's started with `-screenshotData <set>`. Debug builds only, so never on your phone.
enum ScreenshotMode {
    static var dataSet: String? { UserDefaults.standard.string(forKey: "screenshotData") }

    static func makeContainer() throws -> ModelContainer {
        let memoryOnly = ModelConfiguration(isStoredInMemoryOnly: true)
        let container = try ModelContainer(for: Deck.self, Card.self, ReviewLog.self, configurations: memoryOnly)
        seed(container.mainContext)
        try container.mainContext.save()
        return container
    }

    private static func seed(_ context: ModelContext) {
        switch dataSet {
        case "standard":
            let vpa = addDeck("VPA", to: context)
            addDue(12, to: vpa, in: context)
            addLater(23, to: vpa, in: context)
            addNew(5, to: vpa, in: context)
            let pharmacology = addDeck("Pharmacology", to: context)
            addDue(1, to: pharmacology, in: context)
            addLater(7, to: pharmacology, in: context)
            let long = addDeck("Large Animal Surgery and Obstetrics for Final Year Revision", to: context)
            addDue(1250, to: long, in: context)
        case "nothingDue":
            addLater(10, to: addDeck("VPA", to: context), in: context)
        case "picture":
            let deck = addDeck("VPA", to: context)
            _ = try? MediaStore.save(samplePicture(), named: "sample.png")
            addCard(front: "[img:sample.png]\nWhat is this?", back: "A sample picture.", to: deck, in: context)
        case "long":
            let lines = (1...60).map { "Line \($0) of a very long answer." }
            addCard(front: "What are the stages of the life cycle?", back: lines.joined(separator: "\n"),
                    to: addDeck("VPA", to: context), in: context)
        case "typeIn":
            let card = addCard(front: "Name the host.", back: "Sheep", to: addDeck("VPA", to: context), in: context)
            card.kind = .typeIn
        default:
            break   // "empty"
        }
    }

    private static func addDeck(_ name: String, to context: ModelContext) -> Deck {
        let deck = Deck(name: name)
        context.insert(deck)
        return deck
    }

    @discardableResult
    private static func addCard(front: String, back: String, due: Date = .now.addingTimeInterval(-86_400),
                                to deck: Deck, in context: ModelContext) -> Card {
        let card = Card(front: front, back: back)
        card.deck = deck
        card.phase = .review
        card.intervalDays = 3
        card.reps = 3
        card.due = due
        context.insert(card)
        return card
    }

    private static func addDue(_ count: Int, to deck: Deck, in context: ModelContext) {
        for number in 0..<count {
            addCard(front: "What does question \(number + 1) ask?", back: "The answer to question \(number + 1).",
                    to: deck, in: context)
        }
    }

    private static func addLater(_ count: Int, to deck: Deck, in context: ModelContext) {
        for number in 0..<count {
            addCard(front: "A later question \(number + 1)?", back: "A later answer \(number + 1).",
                    due: .now.addingTimeInterval(3 * 86_400), to: deck, in: context)
        }
    }

    private static func addNew(_ count: Int, to deck: Deck, in context: ModelContext) {
        for number in 0..<count {
            let card = Card(front: "A new question \(number + 1)?", back: "A new answer \(number + 1).")
            card.deck = deck
            context.insert(card)
        }
    }

    private static func samplePicture() -> Data {
        let size = CGSize(width: 400, height: 300)
        return UIGraphicsImageRenderer(size: size).pngData { drawing in
            UIColor.systemOrange.setFill()
            drawing.fill(CGRect(origin: .zero, size: size))
        }
    }
}
#endif
