#!/usr/bin/env bash
# The fresh-install flow, in sandboxes — the Linux half of `testenv.ps1 flow`. Runs on WSL or the
# Deck; testenv.ps1 copies a bundle here first (the two launchers, a mirror of a release, the codes).
#
# The rest of the rig runs a pre-built agent beside the real one. This tests what comes BEFORE an
# agent exists — the .desktop launchers, install.sh, the menu entry and its savelocker:// handler,
# enrolling — and every one of those writes to $HOME. So each flow gets a throwaway HOME of its own
# under $FLOW_ROOT, and install.sh's test-only SAVELOCKER_INSTALL_SANDBOX keeps it off the one thing
# $HOME does not separate: the user's systemd manager. Nothing here can reach the real
# ~/.local/share/SaveLocker, its savelocker.service, its port 5178, its menu entry or Steam.
#
#   stage   two sandboxed launchers on ~/Desktop (when there is one) for double-clicking by hand
#   run     the same two, run headless, then checked (WSL's default; -Headless on the Deck)
#   clean   stop the sandboxes' daemons and delete $FLOW_ROOT and the desktop launchers
set -uo pipefail

CMD="${1:-stage}"
FLOW_ROOT="${SAVELOCKER_FLOW_ROOT:-$HOME/savelocker-flow}"
FLOW_ROOT="${FLOW_ROOT/#\~/$HOME}"
BUNDLE="${SAVELOCKER_FLOW_BUNDLE:-/tmp/savelocker-flow-bundle}"
JOIN_PORT="${SAVELOCKER_FLOW_JOIN_PORT:-5175}"
GITHUB_PORT="${SAVELOCKER_FLOW_GITHUB_PORT:-5174}"
LINK_URL="${SAVELOCKER_FLOW_LINK_URL:-}"

die() { echo "ERROR: $*" >&2; exit 1; }

# Every subcommand can reach an rm -rf of FLOW_ROOT, which Windows passed in.
case "$FLOW_ROOT" in
  */savelocker-flow) ;;
  *) die "refusing to operate — FLOW_ROOT '$FLOW_ROOT' does not end in /savelocker-flow" ;;
esac

JOIN_HOME="$FLOW_ROOT/join-home"
GITHUB_HOME="$FLOW_ROOT/github-home"
DESKTOP_DIR="$HOME/Desktop"
DESKTOP_GLOB="SaveLocker TEST - "

# The environment a launcher runs in: its own HOME and XDG dirs (XDG_DATA_HOME too — the agent reads
# it before HOME), the sandbox port, and the release mirror instead of GitHub.
sandbox_env() {
  local home="$1" port="$2"
  printf 'env HOME=%s XDG_DATA_HOME=%s/.local/share XDG_CONFIG_HOME=%s/.config XDG_CACHE_HOME=%s/.cache SAVELOCKER_INSTALL_SANDBOX=%s SAVELOCKER_RELEASE_BASE=file://%s/release' \
    "$home" "$home" "$home" "$home" "$port" "$FLOW_ROOT"
}

# A launcher as the server or the release ships it, rewritten to run inside a sandbox. Only the
# Exec gains an `env …` prefix — the part under test, the `sh -c '…'` the real file carries, is
# left byte for byte.
sandbox_launcher() {
  local src="$1" dst="$2" home="$3" port="$4" label="$5"
  sed -e "s|^Exec=|Exec=$(sandbox_env "$home" "$port") |" \
      -e "s|^Name=.*|Name=SaveLocker TEST - ${label}|" "$src" > "$dst"
}

cmd_prepare() {
  [ -d "$BUNDLE" ] || die "no bundle at $BUNDLE — run it through testenv.ps1 flow"
  reset_sandboxes
  mkdir -p "$JOIN_HOME" "$GITHUB_HOME" "$FLOW_ROOT/launchers" "$FLOW_ROOT/release"
  cp "$BUNDLE"/release/* "$FLOW_ROOT/release/"
  cp "$BUNDLE/run-desktop-launcher.py" "$FLOW_ROOT/"
  sandbox_launcher "$BUNDLE/join.desktop" "$FLOW_ROOT/launchers/join-link.desktop" "$JOIN_HOME" "$JOIN_PORT" "join link"
  sandbox_launcher "$BUNDLE/SaveLocker-Installer.desktop" "$FLOW_ROOT/launchers/github-installer.desktop" "$GITHUB_HOME" "$GITHUB_PORT" "GitHub installer"

  # The savelocker:// hop, inside the GitHub sandbox: what a browser does when the pairing page's
  # "Open the agent and finish" is clicked, minus the browser. gio is GLib's opener (it reads the
  # sandbox's mimeapps.list); xdg-open is the fallback.
  cat > "$FLOW_ROOT/open-link.sh" <<EOF
#!/bin/sh
# Opens the GitHub sandbox's agent on its Join card through the savelocker:// handler.
link='$LINK_URL'
export $(sandbox_env "$GITHUB_HOME" "$GITHUB_PORT" | sed 's/^env //')
if command -v gio >/dev/null 2>&1; then exec gio open "\$link"; else exec xdg-open "\$link"; fi
EOF
  chmod +x "$FLOW_ROOT/open-link.sh"
}

cmd_stage() {
  cmd_prepare
  if [ -d "$DESKTOP_DIR" ]; then
    # Not executable on purpose: a downloaded launcher is not, and KDE's "trust this?" prompt is
    # part of what this is for.
    cp "$FLOW_ROOT/launchers/join-link.desktop" "$DESKTOP_DIR/${DESKTOP_GLOB}join link.desktop"
    cp "$FLOW_ROOT/launchers/github-installer.desktop" "$DESKTOP_DIR/${DESKTOP_GLOB}GitHub installer.desktop"
    echo "staged on $DESKTOP_DIR: '${DESKTOP_GLOB}join link' and '${DESKTOP_GLOB}GitHub installer'"
  else
    echo "no $DESKTOP_DIR here — launchers are in $FLOW_ROOT/launchers"
  fi
  echo "savelocker:// check, after the GitHub installer:  $FLOW_ROOT/open-link.sh"
}

pass=0; fail=0
check() {
  if eval "$2"; then echo "PASS: $1"; pass=$((pass + 1)); else echo "FAIL: $1"; fail=$((fail + 1)); fi
}

cmd_run() {
  cmd_prepare
  local runner="$FLOW_ROOT/run-desktop-launcher.py"

  echo "== join-link launcher (sandbox $JOIN_HOME, :$JOIN_PORT)"
  python3 "$runner" "$FLOW_ROOT/launchers/join-link.desktop" 2>&1 | tee "$FLOW_ROOT/join.log"
  echo "== GitHub installer (sandbox $GITHUB_HOME, :$GITHUB_PORT)"
  python3 "$runner" "$FLOW_ROOT/launchers/github-installer.desktop" 2>&1 | tee "$FLOW_ROOT/github.log"

  local j="$JOIN_HOME/.local/share/SaveLocker" g="$GITHUB_HOME/.local/share/SaveLocker"
  echo
  check "join link: the agent is installed in its sandbox"     '[ -x "$j/savelocker" ]'
  check "join link: the device is enrolled"                    'grep -q "\"ApiKey\": *\"" "$j/config.json"'
  check "join link: its daemon runs on the sandbox port"       'curl -sf "http://127.0.0.1:$JOIN_PORT/" >/dev/null'
  check "join link: the menu entry handles savelocker://"      'grep -qx "MimeType=x-scheme-handler/savelocker;" "$JOIN_HOME/.local/share/applications/savelocker.desktop"'
  check "GitHub: installed from the release mirror"            '[ -x "$g/savelocker" ] && grep -q "Downloading savelocker-" "$FLOW_ROOT/github.log"'
  check "GitHub: the checksum was checked (no mismatch)"       '! grep -q "does not match" "$FLOW_ROOT/github.log"'
  check "GitHub: not enrolled — it ends at the Join step"      'grep -q "Last step: join your server" "$FLOW_ROOT/github.log" && ! grep -q "\"ApiKey\": *\"" "$g/config.json" 2>/dev/null'
  check "GitHub: savelocker:// is this sandbox's menu entry"         'grep -q "x-scheme-handler/savelocker=savelocker.desktop" "$GITHUB_HOME/.config/mimeapps.list" 2>/dev/null || ! command -v xdg-mime >/dev/null'

  if [ -n "$LINK_URL" ]; then
    # What the menu entry runs for the link. Where there is a desktop session it opens the window
    # (look at it: the Join card, pre-filled); with none it prints the address it would have opened.
    # Either way it must accept the link — a refused one exits 2.
    local opened status
    opened="$(env $(sandbox_env "$GITHUB_HOME" "$GITHUB_PORT" | sed 's/^env //') "$g/savelocker" open "$LINK_URL" 2>&1)"
    status=$?
    echo "$opened" | sed 's/^/    /'
    check "GitHub: open accepts the savelocker:// link" '[ "$status" -ne 2 ] && ! echo "$opened" | grep -q "Not a SaveLocker link"'

    # The click on that card: the same local API call the agent UI's Join button makes.
    local join_url token
    join_url="$(python3 -c 'import sys,urllib.parse as u;print(u.parse_qs(u.urlsplit(sys.argv[1]).query)["link"][0])' "$LINK_URL")"
    token="$(cat "$g/api-token" 2>/dev/null)"
    curl -s -X POST -H "Content-Type: application/json" -H "X-SaveLocker-Token: $token"       -d "{\"url\":\"$join_url\"}" "http://127.0.0.1:$GITHUB_PORT/api/join" > "$FLOW_ROOT/join-click.json"
    check "GitHub: Join on that card enrolls the device" 'grep -q "\"ApiKey\": *\"" "$g/config.json"'
  fi

  echo
  echo "Flow: $pass passed, $fail failed.  (daemons left running for a look; testenv.ps1 clean stops them)"
  [ "$fail" -eq 0 ]
}

# Each run starts from nothing: a sandbox left from the last one would make "installed" and
# "enrolled" true before anything ran.
reset_sandboxes() {
  pkill -f "^$FLOW_ROOT/" 2>/dev/null
  if [ -d "$DESKTOP_DIR" ]; then rm -f "$DESKTOP_DIR/${DESKTOP_GLOB}"*.desktop; fi
  if [ -d "$FLOW_ROOT" ]; then rm -rf "$FLOW_ROOT"; echo "removed $FLOW_ROOT"; fi
  return 0
}

cmd_clean() {
  reset_sandboxes
  rm -rf "$BUNDLE"
}

case "$CMD" in
  stage) cmd_stage ;;
  run)   cmd_run ;;
  clean) cmd_clean ;;
  *)     die "unknown subcommand: $CMD (stage | run | clean)" ;;
esac
