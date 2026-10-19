# Session summary — 2026-10-18/19 — Emulator saves Groups G, I and J (PR #78)

Standalone: the facts below do not need the rest of the vault.

**PR:** [#78 on `Marwanello/SaveLocker`](https://github.com/Marwanello/SaveLocker/pull/78), branch `emulator-saves-group-.g-i-j`,
base `main`. One commit per phase: `d07d43d` Phase 19, `06dd0b6` Phase 20, `c3fde8f` Phase 17 (+ merge), `d2f3802` Phase 21,
then a testenv fixture commit and this Docs commit. Agent tests **721, all passing**; `web` and `agent-ui` build and lint clean.

## Decisions taken with the maintainer before building
- Phase 17: **same emulator only**; a **file-by-file name list** per machine that **grows by itself** for new files; links made
  **from the agent only**; a **clash refuses the link**; the **server merge built now**, from the **console only**.
- Phase 20: a shortcut is the emulator's game when it starts a **known emulator/launcher with a ROM path** (SRM's records count
  too); a ROM with no save is **listed as "No save here yet"** where its save's place is known; the Deck was captured read-only.
- Decided here: Cemu, RPCS3 and ScummVM use their console's glyph until their logos' terms are checked; PrimeHack uses Dolphin's.

## What was built — in plain terms
- **States-only games (Group I).** If you've only ever used save states in RetroArch, melonDS, PCSX2, DuckStation, Dolphin,
  PrimeHack or Azahar, the game now shows in Add games ("Save states only so far"). Adding it syncs the states now and the
  real save later, as soon as the game makes one.
- **Steam ROM Manager shortcuts (Group G, Phase 20).** EmuDeck puts every ROM into Steam as a shortcut; SaveLocker used to
  list those as separate games (Forza Horizon twice on the Deck). Now the shortcut is recognised as the emulator's game: it
  isn't listed on its own, and launching the game from Steam syncs it before and after you play. A game you already track
  twice gets a "Same game as …" notice with **Stop tracking this copy**.
- **Different file names (Group G, Phase 17).** If one machine has a different dump of a ROM (e.g. "(Rev 1)"), you can now
  link its save to the same server game: Add games → **Change** → *Different file names*. Each machine keeps its own file
  names; SaveLocker renames on the way to and from the server. The first sync asks which save to keep.
- **Merging two games (Group G).** On a game's console page, **Merge** folds a duplicate into another game after a preview
  and typing its name. Its old saves are kept as read-only history; machines move over by themselves where possible.
- **Icons (Group J).** Every emulator and console now has a small icon in the console, the agent's pages and the Deck's
  Add games list.

## What was built — technically
- **Phase 19:** `RomSaveRules.StateRom` (RetroArch `<rom>.state*`, melonDS `.ml1–8`, per-core state folders) in `RomSaves`;
  `Pcsx2Saves` (`<serial> (<CRC>).NN.p2s`, first folder card, `SavePrefix` BA/BE/BI); `DuckStationSaves` (`<serial>_N.sav`,
  card under the bundled table's title or the serial); `DolphinSaves.ForGameId` (GC Slot A region folder / Wii `title`);
  `AzaharSaves` (`<program ID>.NN.cst`). Candidates carry `StatesOnly`. Tests: `StatesOnlyTests`, `GroupISyncTests`.
- **Phase 20:** `SrmShortcuts` — `Recognize` (raw `Exe` + launch options, nested quotes, Xenia `Z:` paths; emulator by
  launcher/Flatpak name, never the ROM's), `SrmAppIds` (`userdata/*/config/addedItemsV2.json`, app ID = key >> 32), `Apply`
  (drops the shortcut row, puts `ShortcutAppIds`/`SteamAppId` on the emulator row, matched by Switch title ID, NCCH program ID,
  GC/Wii disc ID incl. `.wbfs`/`.rvz` headers, ROM name, else cleaned name), `NoSaveYet`, `Backfill`. `SteamShortcut` keeps
  `RawExe`/`LaunchOptions`/`Tags`. `TrackedGame.ShortcutAppIds` + `LaunchesUnder` (launch wrapper, launch-option rows).
  `TrackedGameDto.SameGameAs`, `doctor` check. Tests: `SrmShortcutTests` (the Deck's shapes verbatim + gated full capture).
- **Phase 17:** `Shared/FileNames` (`FileNameDto`, `ToServer`/`ToLocal`/`Derive`/`Validate`); `SaveRoot.Names`; `SaveArchive`
  swaps in `EnumerateItems` (scope/hash/manifest/archive under server names), delta subsets and restore (local names written,
  delete pass mapped back). Agent: `TrackedGame.OwnFileNames`, `OwnFileNames.Grow` on every push/pull, `EnrollLinks.RenamePairs`
  + choice `rename`, `Enroller` join path + clash refusals, `TrackedFor` via server names. Server: `PUT
  /api/agent/games/{id}/file-names` (`MachineSavePath.FileNames`); merge `GET /api/games/{id}/merge-preview?into=`, `POST
  /api/games/{id}/merge` (`SyncService.MergeGamesAsync`: versions moved, protected, `SaveVersion.MergedFrom`, `SetHeadAsync`
  refuses them; same-layout machines' paths/sources moved; `Game.MergedGameIds`), poller re-points and clears an opt-out
  (`SetTracked`). Migration `AddFileNamesAndMerge`. UIs: agent game page lists own names; console machine rows list them,
  `MergeCard`, merged versions labelled. Tests: `FileNamesTests`, `GroupGSyncTests` (mutation-checked).
- **Phase 21:** `web/scripts/build-platform-icons.mjs` (`icons:build`/`icons:check`) → `platformIcons.generated.ts` in both
  UIs + `src/Agent.Linux/Ui/EmulatorIcons.g.cs`; `PlatformIcon` component + `platformIcon.ts` helpers; `Icons.Emulator` on the
  Deck; `SvgPath` gained S/Q/T and elliptical/rotated arcs. Credits `NOTICE-icons.md`.

## How to verify with testenv (step by step)
1. `.\tests\testenv.ps1 sync`, then `.\tests\testenv.ps1 build` (the WSL/Deck agents build from the WSL clone).
2. **Start from a clean rig:** the Windows test agent still maps Animal Crossing to the real `D:\Emulation\...` from the
   Group F check, and starting it could pull the test console's copy over it. Run `.\tests\testenv.ps1 clean` first (it
   deletes only the rig's own state), then step 1's `build` again.
3. `.\tests\testenv.ps1 emu-fixture` — from a normal PowerShell terminal (from the Claude app the WSL half never reaches WSL;
   see Gotchas → testenv).
4. `.\tests\testenv.ps1 up` (console :5080, Windows agent :5188, WSL agent :5187).
5. **Group I:** on both agents, Add games → All → *Super Mario World* says "Save states only so far". Add it on Windows, Sync,
   then add it on WSL: it joins the same game and WSL gets Windows' `.state1`.
6. **Group G, own file names:** on Windows add *Earthbound* (`Earthbound (USA).srm`) and Sync. On WSL, Add games → *Earthbound*
   → **Change** → pick Windows' "Earthbound" (*Different file names*) → Add. Sync on WSL: a conflict appears — keep the cloud's.
   WSL's `Earthbound (USA) (Rev 1).srm` now has Windows' content; WSL's game page lists its own names; the console's game page
   shows them under the WSL machine. Make a new state on WSL (`… (Rev 1).state3`), Sync, then Pull on Windows:
   `Earthbound (USA).state3` appears.
7. **Merge:** add *Zelda* on WSL with **Change → Keep it as its own game**, Sync. In the console open that copy → **Merge** →
   pick the other Zelda → read the preview → type the name → Merge. Its version shows "From …" in the kept game's Versions
   with no *Set as Latest*; WSL's agent shows the game under the kept name after its next poll.
8. **Group J:** console Games → Group: Platform (console glyphs on headings, emulator icons on sub-headings), a game's source
   and release chips, machine rows; agent Add games chips and rows, the Games wall (console badge), a Switch game's emulator
   picker. `npm --prefix web run icons:check` → "54 icons, all drawn".
9. **Group G, SRM shortcuts (Deck):** `.\tests\testenv.ps1 build -Only deck; up -Only deck` with the Deck awake. In Game Mode
   Add games, *Forza Horizon* is listed once, as Xenia's game, with the Xenia icon. Add it, launch it from its Steam shortcut,
   and check `agent.log` for `[run] launch: appid=2777482215` followed by the pull and, on exit, the push.

## Verified in this session
Unit/integration tests (721); the Deck's real `shortcuts.vdf` through the recogniser; testenv console + WSL agent: the
states-only row, icons in agent Add games and the console's grouped list/grid/source chips/machine rows, the merge preview.
**Not verified here:** steps 6–7 between two agents (the Windows test agent was left down because of the real-save mapping),
step 9 on the Deck (asleep), screenshots in the light theme.

## Next
Run steps 2–9, then merge PR #78. Group H (real-game checks) stays open.
