# Task — Emulator save detection and sync

**Created:** 2026-08-26

**Target:** `src/Agent.Core/ScanCandidate.cs`, `src/Agent.Core/SaveDirSanity.cs`,
`src/Agent.Core/AgentApiServer.cs` (`CandidateDto`), `src/Shared/SaveArchive.cs`, new readers under
`src/Agent.Core/` (one per emulator family, listed per phase below), `src/Agent/GameScanner.cs`,
`src/Agent.Linux/LinuxGameScanner.cs`, `agent-ui/src/components/AddGamesView.tsx`,
`src/Agent.Linux/Ui/UiApp.cs`. No server/database changes in any phase (see Decisions §1).

**Goal:** detect and sync emulator saves (RetroArch, PCSX2, Dolphin, DuckStation, PrimeHack, RPCS3,
Xenia, Cemu, Azahar, Model 2, PPSSPP and Nintendo Switch via Yuzu, Citron, Eden and Ryujinx; bonus:
Supermodel, melonDS, ScummVM, shadPS4, Vita3K — widened 2026-10-07) the way EmuDeck and Steam ROM Manager / EmulationStation
actually set machines up — one server `Game` per ROM, individually trackable — with an equally
reliable detection path on Windows via EmuDeck for Windows, and without silently syncing a
save file/memory card that is actually shared across an entire console's library.

This is a **multi-phase task, mirroring the multi-session structure other large items in this
vault use** (see `Backlog.md` → *Native Linux save support*). The phases are grouped into PRs in
`implementation-grouping.md` (one PR per group, one commit per phase). Verify each phase per its own
Verify section and commit before moving to the next — do not continue past a group's own stopping
point unless explicitly instructed to.

## Status

| Phase | Status |
|-------|--------|
| 1 — `SaveArchive` include-globs + RetroArch | ✅ Shipped 2026-10-07 (PR from `emulator-saves-phase-1`) — code + tests done 2026-10-04 (branch `emulator-saves`); **merged with *Multiple save paths* 2026-10-06**, whose per-folder include scopes replaced this phase's own server column and archive code (below, *Merged onto multiple save paths*). Verified through `testenv` (Windows + WSL fixtures). **Waiting on the real-hardware pass** (EmuDeck on the Deck + EmuDeck for Windows). |
| 1b — RetroArch save states | ✅ Shipped 2026-10-07 (same PR) — built 2026-10-06: each RetroArch game declares a second folder, key `states`, scoped to `<rom>.state*`; `RetroArchSyncTests` (two machines, real server) + 4 new `RetroArchTests`, unit 247; testenv Windows ↔ WSL. Same hardware pass as Phase 1 still to do. |
| 2 — PCSX2 / Dolphin / DuckStation + shared-card warning | ✅ Shipped 2026-10-11 (PR #63, branch `emulator-saves-group-c`) — built 2026-10-07: `Pcsx2Saves` (folder card: one game per product code, the card as its folder and `BASLUS-21005*/**` as its scope; states `<serial> (*.p2s`, `.p2s.backup` left out), `DuckStationSaves` (`<title>_<slot>.mcd`, states by the card's own product codes), `DolphinSaves` (GCI `<maker>-<code>-*.gci` per game ID, Wii `Wii/title` scoped to `<type>/<id>/data/**`, named by serial from a bundled Redump table, `ConsoleTitles`, else from the save's `icon.sys` / comment / `banner.bin`). A shared card is a greyed-out row with how to switch (`ScanCandidate.NotSyncable`, maintainer's choice) and `SaveDirSanity` warns when one is mapped by hand. `GroupCSyncTests` (two machines, real server). **Captured 2026-10-07:** the Deck has one 64 MB PCSX2 file card `PS2MC-1.ps2` (shared), Dolphin's GCI folder default, Wii saves for Trilogy and Other M (banner layout as read); EmuDeck for Windows on the maintainer's PC links `PCSX2-Qt\memcards`, `duckstation\memcards` and `Dolphin-x64\User\{GC,Wii,StateSaves}` into `D:\Emulation\saves` exactly as researched (no saves yet). See *Group C — as built* |
| 3 — Identity by folders, then `gamelist.xml` names | ✅ Shipped 2026-10-07 (PR #61, branch `emulator-saves-group-b`) — D1 confirmed by the maintainer: `Enroller.ServerNameFor`/`TrackedFor` match a scoped candidate by its folders first (mutation-checked, `IdentityByFoldersTests`, two machines against a real server). `GamelistXml` names **arcade systems only** (maintainer's choice), from `Emulation/storage/es-de/gamelists` (EmuDeck for Windows' link) and `~/ES-DE/gamelists`. Group B — **Revised 2026-10-07:** Phase 1 made the save file's name the game's identity, so a name from a machine-local source could split one game in two; matching a server game by its folders first (D1) makes such a name safe **Review fixes (PR #61):** only the primary folder's patterns decide, ignoring case — a game whose other folders differ (an older agent's, an `add-path`) is joined and the missing folder added, never split into a second game. |
| 4 — PrimeHack | ✅ Shipped 2026-10-11 (PR #63, branch `emulator-saves-group-c`) — built 2026-10-07: the Dolphin reader against PrimeHack's folders (EmuDeck `saves/primehack`, Flatpak `io.github.shiiion.primehack`, EmuDeck for Windows `Emulators\primehack\User`). **Maintainer's choice:** saves from different emulators are never one game, even with the same files — `GameDto.Emulators` (server, from the sources) + `Enroller.SameEmulator`, mutation-checked. EmuDeck for Windows had created `Emulators\PrimeHack` but never run it (no `User` yet), so where a non-portable PrimeHack keeps its user folder is still to see (Group H) |
| 5a — RPCS3 | ✅ Shipped 2026-10-12 (PR #65, branch `emulator-saves-group-d`) — `Rpcs3Saves`: `dev_hdd0/home/<user>/savedata`, scope `<ID>*/**`, named by Redump's PS3 set, else the save's `PARAM.SFO` `TITLE`; EmuDeck's link, else `vfs.yml`. **Captured 2026-10-11:** the Deck's real save `BCUS98132-GAMEAUTOSAVE` (*Heavenly Sword*) and `vfs.yml` (dev_hdd0 on the SD card); the PC's `RPCS3\config\vfs.yml`. See *Group D — as built* |
| 5b — Xenia | ✅ Shipped 2026-10-13 (PR #68, branch `emulator-saves-group-e`) — built 2026-10-12 (follow-up the same day: device saves as a second folder, the named-profile layout) — `XeniaSaves`: a game is its `<TID>/00000001` saved games and their `Headers/00000001`, in a profile's `content/<XUID>` (Canary) or `content` itself (older builds); DLC, title updates, XUID 0 and the dashboard's `FFFE07D1` never synced (scope mutation-checked). Named by title ID from the bundled table (Redump disc serials → title IDs, No-Intro Arcade titles), else `Xbox 360 <TID>`. **Captured 2026-10-12:** the PC's real *Forza Horizon* / *Forza Horizon 2* saves (profile layout); the Deck's Proton Canary with both layouts side by side. See *Group E — as built* |
| 5c — Xenia profile and DLC | ✅ Shipped 2026-10-13 (PR #68) — the testenv pass and Group H still to do (final plan approved; see *Phase 5c — final plan*). **Steps 1–14 built**: formats, launch questions, profile at launch + plugin prompts (Marwanello/SaveLocker-Decky#5, Marwanello/SaveLocker-Playnite#8), the per-game profile slice, the core copied once, the game's profile on the server + alignment, DLC lists, the hold before a pull, the console DLC card + profile chip, the server hand-over, the agents' send/fetch (poller, own task, resumable, installed while Xenia is closed), held pulls re-checked every minute + notices, the console's profile-at-launch table + hand-over card, a Decky "Waiting for DLC" section (Decky#5), help article, `dlc` CLI, testenv fixtures. Unit **517**. Left: the testenv pass (`Build and Run` → *Testing emulator saves*) and Forza Horizon for real between PC and Deck (Group H). Not covered: games started outside SaveLocker's launch path (EmulationStation, Xenia directly) get no profile check or pull before launch — **DLC: the DLC check chosen** (lists only, hand-over through the server, 9-step build plan); **profile: F (checked at launch) with the profile split by game chosen** (10-step build plan). Mockups in this folder. See *Phase 5c* |
| 6 — Switch: Yuzu, Citron, Eden, Ryujinx | ✅ Done 2026-10-18 (PR #73, branch `emulator-saves-group-f`) — testenv pass and the real-game check passed 2026-10-18 (Animal Crossing 3.0.3, Eden on the PC ↔ Yuzu/Eden on the Deck). Built 2026-10-13: D2 server key (`Games.EmulatorKey`, migration `AddGameEmulatorKey`), `SwitchEmulators` (the five Yuzu forks as a table) + `RyujinxSaveIndex` (`imkvdb.arc`), one game per title across every Switch emulator (maintainer's call), ROMs with no save yet listed, and **copies in several emulators on one machine** on the agent's Emulator settings page (copy once, keep in sync, make it the synced copy; asks when both changed). Captured read-only on the PC (Eden's MP4 save, Ryujinx's real index) and the Deck (Yuzu + Ryujinx, Undertale saved in both). Unit **551**; follow-ups through 2026-10-18 (eShop names, the emulator picker, game versions, one Switch user, device saves kept in the device save): agent tests **656**. See *Group F — as built* |
| 7 — UI filter | ✅ Shipped 2026-10-07 (same PR), filter by emulator — agent UI *Emulators* chip with an *Emulator* row under it (one chip per emulator, RetroArch today), Deck *Emulators* pill. A per-console sub-breakdown was not built — it comes back with Phase 2, when more than one console has saves. |
| 7b — Game source per machine, names without "(RetroArch)" | ✅ Shipped 2026-10-07 (same PR) — built 2026-10-07 (maintainer asked; picked variation B of a clickable mockup on all three screens). Unit 280. Console checked in the browser against a seeded dev server; agent UI and Deck need the testenv pass. **Review fixes 2026-10-07** (same PR): server-decided names, the scope-join refusal, source re-send guard, EmuDeck tag, server bounds — unit 291, see `progress.md`. |
| 8 — melonDS (bonus) | ✅ Shipped 2026-10-07 (PR #61, branch `emulator-saves-group-b`) — `RetroArchSaves` generalised into `RomSaves` (D4 shape 1: `RomSaveRules` + `RomSaveFolders`); `MelonDsSaves`: EmuDeck `saves/melonds/{saves,states}`, else `melonDS.toml` `[Instance0] SaveFilePath` (1.0, read first — `Config::Load`), else `melonDS.ini`, else beside the ROM (`LastROMFolder`). Scope `<rom>.sav`, states `<rom>.ml*`. Both scanners now loop `EmulatorSaves.Sources`. **Captured on the Deck 2026-10-07:** Flatpak melonDS 1.1 has only `melonDS.ini` (absolute SD-card paths), no TOML |
| 9 — Model 2 + Supermodel (Supermodel bonus) | ✅ Shipped 2026-10-07 (PR #61, branch `emulator-saves-group-b`) — `SupermodelSaves`: `NVRAM/<set>.nv` + states `Saves/<set>.st*`, `~/.supermodel` / XDG data / Flatpak data on Linux, EmuDeck for Windows' `Emulators\Supermodel`; title from its `Config/Games.xml`, then the arcade gamelist; **EmuDeck's 29 seeded `~/.supermodel/NVRAM` files hidden by SHA-256 too — found on the real Deck 2026-10-07**, not in the plan (`Supermodel_init` rsyncs `configs/supermodel` whole). `Model2Saves`: `NVDATA/<set>.DAT` (`Emulation/roms/model2`, `Emulators\m2emulator`), **EmuDeck's 39 seeded files hidden by SHA-256** (maintainer's choice; one version in both EmuDeck repos), titles from a bundled table; **states `STATES/<set><0-9>.sta` beside `NVDATA`** (added 2026-10-07, read from the `EMULATOR.EXE` on the Deck: format string `.\STATES\%s%d.sta`, number keys pick slots 0–9, both EmuDeck launchers start it in its own folder), scoped slot by slot — `vcop*.sta` would take Virtua Cop 2's `vcop20.sta` — and a state makes a seeded set a candidate. **Captured on the Deck 2026-10-07:** the 39 Model 2 seeds and the 29 Supermodel seeds match the bundled hashes exactly; no `STATES` folder yet (it is created on the first save). **Still to see:** a Model 2 / Supermodel save written by play (nothing has been played on this Deck) **Review fixes (PR #61):** untouched seeds are listed (`ScanCandidate.UntouchedSeed`) only when a server game keeps the same file, may only join, and the first pull replaces them. |
| 10 — ScummVM (bonus) | ✅ Shipped 2026-10-07 (PR #61, branch `emulator-saves-group-b`) — `ScummVmSaves`: targets from `scummvm.ini` (Windows `%APPDATA%\ScummVM`, XDG, `~/.scummvmrc`, Flatpak), `savepath` per target → `[scummvm]` → default; scope `<target>.*`, or the engine's own prefix for the four fixed-name engines (`SKY-VM`, `sword1`, `queen`, `lure` — read from each `metaengine.cpp`); name from `description=`, cleaned. `GroupBSyncTests` (two machines, real server) for Supermodel and ScummVM. Unit **319**. **Captured on the Deck 2026-10-07:** the Flatpak's `scummvm.ini` sets the global `savepath` to `Emulation/saves/scummvm/saves`; no targets added yet, so no engine's real file names seen |
| 11 — PPSSPP | ✅ Shipped 2026-10-12 (PR #65, branch `emulator-saves-group-d`) — `ParamSfo`, `TitleFolders` (D4 shape 2), `PpssppSaves`: `SAVEDATA/<ID>*`, states `PPSSPP_STATE/<ID>_*`, named by Redump's PSP set (added to the table) else the SFO; `GroupDSyncTests` (two machines, real server, mutation-checked). **Captured 2026-10-11:** Deck Flatpak 1.20.4 (EmuDeck links into `~/.var/app/org.ppsspp.PPSSPP/config/ppsspp/PSP`), PC `memstick\PSP` (the emulator's folders are the junctions); no save on either |
| 12 — Vita3K (bonus) | ✅ Shipped 2026-10-12 (PR #65, branch `emulator-saves-group-d`) — `Vita3KSaves`: `ux0/user/00/savedata/<ID>/**`, named by the save's own `sce_sys/param.sfo`, else the installed game's, region from the ID prefix. **Captured 2026-10-11:** `pref-path` on both machines (Deck: SD card), no save yet — whether a save has its own `param.sfo` is still to see (Group H) |
| 13 — shadPS4 (bonus) | ✅ Shipped 2026-10-12 (PR #65, branch `emulator-saves-group-d`) — `ShadPs4Saves`: both layouts, folder per user, scope `<CUSA>/**`, named by the save's `MAINTITLE`, no region. **Captured 2026-10-11:** Deck AppImage and PC `ShadPS4-qt` both on the old `savedata` layout EmuDeck links, no save yet |
| 14 — Cemu | ✅ Shipped 2026-10-13 (PR #68, branch `emulator-saves-group-e`) — built 2026-10-12: `CemuSaves`: `usr/save/00050000/<low>`, scope `<low>/**`; named by Cemu's `title_list_cache.xml`, else `meta.xml`, else (2026-10-19, PR #78) a game folder's `meta/meta.xml` in the ROM folders (EmuDeck's `roms/wiiu`, Cemu's `<GamePaths>`), else `Wii U <low>` (no licensed Wii U table). **Captured 2026-10-12:** Deck AppImage with the MLC on the SD card (only system-app saves), PC portable `mlc01` with EmuDeck's junction; no game save on either |
| 15 — Azahar | ✅ Shipped 2026-10-13 (PR #68, branch `emulator-saves-group-e`) — built 2026-10-12: `AzaharSaves` + `Ncch`: `title/00040000/<low>/data`, scope `<low>/data/**` (mutation-checked), states `<program ID>.*`; named from a ROM's NCCH product code via No-Intro's 3DS sets (added to the table), else the ROM's file name, else `3DS <low>`. Citra folders are rows of it (maintainer: one game). **Captured 2026-10-12:** the Deck's three real saves, custom SD-card `sdmc`, 11 ROM headers (program IDs match the save folders); the PC's junctions |
| 16 — Linking by hand, same files (mockup version 2) | ✅ Shipped 2026-10-07 (PR #61, branch `emulator-saves-group-b`, with Group B) — each emulator row on Add games says which server game the save joins, with a **Change** button for that row only: automatic, *Keep it as its own game*, or another server game keeping the same files; a same-titled game with other file names is listed greyed out with the reason. `EnrollLinks` + `GET /api/candidates/links` + `EnrollRequest.Links`; `LinkByHandTests` 5, two of them mutation-checked; checked in the browser against a scratch server **Review fixes (PR #61):** *Keep it as its own game* opts this machine out of the fleet's same-files game, and the poller never maps a second game onto files another syncs here (`SaveFolderClaims`); Add games says when the server could not be asked. |
| 17 — Each machine's own file names (linking saves with different names) | 🚧 In progress — built 2026-10-18 (branch `emulator-saves-group-.g-i-j`, [PR #78](https://github.com/Marwanello/SaveLocker/pull/78)), with the **server merge**. Maintainer's calls: same emulator only, a **file-by-file list** that grows by itself, linked from the agent only, a clash refuses the link, the merge built now from the console only. `FileNames` + `OwnFileNames`, migration `AddFileNamesAndMerge`. Testenv pass and the hardware pass to do. See *Group G — as built* |
| 18 — Real-game checks (Group H, no code) | ⏳ Waiting for games — melonDS, ScummVM, Supermodel and Model 2 have no real-game test yet (the maintainer has no games for them as of 2026-10-07); Group C added PCSX2, DuckStation, Dolphin and PrimeHack (the Deck already has Trilogy and Other M saves to try); Group D added PPSSPP, RPCS3 (the Deck has a *Heavenly Sword* save), Vita3K and shadPS4; Group E added Cemu, Azahar (the Deck has three 3DS saves) and Xenia (the PC and the Deck both have *Forza Horizon*); Group F added the Switch emulators — ✅ checked 2026-10-18 (Animal Crossing, Eden/Yuzu, PC ↔ Deck). RPCS3 and Xenia ✅ checked 2026-10-19, Cemu, Azahar, Dolphin, PrimeHack, shadPS4, melonDS and ScummVM ✅ 2026-10-20 (Azahar: the files sync; the two machines' Azahar versions differ, so the save did not load); PCSX2 ❌ 2026-10-19, DuckStation and PPSSPP ❌ 2026-10-20 (`tasks/emulator-pull-safety/plan.md`). Rows are added as later phases ship untested; see *Phase 18* |
| 20 — Steam ROM Manager shortcuts are the emulator's game, not a game of their own | 🚧 In progress — built 2026-10-18 (same branch). **Captured read-only from the Deck 2026-10-18:** `shortcuts.vdf` (127 shortcuts) and SRM's `addedItemsV2.json`; every ROM shortcut there is recognised, no PC game is. Maintainer's calls: launcher + ROM path is enough (SRM's record stands in for an unknown launcher), a ROM with no save is listed as *No save here yet* where the save's place is known. `SrmShortcuts`. Forza Horizon listed once on the Deck still to see. See *Group G — as built* |
| 19 — Games found by their save states alone (every emulator but PPSSPP) | 🚧 In progress — built 2026-10-18 (same branch): RetroArch, melonDS, PCSX2, DuckStation, Dolphin/PrimeHack, Azahar. `StatesOnlyTests` + `GroupISyncTests` (two machines, real server, one per save shape). See *Group I — as built* |
| 21 — Emulator and console icons | 🚧 In progress — built 2026-10-18 (same branch): `web/scripts/build-platform-icons.mjs` → one sprite module per UI + the Deck's stroke table; placed in the console, the agent UI and Game Mode's Add games rows; `SvgPath` draws every path command. Screenshots in both themes and the Deck on hardware to do. See *Group J — as built* |

### Phase 1 — as built (2026-10-04), and where it departs from the plan below

Decided with the maintainer at the start of the session:
- **The include scope lives on the server** (`Game.IncludeGlobs`, migration `AddGameIncludeGlobs`,
  `GameDto`/`CreateGameRequest.IncludeGlobs`), not only in the agent — overriding this plan's "no server
  changes" line. Reason: the poller adopts a game on every machine and fills its folder from a template;
  without the scope there, a second machine maps the whole shared folder and a pull deletes the other
  ROMs' saves. [[Decisions]] has the full entry.
- **Only ROMs with a save file are candidates**, found from the `.srm` files themselves, not from
  `playlists/*.lpl` (under EmuDeck, ES-DE/SRM launch ROMs directly and the playlists are usually empty).
  No playlist reader was built.
- **Names are the cleaned file name + ` (RetroArch)`** (`RomNames`, pulled forward from Phase 2). *Superseded
  2026-10-07: the suffix is gone — see* Game sources and names.

Found and fixed, not in the plan: **`RestoreArchive` deletes every local file absent from the archive**,
so it takes the include scope too — only matching files are written or deleted
(`IncludeGlobTests.Scoped_restore_*`, mutation-checked). `HasLocalData` (the pull guard) is scoped as well, or
the other ROMs' saves would block a game's first pull on every machine. `Enroller` now fills in a game that is
tracked here but has no folder (adopted from the server) instead of skipping it, and skips — with a log line —
a game whose server-side scope differs from the candidate's. `EmulatorSystem` comes from the
`Emulation/roms/<system>` folder the ROM is in (ES-DE vocabulary, as Phase 7 wants); `EmulatorCore` is the
per-core folder name. Windows `SuggestedProcessName` stays null (no lease/exit-push lifecycle for emulator
games yet — that is the deferred SRM item).

### Save states — decided (2026-10-04, maintainer), reversing "out of scope everywhere" for RetroArch

- **They will be synced, always — not opt-in.** The maintainer accepts the cross-version risk: a state
  is tied to the core build that wrote it and often fails to load after a core update, and never across
  emulators (libretro/RetroArch#18033, #17836) — the Deck's Flatpak RetroArch and a Windows RetroArch can
  run different core versions. The `.srm` is unaffected either way.
- **As a second save path of the same game, after *Multiple save paths* lands — not now.** The considered
  alternative (point the game at the RetroArch folder and scope it to `saves/<rom>.srm` +
  `states/<rom>.state*`, which works on EmuDeck because both are siblings on both OSes) was turned down in
  favour of the cleaner model.
- **What 1b reads**, researched 2026-10-04: EmuDeck links `Emulation/saves/retroarch/states` to
  `~/.var/app/org.libretro.RetroArch/config/retroarch/states` (SteamOS) and
  `%USERPROFILE%\emudeck\EmulationStation-DE\Emulators\RetroArch\states` (Windows) —
  emudeck.github.io/save-management/steamos and /emulators/windows/retroarch. A standalone RetroArch has
  `savestate_directory` in `retroarch.cfg` (already parsed, unused) and a "sort states by core" option.
  Files per ROM: `<rom>.state`, `<rom>.state1…N`, `<rom>.state.auto` (written on every exit when auto-save
  is on — expect a push after every session) and `<rom>.state*.png` thumbnails. Scope for the states path:
  `<rom>.state*` (catches all of them, thumbnails included). Sizes run from a few hundred KB (SNES) to
  tens of MB (N64/PS1), so the per-file delta upload matters here.

Also fixed the same day: Phase 1 looked for EmuDeck-for-Windows' RetroArch under a guessed `%APPDATA%`
path; the documented one is `%USERPROFILE%\emudeck\EmulationStation-DE\Emulators\RetroArch`, now a config
root. It matters because EmuDeck's docs call `Emulation\saves\retroarch\saves` a "shortcut" — if that is a
`.lnk` file and not a link, the EmuDeck path finds nothing on Windows and this is the only way in.

Known limits: two saves that clean to one name on the same machine (two regions, two cores) keep only the
newest; two machines with different dumps of a game (`(USA)` vs `(USA) (Rev 1)`) get the same name but different
scopes, so the second machine's enrollment is skipped (logged) rather than syncing the wrong file.

### Merged onto multiple save paths (2026-10-06), and Phase 1b — as built

*Multiple save paths* (Groups A and B, on `main`) ported this branch's include primitive into its own
per-folder model, so the merge **kept `main`'s version of every shared file** and dropped this branch's copies:
the migration `AddGameIncludeGlobs` (`main`'s `AddMultipleSavePaths` adds the same `Games.IncludeGlobs`
column), its `GameDto`/`CreateGameRequest.IncludeGlobs`, its server validation and its `SaveArchive`
include code (now `SaveRoot.IncludeGlobs`, one scope per folder, restored per folder). An emulator game is
now simply a game with two scoped folders: `main` (the save) and `states`. What this branch still adds on
top of `main`:
- the readers (`EmuDeckRoots`, `RetroArchConfig`, `RetroArchSaves`, `RomNames`), `ScanSource.Emulator` and
  the `EmulatorName`/`EmulatorSystem`/`EmulatorCore` candidate fields (+ `CandidateDto`);
- `SaveDirSanity.Inspect`/`Measure` take the folder's scope — `main` measured the whole shared folder, so
  doctor and the folder check would have reported every ROM's saves as this game's (callers in the agent
  API, `add-path` and doctor pass each folder's own scope);
- enrolling a game this machine tracks with **no folder** (adopted from the server) fills it in rather than
  skipping it (`Enroller` + `AgentConfig.SetTracked`). `main`'s `Enroller` compares the candidate's folders
  and scopes with the server's game (`SameFolders`) — that replaces this branch's `SameScope`.

**Phase 1b.** `RetroArchConfig.Folders()` returns each setup's (saves, states) pair: EmuDeck's
`saves/retroarch/{saves,states}`, or a standalone `retroarch.cfg`'s `savefile_directory` /
`savestate_directory` (default `<root>/saves`, `<root>/states`). Each candidate declares
`ExtraSaveDirs: [("states", <dir>, ["<rom>.state*"])]`, adopted without asking (multi-path plan §8). The
states folder is declared **even when it does not exist yet**, so every machine defines the game with the
same folders — a machine without one gets it from its first pull. "Sort save states by core" is independent of
sorting saves, so `StatesDirFor` picks the folder that already holds the ROM's states, then the save's core
folder, then the states root.

Known limits: games enrolled by Phase 1 before this merge have no `states` folder on the server, so the
scanner's two-folder candidate no longer matches them and enrollment is skipped (logged) — Phase 1 never
shipped, so only test servers have such games; delete and re-add them. A state written by one core build may
not load in another (accepted, *Save states — decided*).

### Game sources and names — as built (2026-10-07)

The maintainer asked to drop "(RetroArch)" from names and show **how each device found a game** on the game
page of the console, the agent UI and Game Mode, in two levels: the kind, then which one ("Emulator ›
RetroArch", "Steam › Installed game", "Heroic › Epic Games", "Save-folder scan › Saved Games", "Added by hand ›
Folder picked in the agent"), plus tags ("SNES", "Flatpak", "EmuDeck", "AppID …", "Proton"). A clickable
mockup offered three placements per screen; the maintainer picked **B everywhere**: a chip under the name
on the console ("N sources" when machines differ, a popover lists each machine), a chip + tags under the
name in the agent UI, a pill beside the sync status in Game Mode.

- **Per machine, on the server.** New table `MachineGameSources` (migration `AddMachineGameSources`, key
  machine + game, cascade-deleted with either). `PUT /api/agent/source/{gameId}` (agent),
  `GET /api/games/{id}/sources` (admin), and `GameDto.MachineSource` on the agent's game list, so the poller
  re-sends a source whenever the server's copy differs — self-healing after an offline enrollment or a
  server reset. Display only; nothing syncs differently because of it.
- **The agent decides the words** (`GameSources.From(ScanCandidate)`, Agent.Core) and stores them on
  `TrackedGame.Source`, set at enrollment; `add-game` and picking a folder for a game not set up here record
  "Added by hand". The console and agent UI only map the kind to an icon and label; an unknown kind shows
  as itself. Game Mode writes "Emulator · RetroArch · SNES" — its font is Latin-1 only, so no "›".
- **Games enrolled before this** get a source from the next scan that finds them **at the same folder**
  (`GameSources.Backfill`, agent API rescan and Game Mode's scan). `AgentConfig.Save` keeps a source
  another process wrote (`??=`, mutation-checked).
- **Names:** the title alone ("Chrono Trigger") when the server has no other game by that name. **Revised
  2026-10-07 after review of PR #60** — the first version appended the console when *this machine's scan*
  had a same-named game (`GameSources.AvoidNameClashes`, now removed), so a Deck with Steam ROM Manager
  shortcuts and a PC without them named one ROM two ways. Now the names come from the save file alone
  (`Enroller.NamesFor`: title, "title (RetroArch)", "save-file name (RetroArch)") and the enroller takes the
  one whose server game already keeps this ROM's files, else the first free one (`ServerNameFor`). Scans keep
  an emulator save out of the name-merge (`ScanCandidate.DedupeKey`); two ROMs that clean to one title are
  two rows, the save file's name shown as a chip. A PC candidate that would join an emulator game with none
  of its files in scope is refused with the reason shown (Decisions → *A game never joins…*). Test servers
  holding games from earlier builds: delete and re-add them.
- **The Emulator filter (Phase 7, partly).** Agent UI: an *Emulators* chip in the source row; while it is on,
  an *Emulator* row (All / RetroArch) sits between it and the *Save folder* row. One chip per supported
  emulator, listed in `EMULATORS` — every new emulator adds itself there. Game Mode: an *Emulators* pill,
  no second row (each pill costs a d-pad press there, same reason Game Mode has no Store row).

### Linking by hand — researched 2026-10-07; same-files case built as Phase 16, the rest is Phase 17

Asked for with D1's yes: let the user choose, at enrollment, which server game an emulator save links to. A
clickable mockup with three variants is at <https://claude.ai/artifact/XNQue1U2VQGY31xU1FBK9a> (A: a
*Server game* picker inline on each Add-games row; B: *Link…* opens a search of the server's games with a
side-by-side "what will sync" and a "which save wins the first time" choice; C: add first, then the game page
suggests "Same game on another machine?" and links the two, keeping the other's versions as history).

What it would take, by case:
- **Same files, different name** (what D1 now does by itself): a manual choice only adds *Keep it as its own
  game* — which is the split D1 exists to prevent, so it would need a warning, not just a radio button.
- **Different file names for one game** — two dumps (`Chrono Trigger (USA).srm` here, `(USA) (Rev 1).srm`
  there) or two emulators (melonDS `.sav`, RetroArch's melonDS core `.srm`). This is the case worth building,
  and the expensive one: the archive stores the server's file name, so each machine needs its **own** name for
  the file — a per-machine rename map (server: a column on `MachineSavePath`; `SaveArchive` maps on push and
  pull; the scope stays the server's). Without it, a pull writes `Chrono Trigger (USA).srm` beside the Rev 1
  save and the emulator never reads it.
- **Refusals stay:** a PC game, or another console's save (sizes and formats differ) — B shows why inline.
- **Linking two existing server games** (C) is a merge: one history kept, the other's versions kept read-only.
  There is no merge on the server today ([[Backlog]] → *One game, several real sources* has the same gap).

Recommendation: B for the enrollment case (it is the only one with room to show the file-name mapping), C later
together with the server merge. Decide after the mockup; nothing here blocks Groups C–F.

**Decided 2026-10-07 (maintainer):** variant A's picker, hidden behind a per-row **Change** button (a second mockup,
<https://claude.ai/artifact/RgXaZgoZsLoGRvaCuHwQyk>, version 2). Built in two steps: the same-files case now
(Phase 16, option 1 of three offered), the different-file-names case later in its own group (Phase 17, Group G).

### Phase 16 — as built (2026-10-07)

- **Agent:** `EnrollLinks.For(serverGames, candidate)` lists one emulator row's options, the automatic one first
  (`Enroller.ServerNameFor`'s answer: join, or a new game under the first free name). Then *Keep it as its own game*
  (only when the automatic choice joins; it takes the first free name of `Enroller.NamesFor`), then any other server
  game with exactly the same folders and scopes (one older than D1). Last, greyed out with a reason, every server game
  of the same title (the name, or the name followed by a bracket) that keeps **other** files, badged *Different file
  names*, or *Whole folder* for a PC game. `GET /api/candidates/links` reads the server's games once per call; an
  unreachable server answers `reachable: false` and every row stays automatic. `POST /api/enroll` takes
  `links: [{ id, choice: "separate" | "game", gameId }]`; the enroller resolves the pick again against the server as
  it is then (`EnrollLinks.Resolve`): a game gone since, or one keeping other files, is refused with a note.
- **One fix it needed:** `Enroller.TrackedFor` now prefers the game set up **here** over one only known from the
  server. A save kept as its own game has the same files as the fleet's game, which this machine may also track,
  unmapped, under a name `NamesFor` lists first — and Add games then showed the row as not added.
- **UI:** `ServerGameLink` under each emulator row: a dot, the result ("Joins “Daytona USA”", "New game “Chrono
  Trigger (RetroArch)” on the server"), and on the right a 30px **Change** button that becomes **Close** while the
  choices are open, plus **Back to automatic** after a hand-made pick ("· chosen by you"). Picks are dropped on a
  rescan (the ids change). Game Mode on the Deck links automatically, as before.
- **Not in it:** a search of every server game ("Pick another server game…" in the first mockup) — with only
  same-files games pickable there is nothing more to search; Phase 17 brings it back.

### Phase 17 — each machine's own file names (Group G, not started)

Lets a save join a server game whose files are named differently on another machine: two dumps of one ROM
(`Chrono Trigger (USA).srm` ↔ `Chrono Trigger (USA) (Rev 1).srm`), or two emulators
(melonDS `Pokemon … (USA).sav` ↔ RetroArch's `Pokemon … (USA).srm`). Phase 16 already shows these, greyed out.

What has to change, all of it on the path every sync takes:
1. **Server:** a per-machine name map for a game (a column on `MachineSavePath`, or a table beside it), set at
   enrollment and shown in the console; migration, API change, `openapi.json` and both `api-types.ts`.
2. **Push:** files are renamed into the server's names on the way into the archive.
3. **Pull / restore:** renamed back to this machine's names on the way out.
4. **The in-sync hash and the manifest** use the server's names, or two linked machines never agree they are in
   sync.
5. **Every file list** (conflict screen, version history, "what will sync", dry run, restore preview) shows one
   side's names consistently.
6. **States:** RetroArch, Model 2 and Supermodel name states `<rom><suffix>`, so the rule is "swap the ROM-name
   prefix" in every folder of the game. ScummVM is excluded (not ROM-named).
7. **The first sync** asks which machine's save wins (the two have different progress), as in the first mockup's B.
8. **Unlinking** puts this machine's own names back.

**Open questions (decide before building):**
- **Across emulators, or the same emulator only?** melonDS `.sav` and RetroArch's melonDS core `.srm` are the same
  raw bytes for most DS games, but not every pair of emulators is (some add footers or RTC blocks). Same emulator
  only is the safe first step; a list of known-compatible pairs could follow.
- **States across emulators:** never synced (a melonDS state does not load in RetroArch) — confirm, and say so in
  the picker ("syncs the save file only").
- **Where the map lives:** per machine and folder key (`MachineSavePath`), or per machine for the whole game
  (one prefix swap)? A prefix swap covers every ROM-named emulator; an explicit file-by-file map would also cover
  odd cases but is more to show and edit.
- **Who can change it:** only at enrollment from the agent, or also from the console's game page (*Same game on
  another machine?*, mockup variant C)?
- **Merging two existing server games** (variant C) needs a server merge, which does not exist (also blocks
  [[Backlog]] → *One game, several real sources*). In Phase 17, or later?
- **Name collisions:** a pull that would write a file this machine already has under another game (two ROMs mapped
  onto one name) — refuse the link, or refuse the pull?
- **Console display:** which names the console shows for a game linked under two names (server's only, or both with
  the machine).
- **Testing:** a two-machine test per emulator family with different names on each side, including states, a
  conflict, and a restore of an old version on the renamed side — the riskiest paths, mutation-checked.

### Group C — as built (2026-10-07, branch `emulator-saves-group-c`)

Decided with the maintainer at the start of the group:
- **A shared memory card is a greyed-out row, never a game** (option A of three: the others were "the whole card
  as one game, with a warning" and "hide it"). `ScanCandidate.NotSyncable` carries why and how to switch the
  emulator to a card per game. The enroller refuses it, `EnrollLinks` offers nothing, and Game Mode shows it too.
  Each fix was checked in the emulator's own source. **PCSX2:** Convert → Folder makes a new card, then Use for
  Slot 1. **Dolphin:** Slot A → GCI Folder copies the old card's saves in by itself (`MigrateFromMemcardFile`).
  **DuckStation:** the per-game cards start empty, and Memory Card Editor copies saves across.
- **States sync, without PCSX2's `.p2s.backup` copies**: D3 stands, but a backup per slot would double a PS2
  game's states against the upload cap.
- **Wii is in this group** (it was D4 shape 2, planned for Group D): without it PrimeHack finds nothing, and
  neither did the Deck.
- **Saves from different emulators are never one game, even with the same files.** This is the reverse of what
  D1 alone gives: Metroid Prime Trilogy saved in Dolphin and in PrimeHack on the Deck stays two games. The server
  now says which emulators each game was found through (`GameDto.Emulators`, from `MachineGameSources`, additive).
  `Enroller.SameFiles` also requires the same emulator, and a game no emulator reported yet stays open to any.
  **Group F's plan (a Switch save syncing Eden ↔ Ryujinx) contradicts this: confirm with the maintainer when F starts.**

What the plan below got wrong:
- **PCSX2 under EmuDeck is NOT per game.** EmuDeck's `PCSX2.ini` sets `McdFolderAutoManage = true`, but that only
  affects a *folder* card, and PCSX2 creates a *file* card. The Deck has one 64 MB `PS2MC-1.ps2`. The card's type is
  read from the disk (a folder holding `_pcsx2_superblock`), not from any setting.
- **DuckStation and Dolphin ARE per game by default**, upstream and in EmuDeck: `Card1Type = PerGameTitle`, and
  `MAIN_SLOT_A = MemoryCardFolder`.
- No ROM-folder walk (step 4): as with every reader since Phase 1, discovery starts from the save.
- **A PCSX2 game's folder is its card, not the folder of cards** (changed 2026-10-11, maintainer's report: the
  Windows PC's European Warrior Within became a second game, *… (PCSX2)*, beside the Deck's). The scope was
  `<card>/BESLES-52822*/**`, and the card's name is each machine's own: `PS2MC-1_converted.ps2` on the Deck,
  `MC1.ps2` from EmuDeck for Windows (`Slot1_Filename`). Same game ID and region, other files, so D1 never joined
  them and the name was taken. The machine's folder is already its own (`MachineSavePath`), so the card moved
  there and the scope is `BESLES-52822*/**` on every machine; nothing on the server changed. Games enrolled with
  the old scope (only the test rig's: Group C is unreleased) have to be removed and added again.
- **Names come from the serial, not from inside the save** (changed 2026-10-08, maintainer's report from the Deck:
  *Prince of Persia: Warrior Within* and *The Two Thrones* both showed as "Prince of Persia" — the first line of
  `icon.sys` is often only the series). `ConsoleTitles` looks the save's serial up in a table built from Redump's
  dats as libretro-database ships them (CC BY-SA 4.0, `src/Agent.Core/ConsoleTitles/NOTICE.md`; 32,182 serials
  across PS1, PS2, PS3, GameCube and Wii, ~400 KB embedded): *Prince of Persia - The Two Thrones*. Each region's
  disc has its own serial and reads only its own saves (verified — a save moved across regions has to be renamed with
  a save tool, and even that fails where the save checksums its own name), so a USA and a European save are two
  games, which the scope already made them. **The region is not in the name** (changed 2026-10-11, maintainer's
  choice from a clickable mockup): the table keeps title, region and languages apart, the candidate carries a
  `GameReleaseDto` (console, region, game IDs, languages), and the server keeps it on the game and lets two regions
  share a name — unique by (name, region), migration `AddGameRelease`. A save joins a game of its name only in its
  own region; another region's same-named game is listed in *Linking by hand* as "Another region", never pickable.
  The console's game page and the agent's game page show the region as a chip that opens the release's details
  (hover, click or tap; Esc closes); the console sidebar and grid, the agent's Games list and Add games show the
  region as text. Only a serial Redump
  has no disc of — homebrew, WiiWare, channels — falls back to inside the save: PS2 `icon.sys` (Shift-JIS,
  full-width folded by hand, because the Linux agent runs with invariant globalization), the GameCube comment, Wii
  `banner.bin` (UTF-16BE, layout read on the Deck), DuckStation's card name. PCSX2's `GameIndex.yaml` (GPL-3.0)
  and DuckStation's `gamedb.yaml` (CC BY-NC-ND) were ruled out by their licenses. PS4 has no Redump set; its saves
  carry their own title (`sce_sys/param.sfo`). Games already on a server keep their names (D1).

The readers (`MemoryCards`, `Pcsx2Saves`, `DuckStationSaves`, `DolphinSaves` with PrimeHack as its second
emulator):

| Emulator | A game is | Scope | States |
|---|---|---|---|
| PCSX2 | a product code's save folders on one folder card | `BASLUS-21005*/**` in the card's own folder (each save's `_pcsx2_index` included; the card's superblock never) | `<serial> (*.p2s` |
| DuckStation | a per-game card | `<title>_1.mcd`, `<title>_2.mcd` (never `<title>_*`: `Crash_*` takes `Crash_Team_1.mcd`) | `<code>_*.sav` per product code of the game the card is named after (every disc; not a save imported from another game), else every code on the card |
| Dolphin / PrimeHack, GameCube | a game ID's GCI files in one card folder | `<maker>-<code>-*.gci` | `<game id>.s*` |
| Dolphin / PrimeHack, Wii | a title folder holding `banner.bin` (types 00010000/1/4) | `Wii/title` + `<type>/<id>/data/**` | `<id>*.s*` (the folder names only 4 of the ID's 6 letters) |

Also in the group:
- **The `SaveDirSanity` check**: a shared card file, a whole PCSX2 folder card or a GCI folder of several games,
  judged by what would be archived. It is a warning, never a refusal.
- **The Console row** on Add games, labelled by the agent (`CandidateDto.emulatorConsole`).
- **`EmulatorChipTests`**: every reader's emulator has its chip.
- **A test-isolation fix found on the way**: the Supermodel and Model 2 scans the tests drive read the developer's
  real ES-DE gamelists. That failed on the maintainer's PC once EmuDeck for Windows wrote a `model3` gamelist.

Known limits:
- A PS2 game saved on `Mcd001` on one machine and `Mcd002` on another is two games.
- A multi-disc PS1 game's states on a later disc's code are in scope only once that disc has saved to the card
  (the card's codes say which discs there are).
- A custom GCI or NAND folder set in `Dolphin.ini` is not read.
- A save made before the PCSX2 card was converted is in the new card only once converted.

### Group D — as built (2026-10-11, branch `emulator-saves-group-d`)

Decided with the maintainer at the start of the group:
- **Build from the research, check on the machines that exist.** Neither machine had a PSP, Vita or PS4 save, so the
  readers follow EmuDeck's scripts and the emulators' source, checked against what both machines do have (read-only
  SSH to the Deck, the PC's EmuDeck for Windows). All four go to Group H.
- **PSP and PS3 are named by Redump, then `PARAM.SFO`** — the same rule as PS1/PS2/GameCube/Wii. Redump's PSP set is
  now in the bundled table (3,798 serials), rebuilt at the same pinned libretro-database commit so no other row moved.
  A Vita or PS4 game has no Redump set and is named by its `PARAM.SFO`.
- **Both bonus emulators ship** (Vita3K and shadPS4).

Added after the PR opened (2026-10-12, maintainer's calls):
- **Vita3K reads every user's `savedata`** (`ux0/user/*/savedata`), not only `00`; RPCS3 and shadPS4 already read every
  user. No emulator ties a save to a user (RPCS3 and shadPS4 write `ACCOUNT_ID` as zeros and never check it), so
  different users on two machines join one game; the help article says how to move a game to another user's folder.
- **A PSP game played from states only is a game** (`ScanCandidate.StatesOnly`, *Save states only so far* on Add
  games): its ID comes from the state's name, its `SAVEDATA` scope is declared so the first in-game save joins, and a
  save anywhere on the machine wins. A machine that pushed only states never empties another's real save — that pull
  is refused (`GroupDSyncTests`). The other emulators are Phase 19.

What the research did not say:
- **A region from the ID's prefix.** A PSN game (`NPUB…`, `NPUH…`) and every Vita game would otherwise have none, and
  games are unique by (name, region). `ConsoleTitles.PrefixRegion`: the third letter of a PSP or PS3 ID, the fourth of a
  Vita one (`PCSE` USA, `PCSB` Europe). A PS4 `CUSA` ID names no region, so none is given.
- **The Deck's real PS3 save says "Heavenly Sword™".** `PARAM.SFO` titles carry trademark signs and line breaks; both
  are dropped from a fallback name.
- **On the Deck, `~/Emulation` holds the links and the SD card holds the storage.** Vita3K's installed games are read from
  the `app` folder beside the real `savedata`, not under the EmuDeck root that was scanned.
- **On the PC, the links go the other way:** `Emulation\saves\<emulator>\…` are real folders, and the emulators'
  own folders are the junctions (`PPSSPP\memstick\PSP\SAVEDATA`, `rpcs3\dev_hdd0\home\00000001\savedata`, …).
  Real paths are recorded, so this needs no special case.

The readers (`ParamSfo`, `TitleFolders` — D4 shape 2, for Group E to reuse — and one per emulator):

| Emulator | A game is | Scope | Name | States |
|---|---|---|---|---|
| PPSSPP | its ID's folders in `PSP/SAVEDATA` | `ULUS10041*/**` | Redump PSP, else the save's `TITLE` | `PPSSPP_STATE/<ID>_*` |
| RPCS3 | its ID's folders in `dev_hdd0/home/<user>/savedata` | `BCUS98132*/**` | Redump PS3, else the save's `TITLE` | none |
| Vita3K | its one folder in `ux0/user/00/savedata` | `PCSE00120/**` | the save's `sce_sys/param.sfo`, else the installed game's | none |
| shadPS4 | its folder in a user's `savedata` (old `savedata/<user>`, new `home/<user>/savedata`) | `CUSA00900/**` | a save's `sce_sys/param.sfo` `MAINTITLE` | none |

Known limits:
- A portable PPSSPP or RPCS3 outside EmuDeck's folder (an exe anywhere) is not looked for.
- RetroArch's PPSSPP core keeps PSP saves in its own folder: not read.
- Vita3K reads user `00` only; RPCS3 reads every user, and a game saved under two users there is the copy written last.
- A PS4 game's EU and US releases have different `CUSA` IDs and the same name with no region: the second one to be
  added is called *… (shadPS4)*.

### Group E — as built (2026-10-12, branch `emulator-saves-group-e`)

Decided with the maintainer at the start of the group:
- **Captures from both machines plus the research** (read-only SSH to the Deck, the PC's EmuDeck for Windows).
- **A Citra save and an Azahar save of one game are one game**: Citra's folders are rows of the Azahar reader.
- **Bundle a title table where the licence allows** (libretro-database, CC BY-SA 4.0, same pinned commit, every existing
  row unchanged): **Xbox 360** by title ID — a Redump disc serial spells it out (`MS-2505` = `'M' 'S'` + 2505 =
  `4D5309C9`, *Forza Horizon*, the folder Xenia saved it in on the PC), No-Intro gives Arcade titles' outright;
  **3DS** by product code (`CTR-P-AREE`) from No-Intro's 3DS sets, read from a ROM's NCCH header. **No Wii U table**: no
  bundlable source maps a Wii U title ID to a name, and the only 3DS title-ID list found (3dsdb) has no licence.

What the captures showed that the research did not say:
- **Xenia Canary saves per profile**: `content/<XUID>/<TID>/00000001/<package>/` with `Headers/00000001/<package>.header`;
  `content/0000000000000000/` holds content no profile owns, and `FFFE07D1` is the dashboard's profile data. The Deck's
  Proton Canary has the old `content/<TID>/` layout (DLC, updates) beside the profile one. A save's header names the save
  ("MaroLFC Car Setup Files"), never the game, and Xenia leaves the STFS title-name field empty.
- **The Deck's Cemu is an AppImage**, MLC on the SD card (`Emulation/roms/wiiu/mlc01`); its `usr/save` holds only system-app
  saves (`00050010`) and Cemu's `system/` account data, both skipped.
- **The Deck's Azahar is an AppImage** with `use_custom_storage=true` and `sdmc_directory` on the SD card; states in
  `~/.local/share/azahar-emu/states` (empty). Its three saves are `title/00040000/{00054000,001bb200,0016e300}/data`; the
  ROMs' NCSD headers put the first NCCH at 0x4000 and their program IDs match the folders (Super Mario 3D Land, Metroid:
  Samus Returns; the third has no ROM left).

The readers (`TitleFolders` from Group D, one per emulator):

| Emulator | A game is | Scope | Name | States |
|---|---|---|---|---|
| Cemu | `usr/save/00050000/<low>` | `10145d00/**` | Cemu's title cache, else `meta.xml`, else a ROM game folder's `meta/meta.xml`, else `Wii U <low>` | none |
| Azahar | `sdmc/Nintendo 3DS/<id0>/<id1>/title/00040000/<low>/data` | `00054000/data/**` | the ROM's product code (No-Intro), else its file name, else `3DS <low>` | `states/<program ID>.*` |
| Xenia | `content/<XUID>/<TID>` (or `content/<TID>`) saved games | `<TID>/00000001/**`, `<TID>/Headers/00000001/**` | the title ID (Redump, No-Intro), else `Xbox 360 <TID>` | none |

Known limits:
- A Cemu save made under a different account on another machine is not remapped (*Deferred*); the help article says to use
  the same account.
- 3DS extdata is not synced (*Deferred*); a 3DS game played from states only is not listed yet (Group I).
- Xenia: a game's device data under XUID 0 is not synced; a native Linux Xenia's storage root (`~/.local/share/Xenia`) is
  from the source, not a capture.
- Wii U and 3DS names are machine-local: the first machine to add a game picks its name; D1 joins the rest by folder.

Follow-ups on the same branch (2026-10-12, maintainer asked after a user-binding review of every emulator):
- **Device saves** (built): some games save for the device, not a player, into `content/0000000000000000` (Rock Band's
  calibration, Bullet Soul's options and high scores — xenia-canary#743). Every Xenia game declares that folder as its second
  one, key `device`, with the game's save scope (so the DLC Xenia keeps there never syncs), even before it exists. A game with
  only device saves is listed; its profile folder is the newest profile on the machine.
- **Named-profile layout** (built): Canary's 2023 `content/<profile name>/<title ID>` (PR #118) is read, ranked between an XUID
  profile and the no-profile layout.
- **User binding, researched**: no emulator in this task checks a save's owner when it loads one. Cemu (`user/<account>` folder,
  first account always `80000001`), RPCS3/shadPS4/Vita3K (user folders; `ACCOUNT_ID` written as zeros, RPCS3 never verifies
  `PARAM.PFD`) and Xenia (`content/<XUID>`) bind by **folder** only. Xenia makes each new profile's XUID at random
  (`GenerateXuid`: `0xE03…` + random), and some Xbox 360 games may keep the creator's XUID inside the save — so the safe Xenia
  setup is **one profile copied to every machine**, which Phase 5c below makes possible.

### Phase 5c — Xenia profile and DLC (designed 2026-10-12, not built — waiting on the maintainer's calls)

The maintainer asked for: copying the **whole Xenia profile** (without any game's saves) to another machine so saves work
there, and an **option to sync a game's DLC**, because some saves need it. Replaces the first idea of syncing one game's `.gpd`.

**What is on disk** (captured on the PC 2026-10-12):
- The profile: `content/<XUID>/FFFE07D1/00010000/<XUID>/` — `Account` (404 bytes, **encrypted** like a real console's; holds
  the gamertag), `FFFE07D1.gpd` (the dashboard's: profile settings, titles played) and one `<title ID>.gpd` per game played
  (achievements and the three title-specific settings games may write) — plus `content/<XUID>/FFFE07D1/Headers/00010000/<XUID>.header`.
  About 210 KB on the PC. The header carries no gamertag.
- DLC: Xenia always files marketplace content under XUID 0 (`ContentManager::ResolvePackagePath`):
  `content/0000000000000000/<title ID>/00000002/<package>/` and `Headers/00000002/`. **Forza Horizon's 10 packages are 1.0 GB**,
  twice the server's 500 MB upload cap (`Storage:MaxUploadMb`). Title updates are `000B0000` beside them (seen on the Deck).

**Step 1, first: the profile as its own entry.**
1. Add games lists each profile on the machine as one row, *Xenia profile — <gamertag>* (decrypted from `Account` with the
   retail key Xenia itself ships, else the XUID). Its folder is `content/<XUID>`, scope `FFFE07D1/00010000/<XUID>/**` and
   `FFFE07D1/Headers/00010000/<XUID>.header` — the profile only, never a game's save. The XUID in the scope is its identity, so
   two profiles are never one entry (D1).
2. **Copying it to a machine that lacks it:** that machine's Add games lists the server's profiles it does not have
   (*On the server*), with the folder worked out from its own Xenia `content` folder. Adding one pulls it, and the profile
   appears in Xenia; sign in to it once (Xenia picks it by itself when it is the only profile). From then on every game saved
   under it on the first machine lands in the same `content/<XUID>` folder here — the per-game rows need nothing new.
3. **After enrollment:** a profile can be added from Add games at any time, and each Xenia game's page says which profile its
   saves are under, with *Sync this profile too* when that profile is not tracked yet.
4. **A machine with its own different profile:** it keeps it; the copied one is a second profile. Add games says the game's
   saves only show under the signed-in profile.
5. **Conflicts:** the profile changes whenever any game unlocks an achievement or writes a setting, so playing on two machines
   between syncs is a conflict (whole files, no merge).

**Step 2: DLC, optional, per game.**
1. A *Sync DLC (10 packages, 1.0 GB)* tick on the game's Add games row, and *Add DLC* on its page later. Off by default.
2. Proposed shape: its own entry per game, *Forza Horizon — DLC*, folder `content/0000000000000000`, scope
   `<title ID>/00000002/**` and `<title ID>/Headers/00000002/**`, rather than a third folder of the save game. DLC packages do
   not change, so the entry keeps one version and never conflicts, and the save's own versions stay small.
3. **Blocker — the upload cap:** a 1.0 GB entry cannot be pushed under the 500 MB cap, and pulling gigabytes over a slow link
   needs the chunked transfer to cope. Needs a server change: a separate, larger cap for DLC entries (`Storage:MaxDlcUploadMb`).

**Open questions for the maintainer:**
- Profile: its own entry scoped to the XUID (recommended), or a folder of every Xenia game?
- DLC: its own entry (recommended) or a third folder of the game; title updates (`000B0000`) included with DLC?
- The upload cap for DLC: a separate setting, and what default?
- Gamertag from the decrypted `Account` (Xenia's public retail key), or show the XUID only?

**Maintainer's answers (2026-10-12)** — not the recommended options on the first three, so two consequences are recorded
here and confirmed with the maintainer before any code:
- **The profile is a folder of every Xenia game**, key `profile`, folder `content/<XUID>`, scope
  `FFFE07D1/00010000/<XUID>/**` and `FFFE07D1/Headers/00010000/<XUID>.header`, offered at enrollment and later (*More save
  folders found*). Consequences:
  1. Every game on the machine then syncs the *same* files. `SaveFolderClaims` refuses to map a second game onto files another
     syncs (the Group B fix that stops two games ping-ponging one save), so it needs an exception for this folder.
  2. **A pull of one game restores the profile as that game last pushed it.** Achievements or settings another game wrote since
     are overwritten on that machine: play game B, then sync game A, and B's new achievements in the profile are gone (B's own
     saves are safe). Avoiding it needs a merge of the profile's files — or the profile as its own entry.
- **DLC is a third folder of the game**, key `dlc`, folder `content/0000000000000000`, scope `<TID>/00000002/**` and
  `<TID>/Headers/00000002/**`, opt-in. Consequence: the server keeps every version as its own full zip (`ArchiveStore`, 7 kept
  by default) and a pull downloads the whole archive, so Forza Horizon becomes ~1 GB per version — **~7 GB on the server, and
  1 GB downloaded on every pull** — while pushes stay small (only changed files are sent).
- **A separate cap for archives with DLC, 2 GB** (`Storage:MaxDlcUploadMb`, server; the agent's size backstop learns it too).
- **The gamertag is decrypted from `Account`** with the retail key Xenia ships, to name the profile folder in the UI.

**Mockups, round 1 (2026-10-12):** three ways to show the profile (its own row, a Profiles section, split by owner) and three
for DLC (a sub-entry, a check with nothing uploaded, a library). Found in Xenia Canary's source while drawing them: a game's
`<TID>.gpd` is loaded only when `FFFE07D1.gpd` lists the title (`UserProfile::LoadProfileGpds`); otherwise the first launch makes
an empty one and overwrites it (`UserTracker::AddTitleToPlayedList`). So a profile split by game must carry the game's row of
`FFFE07D1.gpd` too, merged per row, and an achievements file alone never works.

**Round 2 (same day): the maintainer liked none of the profile options and chose the DLC check.**

*Profile, three new options* — [`phase-5c-profile-mockups.html`](phase-5c-profile-mockups.html), agent (PC, Deck) and console:
- **D · One setting, every machine.** No entry anywhere: Settings → Emulators → Xenia, “One Xbox profile on every machine”.
  Turning it on copies the profile, sets Xenia to sign in with it, and each game then carries its own achievements and its
  row of `FFFE07D1.gpd`; the account once.
- **E · Gamer card, merged.** The profile shown as an Xbox gamer card (gamerscore, every game's achievements and where each was
  unlocked). Synced whole but **merged entry by entry**: achievements are unioned (each GPD achievement entry has an unlocked
  flag and an unlock time — `GpdInfoTitle`), gamerscore recounted, settings newest-wins, so the profile never conflicts.
- **F · Checked at launch.** Nothing shown until Play: if Xenia would sign in as another profile than the game's saves are
  under, a sheet asks and SaveLocker switches it. Xenia Canary reads the start-up profile from its config,
  `logged_profile_slot_0_xuid` under `[Profiles]` (`profile_manager.cc`), so the switch is a config write before launch.
  Profile files sync per game as in D.

*Profile: F chosen, together with the split by game* — the full journey is in
[`phase-5c-profile-launch-mockups.html`](phase-5c-profile-launch-mockups.html) (Journey and Build plan tabs). The two halves
need each other: the split decides **what syncs** (each game's `<TID>.gpd` and its row of `FFFE07D1.gpd`; the account, header and
settings once), F decides **who Xenia signs in as**, so it loads the profile those files belong to. Three conditions, each a
build step below:
- **The same XUID on every machine.** Xenia makes each new profile's XUID at random, so a Deck with the game under its own
  profile is offered, once, to use the game's profile (copy the core, add the row, pull) or to keep its own as a separate game.
- **A prompt only where SaveLocker is in the launch path:** the Linux launch command (shown by the Decky plugin over Game
  Mode) and the Playnite plugin's pre-launch hook (Windows). Elsewhere “Ask” switches without asking, or, with Xenia started
  directly, the profile is set ahead of time after each sync and a notice follows if Xenia ran as another profile.
- **Never while Xenia runs:** the config line and the row merge are written only when the game's process is not running.

*Build plan for the profile, one commit each:*
1. **Capture and confirm, no code:** from PC and Deck, `FFFE07D1.gpd`, two title GPDs, `Account`, the profile header and
   `xenia-canary.config.toml`. Confirm that `logged_profile_slot_0_xuid` (`[Profiles]`) signs in at boot, whether Xenia rewrites it
   when signing in from its own menu or on exit, and where the config lives in each setup (EmuDeck Windows, Proton, portable,
   Documents).
2. **XDBF reader and writer** (`Agent.Core/Xdbf.cs`): header, entry and free-space tables; achievements (namespace 1),
   settings (3), title rows (4). Unchanged entries are written back untouched; the captured files round-trip byte for byte.
3. **The game's part of the profile:** a `profile` folder for every Xenia game under an XUID —
   `content/<XUID>/FFFE07D1/00010000/<XUID>`, scope `<TID>.gpd`. Its row travels as a small file in a SaveLocker-owned folder
   (`<state>/xenia/rows/<game>`, key `profile-row`): exported before a push, merged after a pull (insert or replace that one row,
   recount the gamerscore and achievement totals). A per-emulator hook in `SyncEngine` (before push / after restore), guarded by
   `GameActivity`. `SaveFolderClaims` is unchanged since no two games share a file. Two-machine test: Halo 3's newer row
   survives a Forza Horizon pull, and a whole-file copy fails it.
4. **The core, copied once:** account, header and the main file's settings exported to `<state>/xenia/core/<XUID>` (key
   `profile-core`, ~100 KB) and restored **only where the profile is missing** — a new restore-if-missing folder mode. Settings
   changed later are not synced in this phase (newest-wins per setting is a follow-up).
5. **The game's profile on the server:** `Game.EmulatorProfile` (`xenia:<XUID>`), set by the first enrollment, shown and
   changeable in the console. Migration; regenerate `openapi.json` and `api-types.ts`.
6. **Aligning a machine:** Add games offers “Use <profile> here” (folder `content/<game XUID>/<TID>`, declared before it exists)
   or “Keep <own> as a separate game”; then core → row → pull, in that order (the row before the save, because Xenia ignores a
   title GPD the main file does not list). A folder under the game's XUID ranks above one under another XUID.
7. **Xenia config and the decision:** `XeniaConfig` finds the config through the existing Xenia setups and edits
   `logged_profile_slot_0_xuid` line by line (comments and order kept, `[Profiles]` added if missing, atomic, one backup).
   `XeniaLaunchProfile.Decide(configured, gameProfile, policy, canPrompt)` → start / switch / ask / leave, pure and unit-tested.
   Policy: game → machine → console default (Ask). Pre-profile layouts skip the check.
8. **Into every launch path:** `PrepareLaunchAsync` (Linux launch command, the Playnite route, the agent API) gains a profile
   step after the pull. Ask raises a pending prompt (`GET /api/launch/prompts`, `POST …/{id}/answer`) and waits up to 30 s,
   then uses the game's profile. With nothing before the launch, the config is set after each sync while Xenia is closed, and a
   notice follows an exit that ran as another profile. Activity entries and agent event codes for the console.
9. **Prompts:** the Decky plugin (its own repo) shows a modal while a launch waits; the Playnite plugin a real dialog; the agent
   UI too when open. Without Decky in Game Mode, Ask acts as Switch and says so afterwards.
10. **Console, help, testenv, Group H:** the game's profile line and a “Profile at launch” table from heartbeats, the
    Configuration default, a help article, testenv with two profiles and a config file, and Forza Horizon between PC (Playnite)
    and Deck (Game Mode, with and without Decky). The DLC check then reuses the Decky prompt for its own “Ask” in Game Mode.

*DLC: the DLC check* — chosen. The whole journey, every screen on PC, Deck and console, is in
[`phase-5c-dlc-check-mockups.html`](phase-5c-dlc-check-mockups.html) (Journey and Build plan tabs). Decided by the design:
- **Stored:** a list per save version only (each package's ID, display name, type, size; about 2 KB). Packs are never stored;
  a **hand-over** passes them through the server from a machine that has them and deletes them on delivery, or after 7 days.
- **Policy before a pull** when packs are missing: **Ask** (default) / Pull anyway / Hold; console-wide, a machine may override.
  An automatic pull (before launch, from the console, the poller) never asks: “Ask” holds it and notifies.
- **Sending:** per machine, Ask me (default) / Send automatically / Never.
- **Caps:** hand-over `Storage:MaxHandoverMb`, 2 GB, separate from the 500 MB save cap. This replaces the DLC-as-folder and
  `MaxDlcUploadMb` calls above.
- **Covers title updates** (`000B0000`) as well as DLC (`00000002`): a save from a newer update may not load at all.

*Build plan, one commit per step:*
1. **Inventory:** `ContentInventory` per emulator, Xenia first — each package folder under `content/0000000000000000/<TID>/`
   `00000002` and `000B0000`, display name and type from its file in `Headers/`, total size; identity = package ID + size, no
   hashing of package data. Check the header layout against the PC's real Forza Horizon packages before writing the reader.
   Add games shows the line.
2. **List with every push:** optional `Content` on `BeginUploadRequest`, stored per version (`SaveVersionContent`, migration),
   returned with the version. Old agents send nothing. Regenerate `openapi.json` and `api-types.ts`.
3. **What each machine has:** `POST /agent/games/{id}/content` after every scan, kept per machine per game
   (`MachineGameContent`), so the console shows the gap before anyone pulls.
4. **Check before a pull:** in `SyncEngine`'s pull, after the head is known and before the restore; `DlcCheck.Decide(head, here,
   policy, interactive)` is pure and unit-tested. Agent API `GET /api/games/{id}/dlc`, `POST …/dlc/choice`; agent UI card,
   dialog, Overview banner.
5. **Console:** the game page's DLC check card and the Configuration card (policy, hand-over cap, expiry) as server settings that
   agents learn on poll.
6. **Hand-over, server:** `ContentTransfer` (game, packs, from, to, status, expiry) and its parts in a temporary store beside
   the archives: Requested → Approved → Uploading → Ready → Delivered → Deleted, or Declined / Expired / Cancelled. Parts
   upload through the save upload's begin/chunk/complete pattern and download ranged; only the asking machine may fetch;
   the lease sweeper deletes expired ones. Audit entries.
7. **Hand-over, agents:** the 20 s poller lists transfers to send (approve per the setting, upload) and to fetch (download to a
   temp folder, check against the list, move into place, run the held pull). Never writes into a running game
   (`GameActivity`). Two-machine test end to end.
8. **Hold, notifications, launch:** a held pull is durable on disk and re-checked on every scan and when the DLC folder changes;
   notices through `NoticeCatalog` (needs DLC, asked, received, declined, expired) open the game screen; the launch wrapper
   never waits on DLC; a Decky line for a held pull.
9. **Help, CLI, testenv, Group H:** a help article, `dlc status` / `dlc ask`, testenv packages on Windows and missing on WSL,
   and the real Forza Horizon DLC between PC and Deck. Then further inventories: Cemu (`0005000c`), Azahar (`0004008c`), RPCS3.

### Phase 5c — final plan (approved by the maintainer 2026-10-12)

Both designs above are approved: **the profile checked at launch (F) with the profile split by game**, and **the DLC check**.
This is the build order; each numbered step is one commit on this PR (`emulator-saves-group-e`, #68), and the plugin parts are
their own PRs on `Marwanello/SaveLocker-Decky` and `Marwanello/SaveLocker-Playnite`.

**Captured before building (2026-10-12, this PC; the Deck was offline):**
- Xenia Canary under **Xenia Manager** (`D:\Game Sources\Emulators\Xenia Manager\Emulators\Xenia Canary`, portable). Its root
  `xenia-canary.config.toml` has `[Profiles] logged_profile_slot_0_xuid = "E030000033894516"`, and Xenia Manager keeps **one config
  per game** in `config/<game name>.config.toml`, which carry the same line. The launch check writes the root config and the
  game's own config when there is one.
- `FFFE07D1.gpd` is XDBF exactly as Free60 documents it: 24-byte header, 18-byte entries (namespace, id, offset, length), 8-byte
  free entries, data after both tables; the last free entry is `(end, ~end)`. Title rows are namespace 4 keyed by title ID
  (unlocked at +8, gamerscore earned at +16); settings are namespace 3, 24 bytes, an Int32 value at +16. The three totals —
  `0x10040006` gamerscore, `0x10040012` games played, `0x10040013` achievements — equal the sums over the rows, so a merge
  recounts them.
- `Account` decrypts with Xenia's retail key `0x19` (HMAC-SHA1 of the first 16 bytes → RC4 key); the hash checks out and the
  gamertag is UTF-16BE at +16 of the decrypted data.
- A DLC header in `Headers/00000002/<package>.header` is 332 bytes: device, content type (2 = DLC), display name UTF-16BE
  (`Pre-Order Car Pack`), the package file name, the title ID. Forza Horizon's ten packages are 48 MB–349 MB, 1.0 GB in all.

**Launch questions (shared by the profile and the DLC check).** `LaunchDecision` gains `Ask`, and `LaunchGateResult` gains a
`Question` (id, kind, title, text, choices, the default choice, a timeout). **The agent applies the default before it answers
`Ask`,** so a client that does not know `Ask` (today's Decky and Playnite plugins fail open on anything but `Blocked`) still
launches correctly. A client that shows the question posts a different choice to `POST /api/games/{id}/launch-answer`, which the
agent applies before the launch continues. The in-process Linux wrapper cannot ask, so for it `Ask` is the default.

**Steps**
1. **Xenia file formats** — `Xdbf` (read, write, the row merge and recount), `XeniaAccount` (gamertag), `XeniaContentHeader`
   (DLC and title-update headers), `XeniaConfig` (find the config(s), read and write `logged_profile_slot_0_xuid` line by line).
   Unit tests on built fixtures; a local-only test reads the captured real files when `SAVELOCKER_XENIA_CAPTURE` points at them
   (they hold a real gamertag, so they are not committed).
2. **Launch questions** — the contract above in the agent; the answer route; a question store with expiry.
3. **The profile at launch** — `XeniaLaunchProfile.Decide` in `PrepareLaunchAsync`: the game's profile is the XUID its save folder
   is under; policy Ask (default) / Switch / Leave, a machine setting; pre-profile layouts skip it; never while Xenia runs.
4. **Plugins** — Decky: a modal for `Ask` during a held launch, answer through `main.py`, timeout keeps the default. Playnite: a
   real dialog in `OnGameStarting`. Both keep failing open.
5. **The game's part of the profile** — the `profile` folder (`<TID>.gpd`) and its row exported/merged around push and pull.
6. **The profile core, copied once** — restore-if-missing.
7. **The game's profile on the server and aligning a machine** — `Game.EmulatorProfile`; Add games' “Use <profile> here”.
8. **DLC: the list with every push** and **what each machine has** (server tables, migration, API).
9. **DLC: the check before a pull** — agent UI card and dialog; `Ask` in a launch uses the launch question.
10. **Console** — the DLC check card, the profile-at-launch table, the Configuration card.
11. **DLC hand-over, server** — `ContentTransfer`, temporary parts, sweeper, cap, audit.
12. **DLC hand-over, agents** — send and fetch through the poller.
13. **Hold and notifications** — durable held pulls, the folder watch, notices.
14. **Help, CLI, testenv, Group H** — Forza Horizon between PC (Xenia Manager, Playnite) and Deck (Game Mode).

**Verify (when built):** a profile pulled to a machine without it signs in and loads a game's save there; a machine with its own
profile keeps it; a DLC entry pushed once and pulled; a save game's push never carries DLC; the two-machine tests per entry.

### Group F — as built (2026-10-13, branch `emulator-saves-group-f`)

Decided with the maintainer at the start of the group:
- **One game per Switch title across every Switch emulator** — an exception to *Saves from different emulators are
  never one game* (Decisions.md), because a Switch save is the title's own folder of files whichever emulator wrote it.
- **D2 built as planned:** `Games.EmulatorKey` (`switch:<title ID>`, filtered unique index, migration
  `AddGameEmulatorKey`), on `GameDto` and `CreateGameRequest`; the server and the agent match it before the name.
- **A ROM with no save yet is listed** ("No save here yet") under a Yuzu fork's current user.
- **Captures from the PC and the Deck, Citron from research** (EmuDeck's `emuDeckCitron.sh` on the Deck; no Citron
  install on either machine).
- **Several emulators on one machine** (the maintainer plays one game in two for performance): the maintainer asked
  for a way through each agent's Emulator settings page, and picked this design — one copy syncs, every other Switch
  emulator here can take the synced save once, be **kept in sync** (the one played last wins; nothing copied while a
  Switch emulator runs; the overwritten side zipped first, five kept; **both changed → ask**), or become the synced copy.
- **The user binding, researched:** a Switch save's files don't name their user. Yuzu forks file a save under the
  profile's folder, Ryujinx under its index entry (`ExtraData0` holds the attribute too, which SaveLocker never copies),
  so no Xenia-style profile copy is needed; a copy goes under the target emulator's current user.

What the captures showed that the research did not say:
- **The PC** (EmuDeck for Windows): Eden portable with `user\nand\user\save` a junction to `D:\Emulation\saves\eden\saves`
  (the real folder) and a real *Metroid Prime 4* `save.bin` under `C4D7AB8B…`; Ryujinx portable whose real `imkvdb.arc`
  (572 bytes, four entries) maps MP4's account save to `0000000000000003` and Animal Crossing's device and BCAT saves —
  all three folders empty (launched, never saved). Ryujinx's `games/<tid>/gui/metadata.json` names the title.
- **The Deck:** Yuzu (AppImage, `~/.local/share/yuzu/nand`, not EmuDeck's storage) and Ryujinx (EmuDeck links into
  `~/.config/Ryujinx`; `games` linked to the SD card's `storage/ryujinx/games`), **Undertale saved in both with different
  contents** — the same-machine case. Two Ryujinx users; `Profiles.json`'s `last_opened` is the current one. ROMs in
  `roms/switch/{yuzu,ryujinx}` subfolders.
- **`profiles.dat`'s UUID is stored byte-reversed** relative to the save folder's name (`ca0ef0a0…c4` →
  `C4D7AB8B…0ECA`); both machines' yuzu builds leave a mangled folder (`8D745B09…00000000`) holding only
  `.yuzu_save_size` beside the real one — ignored. A device save (Animal Crossing) is under the all-zero user in a
  fork, type `Device` with user 0 in Ryujinx's index.
- **The index layout** (`IMKV`, count, then `IMEN` + 0x40 key + 0x40 value): key = program ID, user (high, low),
  system save ID, type; value = the save ID first.
- **Ryujinx's two copies:** `0` committed, `1` working. LibHac's source is no longer on GitHub, so a pulled save is
  written to `0` and `1` is then made the same — whether Ryujinx would rebuild `1` itself is a Group H check.

The readers (`SwitchEmulators`, `RyujinxSaveIndex`; one game per title, the copy written last):

| Emulator | A game is | Scope | Name | States |
|---|---|---|---|---|
| Eden, Citron, Yuzu, Suyu, Sudachi | `nand/user/save/0000000000000000/<user>/<title ID>` (the current user's, else the newest); device save beside it as folder `device` | none (the folder is the game's) | Ryujinx's `metadata.json`, else the ROM's `[title ID]` file name, else the bundled eShop table, else `Switch <title ID>` | none |
| Ryujinx | `bis/user/save/<save ID>/0` from `imkvdb.arc` (the last-opened user's account save, else the newest; else the device save) | none | the same | none |

Same machine, several emulators — `SwitchCopies` (agent only; routes `GET /api/games/{id}/switch-copies`, `POST
/api/games/{id}/switch-copies/{emulator}` with `copy-here`, `keep-in-sync`, `stop-keeping`, `make-synced`, `keep-copy`,
`keep-synced`). Kept-in-sync copies are reconciled before a push and a pull (a copy played since is taken in, so a pull
still refuses to overwrite it), after them, and on the poller's tick; a copy taken into the synced folder is pushed by
that folder's own watcher. State in `TrackedGame.EmulatorCopies` (`LastHash` = what both held when last the same).

Known limits:
- Ryujinx has no save slot for a title until the game runs once there: it is never listed as "No save yet", a copy
  can't go to it, and SaveLocker never writes `imkvdb.arc`.
- A copy between emulators runs only while the agent is: a game played in the second emulator with the agent stopped
  is taken in on its next start (or flagged *Both changed* if the synced copy changed meanwhile).
- BCAT and cache saves are not synced. A title's account saves under two users of one emulator: the current user's wins.
- Two machines' names can differ (Ryujinx's metadata on one, a ROM name on the other): the first to add it names it.

Follow-ups, 2026-10-14 (maintainer, from the testenv pass: a game added from WSL first stayed *Switch 0100F2C0115B6000*
for good, Switch rows had no **Change**, and a title ID is no name anyone recognises):
- **A bundled title table** — `src/Shared/SwitchTitles` (title ID → eShop name and icon), from
  [blawar/titledb](https://github.com/blawar/titledb) (MIT; `US.en`, then `GB.en`, then `JP.ja` for games sold only in
  Japan; base games only, no demos), 23,982 titles, 1.5 MB gzipped (mostly the icon hashes), rebuilt by
  `Update-SwitchTitles.ps1`. GameTDB's Switch list was ruled out: it is keyed by product code (`AXN7A`), not title ID.
  The name order is the maintainer's: Ryujinx's record, the ROM's name, the table, then `Switch <title ID>`.
- **The server renames a placeholder:** a game joined by its title key whose name is still `Switch <title ID>` takes
  the first real name a machine brings (unless another game has it; audit `game.rename`), the agent sends its own name
  in that case (`Enroller.KeyedName`), and every agent takes a renamed game's name on its next tick.
- **Change on Switch rows** (`EnrollLinks.SwitchOptions`): automatic joins the game keyed to the title (alone, so the
  picker says every save of the game joins it), else a new game; by hand, a same-titled game with no title recorded
  and a whole folder takes the title; one of another title or with scoped files is listed greyed out.
- **The eShop icon is the game's icon** (`ArtService.EshopIconAsync`): from the table's hash on
  `img-eshop.cdn.nintendo.net` (added to the image allowlist; `Art:EshopImageBase` for tests), on enroll and on refresh,
  with or without a SteamGridDB key; SteamGridDB still supplies cover, hero and logo.
- Tests: `SwitchNamesAndLinksTests` 4, `GroupFSyncTests` +3 against real servers (the WSL-first rename, a game picked by
  hand taking the title, the eShop icon from a stand-in host); both server changes mutation-checked. Unit **558**.

Follow-up, 2026-10-15 (maintainer, on the Deck: "I chose the cloud save and nothing changed" — it went into Yuzu, the
emulator Animal Crossing synced through there, while the maintainer plays in Eden, which isn't on the Deck; nothing on
screen said which). Three directions were mocked up (`which-emulator`); the maintainer chose **A, the emulator as one
visible choice**, built on the Emulator settings page (`SwitchPlaysIn`):
- A status line compares the synced emulator's save with the cloud's (`GET vs-cloud`, `SaveVsCloud.Of`) and names it in
  every button: **Put the cloud save in Yuzu** (`take-cloud`: `SyncEngine.TakeSideAsync`, the save zipped first with the
  copies' backups, then a forced pull) and **Keep Yuzu's and upload it** (`upload`, a forced push). Both are refused when
  the page named an emulator that isn't the one that syncs any more.
- **On this device, you play it in**: one radio card per Switch emulator here, plus any the game's other machines report
  (`GameStateDto.Sources`) that isn't installed. Picking one with a save asks whether to keep its save or put the cloud's
  in (`make-synced`, then `take-cloud`); one with no save offers only the cloud's; Ryujinx with no slot and an emulator
  not installed explain the steps (EmuDeck → Manage Emulators) and offer **Tell me when … is ready**
  (`EmulatorCopy.TellWhenReady`; the poller's `SwitchCopies.NowReady` raises one `NoticeCatalog.EmulatorReady`).
- The copies card lost **Make this the synced copy** (the picker does it) and was renamed *Copies in the other emulators*.
- Not built: B (the save map) and a Deck Game Mode (ImGui) screen; the agent UI page works with the controller.
- Tests: `GroupFSyncTests` +7 (the Deck case end to end against a real server: waiting for Eden, the cloud save into the
  synced emulator with its own zipped, Eden ready once, switched to, filled, then uploaded with the PC's kept as a version;
  `SaveVsCloud.Of`'s six cases). Agent tests **639**. Checked by hand against a stand-in agent (every dialog, Escape,
  1280×800); not yet on the rig or the Deck.

Follow-up, 2026-10-16 (maintainer, on the rig: the Deck's Yuzu island never reached the PC's Eden; two *Eden* cards; the
copies card still there; the controller drove the agent UI while another app had focus):
- **Folder keys meant different saves on two machines.** The Deck has only Animal Crossing's device save, so it synced
  as `main`; the PC also has a stray account copy under Eden's user, so `main` was that copy and the real island was
  `device`. The Deck's island was pulled into the stray copy and Eden kept loading the old one. Now a joining machine
  lays its folders out as the server game has them (`SwitchEmulators.AlignToGameFolders`, in `Enroller` beside Xenia's
  profile alignment): no `device` on the game → the device save is `main` here and the account copy is left alone; a
  `device` on the game and only a device save here → it is `device`, and the current user's folder
  (`ScanCandidate.SwitchAccountDir`, created empty) is `main`. A game enrolled before this has to be removed and added
  again.
- **Each install is its own choice:** rows carry `saves` and `install` (`EmuDeck`/`Standalone`); actions take `saves`.
- **Picking an emulator moves the save to it:** the save played here when the cloud has nothing newer (copied in, made
  the synced copy, uploaded), the cloud's when only it moved on, the user's pick when both changed, or the emulator's own
  on request. The copies card is gone (keep-in-sync copies already set keep working in the background).
- Eden, Citron, Yuzu and Ryujinx are always on the picker.
- The controller drives the agent UI only while its window has focus (`pad.ts`).
- Tests +3 (two installs of one emulator; a device save landing in the device save with either machine joining first).

Follow-up, 2026-10-16, later (maintainer: after the re-enroll, Animal Crossing on the PC's Eden says the save data is
damaged):
- **Cause: the game version, not the copy.** The PC's island is byte for byte the Deck's (MD5 of every file). Its
  `mainHeader.dat` says save revision 15, which NHSE's `RevisionChecker` maps to **1.6.0**; the Deck's Yuzu log boots
  `| 1.6.0` (the update is in Yuzu's NAND), the PC's Eden log boots `| 1.0.0` (only the `[v0]` NSP, nothing installed).
  A Switch game can't read a save from a newer version of itself. General to Switch games; Animal Crossing changes its
  save format with almost every update (35 revisions), so it shows the most.
- **The user ID matters to Animal Crossing.** Decrypted with NHSE's scheme (`main.dat` at `0x1E2280`, 1.6.0): an
  8-slot table of user ID (the profile UUID's bytes) + `PersonalId`, slot 0 = the Deck's Yuzu user `DBBE23FA…` → resident
  *Maro* of *Al Jazzira*. The PC's Eden user `C4D7AB8B…` is not in it, so once the version matches, the game will ask to
  relink the resident, then the Deck will. The fix is one user ID on every device (`profiles.dat` copied); documented in
  the help article, not automated yet.
- **Built: the DLC check now covers a Switch game's version.** `SwitchGameVersion` reads it per install from the fork's
  log (`Booting game: <TID> | … | 1.6.0`), Yuzu's `game_list/<TID>.pv.txt` (`Update (x)`, `[D]` = off; Eden writes none),
  or Ryujinx's `Logs` (`Application Loaded: … v1.6.0 [tid]`), newest wins. `ContentInventory.For` reports it for the
  install that holds the game's folder (never one matched by name): `Update <v>` (`<TID>800`, size 0, `version`), empty for
  the base game, null when unknown. `ContentPackageDto.Version` (optional): an update counts as present only when the
  version here is the same or newer. A pull of a newer version's save is held with a message naming the update; Switch
  updates can't be handed over (`DlcCheckDto.CanHandOver`, the agent's hand-over route refuses). The picker shows *Runs
  version x* per card, warns in the switch dialog, and the page leads with "Eden here can't open the cloud save" when the
  cloud save (`SaveVsCloudDto.HeadGameVersion`) is newer than the synced emulator's.
- Tests +5 (`SwitchGameVersionTests` 4: real log and cache lines, Ryujinx, the version compare, only the holding
  install; `GroupFSyncTests` +1: the version reaches the server and the PC's base game lacks it). Agent tests **647**.
  Picker clicked through against the stand-in agent (Yuzu 1.6.0, Eden 1.0.0). `api-types.ts` regenerated from a running
  Linux agent; `openapi.json` from a scratch server (also adds the existing `/join.js` it had missed).

Follow-up, 2026-10-16, last (maintainer: build "one Switch user on every device" into SaveLocker; always ask which user;
every Yuzu-based install changes; in this PR):
- **Server:** the heartbeat carries `SwitchUsers` (each Yuzu-based install's users, kept on `AgentHealth.SwitchUsersJson`,
  migration `AddSwitchUsers`); the chosen user is the setting `Switch:SharedUser`, set and cleared by any machine
  (`/api/agent/switch/shared-user`, audited) and handed out as `AgentHeartbeatResponse.SwitchSharedUser`.
- **Agent:** `SwitchUserSharing` — per install: refuse while a Switch emulator runs, refuse a title saved under both users
  until the user picks, zip `profiles.dat` + the old user's saves (+ a replaced one) to `<state>/switch-users`, tell the
  server every tracked game's new folder, move the old user's folder to the new ID's, rewrite the current user's UUID in
  `profiles.dat` (both copies; the name kept) and rename an avatar named by it, then re-point the games and record it in
  `switch-users/applied.json` for the undo. The poller adopts the shared user each tick (`ShareSwitchUserAsync`), says a
  needed choice once; "Put back this device's original user" undoes from the backup and stops adopting that ID here
  (`SwitchSharedUserDeclined`). Ryujinx is left alone (its save index holds the user too).
- **UI:** `SwitchUserCard` in Settings, and on a Switch game's Emulator settings while devices differ: every device's users,
  a pick (never preselected), a confirmation saying every step, the both-users choice per title, undo, stop sharing.
- Tests +4 (`SwitchUserSharingTests` against a real server: adopt and put back with the game re-pointed on the server and
  the backup's contents; a title under both users waits, kept from here, and the shared one's copy restored by the undo;
  an install that already has the ID as another user; the server's report, setting and heartbeat). Agent tests **651**.
  The card clicked through against the stand-in agent (pick, confirm, both-users choice, switch, put back). Not yet run on
  the rig or the Deck.

Fixes from the Deck, 2026-10-18:
- One Eden listed twice: EmuDeck's link ends in `/`, Eden's `nand_directory` doesn't; `EmuDeckRoots.RealPath` trims it.
- Eden added on the Deck started a new island: the game synced Yuzu's device save, and "copy here" / "make synced" put it
  under Eden's user, where Eden never looks (its log: the device save is opened). A game whose one folder is a device save
  now goes to the other emulator's device save (`SwitchCopies.SlotFor`).
- Then for every route (maintainer: "make sure this doesn't happen with any game on any device"): the server records a
  device-save game as `EmulatorProfile = "switch:device"` (`EmulatorProfiles.SwitchDevice`, set by the first machine that
  enrolls it from a device save only, or — for one enrolled before — by a machine syncing it from a fork's device save,
  through the join). A machine joining such a game takes its fork's device save even with no save or only one under a
  user (`AlignToGameFolders`); copies follow it (`SlotFor`); one already under a user moves itself to the device save on
  the poller's tick, the server told first, both sides zipped, the old folder removed (`MoveToDeviceSaveAsync`). Ryujinx
  is left out of the move: its folders come from its index. Agent tests **656**.

### Group G — as built (2026-10-18, branch `emulator-saves-group-.g-i-j`)

Decided with the maintainer at the start (2026-10-18):
- **Phase 17, same emulator only.** A save links to a game of another emulator never; two dumps of one ROM in one emulator do.
- **A file-by-file list**, not a prefix rule: each machine keeps one pair per file (`server name` ⇄ `its own name`) per save
  folder. **New files join it by themselves**: a file whose name starts with a listed file's name without its extension and a
  dot (`… (Rev 1).state3`) takes the same swap and is added to the list on the next push or pull.
- **Linked from the agent only** (Add games → **Change** → *Different file names*), undone by stopping tracking it there; the
  console shows each machine's names, read-only.
- **A clash refuses the link** up front: a file of the server's name already here, or the game already synced here.
- **Merging two existing server games: built now, console only**, with a preview and the merged game's name typed.
- **Phase 20: launcher + ROM path** is enough; SRM's `addedItemsV2.json` stands in for a launcher SaveLocker does not know. **A ROM
  with no save** is listed as *No save here yet* when the reader can say where its save will be, else hidden.

**Phase 17 — each machine's own file names.**
- `Shared/FileNames`: `FileNameDto(Key, Server, Local)` and the swap. `ToServer` returns null for a file here that already has a
  listed server name without being listed itself (another dump's save under the server's name: never this game's).
  `Derive` lists the pairs a stem swap reaches; `Validate` bounds names (relative, no `..`/drive/backslash, 256 pairs).
- `SaveRoot.Names`; `SaveArchive` swaps on the way in (`EnumerateItems`: the scope, the archive name, the hash and the manifest are
  the server's), on a delta subset, and on the way out (restore writes local names; the delete pass reads local names back to the
  server's before the scope test). A shadow folder keeps the server's names.
- Agent: `TrackedGame.OwnFileNames`; `OwnFileNames.Grow` before every push and after every pull, reported to the server
  (`PUT /api/agent/games/{id}/file-names`, stored on the primary `MachineSavePath` row, `FileNames` JSON). `EnrollLinks.RenamePairs`
  pairs the save's scope with the game's by extension; `LinkKind.Join` + choice `rename`. `Enroller.TrackedFor` finds the linked
  game by the save's scope under the server's names, so Add games shows it added.
- **The first sync asks**: a pull refuses (this machine's save was never pushed), the push is a conflict; the user keeps one.
- Console: each machine's row lists its own names; the agent game page lists this device's with how to unlink.
- Merge (`SyncService.MergePreviewAsync` / `MergeGamesAsync`, `GET /api/games/{id}/merge-preview?into=`, `POST
  /api/games/{id}/merge`): refused while the game is leased or has an open conflict. The merged game's versions move to the kept one,
  **protected and read-only** (`SaveVersion.MergedFrom`; never made Latest — `SetHeadAsync` refuses), archives untouched. A machine
  that synced only it **moves** when the two keep the same folders (same scopes and extra keys) — its path rows and source move —
  else it **re-adds** the other from Add games. `Game.MergedGameIds` tells agents: the poller re-points its copy (GameId, name),
  clearing a "Keep it as its own game" opt-out (`SetTracked`), or says what to do. Console: **Merge** card on the game page.
- Migration `AddFileNamesAndMerge` (`MachineSavePaths.FileNames`, `SaveVersions.MergedFrom`, `Games.MergedGameIds`).
- Tests: `FileNamesTests` 11; `GroupGSyncTests` 4 against a real server (link + first-sync conflict + new slot + console names;
  restore of an old version on the renamed side; refusals; the merge and the poller's move). Mutation-checked: with the swap off in
  `SaveArchive`, three tests fail.

**Phase 20 — Steam ROM Manager shortcuts.** `SrmShortcuts`: `Recognize` reads the command from `Exe` *and* the launch options
(SRM writes both shapes, quotes inside quotes, Xenia's `Z:` path), finds the emulator by the launcher's or Flatpak ID's name
(never by the ROM's own name) and the ROM by a path in a `roms` folder or with a ROM extension. `Apply` drops the shortcut's row and
puts its app ID(s) on the emulator row for its ROM (`ShortcutAppIds`, the first also `SteamAppId`): matched by Switch title ID,
3DS program ID (NCCH), GameCube/Wii game ID (`[R3OE01]` or the disc header: `.iso` 0, `.wbfs` 0x200, `.rvz`/`.wia` 0x58), the save's
ROM name, else the cleaned name. `NoSaveYet` makes RetroArch (not when sorting saves by core), melonDS, Azahar, Dolphin and
PrimeHack rows. `TrackedGame.ShortcutAppIds` + `LaunchesUnder`: the launch wrapper and the launch-option rows know every shortcut of a
game. `SrmShortcuts.Backfill` records them on a game added before. A shortcut game tracked beside its emulator game:
`TrackedGameDto.SameGameAs` → the agent's *Same game as …* banner with **Stop tracking this copy**; `doctor` says so too.
Captured: the Deck's whole `shortcuts.vdf` (gated test, `SAVELOCKER_SRM_CAPTURE`). Not built: other front ends (BoilR, Heroic's
emulator entries, RetroDECK); the Decky plugin still reads one app ID per game (`TrackedGameDto.SteamAppId`).

### Group I — as built (2026-10-18)

Phase 19 for every reader the plan listed, on PPSSPP's rule. RetroArch and melonDS through `RomSaveRules.StateRom`
(`<rom>.state`, `.stateN`, `.state.auto`; `<rom>.ml1`–`.ml8`), RetroArch's per-core state folders included (the save goes in the
saves folder's same-named core folder when there is one). PCSX2: `<serial> (<CRC>).NN.p2s` / `.resume.p2s`, on the setup's first
folder card, prefix `BA`/`BE`/`BI` + serial (`SavePrefix`); none with only a shared file card. DuckStation: `<serial>_N.sav`, the
card declared under the bundled table's title (DuckStation names a card after its own database's title, which nothing here can
read; when it differs, the first save is its own row and **Change** links it), else the serial. Dolphin/PrimeHack: `<ID>.sNN`,
GameCube (`G`/`D`/`P`/`U`) in Slot A's region folder, Wii (`R`/`S`) in `Wii/title` (`DolphinSaves.ForGameId`). Azahar:
`<program ID>.NN.cst` (and recordings) in the setup's title folder. Testenv: one states-only game per emulator.

### Group J — as built (2026-10-18)

`web/scripts/build-platform-icons.mjs` (`npm --prefix web run icons:build`, `icons:check`): downloads Arcticons (`icons/white`) and
RetroArch's Monochrome glyphs at their current commits (recorded in the outputs), inlines class styles, makes every colour
`currentColor`, namespaces ids, fits a glyph's viewBox to what it draws (rendered with resvg — MAME and several glyphs are off their
canvas), and writes `platformIcons.generated.ts` to both UIs and `src/Agent.Linux/Ui/EmulatorIcons.g.cs` (the line icons as stroke
paths, shapes and transforms flattened). `--check` verifies every mapped icon exists and draws. Credits: `NOTICE-icons.md`.
Decided here (no answer needed from the maintainer): Cemu, RPCS3 and ScummVM use their console's glyph (Wii U, PS3, RetroArch's
ScummVM) until their own logos' terms are checked; PrimeHack uses Dolphin's icon. Placed: console platform headings and emulator
sub-headings, covers with no art, the release and source chips, each machine's row; agent Add games emulator and console chips and
rows, covers (a console badge), the Switch emulator picker; Game Mode's Add games rows (`Icons.Emulator`). `SvgPath` now draws
S/Q/T and elliptical, rotated arcs. Not done: the Switch eShop icon as a cover (optional in the plan).

### The other emulators — researched 2026-10-07

The maintainer asked to add **Cemu, Azahar, Model 2, PPSSPP and the Switch family (Yuzu, Ryujinx and Citron
alongside Eden)**, plus **bonus** Supermodel, melonDS, ScummVM, shadPS4 and Vita3K, and to fold every remaining
phase into as few groups as keep each PR a reasonable size, alike work together (`implementation-grouping.md`).
Read 2026-10-07, not recalled:
- **EmuDeck's own setup scripts**, which say exactly where each emulator's saves go and what
  `Emulation/saves/<name>/` links to: `dragoonDorise/EmuDeck` → `functions/EmuScripts/emuDeck<Name>.sh`
  (SteamOS, at `dddb302`, 2026-10-05) and `EmuDeck/emudeck-we` → `functions/EmuScripts/emuDeck<Name>.ps1`
  (Windows). **Every emulator in this plan has a script in both**, PrimeHack, RPCS3, Xenia and Eden included.
  EmuDeck for Windows keeps its emulators in `%APPDATA%\EmuDeck\Emulators\` (`vars.ps1`), not the
  `%USERPROFILE%\emudeck\EmulationStation-DE\Emulators` Phase 1 reads for RetroArch — check which one a current
  install uses during the hardware pass.
- **Each emulator's source**, for file names: Azahar `src/core/savestate.cpp` and `src/common/file_util.cpp`;
  PPSSPP `Core/SaveState.cpp`; melonDS `src/frontend/qt_sdl/EmuInstance.cpp`; Supermodel `Src/OSD/SDL/Main.cpp`
  and `Src/OSD/{Unix,Windows}/FileSystemPath.cpp`; shadPS4 `src/common/path_util.cpp` and
  `src/core/libraries/save_data/save_instance.cpp`; Cemu `src/Cafe/TitleList/TitleList.cpp`. Model 2 Emulator is
  closed source, and Ryujinx, Eden and Citron are hosted off GitHub: what is still unconfirmed for them is marked
  **capture** in their phases — confirm it from a real install before wiring it.

**What the research changes in the design.** D1 and D2 change behaviour a maintainer decision rests on, so
confirm each at the start of the group that first needs it.
- **D1 — an emulator game is found by its folders, not its name** (Group B). `ServerNameFor` only looks at
  server games under the candidate's own names, so a name taken from something one machine has and another
  lacks — `gamelist.xml`, Cemu's title cache, Ryujinx's metadata, ScummVM's description — would split one game
  in two. That is why Phase 3 was parked. Proposed: before any name, an emulator candidate joins the server game
  whose folder keys and include scopes are exactly its own. A scope names the save file or the title ID, so no
  two games share one; the name list then only matters to the machine that enrolls first. Unscoped (PC)
  candidates keep matching by name.
- **D2 — Switch needs a title key on the server** (Group F). Ryujinx keeps each save in a numbered folder
  (`bis/user/save/<id>/`) handed out per install, so neither the name nor the scope is the same on two machines.
  Proposed: a nullable `Games.EmulatorKey` (`switch:0100F2C0115B6000`, migration + `GameDto`/`CreateGameRequest`
  field, additive), sent at enrollment and matched before anything else. Every Switch emulator then points the
  game at its per-title folder, the archive holds the same files whichever emulator wrote it, and **a save syncs
  Eden ↔ Ryujinx**. Without it, Ryujinx stays manual-only and the Yuzu forks sync among themselves by scope (D1).
- **D3 — save states follow *Save states — decided***: where an emulator writes them, they are the game's
  `states` folder, always synced and declared even before the first one exists — PPSSPP, Azahar, melonDS,
  Supermodel, Model 2 if the capture finds any, and Group C's PCSX2, Dolphin, PrimeHack and DuckStation (their
  state files are per game even when the memory card is not). Cemu, the Switch emulators, RPCS3, Vita3K, shadPS4, Xenia and
  ScummVM have no separate states to sync.
- **D4 — one reader per save *shape*, the emulators as tables.** Every emulator here has one of three shapes:
  1. **a file named after the ROM, in a folder every game shares** — RetroArch's shape: melonDS, Supermodel,
     Model 2, ScummVM (and DuckStation's per-game cards). Generalise `RetroArchSaves`: scope `<rom>.<ext>`.
  2. **a folder per title ID or serial, in a folder every game shares**: PPSSPP, RPCS3, Vita3K, shadPS4, Cemu,
     Azahar, Xenia, Dolphin's Wii saves and the Yuzu forks. One new helper: scope `<id>/**`, or `<id>*/**` where
     a game owns several folders (PSP, PS3).
  3. **an opaque folder plus an index**: Ryujinx alone.

  The groups follow the shapes.

**Superseded** by the above or by the as-built sections: "save states are out of scope everywhere" (Background,
*Deferred* — see *Save states — decided* and D3); "no server changes anywhere" (Phase 1 added scopes, 7b sources,
D2 a key); "Ryujinx/Yuzu explicitly out of scope" (Phase 6 — the maintainer asked for both); "PrimeHack, RPCS3,
Eden and Xenia not confirmed in EmuDeck" (each has a script; the paths are in their phases); Phase 1's playlist
reader (never built — names come from the save file).

---

## Motivation

This is `Backlog.md` → **"Emulator saves"**, scoped in full and widened at the maintainer's request
to also cover Nintendo Switch (Eden — Ryujinx/Yuzu are dead upstream, see Phase 6), RPCS3 (PS3),
PrimeHack (a Dolphin fork), and Xenia (Xbox 360). Ludusavi (the manifest SaveLocker's whole existing
detection model is built on) has **zero emulator coverage**, so none of this is visible to discovery
today, despite being — per the maintainer's own framing — "a large share of what people actually
play" on a Deck. Keep it simple: reuse every existing primitive this codebase already has rather
than inventing new mechanisms, and cut real scope (save states, Ryujinx) where the cost doesn't
justify it.

---

## Background — what's already confirmed, so it isn't re-derived mid-implementation

**Existing architecture this task reuses, not replaces:**
- `IGameScanner.ScanAsync()` (`src/Agent.Core/Platform.cs`) is the discovery contract both hosts
  implement. Every new emulator source below is wired in as one more independent, failure-isolated
  source inside `GameScanner.ScanAsync` (Windows) / `LinuxGameScanner.ScanAsync` (Linux) — same rule
  as WA-11 ("discovery is per-source best-effort; one bad source cannot fail the scan").
- `ScanCandidate` (`src/Agent.Core/ScanCandidate.cs`) is a record that has grown additively before
  (`PrefixPath`, `Store`, `SuggestedProcessName`) — this task adds three more fields the same way
  (§ScanCandidate additions, below).
- **Heroic's integration (`HeroicRoots.cs` + `HeroicLibrary.cs`) is the direct structural precedent**
  for "a third-party library format, read by one small static reader per source, each independently
  fault-isolated." Every new emulator reader in this task follows that shape, not a shared
  polymorphic interface — `HeroicLibrary` already proves one parser per source degrades gracefully
  (a malformed file yields zero games from *that* source, never a failed scan) where one abstraction
  covering RetroArch's playlists, PCSX2's directory walk, and RPCS3's ID-keyed folders would not.
- `SteamShortcuts.MoonDeckAppId` is the precedent for "a shortcut's `Exe` launches a wrapper, not
  the real game — recover identity from `LaunchOptions`." This is structurally the same problem as a
  Steam ROM Manager shortcut (`Exe` = the emulator, not the ROM) — deliberately **not** built in this
  task (see Phase 7's deferred item) because SRM's actual argument format hasn't been captured from
  a real install yet, and — per the EmuDeck research below — it turns out to only matter for a
  subset of installs anyway.
- `SavePathGuard` (hard floor) / `SaveDirSanity` (heuristic tier, overridable) is the existing
  two-tier validation this task's one new safety check (the shared-memory-card warning, Phase 2)
  plugs into — no new validation mechanism is introduced.
- **`Game.Platform` is deliberately NOT added.** A RetroArch `.srm`, a PS2 memory-card block, or a
  PS3/Xbox 360/Switch title-ID save folder all encode the *emulated console's* native save format —
  a spec the emulator itself must reproduce byte-identically on every host OS. This is unlike the
  *other* platform-isolation backlog item (native-Linux game builds), whose save format is an
  unversioned per-developer choice with no cross-platform contract. Emulator saves need no
  OS-isolation field and are the first source in this codebase that can sync Deck↔Windows directly.
  <br>The real footgun is **cross-core/cross-version, not cross-OS**: a save *state* is tied to the
  exact core/build that wrote it, unlike a save *file* (SRAM/memory card/title-ID data), which
  follows the stable hardware format. **Every phase in this task syncs save files only — save states
  are out of scope everywhere**, left as a separate future item once that risk gets its own design
  pass.
- **No server, `Entities.cs`, `GameDto`, or migration changes anywhere in this task.** "A game is
  defined once on the server" already covers one ROM = one `Game`, exactly like one Heroic title.

**EmuDeck's real folder layout, confirmed by reading its own documentation
(`emudeck.github.io`, `manual.emudeck.com`) directly rather than trusting a guess:**
- The `Emulation/{bios,hdpacks,roms,saves,storage,tools}/<system>/` layout is **confirmed identical
  between SteamOS and EmuDeck for Windows** — the docs state this explicitly. This is why EmuDeck
  (not RetroBat) was chosen as the Windows target: the `Emulation/roms/<system>/` and
  `Emulation/saves/<emulator>/` readers this task builds are shared, path-for-path, across both
  scanners — only OS-specific root-finding differs.
- **`Emulation/saves/<emulator>/` is a stable, documented per-emulator save root** —
  confirmed by name for RetroArch (`Emulation/saves/retroarch/saves`), PCSX2
  (`Emulation/saves/pcsx2/saves`), Dolphin (`Emulation/saves/dolphin/Wii` and `/GC`), DuckStation
  (`Emulation/saves/duckstation/saves`), Yuzu (`Emulation/saves/yuzu/`), and Cemu
  (`Emulation/saves/Cemu/saves/`, not in scope here but shows the convention generalizes). This means
  **no need to parse each emulator's own config file just to find the save root** when an EmuDeck
  install is present — reading `retroarch.cfg`/`PCSX2.ini`/etc. is only needed as the
  standalone-install fallback (no EmuDeck root found), or — for PCSX2/Dolphin/DuckStation — to read
  the per-game-memory-card *setting*, which is a mode, not a location.
- **NOT confirmed in the documentation read**: PrimeHack, RPCS3, Eden, and Xenia did not appear by
  name in the save-management page. Each phase below that touches one of these keeps its own
  "capture a real install before wiring the fast path" step for exactly this reason — do not assume
  the `Emulation/saves/<name>/` convention extends to them without checking.
- **`Emulation/saves/` entries are typically symlinks**, and EmuDeck's own docs warn that backing up
  the symlink itself (not its target) loses the data. `SuggestedSaveDir` must store the *resolved*
  real path, not the symlink path — the same "canonicalize, don't trust the stored string" instinct
  WA-02 already established for `SavePathGuard`.
- **EmuDeck's own "Level of integration" choice matters for scope**: **Low integration** makes
  EmulationStation DE the launcher, added to Steam as a *single* non-Steam entry (no per-ROM Steam
  shortcuts at all); **High integration** uses Steam ROM Manager to add *each ROM* as its own
  non-Steam entry. The folder-walk + native-library-file detection this task builds works under
  **both** levels (it never depends on Steam) — the deferred SRM-shortcut-identity item (Phase 7)
  only ever improves launch/exit lifecycle precision for High-integration installs specifically, it
  is never needed for detection coverage.

**The one correction already made against a wrong initial assumption** (verified, not guessed,
against `src/Shared/SaveArchive.cs:504-533`): scoping one `TrackedGame` to a single file inside a
directory shared by many ROMs (RetroArch's `savefile_directory`) **cannot** be done with
`ExcludeGlobs` negation (`["*", "!<romname>.srm"]`) — `ExcludeGlobs` only ever calls `AddExclude`
against a matcher that already includes everything; there is no negation semantics, and the proposed
globs would produce an *empty* archive, not a one-file one. The real fix is a small, genuine
addition (§ScanArchive changes, Phase 1).

---

## New `ScanCandidate` fields (built once, in Phase 1, reused by every later phase)

```csharp
// ScanSource gains:
Emulator

// ScanCandidate record gains, additive with defaults (same pattern as PrefixPath/Store before it):
string? EmulatorName = null,   // "RetroArch", "PCSX2", "Dolphin", "PrimeHack", "RPCS3", "Xenia",
                                // "Eden", … — free text, deliberately not a closed enum (Phase 6
                                // shows how fast this list actually changes)
string? EmulatorSystem = null, // "snes", "psx", "gamecube", "switch", "ps3", "xbox360", … —
                                // EmuDeck/ES-DE's own vocabulary, reused directly as the UI
                                // sub-filter key in Phase 7
string? EmulatorCore = null,   // libretro core name (RetroArch only); null for standalone emulators
```

Wire `CandidateDto` (`src/Agent.Core/AgentApiServer.cs`) gains `EmulatorName`/`EmulatorSystem` (skip
`EmulatorCore` — internal/Doctor-only, not UI-facing until there's a reason).

---

## Execution order

Group A (RetroArch) has shipped. The remaining phases are in five groups, B–F, one PR each, in this order —
`implementation-grouping.md` has the reasons and the status. Every group, per emulator: a config-root table and a
reader in the Heroic shape (failure-isolated in both scanners), fixtures and unit tests, its name in agent-ui's
`EMULATORS` (one chip each), and `GameSources` tags (console, Flatpak, EmuDeck). Per group: one two-machine test
against a real server (the `RetroArchSyncTests` shape), testenv Windows + WSL fixtures, then the hardware pass on
whatever is installed.

| Group | Phases | Shape (D4) |
|-------|--------|------------|
| **A — RetroArch** ✅ | 1, 1b, 7, 7b | file per ROM |
| **B — Saves named after the ROM** ✅ | 3 (identity by folders), 8 melonDS, 9 Model 2 + Supermodel, 10 ScummVM | file per ROM |
| **C — Memory cards** ✅ | 2 PCSX2 / Dolphin / DuckStation + shared-card warning, 4 PrimeHack, per-console row | cards, file per game, title folders |
| **D — Sony (`PARAM.SFO`)** ✅ | 11 PPSSPP, 5a RPCS3, 12 Vita3K, 13 shadPS4 | folder per serial |
| **E — Title-ID folders** ✅ | 14 Cemu, 15 Azahar, 5b Xenia | folder per title ID |
| **F — Switch** 🚧 | 6 Yuzu, Citron, Eden, Ryujinx + title key (D2) | folder per title ID + opaque index |

---

### Group A — RetroArch (Phases 1, 1b, 7, 7b — shipped)

### Phase 1 — `SaveArchive` include-glob support, then RetroArch (both OSes), save files only

The anchor phase: RetroArch has a real per-ROM library (its own `playlists/*.lpl` JSON files, one
per system, giving real titles — not filename-guessing) and native per-ROM save naming
(`<savefile_directory>/<romname>.srm`, or `.../<core>/<romname>.srm` with "Sort Saves By Core," on
by default under EmuDeck) — no per-game-folder configuration needed at all, unlike every emulator in
Phase 2. It's also the default frontend for most of what EmuDeck actually runs.

1. **`SaveArchive` addition (build and test this in isolation first — every later phase depends on
   it)**: add an opt-in `IncludeGlobs` parameter mirroring `ExcludeGlobs`'s shape, threaded through
   `EnumerateRelativeFiles`/`HashDirectory`/`CreateArchive`/`ListFiles` — when present, it replaces
   the default `AddInclude("**/*")` with the caller's own include patterns before excludes apply.
   This is what lets one `TrackedGame` be scoped to a single `<romname>.srm` inside a directory
   shared by every other ROM's saves.
2. **Config-root discovery**, EmuDeck fast path first: `Emulation/saves/retroarch/saves` directly
   (confirmed identical path on both OSes, no `retroarch.cfg` parsing needed). Standalone fallback
   (no EmuDeck root found): a new small parser for `retroarch.cfg` (flat `key = "value"` text,
   structurally simpler than `SteamTextVdf.cs`'s tokenizer but the closest existing precedent) for
   `savefile_directory` (used) and `savestate_directory` (read, unused — save states are out of
   scope). Config roots to try: Linux native (`~/.config/retroarch/`), Linux Flatpak
   (`~/.var/app/org.libretro.RetroArch/config/retroarch/`), Windows standalone
   (`%APPDATA%\RetroArch`). An empty `savefile_directory` value means "save beside the ROM," a real
   case, not an error.
3. **Symlink resolution**: `Emulation/saves/` entries are typically symlinks (confirmed via EmuDeck's
   own docs) — resolve to the real target path before storing as `SuggestedSaveDir`, never store the
   symlink path itself.
4. **Per-ROM enumeration**: read RetroArch's own `playlists/*.lpl` files for `path` (ROM file),
   `label` (real title — use this over any filename heuristic), `core_path`/`core_name`.
5. **Per-ROM save selection**: use the new `IncludeGlobs` (step 1) to scope each `TrackedGame` to
   `<romname>.srm` (and, if "Sort Saves By Core" is detected active, `<core>/<romname>.srm`) inside
   the shared save directory — the directory is real and shared, the include-glob is what makes one
   `TrackedGame` single-ROM.
6. Files: `src/Shared/SaveArchive.cs`, `src/Agent.Core/ScanCandidate.cs`, new
   `src/Agent.Core/RetroArchConfig.cs` + `RetroArchPlaylists.cs` (parallel to
   `HeroicRoots.cs`/`HeroicLibrary.cs`), wired into `src/Agent/GameScanner.cs` and
   `src/Agent.Linux/LinuxGameScanner.cs`, `src/Agent.Core/AgentApiServer.cs` (`CandidateDto` fields).

**Verify:**
- `SaveArchive`'s `IncludeGlobs` in isolation first: archive a directory with several files, confirm
  only the included pattern lands in the zip and the hash.
- Fixture-based test (a captured `retroarch.cfg` + sample `.lpl` files, parallel to `HeroicLibrary`'s
  own fixture tests) for the parser and dedupe logic.
- Real hardware pass against an actual EmuDeck (Deck) and EmuDeck-for-Windows install.

### Phase 7 — UI: Emulator filter + Game Mode mirror (shipped — as built in *Game sources and names*)

Last, after every detection phase, because the Backlog stub already frames this as the finishing
touch ("the row is built to take another entry") once real candidates exist to filter, and because
it's the natural point to surface `EmulatorSystem` as a Heroic-`STORES`-style sub-breakdown (by
console). No further data-model work needed — `EmulatorSystem` is already free-text (Phase 1), so
every value from Phases 1–6 flows through the same sub-filter mechanism Heroic's store breakdown
uses today.

1. `agent-ui/src/components/AddGamesView.tsx`: `FILTERS` gains `'emulator'` (`c.source ===
   'Emulator'`), a `STORES`-shaped sub-breakdown keyed on `emulatorSystem`.
2. `src/Agent.Linux/Ui/UiApp.cs`: `AddFilter` enum + `MatchesFilter`, mirroring the React side by
   the existing convention (nothing shares code between the two UIs today).
3. Regenerate `agent-ui/src/types.ts` from Phase 1's `CandidateDto` changes.

**Verify:** the Backlog already flags that nothing tests the Heroic store sub-chips — add real
coverage for these new chips rather than repeat that gap.

### Group B — Saves named after the ROM

RetroArch's shape again (D4 shape 1): one folder every game shares, a save file named after the ROM. Generalise
`RetroArchSaves` into a shared reader — folders, the save's extension, the states pattern, a name rule — that
RetroArch and the four emulators below are rows of. Lands D1 first, which every later group's names rely on.

### Phase 3 (revised 2026-10-07) — identity by folders (D1), then `gamelist.xml` names

1. `Enroller.ServerNameFor`: before the name list, a scoped emulator candidate takes the name of the server game
   for which `SameFolders` holds — whatever it is called. `TrackedFor` does the same against the local config.
   Unscoped candidates are unchanged.
2. Only then may a name come from something one machine has: a small reader for ES-DE's `gamelist.xml`
   (`<game><path>`/`<name>`, keyed on the ROM path). **Capture** where EmuDeck's ES-DE keeps it on each OS (ES-DE 3
   uses `~/ES-DE/gamelists/<system>/gamelist.xml`). Use it where the save's own name is not a title — arcade set
   names (Phase 9); `Chrono Trigger (USA)` already cleans to a good name.
3. Files: `src/Agent.Core/Enroller.cs`, new `src/Agent.Core/GamelistXml.cs`.

**Verify:** two machines against a real server, one naming the game from a `gamelist.xml` and one without → one
server game; mutation-check by removing the folder match (→ two games). Fixture `gamelist.xml` for the reader.

### Phase 8 — melonDS (Nintendo DS) — bonus

1. **Where:** EmuDeck sets melonDS's own `SaveFilePath`/`SavestatePath` to `Emulation/saves/melonds/saves` and
   `/states` — real folders, not links, on both OSes (`melonDS_setupSaves`). Config: the SteamOS Flatpak
   `net.kuribo64.melonDS` → `~/.var/app/net.kuribo64.melonDS/config/melonDS/melonDS.ini` (EmuDeck also keeps a
   `melonDS.toml`, which 1.0 reads); Windows `%APPDATA%\EmuDeck\Emulators\melonDS\melonDS.ini`. Standalone:
   `~/.config/melonDS/`, `%APPDATA%\melonDS\` or beside the exe — **capture** which file a 1.0 install keeps.
   An empty path means beside the ROM (`EmuInstance::getAssetPath`): then the ROM folders are the save folders,
   and the scope keeps each game to its own file.
2. **Files:** `<rom>.sav`; states `<rom>.ml1`…`.ml8` (`getSavestateName`). Scope `<rom>.sav`, states
   `<rom>.ml*`.
3. **Name:** `RomNames.CleanTitle`, as RetroArch. System `nds`.

**Verify:** fixtures for both config formats and the beside-the-ROM case.

### Phase 9 — Model 2 and Supermodel (Sega arcade) — Supermodel is bonus

Both name saves after the MAME-style ROM set (`daytona`, `scud`), not a title.

1. **Supermodel (Model 3).** NVRAM `NVRAM/<set>.nv`, written on every exit; states `Saves/<set>.st<slot>`
   (`Main.cpp`). Folders: beside the exe on Windows; on Linux `~/.supermodel/{NVRAM,Saves}` when `~/.supermodel`
   exists, else `~/.local/share/supermodel/` (`FileSystemPath.cpp`). EmuDeck on SteamOS installs the Flatpak
   `com.supermodel3.Supermodel` but keeps everything in `~/.supermodel` and links nothing (`Supermodel_setupSaves`
   is "NYI"). EmuDeck for Windows keeps it in `%APPDATA%\EmuDeck\Emulators\Supermodel\` and links its `saves`
   folder — the **states** folder, since Windows ignores case — to `Emulation\saves\supermodel\saves`; NVRAM is
   only in the emulator's folder. Name: Supermodel's own `Config/Games.xml` (`<game name="scud">` →
   `<identity><title>`; EmuDeck downloads upstream's copy), the same file everywhere. Scope `<set>.nv`, states
   `<set>.st*`. System `model3`.
2. **Model 2 Emulator (ElSemi; Windows only, Proton on SteamOS).** EmuDeck on SteamOS installs it in
   `Emulation/roms/model2/` (`emulator_multicpu.exe`, `EMULATOR.INI`), on Windows in
   `%APPDATA%\EmuDeck\Emulators\m2emulator\`; neither links its saves (`Model2_setupSaves` is "NYI"). Saves are
   in `NVDATA\` beside the exe. **Capture first:** the file names (per set?), whether save states exist and
   where, and a trap EmuDeck sets — `Model2_init` copies EmuDeck's own `configs/model2/NVDATA` files in at
   install, so a file there does not mean the game was played. Tell EmuDeck's files from played ones before
   listing a candidate. Name: no title table ships with it — `gamelist.xml` (Phase 3), or a small bundled set →
   title table (about 50 sets). System `model2`. If the capture is not ready, ship the group without Model 2.

**Verify:** a `Games.xml` fixture; NVRAM + states fixtures per OS layout; for Model 2, a fixture built from the
captured folder that includes EmuDeck's seeded files and asserts they are not candidates.

### Phase 10 — ScummVM — bonus

1. **Where:** `scummvm.ini`, `savepath=` in `[scummvm]`, which one game's own section may override. Config:
   `%APPDATA%\ScummVM\scummvm.ini`, `~/.config/scummvm/scummvm.ini`, Flatpak
   `~/.var/app/org.scummvm.ScummVM/config/scummvm/scummvm.ini`. With no `savepath`: `%APPDATA%\ScummVM\Saved games`,
   `~/.local/share/scummvm/saves`, Flatpak `…/data/scummvm/saves`. EmuDeck sets `savepath` to
   `Emulation/saves/scummvm/saves` on both OSes (and on SteamOS moves the Flatpak's folder there).
2. **Games:** one `[target]` section each; `description=` is the name, `gameid`/`engineid` say what it is. Files
   are `<target>.<NNN>` or `<target>.s<NN>` depending on the engine → scope `<target>.*` (`monkey.*` never
   matches `monkey2.s01`). **Capture** an engine or two that name files otherwise before trusting it.
3. Only targets with a save file are candidates. System `scummvm`.

**Verify:** a fixture `scummvm.ini` with a global and a per-game `savepath`, and saves from two engines.

### Group C — Memory cards

The most-played consoles after RetroArch's, and the one real data-safety risk in this task: a card shared by every
game, where restoring one game's old version restores all of them.

### Phase 2 — PCSX2 / Dolphin / DuckStation + the shared-memory-card `SaveDirSanity` warning

This is where the real data-safety risk in the whole task lives — the reason it is a group of its own,
with its warning tested before any of its readers ships.

1. **EmuDeck fast path, confirmed**: `Emulation/saves/pcsx2/saves`, `Emulation/saves/dolphin/Wii`
   and `/GC`, `Emulation/saves/duckstation/saves`. Standalone fallback reads each emulator's own
   `.ini`/config file, same shape as Phase 1's RetroArch fallback.
2. **The granularity quirk, the reason this phase exists**: each of these emulators' *default*
   memory-card mode is one shared file per card slot covering the *entire* console library —
   restoring an old version would silently restore every other game that touched that card, not
   just the one being tracked. Each has a **per-game mode** (PCSX2: memory card type "Folder" +
   "Automatically manage saves based on running game"; Dolphin/DuckStation have their own per-game
   equivalents) that EmuDeck enables by default but a hand-configured standalone install typically
   does not. **Read the relevant config key to know which mode is active before deciding how to
   enumerate saves** — this one read gates everything else for these three emulators. When per-game
   mode is not confirmed active, leave `SuggestedSaveDir = null` rather than guess (WA-08's "admit
   ignorance" precedent) — the candidate feeds the diagnostic below instead.
3. **New `SaveDirSanity.Inspect` check**: judge the directory's actual *shape* (a flat card file
   directly in a memcard root vs. a per-game-keyed subfolder), not a stored config flag — same
   philosophy as the existing Wine-prefix check. This is heuristic-tier (overridable), not a hard
   refusal, per the existing two-tier model. It reaches every save-folder confirmation surface for
   free because `Inspect` is already wired into `/api/games/{id}/folder` (both hosts' folder
   pickers) and Linux `Doctor.cs`. Message style matches the existing ones, e.g.: *"'memcards' looks
   like a SHARED PS2 memory card, not a per-game save. Pulling an older version here would restore
   EVERY game that used this card. Enable per-game memory cards in PCSX2 (Storage → Folder +
   Automatically manage saves) and re-map to the per-game folder it creates."*
4. **Per-ROM enumeration fallback** (no library-manifest analog to RetroArch's playlists exists
   here): filtered walk of `Emulation/roms/<system>/` by known extension per system, with a
   filename-cleanup helper — `RomNames` (Phase 1) already strips `(USA)`/`(Rev 1)`-style tags; reuse it.
5. Files: `src/Agent.Core/SaveDirSanity.cs` (new check), new `Pcsx2Config.cs`/`DolphinConfig.cs`/
   `DuckStationConfig.cs`, wired into both scanners.

**Verify:** fixture tests for each config reader (shared vs. per-game mode detection) and for the
`SaveDirSanity` check against synthetic shared-file and per-game-folder layouts; real-hardware pass
on whichever emulator(s) are actually available.

### Phase 4 — PrimeHack (reuses Phase 2's Dolphin reader against a second config root)

The cheapest addition in this task. PrimeHack is a Dolphin fork (Metroid Prime Trilogy mouselook)
with its **own separate config/data root** (confirmed: Flatpak app id `io.github.shiiion.primehack`
on Linux, distinct from mainline Dolphin) but otherwise inherits Dolphin's exact save/memory-card
mechanics unmodified — same quirk, same `.ini` key, same Phase 2 `SaveDirSanity` warning.

1. `DolphinConfig.cs` (Phase 2) gains a second config-root candidate list — no new reader needed.
2. Tag candidates `EmulatorName = "PrimeHack"` (not `"Dolphin"`) so the two are never confused in
   Phase 7's UI sub-filter.
3. **EmuDeck, confirmed 2026-10-07 from its scripts**: `Emulation/saves/primehack/{GC,Wii}` link to
   `~/.var/app/io.github.shiiion.primehack/data/dolphin-emu/{GC,Wii}` on SteamOS (config
   `…/config/dolphin-emu/Dolphin.ini`) and to `%APPDATA%\EmuDeck\Emulators\primehack\User\{GC,Wii}` on Windows.
   States: `Emulation/saves/primehack/StateSaves` on SteamOS but `…/primehack/states` on Windows — the same
   folder under two names, so read the link targets, not the names.

**Verify:** point Phase 2's existing fixture tests at a captured PrimeHack config-root sample.

### Also in Group C — the per-console row Phase 7 left out

Agent UI: a *Console* chip row keyed on `EmulatorSystem`, under the *Emulator* row, once PS1, PS2, GameCube and
Wii join RetroArch's consoles. Game Mode keeps its one *Emulators* pill. Add the test coverage Phase 7's Verify
asks for.

### Group D — Sony (`PARAM.SFO`)

All four keep saves in a folder per game ID, and every save — or the installed game — carries a `PARAM.SFO`, the
one metadata format the PSP, PS3, Vita and PS4 share (`\0PSF` header, a key table, a data table; `TITLE`,
`TITLE_ID`, `MAINTITLE`). One new pure `ParamSfo.cs` (fixture-tested) names all four, and where the SFO is inside
the save the name is the same on every machine by construction. Builds D4's shape-2 helper, which Group E reuses.

### Phase 11 — PPSSPP (PSP)

1. **Where:** EmuDeck links `Emulation/saves/ppsspp/saves` → `PSP/SAVEDATA` and `/states` → `PSP/PPSSPP_STATE`,
   under `~/.var/app/org.ppsspp.PPSSPP/config/ppsspp/` (SteamOS Flatpak) and
   `%APPDATA%\EmuDeck\Emulators\PPSSPP\memstick\` (Windows, portable). Standalone: `~/.config/ppsspp/PSP/`, the
   Flatpak's folder above, or on Windows `Documents\PPSSPP\PSP` for an installed copy and `memstick\PSP` beside
   the exe for a portable one — **capture** the rule (`installed.txt`, the memory-stick setting).
2. **Saves:** `SAVEDATA/<GAMEID><suffix>/` (`ULUS10041DATA00`; a game often has several), each with a
   `PARAM.SFO` whose `TITLE` is the game. Folder `SAVEDATA`, scope `<GAMEID>*/**` — game IDs are 9 characters, so
   the prefix belongs to one game.
3. **States:** `PPSSPP_STATE/<GAMEID>_<version>_<slot>.ppst` with `.jpg` thumbnails and `undo.ppst`
   (`SaveState.cpp`, `GenerateFullDiscId`) → states scope `<GAMEID>_*`.
4. System `psp`. RetroArch's PPSSPP core keeps PSP saves elsewhere — out of scope until someone has it.

### Phase 5a — RPCS3 (PS3) — split from Phase 5

1. **Where:** EmuDeck links `Emulation/saves/rpcs3/saves` → `Emulation/storage/rpcs3/dev_hdd0/home/00000001/savedata`
   on both OSes (`trophy` is linked too — not synced). Standalone: `dev_hdd0` from RPCS3's `vfs.yml` (default
   `$(EmulatorDir)dev_hdd0/`: `~/.config/rpcs3/` on Linux, beside the exe on Windows — **capture** `vfs.yml`).
2. **Saves:** `savedata/<GAMEID><suffix>/` with a `PARAM.SFO` in each — PPSSPP's shape and scope, `<GAMEID>*/**`.
   Reading the installed game's `dev_hdd0/game/<id>/PARAM.SFO` (the old step 2) is only the fallback. System
   `ps3`.

### Phase 12 — Vita3K (PS Vita) — bonus

1. **Where:** EmuDeck sets Vita3K's `pref-path` to `Emulation/storage/Vita3K/` (`~/.config/Vita3K/config.yml` on
   SteamOS, `%APPDATA%\EmuDeck\Emulators\Vita3K\config.yml` on Windows) and links `Emulation/saves/Vita3K/saves` →
   `…/ux0/user/00/savedata`. Standalone: `config.yml`'s `pref-path`, defaulting to SDL's per-user folder
   (`~/.local/share/Vita3K/Vita3K/`, `%APPDATA%\Vita3K\Vita3K\` — **capture**).
2. **Saves:** `ux0/user/00/savedata/<TITLEID>/`, one per game → scope `<TITLEID>/**`. Name: the installed game's
   `ux0/app/<TITLEID>/sce_sys/param.sfo`, there wherever it is played; **capture** whether the save has its own
   `sce_sys/param.sfo`. System `psvita`.

### Phase 13 — shadPS4 (PS4) — bonus

1. **Where:** the user folder is `user/` beside the exe when it exists (portable), else `$XDG_DATA_HOME/shadPS4`,
   `~/.local/share/shadPS4` or `%APPDATA%\shadPS4` (`path_util.cpp`). EmuDeck links `Emulation/saves/shadps4/saves`
   → `~/.local/share/shadPS4/savedata` (SteamOS) and `%APPDATA%\EmuDeck\Emulators\ShadPS4-qt\user\savedata`
   (Windows).
2. **The layout moved.** Older builds wrote `savedata/<user id>/<CUSAxxxxx>/<save dir>/`; current `main` writes
   `<home dir>/<user id>/savedata/<CUSAxxxxx>/<save dir>/`, home dir `user/home` by default (`save_instance.cpp`
   `MakeTitleSavePath`, `path_util.h` `HOME_DIR`). EmuDeck's link points at the old one. Scan both; the folder is
   the per-user one and the scope `<CUSAxxxxx>/**`, so machines on either layout share one game.
3. **Name:** every save folder has `sce_sys/param.sfo` (`MAINTITLE`), written by shadPS4 itself. System `ps4`.
   The first phase to slip if the group grows: the layout is still moving.

**Verify (Group D):** `PARAM.SFO` fixtures (PSP save, PS3 save, Vita app, PS4 save); folder fixtures per layout,
both shadPS4 layouts included; the two-machine test on PPSSPP with a states folder.

### Group E — Title-ID folders: Wii U, 3DS, Xbox 360

D4's shape 2 with hex title IDs. Nothing in the save names the game, so the name comes from the emulator's own
cache or the ROM — machine-local, which D1 makes safe.

### Phase 14 — Cemu (Wii U)

1. **Where:** the MLC folder — `settings.xml` `<mlc_path>`, by default `~/.local/share/Cemu/mlc01` on Linux;
   **capture** the Flatpak (`info.cemu.Cemu`) and Windows defaults (`%APPDATA%\Cemu\mlc01`, or a portable folder
   beside the exe). EmuDeck on SteamOS sets `mlc_path` to `Emulation/roms/wiiu/mlc01` (kept from its Proton build)
   and links `Emulation/saves/Cemu/saves` → `…/mlc01/usr/save`; on Windows it links
   `%APPDATA%\EmuDeck\Emulators\cemu\mlc01\usr\save` ↔ `Emulation\saves\Cemu\saves`.
2. **Saves:** `usr/save/00050000/<title ID low>/`, holding `user/<account>` (`80000001` is the first account)
   and `common/`. Folder `usr/save/00050000`, scope `<low>/**` (`0005000e`/`0005000c` are updates and DLC).
3. **Name:** Cemu's `title_list_cache.xml` in its user-data folder (`<title titleId=…><name>`, `TitleList.cpp`),
   else the game's `meta/meta.xml` `longname_en`. System `wiiu`.
4. Known limit, documented rather than remapped: two machines on different Cemu accounts keep the save under
   different `user/` folders, and each loads only its own.

### Phase 15 — Azahar (3DS; Citra's successor)

1. **Where:** `%APPDATA%\Azahar\` or a portable `user\` beside the exe (Windows), `~/.local/share/azahar-emu/` (Linux,
   `file_util.cpp`), and a Flatpak — EmuDeck's script checks `~/.var/app/io.github.azahar.Azahar/data/azahar-emu/`,
   **capture** the app id. `qt-config.ini`'s `sdmc_directory`/`nand_directory` override. EmuDeck on SteamOS moves
   `sdmc` to `Emulation/storage/azahar/sdmc` and links `Emulation/saves/azahar/saves` → it and `/states` →
   `~/.local/share/azahar-emu/states`; on Windows it links `%APPDATA%\EmuDeck\Emulators\azahar\user\{sdmc,states}`
   ↔ `Emulation\saves\azahar\{saves,states}`. EmuDeck migrates old Citra folders (`citra-emu`,
   `org.citra_emu.citra`); add those as rows for standalone installs too.
2. **Saves:** `sdmc/Nintendo 3DS/<32 zeros>/<32 zeros>/title/00040000/<low>/data/`. Folder `…/title/00040000`,
   scope `<low>/data/**` — **not** `<low>/**`: the `content/` beside `data/` holds a CIA-installed game,
   gigabytes. Extdata (`…/extdata/00000000/<id>/`, used by e.g. Animal Crossing: New Leaf) is keyed by an ID only
   the game's own header names — deferred; `add-path` covers it.
3. **States:** `states/<program ID, 16 hex, upper case>.<NN>.cst`, or `.movie<id>.<NN>.cst` while recording
   (`savestate.cpp`) → states scope `<ID>.*`.
4. **Name:** a ROM's NCCH header holds its program ID in plain text (NCCH offset `0x118`), so
   `Emulation/roms/n3ds/*.3ds|.cci` map to title IDs without decrypting; the name is the cleaned file name. The
   SMDH title needs a decrypted ExeFS — **capture** whether Azahar's game-list cache already has names. Fallback
   `3DS <title ID>`. System `n3ds`.

### Phase 5b — Xenia (Xbox 360) — split from Phase 5

1. **Where:** EmuDeck on SteamOS runs Xenia Canary under Proton from `Emulation/roms/xbox360/` and links
   `Emulation/saves/xenia/saves` → `Emulation/roms/xbox360/content`; with native Xenia (`emuDeckXeniaNative.sh`)
   it is the `content` folder of its data path (**capture**). On Windows: `%APPDATA%\EmuDeck\Emulators\xenia\content`
   ↔ `Emulation\saves\xenia\saves`. Standalone Canary keeps `content\` beside the exe or in `Documents\Xenia`
   (**capture**).
2. **Saves:** `content/<profile XUID>/<title ID>/00000001/` (older builds: `content/<title ID>/00000001/`). The
   XUID is per profile, so it is part of the folder; scope `<title ID>/00000001/**`.
3. **Name:** a ROM file name that embeds the title ID (redump-style), else `Headers/00000001/*.header` beside
   the save, which carries a display name (**capture**), else `Xbox 360 <title ID>`. System `xbox360`.

**Verify (Group E):** fixtures per layout, Azahar's `content/` asserted out of scope; a Cemu
`title_list_cache.xml` fixture; an NCCH header fixture; the two-machine test on Azahar with states.

### Group F — Switch

### Phase 6 — Yuzu, Citron, Eden and Ryujinx (widened 2026-10-07)

The maintainer asked for all four, reversing this phase's "Ryujinx/Yuzu out of scope": the upstreams are gone,
but their builds and forks are what people run — EmuDeck still ships a script for Yuzu, Citron, Eden, Ryujinx
and Suyu. Keep the config roots a table, one row per fork, so a new fork is one row.

1. **The Yuzu forks — Yuzu, Citron, Eden (Suyu and Sudachi as rows).** Saves
   `nand/user/save/0000000000000000/<profile UUID>/<title ID>/`; device saves sit under the all-zero UUID. The
   profile UUID is made per install, so it is part of the folder, never the scope. Where: `qt-config.ini`
   `nand_directory`; standalone `%APPDATA%\<fork>\nand` or a portable `user\nand` (Windows), `~/.local/share/<fork>/nand`
   (Linux; old Flatpak `org.yuzu_emu.yuzu`). EmuDeck on SteamOS sets `nand_directory` to
   `Emulation/storage/<fork>/nand` and links `Emulation/saves/<fork>/saves` → `…/nand/user/save/`; on Windows
   (portable) it links `%APPDATA%\EmuDeck\Emulators\{yuzu\yuzu-windows-msvc,citron,eden-windows-msvc}\user\nand\user\save`
   ↔ `Emulation\saves\<fork>\saves`. Its `profiles` link is the avatars folder — not synced.
2. **Ryujinx (and the Ryubing fork).** Saves `bis/user/save/<save ID, 16 hex>/0/` — `0` is the committed copy,
   `1` the working one. IDs are handed out in order per install; `bis/system/save/8000000000000000/0/imkvdb.arc`
   maps (title ID, user, type) → ID. A new pure `RyujinxSaveIndex` reads it (`IMKV` header, `IMEN` entries;
   **capture** a real file and confirm the key/value layout before trusting it). Where: `%APPDATA%\Ryujinx`,
   `portable\` beside the exe (what EmuDeck for Windows uses), `~/.config/Ryujinx`, Flatpak `org.ryujinx.Ryujinx`;
   EmuDeck on SteamOS links `Emulation/saves/ryujinx/saves` → `~/.config/Ryujinx/bis/user/save`.
3. **Identity — D2.** Every candidate carries `switch:<title ID>` and points at its per-title folder
   (`<profile>/<title ID>`, or `<save ID>/0`) with no scope, so the archive has the same files from any of the
   four and a save syncs Eden on the Deck ↔ Ryujinx on Windows. Confirm on hardware that such a save loads (it is
   the documented manual migration) and that Ryujinx rebuilds `1/` from `0/` on its next mount.
4. **A machine that has never run the game:** a Yuzu fork's folder is known in advance (`<profile>/<title ID>`),
   declared, and created by the first pull. A Ryujinx save has no ID until the game makes one, so the game shows
   up there after its first launch — SaveLocker never writes `imkvdb.arc`.
5. **Which profile:** the current user in `qt-config.ini`, else the one with the newest writes — **capture**.
6. **Name:** Ryujinx's `games/<title ID>/gui/metadata.json` (**capture** its title field), a Yuzu fork's
   game-list cache (**capture**), a ROM file name in `Emulation/roms/switch/` carrying `[0100…]`, else
   `Switch <title ID>`. System `switch`.
7. Files: new `SwitchEmulators.cs` (the fork table and the Yuzu walk), `RyujinxSaveIndex.cs`; server: the
   `EmulatorKey` migration, DTO fields and the enroller match; regenerate `openapi.json` and both `api-types.ts`.

**Verify:** fixtures for the Yuzu layout and a captured `imkvdb.arc`; the two-machine test with one machine on
the Eden layout and one on the Ryujinx layout against a real server; hardware: the Deck (EmuDeck, Eden or
Citron) ↔ Windows (Ryujinx). Do not wire a fork's fast path before its layout is captured.

**Competitor research (2026-10-18, [[logs/2026-10-18_competitor-gap-analysis]]) — High priority, the next group
to build.** OpenSave and Hoard both support Switch, which is the most-emulated console on a Deck. What their code
adds to the plan above:

- **Nobody splits Ryujinx per title.** Hoard's catalogue says so in a comment (`emulators.rs`: the save ID "only its
  internal database knows", so it offers the whole `bis/user/save` root) and OpenSave does the same. Step 2's
  `RyujinxSaveIndex` would go further than either. That makes a fallback worth planning: if the `imkvdb.arc`
  capture is not conclusive, ship the Yuzu forks first and leave Ryujinx manual-only, as D2 already allows.
- **Which profile (step 5) — OpenSave's rule** (`internal/presets/switchsave.go`, MIT), for a save arriving from
  another machine: use the folder the other machine's path maps to if it exists here; else the game's own folder
  when exactly one emulator here has it; else a new `<profile>/<title ID>` under the emulator's **only** profile;
  anything less certain keeps the plain path and does not guess between two people's profiles. The all-zero
  profile is device saves, never a player.
- **Names (step 6) — where the caches are** (`switchtitles.go`): the Yuzu forks keep `<cache>/game_list/<TITLE
  ID>.appname.txt` (+ `.jpeg` icon) for NAND-installed games, and Citron and Eden also keep
  `game_metadata_cache.json` (icon base64 inside) for a ROM folder's games; Ryujinx and Ryubing keep
  `games/<title id>/gui/metadata.json` with a `title` field. The cache folder is `%APPDATA%\<emu>\cache`,
  `<emu>\user\cache` for a portable copy, `$XDG_CACHE_HOME/<emu>` on Linux (inside `~/.var/app/<id>/cache` for a
  Flatpak). Still **capture** each before trusting it.
- **More config-root rows**: Ryubing (the maintained Ryujinx fork) `%APPDATA%\Ryubing\bis\user\save`,
  `~/.config/Ryubing/…`, Flatpak `io.github.ryubing.Ryujinx` (its data still under `config/Ryujinx`); Flatpak IDs
  for Suyu `dev.suyu_emu.suyu`, Sudachi `org.sudachi_emu.sudachi`, Citron `org.citron_emu.citron`, Eden
  `dev.eden_emu.eden` (from OpenSave's `presets.go`; confirm against EmuDeck).
- Still open from above: D2 (one game across emulators) contradicts *Saves from different emulators are never one
  game*. Ask the maintainer before step 3.

---

### Group H — Real-game checks (no code)

### Phase 18 — real-game checks for emulators shipped without one

Added 2026-10-07 by the maintainer: no implementation, only testing with real games. Every emulator below shipped
with fixtures, unit tests and a testenv pass, but nobody has played a real game in it with SaveLocker watching, so
what the emulator actually writes, and when, is still unconfirmed. **When a later phase ships without a real-game
test, add its emulator here.** Tick a row only when both machines pass; a failure becomes a fix in that emulator's
own group, in a follow-up PR.

**Before testing: back up the save folders listed.** A test agent that adds a game pushes and pulls real files.

**Rig.**
- **Deck:** run `.\tests\testenv.ps1 build -Only deck`, then `.\tests\testenv.ps1 up -Only deck`. The test
  daemon scans the Deck's real EmuDeck install. Open its UI through the tunnel `up` prints.
- **Windows:** point the test tray at a **copy** of the real folders, not the originals. Copy `Emulation`, plus
  `%APPDATA%\EmuDeck\Emulators\{Supermodel,m2emulator}` and `%APPDATA%\ScummVM\scummvm.ini` into a copy home, then:
  ```powershell
  $env:SAVELOCKER_EMULATOR_HOME = '<copy>\home'
  .\tests\testenv.ps1 up -Only windows -EmuDeckPath <copy>\Emulation
  ```
  Edit `savepath` in the copied `scummvm.ini` so it points into the copy. The full commands are in PR #61.

**For every emulator:**
1. Play and save on the **Deck**. Add the game in the Deck agent's UI and sync.
2. Add it on **Windows**. Expect "Joins “…”" on its row, then sync. The save (and a state, where the emulator has
   them) arrives, and the emulator on Windows loads it.
3. Play further on Windows, sync, and check that the Deck loads the newer save.
4. Check the neighbours: other games' saves in the same folder are untouched on both machines.
5. Record in the table: the date, whether each step passed, and anything the emulator did that the plan didn't
   expect (when it writes, which file names, extra files).

| Emulator | Make a save | Make a state | Files (Deck · Windows) | Also check | Deck | Windows |
|---|---|---|---|---|---|---|
| **melonDS** | Save in-game. melonDS writes `<rom>.sav` shortly after | System → Save state → slot 1 (`<rom>.ml1`) | `Emulation/saves/melonds/{saves,states}` on both | Which file the Flatpak actually writes (ini vs toml paths); a save written beside the ROM | ✅ 2026-10-20 — tested by the maintainer, works | ✅ 2026-10-20 — tested by the maintainer, works |
| **ScummVM** | The game's own save menu, or Ctrl+F5 → Save | None (saves are the slots, `<target>.s00`…) | `Emulation/saves/scummvm/saves` (EmuDeck's `savepath`) on both | The real file names per engine (only four fixed-name engines are confirmed); two machines with different target names | ✅ 2026-10-20 — tested by the maintainer, works | ✅ 2026-10-20 — tested by the maintainer, works |
| **Supermodel** | Play, then exit: `NVRAM/<set>.nv` is written on exit | F5 save, F6 next slot, F7 load (`Saves/<set>.st0`…) | `~/.supermodel/{NVRAM,Saves}` · `%APPDATA%\EmuDeck\Emulators\Supermodel\{NVRAM,Saves}` | An unplayed set stays hidden (EmuDeck's preinstalled NVRAM); the title comes from `Games.xml` | ⏳ | ⏳ |
| **Model 2** | Play, then exit: `NVDATA/<set>.DAT` is written on exit | Number keys 1–9, 0 pick the slot, then the emulator's Save State key (keyboard needed) (`STATES/<set><slot>.sta`) | `Emulation/roms/model2/{NVDATA,STATES}` · `%APPDATA%\EmuDeck\Emulators\m2emulator\{NVDATA,STATES}` | Whether `.DAT` changes on every exit; which key saves a state; an unplayed set stays hidden; `STATES` is created on the first save | ⏳ | ⏳ |
| **PCSX2** | Convert the card to a folder first (Settings > Memory Cards > Convert > Folder, then Use for Slot 1 — the Deck's `PS2MC-1.ps2` is a file card), then save in-game | Save state slot 1 (`<serial> (<crc>).01.p2s`) | `Emulation/saves/pcsx2/{saves,states}` on both | The game's save folder names on the card (one product code, or several?); the `.backup` copies stay local; a pull into a running card is never needed (PCSX2 closed); the shared file card row disappears once converted | ⏳ | ❌ 2026-10-19 — *Warrior Within* from the Deck arrived intact but PCSX2 showed no save: a new folder card's empty superblock, and a pull while PCSX2 was open. Fix: `tasks/emulator-pull-safety/plan.md` |
| **DuckStation** | Save in-game (Card 1 is per game by default) | Save state slot 1 (`<serial>_1.sav`), and the resume state on exit | `Emulation/saves/duckstation/{saves,states}` on both | The card's real name (game database title, or the playlist's for a multi-disc game); a multi-disc game's states on disc 2 | ❌ 2026-10-20 — never set up: the Deck had no save of its own, so it never pulled the PC's save or states (`tasks/emulator-pull-safety/plan.md` → *Phase 5*) | ✅ 2026-10-20 — saved and state-saved, pushed |
| **Dolphin** | Save in-game, GameCube and Wii (Metroid: Other M is on the Deck already) | Save state slot 1 (`<id>.s01`) | `Emulation/saves/dolphin/{GC,Wii}` + `StateSaves` (Deck) · `states` (Windows) | A GameCube save's real `.gci` name; a WiiWare title (`00010001`) | ✅ 2026-10-20 — tested by the maintainer, works | ✅ 2026-10-20 — tested by the maintainer, works |
| **PPSSPP** (PSP) | Save in-game (`SAVEDATA/<ID>…`, usually several folders) | Save state slot 1 (`<ID>_<version>_0.ppst` + `.jpg`) | `~/.var/app/org.ppsspp.PPSSPP/config/ppsspp/PSP/{SAVEDATA,PPSSPP_STATE}` · `%APPDATA%\EmuDeck\Emulators\PPSSPP\memstick\PSP\…` | All of a game's folders share its 9-character ID; the undo state's real name; a PSN game is named by its own SFO | ❌ 2026-10-20 — never set up: the Deck had no save of its own, so it never pulled the PC's save or states (`tasks/emulator-pull-safety/plan.md` → *Phase 5*) | ✅ 2026-10-20 — saved and state-saved, pushed |
| **RPCS3** (PS3) | Save in-game (the Deck has *Heavenly Sword*'s autosave already) | None | `Emulation/storage/rpcs3/dev_hdd0/home/00000001/savedata` on both | A game with several save folders; the trophy folder stays local | ✅ 2026-10-19 — tested by the maintainer, works | ✅ 2026-10-19 — tested by the maintainer, works |
| **Vita3K** (PS Vita) | Save in-game | None | `Emulation/storage/Vita3K/ux0/user/<user>/savedata` on both (`00` is the first user, the one EmuDeck links) | **Whether the save has its own `sce_sys/param.sfo`** (else the name comes from the installed game only); a second Vita3K user's save is listed too | ⏳ | ⏳ |
| **shadPS4** (PS4) | Save in-game | None | Old layout `~/.local/share/shadPS4/savedata/<user>` · `%APPDATA%\EmuDeck\Emulators\ShadPS4-qt\user\savedata\<user>`; new layout `…/home/<user>/savedata` (users `1000`–`1003`) | **Which layout the installed build writes** (both EmuDeck installs link the old one) and, after shadPS4 offers to move old saves to `home/1000`, that the game is still found; the user ID folder's name; `MAINTITLE` in the save; a save on one layout joining a save on the other | ✅ 2026-10-20 — tested by the maintainer after the test rig fix (pull-safety Phase 0): *Dark Souls: Remastered* found and synced. The first try failed because the rig hid `~/.local/share`. Still named "…Savedata (EU)" (`tasks/emulator-pull-safety/plan.md` → *Phase 7*) | ✅ 2026-10-20 — saved and pushed (user `1004`, new layout) |
| **Cemu** (Wii U) | Save in-game (`usr/save/00050000/<low>/user/80000001/…`) | None | `Emulation/roms/wiiu/mlc01/usr/save` (Deck, SD card) · `%APPDATA%\EmuDeck\Emulators\cemu\mlc01\usr\save` | The name: Cemu's `title_list_cache.xml` on each machine (where the AppImage keeps it); both machines on account `80000001`; a disc-format game (`.wud`/`.wux`) with no `meta.xml` | ✅ 2026-10-20 — tested by the maintainer, works | ✅ 2026-10-20 — tested by the maintainer, works |
| **Azahar** (3DS) | Save in-game (the Deck has Super Mario 3D Land, Samus Returns and one more) | Save state slot 1 (`<program ID>.01.cst`) | `Emulation/storage/azahar/sdmc` + `~/.local/share/azahar-emu/states` · `%APPDATA%\EmuDeck\Emulators\azahar\user\{sdmc,states}` | The name from the ROM on each machine; a CIA-installed game's `content/` never syncs; the third Deck save (`0016e300`, no ROM) | ✅ 2026-10-20 — tested by the maintainer: the files sync. The Deck and the PC run different Azahar versions, so the synced save did not load in the game; that is Azahar's version gap, not SaveLocker's | ✅ 2026-10-20 — tested by the maintainer: the files sync (same version note) |
| **Xenia** (Xbox 360) | Save in-game (the PC and the Deck both have Forza Horizon) | None | `Emulation/roms/xbox360/content` (Deck, Proton) · `%APPDATA%\EmuDeck\Emulators\xenia\content` | Two profiles' XUIDs (the same one on both machines today); the Deck's old-layout folder; the DLC under `00000002` stays local; a pulled save loads in the other machine's profile | ✅ 2026-10-19 — tested by the maintainer, works | ✅ 2026-10-19 — tested by the maintainer, works |

| **Switch** (Eden/Yuzu ↔ Ryujinx) | Save in-game (the Deck has Undertale in both Yuzu and Ryujinx, the PC Metroid Prime 4 in Eden) | None | `~/.local/share/yuzu/nand/user/save` + `~/.config/Ryujinx/bis/user/save` (Deck) · `Emulation\saves\{eden,ryujinx}\saves` (PC) | One game across Yuzu on the Deck and Ryujinx/Eden on the PC; **a pulled save loads in Ryujinx** (we write `0` and `1`) and Ryujinx keeps it; the Emulator settings copies between Yuzu and Ryujinx on the Deck (Undertale differs in the two today — *Both changed* is expected the first time) | ✅ 2026-10-18 — Animal Crossing 3.0.3 in Eden (SD-card NAND); found Eden opens the island from the device save, so device-save games now stay there on every device | ✅ 2026-10-18 — Animal Crossing 3.0.3 in Eden; Ryujinx not played against a real game |
| **PrimeHack** | Save in Metroid Prime Trilogy (on the Deck already) | Save state slot 1 (`R3ME01.s01`) | `Emulation/saves/primehack/{GC,Wii}` + `StateSaves` (Deck) · `states` (Windows) | It stays its own game beside Dolphin's Trilogy; where a non-portable Windows PrimeHack keeps its user folder (the PC's EmuDeck install has never run it) | ✅ 2026-10-20 — tested by the maintainer, works | ✅ 2026-10-20 — tested by the maintainer, works |

### Group I — Games found by their save states alone

### Phase 19 — a game with states but no in-game save yet, for every other emulator

**Why.** Until 2026-10-12 every reader found a game by its in-game save only; states were the second folder of a game
already found. A game played from states alone — common between a PSP game's save points, or with RetroArch's
auto-state — was never offered. PPSSPP's shipped with Group D (*Group D — as built*); this phase does the rest.

**The rule PPSSPP set, for each reader:** read the game's identity from the state's file name; list it marked
`StatesOnly` (*Save states only so far*), named and scoped exactly as if it had a save, its save scope declared so the
first in-game save joins; a save anywhere on the machine wins over states alone; skip empty files and states no game's
name starts (PPSSPP's `load_undo.ppst`). A states-only push never empties another machine's never-synced save (pull
refused — one two-machine test per family).

| Emulator | State names → identity | To settle |
|---|---|---|
| RetroArch | `<rom>.state`, `.state1`…, `.state.auto`, per-core folders | the save's extension per core (`.srm`, `.sav`) to declare the save scope; per-core folder for saves vs states |
| melonDS | `<rom>.ml1`…`.ml8` | the save's name beside the ROM's |
| PCSX2 | `<serial> (<CRC>).NN.p2s` | which folder card the game's saves would go to |
| DuckStation | `<serial>_N.sav`, `<serial>_resume.sav` | the per-game card's file name (`<title>_1.mcd`) from a serial alone |
| Dolphin / PrimeHack | `<game ID>.s01`…`.s10` | GameCube `.gci` vs Wii `title/` scope from the ID's first letter |
| Azahar | `<program ID>.<NN>.cst` | the program ID gives the `title/00040000/<low>/data` scope directly; added 2026-10-12 with Group E |

No states, so not in this phase: RPCS3, Vita3K, shadPS4, ScummVM, Supermodel, Model 2, Cemu, Xenia.

**Verify:** per emulator, a fixture with a state and no save (listed, `StatesOnly`, correct name and scope), a save
elsewhere winning, and an empty state ignored; the two-machine test per family above; testenv fixture rows.

### Phase 20 — a Steam ROM Manager shortcut is the emulator's game (Group G, not started)

**Why.** EmuDeck's Steam ROM Manager (SRM) adds a non-Steam shortcut to Steam for every ROM, so the game shows in Game
Mode. SaveLocker reads `shortcuts.vdf` and offers each one as a *Steam shortcut* game, while the emulator's reader
offers the same game from its save. On the maintainer's Deck *Forza Horizon* is listed twice (2026-10-13): once from
Xenia's save, once from its SRM shortcut, which launches Xenia with the ROM and has no save folder of its own. Tracking
both makes two server games for one save, or one that never syncs.

**Detect.** A shortcut is SRM's when any of these hold, strongest first (each to be confirmed on a capture):
1. SRM's own record of what it added (`~/.config/steam-rom-manager/userData/` — the added items with their app IDs),
   when SRM is installed on this machine.
2. Its `Exe` is an EmuDeck launcher (`Emulation/tools/launchers/<emulator>.sh`, or the `.ps1`/`.exe` launchers on
   EmuDeck for Windows) or an emulator SaveLocker reads, and its `LaunchOptions` name a file under the ROMs folder.
3. Its tags carry SRM's category for a system (`Xbox 360`, `PlayStation 2`, …).
The shortcut's ROM path (from `LaunchOptions`) is what ties it to a game: the emulator reader's candidate for that ROM
(`EmulatorRom`), or for Xenia/Cemu/RPCS3 the title the ROM or its folder names. Only when no ROM path is readable, the
cleaned name.

**What changes.**
- **Add games:** an SRM shortcut is not listed as a Steam shortcut game. Its emulator row says it was also found as a
  Steam shortcut, and the shortcut's app ID is attached to it.
- **Launch and exit:** the attached app ID lets pull-before-launch and push-after-exit work for the emulator game
  started from that shortcut (today only the shortcut row could have them). This is the deferred
  *Recovering ROM identity from an SRM shortcut's `LaunchOptions`*, now built.
- **Art:** the shortcut's grid and icon can be offered for the emulator game.
- **Already tracked twice:** a tracked shortcut game whose emulator game is also tracked shows *Same game as
  \<name\> (\<emulator\>)* with **Stop tracking this copy**. Merging the two server games waits for a server merge
  (Phase 17's open question; [[Backlog]] → *One game, several real sources*).
- **Windows:** EmuDeck for Windows and SRM there work the same way; the same rules apply.

**Open questions (decide before building):**
- **Which signal is enough on its own?** SRM's record alone, or the launcher plus a ROM path too? A hand-made shortcut
  to an emulator with a ROM is the same thing to SaveLocker, so the launcher plus ROM path may be the rule.
- **A shortcut with no emulator candidate** (a ROM never played, so no save yet): hidden, or listed as the emulator game
  waiting for its first save (Phase 19's *states only* gives the precedent)?
- **Other front ends** that add shortcuts the same way (BoilR, Heroic's emulator entries, RetroDECK): in this phase or
  later?
- **Name on the server** when only the shortcut's name differs from the reader's (SRM's titles come from its own
  fuzzy match): always the reader's, as today.

**Verify:** a captured `shortcuts.vdf` and SRM's records from the Deck (read-only) as fixtures — the Xenia, RetroArch
and PCSX2 shortcuts each matched to their emulator candidate, a hand-made Steam shortcut to a PC game left alone, a
shortcut whose ROM has no save; Add games on the Deck lists *Forza Horizon* once; the app ID drives pull-before-launch
for the emulator game (testenv WSL with a fixture shortcut, then the real Deck under Group H).

### Phase 21 — emulator and console icons (Group J, not started)

**Why.** Emulator games are told apart by small text today ("Ryujinx", "Nintendo Switch"). A flat icon per emulator and
per console makes the Add games chips, the platform groups and the game page readable at a glance. Researched and mocked
up 2026-10-14 with the maintainer (`phase-21-icons-mockup.html` in this folder: the console's games list grouped by
platform, Add games, the game page with the Switch copies card, the agent's grid and Emulator settings).

**Sources (settled with the maintainer):**

| What | From | Licence |
|---|---|---|
| Emulators | [Arcticons](https://github.com/Arcticons-Team/Arcticons) line icons (48 grid, `currentColor`): RetroArch, Dolphin, PPSSPP, DuckStation, Azahar, Eden, Citron, Yuzu, Suyu, Sudachi, Ryujinx, melonDS, Vita3K, Xenia | CC BY-SA 4.0 |
| Consoles | RetroArch's Monochrome theme glyphs (`libretro/retroarch-assets`, `src/xmb/monochrome`) | CC BY 4.0 |
| RPCS3 | its own logo, `rpcs3/rpcs3.svg` in the RPCS3 repo (rpcs3.net/branding is behind a bot check), one colour | the project's — check its terms |
| ScummVM | `scummvm-media` → `derivate/scummvm_ios7_tinted_logo.svg`, flattened: the shape filled, the S cut out | the project's — check its terms |
| PCSX2, shadPS4 | the PS2 and PS4 console glyphs (maintainer's call) | as consoles |
| Model 2 Emulator, Supermodel | the MAME glyph (maintainer's call) — a wordmark about 3.6 : 1, so it gets a wider box than the square icons | as consoles |
| Cemu | **none yet** — no Arcticons icon and no logo given; the mockup's amber stand-in is a placeholder to replace | — |

Simple Icons was ruled out: it dropped every Nintendo logo in v14 and has no Xbox.

**Where they go** (numbered in the mockup): the console's platform group headings (console glyph) and emulator
sub-headings (emulator icon); cover tiles of games with no art (console glyph); the release chip (console) and source
chip (emulator) on both game pages; each machine's row on the console's game page; the agent's Add games emulator chips
and candidate rows; the Switch copies panels; the agent's grid covers (a console badge); the Emulator settings list; the
Deck's emulator pills.

**Build notes:**
- One generated SVG sprite (`<symbol>` per icon) shared by agent-ui and web, from a script that downloads the sources
  and writes the sprite, so a refresh is one command. Read each source file's own `viewBox` — several RetroArch glyphs
  are drawn at `0 128 64 64` and MAME on a 256 canvas, and assuming 64 × 64 left them blank in the first mockup.
- Arcticons' default stroke of 1 is too faint at 15–20 px; draw them at about 2.6.
- Icons are decoration (`aria-hidden`); labels and controller A-button labels don't change; anything new still follows the gamepad
  navigation rules (PR #72: `data-pad-label`, `data-pad-action`).
- Deck Game Mode: `SvgPath.cs` reads only M/L/H/V/A/Z, and these icons use curves. Either teach it C/S/Q/T (about 40
  lines) or flatten the curves when the sprite is generated.
- Credits in `NOTICE.md`: Arcticons (CC BY-SA 4.0 — share-alike covers the icon files, not our code), RetroArch assets
  (CC BY 4.0), and the RPCS3 and ScummVM logos once their terms are checked.
- **Optional, decide when starting:** a Switch game's eShop icon as its cover. titledb (the table Group F's names come
  from) carries `iconUrl` / `bannerUrl` per title ID.

**Verify:** the sprite has no missing ids and every icon has a non-empty bounding box (a script check); screenshots of
each placement in both themes in testenv (Windows + WSL agents, the console); the Deck pills on hardware.

## Deferred, not built in this task

**Recovering ROM identity from a Steam ROM Manager shortcut's `LaunchOptions`** moved into Phase 20 (2026-10-13), with
the duplicate entries it causes on the Deck.

**Save states** and **Ryujinx** were here; both are now in scope (*Save states — decided*, D3; Phase 6).

**3DS extdata** (Phase 15) — keyed by an ID only the game's own header names. `add-path` covers it.

**Cemu accounts** (Phase 14) — a save made under a different account on another machine is not remapped.

**Writing Ryujinx's `imkvdb.arc`** (Phase 6) — so a machine could receive a Switch save before the game first
ran there. Not worth the risk of corrupting Ryujinx's own index.

---

## Done when

- Each phase above is built, verified per its own Verify section, and committed separately.
- No phase silently tracks a shared memory card/save file as if it were one game's save — Phase 2's
  `SaveDirSanity` warning is in place and tested against both shared and per-game layouts before
  Phase 4 (which depends on it) begins.
- Every point marked **capture** is confirmed against a real install before it is wired in — EmuDeck's
  scripts say where the folders are, not what the emulator writes into them.
- Group F's title key (D2) and Group B's identity by folders (D1) were confirmed with the maintainer before
  being built.
- `EmulatorName`/`EmulatorSystem` flow end-to-end from scan through to the Phase 7 UI filter row and
  its Game Mode mirror, with test coverage neither host's existing Heroic-store sub-chips have today.
