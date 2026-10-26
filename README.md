# Rev

A personal flashcard app for iPhone, built with SwiftUI and SwiftData.

## Features
- Decks: create, rename (long-press), delete (long-press)
- Cards: add one at a time ("Save & next"), paste many at once, edit, delete
- Review: question → tap → answer → Again / Hard / Good / Easy
- Scheduling: SM-2 style, swappable for FSRS later (`Rev/Scheduling`)
- Stats: cards due, reviews today, streak, last 7 days
- Backup: export/restore everything as JSON through the Files app
- Anki: import and export `.apkg` decks (text only for now)

## How to build
1. Upload or change files on the `main` branch. The **iOS Build** workflow then starts on its own.
   You can also start it by hand: **Actions → iOS Build → Run workflow**.
2. When it finishes (green tick), open the run and download **Rev-ipa** under *Artifacts*.
3. Unzip it to get `Rev.ipa`, put that on your iPhone, and install it with SideStore.

## Importing from Anki
In Anki on your computer: **File → Export → Anki Deck Package (.apkg)**. Rev reads both the
old and the newest format, including `.colpkg`. Text, images, progress, suspended cards and
leech tags come across; sounds are skipped.

## Folder map
```
Rev/App          app start + tabs
Rev/Models       Deck, Card, ReviewLog (saved with SwiftData)
Rev/Scheduling   Rating, SM-2 algorithm, study queue
Rev/Stats        numbers for the stats screen
Rev/Backup       JSON backup / restore
Rev/Anki         .apkg import / export
Rev/Theme        colours, fonts, shared buttons
Rev/Views        all screens
```
