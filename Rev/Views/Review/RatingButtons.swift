import SwiftUI

/// The four big answer buttons. Each one shows when you'd see the card again.
struct RatingButtons: View {
    let previews: [Rating: String]
    let onRate: (Rating) -> Void

    var body: some View {
        HStack(spacing: Theme.isSignal ? 4 : 10) {
            ForEach(Rating.allCases, id: \.self) { rating in
                Button {
                    onRate(rating)
                } label: {
                    VStack(spacing: 4) {
                        Text(rating.title)
                            .font(Theme.body(18, weight: .bold))
                            .lineLimit(1)
                            .minimumScaleFactor(0.7)
                        Text(previews[rating] ?? " ")
                            .font(Theme.body(13, weight: .medium))
                            .lineLimit(1)
                            .minimumScaleFactor(0.7)
                            .opacity(Theme.isSignal ? 1 : 0.85)   // Signal's red text needs its full strength
                    }
                    .frame(maxWidth: .infinity, minHeight: Theme.isSignal ? 56 : 76)
                    .foregroundStyle(rating.textColor)
                    .background(rating.color, in: RoundedRectangle(cornerRadius: Theme.radius(24, signal: 8), style: .continuous))
                }
                .buttonStyle(PressableButtonStyle())
            }
        }
    }
}

extension Rating {
    var color: Color {
        if Theme.isSignal { return self == .good ? Theme.teal : Theme.softFill }
        return switch self {
        case .again: Theme.coral
        case .hard: Theme.mustard
        case .good: Theme.teal
        case .easy: Theme.sage
        }
    }

    var textColor: Color {
        if Theme.isSignal {
            return switch self {
            case .again: Theme.coral
            case .hard: Theme.mustard
            case .good: Theme.onAccent
            case .easy: .white
            }
        }
        return switch self {
        case .again, .good: Theme.onAccent
        case .hard: Theme.darkText
        case .easy: Theme.ink
        }
    }
}
