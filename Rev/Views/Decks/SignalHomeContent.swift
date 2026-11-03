import SwiftUI

/// The Decks tab in the Signal theme: today's count up top, then your decks as compact rows.
struct SignalHomeContent<Empty: View>: View {
    let decks: [Deck]
    let allCards: [Card]
    let reviewedToday: Int
    let onRename: (Deck) -> Void
    let onDelete: (Deck) -> Void
    let empty: Empty

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(dateLine)
                .font(Theme.body(11, weight: .bold))
                .tracking(1)
                .foregroundStyle(Theme.teal)

            // Re-checked every 30 seconds, so cards that come back show up by themselves.
            TimelineView(.periodic(from: .now, by: 30)) { context in
                let due = allCards.toReviewCount(now: context.date)
                HStack(alignment: .firstTextBaseline, spacing: 10) {
                    Text("\(due)")
                        .font(.system(size: 56, weight: .heavy))
                        .foregroundStyle(Theme.ink)
                        .lineLimit(1)
                        .minimumScaleFactor(0.5)
                    Text(caption(due: due))
                        .font(Theme.body(14, weight: .medium))
                        .foregroundStyle(Theme.mutedInk)
                        .lineLimit(2)
                }
            }

            // Only when it has something to say: an empty one would still take up a gap.
            if allCards.comingBackText() != nil {
                ComingBackLabel(cards: allCards)
            }

            NavigationLink {
                ReviewView(deck: nil)
            } label: {
                Text("Start review →")
                    .accessibilityLabel("Start review")
            }
            .buttonStyle(PillButtonStyle(background: Theme.teal, foreground: Theme.onAccent, height: 44))

            if decks.isEmpty {
                empty
            } else {
                VStack(spacing: 0) {
                    ForEach(decks) { deck in
                        row(for: deck)
                    }
                }
            }
        }
    }

    private func row(for deck: Deck) -> some View {
        NavigationLink(value: deck) {
            VStack(spacing: 0) {
                Rectangle()
                    .fill(Theme.mutedInk.opacity(0.25))
                    .frame(height: 1)
                HStack(alignment: .firstTextBaseline, spacing: 12) {
                    Text(deck.name)
                        .font(Theme.body(16, weight: .semibold))
                        .foregroundStyle(Theme.ink)
                        .lineLimit(1)
                    Spacer(minLength: 0)
                    HStack(alignment: .firstTextBaseline, spacing: 4) {
                        TimelineView(.periodic(from: .now, by: 30)) { context in
                            Text("\(deck.cards.toReviewCount(now: context.date))")
                                .font(.system(size: 17, weight: .heavy))
                                .foregroundStyle(Theme.ink)
                        }
                        Text("/ \(deck.cards.count)")
                            .font(Theme.body(12))
                            .foregroundStyle(Theme.mutedInk)
                    }
                    .fixedSize()
                }
                .frame(maxWidth: .infinity, minHeight: 43)
            }
            .contentShape(Rectangle())
        }
        .accessibilityLabel("\(deck.name), \(deck.cards.toReviewCount()) to review, \(deck.cards.count.counted("card"))")
        .buttonStyle(PressableButtonStyle())
        .contextMenu {
            Button {
                onRename(deck)
            } label: {
                Label("Rename", systemImage: "pencil")
            }
            Button(role: .destructive) {
                onDelete(deck)
            } label: {
                Label("Delete", systemImage: "trash")
            }
        }
    }

    /// Like "TUESDAY · 3 NOV".
    private var dateLine: String {
        let now = Date()
        let weekday = now.formatted(.dateTime.weekday(.wide))
        let day = now.formatted(.dateTime.day())
        let month = now.formatted(.dateTime.month(.abbreviated))
        return "\(weekday) · \(day) \(month)".uppercased()
    }

    private func caption(due: Int) -> String {
        var parts = [due == 1 ? "card due" : "cards due", "\(reviewedToday) done"]
        if allCards.newCount > 0 { parts.append("\(allCards.newCount) new") }
        return parts.joined(separator: " · ")
    }
}
