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
#
# Navigation work: --nav-debug overlays the live nav cursor state (focused item, the child window
# that owns it, and whether a focus request is armed). Combine with --nav to script presses and
# --screenshot to capture where the cursor landed, e.g.
#   bash tests/linux/run-ui-wslg.sh --fixtures --nav-debug --nav right,left --screenshot /tmp/x.png

set -euo pipefail

SIZE="1280x800"
BUILD=1
SHOT=""
EXTRA=""
FIXTURES=0
while [ $# -gt 0 ]; do
  case "$1" in
    --no-build)   BUILD=0 ;;
    --screenshot) SHOT="${2:?--screenshot needs a path}"; shift ;;
    --gallery)    EXTRA="$EXTRA --gallery" ;;   # every widget in every state, the Phase B gate
    --screen)     EXTRA="$EXTRA --screen ${2:?--screen needs a name}"; shift ;;
    --fixtures)   FIXTURES=1 ;;
    --autoscan)   EXTRA="$EXTRA --autoscan" ;;
    --nav)        EXTRA="$EXTRA --nav ${2:?--nav needs a sequence, e.g. right,down,a}"; shift ;;
    --nav-debug)  EXTRA="$EXTRA --nav-debug" ;;   # live focus/zone/request read-out, top right
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

if [ "$FIXTURES" -eq 1 ]; then
  # Reuse the Linux harness's fake-game fixtures so the POPULATED screens can be reviewed. Without
  # this, a dev box has no Proton prefixes and no enrolment, so Add game and Set save folder only
  # ever render their empty and gated states — which is exactly where layout bugs hide.
  #
  # The API key is a placeholder: nothing here talks to a server, it only opens the enrolment gate
  # so the candidate list renders. Never point --fixtures at a real config.
  SCRATCH="$REPO_ROOT/.wslg-ui/fixtures"
  rm -rf "$SCRATCH"; mkdir -p "$SCRATCH"
  eval "$(python3 "$REPO_ROOT/tests/linux/make-fixtures.py" "$SCRATCH")"
  export HOME="${HOME_DIR}"
  export XDG_DATA_HOME="${HOME_DIR}/.local/share"

  # Tracked games and a lease warning are seeded too, so Overview's game list, the Settings
  # screen's remove flow and the conflict banner all render populated rather than empty.
  CONFIG="$SCRATCH/ui-config.json"
  cat > "$CONFIG" <<JSON
{
  "ServerUrl": "http://localhost:5179",
  "MachineName": "DECK-FIXTURE",
  "ApiKey": "fixture-key-not-a-real-credential",
  "SettleQuietSeconds": 10,
  "TotalSavesPushed": 348,
  "LastSyncTime": "$(date -u -d '4 minutes ago' +%Y-%m-%dT%H:%M:%SZ)",
  "Games": [
    { "Name": "Hollow Knight", "SaveDirectory": "$HOME/.local/share/Steam/steamapps/compatdata/367520/pfx/drive_c/users/steamuser/AppData/LocalLow/Team Cherry/Hollow Knight" },
    { "Name": "Hades", "SaveDirectory": "" }
  ]
}
JSON

  # The store lives beside the config (AgentConfig.StateDir), same as the real agent.
  cat > "$SCRATCH/lease-warnings.json" <<JSON
[
  {
    "GameName": "Hollow Knight",
    "HolderMachine": "WIDEBOY",
    "RaisedAt": "$(date -u +%Y-%m-%dT%H:%M:%S+00:00)"
  }
]
JSON
  # Plant fake SteamOS UI sounds so Ui/Wav.cs actually runs. Deliberately 44100 Hz MONO 16-bit,
  # while the audio device is opened at 48000 Hz stereo — that forces the decoder through both the
  # resample and the channel fan-out paths on every fixture run. Written with Python's `wave`
  # module, an implementation independent of ours, so the test cannot pass by sharing our bugs.
  SOUNDS="$HOME/.local/share/Steam/steamui/sounds"
  mkdir -p "$SOUNDS" "$HOME/.local/share/Steam/userdata"   # userdata/ is what marks a Steam root
  python3 - "$SOUNDS" <<'PY'
import math, struct, sys, wave, os
out = sys.argv[1]
for name, hz in [("deck_ui_navigation", 1000), ("deck_ui_default_activation", 700),
                 ("deck_ui_hide_modal", 500), ("deck_ui_switch_toggle_on", 850)]:
    rate, secs = 44100, 0.04
    with wave.open(os.path.join(out, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(rate)
        frames = int(rate * secs)
        w.writeframes(b"".join(
            struct.pack("<h", int(math.sin(2 * math.pi * hz * i / rate)
                                  * math.exp(-5 * i / frames) * 12000))
            for i in range(frames)))
PY

  echo "Fixtures in $SCRATCH (fake HOME=$HOME)"
fi

if [ -n "$SHOT" ]; then
  # Capture-and-exit: no window to interact with, so this works unattended.
  echo "Capturing $SIZE screenshot to $SHOT"
  exec "$BIN" ui --size "$SIZE" --config "$CONFIG" --screenshot "$SHOT" $EXTRA
fi

echo "Launching UI at $SIZE  (config: $CONFIG)"
echo "Keyboard nav mirrors gamepad nav: arrows move focus, Enter activates, Escape backs out."
exec "$BIN" ui --size "$SIZE" --config "$CONFIG" $EXTRA
