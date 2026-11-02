# Rev on Linux Mint

Rev for Linux is the same app as Rev for Windows: the same screens, keys, decks, sync and files. It runs on
Linux Mint 21 and 22 (and Ubuntu, Debian and most other 64-bit Linux).

## 1. Install Rev

1. From the [latest release](https://github.com/NevamindHaze/Rev/releases?q=%22for+Windows+and+Linux%22&expanded=true), download
   **rev-flashcards_<version>_amd64.deb**.
2. Double-click it in your Downloads folder. Mint's package installer opens: click **Install package** and type
   your password.
3. Open Rev from the menu (it's under **Education**, or type "Rev" in the menu's search).

To update later, download the new `.deb` and double-click it the same way: your decks stay.
To uninstall: Menu → Software Manager isn't needed; in a terminal, `sudo apt remove rev-flashcards`
(your decks stay in `~/.local/share/Rev`).

Not a Mint/Ubuntu/Debian Linux? Download **Rev-<version>-linux-x64.tar.gz** instead, unpack it anywhere, and run
`./Rev` inside the `Rev` folder.

From a terminal, Rev starts with the command `rev-cards`. (Not `rev`: Linux already has a small command of that
name.)

## 2. Bring your decks from Windows

The simplest way, with everything (decks, cards, progress, history, settings):

1. **On Windows**, in Rev: Settings → Backup → **Export backup (.json)…**. Save it on a USB stick or in a cloud
   folder.
2. **On Mint**, in Rev: Settings → Backup → **Restore from backup…** and choose that file.

Images aren't inside the backup file. If your cards have pictures, also import your Anki decks again in Rev
(Settings → Anki → Import), which brings the pictures back and keeps your progress. If you set up the sync below,
the pictures come from Anki by themselves.

## 3. Anki and the Rev Sync add-on on Linux

1. Install Anki for Linux from [apps.ankiweb.net](https://apps.ankiweb.net) and sign in to AnkiWeb, so your
   collection comes across from AnkiWeb (Anki on Windows syncs to AnkiWeb first).
2. Install the Rev Sync add-on: download the [Rev repository](https://github.com/NevamindHaze/Rev) as a ZIP,
   unzip it, and copy the `anki-addon/rev_sync` folder into Anki's add-on folder,
   `~/.local/share/Anki2/addons21/` (in the file manager, press Ctrl+H to see hidden folders like `.local`).
   Restart Anki.
3. The add-on's folder: on Linux there's no iCloud Drive, so the add-on uses **~/Rev Sync** (the newest add-on
   does this by itself; with an older one, set `"sync_folder": "~/Rev Sync"` in Tools → Add-ons → Rev Sync →
   Config).

## 4. Sync Rev with Anki

In Rev: Settings → Sync with Anki → **Choose folder…** → pick **Rev Sync** in your home folder. Rev saves a backup,
shows what the first sync will do, and starts when you agree. After that it syncs by itself, within seconds of a
change in Anki.

## 5. Rev on iPhone

Linux has no iCloud Drive, so the iPhone syncs with Anki **over your home Wi-Fi**:

1. In Anki: **Tools → Rev Sync Wi-Fi…**, tick the box, and note the computer's address and the pairing code.
2. On the iPhone: Rev → Settings → **Sync over Wi-Fi**, type them, tap **Connect**, and allow Rev to use the
   local network.

Changes then go iPhone → Anki → Rev for Linux in seconds when you're home with Anki open. Away from home, the
iPhone keeps your changes and sends them the next time it's on your Wi-Fi.

Mint's firewall is off unless you switched it on. If you did, allow port 8765 (Menu → Firewall Configuration →
Rules → Add → port 8765, TCP).

## Where things are

| | |
|---|---|
| Your decks and progress | `~/.local/share/Rev` (Settings → About → Open data folder) |
| Safety backups | `~/.local/share/Rev/Backups` |
| Sync folder | `~/Rev Sync` |
| Anki's add-ons | `~/.local/share/Anki2/addons21` |
