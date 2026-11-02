#!/usr/bin/env bash
# Checks build-apt-repo.sh with the real apt, without installing anything or needing root: a computer with an old
# Rev must be offered the new one, and must refuse the repository if it has been changed or signed by another key.
#
# Usage: packaging/linux/test-apt-repo.sh <a rev-flashcards .deb>
set -euo pipefail
deb="$(realpath "$1")"
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# A throwaway signing key, never the real one.
export GNUPGHOME="$work/gnupg"
mkdir -m 700 "$GNUPGHOME"
gpg --batch --quiet --passphrase '' --quick-generate-key "Rev apt test" ed25519 sign never

# The same package as version 9.9.9. It has no dependencies, because the pretend computer has no other packages.
mkdir "$work/unpacked"
dpkg-deb -R "$deb" "$work/unpacked"
sed -i -e 's/^Version: .*/Version: 9.9.9/' -e '/^Depends:/d' "$work/unpacked/DEBIAN/control"
dpkg-deb -b -Zgzip -z1 "$work/unpacked" "$work/rev-flashcards_9.9.9_amd64.deb" > /dev/null
bash "$here/build-apt-repo.sh" "$work/rev-flashcards_9.9.9_amd64.deb" "$work/repo"

# A pretend computer: apt's folders in the test folder, with Rev 1.0.0 as the only thing installed.
mkdir -p "$work/state/lists/partial" "$work/cache/archives/partial" "$work/etc/apt.conf.d" "$work/etc/preferences.d" "$work/etc/sources.list.d"
cat > "$work/status" <<'EOF'
Package: rev-flashcards
Status: install ok installed
Version: 1.0.0
Architecture: amd64
Maintainer: Rev
Description: Rev

EOF
echo "deb [signed-by=$work/keyring.gpg] file:$work/repo ./" > "$work/etc/sources.list"
cp "$work/repo/rev.gpg" "$work/keyring.gpg"
pretend_apt() {
  "$@" -o Dir::Etc="$work/etc" -o Dir::Etc::sourcelist="$work/etc/sources.list" -o Dir::Etc::sourceparts="$work/etc/sources.list.d" \
    -o Dir::State="$work/state" -o Dir::State::status="$work/status" -o Dir::Cache="$work/cache" \
    -o Debug::NoLocking=1 -o APT::Sandbox::User="$(id -un)" -o APT::Architecture=amd64
}

echo "--- A computer with an old Rev is offered the new one"
pretend_apt apt-get update -q
pretend_apt apt-get -s upgrade | tee "$work/upgrade.txt"
grep -q '^Inst rev-flashcards \[1.0.0\] (9.9.9' "$work/upgrade.txt"

echo "--- A changed repository is refused"
cp "$work/repo/InRelease" "$work/InRelease.good"
sed -i 's/^Label: Rev/Label: Evil/' "$work/repo/InRelease"
rm -f "$work/repo/Release" "$work/repo/Release.gpg"
rm -rf "$work/state/lists"; mkdir -p "$work/state/lists/partial"
pretend_apt apt-get update -q > "$work/changed.txt" 2>&1 || true
grep -q 'BADSIG' "$work/changed.txt" || { cat "$work/changed.txt"; echo "Changed repository wasn't reported"; exit 1; }
if pretend_apt apt-get -s upgrade 2>/dev/null | grep -q '^Inst rev-flashcards'; then echo "Changed repository was accepted"; exit 1; fi

echo "--- A repository signed by another key is refused"
cp "$work/InRelease.good" "$work/repo/InRelease"
gpg --batch --quiet --passphrase '' --quick-generate-key "Someone else" ed25519 sign never
gpg --batch --yes --export "Someone else" > "$work/keyring.gpg"
rm -rf "$work/state/lists"; mkdir -p "$work/state/lists/partial"
pretend_apt apt-get update -q > "$work/otherkey.txt" 2>&1 || true
grep -q 'NO_PUBKEY\|EXPKEYSIG\|BADSIG\|not signed\|invalid' "$work/otherkey.txt" || { cat "$work/otherkey.txt"; echo "Wrong key wasn't reported"; exit 1; }
if pretend_apt apt-get -s upgrade 2>/dev/null | grep -q '^Inst rev-flashcards'; then cat "$work/otherkey.txt"; echo "Wrong key was accepted"; exit 1; fi

echo "APT repository checks passed"
