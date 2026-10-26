import SwiftUI
import UniformTypeIdentifiers

extension UTType {
    /// Anki's deck package (.apkg). Declared in project.yml.
    static let apkg = UTType(importedAs: "net.ankiweb.apkg")
}

/// A file we hand to the iOS "Save to Files" screen (JSON backup or .apkg).
struct ExportFile: FileDocument {
    static var readableContentTypes: [UTType] { [.json, .apkg] }

    var data: Data

    init(data: Data) {
        self.data = data
    }

    init(configuration: ReadConfiguration) throws {
        data = configuration.file.regularFileContents ?? Data()
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        FileWrapper(regularFileWithContents: data)
    }
}
