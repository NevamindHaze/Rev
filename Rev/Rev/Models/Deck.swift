import Foundation
import SwiftData

/// A deck is a named group of cards, e.g. "Pharmacology" or "Large Animal Surgery".
@Model
final class Deck {
    var id: UUID = UUID()
    var name: String = ""
    var createdAt: Date = Date()

    /// The cards in this deck. "cascade" means deleting the deck deletes its cards too.
    @Relationship(deleteRule: .cascade, inverse: \Card.deck)
    var cards: [Card] = []

    init(name: String) {
        self.name = name
    }
}

extension Deck {
    /// Deletes the deck and every card in it, then saves.
    /// (We delete the cards one by one as well, to be extra safe.)
    func deleteWithCards(in context: ModelContext) {
        for card in Array(cards) {
            context.delete(card)
        }
        context.delete(self)
        try? context.save()
    }
}
