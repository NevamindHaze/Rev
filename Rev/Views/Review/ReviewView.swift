import SwiftUI
import SwiftData

/// The study screen: question → tap anywhere → answer → rate it.
struct ReviewView: View {
    /// The deck to study, or `nil` to study all decks together.
    let deck: Deck?
    /// Set for a custom study session; `nil` for the normal daily queue.
    var custom: CustomStudy? = nil

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
    /// Set when a card has just become a leech, to show the "rewrite or suspend?" question.
    @State private var newLeech: Card?
    /// What you typed on a "Type the answer" card.
    @State private var typedAnswer = ""
    @FocusState private var answerFieldFocused: Bool
    /// Custom study: the cards picked when the session started, and which ones you've answered.
    @State private var sessionCards: [Card]?
    @State private var answeredInSession: Set<UUID> = []
    @State private var showingCustomStudy = false
    @State private var customSession: CustomStudy?

    private struct PastAnswer {
        let card: Card
        let stateBefore: SchedulingState
        let wasLeech: Bool
        let wasSuspended: Bool
        let log: ReviewLog
    }

    /// Like Anki: a card becomes a leech at 8 lapses, and you're reminded again every 4 more.
    private static let leechThreshold = 8

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
        .navigationTitle(custom?.title ?? deck?.name ?? "Review")
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
        .sheet(isPresented: $showingCustomStudy) {
            CustomStudyView(cards: cards, place: deck == nil ? "your decks" : "this deck") { customSession = $0 }
        }
        .navigationDestination(item: $customSession) { session in
            ReviewView(deck: deck, custom: session)
        }
        .alert("This card is a leech",
               isPresented: Binding(get: { newLeech != nil }, set: { if !$0 { newLeech = nil } }),
               presenting: newLeech) { card in
            Button("Rewrite it") { cardToEdit = card }
            Button("Suspend it") { suspend(card) }
            Button("Keep studying", role: .cancel) {}
        } message: { card in
            Text("You've forgotten it \(card.lapses) times. Cards like this usually need rewriting: make them shorter, split them up, or add a memory hook. Suspending hides it from reviews until you unsuspend it in its deck.")
        }
        .onAppear {
            if let custom, sessionCards == nil {
                sessionCards = custom.pickCards(from: cards, context: context)
            }
            loadNext()
        }
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
                CardTextView(text: card.front, size: 26, weight: .semibold)

                if card.kind == .typeIn {
                    typeInBox(for: card)
                }

                if showAnswer {
                    Capsule()
                        .fill(Theme.softFill)
                        .frame(height: 3)
                        .padding(.vertical, 8)
                    sideLabel("ANSWER")
                    CardTextView(text: card.back, size: 22)
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

    /// Before revealing: a box to type into. After: what you typed, green if it matched.
    @ViewBuilder
    private func typeInBox(for card: Card) -> some View {
        if showAnswer {
            let correct = Self.normalized(typedAnswer) == Self.normalized(card.back)
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Image(systemName: correct ? "checkmark.circle.fill" : "xmark.circle.fill")
                Text(typedAnswer.isEmpty ? "(nothing typed)" : typedAnswer)
            }
            .font(Theme.body(18, weight: .semibold))
            .foregroundStyle(correct ? Theme.teal : Theme.coral)
        } else {
            TextField("Type your answer", text: $typedAnswer)
                .focused($answerFieldFocused)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .submitLabel(.done)
                .onSubmit(reveal)
                .font(Theme.body(19))
                .padding(14)
                .background(Theme.softFill, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        }
    }

    /// Ignores capitals, extra spaces and a final full stop when comparing typed answers.
    private static func normalized(_ text: String) -> String {
        text.lowercased().replacingOccurrences(of: "*", with: "")
            .components(separatedBy: .whitespacesAndNewlines).filter { !$0.isEmpty }.joined(separator: " ")
            .trimmingCharacters(in: CharacterSet(charactersIn: ". "))
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
            Image(systemName: isEmpty ? "tray" : "checkmark.seal.fill")
                .font(.system(size: 70))
                .foregroundStyle(Theme.teal)
            Text(isEmpty ? (custom == nil ? "No cards yet" : "No matching cards") : "All caught up!")
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
            // Done for today, or want something different? Like Anki's Custom study.
            if custom == nil && !cards.isEmpty {
                Button { showingCustomStudy = true } label: {
                    Label("Custom study", systemImage: "slider.horizontal.3")
                }
                .buttonStyle(PillButtonStyle(background: Theme.softFill, foreground: Theme.ink))
                .padding(.top, queue.laterToday > 0 ? 0 : 8)
            }
            Spacer()
        }
        .frame(maxWidth: .infinity)
    }

    /// Nothing to study at all: an empty deck, or a custom session that matched no cards.
    private var isEmpty: Bool {
        custom == nil ? cards.isEmpty : (sessionCards ?? []).isEmpty
    }

    private var finishedMessage: String {
        if custom != nil && isEmpty { return "Nothing matches those choices. Go back and try different ones." }
        if cards.isEmpty { return "Add some cards to a deck, then come back here." }
        var lines: [String] = []
        if reviewedThisSession > 0 { lines.append("You reviewed \(reviewedThisSession.counted("card")) this session.") }
        if queue.laterToday > 0 {
            lines.append("\(queue.laterToday) learning cards come back later today.")
        } else if custom != nil {
            lines.append("That's the end of this custom study session.")
        } else {
            lines.append("Come back tomorrow for more.")
        }
        return lines.joined(separator: "\n")
    }

    // MARK: Actions

    private func reveal() {
        answerFieldFocused = false
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
        card.markScheduled()
        let log = ReviewLog(cardID: card.id, date: now, rating: rating,
                            wasNew: stateBefore.phase == .new, intervalDays: newState.intervalDays)
        context.insert(log)
        history.append(PastAnswer(card: card, stateBefore: stateBefore, wasLeech: card.isLeech,
                                  wasSuspended: card.isSuspended, log: log))

        // Forgot a graduated card for the 8th time (or 12th, 16th...)? It's a leech.
        let lapses = newState.lapses
        if rating == .again, stateBefore.phase == .review,
           lapses >= Self.leechThreshold, (lapses - Self.leechThreshold) % 4 == 0 {
            card.isLeech = true
            card.markEdited()
            newLeech = card
        }

        try? context.save()
        answeredInSession.insert(card.id)
        reviewedThisSession += 1
        withAnimation(.snappy) { loadNext() }
    }

    private func suspend(_ card: Card) {
        card.isSuspended = true
        card.markScheduled()
        try? context.save()
        withAnimation(.snappy) { loadNext() }
    }

    /// Puts the last answered card back exactly as it was and shows its question again.
    private func undo() {
        guard let last = history.popLast() else { return }
        last.card.schedulingState = last.stateBefore
        last.card.isLeech = last.wasLeech
        last.card.isSuspended = last.wasSuspended
        last.card.markScheduled()
        last.card.markEdited()
        context.delete(last.log)
        if !history.contains(where: { $0.card.id == last.card.id }) {
            answeredInSession.remove(last.card.id)
        }
        try? context.save()
        reviewedThisSession = max(0, reviewedThisSession - 1)
        withAnimation(.snappy) {
            rebuildQueue()
            current = last.card
            showAnswer = false
            typedAnswer = ""
        }
    }

    /// Rebuilds the queue and picks the next card.
    private func loadNext() {
        rebuildQueue()
        current = queue.nextCard
        showAnswer = false
        typedAnswer = ""
    }

    private func rebuildQueue() {
        if let sessionCards {
            queue = StudyQueue(sessionCards: sessionCards, answered: answeredInSession, now: Date())
            return
        }
        let newLeft = max(0, newCardsPerDay - StatsCalculator.newCardsStudiedToday(in: context))
        // Notes you already studied a card from today: their sibling cards wait until tomorrow.
        let studiedToday = StatsCalculator.cardIDsReviewedToday(in: context)
        let buriedNotes = Set(allCards.filter { studiedToday.contains($0.id) }.compactMap { $0.noteID })
        queue = StudyQueue(cards: cards, newCardsAllowed: newLeft, now: Date(), buriedNoteIDs: buriedNotes)
    }
}
