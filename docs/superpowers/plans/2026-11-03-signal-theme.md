# Signal Theme Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a switchable, dark Signal theme to Rev on iPhone, with today's look kept as Original.

**Architecture:** `Theme`'s tokens become computed and read the saved `AppTheme`, so most screens change with no edits. Decks and Review branch to Signal layouts for the pieces the spec names. `RootTabView` redraws the app when the theme changes. A Debug-only sample database and a UI test screenshot every main screen on GitHub's Macs.

**Tech Stack:** SwiftUI, SwiftData, XcodeGen, XCUITest, GitHub Actions. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-11-03-signal-theme-design.md`

## Global Constraints

- Deployment target stays iOS 18.0. iPhone only, portrait.
- No Swift compiler on the working machine. Every Swift check runs on GitHub's Macs, by pushing and reading the run: `gh run watch --exit-status $(gh run list --branch signal-theme --workflow "<name>" --limit 1 --json databaseId -q '.[0].databaseId')`.
- Original's values and layouts stay exactly as they are today.
- The setting is `UserDefaults` key `appTheme`. It never syncs. A missing or unknown value means Original.
- Signal values: background `#000000`, surface `#181818`, softFill `#1B1B1B`, ink `#F6F5F2`, mutedInk `#A3A3A3`, teal `#C8FF3D`, coral `#FF5C5C`, mustard `#FFB647`, sage `#1B1B1B`, corner `12`.
- All text on Signal's colors reaches 4.5:1. Tap targets stay at least 44 points.
- Windows and Linux code under `desktop/` isn't touched.
- Commits use the noreply identity and end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- One pull request. The user merges it.

## Review Focus

1. Phone in light mode, Signal switched back to Original: the app returns to light. `preferredColorScheme(nil)` can leave a window dark. Pinned by `testSwitchingThemeSticksAndGoesBack`.
2. A very long deck name and a four-digit due count: the name truncates, the count stays on one line, and the row stays one line. Pinned by the `standard` sample set.
3. No decks, and decks with nothing due: Signal's Decks screen still reads properly and Start review stays. Pinned by the `empty` and `nothingDue` sample sets.
4. A long answer, a picture card and a type-in card: the answer scrolls, and the 56-point rating buttons stay on screen and readable. Pinned by the `long`, `picture` and `typeIn` sample sets.
5. Very long rating previews, such as `2.1 mo`, on four narrow buttons: the text stays on one line. Pinned by `.lineLimit(1)` and `.minimumScaleFactor(0.7)` in Task 3, and by the answer screenshots.

---

### Task 1: Screenshot harness and Original baseline

Lands first, before any theme code, so the baseline shows today's app.

**Files:**
- Create: `Rev/App/ScreenshotMode.swift`
- Create: `Tests/RevScreenshots/ScreenshotTests.swift`
- Create: `.github/workflows/ios-screenshots.yml`
- Modify: `Rev/App/RevApp.swift:13-19`
- Modify: `project.yml:24-34`
- Modify: `.github/workflows/ios-build.yml` (after "Build the app (unsigned)")

**Interfaces:**
- Produces: launch argument `-screenshotData <set>`, where set is `standard`, `nothingDue`, `empty`, `picture`, `long` or `typeIn`.
- Produces: `ScreenshotTests.shoot(_ name: String)`, and the test helper `launch(data: String, theme: String?) -> XCUIApplication`. A non-nil theme adds `-appTheme <theme>`, which later tasks read through `UserDefaults`' argument domain, with no extra app code.
- Produces: artifact `screenshots`, containing `<name>.png`.

- [ ] **Step 1: Add `ScreenshotMode` inside `#if DEBUG`**

```swift
#if DEBUG
enum ScreenshotMode {
    static var dataSet: String? { UserDefaults.standard.string(forKey: "screenshotData") }
    static func makeContainer() throws -> ModelContainer   // isStoredInMemoryOnly: true, seeded from dataSet
}
#endif
```

Seed values, with review cards in `.review` phase, `due` in the past, and `intervalDays` 3:

| Set | Contents |
|---|---|
| `standard` | Decks `VPA` (12 due of 40), `Pharmacology` (1 due of 8), `Large Animal Surgery and Obstetrics for Final Year Revision` (1,250 due). 5 new cards in VPA. |
| `nothingDue` | Deck `VPA` with 10 review cards due in 3 days. |
| `empty` | Nothing. |
| `picture` | One deck. First card's front is `[img:sample.png]\nWhat is this?`. The PNG comes from `UIGraphicsImageRenderer` (400×300, solid fill) via `MediaStore.save(_:named:)`. |
| `long` | One deck. First card's back is 60 lines of text. |
| `typeIn` | One deck. First card has `kind = .typeIn`. |

- [ ] **Step 2: Use it in `RevApp.init`**

```swift
#if DEBUG
if ScreenshotMode.dataSet != nil { container = try! ScreenshotMode.makeContainer(); return }
#endif
```

This goes before the existing `do`. Real data is never opened in this mode.

- [ ] **Step 3: Add the UI test target in `project.yml`**

Add target `RevScreenshots`: `type: bundle.ui-testing`, `platform: iOS`, `sources: [Tests/RevScreenshots]`, `dependencies: [{target: Rev}]`, `settings.base.GENERATE_INFOPLIST_FILE: YES`. Change Rev's `scheme: {}` to `scheme: {testTargets: [RevScreenshots]}`.

- [ ] **Step 4: Write `ScreenshotTests`**

`shoot` writes `XCUIScreen.main.screenshot().pngRepresentation` to `$SCREENSHOT_DIR/<name>.png`, and `XCTAssertNoThrow`s the write. `setUp` sets `continueAfterFailure = false`.

`testCaptureAll` loops over `[("original-light", .light, nil), ("original-dark", .dark, nil)]`, setting `XCUIDevice.shared.appearance`. It captures these names with the given prefix, using sample set `standard` unless stated:

| Name | How |
|---|---|
| `decks` | launch |
| `decks-empty` | set `empty` |
| `decks-nothing-due` | set `nothingDue` |
| `review-question` | tap tab `Review` |
| `review-answer` | tap `Show answer` |
| `review-picture`, `review-long`, `review-typein` | matching set, tab `Review`, then answer shown |
| `deck-page` | tap `Pharmacology` on Decks |
| `stats` | tap tab `Stats` |
| `settings` | tap tab `Settings` |
| `card-editor` | tab `Review`, tap `Edit card` |

- [ ] **Step 5: Add `.github/workflows/ios-screenshots.yml`**

Triggers: `pull_request` and `workflow_dispatch`, with the same `paths-ignore` as `ios-build.yml`. Runs on `macos-latest`, timeout 45 minutes. Steps:

1. Checkout, `brew install xcodegen`, `xcodegen generate`.
2. Pick the first available iPhone: `xcrun simctl list devices available -j | jq -r '[.devices[][] | select(.name | startswith("iPhone"))][0].udid'`. Boot it, and run `xcrun simctl status_bar "$UDID" override --time 9:41`.
3. `TEST_RUNNER_SCREENSHOT_DIR="$PWD/screenshots" xcodebuild test -project Rev.xcodeproj -scheme Rev -destination "platform=iOS Simulator,id=$UDID" -parallel-testing-enabled NO CODE_SIGNING_ALLOWED=NO`, after `mkdir screenshots`.
4. `actions/upload-artifact@v4` with name `screenshots`, path `screenshots`, `retention-days: 30`, `if: always()`.

It publishes nothing.

- [ ] **Step 6: Guard the Release build in `ios-build.yml`**

After the build step, add a step that fails if the harness shipped:

```bash
! strings build/Build/Products/Release-iphoneos/Rev.app/Rev | grep -q screenshotData
```

- [ ] **Step 7: Commit, push and open a draft pull request**

Commit: `Screenshot Rev's main screens on GitHub's Macs`. Use the `gh pr create --draft` body to say it will carry the whole theme. Don't mark it ready.

- [ ] **Step 8: Check the runs**

Both `iOS Build` and `iOS Screenshots` must pass. Download the artifact with `gh run download <id> -n screenshots -D <scratchpad>/baseline`, then read every PNG. Each must show the screen its name says, with the sample data. Note the run ID: this is the baseline for Task 6.

---

### Task 2: Theme setting, tokens and switching

**Files:**
- Create: `Rev/Theme/AppTheme.swift`
- Create: `Tests/ThemeCheck/main.swift`
- Modify: `Rev/Theme/Theme.swift`
- Modify: `Rev/App/RootTabView.swift`
- Modify: `Rev/Views/Settings/SettingsView.swift:9-12,38-40`
- Modify: `.github/workflows/ios-build.yml` (after "Check the card text rules")
- Test: `Tests/RevScreenshots/ScreenshotTests.swift`

**Interfaces:**
- Consumes: `launch(data:theme:)` and `shoot(_:)` from Task 1.
- Produces: `AppTheme` with `init(stored: String?)`, `static let storageKey = "appTheme"`, `var title: String`, and `Theme.current: AppTheme`.
- Produces: `Theme.isSignal: Bool`, used by Tasks 3, 4 and 5.

- [ ] **Step 1: Write the failing check in `Tests/ThemeCheck/main.swift`**

```swift
precondition(AppTheme(stored: nil) == .original)
precondition(AppTheme(stored: "signal") == .signal)
precondition(AppTheme(stored: "original") == .original)
precondition(AppTheme(stored: "neon") == .original)
precondition(AppTheme(stored: "") == .original)
```

Add a CI step named "Check the theme rules", in the same form as "Check the card text rules", compiling `Rev/Theme/AppTheme.swift Tests/ThemeCheck/main.swift`.

- [ ] **Step 2: Push and watch it fail**

Expected: `iOS Build` fails at "Check the theme rules", because `AppTheme.swift` doesn't exist.

- [ ] **Step 3: Implement `AppTheme`**

Foundation only, so the check compiles without SwiftUI: `enum AppTheme: String, CaseIterable, Identifiable { case original, signal }`, titles `Original` and `Signal`.

- [ ] **Step 4: Make `Theme`'s tokens computed**

```swift
static var current: AppTheme { AppTheme(stored: UserDefaults.standard.string(forKey: AppTheme.storageKey)) }
static var isSignal: Bool { current == .signal }
private static func pick(_ original: Color, signal: UInt32) -> Color {
    isSignal ? Color(uiColor: UIColor(hex: signal)) : original
}
static var background: Color { pick(Color(light: 0xFBF6E3, dark: 0x181714), signal: 0x000000) }
```

Do the same for `surface`, `softFill`, `ink`, `mutedInk`, `coral`, `teal`, `mustard` and `sage`, keeping today's hex values and using Signal's from Global Constraints. `darkText` and `padding` stay as they are. `corner` becomes `isSignal ? 12 : 30`. `title` uses `.heavy` and `.default` in Signal, and `body` uses `.default`; Original keeps `.bold` and `.rounded`. Add a comment that `teal`, `coral` and the like name a role, and that Signal gives them other colors. `deckPalette` is only used by `DeckTile`, which Signal doesn't show, so it stays as is.

- [ ] **Step 5: Redraw from `RootTabView`**

Add `@AppStorage(AppTheme.storageKey) private var storedTheme = AppTheme.original.rawValue`. On the `TabView`, add `.id(storedTheme)` and `.preferredColorScheme(AppTheme(stored: storedTheme) == .signal ? .dark : nil)`.

- [ ] **Step 6: Add the picker to Settings**

`@AppStorage(AppTheme.storageKey) private var appTheme = AppTheme.original.rawValue`, plus this section directly after "You", with the same `Picker` form as `scheduler`:

```swift
Section("Appearance") {
    Picker("Theme", selection: $appTheme) { /* ForEach AppTheme.allCases, Text(title).tag(rawValue) */ }
}
```

- [ ] **Step 7: Add the switching test and Signal captures**

`testSwitchingThemeSticksAndGoesBack`, with the device appearance set to `.light`:

1. Launch `standard` with no theme.
2. Tab `Settings`, tap `Theme`, tap `Signal`.
3. Tap tab `Decks`, then `shoot("switch-signal")`.
4. Terminate and relaunch, with no theme. Tab `Settings`. Assert `app.buttons["Theme"].label` contains `Signal`.
5. Tap `Theme`, tap `Original`. Tap tab `Decks`, then `shoot("switch-back-original-light")`.
6. Terminate.

Add `("signal", .dark, "signal")` to the `testCaptureAll` loop.

- [ ] **Step 8: Push and check**

Expected: "Check the theme rules" passes, and both workflows pass. In the artifact, `switch-back-original-light.png` is light, and `original-*` match the baseline apart from sample data dates. If that screenshot is dark, set `overrideUserInterfaceStyle` on the key window from `RootTabView`'s `.onChange(of: storedTheme)` instead of passing `nil`, then re-run.

- [ ] **Step 9: Commit**

`Add the Signal theme setting and colors`. Push it with the check from Step 1 included.

---

### Task 3: Colors and shapes the tokens miss

**Files:**
- Modify: `Rev/Theme/Theme.swift`
- Modify: `Rev/Theme/Components.swift:25,70`
- Modify: `Rev/Views/Review/RatingButtons.swift`
- Modify: `Rev/Views/Review/ReviewView.swift:174,221`
- Modify: `Rev/Views/Decks/DeckDetailView.swift:50`
- Modify: `Rev/Views/Cards/BulkAddView.swift:31,34`
- Modify: `Rev/Views/Cards/CardEditorView.swift:65,151,180,225`
- Modify: `Rev/Views/Cards/CardBrowser.swift:162,259,282`
- Modify: `Rev/Views/Cards/CardTextView.swift:55`
- Modify: `Rev/Views/Settings/SettingsView.swift:118`
- Modify: `Rev/Views/Stats/StatsView.swift:25,72`

**Interfaces:**
- Consumes: `Theme.isSignal` from Task 2.
- Produces: `Theme.onAccent: Color`, `Theme.radius(_ original: CGFloat, signal: CGFloat = 12) -> CGFloat`, `Theme.pillShape: AnyShape`.

- [ ] **Step 1: Add the three tokens**

`onAccent` is `.white` in Original and `.black` in Signal. `pillShape` is `AnyShape(Capsule())` in Original and `AnyShape(RoundedRectangle(cornerRadius: 10, style: .continuous))` in Signal.

- [ ] **Step 2: Replace the hardcoded values**

| Site | Change |
|---|---|
| `ReviewView:221`, `DeckDetailView:50`, `BulkAddView:34`, `CardEditorView:65` | `foreground: Theme.onAccent` |
| `CardBrowser:162` | `active ? Theme.onAccent : Theme.ink` |
| `CardBrowser:282` | `dark ? Theme.darkText : Theme.onAccent` |
| `StatsView:72` | `.foregroundStyle(Theme.onAccent)` |
| `StatsView:25` | `.font(Theme.title(84))`, identical in Original |
| Every `cornerRadius: <n>` in the Files list | `cornerRadius: Theme.radius(<n>)` |
| `Components:25` | `.background(background, in: Theme.pillShape)` |

Leave `CardTextView:80,100,101` and `CardEditorView:239,241` alone: the spec keeps them fixed. Other `.system(size:)` uses are icons, and stay too.

- [ ] **Step 3: Style the rating buttons**

In `RatingButtons`: `HStack` spacing `Theme.isSignal ? 4 : 10`, `minHeight: Theme.isSignal ? 56 : 76`, `cornerRadius: Theme.radius(24, signal: 8)`. Add `.lineLimit(1).minimumScaleFactor(0.7)` to both `Text`s.

In `Rating`, Signal only: `color` is `Theme.teal` for good and `Theme.softFill` for the rest. `textColor` is `Theme.coral` for again, `Theme.mustard` for hard, `Theme.onAccent` for good and `.white` for easy. Original's branches keep today's values, with `Theme.onAccent` in place of `.white`.

- [ ] **Step 4: Push and check**

Both workflows pass. In the artifact, all `signal-*` screens have readable text, with no white on lime and no text on a matching background. All `original-*` match the baseline.

- [ ] **Step 5: Commit**

`Make buttons, badges and corners follow the theme`.

---

### Task 4: Signal Decks screen

**Files:**
- Create: `Rev/Views/Decks/SignalHomeContent.swift`
- Modify: `Rev/Views/Decks/HomeView.swift:20-54`

**Interfaces:**
- Consumes: `Theme.isSignal`, `Theme.onAccent`, `PillButtonStyle` and `ComingBackLabel`; `toReviewCount(now:)` and `newCount` on `[Card]`.
- Produces:

```swift
struct SignalHomeContent<Empty: View>: View {
    let decks: [Deck]
    let allCards: [Card]
    let reviewedToday: Int
    let onRename: (Deck) -> Void
    let onDelete: (Deck) -> Void
    let empty: Empty
}
```

- [ ] **Step 1: Write `SignalHomeContent`**

Top to bottom, in a `VStack(alignment: .leading, spacing: 12)`:

1. Date line: 11 points, bold, tracking 1, `Theme.teal`, built as `"\(weekday) · \(day) \(month)"` and uppercased, such as `TUESDAY · 3 NOV`.
2. Inside `TimelineView(.periodic(from: .now, by: 30))`, an `HStack(alignment: .firstTextBaseline, spacing: 10)`:
   - The due count: `.system(size: 56, weight: .heavy)`, `Theme.ink`, `.lineLimit(1)`, `.minimumScaleFactor(0.5)`.
   - The caption: `Theme.body(14, weight: .medium)`, `Theme.mutedInk`, `.lineLimit(2)`. Text is `card due` or `cards due` by count, then ` · N done`, then ` · N new` when above zero.
3. `ComingBackLabel(cards: allCards)`.
4. `NavigationLink { ReviewView(deck: nil) } label: { Text("Start review →") }` with `PillButtonStyle(background: Theme.teal, foreground: Theme.onAccent, height: 44)`.
5. `empty` when `decks` is empty. Otherwise a `ForEach` of rows in a `VStack(spacing: 0)`.

Each row is a `NavigationLink(value: deck)` with `PressableButtonStyle` and the same `contextMenu` labels as today, calling `onRename` and `onDelete`:

- `HStack` of the name (`Theme.body(16, weight: .semibold)`, `Theme.ink`, `.lineLimit(1)`), a `Spacer`, the due count (`.system(size: 17, weight: .heavy)`, `Theme.ink`), and `/ total` (`Theme.body(12)`, `Theme.mutedInk`). The count text gets `.fixedSize()`, and the due count refreshes in a 30-second `TimelineView`.
- `.frame(minHeight: 44)`, `.contentShape(Rectangle())`.
- A 1-point divider above each row, `Theme.mutedInk.opacity(0.25)`.

- [ ] **Step 2: Branch in `HomeView`**

Inside the `ScrollView`, `if Theme.isSignal { SignalHomeContent(…).padding(.horizontal, Theme.padding).padding(.vertical, 12) } else { existing VStack unchanged }`. `onRename` sets `renameText` and `deckToRename`, `onDelete` sets `deckToDelete`, and `empty: emptyState`.

- [ ] **Step 3: Push and check**

In the artifact:
- `signal-decks.png` matches the compact layout, with the long name truncated and `1250` on one line.
- `signal-decks-empty.png` and `signal-decks-nothing-due.png` read properly.
- `original-decks.png` matches the baseline.

- [ ] **Step 4: Commit**

`Add Signal's compact Decks screen`.

---

### Task 5: Signal Review screen

**Files:**
- Create: `Rev/Utilities/ReviewProgress.swift`
- Modify: `Tests/ThemeCheck/main.swift`
- Modify: `Rev/Views/Review/ReviewView.swift:55,58,116-152`
- Modify: `.github/workflows/ios-build.yml` ("Check the theme rules")

**Interfaces:**
- Consumes: `Theme.isSignal`, `queue.newCards`, `queue.learningCount`, `queue.reviewDue`, `reviewedThisSession`, `typeInBox(for:)`, `reveal()`.
- Produces: `ReviewProgress.segments(reviewed: Int, left: Int, maxSegments: Int = 30) -> (filled: Int, total: Int)`.

- [ ] **Step 1: Add failing checks**

```swift
precondition(ReviewProgress.segments(reviewed: 0, left: 10) == (0, 10))
precondition(ReviewProgress.segments(reviewed: 3, left: 7) == (3, 10))
precondition(ReviewProgress.segments(reviewed: 10, left: 0) == (10, 10))
precondition(ReviewProgress.segments(reviewed: 0, left: 0) == (0, 1))
precondition(ReviewProgress.segments(reviewed: 0, left: 1250) == (0, 30))
precondition(ReviewProgress.segments(reviewed: 1249, left: 1) == (29, 30))   // full only when the queue is empty
precondition(ReviewProgress.segments(reviewed: 1250, left: 0) == (30, 30))
```

Add `Rev/Utilities/ReviewProgress.swift` to the step's `swiftc` line.

- [ ] **Step 2: Push and watch it fail**

Expected: "Check the theme rules" fails, because `ReviewProgress.swift` doesn't exist.

- [ ] **Step 3: Implement `ReviewProgress.segments`**

`total` is `max(1, min(reviewed + left, maxSegments))`. `filled` rounds `reviewed * total / (reviewed + left)` down, and is 0 when both are 0. Foundation only.

- [ ] **Step 4: Add `signalCounts` and `signalCardFace(_:)` to `ReviewView`**

In `body`, branch: `Theme.isSignal ? signalCounts : counts`, and the same for `cardFace`.

`signalCounts`, in a `VStack(alignment: .leading, spacing: 6)`:
- An `HStack(spacing: 2)` of `total` capsules, 3 points tall: `Theme.teal` when filled, `Theme.softFill` otherwise. `left` is `queue.newCards.count + queue.learningCount + queue.reviewDue.count`.
- `"\(new) new · \(learning) learning · \(review) review"`, in `Theme.body(12, weight: .medium)`, `Theme.mutedInk`.

`signalCardFace(_:)`, a `ScrollView` with no background:
- A kicker: `(custom?.title ?? deck?.name ?? "All decks").uppercased()`, `Theme.body(11, weight: .bold)`, tracking 1, `Theme.teal`, `.lineLimit(1)`.
- `CardTextView(text: card.front, size: 27, weight: .heavy)`.
- `typeInBox(for: card)` for type-in cards.
- When shown, a 1-point `Theme.mutedInk.opacity(0.25)` divider and `CardTextView(text: card.back, size: 19)`.
- `.frame(maxHeight: .infinity)`, `.contentShape(Rectangle())`, `.onTapGesture(perform: reveal)`.

- [ ] **Step 5: Push and check**

"Check the theme rules" passes. In the artifact:
- `signal-review-question.png` and `signal-review-answer.png` match the mockup, with a lime Good button.
- `signal-review-long.png` shows the rating buttons on screen below the long answer.
- `signal-review-picture.png` and `signal-review-typein.png` read properly.
- `original-review-*` match the baseline.

- [ ] **Step 6: Commit**

`Add Signal's Review screen`.

---

### Task 6: Compare, release notes and ready for merge

**Files:**
- Modify: `RELEASE_NOTES.md`

- [ ] **Step 1: Write the release notes**

Replace the bullet with:

`• New look to try: Signal, a dark theme with big numbers and lime accents. Turn it on in Settings → Appearance. The app stays as it is until you do, and you can switch back any time.`

- [ ] **Step 2: Compare Original against the baseline**

Download the final artifact beside `baseline`. For each `original-*.png` present in both, use `PIL.ImageChops.difference(a, b).getbbox()` with Python run as `python3 -I`, and list those that aren't `None`. Read each listed pair. The only accepted differences are the date line and rating previews, because `fuzzSeed` varies between runs. Fix anything else.

- [ ] **Step 3: Review every Signal screen once more**

Read every `signal-*` PNG against the spec's Decks, Review and Other screens sections.

- [ ] **Step 4: Commit, push and mark ready**

Commit `Say what's new: Signal`. Update the PR body via `gh api -X PATCH repos/NevamindHaze/Rev/pulls/<n> --input <file>.json`, ending with the Claude Code attribution line. Run `gh pr ready`, then confirm `iOS Build` and `iOS Screenshots` pass. Bind the PR with the `ccd_pr` tools. Tell the user what to try on the phone after merging: switch theme, study a few cards, switch back.
