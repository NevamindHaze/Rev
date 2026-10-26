import SwiftUI

/// A big, round, thumb-friendly button.
struct PillButtonStyle: ButtonStyle {
    var background: Color = Theme.ink
    var foreground: Color = Theme.background
    var height: CGFloat = 60

    func makeBody(configuration: Configuration) -> some View {
        PillBody(configuration: configuration, background: background, foreground: foreground, height: height)
    }

    private struct PillBody: View {
        let configuration: ButtonStyleConfiguration
        let background: Color
        let foreground: Color
        let height: CGFloat
        @Environment(\.isEnabled) private var isEnabled

        var body: some View {
            configuration.label
                .font(Theme.body(18, weight: .semibold))
                .frame(maxWidth: .infinity, minHeight: height)
                .foregroundStyle(foreground)
                .background(background, in: Capsule())
                .opacity(isEnabled ? 1 : 0.4)
                .scaleEffect(configuration.isPressed ? 0.97 : 1)
                .animation(.easeOut(duration: 0.15), value: configuration.isPressed)
        }
    }
}

/// Shrinks anything a little while you press it.
struct PressableButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.96 : 1)
            .animation(.easeOut(duration: 0.15), value: configuration.isPressed)
    }
}

/// A small number tile with an icon, like "12 · Due today".
struct StatTile: View {
    let value: String
    let label: String
    let icon: String
    let tint: Color

    var body: some View {
        VStack(spacing: 8) {
            Image(systemName: icon)
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(Theme.darkText)
                .frame(width: 36, height: 36)
                .background(tint, in: Circle())
            Text(value)
                .font(Theme.title(28))
                .foregroundStyle(Theme.ink)
                .lineLimit(1)
                .minimumScaleFactor(0.6)
            Text(label)
                .font(Theme.body(13))
                .foregroundStyle(Theme.mutedInk)
                .multilineTextAlignment(.center)
                .lineLimit(2)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 16)
        .padding(.horizontal, 8)
        .background(Theme.softFill, in: RoundedRectangle(cornerRadius: 24, style: .continuous))
    }
}

/// "3 coming back · next in 8 min": cards you pressed Again that return later today.
/// Shows nothing when there are none, and updates itself every 30 seconds.
struct ComingBackLabel: View {
    let cards: [Card]

    var body: some View {
        TimelineView(.periodic(from: .now, by: 30)) { context in
            if let text = cards.comingBackText(now: context.date) {
                Label(text, systemImage: "arrow.uturn.backward.circle.fill")
                    .font(Theme.body(14, weight: .semibold))
                    .foregroundStyle(Theme.mutedInk)
                    .frame(maxWidth: .infinity)
            }
        }
    }
}

/// A coloured dot with a count, used at the top of the review screen.
struct CountPill: View {
    let label: String
    let count: Int
    let color: Color

    var body: some View {
        HStack(spacing: 6) {
            Circle().fill(color).frame(width: 9, height: 9)
            Text("\(count) \(label)")
                .font(Theme.body(14, weight: .semibold))
                .foregroundStyle(Theme.ink)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .background(Theme.softFill, in: Capsule())
    }
}
