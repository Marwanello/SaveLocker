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

**Start `tasks/WinAgent-BugBounty.md`** — the last of the three bug bounties. Read it first; it is
bounded and self-verifying like the other two. Everything below is context for that session.

Before the branch can ship there is also one **manual LAN check** (5 minutes, needs the real
deployment): open the console at the server's LAN address, mint an enrollment file with the URL
override blank, and confirm `policy.serverUrl` is that LAN address rather than `localhost`. Recorded
in `logs/2026-07-27_console-bugbounty.md` → Verification.

## Open work

Branch `linux-agent-bugbounty` carries **all three bug bounties** and is **not pushed**. See
[[Backlog]] for the prioritized list. State of each:

- **Console / server bug bounty — DONE 2026-07-27.** All 13 findings fixed, one commit each,
  `a761f3f` … `99648cb`. New harness `tests/run-server-bugbounty-tests.ps1` (**145 checks**). Full
  write-up, including the two scope decisions and what is deliberately untested:
  `logs/2026-07-27_console-bugbounty.md`. **This half changes the server, so the console must be
  redeployed** — and it carries **two schema migrations**, so back up `/data` first.
- **Linux agent bug bounty** — all 9 findings have code fixes and the Windows suites are green.
  Remaining: run the Linux suites in WSL (`run-linux-tests.sh`, 40 checks, carries the unrun
  LA-08/LA-09 checks), add regression tests for LA-04/05/06/07, and do the Deck verification.
  See `tasks/LinuxAgent-BugBounty.md` → Progress.
- **Windows agent bug bounty — not started.** `tasks/WinAgent-BugBounty.md`.

User-facing notes for all three accumulate in [[Release Notes Pending]]. Version is still unchosen;
the console migrations argue for v0.5.0 over v0.4.2, since a container downgrade stops being a clean
rollback.

### Suite baseline (all green at 2026-07-27, Windows)

server bug bounty 145 · agent 45 · hardening 33 · local-api 30 · concurrency 23 · health 19 ·
enrollment 18 · enrollment-TLS 6. Server build 0/0; console lint and build clean — the console build
is **warning-free for the first time** (CS-13 fixed the discarded `@import`), so a new warning there
now means something.

**Running the suites: clear `.verify/` and `src/Server/localstate/` together, and keep a dev server
on :5179 up for `run-agent-tests` and `run-enrollment-tests`.** Both traps cost time this session —
they produce confident, unrelated-looking failures (31 and 13) that read exactly like regressions.
See [[Gotchas]] → Testing.
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
| Draft notes for the next release | [[Release Notes Pending]] |
| Console bounty write-up | `logs/2026-07-27_console-bugbounty.md` |
| Session history | `logs/sessions.md` |
