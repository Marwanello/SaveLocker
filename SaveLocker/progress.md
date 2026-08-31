Running log of session outcomes, newest entries appended at the bottom.

---

## 2026-08-24/25 — File-count / newest-mtime delta in conflict UI (conflict-version-stats, PR #15)

**Branch:** `conflict-version-stats`. PR: https://github.com/Marwanello/SaveLocker/pull/15.

### Request sequence

1. Asked whether the backlog item "File-count / newest-mtime delta in conflict UI" (the last piece of
   an otherwise-complete conflict Tier 1) was hard to implement — exploratory question, no
   implementation yet.
2. Asked to implement it, with an explicit forward-looking constraint: design it so it doesn't paint
   the codebase into a corner for a future migration where conflict resolution moves from manual
   dashboard clicks to agent-side automatic decisions, the way Steam Cloud auto-resolves save
   conflicts.
3. Asked to create a new branch using kebab-case naming and commit the changes, then open a PR.
4. Requested an xhigh-effort code review of PR #15, then asked for the 7 surviving findings to be
   applied with minimal edits.

### What was found

Reading `SaveArchive.CreateArchive` (`src/Shared/SaveArchive.cs`) showed it has always stamped
`entry.LastWriteTime = File.GetLastWriteTime(full)` on every zip entry, long before this task existed.
That meant the "upload time vs. on-demand" tradeoff the backlog note posed wasn't really a choice:
deriving file count and newest-mtime from the archive on demand costs nothing extra, needs no DB
migration or wire-field addition, and works retroactively on every archive ever uploaded.

### What was built

- `SaveArchive.GetArchiveStats(zipPath)` (Shared) — opens the zip, counts real file entries (skipping
  directory entries), tracks the max `LastWriteTime`. Returns
  `readonly record struct ArchiveStats(int FileCount, DateTime? NewestFileWriteUtc)`.
- `VersionStatsDto(int FileCount, DateTime? NewestFileWriteUtc)` (`src/Shared/Contracts.cs`) — the wire
  shape, kept separate from `SaveVersionDto` so listing versions never has to open a zip for versions
  nobody is looking at.
- `SyncService.GetVersionStatsAsync(Guid? gameId, Guid versionId)` — a nullable-`gameId` overload used
  by both routes: `null` for the agent group's flat route (matching `DownloadVersionAsync`'s own
  precedent — a valid machine key already gates the group), and a real game id for the admin group's
  nested route, which checks the version actually belongs to that game before answering.
- Two new endpoints backed by the same service call: `GET /api/versions/{versionId}/stats` (agent
  group) and `GET /api/games/{id}/versions/{versionId}/stats` (admin group).
- Console (`GameDetail.tsx`): conflict card fetches stats lazily for only the two versions an open
  conflict is showing, cached in a `useRef` `Set` keyed by version id (archive contents never change
  once uploaded, so no cache invalidation is needed). Renders `"N files · newest change <time>"` under
  the existing machine/time/size line; spacing kept stable by moving `marginTop` onto the always-
  rendered button row rather than the conditionally-rendered stats line, so the card doesn't jump once
  the fetch resolves.

**Agent-migration angle:** the agent-scoped route (`/api/versions/{versionId}/stats`) has no UI caller
today — it exists ahead of need. When conflict resolution moves to the agent (Steam-Cloud-style), an
agent deciding between its own local archive and a conflicting server version will want exactly this
comparison, through the same authenticated call the agent already makes for everything else. One
shared `SyncService` method with two thin, appropriately-scoped entry points means future work reuses
this endpoint rather than inventing a second one.

### Code review, xhigh effort — 7 findings, all fixed

An xhigh-effort review of PR #15 (10 finder angles, 2 of which hit a subagent weekly rate limit
mid-run and were covered manually instead, including writing a standalone .NET test to confirm the
most severe finding) surfaced 7 findings, all applied with minimal edits:

1. **Timezone skew in the newest-mtime value (confirmed empirically).** `entry.LastWriteTime.UtcDateTime`
   reconstructs the DOS zip timestamp using the *reading* process's local timezone, not the *writing*
   agent's — a standalone test proved an 8-hour skew when the writer and reader ran in different
   zones. Since the two sides of a real conflict usually come from different physical machines, this
   could flip which save looked "newer." Fixed by treating the stored wall-clock value as UTC directly
   (`DateTime.SpecifyKind(..., DateTimeKind.Utc)`) instead of reinterpreting it through whichever
   timezone the server happens to be deployed in — deterministic now, though still bounded by
   whatever offset the agent's own clock had at upload time (the DOS format can't carry more than
   that).
2. **A failed stats fetch permanently hid a version's stats.** `GameDetail.tsx` marked a version id as
   "requested" before the fetch resolved and never un-marked it on failure. Fixed: the `.catch` now
   clears the id so the next 15s poll retries.
3. **The agent-scoped stats route had no test coverage** — only the admin-scoped route was exercised.
   Added a check that authenticates with a machine's own `ApiKey` (read from its config file) and
   hits `/api/versions/{id}/stats` directly.
4. **No server-side caching**, despite the PR's own write-up asserting archive stats never change once
   uploaded. Added a `ConcurrentDictionary<Guid, VersionStatsDto>` cache on `SyncService`, keyed by
   version id, cleared only by a restart (fine — a pruned version's id is never reused).
5. **Two competing idioms for ownership-scoping** existed side by side (a route-level check for
   download, a service-level check for stats), and the stats overload's doc comment inaccurately
   claimed to match the download route's precedent. Resolved by dropping the misleading claim; the
   nullable-`gameId` overload itself was not restructured.
6. **An unnecessary single-arg forwarding overload** (`GetVersionStatsAsync(Guid) => GetVersionStatsAsync(null, ...)`)
   existed only so the agent route didn't have to pass `null` explicitly. Removed; the agent route now
   calls the two-arg overload directly.
7. **A comment restated what the code does rather than why**, per this repo's CLAUDE.md rule "No
   comments unless the WHY is non-obvious." Rewritten to explain the actual non-obvious reason the
   admin route is separate from the agent one (different auth scheme).

### Verification

- `dotnet build --no-incremental` clean for `SaveLocker.Server` and `SaveLocker.Agent` (the
  Windows/WinForms host — the one pre-existing MSB3277 WindowsBase warning, no new ones).
- `web`: `npm run build` (tsc + vite) and `npm run lint` (oxlint) both clean, both before and after
  the review fixes.
- `src/Server/openapi.json` regenerated from a real dev server's `/openapi/v1.json` and committed;
  `web/src/api-types.ts` regenerated from that snapshot (`npm run gen:api`) — diff is exactly the two
  new routes and the new schema, nothing else moved.
- Manual, live: registered two throwaway machines against a dev server, pushed two divergent
  single-file/two-file archives to force a real conflict, confirmed both `/stats` routes (admin- and
  agent-scoped) return the right `fileCount`, then loaded the console in the browser pane and
  confirmed the conflict card rendered `DemoMachineA — current Latest · 1 file · newest change …` and
  `DemoMachineB · 2 files · newest change …`. Demo game deleted afterward; no dev-state residue.
- `tests/run-health-tests.ps1` — three checks total for this feature (file count, newest-mtime, and
  the added agent-scoped-route check), all passing. 22/22 passing overall, no regressions.

### Not done

PR #15 open against `main`, mergeable after resolving a docs-only merge conflict on this file,
`CONTEXT.md`, and `session_summary.md` against an unrelated PR (#14) that landed on `main` in the
meantime. No task file existed for this — it was an ad hoc backlog pick. Full technical write-up:
`logs/2026-08-24_conflict-version-stats.md`.

---

## 2026-08-26 — PR #12 review, fixes, and PR #14

[PR #14](https://github.com/Marwanello/SaveLocker/pull/14) is open and mergeable.

**Rename:** `decky-api-contract-and-steam-cloud-fallback` -> **`steam-cloud-fallback`** (43 -> 20
chars), still kebab-case and in line with repo names like `wine-proton-case-insensitive-paths` and
`per-file-delta-upload` — a noun phrase naming the headline change.

**PR:** [Marwanello/SaveLocker#14](https://github.com/Marwanello/SaveLocker/pull/14) —
`steam-cloud-fallback` -> `main`, 8 files, +135/-76, MERGEABLE.

Three things worth knowing about how this was done:

- **Targeted the fork, not upstream.** `gh`'s default repo here resolves to `SkorcherX/SaveLocker`,
  so a bare `gh pr create` would have opened a PR on the upstream maintainer's repo. `--repo
  Marwanello/SaveLocker` was passed explicitly. That is also the only correct base: the parent commit
  `01f1c56` is a fork-only merge, and it was verified to be an ancestor of `origin/main` before
  creating the PR. Upstream has a *different* PR #12, so a PR there would have diverged.
- **Amended rather than adding a commit.** The `CONTEXT.md` entry named the old branch, so that one
  line was fixed. Standard guidance prefers a new commit over amending, but nothing was pushed yet
  and a separate commit to correct a stale string would have been noise. The commit is now
  `1ae1538`; nothing else was rewritten.
- **Local `main` is 1 commit behind `origin/main`** — pre-existing, unrelated to this work.
  Fast-forward it when convenient: `git -C "D:\Projects\SaveLocker\SaveLocker" pull --ff-only`

Still outstanding: **no test suite has been run** against these changes, and the Decky plugin repo
needs updating to read `hasSteamCloud: null` as "unknown, use your own heuristic" — the agent can now
legitimately send it. Both are called out in the PR body.

The empty worktree directory at `.claude\worktrees\review-pr-12-b073b2` could not be deleted from
that session (the shell's cwd sat in it); `rmdir /s /q` clears it once the session ends.

---

## 2026-08-26 — PR #1-10 review, 15 fixes, and PR #16

[PR #16](https://github.com/Marwanello/SaveLocker/pull/16) is open and mergeable —
`chunked-upload-integrity-and-review-fixes` -> `main`, 16 files, +570/-55.

An `xhigh` recall-oriented review of merged PRs #1-10 produced 15 findings; all 15 were triaged and
applied as minimal edits across two commits:

- `9d08e65` — the fifteen fixes (15 files, +526/-55)
- `6d7c7f3` — `Docs:` vault entry in `CONTEXT.md`, per the session-handoff convention

**The headline finding is a save-data-loss bug in the chunked upload protocol** (added in PR #3).
A chunk that faulted mid-body left its partial bytes on disk *and counted them*, so the client's
retry at the original offset read as "behind" and was dismissed as a harmless replay — the rest of
that chunk was never written. On the final chunk this published a truncated zip under the *full*
archive's content hash, which every other machine then pulled believing it was intact.
`AppendChunkAsync` is now all-or-nothing (rollback to the chunk's start offset on any fault, byte
count advanced only once the write is durable), and `CompleteSession` refuses to publish an archive
whose central directory will not open, returning 422 rather than an unhandled 500.

Three things worth knowing about how this was done:

- **`origin/main` moved mid-task.** PR #14 landed and touched two files this work also changed
  (`AgentApiServer.cs`, `testenv.ps1`). The branch was rebased onto it rather than opening against a
  stale base. Git merged cleanly, but a clean auto-merge can still be semantically wrong, so
  everything was re-verified afterwards rather than trusted — see the verification numbers below.
- **The tests were proven to catch the bug, not just to pass.** With `ArchiveStore.cs` and
  `Program.cs` reverted to HEAD, the new `CS-12` suite fails 8 checks — including *"the truncated
  session published no version"*, which demonstrates the pre-fix server really did publish corrupt
  archives. Restored, it is 30/30.
- **Two findings were deliberately left undone,** both recorded in the PR body and `CONTEXT.md`.

**Verification:** `CS-12` 30/30 (8 failures pre-fix); `run-linux-tests` 235/237 baseline; all five C#
projects and `agent-ui` build green, re-run after the rebase.

Still outstanding, by choice:

- **Retry on `/upload/{sessionId}/complete`** — the endpoint is not idempotent server-side (a second
  call hits `TryRemove` and 404s), so adding retry today would convert a lost-response *success* into
  a spurious hard failure. Needs the server to remember completed sessions first.
- **Repointing the Decky plugin URL** (`web/src/help/decky-plugin.md:28`,
  `src/Agent.Linux/DeckyPlugin.cs:66`) from `SkorcherX/SaveLocker-Decky` to
  `Marwanello/SaveLocker-Decky` — the fork's plugin repo exists but has **no releases**, so the
  documented *Install Plugin from URL* flow would 404. The stale-looking URL is the working one.

---

## 2026-08-26/27 — Per-file delta upload review + PR #17

Reviewed the `per-file-delta-upload` branch at xhigh effort, applied 14 findings
with minimal edits, then merged and opened a PR.

**PR:** https://github.com/Marwanello/SaveLocker/pull/17 (`per-file-delta-upload` → `main`).
Targeted `Marwanello/SaveLocker` explicitly because `gh` has no default repo set and
`upstream` resolves to `SkorcherX/SaveLocker`.

**Commits on top of 41b90c2:**
- `c054676` Fix: 14 review findings on per-file delta upload (src/ + tests/)
- `5d7174d` Docs: record the delta-upload review, fold two stray vault files into logs/

### The three serious findings
1. **Arbitrary file exfiltration (highest severity).** Server-controlled `NeedPaths`
   fed unchecked into local archiving. Fixed at two layers: `SyncEngine.SendPushAsync`
   intersects requested paths against the manifest it just declared and refuses +
   alerts on any undeclared path; `SaveArchive.CreateArchiveSubset` adds path
   containment as a floor.
2. **Live-head archive could be destroyed.** The archive-deleting `catch` in
   `CompleteChunkedUploadAsync` was widened to cover post-commit DB work.
   Restructured so only pre-commit failures un-publish; post-commit manifest/audit
   failures clear the change-tracker and best-effort audit instead.
3. **Reconstructed archive was unverified.** Server now re-hashes the rebuilt archive
   against the agent's declared content hash and enforces the `MaxUploadMb` ceiling
   before ingest.

### Structural changes
- Push decision tree moved from `ApiClient` into `SyncEngine.SendPushAsync`, so the
  agent holds the manifest it sent and can reject rogue `NeedPaths`.
- `SaveArchive.ComputeManifest` returns manifest + aggregate hash in one pass,
  making `HashDirectory`'s second pass redundant and the file-count floor deletable
  (findings 7/9/10 collapsed into one change).
- Server `ValidateManifest`: rejects duplicate/rooted/`..`/empty/negative-size entries,
  caps at 100k entries; wired into `POST /upload/begin` as a 400.
- Staging files renamed `.build` → `.part` so startup `SweepIncoming` reclaims crashes.

### Tests
- `tests/run-delta-upload-tests.ps1`: 17 → 29 checks. Added section 7 (head moves
  between Begin/Complete → RetryFull, asserted via `upload.conflict` audit delta),
  section 8 (manifest validation 400s), section 9 (hostile `../` NeedPaths refused,
  no chunk sent). Full agent suite 47/47 against a fresh server DB.

### Follow-ups / gaps
- `MaxUploadMb` ceiling on the reconstructed archive rests on inspection, not a test.
- None of this has run against a real fleet.

### Merge to open PR #17
- Local `main` was 3 commits behind `origin/main` (PR #14, #15, #16). Merged
  `origin/main` into `per-file-delta-upload` after opening the PR when GitHub reported
  conflicts. Conflicts were mechanical: both branches added imports to
  `SyncService.cs` (all three retained), and both added an enum member (`RetryFull`)
  and a DTO (`VersionStatsDto`) to `web/src/api-types.ts` (both retained).
  Server build clean (0 warnings, 0 errors), agent-ui typecheck clean.

---

## 2026-08-27 — LA-04/05/06/07 regression suite review, full `testenv.ps1 test` triage, and WA-01 fixed

**Worktree/branch:** `linux-regression-tests` (`.claude/worktrees/linux-regression-tests`).

### Request sequence

1. Review the new `tests/linux/run-linux-regression-tests.sh` (LA-04/05/06/07 — the backlog item
   "Missing regression tests from the Linux bounty"), fix any bugs, wire it into `testenv.ps1 test`,
   run it, fix failures.
2. Pasted the full output of `.\tests\testenv.ps1 test` after a real `build`+`up` — asked to
   investigate and fix every failure shown.
3. Asked twice for a status check on the rerun; asked "is there anything left to fix?"; then asked
   directly to fix the two pre-existing `run-linux-tests.sh` Decky-messaging failures, with an
   explicit constraint given mid-fix: **"don't mess with any other plugin please. moondeck is not any
   way relevant to this."**
4. This session: asked to dig into the long-standing intermittent `WA-01` flake and fix it if the fix
   turned out to be easy.

### Bugs found and fixed

**In the new regression script (`tests/linux/run-linux-regression-tests.sh`):**
- LA-04: the game id from `GET /api/games` is `id`/`path` (frozen for Decky-plugin back-compat, per
  `AgentApiServer.cs`'s `TrackedGameDto`) — an earlier attempt to "fix" this to `gameId` was itself
  wrong and reverted after tracing the real source rather than trusting a stale ad-hoc directory.
- LA-05: needed an explicit `agent register` before `add-game`; success check corrected from
  `"mapped"` to `"Tracking"` (the CLI's actual output string).
- LA-06: a bare `wait` blocks on *every* backgrounded job including the long-lived test server —
  fixed by capturing each job's own `$!` into an array and waiting on those PIDs specifically.
- LA-07: bash variables inside a single-quoted heredoc (`<<'PY'`) don't expand — Python was receiving
  the literal text `${deck_cfg}` as a path. Fixed by passing values as `sys.argv` instead.
- The agent build step needed `--no-incremental` (this repo's own documented convention) — an
  incremental build was serving a stale DLL after a branch switch.

**In `tests/testenv.ps1`:** `sync` skipped the git fetch+checkout entirely whenever the working tree
had no uncommitted changes, so a persistent WSL clone could sit on an ancient commit indefinitely.
Fixed to always invoke the WSL-side `sync` (with a possibly-empty changed-file list).

**In `tests/run-server-bugbounty-tests.ps1` and `tests/verify-password-compat.ps1`:** both hardcoded
the WSL distro name `Ubuntu-24.04`; this machine's distro is named `Ubuntu`. Added a `$WslDistro`
parameter (default `"Ubuntu"`) used by `wsl -d $WslDistro`. Fixed all 4 CS-01 raw-schema failures.

**In `tests/run-winagent-tests.ps1` (WA-10):** an ACL-cleanup `finally` block's `Set-Acl` threw a
cosmetic, non-fatal error (a Deny-ACE blocking its own later removal, though the parent key is
force-deleted regardless) — wrapped in `try { } catch { }`.

**In `src/Agent.Core/AgentCli.cs` (`add-game`):** a genuine product bug, not a test bug —
`config.Save()` was called unconditionally after `SetTracked`, but `SetTracked` already re-reads and
re-writes fresh under its own lock for a *brand-new* game; calling `Save()` again reopened a
lost-update window against a sibling process's concurrent add. Fixed to call `Save()` only when
re-adding an *existing* game (the case where field updates live only in memory until saved).

**Decky "no plugin installed" messaging gap (`tests/linux/run-linux-tests.sh`), fixed on request:**
root cause was `make-fixtures.py` unconditionally creating `$HOME/homebrew/plugins/moondeck/python`
(an unrelated MoonDeck fixture), which made `DeckyPlugin.DeckyPresent` (a directory-existence check)
evaluate `true` even in the "no Decky at all" test section. Fixed with `rm -rf "${HOME}/homebrew"`
immediately before that section, matching a cleanup pattern the file already used elsewhere. This
touches only the test harness's disposable fake `$HOME`, never a real install, and every
MoonDeck-dependent check runs and completes *before* that line — verified by a full rerun with zero
regressions (235/2 → 237/0), i.e. MoonDeck's own checks (part of the 237) were unaffected.

**`WA-01` (`the dashboard is told the real reason`) — the long-standing intermittent flake, found
2026-08-14, "not investigated":** reproduced live by extracting just the WA-01 block into a scratch
script and running it directly against the already-built Windows agent/server/daemon binaries (WA-01
itself needs no interactive desktop — the tray tests later in the same file do, which is why the
*whole* file is normally skipped as `-SkipWinAgentSuite`). Debug output on the failing run showed the
real command snapshot: `{"status":"Dispatched","result":null}`. `CommandStatus.Dispatched` is
documented as *"a lease, not a terminal state"* — it's set the instant the agent claims the command
from `/agent/commands`, before it has actually run the pull and POSTed a result back via
`/agent/commands/{id}/result`. The test's polling loop matched on `status -ne "Pending"`, which
`Dispatched` satisfies, so a 1-second poll could catch that exact in-between window and grab a
command with no result yet. Fixed by matching on the real terminal states (`status -in @("Done",
"Failed")`) instead. While in there, also hardened the match to filter by the command's own `id`
(captured from the `POST /api/commands` response, previously discarded) rather than "the most recent
Pull command server-wide" (`GET /api/commands` returns the 50 most recent commands across *all*
machines/games) — a real but separate robustness gap, unlikely to have been the actual cause here but
cheap and safe to close regardless. **Confirmed fixed**: reran the isolated WA-01 block twice more —
10/10 passing both times, where it had reproducibly failed once before the fix.

### Verification

- `run-linux-tests.sh`: 237 passed, 0 failed (was 235/2).
- `run-linux-regression-tests.sh` (LA-04/05/06/07): 15 passed, 0 failed.
- `run-server-bugbounty-tests.ps1`: 194 passed, 0 failed (was 190/4).
- WA-01 (isolated, live, on this Windows host, against the already-built Debug binaries): 10/10,
  twice in a row after the fix, having reproduced the historical failure once before it.
- Windows suite otherwise not rerun in full (needs an interactive desktop for the tray blocks).

### Not done

- The rest of `run-winagent-tests.ps1` (WA-02 onward) was not rerun end-to-end this session.

---

## 2026-08-28 — Decky conflict-resolution design (planning only, no app code)

### Task

Design moving conflict resolution out of the web dashboard and into the per-platform agents
(Steam Deck/Decky, Windows/Playnite, Linux). Two passes: (1) an initial 8-document design in
`docs/design/00`–`07` written *without* the user's real Decky fork; (2) after checking the actual
fork at `D:\Projects\SaveLocker\SaveLocker-Decky` (v0.2.3), a single consolidated plan reflecting
reality — `logs/2026-08-28_decky-conflict-resolution.md` — plus a `Backlog.md` entry. No application
code was modified; this pass produced planning documents only.

### Core architectural correction (from the user)

- **The server never decides a conflict.** It does only two mechanical things: move the head when
  told, and keep whatever isn't the head as a labeled, recoverable backup. All resolution *logic*
  (policy evaluation, human chooser, "mine vs. the cloud") lives in **Agent.Core**, the one engine
  every frontend reaches through the local `:5178` API. This reverses the original design's
  server-side `NewestWins`/`PreferMachine` auto-win in `SyncService.IngestAsync`.
- **Conflicts are always local-vs-cloud, never device-vs-device.** The other machine's name is
  supporting context only. This retires the "who else can resolve this / bystander" question.
- **"Keep both" = also protect the losing save as a downloadable backup**, surfaced in a *separate*
  dashboard "Backups" sub-menu — not mixed into normal history. Verified against `Entities.cs`: this
  needs **no new schema** (a backup is any version that isn't an ancestor of `Game.HeadVersionId`;
  reuses existing `GET /api/versions/{id}/download` and `POST /api/games/{id}/set-latest`).

### Launch-blocking spec (settled with the user)

When "sync before start" is active and a genuine, confirmed conflict is found as the player presses
Play — **whether or not "sync on page open" is also on** — the launch is cancelled, a resolve popup
opens (ASCII mockup in the doc), the player picks local or cloud, the app syncs that choice, then
auto-relaunches via `SteamClient.Apps.RunGame` — no second Play press. Carve-out: the
`pageOpenPullIsFresh` skip in `gamingSync.tsx` must NOT apply when the fresh page-open result was a
conflict. Playnite mirrors this via `OnGameStarting`/`CancelStartup` → dialog → sync →
`IPlayniteAPI.StartGame(Guid)`.

### Phasing (in the doc)

Phase 0/1 (server + Agent.Core, must ship together) → Phase 2 (Force push/pull bookkeeping fix),
Phase 7 (cheap sync-status endpoint), Phase 4 (Linux `ProtonRun.cs` gate), Phase 5 (Decky UI),
Phase 8 (Playnite) all parallelizable off 0/1 → Phase 6 (Decky launch-gate wiring) depends on 5.
Phase 3 (dashboard Backups tab) has zero dependencies and can ship first.

### Open item flagged, not resolved

The doc's Phase 0/1 claims it is "usable end-to-end from the CLI alone," but no `savelocker
conflicts` / `savelocker resolve` commands were added to `AgentCli.cs` in that scope. Either add
those CLI commands to Phase 0/1 or drop the claim — awaiting the user's choice.

### Deliverables (uncommitted, on branch `claude/savelocked-conflict-resolution-16e289`)

- `SaveLocker/logs/2026-08-28_decky-conflict-resolution.md` (new — the consolidated plan)
- `SaveLocker/Backlog.md` (modified — High-priority entry referencing the plan)
- `docs/design/00`–`07` (untracked — first-pass reference, partly superseded)

---

## 2026-08-29 — Conflict resolution Phase 0/1 implemented (server + Agent.Core), PR #20 green

**Branch:** `save-conflicts-phase-0-1`. PR: https://github.com/Marwanello/SaveLocker/pull/20.

### Task

Implement **Phase 0/1** of the Decky conflict-resolution plan
(`logs/2026-08-28_decky-conflict-resolution.md`): move the conflict-resolution *decision* out of the
server and into the agent. Server + Agent.Core only — no UI/Decky/Playnite (those are later phases).
One phase per session, verify it, stop.

### The architectural change, in code

- **Server no longer decides.** Removed the `autoWins` (`NewestWins`/`PreferMachine`) branch from
  `SyncService.IngestAsync` — every divergence now unconditionally records a `ConflictFlag`, full
  stop. Server-side auto-policy evaluation is gone.
- **The agent decides.** `SyncEngine` gained `TryPolicyResolveAsync`, wired into the
  `UploadStatus.Conflict` path *before* the CONFLICT alert: it fetches the game's policy, evaluates it
  locally (`thisMachineWins = NewestWins || (PreferMachine && PreferredMachineId == this machine)`),
  and if this machine wins, calls the resolve endpoint itself. Its success log deliberately avoids the
  word "conflict" — `"diverged from the server, but the save policy kept this machine's version."`
- **Comparison is always local-vs-cloud**, never device-vs-device.
- **`resolverMachineId` fan-out exclusion.** `ResolveConflictAsync` took a new `resolverMachineId`
  param: when an agent auto-resolves *its own* push, that machine is excluded from the fleet pull
  fan-out (it already has the winning bytes). The admin/dashboard path passes `null` → everyone,
  including the winner, is told (preserves the existing "resolving queued a pull for BOTH machines"
  test). This is what lets a *second* machine learn about a conflict the *first* machine's agent
  auto-resolved.

### Surfaces added

- **Server agent-group routes** (`Program.cs`, X-Api-Key gated): `GET /agent/conflicts`,
  `GET /agent/conflicts/{id}`, `POST /agent/conflicts/{id}/resolve` (passes the machine's name and id
  as resolvedBy/resolverMachineId), `GET/POST /agent/games/{id}/conflict-policy`.
- **Agent local API** (`AgentApiServer.cs`, loopback :5178, token-gated): `GET /api/conflicts`,
  `GET /api/conflicts/{id}`, `POST /api/conflicts/{id}/resolve` (`LocalResolveRequest`),
  `GET/POST /api/games/{id}/conflict-policy`, and the cheap `GET /api/games/{id}/sync-status`
  (Phase 7, folded in since it touches the same files — local hash vs. head hash, no download).
- **CLI** (`AgentCli.cs`): `conflicts` and `resolve-conflict` commands (`--keep local`→VersionBId,
  `--keep cloud`→VersionAId), filtered to this machine — so Phase 0/1 is genuinely usable end-to-end
  from the CLI alone, closing the open item the design flagged.
- **Contracts** (`Contracts.cs`): `ConflictPolicyDto`, `SyncStatusDto`.
- **ApiClient** (`Agent.Core`): the five client methods for the new agent-group routes.

### CI failure and fix (the one non-obvious part)

`package-linux` failed at `agent-ui`'s `npm run gen:api -- --check`: the committed
`agent-ui/src/api-types.ts` had **Windows** schema ordering. `openapi-typescript` emits
`components.schemas` in the OpenAPI document's key order, and .NET's OpenAPI generator orders schemas
by **reflection order — which differs between Windows and Linux**. One existing, unrelated schema
(`ResolveLaunchOptionsRequest`) sat in a different position than a Linux daemon produces; content was
identical. Fixed by regenerating the file against a real **Linux** daemon (built + run in WSL), so it
matches exactly what CI produces. Verified `gen:api -- --check` exits 0 against the Linux daemon
before pushing (`778a874`). The other two generated artifacts (`src/Server/openapi.json`,
`web/src/api-types.ts`) were pure additions with no reordering — already canonical.

**Repeat-prone footgun for next time:** any hand-regenerated `agent-ui/src/api-types.ts` must be
generated against a **Linux** daemon (WSL), not the Windows tray, or `package-linux` will fail on the
schema-order diff.

### Verification

- All CI checks on PR #20 green: build-dotnet, build-web, build-agent-ui, docker-build,
  package-linux, agent-tests-linux, and the crossos chain.
- Design preserves both load-bearing test assertions: run-server-bugbounty CS-04 ("the winning
  uploader gets no redundant pull") via the `resolverMachineId` exclusion, and run-agent-tests
  ("resolving queued a pull for BOTH machines") via the admin path's `null` resolverMachineId.

### Notes

- All three GitHub release build paths run `npm install` unconditionally before building
  (`installer/build-installer.ps1:28`, `packaging/linux/build-linux.sh:39`, `src/Server/Dockerfile`),
  and `testenv.ps1 build` installs too — so a fresh worktree/CI never ships an unbuilt agent-ui.
- A pre-existing, unrelated `run-server-bugbounty-tests.ps1` CS-03 flake (Windows Defender briefly
  holding a just-written zip → `IOException` on line 501) was confirmed out of scope and left as-is.

### Commits on `save-conflicts-phase-0-1`

- `41236fd` Conflict resolution Phase 0/1: move the decision into the agent
- `778a874` Fix CI: regenerate agent-ui api-types on Linux for canonical schema order
- (plus the docs commit for the design pass)

---

## 2026-08-29 — PR #20 xhigh review (6 fixes) + a Claude Code web SessionStart hook (PR #21)

### Request sequence

1. Asked for an xhigh-effort code review of [PR #20](https://github.com/Marwanello/SaveLocker/pull/20)
   (`save-conflicts-phase-0-1` → `main` — Phase 0/1 of the Decky conflict-resolution plan: the server
   stops auto-resolving conflicts, and `SyncEngine.TryPolicyResolveAsync` on the agent side becomes the
   new decision point, calling the same mechanical `ResolveConflictAsync` endpoint a human's choice
   would use).
2. Asked to apply the 6 surviving findings with minimal edits — explicitly as a description of each
   problem, not literal instructions — then report them back via `ReportFindings` echoing the original
   file/line/summary/failure-scenario text plus a fixed/no_change_needed/skipped outcome per finding.
3. Asked to set up a `SessionStart` hook so a Claude Code on the web session has the toolchain to build
   the repo and run its test suites, with instructions to implement directly unless it turned out to be
   a big task (it didn't).
4. Asked to push the review-fix commit to PR #20's actual head branch, not the workspace branch the fix
   had been committed to.
5. Asked to cut a pure-kebab-case branch (no `claude/` prefix) from `claude/session-start-hook` and open
   a PR from it.

### PR #20 review — 6 findings, all fixed

1. **`AgentCli.cs` (`resolve-conflict --keep local`)** didn't advance the local parent pointer after a
   successful resolve, leaving the CLI-driven path out of step with the assumption the server's
   fan-out-skip optimization already makes for the auto-policy resolver. Fixed by setting
   `LastKnownVersionId`/`LastSyncedHash` from the resolve response and saving config.
2. **`SyncService.ResolveConflictAsync`'s rewind guard could strand an auto-policy resolver.** An
   earlier attempt reassigned the winning version to the current head to dodge the refusal — caught in
   self-review that `SyncEngine.PushCoreAsync` assumes `ok=true` always means *the resolver's own*
   version won, so that would have silently corrupted local state in exactly the race the finding
   described. Fixed instead by keeping the refusal (`ok=false`) unconditional and queuing the stuck
   machine an unforced `Pull` so it converges on its own.
3. **`AgentApiServer.cs`, three related gaps** in the new local conflict routes: no filter on
   `/api/games/{id}/sync-status`'s open-conflict lookup (it could surface another machine's conflict),
   unhandled exceptions surfacing as bare 500s instead of this file's usual typed `ErrorResponse`
   shape, and `SaveArchive.HashDirectory` running synchronously on a request thread. Fixed with a
   `MachineId` filter, try/catch around all 6 conflict routes, and wrapping the hash in `Task.Run`.
4. **`SyncEngine.TryPolicyResolveAsync`'s catch blocks swallowed cancellation** from a retiring engine
   instead of letting it propagate, unlike this file's existing convention elsewhere (e.g.
   `PushCoreAsync`). Fixed by adding `when (!ct.IsCancellationRequested)` guards to match.

No .NET SDK was available in the review sandbox to `dotnet build`; verified by manual brace/paren-
balance checking and careful tracing instead, and disclosed as a limitation at the time.

### Landing the fix on PR #20's real branch

The review/fix work had been committed on workspace branch `claude/pr-20-xhigh-review-lrcpdq`
(`0551bb7`), but PR #20 is actually backed by `save-conflicts-phase-0-1`. Cherry-picked `0551bb7` onto
`save-conflicts-phase-0-1` as `e98f5ef` rather than force-pushing or rewriting history; the cherry-pick
left a 3-line `agent-ui/src/api-types.ts` diff from schema-key ordering differing between generation
environments, resolved the same way `778a874` (`Fix CI: regenerate agent-ui api-types on Linux for
canonical schema order`) already had, by regenerating on Linux.

### SessionStart hook for Claude Code on the web

`.claude/hooks/session-start.sh` (registered via `.claude/settings.json`), gated on
`CLAUDE_CODE_REMOTE=true` so local sessions are untouched:

- Installs the .NET SDK pinned in `global.json` via Ubuntu 24.04's own apt archive — not
  `dotnet-install.sh`, which fetches from `builds.dotnet.microsoft.com` and was blocked outright by
  this container's own network policy (`403` on the proxy `CONNECT`, confirmed via
  `$HTTPS_PROXY/__agentproxy/status`). Reads the major.minor from the pin dynamically rather than
  hardcoding it.
- Installs PowerShell Core from Microsoft's apt feed (not in Ubuntu's default archive, and mixing
  Microsoft's dotnet feed in alongside Ubuntu's own is a known source of package conflicts, so only
  PowerShell uses it).
- Restores `src/Server` and `src/Agent.Linux` specifically, not the full solution —
  `src/Agent` (WinForms) targets `net10.0-windows` and can't restore on Linux (`NETSDK1100`).
- Runs `npm install` in both `web/` and `agent-ui/` (the latter is `CONTEXT.md`'s recurring
  fresh-worktree gotcha).

Validated live end-to-end: cold run and an idempotent re-run of the hook, `oxlint` clean in both
frontends, `dotnet build --no-incremental` clean for Server + Agent.Linux, and
`pwsh tests/run-hardening-tests.ps1` → 37/37 against a real running server.

### Outcome

- PR #20: 6 findings fixed on its actual head branch (`save-conflicts-phase-0-1`, commit `e98f5ef`).
- New branch `session-start-hook` (kebab-case, no `claude/` prefix) cut from `claude/session-start-hook`
  and pushed; PR [**#21**](https://github.com/Marwanello/SaveLocker/pull/21) opened,
  `session-start-hook` → `main`.

### Not done

- PR #20's `mergeable_state` was `unstable` as of this session (checks/CI still settling) — not
  something this session's fixes addressed further.
- No `dotnet build` verification of the PR #20 fixes was possible in the review sandbox; only manual
  tracing.

---

## 2026-08-30 — Conflict-resolution plan expanded to 15 phases, docs consolidated into `tasks/`, Phase 5 shipped (PR #23)

**Branch:** `save-conflicts-phase-5`. PR: https://github.com/Marwanello/SaveLocker/pull/23.

### Request sequence

1. Investigative question: did any phase of the Decky conflict-resolution plan build a native
   Linux/Wayland resolve popup equivalent to Decky's, and do conflict popups exist on Windows/Linux
   outside Decky and a future Playnite plugin — with the constraint that everything should work even
   without the Decky plugin or Playnite installed.
2. Asked to close the gap found: add the missing phases, increase the phase count as needed, plan
   carefully and in detail.
3. Asked to explain D-Bus and recommend how to group the remaining phases into sessions, factoring in
   weekly Claude Pro quota usage (reported at 66%).
4. Asked whether running locally vs. in the cloud is cheaper token-wise, and how that changes the
   grouping.
5. Asked to write the grouping into a doc, move all conflict docs into a clearly-named tasks subfolder,
   and start implementing Group 1.
6. Asked to create a branch named like the previous two PRs, open a PR on the fork, and explain how to
   test it manually via `testenv`.

### What was found

- The original plan had no phase producing a resolve UI reachable without Decky or Playnite — a plain
  Linux desktop or a headless/SSH session had no way to even see a conflict, let alone act on one.
- `GET /api/games/{id}/sync-status` — described both in the plan and in this session's own first-draft
  grouping doc as "the cheap sync-status endpoint" — is not cheap. Its handler hashes the entire local
  save directory and internally calls the same full-state fetch the `status` CLI command already makes.
  Caught by reading the actual handler before wiring any UI to it, avoiding a real performance
  regression (a polling badge re-hashing save folders on a timer).

### What was built (design)

- `plan.md` expanded from 9 to 15 phases (0–14): a new "Decisions" §8, a rewritten dependency diagram,
  and an "escalation ladder" for Linux conflict surfacing — environment detection → native Wayland
  modal → D-Bus desktop notification → local web chooser → CLI → optional webhook → safe terminal state
  (never silently pick a side).
- `SaveLocker/tasks/conflict-resolution-ui/` created as the single home for all conflict-resolution
  planning docs:
  - `plan.md` (moved from `logs/2026-08-28_decky-conflict-resolution.md`)
  - `reference/00-inventory.md` … `07-open-questions.md` (moved from repo-root `docs/design/`)
  - `README.md` (new — folder index/orientation, explicit note this is not an ordinary single-file
    tasks item)
  - `implementation-grouping.md` (new — Groups 1–5, a buildable/verifiable-in-this-environment table,
    and the finding that local vs. cloud execution costs the same weekly quota, so grouping is driven
    by real dependencies and what can actually be verified here, never by cost)
- `logs/2026-08-28_decky-conflict-resolution.md` left as a one-paragraph stub pointing at the new
  location, matching the vault's existing stub convention.
- `CONTEXT.md` and `Backlog.md` updated: path references fixed, phase-count/status summary corrected,
  Phase 12's mischaracterization documented transparently rather than silently fixed.

### What was built (Group 1 / Phase 5)

- `src/Agent.Linux/DesktopEnvironment.cs` (new): `DesktopEnvironment.Detect()` returns a
  `DesktopSessionInfo` — graphical session (`$WAYLAND_DISPLAY`/`$DISPLAY`), D-Bus session bus
  reachability (parses `DBUS_SESSION_BUS_ADDRESS`, connects a real `UnixDomainSocketEndPoint`),
  notification daemon presence (`gdbus call ... org.freedesktop.DBus.NameHasOwner
  org.freedesktop.Notifications` — chosen over `dbus-send` for its poor `as`/`a{sv}` marshalling),
  interactive TTY, and whether running as a `systemd --user` unit (`$INVOCATION_ID`). Its subprocess
  runner mirrors `SystemdAutoStart.Run`'s concurrent-stdout/stderr-drain pattern, plus an added
  2-second timeout + `Kill(entireProcessTree: true)` so a stale/half-open D-Bus socket can never hang
  `doctor` or the future launch wrapper.
- `Doctor.cs`: new purely-informational "Session" section reporting all five signals.
- `run-linux-tests.sh`: 9 new checks — baseline (no session), a bogus `DBUS_SESSION_BUS_ADDRESS`, a
  real-but-protocol-incomplete UNIX socket (proves session-bus-reachable and notification-daemon-present
  are genuinely independent signals), and `INVOCATION_ID` detection.

### Bug caught before running the suite

The three new sub-invocation blocks in `run-linux-tests.sh` initially reused the shared `$out`
variable, which would have silently broken later, pre-existing MoonDeck assertions in the same script
that depend on the original `$out`. Renamed to a dedicated `$env_out` before running anything.

### Verification

- `run-linux-tests.sh`: 246 passed (was 237), 0 new failures.
- Confirmed via `git stash` that a separate, pre-existing 7-failure cluster in the "Decky plugin
  updates" section predates this session's changes — documented in `Backlog.md` as a known issue, not
  fixed here (out of scope for Phase 5).
- `dotnet build --no-incremental` and `tsc -b` both clean after stale-path `sed` fixes across
  `Program.cs`, `SyncService.cs`, `GameDetail.tsx`, `AgentCli.cs`, `SyncEngine.cs`,
  `run-server-bugbounty-tests.ps1`.

### PR and manual test instructions

Branch `save-conflicts-phase-5` cut from `claude/save-conflict-resolution-ui-9pjaju` (naming matches
`save-conflicts-phase-0-1` / `save-conflicts-phase-2-3` from PRs #20/#22); PR
[**#23**](https://github.com/Marwanello/SaveLocker/pull/23) opened against `main`. Manual verification
instructions were given for the new `doctor` "Session" block on a real Steam Deck via
`tests/testenv.ps1` (`build -Only deck` → `up` → SSH in, run `doctor`), including the nuance that an
SSH login does not inherit the Deck's own desktop session's `DISPLAY`/`WAYLAND_DISPLAY`/
`DBUS_SESSION_BUS_ADDRESS` — so the "real session present" case needs either a terminal opened directly
inside Desktop Mode, or importing `systemctl --user show-environment` first.

### Not done

- Phase 12 (sync-status consumer) deliberately deferred — waits for Phase 6/8/10 to add a genuine
  on-demand trigger.
- Groups 2–5 (Phases 4, 6, 7, 8, 9-impl, 10, 11, 13, 14) not started this session.
- Real-hardware confirmation of the "Session" block's values in Game Mode vs. Desktop Mode is still
  outstanding — flagged in the PR for the user to check.
- Offered to `subscribe_pr_activity` on PR #23; not yet accepted or declined as of this write-up.

---

## 2026-08-30/31 — Phase 9 regrouped off Decky/Playnite; PR #23 review-fix pulled in

**Branch:** `save-conflicts-phase-5`. PR: https://github.com/Marwanello/SaveLocker/pull/23.

### Request sequence

1. Asked why Phase 9 wasn't implemented, since it seemed to be part of Group 1 — then asked to
   implement it if it was.
2. After being shown Phase 9's full implementation has a second real dependency (Phase 6, not yet
   built) beyond Phase 5, and asked to choose between building Phase 6 first, implementing Phase 9 now
   with a temporary action-button target, or implementing just the notification-firing logic with no
   action button: chose a fourth option — regroup the phases so no dependency conflicts exist, place
   Phase 9 in the group it actually belongs to, and don't implement it yet.
3. Asked to pull the latest branch changes, then update both `progress.md` and `session_summary.md`.

### What was found

Only Phase 9's **D-Bus library spike/decision** (picking `gdbus`) was ever part of Group 1 — the
notification-sending implementation itself was always scoped to a later group. But that later group
(the original Group 5) bundled it with Decky (Phase 10/11) and Playnite (Phase 13) under a "needs real
hardware" rationale that, on re-checking `plan.md`'s own dependency diagram, doesn't actually hold:
Phase 9's only real dependencies are Phase 5 (done) and Phase 6 (not yet built) — never the separate
`SaveLocker-Decky` repo, never Playnite, and building it doesn't require real hardware at all. Only
*verifying* a popup actually fires does. The original grouping had silently coupled a buildable,
hardware-independent phase to an unrelated repo-access and hardware requirement it never needed.

### What was built

`implementation-grouping.md` corrected, no application code touched:

- The "buildable/verifiable here" table's Phase 9 row rewritten to state its real dependencies (Phase 5
  + Phase 6 only, no Decky repo needed to build).
- A new **Group 3** created for Phase 9's actual implementation, placed right after Group 2 (which
  ships Phase 6, the action button's target) rather than after the Decky/hardware groups. Old Groups
  3–5 renumbered to 4–6 (Phase 8; Phase 7+14 on a Windows machine; Phase 10/11+13 needing the Decky repo
  and real hardware, with Phase 9 removed from this last group).
- A new "Correction found before Group 3" subsection documenting the miscategorization and the fix,
  matching this doc's existing convention for the Phase 12 correction.
- The final sequencing code block updated to match the new 6-group numbering.

Committed as `764ca3c` ("Docs: regroup Phase 9's implementation away from Decky/Playnite") and pushed.

### Pulled in: PR #23 review-fix commit from another session

`git pull origin save-conflicts-phase-5` fast-forwarded `764ca3c` → `e63f5c2` ("Fix PR #23 review
findings: crash guard, dedup, doc rule, test coverage"), authored by a separate Claude session working
the PR's review findings directly (not this session). Four changes:

- **`DesktopEnvironment.IsInteractiveTty`** now wraps `Console.IsInputRedirected` in a try/catch
  (`IsInteractiveTtySafe`) — it can throw when stdin is in an unusual state (e.g. its file descriptor
  closed outright), which would have crashed `doctor` outright instead of just reporting "no" like
  every other probe in the class.
- **Deduplication:** the process-run-with-drained-streams helper that `DesktopEnvironment.Run` (Phase 5)
  and `SystemdAutoStart.Run` had each implemented separately is now one shared internal
  `Agent.Linux/ProcessRunner.cs`, with a default infinite-wait overload (matching
  `SystemdAutoStart`'s prior behavior) and a timed overload (matching `DesktopEnvironment`'s 2-second
  D-Bus-hang guard) — so a future deadlock/timeout fix lands once instead of twice.
- **`CLAUDE.md`** gained a one-paragraph note documenting `tasks/conflict-resolution-ui/` as a
  deliberate, disclosed exception to the vault's "flat by design" rule — a standing multi-session design
  doc set read as reference material, not a one-shot `tasks/*.md` file.
- **`run-linux-tests.sh`**: added the missing `IsInteractiveTty: no` assertion for the baseline "no
  session" case, pinned deterministically via `< /dev/null` stdin redirection (the check depends on the
  harness's own stdin, which varies between a developer's terminal and CI's redirected one).

No merge conflicts — a clean fast-forward on top of this session's own regrouping commit.

### Not done

- Phase 9's actual implementation still not started, per this session's explicit instruction.
- Real-hardware confirmation of the Phase 5 "Session" block (Game Mode vs. Desktop Mode) still
  outstanding.
- Whether to `subscribe_pr_activity` on PR #23 remains an open offer.

---

## 2026-08-31 — Real-Deck doctor verification: a missed `sync`, then a wrong assumption caught

**Branch:** `save-conflicts-phase-5`. PR: https://github.com/Marwanello/SaveLocker/pull/23.

### Request sequence

1. User ran the manual `testenv.ps1` verification instructions from the prior session on a real Deck
   and reported `doctor`'s output had no `── Session ──` block at all.
2. After the fix, user re-ran it and reported the block appeared but with unexpected values — asked
   about the discrepancy.
3. Answered a clarifying question: confirmed the Deck was genuinely in **Game Mode (Gamescope)**, not
   Desktop Mode, when the surprising result was captured.

### Bug #1: `testenv.ps1 build` never syncs the WSL clone by itself

Root-caused by re-reading `testenv.ps1` directly: `build -Only deck` shells into WSL and runs
`build-deck` on whatever commit is *already checked out there* — it never fetches or checks out
anything itself. That's what the separate `sync` command does. The user's first `build -Only deck`
ran without a preceding `sync`, so it silently built a commit from before Phase 5 existed — explaining
the total absence of the Session block, not a code bug. Fixed by giving the correct sequence
(`git checkout` → `sync` → `build -Only deck` → `up -Only deck`) and documenting the gap in both
`Build and Run.md` and `Gotchas.md` (commit `0d36eab`) so it isn't repeated.

### Bug #2: a real, confirmed-wrong assumption in the Phase 5 code and plan

After the rebuild, the Session block appeared correctly, but with a result nobody had predicted:
```
graphical session: no
D-Bus session bus: yes
notification daemon: yes
interactive terminal: yes
running as: interactive process
```
over a plain SSH shell. `DesktopEnvironment.cs`'s own doc comment asserted "Game Mode has no session
bus at all" — a claim that had never actually been checked on hardware, only assumed. Rather than
accept the surprising result at face value or dismiss it as an anomaly, asked the user to confirm what
mode the Deck was actually in at the time (`AskUserQuestion`) before writing anything down: **the Deck
was genuinely in Game Mode.** That confirms the assumption was wrong, not that Desktop Mode was
running unnoticed: SteamOS keeps one persistent per-user D-Bus bus alive via `systemd --user`
regardless of graphical mode, and SSH reaches the same bus a running Gamescope session already has,
with something already claiming `org.freedesktop.Notifications` ownership on it.

**Fixed (commit `71b50fa`):** `DesktopEnvironment.cs`'s doc comment and `plan.md`'s Phase 9 section
both corrected to state what's now actually confirmed, and what's still genuinely open — who the real
notification-name claimant is, and whether a live `Notify` call would render anything visible in Game
Mode or get silently dropped. `Detect()`'s code itself needed no change; it already computed the right
answer (`true`/`true`) — only the prose describing what that meant was wrong. Flagged as a real,
plan-changing possibility: a Game-Mode-visible desktop notification (not just Desktop-Mode-only, as
Phase 9 currently scopes it) may be reachable sooner than assumed, pending one more live check (fire a
test `Notify` call while actually in Game Mode, see if anything appears on screen).

### Verification

- The rebuilt binary's `doctor` output now shows the Session block as expected structurally; its
  *values* in Game Mode are the new, corrected information above rather than a test failure.
- No code behavior changed in either fix — both were documentation/assumption corrections traced back
  to real, reproducible evidence (a stale build in the first case, a live hardware result confirmed by
  the user in the second) rather than either being taken on faith.

### Not done

- The live "does `Notify` actually render in Game Mode" check flagged in `plan.md` is not yet
  performed — needs one more manual SSH session against the real Deck.
- Desktop Mode's own Session-block values (as opposed to Game Mode's, now captured) are still
  unconfirmed.
