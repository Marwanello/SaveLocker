# Progress Log

## 2026-08-24/25 — File-count / newest-mtime delta in conflict UI

**Branch:** `conflict-version-stats` (commit `800eed2`). Not merged, not pushed.

### Request sequence

1. Asked whether the backlog item "File-count / newest-mtime delta in conflict UI" (the last piece of
   an otherwise-complete conflict Tier 1) was hard to implement — exploratory question, no
   implementation yet.
2. Asked to implement it, with an explicit forward-looking constraint: design it so it doesn't paint
   the codebase into a corner for a future migration where conflict resolution moves from manual
   dashboard clicks to agent-side automatic decisions, the way Steam Cloud auto-resolves save
   conflicts.
3. Asked to create a new branch using kebab-case naming and commit the changes.

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
- `SyncService.GetVersionStatsAsync` — two overloads sharing one body: an unscoped `(Guid versionId)`
  for the agent group's flat route (matching `DownloadVersionAsync`'s own precedent — a valid machine
  key already gates the group), and a game-scoped `(Guid gameId, Guid versionId)` for the admin group,
  which checks the version actually belongs to that game before answering.
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

### Verification

- `dotnet build --no-incremental` clean for `SaveLocker.Server` and `SaveLocker.Agent`.
- `web`: `npm run build` (tsc + vite) and `npm run lint` (oxlint) both clean.
- `src/Server/openapi.json` regenerated from a live dev server and committed; `web/src/api-types.ts`
  regenerated via `npm run gen:api` — diff is exactly the two new routes plus the new schema.
- Manual, live: two throwaway machines registered against a dev server, two divergent archives pushed
  to force a real conflict, both `/stats` routes confirmed correct, console conflict card visually
  confirmed in the browser pane. Demo game deleted afterward, no dev-state residue.
- `tests/run-health-tests.ps1` — two new checks added after the existing "stale conflict is escalated
  in the console contract" assertion, reusing that test's real-conflict fixture. 21/21 passing, no
  regressions to the other 19.

### Files changed (14: 13 modified, 1 new)

`src/Shared/SaveArchive.cs`, `src/Shared/Contracts.cs`, `src/Server/Services/SyncService.cs`,
`src/Server/Program.cs`, `src/Server/openapi.json`, `web/src/api-types.ts`, `web/src/api.ts`,
`web/src/types.ts`, `web/src/components/GameDetail.tsx`, `tests/run-health-tests.ps1`,
`SaveLocker/Backlog.md`, `SaveLocker/CONTEXT.md`, `SaveLocker/logs/shipped-2026-08.md`,
`SaveLocker/logs/2026-08-24_conflict-version-stats.md` (new).

### Write-up

Full write-up: [`SaveLocker/logs/2026-08-24_conflict-version-stats.md`](logs/2026-08-24_conflict-version-stats.md).

### Not done / next step

Branch `conflict-version-stats` is committed locally but not pushed and no PR has been opened — neither
was requested. No further action planned unless asked.
