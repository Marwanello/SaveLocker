#!/usr/bin/env bash
# Build and launch the Game Mode UI (`savelocker ui`) in a real window under WSLg.
#
# Why: on-hardware Deck testing is vital but slow — push a branch, wait for CI, download a tarball,
# re-run install.sh. WSLg has a Wayland/X display and a GL stack, so layout, palette, typography,
# spacing and widget states can be iterated on the dev box in seconds instead.
#
# WHAT THIS DOES NOT VALIDATE — these are Deck-only, no exceptions:
#   * gamepad input via Steam Input (the ButtonDown-event workaround in UiApp.HookPad exists
#     precisely because this behaves differently on real hardware — see Gotchas.md)
#   * gamescope compositing, and the on-screen-keyboard suppression StopTextInput handles
#   * the clipboard crossing into Steam's Launch Options field
#   * real font legibility at arm's length on a 7" panel. A 1280x800 window on a 27" monitor is NOT
#     the same angular size. This is the easiest thing here to get wrong.
#
# Mesa/d3d12 under WSLg also differs from the Deck's native Mesa on GL edge cases. Treat a
# WSLg-only rendering artifact as suspect until it reproduces on hardware, and vice versa.
#
# Usage (from inside WSL):
#   bash tests/linux/run-ui-wslg.sh              # 1280x800, the Deck's native panel size
#   bash tests/linux/run-ui-wslg.sh 1024x640     # other sizes, to sanity-check the layout scales
#   bash tests/linux/run-ui-wslg.sh --no-build   # skip the build, just launch

set -euo pipefail

SIZE="1280x800"
BUILD=1
SHOT=""
EXTRA=""
while [ $# -gt 0 ]; do
  case "$1" in
    --no-build)   BUILD=0 ;;
    --screenshot) SHOT="${2:?--screenshot needs a path}"; shift ;;
    --gallery)    EXTRA="$EXTRA --gallery" ;;   # every widget in every state, the Phase B gate
    --screen)     EXTRA="$EXTRA --screen ${2:?--screen needs a name}"; shift ;;
    *x*)          SIZE="$1" ;;
    *) echo "Unknown argument '$1'. Usage: run-ui-wslg.sh [WxH] [--no-build] [--gallery] [--screenshot out.png]" >&2; exit 2 ;;
  esac
  shift
done

# dotnet is not on a non-interactive PATH in this WSL image (CONTEXT.md).
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$HOME/.local/bin:$PATH"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet not found. Expected it at \$DOTNET_ROOT ($DOTNET_ROOT)." >&2
  exit 1
fi

# Fail with something readable rather than a native SDL stack trace 20 frames deep.
if [ -z "${WAYLAND_DISPLAY:-}" ] && [ -z "${DISPLAY:-}" ]; then
  cat >&2 <<'EOF'
No display available: both WAYLAND_DISPLAY and DISPLAY are unset.

This script needs WSLg (or any X/Wayland session). WSLg ships with WSL2 on Windows 11 and is
normally automatic — if it is missing, check from Windows:
    wsl --update      then      wsl --shutdown
and confirm /mnt/wslg exists inside the distro.

The UI cannot be tested headlessly; there is no software-rendering fallback wired up.
EOF
  exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

if [ "$BUILD" -eq 1 ]; then
  # --no-incremental: stale DLL reuse has masked changes in this repo before (CONTEXT.md).
  #
  # -r linux-x64 is REQUIRED, not an optimisation. The csproj is deliberately RID-agnostic so it
  # builds anywhere, but a RID-agnostic build does not map runtimes/linux-x64/native into
  # deps.json, so the host never resolves the bundled libSDL2 — and Silk reports that as the
  # thoroughly misleading "Couldn't find a suitable window platform. (SdlPlatform - not
  # applicable)", which reads like a missing display rather than a missing native library.
  # --no-self-contained keeps this a fast dev build; releases publish self-contained.
  echo "Building savelocker for linux-x64 (--no-incremental)..."
  dotnet build src/Agent.Linux/SaveLocker.Agent.Linux.csproj \
    -r linux-x64 --no-self-contained --no-incremental -v quiet --nologo
fi

BIN="$REPO_ROOT/src/Agent.Linux/bin/Debug/net10.0/linux-x64/savelocker"
if [ ! -x "$BIN" ]; then
  echo "Built binary not found at $BIN" >&2
  exit 1
fi

# Use a scratch config so a dev run cannot touch a real enrolled agent's state.
CONFIG="${SAVELOCKER_UI_TEST_CONFIG:-$REPO_ROOT/.wslg-ui/config.json}"
mkdir -p "$(dirname "$CONFIG")"

if [ -n "$SHOT" ]; then
  # Capture-and-exit: no window to interact with, so this works unattended.
  echo "Capturing $SIZE screenshot to $SHOT"
  exec "$BIN" ui --size "$SIZE" --config "$CONFIG" --screenshot "$SHOT" $EXTRA
fi

echo "Launching UI at $SIZE  (config: $CONFIG)"
echo "Keyboard nav mirrors gamepad nav: arrows move focus, Enter activates, Escape backs out."
exec "$BIN" ui --size "$SIZE" --config "$CONFIG" $EXTRA
