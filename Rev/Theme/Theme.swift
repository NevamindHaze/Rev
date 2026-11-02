import SwiftUI
import UIKit

/// Rev's look in one place: colours, corner sizes and fonts.
/// Warm and playful: cream background, coral, teal and mustard,
/// big rounded cards and bold rounded text. Dark mode is built in.
/// That's the Original theme. Signal, picked in Settings, is black and lime with sharper corners.
enum Theme {
    /// Read fresh each time. RootTabView redraws the whole app when this changes.
    static var current: AppTheme { AppTheme(stored: UserDefaults.standard.string(forKey: AppTheme.storageKey)) }
    static var isSignal: Bool { current == .signal }

    private static func pick(_ original: Color, signal: UInt32) -> Color {
        isSignal ? Color(uiColor: UIColor(hex: signal)) : original
    }

    // MARK: Colours (Original has a light-mode and a dark-mode version; Signal is always dark)
    // Names like `teal` say what a colour is for, not how it looks: in Signal, `teal` is lime.

    static var background: Color { pick(Color(light: 0xFBF6E3, dark: 0x181714), signal: 0x000000) }
    static var surface: Color    { pick(Color(light: 0xFFFDF4, dark: 0x24231F), signal: 0x181818) }   // cards
    static var softFill: Color   { pick(Color(light: 0xEEEFD8, dark: 0x302F29), signal: 0x1B1B1B) }   // small tiles
    static var ink: Color        { pick(Color(light: 0x1F1B16, dark: 0xF5F1E3), signal: 0xF6F5F2) }   // main text
    static var mutedInk: Color   { pick(Color(light: 0x6F695C, dark: 0xA8A393), signal: 0xA3A3A3) }   // secondary text

    static var coral: Color   { pick(Color(light: 0xE96F8A, dark: 0xE47C93), signal: 0xFF5C5C) }
    static var teal: Color    { pick(Color(light: 0x4A9886, dark: 0x58AA97), signal: 0xC8FF3D) }
    static var mustard: Color { pick(Color(light: 0xF4B53F, dark: 0xE8AE45), signal: 0xFFB647) }
    static var sage: Color    { pick(Color(light: 0xD3E4C8, dark: 0x3E5A4D), signal: 0x1B1B1B) }

    /// Always-dark text, for bright backgrounds like mustard.
    static let darkText = Color(light: 0x1F1B16, dark: 0x1F1B16)

    /// Text on a bright colour like `teal` or `coral`.
    static var onAccent: Color { isSignal ? .black : .white }

    // MARK: Shapes and spacing

    static var corner: CGFloat { isSignal ? 12 : 30 }
    static let padding: CGFloat = 20

    /// A corner size that's sharper in Signal. Original keeps each place's own size.
    static func radius(_ original: CGFloat, signal: CGFloat = 12) -> CGFloat {
        isSignal ? signal : original
    }

    static var pillShape: AnyShape {
        isSignal ? AnyShape(RoundedRectangle(cornerRadius: 10, style: .continuous)) : AnyShape(Capsule())
    }

    // MARK: Fonts

    static func title(_ size: CGFloat = 32) -> Font {
        .system(size: size, weight: isSignal ? .heavy : .bold, design: isSignal ? .default : .rounded)
    }

    static func body(_ size: CGFloat = 17, weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: isSignal ? .default : .rounded)
    }

    // MARK: Deck colours (they take turns down the list; only Original shows them)

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
