import Foundation
import ZIPFoundation

enum ApkgError: LocalizedError {
    case notAnAnkiFile
    case noCards

    var errorDescription: String? {
        switch self {
        case .notAnAnkiFile:
            return "This doesn't look like an Anki .apkg or .colpkg file."
        case .noCards:
            return "No cards were found in this file."
        }
    }
}

/// What came out of an Anki file.
struct ApkgContents: Sendable {
    var decks: [DeckDTO]
    var imagesSaved: Int
}

/// Reads an Anki .apkg (or .colpkg) file and turns it into Rev decks.
///
/// The file is a zip. Inside is a SQLite database with the notes and cards, plus the media files.
/// There are two formats:
/// - Older ("collection.anki2" / ".anki21"): deck names and note types are JSON text in the `col` table,
///   and media files are stored as-is.
/// - Newest ("collection.anki21b"): everything is compressed with zstd, and note types live in
///   their own tables in protobuf format.
/// We fill each card's template with its note's fields, then turn the HTML into plain text.
/// Study progress, images, suspended cards and leech tags come across. Sounds are skipped.
enum ApkgReader {
    static func read(fileAt url: URL, now: Date = Date()) throws -> ApkgContents {
        let workFolder = try FileHelpers.makeTemporaryFolder()
        defer { try? FileManager.default.removeItem(at: workFolder) }

        let archive: Archive
        do {
            archive = try Archive(url: url, accessMode: .read)
        } catch {
            throw ApkgError.notAnAnkiFile
        }

        let names = Set(archive.map { $0.path })
        let isNewFormat: Bool
        let collectionName: String
        if names.contains("collection.anki21b") {
            isNewFormat = true
            collectionName = "collection.anki21b"
        } else if names.contains("collection.anki21") {
            isNewFormat = false
            collectionName = "collection.anki21"
        } else if names.contains("collection.anki2") {
            isNewFormat = false
            collectionName = "collection.anki2"
        } else {
            throw ApkgError.notAnAnkiFile
        }

        // 1. The database.
        var collectionData = try entryData(collectionName, in: archive)
        if isNewFormat { collectionData = try Zstd.decompress(collectionData) }
        let databaseURL = workFolder.appendingPathComponent("collection.sqlite")
        try collectionData.write(to: databaseURL)
        collectionData = Data()   // free the memory

        let database = try SQLiteDatabase(path: databaseURL.path, readOnly: true)
        let decks = try readCollection(database, now: now)
        if decks.isEmpty { throw ApkgError.noCards }

        // 2. Images (a problem here shouldn't stop the cards from importing).
        let imagesSaved = (try? saveImages(from: archive, isNewFormat: isNewFormat)) ?? 0

        return ApkgContents(decks: decks, imagesSaved: imagesSaved)
    }

    private static func entryData(_ name: String, in archive: Archive) throws -> Data {
        guard let entry = archive[name] else { throw ApkgError.notAnAnkiFile }
        var data = Data()
        _ = try archive.extract(entry) { chunk in data.append(chunk) }
        return data
    }

    // MARK: - Media

    /// The "media" file lists which zip entry ("0", "1", ...) is which file name.
    private static func saveImages(from archive: Archive, isNewFormat: Bool) throws -> Int {
        guard archive["media"] != nil else { return 0 }
        let listData = try entryData("media", in: archive)

        var namesByEntry: [String: String] = [:]
        if isNewFormat {
            // Protobuf: a list of entries, each with the file name in field 1.
            // The zip entry is its position in the list (or field 255, if set).
            let list = ProtobufMessage(try Zstd.decompress(listData))
            for (index, entry) in list.messages(1).enumerated() {
                guard let name = entry.string(1) else { continue }
                let entryName = entry.number(255).map { String($0) } ?? String(index)
                namesByEntry[entryName] = name
            }
        } else if let json = try? JSONSerialization.jsonObject(with: listData) as? [String: String] {
            namesByEntry = json
        }

        var saved = 0
        for (entryName, fileName) in namesByEntry where MediaStore.isImage(fileName) {
            guard archive[entryName] != nil else { continue }
            var data = try entryData(entryName, in: archive)
            if isNewFormat { data = try Zstd.decompress(data) }
            if try MediaStore.save(data, named: fileName) { saved += 1 }
        }
        return saved
    }

    // MARK: - Reading the database

    private static func readCollection(_ db: SQLiteDatabase, now: Date) throws -> [DeckDTO] {
        var collectionCreated: Int64 = 0
        try db.query("SELECT crt FROM col LIMIT 1") { row in
            collectionCreated = row.int(0)
        }

        var hasNewTables = false
        try db.query("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'notetypes'") { _ in
            hasNewTables = true
        }
        let deckNames = try (hasNewTables ? readDeckNamesFromTables(db) : readDeckNamesFromJSON(db))
        let noteTypes = try (hasNewTables ? readNoteTypesFromTables(db) : readNoteTypesFromJSON(db))

        var notes: [Int64: (noteTypeID: Int64, fields: [String], guid: String, tags: String)] = [:]
        try db.query("SELECT id, mid, flds, guid, tags FROM notes") { row in
            notes[row.int(0)] = (row.int(1), row.text(2).components(separatedBy: "\u{1F}"), row.text(3), row.text(4))
        }

        var cardsByDeck: [Int64: [CardDTO]] = [:]
        var order = 0
        let sql = "SELECT nid, did, odid, ord, type, due, ivl, factor, reps, lapses, data, queue FROM cards ORDER BY type, due, id"
        try db.query(sql) { row in
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
            card.isSuspended = row.int(11) == -1
            card.isLeech = note.tags.lowercased().split(separator: " ").contains("leech")
            card.tags = Tags.format(Tags.parse(note.tags).filter { $0.lowercased() != "leech" })

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
            DeckDTO(id: UUID(), name: deckNames[deckID] ?? "Imported", createdAt: now, cards: cards)
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

    // MARK: - Older format: JSON in the `col` table

    private static func readDeckNamesFromJSON(_ db: SQLiteDatabase) throws -> [Int64: String] {
        var json = "{}"
        try db.query("SELECT decks FROM col LIMIT 1") { row in json = row.text(0) }
        var names: [Int64: String] = [:]
        for (key, value) in jsonDictionary(json) {
            if let id = Int64(key), let deck = value as? [String: Any], let name = deck["name"] as? String {
                names[id] = name.replacingOccurrences(of: "::", with: " › ")
            }
        }
        return names
    }

    private static func readNoteTypesFromJSON(_ db: SQLiteDatabase) throws -> [Int64: AnkiNoteType] {
        var json = "{}"
        try db.query("SELECT models FROM col LIMIT 1") { row in json = row.text(0) }
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

    // MARK: - Newest format: separate tables with protobuf settings

    private static func readDeckNamesFromTables(_ db: SQLiteDatabase) throws -> [Int64: String] {
        var names: [Int64: String] = [:]
        try db.query("SELECT id, name FROM decks") { row in
            // Sub-decks are separated by an invisible character (0x1F).
            names[row.int(0)] = row.text(1).replacingOccurrences(of: "\u{1F}", with: " › ")
        }
        return names
    }

    private static func readNoteTypesFromTables(_ db: SQLiteDatabase) throws -> [Int64: AnkiNoteType] {
        var isCloze: [Int64: Bool] = [:]
        try db.query("SELECT id, config FROM notetypes") { row in
            // Field 1 of the note type's settings is its kind: 0 = normal, 1 = cloze.
            isCloze[row.int(0)] = ProtobufMessage(row.blob(1)).number(1) == 1
        }

        var fields: [Int64: [(ord: Int64, name: String)]] = [:]
        try db.query("SELECT ntid, ord, name FROM fields") { row in
            fields[row.int(0), default: []].append((row.int(1), row.text(2)))
        }

        var templates: [Int64: [Int: (front: String, back: String)]] = [:]
        try db.query("SELECT ntid, ord, config FROM templates") { row in
            // Field 1 = question template, field 2 = answer template.
            let config = ProtobufMessage(row.blob(2))
            templates[row.int(0), default: [:]][Int(row.int(1))] = (config.string(1) ?? "", config.string(2) ?? "")
        }

        var result: [Int64: AnkiNoteType] = [:]
        for (id, cloze) in isCloze {
            let names = (fields[id] ?? []).sorted { $0.ord < $1.ord }.map { $0.name }
            result[id] = AnkiNoteType(fieldNames: names, templates: templates[id] ?? [:], isCloze: cloze)
        }
        return result
    }

    private static func jsonDictionary(_ text: String) -> [String: Any] {
        guard let data = text.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return [:] }
        return object
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
