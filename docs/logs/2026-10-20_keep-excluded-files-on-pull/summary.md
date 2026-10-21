# A pull keeps this machine's excluded files — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]).
**Shipped 2026-10-20** on branch `save-safety-empty-hold-and-excludes` (commit `a0b6472`), with
[[logs/2026-10-20_empty-save-hold/summary|empty-save-hold]].

## Status

| Phase | Status |
|-------|--------|
| 1. Test first (`run-hardening-tests` §10) | ✅ Shipped 2026-10-20 — reproduced: **5 checks fail on `main`**, 45/45 with the fix |
| 2. Excludes into `RestoreArchive`, both `SyncEngine` call sites | ✅ Shipped 2026-10-20 |
| 3. An old archive carrying a now-excluded file | ✅ Shipped 2026-10-20 — **skipped** (OpenSave's choice), recorded in [[Decisions]] |

## Outcome

- `SaveArchive.RestoreArchive` (both overloads) takes `excludeGlobs`. A file matching them is neither
  deleted by the delete pass nor written from the archive. The matcher is `FilterExcluded`'s own
  (`IsExcluded`, bare names match at any depth), applied to the archive name (`.savelocker/paths/<key>/`
  prefix included), exactly what `EnumerateItems` filters on a push.
- `SyncEngine` passes `game.ExcludeGlobs` (the server's effective list) at the pull and at `CopyFolder`
  (an extra folder moved onto another). `SwitchCopies`' copy between two local emulator folders is
  unchanged: it is not a pull.
- The reverse case: in that one situation the pull sets the head's hash, the local hash (excludes
  applied) differs, and the next push sends a version without the file. Intended, see [[Decisions]].
- Not changed: `HasLocalData` (the pull's "un-pushed local changes" guard) still counts excluded files.

---

## The problem

Exclude patterns keep files out of a push (`SaveArchive.CreateArchive` skips them), so they are never in an
archive. On a pull, `SaveArchive.RestoreArchive` (`src/Shared/SaveArchive.cs`, the delete pass under "This is
the most dangerous loop in the codebase") deletes every file that is **in the folder's include scope** but
**not in the archive slice**. An excluded file is in scope and not in the slice, so it is deleted.
`RestoreArchive` takes no exclude list at all; `SyncEngine` calls it at two places (the pull, and the
extra-folder shadow restore).

What that costs:

- `Sync:DefaultExcludeGlobs` (`src/Server/appsettings.json`) is `*.tmp, *.log, *.bak, Thumbs.db,
  desktop.ini`. **`*.bak` is what many games call their own backup saves**, so every pull would delete the
  game's own safety copies on that machine.
- A per-game exclude added *because* a file belongs to one machine (a graphics `settings.ini`, a
  machine-bound config) is deleted on every pull, which is the opposite of what the user asked for.

## What a competitor does

OpenSave (MIT) — `internal/snapshot/keep.go`, `RestoreKeeping`: before restoring a snapshot that came from
another device, the excluded files are copied aside; after the restore, any excluded files the snapshot
brought are removed and this device's own are put back. Restoring the device's **own** snapshot ("make the
folder look like it did") still uses the plain restore.

SaveLocker can do it more simply: the delete pass should never delete a file that matches an exclude
pattern. That is a filter, not a copy.

## Proposed shape

1. **Test first** in `tests/run-hardening-tests.ps1` (it already owns "the delete pass must not delete what it
   should not"): a game with `*.bak` excluded; machine B has `slot1.bak`; machine A pushes; B pulls; assert
   `slot1.bak` still exists. Same for a per-game exclude. Should fail on `main`.
2. Pass the game's effective exclude globs into `RestoreArchive` (both overloads and both `SyncEngine` call
   sites) and skip matching files in the delete pass, using the same matcher `EnumerateItems` uses, so push
   and pull can never disagree about what "excluded" means.
3. Consider the reverse case too: an archive written **before** a pattern was added can still carry a file
   that is now excluded. Decide whether the restore writes it (today's behaviour) or skips it; OpenSave
   skips it. Record the choice in [[Decisions]].

## Touch points

`src/Shared/SaveArchive.cs` (`RestoreArchive`, the delete pass), `src/Agent.Core/SyncEngine.cs` (both call
sites), `tests/run-hardening-tests.ps1`, [[Decisions]] (one line next to the hostile-archive rule).

## Verify

The new hardening checks pass and fail again when the filter is removed; `run-hardening-tests`,
`run-multipath-tests` (include scopes share this loop), `run-agent-tests` unchanged.
