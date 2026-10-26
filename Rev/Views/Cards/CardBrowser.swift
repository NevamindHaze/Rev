import SwiftUI
import SwiftData

/// Which cards to show, by where they are in their life.
enum CardStateFilter: String, CaseIterable, Identifiable {
    case all = "All cards"
    case dueToday = "Due today"
    case new = "New"
    case learning = "Learning"
    case review = "Review"
    case leeches = "Leeches"
    case suspended = "Suspended"
    var id: String { rawValue }

    func matches(_ card: Card, now: Date) -> Bool {
        switch self {
        case .all: return true
        case .dueToday: return !card.isSuspended && card.phase != .new && card.due < now.endOfDay
        case .new: return card.phase == .new
        case .learning: return card.phase == .learning || card.phase == .relearning
        case .review: return card.phase == .review
        case .leeches: return card.isLeech
        case .suspended: return card.isSuspended
        }
    }
}

/// How to order the list.
enum CardSort: String, CaseIterable, Identifiable {
    case newest = "Newest first"
    case oldest = "Oldest first"
    case due = "Due soonest"
    case alphabetical = "A to Z"
    case mostForgotten = "Most forgotten"
    case hardest = "Hardest (lowest ease)"
    var id: String { rawValue }

    func ordered(_ a: Card, _ b: Card) -> Bool {
        switch self {
        case .newest: return a.createdAt > b.createdAt
        case .oldest: return a.createdAt < b.createdAt
        case .due: return a.due < b.due
        case .alphabetical: return a.front.localizedCaseInsensitiveCompare(b.front) == .orderedAscending
        case .mostForgotten: return a.lapses > b.lapses
        case .hardest: return a.ease < b.ease
        }
    }
}

/// A searchable, filterable, sortable list of cards with tag and state filters.
/// Used inside a deck and on the "All cards" screen.
struct CardBrowser: View {
    let cards: [Card]
    let search: String
    var showDeckName = false
    @Binding var cardToEdit: Card?

    @Environment(\.modelContext) private var context
    @State private var stateFilter: CardStateFilter = .all
    @State private var tagFilter: String?
    @State private var sort: CardSort = .newest
    @State private var tagTarget: Card?
    @State private var newTagText = ""

    private var allTags: [String] {
        var seen = Set<String>()
        var result: [String] = []
        for card in cards {
            for tag in card.tags where seen.insert(tag.lowercased()).inserted {
                result.append(tag)
            }
        }
        return result.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }

    private var visibleCards: [Card] {
        let now = Date()
        return cards
            .filter { stateFilter.matches($0, now: now) }
            .filter { card in
                guard let tagFilter else { return true }
                return card.tags.contains { $0.caseInsensitiveCompare(tagFilter) == .orderedSame }
            }
            .filter { card in
                search.isEmpty
                    || card.front.localizedCaseInsensitiveContains(search)
                    || card.back.localizedCaseInsensitiveContains(search)
                    || card.tagsText.localizedCaseInsensitiveContains(search)
            }
            .sorted(by: sort.ordered)
    }

    var body: some View {
        let visible = visibleCards
        VStack(alignment: .leading, spacing: 12) {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    Menu {
                        Picker("Show", selection: $stateFilter) {
                            ForEach(CardStateFilter.allCases) { Text($0.rawValue).tag($0) }
                        }
                    } label: {
                        chip(stateFilter.rawValue, icon: "line.3.horizontal.decrease.circle", active: stateFilter != .all)
                    }

                    Menu {
                        Button("All tags") { tagFilter = nil }
                        ForEach(allTags, id: \.self) { tag in
                            Button(tag) { tagFilter = tag }
                        }
                    } label: {
                        chip(tagFilter.map { "#\($0)" } ?? "Any tag", icon: "tag", active: tagFilter != nil)
                    }

                    Menu {
                        Picker("Sort", selection: $sort) {
                            ForEach(CardSort.allCases) { Text($0.rawValue).tag($0) }
                        }
                    } label: {
                        chip(sort.rawValue, icon: "arrow.up.arrow.down", active: false)
                    }
                }
            }

            Text("\(visible.count) card\(visible.count == 1 ? "" : "s")")
                .font(Theme.body(13, weight: .semibold))
                .foregroundStyle(Theme.mutedInk)

            LazyVStack(spacing: 10) {
                ForEach(visible) { card in
                    Button { cardToEdit = card } label: {
                        CardRow(card: card, showDeckName: showDeckName)
                    }
                    .buttonStyle(PressableButtonStyle())
                    .contextMenu { menu(for: card) }
                }
            }
        }
        .alert("Add tags", isPresented: Binding(get: { tagTarget != nil }, set: { if !$0 { tagTarget = nil } }),
               presenting: tagTarget) { card in
            TextField("e.g. high_yield", text: $newTagText)
                .textInputAutocapitalization(.never)
            Button("Add") {
                card.tags = card.tags + Tags.parse(newTagText)
                card.markEdited()
                try? context.save()
            }
            Button("Cancel", role: .cancel) {}
        } message: { _ in
            Text("Separate tags with spaces.")
        }
    }

    // MARK: - Pieces

    private func chip(_ text: String, icon: String, active: Bool) -> some View {
        Label(text, systemImage: icon)
            .font(Theme.body(14, weight: .semibold))
            .lineLimit(1)
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .foregroundStyle(active ? .white : Theme.ink)
            .background(active ? Theme.teal : Theme.softFill, in: Capsule())
    }

    @ViewBuilder
    private func menu(for card: Card) -> some View {
        Button { cardToEdit = card } label: {
            Label("Edit", systemImage: "pencil")
        }
        Button {
            newTagText = ""
            tagTarget = card
        } label: {
            Label("Add tags", systemImage: "tag")
        }
        if !card.tags.isEmpty {
            Menu {
                ForEach(card.tags, id: \.self) { tag in
                    Button(tag) {
                        card.tags = card.tags.filter { $0 != tag }
                        card.markEdited()
                        try? context.save()
                    }
                }
            } label: {
                Label("Remove a tag", systemImage: "tag.slash")
            }
        }
        Button {
            card.isSuspended.toggle()
            card.markScheduled()
            try? context.save()
        } label: {
            if card.isSuspended {
                Label("Unsuspend", systemImage: "play.circle")
            } else {
                Label("Suspend", systemImage: "pause.circle")
            }
        }
        if card.isLeech {
            Button {
                card.isLeech = false
                card.markEdited()
                try? context.save()
            } label: {
                Label("Not a leech any more", systemImage: "checkmark.circle")
            }
        }
        Button(role: .destructive) {
            context.delete(card)
            try? context.save()
        } label: {
            Label("Delete", systemImage: "trash")
        }
    }
}

/// One card in a list: its badges, question, answer and tags.
struct CardRow: View {
    let card: Card
    var showDeckName = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                badge(stateText, color: stateColor)
                if card.kind != .basic { badge(card.kind.shortTitle, color: Theme.mustard, dark: true) }
                if card.isLeech { badge("Leech", color: Theme.coral) }
                if card.isSuspended { badge("Suspended", color: Theme.mutedInk) }
                Spacer()
                if showDeckName, let deck = card.deck {
                    Text(deck.name)
                        .font(Theme.body(12, weight: .medium))
                        .foregroundStyle(Theme.mutedInk)
                        .lineLimit(1)
                }
            }
            Text(CardTextView.styled(CardText.preview(card.front)))
                .font(Theme.body(17, weight: .semibold))
                .foregroundStyle(Theme.ink)
                .lineLimit(2)
            if !card.back.isEmpty {
                Text(CardTextView.styled(CardText.preview(card.back)))
                    .font(Theme.body(15))
                    .foregroundStyle(Theme.mutedInk)
                    .lineLimit(2)
            }
            if !card.tags.isEmpty {
                Text(card.tags.map { "#\($0)" }.joined(separator: "  "))
                    .font(Theme.body(13, weight: .medium))
                    .foregroundStyle(Theme.teal)
                    .lineLimit(1)
            }
        }
        .multilineTextAlignment(.leading)
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(18)
        .background(Theme.surface, in: RoundedRectangle(cornerRadius: 22, style: .continuous))
        .opacity(card.isSuspended ? 0.6 : 1)
    }

    private var stateText: String {
        switch card.phase {
        case .new: return "New"
        case .learning, .relearning: return "Learning"
        case .review: return "Due " + card.due.formatted(.relative(presentation: .named))
        }
    }

    private var stateColor: Color {
        switch card.phase {
        case .new: return Theme.coral
        case .learning, .relearning: return Theme.mustard
        case .review: return Theme.teal
        }
    }

    private func badge(_ text: String, color: Color, dark: Bool = false) -> some View {
        Text(text)
            .font(Theme.body(12, weight: .bold))
            .foregroundStyle(dark ? Theme.darkText : .white)
            .padding(.horizontal, 10)
            .padding(.vertical, 4)
            .background(color, in: Capsule())
    }
}
