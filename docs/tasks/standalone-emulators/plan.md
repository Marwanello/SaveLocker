# Task: More standalone emulators, and RetroArch cores

Planned 2026-10-29. This file replaces the summary seeded on 2026-10-18 from the competitor gap analysis
([[logs/2026-10-18_competitor-gap-analysis]]). **Priority: Low.** It is separate from
[[tasks/_done/emulator-saves/plan|Emulator saves]]: most of this work is new rows of the readers that task built (`RomSaves`
for D4 shape 1, `TitleFolders` for shape 2).

**Scope (maintainer, 2026-10-29):**
- **EmuDeck's 7 + Lime3DS.** EmuDeck installs seven emulators SaveLocker does not read yet: mGBA, RMG (N64), Flycast, MAME,
  ares, xemu and BigPEmu. Lime3DS is a cheap extra row. The non-EmuDeck standalones (DeSmuME, Snes9x, Project64, …) go to
  *Later*.
- **MAME is named from a bundled set → title table**, the way `SwitchTitles` and `ConsoleTitles` work.
- **Flycast's shared VMU becomes a greyed NotSyncable row with a how-to-fix**, like PCSX2's shared file card.
- **RetroArch cores** were added on request. The plan covers how switching cores works today, whether cores share saves
  and states, and what to fix.

Execute one phase per session (or one group per PR, per [[#Status]]), verify it, and stop.

## Status

| Phase | Status |
|---|---|
| 1 — RetroArch: pin today's core behaviour with tests | ⏳ Not started |
| 2 — RetroArch: know the core (`content_history.lpl`) + cross-core warning | ⏳ Not started |
| 3 — RetroArch: follow a core switch (sort-by-core) | ⏳ Not started |
| 4 — `RomSaveRules` with several save extensions | ⏳ Not started |
| 5 — mGBA | ⏳ Not started |
| 6 — RMG (N64) | ⏳ Not started |
| 7 — Lime3DS rows for the Azahar reader | ⏳ Not started |
| 8 — Flycast | ⏳ Not started |
| 9 — MAME + set title table | ⏳ Not started |
| 10 — ares | ⏳ Not started |
| 11 — xemu: not-supported row | ⏳ Not started |
| 12 — BigPEmu: capture, then reader or not-supported row | ⏳ Not started |
| 13 — RetroArch: per-system extra save files (scope widening) | ⏳ Not started |
| 14 — RetroArch: shared files as NotSyncable rows | ⏳ Not started |
| 15 — Copy pairs: mGBA ⇄ RetroArch, DuckStation ⇄ RetroArch | ⏳ Not started |
| 16 — Real-game checks (no code) | ⏳ Not started |
| 17 — Later: non-EmuDeck standalones | ⏳ Not started (separate task when picked) |

## Research (2026-10-29)

Sources: EmuDeck's setup scripts (`dragoonDorise/EmuDeck` `functions/EmuScripts/*.sh` for SteamOS and `EmuDeck/emudeck-we`
`functions/EmuScripts/*.ps1` for Windows) and EmuDeck's shipped `retroarch.cfg`. Emulator sources: Flycast
`core/oslib/oslib.cpp` (`getVmuPath`, `getArcadeFlashPath`, `getSavestatePath`), Mupen64Plus `src/main/main.c`
(`get_save_filename`) and ares `desktop-ui/emulator/emulator.cpp` (`Emulator::locate`).

EmuDeck's script list also has ARMSX2 (ARM only), RPCS3Legacy and XeniaNative. The last two are variants of emulators
SaveLocker already reads, so none of the three are in scope.

| Emulator | EmuDeck (Deck) | EmuDeck (Windows) | Save shape | States |
|---|---|---|---|---|
| **mGBA** (GB/GBC/GBA) | AppImage. `~/.config/mgba/config.ini` `savegamePath`/`savestatePath` → `Emulation/saves/mgba/{saves,states}` | `%APPDATA%\EmuDeck\Emulators\mgba\config.ini` (template paths rewritten), same folders | `<rom>.sav` (shape 1) | `<rom>.ss0`–`.ss9` |
| **RMG** (N64, Mupen64Plus core) | Flatpak `com.github.Rosalie241.RMG`. `config/RMG/mupen64plus.cfg` `SaveSRAMPath`/`SaveStatePath` → `Emulation/saves/RMG/{saves,states}` | — | `<GoodName≤32>-<MD5[0..8]>.{eep,sra,fla,mpk}` (core default `SaveFilenameFormat=1`; a legacy `<GoodName>.<ext>` wins if it exists). **Several extensions per game.** | `<same base>.st0`–`.st9`: confirm in `savestates.c` |
| **Flycast** (Dreamcast/NAOMI/Atomiswave) | Flatpak `org.flycast.Flycast`. `Emulation/saves/flycast/saves` **links to** `~/.var/app/org.flycast.Flycast/data/flycast/`, which is also the BIOS folder | `%APPDATA%\EmuDeck\Emulators\flycast\data` (linked) | VMU: a shared `vmu_save_A1.bin` by default. With `PerGameVmu`, `<gameId>_vmu_save_A1.bin` (A1 only). Arcade: `<rom>.nvmem`/`.eeprom`/… through `get_game_save_prefix` | `<rom>.state`, `<rom>_N.state` (data folder unless `SavestatePath` is set) |
| **MAME** (arcade) | Flatpak `org.mamedev.MAME`. `~/.mame/mame.ini` `nvram_directory`/`state_directory` → `Emulation/saves/mame/{saves,states}` | `%APPDATA%\EmuDeck\Emulators\mame\mame.ini`, same | **One folder per set**: `saves/<set>/…` (shape 2) | `states/<set>/<slot>.sta` |
| **ares** (several systems) | Flatpak `dev.ares.ares`. `data/ares/settings.bml` `Paths` → `Saves: …/Emulation/saves/ares/` | — | `<Saves>/<System name>/<rom>.<suffix>`: one level of system folders, several suffixes | Unconfirmed: capture on the Deck |
| **xemu** (Xbox) | `xemu.toml` `hdd_path` → `Emulation/storage/xemu/xbox_hdd.qcow2`, and `saves/xemu/saves` links there | same | Saves live **inside the qcow2 disk image**, so a game's save can't be synced on its own | — |
| **BigPEmu** (Jaguar) | Linux build. `saves/BigPEmu/saves` → `Emulators/BigPEmu/UserData` | `%APPDATA%\BigPEmu` (linked) | **File names unknown** (no public doc): capture first | ? |
| **Lime3DS** (3DS) | — (discontinued and merged into Azahar) | — | Azahar's layout under `lime3ds-emu` (XDG and Flatpak `io.github.lime3ds.Lime3DS`) and `%APPDATA%\Lime3DS` | as Azahar |

### RetroArch cores: how SaveLocker handles them today

From `RetroArchSaves.cs`, `RomSaves.Scan` and `Enroller.TrackedFor`:

- **EmuDeck, the default target.** EmuDeck's `retroarch.cfg` sets `sort_savefiles_enable`, `sort_savefiles_by_content_enable`
  and both `sort_savestates_*` to `"false"`. Every core therefore writes the same `saves/retroarch/saves/<rom>.srm` and the
  same `states/<rom>.state*`. When the user switches core, the new core opens the old core's file. SaveLocker sees one game
  whichever core runs (`EmulatorCore` is null), which works when the formats match.
  **States are the trap:** core B cannot load core A's state, and core B's slot 1 overwrites core A's because the file name
  is the same.
- **"Sort saves by core" on, in a standalone RetroArch.** Saves go to `saves/<core>/<rom>.srm`. The scan reads one level of
  core folders, merges the same ROM into one candidate (the newest copy, `GroupBy(EmulatorRom)`) and enrolls that core's
  folder.
  **Gap, to pin with a test first:** after a core switch, RetroArch looks only in the new core's folder and starts an empty
  save there. The tracked game stays mapped to the old core's folder, because `TrackedFor` matches by name and scope and the
  row reads "already tracked". The new core's save is then silently not synced, and pulls land in the folder the user no
  longer plays from. `EmulatorPlaces` already declines per-core setups.
- **Across machines.** The archive holds `<rom>.srm` at its root, whichever core folder it came from. A Deck on core X and a
  PC on core Y exchange saves across cores with no warning, and states too.
- **Files the scope misses.** The scope is only `<rom>.srm` + `<rom>.rtc`.
  - EmuDeck **turns on Beetle PSX memory card 1**, which is `<rom>.1.mcr` (cards go up to `.63.mcr`).
  - Beetle Saturn writes `<rom>.bkr`, `.bcr` and `.smpc`.
  - NGP writes `.flash` and PUAE `.nvr`.
  - Genesis Plus GX can keep the CD BRAM per game as `.brm`; the default is per BIOS, one shared file.
  - Outside the saves folder: the Flycast core's VMUs sit in `Emulation/bios/dc/`, and EmuDeck sets
    `reicast_per_content_vmus` to disabled, so they are shared.
  - The MAME and FBNeo cores keep NVRAM in their own subfolders, DOSBox Pure keeps `saves/DOSBox-pure/<game>.pure.zip`, and
    the PPSSPP core (already noted) keeps its own folders.

### Do cores share saves and states?

Save files mostly do. States never do. Rows marked *verify* go to Phase 16.

| System | EmuDeck cores | `.srm` across cores | Notes |
|---|---|---|---|
| NES | Mesen, Nestopia | Compatible (raw battery RAM) | |
| SNES | Snes9x, bsnes, Mesen-S | Compatible (raw SRAM) | |
| GB/GBC | Gambatte, SameBoy, mGBA, Gearboy | SRAM compatible. **RTC differs** (a separate `.rtc` vs an appended footer): *verify* | Pokémon Gold/Silver/Crystal clock |
| GBA | mGBA, gpSP, VBA-M | Expected compatible (raw). *Verify* an EEPROM game | Same bytes as standalone mGBA's `.sav` |
| N64 | Mupen64Plus-Next (+ ParaLLEl) | Compatible: the same combined 290 KiB `.srm` (EEPROM + SRAM + Flash + 4 controller paks) | Standalone RMG keeps the parts as separate files, so converting needs a split/join |
| PS1 | Beetle PSX HW, PCSX ReARMed, SwanStation | Slot 0 `.srm` is a raw 128 KiB card for Beetle and ReARMed. SwanStation: *verify* | Same bytes as DuckStation's `.mcd` |
| DS | melonDS, melonDS DS, DeSmuME | Raw | Already an `EmulatorSaveCopy` pair |
| Genesis/CD | Genesis Plus GX, PicoDrive, BlastEm | *Verify* | |
| Saturn | Beetle Saturn, Kronos, YabaSanshiro | **Incompatible** (Beetle's `.bkr` vs Yabause's `.srm`) | |
| any | — | **States never cross cores**, or core versions | |

## Phases

### Phase 1 — RetroArch: pin today's core behaviour with tests
In `RetroArchTests`/`RetroArchSyncTests`:
- A flat folder plus a core switch: one game, one file.
- Sort-by-core plus a core switch: the new folder is not synced. Expected to fail until Phase 3, so mark it skipped with a
  reason.
- Two machines on different cores: the save crosses with no warning.

**Verify:** `dotnet test`.

### Phase 2 — RetroArch: know the core
- Read RetroArch's `playlists/builtin/content_history.lpl` (`path` → `core_name`) on both EmuDeck installs and on a
  standalone install, so even a flat folder knows the last core per ROM.
- Record the core as a source tag (`MachineGameSources`, like SNES/Flatpak/EmuDeck) and show *played with mGBA* in the
  console, the agent UI and Game Mode.
- **Warn** on the game page when machines play one game with different cores. Use the compatibility table: states won't
  load, and the save is fine only where the table says so.

**Verify:** unit tests on a fixture `.lpl`; then the warning through `testenv` with two machines on different cores.

### Phase 3 — RetroArch: follow a core switch (sort-by-core)
- Trigger: the newest copy of a tracked game's `<rom>.srm` is in a different core folder than the one mapped.
- Response: a notice plus one click, **"Sync the <new core> save instead"**. It remaps the folder and backs up the old copy,
  as `SwitchCopies` does for Switch copies.
- Never automatic, because a pull would overwrite.

**Verify:** Phase 1's skipped test passes; `testenv` by hand.

### Phase 4 — `RomSaveRules` with several save extensions
`SaveExtensions` replaces `SaveExtension` in `src/Agent.Core/RomSaves.cs`; the existing rows pass one extension each. The
ROM-name rules from `RomNames` are unchanged. **Verify:** every existing reader test is unchanged and green.

### Phase 5 — mGBA
`MgbaSaves.cs` is a row like `MelonDsSaves.cs`:
- Where: EmuDeck's folders first, then `config.ini` in the standalone, portable and Flatpak (`io.mgba.mGBA`) locations,
  read through `EmulatorPaths.ReadIni`.
- A save beside its ROM counts only next to a GB/GBC/GBA ROM (`RomExtensions`).
- `System` comes from `Emulation/roms` (`RomSystems`).
- `StateRom` matches `.ss0`–`.ss9`.

### Phase 6 — RMG (N64)
`RmgSaves.cs`:
- Four extensions: `.eep`, `.sra`, `.fla`, `.mpk`.
- The ROM key is the whole base name, which is stable across machines because it carries the MD5.
- The title is the base name without its trailing `-XXXXXXXX`, then cleaned by `RomNames`.
- Standalone Mupen64Plus and simple64 share the core, so their `mupen64plus.cfg` locations are cheap extra rows.

### Phase 7 — Lime3DS
- Extra `AzaharSetup` rows in `AzaharSaves.cs`: XDG `lime3ds-emu`, the Flatpak, `%APPDATA%\Lime3DS`.
- `EmulatorProcesses` gains `lime3ds`/`lime3ds-gui`. `SrmShortcuts` already has the `lime3ds` row.

### Phase 8 — Flycast
`FlycastSaves.cs` reads the setup's data folder:
- Where: EmuDeck's link through `EmuDeckRoots.RealPath`, the Flatpak's data folder, the Windows portable `data`, and
  `emu.cfg`'s `VMUPath`/`SavePath`/`SavestatePath` when set.
- Arcade `<rom>.nvmem*`/`.eeprom` become shape-1 rows (system from `Emulation/roms` naomi/naomi2/atomiswave).
- `<gameId>_vmu_save_A1.bin` becomes one game per disc ID, named through `ConsoleTitles`. This needs a **Dreamcast serial
  table**: add Redump's *Sega - Dreamcast* to `Update-ConsoleTitles.ps1`.
- The shared `vmu_save_A1.bin` (and B1…D1) becomes a **NotSyncable** row: "turn on *Per-game VMU* in Flycast's Controls
  settings". Build it the way `MemoryCards.cs` builds PCSX2's row.
- States `<rom>.state`/`<rom>_N.state` are the `states` extra folder.
- The data folder also holds the BIOS, so every scope names files and never the whole folder.

### Phase 9 — MAME
`MameSaves.cs` on `TitleFolders` (shape 2):
- Folders: `nvram_directory/<set>/` is the main folder and `state_directory/<set>/` the states, read from `mame.ini`
  (EmuDeck, `~/.mame`, the Flatpak, Windows portable).
- If a Deck capture shows EmuDeck seeds NVRAM, use the `IsUntouchedSeed` check.
- **Names:** a new `src/Shared/MameTitles` table (set → description, parent), built by `Update-MameTitles.ps1` from MAME's
  `-listxml` or libretro-database's MAME DAT. It ships as an embedded `.tsv.gz` with a NOTICE entry, like `SwitchTitles`.
- Name precedence: ES-DE gamelist → table → set name.

### Phase 10 — ares
`AresSaves.cs`:
- Reads `Paths/Saves` from `settings.bml` with a small BML reader.
- System folders (`Nintendo 64`, `Super Famicom`, …) map to EmuDeck system ids. This is the `PerCoreFolders` level.
- Scope `<rom>.*` only once a capture shows states don't share the folder.

### Phase 11 — xemu
A NotSyncable row when `xbox_hdd.qcow2` exists: "xemu keeps every game's save inside one disk image." No sync.

### Phase 12 — BigPEmu
**Capture first:** save a game, then look at the files over read-only SSH on the Deck. Build a reader only if the saves are
files per game; otherwise this becomes a NotSyncable row like xemu's.

### Phase 13 — RetroArch: per-system extra save files
Scope the extra files **per system**, so other systems' scopes stay as they are:
- PS1 `<rom>.*.mcr`
- Saturn `.bkr`/`.bcr`/`.smpc`
- NGP `.flash`
- Amiga `.nvr`
- per-game `.brm`

**Design point for the maintainer when this starts:** existing games' server scopes are narrower, so a wider candidate would
no longer join them (D1, "same files"). Proposed: a scope-widening step (the agent proposes the superset and the server
updates the game once), not a second game.

### Phase 14 — RetroArch: shared files as NotSyncable rows
- The Flycast core's shared VMU and Genesis Plus GX's per-BIOS BRAM get the how-to-fix: turn on per-content VMUs or
  per-game BRAM. This matches Phase 8.
- The MAME, FBNeo and DOSBox Pure core folders wait for Phase 9's reader to reuse.

### Phase 15 — Copy pairs
Rename-only pairs (same bytes) in `src/Shared/EmulatorSaveCopy.cs`:
- **mGBA ⇄ RetroArch** (`.sav` ↔ `.srm`).
- **DuckStation ⇄ RetroArch** for PS1 (`.mcd` ↔ `.srm`), after a byte check on a real card.

RMG ⇄ RetroArch N64 needs a split/join converter, so it is listed but deferred. A standalone game and its RetroArch-core game
already group under one `GameTitle`, so the copy button shows up there.

### Phase 16 — Real-game checks (no code, always last)
Add a row per emulator to the hardware table, in the format of [[tasks/_done/emulator-saves/plan|Emulator saves]] Phase 18:
Deck plus Windows, with the neighbouring saves untouched. Check most carefully:
- RMG's MD5-suffixed names match across machines.
- Flycast's per-game VMU name.
- MAME seeds.
- ares states.
- RetroArch: a core switch on the Deck (mGBA→gpSP, Beetle→SwanStation), the *verify* cells of the core table, and
  Beetle's `.1.mcr` actually showing up.

### Phase 17 — Later: non-EmuDeck standalones
DeSmuME (`.dsv`), SameBoy, Snes9x/bsnes (`.srm`), FCEUX/Mesen, Project64 (header-name `.eep`) and BlastEm (per-ROM
folders). After Phase 4, each one is mostly a single `RomSaveRules` row.

## Every new reader also needs

- A row in `EmulatorSaves.Sources` (`src/Agent.Core/EmulatorSaves.cs`).
- Rows in `EmulatorProcesses.Rows`, so a pull is refused while the emulator runs, and in `SrmShortcuts.Launchers` (Flatpak id
  and exe).
- Its chip in the agent UI (`AddGamesView.tsx` `EMULATORS`, enforced by `EmulatorChipTests`) and in Game Mode.
- An icon mapping in `web/scripts/build-platform-icons.mjs`, from Arcticons where one exists. Regenerate both
  `platformIcons.generated.ts` files and `EmulatorIcons.g.cs`, and credit the icon in `NOTICE-icons.md`.
- New system labels in `GameSources.SystemLabel`, plus `EmulatorChipTests` InlineData: `naomi`, `atomiswave`, `jaguar`,
  `xbox` as needed.
- Fixture folders in `testenv emu-fixture` (`tests/testenv.ps1`) and `tests/linux/make-fixtures.py`.
- xUnit tests modelled on `MelonDsTests.cs`: EmuDeck and standalone folders, scopes, states-only games, neighbouring saves
  untouched.
- Its folders in `web/src/help/emulator-save-locations.md`, the source of truth for where Add games looks, and the emulator
  in `emulator-backups.md`'s list.
- A row in Phase 16.

## Verification (every group)

- `dotnet build --no-incremental` with the agent stopped, then `dotnet test tests/SaveLocker.Agent.Tests`. Each new reader's
  tests must fail with its `EmulatorSaves` row removed.
- `npm --prefix web run icons:check`. The agent-ui build and `gen:api -- --check` are unchanged, since no API change is
  expected (Phases 2 and 13 may add one; regenerate then).
- `testenv clean` → `emu-fixture` → the Windows test tray's Add games lists each fixture save with its chip, and Sync uploads a
  scoped archive. The Windows ↔ WSL round trip leaves the other games' files in the shared folder intact.
