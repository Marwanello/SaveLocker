# SaveLocker — Session Context

Read this and [[REPO_MAP]] at the start of every session. This file is a **short orientation** —
where the project is right now, and where to go next. Detail belongs in the files it links to; if a
section here starts growing, move it and leave a link.

**What:** Self-hosted Windows/Linux game save sync. Hub-and-spoke: a tray/headless agent on each PC
or Steam Deck syncs saves through an ASP.NET Core server (Docker on unRAID). React admin dashboard +
embedded React agent UI + a gamepad-native Deck Game Mode UI. See [[Architecture]].

**Repo:** https://github.com/SkorcherX/SaveLocker | **Branch:** `main`

**Released:** **v0.5.4** (2026-08-10, **rolled out 2026-08-15**) — notes in
`web/src/releases/0.5.4.md`. GHCR package `savelocker` is **public** ([[Gotchas]]).

---

## Where things stand

**In flight — branch `steam-cloud-from-manifest`** (**not pushed**, not merged). It now carries two
unrelated pieces of work; the maintainer's call is that the whole branch pushes when the auto-update
feature is finished.

1. **Steam Cloud from the manifest** (2 commits). Steam Cloud is read from the Ludusavi manifest
   instead of assumed from "Steam installed it" — only 14,340 of the manifest's 48,908 Steam-id
   titles actually have Cloud, and the default Add Games view hides whatever is flagged, so the
   assumption was hiding games nothing was backing up. Agent-side only: no server, schema or console
   change. `run-linux-tests` 63 → 69. Write-up:
   `logs/2026-08-14_steam-cloud-from-manifest.md`. It also settled a recurring idea in
   [[Decisions]]: **do not scrape PCGamingWiki — the Ludusavi manifest already is that scrape.**

2. **Linux / Steam Deck agent auto-update** — in progress, `tasks/LinuxAutoUpdate.md`, four phases,
   one commit each. **Phase 1 (server) is done:** the hosted-installer store is platform-aware —
   Windows keeps the storage root it always had, `linux-x64/` is a subdirectory beside it, and every
   update route takes `?platform=`, absent meaning `win-x64` so no deployed agent notices.
   `run-server-bugbounty-tests` 145 → **161**. **Phase 2 (agent check-only) is next.**

Everything shipped before this: `logs/sessions.md` (reverse-chronological) and
`logs/shipped-2026-07.md`.

## Next action

1. **Phase 2 of `tasks/LinuxAutoUpdate.md`** — the agent sends `?platform=`, the MZ header check
   becomes a per-platform payload check, and `Daemon.cs`'s `getUpdateResult: () => null` gets wired
   up. Read-only: nothing is executed or replaced until Phase 3.
2. **Push and PR `steam-cloud-from-manifest`** once auto-update lands. Worth release-notes lines for
   both halves — users will see games appear in Add Games that were previously hidden, and Decks
   stop needing a hand-run `install.sh`.
3. **Check the live server for duplicate games** before deploying. Canonical naming stops *new*
   splits; it does not merge what a server already holds under two spellings, and there is no merge
   tool ([[Backlog]]).

Everything else — Deck verification, the WA-03 second-account ACL test, the remaining Windows manual
gates, the LAN enrollment-URL check, the missing LA-04/05/06/07 regression tests, and the
`WA-01`/113-of-114 baseline drift — is prioritised in [[Backlog]].

## Parked on a branch — `offline-backoff-task` (`bee3116`, pushed, not merged)

`SaveLocker/tasks/` is empty **on `main`**; do not conclude there is no task. `tasks/OfflineBackoff.md`
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
