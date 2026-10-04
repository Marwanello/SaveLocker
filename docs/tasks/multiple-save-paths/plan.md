# Task: Multiple save paths per game

Planned 2026-10-04. Lets one game sync several save folders, for **any** game:
- a PC game with saves in both Documents and AppData,
- a manifest entry with more than one real location,
- a folder a user adds by hand,
- an emulator's saves and save states.

It blocks [emulator saves](../emulator-saves/plan.md): RetroArch save states are a second folder of the
same game, and PCSX2, Dolphin and DuckStation need two folders each. That branch (`emulator-saves`) waits
until Group B here is merged, then gets rebased (see *Rebase notes*).

`summary.md` beside this file is the research this plan comes from: the 2026-08-20 scoping, plus the six
requirements the emulator branch found. This file supersedes its "proposed phased shape".
`implementation-grouping.md` says which phases share a session.

## Status

| Phase | Status |
|---|---|
| 1 — Archive core | ✅ Shipped 2026-10-04 — `SaveRoot` + multi-root `SaveArchive`, include primitive ported; `MultiRootArchiveTests` 16 + `IncludeGlobTests` 9, every guard mutation-checked; unit 206/206, hardening 33, delta 33 |
| 2 — Server model + wire | ⏳ Not started |
| 3 — Agent sync core | ⏳ Not started |
| 4 — Reconcile, CLI, local API, doctor | ⏳ Not started |
| 5 — Scanners declare extra paths | ⏳ Not started |
| 6 — Manifest suggestions | ⏳ Not started |
| 7 — UI | ⏳ Not started |

---

## Background — what is single-path today

- **Server.** `MachineSavePath` (`Entities.cs`) has the primary key `(MachineId, GameId)` and no FKs.
  `Game.SuggestedSaveDir` is one portable template. The first agent to report one sets it
  (`TrySetSaveTemplateAsync`, from `PathResolver.Tokenize`); an admin can overwrite it.
- **Wire.** `GameDto.SuggestedSaveDir`/`MachineSavePath`, `MachineSavePathDto.SavePath` and the agent's
  `TrackedGameDto.Path` (the Decky contract, frozen) are each one string.
- **Agent.** `TrackedGame.SaveDirectory` is one string, with about 84 uses across 15 files.
  `CommandPoller.ReconcileGamesAsync` does four things for that one path: applies the server's machine
  path, expands the template, falls back to manifest detection, and reports both the path and the
  template back.
- **Archive.** `SaveArchive.HashDirectory`/`ComputeManifest`/`CreateArchive`/`CreateArchiveSubset`/
  `RestoreArchive` each take one folder. Restore's safety logic is written for one root: the
  nested-depth guard (`NestedRestoreDepth` compares the archive's common prefix with the target's last
  segments), the symlink guard on copy, and the delete pass.
- **Discovery.** `ManifestLoader.ResolveSaveDirectories` returns every location that resolves, but every
  caller keeps only `.FirstOrDefault()`.
- **Delta upload (server).** `ValidateManifest` accepts any relative, forward-slash path without `..`.
  `SaveVersionFile.Path` is the zip entry name. `ReconstructDelta` re-hashes the rebuilt archive as
  path + bytes in Ordinal order and refuses it unless that equals the agent's declared hash.

## The ambiguity this resolves

The manifest doesn't say whether two locations are **alternatives** (pick the one that resolves) or are
**both needed** (sync both). DRAGON QUEST III's two templates are one save folder and one sibling
`Config` folder. So:
- Manifest locations beyond the first are **suggested, never adopted on their own** (Phase 6).
- A scanner that *knows* its paths belong together, like the emulator readers, declares them, and they
  are adopted without asking (Phase 5, requirement 5).
- A user can always add a folder by hand.

---

## Design

### 1. Archive layout (`src/Shared/SaveArchive.cs`)
- **Paths and keys.** A game has one **primary** path, key `main`, and any number of **extra** paths.
  Extra keys are slugs (`[a-z0-9-]{1,32}`, unique per game, never renamed): `states`, `appdata`, … Every
  machine has the same keys, and restore matches a path by key, never by position or literal path
  (requirement 2).
- **Where files go.** The primary path's files stay at the **archive root**, so every stored version still
  restores. A single-path game's hash, delta baseline and archive bytes don't change (requirement 6). An
  extra path's files go under `.savelocker/paths/<key>/<rel>`.
- **Markers.** Each extra path whose folder exists also writes a zero-byte **marker** entry at
  `.savelocker/keys/<key>`. A marker means "this version holds this path", even when the path is empty.
  Without one, a restore can't tell an empty path from one the version never had. Markers are synthetic:
  they are listed in the manifest with the empty-file hash, and `CreateArchiveSubset` writes one when
  asked for it.
- **Reserved prefix.** `.savelocker/` is reserved. The primary folder's own listing skips it, so it never
  counts it, archives it or deletes it. Registry saves (`tasks/registry-saves`) can use the same prefix
  later.
- **One ordering.** The hash, manifest and archive treat all of a game's folders as **one list of final
  archive names, sorted once in Ordinal order**. `ReconstructDelta` hashes exactly that list. An older
  agent that has `.savelocker/…` sitting inside its primary folder must also produce the same hash.
  Hashing folder by folder would fail both checks, because names like `-x.sav` sort before
  `.savelocker/`.
- **Exclude globs** stay per game and match **archive names**, the way older agents and the server's
  exclude preview (`SyncService.PreviewExcludesAsync`) already match them. A rooted `cache/**` therefore
  applies to the primary path only, while a bare `*.log` applies everywhere.
- **Include globs** belong to each path and match names relative to that path's own folder (§4).
- **Server.** No server code learns this layout. `GetArchiveStats` skips markers. The dashboard Download
  of a multi-path version shows a `.savelocker/` folder; the KB says what it is.
- **Mixed fleet.** An older agent restores `.savelocker/…` as a real folder inside its primary save folder
  and pushes it back unchanged. Nothing is lost, the hash matches, and the paths keep traveling through
  it. Its own nested-depth guard is weaker for such games, because the prefix breaks the common prefix.
  The console flags machines whose agent is too old (Phase 7).

### 2. Restore (requirements 3 and 4)
- **Never a partial restore.** Stage and check the zip once, as today: entry count, size limits,
  zip-slip. Then, before copying anything, run every slice's checks: nested depth on that slice's entries
  with the prefix stripped, the target-folder checks, and `SavePathGuard` for real folders. If any slice
  fails, nothing is written.
- **Each path restores from its own slice** of the archive: the symlink guard on copy, then a delete pass
  limited to that folder **and its include scope**.
- **The primary path** always restores, exactly as today.
- **An extra path restores only when the archive has its marker**, delete pass included. Without a marker
  it is left alone. That covers versions older than the path, older agents' pushes, and pushes from a
  machine whose folder was missing.
- **Folders and keys.** A mapped folder that doesn't exist yet is created. Entries for a key the game no
  longer defines are skipped and logged.

### 3. Shadow folders: every push carries every path
**The problem.** Machine A can't map `states` (nothing on A matches its template) and machine B can.
Without a fix:
1. A's push drops `states` from the head.
2. `PullAsync` sets `LastSyncedHash = headHash` (`SyncEngine.cs:705`), so B pushes `states` back right
   after its pull (`PullThenPushAsync`, and the folder watcher reacting to the restore's own writes).
3. A pulls, can't apply it, and pushes again.

Every round makes two versions and burns through retention, and pushing at the same moment raises a
conflict that blocks launches. Emulators across OSes will hit this often; it is requirement 4's case.

**The fix.**
- For an extra key that isn't mapped on this machine, the agent keeps a **shadow copy** in
  `<StateDir>/shadow/<gameId>/<key>/`. A pull writes that key's slice into the shadow, and a push hashes
  and archives from it.
- Every machine then carries every key, its hash after a pull equals the head's, and nothing loops. The
  server needs no change.
- Shadows belong to the agent. `SavePathGuard` doesn't apply to them (they live inside the state dir on
  purpose) and they are never watched. `doctor` lists them with their size.
- **A key becomes mapped:**
  - If the chosen folder is empty or missing, the shadow's files move in.
  - If the folder already has files, the user picks local or cloud (`--keep local|cloud`, a prompt in the
    UI).
  - Either way the shadow is deleted afterwards.
- **A key is removed** on the server: its shadow is deleted, the next push leaves it out, and older
  versions keep it.
- **A mapped folder that's missing at push time** writes no marker, so other machines leave theirs alone.
  Their hash then no longer matches the head, so one of them pushes the key back once, and the next pull
  on the first machine recreates the folder. That's a single corrective round, not a loop.

### 4. Include scope per path (requirement 1)
- Port the include-glob primitive from `emulator-saves` commit `a56cc2b` unchanged in behavior:
  `FilterIncluded`, `ValidateIncludeGlob`, the scoped restore, and the scoped `HasLocalData`. Its tests
  (`IncludeGlobTests`) come along too, extended to several roots.
- **Where scopes live.** The primary path's scope is `Game.IncludeGlobs`, the column the emulator branch
  already adds. Each extra path's scope is on its `GameSavePath` row. The scope is identical on every
  machine.
- **Any game can use one:** "only `*.sav` under `Documents/My Games`".

### 5. Server data model (one migration, Phase 2)
- **`Games.SuggestedSaveDir` stays** as the primary path's template, and `Game.IncludeGlobs` is added.
  SQLite `ADD COLUMN` needs no table rebuild. Dropping a column would rebuild `Games`, which every cascade
  FK points at (SaveVersions, ConflictFlags, Leases, MachineScanCandidates). If `PRAGMA foreign_keys=0`
  ever failed to take effect, that rebuild's `DROP TABLE Games` would cascade through every version row.
  Keeping the column also leaves `EnrollmentService`, `Mapping` and the template endpoints untouched.
- **New `GameSavePath`** holds the extra paths: PK `(GameId, Key)`, FK to `Game` with cascade, plus
  `Label`, `Template`, `IncludeGlobs` (newline-separated) and `SortOrder`.
- **`MachineSavePath` gets PK `(MachineId, GameId, PathKey)`**, backfilled with `main`, plus FKs to
  `Machine` and `Game` with cascade.
  - It's a leaf table, so the rebuild is safe.
  - **Delete orphan rows before adding the FKs.** These rows never had FKs, and the rebuild doesn't check
    them. That closes the gap the summary noted; the hand-written `RemoveRange` calls stay as
    belt-and-braces.
- **Breaks at runtime once the key changes, so fix it in the same phase:**
  - `FindAsync(machineId, gameId)` (`SetMachinePathAsync`, `ClearMachinePathAsync`) needs the third key
    part.
  - `GetMachinePathMapAsync`'s `ToDictionaryAsync(p => p.GameId)` throws on duplicate keys, which would
    break the agent's whole game list. It must filter to `main` or group by game.
  - `SetMachinePathAsync` clears the matching scan candidate; only setting `main` may do that.
  - `HealthService`'s scan-candidate filter must look at `main` only.
- **Template and path methods.** `TrySetSaveTemplateAsync`, `SetSuggestedSaveDirAsync` and the machine
  path CRUD gain `pathKey`, defaulting to `main`. New methods add and remove an extra path (`main` can't
  be removed). Everything is audited.

### 6. Wire (additive only)
- **`GameDto`** keeps `SuggestedSaveDir`/`MachineSavePath` for the primary path and gains:
  - `IncludeGlobs` (the primary path's scope),
  - `ExtraPaths: SavePathDto[]`, where each entry is `(Key, Label, Template, IncludeGlobs, MachinePath)`.
- **Requests.** `MachineSavePathDto` gains `PathKey`. `CreateGameRequest` gains `IncludeGlobs` and
  `ExtraPaths`, applied only when the request creates the game; the caller compares what comes back, the
  way the emulator branch's `SameScope` check does.
- **Routes.** The path and template routes take `?path=<key>`, defaulting to `main`. New admin routes
  add and remove an extra path.
- **Agent local API.** `TrackedGameDto` gets a trailing optional `Paths` field. `id`/`path` stay frozen:
  `path` is the primary path, `""` when unmapped. `/api/games/{id}/folder` without a key means `main`.
  The pre-launch, sync-status, alias and pull/push-toggle routes don't change shape. Only what `inSync`
  covers changes: every root.
- Regenerate `src/Server/openapi.json`, `web/src/api-types.ts` and `agent-ui/src/api-types.ts`, and diff
  them: additions only.

### 7. Agent
- **Config.** `TrackedGame.SaveDirectory` and `IncludeGlobs` remain the primary path, so old configs load
  unchanged and the read-only uses don't change.
  - Add `ExtraPaths: List<TrackedSavePath>`, with entries `(Key, Directory?, IncludeGlobs)`. `Directory`
    is null when the key isn't mapped.
  - Add `Roots()`, which resolves an unmapped key to its shadow, and `LocalHash()`, so no caller can hash
    the primary folder by itself.
  - `SaveGameSyncState` (`AgentConfig.cs`) must also write `ExtraPaths`. Otherwise the daemon and the
    launch wrapper undo each other's changes.
- **Everything that hashes, archives, restores or checks a tracked game goes through `Roots()`:**
  - `SyncEngine`: settle, manifest, both archive paths, the pull's "already current" check,
    `HasLocalData`, the restore, and `RefuseUnsafePath`, which checks every real root because pulls delete
    inside them.
  - `SaveSettler` and `FileLockProbe`.
  - The local API's sync-status hash.
  - The CLI's `hash`, which the cross-OS tests use, and `resolve-conflict --keep local`.
  - The Deck's keep-local conflict resolve (`UiApp.cs`).
- **A missing primary folder still refuses the push** (`SyncEngine` and `OfflineQueueDrainer`), even when
  extra folders exist. Otherwise an empty primary path would wipe every other machine.
- **Watchers.** `FolderWatcher` takes every real root of a game: one `FileSystemWatcher` per root with
  one shared delay timer, so a save that writes two folders triggers one push. `TrayApp` and `Daemon`
  rebuild the watchers after a pull creates a folder, because a watcher can't watch a folder that doesn't
  exist.
- **Reconcile.** `CommandPoller.ReconcileGamesAsync` runs its four steps for each key. Manifest detection
  stays the fallback for `main` only. `ProtonRun`, the one place a Proton prefix is known, also expands the
  extra paths' templates.
- **Folder rules.** Every real root goes through `SavePathGuard`. Two paths of one game may not be nested.
  They may share a folder only when both have an include scope, and a file matched by both is refused
  (RetroArch set to keep states in the saves folder).
- **CLI.** `add-path <game> --key <k> --dir <d> [--include …] [--keep local|cloud]` and
  `remove-path <game> --key <k>`. `status` lists every path. `doctor`'s existence, writable and
  `SaveDirSanity` checks run for each root; an unmapped or missing extra path is *Info*, shown with its
  shadow size.

### 8. Discovery
- **Declared extras (requirement 5).** `ScanCandidate` gains `IncludeGlobs` and
  `ExtraSaveDirs: (Key, Dir, IncludeGlobs)[]`. `Enroller` adopts them without asking and sends them in
  `CreateGameRequest`. If the server already holds a game whose paths or scopes differ, the candidate is
  skipped with a log line.
- **Manifest suggestions.** The primary path stays `.FirstOrDefault()`, so detection results don't
  change. The other existing locations go into `ScanCandidate.AlternateSaveDirs` and are offered as "Also
  found: … — add as a save path". The key is taken from the template's last fixed segment. Nothing is
  adopted until the user confirms. The same applies to games already tracked, through the poller's
  existing scan-candidate report.

### 9. UI
- **agent-ui** (`GameManagement.tsx`): the paths as a list, each with Change and Remove. "Add save folder"
  goes through `PathBrowserModal`, offers optional include patterns, and asks keep-local-or-cloud when the
  folder has files. Phase 6's suggestions show up here.
- **Dashboard** (`SaveFoldersCard.tsx`): one section per path, holding its template row and its
  per-machine rows as they are today. Add and remove a path, with include patterns edited through
  `ExcludePatternsCard`'s `GlobChips`. Machines whose agent is too old for multiple paths are flagged.
- **Deck** (`UiApp.Games.cs`): the game page lists the paths. The existing folder browser sets one for a
  chosen key.

---

## Phases

### Phase 1 — Archive core *(Group A)*
`SaveRoot (Key, Directory, IncludeGlobs)`. The `SaveArchive` methods gain overloads that take
`IReadOnlyList<SaveRoot>`; the current single-folder signatures become one-root wrappers, so existing
callers and tests are untouched. Covers §1, §2 and §4.
**Files:** `src/Shared/SaveArchive.cs`, `tests/SaveLocker.Agent.Tests/` (new `MultiRootArchiveTests`, plus
the ported `IncludeGlobTests`).
**Verify** (xUnit; break each guard on purpose and confirm the test fails):
- two roots with their own scopes round-trip byte-identical;
- an old single-root archive restores exactly as before, and a single-root hash is unchanged;
- a primary folder holding `.savelocker/…` hashes the same as the multi-root game (the older-agent view);
- a slice without a marker is left untouched, while an empty slice with a marker has its files deleted;
- the nested and symlink guards work on each slice;
- a failing check in any slice writes nothing;
- `run-hardening-tests` and `run-delta-upload-tests` stay green.

### Phase 2 — Server model + wire *(Group A)*
§5 and §6.
**Files:** `src/Server/Data/Entities.cs`, `AppDbContext.cs`, a new migration, `Services/SyncService.cs`,
`Services/Mapping.cs`, `Services/HealthService.cs`, `Program.cs`, `src/Shared/Contracts.cs`, `openapi.json`,
both `api-types.ts`, `docs/API Reference.md`.
**Verify:**
- Migrate a copy of a real DB: rows backfill to `main`, orphans are removed, FKs cascade on game and
  machine delete, and the agent route `GET /api/games` works with an extra path mapped.
- `run-server-bugbounty-tests`, `run-health-tests` and `run-console-security-tests` stay green.
- The API diffs show additions only.

### Phase 3 — Agent sync core *(Group B)*
§3 and §7: config, `Roots()`/`LocalHash()`, shadows, every `SaveArchive` call site, the missing-primary
rule, settler, lock probe, watchers.
**Files:** `src/Agent.Core/AgentConfig.cs`, `SyncEngine.cs`, `SaveSettler.cs`, `FileLockProbe.cs`,
`Watchers.cs`, `OfflineQueueDrainer.cs`, `AgentApiServer.cs` (sync-status), `AgentCli.cs`
(`hash`, `resolve-conflict`), `src/Agent/TrayApp.cs`, `src/Agent.Linux/Daemon.cs`, `Ui/UiApp.cs`.
**Verify:** `run-agent-tests`, `run-delta-upload-tests`, `run-concurrency-tests`, `crossos.ps1`. End to
end, it's verified after Phase 4 (the CLI is what adds a path).

### Phase 4 — Reconcile, CLI, local API, doctor *(Group B)*
§7: the poller handles each key, `ProtonRun`, the folder rules, `add-path`/`remove-path`, `status`, the
local API's `Paths` and keyed `/folder`, and doctor.
**Files:** `src/Agent.Core/CommandPoller.cs`, `ApiClient.cs`, `SavePathGuard.cs` (folder rules),
`AgentCli.cs`, `AgentApiServer.cs`, `src/Agent.Linux/ProtonRun.cs`, `Doctor.cs`,
`web/src/help/cli-reference.md`.
**Verify:** through `testenv` (Windows + WSL), after giving a game a second path with `add-path`:
- run **two full sync cycles on both machines**; the folders match, the version count stops rising, and
  no conflict appears;
- unmap the path on one machine: its shadow fills and the head keeps the key;
- delete a mapped folder: the next pull recreates it;
- deleting one file spreads to the other machine, and so does emptying a path;
- `run-local-api-tests` and `run-linux-tests` pass.

### Phase 5 — Scanners declare extra paths *(Group B)*
§8, first half. This is the part `emulator-saves` Phase 1b needs.
**Files:** `src/Agent.Core/ScanCandidate.cs`, `Enroller.cs`, `AgentApiServer.cs` (`CandidateDto`),
`src/Shared/Contracts.cs` (already widened in Phase 2).
**Verify:**
- A fixture scanner that declares two scoped paths is enrolled on one machine and adopted on the other,
  with both folders and scopes set.
- An existing game whose scopes differ is skipped and logged.

### Phase 6 — Manifest suggestions *(Group C)*
§8, second half.
**Files:** `src/Agent/GameScanner.cs`, `src/Agent.Linux/LinuxGameScanner.cs`, `src/Agent.Core/AgentCli.cs`,
`CommandPoller.cs`, `ScanCandidate.cs`.
**Verify:**
- A fixture manifest game with two locations that both exist produces a suggestion, and nothing is
  adopted until the user confirms.
- The detection sweep (`tests/detection`) shows the same primary-path score as before.

### Phase 7 — UI *(Group C)*
§9.
**Files:** `agent-ui/src/components/GameManagement.tsx`, `AddGamesView.tsx`,
`web/src/components/game/SaveFoldersCard.tsx`, `GameDetail.tsx`, `src/Agent.Linux/Ui/UiApp.Games.cs`,
`web/src/help/*.md`.
**Verify:**
- In testenv's console and agents, add, change and remove a path in each UI and take screenshots.
- Check the Deck through `-Only deck`.
- `web`/`agent-ui` build and lint, and `run-appearance-consistency-tests`.

---

## Known limits
- **Older agents** carry extra paths as a `.savelocker/` folder inside their primary save folder until
  they update.
- **A machine that can't map a path stores it anyway** (as a shadow), using disk in its state dir. That is
  the price of never dropping the path from the head.
- **Rolling back to a version older than a path** leaves that path's current files where they are.
- **Exclude globs** match archive names, so to exclude something in one extra path only, a rooted pattern
  needs the `.savelocker/paths/<key>/` prefix. An include scope is usually the better tool.

## Deferred
- Resolving conflicts per path (a conflict still covers the whole game).
- Registry saves under `.savelocker/registry/` (`tasks/registry-saves`).
- File-level saves (`tasks/file-level-saves`) — include scopes plus a folder that only holds one file
  might recover some of those 702 manifest entries. Measure first.

## Rebase notes for `emulator-saves`
- Drop its migration `AddGameIncludeGlobs` and its `Game.IncludeGlobs`/`GameDto.IncludeGlobs`/
  `CreateGameRequest.IncludeGlobs` changes. This feature adds the same column and fields.
- Drop its `SaveArchive` include changes, which are ported here; keep its readers.
- Phase 1b (save states) becomes `ExtraSaveDirs: [("states", <states dir>, ["<rom>.state*"])]` on the
  RetroArch candidate.
