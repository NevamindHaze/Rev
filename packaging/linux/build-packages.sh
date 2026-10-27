#!/usr/bin/env bash
# Makes the two Linux downloads from a "dotnet publish" folder (run on Linux, e.g. GitHub's Ubuntu machines):
#   rev-flashcards_<version>_amd64.deb   for Linux Mint / Ubuntu / Debian: double-click it to install
#   Rev-<version>-linux-x64.tar.gz       for any Linux: unpack it anywhere and run ./Rev
#
# Usage: packaging/linux/build-packages.sh <version> <publish folder> <output folder>
set -euo pipefail
version="$1"
publish="$2"
out="$3"
here="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$out"

# ---- The .deb: Rev goes in /usr/lib/rev, with a "rev-cards" command and an entry in the app menu.
# (Not "rev": Linux already has a small command of that name, which prints text backwards.)
root="$(mktemp -d)"
mkdir -p "$root/DEBIAN" "$root/usr/lib/rev" "$root/usr/bin" "$root/usr/share/applications" "$root/usr/share/pixmaps"
cp -r "$publish"/. "$root/usr/lib/rev/"
chmod 755 "$root/usr/lib/rev/Rev"
ln -s ../lib/rev/Rev "$root/usr/bin/rev-cards"
cp "$here/rev.desktop" "$root/usr/share/applications/rev.desktop"
# The comic icon, in the sizes the menu and the panel use.
icon="$here/../../src/Rev.App/Assets/AppIcon.png"
cp "$icon" "$root/usr/share/pixmaps/rev.png"
if command -v convert >/dev/null; then
  for size in 48 64 128 256 512; do
    mkdir -p "$root/usr/share/icons/hicolor/${size}x${size}/apps"
    convert "$icon" -resize "${size}x${size}" "$root/usr/share/icons/hicolor/${size}x${size}/apps/rev.png"
  done
fi
installed=$(du -sk "$root/usr" | cut -f1)
cat > "$root/DEBIAN/control" <<CONTROL
Package: rev-flashcards
Version: $version
Section: education
Priority: optional
Architecture: amd64
Maintainer: NevamindHaze <noreply@users.noreply.github.com>
Installed-Size: $installed
Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libssl3 | libssl1.1, libfontconfig1, libx11-6, libice6, libsm6
Homepage: https://github.com/NevamindHaze/Rev-Windows
Description: Flashcards with spaced repetition that sync with Anki
 Rev is a calm flashcard app: decks, basic / reversed / cloze / type-in cards,
 reviews scheduled with FSRS (or SM-2), stats, Anki import and export, JSON
 backups, and two-way sync with Anki (and Rev on iPhone, through Anki).
CONTROL
# Refresh the app menu and icons after installing or removing.
cat > "$root/DEBIAN/postinst" <<'SCRIPT'
#!/bin/sh
set -e
command -v update-desktop-database >/dev/null && update-desktop-database -q /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true
SCRIPT
cp "$root/DEBIAN/postinst" "$root/DEBIAN/postrm"
chmod 755 "$root/DEBIAN/postinst" "$root/DEBIAN/postrm"
dpkg-deb --build --root-owner-group "$root" "$out/rev-flashcards_${version}_amd64.deb"
rm -rf "$root"

# ---- The .tar.gz: the same program in a "Rev" folder.
staging="$(mktemp -d)"
mkdir -p "$staging/Rev"
cp -r "$publish"/. "$staging/Rev/"
cp "$here/rev.desktop" "$icon" "$staging/Rev/"
chmod 755 "$staging/Rev/Rev"
tar -C "$staging" -czf "$out/Rev-${version}-linux-x64.tar.gz" Rev
rm -rf "$staging"

ls -l "$out"
