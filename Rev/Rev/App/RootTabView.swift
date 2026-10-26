import SwiftUI

/// The four tabs at the bottom of the screen.
/// Each tab has its own NavigationStack so it remembers where you were.
struct RootTabView: View {
    var body: some View {
        TabView {
            NavigationStack { HomeView() }
                .tabItem { Label("Decks", systemImage: "square.stack.fill") }

            NavigationStack { ReviewView(deck: nil) }
                .tabItem { Label("Review", systemImage: "rectangle.on.rectangle.angled") }

            NavigationStack { StatsView() }
                .tabItem { Label("Stats", systemImage: "chart.bar.fill") }

            NavigationStack { SettingsView() }
                .tabItem { Label("Settings", systemImage: "gearshape.fill") }
        }
        .tint(Theme.teal)
    }
}
