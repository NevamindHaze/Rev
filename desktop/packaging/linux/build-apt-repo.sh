#!/usr/bin/env bash
# Makes the signed APT repository that lets "sudo apt upgrade" update Rev: one .deb plus the index files apt reads.
# It signs with the secret key in gpg's keyring, and also writes the public key as rev.gpg for people to install.
#
# Usage: packaging/linux/build-apt-repo.sh <the .deb> <output folder>
set -euo pipefail
deb="$1"
out="$2"
mkdir -p "$out"
cp "$deb" "$out/"
cd "$out"

apt-ftparchive packages . > Packages
gzip -9kf Packages

# Written elsewhere first, or the half-made Release file would list itself.
release="$(mktemp)"
apt-ftparchive \
  -o APT::FTPArchive::Release::Origin=Rev \
  -o APT::FTPArchive::Release::Label=Rev \
  -o APT::FTPArchive::Release::Suite=stable \
  -o APT::FTPArchive::Release::Codename=rev \
  release . > "$release"
mv "$release" Release
chmod 644 Release

gpg --batch --yes --armor --detach-sign -o Release.gpg Release
gpg --batch --yes --clearsign -o InRelease Release
gpg --batch --yes --export -o rev.gpg

# Tells GitHub Pages to serve the files as they are.
touch .nojekyll
