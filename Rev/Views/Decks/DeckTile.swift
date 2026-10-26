import SwiftUI

/// One deck on the home screen: a big coloured card with its name and counts.
struct DeckTile: View {
    let deck: Deck
    let palette: Theme.DeckPalette

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(alignment: .top) {
                Text(deck.name)
                    .font(Theme.title(24))
                    .multilineTextAlignment(.leading)
                Spacer()
                Image(systemName: "arrow.up.right")
                    .font(.system(size: 16, weight: .bold))
                    .frame(width: 42, height: 42)
                    .background(palette.foreground.opacity(0.15), in: Circle())
            }
            HStack(spacing: 8) {
                chip("\(deck.cards.count) cards")
                chip("\(deck.cards.dueCount()) due")
                chip("\(deck.cards.newCount) new")
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
