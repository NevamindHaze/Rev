import Foundation

extension Int {
    /// "1 card", "2 cards", "0 cards".
    func counted(_ singular: String, plural: String? = nil) -> String {
        "\(self) \(self == 1 ? singular : (plural ?? singular + "s"))"
    }
}
