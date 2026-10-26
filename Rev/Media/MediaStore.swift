import Foundation
import UIKit

/// Keeps card images in the app's own storage, in a folder called "Media".
/// Card text refers to an image with a marker like [img:heart.jpg].
enum MediaStore {
    static let imageExtensions: Set<String> = ["jpg", "jpeg", "png", "gif", "webp", "heic", "bmp", "tif", "tiff"]

    static var folder: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let url = base.appendingPathComponent("Media", isDirectory: true)
        try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    /// Only a plain file name is allowed (no folders), for safety.
    static func safeName(_ name: String) -> String? {
        let last = (name as NSString).lastPathComponent
        guard !last.isEmpty, last != ".", last != "..", !last.hasPrefix(".") else { return nil }
        return last
    }

    static func isImage(_ name: String) -> Bool {
        imageExtensions.contains((name as NSString).pathExtension.lowercased())
    }

    /// Saves an image file. Returns false if the name isn't usable.
    @discardableResult
    static func save(_ data: Data, named name: String) throws -> Bool {
        guard let name = safeName(name), isImage(name) else { return false }
        try data.write(to: folder.appendingPathComponent(name), options: .atomic)
        return true
    }

    /// Where an image is stored, if we have it.
    static func url(for name: String) -> URL? {
        guard let name = safeName(name) else { return nil }
        let url = folder.appendingPathComponent(name)
        return FileManager.default.fileExists(atPath: url.path) ? url : nil
    }

    private static let cache = NSCache<NSString, UIImage>()

    /// Loads an image for showing on a card (kept in memory after the first time).
    static func image(named name: String) -> UIImage? {
        if let cached = cache.object(forKey: name as NSString) { return cached }
        guard let url = url(for: name), let image = UIImage(contentsOfFile: url.path) else { return nil }
        cache.setObject(image, forKey: name as NSString)
        return image
    }
}
