# Task — Audit trail gaps: package updates, conflict detail, exclude-pattern detail

**Shipped:** 2026-08-16, commit `6d594e3`. All three gaps closed as scoped below; 164/164 server
bug-bounty suite passes.

**Created:** 2026-08-16

**Target:** `src/Server/Services/SyncService.cs`, `src/Server/Program.cs`,
`src/Server/Services/AgentInstallerService.cs`, `src/Server/Services/AgentInstallerPollerService.cs`,
`web/src/components/AuditView.tsx`

**Goal:** three gaps in the audit log the maintainer hit directly:
1. Agent/Decky-plugin **package updates** — manual upload, admin-triggered GitHub fetch, and the
   background poller's own fetch — write nothing to `AuditLogs` today. There is no record of what
   package version replaced what, or who/what triggered it.
2. **Conflict resolution** detail is a bare version GUID (`conflict.resolve` → `winningVersionId.ToString()`).
   If the wrong side gets picked, nothing in the audit log says which machine's save won, so there is
   no way to investigate after the fact.
3. **Exclude-glob changes** (`game.excludes`) record only a pattern *count*
   (`"3 pattern(s)"`), never which pattern was added or removed. A user cannot audit what got
   excluded, which is exactly the thing that matters when a save file mysteriously stops syncing.

**Motivation (maintainer, 2026-08-16):** all three came up as concrete "I couldn't tell what happened"
complaints while reviewing the Audit Log and Config pages.

---

## 1. Package update events

`SyncService.Audit(...)` is the existing helper (`SyncService.cs:1217`) — reuse it, don't build a
parallel logging path. Needs a machine-less, game-less call (both already nullable).

Three call sites, all currently silent:
- `Program.cs` `admin.MapPost("/admin/agent-installer", ...)` (~717) — manual upload. Detail should
  include the platform slot (`win-x64` / `linux-x64` / `decky-plugin`) and the version string that
  was uploaded.
- `Program.cs` `admin.MapPost("/admin/agent-installer/fetch-github", ...)` (~774) — admin-triggered
  fetch. Same detail, plus that it came from GitHub (distinguish from manual upload in the action
  code, e.g. `agent_installer.upload` vs `agent_installer.fetch_github`).
- `AgentInstallerPollerService` — the background poller that refreshes the hosted copy on its own
  schedule. This one has no `HttpContext` / admin caller, so `Audit`'s signature (which takes
  optional machine/game IDs but implicitly assumes an HTTP-request context for nothing else) should
  work unchanged, but confirm `SyncService` is reachable from a `BackgroundService` (it should be
  scoped — check how `LeaseSweeperService` gets `AppDbContext`/services for the pattern). Action code:
  `agent_installer.auto_fetch`. Detail: same platform + version, and note it replaced a previous
  version if one was hosted (`old → new`).

Only log on an actual change (new version differs from what was hosted, or nothing was hosted
before) — a fetch that finds nothing newer is not an event.

Add the three action codes to `ACTION_COLORS` in `AuditView.tsx` (pick a distinct color family from
game/machine actions — blue-ish like `command.enqueue` is a reasonable fit for "the system did this
on its own").

## 2. Conflict resolution detail

`ResolveConflictAsync` (`SyncService.cs:853`) already has everything needed at the call site: `game`,
`winner`, `currentHead`, `conflict.VersionAId`/`VersionBId`, `resolvedBy`, and whether `keepBoth`.
Build a human-readable detail string before calling `SetHeadAndPropagateAsync`, e.g.:

```
resolved by {resolvedBy}: kept {winner version's originating machine name or "?"} version
({winner.CreatedAt:o}), {keepBoth ? "both versions retained" : "other version pruned"}
```

The version doesn't carry a machine name directly — check `SaveVersion` on `Entities.cs` for what
identifies "which machine produced this version" (likely via `Conflict.MachineId` for the losing
side, or the upload that created each `SaveVersion`). If there's no clean way to resolve "which
machine" per version, fall back to including both version IDs and their timestamps rather than
inventing a lookup — the goal is "investigable", not necessarily "names a machine".

`conflict.resolve_rewind_blocked` (line 877) and `conflict.resolve_superseded` (line 997) are already
reasonably self-describing; leave them unless the same improvement is cheap while in this file.

## 3. Exclude-pattern detail

`SetExcludeGlobsAsync` (`SyncService.cs:719`) replaces the whole pattern list in one call — it has no
way today to know if a caller "added X" vs "removed Y" vs "replaced everything". Two options, pick
the simpler one that satisfies the goal:

- **Diff old vs. new** inside `SetExcludeGlobsAsync`: read `GlobConfig.Parse(game.ExcludeGlobs)`
  before overwriting, compute added/removed sets against the new list, and write one audit entry
  (or one per changed pattern) naming them, e.g. `+save.tmp, -old.bak`.
- **Or** change the API surface so the caller (the Config UI's add/remove pattern actions) tells the
  server which single pattern changed and how — cleaner semantically but touches more of the stack
  (`Contracts.cs`, `Program.cs` route, `web/src/api.ts`, `ConfigView.tsx`'s glob editor). Only do this
  if the UI already calls the endpoint per-pattern rather than replacing the whole list — check
  `ConfigView.tsx`'s exclude-glob editor before choosing.

Prefer the diff approach unless the UI investigation shows the per-pattern call already exists.

---

## Verification

- `tests/run-server-bugbounty-tests.ps1` and whatever suite exercises `/admin/agent-installer*` —
  confirm a fetch/upload now produces an `AuditLogs` row.
- A conflict resolved via the API (existing conflict tests) should produce a `conflict.resolve` (or
  `_keep_both`) entry whose detail is no longer a bare GUID.
- Exclude-pattern add/remove through the Config UI should produce an audit entry naming the pattern,
  not just a count. Manual check in the browser is fine if no test currently covers this path.
- Regenerate `src/Server/openapi.json` and `web/src/api-types.ts` only if any contract/route shape
  changed (the diff-inside-the-existing-endpoint approach should not require this).

**Stop and report after this task** — do not continue to CsvExport / AgentUpdatesRedesign /
UpdateScheduling without instruction.
