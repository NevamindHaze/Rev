import SwiftUI

/// The four big answer buttons. Each one shows when you'd see the card again.
struct RatingButtons: View {
    let previews: [Rating: String]
    let onRate: (Rating) -> Void

    var body: some View {
        HStack(spacing: 10) {
            ForEach(Rating.allCases, id: \.self) { rating in
                Button {
                    onRate(rating)
                } label: {
                    VStack(spacing: 4) {
                        Text(rating.title)
                            .font(Theme.body(18, weight: .bold))
                        Text(previews[rating] ?? " ")
                            .font(Theme.body(13, weight: .medium))
                            .opacity(0.85)
                    }
                    .frame(maxWidth: .infinity, minHeight: 76)
                    .foregroundStyle(rating.textColor)
                    .background(rating.color, in: RoundedRectangle(cornerRadius: 24, style: .continuous))
                }
                .buttonStyle(PressableButtonStyle())
            }
        }
    }
}

extension Rating {
    var color: Color {
        switch self {
        case .again: Theme.coral
        case .hard: Theme.mustard
        case .good: Theme.teal
        case .easy: Theme.sage
        }
    }

    var textColor: Color {
        switch self {
        case .again, .good: .white
        case .hard: Theme.darkText
        case .easy: Theme.ink
        }
    }
}
