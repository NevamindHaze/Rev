import SwiftUI
import SwiftData
import UniformTypeIdentifiers

/// Your name, the daily new-card limit, JSON backup/restore, and Anki import/export.
struct SettingsView: View {
    @Environment(\.modelContext) private var context
    @Query(sort: \Deck.createdAt) private var decks: [Deck]
    @AppStorage("userName") private var userName = ""
    @AppStorage("newCardsPerDay") private var newCardsPerDay = 20

    // Saving a file
    @State private var exportDocument: ExportFile?
    @State private var exportType: UTType = .json
    @State private var exportName = ""
    @State private var isExporting = false

    // Opening a file
    private enum ImportKind { case backup, anki }
    @State private var importKind: ImportKind = .backup
    @State private var isImporting = false
    @State private var pendingBackup: BackupFile?

    @State private var isWorking = false
    @State private var message: String?

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
                Text("Save a backup to the Files app often. If the app expires or gets deleted, reinstall it and restore your latest backup. Restoring replaces everything in the app.")
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
                Text("In Anki on your computer: File › Export › Anki Deck Package, and tick “Support older Anki versions”. Text and study progress are imported; images and sounds are skipped for now.")
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
                      allowedContentTypes: importKind == .backup ? [.json] : [.apkg, .data]) { result in
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
        .confirmationDialog("Replace everything with this backup?",
                            isPresented: Binding(get: { pendingBackup != nil }, set: { if !$0 { pendingBackup = nil } }),
                            titleVisibility: .visible,
                            presenting: pendingBackup) { backup in
            Button("Replace everything", role: .destructive) { restore(backup) }
        } message: { backup in
            Text("Backup from \(backup.exportedAt.formatted(date: .abbreviated, time: .shortened)): \(backup.decks.count) decks, \(backup.decks.reduce(0) { $0 + $1.cards.count }) cards.")
        }
        .alert("Rev", isPresented: Binding(get: { message != nil }, set: { if !$0 { message = nil } })) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(message ?? "")
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
                let localCopy = try FileHelpers.copyToTemporaryFolder(url)
                switch importKind {
                case .backup:
                    pendingBackup = try BackupService.decode(Data(contentsOf: localCopy))
                case .anki:
                    importAnki(from: localCopy)
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
                let importedDecks = try await Task.detached(priority: .userInitiated) {
                    try ApkgReader.read(fileAt: url)
                }.value
                let result = try BackupService.insert(importedDecks, into: context, mergeByName: true)
                var text = "Imported \(result.cards) cards into \(importedDecks.count) decks."
                if result.skipped > 0 { text += " Skipped \(result.skipped) cards you already had." }
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
