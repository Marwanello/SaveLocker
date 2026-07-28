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

**Resume `tasks/WinAgent-BugBounty.md` at WA-09.** Read its **Progress** section first — it records
which findings are done, which commits they are in, and (importantly) where the test evidence is
weaker than the pass count suggests. WA-01…WA-08 are fixed and committed; WA-09…WA-12 are not
started.

WA-09 is the largest remaining item and the one that touches code the others now rely on:
`TrayContext` captures `SynchronizationContext.Current` **before `Application.Run` installs the
WinForms context**, so every `_ui.Post(...)` in `TrayApp.cs` — including ones added by WA-01, WA-05
and WA-06 — is currently a thread-pool post, not a UI-thread marshal.

Two things need the maintainer rather than the next agent:

1. **An open decision.** Two test-only environment variables now live in production code
   (`SAVELOCKER_LEASE_RENEW_SECONDS`, `SAVELOCKER_SYNC_LOCK_SECONDS`). Keep, promote to real
   settings, or remove and mark those tests manual? See the task's Progress section.
2. **Manual verification, none of which has been run.** The whole of the task's Verification
   section, plus the console bounty's outstanding **LAN enrollment-URL check** (open the console at
   the server's LAN address, mint an enrollment file with the URL override blank, confirm
   `policy.serverUrl` is the LAN address, not `localhost` — `logs/2026-07-27_console-bugbounty.md`
   → Verification). WA-03's second-account ACL test is the release blocker among these.

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
- **Windows agent bug bounty — 8 of 12 done, 2026-07-27.** WA-01…WA-08 fixed, one commit each,
  `34a7c75` … `c27197e` (plus `98a1a68`, the http/TLS decision WA-05 depends on). New harness
  `tests/run-winagent-tests.ps1` (**81 checks**), each finding's block verified to fail against
  pre-fix code. **WA-09…WA-12 remain**; WA-09 is the big one. Progress table, the four places where
  test evidence is weaker than the pass count, and the outstanding manual gates are all in
  `tasks/WinAgent-BugBounty.md` → Progress.
  This half changes the **agent, the server and both UIs**: `openapi.json` + `web/src/api-types.ts`
  moved (WA-05's update digest), and `agent-ui/src/api-types.ts` moved three times (WA-02, WA-04,
  WA-08). New agent CLI command `check-update`; new agent-local API routes for save-path confirm and
  process names.

User-facing notes for all three accumulate in [[Release Notes Pending]]. Version is still unchosen;
the console migrations argue for v0.5.0 over v0.4.2, since a container downgrade stops being a clean
rollback.

### Suite baseline (all green at 2026-07-27, Windows)

server bug bounty 145 · **win agent bug bounty 81** · agent 45 · hardening 33 · local-api 30 ·
concurrency 23 · health 19 · enrollment 18 · enrollment-TLS 6. Server build 0/0; console lint and
build clean — the console build is **warning-free for the first time** (CS-13 fixed the discarded
`@import`), so a new warning there now means something. The Windows agent build has **one**
pre-existing warning (MSB3277, a WindowsBase 4.0/5.0 conflict from WebView2); a second one means
something.

`run-winagent-tests.ps1` owns :5189 (+ :5190–5195 for daemons and stub servers) and
`.verify-winagent`. It is slow by design — ~4 minutes, most of it real waits on lease renewal and
lock contention.

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
