import Foundation

// The shared file format, written by Rev (rev.json) and by the Anki add-on (anki.json).
// Keep in sync with anki-addon/rev_sync/core.py. Times are seconds since 1970.

struct SyncSnapshot: Codable {
    var format: Int
    var writer: String
    var writtenAt: Double
    var cards: [SyncCard]
    var deleted: [SyncTombstone]
    var reviews: [SyncReview]
}

struct SyncCard: Codable {
    /// "<note id>:<ord>", the same in both apps.
    var key: String
    var noteId: String
    var ord: Int
    /// Anki style, sub-decks separated by "::".
    var deck: String
    /// "basic", "reversed", "cloze", "typeIn" or "other".
    var kind: String
    /// From Anki: rendered HTML. From Rev: plain text.
    var front: String
    var back: String
    /// HTML for each Anki field (Rev only; nil when Anki's note type can't be edited from Rev).
    var fields: [String: String]?
    var clozeSource: String?
    var clozeExtra: String?
    var tags: [String]
    var leech: Bool
    var editedAt: Double
    var sched: SyncSchedule
    var schedAt: Double
    var lastReview: Double?
}

struct SyncSchedule: Codable {
    var phase: String
    var due: Double
    var interval: Double
    var ease: Double
    var reps: Int
    var lapses: Int
    var stability: Double?
    var difficulty: Double?
    var suspended: Bool
}

struct SyncTombstone: Codable {
    var key: String
    var at: Double
}

struct SyncReview: Codable {
    var key: String
    /// Milliseconds since 1970 (Anki's review id).
    var at: Int64
    var rating: Int
    var interval: Double
}

/// What Rev remembers between syncs (stored in the app's own storage, not in the shared folder).
struct SyncState: Codable {
    var exportedKeys: [String] = []
    var tombstones: [String: Double] = [:]
    var lastAnkiWrittenAt: Double?
    var lastExportDigest: String?
    /// 2 = cards from Anki keep bold/italic. Older syncs (nil) get one refresh of their text.
    var contentFormat: Int?

    private static var url: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        return base.appendingPathComponent("sync-state.json")
    }

    static func load() -> SyncState {
        guard let data = try? Data(contentsOf: url),
              let state = try? JSONDecoder().decode(SyncState.self, from: data) else { return SyncState() }
        return state
    }

    func save() {
        if let data = try? JSONEncoder().encode(self) {
            try? data.write(to: Self.url, options: .atomic)
        }
    }
}

extension Card {
    /// The card's sync identity, or nil if it isn't linked to Anki yet.
    var syncKey: String? {
        guard let noteID, let noteOrd else { return nil }
        return "\(noteID):\(noteOrd)"
    }
}

extension Date {
    var epoch: Double { timeIntervalSince1970 }
}
