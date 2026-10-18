# Hold back a save folder that was emptied — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]). No plan
file yet. **Priority: High** — a data-safety gap, small to fix. Found by reading the code; **not reproduced
yet**, so step 1 is a test that should fail today.

---

## The problem

When every save file in a tracked folder disappears at once (an uninstaller, a game resetting its saves, a
user clearing the wrong folder, a Steam "delete local content"), the next push treats it as a normal change:

- `SyncEngine.PushCoreAsync` (`src/Agent.Core/SyncEngine.cs`) only refuses when the folder is **missing**
  (`!Directory.Exists(game.SaveDirectory)` → `SaveDirMissing`). A folder that exists but is empty goes on.
- `SaveArchive.ComputeManifest` returns an empty manifest and its hash, which differs from
  `LastSyncedHash`, so a zero-file version is uploaded as a clean fast-forward and becomes the head.
- Every other machine then pulls that head, and the restore's delete pass removes all its local save files.

Nothing is lost for good: the previous version is still on the server, so *Set as Latest* recovers it. But it
reaches every machine before anyone notices, and on a Deck that nobody is looking at, it can sit unseen.

## What a competitor does

OpenSave (MIT) — `internal/p2p/syncengine/hold.go`. A location that held files before and is now **empty**
puts the game on hold: the device starts no sync of it and does not offer it to others, until someone
answers one of two choices — *Delete them there too* (sync the deletion) or *Put them back* (restore the
newest snapshot that had the files). The hold lifts by itself if every file it covers comes back (from the
Recycle Bin, say). It deliberately triggers only on **all** files gone: a game deleting a few files is
normal tidying.

## Proposed shape

1. **Test first** (`tests/run-agent-tests.ps1` or a new section in `run-health-tests.ps1`): two machines,
   push a save, empty the folder on machine A, sync; assert machine B still has its files. Should fail on
   `main`.
2. **Agent guard** in `PushCoreAsync`, right after the manifest is computed: zero files *and* the last pushed
   or pulled version had files (`LastKnownVersionId`, or the stored per-file baseline) → do not upload. Record
   a new `AgentEventCodes.SaveFolderEmptied` (Warning) so the console and `doctor` show it, and keep the game
   held until it is answered.
   - Multi-folder games: hold only when **every** folder is empty, mirroring the marker rule in
     `SaveArchive.RestoreArchive` (an empty extra folder *with* a marker is a real deletion today).
3. **Answering it**: two actions wherever conflicts are already answered (agent UI, Game Mode, Decky, console
   game page): *Delete everywhere* (pushes the empty version with `force`) and *Restore here* (an ordinary
   pull of the head). Reuse the conflict card's layout; a held game is a new state, not a `ConflictFlag`.
4. **Server backstop** (optional, small): `SyncService` refuses a zero-entry archive on a non-forced push when
   the head has files. This protects against an older agent that lacks step 2.

## Touch points

`SyncEngine.cs` (push path, a new held state on `TrackedGame`), `AgentEventCodes.cs`, `Doctor.cs`,
agent-ui `ConflictsView`/`GameDetailView`, `Ui/UiApp.Games.cs` (if Game Mode still shows it), the Decky
plugin's conflict section, `SyncService.cs` (backstop), [[API Reference]] if a route is added.

## Verify

The test from step 1 passes; the deliberate deletion path (*Delete everywhere*) still propagates; a game that
deletes a few files still syncs normally; `run-agent-tests`, `run-health-tests`, `run-multipath-tests`
unchanged.
