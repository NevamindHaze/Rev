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

    static func plainText(fromHTML html: String) -> String {
        var s = html
        s = replace(#"<(style|script)[^>]*>.*?</\1>"#, in: s) { _ in "" }
        s = replace(#"<br\s*/?>"#, in: s) { _ in "\n" }
        s = replace(#"<hr[^>]*>"#, in: s) { _ in "\n" }
        s = replace(#"</(div|p|li|tr|h[1-6])>"#, in: s) { _ in "\n" }
        s = replace(#"<li(\s[^>]*)?>"#, in: s) { _ in "• " }
        s = replace(#"<img[^>]*>"#, in: s) { _ in "[image]" }
        s = replace(#"\[sound:[^\]]*\]"#, in: s) { _ in "" }
        s = replace(#"<[^>]+>"#, in: s) { _ in "" }
        s = decodeEntities(s)
        s = replace(#"[ \t]+\n"#, in: s) { _ in "\n" }
        s = replace(#"\n{3,}"#, in: s) { _ in "\n\n" }
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
        text.replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\n", with: "<br>")
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
