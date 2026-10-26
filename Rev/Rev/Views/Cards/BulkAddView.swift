import SwiftUI
import SwiftData

/// Paste many cards at once: one card per line, with "|" (or a tab) between front and back.
/// Handy for copying from your notes or a spreadsheet.
struct BulkAddView: View {
    let deck: Deck

    @Environment(\.dismiss) private var dismiss
    @Environment(\.modelContext) private var context
    @State private var text = ""

    private var parsed: [(front: String, back: String)] {
        Self.parse(text)
    }

    var body: some View {
        NavigationStack {
            VStack(alignment: .leading, spacing: 14) {
                Text("One card per line. Put a | between the front and the back.")
                    .font(Theme.body(15))
                    .foregroundStyle(Theme.ink)
                Text("Example:  Normal canine heart rate | 60–160 bpm")
                    .font(Theme.body(14))
                    .foregroundStyle(Theme.mutedInk)

                TextEditor(text: $text)
                    .font(Theme.body(17))
                    .scrollContentBackground(.hidden)
                    .padding(12)
                    .background(Theme.surface, in: RoundedRectangle(cornerRadius: 22, style: .continuous))

                Button("Add \(parsed.count) cards", action: add)
                    .buttonStyle(PillButtonStyle(background: Theme.teal, foreground: .white, height: 64))
                    .disabled(parsed.isEmpty)
            }
            .padding(Theme.padding)
            .background(Theme.background.ignoresSafeArea())
            .navigationTitle("Paste many")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
        }
    }

    private func add() {
        let start = Date()
        for (index, pair) in parsed.enumerated() {
            // Tiny time steps keep the cards in the order you pasted them.
            Card.create(front: pair.front, back: pair.back, in: deck, context: context,
                        createdAt: start.addingTimeInterval(Double(index) * 0.001))
        }
        try? context.save()
        dismiss()
    }

    /// Splits pasted text into (front, back) pairs. Lines without a separator are skipped.
    static func parse(_ text: String) -> [(front: String, back: String)] {
        text.split(whereSeparator: \.isNewline).compactMap { line -> (front: String, back: String)? in
            let string = String(line)
            let separator = string.contains("\t") ? "\t" : "|"
            guard let range = string.range(of: separator) else { return nil }
            let front = string[..<range.lowerBound].trimmingCharacters(in: .whitespaces)
            let back = string[range.upperBound...].trimmingCharacters(in: .whitespaces)
            return front.isEmpty ? nil : (front, back)
        }
    }
}
