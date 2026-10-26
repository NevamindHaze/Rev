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
tools/                small helper scripts (e.g. make-icon.ps1)
```

## Building it yourself

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then in this folder:

```
dotnet test              # runs the automatic checks
dotnet run --project src/Rev.App
```
