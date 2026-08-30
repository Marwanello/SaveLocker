# Session summary — 2026-08-30

Conflict-resolution plan expanded from 9 to 15 phases to close a real gap (no resolve UI existed
outside Decky/Playnite), all planning docs consolidated into `tasks/conflict-resolution-ui/`, and
**Phase 5** (Linux environment-capability detection) implemented and shipped as PR #23.

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
