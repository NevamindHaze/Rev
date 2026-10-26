import SwiftUI
import SwiftData
import UniformTypeIdentifiers

/// Your name, the daily new-card limit, JSON backup/restore, and Anki import/export.
struct SettingsView: View {
    @Environment(\.modelContext) private var context
    @Query(sort: \Deck.createdAt) private var decks: [Deck]
    @AppStorage("userName") private var userName = ""
    @AppStorage("newCardsPerDay") private var newCardsPerDay = 20
    @AppStorage("scheduler") private var scheduler = SchedulerKind.fsrs.rawValue
    @AppStorage("desiredRetention") private var desiredRetention = 0.9

    // Saving a file
    @State private var exportDocument: ExportFile?
    @State private var exportType: UTType = .json
    @State private var exportName = ""
    @State private var isExporting = false

    // Opening a file
    private enum ImportKind { case backup, anki, syncFolder }
    @ObservedObject private var sync = SyncService.shared
    @State private var importKind: ImportKind = .backup
    @State private var isImporting = false
    @State private var pendingBackup: BackupFile?

    @State private var isWorking = false
    @State private var message: String?

    // Pairing for sync over Wi-Fi
    @State private var wifiAddressText = ""
    @State private var wifiCodeText = ""
    @State private var isPairing = false
    @State private var pairingMessage: String?

    var body: some View {
        Form {
            Section("You") {
                TextField("Your name", text: $userName)
            }

            Section {
                Stepper("New cards per day: \(newCardsPerDay)", value: $newCardsPerDay, in: 0...500, step: 5)
            } header: {
                Text("Study")
            } footer: {
                Text("How many never-seen cards to introduce each day.")
            }

            Section {
                Picker("Algorithm", selection: $scheduler) {
                    ForEach(SchedulerKind.allCases) { kind in
                        Text(kind.title).tag(kind.rawValue)
                    }
                }
                if scheduler == SchedulerKind.fsrs.rawValue {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Target memory: \(Int((desiredRetention * 100).rounded()))%")
                        Slider(value: $desiredRetention, in: 0.80...0.97, step: 0.01)
                    }
                }
            } header: {
                Text("Scheduling")
            } footer: {
                Text("FSRS predicts when you're about to forget each card. A higher target means you remember more but review more often; 90% suits most people. Switching algorithms keeps all your cards and progress.")
            }

            syncSection

            wifiSection

            Section {
                Button(action: exportBackup) {
                    Label("Export backup (.json)", systemImage: "square.and.arrow.up")
                }
                Button {
                    importKind = .backup
                    isImporting = true
                } label: {
                    Label("Restore from backup", systemImage: "arrow.counterclockwise")
                }
            } header: {
                Text("Backup")
            } footer: {
                Text("Save a backup to the Files app often. If the app expires or gets deleted, reinstall it and restore your latest backup. Restoring replaces everything in the app. Images aren't inside the backup file: after restoring, import your Anki decks again to bring them back (your progress is kept).")
            }

            Section {
                Button {
                    importKind = .anki
                    isImporting = true
                } label: {
                    Label("Import Anki deck (.apkg)", systemImage: "square.and.arrow.down")
                }
                Button(action: exportAllToAnki) {
                    Label("Export all decks to Anki (.apkg)", systemImage: "square.and.arrow.up")
                }
                .disabled(decks.isEmpty)
            } header: {
                Text("Anki")
            } footer: {
                Text("Works with any .apkg or .colpkg from Anki, old or new. Text, images, study progress, suspended cards and leech tags come across; sounds are skipped. Importing the same deck again updates your cards instead of adding copies.")
            }

            Section("About") {
                LabeledContent("Version", value: appVersion)
            }
        }
        .scrollContentBackground(.hidden)
        .background(Theme.background.ignoresSafeArea())
        .navigationTitle("Settings")
        .disabled(isWorking)
        .overlay {
            if isWorking {
                ProgressView()
                    .controlSize(.large)
                    .padding(30)
                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 20))
            }
        }
        .fileImporter(isPresented: $isImporting,
                      allowedContentTypes: allowedImportTypes) { result in
            handlePickedFile(result)
        }
        // The exporter sits on a separate background view so it doesn't clash with the importer.
        .background {
            Color.clear
                .fileExporter(isPresented: $isExporting, document: exportDocument,
                              contentType: exportType, defaultFilename: exportName) { result in
                    if case .failure(let error) = result { message = error.localizedDescription }
                }
        }
        .alert("Replace everything with this backup?",
               isPresented: Binding(get: { pendingBackup != nil }, set: { if !$0 { pendingBackup = nil } }),
               presenting: pendingBackup) { backup in
            Button("Replace everything", role: .destructive) { restore(backup) }
            Button("Cancel", role: .cancel) {}
        } message: { backup in
            Text("Backup from \(backup.exportedAt.formatted(date: .abbreviated, time: .shortened)): \(backup.decks.count.counted("deck")), \(backup.decks.reduce(0) { $0 + $1.cards.count }.counted("card")). Everything currently in Rev will be replaced.")
        }
        .alert("Rev", isPresented: Binding(get: { message != nil }, set: { if !$0 { message = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(message ?? "")
        }
    }

    // MARK: Sync with Anki

    private var syncSection: some View {
        Section {
            if sync.isSetUp {
                LabeledContent("Folder", value: sync.folderName ?? "Rev Sync")
                LabeledContent("Last synced") {
                    if sync.isSyncing {
                        ProgressView()
                    } else {
                        Text(sync.lastSync.map { $0.formatted(.relative(presentation: .named)) } ?? "Not yet")
                    }
                }
                if !sync.lastMessage.isEmpty {
                    Text(sync.lastMessage)
                        .font(Theme.body(14))
                        .foregroundStyle(Theme.mutedInk)
                }
                Button {
                    Task { await sync.sync(context: context) }
                } label: {
                    Label("Sync now", systemImage: "arrow.triangle.2.circlepath")
                }
                .disabled(sync.isSyncing)
                Button(role: .destructive) {
                    sync.turnOff()
                } label: {
                    Label("Stop syncing", systemImage: "xmark.circle")
                }
            } else {
                Button {
                    importKind = .syncFolder
                    isImporting = true
                } label: {
                    Label("Choose the Rev Sync folder", systemImage: "folder.badge.gearshape")
                }
            }
        } header: {
            Text("Sync with Anki")
        } footer: {
            Text(sync.isSetUp
                 ? "Rev syncs by itself when you open it and when you leave it. Anki on your computer syncs through the Rev Sync add-on, and then with AnkiWeb as usual."
                 : "Keeps Rev and Anki on your computer in step, both ways. Tap the button, open iCloud Drive, select the “Rev Sync” folder and tap Open.")
        }
    }

    // MARK: Sync over Wi-Fi

    private var wifiSection: some View {
        Section {
            if sync.isWifiSetUp {
                LabeledContent("Computer", value: sync.wifiComputer ?? sync.wifiAddress ?? "")
                if !sync.isSetUp {
                    // Without an iCloud folder, the Sync now button and the last result live here.
                    if !sync.lastMessage.isEmpty {
                        Text(sync.lastMessage)
                            .font(Theme.body(14))
                            .foregroundStyle(Theme.mutedInk)
                    }
                    Button {
                        Task { await sync.sync(context: context) }
                    } label: {
                        Label("Sync now", systemImage: "arrow.triangle.2.circlepath")
                    }
                    .disabled(sync.isSyncing)
                }
                Button(role: .destructive) {
                    sync.forgetWifi()
                    pairingMessage = nil
                } label: {
                    Label("Stop syncing over Wi-Fi", systemImage: "wifi.slash")
                }
            } else {
                TextField("Computer, like 192.168.0.7:8765", text: $wifiAddressText)
                    .keyboardType(.URL)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                TextField("Pairing code, like K7QM-3XPA", text: $wifiCodeText)
                    .textInputAutocapitalization(.characters)
                    .autocorrectionDisabled()
                Button {
                    Task {
                        isPairing = true
                        pairingMessage = await sync.pairWifi(address: wifiAddressText, code: wifiCodeText)
                        isPairing = false
                    }
                } label: {
                    if isPairing {
                        ProgressView()
                    } else {
                        Label("Connect", systemImage: "wifi")
                    }
                }
                .disabled(isPairing || wifiAddressText.isEmpty || wifiCodeText.isEmpty)
            }
            if let pairingMessage {
                Text(pairingMessage)
                    .font(Theme.body(14))
                    .foregroundStyle(Theme.mutedInk)
            }
        } header: {
            Text("Sync over Wi-Fi")
        } footer: {
            Text(sync.isWifiSetUp
                 ? "When your iPhone is on the same Wi-Fi as your computer and Anki is open, Rev syncs with Anki directly in a second or two. Otherwise it uses the iCloud folder, as before."
                 : "Much faster than iCloud at home. In Anki on your computer, open Tools → Rev Sync Wi-Fi…, switch it on, and type the computer and pairing code it shows here. The first time, allow Rev to use the local network.")
        }
    }

    private var allowedImportTypes: [UTType] {
        switch importKind {
        case .backup: return [.json]
        case .anki: return [.apkg, .data]
        case .syncFolder: return [.folder]
        }
    }

    private var appVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.0"
    }

    private var dateStamp: String {
        Date().formatted(.iso8601.year().month().day())
    }

    // MARK: Export

    private func exportBackup() {
        do {
            let backup = try BackupService.makeBackup(context: context, newCardsPerDay: newCardsPerDay, userName: userName)
            present(try BackupService.encode(backup), type: .json, name: "Rev backup \(dateStamp)")
        } catch {
            message = "Backup failed: \(error.localizedDescription)"
        }
    }

    private func exportAllToAnki() {
        isWorking = true
        Task {
            do {
                let data = try await AnkiExport.package(decks)
                present(data, type: .apkg, name: "Rev decks \(dateStamp)")
            } catch {
                message = "Export failed: \(error.localizedDescription)"
            }
            isWorking = false
        }
    }

    private func present(_ data: Data, type: UTType, name: String) {
        exportDocument = ExportFile(data: data)
        exportType = type
        exportName = name
        isExporting = true
    }

    // MARK: Import

    private func handlePickedFile(_ result: Result<URL, any Error>) {
        switch result {
        case .failure(let error):
            message = error.localizedDescription
        case .success(let url):
            do {
                if importKind == .syncFolder {
                    // A folder isn't copied: Rev keeps permission to use it in place.
                    try sync.setFolder(url)
                    Task { await sync.sync(context: context) }
                    return
                }
                let localCopy = try FileHelpers.copyToTemporaryFolder(url)
                switch importKind {
                case .backup:
                    pendingBackup = try BackupService.decode(Data(contentsOf: localCopy))
                case .anki:
                    importAnki(from: localCopy)
                case .syncFolder:
                    break
                }
            } catch {
                message = "Couldn't open that file: \(error.localizedDescription)"
            }
        }
    }

    private func importAnki(from url: URL) {
        isWorking = true
        Task {
            do {
                let contents = try await Task.detached(priority: .userInitiated) {
                    try ApkgReader.read(fileAt: url)
                }.value
                let result = try BackupService.insert(contents.decks, into: context, mergeByName: true)
                var text = "Imported \(result.cards.counted("new card")) into \(contents.decks.count.counted("deck"))."
                if contents.imagesSaved > 0 { text += " Saved \(contents.imagesSaved.counted("image"))." }
                if result.updated > 0 { text += " Updated \(result.updated.counted("card")) you already had." }
                if result.skipped > 0 { text += " Skipped \(result.skipped) unchanged cards." }
                message = text
            } catch {
                message = error.localizedDescription
            }
            isWorking = false
        }
    }

    private func restore(_ backup: BackupFile) {
        do {
            try BackupService.restore(backup, into: context)
            if let limit = backup.newCardsPerDay { newCardsPerDay = limit }
            if let name = backup.userName { userName = name }
            message = "Backup restored."
        } catch {
            message = "Restore failed: \(error.localizedDescription)"
        }
    }
}
