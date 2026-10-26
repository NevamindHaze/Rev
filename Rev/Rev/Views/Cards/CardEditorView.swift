import SwiftUI
import SwiftData

/// Add a new card or edit an existing one.
/// When adding, "Save & next" keeps the screen open so you can type card after card.
struct CardEditorView: View {
    enum Mode {
        case add(Deck)
        case edit(Card)
    }

    let mode: Mode

    @Environment(\.dismiss) private var dismiss
    @Environment(\.modelContext) private var context
    @State private var front = ""
    @State private var back = ""
    @State private var addedCount = 0
    @FocusState private var focusedField: Field?

    private enum Field { case front, back }

    private var isAdding: Bool {
        if case .add = mode { return true }
        return false
    }

    private var canSave: Bool {
        !front.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 20) {
                    textBox("Front", text: $front, field: .front)
                    textBox("Back", text: $back, field: .back)

                    if isAdding {
                        Button("Save & next", action: saveAndNext)
                            .buttonStyle(PillButtonStyle(background: Theme.teal, foreground: .white, height: 64))
                            .disabled(!canSave)
                        if addedCount > 0 {
                            Text("\(addedCount) added so far")
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
                    } else if isAdding {
                        Button("Save & next", action: saveAndNext).disabled(!canSave)
                    }
                }
            }
            .onAppear(perform: load)
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

    private func load() {
        if case .edit(let card) = mode {
            front = card.front
            back = card.back
        }
        // A short pause lets the sheet finish sliding up before the keyboard appears.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { focusedField = .front }
    }

    private func saveAndNext() {
        guard canSave, case .add(let deck) = mode else { return }
        Card.create(front: trimmed(front), back: trimmed(back), in: deck, context: context)
        try? context.save()
        addedCount += 1
        front = ""
        back = ""
        focusedField = .front
    }

    private func saveEdit() {
        guard canSave, case .edit(let card) = mode else { return }
        card.front = trimmed(front)
        card.back = trimmed(back)
        try? context.save()
        dismiss()
    }

    private func trimmed(_ text: String) -> String {
        text.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
