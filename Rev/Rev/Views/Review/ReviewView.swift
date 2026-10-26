import SwiftUI
import SwiftData

/// The study screen: question → tap anywhere → answer → rate it.
struct ReviewView: View {
    /// The deck to study, or `nil` to study all decks together.
    let deck: Deck?

    @Environment(\.modelContext) private var context
    @Query private var allCards: [Card]
    @AppStorage("newCardsPerDay") private var newCardsPerDay = 20

    @State private var queue = StudyQueue()
    @State private var current: Card?
    @State private var showAnswer = false
    @State private var reviewedThisSession = 0
    @State private var cardToEdit: Card?
    /// Answers given in this session, newest last, so they can be undone.
    @State private var history: [PastAnswer] = []

    private struct PastAnswer {
        let card: Card
        let stateBefore: SchedulingState
        let log: ReviewLog
    }

    /// Read fresh each time, so a change in Settings applies straight away.
    private var scheduler: any SchedulingAlgorithm { ActiveScheduler.current }

    /// The cards this screen is allowed to show.
    private var cards: [Card] {
        guard let deck else { return allCards }
        return allCards.filter { $0.deck?.id == deck.id }
    }

    var body: some View {
        VStack(spacing: 18) {
            counts
            if let card = current {
                cardFace(card)
                controls(for: card)
            } else {
                finished
            }
        }
        .padding(.horizontal, Theme.padding)
        .padding(.bottom, 12)
        .background(Theme.background.ignoresSafeArea())
        .navigationTitle(deck?.name ?? "Review")
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItemGroup(placement: .topBarTrailing) {
                // Take back the last answer (e.g. you tapped the wrong button).
                Button(action: undo) {
                    Label("Undo", systemImage: "arrow.uturn.backward")
                }
                .disabled(history.isEmpty)

                // Fix a typo or improve the card you're looking at, without leaving the review.
                if let card = current {
                    Button { cardToEdit = card } label: {
                        Label("Edit card", systemImage: "pencil")
                    }
                }
            }
        }
        .sheet(item: $cardToEdit) { card in
            CardEditorView(mode: .edit(card))
        }
        .onAppear(perform: loadNext)
        .onChange(of: allCards.count) {
            if current == nil { loadNext() }
        }
        .sensoryFeedback(.impact(weight: .light), trigger: reviewedThisSession)
    }

    // MARK: Pieces of the screen

    private var counts: some View {
        HStack(spacing: 8) {
            CountPill(label: "new", count: queue.newCards.count, color: Theme.coral)
            CountPill(label: "learning", count: queue.learningCount, color: Theme.mustard)
            CountPill(label: "review", count: queue.reviewDue.count, color: Theme.teal)
        }
        .frame(maxWidth: .infinity)
        .padding(.top, 8)
    }

    private func cardFace(_ card: Card) -> some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                sideLabel("QUESTION")
                Text(card.front)
                    .font(Theme.body(26, weight: .semibold))
                    .foregroundStyle(Theme.ink)

                if showAnswer {
                    Capsule()
                        .fill(Theme.softFill)
                        .frame(height: 3)
                        .padding(.vertical, 8)
                    sideLabel("ANSWER")
                    Text(card.back)
                        .font(Theme.body(22))
                        .foregroundStyle(Theme.ink)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(28)
        }
        .frame(maxHeight: .infinity)
        .background(Theme.surface, in: RoundedRectangle(cornerRadius: Theme.corner, style: .continuous))
        .contentShape(RoundedRectangle(cornerRadius: Theme.corner, style: .continuous))
        .onTapGesture(perform: reveal)
    }

    private func sideLabel(_ text: String) -> some View {
        Text(text)
            .font(Theme.body(13, weight: .bold))
            .tracking(1.2)
            .foregroundStyle(Theme.mutedInk)
    }

    @ViewBuilder
    private func controls(for card: Card) -> some View {
        if showAnswer {
            RatingButtons(previews: previews(for: card)) { rating in
                rate(card, rating)
            }
            .transition(.opacity.combined(with: .move(edge: .bottom)))
        } else {
            Button("Show answer", action: reveal)
                .buttonStyle(PillButtonStyle(height: 76))
                .transition(.opacity)
        }
    }

    private var finished: some View {
        VStack(spacing: 16) {
            Spacer()
            Image(systemName: cards.isEmpty ? "tray" : "checkmark.seal.fill")
                .font(.system(size: 70))
                .foregroundStyle(Theme.teal)
            Text(cards.isEmpty ? "No cards yet" : "All caught up!")
                .font(Theme.title(30))
                .foregroundStyle(Theme.ink)
            Text(finishedMessage)
                .font(Theme.body(16))
                .foregroundStyle(Theme.mutedInk)
                .multilineTextAlignment(.center)
            if queue.laterToday > 0 {
                Button("Check again", action: loadNext)
                    .buttonStyle(PillButtonStyle(background: Theme.teal, foreground: .white))
                    .padding(.top, 8)
            }
            Spacer()
        }
        .frame(maxWidth: .infinity)
    }

    private var finishedMessage: String {
        if cards.isEmpty { return "Add some cards to a deck, then come back here." }
        var lines: [String] = []
        if reviewedThisSession > 0 { lines.append("You reviewed \(reviewedThisSession) cards this session.") }
        if queue.laterToday > 0 {
            lines.append("\(queue.laterToday) learning cards come back later today.")
        } else {
            lines.append("Come back tomorrow for more.")
        }
        return lines.joined(separator: "\n")
    }

    // MARK: Actions

    private func reveal() {
        withAnimation(.snappy) { showAnswer = true }
    }

    /// What each button would do, e.g. Again "1 min", Good "3 d".
    private func previews(for card: Card) -> [Rating: String] {
        let now = Date()
        var result: [Rating: String] = [:]
        for rating in Rating.allCases {
            result[rating] = scheduler.next(after: card.schedulingState, rating: rating, now: now).waitText(from: now)
        }
        return result
    }

    private func rate(_ card: Card, _ rating: Rating) {
        let now = Date()
        let stateBefore = card.schedulingState
        let newState = scheduler.next(after: stateBefore, rating: rating, now: now)
        card.schedulingState = newState
        let log = ReviewLog(cardID: card.id, date: now, rating: rating,
                            wasNew: stateBefore.phase == .new, intervalDays: newState.intervalDays)
        context.insert(log)
        try? context.save()
        history.append(PastAnswer(card: card, stateBefore: stateBefore, log: log))
        reviewedThisSession += 1
        withAnimation(.snappy) { loadNext() }
    }

    /// Puts the last answered card back exactly as it was and shows its question again.
    private func undo() {
        guard let last = history.popLast() else { return }
        last.card.schedulingState = last.stateBefore
        context.delete(last.log)
        try? context.save()
        reviewedThisSession = max(0, reviewedThisSession - 1)
        withAnimation(.snappy) {
            rebuildQueue()
            current = last.card
            showAnswer = false
        }
    }

    /// Rebuilds the queue and picks the next card.
    private func loadNext() {
        rebuildQueue()
        current = queue.nextCard
        showAnswer = false
    }

    private func rebuildQueue() {
        let newLeft = max(0, newCardsPerDay - StatsCalculator.newCardsStudiedToday(in: context))
        // Notes you already studied a card from today: their sibling cards wait until tomorrow.
        let studiedToday = StatsCalculator.cardIDsReviewedToday(in: context)
        let buriedNotes = Set(allCards.filter { studiedToday.contains($0.id) }.compactMap { $0.noteID })
        queue = StudyQueue(cards: cards, newCardsAllowed: newLeft, now: Date(), buriedNoteIDs: buriedNotes)
    }
}
