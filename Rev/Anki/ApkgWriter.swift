import Foundation
import CryptoKit
import ZIPFoundation

/// Builds an Anki .apkg file from Rev decks, so you can open your cards in Anki.
///
/// We write the older, widely supported format ("collection.anki2"), which every
/// current Anki version (computer, AnkiMobile, AnkiDroid) can import.
/// Cards use a simple "Rev Basic" note type with Front and Back fields.
/// Review progress is included: graduated cards keep their interval and due date.
enum ApkgWriter {
    /// A fixed ID for the "Rev Basic" note type, so importing again reuses the same note type.
    private static let noteTypeID: Int64 = 1_700_000_000_777

    static func makePackage(decks: [DeckDTO], now: Date = Date()) throws -> Data {
        let workFolder = try FileHelpers.makeTemporaryFolder()
        defer { try? FileManager.default.removeItem(at: workFolder) }

        try writeCollection(decks: decks, to: workFolder.appendingPathComponent("collection.anki2"), now: now)

        // Images: copied in as "0", "1", ... and listed in the "media" file as {"0": "heart.jpg", ...}.
        var mediaList: [String: String] = [:]
        let imageNames = Set(decks.flatMap { $0.cards }.flatMap {
            CardText.imageNames(in: $0.front) + CardText.imageNames(in: $0.back)
        })
        for name in imageNames.sorted() {
            guard let source = MediaStore.url(for: name) else { continue }
            let entryName = String(mediaList.count)
            try FileManager.default.copyItem(at: source, to: workFolder.appendingPathComponent(entryName))
            mediaList[entryName] = name
        }
        let mediaJSON = try JSONSerialization.data(withJSONObject: mediaList)
        try mediaJSON.write(to: workFolder.appendingPathComponent("media"))

        let packageURL = workFolder.appendingPathComponent("export.apkg")
        let files = ["collection.anki2", "media"] + mediaList.keys.sorted { (Int($0) ?? 0) < (Int($1) ?? 0) }
        try writeZip(files: files, in: workFolder, to: packageURL)
        return try Data(contentsOf: packageURL)
    }

    private static func writeZip(files: [String], in folder: URL, to url: URL) throws {
        let archive = try Archive(url: url, accessMode: .create)
        for name in files {
            try archive.addEntry(with: name, relativeTo: folder, compressionMethod: .deflate)
        }
    }

    // MARK: - The database inside the package

    private static func writeCollection(decks: [DeckDTO], to url: URL, now: Date) throws {
        let db = try SQLiteDatabase(path: url.path, readOnly: false)
        try db.execute(schema)

        let nowSeconds = Int64(now.timeIntervalSince1970)
        let nowMillis = nowSeconds * 1000
        let today = now.startOfDay
        let collectionCreated = Int64(today.timeIntervalSince1970)

        // Give each deck an Anki ID. (Anki matches decks by name when importing.)
        var deckJSON: [String: Any] = ["1": deckEntry(id: 1, name: "Default", modified: nowSeconds)]
        var deckIDs: [Int64] = []
        for (index, deck) in decks.enumerated() {
            let id = nowMillis + Int64(index) + 1
            deckIDs.append(id)
            // Rev shows sub-decks as "Parent › Child"; Anki writes them as "Parent::Child".
            let ankiName = deck.name.replacingOccurrences(of: " › ", with: "::")
            deckJSON[String(id)] = deckEntry(id: id, name: ankiName, modified: nowSeconds)
        }

        try db.run("INSERT INTO col VALUES (1, ?, ?, ?, 11, 0, 0, 0, ?, ?, ?, ?, '{}')", [
            .int(collectionCreated), .int(nowMillis), .int(nowMillis),
            .text(json(collectionConfig)),
            .text(json([String(noteTypeID): noteType(modified: nowSeconds)])),
            .text(json(deckJSON)),
            .text(json(deckOptions)),
        ])

        try db.execute("BEGIN")
        var nextID = nowMillis
        var newPosition: Int64 = 0
        for (deck, deckID) in zip(decks, deckIDs) {
            for card in deck.cards {
                nextID += 1
                let fields = [AnkiText.html(fromPlain: card.front), AnkiText.html(fromPlain: card.back)]
                // Anki stores tags with a space on each side. Leeches also get Anki's "leech" tag.
                var tagList = Tags.parse(card.tags ?? "")
                if card.isLeech == true { tagList.append("leech") }
                let tags = tagList.isEmpty ? "" : " " + Tags.format(tagList) + " "
                try db.run("INSERT INTO notes VALUES (?, ?, ?, ?, -1, ?, ?, ?, ?, 0, '')", [
                    .int(nextID), .text(guid(for: card.id)), .int(noteTypeID), .int(nowSeconds), .text(tags),
                    .text(fields.joined(separator: "\u{1F}")), .text(card.front), .int(checksum(card.front)),
                ])

                let progress = ankiProgress(for: card, today: today, newPosition: &newPosition)
                // Anki marks a suspended card with queue = -1.
                let queue = card.isSuspended == true ? -1 : progress.type
                try db.run("INSERT INTO cards VALUES (?, ?, ?, 0, ?, -1, ?, ?, ?, ?, ?, ?, ?, 0, 0, 0, 0, '')", [
                    .int(nextID), .int(nextID), .int(deckID), .int(nowSeconds),
                    .int(progress.type), .int(queue), .int(progress.due),
                    .int(progress.interval), .int(progress.factor),
                    .int(Int64(card.reps)), .int(Int64(card.lapses)),
                ])
            }
        }
        try db.execute("COMMIT")
    }

    /// Converts Rev's schedule to Anki's numbers.
    /// Graduated cards become Anki review cards; everything else becomes a new card.
    private static func ankiProgress(for card: CardDTO, today: Date, newPosition: inout Int64)
        -> (type: Int64, due: Int64, interval: Int64, factor: Int64) {
        if card.phase == CardPhase.review.rawValue || card.phase == CardPhase.relearning.rawValue {
            let days = Calendar.current.dateComponents([.day], from: today, to: card.due.startOfDay).day ?? 0
            return (2, Int64(days), Int64(max(1, card.intervalDays.rounded())), Int64(card.ease * 1000))
        }
        newPosition += 1
        return (0, newPosition, 0, 0)
    }

    /// A stable ID for each note, so exporting the same card twice updates it instead of duplicating.
    private static func guid(for id: UUID) -> String {
        id.uuidString.replacingOccurrences(of: "-", with: "")
    }

    /// Anki's duplicate-check number: the first 8 hex digits of the SHA-1 of the first field.
    private static func checksum(_ text: String) -> Int64 {
        let digest = Insecure.SHA1.hash(data: Data(text.utf8))
        return Int64(digest.prefix(4).reduce(UInt32(0)) { ($0 << 8) | UInt32($1) })
    }

    private static func json(_ object: Any) -> String {
        guard let data = try? JSONSerialization.data(withJSONObject: object),
              let text = String(data: data, encoding: .utf8) else { return "{}" }
        return text
    }

    // MARK: - Fixed pieces Anki expects

    private static let schema = """
    CREATE TABLE col (id integer primary key, crt integer not null, mod integer not null, scm integer not null,
        ver integer not null, dty integer not null, usn integer not null, ls integer not null, conf text not null,
        models text not null, decks text not null, dconf text not null, tags text not null);
    CREATE TABLE notes (id integer primary key, guid text not null, mid integer not null, mod integer not null,
        usn integer not null, tags text not null, flds text not null, sfld integer not null, csum integer not null,
        flags integer not null, data text not null);
    CREATE TABLE cards (id integer primary key, nid integer not null, did integer not null, ord integer not null,
        mod integer not null, usn integer not null, type integer not null, queue integer not null, due integer not null,
        ivl integer not null, factor integer not null, reps integer not null, lapses integer not null,
        left integer not null, odue integer not null, odid integer not null, flags integer not null, data text not null);
    CREATE TABLE revlog (id integer primary key, cid integer not null, usn integer not null, ease integer not null,
        ivl integer not null, lastIvl integer not null, factor integer not null, time integer not null, type integer not null);
    CREATE TABLE graves (usn integer not null, oid integer not null, type integer not null);
    CREATE INDEX ix_notes_usn ON notes (usn);
    CREATE INDEX ix_cards_usn ON cards (usn);
    CREATE INDEX ix_revlog_usn ON revlog (usn);
    CREATE INDEX ix_cards_nid ON cards (nid);
    CREATE INDEX ix_cards_sched ON cards (did, queue, due);
    CREATE INDEX ix_revlog_cid ON revlog (cid);
    CREATE INDEX ix_notes_csum ON notes (csum);
    """

    private static var collectionConfig: [String: Any] {
        ["activeDecks": [1], "curDeck": 1, "newSpread": 0, "collapseTime": 1200, "timeLim": 0,
         "estTimes": true, "dueCounts": true, "curModel": NSNull(), "nextPos": 1,
         "sortType": "noteFld", "sortBackwards": false, "addToCur": true]
    }

    private static var deckOptions: [String: Any] {
        let new: [String: Any] = ["delays": [1, 10], "ints": [1, 4, 7], "initialFactor": 2500,
                                  "separate": true, "order": 1, "perDay": 20, "bury": false]
        let lapse: [String: Any] = ["delays": [10], "mult": 0, "minInt": 1, "leechFails": 8, "leechAction": 0]
        let rev: [String: Any] = ["perDay": 200, "ease4": 1.3, "fuzz": 0.05, "minSpace": 1, "ivlFct": 1,
                                  "maxIvl": 36500, "bury": false, "hardFactor": 1.2]
        let options: [String: Any] = ["id": 1, "name": "Default", "mod": 0, "usn": 0, "maxTaken": 60,
                                      "autoplay": true, "timer": 0, "replayq": true, "dyn": false,
                                      "new": new, "lapse": lapse, "rev": rev]
        return ["1": options]
    }

    private static func deckEntry(id: Int64, name: String, modified: Int64) -> [String: Any] {
        ["id": id, "name": name, "mod": modified, "usn": -1, "desc": "", "dyn": 0, "conf": 1,
         "collapsed": false, "browserCollapsed": false, "extendNew": 0, "extendRev": 0,
         "newToday": [0, 0], "revToday": [0, 0], "lrnToday": [0, 0], "timeToday": [0, 0]]
    }

    private static func noteType(modified: Int64) -> [String: Any] {
        func field(_ name: String, _ ord: Int) -> [String: Any] {
            ["name": name, "ord": ord, "sticky": false, "rtl": false, "font": "Arial", "size": 20,
             "media": [] as [Any]]
        }
        let template: [String: Any] = [
            "name": "Card 1", "ord": 0, "qfmt": "{{Front}}",
            "afmt": "{{FrontSide}}\n\n<hr id=answer>\n\n{{Back}}",
            "did": NSNull(), "bqfmt": "", "bafmt": "",
        ]
        let requirement: [Any] = [0, "all", [0]]
        return [
            "id": noteTypeID, "name": "Rev Basic", "type": 0, "mod": modified, "usn": -1,
            "sortf": 0, "did": 1, "tags": [] as [Any], "vers": [] as [Any],
            "flds": [field("Front", 0), field("Back", 1)],
            "tmpls": [template],
            "req": [requirement],
            "css": ".card {\n font-family: arial;\n font-size: 20px;\n text-align: center;\n color: black;\n background-color: white;\n}\n",
            "latexPre": "\\documentclass[12pt]{article}\n\\special{papersize=3in,5in}\n\\usepackage[utf8]{inputenc}\n\\usepackage{amssymb,amsmath}\n\\pagestyle{empty}\n\\setlength{\\parindent}{0in}\n\\begin{document}\n",
            "latexPost": "\\end{document}",
        ]
    }
}

/// Exporting from the app: copies the decks out of the database, then builds the file in the background.
enum AnkiExport {
    @MainActor
    static func package(_ decks: [Deck]) async throws -> Data {
        let snapshot = decks.map { DeckDTO($0) }
        return try await Task.detached(priority: .userInitiated) {
            try ApkgWriter.makePackage(decks: snapshot)
        }.value
    }
}
