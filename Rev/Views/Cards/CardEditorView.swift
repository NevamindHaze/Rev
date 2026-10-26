import SwiftUI
import SwiftData

/// Add new cards or edit an existing one.
/// When adding, choose the kind of card (Basic, Reversed, Cloze, Type-in) and add tags.
/// "Save & next" keeps the screen open (and keeps your tags) so you can type card after card.
struct CardEditorView: View {
    enum Mode {
        case add(Deck)
        case edit(Card)
    }

    let mode: Mode

    @Environment(\.dismiss) private var dismiss
    @Environment(\.modelContext) private var context
    @Query private var allCards: [Card]
    @AppStorage("lastCardKind") private var lastKindRaw = CardKind.basic.rawValue

    @State private var kind: CardKind = .basic
    @State private var front = ""
    @State private var back = ""
    @State private var tagsText = ""
    @State private var wordToHide = ""
    @State private var addedCount = 0
    @FocusState private var focusedField: Field?

    private enum Field { case front, back, tags, hide }

    private var isAdding: Bool {
        if case .add = mode { return true }
        return false
    }

    private var clozeCount: Int { CardFactory.clozeNumbers(in: front).count }

    private var canSave: Bool {
        if kind == .cloze { return clozeCount > 0 }
        return !trimmed(front).isEmpty
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    kindSection

                    textBox(kind == .cloze ? "Text" : "Front", text: $front, field: .front)
                    if kind == .cloze { clozeTools }
                    textBox(kind == .cloze ? "Extra (optional, shown on the back)" : "Back", text: $back, field: .back)

                    tagsSection

                    if isAdding {
                        Button(saveTitle, action: saveAndNext)
                            .buttonStyle(PillButtonStyle(background: Theme.teal, foreground: .white, height: 64))
                            .disabled(!canSave)
                        if addedCount > 0 {
                            Text("\(addedCount.counted("card")) added so far")
                                .font(Theme.body(15, weight: .medium))
                                .foregroundStyle(Theme.mutedInk)
                                .frame(maxWidth: .infinity)
                        }
                    }
                }
                .padding(Theme.padding)
            }
            .background(Theme.background.ignoresSafeArea())
            .navigationTitle(isAdding ? "New card" : "Edit card")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button(isAdding ? "Done" : "Cancel") {
                        // "Done" also keeps a card you typed but didn't save yet.
                        if isAdding && canSave { saveAndNext() }
                        dismiss()
                    }
                }
                if !isAdding {
                    ToolbarItem(placement: .confirmationAction) {
                        Button("Save", action: saveEdit).disabled(!canSave)
                    }
                }
                ToolbarItemGroup(placement: .keyboard) {
                    Spacer()
                    if focusedField == .front {
                        Button("Next") { focusedField = .back }
                    } else if isAdding && canSave {
                        Button(saveTitle, action: saveAndNext)
                    }
                }
            }
            .onAppear(perform: load)
        }
    }

    // MARK: - Pieces of the screen

    @ViewBuilder
    private var kindSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            if isAdding {
                Picker("Card type", selection: $kind) {
                    ForEach(CardKind.allCases) { option in
                        Text(option.title).tag(option)
                    }
                }
                .pickerStyle(.menu)
                .tint(Theme.teal)
                .padding(.horizontal, 6)
                .background(Theme.surface, in: Capsule())
            } else {
                Text(kind.title)
                    .font(Theme.body(15, weight: .semibold))
                    .foregroundStyle(Theme.teal)
            }
            Text(kind.explanation)
                .font(Theme.body(14))
                .foregroundStyle(Theme.mutedInk)
        }
    }

    /// "Word to hide" + Hide buttons, and how many cards the text will make.
    private var clozeTools: some View {
        let next = CardFactory.nextClozeNumber(in: front)
        return VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                TextField("Word to hide", text: $wordToHide)
                    .focused($focusedField, equals: .hide)
                    .textInputAutocapitalization(.never)
                    .padding(12)
                    .background(Theme.surface, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
                Button("Hide c\(next)") { hide(as: next) }
                    .buttonStyle(.borderedProminent)
                    .tint(Theme.coral)
                    .disabled(trimmed(wordToHide).isEmpty)
                if next > 1 {
                    Button("Add to c\(next - 1)") { hide(as: next - 1) }
                        .buttonStyle(.bordered)
                        .tint(Theme.coral)
                        .disabled(trimmed(wordToHide).isEmpty)
                }
            }
            Text(clozeCount == 0 ? "Nothing hidden yet." : "This makes \(clozeCount) card\(clozeCount == 1 ? "" : "s").")
                .font(Theme.body(14, weight: .medium))
                .foregroundStyle(clozeCount == 0 ? Theme.coral : Theme.teal)
        }
    }

    private var tagsSection: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Tags")
                .font(Theme.body(15, weight: .semibold))
                .foregroundStyle(Theme.mutedInk)
            TextField("e.g. parasitology paper_1", text: $tagsText)
                .focused($focusedField, equals: .tags)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()
                .font(Theme.body(17))
                .padding(14)
                .background(Theme.surface, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
            if !suggestedTags.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        ForEach(suggestedTags, id: \.self) { tag in
                            Button { addTag(tag) } label: {
                                Label(tag, systemImage: "plus")
                                    .font(Theme.body(14, weight: .semibold))
                                    .padding(.horizontal, 12)
                                    .padding(.vertical, 7)
                                    .background(Theme.softFill, in: Capsule())
                                    .foregroundStyle(Theme.ink)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }
            Text("Separate tags with spaces. Tags stay filled in for the next card.")
                .font(Theme.body(13))
                .foregroundStyle(Theme.mutedInk)
        }
    }

    private func textBox(_ title: String, text: Binding<String>, field: Field) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title)
                .font(Theme.body(15, weight: .semibold))
                .foregroundStyle(Theme.mutedInk)
            TextField(title, text: text, axis: .vertical)
                .lineLimit(3...12)
                .focused($focusedField, equals: field)
                .font(Theme.body(19))
                .foregroundStyle(Theme.ink)
                .padding(16)
                .background(Theme.surface, in: RoundedRectangle(cornerRadius: 22, style: .continuous))
        }
    }

    private var saveTitle: String {
        switch kind {
        case .reversed: return "Save 2 cards & next"
        case .cloze where clozeCount > 1: return "Save \(clozeCount) cards & next"
        default: return "Save & next"
        }
    }

    /// Your most-used tags that aren't on this card yet.
    private var suggestedTags: [String] {
        let current = Set(Tags.parse(tagsText).map { $0.lowercased() })
        var counts: [String: Int] = [:]
        for card in allCards {
            for tag in card.tags where !current.contains(tag.lowercased()) {
                counts[tag, default: 0] += 1
            }
        }
        return counts.sorted { $0.value > $1.value }.prefix(12).map { $0.key }
    }

    // MARK: - Actions

    private func load() {
        switch mode {
        case .add:
            kind = CardKind(rawValue: lastKindRaw) ?? .basic
        case .edit(let card):
            kind = card.kind
            tagsText = card.tagsText
            if card.kind == .cloze, let source = card.clozeSource {
                front = source
                back = card.clozeExtra ?? ""
            } else {
                front = card.front
                back = card.back
            }
        }
        // A short pause lets the sheet finish sliding up before the keyboard appears.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { focusedField = .front }
    }

    private func addTag(_ tag: String) {
        tagsText = Tags.format(Tags.parse(tagsText) + [tag])
    }

    /// Wraps the first not-yet-hidden appearance of the word in {{cN::...}}.
    private func hide(as number: Int) {
        let word = trimmed(wordToHide)
        guard !word.isEmpty else { return }
        // Skip text that's already inside a cloze.
        let pattern = #"\{\{c\d+::.*?\}\}|"# + NSRegularExpression.escapedPattern(for: word)
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) else { return }
        let ns = front as NSString
        for match in regex.matches(in: front, range: NSRange(location: 0, length: ns.length)) {
            let found = ns.substring(with: match.range)
            if found.hasPrefix("{{") { continue }
            front = ns.replacingCharacters(in: match.range, with: "{{c\(number)::\(found)}}")
            wordToHide = ""
            return
        }
    }

    private func saveAndNext() {
        guard canSave, case .add(let deck) = mode else { return }
        let made = CardFactory.create(kind: kind, front: trimmed(front), back: trimmed(back),
                                      tags: Tags.parse(tagsText), in: deck, context: context)
        try? context.save()
        lastKindRaw = kind.rawValue
        addedCount += made
        front = ""
        back = ""
        wordToHide = ""
        focusedField = .front
    }

    private func saveEdit() {
        guard canSave, case .edit(let card) = mode else { return }
        let tags = Tags.parse(tagsText)
        if card.kind == .cloze, card.clozeSource != nil {
            CardFactory.updateCloze(card, source: trimmed(front), extra: trimmed(back), tags: tags, context: context)
        } else {
            card.front = trimmed(front)
            card.back = trimmed(back)
            card.tags = tags
        }
        try? context.save()
        dismiss()
    }

    private func trimmed(_ text: String) -> String {
        text.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
