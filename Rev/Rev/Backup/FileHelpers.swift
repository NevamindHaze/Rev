import Foundation

enum FileHelpers {
    /// Files picked in the Files app are only readable for a moment,
    /// so we copy them into the app's own temporary folder first.
    static func copyToTemporaryFolder(_ url: URL) throws -> URL {
        let hasAccess = url.startAccessingSecurityScopedResource()
        defer { if hasAccess { url.stopAccessingSecurityScopedResource() } }

        let folder = try makeTemporaryFolder()
        let copy = folder.appendingPathComponent(url.lastPathComponent)
        try FileManager.default.copyItem(at: url, to: copy)
        return copy
    }

    /// A new, empty folder inside the app's temporary storage.
    static func makeTemporaryFolder() throws -> URL {
        let folder = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        return folder
    }
}
