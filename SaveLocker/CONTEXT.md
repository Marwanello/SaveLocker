# SaveLocker — Session Context

Read this and [[REPO_MAP]] at the start of every session. This file is a **short orientation** —
where the project is right now, and where to go next. Detail belongs in the files it links to; if a
section here starts growing, move it and leave a link.

**What:** Self-hosted Windows/Linux game save sync. Hub-and-spoke: a tray/headless agent on each PC
or Steam Deck syncs saves through an ASP.NET Core server (Docker on unRAID). React admin dashboard +
embedded React agent UI + a gamepad-native Deck Game Mode UI. See [[Architecture]].

**Repo:** https://github.com/SkorcherX/SaveLocker | **Branch:** `main`

**Released:** **v0.5.5** (tagged 2026-08-14, merged as PR #40) — notes in
`web/src/releases/0.5.5.md`. GHCR package `savelocker` is **public** ([[Gotchas]]).

---

## Where things stand

**Nothing in flight.** `steam-cloud-from-manifest` merged (PR #40, `10be23e`) and v0.5.5 is tagged.
The branch carried two unrelated pieces of work, both complete:

1. **Steam Cloud from the manifest** (2 commits). Steam Cloud is read from the Ludusavi manifest
   instead of assumed from "Steam installed it" — only 14,340 of the manifest's 48,908 Steam-id
   titles actually have Cloud, and the default Add Games view hides whatever is flagged, so the
   assumption was hiding games nothing was backing up. Agent-side only: no server, schema or console
   change. `run-linux-tests` 63 → 69. Write-up:
   `logs/2026-08-14_steam-cloud-from-manifest.md`. It also settled a recurring idea in
   [[Decisions]]: **do not scrape PCGamingWiki — the Ludusavi manifest already is that scrape.**

2. **Linux / Steam Deck agent auto-update** — done, `logs/2026-08-15_linux-auto-update.md`, four
   phases, one commit each.
   - **Phase 1 (server) done.** The hosted-installer store is platform-aware — Windows keeps the
     storage root it always had, `linux-x64/` is a subdirectory beside it, and every update route
     takes `?platform=`, absent meaning `win-x64` so no deployed agent notices.
     `run-server-bugbounty-tests` 145 → **164**.
   - **Phase 2 (agent, check-only) done.** The agent asks for its own platform, the MZ header
     assertion is now a per-platform payload check (gzip on Linux), and the Linux daemon's
     hardcoded `getUpdateResult: () => null` is wired up — so `doctor`, the agent UI and the
     console can all say a Deck is behind. `run-linux-tests` 69 → **84**. Still read-only: a Deck
     is *told* about an update, and still takes it by re-running `install.sh`.
   - **Phase 3 (stage and apply) done.** A Deck now updates itself: the daemon stages (download,
     verify, unpack, smoke-test) and the swap happens from the unit's `ExecStartPre` at the next
     start, with the previous version kept until the new one proves it can run. `run-linux-tests`
     84 → **117**. Folds in the systemd unit hardening, and makes the packaged unit and the
     generated one one file.
   - **Phase 4 (policy, events, provenance, docs) done.** `update.staged` / `update.failed` /
     `update.rolled_back` reach the console, which on a Deck is the only notice anyone gets;
     `AutoUpdate: false` opts out of staging while still reporting being behind; Game Mode says when
     an update is waiting; release workflow pins its actions to SHAs and publishes checksums plus a
     build attestation. `run-linux-tests` 117 → **123**, `run-winagent-tests` **114/114**.
     <br>**The feature is complete.** Release notes are written as **v0.5.5**
     (`web/src/releases/0.5.5.md`) covering both halves of the branch.

Everything shipped before this: `logs/sessions.md` (reverse-chronological) and
`logs/shipped-2026-07.md`.

## Next action

1. **Finish rolling v0.5.5 out** — tagged, but confirm the deploy landed and, above all, that
   **both** rows in Config → Agent updates are filled. Uploading the installer is the step that
   always gets missed, there are now **two** of them, and the Linux one is what makes every future
   Deck update automatic. Mechanics in [[Build and Run]] → Rolling a release out. No migration.
2. **Deck hardware pass.** Three things have never been seen on real hardware: a genuine end-to-end
   self-update, the Game Mode "update is ready" notice, and the agent UI's Updates panel. Nothing
   there can lose save data, and everything behind them is covered by `run-linux-tests`, but no
   screen has been looked at. Record `systemd-analyze --user security savelocker.service` before and
   after while there ([[Backlog]] → the hardening item this folded in).
   <br>**`tasks/DeckyPlugin.md` Phase 3 wants the same trip** — plan it in if the plugin work has
   started by then.
3. **Check the live server for duplicate games.** Canonical naming stops *new* splits; it does not
   merge what a server already holds under two spellings, and there is no merge tool ([[Backlog]]).
4. **`tasks/DeckyPlugin.md`** — planned 2026-08-15, not started. Phases 1–2 are agent-side, need no
   Decky and no hardware, and Phase 2 pays off on its own (`doctor` gains "launch options are not
   set for this game").

Everything else — Deck verification, the WA-03 second-account ACL test, the remaining Windows manual
gates, the LAN enrollment-URL check, the missing LA-04/05/06/07 regression tests, and the
`WA-01`/113-of-114 baseline drift — is prioritised in [[Backlog]].

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
