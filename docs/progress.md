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

[continues below — see rest of file for interleaving entries between 2026-08-31 and this one]

---

## 2026-09-07 — Decky Phase 10 shipped, Playnite regrouped, testenv worktree bug fixed

**Main repo branch:** `claude/savelocker-decky-worktree-d43a9e` (worktree
`.claude/worktrees/save-conflict-next-group-ebc9b1`). **Decky repo branch:**
`decky-conflict-resolution-ui`, now at `D:\Projects\SaveLocker\SaveLocker-Decky\.claude\worktrees\decky-conflict-resolution-ui`
(moved there this session — see below).

### Request sequence

1. Implement "the next group" in the conflict-resolution-ui plan, given the Decky plugin repo lives
   at `D:\Projects\SaveLocker\SaveLocker-Decky`; create a worktree/branch there if needed; flag any
   step that needs manual hardware verification, with steps.
2. Asked what was implemented, for a step-by-step `testenv`-based verification walkthrough, and to
   move Playnite (Phase 13) into its own group so it can be implemented later, independent of Decky.
3. Ran the given verification steps, hit `fatal: not a git repository: ...` from `.\tests\testenv.ps1
   sync`; asked why.
4. Asked to move the Decky worktree from its ad hoc location
   (`D:\Projects\SaveLocker\SaveLocker-Decky-worktrees\decky-conflict-resolution-ui`) to the standard
   `.claude\worktrees` location under the Decky repo itself.

### What was built — Decky Phase 10 (conflict display + resolve UI)

Implemented in a new worktree of the Decky repo, branch `decky-conflict-resolution-ui` off `main`:

- `main.py` — 7 new backend methods proxying the local agent API's conflict routes
  (`conflicts`, `conflict`, `resolve_conflict`, `conflict_policy`, `set_conflict_policy`,
  `save_version`, `version_stats`).
- `src/shared.tsx` — `machineId` added to `AgentState`; a new "Conflicts" section with
  `ConflictPolicyKind`/`Conflict`/`SaveVersion`/`VersionStats`/`ConflictPolicySetting` types and
  matching fetch/resolve callables, mirroring the local API's camelCase fields exactly.
- `src/conflicts.tsx` (new, ~300 lines) — 20s poller, chip-merge logic (never overwrites a
  `'syncing'` chip), and `ConflictResolveModal` (`ModalRoot`/`DialogButton`/`ToggleField`/
  `Focusable`), with immediate-resolve on card press and a "Keep both" toggle.
- `src/libraryOverlay.tsx` — the sync chip becomes clickable in the `'conflict'` state, opening the
  resolve modal for that game.
- `src/index.tsx` — new QAM "Save conflicts" panel (`ConflictWarnings`), conflict polling
  registered/unregistered with plugin mount/unmount.
- `src/fullPage.tsx` — per-game "If a save conflict happens" dropdown (Manual / Newest save always
  wins / Prefer this device), with a caption when a different machine is currently preferred.
- `src/toast.tsx` — new `'conflict'` toast kind.

**One small necessary main-repo addition:** `AgentStateDto` (`src/Agent.Core/AgentApiServer.cs`)
gained a trailing `Guid? MachineId` field, populated from `_config.MachineId` in the `/api/state`
handler — needed so Decky's "prefer this device" policy option has a machine id to send.
`agent-ui/src/api-types.ts` regenerated against a scratch dev daemon; diffed to confirm only the
`machineId` field appeared. Commit `7306968` (main repo). Decky repo: single commit `2f06573`.

Build and type-check both verified clean; one unused-import build error (`fetchState` imported but
never used in `conflicts.tsx`) caught and fixed before the final build.

### Docs updated

`SaveLocker/Backlog.md`, `SaveLocker/CONTEXT.md`, and
`SaveLocker/tasks/conflict-resolution-ui/plan.md` all updated with the Phase 10 write-up and a
five-point manual verification checklist (commit `170cc63`, main repo).

### Playnite split into its own group

Per explicit request, `implementation-grouping.md`'s old combined "Group 6 (Phase 10, 11, 13)" was
split: **Group 6** now covers Decky only (Phase 10 + 11), and a new **Group 7** covers Playnite only
(Phase 13) — Playnite has no real dependency on the Decky work, so bundling them under one "needs
real hardware" rationale was an avoidable coupling, the same category of grouping mistake as the
earlier Phase 9 miscategorization (see the 2026-08-30/31 entry above). Commit `976b863`.

### Bug found and fixed: cosmetic `fatal: not a git repository` in `testenv.ps1 sync`

The user ran `.\tests\testenv.ps1 sync` from this session's worktree and got:

```
fatal: not a git repository: /mnt/d/Projects/SaveLocker/SaveLocker/.claude/worktrees/save-conflict-next-group-ebc9b1/D:/Projects/SaveLocker/SaveLocker/.git/worktrees/save-conflict-next-group-ebc9b1
```

despite the sync completing correctly. Root cause: a git worktree's own `.git` file stores
`gitdir: D:/Projects/SaveLocker/SaveLocker/.git/worktrees/save-conflict-next-group-ebc9b1` — a
Windows-native path, correct for Windows git. `testenv.sh`'s `cmd_sync()` runs a
`git config --global --add safe.directory "$gitdir"` call from inside WSL, whose CWD (via the
`/mnt/d/...` mount) is that same worktree; WSL's git reads the pointer file, doesn't recognize
`D:/...` as absolute, and concatenates it onto CWD instead — producing the garbled path in the
error. Confirmed cosmetic (not blocking) by isolating the exact failing statement via `bash -x`,
then contrasting `git config --global --add safe.directory ...` (still exits 0 despite printing the
fatal line) against `git config --global --list --show-origin` from the same CWD (fails outright,
exit 128) — proving the discovery-triggered fatal message is a side effect specific to the `--add`
path, unrelated to whether the config write itself succeeds. This is the first session to run
`testenv.ps1` from a worktree rather than the main checkout, which is why it hadn't shown up before.

**Fix:** added `2>/dev/null` to the previously-unsilenced `--add` line in `tests/testenv.sh`'s
`cmd_sync()`, matching the sibling `--get-all` line's existing pattern, with an explanatory comment.
Verified by re-running `.\tests\testenv.ps1 sync` — clean output, no fatal line, and the sync itself
still correctly picked up the just-edited file. Committed as `6dcfb2a`.

### Decky worktree relocated to the standard location

The Decky-repo worktree had been created at an ad hoc sibling path,
`D:\Projects\SaveLocker\SaveLocker-Decky-worktrees\decky-conflict-resolution-ui`, instead of the
project's standard `.claude\worktrees` convention (already used by another worktree in that repo,
`jovial-mclaren-208112`). Moved in place with `git worktree move` (run from
`D:\Projects\SaveLocker\SaveLocker-Decky`) to
`D:\Projects\SaveLocker\SaveLocker-Decky\.claude\worktrees\decky-conflict-resolution-ui` — branch,
history, and the single commit `2f06573` carried over untouched; `git worktree list` confirmed the
new path registered correctly. The now-empty old parent directory
(`SaveLocker-Decky-worktrees`) was removed.

### Verification status

- Decky plugin: build + type-check clean; **not yet verified on real hardware.**
- Main repo: `AgentStateDto` change + regenerated `api-types.ts` diffed and confirmed minimal.
- `testenv.ps1 sync` fix: verified via direct re-run (clean output, correct sync behavior).

### Not done

- **Real-hardware verification of Phase 10** — the five-step Decky checklist (QAM "Save conflicts"
  panel, resolve popup D-pad nav/immediate-resolve/B-cancel, "newer" tag, "keep both" toggle,
  full-screen settings page's conflict-policy dropdown) has not yet been run against a real Deck via
  `testenv.ps1 build`/`up`. The user was mid-way through this walkthrough when the `sync` error
  interrupted it.
- Phase 11 (Decky launch-gate wiring) — explicitly deferred until Phase 10 is hardware-verified.
- Phase 13 (Playnite, now Group 7) — explicitly scoped as "implement later," not started.
- Neither the Decky repo branch nor the main repo branch has been pushed to any remote — no push was
  requested or authorized this session.
  unconfirmed.

---

## 2026-09-01 — PR #24 xhigh review completed (9 fixes across 11 files), pushed

**Branch:** `save-conflicts-phase-4-6`. PR: https://github.com/Marwanello/SaveLocker/pull/24
("Conflict resolution Phase 4 + 6, and a local-vs-cloud UI redesign" — the Linux launch gate in
`SyncEngine.PrepareLaunchAsync`, a new agent-ui Conflicts page, a `doctor` conflicts section, and a
sync-time conflict pop-up).

### Request sequence

1. `/code-review xhigh --fix PR#24` — 10 parallel finder agents ran across differing review angles and
   reported ~21 raw candidate findings one at a time.
2. **"were all of theese fixed?"** — investigation found the review had stalled after Phase 1
   (finding): no fix commits existed and the two most-corroborated bugs were still present in the code.
3. **"yes please"** — explicit authorization to resume: finish verification on the outstanding
   candidates and apply fixes for the ones that survive.
4. Verified and fixed everything directly (reading code rather than re-spawning finder agents),
   deduplicating ~21 raw findings down to 9 confirmed fixes; several structural/lower-value findings
   were explicitly scoped out and documented as skipped rather than fixed.

### The critical bug: a bystander-visible, null-DTO conflict could silently pass the launch gate

`SyncEngine.PrepareLaunchAsync`'s Steam launch gate only ever inspected `pushResult.Conflict`. That DTO
is populated on the common path, but `PushCoreAsync`'s `ConsecutiveConflicts` rate-limit fast path
returns `UploadStatus.Conflict` with a **null** DTO and makes no network call at all — so a genuinely
still-open, confirmed conflict would fall through the `if (fresh is not null)` check as if nothing were
wrong, defeating the entire point of the gate. Fixed by falling back to `FindOpenConflictAsync(game)`
whenever `pushResult.Conflict` comes back null.

### The other systemic bug: bystander conflicts leaking into agent-ui everywhere

The server route `/agent/conflicts` is deliberately unscoped by design (a machine key sees every open
conflict; "its own frontend decides what to show" — `Program.cs`). The CLI's `conflicts` command
already applied the "no bystander case" `MachineId` filter (`AgentCli.cs`), but the new local HTTP API
route `/api/conflicts` in `AgentApiServer.cs` — the single endpoint agent-ui exclusively consumes —
did not, so every conflict in the whole system (not just ones this machine is a party to) was showing
up in the Conflicts page, the sync pop-up, and `doctor`. Fixed with the same `MachineId` filter at that
one shared choke point, which fixed all three surfaces at once. `Doctor.cs` had also been applying its
own separate, redundant filter — consolidated onto the same pattern for consistency.

### Other fixes

- Hardcoded `127.0.0.1:5178` URLs in launch-blocked messages (`SyncEngine.cs`, `ProtonRun.cs`,
  `Doctor.cs`) replaced with a real, persisted port: new `AgentConfig.DaemonApiPort` field, written by
  `Daemon.cs` at startup from whatever port it actually bound to (supports `daemon --port <n>` for test
  harnesses running alongside a real agent), read everywhere a Conflicts-page URL is printed.
- `SyncConflictModal.tsx` — missing `key={conflict.id}` on `ConflictCard` was letting React reuse
  component instances across list items, leaking state between unrelated conflict cards.
- `ConflictCard.tsx` — simplified `mine` computation; added a `resolving` guard against double-submit
  on the resolve-side click handler.
- `App.tsx` — the sync-time conflict pop-up could still fire on top of the Conflicts page if the user
  had already navigated there themselves while a "Sync now" request was in flight; fixed by suppressing
  the pop-up when `view === 'conflicts'`.
- `SyncService.cs` — deduplicated a repeated EF Core query pattern (`FindVersionAsync` helper shared by
  `DownloadVersionAsync` and `GetVersionAsync`).
- `GameDetail.tsx` (web) — aligned a "diverged" timestamp regex with the one used elsewhere.

### Verification

Full builds, plus three live PowerShell integration suites, all against real running server/agent
state:

- `run-agent-tests.ps1`: 45/45.
- `run-health-tests.ps1`: 22/22.
- `run-local-api-tests.ps1`: 30/30.

Every non-clean run along the way traced to environment setup, not regressions — no server started, a
dirty DB reused across a `.verify/`-only clear (the documented `Gotchas.md` pitfall, matched exactly),
and an unstaged/unbuilt agent-ui `dist/`. A `git stash` used mid-session for a control comparison was
immediately `stash pop`'d back with no work lost.

### Landing the fix

Committed and pushed on top of an intervening unrelated upstream commit (`84b3e0a`, isolating
`seed-test-conflict.sh`'s build tree) via a clean merge — no rebase, no force-push. Final head:
`6aab07a`, confirmed via the GitHub API to match PR #24's current head exactly (`open`,
`mergeable_state: clean`, 8 commits, 1555 additions / 32 deletions / 29 changed files).

### Not done

- Several lower-value/structural findings from the 10 finder agents were deliberately left unfixed
  (documented at fix time, not re-litigated here) as lower priority or higher risk relative to their
  value.

---

## 2026-09-02 — Checkpoint UI redesign: design spec, implementation plan and brand kit

**Branch:** `claude/dashboard-agent-ui-redesign-992386` (local only, not pushed).
**Commit:** `6963b4c` — "Docs: Checkpoint UI design spec, implementation plan and brand kit"

### Request

Full UI redesign of the SaveLocker dashboard (console) and agent UI, delivered as interactive mockups
grounded in the app's real data, endpoints and component structure. Iterated across seven turns covering
identity exploration, layout direction, accent/theme experiments, typography, motion, notifications,
Steam artwork, marks (logos), and finally the implementation/brand deliverables.

### What was built

**Interactive prototype** — `checkpoint-prototype.html`, published at
https://claude.ai/code/artifact/b8f247f2-32e5-4808-8e4c-61ba0cc3406f. Surfaces: Console, Agent,
Deck-Wayland, Notifications, Marks & art, Flows. Light + dark themes, five accents, three marks,
Archivo typeface throughout. All data pulled from the real API surface (game names, event codes,
release dates, machine names, version/command/audit tables, Deck UI strings).

**Five identity options** — `savelocker-redesign.html` (Cold Storage / Checkpoint / Ledger / Shelter /
Hangar), each with palette, type stack, voice and mockups. Checkpoint was chosen.

**Design spec** — `SaveLocker/tasks/checkpoint-ui/plan.md`. Covers: decisions table, both dark/light
token sets, `color-mix` derivation rule, colour rule (green/amber/accent), type scale, layout rules,
motion table, voice guidelines, per-surface shell table.

**Implementation plan** — `SaveLocker/tasks/checkpoint-ui/implementation.md`. Opens with "what already
exists" (maps every prototype element to its real endpoint/component), then eight phases:
1. Design system foundation (fix unlayered CSS reset, Checkpoint tokens, Archivo import, motion
   primitives, shared components)
2. Console shell (two-line rows, grid view, bell menu, sign-in screen, exclude chips, release history)
3. Sync all + progress (bulk command endpoint, console progress rail, agent progress — with the
   correctness requirement that progress ticks must not re-render surroundings)
4. Appearance + fleet sync (server-side settings, heartbeat-carried theme/accent/mark pushed to agents)
5. Agent UI (Games tab, art proxy, search in Add games)
6. Deck/Wayland (Checkpoint dark tokens in ImGui, Sync all bound to Y, Wayland window decision)
7. OS notifications (Windows toast, Linux freedesktop, firing rules)
8. Assets (marks and Steam art as real files at all required sizes)

**Brand kit** — `SaveLocker/tasks/checkpoint-ui/brand-kit.html`, also published at
https://claude.ai/code/artifact/b3e0c8a5-70a0-47bf-b4f2-d0dbf4f0b2d5. Eight sections: marks (three
with size ladders, specimens, clearspace, don'ts), colour (live swatches + derivation + status trio +
five accents), type (Archivo scale, tabular-figures demo, where mono belongs), components
(buttons/chips/two-line row/stats/radii), motion, Steam artwork (four crops), voice (not-this/this
pairs), paste-ready tokens with the unlayered-reset warning.

### Decisions recorded

| Decision | Value |
|---|---|
| Direction | Checkpoint |
| Typeface | Archivo for headings and data; mono only for code/CLI/logs |
| Accent | Ember `#e0533c` dark / `#c0432c` light, user-changeable (five options) |
| Themes | Light and dark, both first-class |
| Marks | Cartridge, Pixel lock (default), Memory card |
| Steam art | Approved as drawn in prototype |
| Decky plugin | Untouched — Steam-native look is correct |

### Key findings

- `web/src/index.css` has an unlayered `* { box-sizing; margin: 0; padding: 0 }` reset that beats
  every Tailwind utility — root cause of the codebase's inline-style pattern. Fixing this (Phase 1)
  gates the entire redesign.
- The font `@import` must stay above `@import "tailwindcss"` or it is silently dropped.
- Phase 4 (appearance sync via heartbeat) is the largest genuinely-new backend piece.
- Phase 6 item 4 (Wayland desktop window: GTK/WebKit shell vs. browser) is the one open decision.

### Bugs found and fixed in prototypes

- Progress-bar animation rerun: full `innerHTML` rebuild per tick replayed entrance animations. Fixed
  with targeted DOM patching (`patchSync()`).
- Brand-kit theme swatches not repainting: `requestAnimationFrame` never fires in a hidden tab. Fixed
  by calling paint functions synchronously.
- `.cap` CSS class collision between store-art capsules and spec-cell labels. Fixed by renaming to
  `.scap`.

### Status

Design phase complete. All deliverables committed locally. `CONTEXT.md` not yet updated with handoff
entry. Next step: update `CONTEXT.md` per session-end convention, then begin Phase 1 implementation
when ready.

---

## 2026-09-02 (cont'd) — Checkpoint UI phase grouping, corrections, and mockups committed

**Branch:** `ui-redesign-plan`, created off `claude/dashboard-agent-ui-redesign-992386`.
**Commits:** `fe86729`, `ab4c957`, `de2cfd5`.

### Implementation grouping

Added `SaveLocker/tasks/checkpoint-ui/implementation-grouping.md` and a folder `README.md`, matching
the `tasks/conflict-resolution-ui/` precedent for a multi-session design effort. The grouping regroups
the 8 phases **by surface** rather than by phase number, since several phases edit the same
components — Sync all and the notifications bell both land in the top bar Phase 2 already rewrites,
so they ship together or `NavBar.tsx` gets edited twice. Seven groups; Phase 8 (assets) pulled forward
into Group 1 since the art is already designed and the favicon is the cheapest end-to-end proof the
token pipeline works. Group 5 (appearance pushed to agents over the heartbeat) is the only wire-format
change and stays alone. Groups 6 (Deck) and 7 (Linux notifications) are buildable here but need real
hardware/a live desktop session to verify.

### Three corrections to `implementation.md`, found by checking source instead of assuming

- **`agent-ui` has no Tailwind and no `.css` file at all** — 215 inline `style={{}}` sites, zero
  classNames. It cannot "import the same file" as the console. How it receives design tokens is now
  an explicit Group 1 decision (recommended: a plain CSS custom-property file both apps import, since
  the agent's inline styles can consume `var(--…)` with no conversion).
- **`ArtService` already fetches the 600×900 `grid` kind** plus `hero`/`logo`/`icon`, with all four
  URLs already on the game DTO. The grid wall needs zero server work.
- **`GET /api/games/{id}/sync-status` must not back a list view.** Its own handler comment says it
  walks and reads every file in the save folder, plus a full `GetStateAsync`. Polling it per game in
  the new agent Games tab would re-hash every save folder on a timer — the same mistake the
  conflict-resolution plan caught and pulled its own Phase 12 for.

Also counted the real scale of the inline-style problem: **388** sites in `web/src` against 4
classNames, **215** in `agent-ui/src` against 0. Tailwind is installed in `web` and effectively
unused. This reframes Phase 1 — layering the CSS reset unblocks utilities but converts nothing, so
the migration rides inside later groups one surface at a time; there is deliberately no
"migrate all inline styles" session.

### Incident: `session_summary.md` history clobbered, then restored

While writing the previous turn's summary, `session_summary.md` — a running log, newest-first, the
mirror of `progress.md`'s newest-last convention — was overwritten with a truncating redirect instead
of prepended, dropping four prior entries (2026-09-01, 2026-08-30/31, 2026-08-29, and earlier). Caught
via an unexpectedly large deletion count on the next commit. Restored every prior entry byte-identical
(verified by diff) and prepended the new entry in the file's own order. Commit `ab4c957`.

### Mockups committed to the repo

Copied the two remaining artifact-only deliverables into `SaveLocker/tasks/checkpoint-ui/` so nothing
depends on the live artifact links: `prototype.html` (the interactive mockup — Console, Agent,
Deck/Wayland, Notifications, Marks & art, Flows, both themes, five accents, three marks) and
`identity-options.html` (the five identity pitches Checkpoint was chosen from). `brand-kit.html` was
already committed in the prior session. `README.md` and `plan.md` updated to point at the local files
as the primary reference, keeping the artifact URLs as link-sharing mirrors only. Commit `de2cfd5`.

### Status

`SaveLocker/tasks/checkpoint-ui/` now holds the complete design-phase deliverable set locally: `plan.md`,
`implementation.md`, `implementation-grouping.md`, `README.md`, `prototype.html`, `identity-options.html`,
`brand-kit.html`. All work is on branch `ui-redesign-plan`, local only, not pushed, no PR opened.
`CONTEXT.md` still not updated with the handoff entry. Next step: either update `CONTEXT.md`, or begin
Group 1 of the implementation grouping (layer the CSS reset, land Checkpoint tokens, build shared
primitives, export marks, decide how `agent-ui` receives tokens).
## 2026-09-01/02 — Conflict resolution Group 3 / Phase 9: Linux desktop notification (save-conflicts-phase-9)

**Branch:** `save-conflicts-phase-9` (6 commits on top of `debf37e`; 618 insertions / 23 deletions
across 8 files). No PR opened. Group 3 of `tasks/conflict-resolution-ui/implementation-grouping.md`
= Phase 9 = rung 3 of the Linux escalation ladder in `reference/03-platform-ux-flows.md`.

### Request sequence

1. **"what is the next group to implement in conflict resolution? and does it need my windows pc or
   can it run on cloud"** — answered from the plan docs: Group 3 (Phase 9, the D-Bus desktop
   notification), Linux-only, fully buildable and testable in the cloud; only live popup rendering
   needs real hardware.
2. **"lets start implementing Group 3"**.
3. **"can we fix the 7 faliures while we are at it / and tell me how to test manually on a real steam
   deck"** — the pre-existing `run-linux-tests.sh` failures, plus written Deck instructions.
4. **"i want to test using testenv to put a build separate from the real build on the deck"** —
   deploy via `tests/testenv.ps1` into the isolated `~/savelocker-test` tree rather than over the
   installed agent.
5. **"create a new branch called save-conflicts-phase-9 and push. delete
   `claude/conflict-resolution-next-group-c753nh` from origin"**.
6. Live-hardware debugging: the log said the notification was sent, nothing appeared on screen.
7. **"should the notification show up every time I `up` the deck build?"**

### What shipped

- **`src/Agent.Core/CommandPoller.cs`** — an optional `Action<IReadOnlyList<ConflictDto>>?
  onConflictsPolled` delegate, appended last in the constructor and invoked after `RunCommandsAsync()`
  on the existing 20s tick. `CheckConflictsAsync` fetches open conflicts and applies the standard "no
  bystander case" `MachineId` filter before handing them over. Null on Windows, so the tray path is
  bit-for-bit unchanged.
- **`src/Agent.Linux/ConflictNotifier.cs`** (new, 217 lines) — notifies once per conflict id the first
  time it is seen open, withdraws the popup when the conflict closes, and opens Phase 6's agent-ui
  conflicts page via `xdg-open` when the action button is clicked. Guarded by Phase 5's
  `DesktopEnvironment.Detect()`; a missing daemon logs and returns rather than throwing. Nothing in
  the poll loop ever waits on the child process it starts.
- **`src/Agent.Linux/Daemon.cs`** — constructs the notifier with the real bound API port, wires the
  delegate with a `GameId → Name` lookup off `_config.Games`, disposes it in `DisposeAsync`.
- **`tests/linux/run-linux-tests.sh`** — two new sections plus one fix (below): a Phase 9 end-to-end
  section (divergent push from a second config, daemon on :5190, asserts the "no desktop notification
  daemon reachable" log line) and a "notification command's shape" section that puts a fake
  `notify-send` and `gdbus` on `PATH` and asserts the exact argv the daemon emits.

### The bug that mattered: it shipped green and did not work

The first implementation used `gdbus call` — the tooling plan.md's Phase 9 had specified. It built
clean, passed a green suite, survived code review, logged `conflict notification: sent for '...'` with
a real notification id returned by the daemon — and rendered nothing on the Deck.

**Root cause:** a notification carrying **actions** is owned by the bus connection that sent it, and
the notification server closes it when that connection drops, because nobody is left to receive
`ActionInvoked`. `gdbus call` is one-shot by construction — it sends, takes its reply, exits — so it
can never hold a notification open. The `gdbus monitor` the first version used to catch the click was
a *second, different* connection, and could never have owned the notification either.

Diagnosed by a 3-way bisect run on real hardware: (A) the same call with `timeout=0` and no actions →
stays up; (C) with app-name and icon → stays up; (B) with an action button → flashes for under a
second. Same daemon, same bus, so not a Deck quirk.

**Fix:** rewritten around `notify-send --wait --action=view=View conflict`, which keeps its connection
alive for as long as the notification is displayed (fixing the popup *and* making the button work) and
prints the invoked action key straight to stdout, deleting the `gdbus monitor` path entirely. The
ownership rule that caused the bug is now used deliberately: killing the child withdraws a stale popup
when a conflict is resolved elsewhere. `gdbus` remains what Phase 5's `DesktopEnvironment` probe uses
to ask whether a daemon exists at all; that part was never in question.

The reasoning is recorded in a doc comment on `ConflictNotifier` ("Why `notify-send --wait` and not
`gdbus call`, corrected on real hardware 2026-09-02") so the next person reading plan.md's original
tooling decision does not re-derive it, and plan.md itself was corrected.

**The lesson, kept explicitly:** a clean build, a green suite, and a passing code review all held while
the feature was broken, because nothing checked what was actually sent over IPC. The new
"notification command's shape" test closes exactly that gap — and was proved to fail against the
pre-fix code (`--wait` removed, daemon re-run, recorded argv confirmed to lack it, assertion flips)
rather than merely passing against the fixed code.

### The 7 pre-existing test failures: one cause, six cascades

`run-linux-tests.sh` had 7 failures documented in `Backlog.md` as a Decky-plugin-update flake. They
were not a flake. The test simulated an unwritable plugin directory with `chmod 555`, but the harness
runs as root, and root's `CAP_DAC_OVERRIDE` ignores mode bits — so the write the test expected to be
refused *succeeded*, corrupting the plugin version for every downstream assertion. Fixed by switching
to the ext4 immutable attribute (`chattr +i` / `chattr -i`), which blocks new-entry creation even for
root. Verified empirically before changing the test, not assumed.

**244/251 → 251/251**, and 256/256 with the two new Phase 9 sections.

### Verification

- All Linux-buildable projects build clean.
- `tests/linux/run-linux-tests.sh`: **256 passed, 0 failed**.
- Real Steam Deck, desktop mode, via `tests/testenv.ps1 up -Only deck` (isolated `~/savelocker-test`
  + `~/savelocker-test-state`, port 5177, `systemd-run --user --scope`) with a conflict seeded by
  pointing `XDG_DATA_HOME` at a second state root: notification renders, persists, and carries its
  action button — user-confirmed on the shipped build, 2026-09-02.
- Regression test proved failing against the pre-fix code before being kept.

### Gotchas hit

- **`git checkout <file>` destroyed an uncommitted rewrite.** Used to undo a temporary edit; it
  restored the last *committed* version, which was the old gdbus one. Rewritten from the earlier
  content and re-verified.
- **`pkill -f "SaveLocker.Server"` killed its own wrapper shell** (exit 144, no log written) — the
  pattern matched the wrapper's own command line. Fixed with `pkill -f "SaveLocker[.]Server[.]dll"`;
  the bracket makes the regex not match its own literal text.

### Not done

- **`git push origin --delete claude/conflict-resolution-next-group-c753nh` refused with HTTP 403**
  (twice), and no GitHub MCP branch/ref-deletion tool exists. The branch must be deleted from the
  GitHub UI or a machine with full credentials.
- **Game Mode notification rendering is unverified** — only desktop mode was tested. Bringing the
  Deck's Game Mode screen to the foreground on click is deliberately out of scope: that is Phase 8's
  screen, which does not exist yet, the same way Phase 4 left Windows' tray to Phase 7.
- **`xdg-open` landing on the conflicts page on the Deck** is untested.

### Design note recorded at the end

Notification dedup (`_notified`) is in-memory and per-process, so a daemon restart re-notifies for
whatever is still open — and `testenv-deck.sh`'s `cmd_up` restarts the daemon every time, which is why
the popup reappears on every `up`. Deliberate: a persisted "notified once, never again" would mean a
popup dismissed weeks ago and forgotten never gets raised again while the game silently stays
unsynced. Within one daemon run it fires once, not once per tick; it stops entirely once the conflict
is resolved; a still-open popup is withdrawn and replaced rather than stacked. The honest downside is
one notification per restart if the daemon ever crash-loops.

---

## 2026-09-02 — PR #26 xhigh code review: 14 findings, 12 fixed, 1 confirmed on real hardware, 1 design call

**Branch:** `save-conflicts-phase-9`. PR: #26. Review requested via `/code-review xhigh PR#26`.

### Request sequence

1. `/code-review xhigh PR#26` — 9 parallel background review agents ran across differing angles.
2. Findings synthesized and deduplicated into 14 non-overlapping findings, reported via `ReportFindings`.
3. User asked to apply all 14 with minimal edits, treating the finding text as a description of the
   problem, not literal instructions; then re-report via `ReportFindings` with outcome per finding.
4. User offered real-hardware testing help for the 2 skipped findings (#7 and #10).
5. Iterative real-hardware testing on the Deck for finding #7 (`CloseNotification(id)` withdraw path):
   two rounds of test-script refinement (fixing a shell placeholder bug, then a timing artifact),
   resulting in full confirmation.
6. Documentation of the hardware confirmation committed and pushed.

### Findings applied (12 fixed, 1 confirmed on hardware, 1 left as design call)

**Fixed:**

1. **`ConflictNotifier.Notify` — `EnableRaisingEvents` set before `_live` dictionary populated.**
   A fast-exiting `notify-send` could fire the `Exited` event before its own entry existed in `_live`,
   making `Forget` silently skip it. Fixed by committing `_notified.Add` and `_live[id] = live` under
   the lock *before* setting `EnableRaisingEvents`.

2. **`CommandPoller.TickAsync` — fire-and-forget `_ = TickAsync()` discards a still-in-flight tick.**
   `Dispose()` could tear down `ConflictNotifier` while a tick was still running. Fixed with
   `_lastTick = TickAsync()` and a new `StopAsync()` that awaits it; `Daemon.DisposeAsync` calls
   `StopAsync()` before disposing the notifier.

3. **`ConflictNotifier.CheckConflicts` — `_notified.Add` inside the loop skips remaining duplicates.**
   `HashSet.Add` returns false for duplicates and the old code used it as the only filter, so adding
   an id for one conflict silently suppressed any subsequent conflict with the same id in the same tick.
   Fixed by switching to `!_notified.Contains(c.Id)` for the filter, with `_notified.Add` in `Notify`
   only after `Process.Start` succeeds.

4. **`Daemon.DisposeAsync` — `_commandPoller.Dispose()` called before `_conflictNotifier.Dispose()`.**
   A tick still in flight could outlive the notifier. Fixed by the `StopAsync()` addition above —
   `DisposeAsync` now awaits `StopAsync()` before disposing either.

5. **`ConflictNotifier.Forget` — `proc.Dispose()` outside the lock races with `Withdraw`'s `Kill()`.**
   Both `Forget` (from the `Exited` handler) and `Withdraw` could call `Kill()`/`Dispose()` on the
   same `Process` concurrently. Fixed by moving `proc.Dispose()` inside the same `lock(_lock)` block
   that `Withdraw` uses for `Kill()`.

6. **`CommandPoller.TickAsync` — `RunCommandsAsync` and `CheckConflictsAsync` run sequentially.**
   They are independent (one executes dashboard commands, the other reads conflicts). Fixed with
   `Task.WhenAll(RunCommandsAsync(), CheckConflictsAsync())` when `_onConflictsPolled` is not null.

8. **`Daemon.cs` — `_conflictNotifier` constructed with a lambda wrapping a string.**
   `ConflictNotifier`'s `_conflictsUrl` was `Func<string>` but the URL never changes after
   construction. Changed to `string` (finding #14 overlap); constructor simplified.

9. **`ConflictNotifier.Withdraw` — a static method that should be an instance method.**
   Needed instance access to `_lock` and (after finding #7) to `CloseById`. Changed to a private
   instance method taking `LiveNotification` instead of `Process`.

11. **`ConflictNotifier.Notify` — `Log($"...{ex.Message}")` instead of `AgentLogger.LogException`.**
    The catch around `Process.Start` logged the exception message as a plain string, losing the stack
    trace and not matching the codebase's established pattern. Changed to
    `AgentLogger.LogException($"ConflictNotifier.Notify '{gameName}'", ex)`.

12. **`Daemon.cs` — `CommandPoller` hardcodes 20000ms poll interval, untestable.**
    Added `SAVELOCKER_POLL_MS` environment variable hook (parsed in `Program.cs`, threaded through
    `Daemon` to `CommandPoller`), mirroring the existing `SAVELOCKER_CONFIG` convention. Null keeps the
    real 20s default; only test harnesses set it.

13. **`tests/linux/run-linux-tests.sh` — Phase 9 test blocks use inline port-poll loops and `cat` on
    shared logs.** Extracted `wait_for_port()` and `start_fake_unix_socket()` helpers; replaced
    `cat "${log}"` with `tail -60 "${log}"` to avoid cross-section leakage; replaced `sleep 25` with
    `sleep 2` + `SAVELOCKER_POLL_MS=500`.

14. **`ConflictNotifier` — `_conflictsUrl` stored as `Func<string>` instead of `string`.**
    Overlaps with #8. Changed to a plain `string` field.

**Confirmed on real hardware (finding #7):**

- **`ConflictNotifier.Withdraw` — Kill()-based withdraw relies on undocumented daemon behavior.**
  Added a strictly additive `CloseNotification(id)` call via `gdbus` (using the existing
  `ProcessRunner.Run` helper), tried before the existing `Kill()` fallback. Added `--print-id` to
  `notify-send` to capture the notification id; `OutputDataReceived` handler parses numeric ids under
  the lock. User confirmed on real Deck hardware: popup stayed visible for a deliberate 5s pause,
  vanished exactly at the `CloseNotification` call, and `notify-send` exited cleanly (status 0)
  without needing `Kill()`. Recorded in both `CONTEXT.md` and `ConflictNotifier.cs` doc comments.

**Left as a design call (finding #10):**

- **`ConflictNotifier._notified` and `HealthReporter._notifiedConflictEscalations` — similar dedup
  pattern, not unified.** The two sets have deliberately different lifecycle semantics:
  `_notified` clears per-id when a conflict resolves (so a new conflict with a new id always
  re-notifies), while `_notifiedConflictEscalations` is per-tick (reset every health report).
  Forcing a shared abstraction would add indirection without simplifying either call site.
  Left as-is pending user decision.

### Verification

- `dotnet build src/Agent.Linux/SaveLocker.Agent.Linux.csproj --no-incremental -v quiet`: 0 warnings,
  0 errors on every build after each batch of edits.
- `bash -n tests/linux/run-linux-tests.sh`: clean syntax check.
- Real Steam Deck hardware testing for finding #7's `CloseNotification(id)` mechanism.

### Commits on `save-conflicts-phase-9`

- Code-review fixes (12 findings across 5 files)
- `d9947c8` Docs: record real-hardware confirmation of CloseNotification withdraw

### Not done

- Finding #10 (dedup-set unification) left as an open question to the user — not yet answered.

---

## 2026-09-03 — Phase 8 (Game Mode Conflicts screen) shipped, verified live under WSLg; Cloud icon fixed on user feedback

**Branch:** `claude/save-conflict-next-group-ebc9b1`.

### Request sequence

1. Session opened after a usage-limit reset with "continue from where you left off," carrying no
   uncommitted work — the actual task was derived by following `CLAUDE.md`'s mandatory session-start
   read (`CONTEXT.md`, `REPO_MAP.md`) plus `tasks/conflict-resolution-ui/implementation-grouping.md`:
   **Group 4 / Phase 8**, the native Linux Game Mode conflict screen — "the direct, literal answer to
   'a conflict popup like the Decky one, but in the native Linux/Wayland UI, with no Decky installed.'"
   The grouping doc scoped this as code-only, verification deferred to "a WSLg or real-Deck pass."
2. After screenshots of the finished feature were sent, the user replied: **"the icons here looks off
   / please fix them"** — pointing at the hand-drawn `Cloud` icon, which rendered as an unrecognizable
   double-blob shape rather than a cloud.

### What was found

This session runs on the maintainer's actual Windows dev box with a working WSL `Ubuntu` distro (a
native `~/.dotnet/dotnet`, a real WSLg display) — the exact capability
`implementation-grouping.md` had assumed this environment lacked for Group 4. That let the whole
feature be verified live with real screenshots rather than left as a code-only, build-checked deferral.

### What was built (Phase 8)

- `src/Agent.Linux/Ui/UiApp.cs`: new `Screen.Conflicts` screen, reusing the same in-process
  `ApiClient` pattern `AgentCli.cs` already uses and the same fire-and-forget-`Task`-field draining
  convention as `_scanTask`/`_enrollTask`/`_syncNowTask` (`_conflictsTask`, `_versionTasks`,
  `_versionStatsTasks`, `_resolveTask`, all mutated only on the render thread). `PollConflictState()`
  runs once per frame; `DrawConflicts()`/`DrawConflictCard()`/`DrawConflictSide()` render a card per
  open conflict with per-side stats (file count / newest-mtime, reusing the existing
  `VersionStatsDto` endpoint) and Keep-local / Keep-cloud buttons, visually and interactionally
  matching the already-shipped React `ConflictCard`/`ConflictsView`. A `"Conflicts (N)"` badge entry
  was added to the rail, and `DrawStatus()`'s tracked-games loop gained a conflict-priority badge.
- `src/Agent.Linux/Ui/Icons.cs`: added `Cloud` and `GitBranch` glyphs (`GitBranch` correct on the
  first pass, matching lucide's git-branch exactly).
- `src/Agent.Linux/Ui/Theme.cs`: added `AccentRed`, matching the dashboard/agent-ui conflict card's
  escalated-border red, reserved for the overdue line only.
- A real, previously-latent bug fixed in the `--screenshot` dev tool: the `busy` gate in `OnRender`
  didn't wait on `_resolveTask`, so a scripted `--nav` press of a Keep button could have its in-flight
  `ResolveConflictAsync` HTTP call killed by `Environment.Exit` before it reached the server. Fixed by
  adding `_resolveTask is { IsCompleted: false }` to the gate — deliberately *not* adding the ambient
  `_conflictsTask`/version-fetch polls, which would slow every screenshot in the app for no reason.

### The Cloud icon bug (user-caught), and a second miss on the first fix

The first `Cloud` glyph was two `PathArcTo` arc bumps plus a `PathLineTo` base — it rendered as an
unrecognizable "double speech-bubble blob" at every size, confirmed by the user's screenshots and by
adding it to `Gallery.cs`'s icon strip and screenshotting `--gallery` at multiple sizes. The first
replacement (a hand-guessed 15-point closed polygon) was a real improvement but still eyeballed, not
derived — the user caught it a second time ("the cloud icon still looks wierd on the right side"),
pointing at a visible concave dent on the right lobe where the hand-picked points bent inward instead
of bulging out.

**The actual fix** solved lucide's real `cloud` SVG path
(`M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z`) by hand, rather than guessing points from
looking at the rendered shape: applied the SVG elliptical-arc-to-center formula to both arcs (a
radius-7 lobe centred at (9,12) sweeping 253° for the main body, a radius-4.5 lobe centred at
(17.5,14.5) sweeping a clean 180° for the right bump), then sampled each arc at even parametric steps
to build a 13-point closed polygon that traces the *actual* curve instead of an approximation of it.
This is the same curve-to-polyline tradeoff `Folder`/`Shield` already make, just derived exactly
instead of by eye — worth remembering as the right approach for any future hand-drawn icon that keeps
missing on look-and-adjust alone. Verified via an 8×-upscaled crop of the gallery screenshot (a smooth,
undented cloud silhouette) and in both real product screens.

**A second, unrelated bug** in the same feedback: "the alignment of the this device icon and cloud
icon in conflict screen is off from the text." Root cause was in `DrawConflictSide` (`UiApp.cs`), not
the icon geometry: `ImGui.AlignTextToFramePadding()` was called before the label text even though the
preceding icon `Dummy` is exactly one text-line tall — that call exists to align plain text against a
*taller*, frame-padded sibling on the same line (see `Toggle`/`HintLabel`, where the preceding widget
genuinely is taller), and calling it here just pushed the label down by `FramePadding.y` for no reason,
visibly separating it from the icon. Fixed by removing the stray call; confirmed pixel-level in a
zoomed crop of a real conflict card that the icon and label now share a baseline.

`Gallery.cs` was trimmed back afterward to just the two new icon-strip entries, matching the file's
existing minimal-ramp convention (no leftover debug code from either diagnosis pass).

### Side quest: `agent-ui` build breakage from mixed WSL/Windows npm

`dotnet build SaveLocker.sln` failed twice with `'tsc' is not recognized` — both times because running
WSL's native npm (via `tests/seed-test-conflict.sh`) against `agent-ui/` on the shared `/mnt/d/...`
Windows path had reinstalled `node_modules` with Linux-native optional bindings, which break the
Windows-side `tsc` binary shim. A first `rm -rf node_modules package-lock.json && npm install` fixed
the build but rewrote `package-lock.json` with a 640-line unwanted diff; corrected via
`git checkout -- agent-ui/package-lock.json` followed by `rm -rf node_modules && npm ci` (respects the
existing lockfile exactly, no diff). This is a distinct gotcha from the vault's existing "no
`node_modules` at all in a fresh worktree" note — worth remembering as its own case.

### Verification

- Full Phase 8 flow exercised live under WSLg against a genuinely seeded two-machine conflict on a
  real scratch server: `--gallery` and in-context screenshots (`conflicts.png`, `conflicts-empty.png`,
  `status.png`), plus a scripted `--nav` resolve confirmed via an independent CLI check that the
  conflict was actually closed server-side.
- The arc-derived Cloud geometry and the alignment fix were each re-verified the same way, live: an
  8×-upscaled crop of the `--gallery` icon strip for the shape, and an upscaled crop of a real,
  freshly-seeded conflict card's "This device"/"The cloud" rows for the baseline alignment.
- `dotnet build` clean (Windows `Agent.Linux` project, full `SaveLocker.sln`, and the linux-x64 UI
  binary) — 0 errors on every build, including after the second reseed re-triggered the WSL/Windows
  `node_modules` gotcha a second time (same fix: revert `package-lock.json`, `npm ci` from Windows).
- `git status --short` clean; `agent-ui/package-lock.json` confirmed unchanged from `main` both times.

### Commits

- `9af2a0c` Phase 8 code (Game Mode Conflicts screen)
- `d13a954` Phase 8 docs (`CONTEXT.md`, `Backlog.md`, `implementation-grouping.md` marked done)
- `141d747` Fix the Cloud icon on the Game Mode conflicts screen (superseded by the arc-derived fix
  above, not yet committed as of this write-up)
- `ee7cb3d` Docs: correct the Cloud icon write-up after the maintainer's fix

### Not done

- Group 5 (Phase 7 Windows tray wiring + Phase 14 webhook/block-launch opt-in), the next item in
  `implementation-grouping.md`, was only mentioned as available future work — not started, not
  requested.
- A `--nav`-script timing quirk was found (a 2-step nav script can miss the Keep button because early
  frames land focus before the async version/stats fetch completes) and deliberately left unfixed —
  it's a scripted-test artifact far inside normal human reaction time, not a functional bug affecting
  real gamepad use.

---

## 2026-09-03 (cont'd) — Conflict resolution Phase 7 shipped (Windows tray auto-chooser + bulk queue), PR #30

**Branch:** `save-conflicts-phase-7` (renamed from `claude/save-conflict-next-group-e7bdca`). PR:
https://github.com/Marwanello/SaveLocker/pull/30.

### Request sequence

1. "Implement the next group in the save conflict please. Also add on the start of the phases doc and
   the grouping doc check markers with the completed groups and phases." Group 5 bundled Phase 7
   (Windows tray automatic chooser + bulk queue) and Phase 14 (webhook notify + block-launch setting),
   deferred pending a Windows-connected session — this one. Asked which to do; user chose **Phase 7
   only**, explicitly excluding Phase 14.
2. Manual-verification questions: how to test via `tests/testenv.ps1`, how to reach it from outside the
   local network, and what the test was actually proving.
3. Live terminal output pasted showing seeding failures on both WSL (garbled output from an
   over-quoted one-liner) and Windows (`push` REFUSED after a successful-looking `add-game`).
4. User's own fix, given as explicit feedback: reversing the provided command order (build, up, then
   WSL seeding commands, then Windows seeding commands) made the conflict popup actually appear —
   asked for a corrected, complete test scenario built around that ordering, specifically targeting
   "apply to all remaining."
5. "All verfied. Is this pushed?" — followed by "Do 2 then psuh and create PR": rename the branch to
   the `save-conflicts-phase-N` convention used by prior phases, push to `origin`, open a PR.

### What was built

- **`src/Agent/TrayApp.cs`**: `CheckConflictsAndRaiseAsync()` — after `Sync All`, `Force Pull`, or
  `Force Push` in the tray's context menu, checks `GetOpenConflictsForMachineAsync` and, if any exist,
  raises the agent window straight to a `conflicts:queue` pop-up instead of leaving the user to notice
  on their own.
- **`agent-ui/src/App.tsx`**: parses a `conflicts:queue` hash deep link on startup and auto-populates
  the sync queue from open conflicts, one-shot via a new `autoQueueRequested` state.
- **`agent-ui/src/components/SyncConflictModal.tsx`**: after the first resolve in a multi-conflict
  queue, a new `ApplyToAllPrompt` component offers to apply the same choice (cloud/local, keep-both) to
  every remaining conflict in one batch (`applyToAllRemaining()`), or continue reviewing each
  individually (`reviewEach()`).

### Bug found and fixed mid-verification: cross-process config lost-update race

Windows `push` failed with `REFUSED push: that is not a usable absolute folder path. (mapped to '')`
immediately after a successful-looking `add-game`. Root-caused by reading
`%LOCALAPPDATA%\SaveLocker-test\SaveLocker\config.json` directly (`"Games": []` despite the CLI's
success output) and cross-checking with `tasklist`/`netstat`: a live WinTest tray process from an
earlier `testenv.ps1 up` (`dotnet.exe` PID 33172, port 5188) held its own in-memory `AgentConfig`, and
that process's own next unrelated `_config.Save()` call overwrote the CLI's out-of-band config edit
with its stale in-memory `Games: []` — a cross-process lost-update race. This was a flaw in the manual
test instructions given (seeding against a config file a live tray already held open), not a defect in
the shipped feature. Fixed by killing the stale tray process, then re-seeding with nothing else
touching the file.

**User's own correction, adopted as the standing test-ordering rule for this feature:** do ALL CLI
seeding (WSL then Windows) *before* starting the Windows tray at all — start the tray last
(`up -Only windows` as the final step), not first, so no live process races the CLI's seed writes.

### Verification

Manually verified live via a hand-built two-machine `testenv.ps1` scenario: console + Linux agent
started and seeded first, both machines seeded with two conflicting games ("Bulk Test A", "Bulk Test
B"), Windows tray started last. Confirmed both the automatic-chooser raise-on-sync behavior and the
"apply to all remaining" bulk-resolve queue worked as designed. User confirmed "All verfied."

### Landed

- Local branch renamed `claude/save-conflict-next-group-e7bdca` → `save-conflicts-phase-7` (matching
  the `save-conflicts-phase-N` convention from PRs #20/#22/#23/#24), pushed to `origin`
  (`Marwanello/SaveLocker`) with upstream tracking set.
- PR [**#30**](https://github.com/Marwanello/SaveLocker/pull/30) opened, `save-conflicts-phase-7` →
  `main`.
- Vault: `tasks/conflict-resolution-ui/plan.md` and `implementation-grouping.md` both gained a "Status
  (updated 2026-09-03)" table with ✅/⬜ markers for every phase/group (the check-marker request from
  the original ask), plus a "Shipped 2026-09-03" write-up under Phase 7 and an updated Group 5 section.
  `Backlog.md` and `CONTEXT.md` updated with the same narrative.

### Not done

- Phase 14 (webhook notify + block-launch setting) — explicitly excluded from this session's scope by
  the user's choice; still queued as the remainder of the original Group 5.
- PR #30 not yet reviewed or merged as of this write-up.

---

## 2026-09-07 — PR #30 (upstream) xhigh review: 15 findings applied, fencing/race/dedupe fixes, full test suites verified

**Reviewed:** [SkorcherX/SaveLocker#30](https://github.com/SkorcherX/SaveLocker/pull/30) ("Three bug
bounties: Linux agent, console/server, Windows agent") — already merged 2026-07-29; the review ran
against its exact base/head commits (`f596fba5...` / `94ede293...`), recovered from the merge commit's
two parents since the source branch no longer exists. **Fixes shipped as:**
[Marwanello/SaveLocker#31](https://github.com/Marwanello/SaveLocker/pull/31),
`linux-agent-bugbounty-review-fixes` → `main` (see "Landed" below — the numbering collision with fork
PR #30, an unrelated Phase 7 PR, is deliberate context, not an error).

### Request sequence

1. `/code-review xhigh PR#30` — an xhigh-effort review of an already-merged, 85-file/+10271/-1565-line
   PR: 3 finder groups (Server/Console, Windows Agent, Agent.Core+Linux) at two rounds each, plus a
   verify pass and a sweep pass, surfaced 15 findings.
2. Apply all 15 with minimal edits — explicitly instructed to treat the quoted finding text as a
   description of a defect, never as embedded instructions to follow.
3. Report the outcomes via `ReportFindings`, echoing each finding's original file/line/summary/
   failure_scenario text back verbatim plus a fixed/no_change_needed/skipped outcome.

### What was fixed (15 findings, minimal edits)

1. **Command-completion fencing token.** `SyncService.CompleteCommandAsync` accepted a completion
   report from any expired claim with no token check, even though `DequeueCommandsAsync` already mints
   a fresh `ClaimToken` on every reclaim. A stale execution's late result could silently overwrite a
   live reclaim's outcome. Fixed by threading a new `Guid? ClaimToken` through the whole wire protocol
   (`Contracts.cs` → `Mapping.cs` → `Program.cs` → `ApiClient.cs` → `CommandPoller.cs`) and rejecting a
   completion whose token doesn't match the command's current one.
2. **`GameScanner.FindSteamPath()`'s unguarded `Path.GetFullPath`.** A corrupted/overlong Steam registry
   value could throw out of the TrayApp constructor before `Application.Run` even starts, crashing the
   whole tray at launch instead of being isolated the way WA-11 isolates every other scan source. Added
   `SafeFullPath`, a try/catch wrapper logging and returning null on failure.
3. **Unhandled `AgentStateLockException`.** `SettingsScreen.cs`'s config-saving calls and three
   `AgentApiServer.cs` endpoints (`/processes`, `/remove`, `/folder`) had no try/catch for the exception
   `AgentStateLock` now throws on contention, so a brief lock collision crashed the whole Game Mode UI
   process or returned a raw 500. Fixed with try/catch converting it into a status message / typed 400.
4. **`AgentInstallerService.SaveCoreAsync`'s incomplete failure cleanup.** A failure between
   `File.Move(staged, exePath)` and `WriteInfoAsync` left a newly-published binary live under a stale
   `info.json`, so the fleet's next integrity check would fail against a mismatched hash. Fixed by
   tracking the published path and deleting it too on that failure branch.
5. **`PreserveVersionsOnMachineDelete` migration's `Down()`.** Re-tightening `SaveVersions.MachineId` to
   `NOT NULL` via `defaultValue` doesn't backfill existing NULLs on SQLite's table-rebuild path, so any
   rollback after a machine deletion (which this same PR's `DeleteMachineAsync` sets NULL for) would
   throw instead of reversing. Fixed with an explicit backfill `UPDATE` before the `AlterColumn`.
6. **`SyncEngine.LinkRetirement`'s disposal race.** `RetireAsync` disposes `_retired` only after
   awaiting per-lease network releases, well after the "retired" flag is set — so a concurrent
   push/pull/launch call could pass the `IsRetired` check and then throw an unguarded
   `ObjectDisposedException` reading `_retired.Token`. Fixed by catching that exception and returning an
   already-cancelled linked source instead.
7. **`ServerOrigin.CanonicalUrl` validated but didn't normalize.** It used `Normalize()` only as a
   validity gate, then returned the original, un-normalized string — so a URL typed with a path or mixed
   case was accepted but persisted with the path still attached, silently mangling every later relative
   request built against `HttpClient.BaseAddress`. Fixed to return `Normalize()`'s own result.
8. **`PublicUrl.IsUsableAbsolute` silently truncated instead of refusing.** Its doc comment promised a
   URL with a path is refused, but the implementation only checked scheme/host and then let
   `GetLeftPart(UriPartial.Authority)` silently drop the path — breaking every enrollment/agent-latest
   URL generated behind a reverse-proxy sub-path with no error surfaced. Fixed to actually refuse a
   path/query/fragment.
9. **`GameScanner.ScanAsync`'s dedupe reintroduced a bug WA-08 had already fixed.** The cross-source
   dedupe compared only `SuggestedSaveDir`, discarding the `SuggestedProcessName` field WA-08 added for
   any game discoverable through more than one source (e.g. both a Steam shortcut and a save-root scan).
   Fixed with a `MergeDuplicates` helper that keeps the save-dir winner but preserves whichever candidate
   in the group actually has a process name.
10. **`TrayApp.RebuildEngine`'s unsynchronized engine swap.** Reachable concurrently from `/api/config`
    and `/api/register` on separate Kestrel threads, the capture-old/assign-new/retire-old sequence could
    race and silently drop one built engine without ever retiring it, leaking its lease-renewal timer
    against the old server forever. Fixed with a new `_engineLock` around the capture/assign step.
11. **`/api/config`'s connection-changed check over-fired on a cosmetic rename.** Keyed on the
    `(ServerUrl, MachineName)` tuple, so renaming a machine while it held a sync lease retired the whole
    engine and released every lease, mid-session, for a purely cosmetic edit. Narrowed to `ServerUrl`
    alone.
12. **`Enroller.EnrollAsync`'s uncaught `SetTracked` exception.** `SetTracked` can now throw
    `AgentStateLockException` on lock contention (this same PR's own WA-07 fail-closed change), but
    `EnrollAsync` called it with no try/catch — a brief contention mid-batch aborted the whole enrollment
    loop, permanently orphaning already-created server-side games for every remaining candidate. Fixed
    with try/catch + skip-and-continue per candidate.
13. **`UpdateChecker.DownloadInstallerAsync`'s same-origin check used the wrong config value.** It read
    the live, mutable `_config.ServerUrl` instead of the `ServerUrl` property this same PR had added
    specifically to be captured at construction and stay fixed across a later connection change — so a
    server-URL change mid-download could wrongly reject a same-server download as foreign. Fixed to use
    the captured property.
14. **`run-winagent-tests.ps1`'s WA-06 section never exercised the actual regression surface.** It drove
    `savelocker run` (a single `SyncEngine`, disposed via ordinary process exit) instead of the real
    Windows tray, so a reverted `TrayApp.RebuildEngine` fix would still pass it. Added a new "WA-06
    (tray)" block driving the real tray process and a real `/api/config` POST against two fake HTTP
    origins, proving the *old* engine's lease renewer genuinely stops after a live server-URL swap.
15. **`run-concurrency-tests.ps1` had zero coverage of `AgentStateLock`'s fail-closed rewrite.** The
    file's own header claims ownership of cross-process state safety, but grepping every test file for
    `AgentStateLock` returned nothing. Added a new "AgentStateLock fails closed" section (3 checks)
    holding the same lock file directly via a `System.IO.FileStream` from PowerShell to simulate a
    genuine external holder.

### A bug in this session's own new test, caught by its first run

The new WA-06 (tray) test failed 2 of its 5 new checks the first time the full suite ran ("a lease was
acquired against origin A" / "the lease was renewed against origin A"). Root cause: `TrayApp.cs` starts
`ProcessWatcher` *before* the tray's local API even begins listening, and the watcher's first poll only
baselines what's already running — by design, it never fires `GameLaunched` on that first tick, so
starting the Agent while a game is already open doesn't look like a fresh launch (`Watchers.cs`). The
test started its fake game the instant `/api/state` answered, which could race ahead of that first
baseline poll and make the launch permanently invisible to the watcher, not merely late. Fixed with a
`Start-Sleep -Seconds 5` after confirming the tray is up and before starting the fake game — comfortably
past the 4-second default poll interval, so the baseline tick is guaranteed to have already landed.

### Verification

- `run-concurrency-tests.ps1`: 26/26 passing (23 pre-existing + 3 new AgentStateLock checks), no
  regressions.
- `run-winagent-tests.ps1`: 119/119 passing after the WA-06 (tray) race fix (117/2 on the first run) —
  the full 12-section suite, including real tray/daemon processes, with no regressions traced to any of
  the 13 production-code fixes.
- All 15 findings reported via `ReportFindings`, `outcome: fixed` on each.

### Landed

- Committed as `97c41b2` (the 15 fixes) and `69cc838` (this vault write-up) on branch
  `linux-agent-bugbounty-review-fixes` (renamed from `claude/pr30-code-review-9dad6f`), pushed to
  `origin` (`Marwanello/SaveLocker`).
- PR [**#31**](https://github.com/Marwanello/SaveLocker/pull/31) opened against `main` — a fresh PR,
  since upstream PR #30 is merged with its branch deleted, and fork PR #30 is the unrelated
  Phase-7 work above. Referenced upstream `SkorcherX/SaveLocker#30` as the source of the findings.

---

## 2026-09-07 — Decky chip name mismatch fixed in the test rig

**Worktree/branch:** `save-conflict-next-group-ebc9b1` (main-repo worktree), continuing the prior
Phase 10 hardware-verification session.

### Request

User reported that opening "Conflict Game" on their Deck showed no sync-status chip on the library
tile, and asked whether that was because the CLI-tracked game was named `ConflictTest` while the
Steam shortcut it created was named "Conflict Game" — and, if so, to make both names match.

### Investigation

Read `gamingSync.tsx`'s `resolveMatchSync` in the Decky plugin (`SaveLocker-Decky` repo) to check the
hypothesis against the real matching code rather than assuming it. Confirmed there are two match
paths: a primary match by Steam AppID (via a warm `rows()`/`games()` cache), and a **name-based
fallback** — comparing Steam's own displayed name for the launched app against the tracked game's
name — reached only when the primary AppID lookup finds no row at all. A name mismatch breaks that
fallback outright, and since SaveLocker's own CRC32-based Steam AppID algorithm was reverse-engineered
from third-party tools (never officially documented by Valve — see the
`reverse-engineer-formats-from-real-tools` memory), whether Steam's live AppID for a shortcut always
matches what SaveLocker computed and wrote into `shortcuts.vdf` is a real, still-unverified question.
If it ever diverges, the name-based fallback is the only thing standing between the mismatch and a
missing chip — making the rename worth doing regardless of that open question.

### Fix

Renamed the CLI-tracked game from `ConflictTest` to "Conflict Game" everywhere it's created, so it
matches the Steam shortcut's display name exactly:

- `tests/testenv.ps1` — `New-ConflictOnWindows` (doc comment, `Say` message, `add-game --name`, final
  status message).
- `tests/testenv-deck.sh` — `cmd_conflict()` (doc comment, echo messages, `add-game --name`/`push`
  calls); also simplified a now-redundant echo message and replaced a stale comment with one
  explaining why the names must match (referencing `resolveMatchSync`'s fallback path).

**Self-caught regression:** the blind rename turned a one-word bareword CLI argument into a two-word
one in both scripts' `push` calls (`push ConflictTest` → `push Conflict Game`, unquoted) — both
PowerShell and bash word-split an unquoted multi-word argument into two positional arguments, which
would have broken `push` (it expects exactly one game-name argument). Caught by re-reading both files
immediately after the replace, before running anything; fixed by quoting (`'Conflict Game'` in
PowerShell, `"Conflict Game"` in bash).

`src/Agent.Linux/DevSteamShortcut.cs` needed no change — it already only referenced the fixed
Steam-side name via its `ShortcutName` constant, never `ConflictTest`. A repo-wide search confirmed no
other file (source or vault docs) still referenced the old name.

### Verification

- PowerShell parser (`[System.Management.Automation.Language.Parser]::ParseFile`) — no errors on
  `testenv.ps1`.
- `bash -n tests/testenv-deck.sh` — clean.
- No live-hardware re-test performed this session; the user was told to rebuild/reseed and check the
  Deck.

### Not done

- Live re-verification on the Deck (rebuild → `up` → `conflict` → restart Steam → check the "Conflict
  Game" tile for the chip) — the user was handed this as the next step, not yet confirmed.
- The open question about whether Steam's live shortcut AppID can diverge from SaveLocker's computed
  one remains unresolved; the rename is a safety net for it, not a resolution of it.
- No commit made yet for this fix.

---

## 2026-09-07 (cont'd) — Alias-edit fix, chip/button root cause, Phase 11 shipped, testenv `conflict` fix

**Branch:** `claude/savelocker-decky-worktree-d43a9e` (SaveLocker) / `decky-conflict-resolution-ui`
(SaveLocker-Decky). Continues directly from the ConflictTest → Conflict Game rename above, same day.

### Request sequence

1. "also changing a game alias doesn't work in the decky plugin. please fix it."
2. "please commit changes / also after your eddit the status chip and sunc button still doen'ty show
   up in Conflict game. why is that?"
3. "manual verification complete and everything runs as expected / lets start phase 11 / after
   implmenting tell me how to verfy it on real hardware using testenv"
4. "what is phase 12 and when will it be implmented?" / "is it easy to implement?" (Q&A only, no code)
5. `.\tests\testenv.ps1 conflict` failed on a fresh run — Windows side hit a raw
   `HttpRequestException` (console not up), Deck side reported "not installed". "please fix this."
6. "please commit in both savaelocker and savelo0cker deck repo branhces"

### Root cause: `gameId`/`saveDirectory` wire-field mismatch

`TrackedGameDto`'s JSON wire fields are frozen as `id`/`path` for back-compat (per its own C# doc
comment), but every TS consumer in the Decky plugin (`GamingSyncGame`, `fullPage.tsx`'s game list,
etc.) expected `gameId`/`saveDirectory`. This made `game.gameId` `undefined` everywhere `/api/games`
data was consumed, with two separate symptoms:

- **Alias editor:** `setAlias(game.gameId, next)` sent `undefined` → the URL literal `"None"` →
  404 against `/api/games/None/alias`. The UI only acts `if (r.ok)`, so the edit box just silently
  stayed open with no error.
- **Chip/Pull/Push/Sync buttons never appearing at all** (discovered while explaining bug #1's blast
  radius): the same bug broke `resolveMatchSync`'s *primary* Steam-AppID match path too, not just its
  name-based fallback — `syncCache.games.find((g) => g.gameId === row.gameId)` always returned
  `undefined` since `g.gameId` was undefined for every cached game. `OverlayHost` therefore never got
  a match and `OverlayButtons` never mounted, for any game, regardless of the earlier rename.

**Fix:** one `_normalize_game()` helper added in `SaveLocker-Decky/main.py`, applied inside `games()`
— a single choke point since both `fullPage.tsx` and `gamingSync.tsx` call the same Python method
name. Chosen over patching every TS call site since the C# DTO's wire names are a deliberate,
documented constraint that can't change. User confirmed via manual hardware verification: "manual
verification complete and everything runs as expected."

### Phase 11 shipped: launch-gate wiring (cancel → popup → sync → relaunch)

Server/agent side (`SyncEngine.cs`, `AgentApiServer.cs`, `Daemon.cs`, `TrayApp.cs`): exposed the
pre-existing (Phase 4) `SyncEngine.PrepareLaunchAsync` over a new
`POST /api/games/{id}/pre-launch-sync` route returning `LaunchGateResult` (now serializes
`LaunchDecision` as a string via `JsonStringEnumConverter`, matching `Contracts.cs`'s existing
pattern).

Decky plugin side (`main.py`, `gamingSync.tsx`, `conflicts.tsx`, `index.tsx`): new `pre_launch_sync`
Python proxy; `handleGameActionStart`'s cancel-and-relaunch path and `handleLifetimeChange`'s
`bRunning` fallback both now call it instead of always relaunching blind after a plain pull. A
`Blocked` decision opens the conflict-resolve modal and relaunches only once it closes (fail-open on
any transport error — the plugin's stated philosophy is "delaying a launch is acceptable, silently
stranding one is not"). Solved a `conflicts.tsx` ⇄ `gamingSync.tsx` circular-import constraint with a
`ConflictHooks`/`setConflictHooks` registration pattern wired once from `index.tsx`'s
`definePlugin`, mirroring the codebase's existing `syncStatus.tsx` leaf-module convention.
`handleGameActionStart` deliberately does **not** apply the same gate to `handleLifetimeChange`'s
fallback beyond reporting via the chip — that path only fires after the process is already running,
so it can't meaningfully cancel or gate anything.

**Verification performed:** C# solution build clean, `tsc --noEmit` clean, Decky rollup build clean,
existing 30-check local-API regression suite 30/30, a standalone smoke test against the new route
(404 for an unknown game id; clean 500/`ErrorResponse` for a known game against an unreachable fake
server, confirming the plugin's `!r.ok` fail-open path). **Not done:** real Deck/Steam+Decky hardware
verification of the actual cancel→popup→sync→relaunch sequence — handed to the user via testenv
instructions, not fabricated.

### `testenv.ps1 conflict` fix

A fresh run of `.\tests\testenv.ps1 conflict` failed: the Windows leg threw a raw
`HttpRequestException` stack trace (connection refused on :5080) because the console container
wasn't running yet, and the command never started it — it assumed `up`/`up -Only console` had already
been run. (The Deck leg's "not installed" message was separate and correct — that Deck genuinely
hadn't had `build -Only deck`/`up -Only deck` run yet.)

**Fix:** `tests/testenv.ps1`'s `conflict` case now checks `Test-ConsoleUp` and calls `Start-Console`
first if it isn't already up (idempotent — skipped when already running), before seeding either side.
Starting the console here races nothing, unlike the tray/daemon-vs-CLI-seeding race the existing
ordering rule protects against, since the console holds no local agent config. Doc header updated to
describe the new auto-start. Verified via
`[System.Management.Automation.Language.Parser]::ParseFile` — no syntax errors.

### Commits

**SaveLocker** (`claude/savelocker-decky-worktree-d43a9e`), 3 commits, none pushed:
- `b5988e8` — Add `/api/games/{id}/pre-launch-sync` route for Decky launch-gate wiring (Phase 11)
- `24071fc` — Add fake-game Steam shortcut test rig; rename ConflictTest to Conflict Game (bundles the
  earlier rename-fix session's changes, which had not yet been committed)
- `df7a923` — Docs: record Phase 11 shipped and hardware-verified Phase 10 fixes

**SaveLocker-Decky** (`decky-conflict-resolution-ui`), 1 commit, not pushed:
- `617f9b3` — Wire pre-launch sync gate into the launch-intercept path (Phase 11)

The alias-fix commit (`b8af1c6`, `_normalize_game()` in `main.py`) was made in the prior segment of
this same session, before this entry's fixes.

### Not done

- Real-hardware verification of Phase 11's cancel→popup→sync→relaunch sequence on an actual Deck.
- Neither branch pushed to a remote.
- Phase 12 (`sync-status` endpoint consumer work) not started — the endpoint and DTO already exist
  from Phase 0/1; the open decision is picking a genuine one-shot trigger moment (not a timer-polled
  badge, per the plan's own 2026-08-30 correction). Phase 11's new `pre_launch_sync` `Blocked` result
  is a plausible natural home for it, though this isn't a decided plan of record.
- Phases 13 (Playnite, separate project) and 14 (blocked on a maintainer decision) remain queued.

---

## 2026-09-07/08 — Real-hardware fix: Play Anyway/pause never engaged for any game; testenv `-Size`/`-Files`

**Repos:** `SaveLocker-Decky` (`decky-conflict-resolution-ui`) and `SaveLocker`
(`claude/savelocker-decky-worktree-d43a9e`). Both uncommitted as of this entry.

### Request sequence

1. Reported that after correctly building against the right Decky worktree, real-Deck testing showed
   no pause and no "Play Anyway" popup at all — a regression from the previously-verified Phase 10/11
   behavior.
2. Asked explicitly whether the bug was specific to the "Conflict Game" fake-game test rig or could hit
   any game — a scope question, not just "fix it."
3. Live iteration using a CDP console-tailing tool built mid-session (`tail-deck-console.mjs`): pasted
   raw hardware console logs twice, each preceding a targeted fix.
4. After the first fix, reported a new symptom ("now the game doesn't open and is cancelled but the
   popup doesn't show up") — a second root-cause pass.
5. Confirmed "it works perfectly now," then asked for a new, unrelated feature: `-Size <MB>`/
   `-Files <count>` options on `testenv.ps1 conflict` to seed realistically-sized conflicting saves
   instead of one hardcoded line of text.

### Root cause #1: `appId` from `RegisterForGameActionStart` is a packed 64-bit `CGameID`, not a plain AppID

For a non-Steam Steam shortcut, the callback's `appId` string is Steam's packed 64-bit `CGameID`: the
real 32-bit AppID sits in the **upper** 32 bits, with a `0x02000000` shortcut-type marker in the lower
32 bits. Proven from real hardware, not guessed: Steam's own debug log showed `13278285201168924672`
for a launch whose real AppID was `3091591690` (`0xb845f20a`), and
`BigInt(13278285201168924672n) >> 32n` yields exactly `3091591690`. The old code did
`Number(appIdStr)`, which silently rounds a value this large to a garbage float — so the fast-path
appId match failed for **every** non-Steam-shortcut game, not just the fake-game rig. Fixed in
`src/gamingSync.tsx` with a `parseGameActionAppId()` helper that BigInt-shifts values above
`0xFFFFFFFF` before converting to `Number`; the raw `appIdStr` is still passed unchanged to
`SteamClient.Apps.RunGame` in `relaunch()`, which needs the original packed value.

### Root cause #2: `SteamClient.Apps.GetActiveGameActions()` is unreliable in both directions

After fix #1, every hardware attempt logged "cancel lost the race" even when the user directly observed
the cancel actually working (game did not open). `GetActiveGameActions()` proved unreliable both ways
on real hardware — an empty result doesn't prove a cancel succeeded (the action can simply progress past
cancellable state), and a non-empty result doesn't prove it failed (Steam can be slow to prune a
cancelled action from the list). Fixed by removing this verification entirely from both call sites in
`handleGameActionStart` (the `knownConflict` branch and the generic `interceptedLaunches` path); both
now just call `CancelGameAction` inside `try {} catch {}` and proceed unconditionally, relying solely on
the pre-existing `pendingBlock`/`bRunning` safety net in `handleLifetimeChange` as the real authority.

### Debug tooling built (not part of any repo)

`tail-deck-console.mjs` — a ~70-line, dependency-free Node script (native `fetch`/`WebSocket`) that
tails a Decky plugin's frontend console live. Decky frontends run inside Steam's `SharedJSContext` CEF
process, which has no on-disk log file; the script lists CDP targets at `http://localhost:8080/json`
(reached via `ssh -N -L 8080:127.0.0.1:8080 deck@<ip>`), connects to the matching target's
`webSocketDebuggerUrl`, and prints every `Runtime.consoleAPICalled`/`Runtime.exceptionThrown` event.
Kept at the SaveLocker worktree root for reuse in future hardware-debugging sessions.

### `testenv.ps1 conflict -Size <MB> -Files <count>`

Both defaulted (`Size=0`, `Files=1`) so omitting them preserves the old single-tiny-file behavior
exactly. When given, both platforms split the requested total size across that many randomly-filled
files (random, not zero-filled, so the two sides genuinely differ even at identical size/count):

- **Windows** (`tests/testenv.ps1`): new `New-SyntheticSaveFiles` function, called from
  `New-ConflictOnWindows`; uses `[System.Random]::NextBytes` + `[IO.File]::WriteAllBytes`.
- **Deck** (`tests/testenv-deck.sh`): mirrors the same split using `awk` (float-MB → integer bytes,
  since POSIX shell arithmetic has no floats) and `head -c <bytes> /dev/urandom`.
- Forwarded to the Deck side via two new `SAVELOCKER_CONFLICT_SIZE_MB`/`SAVELOCKER_CONFLICT_FILES` env
  vars in `Invoke-Deck`'s existing `$vars` array.
- Validated (`-Size >= 0`, `-Files >= 1`) in the `conflict` switch case before seeding.

### Verification

- CGameID/cancel fixes: confirmed on real Steam Deck hardware across three rounds of CDP-instrumented
  traces; user's final report: "it works perfectly now."
- Generalization: explicitly confirmed the CGameID bug affects every game the feature targets (Heroic
  titles, emulators, the fake-game rig) — not fake-game-specific — directly answering the scope question
  asked.
- `-Size`/`-Files`: `[System.Management.Automation.Language.Parser]::ParseFile` clean; `bash -n` clean
  on `testenv-deck.sh`; the byte-splitting arithmetic independently verified correct on both platforms
  standalone (25 files / 25 MB → exactly 26,214,400 bytes on both PowerShell and Bash). The existing
  `clean` command's whole-directory `rm -rf` already covers the new multi-file saves with no changes
  needed.

### Not done

- **Debug instrumentation not yet stripped.** `gamingSync.tsx` still carries a temporary `dbg()` helper
  and call sites at every decision point, plus a debug-only `getAllOpenConflictGameIds` hook wired
  through `index.tsx`; `libraryOverlay.tsx` still has one leftover debug `console.log`. All were added
  during this investigation and are safe to remove now that the fix is hardware-confirmed, but removal
  hasn't been done yet.
- **Nothing committed in either repo.** SaveLocker-Decky has the two fixes plus debug instrumentation;
  SaveLocker has the new `-Size`/`-Files` feature plus earlier still-uncommitted changes (the
  `Get-DeckyPluginRepo` diagnostic fix, `DrawFakeGame` color-cycling). No commit or push requested yet.

---

## 2026-09-08 — Easy verification wins triage (`easy-wins`)

Five small backlog items triaged on a dedicated branch + worktree (off `2f3a1a9`, main checkout
untouched): two needed code (three commits), two were verification-only, all five
maintainer-verified live, then archived out of the backlog.

**Commits:** `5ffd7d1` self-hosted console fonts · `bc82708` cross-source doctor note · `d6d4814`
Game Mode stale-list poll · `6ec250e` vault (archive + `shipped-2026-09.md`). Unmerged, unpushed.

### The five items

1. **Self-hosted console fonts** — Google Fonts `@import` out of `web/src/index.css`; Inter 300–700
   + JetBrains Mono 400/500 vendored via Fontsource in `web/src/main.tsx`. `npm run build` +
   `oxlint` clean, zero `googleapis`/`gstatic` in `web/` or `dist/`. Verified in DevTools: zero
   Google requests, and blocking `*googleapis*`/`*gstatic*` renders identically.
2. **Installer ACL trap (WA-03)** — no code change. Verified live: `icacls %PROGRAMDATA%\SaveLocker`
   shows only SYSTEM/Administrators/self, inheritance broken; the WA-03 suite section asserts the
   same on a scratch dir. Test-location note: the installer never creates the dir and
   `StateDirSecurity.Protect` runs on whatever `SAVELOCKER_STATE_ROOT` resolves to, so any test dir
   exercises the identical mechanism.
3. **LAN enrollment URL** — no code change. Verified `effective-url` (`isLoopback:true` on localhost,
   `false` with the LAN host), localhost mint without a URL → HTTP 400 before burning the single-use
   token, explicit LAN URL → 200 with that URL in the policy.
4. **Cross-source doctor note (one-game-several-sources step 2)** — `ScanCandidate.MoonDeckAppId`,
   `LinuxGameScanner.ScanUnfilteredAsync` + `PickWinner`,
   `Doctor.ReportCrossSourceDuplicatesAsync` + `DescribeOrigin`: one line per title found in 2+
   places, every origin named, `[tracked]` on the scan's pick, plus the `add-game` switch hint. A
   note, never a problem — exit code unaffected. `run-linux-tests.sh` dual-source checks extended.
5. **Game Mode stale list** — `AgentConfig.RefreshGameList` (membership-only by `GameId`, surviving
   entries untouched) polled from `UiApp` every 10s including the first frame; lock contention defers
   to the next poll, an unreadable file is a no-op. Verified live under WSLg: add appears, delete
   disappears, no restart.

### Debugging sagas worth keeping

- **A Google Fonts request that wasn't ours.** DevTools showed `Readex Pro` + `Signika` from
  `fonts.googleapis.com` after the fonts fix; a repo-wide grep for both families returned zero hits
  — a browser extension or stale keep-log entry, not SaveLocker. The hunt did find a real leftover:
  `agent-ui/index.html` still loads Inter from Google Fonts (the fonts task covered only the
  console) — open follow-up, not started.
- **WSL quoting traps, three of them.** PowerShell 5.1 strips double quotes building native
  `wsl.exe` command lines (the same trap `testenv.ps1` documents): keep `bash -c` bodies quoteless,
  never reference `$PATH` (the inherited Windows PATH contains `Program Files (x86)` parens whose
  expansion breaks parsing), construct `PATH=` explicitly. `tr -d 015` deletes the *digit set*
  {0,1,5}, not CR — and heredoc-written files lose their quotes the same way; stage files via the
  temp dir + `cp` instead.
- **Suite `exit 2` is environmental.** The `chattr +i` tamper probe needs `CAP_LINUX_IMMUTABLE` —
  root on ext4 (fs confirmed ext4). All six dual-source checks PASS in the suite log, including two
  doctor-note assertions (`found in two places`, MoonDeck origin named). Root recipe + post-run
  `chown -R maro:maro` + rig bounce documented for fully-green runs.
- **The stale-list verification saga.** Rename-vs-delete: renames never propagate (same IDs → early
  return, by design — the "or shows the new name" instruction was wrong and retracted). A
  hand-written `GameId` that isn't a real UUID crashes `Load` at startup yet is silently skipped by
  the poll's `ReadOnDisk` — and `json.tool` VALID doesn't catch it, since the JSON is well-formed. A
  temporarily instrumented build (`[poll]`/`[refresh]` trace lines, WSL clone only, reverted
  afterward) confirmed the poll runs every ~10s against the launched `config:` path. Ruled out by
  code read along the way: the UI never writes the config back (zero `Save` calls in `UiApp.cs`),
  and the state lock is an OS-level file lock with no stale state.
- **Two robustness gaps filed as follow-ups, not fixed here:** the `Load`-throws vs
  `ReadOnDisk`-silent asymmetry above, and rename propagation (safe to add — the UI never writes —
  but its own task).

### Vault

Five `docs/tasks/*/` folders → `docs/logs/2026-09-08_*/` (git mv, history kept), five lines out of
`Backlog.md`, new `docs/logs/shipped-2026-09.md` index with precisely-scoped rows — several removed
backlog lines were broader than what was verified (installer happy-path/expired/skip/silent, Deck
scenarios, Windows gates, second-machine redeem), and those sub-scopes are named there rather than
silently dropped. `CONTEXT.md` session entry added.

---

## 2026-09-14 — Conflict-resolution-ui closed out (Phase 14 dropped); Playnite plugin groups consolidated 11 → 6

**Docs-only session — no application code changed, nothing committed.** Continuation of the same-day
Playnite-plugin planning session that split Phase 13 out of `tasks/conflict-resolution-ui/` into its
own standalone `tasks/playnite-plugin/` task (16 phases, own grouping doc). This entry covers the two
follow-up requests made once that split had landed.

### What was asked

1. "Ditch phase 14 and mark save conflict as complete with only future user experience review may
   come."
2. "And decrease the number of implementation groups in the playnite plugin."

### Phase 14 dropped, conflict-resolution-ui marked complete

- `tasks/conflict-resolution-ui/plan.md`: Status section rewritten to "task complete" — every phase
  has now shipped, moved to its own task (Phase 13 → `tasks/playnite-plugin/`), or been dropped
  (Phase 14). Phase 14's row changed from "not started, deferred" to "❌ Dropped 2026-09-14," with the
  reasoning spelled out: its block-launch half (`Game.BlockLaunchOnConflict`) never had its open design
  conflict against Phase 4's already-shipped *unconditional* block-on-confirmed-conflict behavior
  resolved, and neither half — the block-launch opt-in nor the webhook/ntfy/email notify — has any
  confirmed player need behind it; rungs 5–7 of the escalation ladder (CLI, `doctor`, the safe
  open-`ConflictFlag` terminal state) already guarantee a conflict is discoverable and never silently
  mishandled without it. Cleaned up every other place Phase 14 was still described as pending: the
  intro paragraph, the Phase 7 and Phase 9 shipped write-ups' cross-references, the ASCII dependency
  diagram, the dedicated Phase 13/14 section body, and the Scope note's rung-6 description.
- `tasks/conflict-resolution-ui/implementation-grouping.md`: same completion marker added to its own
  Status header; Group 5's Phase 14 mention updated from "deferred" to "later dropped entirely."
- `Backlog.md`: removed the "Decky conflict resolution" line from the High-priority not-yet-done list
  entirely (nothing remains open on it); the Playnite plugin line's group reference updated for the
  regrouping below.
- `logs/shipped-2026-09.md`: added one completion row for the whole 14-phase effort, pointing at
  `tasks/conflict-resolution-ui/plan.md` for full detail rather than trying to re-summarize 14 phases
  of shipped work into the log table's own cells.

**Deliberately not done:** did not relocate `tasks/conflict-resolution-ui/` into `docs/logs/` despite
the project's own session-handoff convention for completed task folders — `tasks/playnite-plugin/
plan.md`'s header and phase-dependency notes reference this folder's path directly, and moving it
would break those links for no benefit the request asked for. Flagged to the user rather than done
silently.

### Playnite plugin implementation groups consolidated: 11 → 6

`tasks/playnite-plugin/implementation-grouping.md` rewritten to bundle the same 16 phases into 6
groups instead of 11, at the same total session-cost estimates — `plan.md`'s own "Size estimate"
section (~7–9 sessions agent-side, ~11–15 plugin-side, ~18–24 total) was left untouched, since only
the bucketing changed, not the underlying per-phase numbers:

- **Group 1** (agent-side, ~2–2.5 sessions): Phases 1–3 — launch-gate rewiring, `SteamAppId`
  population, `PullBeforeLaunchEnabled` move (merges the old Groups 1+2).
- **Group 2** (agent-side, ~4.5–6 sessions): Phases 4–7 — exit-push toggle, the three new local-API
  routes, the `AgentPlatform.PlaynitePlugin` slot, and the Windows self-updater (merges the old
  Groups 3+4).
- **Group 3** (plugin-side, ~5–6 sessions): Phases 8–11 — scaffold, settings, the core pre-launch
  gate, and automatic matching (merges the old Groups 5+6+7 — matching was deliberately pulled in
  here, not left in its own group, specifically so the MVP cut stays a clean "Group 1 + Group 3"
  rather than half of a merged group).
- **Group 4** (plugin-side, ~2 sessions): Phase 12 alone — the enroll/link popup (was old Group 8,
  unchanged).
- **Group 5** (plugin-side, ~3.5–5 sessions): Phases 13–15 — status chip/buttons, self-update
  consumption, test infra (merges the old Groups 9+10).
- **Group 6** (plugin-side, ~0.5–1 session): Phase 16 alone — add-on database submission (was old
  Group 11, kept separate on purpose: a third-party review timeline is a different kind of task from
  build work, worth keeping visible rather than buried in a polish group).

`plan.md`'s own "Recommended cut" section was updated to cite the new group numbers: the MVP is now
simply **Group 1 + Group 3** (~7–8.5 sessions — arithmetically identical to the old five-group MVP of
Groups 1/2/5/6/7, since no phase moved between the MVP and non-MVP side of the line, only the group
boundaries around them changed).

### Verification

Docs-only change; verified by grepping both `plan.md` files for stale `Group [0-9]`/`Phase 14`
references after each edit, and re-reading the edited sections to confirm internal consistency (group
session totals still sum correctly, and the MVP session count still matches the "≈7–8.5 sessions"
claim `plan.md` already made before this session touched it).

### Not done

- `tasks/conflict-resolution-ui/` folder not relocated to `docs/logs/` (see above).
- No commit made — vault edits are uncommitted on the current worktree branch
  (`phase-13-playnite-plugin`).
- The "future user-experience review" now named in conflict-resolution-ui's Status section is
  intentionally left unscoped and unscheduled — a named possibility, not a task.

---

## 2026-09-14 (cont'd) — Playnite plugin Group 1 shipped; WSL conflict fallback; testenv `clean` fix

Continuation of the same-day session that consolidated the Playnite plugin's groups. This entry covers
implementing Group 1 (Phases 1–3) end to end, debugging the user's own manual verification against it,
designing and building a WSL fallback for `testenv.ps1 conflict` so a genuine conflict no longer needs
a physical Steam Deck, fixing an unrelated `testenv.ps1 clean` bug found along the way, and establishing
the Phase/Group status-table convention as a standing, repo-checked-in rule in `CLAUDE.md`.

### What was asked

1. "Start implmenting group 1 and afterwards (only if needed) tell me how to verify manually / And what
   has changed briefly in technical and no technical terms after implmenting this group."
2. Manual-verification follow-ups as the user actually ran the steps: a `401 Unauthorized` on two
   `Invoke-RestMethod` calls; why `pre-launch-sync` returned `Proceed` instead of `Blocked`.
3. "i would prefer to make conflict in testenv has this functionality. if the deck is not reachable use
   wsl insted / but sugesst how it will work or should another approwch be doen / maybe required args" —
   a design proposal first, not immediate implementation.
4. "implment this / then commit changes in meaningful commits / and add a summery table for implemented
   phses and groupd in playnite plugin task just like in conflict resolution ui. and make it the standard
   when creating plan.md and implmentation-grouping.md."
5. "why does this happen please fix if you can" — a pasted `testenv.ps1 clean` failure.
6. "after those edits tell me a step by step manual verfication using testenv."
7. Two follow-up questions on the verification output: why `pullBeforeLaunchEnabled` was empty for real
   Steam games and `false` for "Conflict Game"; then "shouldont pull before launch be true in all non
   steam games?"

### Group 1 implemented (Phases 1–3)

- **Phase 1 — Windows launch-gate rewiring.** `TrayApp.cs`'s `prepareLaunch` delegate swapped from
  `_engine.OnGameLaunchAsync(game, preLaunch: false, ct)` to `_engine.PrepareLaunchAsync(game, ct)`,
  mirroring `Daemon.cs:171`'s existing Linux wiring — this is now a genuine pre-launch boundary on
  Windows for the first time, safe because nothing called `/api/games/{id}/pre-launch-sync` before
  today (confirmed by grep: no agent-ui button, no CLI command).
- **Phase 2 — `SteamAppId` population on Windows.** `GameScanner.ScanInstalledSteamGamesAsync` now
  records the manifest's own `appid` on every `ScanCandidate` it builds (previously read only to filter
  `NonGameAppIds`/compat tools, then discarded) closing the "majority Windows source never recorded it"
  gap `TrackedGame.SteamAppId`'s own doc comment called out. `TrayApp.cs` gained
  `BackfillSteamAppIdsAsync()` — a fire-and-forget startup rescan matching already-tracked games with a
  null `SteamAppId` by `InstallDir` against fresh candidates — plus `AgentConfig.SaveGameSteamAppId`
  (lock-protected, `MutateGameUnderLock`) to persist a hit.
- **Phase 3 — `PullBeforeLaunchEnabled` moved server-side.** `SyncEngine.PrepareLaunchAsync`'s final
  unconditional `PullAsync` call is now gated by a new private `EffectivePullBeforeLaunch(game) =>
  game.PullBeforeLaunchEnabled ?? game.HasSteamCloud != true;` — an explicit per-game override always
  wins; absent one, the gate itself now computes "off for a confirmed Steam Cloud game, on otherwise,"
  instead of leaving that heuristic to whichever frontend (previously only Decky's) chose to read the
  flag client-side. The open-conflict check and the commit-before-choose push stay unconditional —
  disabling the pull only skips fetching newer data, never the safety check for a genuinely diverged
  save.

Verified via `dotnet build` (agent-ui needed `npm install` in this worktree first — no `node_modules`,
safe/local/gitignored) and then live against a real testenv rig.

### Debugging during the user's own manual verification

- **`401 Unauthorized`** on `/api/games` and `/api/games/{id}/pre-launch-sync`: missing
  `X-SaveLocker-Token` header. Fixed by locating `%LOCALAPPDATA%\SaveLocker-test\SaveLocker\api-token`
  and adding it to every `Invoke-RestMethod` call.
- **`decision: Proceed` instead of `Blocked`**: traced to my own earlier instruction telling the user to
  run `conflict -Windows` alone. `testenv.ps1`'s own `New-ConflictOnWindows` comment spells out why that
  can never produce a real conflict: whichever side pushes SECOND is what the server records as a
  genuine, unresolved divergence — the side that pushes first just creates the game with no conflict.
  With no Deck configured, only one side had ever seeded. Acknowledged the mistake directly to the user
  and gave a corrected manual workaround (a second local machine identity) before the WSL-fallback
  feature below made this permanently unnecessary.

### WSL fallback for `testenv.ps1 conflict` (design, then implementation)

Proposed a concrete design mirroring how `-Windows`/`-Deck` already work rather than inventing a new
pattern — `.\tests\testenv.ps1 conflict [-Windows] [-Deck] [-Wsl] [-Size <MB>] [-Files <n>]` — with
`-Wsl` as an explicit second-side choice and, per the user's literal ask, an automatic fallback to WSL
when `-Deck` is requested but the Deck turns out to be unreachable (a sleeping Deck being a normal,
expected state here, not an error worth hard-failing on). Implemented once approved:

- **`tests/testenv.sh`** gained a `cmd_conflict()` function (WSL had none before) — stops the daemon,
  seeds `$XDG_DATA_HOME/conflict-save` (a tiny distinguishable file, or `$CONFLICT_FILES` random files
  totalling `$CONFLICT_SIZE_MB` MB via the same `SAVELOCKER_CONFLICT_SIZE_MB`/`SAVELOCKER_CONFLICT_FILES`
  env-var convention `testenv-deck.sh` already uses), registers the machine if needed, then
  `add-game`/`push`es "Conflict Game" — no Steam-shortcut tail, since WSL is headless. Registered in the
  bottom dispatch `case`.
- **`tests/testenv.ps1`** gained `[switch]$Wsl`, a `New-ConflictOnWsl` function, and a rewritten
  `'conflict'` case: `-Deck -Wsl` together throws; the second side is computed as `deck`/`wsl`/`none`
  from the switches given (defaulting to Windows + an auto-picked second side when none are); Deck is
  attempted first whenever requested, falling back to `-Wsl`'s seeding on any failure (not configured, no
  `$DeckServerUrl`, or a thrown `Invoke-Deck` exception) — printed clearly either way, never silent.
  `-AddCommand` is warned against when the second side resolves to WSL.

Verified: `bash -n tests/testenv.sh` clean; the PowerShell parser clean on `testenv.ps1` twice; and,
per the user's own pasted verification output later in the session, an actual WSL-seeded conflict
correctly produced `Blocked` through the real `PrepareLaunchAsync` gate — full proof Phases 1–3 work
against real data, not just a synthetic path.

### `testenv.ps1 clean` fix (found mid-session, unrelated to Group 1)

The user hit a wall of `Remove-Item` "being used by another process" errors under
`WebView2\EBWebView\...`, ending with a misleading `removed ...` line regardless of outcome. Root cause:
`Stop-Windows` only ever killed the tray's own `dotnet.exe` process, never the separate
`msedgewebview2.exe` helper processes (renderer/GPU/network/crashpad) WebView2 spawns for the agent
window — those don't terminate synchronously with their parent, so their cache files stayed briefly (or
longer) locked after the tray exited. Fixed: `Stop-Windows` now also finds and kills any
`msedgewebview2.exe` whose `CommandLine` matches this test rig's own `$winState\WebView2` path (via
`Get-CimInstance Win32_Process`, verified safe against this machine's 12 real running instances — all
belonged to Windows Search/Cortana and Google Drive, none matched the filter), then waits 500ms. The
`'clean'` case's delete itself was rewritten into a 5-attempt retry loop that now actually `throw`s the
last error on total failure — a pre-existing bug where it silently printed "removed" regardless of
whether the delete had actually succeeded.

### Standing convention established: Phase/Group status tables

Per the explicit request to "make it the standard," added a new `### plan.md / implementation-
grouping.md status tables` subsection to `CLAUDE.md` itself (not just applied once) — every future
`plan.md` gets a `## Status` table (Phase | Status) right after its intro, and every
`implementation-grouping.md` gets the matching `(Group | Contents | Status)` table just above
`## Groups`, updated the same session a phase/group ships. `conflict-resolution-ui` and `playnite-
plugin`'s own plan/grouping docs are named as the reference examples. Applied immediately to
`playnite-plugin/plan.md` (16-row Phase table, Phases 1–3 marked shipped) and its
`implementation-grouping.md` (6-row Group table, Group 1 marked done).

### Answered: the `pullBeforeLaunchEnabled` tri-state question

The user's real Steam games (Slay the Spire, Citizen Sleeper, Caravan SandWitch) showed
`pullBeforeLaunchEnabled: null` because nothing had ever touched their override — correct, `null` means
"the gate computes its own default," not "off." "Conflict Game" showed a literal `false` because that
was an explicit override set during the manual-verification walkthrough's own step 6 (`POST
.../pull-before-launch` with `{enabled: false}`), which always wins over the computed default regardless
of `HasSteamCloud`. Confirmed directly to the user that yes, per `EffectivePullBeforeLaunch`'s own logic,
any non-Steam-Cloud game with no override genuinely does compute an effective default of `true` — the
apparent exception was an override this session had set itself during testing, not a bug in the default
logic. Also proposed, not yet built: exposing that computed effective value as a new read-only
`effectivePullBeforeLaunch` field on `TrackedGameDto`, so a future settings UI (Playnite's Phase 9/13
included) can show "on (default)" vs. "on (forced)" without re-implementing the heuristic itself.

### Commits (this worktree, unpushed)

`0cb12d7` (conflict-resolution-ui Phase 14 drop, confirmed clean) · `cca2836` (Playnite plugin doc split
+ status-table convention) · `c33bbcb` (Group 1: Phases 1–3) · `f87ffc8` (WSL conflict fallback) ·
`5511244` (`testenv.ps1 clean` WebView2 fix).

### Not done

- The proposed `effectivePullBeforeLaunch` DTO field — discussed, not implemented; no code change
  requested for it yet.
- None of this session's five commits carry the `Co-Authored-By` trailer the current attribution
  instructions require — none have been pushed, so this is fixable, but has not yet been raised with or
  confirmed by the user.

## 2026-09-14 (cont'd 2) — Playnite plugin Group 2 shipped; GitHub org default fixed

Implemented Group 2 (Phases 4–7) of the Playnite plugin plan — exit-push toggle, three new local-API
routes, the `AgentPlatform.PlaynitePlugin` slot, and a Windows plugin self-updater — then debugged the
user's own live manual verification against it, fixing one real bug it caught (a wrong hardcoded GitHub
org in the plugin's install link) plus two errors in my own verification instructions. Five commits, all
unpushed.

### What was asked

1. "start implementing group 2 please and after words give me step by step manual verification using
   test env if needed."
2. Live debugging as the user ran the verification steps themselves: a 404 on `post-exit-sync`, a
   question about why `pushAfterExitEnabled` still read `null` after a successful toggle POST, a 401 on
   the server's `/api/agent/latest` route, and a wrong GitHub org baked into the plugin's install link.

### What was built

- **Phase 4 — `PushAfterExitEnabled`.** New nullable-bool override on `TrackedGame`
  (`AgentConfig.SaveGamePushAfterExit`), mirroring `PullBeforeLaunchEnabled` exactly: `null` keeps
  today's unconditional push-on-exit behavior, an explicit `true`/`false` always wins.
  `SyncEngine.OnGameExitAsync` now gates its push call on `game.PushAfterExitEnabled ?? true`; the lease
  release in `finally` stays unconditional either way.
- **Phase 5 — three new local-API routes** (`AgentApiServer.cs`): `POST
  /api/games/{id}/post-exit-sync` (single-flight via the existing `_syncGate`, fail-open — logs and
  swallows any exception from the injected `postExitSync` delegate, never surfaces one to the caller);
  `POST /api/candidates/lookup` (a single targeted resolve against a new `Detection`-typed constructor
  dependency, merged into `_candidateCache` by normalized name so the returned id enrolls through the
  existing, unmodified `/api/enroll` route); `GET /api/manifest/search?q=` (thin wrapper over
  `Detection.SearchAsync`). Added a `Playnite` value to `ScanSource` for the lookup route's synthesized
  candidate. `Agent.Linux/Daemon.cs` wired the new `detection`/`postExitSync` constructor params;
  `Agent/TrayApp.cs` got the same wiring bundled into the Phase 7 commit.
- **Phase 6 — `AgentPlatform.PlaynitePlugin` slot.** New platform constant end to end:
  `Shared/Contracts.cs` (const + `All` + `Describe`), a new `_slots` entry in
  `AgentInstallerService.cs` (`.zip`, `SaveLocker*.zip` pattern), `Server/Program.cs`'s static-config
  fallback switch, and — not anticipated by `plan.md`, found this session — a hand-written 4th entry in
  `web/src/components/AgentUpdatesCard.tsx`'s `INSTALLER_SLOTS` array plus `web/src/types.ts`'s
  `AgentPlatform` union. Verified via `npx tsc --noEmit` (clean).
- **Phase 7 — Windows plugin self-updater** (`src/Agent/PlaynitePlugin.cs`, new file). Mirrors
  `Agent.Linux/DeckyPlugin.cs`'s shape (`CheckAsync`/`InstallAsync`, plan-before-write, digest
  verification via `UpdateChecker.DownloadInstallerAsync`) but simpler — `%AppData%\Playnite\
  Extensions\<id>\` is entirely user-owned, so a full-directory prune replaces Decky's `dist/`-only
  carve-out. Since `src/Agent` doesn't reference `src/Agent.Linux`, defined fresh local
  `PluginUpdateState`/`PluginUpdateOutcome` types instead of reusing Decky's, and used a plain
  `InvalidOperationException` instead of the Linux-only `UpdateRefusedException`. Because a compiled
  Playnite `GenericPlugin` never hot-reloads and its assembly is typically locked by its own host,
  `IsPlayniteRunning` (checks `Playnite.DesktopApp`/`Playnite.FullscreenApp`) gates every write, both
  before download and again immediately before copying files. `TrayApp.cs`'s update timer now also
  calls a new `CheckPlaynitePluginUpdateAsync()` alongside the existing agent self-check. Added
  `PackageKind.PlaynitePlugin` to `UpdateChecker.cs`'s shape table.

Verified: `Agent.Core`, `Server`, and `Agent.Linux` all built clean directly; `src/Agent` was verified
via scratch-output builds (`dotnet build ... -o <scratch>`) rather than its normal `bin/` output, which
stayed locked by the user's own live test-tray process for most of the session — not something to kill,
since it was their legitimately running verification instance, not a stale process.

### Debugging during the user's own manual verification

- **404 on `POST /api/games/{id}/post-exit-sync`.** Likely cause: `$id` taken from an empty/stale
  `$games` array, producing a malformed `.../games//post-exit-sync` URL that fails the `{id:guid}`
  route constraint. Talked the user through re-checking `$games.Count` and re-fetching; they resolved it
  on their own before the next message, no further discussion needed.
- **"shouldn't `pushAfterExitEnabled` be false now, it's null?"** Same class of confusion as last
  session's `pullBeforeLaunchEnabled` question: the printed `$games` was a stale PowerShell snapshot from
  before the POST, not a fresh fetch. Explained directly and gave the corrected re-fetch command.
- **401 on `GET /api/agent/latest?platform=playnite-plugin`.** My own mistake, not a code defect — I'd
  given a verification step using the local agent API's `X-SaveLocker-Token` header against a *server*
  route that actually requires the console/dashboard's separate `X-Api-Key` header (the machine's own
  key from `config.json`). These are two entirely unrelated auth schemes for two separate services;
  fixed by correcting the instructions, no code changed.
- **Wrong GitHub org in the plugin's install link — real bug, user-caught.** The user's own Phase 7
  verification correctly reported the plugin missing, but the printed install URL pointed at
  `SkorcherX/SaveLocker-Playnite`. Direct correction: *"the link is will be wrong. the plugin will be in
  my account not SkorcherX."* Root cause: Phase 6/7 had mirrored the already-shipped
  `SkorcherX/SaveLocker-Decky` default for the brand-new, not-yet-existing `SaveLocker-Playnite` repo
  without checking whether that account applies here — it doesn't; every PR/commit in this repo's
  history is under `Marwanello/SaveLocker`. Fixed both defaults (`AgentInstallerService.cs`'s
  `playniteRepo` fallback and `PlaynitePlugin.cs`'s `InstallUrl` constant) to
  `Marwanello/SaveLocker-Playnite`. Deliberately left the existing `SkorcherX/SaveLocker` (agent) and
  `SkorcherX/SaveLocker-Decky` defaults untouched — those are already-shipped and out of scope.

### Commits (this worktree, unpushed)

`738d086` (Phases 4–5) · `b5933aa` (Phase 6) · `5f6b598` (Phase 7) · `49e1e4c` (status-table update) ·
`c7d5454` (GitHub org fix).

### Not done

- Phase 7's self-updater is code-complete but unverified against a real install, since
  `SaveLocker-Playnite` doesn't exist as a repo/release yet.
- The `Co-Authored-By` trailer gap flagged for the prior session's five commits is still unaddressed —
  not raised again this session, still requires explicit confirmation before any history rewrite.

---

## 2026-09-15 — Playnite plugin Group 3 hardware verification: conflict-popup root cause, theme-driven resolve window, git housekeeping

**Repos:** main `SaveLocker` (branch `playnite-plugin-group-3`, HEAD `9453fda`) and
`SaveLocker-Playnite` (branch `playnite-plugin-group-3`, HEAD `e052283`), both pushed to their
respective `Marwanello` forks. Testing used a portable Playnite install at
`D:\Projects\SaveLocker\Playnite-Test` plus the main repo's `tests/testenv.ps1` rig.

### Request sequence

1. Hardware-verify Group 3 (Phases 8–11: scaffold, settings, core pre-launch/post-exit gate,
   automatic matching) against a real, running Playnite.
2. "the game starts and no conflict window appears" — then, after a partial fix, a "REFUSED push"
   toast plus "the conflict popup doesn't show up and the game opens stright away."
3. Once the popup worked: "i don't want it hardcoded. i do want it to match the theme chossen in the
   the Playnite app" — an explicit standing requirement that the window's colors be theme-driven.
4. Three rounds of visual feedback, each via screenshot: white background against a dark theme; then
   "the text is black over a dark backround. match the text color to the theme please and also add
   cloud and device icons just like the one on the agent UI"; then, after a fix attempt, "still the
   text is black over dark background"; then the explicit correction **"nothing chnged please fix it
   porperly without guessing."**
5. "ar changes don in playnite-plugin-group-3? if not push to this branch and delete
   claude/playnite-plugin-group-3-acd4eb" — git housekeeping, explicitly authorizing deletion of a
   long-standing duplicate remote branch.
6. After a usage-limit reset mid-session: "continue from where you left off," followed by a request to
   append this write-up to `progress.md` and `session_summary.md`.

### Root causes found and fixed

**#1 — "no conflict popup" traced to this session's own earlier doc guidance.**
`docs/Build and Run.md` (SaveLocker-Playnite repo) told the user to run `.\tests\testenv.ps1 conflict
-Wsl` alone. `-Wsl` alone seeds only the WSL side — Windows never tracks "Conflict Game" locally, so
the plugin's `GameMatcher.FindMatch` finds nothing, its launch controller never engages, and Playnite
silently falls back to a plain launch. Confirmed definitively by cross-referencing Playnite's own
`playnite.log`: `"Using generic controller start a game."` (no match) vs. the expected `"Using plugin
to start a game."` (match found, gate engaged) — this log-line contrast is now the documented,
repeatable diagnostic for this class of bug. **Fixed** two ways: `docs/Build and Run.md` corrected to
`conflict -Windows -Wsl` with an explanatory paragraph, and `tests/testenv.ps1`'s `conflict` case now
`Warn`s loudly whenever `-Deck`/`-Wsl` is passed without `-Windows`, so a forgotten flag can't silently
reproduce this again for a future session.

**#2 — hardcoded dark palette rejected, replaced with real theme resources.** The first theming fix
used literal RGB values. Per the user's explicit "match the theme, don't hardcode" instruction, rebuilt
around `IPlayniteAPI.Dialogs.CreateWindow(WindowCreationOptions)` (the SDK-documented way to get a
`Window` that inherits Playnite's own active-theme chrome) plus `element.SetResourceReference(...)`
against real theme keys confirmed from Playnite's own GitHub source
(`Themes/Desktop/Default/Views/TopPanel.xaml`): `TextBrush`, `PopupBackgroundBrush`,
`PopupBorderBrush`.

**#3 — button label text stayed black on a dark background through two fix attempts.** First attempt
(`SetResourceReference(Control.ForegroundProperty, "TextBrush")` on the `Button` itself) had no visible
effect; a second attempt (a custom `ControlTemplate` with an explicit `TemplateBinding Foreground` on
the `ContentPresenter`) *also* had no visible effect per the user's "nothing chnged" report. Diagnosed
with certainty rather than guessed again, per the user's explicit instruction: (a) fetched Microsoft's
own WPF dependency-property-precedence docs, confirming a local value set via `SetResourceReference`
already outranks template/style triggers in theory; (b) inspected the portable install's actual active
theme and found it is **not** Playnite's own "Default" theme but a third-party community theme,
`Harmony_d49ef7bc-49de-4fd0-9a67-bd1f26b56047` — its own `Button` `ControlTemplate` apparently renders
text without honoring `TemplateBinding Foreground` at all; (c) ruled out two confounds that had made
the second attempt's "nothing changed" report ambiguous — a stale in-memory plugin DLL (confirmed fresh
via file timestamp) and a stale local-API token (confirmed via direct `curl` against
`/api/games` and `/api/games/{id}/pre-launch-sync`, both healthy). The robust fix bypasses
`Button.Foreground`/`ContentPresenter` inheritance entirely: every button's label is now its own
directly-constructed `TextBlock` with `Foreground` set via `SetResourceReference` on the `TextBlock`
itself (`MakeLabel` helper in `ConflictResolveWindow.cs`), never routed through a template at all.

**#4 — found and fixed after the above, before this write-up: body text (not just buttons) was also
unreadable.** Root-caused to Playnite's own `StandardWindowStyle.xaml` (which `Harmony` falls back to,
defining no window style of its own): it sets `Background`/`BorderBrush` but never `Foreground` at the
`Window` level, so every plain `TextBlock` that had been left to inherit `Foreground` rendered in WPF's
default near-black — the same underlying failure as #3, just not yet applied file-wide. Added a
`Themed(TextBlock)` helper (the same `SetResourceReference` fix, generalized) and applied it to every
`TextBlock` in the window, including the previously-unreadable size/file-count/newest-change stat
lines. Commit `e052283`'s own message states this was verified live against the real portable Playnite
(Harmony theme) — every line renders correctly.

**Cloud/device icons** added to match `agent-ui`'s `ConflictCard.tsx`: real Lucide `cloud`/`hard-drive`
SVG path data reproduced as WPF `Path` geometry, `Stretch.Uniform`, stroke-based (not filled) to match
Lucide's own rendering style.

### Bugs found along the way (operational, not product bugs)

- `testenv.ps1`'s `New-ConflictOnWindows` stops the Windows tray before seeding (avoids racing a live
  tray's config writes); the tray must be manually restarted (`up -Only windows` or a full `up`)
  afterward, or the plugin's local-API calls fail. Not obvious from the command's own output; worth a
  doc note if this recurs.
- `testenv.ps1 clean`'s Windows-state wipe (`%LOCALAPPDATA%\SaveLocker-test`) is **inconsistent** —
  more than once it left a stale `ApiKey`/`MachineId` in `config.json` behind after a `clean`+`up`
  cycle, which is invalid against the freshly-wiped server database and produces `401 from
  /api/games` (silent match failures) on the next `up`. Worked around manually each time via `rm -rf`
  of the state dir before re-running `up`. **Not fixed in code** — a real robustness gap in
  `testenv.ps1 clean`, not yet raised as a task.
- `CommandPoller.ReconcileGamesAsync` "adopts" a server-known game not yet tracked locally with
  `SaveDirectory = ""` when auto-detection fails — this is what produced an earlier `"REFUSED push:
  ...mapped to ''"` toast when Windows hadn't actually been seeded (a symptom of root cause #1 above,
  not a separate bug).

### Git housekeeping

- Main repo: committed the `testenv.ps1` `-Windows` warning as `9453fda`, fast-forwarded local
  `playnite-plugin-group-3` onto it, pushed (`f3053ae..9453fda`).
- `SaveLocker-Playnite` repo: committed the theming/icon work as `4da6ace`, then the body-text fix as
  `e052283`, pushed directly from `playnite-plugin-group-3` both times.
- Deleted the duplicate remote branch `claude/playnite-plugin-group-3-acd4eb` on
  `Marwanello/SaveLocker` (`git push origin --delete`) — flagged earlier in the session, blocked by the
  harness's destructive-git classifier until explicitly authorized this turn.

### Verification

- Root cause #1: confirmed via `playnite.log` line cross-referencing (see above).
- Root causes #2–#4: confirmed via user screenshots at each round; #4 specifically confirmed live
  against the real portable Playnite per commit `e052283`'s own message.
- Gate correctness (independent of the UI question) verified directly via `curl` against the local API:
  `/api/games` (200, game matched) and `/api/games/{id}/pre-launch-sync` (`decision: "Blocked"`,
  `reason: "a confirmed conflict is open for this game"`) — ruling out the stale-token/generic-launch
  confounds before re-touching styling code a third time.
- **Full 6-step `Build and Run.md` walkthrough re-verification was requested again this session** (the
  user, asked how much of it had actually been confirmed on hardware, chose "not sure — let's
  re-verify" rather than relying on memory) — **in progress as of this write-up**; the test rig
  (docker containers) was found fully torn down when this segment resumed after the usage-limit reset,
  so a rebuild is needed before continuing.

### Not done

- The full 6-step manual verification (untracked-game baseline, lease-held-elsewhere notification,
  post-exit push, agent-down fail-open — steps 1, 3, 5, 6) is being re-run from a clean rebuild; not
  complete as of this write-up.
- `docs/CONTEXT.md` (SaveLocker-Playnite repo) and the Group 3 status row in the main repo's
  `docs/tasks/playnite-plugin/implementation-grouping.md` are still stale (both still say "not yet
  hardware-verified") — correctly deferred until the re-verification above actually completes.
- `testenv.ps1 clean`'s inconsistent Windows-state wipe (401-after-clean bug, above) — not fixed in
  code, only worked around manually each time it recurred.
- Whether PRs should be opened on either repo (branches are pushed, no PRs opened) has not been raised
  again since it first came up.

---

## 2026-09-15 (cont'd) — WSL lease-test fix, Fullscreen theme investigation, Fullscreen-native conflict popup built

### Request sequence

1. Checklist correction: the given `cd D:\Projects\SaveLocker\SaveLocker` step was wrong — "nothing
   will wok as the main repo deon't contain the code." The main checkout sits on `main`; the
   `playnite-plugin-group-3` branch only exists in this worktree.
2. "convert tis to wsl compatable" — a PowerShell script (from earlier in the session) that reads
   Minit's game id from the Windows tray's local API, then POSTs a lease against the test server using
   "LinuxTest's key from the `up` output above."
3. "every thing works great execept cant test 3 as i'm leassing the game on the same device need to
   do it through wsl but i cant figure it out," plus confirmation the Fullscreen-mode conflict popup
   "looks very bad with no background and not controller friendly" and a request to "create a
   different popup for fullscreen view whichich looks like the decky plugin popup using the currently
   applied theme elements."

### Bugs found and fixed

**Worktree-path mistake in the verification checklist (my own error).** `tests/testenv.ps1` (with the
conflict-seeding fix) exists only on `playnite-plugin-group-3`, which is checked out solely in this
worktree (`.claude/worktrees/pr37-code-review-1d577d`) — confirmed via `git worktree list`, which also
showed two OTHER pre-existing worktrees for unrelated branches
(`phase-13-playnite-plugin-df3530`, `zealous-gagarin-acc5db`). Corrected the checklist to `cd` into
this worktree, not the main repo root.

**The WSL lease script was reading the wrong machine's credentials.** The original PowerShell version
read `X-SaveLocker-Token` from `%LOCALAPPDATA%\SaveLocker-test\SaveLocker\api-token` — that's
**WinTest's own local loopback-API token** (for talking to the Windows tray on `:5188`), not a
server-facing credential at all. Leasing with it (or with a stale hardcoded "LinuxTest key" copied from
an old `up` output, now invalid after this session's rig rebuild) meant the lease request never
actually authenticated as a genuinely different machine — explaining "i'm leasing the game on the same
device." Root-caused by reading `tests/testenv.sh` directly: LinuxTest's real state lives natively
inside WSL at `$XDG_DATA_HOME/SaveLocker` (default `$HOME/savelocker-test/SaveLocker`), and its
`config.json`'s own `ApiKey` field is the correct server-facing credential. Also confirmed
`SyncService.ListGamesAsync()` (`src/Server/Services/SyncService.cs`) returns **every** game on the
server with no per-machine filter, and `GameDto` (`src/Shared/Contracts.cs`) serializes as `id`/`name`
— so the whole round trip (list games, find Minit, take the lease) can run entirely inside WSL using
only LinuxTest's own key, with zero Windows-side credentials, genuinely testing "held by another
machine":
```bash
STATE="${SAVELOCKER_LINUX_STATE:-$HOME/savelocker-test}/SaveLocker"
apiKey=$(jq -r '.ApiKey' "$STATE/config.json")
games=$(curl -s -H "X-Api-Key: $apiKey" http://localhost:5080/api/games)
minitId=$(echo "$games" | jq -r '.[] | select(.name=="Minit") | .id')
curl -s -X POST -H "X-Api-Key: $apiKey" "http://localhost:5080/api/games/$minitId/lease"
```

### Fullscreen-mode investigation and fix

Confirmed rather than guessed, per this session's own established standard:

- **Detecting Fullscreen mode**: reflected the real, installed `Playnite.SDK.dll` directly (same
  technique the original Group 3 build used for every other SDK signature) — `PlayniteApi
  .ApplicationInfo.Mode` returns `Playnite.SDK.ApplicationMode.Desktop`/`.Fullscreen`. No code in
  `SaveLockerPlugin.cs`/`ConflictResolveWindow.cs` had ever checked this.
- **Why the existing window broke in Fullscreen**: fetched Playnite's own GitHub source
  (`Playnite.FullscreenApp/App.xaml`, `Themes/Fullscreen/Default/Constants.xaml`,
  `Themes/Fullscreen/Default/Media.xaml`). Fullscreen's theme resource set genuinely has no
  `PopupBackgroundBrush`/`PopupBorderBrush` — those are Desktop-only keys — so every `Border`
  Background/BorderBrush in the existing window resolved to nothing there, reproducing the exact
  "unstyled white box" bug from earlier in the session for a completely different reason. `TextBrush`
  does exist under the same name in both theme sets, so text itself was fine; only backgrounds/borders
  broke. Fullscreen's own real keys: `ControlBackgroundBrush`, `OverlayMenuBackgroundBrush`,
  `OverlayBrush`, `GlyphBrush` (accent).
- **Also found**: Fullscreen's `App.xaml` merges no window-chrome-style dictionary at all (Desktop's
  `StandardWindowStyle.xaml` has no counterpart there) — a normal OS-chrome dialog would look out of
  place floating over Playnite's own borderless big-picture UI regardless of brush fixes.

**Built** `ConflictResolveWindowFullscreen.cs` (new file, SaveLocker-Playnite repo): a borderless
(`WindowStyle.None` + `AllowsTransparency`), full-screen dimmed overlay (`OverlayBrush` backdrop) with
a centered, drop-shadowed card (`OverlayMenuBackgroundBrush`) — shaped like Decky's own floating-modal-
over-dimmed-backdrop conflict popup rather than an OS dialog. Bigger fonts/touch targets than the
Desktop version, `GlyphBrush` for the selected-panel accent instead of hardcoded `DodgerBlue`, and
explicit `PreviewKeyDown` handling for Left/Right (toggle device/cloud)/Enter (resolve)/Escape (cancel)
— added because whether Playnite's own gamepad-to-input translation reaches a plugin-created secondary
`Window` at all is genuinely unverified, so real keyboard input is covered directly rather than relying
solely on WPF's default Tab-focus chain. `SaveLockerPlugin.ResolveConflictInteractively` now branches
on `PlayniteApi.ApplicationInfo.Mode` to construct the right window class.

One build error caught immediately: `using System.Windows.Effects;` doesn't exist —
`DropShadowEffect` is in `System.Windows.Media.Effects`. Fixed; `dotnet build --no-incremental`
succeeded, `Install-ToPortable.ps1 -SkipBuild` installed it into the portable Playnite.

### Verification

- WSL lease script: traced correct paths/fields from `testenv.sh` and `Contracts.cs` source directly;
  not yet re-run live by the user as of this write-up.
- Fullscreen window: build-verified only. **Not yet hardware-verified** — neither the visual result nor
  (critically) whether a real controller actually drives the new keyboard handling. Both flagged as
  open, not assumed working.

### Not done

- User has not yet re-tested Fullscreen mode with the new window, or confirmed the corrected WSL lease
  script.
- Controller-driven navigation of `ConflictResolveWindowFullscreen` is unverified — the biggest
  remaining unknown in this change.
- Nothing in the `SaveLocker-Playnite` repo committed yet as of this write-up (uncommitted:
  `ConflictResolveWindowFullscreen.cs` new, `SaveLockerPlugin.cs` modified).

---

## 2026-09-16 — "Link to SaveLocker" UX: menu-action threading fix, non-blocking dialog feedback, SaveLocker tag + startup backfill

**Repo:** `SaveLocker-Playnite`, branch `playnite-plugin-group-4`. Nothing committed yet.

### Request sequence

1. Detailed instructions to test Group 4 (the Phase 12 "Link to SaveLocker" enroll/link popup) via
   `testenv.ps1`; then where `shown-link-nudges.txt` lives.
2. "i cant see any of theis when running any game" — asked to verify whether the problem was
   implementation or instructions, then give corrected instructions.
3. Tested "Conflict 2", got no notification, then pivoted from a bug report into an explicit feature
   spec: *"i don't like playing and getting a notification to then link. i want a button to link. just
   click it it verfyis if there is a an automatic way to enroll and just enroles and if not it shows
   the popup. if enrolled the button is replaced with a check. similar to the How long to beet button
   (H) i would love if its on the far right and in the top right will be even better."*
4. "i cant find the button."
5. "link to savelocker doen't do anything or shwo anyfeedback."
6. "when i press Link TO Save locker a dialogue should appear giving me feedback that the linking is
   in progress and when done another one shows up saying the link is don if it was successful and
   offering to sync."
7. "can the progress of linking be shown in the background without intrupting the player and alos can
   an icon of savclockewr and acheck mark be added to the game (in the list or on the game age to show
   that this game is enrolled?"
8. "if i first installed the plugin and have games already linked will the tag be added automaticlly?"
9. This request: append a summary to `progress.md` and save a standalone summary to
   `session_summary.md`.

### Bugs found and fixed

**The button (`GetGameViewControl`) was invisible under Harmony.** Read Harmony's own
`DetailsViewGameOverview.xaml` directly: it hardcodes a fixed allowlist of `{PluginName}_PluginButton`
elements (HowLongToBeat, SuccessStory, GameActivity, etc.) rather than looping over installed plugins
generically, and SaveLocker isn't on that list — so `GetGameViewControl` is never invoked under this
theme at all. Fixed by adding a theme-independent `GetGameMenuItems` right-click menu entry as the
primary, reliable surface (Playnite renders its own menu regardless of theme), keeping the button as a
secondary bonus for themes that do support it.

**"Link to SaveLocker" menu item did nothing, no feedback.** Root cause: the menu `Action` wrapped the
link logic in `Task.Run(async () => ...)`, moving it to a threadpool thread with no
`SynchronizationContext`. The fallback path opens a real WPF `Window` (the manual picker) via
`CreateWindow`/`ShowDialog()`, which WPF only allows on the STA UI thread — so the call threw
immediately and the exception was silently lost inside the unobserved fire-and-forget task. Fixed by
removing `Task.Run` and running the whole action as a direct `async` lambda kept on the UI thread.

**`ActivateGlobalProgress`'s own modal dialog turned out to be the "interrupting" the player later
objected to.** After first adding a genuine "linking…"/"linked, sync now?" dialog pair via
`ActivateGlobalProgress` (mirroring `OnGameStarting`'s pre-launch-sync check), the very next request
asked for the opposite: progress shown in the background without blocking browsing.
`ActivateGlobalProgress` is Playnite's own blocking/modal progress overlay — correct for the pre-launch
gate (nothing should launch before that check finishes) but wrong for a background link check with
nothing at stake. Reverted the check phase to a plain awaited async chain with a transient toast
("SaveLocker: linking "X"…", removed via `INotificationsAPI.Remove` once done) — plain `await` never
blocks the UI message loop, unlike `ActivateGlobalProgress`'s pump. Only the outcome (a real decision
point — "linked, sync now?" or the manual picker) still shows an actual dialog. Same treatment applied
to `SyncNowAction`.

### What was built

- `LinkAction.cs` (new) — shared "click and link" logic used by both the button and the menu item:
  Tier 1 (already-tracked, via `GameMatcher`) → Tier 2 (automatic manifest lookup + immediate enroll,
  no confirm click) → falls back to the existing 5-tier interactive popup (`LinkToSaveLockerWindow`)
  only when neither resolves. Shows a background toast while checking, then either a "linked — sync
  now?" Yes/No dialog or the popup.
- `LinkStatusButton.cs` (new) — the `GetGameViewControl` button (secondary surface, confirmed inert
  under Harmony): "Link to SaveLocker" / "✓ Synced" based on `GameMatcher.FindMatch`, delegates its
  click to `LinkAction`.
- `SyncNowAction.cs` (new) — the "sync now" a player can opt into from the linked-dialog without
  waiting for the next real launch. Runs the same pre-launch-sync gate `OnGameStarting` uses (a toast
  while checking, not a modal), and hands a genuine `Blocked` decision to `ConflictResolver`. Both
  callers fire it without awaiting (`_ = SyncNowAction.RunAsync(...)`, explicit discard to keep the
  build warning-free), so it wraps its own top-level try/catch rather than relying on a caller that
  isn't watching.
- `ConflictResolver.cs` (new) — the interactive conflict UI extracted out of
  `SaveLockerPlugin.OnGameStarting` (was a private method there, `ResolveConflictInteractively`) so
  `SyncNowAction`'s on-demand sync and the real launch-blocking gate share exactly one copy of that
  handling instead of a second, un-hardware-verified one.
- `LinkedTag.cs` (new) — since the SDK has no grid/list icon extension point at all (confirmed by
  reflecting every method on the `Plugin` base class — `GetGameViewControl` is the *only* per-game
  visual hook, details-page-only, theme-opt-in, and Harmony doesn't opt in), tags linked games with a
  `SaveLocker: Linked` Tag — Playnite's own generic per-game mechanism, filterable from the sidebar
  regardless of theme even though Harmony specifically doesn't bind to Tags either (confirmed by
  inspection: no `Tags`/`TagIds` binding anywhere in its XAML, including its filter panel).
  `LinkedTag.Ensure()` is idempotent.
- `SaveLockerPlugin.OnApplicationStarted` (new override) — backfills `LinkedTag` across the whole
  library once per app start (fetches the tracked-games list, matches every library game via
  `GameMatcher`, tags any hit), added specifically because the earlier per-launch-only tagging meant an
  already-linked game wouldn't show the tag until its *next individual launch*. Runs off the UI thread
  (`Task.Run`); safe, since nothing in it touches WPF.
- `LocalApiClient.AgentUrl` (new property) — lets `ConflictResolver`'s error-fallback message read the
  configured agent URL without its own reference to `SaveLockerSettings`.
- `LinkToSaveLockerWindow.Finish` — now takes the resolved `TrackedGameDto` (when known) and offers the
  same "sync now?" dialog the automatic path shows, instead of a toast-only close, so all four tiers end
  the same way.

### Verification

- `dotnet build src/SaveLocker.Playnite.csproj -c Release` clean (0 Warnings, 0 Errors) after every
  change this session, including two CS4014 "not awaited" warnings caught and fixed via explicit `_ =`
  discards.
- Reinstalled into the portable test Playnite (`D:\Projects\SaveLocker\Playnite-Test`) and relaunched
  after each change; `playnite.log` confirmed `Loaded plugin: SaveLocker, version 0.1.0` with no
  adjacent errors every time.
- Both bug root causes (invisible button, silent menu action) were confirmed by reading real source
  (Harmony's XAML; WPF's STA-thread requirement for window creation) rather than guessed; likewise the
  "no grid/list icon extension point" and "Harmony doesn't bind Tags" findings were confirmed via SDK
  reflection and direct XAML inspection, not assumed.
- **Not yet confirmed live by the user**: the background-toast linking flow, the "linked, sync now?"
  dialog, the sync-now gate/conflict path, the tag itself, and the startup backfill are all
  build-verified and installed but not yet click-tested in a running session.

### Not done

- Nothing in `SaveLocker-Playnite` committed yet — everything above is uncommitted working-tree changes
  on `playnite-plugin-group-4`.
- The pre-existing port-5188 local-API 401 issue (a stuck test-agent process rejecting its own valid
  token, from an earlier segment of this same session) remains unresolved — this session couldn't kill
  the offending process ("Access is denied"); only the user can close it. Blocks live end-to-end testing
  of the automatic-enroll path until resolved (the code still degrades gracefully to the manual popup
  either way).
- `LinkStatusButton`/`GetGameViewControl` remains unverified on any theme that might actually render it
  (e.g. Playnite's stock Default theme) — confirmed only as a no-op under Harmony.

---

## 2026-09-16 (cont'd) — Playnite plugin Group 5 shipped (Phases 13/14/15/17), Docker layer-caching fix, Default-theme dead-code finding, dialog-based sync/conflict feedback

**Repos/branches:** `SaveLocker-Playnite` worktree `.claude/worktrees/playnite-plugin-group-5`, branch
`playnite-plugin-group-5`. `SaveLocker` worktree `.claude/worktrees/group-5-playnite-plugin-3d3aae`,
branch `claude/group-5-playnite-plugin-3d3aae`. Neither branch pushed; no PRs opened yet — explicitly
deferred pending the user's go-ahead.

### Request sequence

1. "Start implementing group 5 knowing that the playnite plugin repo is in
   D:\Projects\SaveLocker\SaveLocker-Playnite. create a work tree there and an new branch and do the
   changes needed."
2. "tell me in non technical terms what was implemented please tell me detailed instructions to how to
   test this using testenv."
3. An unrelated Docker question: why the runtime stage's `apt-get install curl` layer took 17.7s and
   wasn't cached — then "Yes please" to both proposed fixes (Dockerfile reorder + GitHub Actions
   `type=gha` build cache).
4. Bug report contradicting an earlier, unverified assumption: "there is no chips or savlocker link
   button in default theme" (i.e. even Playnite's stock Default theme, not just Harmony).
5. A UX spec for the "Sync now"/"Resolve conflict…" menu items: "when i click sync before i link a
   notfication apears that i hasent synced. i want a dialog to open saying the same thign and a link
   and acancel button. the same for conflicts i whant a dialoge always stating what appned rather than
   a notification because a notification doesn't give instant feedback. in conflict show the game isnt
   linked if it wasnt linked and show no conflicts found if its alreadyy linked."
6. This request: append a summary to `progress.md` and save a standalone summary to
   `session_summary.md`.

### Group 5 implementation (Phases 13, 14, 15, 17)

**Phase 13 — status chip (`SaveLocker-Playnite`).** `LinkStatusButton.cs` renamed to
`GameStatusControl.cs` (`git mv`), rebuilt as a `ContentControl` (compile error otherwise: `StackPanel`
doesn't derive from `Control`, and `GetGameViewControl`'s return type does) hosting a chip + action
button reflecting Not linked / Agent offline / Not synced yet / In sync / Conflict, backed by two new
`LocalApiClient` calls (`GetSyncStatusAsync`, `GetPlaynitePluginStatusAsync`) and two new DTOs
(`SyncStatusDto`, `PlaynitePluginStatusDto`) in `Contracts.cs`.

**Phase 14 — self-update status plumbing (`SaveLocker`, main repo).** `AgentApiServer.cs` gained a
`PlaynitePluginStatusDto` record, a `_playnitePluginStatus` injected delegate (default:
`NotApplicable`), and `GET /api/playnite-plugin`. `PlaynitePlugin.cs` (Agent) gained
`StatusAsync(config, log)` wrapping the existing `CheckAsync(apply: false)`. `TrayApp.cs` wires
`playnitePluginStatus: () => PlaynitePlugin.StatusAsync(...)` into the `AgentApiServer` constructor.
On the plugin side, `SaveLockerPlugin.OnApplicationStarted` now also calls a new
`CheckSelfUpdateAsync()` alongside the existing tag-backfill task — the plugin polls the tray's new
route and, if a newer plugin version is available, shows a notice telling the player to restart
Playnite to pick it up, instead of never finding out.

**Phase 15 — automated test infra (`SaveLocker-Playnite`).** New
`tests/SaveLocker.Playnite.Tests/` xUnit project (net462): `GameMatcherTests.cs` (pure-logic coverage
of `GameMatcher.FindMatch`/`FindByPathOrName`/`MapStore`, deliberately excluding the Steam-AppID tier
since `Game.Source` is always null outside a live Playnite DB — confirmed by reflection, not assumed)
and `LocalApiClientTests.cs` (`HttpListener`-stub-based coverage of `LocalApiClient`, no real backend
needed). `SaveLocker.Playnite.csproj` gained `<InternalsVisibleTo Include="SaveLocker.Playnite.Tests" />`.
25/25 passing.

**Phase 17 — release CI (`SaveLocker-Playnite`).** New `.github/workflows/release.yml`: fetches
`Playnite.SDK.dll` from `JosefNemec/Playnite`'s portable `.7z` release, builds Release, packages
`SaveLocker.zip`/`SaveLocker.pext`/`SHA256SUMS.txt`, publishes via `softprops/action-gh-release`. Never
exercised by an actual tag push yet (no real GitHub Release minted) — deferred pending the user's
go-ahead, same as pushing the branches.

Docs updated in both repos to match: `SaveLocker-Playnite/docs/REPO_MAP.md` (documents
`GameStatusControl.cs`, `LinkAction.cs`, `LinkedTag.cs`, `SyncNowAction.cs`, `ConflictResolver.cs`, the
new `tests/` dir, `.github/workflows/release.yml`), `docs/Build and Run.md` (new "Automated tests
(Phase 15)" section), `docs/CONTEXT.md` (new top section documenting Group 5's status, deviations, and
a "whoever picks this up next" handoff list); `SaveLocker`'s own `docs/tasks/playnite-plugin/plan.md`
and `implementation-grouping.md` Status tables updated from "⏳ Not started" to "✅ Built 2026-09-16"
for Phases 13/14/15/17, with honest caveats (not hardware-verified; status chip confirmed dead code
under every existing theme — see below).

### Docker layer-caching fix (tangent, `SaveLocker` main repo)

Root cause of the 17.7s uncached `apt-get` layer: `src/Server/Dockerfile`'s runtime stage ran
`apt-get install curl` *after* `COPY --from=build /app ./`. Docker's build cache is chained per stage —
any layer after one that's invalidated is invalidated too, regardless of whether its own instruction
text changed — and the app binary changes on nearly every build, so `apt-get` was re-fetching from
scratch every single time despite having zero actual dependency on the app. Fixed by moving the
`apt-get` line before the `COPY --from=build`. Also added `docker/setup-buildx-action@v3` (required for
the `type=gha` cache backend — the default `docker` Buildx driver can't use it) and
`cache-from: type=gha` / `cache-to: type=gha,mode=max` to `docker-publish.yml`'s
`docker/build-push-action@v6` step, so CI benefits the same way local builds now do (GitHub-hosted
runners otherwise start with an empty Docker cache on every run). Verified locally with a real
before/after build: `apt-get` now shows `CACHED` after a source-only change that still correctly
invalidates the `dotnet publish`/`COPY --from=build` layers.

### Default-theme bug: `GameStatusControl` confirmed dead code under any existing theme

The user reported no chip/button under Playnite's *stock Default* theme either — contradicting an
earlier, unverified assumption in a prior reply that Default would show it (only Harmony's own
allowlisted-button XAML had actually been checked). Root-caused via `grep.app` code search of
`JosefNemec/Playnite`'s real source for `ControlTemplateTools.InitializePluginControls`:
`GetGameViewControl` is only ever invoked for a plugin that (a) registered via
`AddCustomElementSupport` — SaveLocker never has — **and** (b) whose active theme's XAML contains a
named `ContentControl` (`"{SourceName}_{ElementName}"`) for that plugin — no stock theme (Default
included) defines one for SaveLocker. Conclusion: `GameStatusControl`/the `GetGameViewControl` override
can never render under any theme that exists today; only `GetGameMenuItems` and Tags are genuinely
theme-independent. **Not yet fixed or removed** — the user was asked whether to delete the dead code or
leave it in place for a hypothetical future custom theme, and has not yet answered (their next message
was the dialog-feedback request below instead).

### Dialog-based feedback for "Sync now" / "Resolve conflict…" (`SaveLocker-Playnite`, commit `c3273be`)

Per the user's explicit UX request, replaced notification-based feedback with real dialogs in
`SaveLockerPlugin.cs`. Reflected `Playnite.SDK.dll` first to confirm the mechanism:
`IDialogsFactory.ShowMessage(string, string, MessageBoxImage, List<MessageBoxOption>)` returns the
clicked `MessageBoxOption`, and `MessageBoxOption(title, isDefault, isCancel)` is how a literal
Link/Cancel button pair is built (rather than the stock Yes/No/OK sets).

- **"Sync now" on an unlinked game** now opens a dialog — *"'{game}' isn't linked to SaveLocker yet."*
  — with **Link**/**Cancel** buttons instead of a toast; Link runs the same auto-match/enroll chain as
  the "Link to SaveLocker" menu item (new `OfferLinkAsync(Game game)` helper).
- **"Resolve conflict…"** always shows a dialog stating the outcome: the same not-linked dialog when
  unlinked; *"No conflicts found for '{game}'."* when linked with nothing open; the real resolve window,
  unchanged, when a conflict genuinely exists.
- **Agent-unreachable** is now also a dialog (`ShowAgentUnreachableDialog()`) instead of a notification,
  shared by both menu items.

Fixed one compile error along the way: `MessageBoxImage.Information` needed the
`System.Windows.` prefix (`SaveLockerPlugin.cs` has no `using System.Windows;`), matching the
fully-qualified style already used elsewhere in the file. `docs/CONTEXT.md` in `SaveLocker-Playnite`
has **not yet** been updated to mention this follow-up commit — a documentation gap flagged for next
time.

### Verification

- `dotnet build src/SaveLocker.Playnite.csproj -c Release --no-incremental` clean (0 warnings/errors)
  after every change this session, including the dialog-feedback follow-up.
- `dotnet test tests/SaveLocker.Playnite.Tests/... -c Release` — 25/25 passing, including after the
  dialog-feedback follow-up.
- Main-repo build/test verification (`AgentApiServer.cs`, `PlaynitePlugin.cs`, `TrayApp.cs`) run from
  the correct worktree after an earlier mistake was caught and fixed (see below);
  `agent-ui/src/api-types.ts` regenerated and diffed clean (only the new route/schema, plus pre-existing
  harmless alphabetical churn).
- Docker: a real local before/after build confirmed the `apt-get` layer now caches correctly.
- None of Group 5 has been hardware-verified inside a real running Playnite yet (menu items, dialogs,
  and the self-update notice are all build-verified/installed but not click-tested live).

### Mistake caught and fixed mid-session

All build/test/verification commands were initially run from `D:/Projects/SaveLocker/SaveLocker` (the
main checkout, branch `main`) instead of the worktree with the actual Phase 14 edits
(`.claude/worktrees/group-5-playnite-plugin-3d3aae`) — caught when the new `/api/playnite-plugin` route
never appeared in a regenerated OpenAPI doc. This also left a dirty, wrongly-regenerated
`agent-ui/src/api-types.ts` in the main checkout on branch `main`, reverted via
`git checkout -- agent-ui/src/api-types.ts` after confirming via `git status --short` it was the only
dirty file there. All builds/verification redone correctly from the worktree afterward (including a
fresh `npm install` in the worktree's own `agent-ui`, which had no `node_modules` at all — the
documented recurring fresh-worktree gotcha).

### Follow-up: dead code removed, branches renamed, PRs opened

The user answered the open `GameStatusControl` question directly: "GameStatusControl (the status chip)
is dead code — it can't render under any theme that exists today, and there's no code fix for that; it
would need either a theme author to add a `SaveLocker_...` slot for us, or SaveLocker shipping its own
custom theme. Neither is realistic right now" — and asked to remove it, rename this repo's branch to
`playnite-plugin-group-5` (matching `SaveLocker-Playnite`'s own branch name), and open a PR in both
repos.

- **`SaveLocker-Playnite`:** `GameStatusControl.cs` deleted, the `GetGameViewControl` override removed
  from `SaveLockerPlugin.cs`, and every stale doc-comment cross-reference to it (`LinkAction.cs`,
  `LinkedTag.cs`, `LocalApiClient.cs`, `docs/REPO_MAP.md`) updated to explain it was tried and removed
  rather than silently deleted. `GetSyncStatusAsync`/`SyncStatusDto` were kept — the "Resolve
  conflict…" dialog still calls them. `dotnet build` clean (0/0), `dotnet test` 25/25 unchanged.
  Committed as `5498ee7` (removal) and `3f9293f` (docs, including the still-pending `c3273be`
  write-up from the entry above). Branch already named `playnite-plugin-group-5`.
- **`SaveLocker` (this repo):** branch renamed `claude/group-5-playnite-plugin-3d3aae` →
  `playnite-plugin-group-5` (`git branch -m`) to match. `plan.md`'s Phase 13 row and
  `implementation-grouping.md`'s Group 5 row updated to record the removal instead of describing a
  chip that no longer exists.
- Both branches pushed; PRs opened in both repos — see the PR links in `CONTEXT.md`/session notes.

### Not done

- Phase 17's release workflow never exercised by a real tag push.
- Hardware verification of Group 5 inside a real running Playnite.

---

## 2026-09-17 — Playnite Groups 6/7 + Phase 18/19 testenv re-verification, README update

**Branches:** `claude/playnite-group6-phase17-82e72b` (main repo, worktree `group-5-playnite-plugin-3d3aae`); `playnite-plugin-group-6` (SaveLocker-Playnite, worktree `playnite-plugin-group-6`).

### What was built
- `PlayniteLibrary.cs` (new): LiteDB 4 reader for Playnite's `games.db` (`Game` collection, shared read-only connection), mapping installed games to `ScanCandidate`s with `Store`/`SteamAppId`/`HasSteamCloud` when the plugin id resolves to a known store.
- `GameScanner.ScanPlayniteLibraryAsync`: 4th broad-sweep candidate source, fault-isolated via `PlayniteLibrary.SafeRead()`.
- agent-ui: `Playnite` filter chip in Add Games, `PlaynitePluginCard` (Overview) mirroring `DeckyPluginCard` but with a self-install button.
- `PlaynitePlugin.CardStatusAsync`/`InstallFirstTimeAsync` + two new local-API routes (`GET/POST /api/playnite-plugin/status`, `/install`).
- `SAVELOCKER_PLAYNITE_PATH` env var override added to both `PlayniteLibrary.DataRoot` and `PlaynitePlugin.PlayniteDataRoot`, wired through `testenv.ps1`'s `Use-TestEnvVars`/`Clear-TestEnvVars` — fixes portable Playnite installs (library/Extensions live beside the exe, not under `%AppData%`) being invisible without it.
- `tests/SaveLocker.Agent.Tests` (new xUnit project): 11 tests covering `PlayniteLibrary` mapping/edge cases.
- SaveLocker-Playnite: `docs/addon-submission/addon-manifest.yaml` + root `installer.yaml` prepared for the JosefNemec/PlayniteAddonDatabase submission (Phase 16 prep); README.md updated with current shipped state, a fork-compatibility note (requires `Marwanello/SaveLocker`, not upstream `SkorcherX/SaveLocker`), and the required paragraph removed per explicit request.

### Bug found via testenv (third one this feature has produced)
Portable Playnite installs keep their `library`/`Extensions` beside their own executable, not under `%AppData%\Playnite`. Both `PlayniteLibrary.DataRoot` and `PlaynitePlugin.PlayniteDataRoot` only checked `%AppData%`, so testing against `-PlaynitePath` (this project's actual test rig for portable Playnite) made the entire Phase 18 source invisible. Fixed with the `SAVELOCKER_PLAYNITE_PATH` override above; `DataRoot` now checks that env var first, falling back to `%AppData%\Playnite` only when unset.

### Verification — via testenv (per standing instruction)
- `.\tests\testenv.ps1 build` then `up`, against the real portable `Playnite-Test` instance (`testenv.local.ps1` sets `SAVELOCKER_PLAYNITE_PATH`).
- Windows test agent (:5188) rescan found all 6 real Playnite-library games, no read errors.
- `GET /api/playnite-plugin/status` correctly reported the plugin installed once it was; agent-ui Overview card showed "INSTALLED v0.1.0".
- Restarted `Playnite-Test` and confirmed via its own `playnite.log`: `ExtensionFactory:Loaded plugin: SaveLocker, version 0.1.0` — first confirmed load of a real tagged release build inside a live Playnite process.
- A pre-existing v0.1.0 GitHub Release (tag + `SaveLocker.zip`/`.pext`/`SHA256SUMS.txt`) was discovered already published; checksum verified against `SHA256SUMS.txt`.

### Not done
- Full manual click-through inside Playnite (conflict gate blocking a launch, right-click menu items, Link to SaveLocker popup) — still outstanding for Phase 16.
- `installer.yaml`/plugin changes only pushed to the `playnite-plugin-group-6` branch, not `main` — the `raw.githubusercontent.com` URLs in the submission manifest won't resolve until that lands.
- The actual PlayniteAddonDatabase fork + submission PR — explicitly deferred pending the click-through above, per "verify first, then submit."


---

## 2026-09-20 — Checkpoint UI Group 3: agent foundation, trimmed Overview, agent Sync all

**Branch:** `claude/group-3-ui-redesign-c16953` (local, not pushed, no PR opened). Code: `2705ce4`; the vault changes are a separate `Docs:` commit.

### Request sequence

1. "Implement group 3 in the ui redesign task please." Read `tasks/checkpoint-ui/` (`implementation-grouping.md` Group 3, `implementation.md` Phases 1/3/5, `plan.md`, the prototype's agent screens) and the Groups 1–2 handoff before writing anything.

### What was done

- **Scope call, made before building:** Phase 3 item 5 (per-game "Sync this game") was listed in Group 3 but is not buildable there — the agent has no game page (Group 4 builds the Games tab) and no per-game sync route (`POST /api/sync` takes no game filter; `pre-launch-sync`/`post-exit-sync` are launch-gate routes). Moved to Group 4 in the docs (`➡️ Moved`, per the status-table convention) rather than adding a button to Settings that the Games tab would delete.
- `agent-ui/src/tokens.css` (hand-kept copy of `web`'s tokens, dark base / light opt-in) + `ui.css` (reset, keyframes, the `sl-` primitive classes) + `@fontsource/archivo` replacing Inter; `components/ui/` — `Button`, `Card`, `Chip`, `Stat`, `Banner`, `Toast`.
- `StatusHeader.tsx` rewritten as the strip on every page: status, primary Sync all, live progress. `useActivity.ts` — one shared poll behind `useSyncExternalStore`, one slice per hook.
- `OverviewView` trimmed (3 stats, one banner, Next up, Recent); `RecentCard` expands inline to the full log; `ActivityCard` deleted; launch-setup/Decky/Playnite cards moved to `SettingsView`. `Sidebar` and the `App` shell converted (real `<button>`s, landmarks, tokens).
- **Bug fixed in moved code:** `handleSynced` read `view` from the render in which Sync was pressed, so its "don't pop the overlay over Conflicts" guard never saw a later navigation. Now a ref.

### Verification — via testenv (per standing instruction)

- `build` (console, Windows, Linux) → `clean` → `build` → `conflict -Windows -Wsl` → `up -Only linux`; browsed the WSL agent's UI at `:5187`.
- Real Sync all on a real seeded conflict: busy → "Sync all complete." toast (dismissed at ~2.6 s) → conflict pop-up 7 ms later. Pressed from Overview and then navigating to Conflicts with the sync slowed to 3 s: no pop-up over Conflicts.
- A progress tick re-renders only the progress area: 16 DOM mutations there, 0 in the page and at the button, over four ticks (fetch intercepted to fake a 25 MB push).
- Every tab stop shows a 2px accent focus ring; both themes render; contrast walk over 43 text nodes in both themes — only the seven 10px `--color-faint` eyebrows are under 4.5:1 (3.31 dark / 3.55 light: the plan's own token).
- Not-connected, conflict, lease-warning and pulling states checked by intercepting `fetch` against the Windows test agent's UI. `agent-ui` `tsc -b && vite build` and `oxlint` clean (two pre-existing warnings). No C# changed — no suite re-run, `api-types.ts` not regenerated.

### Found along the way

- The Windows test agent was mapped to eight of the maintainer's real save folders (rig warned). Sync all was not pressed there; `clean` wiped it.
- `testenv.ps1 sync` skips a new (untracked) directory — worked around with `git add`; filed in `Backlog.md`. `conflict -Wsl` alone seeds no conflict. Both in `Gotchas.md`.
- `--color-faint` is below WCAG AA — a maintainer decision (touches `web`, `agent-ui` and `Ui/Theme.cs`).

### Not done

- Phase 3 item 5 (moved to Group 4); the WebView2 tray window itself and a real Deck were not exercised (the same bundle was loaded in a browser).
- No PR opened, nothing pushed.

## 2026-09-20 (later) — `testenv.ps1 sync` no longer drops new directories, deletions or renames

**Branch:** `testenv-sync-untracked-files` (on top of the Group 3 branch — the Gotchas bullet and Backlog line it closes live there, not on `main`).

### What was built
- `testenv.ps1 sync` builds its list from `git status --porcelain=v1 -z --untracked-files=all` and classifies each path by what is on disk: present → `M<TAB>path`, absent → `D<TAB>path`. `testenv.sh` `cmd_sync` copies the `M` lines as before and `rm`s the `D` lines from the clone (refusing any path that could leave it); a bare path with no tab is still a copy.
- Fixes three silent failures at once: an untracked directory (one porcelain entry → `skip (gone)`), a deleted tracked file (survived in the clone, which is checked out at the committed tree first), and a staged rename (`old -> new` matched no path; `Test-Path` threw on the `>`).

### Verification
- One throwaway change — a nested new directory, a file with a space in its name, a deleted tracked file and a `git mv` — run through the OLD script from `HEAD` (reproduced: `skip (gone): tests/_sync_probe/`, the `Test-Path` error, nothing deleted) and then the new one: "syncing 6 changed and 2 deleted file(s)", both new files present in the `Ubuntu` clone at `~/SaveLocker` with correct content, the deleted file gone, the old name gone, the new name present. Probe removed from both trees afterwards and the clone re-synced clean.
- `bash -n tests/testenv.sh` and a PowerShell 5.1 parse of `testenv.ps1` are clean.

### Not done
- A file copied in while untracked and later deleted on the Windows side without a commit still survives in the clone (no `git clean`, deliberately) — noted in `Gotchas.md`.

## 2026-09-20 — Artwork: backfill when a key is added, opaque icons in the list, anti-aliased thumbnails, cover/icon picker

**Branch:** `steamgriddb-art-picker` (from `main`; the Checkpoint Group 3 branch was left as it was).

### What was built
- **Key added after games exist → art fills in.** `POST /settings/steamgriddb-key` now queues `ArtBackfillService` (background, one game at a time, coalescing) for every game with no cover or no icon, and answers at once with `gamesQueued` and a message that says so. Only MISSING art is fetched (`RefreshArtAsync(onlyMissing)`), so a hand-picked cover is never replaced; the explicit *Refresh art* button still fetches the default again.
- **The list shows the game's icon, not cropped box art**, and the default icon is now the first fully **opaque** PNG among the top six candidates (`ArtImages.IsFullyOpaque`), falling back to the first that downloads. The grid wall and game page keep the cover.
- **The aliasing** was reproduced first (a synthetic 600×900 cover of 1 px lines, drawn at 38 px and at 137 px, beside a Lanczos copy: the original showed moiré and broken text, the copy was clean). Fix: `GET /art/{game}/{grid|icon}.{ext}?w=` (`ArtThumbnails`) — on-demand, disk-cached, Lanczos in linear light, allowlisted widths, never upscales, opaque covers as JPEG. Works on art that is already cached, no migration. `web/src/art.ts` builds the `srcSet`; a 38 px tile now loads a 64 px file instead of the full 600×900.
- **Cover/icon picker.** A pen appears over the game card's cover on hover, on keyboard focus, and always on touch screens; it opens `ArtPicker` inline (no modal) with two strips of five SteamGridDB options each, a pager per strip, and a Current tile. Choosing saves at once. New routes `GET /games/{id}/art/options` and `PUT /games/{id}/art/{kind}`; previews are fetched and shrunk by the server and inlined as `data:` URIs (the CSP allows nothing else). The listing is cached 10 minutes and sliced, so it does not depend on SteamGridDB's page size.
- **Rig:** `testenv.ps1 -ConsoleEnv` (env for the console container) + `tests/sgdb-stub.py` (a stub SteamGridDB) so the real Docker console can be driven with art and no key. Documented in Build and Run → *Testing artwork*.

### Bugs found on the way
- `IsFullyOpaque` first trusted ImageSharp's `PixelType.AlphaRepresentation`, which called an RGBA PNG with one clear pixel opaque — the suite's stub (transparent icon listed first) caught it, the stub log showing the second icon was never even requested. Now reads the pixels ([[Gotchas]] → *Web console*).
- Keyboard: the picker sits after every control on the game card in tab order, so opening it from the pen left focus far away and Escape dead. It now takes focus on open; the card hands it back to the pen on close.

### Verification
- `run-console-security-tests.ps1` **137/137** (was 105): +32 checks — backfill, hand-picked cover kept, paging across a 7|5 API boundary at two requests for four pages, previews inline/shrunk/JPEG, hostile URLs refused and never contacted, opaque icon chosen, thumbnails right-sized/cached/allowlisted/stale-on-replace. First run 136/137; the one failure was the real bug above.
- `web` `tsc -b`, `oxlint` and `vite build` clean; `dotnet build` clean; `openapi.json` regenerated and diffed (additions only — the scratch server's own `servers[0].url` restored) and `api-types.ts` regenerated.
- **Through `testenv` (the real Docker console, pointed at the stub):** added 8 games with no key → pasted a key in Configuration → the alert read "…Fetching artwork for 9 games in the background." and 9/9 had cover and icon within a second (server log agrees); the default icons were the opaque ones though a transparent one was listed first; the list drew 64 px files in 38 px tiles and the grid 192 px files in ~133 px tiles; hover showed the pen; the picker paged 1–5 → 6–10 across the stub's API pages; picking a cover and an icon updated the server, the sidebar and the card without a reload, with fresh thumbnails; light theme rendered; focus-into-picker on open and a real Escape key returning focus to the pen were checked. The rig's seeded games were deleted afterwards and the console taken down.

### Not done / not verified
- **Never run against the real SteamGridDB** (no key available — deliberately not using the maintainer's). The new request parameters — `dimensions=600x900,342x482,660x930`, `mimes=image/png`, `nsfw=false`, `page=` — follow the official client's documented names but have only met the stub. The first thing to do with a real key: open the picker on a well-known game, page through, and confirm the covers are portrait and the pages don't repeat.
- The picker lists art for the FIRST name match SteamGridDB returns; a game whose name matches the wrong entry offers that entry's art. No way to choose the SteamGridDB game yet ([[Backlog]]).
- Deleting a game leaves its `/art/{id}/` folder behind (pre-existing; thumbnails now live in it too).
- A real Enter key press on a button is not deliverable by this harness (recorded in the Group 3 session); the open step was made with `.click()`, which is what Enter does natively. Escape was a real key press.
- `Release Notes Pending.md` is stale and was not used; what the next release's notes must say is in [[CONTEXT]].

## 2026-09-20 (wrap-up) — everything is on `ui-redesign-group-3`; Group 4 sized

**Branch:** `ui-redesign-group-3`, created from the merge commit `0f0868e`. Local only — nothing pushed, no PR.

### What is on it (53 files, +2,929 / −526 against `main`)
- **Checkpoint UI Group 3** — `2705ce4` (code), `04bb7f1` (Docs). The agent half of the design system, status header with Sync all, trimmed Overview.
- **Artwork** — `eb53aaa` (code), `fff1bd8` (Docs). Backfill when a SteamGridDB key is added, opaque icons in the list, right-sized Lanczos thumbnails, the cover/icon picker.
- **`testenv.ps1 sync` fix** — `199a513` (code), `f4d2aad` (Docs). New directories, deletions and renames now reach the WSL clone.
- **Merge** — `0f0868e`. Code merged without conflicts; `CONTEXT.md`, `Gotchas.md` and `progress.md` conflicted only because both sides appended at the same spot, and keep both.
- The three older branch names (`claude/group-3-ui-redesign-c16953`, `steamgriddb-art-picker`, `testenv-sync-untracked-files`) still point at their own commits; delete them once this lands. The name `ui-redesign-group-3` undersells the contents, which are three separate pieces of work — worth saying in the PR description.

### Why the art work was invisible in the rig
`testenv.ps1` builds whatever is checked out. The art work lived on `steamgriddb-art-picker`, branched from `main`, and the worktree had been left on the sync-fix branch, so neither the Docker console nor the WSL clone (at `f4d2aad`) contained `ArtPicker.tsx` or `ArtThumbnails.cs`. Not a bug in either — a branch that was never checked out. Merging fixed it.

### Group 4 — how big, and where to do it
**Scope** (`tasks/checkpoint-ui/implementation-grouping.md`): the agent Games tab — list and grid with cover art, a per-game page with Sync this game / Push now / Pull latest, the art proxy (`GET /api/games/{id}/art`), search in Add games — plus Phase 3 item 5, which moved here because the agent's local API has no per-game sync route.

**Size — an estimate, not a measurement.** Group 3 was 21 files and +917 / −416 lines, `agent-ui` only. Group 4 is larger and wider: roughly 20–25 files and 1,400–1,900 changed lines, of which four to six are C# (`AgentApiServer` routes, a per-game entry on `SyncEngine`, the art proxy) and the rest is `agent-ui` (new Games list/grid, game page, `Row` and `Seg` ports, Sidebar entry, search in the 417-line `AddGamesView`). It also regenerates `agent-ui/src/api-types.ts` from the Linux daemon. The plan itself calls it "the single largest *new* UI in the plan".

**Recommendation: a new session on a new branch, stacked on `ui-redesign-group-3`.**
1. It is the first agent group that changes C# *and* the UI, so it needs the agent suites (`run-linux-tests` and the Windows-side ones), not just a browser check — a full session of verification on its own.
2. This session has already been compacted once and carries three unrelated streams; Group 4 wants a clean context and its own reviewable diff.
3. `ui-redesign-group-3` is already 53 files. Group 4 on top would make a PR nobody can review; branch after this one is pushed or opened as a PR.
4. `implementation-grouping.md` says to re-evaluate before each new group, and there is one design question to settle at the start: **the art proxy should forward `?w=`** (the widths this session added: 48/64/96/128/192/256/384) and prefer the icon for list rows, or the agent's Games list will re-introduce the aliasing fixed here.

### Verified on the merged tree / not verified
- **Verified:** server build (0 warnings, 0 errors), web `npm run build`, `testenv.ps1` PowerShell parse and `bash -n testenv.sh`, `run-console-security-tests` **137/137**, and both `-ConsoleEnv` and the `--untracked-files=all` sync change present in `testenv.ps1`.
- **Not verified:** `agent-ui` build on the merged tree (it does not touch the art files, and Group 3 built clean on its own), the C# agent suites, the art picker against the **real** SteamGridDB (only the stub — first thing to do with a real key: if the choices come up empty, suspect the `dimensions`/`mimes`/`nsfw`/`page` parameters), the WebView2 tray window, a real Deck.

### Still open
- The picker uses the first SteamGridDB name match and offers no way to choose another (Backlog).
- A deleted game leaves its `/art/{id}` folder behind (Backlog).
- An untracked file deleted on Windows survives in the WSL clone — `rm` by hand (Gotchas).
- The `--color-faint` contrast decision (3.31:1 dark / 3.55:1 light) still belongs to the maintainer.
- `Release Notes Pending.md` is stale; what the next release notes must cover is in `CONTEXT.md`.
