# Rev for Linux desktop layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Rev for Linux's phone-style stacked screens with a desktop layout: a deck sidebar beside one content page, compact controls, and tables for lists.

**Architecture:** `MainWindow` becomes a two-column grid: a new `Sidebar` control and a content area. `MainWindow.Open(Control)` keeps its name but now replaces the page (no history, no `GoBack`). Deck dialogs (new / rename / delete / export) move out of `HomeView` into a shared `DeckActions` class so the sidebar, Home and the deck page all use them. Each page's XAML is reworked in place; `Rev.Core` doesn't change.

**Tech Stack:** C# / .NET 8, Avalonia 11.3, Material.Icons.Avalonia, xUnit with Avalonia.Headless.XUnit.

**Spec:** `docs/superpowers/specs/2026-10-29-linux-desktop-layout-design.md`

## Global Constraints

- Only `src/Rev.Linux` and `tests/Rev.Linux.Tests` change (plus `RELEASE_NOTES.md`). Rev for Windows and `Rev.Core` stay as they are.
- Keep Rev's colour names (`InkBrush`, `CoralBrush`, `TealBrush`, `MustardBrush`, `SageBrush`, `SoftFillBrush`, `SurfaceBrush`, `MutedInkBrush`, `On…Brush`) and Nunito; use them through `DynamicResource` / `.Colour(...)` so light and dark both work.
- Sizes: buttons about 32px tall (primary 36px), 13–14px button text, corner radius 6px; text boxes 32px tall, radius 6px; body text 14px; headings 18–20px; page padding 20px. Card text in the review stays large; the card editor's text boxes keep 19px text.
- Every control a test finds is named with `.Named("…")` / `AutomationProperties.Name`, as the code does now.
- Comments and copy follow the codebase's plain-English style ("Click a deck to open it").
- Run all tests with the repo's .NET 8 SDK: `dotnet test tests/Rev.Linux.Tests` (screenshots go to `REV_SCREENSHOTS` when set).

## Review Focus

- A deck that is open (or being studied) gets deleted or renamed from the sidebar: the page must not keep showing a deck that's gone; it goes to Home, and a rename shows the new name everywhere. Test in Task 2.
- A sync or an edit changes due counts while you're on any page: the sidebar's numbers must update without a click. Test in Task 2.
- Esc while a dialog (`RevDialog`) is open must close the dialog only, never stop a review or change page. Test in Task 6.
- No decks at all: the sidebar's deck list is empty but the + button still works, and Home shows the empty state. Test in Task 3.
- A narrow window (900×600) with the sidebar open: nothing is cut off or overlaps; long deck names end in "…". Checked by screenshot in Task 8.

---

### Task 1: Desktop sizes in the shared styles

**Files:**
- Modify: `src/Rev.Linux/Theme/Styles.axaml` (styles `TextBlock.title/heading/body/muted/label`, `Button.rev`, `Button.chip`, `Button.round`, `TextBox.rev`, `RadioButton.choice`, `Border.card`)
- Test: `tests/Rev.Linux.Tests/ScreenTests.cs`

**Interfaces:**
- Produces: the same style class names as now (`rev`, `secondary`, `teal`, `danger`, `mustard`, `chip`, `round`, `rating`, `choice`, `card`, `title`, `heading`, `body`, `muted`, `label`) plus a new `Button.primary` (ink background, 36px) for the one main action per page, and `Button.toolbar` (32px, transparent until hover) for toolbar buttons.

- [ ] **Step 1: Write the failing test**

```csharp
[AvaloniaFact]
public void ControlsAreDesktopSized()
{
    AddSampleDecks();
    var buttons = All<Button>(Window).Where(b => b.Classes.Contains("rev") && !b.Classes.Contains("tile") && b.IsEffectivelyVisible).ToList();
    Assert.NotEmpty(buttons);
    Assert.All(buttons, b => Assert.InRange(b.Bounds.Height, 24, 38));
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test tests/Rev.Linux.Tests --filter ControlsAreDesktopSized`
Expected: FAIL (buttons are 48–64px tall).

- [ ] **Step 3: Change the style values in `Theme/Styles.axaml`** to the Global Constraints sizes. `MinHeight` on `Button.rev` becomes 32, padding `14,0`, `Pill` corner radius 6; `title` 20px, `heading` 18px, `body` 14px, `muted` 13px. Remove `MinHeight="48|54|64"` attributes in the screens' XAML that override them (search `MinHeight=` under `src/Rev.Linux/Views`).

- [ ] **Step 4: Run the whole Linux suite**

Run: `dotnet test tests/Rev.Linux.Tests`
Expected: all pass.

- [ ] **Step 5: Commit** — `git commit -m "Linux: desktop-sized buttons, text boxes and text"`

---

### Task 2: Sidebar and the new main window

**Files:**
- Create: `src/Rev.Linux/Controls/Sidebar.cs`
- Create: `src/Rev.Linux/Controls/DeckActions.cs`
- Modify: `src/Rev.Linux/MainWindow.axaml`, `src/Rev.Linux/MainWindow.axaml.cs`
- Modify: every `Back_Click` / `GoBack()` caller: `Views/StatsView.axaml(.cs)`, `Views/AllCardsView.axaml(.cs)`, `Views/DeckView.axaml(.cs)`, `Views/SettingsView.axaml(.cs)`, `Views/ReviewView.axaml(.cs)` (remove the ← buttons and their Esc bindings, except the review's — see Task 6)
- Modify: `Views/HomeView.axaml.cs` (its NewDeck / RenameDeck / DeleteDeck move to `DeckActions`)
- Test: `tests/Rev.Linux.Tests/ScreenTests.cs`, `tests/Rev.Linux.Tests/TestApp.cs`

**Interfaces:**
- Produces in `MainWindow`:
  - `void Open(Control page)` — shows the page in the content area, replaces the old one, calls `IScreen.Refresh()` on it if it's already loaded, and tells the sidebar to highlight it.
  - `void ShowHome()`, `void ShowDeck(Deck deck)`.
  - `void RefreshSidebar()` — reloads deck rows and due counts.
  - `bool IsSidebarOpen { get; }`, `void ToggleSidebar()`.
  - Keeps `Current`, `IsReviewing`, `CurrentScreen`. `GoBack()` is removed.
  - Key bindings: Ctrl+1 Home, Ctrl+2 All cards, Ctrl+3 Stats, Ctrl+, Settings, Ctrl+B sidebar, Ctrl+Shift+N new deck.
  - `SyncRunner.Synced` and `Activated` also call `RefreshSidebar()`.
- Produces `Sidebar : Border` with `Refresh()` and `Highlight(Control page)`. Named items: "Home", "All cards", "Stats", "Settings", "New deck", "Collapse sidebar"; one `SidebarDeckRow : Button` per deck with `Deck Deck` and `int ToStudyCount` (`cards.DueCount(now) + cards.NewCount()`, the cards waiting today), named after the deck, showing a colour dot (the same palette as `DeckTile.Palettes`, by deck index), the name (trimmed with "…"), and that count. Right-click menu: Study, Rename (F2), Export to Anki, Delete (Delete). The bottom line shows `SyncRunner`'s last-sync text (the same text Settings shows).
- Produces `static class DeckActions` (all return after the dialog closes, then call `MainWindow.Current.RefreshSidebar()` and refresh the current page):
  - `Task<Deck?> New(Visual owner)` — opens the new deck afterwards.
  - `Task Rename(Visual owner, Deck deck)`
  - `Task Delete(Visual owner, Deck deck)` — if the deleted deck is open or being studied, goes Home.
  - `Task Export(Visual owner, Deck deck)` — the code from `DeckView.Export_Click`.
  - Dialog text and buttons stay exactly as in `HomeView` now.

- [ ] **Step 1: Write the failing tests**

```csharp
[AvaloniaFact]
public void TheSidebarListsDecksWithWhatsDue()
{
    AddSampleDecks();
    var rows = All<SidebarDeckRow>(Window).ToList();
    Assert.Equal(["Pharmacology", "Parasitology"], rows.Select(r => r.Deck.Name));
    Click(rows[1]);
    Assert.IsType<DeckView>(CurrentScreen);
    Assert.Contains("Parasitology", Texts(CurrentScreen!));
}

[AvaloniaFact]
public void ShortcutsOpenEachPage()
{
    AddSampleDecks();
    _ = Window;
    Press(Key.D2, KeyModifiers.Control); Assert.IsType<AllCardsView>(CurrentScreen);
    Press(Key.D3, KeyModifiers.Control); Assert.IsType<StatsView>(CurrentScreen);
    Press(Key.OemComma, KeyModifiers.Control); Assert.IsType<SettingsView>(CurrentScreen);
    Press(Key.D1, KeyModifiers.Control); Assert.IsType<HomeView>(CurrentScreen);
    Press(Key.B, KeyModifiers.Control); Assert.False(Window.IsSidebarOpen);
    Press(Key.B, KeyModifiers.Control); Assert.True(Window.IsSidebarOpen);
}

[AvaloniaFact]
public void DeletingTheOpenDeckFromTheSidebarGoesHome()
{
    AddSampleDecks();
    Window.ShowDeck(Pharmacology);
    Settle();
    var row = All<SidebarDeckRow>(Window).First(r => r.Deck.Name == "Pharmacology");
    row.Focus();
    Press(Key.Delete);
    Click(Find<Button>(OpenDialog(Window)!, "Delete"));
    Assert.IsType<HomeView>(CurrentScreen);
    Assert.DoesNotContain(All<SidebarDeckRow>(Window), r => r.Deck.Name == "Pharmacology");
}

[AvaloniaFact]
public void TheSidebarsDueCountsFollowChanges()
{
    AddSampleDecks();
    var before = All<SidebarDeckRow>(Window).First(r => r.Deck.Name == "Parasitology").ToStudyCount;
    Db.AddNote(Parasitology.Id, CardKind.Basic, "Host of *Dipylidium*?", "Flea", []);
    Window.RefreshSidebar();   // what SyncRunner.Synced and closing the editor call
    Settle();
    Assert.Equal(before + 1, All<SidebarDeckRow>(Window).First(r => r.Deck.Name == "Parasitology").ToStudyCount);
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/Rev.Linux.Tests --filter "TheSidebar|ShortcutsOpenEachPage|DeletingTheOpenDeck"`
Expected: FAIL — `SidebarDeckRow` doesn't exist.

- [ ] **Step 3: Build `DeckActions`** from `HomeView`'s `NewDeck` / `RenameDeck` / `DeleteDeck` and `DeckView.Export_Click`, and make `HomeView` call it.

- [ ] **Step 4: Build `Sidebar`** (220px wide, `SoftFillBrush` background, items 32px tall, selected item `TealBrush` text on a `SurfaceBrush` pill) **and the new `MainWindow`**: `Grid ColumnDefinitions="Auto,*"` holding `Sidebar` and the `Screen` content control, with `DialogLayer` still on top of both. Collapsing hides the sidebar and shows a small "Show sidebar" button at the top left of the content area.

- [ ] **Step 5: Remove back navigation**: delete `GoBack`, the `history` stack, the Alt+← binding, every ← button and every `Escape → GoBack` binding outside the review. In `TestApp.cs` nothing changes (`Window.Open(...)` still works).

- [ ] **Step 6: Update the existing tests that pressed Esc to go Home** (`ADeckShowsItsNumbersAndCards`): assert instead that Ctrl+1 goes Home. Update `DeletingADeckAsksFirstAndEnterMeansCancel` to delete through a `SidebarDeckRow` (focus it, press Delete) — its dialog assertions stay.

- [ ] **Step 7: Run the whole Linux suite** — `dotnet test tests/Rev.Linux.Tests` — all pass.

- [ ] **Step 8: Commit** — `git commit -m "Linux: sidebar with decks instead of screens stacked with back arrows"`

---

### Task 3: Home as a summary and a deck table

**Files:**
- Modify: `src/Rev.Linux/Views/HomeView.axaml`, `src/Rev.Linux/Views/HomeView.axaml.cs`
- Create: `src/Rev.Linux/Controls/DeckListRow.cs`
- Test: `tests/Rev.Linux.Tests/ScreenTests.cs`

**Interfaces:**
- Consumes: `DeckActions`, `MainWindow.ShowDeck`, `MainWindow.Open`.
- Produces: `DeckListRow : Button` with `Deck Deck`, columns Deck (dot + name) / Due / New / Total, named after the deck. Click or Enter opens the deck; Space studies it (`MainWindow.Current.Open(new ReviewView(deck))`); F2 / Delete / right-click as in the sidebar.
- Home's header: greeting + date on the left, "Review all decks" (`Button.primary`, Space when no row has the keyboard) on the right; below it one line `"{due} due · {new} new · {done} done today"`.
- `StatTile` and `DeckTile` are no longer used by Home (leave the classes; the deck page also stops using `StatTile` in Task 4; delete both files in Task 4 if nothing else uses them).

- [ ] **Step 1: Update `HomeShowsTheGreetingNumbersAndDecks`** to assert `"0 due · 7 new · 0 done today"` is in `Texts(home)` and `All<DeckListRow>(home)` lists `["Pharmacology", "Parasitology"]`; update `AnEmptyRevInvitesYouToMakeADeck` to also assert `All<SidebarDeckRow>(Window)` is empty and `Find<Button>(Window, "New deck")` is enabled.

- [ ] **Step 2: Run them and see them fail** — `dotnet test tests/Rev.Linux.Tests --filter "HomeShows|AnEmptyRev"` — FAIL.

- [ ] **Step 3: Rebuild Home** to the interfaces above; keep `Refresh()` and the empty state.

- [ ] **Step 4: Run the whole Linux suite** — all pass.

- [ ] **Step 5: Commit** — `git commit -m "Linux: Home shows today's numbers in a line and decks in a table"`

---

### Task 4: Deck page header and toolbar

**Files:**
- Modify: `src/Rev.Linux/Views/DeckView.axaml`, `src/Rev.Linux/Views/DeckView.axaml.cs`
- Delete if unused: `src/Rev.Linux/Controls/StatTile.cs`, `src/Rev.Linux/Controls/DeckTile.cs` (move `LayoutExtensions` and the palette to `Controls/Ui.cs` first; `Sidebar` and `DeckListRow` use the palette)
- Test: `tests/Rev.Linux.Tests/ScreenTests.cs`

**Interfaces:**
- Consumes: `DeckActions.Export`.
- Header row: deck name (`title`), then `"{cards} · {due} due · {new} new"` (`muted`), then toolbar buttons named "Study" (`Button.primary`, Space when the card list doesn't have the keyboard), "Custom study", "Add card" (Ctrl+N), and "More" (⋯) opening a menu with "Paste many…" and "Export to Anki…".

- [ ] **Step 1: Update `ADeckShowsItsNumbersAndCards`** to open the deck from the sidebar, assert `"3 cards · 0 due · 3 new"` (Parasitology has 3 cards: a 2-card cloze and a basic) is in `Texts(deck)`, and that `Find<Button>(deck, "Study")` and `Find<Button>(deck, "More")` exist. Update `AddingCardsInTheEditor` and `PastingManyCards` only if they clicked the old buttons by name.

- [ ] **Step 2: Run and see it fail** — FAIL (no "More" button yet).

- [ ] **Step 3: Rebuild the header** to the interfaces above; the search box and filters stay in `CardBrowser` (Task 5).

- [ ] **Step 4: Run the whole Linux suite** — all pass.

- [ ] **Step 5: Commit** — `git commit -m "Linux: deck page header with a compact toolbar"`

---

### Task 5: Card list as a table

**Files:**
- Modify: `src/Rev.Linux/Controls/CardBrowser.cs`, `src/Rev.Linux/Theme/Styles.axaml` (`Button.cardrow` becomes a 1-line table row: no rounded card, a hairline below, `SurfaceBrush` on hover)
- Test: `tests/Rev.Linux.Tests/ScreenTests.cs`

**Interfaces:**
- Search box and the filter / tag / sort menus sit on one row (`searchBox` stretches; chips to its right).
- A header row above the rows: Front, Back, Type, Due, plus Deck first when `withDeckNames` is true. Clicking "Front" sorts `CardSort.AToZ`, clicking "Due" sorts `CardSort.DueSoonest`; the sort menu keeps all six sorts and shows the current one. The other headings aren't clickable.
- Each row (still a `Button` named `CardText.Preview(card.Front)`, `Tag = card.Id`) is one line per column, using `RichText.Fill` for bold/italic, `TextTrimming = CharacterEllipsis`. Type shows `DeckView.KindName`, plus "Leech" / "Suspended" after it when they apply; Due shows `CardQuery.StateText`. Tags show in the row's tooltip. The ⋯ button goes away (right-click keeps the menu). Enter or double-click edits (single click selects/focuses the row); Delete deletes as now; ↑ / ↓ move between rows.
- Keep the "Show more" paging and the `"N cards"` / `"1 card of 7"` count text exactly as they are.

- [ ] **Step 1: Write the failing test**

```csharp
[AvaloniaFact]
public void TheCardTableSortsByAColumnAndEnterEdits()
{
    AddSampleDecks();
    Window.Open(new AllCardsView());
    Settle();
    var browser = All<CardBrowser>(Window).Single();
    Click(Find<Button>(browser, "Front"));
    var rows = All<Button>(browser).Where(b => b.Tag is Guid).ToList();
    var fronts = rows.Select(r => AutomationProperties.GetName(r)!).ToList();
    Assert.Equal(fronts.OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase), fronts);
    rows[0].Focus();
    Press(Key.Enter);
    Assert.NotNull(Opened<CardEditorWindow>());
}
```

Also update `TheCardBrowserSearchesAndFilters` and `ADeckShowsItsNumbersAndCards` if they looked for the "#flukes" tag text (tags move to the tooltip: assert the row's `ToolTip.GetTip` contains "#flukes" instead).

- [ ] **Step 2: Run and see it fail** — FAIL (no "Front" heading).

- [ ] **Step 3: Rebuild the rows and add the header** to the interfaces above.

- [ ] **Step 4: Run the whole Linux suite** — all pass.

- [ ] **Step 5: Commit** — `git commit -m "Linux: cards in a sortable table"`

---

### Task 6: Studying in the content area

**Files:**
- Modify: `src/Rev.Linux/Views/ReviewView.axaml`, `src/Rev.Linux/Views/ReviewView.axaml.cs`
- Test: `tests/Rev.Linux.Tests/ScreenTests.cs`

**Interfaces:**
- Consumes: `MainWindow.ShowDeck`, `MainWindow.ShowHome`, `Sidebar.Highlight` (the studied deck row shows a play icon while `IsReviewing`).
- Top toolbar: `"Studying {deck name}"` (or "Studying all decks"), the existing new / learning / review counts, and buttons "Undo" (Ctrl+Z), "Edit" (E), "Stop" (Esc).
- The card (`Border.card`) has `MaxWidth="640"` and is centred; QUESTION / ANSWER labels and card text sizes stay.
- "Show answer" is a `Button.primary` (Space). The rating row is four buttons, centred, 36px tall, each `"{n} · {label}  {interval}"` (e.g. "1 · Again  1m") in their existing colours.
- `Stop()`: goes to `ShowDeck(deck)`, or `ShowHome()` when studying all decks. Esc calls it, unless a `RevDialog` is showing (then Esc belongs to the dialog, as `DialogLayer.For(this).IsShowing` tells).

- [ ] **Step 1: Update `ReviewingWithTheKeyboard`**: after Esc assert `Assert.IsType<DeckView>(CurrentScreen)` (it was Home); assert `Texts(review)` contains "Studying Pharmacology" and, after Space, "1 · Again". Add:

```csharp
[AvaloniaFact]
public void EscWithAQuestionOpenOnlyClosesTheQuestion()
{
    AddSampleDecks();
    Window.Open(new ReviewView(Pharmacology));
    Settle();
    _ = RevDialog.Confirm(Window, "Discard this card?", "You've typed a card that hasn't been saved.", "Discard", destructive: true);
    Settle();
    Press(Key.Escape);
    Assert.Null(OpenDialog(Window));
    Assert.IsType<ReviewView>(CurrentScreen);
}
```

- [ ] **Step 2: Run and see them fail** — FAIL.

- [ ] **Step 3: Rework the review layout and add `Stop()`** to the interfaces above. `AllCaughtUp`, `TypeTheAnswerCardsCheckWhatYouTyped`, `EditingACardFromTheReview` and the custom-study tests keep passing unchanged except where they pressed Esc to reach Home.

- [ ] **Step 4: Run the whole Linux suite** — all pass.

- [ ] **Step 5: Commit** — `git commit -m "Linux: study inside the window, with a compact rating row"`

---

### Task 7: Stats and Settings laid out for a wide window

**Files:**
- Modify: `src/Rev.Linux/Views/StatsView.axaml`, `src/Rev.Linux/Views/StatsView.axaml.cs`, `src/Rev.Linux/Views/SettingsView.axaml`, `src/Rev.Linux/Views/SettingsView.axaml.cs`
- Test: `tests/Rev.Linux.Tests/ScreenTests.cs`

**Interfaces:**
- Stats: the existing numbers and charts in a two-column grid when the page is at least 900px wide, one column below that (switch in `SizeChanged`, like Home's old `DeckGrid`).
- Settings: sections titled "Studying", "Sync with Anki", "Anki files", "Backups", "About". Each setting is a `Grid ColumnDefinitions="220,*"` row: label (and muted hint) left, control right. Same controls, names and handlers as now.

- [ ] **Step 1: Update `Stats` and `SettingsAreSavedAtOnce`** to open the page with Ctrl+3 / Ctrl+, instead of `Window.Open`, and assert each section title ("Studying", "Sync with Anki", "Anki files", "Backups", "About") is in `Texts(settings)`.

- [ ] **Step 2: Run and see them fail** — FAIL ("Anki files" isn't a section title yet).

- [ ] **Step 3: Rework both pages** to the interfaces above.

- [ ] **Step 4: Run the whole Linux suite** — all pass.

- [ ] **Step 5: Commit** — `git commit -m "Linux: Stats in two columns and Settings as labelled rows"`

---

### Task 8: Check every screen by eye, release notes

**Files:**
- Modify: `tests/Rev.Linux.Tests/TestApp.cs` (a `Picture` overload that takes a size), `tests/Rev.Linux.Tests/ScreenTests.cs` (`DarkMode` takes pictures of every page), `RELEASE_NOTES.md`

- [ ] **Step 1: Add `NarrowWindowFitsEverything`**: set `Window.Width = 900; Window.Height = 600;`, open each page (Ctrl+1/2/3, Ctrl+,, a deck, a review) and take a picture of each (`Picture(Window, "30-narrow-home")`, …). Assert no visible `TextBlock` in the sidebar has `Bounds.Right` beyond the sidebar's width.

- [ ] **Step 2: Run the whole suite with screenshots**

Run: `REV_SCREENSHOTS=/tmp/rev-shots dotnet test tests/Rev.Linux.Tests`
Expected: all pass. Then open every picture and check against the spec: sidebar present, no back arrows, nothing over 38px tall except the card, nothing cut off, dark-mode pictures readable.

- [ ] **Step 3: Fix anything the pictures show**, re-running Step 2.

- [ ] **Step 4: Add release notes** — a first bullet in `RELEASE_NOTES.md`: "Rev for Linux is laid out for a desktop: your decks are always in a sidebar, cards are in a sortable table, studying happens in the window, and buttons and text are desktop-sized. Ctrl+1/2/3 open Home, All cards and Stats; Ctrl+B hides the sidebar."

- [ ] **Step 5: Commit** — `git commit -m "Linux desktop layout: screenshots and release notes"`
