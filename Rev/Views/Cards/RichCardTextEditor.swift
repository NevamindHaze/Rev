import SwiftUI

/// Bold or italic, as asked for by the B / I buttons above the keyboard.
enum CardFormat: Equatable {
    case bold, italic

    /// The marker in card text: ** for bold, * for italic.
    var marker: String { self == .bold ? "**" : "*" }

    fileprivate var intent: InlinePresentationIntent { self == .bold ? .stronglyEmphasized : .emphasized }
}

/// The text box for a card's text on iOS 26 and later: bold and italic words show as real bold and italic
/// while you type, with no stars. The card text itself (`text`) still uses Rev's **bold** and *italic*
/// markers, so saving, syncing with Anki and Rev on Linux/Windows work as before.
@available(iOS 26.0, *)
struct RichCardTextEditor: View {
    @Binding var text: String
    /// Set by the B / I buttons; the box applies it to the selected words and sets it back to nil.
    @Binding var formatRequest: CardFormat?
    let fontSize: CGFloat

    @State private var rich = AttributedString()
    @State private var selection = AttributedTextSelection()

    var body: some View {
        TextEditor(text: $rich, selection: $selection)
            .onAppear { rich = RichCardText.attributed(from: text, size: fontSize) }
            // Changed from outside (loading the card, "Hide", clearing after Save & next).
            .onChange(of: text) { _, newText in
                if RichCardText.markdown(from: rich) != newText {
                    rich = RichCardText.attributed(from: newText, size: fontSize)
                }
            }
            // Typed in the box.
            .onChange(of: rich) { _, newRich in
                let markdown = RichCardText.markdown(from: newRich)
                if markdown != text { text = markdown }
            }
            .onChange(of: formatRequest) { _, request in
                guard let request else { return }
                RichCardText.toggle(request, in: &rich, selection: &selection, size: fontSize)
                formatRequest = nil
            }
    }
}

/// Turns card text with **bold** / *italic* markers into rich text for the editor, and back again.
enum RichCardText {
    // The same rules Rev uses everywhere else (AnkiText.html, and Rev for Linux and Windows),
    // so what looks bold here is bold in Anki too.
    private static let pattern = try! NSRegularExpression(
        pattern: #"\*\*(.+?)\*\*|(?<![*\w])\*(?=\S)(.+?)(?<=\S)\*(?![*\w])"#)

    static func attributed(from text: String, size: CGFloat) -> AttributedString {
        var result = AttributedString()
        append(text, format: nil, size: size, to: &result)
        return result
    }

    private static func append(_ text: String, format: CardFormat?, size: CGFloat, to result: inout AttributedString) {
        let ns = text as NSString
        var position = 0
        for match in pattern.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            if match.range.location > position {
                let before = ns.substring(with: NSRange(location: position, length: match.range.location - position))
                result += piece(before, format: format, size: size)
            }
            let isBold = match.range(at: 1).location != NSNotFound
            let inner = ns.substring(with: match.range(at: isBold ? 1 : 2))
            // A word can be bold or italic, not both (Rev's markers can't say "both" reliably): bold wins.
            append(inner, format: isBold || format == .bold ? .bold : .italic, size: size, to: &result)
            position = match.range.location + match.range.length
        }
        if position < ns.length {
            result += piece(ns.substring(from: position), format: format, size: size)
        }
    }

    private static func piece(_ text: String, format: CardFormat?, size: CGFloat) -> AttributedString {
        var result = AttributedString(text)
        style(&result, format: format, size: size)
        return result
    }

    private static func style(_ string: inout AttributedString, format: CardFormat?, size: CGFloat) {
        string.inlinePresentationIntent = format?.intent
        string.font = font(for: format, size: size)
    }

    private static func font(for format: CardFormat?, size: CGFloat) -> Font? {
        switch format {
        case .bold: return Theme.body(size, weight: .heavy)
        case .italic: return Theme.body(size).italic()
        case nil: return nil   // the box's own font
        }
    }

    private static func format(of intent: InlinePresentationIntent?) -> CardFormat? {
        guard let intent else { return nil }
        if intent.contains(.stronglyEmphasized) { return .bold }
        if intent.contains(.emphasized) { return .italic }
        return nil
    }

    /// The rich text as card text with ** and * markers.
    static func markdown(from rich: AttributedString) -> String {
        // Join neighbouring pieces with the same formatting first, so two bold pieces side by side
        // become one **bold** and not **a****b**.
        var pieces: [(text: String, format: CardFormat?)] = []
        for run in rich.runs {
            let text = String(rich[run.range].characters)
            let runFormat = Self.format(of: run.inlinePresentationIntent)
            if let last = pieces.last, last.format == runFormat {
                pieces[pieces.count - 1].text += text
            } else {
                pieces.append((text, runFormat))
            }
        }
        return pieces.map { wrapped($0.text, in: $0.format?.marker ?? "") }.joined()
    }

    /// Puts the markers around each line of the piece, leaving spaces at either end outside them
    /// (a marker next to a space wouldn't count as formatting).
    private static func wrapped(_ text: String, in marker: String) -> String {
        guard !marker.isEmpty else { return text }
        return text.components(separatedBy: "\n").map { line in
            let core = line.trimmingCharacters(in: .whitespaces)
            guard !core.isEmpty, let range = line.range(of: core) else { return line }
            return line.replacingCharacters(in: range, with: marker + core + marker)
        }.joined(separator: "\n")
    }

    /// Bold or italic for the selected words, or off again if they all have it already.
    /// With nothing selected, it switches it on or off for what you type next.
    static func toggle(_ format: CardFormat, in rich: inout AttributedString,
                       selection: inout AttributedTextSelection, size: CGFloat) {
        let alreadyOn: Bool
        switch selection.indices(in: rich) {
        case .insertionPoint:
            alreadyOn = Self.format(of: selection.typingAttributes(in: rich).inlinePresentationIntent) == format
        case .ranges(let ranges):
            alreadyOn = !ranges.isEmpty && rich[ranges].runs.allSatisfy { Self.format(of: $0.inlinePresentationIntent) == format }
        }
        let newFormat: CardFormat? = alreadyOn ? nil : format
        rich.transformAttributes(in: &selection) { attributes in
            attributes.inlinePresentationIntent = newFormat?.intent
            attributes.font = font(for: newFormat, size: size)
        }
    }
}
