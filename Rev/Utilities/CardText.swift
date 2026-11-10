import Foundation

/// One line of a card, ready to be laid out on screen.
enum CardLine {
    case text(String)
    case listItem(marker: String, text: String)   // "•" or "1."
    case image(String)                            // file name in MediaStore
    case blank
}

/// A picture's line taken out of a card's text, for the editor, which shows the picture and not its marker.
struct CardImageLine {
    let raw: String       // the line as it was in the text
    let name: String      // file name in MediaStore
    let position: Int     // how many of the other lines come before it
}

/// Understands the simple structure inside card text: list items, image markers, blank lines.
enum CardText {
    private static let imagePattern = try? NSRegularExpression(pattern: #"^\[img:(.+)\]$"#)
    private static let listPattern = try? NSRegularExpression(pattern: #"^(•|\d{1,3}\.)\s+(.*)$"#)

    /// Placeholders for Anki's <b>, </b>, <i> and </i> while its HTML becomes card text.
    static let boldOn: Character = "\u{3}", boldOff: Character = "\u{4}"
    static let italicOn: Character = "\u{5}", italicOff: Character = "\u{6}"

    /// Turns the placeholders into ** and * markers. Bold or italic that runs over several lines (Anki allows it)
    /// is closed at the end of each line and reopened on the next, because Rev formats one line at a time.
    /// A line with no text gets no markers.
    /// shortcut: bold and italic both open across a line break give "***", which the line formatter can't read;
    /// teach it nesting if such cards turn up.
    static func applyingFormatMarkers(_ text: String) -> String {
        var bold = 0, italic = 0
        return text.components(separatedBy: "\n").map { raw -> String in
            var label = "", rest = raw
            if let groups = match(listPattern, raw) { label = groups[1] + " "; rest = groups[2] }
            let open = (bold > 0 ? "**" : "") + (italic > 0 ? "*" : "")
            var body = "", plain = ""
            for c in rest {
                switch c {
                case boldOn: bold += 1; body += "**"
                case boldOff: if bold > 0 { bold -= 1; body += "**" }
                case italicOn: italic += 1; body += "*"
                case italicOff: if italic > 0 { italic -= 1; body += "*" }
                default: body.append(c); plain.append(c)
                }
            }
            if plain.trimmingCharacters(in: .whitespaces).isEmpty {
                return String((label + plain).reversed().drop { $0 == " " || $0 == "\t" }.reversed())
            }
            return label + open + body + (italic > 0 ? "*" : "") + (bold > 0 ? "**" : "")
        }.joined(separator: "\n")
    }

    static func lines(_ text: String) -> [CardLine] {
        text.components(separatedBy: "\n").map { raw -> CardLine in
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty { return .blank }
            if let groups = match(imagePattern, line) { return .image(groups[1]) }
            if let groups = match(listPattern, line) { return .listItem(marker: groups[1], text: groups[2]) }
            return .text(line)
        }
    }

    /// Short text for lists, with image markers shown as a picture symbol.
    static func preview(_ text: String) -> String {
        text.replacingOccurrences(of: #"\[img:[^\]]+\]"#, with: "🖼", options: .regularExpression)
    }

    /// Every image a card uses.
    static func imageNames(in text: String) -> [String] {
        lines(text).compactMap { line -> String? in
            if case .image(let name) = line { return name }
            return nil
        }
    }

    /// Takes the picture lines out of a card's text. `joiningImages` puts them back.
    static func separatingImages(_ text: String) -> (images: [CardImageLine], text: String) {
        var images: [CardImageLine] = []
        var others: [String] = []
        for raw in text.components(separatedBy: "\n") {
            if let groups = match(imagePattern, raw.trimmingCharacters(in: .whitespaces)) {
                images.append(CardImageLine(raw: raw, name: groups[1], position: others.count))
            } else {
                others.append(raw)
            }
        }
        return (images, others.joined(separator: "\n"))
    }

    /// Puts picture lines back where they were. If those lines of text are gone, the pictures go at the end.
    static func joiningImages(_ images: [CardImageLine], text: String) -> String {
        guard !images.isEmpty else { return text }
        var lines = text.isEmpty ? [] : text.components(separatedBy: "\n")
        let count = lines.count
        for image in images.reversed() {
            lines.insert(image.raw, at: min(image.position, count))
        }
        return lines.joined(separator: "\n")
    }

    private static func match(_ regex: NSRegularExpression?, _ line: String) -> [String]? {
        guard let regex else { return nil }
        let ns = line as NSString
        guard let result = regex.firstMatch(in: line, range: NSRange(location: 0, length: ns.length)) else { return nil }
        return (0..<result.numberOfRanges).map { index in
            let range = result.range(at: index)
            return range.location == NSNotFound ? "" : ns.substring(with: range)
        }
    }
}
