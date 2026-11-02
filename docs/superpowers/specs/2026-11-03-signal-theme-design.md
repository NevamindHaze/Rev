# Signal theme for Rev on iPhone

## Goal

Add **Signal**, a dark, bold, custom look, as a theme you can switch to inside the iPhone app. The current look stays as **Original**, so you can go back at any time.

Success means:

- The Decks and Review screens match the approved Signal mockup, apart from the differences listed here.
- Every other screen uses Signal's colors and fonts, with text readable everywhere.
- Original looks the same as it does today.
- Switching theme never changes cards, schedules, review history or sync.

## Out of scope

- The Windows and Linux apps.
- A third theme, a theme editor or custom accent colors.
- A light version of Signal.
- New features, or changes to study logic and sync.
- A custom tab bar.

## Switching themes

- **Settings → Appearance** gets a new section with a picker: Original or Signal.
- The choice is saved on the phone under the `UserDefaults` key `appTheme`. It never syncs. A missing or unknown value means Original, so everyone starts on Original after updating.
- The change applies straight away. `RootTabView` reads the setting and sets `.id(theme)` on the tab view, which redraws the whole app. This may return you to the Decks tab, which is accepted.
- The redraw closes any study session left open on the Review tab. Answers already given stay saved, and reopening Review carries on with the cards still due.
- Signal is always dark: `.preferredColorScheme(.dark)` while it's active. Original passes `nil` and keeps following the phone's setting.

## How the theme is built

The tokens in `Rev/Theme/Theme.swift` are already used by every screen, and none are read outside a view. So one small change covers most of the app:

- Add `enum AppTheme: String { case original, signal }` and `Theme.current`, which reads the saved setting.
- Change `Theme`'s `static let` colors, fonts and corner sizes into computed `static var`s that return the value for `Theme.current`.
- Original's values stay exactly as they are today.
- Token names such as `teal` and `coral` stay, to keep the change small. A comment in `Theme.swift` explains that they name a role, and that Signal gives them different colors.

### Signal's values

| Token | Signal |
|---|---|
| `background` | `#000000` |
| `surface` | `#181818` |
| `softFill` | `#1B1B1B` |
| `ink` | `#F6F5F2` |
| `mutedInk` | `#A3A3A3` |
| `teal` (main accent) | `#C8FF3D` lime |
| `coral` (Again, warnings) | `#FF5C5C` |
| `mustard` (Hard, learning) | `#FFB647` |
| `sage` (Easy) | `#1B1B1B` |
| `corner` | `12` |
| `title` font | system default design, weight heavy |
| `body` font | system default design, same weights as requested |

Original's rounded fonts stay rounded.

### New tokens for things the current tokens miss

- `Theme.onTeal`: text on the main accent. White in Original, black in Signal, because white on lime is unreadable. It replaces the hardcoded white wherever it sits on `Theme.teal`: `PillButtonStyle` uses at `ReviewView:221`, `DeckDetailView:50`, `BulkAddView:34` and `CardEditorView:65`, plus `CardBrowser:162`, `CardBrowser:282` and `StatsView:72`. Each is checked against what's actually behind it, because not all may be on teal.
- `Theme.smallCorner`, `Theme.tileCorner`: replace hardcoded corner sizes in `RatingButtons`, `ReviewView`, `SettingsView`, `CardBrowser`, `CardEditorView`, `BulkAddView`, `CardTextView` and `Components`. In Original they return the sizes used today, per place. If a place needs its own size, it keeps a per-theme value rather than losing its current size.
- `Rating.color` and `Rating.textColor` in `RatingButtons.swift` return Signal's values when it's active. See the Review section.

Colors meant to stay fixed are left alone: the full-screen image viewer's black background and white close button, and the picture ✕ in the card editor.

The tab bar's tint comes from `Theme.teal`, so it turns lime on its own.

## Decks screen in Signal

`HomeView` keeps its data, toolbar, alerts, long-press menu and pull to sync. Only the content inside its `ScrollView` branches on the theme. Signal's layout goes in a new file, `Rev/Views/Decks/SignalHomeContent.swift`.

The screen is compact, so more decks fit without scrolling. From top to bottom:

1. **Date line:** small lime capitals, such as `TUESDAY · 3 NOV`. It replaces the greeting.
2. **Due count and caption, on one line:** the same to-review count as Original, updated by the same 30-second timer, at about 56 points and heavy, scaling down if it doesn't fit. Beside it, `cards due · 7 done`, plus the new-card count when above zero.
3. **"Coming back" label:** the existing `ComingBackLabel`, shown only when it has something to say.
4. **Start review button:** lime with black text, 44 points tall, opening `ReviewView(deck: nil)` as today. It shows at zero due too, as Original's button does.
5. **Deck rows:** one line each and 44 points tall. Deck name at about 16 points on the left, due count at about 17 points and heavy on the right, with the total small beside it. A thin divider separates rows. The name truncates on one line, and the count never wraps. Each row is a `NavigationLink` with the same long-press menu as today.

Spacing between these parts stays tight, with no gaps larger than 12 points apart from the screen's side margins.

Compact applies to Signal only. Original's Decks screen doesn't change.

The empty state is the existing one, in Signal's colors.

## Review screen in Signal

`ReviewView` keeps all its logic. Only the look of three pieces branches on the theme.

- **Top:** the three count pills become a thin segmented lime progress bar, with a small `new · learning · review` count line below it so that information isn't lost. Progress is `reviewedThisSession / (reviewedThisSession + cards left in the queue)`. It's empty at the start of a session, and full only when the queue is empty.
- **Card:** no card background. A small lime line shows the deck name, or `ALL DECKS`. The question is about 27 points and heavy, the answer about 19 points, and a thin divider separates them. Long answers scroll, as they do today. Tap to reveal still works across the whole area. Pictures use the existing `CardImageView`.
- **Rating buttons:** smaller than Original's, about 56 points tall, to leave more room for answers, while staying comfortably tappable. Good is solid lime with black text. Again, Hard and Easy are dark `#1B1B1B` with red, amber and white text.

Type-in cards, the finished screen, undo, edit and leech alerts stay as they are, in Signal's colors.

## Other screens in Signal

Deck page, Stats, Settings, card editor, bulk add, card browser and custom study keep their layouts and pick up Signal through the tokens.

## Differences from the mockup

- The Decks screen is more compact: the due count is about 56 points rather than 96 and shares a line with its caption, and deck rows are shorter with smaller counts.
- The real iOS tab bar stays, tinted lime, rather than the drawn one. It keeps Apple's accessibility and behavior.
- The `new · learning · review` line, the new-card count and the "coming back" label are added, so no information is lost.

## Accessibility

- All text on Signal's colors must reach at least 4.5:1 contrast. `mutedInk` on black and black on lime both pass.
- Tap targets stay at least 44 points.
- Rating buttons and deck rows keep their current VoiceOver labels.
- Rev's fonts use fixed sizes today, and this doesn't change that.

## Checking it

I can't run an iPhone app on the computer used for this work, so this is done on GitHub's Macs.

**Screenshots.** A new UI test target, `RevScreenshots`, runs in the iPhone simulator on pull requests and uploads the images as a build artifact.

- It starts the app with a launch argument, compiled only in Debug builds, that uses an in-memory database with sample decks and cards, sets the theme, and turns sync off. Release builds, which are what SideStore installs, don't contain this code.
- It captures Decks, Review question, Review answer, a card with a picture, deck page, Stats, Settings and card editor. It does this for Original in light and dark, and for Signal.

**Original unchanged.** The screenshot test lands first, before any theme code, to capture how Original looks today. The final Original screenshots are compared against those. Differences caused by the date or clock are ignored, and any other difference must be explained or fixed.

**Logic.** `Theme` reading a missing or unknown saved value as Original, and the progress calculation, are checked by adding to the existing `Tests/` check if they can compile without SwiftUI. Otherwise the screenshots cover them.

**On your phone.** After merging, you try Signal on the real device: switch theme, study a few cards, switch back. Real iOS 26 glass, touch feel and performance can only be judged there.

## Release

- One pull request. Merging publishes an iPhone update through SideStore, with a new line in `RELEASE_NOTES.md`.
- After updating, the app still looks like Original until Signal is chosen in Settings.
- Undoing it means choosing Original, or reverting the pull request if the code has to go. No data changes either way.
