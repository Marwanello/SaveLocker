# Implementation grouping — how to actually work through Phases 4–14

Written 2026-08-30. This is the practical execution plan layered on top of `plan.md`'s dependency
graph: which phases to do in the same session, in what order, and why — driven by three things,
weighed together rather than any one alone:

1. **Real dependencies** (`plan.md`'s own diagram — some phases genuinely can't start before others).
2. **What this environment can actually verify.** A cloud/remote Claude Code session here is a Linux
   container scoped to this repo only — no Windows, no Steam Deck, no Playnite, and the separate
   `SaveLocker-Decky` repo isn't attached. Writing code that can't be run anywhere is the worst
   value-per-session-of-work in this whole plan, so verifiability gates sequencing as hard as
   dependencies do.
3. **Session-cost precedent from this repo's own history**, used as the sizing yardstick instead of
   guessing: Phase 0/1 (foundational, ~3,077 insertions across 23 files, multiple review/CI rounds)
   was a heavy session; **Phase 2 + Phase 3 were already lumped into one PR** (~426 insertions, 5
   files, one clean round) and that worked fine — proof that two independent, non-file-overlapping
   phases comfortably share a session when neither depends on the other's UI existing first.

**On token/quota cost specifically:** running a session in the cloud vs. locally via the Claude Code
CLI does **not** change how much of your account's weekly quota it costs — the same plan, same
weekly pool, regardless of which machine runs the container. So environment choice below is driven
entirely by *verifiability*, never by quota. The only quota lever that actually exists is: **do
fewer, better-scoped sessions**, and don't ship a phase that can't be checked from wherever it's
built.

## Status (updated 2026-09-03)

| Group | Contents | Status |
|---|---|---|
| 1 | Phase 5, Phase 9 spike | ✅ Done 2026-08-30 |
| 2 | Phase 4, Phase 6 | ✅ Done 2026-08-31 |
| 3 | Phase 9 (D-Bus impl) | ✅ Done 2026-09-01 |
| 4 | Phase 8 | ✅ Done 2026-09-03 |
| 5 | Phase 7 | ✅ Phase 7 done 2026-09-03 — Phase 14 deliberately left out of this pass, see `plan.md`'s Status section for why |
| 6 | Phase 10, 11, 13 | ⬜ Not started — needs the `SaveLocker-Decky` repo attached, and Deck/Windows hardware |

## Which phases are actually reachable from a cloud/remote session

| Phase | Buildable here? | Verifiable here? |
|---|---|---|
| 4, 5, 6, 12, 14 | Yes | **Yes** — existing Linux fake-game harness (`run-linux-tests.sh`, `run-agent-tests.ps1`) covers all of these |
| 8 (Game Mode screen) | Yes | Build/compile only — real gamepad-nav / WSLg confirmation needs the Deck or a Windows+WSLg box |
| 7 (Windows tray wiring) | Yes | **No** — WinForms/WebView2 only runs on Windows |
| 9 (D-Bus notification impl) | Yes, once Phase 6 ships — no `SaveLocker-Decky` repo access needed at all, its only real dependencies are Phase 5 (done) and Phase 6 | **No** — needs a real desktop session with a notification daemon to see a popup actually fire |
| 10, 11 (Decky) | **No** — separate `SaveLocker-Decky` repo not attached to this session | No — needs real Deck hardware regardless of repo access |
| 13 (Playnite) | Effectively no — `.NET Framework 4.6.2` + Playnite SDK wants a Windows toolchain | No — needs Windows + Playnite installed |

## Groups

**Group 1 — small, done 2026-08-30.**
`Phase 5` (Linux environment-capability detection) + `Phase 9`'s **D-Bus library spike/decision
only** (no notification code — just picking the tool, so Phase 9's eventual implementation session
doesn't re-derive it). Both mutually independent, disjoint files, fully buildable and testable here.
`Phase 12` was originally planned as this group's third item and turned out **not to be a valid
small standalone task** — see "Correction found while starting Group 1" below. Smaller than the
Phase 2+3 precedent even without it.

### Correction found while starting Group 1: Phase 12 pulled from this group

Before wiring any UI to `GET /api/games/{id}/sync-status`, its actual handler was read (rather than
trusting this document's own earlier description of it as "the cheap sync-status endpoint... poll
this instead of a full conflict-list fetch"). That description was wrong: the handler's own comment
says it is "NOT cheap on disk: the local hash still walks and reads every file in the save folder,"
and it calls `ApiClient.GetStateAsync` internally — the same full-state fetch `AgentCli.cs`'s
existing `status` command already makes. It is strictly *more* expensive than what a per-game status
loop does today, not a lighter replacement for one. Wiring any passive UI poll to it (which is what
"give it a consumer" was about to become, since `agent-ui` has no per-game list surface to attach a
badge to before Phase 6 exists anyway) would have been re-hashing save folders on a timer — a real
performance regression, not a small cheap win. `plan.md`'s Phase 12 entry and this file are both
corrected; Phase 12 now waits for a genuine on-demand trigger (a button, a CLI flag, or a one-shot
call at a specific decision point) to attach to, decided when Phase 6, 8, or 10 actually builds one —
not before.

**Group 2 — medium, the biggest value-per-session in the whole plan. Done 2026-08-31.**
`Phase 4` (Linux wrapper launch gate) + `Phase 6` (shared `agent-ui` conflicts page + the confirmed
`doctor`/log gap). Independent of each other and of Group 1. Roughly Phase-2+3-sized. This is what
makes conflict resolution end-to-end usable on a plain Linux box for the first time — do this before
any Decky/Windows/Playnite work, since it's the one grouping that delivers real user value with zero
hardware dependency. Shipped as `SyncEngine.PrepareLaunchAsync` (Phase 4, wired into
`ProtonRun.ExecuteAsync` only — Windows' `TrayApp.cs` is untouched, per Phase 7 below) and
`agent-ui/src/components/ConflictsView.tsx` + two new version-lookup routes (Phase 6). Verified live:
a real two-machine conflict seeded against a dev server, resolved through a real browser session
against the real local API (Playwright screenshot), and `savelocker run` confirmed to refuse a launch
on the open conflict and proceed normally once resolved. Full write-up: `CONTEXT.md`/`Backlog.md`,
2026-08-31.

**Group 3 — small, code-only here, live verification later. Requires Group 2 to have shipped first.
Done 2026-09-01.** `Phase 9`'s actual D-Bus notification implementation. Its real dependencies (per
`plan.md`'s own dependency diagram) are Phase 5 (done, Group 1) and Phase 6 (Group 2's `agent-ui`
conflicts page — the action button's target) — nothing else. Buildable and unit-testable here the same
way Phase 5 was: the `gdbus … org.freedesktop.Notifications.Notify` call, the once-per-conflict
`HashSet<Guid>` dedup so the 20s command-poller doesn't re-fire it every tick, and wiring the action
button at Phase 6's page URL, all exercisable against a fake/absent D-Bus socket using the same harness
pattern `run-linux-tests.sh` already established for Phase 5. What can't be checked here is whether a
real notification daemon actually pops the banner and the button opens the right page — that needs a
live Deck Desktop Mode session or a plain desktop Linux box, the same class of hardware-only gap
Phase 8 has below. See "Correction found before Group 3" below for why this is its own group instead of
folded into Decky work.
<br>**Shipped as:** `CommandPoller`'s new optional `onConflictsPolled` hook (null on Windows — Phase 7
still owns wiring the tray) and `src/Agent.Linux/ConflictNotifier.cs`. Full detail, the GVariant
string-escaping bug caught before shipping, and the before/after `git stash` comparison proving the
pre-existing 7-failure Decky-plugin-update flake (`Backlog.md`, Phase 5) is unrelated: `CONTEXT.md` and
`Backlog.md`, 2026-09-01. Not yet run against real hardware — same as every phase below.

### Correction found before Group 3: Phase 9 was miscategorized, not deferred for a real reason

This document originally bundled "Phase 9's actual D-Bus implementation" into the same group as Decky
(Phase 10/11) and Playnite (Phase 13), on the reasoning that all three "need real hardware." Re-checking
`plan.md`'s own dependency diagram before starting Group 2 showed that reasoning doesn't hold: Phase 9
depends only on Phase 5 and Phase 6, never on Decky, Playnite, or the separate `SaveLocker-Decky` repo.
Grouping it with Decky/Playnite invented a dependency it doesn't have and would have delayed a buildable
phase behind an unrelated repo-access and Windows/Deck-hardware requirement it never needed. It is *live
verification* Phase 9 shares with Decky/Playnite work, not a build dependency — so it gets its own
group, placed right after the one phase (Phase 6) it actually needs, not lumped in with hardware it
doesn't touch.

**Group 4 — medium, code-only from here. Done 2026-09-03.**
`Phase 8` (Game Mode conflict screen) — independent of Groups 1–3. Shipped as a new `Screen.Conflicts`
in `Ui/UiApp.cs`, reusing `ApiClient` directly (in-process, no daemon round trip — resolving a
conflict is a stateless mechanical server call, unlike "Sync now") for a fourth, badge-numbered rail
entry and a two-panel local-vs-cloud card mirroring the dashboard/`agent-ui`'s own. Two new hand-drawn
icons (`Cloud`, `GitBranch`) matching the lucide icons the React surfaces already use. **Verified
beyond this group's own "code-only" ceiling**: this session ran on a Windows dev box with WSLg, which
is exactly the environment named above as what a prior cloud-container session couldn't supply — a
real two-machine conflict seeded via `tests/seed-test-conflict.sh`, a `-r linux-x64` build run under
it, and a scripted `--nav` D-pad+A sequence that pressed a real Keep button and resolved the conflict
through the real local `ApiClient` call, confirmed independently via a separate CLI check. A real bug
was caught doing this: the `--screenshot` capture-and-exit path's busy gate didn't wait on the new
resolve task, so a scripted press had its in-flight request killed before it reached the server — fixed
by adding it alongside `_syncNowTask`, the existing analog. Full detail: `Backlog.md`. Still unchecked:
real Steam Input/gamescope specifically (a WSLg pass never covers those two, on any screen in this
codebase).
<br>**A recall review of this PR (2026-09-03) found and fixed 11 issues** (a per-frame retry storm on
a failed version fetch, a slow-server task-cancellation that froze conflict polling permanently, a
missed local-state sync on "keep local," an unbounded version/stats cache leak, and others — commit
`0b8a608` on `save-conflicts-phase-8-review-fixes`). **Three findings were left as future work, now
tracked in `plan.md`**: no `ConflictsScreen` class (Phase 8 section), this screen polling the remote
server directly instead of through the daemon's local API (Phase 8 section), and no shared
`Agent.Core` version/stats cache for Phase 7 to reuse (Phase 7 section).

**Group 5 — Phase 7 done 2026-09-03; Phase 14 deliberately not started this pass.**
`Phase 7` (Windows tray automatic chooser + bulk queue) + `Phase 14` (webhook notify + the per-game
"block launch" setting — bundled with Phase 7 here only because both touch tray/agent-ui settings
surfaces, not because of a hard dependency). Originally deferred waiting on a Windows-connected
session; this one was, so Phase 7 shipped. Phase 14 was asked about directly and scoped OUT of this
session on purpose (see `plan.md`'s Status section: its block-launch half has a real, unresolved
design fork against already-shipped Phase 4 behavior that needs a maintainer decision before code, not
during).
<br>**Shipped as:** `TrayApp.cs`'s three native trigger points that can leave a conflict open — Sync
All, Force Pull, Force Push — now check for one afterward and raise `AgentWindow` straight to a new
`#conflicts:queue` deep link, which `App.tsx` recognizes and uses to auto-open the exact same
`SyncConflictModal` queue pop-up "Sync now" already shows. `SyncConflictModal.tsx` gained the "apply
to all remaining" bulk-resolve step item 2 of Phase 7 asked for: after the first resolve in a
multi-conflict queue, one prompt offers to apply the same choice to every remaining conflict, falling
back to the existing one-at-a-time flow on "Review each." Verified live against a real two-machine,
two-game conflict scenario and the actual built Windows tray — full detail in `plan.md`'s own Phase 7
section and `CONTEXT.md`, including the one thing this environment could not literally automate (a
click on the NotifyIcon's own context menu — no tool here drives native Win32 tray UI).

**Group 6 — later, needs the separate Decky repo + real hardware (Deck and/or Windows).**
`Phase 10`/`Phase 11` (Decky — add the `SaveLocker-Decky` repo to whatever session does this) +
`Phase 13` (Playnite).

```
Done 2026-08-30       →  Group 1 (5, 9-spike)                  tiny, fully verifiable here
Done 2026-08-31       →  Group 2 (4, 6)                        medium, biggest value, fully verifiable here
Done 2026-09-01       →  Group 3 (9-impl)                      small, code-only here, needed Group 2 first
Done 2026-09-03       →  Group 4 (8)                           medium, verified live under WSLg
Done 2026-09-03       →  Group 5 (7)                           verified live on a real Windows dev box; Phase 14 held back — see above
Deck + Windows        →  Group 6 (10, 11, 13)
Whenever 6/8/10 adds a "check now" trigger → Phase 12 (sync-status consumer)
```

## Re-evaluate before each new group

This grouping assumes the dependency graph and environment constraints in `plan.md` as of
2026-08-30. If a later session adds Windows or Deck access to the environment, or attaches the
`SaveLocker-Decky` repo, re-check the "buildable/verifiable here" table above before assuming a
group still needs to be deferred — the constraint driving the deferral may no longer hold.
