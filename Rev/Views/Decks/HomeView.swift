import SwiftUI
import SwiftData

/// The first tab: a greeting, today's numbers, and your decks as big colourful cards.
/// Long-press a deck to rename or delete it. Tap + to create one.
struct HomeView: View {
    @Environment(\.modelContext) private var context
    @Query(sort: \Deck.createdAt) private var decks: [Deck]
    @Query private var allCards: [Card]
    @AppStorage("userName") private var userName = ""

    @State private var reviewedToday = 0
    @State private var showingNewDeck = false
    @State private var newDeckName = ""
    @State private var deckToRename: Deck?
    @State private var renameText = ""
    @State private var deckToDelete: Deck?

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                greeting
                overview

                Text("Your decks")
                    .font(Theme.title(30))
                    .foregroundStyle(Theme.ink)

                if decks.isEmpty {
                    emptyState
                } else {
                    ForEach(Array(decks.enumerated()), id: \.element.id) { index, deck in
                        NavigationLink(value: deck) {
                            DeckTile(deck: deck, palette: Theme.deckPalette(index))
                        }
                        .buttonStyle(PressableButtonStyle())
                        .contextMenu {
                            Button {
                                renameText = deck.name
                                deckToRename = deck
                            } label: {
                                Label("Rename", systemImage: "pencil")
                            }
                            Button(role: .destructive) {
                                deckToDelete = deck
                            } label: {
                                Label("Delete", systemImage: "trash")
                            }
                        }
                    }
                }
            }
            .padding(Theme.padding)
        }
        .background(Theme.background.ignoresSafeArea())
        .navigationDestination(for: Deck.self) { deck in
            DeckDetailView(deck: deck)
        }
        .toolbar {
            ToolbarItem(placement: .topBarLeading) {
                NavigationLink {
                    AllCardsView()
                } label: {
                    Label("All cards", systemImage: "magnifyingglass")
                }
            }
            ToolbarItem(placement: .topBarTrailing) {
                Button {
                    newDeckName = ""
                    showingNewDeck = true
                } label: {
                    Image(systemName: "plus").fontWeight(.bold)
                }
            }
        }
        .alert("New deck", isPresented: $showingNewDeck) {
            TextField("Deck name", text: $newDeckName)
            Button("Create", action: createDeck)
            Button("Cancel", role: .cancel) {}
        }
        .alert("Rename deck",
               isPresented: Binding(get: { deckToRename != nil }, set: { if !$0 { deckToRename = nil } }),
               presenting: deckToRename) { deck in
            TextField("Deck name", text: $renameText)
            Button("Save") { rename(deck) }
            Button("Cancel", role: .cancel) {}
        }
        .confirmationDialog("Delete this deck and all its cards?",
                            isPresented: Binding(get: { deckToDelete != nil }, set: { if !$0 { deckToDelete = nil } }),
                            titleVisibility: .visible,
                            presenting: deckToDelete) { deck in
            Button("Delete \(deck.name)", role: .destructive) {
                deck.deleteWithCards(in: context)
            }
        }
        .onAppear {
            reviewedToday = StatsCalculator.reviewsToday(in: context)
        }
    }

    // MARK: Pieces of the screen

    private var greeting: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(userName.isEmpty ? "Hi there 👋" : "Hi, \(userName) 👋")
                .font(Theme.title(24))
                .foregroundStyle(Theme.ink)
            Text(Date().formatted(.dateTime.weekday(.wide).day().month(.wide)))
                .font(Theme.body(15))
                .foregroundStyle(Theme.mutedInk)
        }
    }

    private var overview: some View {
        VStack(spacing: 14) {
            HStack(spacing: 12) {
                StatTile(value: "\(allCards.dueCount())", label: "Due today", icon: "clock.fill", tint: Theme.teal)
                StatTile(value: "\(allCards.newCount)", label: "New", icon: "sparkles", tint: Theme.coral)
                StatTile(value: "\(reviewedToday)", label: "Done today", icon: "checkmark", tint: Theme.mustard)
            }
            NavigationLink {
                ReviewView(deck: nil)
            } label: {
                Label("Review all decks", systemImage: "play.fill")
            }
            .buttonStyle(PillButtonStyle())
        }
    }

    private var emptyState: some View {
        VStack(spacing: 12) {
            Image(systemName: "square.stack.3d.up")
                .font(.system(size: 40))
                .foregroundStyle(Theme.teal)
            Text("No decks yet")
                .font(Theme.title(22))
                .foregroundStyle(Theme.ink)
            Text("Tap + to make your first deck, or import an Anki deck in Settings.")
                .font(Theme.body(15))
                .foregroundStyle(Theme.mutedInk)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(28)
        .background(Theme.surface, in: RoundedRectangle(cornerRadius: Theme.corner, style: .continuous))
    }

    // MARK: Actions

    private func createDeck() {
        let name = newDeckName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { return }
        context.insert(Deck(name: name))
        try? context.save()
    }

    private func rename(_ deck: Deck) {
        let name = renameText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { return }
        deck.name = name
        try? context.save()
    }
}
