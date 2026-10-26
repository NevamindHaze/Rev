import SwiftUI
import UIKit

/// Shows a card's text nicely: list items with wrapped lines lined up under the text
/// (not under the bullet), and images that you can tap to zoom.
struct CardTextView: View {
    let text: String
    var size: CGFloat = 22
    var weight: Font.Weight = .regular

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(Array(CardText.lines(text).enumerated()), id: \.offset) { _, line in
                switch line {
                case .text(let string):
                    Text(string)
                case .listItem(let marker, let string):
                    HStack(alignment: .firstTextBaseline, spacing: 8) {
                        Text(marker).monospacedDigit()
                        Text(string)
                    }
                case .image(let name):
                    CardImageView(name: name)
                case .blank:
                    Color.clear.frame(height: size * 0.4)
                }
            }
        }
        .font(Theme.body(size, weight: weight))
        .foregroundStyle(Theme.ink)
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// One image on a card. Tap it to see it full screen.
struct CardImageView: View {
    let name: String
    @State private var isZoomed = false

    var body: some View {
        if let image = MediaStore.image(named: name) {
            Image(uiImage: image)
                .resizable()
                .scaledToFit()
                .frame(maxWidth: .infinity, maxHeight: 380)
                .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
                .onTapGesture { isZoomed = true }
                .fullScreenCover(isPresented: $isZoomed) {
                    ZoomableImageView(image: image)
                }
        } else {
            Label("Image not found: \(name)", systemImage: "photo")
                .font(Theme.body(14))
                .foregroundStyle(Theme.mutedInk)
        }
    }
}

/// Full-screen image: pinch to zoom, drag to move, double-tap to reset, ✕ to close.
struct ZoomableImageView: View {
    let image: UIImage

    @Environment(\.dismiss) private var dismiss
    @State private var scale: CGFloat = 1
    @State private var lastScale: CGFloat = 1
    @State private var offset: CGSize = .zero
    @State private var lastOffset: CGSize = .zero

    var body: some View {
        ZStack(alignment: .topTrailing) {
            Color.black.ignoresSafeArea()

            Image(uiImage: image)
                .resizable()
                .scaledToFit()
                .scaleEffect(scale)
                .offset(offset)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .gesture(zoomAndMove)
                .onTapGesture(count: 2) {
                    withAnimation(.snappy) {
                        scale = 1
                        lastScale = 1
                        offset = .zero
                        lastOffset = .zero
                    }
                }

            Button { dismiss() } label: {
                Image(systemName: "xmark.circle.fill")
                    .font(.system(size: 34))
                    .foregroundStyle(.white.opacity(0.85))
            }
            .padding(20)
        }
    }

    private var zoomAndMove: some Gesture {
        let zoom = MagnifyGesture()
            .onChanged { value in scale = max(1, lastScale * value.magnification) }
            .onEnded { _ in lastScale = scale }
        let move = DragGesture()
            .onChanged { value in
                offset = CGSize(width: lastOffset.width + value.translation.width,
                                height: lastOffset.height + value.translation.height)
            }
            .onEnded { _ in lastOffset = offset }
        return zoom.simultaneously(with: move)
    }
}
