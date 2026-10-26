import SwiftUI
import SwiftData

/// The starting point of the app.
/// `.modelContainer` switches on SwiftData, which saves decks, cards and
/// review history on the phone.
@main
struct RevApp: App {
    var body: some Scene {
        WindowGroup {
            RootTabView()
        }
        .modelContainer(for: [Deck.self, Card.self, ReviewLog.self])
    }
}
