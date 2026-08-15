# SaveLocker — Session Context

Read this and [[REPO_MAP]] at the start of every session. This file is a **short orientation** —
where the project is right now, and where to go next. Detail belongs in the files it links to; if a
section here starts growing, move it and leave a link.

**What:** Self-hosted Windows/Linux game save sync. Hub-and-spoke: a tray/headless agent on each PC
or Steam Deck syncs saves through an ASP.NET Core server (Docker on unRAID). React admin dashboard +
embedded React agent UI + a gamepad-native Deck Game Mode UI. See [[Architecture]].

**Repo:** https://github.com/SkorcherX/SaveLocker | **Branch:** `main`

**Released:** **v0.5.6** (tagged 2026-08-15) — notes in `web/src/releases/0.5.6.md`. Installer,
Linux tarball, checksums and `ghcr.io/skorcherx/savelocker:0.5.6` + `:latest` all published. GHCR
package `savelocker` is **public** ([[Gotchas]]).

**Decky plugin:** its own repo, <https://github.com/SkorcherX/SaveLocker-Decky>, at **v0.2.0**. Not
on the Decky store and cannot honestly be submitted — see `tasks/DeckyPlugin.md`.

---

## Where things stand

**Nothing in flight.** Everything below is on `main` and released.

**v0.5.6 (2026-08-15)** carries a **save-losing fix** found on real hardware. A tracked game with no
recorded `SteamAppId` matched nothing in the launch wrapper, so it played with **no sync at all** and
said so only to `agent.log` — two of four games on the maintainer's Deck were in that state, and a
Khazan session on 2026-08-11 was never pushed. A Proton prefix is named for the AppID that launches
into it, so the save path already carried the answer; the agent now resolves it everywhere and the
daemon records it (`b31e160`). Also in the release: the launch-option rule and API (`tasks/DeckyPlugin.md`
Phases 1–2), and `doctor` reporting whether a game's launch options were ever set.

**The Decky plugin is done and working on hardware** — Phases 1–4. It sets Steam launch options
(which the agent *cannot*: Steam rewrites its own config on exit), and its Quick Access panel shows
lease warnings, sync status, push/pull per game or all, and `doctor` on demand. Phases 1–2 are in the
agent and covered by `run-linux-tests`; 3–4 are the plugin and were verified on the Deck.

Everything shipped before this: `logs/sessions.md` (reverse-chronological) and
`logs/shipped-2026-07.md`.

## Next action

1. **Roll v0.5.6 out to the fleet — nothing has it yet.** Upload **both** rows in Config → Agent
   updates (Windows `.exe` and Linux `.tar.gz`) and `docker compose pull` the console. The GitHub
   Release asset is *not* what agents are offered; this is the step that always gets missed.
   Mechanics in [[Build and Run]] → Rolling a release out. No migration.
   <br>**Console and the Windows agent are on v0.5.6; the Deck was moved onto the real release by
   hand** (2026-08-15) after the test build turned out to be un-updatable — it reported plain
   `0.5.6`, so the server correctly answered "up to date" forever. See [[Gotchas]] → Builds.
2. **`tasks/DeckyPlugin.md` Phase 5** — the agent keeps the plugin updated, so users are not stuck
   choosing between Decky's custom-store setting (which replaces the official store while set) and
   never being told about updates. **The hard part is already proven on hardware**; the task file
   carries the two corrections that came out of Phase 4. See the handoff notes below.
3. **Deck hardware pass (the rest).** Never seen on real hardware: a genuine end-to-end *self*-update
   (`install.sh` over a running daemon is proven, twice), the Game Mode "update is ready" notice, and
   the agent UI's Updates panel. Record `systemd-analyze --user security savelocker.service` while
   there ([[Backlog]]).
4. **Check the live server for duplicate games.** Canonical naming stops *new* splits; it does not
   merge what a server already holds under two spellings, and there is no merge tool ([[Backlog]]).

Everything else — the WA-03 second-account ACL test, the remaining Windows manual gates, the LAN
enrollment-URL check, the missing LA-04/05/06/07 regression tests, and the `WA-01`/113-of-114
baseline drift — is prioritised in [[Backlog]].

## Handoff: picking up Decky Phase 5

Read `tasks/DeckyPlugin.md` (Phase 5 section) and [[Gotchas]] → *Decky plugin* first. Both carry
things that were measured on hardware and cannot be re-derived by reading code.

**The Deck.** `deck@192.168.68.67`, key auth is set up (see the maintainer — the key is deliberately
not named here). It is awake only when the maintainer wakes it. Useful state on it:
- agent **v0.5.6 (the released build**, checksum-verified against the published `SHA256SUMS-linux.txt`),
  service `savelocker.service` active, 4 tracked games, all with launch options confirmed;
- plugin at `~/homebrew/plugins/SaveLocker`, and a config backup at
  `~/savelocker-decky-backup-20260815-112213` (`config.json`, `shortcuts.vdf`, `localconfig.vdf`);
- `~/savelocker-plugin-stage/` is the staging directory for anything needing `sudo`.

**How to iterate on the plugin without sudo** — this is also exactly the mechanism Phase 5 automates:
`scp` a new `dist/index.js` (and `main.py`) straight into `~/homebrew/plugins/SaveLocker/`, and Decky
hot-reloads within a second. Works because a non-`_root` plugin's files are chowned to the desktop
user and the plugin ships the `debug` flag. **`plugin.json` is root-owned and needs `sudo`.**

**Debugging the plugin frontend.** It logs nowhere on disk. Tunnel CEF —
`ssh -N -L 8080:127.0.0.1:8080 deck@…` — then `curl http://localhost:8080/json` for targets and drive
CDP over the websocket. **Discard the console buffer before measuring**: `Runtime.enable` replays
history, which already caused one wrong conclusion. Log state at *render* time, not in handlers.

**Plugin releases.** Tag the plugin repo; its workflow builds the zip, publishes it, and regenerates
`store/plugins` with the artifact's SHA-256 — Decky verifies that hash, so index and artifact must
come from the same job. Bump `package.json` too: Decky reads the installed version from it.

## Parked on a branch — `offline-backoff-task` (`bee3116`, pushed, not merged)

`main` carries only `tasks/DeckyPlugin.md`; do not conclude that is the only task. `tasks/OfflineBackoff.md`
and its Backlog entry exist only on that branch, deliberately (2026-07-29) — the task was written and
the work deferred. `git checkout offline-backoff-task` to pick it up.

An agent that cannot reach the server retries forever at a fixed cadence: the offline drainer every
30 s, *re-archiving the save folder each time before it finds out*, and the command poller every 20 s
carrying the reconcile, the command fetch and the heartbeat — roughly five failed requests a minute,
indefinitely. On a Deck away from home that is battery and metered traffic spent on nothing.
**Backing off only the drainer does not achieve the goal**, which is why the task covers all three
loops.

---

## Where to look

| Topic | File |
|-------|------|
| Codebase map | [[REPO_MAP]] |
| System design | [[Architecture]] |
| Locked decisions (don't re-litigate) | [[Decisions]] |
| Recurring traps — **read before any build, test or path work** | [[Gotchas]] |
| Dev build/run, deploy, test suites + baselines | [[Build and Run]] |
| REST endpoints | [[API Reference]] |
| Agent CLI | `web/src/help/cli-reference.md` (KB article) |
| Prioritised open work | [[Backlog]] |
| Shipped release notes | `web/src/releases/*.md` (the console page *and* the GitHub Release body) |
| Session history | `logs/sessions.md` |
| Write-ups | `logs/2026-08-14_steam-cloud-from-manifest.md` · `logs/2026-08-09_heroic-detection.md` · `logs/2026-07-29_winagent-bugbounty.md` · `logs/2026-07-29_linuxagent-bugbounty.md` · `logs/2026-07-27_console-bugbounty.md` |
