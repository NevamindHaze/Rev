import SwiftUI
import UIKit

/// Rev's look in one place: colours, corner sizes and fonts.
/// Warm and playful: cream background, coral, teal and mustard,
/// big rounded cards and bold rounded text. Dark mode is built in.
enum Theme {
    // MARK: Colours (each has a light-mode and a dark-mode version)

    static let background = Color(light: 0xFBF6E3, dark: 0x181714)
    static let surface    = Color(light: 0xFFFDF4, dark: 0x24231F)   // cards
    static let softFill   = Color(light: 0xEEEFD8, dark: 0x302F29)   // small tiles
    static let ink        = Color(light: 0x1F1B16, dark: 0xF5F1E3)   // main text
    static let mutedInk   = Color(light: 0x6F695C, dark: 0xA8A393)   // secondary text

    static let coral   = Color(light: 0xE96F8A, dark: 0xE47C93)
    static let teal    = Color(light: 0x4A9886, dark: 0x58AA97)
    static let mustard = Color(light: 0xF4B53F, dark: 0xE8AE45)
    static let sage    = Color(light: 0xD3E4C8, dark: 0x3E5A4D)

    /// Always-dark text, for bright backgrounds like mustard.
    static let darkText = Color(light: 0x1F1B16, dark: 0x1F1B16)

    // MARK: Shapes and spacing

    static let corner: CGFloat = 30
    static let padding: CGFloat = 20

    // MARK: Fonts

    static func title(_ size: CGFloat = 32) -> Font {
        .system(size: size, weight: .bold, design: .rounded)
    }

    static func body(_ size: CGFloat = 17, weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: .rounded)
    }

    // MARK: Deck colours (they take turns down the list)

    struct DeckPalette {
        let background: Color
        let foreground: Color
    }

    static func deckPalette(_ index: Int) -> DeckPalette {
        let palettes = [
            DeckPalette(background: coral, foreground: .white),
            DeckPalette(background: teal, foreground: .white),
            DeckPalette(background: mustard, foreground: darkText),
            DeckPalette(background: sage, foreground: ink),
        ]
        return palettes[index % palettes.count]
    }
}

extension Color {
    /// A colour that switches automatically between light and dark mode.
    /// Colours are written as hex numbers, e.g. 0xE96F8A.
    init(light: UInt32, dark: UInt32) {
        self.init(uiColor: UIColor { traits in
            UIColor(hex: traits.userInterfaceStyle == .dark ? dark : light)
        })
    }
}

extension UIColor {
    convenience init(hex: UInt32) {
        self.init(red: CGFloat((hex >> 16) & 0xFF) / 255,
                  green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255,
                  alpha: 1)
    }
}
