# Version history upgrades — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]). No plan
file yet. **Priority: Medium.** Several small, independent pieces; each can ship on its own.

---

## Where SaveLocker is today

Per-game retention by **count** (`SyncService.SetGameRetentionAsync`, global default 10), *Protect* to
exempt a version, *Set as Latest* to roll back (fans out a pull), the Versions/Backups tabs, per-version
"what changed" (`VersionFiles`), and download. `SaveVersion` has no label or note, and pruning deletes the
archive at once.

## What competitors have that SaveLocker does not

| Feature | Where | Notes |
|---|---|---|
| Restore **one file** from a version | OpenSave (MIT) `internal/api/routes_backup.go` `handleRestoreFile` / `extractSingleFile` | Useful for a game with one slot file per save slot |
| **Age-based thinning** (dense recent history, sparse old history) | Hoard (AGPL, ideas only) `crates/hoard-server/src/retention.rs` (GFS, one "data saving" knob); OpenSave `snapshot.PruneOlderThan` | Keeps more useful history in less space than a flat count |
| **Prune preview** (what would be deleted) | OpenSave `snapshot.PrunePlan` | Read-only |
| **Labels / notes** on a version | OpenSave `snapshot.EditSnapshot`; Worldline "named checkpoints are kept permanently" | "Before the final boss" |
| **Trash** before the hard delete | Hoard `cleanup.rs` + `trash_retention_days` | A mistaken delete or prune is recoverable for N days |
| Branches for parallel playthroughs | OpenSave `CreateBranch`/`SwitchBranch`; Worldline timelines | Larger; not proposed yet |

## Proposed shape

1. **Labels**: nullable `SaveVersion.Label`; edit from the console version row and the agent's version list; a
   labelled version is implicitly protected. Audit the change.
2. **Single-file restore**: from the console's version file tree and the agent's, an agent command that pulls
   one file out of a version into the live folder. It must go through the same guards as a pull (game not
   running, `SavePathGuard`, no symlink traversal) and leave the head unchanged. Afterwards the folder
   differs from the head, so the next push makes a new version — say so in the UI.
3. **Trash**: pruning and manual deletes move the archive to `{root}/.trash/` with a retention period
   (`Storage:TrashDays`, default 14), purged by the existing background sweep; a Restore action in the console.
4. **Age-based retention**: an optional per-game mode next to the count — keep everything from the last N
   days, then one per day, then one per week. A pure function over version timestamps, unit-tested, with the
   preview from the table above.
5. Branches: only if asked for after the above.

## Touch points

`src/Server/Data/Entities.cs` + migration, `SyncService.cs` (retention, prune), `ArchiveStore.cs` (trash),
`BackupService`-style sweep, `web/src/components/game/VersionsCard.tsx` + `VersionFiles`,
agent-ui `VersionsCard.tsx`, `AgentCli.cs` (a `restore-file` command), `openapi.json` + both `api-types.ts`,
[[API Reference]].

## Verify

A new suite section per piece; `run-delta-upload-tests` (pruning must never remove a delta baseline still
needed) and `run-health-tests` unchanged.
