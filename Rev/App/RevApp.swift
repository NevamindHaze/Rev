import SwiftUI
import SwiftData
import UIKit

/// The starting point of the app.
/// The model container is SwiftData, which saves decks, cards and review history on the phone.
/// Rev also syncs with Anki (if set up in Settings) whenever you open it and whenever you leave it.
@main
struct RevApp: App {
    private let container: ModelContainer
    @Environment(\.scenePhase) private var scenePhase

    init() {
        do {
            container = try ModelContainer(for: Deck.self, Card.self, ReviewLog.self)
        } catch {
            fatalError("Couldn't open Rev's database: \(error)")
        }
    }

    var body: some Scene {
        WindowGroup {
            RootTabView()
        }
        .modelContainer(container)
        .onChange(of: scenePhase) { _, phase in
            guard phase == .active || phase == .background else { return }
            // Ask iOS for a few extra seconds, so a sync started as you leave can finish.
            let task = UIApplication.shared.beginBackgroundTask(withName: "Rev sync")
            Task { @MainActor in
                await SyncService.shared.sync(context: container.mainContext)
                UIApplication.shared.endBackgroundTask(task)
            }
        }
    }
}
