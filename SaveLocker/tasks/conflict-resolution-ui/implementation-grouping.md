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

## Which phases are actually reachable from a cloud/remote session

| Phase | Buildable here? | Verifiable here? |
|---|---|---|
| 4, 5, 6, 12, 14 | Yes | **Yes** — existing Linux fake-game harness (`run-linux-tests.sh`, `run-agent-tests.ps1`) covers all of these |
| 8 (Game Mode screen) | Yes | Build/compile only — real gamepad-nav / WSLg confirmation needs the Deck or a Windows+WSLg box |
| 7 (Windows tray wiring) | Yes | **No** — WinForms/WebView2 only runs on Windows |
| 9 (D-Bus notification impl) | Spike/decision: yes. Full implementation: yes, but nothing to click | **No** — needs a real desktop session with a notification daemon |
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

**Group 2 — medium, the biggest value-per-session in the whole plan.**
`Phase 4` (Linux wrapper launch gate) + `Phase 6` (shared `agent-ui` conflicts page + the confirmed
`doctor`/log gap). Independent of each other and of Group 1. Roughly Phase-2+3-sized. This is what
makes conflict resolution end-to-end usable on a plain Linux box for the first time — do this before
any Decky/Windows/Playnite work, since it's the one grouping that delivers real user value with zero
hardware dependency.

**Group 3 — medium, code-only from here.**
`Phase 8` (Game Mode conflict screen) — independent of Groups 1–2. Build and compile-check here;
flag verification as pending a WSLg or real-Deck pass, the same honest way every other hardware-gated
feature in this project ships.

**Group 4 — defer to a session on a Windows-connected machine.**
`Phase 7` (Windows tray automatic chooser + bulk queue) + `Phase 14` (webhook notify + the per-game
"block launch" setting — bundled with Phase 7 here only because both touch tray/agent-ui settings
surfaces, not because of a hard dependency). Reason to defer: not cheaper, just **verifiable** —
shipping WinForms code nobody can run risks paying for it twice (once to write it, again to fix what
a five-minute manual check would have caught).

**Group 5 — later, needs the separate Decky repo + real hardware (Deck and/or Windows).**
`Phase 9`'s actual D-Bus implementation (once Group 1's spike has picked a library and there's a real
desktop session to test against) + `Phase 10`/`Phase 11` (Decky — add the `SaveLocker-Decky` repo to
whatever session does this) + `Phase 13` (Playnite).

```
Done 2026-08-30       →  Group 1 (5, 9-spike)                  tiny, fully verifiable here
Following session    →  Group 2 (4, 6)                        medium, biggest value, fully verifiable here
Following session    →  Group 3 (8)                           medium, code-only here
Windows machine       →  Group 4 (7, 14)
Deck + Windows        →  Group 5 (9-impl, 10, 11, 13)
Whenever 6/8/10 adds a "check now" trigger → Phase 12 (sync-status consumer)
```

## Re-evaluate before each new group

This grouping assumes the dependency graph and environment constraints in `plan.md` as of
2026-08-30. If a later session adds Windows or Deck access to the environment, or attaches the
`SaveLocker-Decky` repo, re-check the "buildable/verifiable here" table above before assuming a
group still needs to be deferred — the constraint driving the deferral may no longer hold.
