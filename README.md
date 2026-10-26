<p align="center">
  <img src="src/Rev.App/Assets/AppIcon.png" width="140" alt="Rev app icon">
</p>

<h1 align="center">Rev for Windows</h1>

<p align="center">
  <b>The desktop sibling of <a href="https://github.com/NevamindHaze/Rev">Rev for iPhone</a>:
  a calm, warm spaced-repetition flashcard app.</b><br>
  Built for studying veterinary medicine, with FSRS scheduling and Anki import/export.
</p>

## Install

Download **Rev-Setup-x.y.z.exe** from the [latest release](https://github.com/NevamindHaze/Rev-Windows/releases/latest)
and open it. It installs Rev just for you (no administrator needed) and adds it to the Start menu.
Prefer no installer? **Rev-x.y.z-portable.exe** is the whole app in one file: put it anywhere and open it.

Rev isn't signed with a paid certificate, so Windows may first say "Windows protected your PC":
click **More info → Run anyway**. Your decks and progress are kept in `%LOCALAPPDATA%\Rev`, so updating or
uninstalling Rev never touches them (make a backup in Settings now and then anyway).

See [`RELEASE_NOTES.md`](RELEASE_NOTES.md) for what's new.

## How it's built

| | |
|---|---|
| UI | WPF on .NET 8 (C#), Windows 10/11 |
| Storage | SQLite |
| Font | [Nunito](https://github.com/googlefonts/nunito) (SIL Open Font License, bundled) |
| Libraries | [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite), [ZstdSharp](https://github.com/oleg-st/ZstdSharp) |
| Builds | GitHub Actions on Windows ([`build.yml`](.github/workflows/build.yml)): tests, a one-file `Rev.exe` and an [Inno Setup](https://jrsoftware.org/isinfo.php) installer ([`installer/Rev.iss`](installer/Rev.iss)) on every push |
| Release notes | [`RELEASE_NOTES.md`](RELEASE_NOTES.md), the text of each GitHub release |

```
src/Rev.Core          the "thinking": scheduling, cards, Anki, backup, stats (no screens; tested)
src/Rev.App           the Windows app: screens, theme, controls
tests/Rev.Core.Tests  automatic checks for Rev.Core
tools/                helpers: make-icon.ps1, Rev.Demo (sample data), Anki test scripts
docs/                 SYNC-PROPOSAL.md: how Rev for Windows could join the iPhone ↔ Anki sync
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

To make the one-file app yourself (it lands in `publish\Rev.exe`):

```
dotnet publish src/Rev.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

### Publishing a new version

1. Change `<Version>` in [`src/Rev.App/Rev.App.csproj`](src/Rev.App/Rev.App.csproj), e.g. to `1.1.0`.
2. Write the "What's new" for that version in [`RELEASE_NOTES.md`](RELEASE_NOTES.md).
3. Commit, then add a matching tag and push it: `git tag v1.1.0` and `git push origin v1.1.0`.

GitHub Actions then builds everything and publishes the release with both files.

To try Rev with sample veterinary decks:

```
dotnet run --project tools/Rev.Demo -- C:\Temp\rev-demo
$env:REV_DATA_DIR = "C:\Temp\rev-demo"; dotnet run --project src/Rev.App
```
