import SwiftUI
import SwiftData

/// Every card from every deck in one list, with the same filters, tags and sorting as a deck.
struct AllCardsView: View {
    @Environment(\.modelContext) private var context
    @Query private var cards: [Card]
    @State private var search = ""
    @State private var cardToEdit: Card?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                if cards.isEmpty {
                    Text("No cards yet.")
                        .font(Theme.body(15))
                        .foregroundStyle(Theme.mutedInk)
                } else {
                    CardBrowser(cards: cards, search: search, showDeckName: true, cardToEdit: $cardToEdit)
                }
            }
            .padding(Theme.padding)
        }
        .background(Theme.background.ignoresSafeArea())
        // Pull down to sync with Anki.
        .refreshable { await SyncService.shared.sync(context: context) }
        .navigationTitle("All cards")
        .navigationBarTitleDisplayMode(.inline)
        .searchable(text: $search, prompt: "Search cards or tags")
        .sheet(item: $cardToEdit) { card in
            CardEditorView(mode: .edit(card))
        }
    }
}
