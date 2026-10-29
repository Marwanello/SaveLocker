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
| 1 — Server model: `Playthrough`, per-playthrough head, migration to "Main" | ✅ Shipped 2026-10-26 (Group 1, branch `playthroughs-group-1`) — migration `AddPlaythroughs`, proved on a previous-schema seeded database; no copy of a real dev database existed to run it against |
| 2 — Server sync scoped per playthrough (push, pull, conflicts, leases, delta, fan-out, retention) | ✅ Shipped 2026-10-26 — `run-playthrough-tests.ps1` 96/96, mutation-checked (fan-out and retention each fail when broken) |
| 3 — Server playthrough API + per-device selection | ✅ Shipped 2026-10-26 — all but *delete* (Phase 10); `SwitchPlaythrough` is queued and gated by agent version, no agent runs it until Group 2 |
| 4 — Agent core: follow a playthrough, switch safely | ✅ Shipped 2026-10-26 (Group 2, branch `playthroughs-group-2`) — `run-playthrough-agent-tests.ps1` drives it end to end; the console's switch command (`AgentCommand`) runs the same method |
| 5 — Agent local copy store (deduplicated, three modes) | ✅ Shipped 2026-10-26 — `PlaythroughStoreTests` (dedup, verify-on-restore, sweep grace, clear keeps the only copy) + the offline sections of the agent suite |
| 6 — Agent UI (playthrough tabs, Settings in sections) + CLI | ✅ Shipped 2026-10-26 — tabs, New playthrough, switch dialog, Settings in six sections with LT/RT; checked in the browser pane against the testenv console and tray; **not** on a controller or a Deck |
| 7 — Console: Saves card, fork, switch a device | ✅ Shipped 2026-10-27 (branch `playthroughs-group-3`) — Saves card (tabs, variant E), New playthrough (current save / start fresh), rename, archive/unarchive, default for new devices, "New playthrough from this version", and queuing `SwitchPlaythrough` on a device; delete deferred to Phase 10. PR #97 review fixes 2026-10-27 (Prune scoped to one playthrough, a fork's History, tab selection). Follow-up 2026-10-27, mockup-driven: tabs always shown (even one playthrough) with Default/Archived as tab chips, directional slide-fade between tabs, New playthrough is a popup (not inline — the "No modals" rule's one deliberate exception here) with a shared Playthrough+Version picker Current save and Start fresh both sit under, icon-only Download/Fork per version row, Fork opens that same popup pre-filled. Agent-ui's matching "New playthrough from this version" renamed to "Fork…", its label now shows the version's hash instead of a vague sentence. Verified live against testenv (console + WinTest + LinuxTest): a real fork off a real version, tabs, chips all checked on a running fleet. |
| 8 — Decky: dropdown on the Games row and in the QAM, icons, List / Grid | 🚧 Written 2026-10-27 (`SaveLocker-Decky` branch `playthroughs-decky`) — `tsc` and the rollup build pass; **not run on a Deck**, so the Steam art addresses, the context-menu dropdown and the confirm-from-QAM return are unverified |
| 9 — Playnite: Playthrough submenu | 🚧 Written 2026-10-27 (`SaveLocker-Playnite` branch `playthroughs-playnite`) — 34 unit tests pass (6 new, against a stub agent); **not clicked through in a real Playnite** |
| 10 — Delete to trash | ⏳ Not started — needs version-history Phase 1 (trash) |
| testenv pass | ⏳ Not run |

## Design (decided)

Every design question was settled on clickable mockups of the real apps (2026-10-22 to 2026-10-25). The phases
below describe only the chosen designs.

| Feature | Where | Phases | Chosen |
|---|---|---|---|
| Switch on this device | Agent game page | 4, 6 | **Playthrough tabs**, under the emulator tabs on a multi-emulator game; *Versions on the server* follows the tab on show |
| Copies on this device | Agent Settings › Sync | 5 | **One row** with a three-way switch |
| Saves card | Console game page | 7 | **A Saves card on every game**, replacing Versions: a tab per playthrough (Main at least), its devices, its versions and their files |
| Decky | Games page and QAM | 8 | **A dropdown** on the Games row and in the QAM; game icons and List / Grid |
| Playnite | Game menu | 9 | **SaveLocker ▸ Playthrough ▸** submenu with the Selected one ticked |

Across all of them:

- **New playthrough** offers two starts, in the agent, the console and Playnite: **Current save** (a copy of a
  playthrough's latest save, the default) and **Start fresh** (an empty save folder, keeping the files ticked in a
  list: a profile or settings file is ticked by default, saves are not).
- **Every game has at least one playthrough, Main.** The default playthrough can't be archived or deleted.
- **Several emulators** (`multi-emulator-games`): each emulator's save is its own server game with its own
  playthroughs. The playthrough tabs sit under the emulator tab and name it ("melonDS playthroughs"); switching one
  emulator's playthrough leaves the others alone.

### Agent Settings in sections

The agent's Settings page is split into sections, most used first, with a sticky bar under the title to jump
between them (the one in view is pressed; LT/RT on a controller):

| Section | Holds |
|---|---|
| Sync | Sync safety (settle time, start at login), Playthrough copies |
| Games | Tracked games, launch options, the Decky and Playnite plugin cards |
| Server | Connection, Join, Switch user |
| Updates | The agent's own version |
| Look & controls | Appearance, Controller |
| Emulators | Xbox 360 (Xenia) settings |

### Mockups

Every pick on at once: <https://claude.ai/artifact/R3BrsWpzSicoRt4xqqVDtJ>. Every variant that was compared:
<https://claude.ai/artifact/SUG57iLat2YDhc76qTqZ94>. The agent and console mockups are the real apps with a mock
backend and the features built into copies of the real components (`agent-ui/mockup-features/`,
`web/mockup-features/`; run with `npx vite --config mockup-features/vite.config.ts` in `agent-ui/` or `web/`). Decky
and Playnite are rebuilt by hand from captures of the live UIs.

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

### What Group 1 settled (2026-10-26)

- A fork's head **points at** the version it was forked from; the version stays in its own playthrough. So retention and delete treat every
  playthrough's head as untouchable, and "the base of a delta must be the head of the same playthrough" means the head, not a member
  ([[Decisions]]).
- **Archived is frozen**: 409 on push / pull / select / lease / Set as Latest; archiving is refused while a device is on it, it has an open
  conflict or it is being played.
- A device's selection row is written the first time it syncs (and by the migration for every device that already did), so a later change of
  default moves only new devices. An agent that names no playthrough gets its recorded one.
- **Readers left on the `Game.HeadVersionId` mirror on purpose** (they mean "the default playthrough's latest", per Phase 2's health/console
  line): the console game list and `GameStateDto.head`, `ContentTransferService` (DLC hand-over reads the head's recorded packages),
  `PreviewExcludesAsync` (the exclude editor's dry run), `CopyToEmulatorAsync` (copies into the target's default playthrough) and
  `BackupService` (which now reads every playthrough's head from `Playthroughs` instead). Remove the mirror only when none of these needs it.
- Merging a game that has playthroughs of its own is refused (its versions would pour into the target's Main).
- The switch command's `MinAgentVersion` (`0.9.0`) is a placeholder for the release Group 2 ships in.

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

1. Game page (`GameDetailView.tsx`): **playthrough tabs**, one segment per
   playthrough with a tick on this device's Selected one, and "New". Another tab shows that playthrough read-only
   with "Play this one here" (runs the switch with a progress dialog per the explicit-dialog rule,
   [[playnite-plugin-feedback-ux]]). On a game with several emulators (`multi-emulator-games`) the tabs sit under
   the emulator tabs and belong to that emulator's tree. Rename and Archive sit in the tab's menu.
   Gamepad-navigable ([[agent-ui-controller-navigation]]).
   **New playthrough** asks for a name and a start: *Current save* (a new head pointing at the current playthrough's
   latest version, so nothing is uploaded again) or *Start fresh* (after the current save is uploaded, the save folder
   is emptied except the files ticked to keep; the first push makes the new playthrough's first version). "Play it on
   this device now" is on by default.
2. The playthrough name on sync status, activity lines and conflict cards once a game has more than one. *Versions
   on the server* follows the playthrough tab on show and is titled with its name: the Selected one by default,
   another one read-only (plus "New playthrough from this version").
3. Settings: the Phase 5 mode as one row with a three-way switch, under **Sync**.
4. Settings in sections (see *Agent Settings in sections* above): `SettingsView.tsx` gets a section header per
   group and a sticky jump bar; each button is a pad stop and LT/RT move between sections.
5. CLI (`AgentCli.cs`, [[CLI Reference]] KB article): `playthrough list <game>`, `playthrough switch <game> <name>`,
   `playthrough new <game> <name> [--from-version <id>]`.
6. No toast at launch: the maintainer doesn't want a launch notification. The Selected name just shows where the
   game is shown.

### What Group 2 settled (2026-10-26)

- **A switch changes the game's playthrough only inside the restore that records the version it restored** (`PullAsync(..., switchTo)`): naming the new line
  first would let a folder-watch push send the old save into it, and restoring first would leave a window with the new files on the old line.
- **Push first, then restore — and the push is of the *outgoing* line.** A conflict in it, an emptied-folder hold, or a refused push stops the switch; the
  device stays where it was with its edit intact. Only a push that failed because the server **could not be reached** may continue, and only from a copy
  (below); the generic offline-queue entry is dropped then, because a queued push of the folder would later send the *new* line's files to the wrong line.
- **Copies are a deduplicated blob store** (`playthrough-store/blobs/{ab}/{sha256}` + `snapshots/{game}/{playthrough}.json`), written blob-then-snapshot, verified file
  by file on restore, swept (older than an hour, named by no snapshot) at engine start and after every switch. *Only until uploaded*: kept for the switch only,
  removed once the server confirms; *Keep for offline switching*: kept, marked uploaded; *Off*: nothing, so an offline switch is refused. The only copy of a save
  that never reached the server is never cleared.
- **An offline switch** restores the target from its copy with the copy's version as the parent of the next push (so a gap to the server is an ordinary conflict),
  keeps the outgoing save as a copy marked *not uploaded*, and `OfflineQueueDrainer` sends it — and then the selection — when the server is back (`playthrough flush`
  does the same from the CLI on a box with no daemon).
- **Start fresh** empties the folder through the same restore a pull uses (an archive of only the kept files, plus the markers of every extra folder, so its
  delete pass applies); the new line has no version until the game writes something and the next push makes its first.
- **Everything that compares "this device's save" with "the cloud" reads the followed playthrough's head** (`ApiClient.GetPlaythroughHeadAsync`), never the state
  route's head, which is the default's: Check now, Save files on this device, the DLC check, the CLI's `dlc`, the Switch emulator's vs-cloud read.
- **Server routes added to the agent group** (Group 1 had only list / create / select): `rename`, `archive`, `unarchive` — the agent's tab menu needs them and an
  agent has no admin credential. Same service methods and audit entries as the admin routes, under the machine's name.
- **`Playthroughs.MinAgentVersion` stays `0.9.0`.** A testenv build is stamped one patch above the last tag (`0.8.2-test`), which the server treats as too old to be sent
  a switch; build with `testenv build -Version 0.9.0-test` to try the console's command.

## Phase 7 — Console: Saves card, fork, switch a device

1. A **Saves** card replaces the Versions card on **every** game: a tab per
   playthrough, with the devices that play it under its name; every game has at least **Main**. Under the tab: the
   devices on it (from `DeviceSelection`), "Switch a device to this…" (picks a machine with `MachineSelect` and
   queues the command from Phase 3), Rename, Archive (not on the default: **the default playthrough can't be
   archived**, and the server refuses it too), *Default for new devices*, Delete (Phase 10), and that playthrough's
   versions with the Versions card's own row actions: the chevron opening the version's files, Name, Set as Latest,
   Download, and "New playthrough from this version".
2. New playthrough: *Current save* (pick which playthrough's latest) or *Start fresh* (keep files picked from the
   latest's file list), as in the agent.
3. The game list shows a small "3 playthroughs" chip.

## Phase 8 — Decky (`SaveLocker-Decky`)

1. `fullPage.tsx` Games rows: when a game has more than one playthrough, a
   **dropdown button** on its row shows the Selected name. Picking another asks once, then runs the switch through
   the agent's local API, with progress and an explicit result dialog.
2. The QAM: a **Playthrough** dropdown under the selected game (Sync section), and the same row in its Sync options
   dialog, both running the same switch. A confirm opened from the QAM shows in Big Picture Mode and returns to the QAM.
3. Games page: a **game icon** on each row, and a **List / Grid** toggle with posters like the agent's Games page.
   Steam games use Steam's own library art by app id; others use the server's art. The playthrough dropdown sits
   under the poster in Grid.
4. Gamepad-only flow checked on the Deck.

## Phase 9 — Playnite (`SaveLocker-Playnite`)

`GetGameMenuItems`: **SaveLocker ▸ Playthrough ▸** lists the playthroughs, with the
Selected one ticked. Choosing one runs the switch through the local API with a real progress/result dialog (not a
toast, [[playnite-plugin-feedback-ux]]). Refused, with the reason, while the game runs. **New playthrough…** at the
bottom opens a window: name, Current save or Start fresh (files to keep), and "Play it on this PC now".

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
- **Launch:** tabs on the game page. Decky gets a dropdown on the Games row and in the QAM. Playnite gets a
  submenu. No launch notification.
