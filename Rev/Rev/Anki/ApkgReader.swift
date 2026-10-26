import Foundation
import ZIPFoundation

enum ApkgError: LocalizedError {
    case notAnAnkiFile
    case newFormatOnly
    case noCards

    var errorDescription: String? {
        switch self {
        case .notAnAnkiFile:
            return "This doesn't look like an Anki .apkg file."
        case .newFormatOnly:
            return "This file uses Anki's newest format. In Anki, export the deck again and tick “Support older Anki versions”."
        case .noCards:
            return "No cards were found in this file."
        }
    }
}

/// Reads an Anki .apkg file and turns it into Rev decks.
///
/// An .apkg is a zip file. Inside is a SQLite database ("collection.anki2" or ".anki21") with:
/// - `col`:   the deck names and note types (as JSON text)
/// - `notes`: the note fields, separated by an invisible character (0x1F)
/// - `cards`: one row per card, saying which note, which template and which deck
/// We fill each card's template with its note's fields, then strip the HTML to plain text.
/// Your study progress from Anki is kept. Images and sounds are skipped for now.
enum ApkgReader {
    static func read(fileAt url: URL, now: Date = Date()) throws -> [DeckDTO] {
        let workFolder = try FileHelpers.makeTemporaryFolder()
        defer { try? FileManager.default.removeItem(at: workFolder) }

        let archive: Archive
        do {
            archive = try Archive(url: url, accessMode: .read)
        } catch {
            throw ApkgError.notAnAnkiFile
        }

        let names = Set(archive.map { $0.path })
        let collectionName: String
        if names.contains("collection.anki21") {
            collectionName = "collection.anki21"
        } else if names.contains("collection.anki21b") {
            throw ApkgError.newFormatOnly
        } else if names.contains("collection.anki2") {
            collectionName = "collection.anki2"
        } else {
            throw ApkgError.notAnAnkiFile
        }

        guard let entry = archive[collectionName] else { throw ApkgError.notAnAnkiFile }
        let databaseURL = workFolder.appendingPathComponent("collection.sqlite")
        _ = try archive.extract(entry, to: databaseURL)

        let database = try SQLiteDatabase(path: databaseURL.path, readOnly: true)
        let decks = try readCollection(database, now: now)
        if decks.isEmpty { throw ApkgError.noCards }
        return decks
    }

    // MARK: - Reading the database

    private static func readCollection(_ db: SQLiteDatabase, now: Date) throws -> [DeckDTO] {
        var collectionCreated: Int64 = 0
        var decksJSON = "{}"
        var modelsJSON = "{}"
        try db.query("SELECT crt, decks, models FROM col LIMIT 1") { row in
            collectionCreated = row.int(0)
            decksJSON = row.text(1)
            modelsJSON = row.text(2)
        }
        let deckNames = parseDeckNames(decksJSON)
        let noteTypes = parseNoteTypes(modelsJSON)

        var notes: [Int64: (noteTypeID: Int64, fields: [String], guid: String)] = [:]
        try db.query("SELECT id, mid, flds, guid FROM notes") { row in
            notes[row.int(0)] = (row.int(1), row.text(2).components(separatedBy: "\u{1F}"), row.text(3))
        }

        var cardsByDeck: [Int64: [CardDTO]] = [:]
        var order = 0
        try db.query("SELECT nid, did, odid, ord, type, due, ivl, factor, reps, lapses, data FROM cards ORDER BY type, due, id") { row in
            guard let note = notes[row.int(0)],
                  let noteType = noteTypes[note.noteTypeID],
                  let sides = noteType.render(fields: note.fields, ord: Int(row.int(3))) else { return }

            let front = AnkiText.plainText(fromHTML: sides.front)
            let back = AnkiText.plainText(fromHTML: sides.back)
            guard !front.isEmpty else { return }

            // Tiny time steps keep Anki's order for new cards.
            order += 1
            var card = CardDTO.fresh(front: front, back: back, createdAt: now.addingTimeInterval(Double(order) * 0.001))
            applyProgress(to: &card, type: row.int(4), due: row.int(5), interval: row.int(6),
                          factor: row.int(7), reps: row.int(8), lapses: row.int(9),
                          collectionCreated: collectionCreated, now: now)
            card.noteID = note.guid.isEmpty ? String(row.int(0)) : note.guid

            // If Anki used FSRS, it stored the card's memory as {"s": stability, "d": difficulty}.
            let extra = jsonDictionary(row.text(10))
            if let stability = (extra["s"] as? NSNumber)?.doubleValue,
               let difficulty = (extra["d"] as? NSNumber)?.doubleValue {
                card.stability = stability
                card.difficulty = difficulty
            }

            // Cards sitting in a temporary "filtered deck" belong to their original deck (odid).
            let deckID = row.int(2) != 0 ? row.int(2) : row.int(1)
            cardsByDeck[deckID, default: []].append(card)
        }

        return cardsByDeck.map { deckID, cards in
            let name = (deckNames[deckID] ?? "Imported").replacingOccurrences(of: "::", with: " › ")
            return DeckDTO(id: UUID(), name: name, createdAt: now, cards: cards)
        }
        .sorted { $0.name < $1.name }
    }

    /// Copies Anki's study progress onto the card.
    /// Anki card types: 0 = new, 1 = learning, 2 = review, 3 = relearning.
    private static func applyProgress(to card: inout CardDTO, type: Int64, due: Int64, interval: Int64,
                                      factor: Int64, reps: Int64, lapses: Int64,
                                      collectionCreated: Int64, now: Date) {
        card.reps = Int(reps)
        card.lapses = Int(lapses)
        if factor > 0 { card.ease = Double(factor) / 1000 }

        switch type {
        case 2:
            // For review cards, "due" counts days since the collection was created.
            card.phase = CardPhase.review.rawValue
            card.intervalDays = Double(max(1, interval))
            card.due = Date(timeIntervalSince1970: TimeInterval(collectionCreated + due * 86_400))
        case 1, 3:
            card.phase = (type == 3 ? CardPhase.relearning : CardPhase.learning).rawValue
            card.intervalDays = Double(max(1, interval))
            card.due = now
        default:
            break   // new card: keep the fresh defaults
        }
    }

    // MARK: - Reading the JSON in the `col` table

    private static func jsonDictionary(_ text: String) -> [String: Any] {
        guard let data = text.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return [:] }
        return object
    }

    private static func parseDeckNames(_ json: String) -> [Int64: String] {
        var names: [Int64: String] = [:]
        for (key, value) in jsonDictionary(json) {
            if let id = Int64(key), let deck = value as? [String: Any], let name = deck["name"] as? String {
                names[id] = name
            }
        }
        return names
    }

    private static func parseNoteTypes(_ json: String) -> [Int64: AnkiNoteType] {
        var result: [Int64: AnkiNoteType] = [:]
        for (key, value) in jsonDictionary(json) {
            guard let id = Int64(key), let model = value as? [String: Any] else { continue }

            let fieldList = (model["flds"] as? [[String: Any]]) ?? []
            let fieldNames = fieldList
                .sorted { ($0["ord"] as? Int ?? 0) < ($1["ord"] as? Int ?? 0) }
                .map { $0["name"] as? String ?? "" }

            var templates: [Int: (front: String, back: String)] = [:]
            for template in (model["tmpls"] as? [[String: Any]]) ?? [] {
                let ord = template["ord"] as? Int ?? 0
                templates[ord] = (template["qfmt"] as? String ?? "", template["afmt"] as? String ?? "")
            }

            result[id] = AnkiNoteType(fieldNames: fieldNames, templates: templates,
                                      isCloze: (model["type"] as? Int) == 1)
        }
        return result
    }
}

/// An Anki "note type": the list of fields and the card templates that use them.
struct AnkiNoteType {
    var fieldNames: [String]
    var templates: [Int: (front: String, back: String)]
    var isCloze: Bool

    /// Builds the front and back HTML of one card.
    func render(fields: [String], ord: Int) -> (front: String, back: String)? {
        var values: [String: String] = [:]
        for (index, name) in fieldNames.enumerated() where index < fields.count {
            values[name] = fields[index]
        }

        // Cloze note types have one template; `ord` says which cloze number (c1, c2...) this card is.
        let template = isCloze ? templates[0] ?? templates.values.first : templates[ord]
        guard let template else { return nil }
        let clozeNumber = isCloze ? ord + 1 : nil

        let front = AnkiText.render(template: template.front, fields: values, side: .front, clozeNumber: clozeNumber)
        let back = AnkiText.render(template: template.back, fields: values, side: .back, clozeNumber: clozeNumber)
        return (front, back)
    }
}
