import Foundation

/// Converts Anki's card templates and HTML into plain text for Rev,
/// and Rev's plain text back into simple HTML for exporting.
enum AnkiText {
    enum Side { case front, back }

    // MARK: Templates

    /// Fills an Anki card template (like "{{Front}}") with a note's fields.
    /// `clozeNumber` is used for cloze cards: 1 for c1, 2 for c2, and so on.
    static func render(template: String, fields: [String: String], side: Side, clozeNumber: Int?) -> String {
        // The answer side often repeats the question; Rev shows the question separately.
        var text = template.replacingOccurrences(of: "{{FrontSide}}", with: "")

        // 1. Optional parts: {{#Field}} ... {{/Field}} shows only if the field has text,
        //    {{^Field}} ... {{/Field}} shows only if it is empty.
        for _ in 0..<10 {
            let updated = replace(#"\{\{([#^])\s*([^}]+?)\s*\}\}(.*?)\{\{/\s*\2\s*\}\}"#, in: text) { groups in
                let isEmpty = plainText(fromHTML: fields[groups[2]] ?? "").isEmpty
                let show = groups[1] == "#" ? !isEmpty : isEmpty
                return show ? groups[3] : ""
            }
            if updated == text { break }
            text = updated
        }

        // 2. Field tags: {{Back}}, {{text:Back}}, {{cloze:Text}}, {{type:Back}} ...
        return replace(#"\{\{([^{}]+)\}\}"#, in: text) { groups in
            let parts = groups[1].components(separatedBy: ":").map { $0.trimmingCharacters(in: .whitespaces) }
            let name = parts.last ?? ""
            let filters = parts.dropLast().map { $0.lowercased() }
            let value = fields[name] ?? ""
            if filters.contains("type") { return "" }
            if filters.contains("cloze") { return cloze(value, number: clozeNumber ?? 1, side: side) }
            return value
        }
    }

    /// "{{c1::Dog::animal}}" → front "[animal]" (or "[...]"), back "[Dog]".
    /// Other cloze numbers on the same note are simply shown as normal text.
    static func cloze(_ text: String, number: Int, side: Side) -> String {
        replace(#"\{\{c(\d+)::(.*?)(?:::(.*?))?\}\}"#, in: text) { groups in
            let answer = groups[2]
            guard Int(groups[1]) == number else { return answer }
            if side == .back { return "[" + answer + "]" }
            return groups[3].isEmpty ? "[...]" : "[" + groups[3] + "]"
        }
    }

    // MARK: HTML → plain text

    /// Works like a web browser: line breaks typed in the HTML source don't count,
    /// <br> is a line break, and the edges of blocks (paragraphs, list items...)
    /// start a new line, with several edges in a row counting as just one.
    static func plainText(fromHTML html: String) -> String {
        let edge = "\u{1}"         // temporary marker for "a block starts or ends here"
        let afterLabel = "\u{2}"   // temporary marker right after "•" or "1."
        var s = html
        s = replace(#"<(style|script)[^>]*>.*?</\1>"#, in: s) { _ in "" }
        s = replace(#"\s+"#, in: s) { _ in " " }
        s = replace(#"<br\s*/?>"#, in: s) { _ in "\n" }

        // Lists: <ol> items get 1. 2. 3., <ul> items get •. Lists can sit inside lists.
        var openLists: [(numbered: Bool, count: Int)] = []
        s = replace(#"<(/?)(ol|ul|li)(\s[^>]*)?>"#, in: s) { groups in
            switch (groups[2].lowercased(), groups[1] == "/") {
            case ("ol", false):
                openLists.append((true, 0))
            case ("ul", false):
                openLists.append((false, 0))
            case ("ol", true), ("ul", true):
                if !openLists.isEmpty { openLists.removeLast() }
            case ("li", false):
                guard !openLists.isEmpty else { return edge + "•" + afterLabel }
                openLists[openLists.count - 1].count += 1
                let list = openLists[openLists.count - 1]
                return edge + (list.numbered ? "\(list.count)." : "•") + afterLabel
            default:
                break   // </li>
            }
            return edge
        }

        s = replace(#"</?(div|p|tr|table|blockquote|h[1-6])(\s[^>]*)?>"#, in: s) { _ in edge }
        s = replace(#"<hr[^>]*>"#, in: s) { _ in edge }
        // Images become a marker on their own line, e.g. [img:heart.jpg].
        s = replace(#"<img[^>]*?\ssrc\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))[^>]*>"#, in: s) { groups in
            let source = [groups[1], groups[2], groups[3]].first { !$0.isEmpty } ?? ""
            let decoded = decodeEntities(source.removingPercentEncoding ?? source)
            guard let name = MediaStore.safeName(decoded) else { return "" }
            return edge + "[img:\(name)]" + edge
        }
        s = replace(#"<img[^>]*>"#, in: s) { _ in "" }
        // Bold and italic become Rev's **bold** and *italic* markers.
        s = replace(#"</?(b|strong)(\s[^>]*)?>"#, in: s) { _ in "**" }
        s = replace(#"</?(i|em)(\s[^>]*)?>"#, in: s) { _ in "*" }
        s = replace(#"\[sound:[^\]]*\]"#, in: s) { _ in "" }
        s = replace(#"<[^>]+>"#, in: s) { _ in "" }
        s = decodeEntities(s)

        // A list item whose text sits in its own <div> stays on the "•" or "1." line.
        s = replace(#"\x{02}[ \x{01}]*"#, in: s) { _ in " " }
        // A run of block edges becomes one line break. If it also holds a <br>
        // (Anki's way of writing an empty line), keep one blank line.
        s = replace(#"[ \n]*\x{01}[ \x{01}\n]*"#, in: s) { groups in
            groups[0].contains("\n") ? "\n\n" : "\n"
        }
        s = replace(#"[ \t]+\n"#, in: s) { _ in "\n" }
        s = replace(#"\n[ \t]+"#, in: s) { _ in "\n" }
        s = replace(#"\n{3,}"#, in: s) { _ in "\n\n" }
        // Two bold runs side by side ("</b><b>") leave an empty pair; remove it.
        s = s.replacingOccurrences(of: "****", with: "")
        return s.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// Turns codes like "&amp;" and "&#176;" back into normal characters.
    static func decodeEntities(_ text: String) -> String {
        var s = text
        let named = ["&nbsp;": " ", "&lt;": "<", "&gt;": ">", "&quot;": "\"", "&#39;": "'", "&apos;": "'"]
        for (code, character) in named {
            s = s.replacingOccurrences(of: code, with: character, options: .caseInsensitive)
        }
        s = replace(#"&#x([0-9a-f]+);"#, in: s) { groups in
            guard let number = UInt32(groups[1], radix: 16), let scalar = Unicode.Scalar(number) else { return "" }
            return String(Character(scalar))
        }
        s = replace(#"&#(\d+);"#, in: s) { groups in
            guard let number = UInt32(groups[1]), let scalar = Unicode.Scalar(number) else { return "" }
            return String(Character(scalar))
        }
        return s.replacingOccurrences(of: "&amp;", with: "&", options: .caseInsensitive)
    }

    // MARK: Plain text → HTML (for export)

    static func html(fromPlain text: String) -> String {
        let escaped = text.replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\"", with: "&quot;")
            .replacingOccurrences(of: "\n", with: "<br>")
        // Image markers become real images again, and **bold** / *italic* become HTML.
        var s = replace(#"\[img:([^\]]+)\]"#, in: escaped) { groups in "<img src=\"\(groups[1])\">" }
        s = replace(#"\*\*(.+?)\*\*"#, in: s) { groups in "<b>\(groups[1])</b>" }
        s = replace(#"(?<![*\w])\*(?=\S)(.+?)(?<=\S)\*(?![*\w])"#, in: s) { groups in "<i>\(groups[1])</i>" }
        return s
    }

    // MARK: Helper

    /// Finds every match of a regular expression and replaces it with what `transform` returns.
    /// `transform` receives the whole match as groups[0], then each bracketed group.
    private static func replace(_ pattern: String, in text: String,
                                with transform: ([String]) -> String) -> String {
        guard let regex = try? NSRegularExpression(
            pattern: pattern, options: [.caseInsensitive, .dotMatchesLineSeparators]) else { return text }
        let ns = text as NSString
        var result = ""
        var position = 0
        for match in regex.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            result += ns.substring(with: NSRange(location: position, length: match.range.location - position))
            var groups: [String] = []
            for index in 0..<match.numberOfRanges {
                let range = match.range(at: index)
                groups.append(range.location == NSNotFound ? "" : ns.substring(with: range))
            }
            result += transform(groups)
            position = match.range.location + match.range.length
        }
        result += ns.substring(from: position)
        return result
    }
}
