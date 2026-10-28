import SwiftUI
import SwiftData

/// Choose an extra study session: more new cards, cards you forgot, cards due soon, or cards with a tag.
struct CustomStudyView: View {
    /// The cards to choose from (one deck, or every deck).
    let cards: [Card]
    /// "this deck" or "your decks", for the explanations.
    var place = "this deck"
    let start: (CustomStudy) -> Void

    @Environment(\.dismiss) private var dismiss
    @Environment(\.modelContext) private var context
    @State private var study = CustomStudy()
    @State private var choseStartingKind = false

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

    private var matchCount: Int {
        study.pickCards(from: cards, context: context).count
    }

    /// How many cards a choice would pick with the current numbers and tags.
    private func count(for kind: CustomStudy.Kind) -> Int {
        var other = study
        other.kind = kind
        return other.pickCards(from: cards, context: context).count
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Picker("Study", selection: $study.kind) {
                        ForEach(CustomStudy.Kind.allCases) { kind in
                            kindRow(kind).tag(kind)
                        }
                    }
                    .pickerStyle(.inline)
                    .labelsHidden()
                }

                options

                Section {
                    Button {
                        start(study)
                        dismiss()
                    } label: {
                        Label("Study \(matchCount.counted("card"))", systemImage: "play.fill")
                    }
                    .buttonStyle(PillButtonStyle(height: 60))
                    .disabled(matchCount == 0)
                    .listRowInsets(EdgeInsets())
                    .listRowBackground(Color.clear)
                } header: {
                    if matchCount == 0 {
                        // Say why nothing matched, instead of just a greyed-out button.
                        Label(study.emptyReason(from: cards, place: place), systemImage: "info.circle")
                            .font(Theme.body(15))
                            .foregroundStyle(Theme.ink)
                            .textCase(nil)
                            .padding(.bottom, 8)
                    }
                } footer: {
                    Text("Your answers count as normal reviews and reschedule the cards.")
                }
            }
            .scrollContentBackground(.hidden)
            .background(Theme.background.ignoresSafeArea())
            .navigationTitle("Custom study")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
            .onAppear {
                // Start on the first choice that has cards, e.g. "Study by tag" in a deck with no new cards.
                guard !choseStartingKind else { return }
                choseStartingKind = true
                if let kind = CustomStudy.Kind.allCases.first(where: { count(for: $0) > 0 }) {
                    study.kind = kind
                }
            }
        }
    }

    /// An icon, the title, what it picks, and how many cards that is right now.
    private func kindRow(_ kind: CustomStudy.Kind) -> some View {
        let available = count(for: kind)
        return HStack(spacing: 14) {
            Image(systemName: kind.icon)
                .foregroundStyle(Theme.teal)
                .frame(width: 26)
            VStack(alignment: .leading, spacing: 2) {
                Text(kind.title).foregroundStyle(Theme.ink)
                Text(kind.subtitle)
                    .font(Theme.body(13))
                    .foregroundStyle(Theme.mutedInk)
            }
            Spacer()
            Text("\(available)")
                .font(Theme.body(15, weight: .bold))
                .foregroundStyle(available > 0 ? Theme.ink : Theme.mutedInk)
                .padding(.horizontal, 10)
                .padding(.vertical, 3)
                .background(Theme.softFill, in: Capsule())
        }
    }

    private func daysStepper(_ title: String) -> some View {
        Stepper {
            Text("\(title) \(study.days.counted("day"))")
        } onIncrement: {
            study.days = CustomStudy.stepDays(study.days, up: true)
        } onDecrement: {
            study.days = CustomStudy.stepDays(study.days, up: false)
        }
    }

    @ViewBuilder
    private var options: some View {
        switch study.kind {
        case .moreNew:
            Section {
                Stepper("New cards: \(study.limit)", value: $study.limit, in: 1...500, step: 5)
            } footer: {
                Text("Learn new cards beyond today's limit, oldest first.")
            }

        case .forgotten:
            Section {
                daysStepper("Last")
            } footer: {
                Text("Go over every card you pressed Again on in this time.")
            }

        case .reviewAhead:
            Section {
                daysStepper("Next")
            } footer: {
                Text("Review cards that aren't due yet, soonest first. Handy before an exam or a trip.")
            }

        case .byTag:
            Section {
                Picker("Cards", selection: $study.tagCards) {
                    ForEach(CustomStudy.TagCards.allCases) { Text($0.rawValue).tag($0) }
                }
                Stepper("At most \(study.limit.counted("card"))", value: $study.limit, in: 5...1000, step: 5)
                Toggle("Random order", isOn: $study.shuffled)
            }

            Section {
                if allTags.isEmpty {
                    Text("No tags yet. Add tags to cards in the card editor.")
                        .foregroundStyle(Theme.mutedInk)
                } else {
                    ForEach(allTags, id: \.self) { tag in
                        Button {
                            if study.tags.contains(tag) { study.tags.remove(tag) } else { study.tags.insert(tag) }
                        } label: {
                            HStack {
                                Text(tag).foregroundStyle(Theme.ink)
                                Spacer()
                                if study.tags.contains(tag) {
                                    Image(systemName: "checkmark").foregroundStyle(Theme.teal)
                                }
                            }
                        }
                    }
                }
            } header: {
                Text("Tags")
            } footer: {
                Text(study.tags.isEmpty ? "No tags picked: any card can be chosen." : "Cards with any of the picked tags.")
            }
        }
    }
}
