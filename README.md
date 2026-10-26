<p align="center">
  <img src="src/Rev.App/Assets/AppIcon.png" width="140" alt="Rev app icon">
</p>

<h1 align="center">Rev for Windows</h1>

<p align="center">
  <b>The desktop sibling of <a href="https://github.com/NevamindHaze/Rev">Rev for iPhone</a>:
  a calm, warm spaced-repetition flashcard app.</b><br>
  Built for studying veterinary medicine, with FSRS scheduling and Anki import/export.
</p>

> Work in progress. See [`RELEASE_NOTES.md`](RELEASE_NOTES.md) for what the current version does.

## How it's built

| | |
|---|---|
| UI | WPF on .NET 8 (C#), Windows 10/11 |
| Storage | SQLite |
| Font | [Nunito](https://github.com/googlefonts/nunito) (SIL Open Font License, bundled) |

```
src/Rev.Core          the "thinking": scheduling, cards, Anki, backup, stats (no screens; tested)
src/Rev.App           the Windows app: screens, theme, controls
tests/Rev.Core.Tests  automatic checks for Rev.Core
tools/                helpers: make-icon.ps1, Rev.Demo (sample data)
```

## Building it yourself

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then in this folder:

```
dotnet test              # runs the automatic checks
dotnet run --project src/Rev.App
```

Rev keeps its data in `%LOCALAPPDATA%\Rev\rev.db`. For testing, two switches keep your real data out of the way:

| | |
|---|---|
| `REV_DATA_DIR` | use this folder for the database instead |
| `REV_THEME` | `dark` or `light`, instead of following Windows |

The Anki import tests use small made-up Anki files in `tests/Fixtures`, made with Anki's own Python
library (`tools/make-anki-fixtures.py`, which explains how to run it). It builds a throw-away collection
in a temporary folder and never touches a real Anki collection. `tools/check-anki-export.py` does the
opposite check: it imports a .apkg made by Rev into a throw-away collection and prints what Anki made of it
(`dotnet run --project tools/Rev.Demo -- <empty folder> --export` makes such a file).

To try Rev with sample veterinary decks:

```
dotnet run --project tools/Rev.Demo -- C:\Temp\rev-demo
$env:REV_DATA_DIR = "C:\Temp\rev-demo"; dotnet run --project src/Rev.App
```
