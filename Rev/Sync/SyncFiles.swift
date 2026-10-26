import Foundation

/// Reading and writing files in the shared iCloud Drive folder.
/// iCloud files may not be downloaded on the phone yet, so every access goes through
/// NSFileCoordinator, which asks iCloud to fetch the latest version first.
enum SyncFiles {
    struct FileError: LocalizedError {
        let message: String
        var errorDescription: String? { message }
    }

    /// The file's contents, or nil if it doesn't exist yet.
    static func read(_ url: URL) throws -> Data? {
        try? FileManager.default.startDownloadingUbiquitousItem(at: url)
        var coordinationError: NSError?
        var result: Data?
        var readError: Error?
        NSFileCoordinator().coordinate(readingItemAt: url, options: [], error: &coordinationError) { readURL in
            guard FileManager.default.fileExists(atPath: readURL.path) else { return }
            do { result = try Data(contentsOf: readURL) } catch { readError = error }
        }
        if let coordinationError {
            // A missing file isn't an error: the other app just hasn't written it yet.
            if coordinationError.domain == NSCocoaErrorDomain, coordinationError.code == NSFileReadNoSuchFileError {
                return nil
            }
            throw coordinationError
        }
        if let readError { throw readError }
        return result
    }

    static func write(_ data: Data, to url: URL) throws {
        var coordinationError: NSError?
        var writeError: Error?
        NSFileCoordinator().coordinate(writingItemAt: url, options: .forReplacing, error: &coordinationError) { writeURL in
            do { try data.write(to: writeURL, options: .atomic) } catch { writeError = error }
        }
        if let coordinationError { throw coordinationError }
        if let writeError { throw writeError }
    }

    /// Copies images the cards need from the shared folder into Rev, if Rev doesn't have them.
    static func copyImagesIn(_ names: Set<String>, from mediaFolder: URL) {
        for name in names where MediaStore.url(for: name) == nil {
            guard let safe = MediaStore.safeName(name),
                  let data = try? read(mediaFolder.appendingPathComponent(safe)) else { continue }
            try? MediaStore.save(data, named: safe)
        }
    }

    /// Copies Rev's images into the shared folder, so Anki can use them.
    static func copyImagesOut(_ names: Set<String>, to mediaFolder: URL) {
        try? FileManager.default.createDirectory(at: mediaFolder, withIntermediateDirectories: true)
        for name in names {
            guard let source = MediaStore.url(for: name), let safe = MediaStore.safeName(name) else { continue }
            let target = mediaFolder.appendingPathComponent(safe)
            if FileManager.default.fileExists(atPath: target.path) { continue }
            if let data = try? Data(contentsOf: source) { try? write(data, to: target) }
        }
    }
}
