# Task: Playthroughs — named save forks to switch between

Planned 2026-10-22 from [[tasks/save-playthroughs/summary|the summary]] with the maintainer's answers (end of
this file). **High priority, first of three linked tasks:** this one, then [[tasks/version-history-upgrades/plan|
Version history upgrades]], then [[tasks/storage-dedup-offsite/plan|Storage: off-site copies and deduplication]].
Those two are written against a game having several lines of versions, so retention, trash, labels and the off-site
mirror all work per playthrough from the start instead of being reworked later.

`plan.md` lists the phases and their real dependencies. [[tasks/save-playthroughs/implementation-grouping|
implementation-grouping.md]] says which phases share a session and in what order.

## Status

| Phase | Status |
|---|---|
| 1 — Server model: `Playthrough`, per-playthrough head, migration to "Main" | ⏳ Not started |
| 2 — Server sync scoped per playthrough (push, pull, conflicts, leases, delta, fan-out, retention) | ⏳ Not started |
| 3 — Server playthrough API + per-device selection | ⏳ Not started |
| 4 — Agent core: follow a playthrough, switch safely | ⏳ Not started |
| 5 — Agent local copy store (deduplicated, three modes) | ⏳ Not started |
| 6 — Agent UI + CLI | ⏳ Not started |
| 7 — Console: Playthroughs card, fork, switch a device | ⏳ Not started |
| 8 — Decky: chip and QAM line | ⏳ Not started |
| 9 — Playnite: menu item | ⏳ Not started |
| 10 — Delete to trash | ⏳ Not started — needs version-history Phase 1 (trash) |
| testenv pass | ⏳ Not run |

---

## The model in one paragraph

A game has one or more **playthroughs**. Each is its own line of versions with its own head, conflicts, lease and
retention. Every existing game becomes one playthrough named **Main**, so nothing changes for anyone who never
creates a second one. Each **device** has its own **Selected** playthrough per game (selection is per device, not
per game). It pushes to and pulls from only that one. **Switching** on a device: refuse while the game runs, push
the outgoing save to its own playthrough, keep a local copy if the agent setting allows, pull the target's latest,
then record the new selection on the device and on the server. A save never moves between playthroughs silently:
every sync, conflict and version line names its playthrough once a game has more than one.

## What already exists (don't re-derive)

- `Game.HeadVersionId` is the single head. `SaveVersion.ParentVersionId` forms the lineage. `Conflict` has
  `VersionAId`/`VersionBId` per game. `Lease` is per game and machine. `Game.RetainVersions` is applied by
  `SyncService.PruneVersionsAsync`, which never prunes the head, open-conflict versions or `Protected` ones.
- A delta upload's base must be the current head (`SyncService` ~1332: diverged, or the head is not the
  negotiated base, makes it fall back). The baseline rows are `SaveVersionFile`.
- *Set as Latest* / conflict resolution fan out an **unforced** pull to every live machine syncing the game
  ([[Decisions]]); an ordinary push does not.
- The agent's per-game state is `TrackedGame` (`AgentConfig.cs`). Pushes already compute a per-file SHA-256
  manifest (`FileManifestEntry`), which the local store (Phase 5) reuses.
- Agent surfaces: `agent-ui/src/components/GameDetailView.tsx`, `VersionsCard.tsx`, `SwitchPlaysIn.tsx` (a
  switcher pattern to match). Console: `web/src/components/game/GameDetail.tsx`, `VersionsCard.tsx`,
  `MachineSelect.tsx`, `RemoteCommandsCard.tsx`. Decky: `SaveLocker-Decky/src/fullPage.tsx` (per-game row with
  the status and sync buttons) and the QAM game menu. Playnite: `GetGameMenuItems` in `SaveLocker-Playnite`.

---

## Phase 1 — Server model: `Playthrough`, per-playthrough head, migration to "Main"

1. `Playthrough { Id, GameId, Name, CreatedAt, ForkedFromVersionId?, HeadVersionId?, ArchivedAt?, IsDefault }`.
   The name must be unique per game (case-insensitive, trimmed, 1–60 chars). Exactly one playthrough per game has
   `IsDefault`: it's what a new device and an old agent follow (see compatibility below).
2. `SaveVersion.PlaythroughId` (required after migration), `Conflict.PlaythroughId`, `Lease.PlaythroughId`.
3. **Migration:** one `Main` playthrough per game (`IsDefault = true`, `HeadVersionId = Game.HeadVersionId`), every
   existing version, conflict and lease assigned to it. `Game.HeadVersionId` stays as a **computed mirror of the
   default playthrough's head**, written in the same transaction as it, so every existing reader (health, art,
   backups, the console's game list) keeps working until it's moved over. Remove the mirror only once nothing
   reads it (an item in Phase 2's checklist).
4. A merged-in game's versions (`MergedFrom`) go to the target's default playthrough, as today's merge does with
   the head.
5. Migration is tested against a copy of the real dev database, then a full sync round trip.

## Phase 2 — Server sync scoped per playthrough

Every place that reads "the game's head" must instead read "the playthrough's head". Checklist, each with a test:

- **Push** (`PrepareUploadAsync`, finalize, fast-forward, divergence detection): against the head of the
  playthrough named in the request.
- **Pull / download head**: the request's playthrough.
- **Conflicts**: opened per playthrough. Two devices on different playthroughs never conflict.
- **Leases**: per playthrough. Two people can play two playthroughs of one game at once on two devices.
- **Delta baseline**: the base must be the head of the same playthrough. A request whose base belongs to another
  playthrough falls back to a full upload (never a cross-playthrough copy-forward).
- **Fan-out pulls** (*Set as Latest*, resolve): only to devices whose Selected playthrough is the affected one.
- **Retention**: `PruneVersionsAsync` runs per playthrough with the game's setting. Each playthrough's head is
  never pruned. An archived playthrough is not pruned at all.
- **Health** (`HealthService`) and the console's game list: "latest" means the default playthrough's head, with
  the playthrough count shown when it's more than 1.
- **Server backups** (`BackupService`): include every playthrough's head, not just the game's.

**Compatibility:** an agent older than this release sends no playthrough. The server uses that machine's recorded
selection for the game, or the default playthrough when there is none. Old agents can't switch, but they never
write to the wrong line.

## Phase 3 — Server playthrough API + per-device selection

1. `DeviceSelection(MachineId, GameId, PlaythroughId, SelectedAt)`. Written by the agent on switch (and on first
   sync = default), read by the console to show which device is on which playthrough.
2. Admin routes: list (with head time, size, version count, devices on it), create (fork from a version id, or
   "from the current latest" of another playthrough), rename, archive/unarchive, set default for new devices,
   delete (Phase 10). Agent routes: list, create-from-head, select (records `DeviceSelection`).
3. Console "switch this device" = an `AgentCommand` (`SwitchPlaythrough`), so it runs on the device with its
   guards (game not running) and is refused, with the reason shown in the command history, otherwise.
4. Audit every create, rename, archive, default change, delete and device switch.
5. `openapi.json` + both `api-types.ts` + [[API Reference]].

## Phase 4 — Agent core: follow a playthrough, switch safely

1. `TrackedGame.PlaythroughId` (null = the default) and `PlaythroughName` (for offline display). Every push, pull,
   pre-launch gate, post-exit push and conflict call carries it.
2. `SyncEngine.SwitchPlaythroughAsync(game, targetId)`:
   1. Refuse if the game is running or a session lease is held here for it. Refuse if a conflict is open on the
      current playthrough (resolve first).
   2. If the folder differs from the current playthrough's head: push it to the **current** playthrough. If the
      push can't happen (offline), proceed only if the local store (Phase 5) captured the folder, and queue the
      push for when the server is back. Otherwise refuse with "can't reach the server to save *Main* first".
   3. Put the target's latest in the folder: pulled from the server, or from the local store when offline (marked
      "may be older than the server": the next push carries that version as its parent, so normal divergence
      detection turns any gap into an ordinary conflict, never a silent overwrite).
   4. Record the selection locally, then on the server (queued if offline).
   Every step logs to the activity view. A failure mid-way leaves the device on the **old** playthrough with its
   folder unchanged, or restores the folder from the local copy taken in step 2.
3. The empty-save hold and the keep-excluded-files rule apply to the pull in step 3 exactly as for a normal pull.
4. Tests in `run-agent-tests.ps1` + a new `run-playthrough-tests.ps1`: switch online, switch offline with and
   without the store, a refused switch while running, a failed push mid-switch, two devices on two playthroughs
   pushing concurrently with no conflict.

## Phase 5 — Agent local copy store (deduplicated, three modes)

An agent setting, **Playthrough copies on this device**:

| Mode | What's kept | Use |
|---|---|---|
| **Only until uploaded** (default) | The outgoing save at a switch, until the server confirms its push | Never lose a save to a failed push; costs nothing afterwards |
| **Keep for offline switching** | The latest of every playthrough this device has played | Switch with no server, e.g. on a plane |
| **Off** | Nothing | Huge saves, little disk; a switch then needs the server |

- **Layout:** `{agentState}/playthrough-store/blobs/{sha256[0..2]}/{sha256}` plus
  `snapshots/{gameId}/{playthroughId}.json` (version id, time, file list with path, size, SHA-256). A file shared by
  two playthroughs is stored once. A playthrough forked from Main costs almost nothing until it diverges.
- **Writes:** blob to a temp name, fsync, rename; snapshot JSON last. A snapshot never names a blob that isn't on
  disk.
- **Restore:** every file's hash is checked against the snapshot before it goes into the save folder. A mismatch
  aborts the restore and leaves the folder alone.
- **Cleanup:** mark-and-sweep at agent start and after each switch. Every blob no snapshot names is deleted once
  it's older than an hour, so a switch that's writing can't lose a blob. There's one store owner per machine
  (the daemon on Linux, the tray on Windows, see [[tasks/linux-single-state-owner/summary]]), so no reference
  counts are needed.
- **Settings UI:** the mode, the space used, and "Clear copies" (with a warning naming any copy not yet uploaded,
  which is never cleared in *Only until uploaded*).
- Chunking for one-big-file saves is deliberately not here. It's the storage task's Phase 9 and can be reused here
  later.

## Phase 6 — Agent UI + CLI

1. Game page (`GameDetailView.tsx`): a **Playthroughs** card, shown once a game has more than one, or as a small
   "New playthrough…" action otherwise. Each row shows the name, last update time and device ("Hard mode · 2 h
   ago on Deck"), the devices on it, and a *Selected* tag on this device's. Actions: Select (runs the switch with a
   progress dialog per the explicit-dialog rule, [[playnite-plugin-feedback-ux]]), Rename, Archive, New from this
   playthrough's latest. Gamepad-navigable ([[agent-ui-controller-navigation]]).
2. The playthrough name on sync status, activity lines, conflict cards and the Versions card's rows once a game has
   more than one. The Versions card shows the Selected playthrough by default, with a picker to view others
   (read-only, plus "New playthrough from this version").
3. Settings: the Phase 5 mode.
4. CLI (`AgentCli.cs`, [[CLI Reference]] KB article): `playthrough list <game>`, `playthrough switch <game> <name>`,
   `playthrough new <game> <name> [--from-version <id>]`.
5. No toast at launch: the maintainer doesn't want a launch notification. The Selected name just shows where the
   game is shown.

## Phase 7 — Console: Playthroughs card, fork, switch a device

1. A **Playthroughs** card on the game page: list, devices on each (from `DeviceSelection`), Rename, Archive,
   *Default for new devices*, Delete (Phase 10). "Switch a device…" picks a machine (`MachineSelect`) and queues
   the command from Phase 3.
2. Versions card: a playthrough filter, and "New playthrough from this version" on each row.
3. The game list shows a small "3 playthroughs" chip.

## Phase 8 — Decky (`SaveLocker-Decky`)

1. `fullPage.tsx` per-game row: when a game has more than one playthrough, a **playthrough chip** next to the status
   and sync buttons, showing the Selected name. Pressing it opens a picker that runs the switch through the
   agent's local API, with progress and an explicit result dialog.
2. The QAM game menu's advanced section shows the current playthrough (read-only line).
3. Gamepad-only flow checked on the Deck.

## Phase 9 — Playnite (`SaveLocker-Playnite`)

`GetGameMenuItems`: a **SaveLocker: Playthrough ▸** submenu listing the playthroughs, with the Selected one ticked.
Choosing one runs the switch through the local API with a real progress/result dialog (not a toast,
[[playnite-plugin-feedback-ux]]). Refused, with the reason, while the game runs.

## Phase 10 — Delete to trash

Needs [[tasks/version-history-upgrades/plan|version-history]] Phase 1 (trash). Deleting a playthrough is refused
while any device has it Selected, or while it's the default. Otherwise its versions move to trash for the trash
period, and *Restore* brings the playthrough back with them. Archive is the reversible, no-expiry alternative.

---

## Verify

- `run-playthrough-tests.ps1` (new): migration on a seeded database, per-playthrough push/pull/conflict/lease/delta,
  fan-out only to followers, old-agent compatibility, retention per playthrough.
- `run-delta-upload-tests`, `run-concurrency-tests`, `run-health-tests`, `run-agent-tests` unchanged.
- testenv pass ([[use-testenv-for-manual-tests]]): Windows tray + WSL agent + Deck. Fork *Hard mode* on Windows,
  switch the Deck to it from Decky, play both concurrently, switch back offline with *Keep for offline switching*,
  reconnect and confirm one clean push.

## Answers settled with the maintainer (2026-10-22)

- **Ownership:** named lines anyone can pick. Household accounts can add an owner later without reshaping it.
- **Selected is per device.** Selecting on the Deck doesn't change the PC. (Offered: one per game for all devices.
  Chosen: per device.) The *default* playthrough is only what a new device or old agent starts on.
- **Switching:** push first, then keep a local copy, with an agent setting to limit or turn it off. Copies are
  deduplicated (three modes, Phase 5).
- **Another device moving another playthrough:** a quiet line in the playthrough list, no prompt.
- **Emulators:** a playthrough always swaps the whole tracked save. Emulator profiles stay as they are.
- **Retention:** the game's setting applies to each playthrough separately.
- **Actions:** Select (renamed from "Make main"), Rename, Archive, Delete to trash.
- **Launch:** picker on the game page. Decky gets a chip by the status/sync buttons when there's more than one, and
  a QAM line. Playnite gets a menu item. No launch notification.
