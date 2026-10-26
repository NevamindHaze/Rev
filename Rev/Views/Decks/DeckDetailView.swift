import SwiftUI
import SwiftData

/// Inside a deck: its numbers, a big Study button, ways to add cards, and the card list.
/// Tap a card to edit it; long-press it to delete.
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

    /// Newest cards first, filtered by the search box.
    private var visibleCards: [Card] {
        deck.cards
            .filter { search.isEmpty
                || $0.front.localizedCaseInsensitiveContains(search)
                || $0.back.localizedCaseInsensitiveContains(search) }
            .sorted { $0.createdAt > $1.createdAt }
    }

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

                if visibleCards.isEmpty {
                    Text(search.isEmpty ? "No cards yet. Tap “Add card” to start." : "No cards match your search.")
                        .font(Theme.body(15))
                        .foregroundStyle(Theme.mutedInk)
                }

                LazyVStack(spacing: 10) {
                    ForEach(visibleCards) { card in
                        Button { cardToEdit = card } label: {
                            CardRow(card: card)
                        }
                        .buttonStyle(PressableButtonStyle())
                        .contextMenu {
                            Button { cardToEdit = card } label: {
                                Label("Edit", systemImage: "pencil")
                            }
                            Button(role: .destructive) { delete(card) } label: {
                                Label("Delete", systemImage: "trash")
                            }
                        }
                    }
                }
            }
            .padding(Theme.padding)
        }
        .background(Theme.background.ignoresSafeArea())
        .navigationTitle(deck.name)
        .navigationBarTitleDisplayMode(.inline)
        .searchable(text: $search, prompt: "Search cards")
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

    private func delete(_ card: Card) {
        context.delete(card)
        try? context.save()
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

/// One card in the deck's list.
struct CardRow: View {
    let card: Card

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(card.front)
                .font(Theme.body(17, weight: .semibold))
                .foregroundStyle(Theme.ink)
                .lineLimit(2)
            if !card.back.isEmpty {
                Text(card.back)
                    .font(Theme.body(15))
                    .foregroundStyle(Theme.mutedInk)
                    .lineLimit(2)
            }
        }
        .multilineTextAlignment(.leading)
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(18)
        .background(Theme.surface, in: RoundedRectangle(cornerRadius: 22, style: .continuous))
    }
}
