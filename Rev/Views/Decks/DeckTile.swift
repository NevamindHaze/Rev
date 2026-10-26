import SwiftUI

/// One deck on the home screen: a big coloured card with its name and counts.
struct DeckTile: View {
    let deck: Deck
    let palette: Theme.DeckPalette

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(deck.name)
                        .font(Theme.title(24))
                        .multilineTextAlignment(.leading)
                    Text(deck.cards.count.counted("card"))
                        .font(Theme.body(14, weight: .medium))
                        .opacity(0.8)
                }
                Spacer()
                Image(systemName: "arrow.up.right")
                    .font(.system(size: 16, weight: .bold))
                    .frame(width: 42, height: 42)
                    .background(palette.foreground.opacity(0.15), in: Circle())
            }
            // Re-checked every 30 seconds, so cards coming back move between the chips by themselves.
            TimelineView(.periodic(from: .now, by: 30)) { context in
                HStack(spacing: 8) {
                    chip("\(deck.cards.toReviewCount(now: context.date)) to review")
                    let later = deck.cards.comingBack(now: context.date).count
                    if later > 0 { chip("↻ \(later) later") }
                    chip("\(deck.cards.newCount) new")
                }
            }
        }
        .foregroundStyle(palette.foreground)
        .padding(22)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(palette.background, in: RoundedRectangle(cornerRadius: Theme.corner, style: .continuous))
    }

    private func chip(_ text: String) -> some View {
        Text(text)
            .font(Theme.body(14, weight: .semibold))
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(palette.foreground.opacity(0.16), in: Capsule())
    }
}
