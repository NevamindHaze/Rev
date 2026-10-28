# Rev for Linux: a desktop layout

Date: 2026-10-29. Scope: Rev for Linux (`src/Rev.Linux`, Avalonia) only. Rev for Windows (WPF) and Rev.Core don't change.

## Why

Rev for Linux is laid out like the iPhone app stretched onto a monitor: one centred column, stacked full-width pill
buttons (48–64px tall), big stat tiles, and screens that open on top of each other with back arrows. It should feel
like a desktop app: navigation always in view, compact controls, tables for lists, and the keyboard shortcuts on show.

## What stays the same

- Rev's colours (cream, ink, coral, teal, mustard, sage), light/dark themes, and the Nunito font.
- Everything the screens do: studying, scheduling, the card editor, cloze tools, custom study, paste many, Anki
  import/export, backups, sync. Only layout and sizes change.
- The card editor, custom study and paste many stay separate windows (already desktop-like). They pick up the new,
  smaller control sizes from the shared styles.

## Window and navigation

`MainWindow` stops being a stack of screens with back arrows. It becomes two columns:

- **Sidebar (left, about 220px, collapsible with Ctrl+B or a button at its top):**
  - Home (Ctrl+1), All cards (Ctrl+2), Stats (Ctrl+3).
  - "Decks" heading with a + button (new deck, Ctrl+Shift+N), then one row per deck: its colour as a dot, its name,
    and its due count. Right-click a deck: Study, Rename, Export to Anki, Delete.
  - At the bottom: Settings (Ctrl+,) and a one-line sync status ("Synced with Anki 2 min ago"), from `SyncRunner`.
  - The current page is highlighted. While studying, the deck being studied is highlighted with a play icon.
- **Content area (right):** shows one page at a time. Clicking a sidebar item replaces the page; there is no back
  stack. `Alt+←` and the back arrows go away. Pages that are opened from inside another page (for example Custom
  study's session, or reviewing "all decks" from Home) still just replace the content.
- The existing `IScreen.Refresh()` stays: the shell calls it when a page is shown, and refreshes the sidebar's deck
  rows whenever cards change (after a review, an edit, an import or a sync).

## Pages

### Home
- One line: greeting and date, then today's numbers as text ("0 due · 7 new · 0 done today"), and one
  "Review all decks" button (Space).
- A table of decks with columns Deck (colour dot + name), Due, New, Total. Click or Enter opens the deck;
  Space on a selected row studies it. Replaces the coloured deck tiles and the three stat tiles.
- The empty state ("No decks yet") stays, with a "New deck" button.

### Deck
- Header on one line: deck name, then "3 cards · 0 due · 3 new", then a toolbar: **Study** (Space, the one
  dark/filled button), **Custom study**, **Add** (Ctrl+N), and a ⋯ menu with Paste many and Export to Anki.
- Search box (Ctrl+F) with the filter / tag / sort menus beside it, on one row.
- The card list becomes a table (`CardBrowser`): columns Front, Back, Type, Due, with bold/italic shown, one line per
  card with "…" when it's cut off. Click a column heading to sort. Double-click or Enter edits, Delete deletes (asking
  first, as now), right-click shows the existing card actions (edit, suspend, delete, …). The state badges (New,
  Cloze, …) move into the Type and Due columns.
- All cards uses the same table, with a Deck column added.

### Studying
- Shown in the content area; the sidebar stays.
- Top toolbar: "Studying Pharmacology", the "4 new · 0 learning · 0 review" counts, and Undo (Ctrl+Z), Edit (E),
  Stop (Esc) buttons.
- The card is centred at a reading width (max about 640px) instead of the full width. Card text stays large.
- "Show answer" becomes a normal-sized button (Space). Again / Hard / Good / Easy become a compact centred row,
  each showing its key and when you'd see the card next ("1 · Again 1m"), keeping their coral / mustard / teal /
  sage colours. Type-the-answer and "check again later" work as now.
- Stopping (Esc, Stop, or clicking anything in the sidebar) goes to the deck's page.

### Stats
- The same numbers and charts, in two columns when the window is wide enough, one column when narrow.

### Settings
- Grouped sections: Studying (name, new cards per day, scheduler, retention), Sync with Anki, Anki files
  (import / export), Backups, About. Each setting is a row with its label on the left and its control on the right.
  The same options as now.

## Sizes (shared styles in `Theme/Styles.axaml`)

- Buttons about 32px tall (primary actions 36px), 13–14px text, corner radius about 6px instead of pills.
- Text boxes 32px tall with 6px corners; the card editor's text boxes keep their larger 19px text.
- Body text 14px, headings 18–20px, page padding about 20px instead of 32px.
- Stat tiles (`StatTile`) and deck tiles (`DeckTile`) are no longer used on Home and the deck page.

## Testing

- `tests/Rev.Linux.Tests` (headless Avalonia) is updated to the new navigation: tests open pages through the sidebar
  instead of `Window.Open` / back, and find the table rows instead of card rows. Every existing test keeps checking
  the same behaviour.
- New tests: the sidebar lists decks with due counts and opens each page; Ctrl+1/2/3 and Ctrl+B work; a review runs
  inside the content area and Esc returns to the deck; the deck table sorts by a column and Enter opens the editor.
- The tests' screenshots (`Picture`) are checked by eye in light and dark mode, at 1280×800 and at a narrow
  900×600 window.
