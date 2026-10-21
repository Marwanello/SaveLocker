# Hold back a save folder that was emptied — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]).
**Shipped 2026-10-20** on branch `save-safety-empty-hold-and-excludes`, plus SaveLocker-Decky branch
`empty-save-hold` (`ae4ab9f`), with [[logs/2026-10-20_keep-excluded-files-on-pull/summary|keep-excluded-files-on-pull]].

## Status

| Phase | Status |
|-------|--------|
| 1. Test first (`run-health-tests` §9) | ✅ Shipped 2026-10-20 — reproduced: on `main` the other machine **lost its saves**; 10 checks fail there, 51/51 with the fix |
| 2. Agent guard + held state | ✅ Shipped 2026-10-20 |
| 3. Answering it (agent UI, doctor, notification, CLI, console bell, Decky) | ✅ Shipped 2026-10-20 — Game Mode (`Ui/UiApp.Games.cs`) left out by choice |
| 4. Server backstop | ✅ Shipped 2026-10-20 |

## Decisions made with the maintainer (2026-10-20)

- **A held game sits out every automatic sync — push AND pull** (option A). Sync all, the folder
  watcher, the exit push, the pre-launch pull and queued unforced pulls all skip it; only Delete
  everywhere (forced push), Restore here (forced pull) or the files coming back lift it. Rejected:
  holding the push only, which let any ordinary Sync silently refill a folder the user had emptied on
  purpose. A launch still goes ahead (`ProceedSyncPaused`, without a save).
- Surfaces: agent UI, `doctor`, an OS notification and the Decky plugin. Game Mode not done.
- The server backstop is in.

## Outcome

- **Rule** (`SyncEngine.HoldIfEmptied`): the folder exists, holds no file a push would carry (excludes
  applied, every save folder of the game counted), and `LastSyncedHash` is set and differs from the
  empty folder's hash. A game never synced here, or one whose deletion every machine already took,
  is not held. A partial deletion is never held. The hold lifts by itself when **any** file is back
  (OpenSave waits for all of them; this agent does not keep the old file list).
- **State:** `<state>/emptied-holds.json` (`EmptyFolderHoldStore`, the `DlcHoldStore` pattern — shared
  by daemon, tray and launch wrapper). `MarkSynced` clears it. New `PullOutcome.HeldEmptied`,
  `SyncRunSummary.HeldEmptied`, event `savedir.emptied` (Warning).
- **Answers:** `SyncEngine.AnswerEmptiedAsync`; local API `GET /api/holds/emptied`,
  `POST /api/games/{id}/emptied-hold {action}`; CLI `emptied-hold [game] [--restore-here|--delete-everywhere]`;
  agent UI `EmptiedHoldBanner` on the game page and Conflicts (sidebar badge counts it, Sync all says
  "N held: save files gone"); console bell titles the event and offers **Restore there** (a forced Pull
  command — Delete everywhere is deliberately not offered remotely); Decky: a "Needs attention" row and
  a full-page section, both opening Restore here / Delete it everywhere (confirmed once).
- **Backstop:** `SyncService.IngestAsync` refuses an unforced archive with no files over a head with
  files — `EmptySaveRefusedException` → 409 on the single-shot upload and chunked `complete`.
- **Not verified:** the Decky plugin on a real Deck (it typechecks and builds), and Linux `doctor`'s new
  lines by eye.

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
