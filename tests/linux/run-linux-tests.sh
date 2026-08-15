#!/usr/bin/env bash
# Linux agent integration tests — the fake-game harness.
#
# Covers the whole Phase 2 path with no Steam, no Proton, no GPU and no Steam Deck:
#   * shortcuts.vdf discovery, including the signed -> unsigned AppID trap
#   * Proton prefix resolution (manifest tokens resolving INSIDE the prefix)
#   * both save shapes: in-prefix AND portable-next-to-the-exe
#   * Heroic Games Launcher: its own library files, its own prefixes — proton-shaped AND
#     wine-shaped — and the double-listing when a Heroic game is also added to Steam
#   * the launch wrapper: pull on launch, supervise the child, settle, push on exit
#   * the settle gate waiting out a game that flushes after it exits
#   * the /proc lock probe seeing a held write descriptor
#   * doctor's diagnostics: the right state root under --config, and an unenrolled machine
#     reported as an enrolment problem rather than a network one
#   * autostart reporting systemctl's real exit status
#
# MUST be run from a Linux filesystem (~/), never /mnt/c: DrvFs breaks inotify, permissions,
# case-sensitivity and locking, so a green run there would be a fiction.
#
# Usage: tests/linux/run-linux-tests.sh
set -uo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
scratch="${repo_root}/.verify-linux"
fixtures="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
port=5179
server_url="http://127.0.0.1:${port}"

pass=0
fail=0

check() {
  if [ "$2" = "0" ]; then echo "PASS: $1"; pass=$((pass + 1))
  else echo "FAIL: $1"; fail=$((fail + 1)); fi
}
contains() { grep -qF -- "$2" <<<"$1" && echo 0 || echo 1; }

case "${repo_root}" in
  /mnt/*) echo "REFUSING: ${repo_root} is a Windows drive (DrvFs). Run from the WSL ext4 home."; exit 2 ;;
esac

command -v python3 >/dev/null || { echo "python3 is required to build the fixtures."; exit 2; }

cleanup() {
  [ -n "${server_pid:-}" ] && kill "${server_pid}" 2>/dev/null
  pkill -f "${scratch}" 2>/dev/null   # any straggling slow-game writers
  wait 2>/dev/null
  return 0
}
trap cleanup EXIT

rm -rf "${scratch}"
mkdir -p "${scratch}"

# ── Fixtures ────────────────────────────────────────────────────────────────────────────────
eval "$(python3 "${fixtures}/make-fixtures.py" "${scratch}")"

# Everything below runs against the FAKE home, so the agent's Steam-root probe, its XDG state
# dir and its config all land in the fixture tree rather than the developer's real home.
export HOME="${HOME_DIR}"
export XDG_DATA_HOME="${HOME_DIR}/.local/share"
echo "Fixtures in ${scratch} (fake HOME=${HOME})"

# ── Server (fresh DB) ───────────────────────────────────────────────────────────────────────
echo "==> Starting server on ${server_url}"
server_state="${scratch}/server"
mkdir -p "${server_state}/archives"
# AgentInstallerRoot matters as much as the other two, and only on Linux. AgentInstallerService
# creates its root in its CONSTRUCTOR, and the default resolves to /data/agent-installer — which a
# normal user cannot create. The server then died at startup AFTER migrations had run, so the log
# looked like a healthy boot right up to an unhandled UnauthorizedAccessException, and all 16
# server-dependent checks below failed as though the agent were broken. Windows never saw it: there
# the same default happily creates E:\data\.
ASPNETCORE_URLS="${server_url}" \
Storage__DbPath="${server_state}/savelocker.db" \
Storage__ArchiveRoot="${server_state}/archives" \
Storage__AgentInstallerRoot="${server_state}/agent-installer" \
  dotnet run --project "${repo_root}/src/Server/SaveLocker.Server.csproj" \
    --no-launch-profile >"${scratch}/server.log" 2>&1 &
server_pid=$!

# /api/admin/status is the only unauthenticated route. /api/games is an AGENT route and answers
# 401 without an X-Api-Key, so probing it never succeeds and this loop silently burned all 60s.
for _ in $(seq 1 60); do
  curl -sf "${server_url}/api/admin/status" >/dev/null 2>&1 && break
  sleep 1
done

# ── Agent under test ────────────────────────────────────────────────────────────────────────
agent_dir="${repo_root}/src/Agent.Linux"
agent() { dotnet run --project "${agent_dir}/SaveLocker.Agent.Linux.csproj" -v quiet --no-build -- "$@" 2>&1; }

echo "==> Building agent"
dotnet build "${agent_dir}/SaveLocker.Agent.Linux.csproj" -v quiet --nologo >"${scratch}/build.log" 2>&1 \
  || { echo "BUILD FAILED"; tail -30 "${scratch}/build.log"; exit 1; }

deck_cfg="${scratch}/deck-config.json"
other_cfg="${scratch}/other-config.json"
for c in "${deck_cfg}" "${other_cfg}"; do
  cat >"${c}" <<EOF
{
  "ServerUrl": "${server_url}",
  "ManifestCachePath": "${fixtures}/manifest.yaml",
  "SettleQuietSeconds": 3,
  "SettleMaxWaitSeconds": 60,
  "Games": []
}
EOF
done

out="$(agent register --config "${deck_cfg}" --name Deck)"
check "Deck registered" "$(contains "${out}" "Registered 'Deck'")"
out="$(agent register --config "${other_cfg}" --name Desktop)"
check "second machine registered" "$(contains "${out}" "Registered 'Desktop'")"

# ── Discovery: shortcuts.vdf + the signed/unsigned AppID trap ────────────────────────────────
out="$(agent scan --config "${deck_cfg}")"
check "scan finds the non-Steam shortcut"      "$(contains "${out}" "Fake Prefix Game")"
check "scan finds the portable shortcut"       "$(contains "${out}" "Fake Portable Game")"
# The vdf stores -1234567890; compatdata is named 3060399406. Reading the signed form misses.
check "AppID converted signed -> unsigned"     "$(contains "${out}" "appid=${PREFIX_APPID}")"
check "scan resolves the in-prefix save dir"   "$(contains "${out}" "${PREFIX_SAVE}")"
check "scan resolves the portable save dir"    "$(contains "${out}" "${PORTABLE_SAVE}")"
# The portable game's manifest entry saves to a loose file beside its .exe (the Cave Story+ shape),
# so it resolves to the INSTALL DIRECTORY — the whole game. That answer must be dropped: syncing it
# archives the game, and restore's delete pass would prune another machine's installation to match.
# Without the guard the install dir resolves first and beats the real save folder above.
# Whole-line match: the real save dir is ${PORTABLE_INSTALL}/Saves, so a substring test for the
# install directory matches it too and passes no matter what the code does.
check "the install directory is not offered as a save dir" \
  "$(printf '%s\n' "${out}" | grep -qxF "      save: ${PORTABLE_INSTALL}" && echo 1 || echo 0)"
# --prefix is required: without one there is no resolver on Linux at all, so the command would
# report "not found" for a reason that has nothing to do with the guard under test.
out2="$(agent resolve --config "${deck_cfg}" --prefix "${PORTABLE_PREFIX}" \
        --install-dir "${PORTABLE_INSTALL}" "Fake Portable Game")"
check "resolve refuses a <base>-only save path" \
  "$(contains "${out2}" "no existing save directory found")"

# ── Discovery: installed Steam games ────────────────────────────────────────────────────────
# These were deliberately not discovered at all until now, on the reasoning that Steam Cloud covers
# them. The UI filters what the scan returns, so "hidden by default" and "never scanned" look
# identical from the couch — and only one of them can be undone by the user.
check "scan finds the installed Steam game"    "$(contains "${out}" "Fake Installed Game")"
# The library is on a second volume with its own compatdata. Resolving this proves the scan pairs
# each library with its own prefixes rather than looking in the main Steam root.
check "installed game resolves in its own library's prefix" "$(contains "${out}" "${INSTALLED_SAVE}")"
# A runtime is an installed "app" and not a game. Listing it is not cosmetic: it is enrollable.
check "Steam runtimes are not offered as games" \
  "$([ "$(contains "${out}" "Steamworks Common Redistributables")" = 1 ] && echo 0 || echo 1)"
# Its appid is deliberately one no code could have hardcoded. Found on a Deck: the filter WAS a
# list of appids, and Valve mints a new one per Proton release, so Proton 9/10/11, Proton Hotfix
# and Steam Linux Runtime 4.0 all arrived as enrollable games. toolmanifest.vdf is the real marker.
check "an unknown compat tool is not offered as a game" \
  "$([ "$(contains "${out}" "Proton 99.0")" = 1 ] && echo 0 || echo 1)"

# ── Steam Cloud is per-game manifest data, not "Steam installed it" ──────────────────────────
# The flag was hardcoded true for every installed Steam title, and the default Add Games view HIDES
# whatever is flagged — so a game with no cloud behind it at all was invisible, and the user was
# never told it existed. Most Steam titles are in that position: of the manifest's 48,908 entries
# with a Steam id, only 14,340 have Steam Cloud.
#
# The three games below differ in NOTHING the scanner can observe except their manifest cloud block,
# which is what makes this a test of reading it rather than of any particular constant.
check "no cloud block -> not flagged as Steam Cloud" \
  "$(contains "${out}" "Fake Installed Game  <SteamInstalled> appid=")"
check "cloud: steam: true -> flagged as Steam Cloud" \
  "$(contains "${out}" "Fake Cloud Game  <SteamInstalled> [Steam Cloud] appid=")"
# The Breath of Fire IV shape, and the case that motivated this: a cloud block that exists, is
# correct, and does not name Steam. Sold on Steam; only GOG syncs it.
check "cloud: gog only -> not flagged as Steam Cloud" \
  "$(contains "${out}" "Fake Gog Cloud Game  <SteamInstalled> appid=")"

# The flag is only worth anything if the filter acts on it — this is the view the user actually sees.
out_nocloud="$(agent scan --config "${deck_cfg}" --no-cloud)"
check "--no-cloud keeps a Steam game the manifest does not cloud" \
  "$(contains "${out_nocloud}" "Fake Installed Game")"
check "--no-cloud keeps the gog-cloud-only game" \
  "$(contains "${out_nocloud}" "Fake Gog Cloud Game")"
check "--no-cloud drops the genuinely cloud-synced game" \
  "$([ "$(contains "${out_nocloud}" "Fake Cloud Game  <")" = 1 ] && echo 0 || echo 1)"

# ── Prefix resolution: manifest tokens must resolve INSIDE the prefix ────────────────────────
out="$(agent resolve --config "${deck_cfg}" --prefix "${PREFIX}" "Fake Prefix Game")"
check "manifest <winAppData> resolves inside the prefix" "$(contains "${out}" "${PREFIX_SAVE}")"

# The same lookup WITHOUT a prefix must not invent a host path.
out="$(agent resolve --config "${deck_cfg}" "Fake Prefix Game")"
check "no prefix -> no resolution (does not guess a host path)" \
  "$(contains "${out}" "no existing save directory found")"

# ── Heroic Games Launcher ───────────────────────────────────────────────────────────────────
# Heroic owns its own prefixes and obeys neither Steam convention, so nothing here is covered by
# the shortcut checks above.
out="$(agent scan --config "${deck_cfg}")"
check "scan finds the Heroic Epic game"        "$(contains "${out}" "Heroic Prefix Game")"
check "scan finds the Heroic sideloaded game"  "$(contains "${out}" "Heroic Wine Game")"
# Proton-shaped prefix (pfx/, steamuser) that Steam did not create.
check "Heroic proton-shaped prefix resolves"   "$(contains "${out}" "${HEROIC_EPIC_SAVE}")"
# Wine-shaped prefix (drive_c/ directly, Linux username). Unresolvable by anything assuming Steam.
check "Heroic wine-shaped prefix resolves"     "$(contains "${out}" "${HEROIC_WINE_SAVE}")"
# Native-Linux titles are out of scope (Decisions.md §1) — they have no prefix to resolve in.
check "native-Linux Heroic game not listed" \
  "$([ "$(contains "${out}" "Heroic Linux Game")" = 1 ] && echo 0 || echo 1)"

# The Epic game ALSO has a shortcuts.vdf entry from Heroic's "Add to Steam", with no compatdata
# behind it. One game, one row — and it must be the row that has a save dir, not the empty one.
check "Heroic + Steam shortcut collapse to one row" \
  "$([ "$(printf '%s\n' "${out}" | grep -c 'Heroic Prefix Game')" = 1 ] && echo 0 || echo 1)"
check "the surviving row is the Heroic one"    "$(contains "${out}" "Heroic Prefix Game  <Heroic>")"

# --prefix must read the prefix's real layout rather than assume Steam's.
out="$(agent resolve --config "${deck_cfg}" --prefix "${HEROIC_WINE_PREFIX}" "Heroic Wine Game")"
check "resolve --prefix handles a wine-shaped prefix" "$(contains "${out}" "${HEROIC_WINE_SAVE}")"

# ── A sideloaded game whose files and configured prefix are two different prefixes ────────────
# Reproduced on a Deck (2026-08-09) on a CLEAN install, no renaming involved: Heroic's sideload
# dialog seeds the prefix name from a title field that starts as the literal word "Title", so the
# game installs into one prefix and is configured to launch in another. Renaming a prefix by hand
# produces the same split.
#
# Either way the game runs in the CONFIGURED prefix, so that is where saves go. The other prefix can
# hold a full set of real save files that nothing writes to any more — the more convincing of the
# two answers, and the wrong one. Returning nothing beats returning it.
cp "${HEROIC_SIDELOAD_LIBRARY%.json}.renamed-prefix.json" "${HEROIC_SIDELOAD_LIBRARY}"

out="$(agent scan --config "${deck_cfg}")"
check "split prefix: the game is still discovered" "$(contains "${out}" "Heroic Offline Game")"
check "split prefix: no save path is guessed" \
  "$([ "$(contains "${out}" "${HEROIC_STRANDED_SAVE}")" = 1 ] && echo 0 || echo 1)"
# Proof the case is live: the stranded path DOES resolve if you point the resolver at that prefix.
out="$(agent resolve --config "${deck_cfg}" --prefix "${HEROIC_RENAMED_PREFIX}" "Heroic Offline Game")"
check "split prefix: the stranded save would have resolved" \
  "$(contains "${out}" "${HEROIC_STRANDED_SAVE}")"

out="$(agent doctor --config "${deck_cfg}")"
doctor_split_rc=$?
check "split prefix: doctor reports the disagreement" \
  "$(contains "${out}" "its executable is in")"
check "split prefix: doctor names both prefixes" \
  "$([ "$(contains "${out}" "${HEROIC_RENAMED_PREFIX}")" = 0 ] && \
     [ "$(contains "${out}" "${HEROIC_CONFIGURED_PREFIX}")" = 0 ] && echo 0 || echo 1)"
# A NOTE, not a problem. Heroic splits these by default for sideloaded games, so failing doctor's
# exit code over it would report a working Deck as broken.
check "split prefix: doctor still exits 0" "${doctor_split_rc}"

# Back to the healthy library, so the checks below still describe a clean machine.
python3 - "${HEROIC_SIDELOAD_LIBRARY}" <<'PY'
import json, sys
p = sys.argv[1]
lib = json.load(open(p))
lib["games"] = [g for g in lib["games"] if g["app_name"] != "HeroicOfflineGame"]
json.dump(lib, open(p, "w"), indent=2)
PY

# ── Map both games ──────────────────────────────────────────────────────────────────────────
out="$(agent add-game --config "${deck_cfg}" --name "Fake Prefix Game" \
        --manifest "Fake Prefix Game" --prefix "${PREFIX}" --appid "${PREFIX_APPID}")"
check "in-prefix game auto-mapped from the manifest" "$(contains "${out}" "${PREFIX_SAVE}")"

out="$(agent add-game --config "${deck_cfg}" --name "Fake Portable Game" \
        --dir "${PORTABLE_SAVE}" --appid "${PORTABLE_APPID}")"
check "portable game mapped with --dir" "$(contains "${out}" "${PORTABLE_SAVE}")"

# ── doctor ──────────────────────────────────────────────────────────────────────────────────
out="$(agent doctor --config "${deck_cfg}")"
doctor_rc=$?
check "doctor reports no problems"        "${doctor_rc}"
check "doctor finds the Steam root"       "$(contains "${out}" "${STEAM_ROOT}")"
check "doctor resolves the Proton prefix" "$(contains "${out}" "${PREFIX}")"
check "doctor confirms the /proc lock probe" "$(contains "${out}" "available (/proc)")"
check "doctor finds the Heroic config root" "$(contains "${out}" "${HEROIC_CONFIG}")"
# Which shape a prefix has decides where the manifest's tokens resolve, so doctor must say.
check "doctor names the wine-shaped prefix"  "$(contains "${out}" "(wine, user deck)")"
check "doctor names the proton-shaped prefix" "$(contains "${out}" "(proton, user steamuser)")"

# doctor must describe the state root it is ACTUALLY using. ${deck_cfg} is deliberately outside
# AgentConfig.DefaultDir, and doctor used to print (and probe) the default regardless - declaring
# the wrong directory healthy while the one in use might be missing or read-only.
state_dir_line="$(grep -F '  state dir: ' <<<"${out}")"
check "doctor reports the --config state dir" "$(contains "${state_dir_line}" "${scratch}")"
check "doctor does not report the default state dir instead"   "$(grep -qF "${HOME}/.local/share/SaveLocker" <<<"${state_dir_line}" && echo 1 || echo 0)"

# An unenrolled agent gets a correct 401 from an authenticated route. Calling it with no key and
# then labelling the refusal "server unreachable" sent people to debug a network that was fine.
unenrolled_cfg="${scratch}/unenrolled-config.json"
python3 - "${unenrolled_cfg}" "${server_url}" <<'PY2'
import json,sys
json.dump({"ServerUrl": sys.argv[2], "MachineName": "Unenrolled", "Games": []}, open(sys.argv[1], "w"))
PY2
out="$(agent doctor --config "${unenrolled_cfg}")"
check "doctor says an unenrolled machine is NOT a connectivity failure"   "$(grep -q 'server unreachable' <<<"${out}" && echo 1 || echo 0)"
check "doctor names enrolment as the actual problem"   "$(contains "${out}" "not enrolled")"
check "doctor still reports the server as reachable"   "$(contains "${out}" "server reachable")"

# ── The launch wrapper: pull, run the game, settle, push ─────────────────────────────────────
# Seed a save so there is something to push, and prove the wrapper returns the GAME's exit code.
echo "level=0" >"${PREFIX_SAVE}/slot1.sav"

echo "==> Launch wrapper (in-prefix game; writer keeps flushing for 6s after exit)"
start=$(date +%s)
# These MUST be exported, not prefixed onto the assignment: `VAR=x out=$(cmd)` is two plain
# assignments in bash and never puts VAR in the command's environment — which is exactly the
# input this test is about.
export STEAM_COMPAT_DATA_PATH="${PREFIX}"
export SteamAppId="${PREFIX_APPID}"
out="$(agent run --config "${deck_cfg}" -- "${fixtures}/slow-game.sh" "${PREFIX_SAVE}" 6 7)"
run_rc=$?
elapsed=$(( $(date +%s) - start ))
unset STEAM_COMPAT_DATA_PATH SteamAppId

check "wrapper propagates the game's exit code (7)" "$([ "${run_rc}" = "7" ] && echo 0 || echo 1)"

log="${XDG_DATA_HOME}/SaveLocker/agent.log"
logtail="$(tail -60 "${log}" 2>/dev/null)"
check "wrapper saw the prefix from STEAM_COMPAT_DATA_PATH" "$(contains "${logtail}" "prefix=${PREFIX}")"
# The gate must have waited out the post-exit writer rather than archiving immediately.
check "settle gate waited for the save to go quiet" "$(contains "${logtail}" "save files settled.")"
check "settle gate actually took time (>=6s)" "$([ "${elapsed}" -ge 6 ] && echo 0 || echo 1)"
check "wrapper pushed a new version on exit" "$(contains "${logtail}" "pushed new version")"

# The settled save is the COMPLETE one — the writer's last line, not a half-written file.
check "pushed save contains the writer's final line" \
  "$(grep -q '^done$' "${PREFIX_SAVE}/slot1.sav" && echo 0 || echo 1)"

# ── The /proc lock probe, isolated ───────────────────────────────────────────────────────────
# The decisive test for the Linux behaviour change. This writer writes ONCE and then holds the
# file open for writing while staying completely still, so the fingerprint half of the gate goes
# quiet almost at once. Only a working /proc/*/fd probe can hold the gate closed for the 8s the
# descriptor stays open.
#
# A probe that silently answers "nothing is locked" (what FileShare does on Linux) settles here in
# ~3s. That is the exact failure the task file said must not pass silently — so it is asserted on.
echo "==> Lock probe in isolation (silent writer, descriptor held open 8s)"
export STEAM_COMPAT_DATA_PATH="${PREFIX}"
export SteamAppId="${PREFIX_APPID}"
start=$(date +%s)
out="$(agent run --config "${deck_cfg}" -- "${fixtures}/slow-game.sh" "${PREFIX_SAVE}" 8 0 hold)"
held_elapsed=$(( $(date +%s) - start ))
unset STEAM_COMPAT_DATA_PATH SteamAppId

check "/proc probe held the gate for a silent-but-open writer (>=8s, not ~3s)" \
  "$([ "${held_elapsed}" -ge 8 ] && echo 0 || echo 1)"
echo "    (settled after ${held_elapsed}s; a broken probe settles in ~3s)"

# ── The save actually round-trips through the server ─────────────────────────────────────────
# A second machine pulls what the Deck pushed and must get byte-identical content.
out="$(agent add-game --config "${other_cfg}" --name "Fake Prefix Game" --dir "${scratch}/pulled")"
out="$(agent pull --config "${other_cfg}" "Fake Prefix Game")"
check "second machine restored the save" \
  "$(diff -r "${PREFIX_SAVE}" "${scratch}/pulled" >/dev/null 2>&1 && echo 0 || echo 1)"

# ── Portable save shape through the same wrapper ─────────────────────────────────────────────
echo "==> Launch wrapper (portable game — saves next to the .exe, no prefix involved)"
export STEAM_COMPAT_DATA_PATH="${PORTABLE_PREFIX}"
export SteamAppId="${PORTABLE_APPID}"
out="$(agent run --config "${deck_cfg}" -- "${fixtures}/slow-game.sh" "${PORTABLE_SAVE}" 4 0)"
run_rc=$?
unset STEAM_COMPAT_DATA_PATH SteamAppId
logtail="$(tail -40 "${log}" 2>/dev/null)"
check "portable game: wrapper exits cleanly" "$([ "${run_rc}" = "0" ] && echo 0 || echo 1)"
check "portable game: pushed without touching the prefix" \
  "$(contains "${logtail}" "[Fake Portable Game] pushed new version")"

out="$(agent status --config "${deck_cfg}")"
check "both games have a head on the server" \
  "$([ "$(grep -c 'head=' <<<"${out}")" = "2" ] && echo 0 || echo 1)"
check "no conflicts" "$(grep -q 'CONFLICT' <<<"${out}" && echo 1 || echo 0)"

# ── Daemon and launch wrapper, concurrently, on the SAME game ────────────────────────────────
# This is the Deck's real configuration and it is the one shape no other harness reproduces:
# autorun keeps the daemon running (watching every mapped save folder) while Steam starts
# `savelocker run` as a second process. Both sync the same game, through the same config.json,
# offline queue and health-event file.
#
# The damage to look for is NOT a crash. It is the daemon writing its stale in-memory config back
# over the parent version the wrapper just recorded: the next push then presents a stale parent and
# the server rejects it as a conflict — one machine, conflicting with itself, with nothing in the
# log to say why. `tests/run-concurrency-tests.ps1` covers the same contention cross-platform with a
# CLI push as the second actor; this covers it with the real launch wrapper.
echo "==> Daemon + launch wrapper on the same game, concurrently"

dotnet "${agent_dir}/bin/Debug/net10.0/savelocker.dll" daemon \
  --config "${deck_cfg}" --port 5189 >"${scratch}/daemon.log" 2>&1 &
daemon_pid=$!
for _ in $(seq 1 40); do
  curl -sf "http://localhost:5189/" >/dev/null 2>&1 && break
  sleep 0.5
done
check "daemon started alongside the wrapper" \
  "$(kill -0 "${daemon_pid}" 2>/dev/null && echo 0 || echo 1)"

# The daemon now holds a config loaded BEFORE anything below happens.
export STEAM_COMPAT_DATA_PATH="${PREFIX}"
export SteamAppId="${PREFIX_APPID}"
# Third arg is the GAME's exit code, which the wrapper propagates verbatim — so it must be 0 here
# for "exits cleanly" to mean anything. (Propagation itself is asserted earlier, with a 7.)
out="$(agent run --config "${deck_cfg}" -- "${fixtures}/slow-game.sh" "${PREFIX_SAVE}" 5 0)"
wrapper_rc=$?
unset STEAM_COMPAT_DATA_PATH SteamAppId
check "wrapper still exits cleanly with a daemon running" \
  "$([ "${wrapper_rc}" = "0" ] && echo 0 || echo 1)"

# Let the daemon's watcher react to the same writes and persist its own view.
sleep 12

version_after_wrapper="$(python3 - "${deck_cfg}" <<'PY'
import json,sys
cfg=json.load(open(sys.argv[1]))
g=[x for x in cfg["Games"] if x["Name"]=="Fake Prefix Game"]
print(g[0].get("LastKnownVersionId") or "" if g else "")
PY
)"
check "the game still has a parent version after both processes wrote" \
  "$([ -n "${version_after_wrapper}" ] && echo 0 || echo 1)"

check "config.json is still valid JSON" \
  "$(python3 -c 'import json,sys; json.load(open(sys.argv[1]))' "${deck_cfg}" >/dev/null 2>&1 && echo 0 || echo 1)"

# The payoff: a lost parent version shows up HERE, as a conflict against the machine's own push.
out="$(agent push --config "${deck_cfg}" "Fake Prefix Game")"
check "a later push does not conflict with itself" \
  "$(grep -q 'CONFLICT' <<<"${out}" && echo 1 || echo 0)"

# Temp archives must be per-process, or one push deletes the other's mid-upload.
# State lives beside the config file it belongs to, so with --config that is ${scratch}, not the
# machine default under XDG_DATA_HOME.
leftover_tmp="$(find "${scratch}/tmp" -name '*.zip' 2>/dev/null | wc -l)"
check "no temp archives leaked" "$([ "${leftover_tmp}" = "0" ] && echo 0 || echo 1)"

kill "${daemon_pid}" 2>/dev/null
wait "${daemon_pid}" 2>/dev/null

# ---------------------------------------------------------------------------
# autostart must report the REAL outcome (LA-08)
# ---------------------------------------------------------------------------
# `disable` used to throw away systemctl's exit code and print "Auto-start disabled." no matter
# ── The update channel: the agent asks for the LINUX package ─────────────────────────────────
# A Deck asking for updates and being handed a Windows .exe is the whole failure mode this block
# exists for, and it has two shapes: a server that hosts both and must serve the right one, and a
# server too old to know about platforms at all, which answers with the .exe no matter what.
#
# Nothing here is ever executed. `check-update --download` fetches and VERIFIES, and stops.
echo
echo "==> Update channel"
inst_root="${server_state}/agent-installer"

# The tarball is a real gzip stream; content does not matter, only its first two bytes and its
# digest, which the server computes as it stores the file.
printf '\037\213\010fake linux agent tarball' >"${scratch}/savelocker-9.9.9-linux-x64.tar.gz"
curl -sf -X POST "${server_url}/api/admin/agent-installer?version=9.9.9&platform=linux-x64" \
  -F "file=@${scratch}/savelocker-9.9.9-linux-x64.tar.gz" >/dev/null
# A Windows installer in the Windows slot, at a DIFFERENT version. If the agent ignored the
# platform it would report 8.8.8 here, which is why the two versions must not match.
printf 'MZfake windows installer' >"${scratch}/SaveLocker-Agent-Setup-8.8.8.exe"
curl -sf -X POST "${server_url}/api/admin/agent-installer?version=8.8.8" \
  -F "file=@${scratch}/SaveLocker-Agent-Setup-8.8.8.exe" >/dev/null

out="$(agent check-update --config "${deck_cfg}")"
check "check-update sees the Linux package"        "$(contains "${out}" "v9.9.9 is available")"
check "check-update did NOT take the Windows one"  "$(grep -q '8\.8\.8' <<<"${out}" && echo 1 || echo 0)"
check "the offered URL names the Linux platform"   "$(contains "${out}" "platform=linux-x64")"

out="$(agent check-update --download --config "${deck_cfg}")"
check "a good tarball downloads and VERIFIES"      "$(contains "${out}" "VERIFIED")"

# The digest is the only integrity control on plain http (Decisions.md), so it has to be the thing
# that refuses. Rewriting the stored file behind the server's back leaves installer-info.json
# describing bytes that are no longer there — exactly what a substituted download looks like.
printf '\037\213\010tampered payload, same name' >"${inst_root}/linux-x64/savelocker-9.9.9-linux-x64.tar.gz"
out="$(agent check-update --download --config "${deck_cfg}")"
check "a tampered tarball is REFUSED"              "$(contains "${out}" "REFUSED")"
check "the refusal names the checksum"             "$(contains "${out}" "checksum")"

# The payload check, which is the ONLY thing standing between a Deck and a Windows .exe here: the
# digest matches (the server hashed what it was given), the extension matches, and the bytes are
# still an MZ executable. This is what a server that predates platform slots serves to everyone.
printf 'MZ this is a windows executable, not a tarball' >"${scratch}/wrong-payload.tar.gz"
curl -sf -X POST "${server_url}/api/admin/agent-installer?version=9.9.9&platform=linux-x64" \
  -F "file=@${scratch}/wrong-payload.tar.gz;filename=savelocker-9.9.9-linux-x64.tar.gz" >/dev/null
out="$(agent check-update --download --config "${deck_cfg}")"
check "a Windows executable offered to Linux is REFUSED" "$(contains "${out}" "REFUSED")"
check "the refusal says it is not a gzip archive"        "$(contains "${out}" "gzip archive")"

# doctor is the only diagnostic surface a Deck has, so the update state has to reach it.
out="$(agent doctor --config "${deck_cfg}")"
check "doctor reports the available update"        "$(contains "${out}" "update: v9.9.9 available")"

# The daemon's own view, which is what the agent UI reads. This is the path that was hardcoded to
# null on Linux — every other check here goes through the one-shot CLI, which builds its own checker
# and would keep passing while the daemon reported nothing at all.
dotnet "${agent_dir}/bin/Debug/net10.0/savelocker.dll" daemon \
  --config "${deck_cfg}" --port 5188 >"${scratch}/update-daemon.log" 2>&1 &
upd_pid=$!
for _ in $(seq 1 40); do
  curl -sf "http://localhost:5188/" >/dev/null 2>&1 && break
  sleep 0.5
done
# The local API is token-gated (Decisions.md: reaching it is equivalent to owning the box), and the
# token lives beside the config it belongs to. Without the header every request is a 401 — which is
# indistinguishable from "the daemon has no update to report" if you only look at the assertion.
upd_token="$(cat "${scratch}/api-token")"
# The first check is on a short timer rather than inline with startup, so give it room to land.
upd_json=""
for _ in $(seq 1 20); do
  upd_json="$(curl -sf -H "X-SaveLocker-Token: ${upd_token}" \
    "http://localhost:5188/api/agent-version" 2>/dev/null)"
  grep -q '"updateAvailable":true' <<<"${upd_json}" && break
  sleep 1
done
kill "${upd_pid}" 2>/dev/null; wait "${upd_pid}" 2>/dev/null
check "the daemon reports the update to its own API"  "$(contains "${upd_json}" '"updateAvailable":true')"
check "the daemon names the Linux version"            "$(contains "${upd_json}" '"latestVersion":"9.9.9"')"

# ── The update channel: AgentUpdate:Linux:* and the off-origin refusal ───────────────────────
# A second server whose Linux slot is EMPTY, so the config fallback is the only source — the one
# path that proves AgentUpdate:Linux:* is read at all, since an empty section is indistinguishable
# from a missing one when the answer is 204 either way. Its DownloadUrl points somewhere that is
# not this server and carries no digest, which must be refused before a single request is made.
alt_port=5196
alt_url="http://127.0.0.1:${alt_port}"
foreign_port=5997
alt_state="${scratch}/alt-server"
mkdir -p "${alt_state}/archives"
ASPNETCORE_URLS="${alt_url}" \
Storage__DbPath="${alt_state}/savelocker.db" \
Storage__ArchiveRoot="${alt_state}/archives" \
Storage__AgentInstallerRoot="${alt_state}/agent-installer" \
AgentUpdate__Linux__LatestVersion="9.9.9" \
AgentUpdate__Linux__DownloadUrl="http://127.0.0.1:${foreign_port}/savelocker-9.9.9-linux-x64.tar.gz" \
  dotnet run --project "${repo_root}/src/Server/SaveLocker.Server.csproj" \
    --no-launch-profile >"${scratch}/alt-server.log" 2>&1 &
alt_pid=$!
for _ in $(seq 1 60); do
  curl -sf "${alt_url}/api/admin/status" >/dev/null 2>&1 && break
  sleep 1
done

alt_cfg="${scratch}/alt-config.json"
python3 - "${alt_cfg}" "${alt_url}" <<'PY3'
import json,sys
json.dump({"ServerUrl": sys.argv[2], "MachineName": "DeckAlt", "Games": []}, open(sys.argv[1], "w"))
PY3
agent register --config "${alt_cfg}" --name "DeckAlt" >/dev/null

out="$(agent check-update --config "${alt_cfg}")"
check "AgentUpdate:Linux:* is served to a Linux agent"  "$(contains "${out}" "v9.9.9 is available")"
out="$(agent check-update --download --config "${alt_cfg}")"
check "an off-origin update with no digest is REFUSED"  "$(contains "${out}" "REFUSED")"
check "the refusal names the foreign host"              "$(contains "${out}" "127.0.0.1")"
# Nothing is listening on ${foreign_port} and nothing ever will be. That wording only comes from
# the refusal raised BEFORE any request is made — a connection failure would read completely
# differently — so this is what proves the agent never reached out, without needing a listener to
# ask. (WA-05 makes the same point on Windows with a real listener that records its hits.)
check "the refusal is the pre-request one, not a failed fetch" \
  "$(contains "${out}" "Refusing to download an update")"

kill "${alt_pid}" 2>/dev/null
wait "${alt_pid}" 2>/dev/null

# what. The failure is not hypothetical on a Deck: reached over SSH without a login session there
# is no user bus, systemctl fails, and the agent cheerfully said the service was off while it kept
# running. A stub systemctl that exits non-zero reproduces exactly that, without needing to break
# the real one. It must come FIRST on PATH but must not hide dotnet.
fake_bin="${scratch}/fakebin"
mkdir -p "${fake_bin}"
cat >"${fake_bin}/systemctl" <<'STUB'
#!/usr/bin/env bash
echo "Failed to connect to bus: No medium found" >&2
exit 1
STUB
chmod +x "${fake_bin}/systemctl"

# The unit file must exist, or SetEnabled never reaches systemctl at all.
mkdir -p "${HOME}/.config/systemd/user"
touch "${HOME}/.config/systemd/user/savelocker.service"

out="$(PATH="${fake_bin}:${PATH}" agent autostart --disable)"
autostart_rc=$?
check "autostart --disable fails loudly when systemctl fails"   "$([ "${autostart_rc}" != "0" ] && echo 0 || echo 1)"
check "autostart --disable does not claim success"   "$(grep -q 'Auto-start disabled' <<<"${out}" && echo 1 || echo 0)"

echo
echo "==== LINUX AGENT RESULT: ${pass} passed, ${fail} failed ===="
[ "${fail}" = "0" ]
