import SwiftUI
import SwiftData

/// Choose an extra study session: more new cards, cards you forgot, cards due soon, or cards with a tag.
struct CustomStudyView: View {
    /// The cards to choose from (one deck, or every deck).
    let cards: [Card]
    let start: (CustomStudy) -> Void

    @Environment(\.dismiss) private var dismiss
    @Environment(\.modelContext) private var context
    @State private var study = CustomStudy()

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

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Picker("Study", selection: $study.kind) {
                        ForEach(CustomStudy.Kind.allCases) { kind in
                            Label(kind.title, systemImage: kind.icon).tag(kind)
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
                Stepper("Last \(study.days.counted("day"))", value: $study.days, in: 1...30)
            } footer: {
                Text("Go over every card you pressed Again on in this time.")
            }

        case .reviewAhead:
            Section {
                Stepper("Next \(study.days.counted("day"))", value: $study.days, in: 1...30)
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
