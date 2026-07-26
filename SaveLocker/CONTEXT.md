# SaveLocker — Session Context

**What:** Self-hosted Windows/Linux game save sync. Hub-and-spoke: a tray/headless agent on each
PC or Steam Deck syncs saves through an ASP.NET Core server (Docker on unRAID). React admin
dashboard + embedded React agent UI + a gamepad-native Deck Game Mode UI. See [[Architecture]].

**Repo:** https://github.com/SkorcherX/SaveLocker | **Branch:** main

**Current released version:** **v0.4.1** (Deck UI navigation fix). GHCR package `savelocker` is
**public** — see [[Gotchas]].

---

## Status

Core system — Windows tray agent, Linux/Deck agent, server, dashboard, CI/CD, conflict handling,
Deck Game Mode UI — is **built and shipped**. Feature-by-feature ship log: `logs/shipped-2026-07.md`
+ `logs/sessions.md`.

## Next action

**Verify v0.4.1 on the Deck, then confirm the unRAID deploy.** unRAID: `docker compose pull &&
docker compose up -d` (see [[Build and Run]]). After tagging any release, confirm both
`docker-publish` and `release.yml` actually triggered on the tag, not just the preceding push —
see [[Gotchas]].

## Open work

See [[Backlog]] for the prioritized list. Highlights:
- **Linux agent bug bounty** — all 9 findings now have code fixes and the Windows suites are green.
  Remaining: run the Linux suites in WSL (`run-linux-tests.sh`, now 40 checks, carries the unrun
  LA-08/LA-09 checks), add regression tests for LA-04/05/06/07, and do the Deck verification. See
  `tasks/LinuxAgent-BugBounty.md` → Progress.
- **Fresh Windows installer enrollment** on a clean box (no `%PROGRAMDATA%\SaveLocker`) has never
  been exercised — see `logs/2026-07-14_installer-enrollment.md`.
- **`%PROGRAMDATA%\SaveLocker` ACLs** on a multi-user Windows box — `api-token`/`config.json` are
  readable by other local users.
- **Linux auto-update** — Deck users currently re-run `install.sh` manually; no auto-update
  channel yet.

---

## Where to look

| Topic | File |
|-------|------|
| Codebase map | [[REPO_MAP]] |
| System design | [[Architecture]] |
| Locked decisions (don't re-litigate) | [[Decisions]] |
| Recurring traps | [[Gotchas]] |
| REST endpoints | [[API Reference]] |
| Dev build & run commands, test suites | [[Build and Run]] |
| Agent CLI | `web/src/help/cli-reference.md` (KB article) |
| Active backlog | [[Backlog]] |
| Session history | `logs/sessions.md` |
