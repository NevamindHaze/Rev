import Foundation
import SwiftData

/// Runs a sync with Anki: over your home Wi-Fi if you paired Rev with Anki (fast), otherwise through
/// the shared iCloud Drive folder you chose in Settings. When Wi-Fi is set up but the computer doesn't
/// answer (you're away from home, or Anki is closed), it uses the iCloud folder instead.
/// It runs by itself when you open Rev and when you leave it, and from "Sync now".
@MainActor
final class SyncService: ObservableObject {
    static let shared = SyncService()

    @Published private(set) var isSyncing = false
    @Published private(set) var lastSync: Date?
    @Published private(set) var lastMessage = ""
    @Published private(set) var folderName: String?
    @Published private(set) var wifiAddress: String?
    @Published private(set) var wifiComputer: String?

    private let bookmarkKey = "syncFolderBookmark"
    private let folderNameKey = "syncFolderName"
    private let lastSyncKey = "syncLastDate"
    private let wifiAddressKey = "syncWifiAddress"
    private let wifiCodeKey = "syncWifiCode"
    private let wifiComputerKey = "syncWifiComputer"

    private init() {
        folderName = UserDefaults.standard.string(forKey: folderNameKey)
        lastSync = UserDefaults.standard.object(forKey: lastSyncKey) as? Date
        wifiAddress = UserDefaults.standard.string(forKey: wifiAddressKey)
        wifiComputer = UserDefaults.standard.string(forKey: wifiComputerKey)
    }

    /// The iCloud folder is chosen.
    var isSetUp: Bool { UserDefaults.standard.data(forKey: bookmarkKey) != nil }

    /// Rev is paired with Anki for sync over Wi-Fi.
    var isWifiSetUp: Bool { wifiAddress != nil && UserDefaults.standard.string(forKey: wifiCodeKey) != nil }

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

    // MARK: Pairing for Wi-Fi

    /// Checks the address and code with Anki and, if they work, remembers them.
    /// Returns what to show in Settings.
    func pairWifi(address: String, code: String) async -> String {
        let address = address.trimmingCharacters(in: .whitespacesAndNewlines)
        let wifi = WifiSync(address: address, code: code)
        do {
            let hello = try await wifi.hello()
            UserDefaults.standard.set(address, forKey: wifiAddressKey)
            UserDefaults.standard.set(code, forKey: wifiCodeKey)
            UserDefaults.standard.set(hello.computer, forKey: wifiComputerKey)
            wifiAddress = address
            wifiComputer = hello.computer
            var state = SyncState.load()
            state.wifiSentImages = nil   // a new computer may not have Rev's pictures yet
            state.save()
            lastMessage = ""
            return "Connected to \(hello.computer)."
        } catch {
            return WifiSync.explain(error)
        }
    }

    func forgetWifi() {
        for key in [wifiAddressKey, wifiCodeKey, wifiComputerKey] { UserDefaults.standard.removeObject(forKey: key) }
        wifiAddress = nil
        wifiComputer = nil
        lastMessage = ""
    }

    /// The paired computer if it answers on this Wi-Fi: first at the address you typed, then by its name
    /// (in case your router gave it a new number). Nil if Wi-Fi isn't set up; throws if it doesn't answer.
    private func reachableComputer() async throws -> WifiSync? {
        guard let address = wifiAddress, let code = UserDefaults.standard.string(forKey: wifiCodeKey) else { return nil }
        let typed = WifiSync(address: address, code: code)
        do {
            _ = try await typed.hello()
            return typed
        } catch let error as URLError {
            guard let computer = wifiComputer, let byName = typed.byName(computer) else { throw error }
            _ = try await byName.hello()
            return byName
        }
    }

    // MARK: Syncing

    func sync(context: ModelContext) async {
        guard !isSyncing, isSetUp || isWifiSetUp else { return }
        isSyncing = true
        defer { isSyncing = false }

        var wifiProblem: String?
        do {
            if let wifi = try await reachableComputer() {
                let report = try await syncOverWifi(wifi, context: context)
                finished(report.summary + " Over Wi-Fi.")
                return
            }
        } catch {
            wifiProblem = WifiSync.explain(error)
        }

        guard let folder = resolveFolder() else {
            lastMessage = "Sync didn't finish: \(wifiProblem ?? "no sync folder is chosen.")"
            return
        }
        do {
            let report = try await syncThroughICloud(folder, context: context)
            finished(report.summary + (wifiProblem == nil ? "" : " Through iCloud, because Wi-Fi didn't work: \(wifiProblem!)"))
        } catch {
            lastMessage = "Sync didn't finish: \(error.localizedDescription)"
        }
    }

    private func finished(_ message: String) {
        lastSync = Date()
        UserDefaults.standard.set(lastSync, forKey: lastSyncKey)
        lastMessage = message
    }

    /// Rev's file, with its time kept when nothing changed since it was last sent.
    private func makeRevFile(context: ModelContext, state: inout SyncState) throws -> (SyncSnapshot, digest: String, changed: Bool) {
        var snapshot = try SyncMerger.makeSnapshot(context: context, state: &state)
        try context.save()
        let digest = SyncMerger.digest(snapshot)
        let changed = digest != state.lastExportDigest
        snapshot.writtenAt = (changed ? nil : state.lastExportWrittenAt) ?? Date().epoch
        return (snapshot, digest, changed)
    }

    private func applyAnki(_ snapshot: SyncSnapshot, context: ModelContext, state: inout SyncState) throws -> SyncMerger.Report? {
        let needsTextRefresh = (state.contentFormat ?? 1) < 2
        guard snapshot.format == 1, snapshot.writtenAt != state.lastAnkiWrittenAt || needsTextRefresh else { return nil }
        let report = try SyncMerger.apply(snapshot, context: context, state: &state)
        try context.save()
        state.lastAnkiWrittenAt = snapshot.writtenAt
        return report
    }

    private static func imagesInAnki(_ snapshot: SyncSnapshot) -> Set<String> {
        Set(snapshot.cards.flatMap {
            CardText.imageNames(in: AnkiText.plainText(fromHTML: $0.front))
                + CardText.imageNames(in: AnkiText.plainText(fromHTML: $0.back))
        })
    }

    private static func imagesInRev(_ snapshot: SyncSnapshot) -> Set<String> {
        Set(snapshot.cards.flatMap { CardText.imageNames(in: $0.front) + CardText.imageNames(in: $0.back) })
    }

    /// Over Wi-Fi: send Rev's pictures and file, apply the Anki file that comes back, fetch Anki's pictures.
    /// (Rev's side goes first here, the reverse of iCloud: Anki applies it and answers with everything.)
    private func syncOverWifi(_ wifi: WifiSync, context: ModelContext) async throws -> SyncMerger.Report {
        var state = SyncState.load()
        let (snapshot, digest, _) = try makeRevFile(context: context, state: &state)

        var sent = Set(state.wifiSentImages ?? [])
        for name in Self.imagesInRev(snapshot).subtracting(sent).sorted() {
            guard let safe = MediaStore.safeName(name), let url = MediaStore.url(for: safe),
                  let data = try? Data(contentsOf: url) else { continue }
            try await wifi.sendImage(data, named: safe)
            sent.insert(name)
        }

        let anki = try await wifi.sync(try JSONEncoder().encode(snapshot))
        state.lastExportDigest = digest
        state.lastExportWrittenAt = snapshot.writtenAt
        let report = try applyAnki(anki, context: context, state: &state) ?? SyncMerger.Report()

        for name in Self.imagesInAnki(anki) where MediaStore.url(for: name) == nil {
            guard let safe = MediaStore.safeName(name), let data = await wifi.image(named: safe) else { continue }
            _ = try? MediaStore.save(data, named: safe)
        }
        state.wifiSentImages = sent.sorted()
        state.save()
        return report
    }

    /// Through the iCloud folder, as before: apply anki.json, then write rev.json if something changed.
    private func syncThroughICloud(_ folder: URL, context: ModelContext) async throws -> SyncMerger.Report {
        let access = folder.startAccessingSecurityScopedResource()
        defer { if access { folder.stopAccessingSecurityScopedResource() } }

        let ankiURL = folder.appendingPathComponent("anki.json")
        let revURL = folder.appendingPathComponent("rev.json")
        let mediaURL = folder.appendingPathComponent("media", isDirectory: true)

        var state = SyncState.load()
        var report = SyncMerger.Report()

        // 1. Apply what changed in Anki.
        let ankiData = try await Task.detached { try SyncFiles.read(ankiURL) }.value
        if let ankiData {
            let snapshot = try JSONDecoder().decode(SyncSnapshot.self, from: ankiData)
            if let applied = try applyAnki(snapshot, context: context, state: &state) {
                report = applied
                let images = Self.imagesInAnki(snapshot)
                await Task.detached { SyncFiles.copyImagesIn(images, from: mediaURL) }.value
            }
        }

        // 2. Write Rev's side (only if something changed).
        let (snapshot, digest, changed) = try makeRevFile(context: context, state: &state)
        if changed {
            let data = try JSONEncoder().encode(snapshot)
            try await Task.detached { try SyncFiles.write(data, to: revURL) }.value
            state.lastExportDigest = digest
            state.lastExportWrittenAt = snapshot.writtenAt
        }
        let images = Self.imagesInRev(snapshot)
        await Task.detached { SyncFiles.copyImagesOut(images, to: mediaURL) }.value

        state.save()
        return report
    }
}
