#!/bin/sh
# SaveLocker — install the Linux agent from the latest GitHub release.
#
# What SaveLocker-Installer.desktop (the release's double-click installer) downloads and runs; also
# fine on its own:  curl -fsSL <release>/savelocker-install.sh | sh
# It finds the tarball through the release's SHA256SUMS-linux.txt (the tarball's name carries the
# version, so "latest/download" cannot name it directly), checks the download against that sum, then
# runs the tarball's own install.sh. Enrolling is the console's join link, so it ends by opening the
# agent's Join card rather than asking for anything here.
#
# Rendered by build-linux.sh: @REPO@ becomes the GitHub repository the release lives in.
# SAVELOCKER_RELEASE_BASE (test-only) points it at another copy of a release — a file:// folder works.
set -e

BASE="${SAVELOCKER_RELEASE_BASE:-https://github.com/@REPO@/releases/latest/download}"

say() { printf '%s\n' "$*"; }
fail() { say "!! $*"; exit 1; }

command -v curl >/dev/null 2>&1 || fail "curl is required to install SaveLocker."
command -v sha256sum >/dev/null 2>&1 || fail "sha256sum is required to check the download."

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT INT TERM

say "==> Finding the latest SaveLocker release..."
curl -fsSL "$BASE/SHA256SUMS-linux.txt" -o "$TMP/SHA256SUMS-linux.txt" \
  || fail "Could not reach $BASE — check the internet connection."
NAME="$(awk '$2 ~ /linux-x64\.tar\.gz$/ { sub(/^\*/, "", $2); print $2; exit }' "$TMP/SHA256SUMS-linux.txt")"
[ -n "$NAME" ] || fail "The release lists no Linux package."
case "$NAME" in */*|..*) fail "Unexpected package name: $NAME" ;; esac

say "==> Downloading $NAME ..."
curl -fL --progress-bar "$BASE/$NAME" -o "$TMP/$NAME"
( cd "$TMP" && grep " \*\{0,1\}$NAME\$" SHA256SUMS-linux.txt | sha256sum -c --status - ) \
  || fail "The download does not match the release's checksum — not installing it."

say "==> Unpacking..."
mkdir -p "$TMP/out"
tar -xzf "$TMP/$NAME" -C "$TMP/out"
[ -x "$TMP/out/SaveLocker/install.sh" ] || fail "The download does not look like a SaveLocker release."

( cd "$TMP/out/SaveLocker" && ./install.sh )

AGENT="$HOME/.local/share/SaveLocker/savelocker"
CONFIG="$HOME/.local/share/SaveLocker/config.json"
say ""
if [ -f "$CONFIG" ] && grep -q '"ApiKey": *"' "$CONFIG"; then
  say "==> This device is already enrolled — the agent was updated in place."
  exit 0
fi

say "==> Last step: join your server."
say "    In the SaveLocker console: Configuration -> Enroll a machine -> Create join link."
say "    Open that link on this device, or paste it into the Join card that opens now."
"$AGENT" open --view enroll >/dev/null 2>&1 \
  || say "    (Could not open the window here — open SaveLocker from the application menu, Settings.)"
