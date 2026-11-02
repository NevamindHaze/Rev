# Proposal: Rev for Windows joins the Rev ↔ Anki sync

Status: **built, tested and merged** (add-on: Rev pull request #1). Rev for Windows' side is in
`src/Rev.Core/Sync` and Settings → Sync with Anki. The add-on change is in the
[Rev](https://github.com/NevamindHaze/Rev) repository's `anki-addon/rev_sync`. Two things were learned while building
and using it, added below (section 3, point 6, and section 8, point 5). This describes how Rev for Windows takes part in the sync that already
works between Rev on iPhone and Anki, **without changing Rev on iPhone at all** and without any risk to
the iPhone ↔ Anki sync you use today.

## 1. How the sync works today

```
Rev (iPhone)  ⇄  iCloud Drive / "Rev Sync"  ⇄  Rev Sync add-on (Anki on your computer)  ⇄  AnkiWeb
                   ├─ rev.json    written only by Rev on iPhone, read only by the add-on
                   ├─ anki.json   written only by the add-on, read only by Rev on iPhone
                   └─ media/      images, copied in and out by both
```

The rules (from `Rev/Sync` and `anki-addon/rev_sync/core.py`):

- **One writer per file.** Each app writes its own file and reads the other's. Because no file has two
  writers, iCloud never has to choose between two versions of the same file.
- **Same card, same name.** A card is `"<note guid>:<ord>"` in both apps (for notes made in Rev, the guid is
  a UUID).
- **Newest wins, per card, in two parts.** Text/tags/deck (`editedAt`) and schedule/suspended (`schedAt`)
  are merged separately: whichever side changed that part most recently wins.
- **No echoes.** When the add-on applies a change from Rev, it remembers Rev's own timestamp
  (`appliedContent` / `appliedSched`) and reports *that* timestamp in `anki.json`, so Rev doesn't apply its
  own change again.
- **Deletions are tombstones.** A card that disappears is listed in `deleted` with the time. A deletion
  loses if the card was changed after it. Each side also remembers which keys it has sent before
  (`exportedKeys`), so a card one side deleted isn't added back by the other.
- **Reviews from both sides are kept** (the last 60 days are exchanged; duplicates are recognised by
  card + millisecond time).

## 2. The idea: Anki is the hub

Rev for Windows becomes a **second "Rev-like" app that talks only to Anki**, in exactly the way the iPhone does:

```
Rev (iPhone)   ──rev.json──────▶                 ──anki.json──▶  Rev (iPhone)
                                  Rev Sync add-on
Rev (Windows)  ──windows.json──▶  (Anki)         ──anki.json──▶  Rev (Windows)
```

- **Rev for Windows writes its own file, `windows.json`**, in the same "Rev Sync" folder, in the same
  format as `rev.json` (`"writer": "windows"`). Nobody else ever writes it.
- **Rev for Windows reads `anki.json`**, exactly as the iPhone does, and applies it with the same rules.
  The Windows code is a port of the iPhone's `SyncMerger.swift`.
- **The add-on learns to read `windows.json` too.** That is the only change outside this repository.
  If the file isn't there, the add-on behaves exactly as it does today.
- **Rev on iPhone doesn't change at all.** Windows changes reach the iPhone by way of Anki: Windows →
  `windows.json` → Anki → `anki.json` → iPhone, and iPhone changes reach Windows the same way in reverse.

Why not let Windows and the iPhone also read each other's files directly? Three apps all merging each
other's files would need new rules on the iPhone, give every change three routes (and more ways to loop or
clash), and Rev on iPhone reads `anki.json` text as HTML, so it would misread Windows' plain text anyway.
Going through Anki keeps every pair exactly like the one that already works. The cost is that changes
travel between the iPhone and Windows only when Anki syncs, and Anki runs on the same computer as Rev for
Windows, so that's usually the case (see section 7).

## 3. What changes in the Rev Sync add-on

The add-on keeps everything it does now, plus:

1. **Read both files.** Each sync reads `rev.json` and, if present, `windows.json`, and remembers each one's
   `writtenAt` separately (`lastRevWrittenAt`, `lastWindowsWrittenAt`), so a file is only processed when it
   changed.
2. **Merge the two first, then apply once.** This matters. Applying a change makes Anki's own "last
   changed" time *now*, so if the add-on applied the iPhone's file and then Windows', the second would
   always lose, even if it were newer. Instead, for every card key:
   - content: take the peer entry with the newest `editedAt`;
   - schedule: take the peer entry with the newest `schedAt` (content and schedule may come from
     different peers, just as they're already merged separately);
   - then compare that winner with Anki exactly as `import_rev` does today, and apply it once.
3. **Tombstones from either app**, with today's rule: a note is removed only when all its cards were
   deleted and nothing changed them after the deletion. A tombstone from one app loses to a newer change
   from the other app, too.
4. **Reviews from both apps** go into Anki's review log, as now (duplicates skipped by their millisecond id).
5. **New notes** from either app are created as now (the "Rev Basic / Reversed / Cloze / Type-in" note
   types). If both apps somehow sent the same new note id, it's created once.
6. **(Found while testing) Compare with when a card really changed, and don't re-create fresh deletions.**
   Applying a change stamps the note with the time of the sync, so the add-on compares new changes with the
   Rev app's own time it already remembers (`changed_at`). And because both apps' files are read on every
   sync, an app that hasn't yet heard about a deletion would bring the card back; the add-on now skips cards
   deleted in the last 60 days unless the app changed them after the deletion.
7. **The echo memory stays one per card** (`appliedContent`, `appliedSched`): it records the timestamp of
   whichever app's change was applied last, so `anki.json` reports that time and neither app re-applies
   its own change. Nothing else in `export_anki` changes, so the iPhone sees exactly the same kind of file
   as today.

## 4. What Rev for Windows needs

- **A `Rev/Sync` port** (`SyncModels`, `SyncMerger`, `SyncService`), the same rules as the iPhone, reading
  `anki.json` and writing `windows.json` (atomically: a temporary file, then a rename, like the add-on).
  Its own memory between syncs (`exportedKeys`, `tombstones`, `lastAnkiWrittenAt`, `lastExportDigest`)
  lives in Rev's data folder, never in the shared folder.
- **Settings → Sync with Anki**: choose the "Rev Sync" folder. On a PC with iCloud for Windows it's
  usually `C:\Users\<you>\iCloudDrive\Rev Sync`, the same folder the add-on is set to; Rev can suggest it
  by reading the add-on's own setting. Sync runs when Rev opens, when it closes, and from "Sync now".
- **Already in place** (built in versions 1.0 for exactly this reason): every card has `noteID` + `noteOrd`
  (Anki's guid and ord for imported cards, a UUID for new notes), `editedAt` and `schedChangedAt` updated
  wherever those change, review logs in milliseconds, `FieldsReadOnly` for note types Rev can't rewrite,
  deck names with " › " ↔ "::", and the same cloze/reversed field layout as the iPhone.
- **Done: imported cards carry Anki's own change times.** If cards imported from an Anki file had the
  *import time* as their `editedAt` / `schedChangedAt`, the first sync would see Windows' copy as newer and
  push Rev's simplified text back into Anki's fields (losing, say, colours in your Anki HTML). Rev on iPhone
  avoids this by treating imported cards as "changed long ago". Rev for Windows goes one step further:
  `editedAt` is the note's last change in Anki and `schedChangedAt` the card's, which are exactly the times
  the add-on writes in `anki.json`. So on a first sync neither side looks newer and nothing is overwritten;
  a card you then edit or study in Rev becomes newer, as it should. (A missing time counts as "long ago".)
  A re-import that brings Anki's text back also puts Anki's time back.

## 5. Keeping the existing sync safe

- **The iPhone app isn't touched**, and `anki.json` keeps exactly today's format.
- **The add-on change is backwards compatible:** with no `windows.json` in the folder, it takes today's
  code path. Its existing tests stay, and new ones cover the three-app cases (below).
- **Sync is off in Rev for Windows until you switch it on.** The first sync:
  1. saves a JSON backup of Rev for Windows automatically (and the add-on already backs up Anki before its
     first sync);
  2. shows a preview ("12 cards will be updated from Anki, 3 sent to Anki, 0 deleted") and only goes ahead
     when you say so.
- **Deletes are conservative:** a deletion never wins over a newer change, and a note is only removed from
  Anki when all its cards were deleted.
- **Turning sync off** in Rev for Windows simply stops writing `windows.json`; deleting that file returns
  the add-on to exactly today's behaviour.

## 6. How it would be tested (never on your real Anki or iCloud)

- **Windows unit tests** for the merger, using `anki.json` files recorded from the real add-on.
- **Three-app tests in a throw-away Anki collection**, using Anki's own Python library (as the import and
  export tests already do) and the **real add-on code** (`core.sync`) pointed at a temporary folder. A
  `rev.json` stands in for the iPhone. Scenarios include:
  - a card edited on the iPhone, then on Windows, then studied in Anki: each change wins in turn and
    reaches the other two apps, with no echo;
  - the same card edited on both apps between syncs: the newer edit wins everywhere;
  - deleted on Windows but studied on the iPhone afterwards: the card survives;
  - new notes, cloze edits that add or remove numbers, reversed pairs, tags, leeches, suspending, images;
  - reviews from all three apps ending up in each app's history exactly once.
- Only after that: a trial with **copies** of your folder and collection, and then your real setup.

## 7. Limits to know about

- Windows and the iPhone exchange changes **through Anki**: if Anki isn't opened on the computer, changes
  wait there (the add-on syncs when Anki starts and every few minutes while it's open). A later, optional
  step could let Rev for Windows start the add-on's sync itself, or (with a change to Rev on iPhone) let the
  iPhone read `windows.json` directly. Neither is needed to start.
- As today, "newest wins" uses each device's clock, so clocks should be roughly right (Windows and iOS set
  them automatically).
- Text from note types Rev can't rewrite (`FieldsReadOnly`) still travels from Anki to Rev, but edits made in
  Rev stay in Rev, the same as on the iPhone.

## 8. Suggested order of work

1. ~~In this repository: imported cards keep Anki's change times (section 4), with tests.~~ Done.
2. ~~In the Rev repository (`anki-addon/rev_sync`): read `windows.json` and merge-then-apply (section 3),
   with the three-app tests.~~ Done and merged (`tests/test_windows.py`).
3. ~~In this repository: the sync port, the Settings screen, the first-sync backup and preview.~~ Done
   (`tests/Rev.Core.Tests/SyncTests.cs`; `tools/check-sync.py` runs all three apps together).
4. A trial on copies, then your real folder.
5. (Found in use) **Decks emptied by a sync are removed.** Decks aren't synced as objects, only each card's
   deck name, so a deck deleted in one app used to stay behind, empty, in the others. Now each app removes a
   deck when a sync deleted or moved away its last cards. Decks you emptied or made yourself, Anki's Default
   deck, filtered decks and decks that still have sub-decks stay. Built in all three apps.
