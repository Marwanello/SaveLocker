# Task: File-level saves

Planned 2026-10-29 from [[tasks/file-level-saves/summary|the summary]] with the maintainer's answers (the
*Decided* and *Other decisions* sections below). **High priority.** Most of its Windows reach also depends on
[[tasks/launcher-install-dirs/summary|Launcher install folders]] (`<base>` needs a known install folder; today Steam
and Playnite on Windows, Steam and Heroic on Linux).

`plan.md` lists the phases and their real dependencies. [[tasks/file-level-saves/implementation-grouping|
implementation-grouping.md]] says which phases share a session and in what order.

## Status

| Phase | Status |
|---|---|
| 0 — Measure (detection harness) | ⏳ Not started |
| 1 — Resolver: template → folder + scope | ⏳ Not started |
| 2 — Install-folder safety (enumeration, watcher, probes, size, version gate) | ⏳ Not started |
| 3 — Enrolment + adoption carry the scope | ⏳ Not started |
| 4 — Mockups: file picker + narrowing offer | ⏳ Not started |
| 5 — Hand-picked files (Add games + game page) | ⏳ Not started |
| 6 — Offer to narrow existing games | ⏳ Not started |
| 7 — Docs, help article, release notes | ⏳ Not started |

## Context

A save location is a directory throughout SaveLocker. A manifest template that names files directly in the
game's install folder — `<base>/Save.dat`, `<base>/*.sav` — trims to `<base>` itself, and
`PathResolver.IsInstallRoot` (src/Shared/PathResolver.cs:218) refuses it in `ManifestLoader.ResolveSaveLocations`
(src/Shared/ManifestLoader.cs:352): archiving the install folder would sync the whole game, and the restore's
delete pass would prune another machine's installation. Cost: **702** manifest entries have no other save path
(refused outright), **169** more lose one of several (sizing 2026-08-14). Detection sweep dropped 99.0% → 90.9%.

The building block already exists: **include scopes** (`SaveRoot.IncludeGlobs`, `Game.IncludeGlobs`,
`GameSavePath.IncludeGlobs`) — archive, hash, restore and the delete pass all honour them (SaveArchive.cs
`EnumerateItems` / `InScope` / restore delete pass lines ~661-686), the console already edits them
(`SaveFolderParts.tsx` → admin `POST /games/{id}/include-globs` → `SyncService.SetIncludeGlobsAsync`, "files that leave
the scope stop syncing and are left where they are"), and every agent applies a changed scope on its next poll
(CommandPoller.cs:487-537). So `<base>/Save.dat` becomes **folder `<base>` + scope `Save.dat`** — no archive format change.

Intended outcome: install-folder saves sync safely, users can hand-pick save files, and already-enrolled
whole-folder games get an opt-in offer to narrow to what the manifest calls a save.

## Reach: install folder only vs every file/wildcard template

**Today**, any file- or wildcard-shaped template outside `<base>` already resolves to its *parent folder* and syncs
all of it (`ResolveConcrete`, PathResolver.cs:533-572): `<winDocuments>/My Games/Foo/save.dat` syncs everything in
`My Games/Foo`, configs and logs included. Only the `<base>` case is refused.

**A — install folder only** (scope only when the trimmed folder is `<base>`)
- Pros: zero behaviour change for any game that resolves today; small surface; directly recovers the 702 + 169.
- Cons: inconsistent model — elsewhere a file template still drags in its whole folder (e.g. a Deck and a PC
  trading graphics `settings.ini` that sits beside the save).
- Risk to enrolled games: none automatic. Only games someone hand-mapped to an install folder are touched, and
  only via the narrowing offer.

**B — every file/wildcard template gets a scope**
- Pros: syncs exactly what the manifest calls a save; fewer cross-device config clobbers and log-churn pushes;
  smaller archives; one rule everywhere.
- Cons / risks to enrolled games:
  1. **Duplicate or unjoined games.** `Enroller` matches a candidate to a tracked/server game only when
     `SameScope` agrees (Enroller.cs:608, 703). A newer agent scanning a game the fleet enrolled unscoped would
     produce a scoped candidate that matches nothing → Add games lists it as untracked, or it gets created twice.
     Needs a "PC game: the server's scope wins" join rule (worth adding under A too, as a guard).
  2. **Manifest incompleteness.** Ludusavi templates sometimes miss a file the game needs (an index/slot-meta
     beside `*.sav`). Whole-folder over-includes safely; a scope under-includes silently → a pull on another
     machine yields a broken save. The cost is borne on new games with no human looking.
  3. **Manifest drift.** A scope is set once at creation (Decisions: "an existing game's scope is never changed by a
     later create"); a later manifest fix doesn't reach it.
  4. **Mixed fleet.** Old enrolments stay whole-folder, new ones scoped — two behaviours for "the same" kind of game.
- What makes B safer: apply it only through the **narrowing offer** (human sees the file list that would stop
  syncing) rather than automatically.

**Decided (maintainer, 2026-10-29) — hybrid:** automatic scopes only for `<base>` (A, where there is no alternative);
for every other file/wildcard template keep whole-folder on creation, but compute the manifest scope and surface it
as the **opt-in narrowing offer** (Phase 6) — which also covers already-enrolled games. B's benefit, with a human in
the loop for B's risk 2. The join rule from B's risk 1 is built anyway (Phase 3).

## Other decisions (maintainer, 2026-10-29)
- **Existing games:** never changed automatically; an opt-in offer to narrow (Phase 6).
- **Hand-picked files:** yes, in Add games and on the game page (Phase 5).
- **Measure:** Phase 0 sweep, numbers recorded, build continues regardless.
- **Program Files:** a narrow `SavePathGuard` exception — a folder inside a known Steam library's
  `steamapps/common/<game>` **and** with a non-empty scope; everything else under Program Files stays refused.
- **Who can narrow/change a scope:** console **and** agent — a new audited agent route; files leaving the scope are
  never deleted.

## Phases

### Phase 0 — Measure
- Extend `tests/detection/SaveLocker.DetectionHarness` (`Program.cs` coverage + sweep, `Oracle.cs:125-152`) to:
  count templates that trim to `<base>` with a file/wildcard remainder (reuse Oracle's extension test, line 141);
  score scoped results as HIT only when the returned scope matches the expected files (today it would score any
  `<base>` return as HIT).
- Run coverage + a 400-sample sweep before and after Phase 1; record "N of 702 recovered" in this plan.
- Update `pinned-cases.tsv` deliberately (Beneath a Steel Sky, line 54, flips MISS → HIT; Cave Story+ stays HIT).

### Phase 1 — Resolver: template → folder + scope
- `ResolvedSaveLocation` gains `IncludeGlobs`. In `PathResolver` add a sibling of `ResolveConcrete` that also returns the
  template remainder after the trimmed folder (`Save.dat`, `*.sav`, `saves*/*.dat`) as a glob relative to the folder.
- `ManifestLoader.ResolveSaveLocations`: when the folder `IsInstallRoot`, keep it **only if** a non-empty scope came
  out; several templates on the same `<base>` merge into one location with the union of globs (Cave Story+:
  `Save.dat` + `Profile.dat`). Unscoped `<base>` stays refused.
- Glob semantics: confirm manifest wildcards translate to `FileSystemGlobbing` (`IncludeMatcher`, SaveArchive.cs:1307),
  case-insensitively; refuse a scope that is just `*`/`**` at `<base>` (that is the whole install again).
- Defence in depth (gap found): `CommandPoller.cs:574`, `FolderSuggestions.cs:61` and `Enroller.cs:550` resolve
  templates without the install-root check → refuse mapping a folder that `IsInstallRoot` with **no** scope.
- Tests: `ManifestPathConstraintTests` / new `InstallFolderScopeTests` in `tests/SaveLocker.Agent.Tests`.

### Phase 2 — Install-folder safety
An install folder is huge and busy; today several paths walk/watch the whole folder and only filter afterwards.
- `SaveArchive.EnumerateItems` (SaveArchive.cs:1170-1212): when a root has a scope, don't recurse into directories no
  glob can match; literal globs (`Save.dat`) are a direct stat. Shared by archive, manifest, settle fingerprint, tree.
- `Watchers.FolderWatcher` (Watchers.cs:24-51): for a scoped root, ignore events whose relative path is out of scope
  (and `IncludeSubdirectories = false` when no glob has a separator).
- `FileLockProbe.ProcFsProbe` (FileLockProbe.cs:89-129, Linux): only scoped files count, like the Windows probe.
- `FolderSize.Of` (FolderSize.cs:9-23): scope-aware, so "save size here" isn't the whole install
  (AgentApiServer.cs:388, UiApp.Games.cs:138, DlcHandover.cs:95).
- `SavePathGuard` ProtectedTrees (SavePathGuard.cs:173-179) refuses anything under Program Files — the default
  Windows Steam library. Add the narrow exception: `Check` gains the root's scope + known Steam library roots
  (`SteamLayout`); allowed only when the folder is exactly `<library>/steamapps/common/<game>` (or below it) **and** the
  scope is non-empty and not `*`/`**`. Autorun, drive-root and protected-path rules still apply first. Hardening
  test: unscoped, or a sibling Program Files folder, is still refused.
- **Version gate:** an agent that predates scopes would map `<base>` whole and prune the install on pull. The server
  hands a game whose folder template is exactly `<base>` (primary or extra) only to agents reporting ≥ a new
  `FileLevelSaves.MinAgentVersion` (pattern: `Playthroughs.MinAgentVersion`, Contracts.cs:788).
- Restore when `<base>` doesn't exist on that machine (not installed): skip, as an unmapped folder does today.
- Tests: hardening suite (`tests/run-hardening-tests.ps1`) — pull into a fake install with a scope must never touch an
  out-of-scope file (game.exe, DLLs, a subfolder), push must never archive one; a pre-gate agent is not handed the game.

### Phase 3 — Enrolment and adoption carry the scope
- Scanners already pass `installDir`; candidates now get `IncludeGlobs` from the resolved location
  (`GameScanner.SuggestSaveDirAsync` GameScanner.cs:412-418, `LinuxGameScanner` 296-367, `ScanCandidate.IncludeGlobs`).
- `Enroller`: creates with the scope (Enroller.cs:166-173, already wired); add the join rule — a non-emulator
  candidate joins the same-named/AppID server game and adopts **its** scope rather than failing `SameScope`.
- `FolderSuggestions` ("Also found"): the 169 partial games get `<base>` + scope as an extra folder.
- Multi-machine test: extend `tests/run-multipath-tests.ps1` (or a new `run-file-level-tests.ps1`, own port) — two
  machines, a fake install folder with `Save.dat` + `game.exe` + logs, push/pull/conflict; the install files never move.

### Phase 4 — Mockups (decide UI before building)
Per CLAUDE.md, build on `agent-ui/mockup-features/` and `web/mockup-features/` (no new mockup project):
- Agent: `PathBrowserModal` in a "pick files" mode (tick files → folder + scope; edit as pattern), from Add games
  (game with no detected folder) and the game page's save-folder controls (`GameManagement.tsx`).
- Agent + console: the narrowing offer on the game page (proposed patterns, files that would stop syncing).
- Maintainer picks; record the chosen designs in this plan.

### Phase 5 — Hand-picked files
- `PathBrowser.cs` lists files too; a pick produces folder + include globs (literal names, escaped).
- Local API: add/set a game's folder with a scope (extend the Add games enrol path and `SavePathEditor`); agent-ui
  `PathBrowserModal.tsx`, `AddGamesView.tsx`, `GameManagement.tsx`; controller navigation (`data-pad-label`) on
  every new control.
- Changing an *enrolled* game's scope from the agent: new agent route `PUT /api/agent/games/{id}/include-globs?path=`
  beside the add-only one (Program.cs:755), reusing `SyncService.SetIncludeGlobsAsync` + `GlobConfig.ValidateIncludes`,
  audited with the machine id. Agent local API route + `SavePathEditor` call it.
- Regenerate `agent-ui/src/api-types.ts`; server change → `openapi.json` + `web/src/api-types.ts`.

### Phase 6 — Offer to narrow existing games
- The agent computes, per unscoped tracked game, the manifest scope for its folder (Phase 1 resolver, all templates
  of the game that land in that folder) and reports it (heartbeat/game report → stored on the server per game+key).
- Shown on the agent and console game page: "Only these files are saves: …. N files would stop syncing (left where
  they are)." Accept → `SetIncludeGlobsAsync`; Dismiss → remembered. Never automatic.
- Covers hand-mapped install folders and (under the hybrid) every new whole-folder game with a file-shaped template.

### Phase 7 — Docs
Help KB article (save files inside the game's folder; picking files), `docs/Decisions.md` entry (scoped `<base>`,
reach decision, version gate), `docs/API Reference.md`, release notes, CONTEXT/Backlog, task folder → `tasks/_done/`.


## Critical files
`src/Shared/PathResolver.cs`, `src/Shared/ManifestLoader.cs`, `src/Shared/SaveArchive.cs`, `src/Shared/SaveRoot.cs`,
`src/Agent.Core/{Watchers,FileLockProbe,FolderSize,SavePathGuard,Enroller,CommandPoller,FolderSuggestions,PathBrowser,SavePathEditor,AgentApiServer}.cs`,
`src/Agent/GameScanner.cs`, `src/Agent.Linux/LinuxGameScanner.cs`, `src/Server/Services/SyncService.cs`, `src/Server/Program.cs`,
`src/Shared/Contracts.cs`, agent-ui `PathBrowserModal.tsx` / `AddGamesView.tsx` / `GameManagement.tsx`, web `SaveFoldersCard.tsx` / `SaveFolderParts.tsx`,
`tests/detection/*`.

## Verification
- Phase 0 numbers before/after (sweep at 400, quote sample size); `pinned` passes with the deliberate pin change.
- `dotnet test tests/SaveLocker.Agent.Tests`; `run-hardening-tests` (new install-folder section must FAIL on pre-fix code);
  `run-multipath-tests`, `run-delta-upload-tests`, `run-agent-tests` (fresh server), `run-linux-tests` (fixture manifest
  `tests/linux/manifest.yaml` gets a `<base>/Save.dat` game; lines 159-172 currently assert the refusal → flip).
- Live: testenv pass (per memory: manual checks through testenv) with a fake install folder on Windows + WSL/Deck.
- `web` + `agent-ui` build/lint; api-types regenerated; build server with `--no-incremental`.

