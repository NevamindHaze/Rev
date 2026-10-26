import Foundation
import SwiftData

/// Runs a sync with Anki through the shared iCloud Drive folder you chose in Settings.
/// It runs by itself when you open Rev and when you leave it, and from "Sync now".
@MainActor
final class SyncService: ObservableObject {
    static let shared = SyncService()

    @Published private(set) var isSyncing = false
    @Published private(set) var lastSync: Date?
    @Published private(set) var lastMessage = ""
    @Published private(set) var folderName: String?

    private let bookmarkKey = "syncFolderBookmark"
    private let folderNameKey = "syncFolderName"
    private let lastSyncKey = "syncLastDate"

    private init() {
        folderName = UserDefaults.standard.string(forKey: folderNameKey)
        lastSync = UserDefaults.standard.object(forKey: lastSyncKey) as? Date
    }

    var isSetUp: Bool { UserDefaults.standard.data(forKey: bookmarkKey) != nil }

    /// Remembers the folder you picked, so Rev can open it again later without asking.
    func setFolder(_ url: URL) throws {
        let access = url.startAccessingSecurityScopedResource()
        defer { if access { url.stopAccessingSecurityScopedResource() } }
        let bookmark = try url.bookmarkData(options: [], includingResourceValuesForKeys: nil, relativeTo: nil)
        UserDefaults.standard.set(bookmark, forKey: bookmarkKey)
        UserDefaults.standard.set(url.lastPathComponent, forKey: folderNameKey)
        folderName = url.lastPathComponent
        lastMessage = ""
    }

    func turnOff() {
        UserDefaults.standard.removeObject(forKey: bookmarkKey)
        UserDefaults.standard.removeObject(forKey: folderNameKey)
        folderName = nil
        lastMessage = ""
    }

    private func resolveFolder() -> URL? {
        guard let data = UserDefaults.standard.data(forKey: bookmarkKey) else { return nil }
        var isStale = false
        guard let url = try? URL(resolvingBookmarkData: data, options: [], relativeTo: nil,
                                 bookmarkDataIsStale: &isStale) else { return nil }
        if isStale { try? setFolder(url) }
        return url
    }

    func sync(context: ModelContext) async {
        guard !isSyncing, let folder = resolveFolder() else { return }
        isSyncing = true
        defer { isSyncing = false }
        let access = folder.startAccessingSecurityScopedResource()
        defer { if access { folder.stopAccessingSecurityScopedResource() } }

        let ankiURL = folder.appendingPathComponent("anki.json")
        let revURL = folder.appendingPathComponent("rev.json")
        let mediaURL = folder.appendingPathComponent("media", isDirectory: true)

        do {
            var state = SyncState.load()
            var report = SyncMerger.Report()

            // 1. Apply what changed in Anki.
            let ankiData = try await Task.detached { try SyncFiles.read(ankiURL) }.value
            if let ankiData {
                let snapshot = try JSONDecoder().decode(SyncSnapshot.self, from: ankiData)
                if snapshot.format == 1, snapshot.writtenAt != state.lastAnkiWrittenAt {
                    report = try SyncMerger.apply(snapshot, context: context, state: &state)
                    try context.save()
                    let images = Set(snapshot.cards.flatMap {
                        CardText.imageNames(in: AnkiText.plainText(fromHTML: $0.front))
                            + CardText.imageNames(in: AnkiText.plainText(fromHTML: $0.back))
                    })
                    await Task.detached { SyncFiles.copyImagesIn(images, from: mediaURL) }.value
                    state.lastAnkiWrittenAt = snapshot.writtenAt
                }
            }

            // 2. Write Rev's side (only if something changed).
            var snapshot = try SyncMerger.makeSnapshot(context: context, state: &state)
            try context.save()
            let digest = SyncMerger.digest(snapshot)
            if digest != state.lastExportDigest {
                snapshot.writtenAt = Date().epoch
                let data = try JSONEncoder().encode(snapshot)
                try await Task.detached { try SyncFiles.write(data, to: revURL) }.value
                state.lastExportDigest = digest
            }
            let images = Set(snapshot.cards.flatMap { CardText.imageNames(in: $0.front) + CardText.imageNames(in: $0.back) })
            await Task.detached { SyncFiles.copyImagesOut(images, to: mediaURL) }.value

            state.save()
            lastSync = Date()
            UserDefaults.standard.set(lastSync, forKey: lastSyncKey)
            lastMessage = report.summary
        } catch {
            lastMessage = "Sync didn't finish: \(error.localizedDescription)"
        }
    }
}
