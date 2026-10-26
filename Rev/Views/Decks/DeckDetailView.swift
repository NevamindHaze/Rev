import SwiftUI
import SwiftData

/// Inside a deck: its numbers, a big Study button, ways to add cards, and the card list.
/// Filter the list by state or tag, sort it, tap a card to edit it, long-press for more.
struct DeckDetailView: View {
    let deck: Deck

    @Environment(\.modelContext) private var context
    @State private var search = ""
    @State private var showingAddCard = false
    @State private var showingBulkAdd = false
    @State private var cardToEdit: Card?
    @State private var exportDocument: ExportFile?
    @State private var isExporting = false
    @State private var errorMessage: String?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                HStack(spacing: 12) {
                    StatTile(value: "\(deck.cards.count)", label: "Cards", icon: "square.stack.fill", tint: Theme.mustard)
                    StatTile(value: "\(deck.cards.dueCount())", label: "Due today", icon: "clock.fill", tint: Theme.teal)
                    StatTile(value: "\(deck.cards.newCount)", label: "New", icon: "sparkles", tint: Theme.coral)
                }

                NavigationLink {
                    ReviewView(deck: deck)
                } label: {
                    Label("Study now", systemImage: "play.fill")
                }
                .buttonStyle(PillButtonStyle(height: 64))

                HStack(spacing: 12) {
                    Button { showingAddCard = true } label: {
                        Label("Add card", systemImage: "plus")
                    }
                    .buttonStyle(PillButtonStyle(background: Theme.teal, foreground: .white))

                    Button { showingBulkAdd = true } label: {
                        Label("Paste many", systemImage: "text.badge.plus")
                    }
                    .buttonStyle(PillButtonStyle(background: Theme.mustard, foreground: Theme.darkText))
                }

                Text("Cards")
                    .font(Theme.title(24))
                    .foregroundStyle(Theme.ink)
                    .padding(.top, 8)

                if deck.cards.isEmpty {
                    Text("No cards yet. Tap “Add card” to start.")
                        .font(Theme.body(15))
                        .foregroundStyle(Theme.mutedInk)
                } else {
                    CardBrowser(cards: deck.cards, search: search, cardToEdit: $cardToEdit)
                }
            }
            .padding(Theme.padding)
        }
        .background(Theme.background.ignoresSafeArea())
        // Pull down to sync with Anki.
        .refreshable { await SyncService.shared.sync(context: context) }
        .navigationTitle(deck.name)
        .navigationBarTitleDisplayMode(.inline)
        .searchable(text: $search, prompt: "Search cards or tags")
        .toolbar {
            ToolbarItem(placement: .topBarTrailing) {
                Menu {
                    Button(action: exportDeck) {
                        Label("Export as Anki deck (.apkg)", systemImage: "square.and.arrow.up")
                    }
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
            }
        }
        .sheet(isPresented: $showingAddCard) {
            CardEditorView(mode: .add(deck))
        }
        .sheet(item: $cardToEdit) { card in
            CardEditorView(mode: .edit(card))
        }
        .sheet(isPresented: $showingBulkAdd) {
            BulkAddView(deck: deck)
        }
        .fileExporter(isPresented: $isExporting, document: exportDocument, contentType: .apkg,
                      defaultFilename: deck.name) { result in
            if case .failure(let error) = result { errorMessage = error.localizedDescription }
        }
        .alert("Something went wrong",
               isPresented: Binding(get: { errorMessage != nil }, set: { if !$0 { errorMessage = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(errorMessage ?? "")
        }
    }

    private func exportDeck() {
        Task {
            do {
                let data = try await AnkiExport.package([deck])
                exportDocument = ExportFile(data: data)
                isExporting = true
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }
}
