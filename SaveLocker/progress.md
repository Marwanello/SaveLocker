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
