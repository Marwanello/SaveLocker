# Session summary — 2026-09-03 (cont'd) — Phase 7 shipped, PR #30

**Branch:** `save-conflicts-phase-7` (renamed from `claude/save-conflict-next-group-e7bdca`). PR:
https://github.com/Marwanello/SaveLocker/pull/30.

## What was asked

1. "Implement the next group in the save conflict please. Also add on the start of the phases doc and
   the grouping doc check markers with the completed groups and phases." Group 5 (Phase 7: Windows tray
   automatic chooser + bulk queue, plus Phase 14: webhook notify + block-launch setting) was next in
   sequence, deferred pending a Windows-connected session — this one. User scoped it down to
   **Phase 7 only** (Phase 14 explicitly excluded).
2. Follow-up questions on manually verifying via `tests/testenv.ps1`, including remote-network access.
3. Live terminal output showing seeding failures — diagnosed and fixed.
4. User's own fix, given as feedback: reversed the provided command order (build, up, then WSL seeding,
   then Windows seeding) to get the conflict popup to actually appear — asked for a corrected, complete
   "apply to all remaining" test scenario built around that ordering.
5. "All verfied. Is this pushed?" then "Do 2 then psuh and create PR" (rename the branch to the
   `save-conflicts-phase-N` convention, push, open a PR).

## What was built

- **`TrayApp.cs`**: `CheckConflictsAndRaiseAsync()` — after Sync All, Force Pull, or Force Push, checks
  `GetOpenConflictsForMachineAsync` and raises the agent window straight to `conflicts:queue` if any
  exist, instead of leaving the user to notice on their own.
- **`App.tsx`**: parses the `conflicts:queue` hash deep link on startup and auto-populates the sync
  queue from open conflicts, one-shot.
- **`SyncConflictModal.tsx`**: after the first resolve in a multi-conflict queue, an `ApplyToAllPrompt`
  offers to apply the same choice (cloud/local, keep-both) to every remaining conflict in one batch
  (`applyToAllRemaining()`), or continue reviewing each one individually (`reviewEach()`).

## Bug found and fixed mid-verification: cross-process config lost-update race

Windows `push` failed with `REFUSED push: ... (mapped to '')` right after a successful-looking
`add-game`. Root cause, found by reading `config.json` directly (`"Games": []`) and cross-checking with
`tasklist`/`netstat`: a live WinTest tray process from an earlier `testenv.ps1 up` held its own
in-memory `AgentConfig`, and its own next unrelated `Save()` overwrote the CLI's out-of-band addition
with its stale `Games: []`. Not a product bug in the sense of the shipped feature — a flaw in the
manual-test instructions given (seeding against a config file a live tray already had open). Fixed by
killing the stale tray process and giving corrected seeding-before-tray-start instructions.

**User's own correction, now the standing test-ordering rule for this feature:** do ALL CLI seeding
(WSL then Windows) before starting the Windows tray at all — start the tray last (`up -Only windows`
as the final step), not first, so nothing races the seed writes.

## Verification

Manually verified live via a hand-built two-machine `testenv.ps1` scenario (console + Linux agent
seeded first, both sides seeded with two conflicting games "Bulk Test A"/"Bulk Test B", Windows tray
started last), confirming both the automatic chooser raise-on-sync and "apply to all remaining" queue
behavior. User confirmed "All verfied."

## Landed

- Branch renamed `claude/save-conflict-next-group-e7bdca` → `save-conflicts-phase-7`, pushed to
  `origin` (`Marwanello/SaveLocker`) with upstream tracking.
- PR [**#30**](https://github.com/Marwanello/SaveLocker/pull/30) opened, `save-conflicts-phase-7` →
  `main`.
- Vault updated: `plan.md` and `implementation-grouping.md` gained "Status" tables with ✅/⬜ markers
  for all phases/groups, `Backlog.md` and `CONTEXT.md` updated with the Group 5 (Phase 7 only) writeup.

## Not done

- Phase 14 (webhook notify + block-launch setting) — explicitly excluded from this session's scope,
  still queued as the remainder of the original Group 5.
- PR #30 not yet reviewed or merged as of this write-up.

---

# Session summary — 2026-09-03

Phase 8 of the conflict-resolution plan (the native Linux Game Mode conflicts screen) implemented and
verified live under WSLg, then a broken icon caught by the user's own review went through two rounds of
fixes — the first attempt was itself still wrong — plus an unrelated icon/text alignment bug the user
caught on the same screen.

## What was asked

1. Session opened with "continue from where you left off" after a usage-limit reset, but no
   uncommitted work existed — the task was derived from the project's own mandatory session-start
   files (`CONTEXT.md`, `REPO_MAP.md`) plus `tasks/conflict-resolution-ui/implementation-grouping.md`:
   Group 4 / Phase 8, scoped there as code-only with live verification deferred to "a WSLg or
   real-Deck pass."
2. After screenshots of the finished feature were sent: **"the icons here looks off / please fix
   them"** — the operative correction that started the icon work.
3. After the first fix's screenshots: **"The cloud icon still looks wierd on the right side fix it.
   and also the alignment of the this device icon and clodu icon in conflict screen is off from the
   text. also cloud icon when the menu is empty look very bad"** — the first fix had not actually
   solved it, plus a second, distinct bug in the same screen.

## What was built

- The Game Mode Conflicts screen in `src/Agent.Linux/Ui/UiApp.cs`: a new `Screen.Conflicts` reusing
  the existing in-process `ApiClient` and async-`Task`-field-draining conventions, rendering a card per
  open conflict with per-side file-count/newest-mtime stats and Keep-local/Keep-cloud buttons, matching
  the already-shipped React `ConflictCard`/`ConflictsView` visually and interactionally.
- Two new hand-drawn icons (`Cloud`, `GitBranch`) and an `AccentRed` theme color.
- A real, previously-latent bug fix: the `--screenshot` dev tool's busy-gate didn't wait for an
  in-flight conflict-resolve request, so a scripted button press could have its HTTP call killed by
  `Environment.Exit` before reaching the server.

## The catch: a broken Cloud icon, wrong twice

This environment turned out to have a real WSL `Ubuntu` distro with a working WSLg display — the exact
capability the plan had assumed this session wouldn't have — so the feature was verified with live
screenshots instead of left as a code-only deferral. Those screenshots are what let the user actually
see the problem: the hand-drawn `Cloud` icon (two disconnected `PathArcTo` arcs) rendered as an
unrecognizable double-blob shape, not a cloud, at every size. The **first** fix (a hand-guessed closed
polygon) looked plausible in isolation but was still wrong — the user caught a concave dent on the
right lobe where the guessed points curved inward instead of bulging out.

The **second** fix stopped guessing: it solved lucide's actual `cloud` SVG path
(`M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z`) by hand — applying the standard SVG
elliptical-arc-to-center formula to recover each arc's true center and sweep (a radius-7 lobe for the
main body, a radius-4.5 lobe for the right bump, both derived exactly), then sampling each at even
steps into a 13-point polygon that traces the real curve. Confirmed via an 8×-upscaled crop of the
gallery screenshot showing a clean, undented cloud, and again in both real product screens.

## The second bug: icon/text baseline misalignment

A separate, unrelated bug in the same feedback: the "This device"/"The cloud" icon-then-label row in
`DrawConflictSide` (`UiApp.cs`) called `ImGui.AlignTextToFramePadding()` before the label text, even
though the preceding icon is exactly one text line tall. That call exists to align plain text against
a *taller* framed sibling on the same line (the codebase's own `Toggle`/`HintLabel` use it correctly
for that reason) — calling it here, where nothing on the line is taller, just pushed the label down by
`FramePadding.y` for no reason, visibly separating it from the icon above it. Fixed by removing the
stray call; confirmed with a pixel-level zoomed crop of a real conflict card showing the icon and
label now sharing a baseline.

## Side quest

Building `SaveLocker.sln` failed twice (once per reseed, hit again on this second round) with
`'tsc' is not recognized`, both times because a WSL-native `npm` run against `agent-ui/` (a
Windows-shared `/mnt/d/...` path) had reinstalled `node_modules` with Linux-native bindings that break
the Windows `tsc` shim — a gotcha distinct from the vault's existing "no `node_modules` at all" note.
Fixed both times without touching the committed lockfile (`git checkout` to revert an accidental
rewrite, then `npm ci`).

## Verification

Live WSLg screenshots at every stage: the `--gallery` icon strip (upscaled crops to inspect geometry
pixel-by-pixel), a freshly reseeded two-machine conflict's populated card and empty state, and a
scripted resolve confirmed closed via an independent CLI check. Clean `dotnet build` across the
Windows `Agent.Linux` project, the full solution, and the linux-x64 UI binary after every round.

## Commits

`9af2a0c` (Phase 8 code) · `d13a954` (Phase 8 docs) · `141d747` (first Cloud icon fix, superseded) ·
`ee7cb3d` (docs correction after that fix). The arc-derived Cloud fix and the alignment fix are not
yet committed as of this write-up.

## Not done

Group 5 (Windows tray wiring + webhook opt-in) — next in `implementation-grouping.md`, only mentioned
as available future work, not started or requested. The arc-derived icon fix and alignment fix are
implemented and verified but uncommitted, pending the user's go-ahead.

---

# Session summary — 2026-09-02

PR #26 xhigh code review on `save-conflicts-phase-9`: 14 findings synthesized from 9 parallel review
agents, 12 fixed with minimal edits, 1 confirmed on real hardware with the user's help, 1 left as a
design call awaiting the user's decision.

## What was asked

1. `/code-review xhigh PR#26` — run 9 parallel review agents across differing angles.
2. Apply all 14 deduplicated findings with minimal edits, report outcomes via `ReportFindings`.
3. User offered real-hardware testing for the 2 skipped findings (#7 and #10).
4. Iterative test-script refinement and real-Deck verification for finding #7.

## The 12 fixed findings

Concentrated in `ConflictNotifier.cs`, `CommandPoller.cs`, `Daemon.cs`, `Program.cs`, and
`tests/linux/run-linux-tests.sh`. The headline fixes:

- **Race between `EnableRaisingEvents` and `_live` dictionary population** (#1/#3 combined): a
  fast-exiting `notify-send` could fire `Exited` before its own entry existed. Fixed by committing
  both `_notified.Add` and `_live[id]` under the lock *before* setting `EnableRaisingEvents`, and
  only after `Process.Start` succeeds.
- **Fire-and-forget tick disposal race** (#2/#4): `_ = TickAsync()` let `Dispose()` tear down
  `ConflictNotifier` while a tick was still in flight. Fixed with `_lastTick = TickAsync()` and a new
  `StopAsync()` that awaits it; `Daemon.DisposeAsync` calls `StopAsync()` before disposing dependents.
- **`Forget`/`Withdraw` concurrent `Kill()`/`Dispose()` on the same Process** (#5): moved
  `proc.Dispose()` inside the same lock `Withdraw` uses for `Kill()`.
- **Sequential independent async work** (#6): `RunCommandsAsync` and `CheckConflictsAsync` now run
  concurrently via `Task.WhenAll`.
- **Hardcoded 20s poll interval, untestable** (#12): added `SAVELOCKER_POLL_MS` env-var hook.
- **Test helpers extracted** (#13): `wait_for_port()`, `start_fake_unix_socket()`; `cat` replaced with
  `tail -60` on shared logs; `sleep 25` reduced to `sleep 2` + fast polling.

Plus: `Func<string>` → `string` for `_conflictsUrl` (#8/#14), static → instance `Withdraw` (#9),
`Log` → `AgentLogger.LogException` (#11).

## Finding #7: confirmed on real hardware

**The concern:** `Withdraw()`'s `Kill()`-based notification teardown relies on undocumented,
daemon-specific behavior (the ownership rule that connections dropping tear down their notifications).

**The fix:** strictly additive — added `--print-id` to `notify-send`, parsing the numeric id from
stdout; `Withdraw()` now calls `CloseNotification(id)` via `gdbus` (using the existing
`ProcessRunner.Run` helper) *before* the existing `Kill()` fallback, which still always runs
regardless.

**Real-hardware confirmation (user on Steam Deck):** after two rounds of test-script refinement
(fixing my own shell `<id>` placeholder bug, then a timing artifact where the test closed the popup
too fast to see), the user confirmed:
- The popup stayed visible steadily for a deliberate 5-second pause.
- It vanished exactly at the `CloseNotification` call.
- `notify-send` exited on its own with status 0, no `Kill()` needed.

Recorded in `CONTEXT.md` (matching the file's dated hardware-verification convention) and in
`ConflictNotifier.cs`'s `CloseById` doc comment.

## Finding #10: left as a design call

`ConflictNotifier._notified` and `HealthReporter._notifiedConflictEscalations` use similar
`HashSet<Guid>` dedup patterns but have deliberately different lifecycles: `_notified` clears per-id
when a conflict resolves, `_notifiedConflictEscalations` resets per-tick. Forcing a shared abstraction
would add indirection without simplifying either call site. **Still awaiting the user's decision** on
whether to unify anyway or leave as-is.

## Verification

- `dotnet build --no-incremental`: 0 warnings, 0 errors on every build.
- `bash -n tests/linux/run-linux-tests.sh`: clean.
- Real Steam Deck hardware testing for finding #7.

## Commits

- Code-review fixes (12 findings across 5 files)
- `d9947c8` Docs: record real-hardware confirmation of CloseNotification withdraw

## Not done

- Finding #10 unification — open question, not yet answered by the user.
- No PR created for this branch (existing PR #26 already covers it).

---

# Session summary — 2026-09-02 (cont'd)

Follow-on to the same day's Checkpoint UI redesign session: the implementation plan got a
session-by-session grouping, three factual errors in it were caught and corrected, a documentation
mistake from earlier in the session was found and repaired, and the two remaining artifact-only
mockups were committed to the repo. **Still design/docs only — no application code was changed.**

**Branch:** `ui-redesign-plan`, created off `claude/dashboard-agent-ui-redesign-992386`.
**Commits:** `fe86729`, `ab4c957`, `de2cfd5`.

## Implementation grouping

Added `implementation-grouping.md` to `SaveLocker/tasks/checkpoint-ui/`, matching the
`tasks/conflict-resolution-ui/` precedent for a standing multi-session design folder. It regroups the
8 phases **by surface** instead of by phase number — Sync all and the notifications bell both land in
the top bar Phase 2 already rewrites, so splitting them by phase number would mean editing
`NavBar.tsx` twice. Seven groups; Phase 8 (assets) pulled forward into Group 1 since the art is
already designed and the favicon is the cheapest end-to-end proof the token pipeline works. Group 5
(pushing appearance to agents over the heartbeat) is the only wire-format change and stays alone.
Groups 6 (Deck) and 7 (Linux notifications) compile here but need real hardware or a live desktop
session to verify.

## Three corrections, found by reading source instead of trusting the earlier doc

- **`agent-ui` has no Tailwind and no `.css` file at all** — 215 inline `style={{}}` sites, zero
  classNames. It can't "import the same file" as the console. How it receives design tokens is now
  an explicit Group 1 decision (recommended: a plain CSS custom-property file both apps import).
- **`ArtService` already fetches the 600×900 `grid` kind** plus `hero`/`logo`/`icon`, with all four
  URLs already on the game DTO. The grid wall needs zero server work.
- **`GET /api/games/{id}/sync-status` must not back a list view.** Its own handler comment warns it
  walks and reads every file in the save folder, plus a full `GetStateAsync`. Polling it per game in
  the agent's new Games tab would re-hash every save folder on a timer — the same mistake the
  conflict-resolution plan caught and pulled its own Phase 12 for.

Also counted the real scale of the inline-style problem: 388 sites in `web/src` against 4 classNames,
215 in `agent-ui/src` against 0. Reframes Phase 1: layering the CSS reset unblocks Tailwind utilities
but converts nothing — the migration rides inside later groups one surface at a time, deliberately
with no dedicated "migrate everything" session.

## Incident: this file's own history was clobbered, then restored

While writing the prior turn's summary entry, this file — a running log, newest-first — was
overwritten with a truncating redirect instead of prepended, dropping four prior entries
(2026-09-01, 2026-08-30/31, 2026-08-29, and earlier). Caught from an unexpectedly large deletion
count on the next commit. Restored every prior entry byte-identical (verified by diff) and prepended
the new entry in the file's own order. Commit `ab4c957`. This entry is being written with the same
read-then-prepend method specifically to not repeat that mistake.

## Mockups committed to the repo

Copied the two remaining artifact-only deliverables into `SaveLocker/tasks/checkpoint-ui/` so nothing
depends on the live artifact links: `prototype.html` (the interactive mockup) and
`identity-options.html` (the five identity pitches Checkpoint was chosen from). `brand-kit.html` was
already committed earlier in the day. `README.md` and `plan.md` updated to point at the local files
as the primary reference, keeping the artifact URLs as link-sharing mirrors only. Commit `de2cfd5`.

## Status

`SaveLocker/tasks/checkpoint-ui/` now holds the complete design-phase deliverable set locally:
`plan.md`, `implementation.md`, `implementation-grouping.md`, `README.md`, `prototype.html`,
`identity-options.html`, `brand-kit.html`. Everything is on branch `ui-redesign-plan`, local only,
not pushed, no PR opened. `CONTEXT.md` still hasn't been updated with a handoff entry.

---

# Session summary — 2026-09-02

A full UI redesign of the console and agent UI, taken from five identity pitches to an agreed
direction, an interactive prototype, a design spec, an 8-phase implementation plan, a session-by-
session grouping, and a brand kit. **Design only — no application code was changed.**

**Chose "Checkpoint"** out of five pitched identities (Cold Storage / Checkpoint / Ledger / Shelter /
Hangar), each built as a real mockup with its own palette, type stack and voice rather than described
in the abstract. Checkpoint dresses SaveLocker as something you own rather than something you
administer.

**Built an interactive prototype** covering Console, Agent, Deck/Wayland, Notifications, Marks & art
and Flows — light and dark, five accents, three marks, navigable rather than a picture. Everything in
it is real data: real game and machine names, real `AgentEventCodes` values, real release dates from
`web/src/releases/`, real Deck UI strings, the real Decky panel order.

**Decisions taken:** Archivo for headings *and* data, with `tabular-nums` replacing the old monospace
columns (mono survives only in code, CLI output and log excerpts); Ember as the default accent, user-
changeable across five options, and reserved to mean "a decision is waiting" so it can never signal
healthy or broken; light and dark as separate first-class palettes rather than an inversion; three
marks (Cartridge, Pixel lock as default, Memory card); Steam library art approved as drawn. The Decky
plugin is deliberately **out of scope** — its Steam-native look is correct, and an earlier Steam-styled
redesign of it was built and then discarded on review.

**Found the root cause of the codebase's inline-style pattern.** `web/src/index.css` has an unlayered
`* { box-sizing; margin: 0; padding: 0 }` that beats every Tailwind utility regardless of specificity,
which is why `NavBar.tsx` and most of `GameDetail.tsx` are written as inline style objects. Layering
it is Phase 1 and gates everything else.

## Corrections found while writing the grouping (checked against source, not assumed)

Three claims in the freshly-written `implementation.md` were wrong, and were corrected there before
any code was planned around them:

- **`agent-ui` has no Tailwind and no `.css` file at all** — it is 215 inline style objects and zero
  classNames — so it cannot "import the same file" as the console. How it receives design tokens is
  now an explicit decision Group 1 must make (recommendation: a plain CSS custom-property file both
  apps import, since the agent's existing inline styles can consume `var(--…)` without conversion).
- **`ArtService` already fetches the 600×900 `grid` kind** plus `hero`/`logo`/`icon`, with all four
  URLs already on the game DTO. The grid wall needs no server work at all.
- **`GET /api/games/{id}/sync-status` is not a list-view source.** Its own handler comment says it
  walks and reads every file in the save folder, plus a full `GetStateAsync`. Backing the new Games
  tab's list with it would re-hash every save folder on a timer — the same mistake the conflict-
  resolution plan caught and pulled its Phase 12 for.

Also counted the real scale of the migration, which reframes Phase 1: **388** inline styles in `web`
against 4 classNames, and 215 in `agent-ui` against 0. Tailwind is installed in `web` and effectively
unused. Layering the reset *unblocks* utilities but converts nothing, so the migration deliberately
rides inside later groups one surface at a time; there is no "migrate all inline styles" session.

## Grouping

Seven groups, regrouped **by surface** rather than by phase number, because this is a reskin and
several phases edit the same components — Sync all and the notifications bell both live in the top bar
Phase 2 already rewrites, so they ship together or `NavBar.tsx` gets edited twice. Phase 8 (assets)
moves *forward* into Group 1: the art is already designed, and the favicon is the cheapest end-to-end
proof the token pipeline works. Group 5 (appearance pushed to agents over the heartbeat) is the only
one that changes the wire format and stays alone. Group 6 (Deck) compiles here but needs real hardware
to verify; Group 7's Linux notification half is buildable but not observable on this machine.

One open decision, blocking nothing: the Wayland desktop window — host the agent web UI in a small
GTK/WebKit window with a header bar, or accept the browser.

## Bugs found and fixed in the prototypes themselves

- Progress-bar entrance animations replaying on every tick, because each tick rebuilt the whole view.
  Fixed with targeted DOM patching — and recorded in the plan as a **correctness requirement** for the
  real implementation, not polish: progress must live in its own component and its surroundings must
  not be a dependency of it.
- Brand-kit theme swatches showing stale values, because they repainted inside `requestAnimationFrame`,
  which never fires in a hidden tab. Fixed by painting synchronously.
- A `.cap` class collision between the Steam capsule art and the spec-sheet labels.

## Landed

`SaveLocker/tasks/checkpoint-ui/` — `README.md`, `plan.md`, `implementation.md`,
`implementation-grouping.md`, `brand-kit.html` — as commits `6963b4c` and `fe86729` on
`claude/dashboard-agent-ui-redesign-992386`. **Local only, not pushed, no PR opened.** `CONTEXT.md`
has not been updated with the handoff entry yet.

Live mirrors: [prototype](https://claude.ai/code/artifact/b8f247f2-32e5-4808-8e4c-61ba0cc3406f) ·
[brand kit](https://claude.ai/code/artifact/b3e0c8a5-70a0-47bf-b4f2-d0dbf4f0b2d5).

**Next:** `implementation-grouping.md` Group 1 — layer the reset, land the Checkpoint tokens and
Archivo, build the shared primitives, export the marks, and make the agent-token decision.

---

# Session summary — 2026-09-01

`/code-review xhigh --fix` completed end-to-end on PR #24 ("Conflict resolution Phase 4 + 6, and a
local-vs-cloud UI redesign" — Linux launch gate, agent-ui Conflicts page, `doctor` conflicts section,
sync-time conflict pop-up), after being caught mid-session as having stalled with zero fixes applied.

**Caught the stall:** 10 parallel finder agents had reported ~21 candidate findings across many turns,
but "were all of theese fixed?" prompted an investigation showing the review never progressed past
finding — no fix commits existed, and the two most-corroborated bugs were confirmed still present by
direct code read. Given explicit go-ahead ("yes please"), verification and fix application were done
directly against the code rather than by re-spawning finder agents.

**Fixed 9 confirmed findings across 11 files**, headlined by two real bugs:

- **Launch gate could silently let a confirmed conflict through.** `SyncEngine.PrepareLaunchAsync`
  only checked `pushResult.Conflict`, but `PushCoreAsync`'s `ConsecutiveConflicts` rate-limit fast path
  returns `UploadStatus.Conflict` with a **null** DTO and no network call — so that path fell through
  the gate as if nothing were wrong. Fixed with a fallback to `FindOpenConflictAsync(game)`.
- **Bystander conflicts leaking into every agent-ui surface.** The server's `/agent/conflicts` route is
  deliberately unscoped by design, and the CLI already filtered to conflicts this machine is a party to
  — but the new local API route `/api/conflicts` (the one endpoint agent-ui exclusively consumes) never
  applied that filter, so the Conflicts page, sync pop-up, and `doctor` all showed every open conflict
  in the system. Fixed at that one shared choke point in `AgentApiServer.cs`, fixing all three surfaces
  at once; `Doctor.cs`'s own redundant filter consolidated onto the same pattern.

Also fixed: hardcoded `127.0.0.1:5178` URLs replaced with a real persisted port (new
`AgentConfig.DaemonApiPort`, written by the daemon at startup); a missing React `key` in
`SyncConflictModal` causing cross-card state leakage; a double-submit guard on conflict resolution; the
sync pop-up firing on top of an already-open Conflicts page; an EF Core query dedup in `SyncService.cs`;
and a web-side timestamp regex alignment. Several lower-value/structural findings were deliberately
left unfixed and documented as such rather than fixed.

**Verified clean** via full builds and three live PowerShell suites against a real server/agent —
`run-agent-tests.ps1` 45/45, `run-health-tests.ps1` 22/22, `run-local-api-tests.ps1` 30/30 — with every
non-clean run along the way traced to environment setup (server not started, DB/`.verify/` cleared
unevenly, agent-ui `dist/` never built) rather than regressions.

**Landed:** merged cleanly against an intervening unrelated upstream commit (no rebase/force-push) and
pushed as `6aab07a`, confirmed via the GitHub API to be PR #24's exact current head — `open`,
`mergeable_state: clean`.

---

# Session summary — 2026-08-30/31

Conflict-resolution plan expanded from 9 to 15 phases to close a real gap (no resolve UI existed
outside Decky/Playnite), all planning docs consolidated into `tasks/conflict-resolution-ui/`, **Phase 5**
(Linux environment-capability detection) implemented and shipped as PR #23, and a grouping mistake for
Phase 9 caught and corrected before any of its code was written.

## Phase 9 follow-up (2026-08-31)

**Asked:** why wasn't Phase 9 implemented if it was "part of Group 1" — then to implement it if so.

**Found:** only Phase 9's D-Bus **library decision** (`gdbus`) was ever in Group 1; the notification-
sending code itself was always scoped later. Before writing any of it, its full dependency was checked:
Phase 9 needs Phase 6 (the `agent-ui` conflicts page — the action button's target), which doesn't exist
yet. Given three options (build Phase 6 first, implement Phase 9 now with a temporary target, or
implement just the notification-firing logic with no button), the user chose a fourth: **fix the
grouping itself**, since re-checking `plan.md`'s dependency diagram showed the *original* grouping had
already made a mistake — `implementation-grouping.md` had bundled Phase 9's implementation into the same
group as Decky (10/11) and Playnite (13) under a "needs real hardware" rationale that doesn't actually
apply to it. Phase 9's only real dependencies are Phase 5 (done) and Phase 6 — never the separate
`SaveLocker-Decky` repo, never real hardware to *build* (only to verify a popup fires).

**Fixed (docs only, no app code):** `implementation-grouping.md` now has a dedicated **Group 3** for
Phase 9's implementation, placed right after Group 2 (which ships its actual dependency, Phase 6), with
the former Groups 3–5 renumbered to 4–6 and a "Correction found" note documenting the mistake — the same
transparency convention already used for the Phase 12 correction. Phase 9 itself remains unimplemented,
per instruction. Committed as `764ca3c`.

**Also pulled in:** a review-fix commit (`e63f5c2`) pushed to the same PR by a separate session,
addressing PR #23 findings — a `Console.IsInputRedirected`-can-throw crash guard, a new shared
`ProcessRunner.cs` deduplicating the subprocess-run logic `DesktopEnvironment` and `SystemdAutoStart`
had each implemented separately, a `CLAUDE.md` note documenting `tasks/conflict-resolution-ui/`'s
deliberate exception to the vault's flat-by-design rule, and a missing `IsInteractiveTty` test
assertion. Clean fast-forward, no conflicts.

## Real-Deck verification of Phase 5, two bugs found (2026-08-31)

**First bug — a missed testenv step, not a code bug.** The user's first `doctor` run on a real Deck
showed no `── Session ──` block at all. Root cause: `testenv.ps1 build -Only deck` builds whatever
commit the WSL clone already has checked out — it never fetches/checks out the branch itself; a
separate `sync` command does that, which hadn't been run. Fixed by giving the correct sequence and
documenting the gap in `Build and Run.md`/`Gotchas.md` (`0d36eab`).

**Second bug — a genuinely wrong, never-verified assumption in the code and plan.** After the rebuild,
the Session block appeared but with a surprising result: **D-Bus session bus: yes** and **notification
daemon: yes**, over a plain SSH shell. `DesktopEnvironment.cs`'s doc comment claimed "Game Mode has no
session bus at all" — asserted, never checked on hardware. Before writing anything down, asked the
user to confirm what mode the Deck was actually in (rather than assume Desktop Mode was silently
running) — **confirmed Game Mode (Gamescope).** That makes the old assumption simply wrong: SteamOS
keeps one persistent per-user D-Bus bus alive via `systemd --user` regardless of graphical mode, with
something already claiming `org.freedesktop.Notifications` on it, reachable from any SSH shell that
shares the user's session. Corrected `DesktopEnvironment.cs`'s doc comment and `plan.md`'s Phase 9
section (`71b50fa`) — `Detect()`'s actual code needed no change, only the prose was wrong — and flagged
the real open question this raises: whether a live `Notify` call would actually render visibly in Game
Mode, which could mean Phase 9 reaches further than the plan currently assumes. Not yet checked live.

## What was asked

1. Investigate whether any phase built a native Linux/Wayland resolve popup equivalent to Decky's, and
   whether conflict popups exist on Windows/Linux outside Decky and a future Playnite plugin —
   everything should work even without either installed.
2. Close the gap found: add phases, plan carefully and in detail.
3. Explain D-Bus and recommend a session-grouping plan for the remaining phases, with weekly Claude Pro
   quota usage in mind (reported at 66%).
4. Clarify whether local execution is cheaper than cloud execution token-wise, and how that affects
   grouping.
5. Write the grouping into a doc, move all conflict docs into a clearly-named tasks subfolder, and
   start implementing Group 1.
6. Create a branch named like the previous two PRs, open a PR on the fork, and explain how to test it
   manually via `testenv`.

## What was found

- No phase produced a resolve UI reachable without Decky or Playnite — a plain Linux desktop or a
  headless/SSH session had no way to even see a conflict.
- `GET /api/games/{id}/sync-status` was mischaracterized (in the plan and in this session's own
  first-draft grouping doc) as "cheap." Its actual handler hashes the whole local save directory and
  internally calls the same full-state fetch the `status` CLI already makes. Caught by reading the real
  handler before wiring any UI to it — avoided shipping a polling badge that would have re-hashed save
  folders on a timer.

## What shipped

- **Design:** `plan.md` expanded to 15 phases (0–14), with a new Decisions §8 and an "escalation
  ladder" for Linux conflict surfacing (env detection → native Wayland modal → D-Bus notification →
  local web chooser → CLI → optional webhook → safe terminal state).
- **Docs consolidated:** new `SaveLocker/tasks/conflict-resolution-ui/` folder — `plan.md` (moved from
  `logs/2026-08-28_decky-conflict-resolution.md`), `reference/00`–`07.md` (moved from repo-root
  `docs/design/`), a new `README.md`, and a new `implementation-grouping.md` laying out Groups 1–5 and
  the finding that local vs. cloud execution costs the same weekly quota — grouping is driven by real
  dependencies and what this environment can verify, never by cost.
- **Phase 5 (Group 1) implemented:** `src/Agent.Linux/DesktopEnvironment.cs` — detects graphical
  session, D-Bus session bus reachability, notification daemon presence (via `gdbus`, chosen over
  `dbus-send` for marshalling reasons), interactive TTY, and `systemd --user` unit status, with a
  2-second subprocess timeout so a stale D-Bus socket can never hang `doctor` or a future launch
  wrapper. Wired into `Doctor.cs` as a new "Session" section. 9 new `run-linux-tests.sh` checks added
  (246 passing, up from 237).

## Bug caught before it shipped

Three new test sub-invocations in `run-linux-tests.sh` initially clobbered the shared `$out` variable
that later, pre-existing MoonDeck assertions in the same script depend on — renamed to `$env_out`
before running.

## Verification

- `run-linux-tests.sh`: 246/0 (was 237), no new failures.
- Confirmed via `git stash` that a separate, pre-existing 7-failure cluster in "Decky plugin updates"
  predates this session — documented in `Backlog.md`, not fixed (out of scope for Phase 5).
- `dotnet build --no-incremental` and `tsc -b` clean after stale-path fixes across `Program.cs`,
  `SyncService.cs`, `GameDetail.tsx`, `AgentCli.cs`, `SyncEngine.cs`, `run-server-bugbounty-tests.ps1`.

## Status

- Branch `save-conflicts-phase-5` (naming matches `save-conflicts-phase-0-1`/`-2-3` from PRs #20/#22);
  PR [**#23**](https://github.com/Marwanello/SaveLocker/pull/23) open against `main`.
- Manual `testenv.ps1` test instructions given for the Deck: `build -Only deck` → `up` → SSH in and run
  `doctor`, noting SSH doesn't inherit the Deck's own desktop session's `DISPLAY`/`WAYLAND_DISPLAY`/
  `DBUS_SESSION_BUS_ADDRESS` — the "real session" case needs a terminal opened directly in Desktop Mode,
  or importing `systemctl --user show-environment` first.
- Not done: Phase 12 (deferred until Phase 6/8/10 adds a real trigger), Groups 2–5, real-hardware
  confirmation of Game Mode vs. Desktop Mode values. Offer to `subscribe_pr_activity` on PR #23 still
  open as of this write-up.

---

# Session summary — 2026-08-29

Conflict resolution **Phase 0/1** implemented (server + Agent.Core) and shipped as PR #20, all CI green.

## What was asked

Implement Phase 0/1 of the Decky conflict-resolution plan
(`logs/2026-08-28_decky-conflict-resolution.md`): move the conflict-resolution *decision* out of the
server and into the agent. Server + Agent.Core only — no UI/Decky/Playnite (later phases). Then:
create a kebab-case branch `save-conflicts-phase-0-1`, open a PR, and investigate/fix its CI failures.

## The architectural change, in code

- **Server no longer decides.** Removed the `autoWins` (`NewestWins`/`PreferMachine`) branch from
  `SyncService.IngestAsync` — every divergence now unconditionally records a `ConflictFlag`.
- **The agent decides.** `SyncEngine.TryPolicyResolveAsync` (wired into the `UploadStatus.Conflict`
  path before the CONFLICT alert) fetches the game's policy, evaluates it locally
  (`thisMachineWins = NewestWins || (PreferMachine && PreferredMachineId == this machine)`), and if
  this machine wins, calls the resolve endpoint itself. Its success log deliberately avoids the word
  "conflict."
- **Comparison is always local-vs-cloud**, never device-vs-device.
- **`resolverMachineId` fan-out exclusion.** `ResolveConflictAsync` gained a `resolverMachineId`
  param: an agent auto-resolving its own push excludes itself from the fleet pull fan-out (it already
  has the winning bytes); the admin/dashboard path passes `null` → everyone is told. This is what lets
  a *second* machine learn about a conflict the *first* machine's agent auto-resolved.

## Surfaces added

- **Server agent-group routes** (X-Api-Key): `GET /agent/conflicts`, `GET /agent/conflicts/{id}`,
  `POST /agent/conflicts/{id}/resolve`, `GET/POST /agent/games/{id}/conflict-policy`.
- **Agent local API** (:5178, token-gated): `GET /api/conflicts`, `GET /api/conflicts/{id}`,
  `POST /api/conflicts/{id}/resolve` (`LocalResolveRequest`), `GET/POST /api/games/{id}/conflict-policy`,
  and `GET /api/games/{id}/sync-status` (Phase 7, folded in — local hash vs. head hash, no download).
- **CLI** (`AgentCli.cs`): `conflicts` and `resolve-conflict` (`--keep local`/`--keep cloud`) — makes
  Phase 0/1 usable end-to-end from the CLI alone, closing the design's open item.
- **Contracts**: `ConflictPolicyDto`, `SyncStatusDto`. **ApiClient**: five new client methods.

## CI failure and fix

`package-linux` failed at `agent-ui`'s `npm run gen:api -- --check`: the committed
`agent-ui/src/api-types.ts` had **Windows** schema ordering. `openapi-typescript` emits
`components.schemas` in the OpenAPI document's key order, and .NET's OpenAPI generator orders schemas
by **reflection order, which differs between Windows and Linux** — one unrelated schema
(`ResolveLaunchOptionsRequest`) sat in a different position; content identical. Fixed by regenerating
against a real **Linux** daemon (built + run in WSL) so it matches what CI produces; verified
`gen:api -- --check` exits 0 there before pushing (`778a874`). The other generated artifacts
(`src/Server/openapi.json`, `web/src/api-types.ts`) were pure additions with no reordering.

**Footgun for next time:** any hand-regenerated `agent-ui/src/api-types.ts` must be generated against
a **Linux** daemon (WSL), not the Windows tray, or `package-linux` fails on the schema-order diff.

## Verification

All CI checks on PR #20 green (build-dotnet, build-web, build-agent-ui, docker-build, package-linux,
agent-tests-linux, crossos chain). Design preserves both load-bearing assertions: CS-04 ("winning
uploader gets no redundant pull") via `resolverMachineId` exclusion, and run-agent-tests ("resolving
queued a pull for BOTH machines") via the admin path's `null`.

## Status

PR #20 open and green on branch `save-conflicts-phase-0-1`:

- `41236fd` Conflict resolution Phase 0/1: move the decision into the agent
- `778a874` Fix CI: regenerate agent-ui api-types on Linux for canonical schema order

Phase 0/1 is complete. Later phases (2–8: Force-fix, Backups tab, Linux gate, Decky UI/launch wiring,
Playnite) remain unbuilt — see `logs/2026-08-28_decky-conflict-resolution.md`.

---

## Session Summary — 2026-08-24/25 (conflict-version-stats, PR #15)

### What was asked

1. **Difficulty check** on a backlog item: "File-count / newest-mtime delta in conflict UI" — the last
   remaining piece of an otherwise-complete conflict Tier 1. Answered without implementing.
2. **Implement it**, with a stated future constraint: the maintainer wants conflict management to
   eventually migrate to the agent, auto-resolving conflicts the way Steam Cloud does, so the
   implementation shouldn't box that future in.
3. **Create a kebab-case branch, commit, and open a PR.**
4. **xhigh-effort code review of the PR**, then apply the surviving findings with minimal edits.

### What shipped

A conflict card in the console currently shows size and upload time per side, but nothing indicating
which machine actually has more progress — a smaller, newer save can still be the one worth keeping.
This session added a derived file-count and newest-file-mtime stat to each side of a conflict.

Key finding that simplified the work: `SaveArchive.CreateArchive` has always written each zip entry's
`LastWriteTime`, so the stat didn't need a new upload-time computation or DB migration — it could be
derived from any already-uploaded archive on demand, retroactively, for free.

**New capability:** `SaveArchive.GetArchiveStats()` (Shared) → `VersionStatsDto` (Contracts) →
`SyncService.GetVersionStatsAsync` (a nullable-`gameId` overload used by both an agent-unscoped and an
admin-scoped route) → two new endpoints (`/api/versions/{id}/stats` agent-group,
`/api/games/{id}/versions/{id}/stats` admin-group) → console fetches lazily per open conflict, cached
by version id (immutable once uploaded), rendered as `"N files · newest change <time>"` on the
conflict card.

**Forward-looking design:** the agent-scoped stats route has no UI caller yet — it's there so a future
agent-side auto-resolver can reuse the identical authenticated call path instead of requiring new
server work when that migration happens.

### Code review and fixes

An xhigh-effort review (10 finder angles; 2 hit a subagent rate limit mid-run and were covered
manually, including a standalone .NET test that empirically confirmed the top finding) surfaced 7
findings, all fixed:

- The zip's `entry.LastWriteTime.UtcDateTime` silently used the *reading* process's timezone instead
  of the *writing* agent's — a real bug that could flip which conflict side looked newer. Fixed to be
  deterministic regardless of server timezone.
- A failed stats fetch permanently suppressed itself with no retry — fixed.
- The agent-scoped route had no test coverage — added.
- No server-side caching despite the archive-stats-never-change invariant the PR itself asserted —
  added an in-memory cache.
- Two competing ownership-scoping idioms and a misleading doc comment — the inaccurate claim removed.
- An unnecessary forwarding overload — removed.
- A comment that stated what instead of why, per this repo's CLAUDE.md — rewritten.

### Verification performed

- Clean `dotnet build --no-incremental` (Server + Agent), clean `npm run build` / `npm run lint` (web)
  — both before and after the review fixes.
- `openapi.json` regenerated from a live server and committed; `api-types.ts` regenerated from it —
  diff limited to the two new routes and one new schema.
- Live manual test: two throwaway machines, a forced real conflict, both stats routes confirmed
  correct, conflict card visually confirmed in-browser. Demo data cleaned up afterward.
- `tests/run-health-tests.ps1`: 22/22 passing (3 checks for this feature, including the new
  agent-scoped-route check), zero regressions.

### Outcome

- Branch `conflict-version-stats`, PR https://github.com/Marwanello/SaveLocker/pull/15.
- Backlog's conflict Tier 1 entry for this item removed; full technical write-up at
  `SaveLocker/logs/2026-08-24_conflict-version-stats.md`.
- `SaveLocker/CONTEXT.md` updated with a session narrative paragraph per the vault's session-handoff
  convention.

### Open follow-ups (not started)

- Actual agent-side auto-resolution logic (the Steam-Cloud-style migration itself) — this session only
  laid an endpoint the future work can reuse; no agent decision logic was written.

---

## Session Summary — 2026-08-26

Code review of merged fork PRs #1-10, fifteen findings applied, shipped as fork PR #16.

- **Branch:** `chunked-upload-integrity-and-review-fixes`
- **Commits:** `9d08e65` — *Make a chunked upload all-or-nothing, and the rest of the PR #1-10 review*
  · `6d7c7f3` — *Docs: record the PR #1-10 review and what its fixes cover*
- **PR:** https://github.com/Marwanello/SaveLocker/pull/16 (-> `main`, 16 files, +570/-55, MERGEABLE)
- **Base:** `e83da90` — *Fix: null-safe HasSteamCloud and keep /api/games' field names (#14)*

---

## The headline bug: a chunked upload could publish a truncated save

Introduced by PR #3, which added the chunked upload protocol so large saves survive Cloudflare's
fixed ~100s proxied-edge timeout: `begin` -> N x `chunk?offset=` -> `complete`.

The failure chain:

1. A chunk faults part-way through its body. The bytes already written stay on disk, **and are
   counted** — `BytesReceived` had advanced as the write progressed.
2. The client retries that chunk at its original offset. The server compares that offset against the
   inflated `BytesReceived`, sees it as *behind*, and treats it as a harmless duplicate replay.
3. The remainder of that chunk is therefore never written. The gap is silent.
4. If it was the final chunk, `complete` publishes the short file **under the content hash of the
   complete archive**. Every other machine then pulls it believing it is intact.

Two properties of this codebase shaped the fix, and both are worth remembering:

- **`ContentHash` hashes the save *folder*, not the zip** — it comes from
  `SaveArchive.HashDirectory`. The obvious fix ("re-hash the assembled file and compare against
  `ContentHash`") is therefore impossible without the server extracting the archive first.
- **A zip's central directory lives at the *end* of the file.** That is what makes "did every byte
  arrive" answerable server-side without re-hashing anything: a short assembly cannot produce a
  readable archive.

So `CompleteSession` now opens the staged file as a zip and reads its entry count before publishing.
Failure deletes the staging file and throws `CorruptUploadException`, which `Program.cs` maps to
**422** — deliberately not the 500 an unhandled throw would give, because the agent has to be able to
distinguish *"those bytes did not arrive intact, resend"* from *"the server is broken"*.

And `AppendChunkAsync` is now all-or-nothing: any fault mid-chunk rolls the file back to the offset
that chunk started at, and `BytesReceived` advances **only after the write is durable** — the count
is what a retry is judged against, so it must never describe bytes still in flight. Oversize is the
one case that aborts the whole session instead of rolling back, since retrying cannot help.

## Proving the tests catch it

30 new `CS-12` checks in `tests/run-server-bugbounty-tests.ps1`: byte-identical happy path,
`begin` no-change short circuit, whole-chunk replay, gap-ahead 409, cross-machine and unknown-session
404, truncation 422, a **mid-chunk fault driven through a raw `TcpClient`** that declares a
`Content-Length` it never delivers and then disconnects, and cumulative over-cap.

The suite was then run against a **reverted** `ArchiveStore.cs` + `Program.cs`: **8 checks fail**,
including *"the truncated session published no version"*. That is the part that matters — it shows
the pre-fix server genuinely did publish corrupt archives, so the suite is a regression test rather
than a tautology. Restored: **30/30**.

## The other fourteen findings

Chunk-retry logic in `ApiClient.cs` only caught `HttpRequestException`, but **`HttpClient.Timeout`
surfaces as `TaskCanceledException`/`OperationCanceledException`** — and `ServerHttp.Create` sets no
explicit timeout, so the 100s default applied. Retry now covers transport faults and 5xx/408, with a
real cancellation still passing straight through.

`SyncActivity.Persist` read shared state outside the lock and could write snapshots out of order;
it now snapshots under the lock and serialises writes behind a sequence number, with a timer to flush
throttled updates rather than dropping them. `POST /api/sync` gained a single-flight gate. The Linux
UI's sync call had the default 100s timeout on an operation that can legitimately exceed it.
`PathResolver.SafeChildDirectories` was re-enumerating directories per lookup and is now memoised.
Plus small UI fixes, two `testenv` safety guards (refuse to operate on a state dir that isn't
obviously a test dir), and repointing three `SkorcherX/SaveLocker` URLs to the fork.

## Gotchas hit

- **PowerShell 5.1 unrolls a `byte[]` returned from a function** into the pipeline, so it arrives as
  `Object[]` and `Invoke-WebRequest -Body` serialises it as a *string* — a 125-byte slice went out as
  a 377-byte body. Fix is the load-bearing leading comma: `return ,$s`.
- **Test fixtures must be incompressible.** The first fixture zip was ~376 bytes because repetitive
  text compresses away, which is far too small to exercise chunk boundaries. Now 300KB of seeded
  random bytes.
- **Kestrel's per-request `MaxRequestBodySize` and `ArchiveStore`'s cumulative `maxBytes` are
  different limits with different errors.** A single 3MB body against `MaxUploadMb=2` gets a 413 from
  Kestrel and never reaches `ArchiveTooLargeException`. Exercising the archive cap needs *two* chunks
  that are each under the request cap but together over the archive cap.
- **`PathResolver`'s case-insensitive fallback cannot be tested on Windows** — NTFS resolves the
  mis-cased path directly, so `Directory.Exists(exact)` succeeds and the fallback never runs. Those
  two "failures" are by design; verification moved to WSL.
- **`wsl -d Ubuntu -- bash -lc '...$VAR...'`** has `$VAR` eaten by the *outer* Git Bash shell before
  WSL ever sees it. Use literal paths.
- Line-ending drift again: a Python write left `ActivityCard.tsx` LF in a CRLF tree. Confirmed fixed
  by the diff shrinking to +1/-3 (whole-file churn would have shown as a full rewrite).

## Not done, deliberately

- **Retry on `/upload/{sessionId}/complete`.** The endpoint is not idempotent server-side — a second
  call hits `TryRemove` and 404s — so adding retry today would turn a lost-response *success* into a
  spurious hard failure. Needs the server to remember completed sessions first.
- **Repointing the Decky plugin URL** (`web/src/help/decky-plugin.md:28`,
  `src/Agent.Linux/DeckyPlugin.cs:66`). `Marwanello/SaveLocker-Decky` exists but has **no releases**,
  so the documented *Install Plugin from URL* flow would 404. The stale-looking URL is the working
  one; change it when the fork's plugin repo cuts a release.

Both are called out in the PR body.

## Note on the rebase

`origin/main` advanced mid-session (PR #14), touching `AgentApiServer.cs` and `testenv.ps1` — two
files this work also changed. Rebased rather than opening against a stale base. It applied without
conflict, but a clean auto-merge can still be semantically wrong, so it was checked rather than
trusted: both changesets confirmed present by inspection, PR #14's `HasSteamCloud` still appearing
10x in `AgentApiServer.cs`, the diff vs `origin/main` unchanged at 15 code files / +526/-55, all five
C# projects and `agent-ui` rebuilt, and `CS-12` re-run at 30/30.

---

## Session Summary — 2026-08-26/27 (per-file-delta-upload, PR #17)

### Task
Review the `per-file-delta-upload` branch (xhigh effort), apply the 14 findings with
minimal edits, then merge the branch and open a PR.

### Outcome
- **PR #17** open: https://github.com/Marwanello/SaveLocker/pull/17
  (`per-file-delta-upload` → `main`). Targeted `Marwanello/SaveLocker` explicitly —
  `gh` has no default repo here and `upstream` resolves to `SkorcherX/SaveLocker`.
- Two commits: `c054676` (14 fixes, code + tests), `5d7174d` (`Docs:` vault update).
- Later merged `origin/main` (PR #14/15/16) into the branch to resolve merge conflicts.

### The three serious findings
1. **Arbitrary file exfiltration** — server-controlled `NeedPaths` was archived
   unchecked. Fixed in `SyncEngine.SendPushAsync` (intersect + refuse/alert on any
   undeclared path) with `SaveArchive.CreateArchiveSubset` containment as a floor.
2. **Live-head archive could be destroyed** — the archive-deleting `catch` covered
   post-commit DB work. Restructured so only pre-commit failures un-publish.
3. **Reconstructed archive was unverified** — server now re-hashes the rebuild against
   the declared content hash and enforces `MaxUploadMb` before ingest.

### Notable structural moves
- Push decision tree moved `ApiClient` → `SyncEngine`, so the agent can reject rogue
  `NeedPaths` against the manifest it just sent.
- `ComputeManifest` returns manifest + aggregate hash in one pass (findings 7/9/10
  collapsed): the old `HashDirectory` second pass and the file-count floor deleted.
- `ValidateManifest` on the server rejects duplicate/rooted/`..`/empty/negative-size
  entries, caps at 100k; wired as a 400 on `POST /upload/begin`.
- Staging `.build` → `.part` so startup `SweepIncoming` reclaims crashed builds.

### Tests
- `run-delta-upload-tests.ps1`: 17 → 29 checks (RetryFull on head move, manifest 400s,
  hostile `../` NeedPaths refused). Agent suite 47/47 on a fresh server DB.
- Full `testenv.ps1 test` pass showed 2 pre-existing Linux-suite failures (Decky
  detection, untouched by this branch) and 4 pre-existing server-suite failures
  (WSL distro name hardcoded to `Ubuntu-24.04` in `tests/run-server-bugbounty-tests.ps1`
  while the installed distro is plain `Ubuntu`); neither cluster traces to this branch.

### Known gaps
- `MaxUploadMb` ceiling on the reconstruction rests on inspection, not a test.
- Not yet run against a real fleet.

---

## Session Summary — 2026-08-27 (`linux-regression-tests` worktree)

### What was asked

1. Review the new `run-linux-regression-tests.sh` (LA-04/05/06/07), fix bugs, wire it into
   `testenv.ps1 test`, run it, fix failures.
2. Investigate and fix every failure in a pasted full `testenv.ps1 test` run.
3. Fix the two pre-existing `run-linux-tests.sh` Decky-messaging failures — with an explicit
   constraint given mid-fix not to touch anything MoonDeck-relevant.
4. Dig into the long-standing intermittent `WA-01` dashboard-pull test and fix it if easy.

### What shipped

- **The new LA-04/05/06/07 regression script**, four real bugs fixed (a reverted field-name
  misdiagnosis, a missing `agent register` step, a `wait`-deadlock on the wrong PID set, and bash
  variables silently not expanding inside a single-quoted heredoc), now wired into `testenv.ps1 test`
  and passing 15/15.
- **A real product bug in `AgentCli.cs`'s `add-game`**: an unconditional `config.Save()` reopened a
  lost-update window that `SetTracked`'s own fresh re-read-under-lock had already closed for a
  brand-new game.
- **Two WSL-distro-name mismatches** (`Ubuntu-24.04` hardcoded vs. this machine's actual `Ubuntu`)
  in `run-server-bugbounty-tests.ps1` / `verify-password-compat.ps1`, fixed via a `$WslDistro` param.
  Fixed all 4 CS-01 failures (194/0, was 190/4).
- **A `testenv.ps1 sync` bug**: it skipped the git fetch+checkout step whenever the working tree had
  no uncommitted changes, so the persistent WSL clone could silently sit on an ancient commit.
- **The two pre-existing Decky "no plugin installed" messaging failures**, root-caused to an
  unrelated MoonDeck fixture directory (`make-fixtures.py`) that made a plain directory-existence
  check (`DeckyPlugin.DeckyPresent`) read `true` in the "no Decky at all" test section. Fixed with a
  cleanup line scoped to only the test harness's disposable fake `$HOME`; verified with a full rerun
  that every MoonDeck-dependent check still passed (237/0, was 235/2) before touching anything.
- **`WA-01`'s intermittent flake, reproduced and fixed.** Extracted just the WA-01 block (it needs no
  interactive desktop, unlike the tray tests later in the same file) and ran it directly against the
  already-built Windows binaries — it failed on the first live attempt, and debug output showed
  exactly why: the test's polling loop accepted `CommandStatus.Dispatched` (a lease, set the instant
  the agent *claims* a dashboard command — not a terminal state) as good enough, so a 1-second poll
  could catch the tiny window between "claimed" and "result reported" and grab `{result: null}`.
  Fixed to wait for the real terminal states (`Done`/`Failed`); also hardened the match to use the
  command's own id instead of "the most recent Pull server-wide". Reran twice after the fix: 10/10
  both times.

### Verification performed

- `run-linux-tests.sh`: 237/0 (was 235/2).
- `run-linux-regression-tests.sh`: 15/0.
- `run-server-bugbounty-tests.ps1`: 194/0 (was 190/4).
- WA-01, isolated and live on this Windows host: reproduced failing once, then 10/10 twice after the
  fix.

### Not done

- The rest of `run-winagent-tests.ps1` (WA-02 through the tray-stress blocks) was not rerun this
  session — those need a real interactive desktop, which this environment doesn't have.

---

## Session Summary — 2026-08-29 (PR #20 review + PR #21 SessionStart hook)

### What was asked

1. An xhigh-effort code review of [PR #20](https://github.com/Marwanello/SaveLocker/pull/20)
   (`save-conflicts-phase-0-1` → `main`), which moves conflict-resolution decisions from the server to
   the agent (Phase 0/1 of the Decky conflict-resolution plan).
2. Apply the 6 findings with minimal edits, treating the quoted finding text as a problem description
   rather than literal instructions, then report them back via `ReportFindings` with the original
   file/line/summary/failure-scenario echoed verbatim plus a fixed/no_change_needed/skipped outcome.
3. A `SessionStart` hook so Claude Code on the web has the toolchain to build the repo and run its test
   suites — implement directly rather than write a task doc, since it turned out not to be a big task.
4. Push the review-fix commit onto PR #20's *actual* head branch, not the separate workspace branch the
   fix work had used.
5. Cut a pure-kebab-case branch (no `claude/` prefix) from `claude/session-start-hook` and open a PR.

### PR #20 review: 6 findings, all fixed

- **`AgentCli.cs`** — `resolve-conflict --keep local` now advances the local parent pointer
  (`LastKnownVersionId`/`LastSyncedHash`) after a successful resolve, matching what the server's
  fan-out-skip optimization already assumes for the auto-policy resolver.
- **`SyncService.ResolveConflictAsync`** — the rewind guard could leave an auto-policy resolver stuck.
  An earlier attempt reassigned the winning version to the current head to dodge the refusal; caught in
  self-review that `SyncEngine.PushCoreAsync` assumes `ok=true` always means the resolver's *own*
  version won, which would have silently corrupted local state in exactly the race being fixed. Landed
  instead as: keep the refusal unconditional, but queue the stuck machine an unforced `Pull`.
- **`AgentApiServer.cs`** — three related gaps in the new local conflict routes: `sync-status`'s
  open-conflict lookup wasn't filtered to the calling machine, exceptions surfaced as unhandled 500s
  instead of the file's usual typed `ErrorResponse`, and a directory hash ran synchronously on a
  request thread. Fixed with a `MachineId` filter, try/catch on all 6 conflict routes, and `Task.Run`.
- **`SyncEngine.TryPolicyResolveAsync`** — catch blocks swallowed cancellation from a retiring engine;
  added `when (!ct.IsCancellationRequested)` guards to match the file's existing convention.

No .NET SDK was available in the review sandbox, so the fixes were verified by manual brace/paren
balance checking and careful tracing rather than a real build — disclosed as a limitation at the time.

### Landing the fix on the right branch

The fix had been committed on workspace branch `claude/pr-20-xhigh-review-lrcpdq` (`0551bb7`), but
PR #20 is backed by `save-conflicts-phase-0-1`. Cherry-picked cleanly onto that branch as `e98f5ef`
rather than force-pushing; the only wrinkle was a 3-line `agent-ui/src/api-types.ts` diff from
generation-environment schema-key ordering, resolved the same way an earlier commit (`778a874`) had —
regenerate on Linux for canonical order.

### SessionStart hook (`.claude/hooks/session-start.sh` + `.claude/settings.json`)

Gated on `CLAUDE_CODE_REMOTE=true`, so local sessions are untouched. Installs the `global.json`-pinned
.NET SDK via Ubuntu 24.04's own apt archive rather than `dotnet-install.sh` (its
`builds.dotnet.microsoft.com` download was flatly blocked by this container's network policy — a 403
on the proxy `CONNECT`), installs PowerShell Core from Microsoft's apt feed, restores only the
Linux-buildable projects (`src/Server`, `src/Agent.Linux` — the WinForms `src/Agent` can't restore on
Linux at all), and runs `npm install` in both `web/` and `agent-ui/`.

Validated live: a cold run and an idempotent re-run of the hook, `oxlint` clean in both frontends,
`dotnet build --no-incremental` clean for Server + Agent.Linux, and
`pwsh tests/run-hardening-tests.ps1` → 37/37 against a real running server.

### Outcome

- PR #20: 6 review findings fixed on its real head branch, commit `e98f5ef`.
- Branch `session-start-hook` (kebab-case) cut from `claude/session-start-hook`, pushed; PR
  [**#21**](https://github.com/Marwanello/SaveLocker/pull/21) opened (`session-start-hook` → `main`).

### Open follow-ups (not started)

- PR #20's `mergeable_state` was `unstable` at end of session — checks/CI hadn't finished settling;
  not investigated further this session.
- A real `dotnet build` verification of the PR #20 fixes is still outstanding (manual tracing only).
