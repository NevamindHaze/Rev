import Foundation

/// The looks you can pick in Settings. Saved on this phone only; it never syncs.
enum AppTheme: String, CaseIterable, Identifiable {
    case original
    case signal

    static let storageKey = "appTheme"

    /// Anything but a known theme means Original.
    init(stored: String?) {
        self = stored.flatMap(AppTheme.init(rawValue:)) ?? .original
    }

    var id: String { rawValue }

    var title: String {
        switch self {
        case .original: "Original"
        case .signal: "Signal"
        }
    }
}
