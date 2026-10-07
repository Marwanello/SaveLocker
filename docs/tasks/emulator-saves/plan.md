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
| 2 — PCSX2 / Dolphin / DuckStation + shared-card warning | ⏳ Not started — Group C |
| 3 — Identity by folders, then `gamelist.xml` names | ⏳ Not started — Group B. **Revised 2026-10-07:** Phase 1 made the save file's name the game's identity, so a name from a machine-local source could split one game in two; matching a server game by its folders first (D1) makes such a name safe |
| 4 — PrimeHack | ⏳ Not started — Group C |
| 5a — RPCS3 | ⏳ Not started — Group D (split from Phase 5 2026-10-07) |
| 5b — Xenia | ⏳ Not started — Group E (split from Phase 5 2026-10-07) |
| 6 — Switch: Yuzu, Citron, Eden, Ryujinx | ⏳ Not started — Group F. **Widened 2026-10-07** from Eden only, at the maintainer's request; needs the server title key (D2) |
| 7 — UI filter | ✅ Shipped 2026-10-07 (same PR), filter by emulator — agent UI *Emulators* chip with an *Emulator* row under it (one chip per emulator, RetroArch today), Deck *Emulators* pill. A per-console sub-breakdown was not built — it comes back with Phase 2, when more than one console has saves. |
| 7b — Game source per machine, names without "(RetroArch)" | ✅ Shipped 2026-10-07 (same PR) — built 2026-10-07 (maintainer asked; picked variation B of a clickable mockup on all three screens). Unit 280. Console checked in the browser against a seeded dev server; agent UI and Deck need the testenv pass. **Review fixes 2026-10-07** (same PR): server-decided names, the scope-join refusal, source re-send guard, EmuDeck tag, server bounds — unit 291, see `progress.md`. |
| 8 — melonDS (bonus) | ⏳ Not started — Group B (added 2026-10-07) |
| 9 — Model 2 + Supermodel (Supermodel bonus) | ⏳ Not started — Group B (added 2026-10-07); Model 2 needs a capture first |
| 10 — ScummVM (bonus) | ⏳ Not started — Group B (added 2026-10-07) |
| 11 — PPSSPP | ⏳ Not started — Group D (added 2026-10-07) |
| 12 — Vita3K (bonus) | ⏳ Not started — Group D (added 2026-10-07) |
| 13 — shadPS4 (bonus) | ⏳ Not started — Group D (added 2026-10-07) |
| 14 — Cemu | ⏳ Not started — Group E (added 2026-10-07) |
| 15 — Azahar | ⏳ Not started — Group E (added 2026-10-07) |

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
| **B — Saves named after the ROM** | 3 (identity by folders), 8 melonDS, 9 Model 2 + Supermodel, 10 ScummVM | file per ROM |
| **C — Memory cards** | 2 PCSX2 / Dolphin / DuckStation + shared-card warning, 4 PrimeHack, per-console row | cards, file per game, title folders |
| **D — Sony (`PARAM.SFO`)** | 11 PPSSPP, 5a RPCS3, 12 Vita3K, 13 shadPS4 | folder per serial |
| **E — Title-ID folders** | 14 Cemu, 15 Azahar, 5b Xenia | folder per title ID |
| **F — Switch** | 6 Yuzu, Citron, Eden, Ryujinx + title key (D2) | folder per title ID + opaque index |

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

---

## Deferred, not built in this task

**Recovering ROM identity from a Steam ROM Manager shortcut's `LaunchOptions`** (structurally the
same problem `SteamShortcuts.MoonDeckAppId` already solves for MoonDeck) — needs a captured
`shortcuts.vdf` sample from a real SRM-configured, High-integration EmuDeck install first. This only
ever improves launch/exit lifecycle precision for that subset of installs; every integration level
and every standalone install is already fully covered for *detection* by the folder-tree scanning in
Phases 1–6, which never depends on Steam. Flag as a Backlog follow-on once a sample exists.

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
