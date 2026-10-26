import SwiftUI
import SwiftData
import Charts

/// Your progress: cards due, reviews today, streak, and a 7-day chart.
struct StatsView: View {
    @Environment(\.modelContext) private var context
    @Query private var cards: [Card]

    @State private var reviewsToday = 0
    @State private var streak = 0
    @State private var lastWeek: [DayCount] = []

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 24) {
                Text("Your progress")
                    .font(Theme.title(34))
                    .foregroundStyle(Theme.ink)

                HStack(alignment: .center) {
                    VStack(alignment: .leading, spacing: 2) {
                        Text("\(cards.dueCount())")
                            .font(.system(size: 84, weight: .bold, design: .rounded))
                            .foregroundStyle(Theme.ink)
                        Text("cards due today")
                            .font(Theme.body(17, weight: .medium))
                            .foregroundStyle(Theme.mutedInk)
                    }
                    Spacer()
                    streakBadge
                }

                HStack(spacing: 12) {
                    StatTile(value: "\(reviewsToday)", label: "Reviewed today", icon: "checkmark", tint: Theme.teal)
                    StatTile(value: "\(cards.newCount)", label: "New cards", icon: "sparkles", tint: Theme.coral)
                    StatTile(value: "\(cards.count)", label: "Total cards", icon: "square.stack.fill", tint: Theme.mustard)
                }

                weekChart
            }
            .padding(Theme.padding)
        }
        .background(Theme.background.ignoresSafeArea())
        // Pull down to sync with Anki (so Anki's reviews count too) and recalculate.
        .refreshable {
            await SyncService.shared.sync(context: context)
            refresh()
        }
        .navigationTitle("Stats")
        .navigationBarTitleDisplayMode(.inline)
        .onAppear(perform: refresh)
    }

    private var streakBadge: some View {
        VStack(spacing: 2) {
            Image(systemName: "flame.fill")
                .font(.system(size: 24))
            Text("\(streak)")
                .font(Theme.title(30))
            Text(streak == 1 ? "day" : "days")
                .font(Theme.body(13, weight: .semibold))
        }
        .foregroundStyle(.white)
        .frame(width: 112, height: 112)
        .background(Theme.coral, in: Circle())
    }

    private var weekChart: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Last 7 days")
                .font(Theme.title(20))
                .foregroundStyle(Theme.ink)
            Chart(lastWeek) { day in
                BarMark(x: .value("Day", day.label), y: .value("Reviews", day.count))
                    .foregroundStyle(day.isToday ? Theme.coral : Theme.teal)
                    .cornerRadius(8)
            }
            .frame(height: 180)
        }
        .padding(20)
        .background(Theme.surface, in: RoundedRectangle(cornerRadius: Theme.corner, style: .continuous))
    }

    private func refresh() {
        reviewsToday = StatsCalculator.reviewsToday(in: context)
        streak = StatsCalculator.streak(in: context)
        lastWeek = StatsCalculator.lastDays(7, in: context)
    }
}
