#!/usr/bin/env bash
# Seeds a genuine, real conflict against a throwaway scratch server + two fake machine configs, so
# the agent-ui Conflicts page / sync-time pop-up can be exercised by hand without touching a real
# server, a real game, or a real save folder. Nothing here reads or writes anything under
# ~/.local/share/SaveLocker or a real Steam library — every path lives under $STATE (default
# ~/savelocker-conflict-test), the same isolation idiom testenv.sh already uses (XDG_DATA_HOME).
#
# Usage:
#   tests/seed-test-conflict.sh up      # start scratch server, seed a conflict, start a daemon
#   tests/seed-test-conflict.sh open    # print the URL to open (or just re-print it)
#   tests/seed-test-conflict.sh again   # resolve the existing conflict, then seed a fresh one
#   tests/seed-test-conflict.sh down    # stop the daemon + server
#   tests/seed-test-conflict.sh clean   # down, then delete every file this script created
set -euo pipefail

CMD="${1:-up}"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STATE="${SAVELOCKER_CONFLICT_TEST_STATE:-$HOME/savelocker-conflict-test}"
SERVER_PORT="${SAVELOCKER_CONFLICT_TEST_SERVER_PORT:-5199}"
DAEMON_PORT="${SAVELOCKER_CONFLICT_TEST_DAEMON_PORT:-5198}"
SERVER_URL="http://localhost:$SERVER_PORT"
GAME_NAME="Conflict Test Game"

# Refuse to operate on anything that doesn't look like this script's own scratch directory — same
# guard testenv.sh uses before its rm -rf, for the same reason.
case "$STATE" in
  *savelocker-conflict-test*) ;;
  *) echo "ERROR: refusing to operate — '$STATE' doesn't look like a test directory" >&2; exit 1 ;;
esac

SERVER_LOG="$STATE/server.log"
DAEMON_LOG="$STATE/daemon.log"
SERVER_PID="$STATE/server.pid"
DAEMON_PID="$STATE/daemon.pid"
MACHINE_A="$STATE/machine-a"   # "Living Room PC" — pushes first, then the daemon runs as THIS one
MACHINE_B="$STATE/machine-b"   # "This Device" — diverges after A, is what you browse as

die() { echo "ERROR: $*" >&2; exit 1; }

dotnet_env() {
  if [ -x "$HOME/.dotnet/dotnet" ]; then
    export DOTNET_ROOT="$HOME/.dotnet"
    export PATH="$HOME/.dotnet:$PATH"
  fi
}

pid_alive() { [ -f "$1" ] && kill -0 "$(cat "$1")" 2>/dev/null; }

cli_a() { XDG_DATA_HOME="$MACHINE_A" dotnet "$REPO/src/Agent.Linux/bin/Debug/net10.0/savelocker.dll" "$@"; }
cli_b() { XDG_DATA_HOME="$MACHINE_B" dotnet "$REPO/src/Agent.Linux/bin/Debug/net10.0/savelocker.dll" "$@"; }

cmd_build() {
  dotnet_env
  echo "== building Server + Agent.Linux (--no-incremental) =="
  dotnet build "$REPO/src/Server/SaveLocker.Server.csproj" --no-incremental -v q --nologo || die "server build failed"
  dotnet build "$REPO/src/Agent.Linux/SaveLocker.Agent.Linux.csproj" --no-incremental -v q --nologo || die "agent build failed"
}

start_server() {
  pid_alive "$SERVER_PID" && { echo "server already running (pid $(cat "$SERVER_PID"))"; return; }
  dotnet_env
  mkdir -p "$STATE"
  echo "== starting scratch server on :$SERVER_PORT (db+archives under $STATE/server-data) =="
  Storage__DbPath="$STATE/server-data/savelocker.db" \
  Storage__ArchiveRoot="$STATE/server-data/archives" \
  ASPNETCORE_URLS="http://localhost:$SERVER_PORT" \
  ASPNETCORE_ENVIRONMENT="Development" \
  nohup dotnet "$REPO/src/Server/bin/Debug/net10.0/SaveLocker.Server.dll" >"$SERVER_LOG" 2>&1 &
  echo $! > "$SERVER_PID"
  for _ in $(seq 1 30); do
    curl -fsS "$SERVER_URL/api/admin/status" >/dev/null 2>&1 && { echo "server is up."; return; }
    sleep 0.5
  done
  die "server did not come up — see $SERVER_LOG"
}

seed_dummy_save() {
  local dir="$1" text="$2"
  mkdir -p "$dir"
  echo "$text" > "$dir/savefile.txt"
}

seed_conflict() {
  mkdir -p "$MACHINE_A" "$MACHINE_B"

  echo "== registering two fake machines against the scratch server =="
  cli_a set-server --url "$SERVER_URL" >/dev/null
  cli_a register --name "Living Room PC" >/dev/null
  cli_b set-server --url "$SERVER_URL" >/dev/null
  cli_b register --name "This Device" >/dev/null

  # A nonce, not fixed text: re-running against the same long-lived scratch server (e.g. `again`)
  # must always produce genuinely new content on both sides, or a push can land as a silent no-op
  # against whatever head a previous run already left behind instead of a fresh divergence.
  local nonce; nonce="$(date +%s)-$$"
  seed_dummy_save "$STATE/save-a" "progress from Living Room PC ($nonce)"
  seed_dummy_save "$STATE/save-b" "progress from This Device ($nonce)"

  echo "== A pushes first (becomes the cloud head) =="
  cli_a add-game --name "$GAME_NAME" --dir "$STATE/save-a" --force-path >/dev/null
  cli_a push "$GAME_NAME" >/dev/null

  echo "== B tracks the same game, unaware of A's push, then diverges and pushes =="
  cli_b add-game --name "$GAME_NAME" --dir "$STATE/save-b" --force-path >/dev/null
  echo "different progress, never synced with the cloud ($nonce)" >> "$STATE/save-b/savefile.txt"
  cli_b push "$GAME_NAME" >/dev/null 2>&1 || true

  echo "== confirming a real conflict now exists =="
  cli_b conflicts | grep -q "$GAME_NAME" || die "no conflict was seeded — push may have gone through clean"
  cli_b conflicts
}

start_daemon() {
  pid_alive "$DAEMON_PID" && { echo "daemon already running (pid $(cat "$DAEMON_PID"))"; return; }
  dotnet_env
  echo "== starting the daemon as 'This Device' on :$DAEMON_PORT =="
  XDG_DATA_HOME="$MACHINE_B" nohup dotnet "$REPO/src/Agent.Linux/bin/Debug/net10.0/savelocker.dll" \
    daemon --port "$DAEMON_PORT" >"$DAEMON_LOG" 2>&1 &
  echo $! > "$DAEMON_PID"
  sleep 1
  echo ""
  echo "Open the Conflicts page:  http://localhost:$DAEMON_PORT/#conflicts"
  echo "Overview (Sync now):      http://localhost:$DAEMON_PORT/#overview"
}

stop_pidfile() {
  local f="$1" what="$2"
  if pid_alive "$f"; then
    kill "$(cat "$f")" 2>/dev/null || true
    sleep 0.3
    kill -9 "$(cat "$f")" 2>/dev/null || true
  fi
  rm -f "$f"
  echo "$what stopped."
}

case "$CMD" in
  up)
    [ -f "$REPO/src/Agent.Linux/bin/Debug/net10.0/savelocker.dll" ] && \
      [ -f "$REPO/src/Server/bin/Debug/net10.0/SaveLocker.Server.dll" ] || cmd_build
    start_server
    seed_conflict
    start_daemon
    ;;
  open)
    echo "http://localhost:$DAEMON_PORT/#conflicts"
    ;;
  again)
    # A full reset, not a lighter "resolve and reseed" — the server DB is long-lived across `again`
    # calls, and a machine's very first push only ever succeeds unconditionally when NO head exists
    # yet for the game (Phase 0/1: even a brand-new machine is compared against the current cloud
    # head). Reusing the same server state made whichever side pushed first inherit the OTHER run's
    # leftover head and conflict unpredictably. Wiping server-data too keeps this command exactly as
    # deterministic as a fresh `up`.
    stop_pidfile "$DAEMON_PID" "daemon"
    stop_pidfile "$SERVER_PID" "server"
    rm -rf "$STATE/save-a" "$STATE/save-b" "$MACHINE_A" "$MACHINE_B" "$STATE/server-data"
    start_server
    seed_conflict
    start_daemon
    ;;
  down)
    stop_pidfile "$DAEMON_PID" "daemon"
    stop_pidfile "$SERVER_PID" "server"
    ;;
  clean)
    stop_pidfile "$DAEMON_PID" "daemon"
    stop_pidfile "$SERVER_PID" "server"
    rm -rf "$STATE"
    echo "removed $STATE"
    ;;
  *)
    die "unknown command '$CMD' — use up|open|again|down|clean"
    ;;
esac
