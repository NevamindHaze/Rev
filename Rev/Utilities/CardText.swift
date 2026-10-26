import Foundation

/// One line of a card, ready to be laid out on screen.
enum CardLine {
    case text(String)
    case listItem(marker: String, text: String)   // "•" or "1."
    case image(String)                            // file name in MediaStore
    case blank
}

/// Understands the simple structure inside card text: list items, image markers, blank lines.
enum CardText {
    private static let imagePattern = try? NSRegularExpression(pattern: #"^\[img:(.+)\]$"#)
    private static let listPattern = try? NSRegularExpression(pattern: #"^(•|\d{1,3}\.)\s+(.*)$"#)

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
