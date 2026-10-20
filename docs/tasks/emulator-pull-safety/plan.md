# Emulator pulls: unformatted PCSX2 cards, a running emulator, and games never played here

Found 2026-10-19 during the Group H check of PCSX2 (`tasks/emulator-saves/plan.md` → *Phase 18*): *Prince of Persia:
Warrior Within* (`SLES-52822`) was synced from the Deck to Windows. The save's four files arrived byte-for-byte, but
PCSX2 on Windows showed no save. Two separate faults are behind it, and each gets its own fix below.

Found 2026-10-20 in the DuckStation and PPSSPP checks: *Tomb Raider: The Last Revelation* (DuckStation) and *God of
War: Chains of Olympus* (PPSSPP), saved and state-saved on Windows, never reached the Deck. That is a third fault,
Phase 5, plus a DuckStation naming gap, Phase 6.

## Status

| Phase | Status |
|---|---|
| 0 — Deck test rig: stop hiding `~/.local/share` from the emulator readers | ✅ Shipped 2026-10-20 — redeployed to the Deck; a rescan lists shadPS4's *Dark Souls: Remastered* and *Career*, and Azahar's states at `~/.local/share/azahar-emu/states` |
| 1 — Capture PCSX2's formatted superblock | ✅ Shipped 2026-10-20 — `Ps2Superblock.Formatted8Mb()` built field by field, byte-equal to the superblock a game wrote on the Deck's `PS2MC-1_converted.ps2` (8 MB, the test fixture) |
| 2 — Pull into an unformatted folder card | ✅ Shipped 2026-10-20 — formatted after a pull, on the next scan and at the start of a sync; skipped while PCSX2 runs. Not built: a warning on the Add games row and in `doctor` (the scan fixes the card instead) |
| 3 — No pull while the game's emulator is open | ✅ Shipped 2026-10-20 — `EmulatorProcesses` (every emulator, both OSes, Xenia and Switch folded in); `GameActivity.IsActive` waits while a game's emulator runs; `TrackedGame.Emulators` from the server |
| 4 — Help, testenv, Group H re-check | ⏳ Not started |
| 5 — Set up a server emulator game on a machine that has no save for it yet | ✅ Shipped 2026-10-20 — `EmulatorPlaces.CandidateFor` (PCSX2, DuckStation, PPSSPP, melonDS, Supermodel; ROM by title, or the exact ROM for ROM-named saves) and a same-files scan row, enrolled by the poller once a minute (`SetUpEmulatorGamesAsync`). Not built: the other emulators' places, ROM identity reads (title IDs, product codes), Model 2 |
| 6 — DuckStation: a states-only game's card under DuckStation's own title | ✅ Shipped 2026-10-20 — a states-only game under a known title also scopes `<title> (*)_1.mcd`/`_2.mcd`. Not built: rescoping a game already on the server (its scope is fixed once made; the PC's *Tomb Raider* keeps its old scope until it is re-added or its patterns are edited in the console) |
| 7 — shadPS4: name a game by its installed title, not the save's own label | ✅ Shipped 2026-10-20 — the installed game's `TITLE` (EmuDeck's `storage/shadps4/games` and `config.toml` `installDirs`), else `MAINTITLE` without the save label and region tag. The server game keeps its old name until renamed |
| 8 — Another dump of a ROM: join a revision, name an edition | ⏳ Not started — A and B decided 2026-10-20 |

## What happened (evidence)

- **Windows card `D:\Emulation\saves\pcsx2\saves\mc1.ps2`**: `_pcsx2_superblock` is **0 bytes**. The Deck's
  `PS2MC-1_converted.ps2` superblock is 8192 bytes. The card was created at 17:54, as a new folder card rather than one
  converted from a file card.
- **PCSX2 source** (`MemoryCardFile.cpp`, `FileMcd_CreateNewCard`): a new folder card is a directory plus an **empty**
  `_pcsx2_superblock`. A game formats the card on its first save, and only then does `FlushSuperBlock` write the file.
- **PCSX2 source** (`MemoryCardFolder.cpp`, `LoadMemoryCardData`): the card counts as formatted only when the
  superblock reads in full and `raw[0x16] == 0x6F` (the `o` of "Format"). When it doesn't, `AddFolder` is never
  called, so **every save folder on that card is ignored**. Folders are read only when the card is opened (at VM
  start), so files that arrive while a game runs aren't seen until the next boot.
- **The pull itself was correct**: SaveLocker's scope is `BESLES-52822*/**`, and the card's superblock is outside
  it on purpose, because it belongs to the card and not to any one game.
- **PCSX2 was open during the pull.** `pcsx2-qtx64` started at 17:54:21, and the force-pull wrote the card at
  17:57:46. `GameActivity.IsActive` checks `TrackedGame.ProcessNames`, which is empty for every emulator game, so the
  running-game check could never catch it.
- **States:** the Deck has no PCSX2 states (its states folder is empty), so none were missing. The Windows
  `SLES-52822 (105CC366).01.p2s` saved at 17:56 was deleted by that force-pull, which is expected because
  force-pull discards local changes. PCSX2's own `.01.p2s.backup` survived.

## Phase 0 — Deck test rig: stop hiding `~/.local/share` from the emulator readers

**What happened (2026-10-20).** The Deck test daemon (`~/savelocker-test/savelocker daemon --port 5177`) runs with
`XDG_DATA_HOME=/home/deck/savelocker-test-state`, which `testenv-deck.sh` exports to keep the test agent's state apart
from a real install. Nine readers use `EmulatorPaths.XdgData` (Azahar, Cemu, Dolphin, DuckStation, shadPS4, the Switch
emulators, Vita3K, Xenia), so the test daemon never looks in the real `~/.local/share`. A rescan through its local
API returned 265 rows and no shadPS4 game, though `~/.local/share/shadPS4/home/1000/savedata/CUSA08495` holds the
save.

**What it means for Group H so far.** Deck results are valid for saves found through EmuDeck's `Emulation/saves`
links, which is how those that passed were found. Anything kept only under `~/.local/share` was never seen on the
Deck rig: shadPS4's new layout (this failure), **Azahar's states** (`~/.local/share/azahar-emu/states`), Cemu's
title cache (why the Wii U row showed its ID), and a standalone DuckStation/Dolphin/Xenia. Re-check Azahar's states
on the Deck after this fix.

**Fix.** The agent already has `SAVELOCKER_STATE_ROOT` (`AgentConfig.StateRoot`, test-only, any OS). `testenv-deck.sh`
exports it instead of `XDG_DATA_HOME`: the daemon line, the desktop entry's `Exec`, the `run`/state checks (line 83),
and `cmd_conflict`'s folder. `Program.cs`'s `dev-shortcut-add` accepts either variable. Then `build -Only deck` and
`up -Only deck`, and a rescan lists *Dark Souls: Remastered*.

## Phase 1 — Capture PCSX2's formatted superblock

Per the rule to reverse-engineer formats from real tools: on Windows, create a new 8 MB folder card in a **scratch**
memory-card folder, boot any PS2 game, and let it format the card and save. Keep the `_pcsx2_superblock` it writes
as a test fixture. Also keep the Deck's converted-card superblock as a second reference. Compare the two, then write
`Ps2Superblock.Formatted8Mb()`: the PS2 superblock (magic `Sony PS2 Memory Card Format 1.2.0.0`, page 512,
2 pages per cluster, 16 per block, 8192 clusters, `alloc_offset` 41, `alloc_end` 8135, root dir cluster 0, backup
blocks 1023/1022, IFC list, bad-block list `-1`, card type 2, flags), padded the way PCSX2 pads it. The unit test is
byte equality with the captured fixture.

## Phase 2 — Pull into an unformatted folder card

1. `MemoryCards.FolderCardFormatted(dir)`: the superblock exists, is at least 0x17 bytes long and starts with the
   magic. This mirrors `IsFormatted`, made a little stricter. `IsFolderCard` keeps its meaning, and the scanner still
   lists such a card's games.
2. `Pcsx2Saves.AfterPull(game, log)`, called next to `SwitchCopies.AfterPull` in `SyncEngine.PullAsync`. If the
   game is PCSX2's, its `SaveDirectory` is a folder card, and that card is **not** formatted, write
   `Formatted8Mb()` to it atomically (temp file, then rename) and log
   `formatted PCSX2 card "<card>" so it shows the pulled save`. A formatted superblock is **never** overwritten. A
   file card is never touched. Since Phase 3 refuses the pull while PCSX2 is open, this can never race PCSX2's own
   flush.
3. A card that already holds save folders but is unformatted (a pull that landed before this fix) is fixed
   without asking (Q2): on the next scan, and again on the game's next sync if the scan didn't fix it (PCSX2 was
   open, or the write failed). Both skip the card while PCSX2 is running (Phase 3's check) and log what they did.
   Until it's fixed, the game's Add games row and `doctor` warn: *PCSX2 can't see the saves on this card until it's
   formatted.*
4. `MemoryCards.Pcsx2Fix`: add that creating a new folder card also works, and that SaveLocker formats it when it
   pulls a save onto it.

**Q1 — decided 2026-10-19: SaveLocker formats the card.** The question was whether SaveLocker writes the
superblock itself. The alternative is only warning and telling the user to save once in any PS2 game first. Writing it is safe because
PCSX2 rebuilds the card's FAT from the folders on every open, and it treats a missing or empty superblock as "never
formatted", so nothing on the card is lost. Warning only leaves the save invisible until the user acts. And if a game
formats the card while the pulled save already sits on disk, the game's own save lands in that same `BESLES-…`
folder.

**Q2 — decided 2026-10-19: fix it on the next scan, and on the next sync if the scan didn't.** No button. This
covers an unformatted card that already holds pulled saves, like the Windows PC's `mc1.ps2` today.

Tests: `Formatted8Mb` matches the fixture; the scan and the sync each fix an existing unformatted card that holds
saves, and skip it while PCSX2 is open; `AfterPull` writes into a 0-byte, missing or `0xFF` superblock; it leaves
a formatted one and a file card alone; it does nothing for non-PCSX2 games; the write is atomic. Mutation-check the
`0x16` test.

## Phase 3 — No pull while the game's emulator is open

1. `EmulatorProcesses`: one table from emulator name (`ScanCandidate.EmulatorName` / `Source.Detail`) to its process
   names on both OSes, using the Linux 15-character name and the Proton `.ex` truncation that
   `XeniaLaunchProfile.IsXeniaProcessName` and `SwitchCopies.IsProcessOf` already handle. Fold those two into it.
   PCSX2: `pcsx2-qtx64`, `pcsx2-qtx64-avx2`, `pcsx2-qt` (AppImage and Flatpak). Fill in the other emulators from the
   EmuDeck installs captured on both machines in Groups A–F, and add a row with each new emulator, the same way the
   emulator chip row grows.
2. `GameActivity.IsActive` also returns true when the game has an emulator source and that emulator's process is
   running. `RefusalMessage` names it: *PCSX2 is open. It keeps the memory card in memory, so a save restored now is
   not seen and can be overwritten. Close PCSX2 and pull again.* Every existing caller (pull before the download and
   just before the restore, the CLI, the command poller, Restore/Use this save) gets this check with no other change.
3. `ProcessNames` stays empty for emulator games. Putting the emulator in it would make `ProcessWatcher` treat
   "PCSX2 closed" as "every PS2 game exited" and push them all.
4. The Linux launch wrapper (`savelocker run -- %command%`) and the Playnite pre-launch pull happen before the
   emulator starts, so they still pull.

**Q3 — decided 2026-10-19: block the pull for every game of an open emulator.** The question was whether to block
every game, as proposed. It's simple, and it's right
for card-based emulators (PCSX2, Dolphin's GCI folders, DuckStation), which hold the whole card in memory. The
alternative blocks only the card-based ones and lets RetroArch, where a game's `.srm` is per ROM, pull while its
menu is open. That is looser, and RetroArch also writes the `.srm` on close.

Tests: name matching per OS (full, 15-char, Proton `.ex`, AppImage suffix); `IsActive` true for a PCSX2 game while a
`pcsx2-qtx64` process exists, false for a RetroArch game at the same time; the refusal text; Xenia and Switch keep
their current behaviour after the fold-in.

## Phase 4 — Help, testenv, Group H re-check

- Help: the PCSX2 article gets "a new folder card is formatted when the first save is pulled onto it" and
  "close the emulator before pulling".
- testenv: a PCSX2 fixture card with a 0-byte superblock on the Windows side. The flow pulls onto it and asserts the
  superblock is formatted.
- Group H: re-run *Warrior Within* Deck → Windows with PCSX2 closed. Expect the save in PCSX2's memory-card
  manager and in game. Then open PCSX2 and confirm a pull is refused with the new message.

## Phase 5 — Set up a server emulator game on a machine that has no save for it yet

**What happened (2026-10-20).** Windows enrolled and pushed both games. The Deck has both ROMs (`roms/psx/Tomb Raider -
The Last Revelation.chd`, `roms/psp/God Of War - Chains Of Olympus.iso`) and both emulators, but logged *"… was added
on the server — set its save folder in Settings"* and has never pulled either game. Its DuckStation `saves`/`states` and
PPSSPP `SAVEDATA`/`PPSSPP_STATE` are empty.

**Why.** `CommandPoller` adopts a server game it doesn't track yet with the folder from `ResolveSaveDirAsync`: this
machine's stored path, else the server's `suggestedSaveDir` (a template or a literal path that exists here), else
Ludusavi detection. For an emulator game all three are empty. The server reports `suggestedSaveDir: null` and
`saveTemplates: null`, because an EmuDeck path (`D:\Emulation\…`, `/run/media/deck/SDCard/Emulation/…`) has no token
to describe it, and the manifest has no emulator games. The scan can't help either: it lists a game only once this
machine has its save (or a state, Phase 19). So a game played on one machine only never reaches the others. This
affects every emulator, not just these two. The Deck → Windows checks so far passed only because the receiving machine
already had a save of its own.

**Fix.** In `ResolveSaveDirAsync`, before detection: when the server game names exactly one emulator
(`GameDto.Emulators`), that emulator's reader finds a setup on this machine, **and the game's ROM is on this machine**
(Q4), the game's folders are that setup's.
- **The ROM check**: look in that emulator's ROM folders (EmuDeck's `roms/<system>`, a standalone install's game paths,
  one level down as the readers already do). A ROM is the game's when it carries the game's identity and the reader can
  read it: the Switch title ID (`SwitchEmulators`' ROM scan), the 3DS product code (Azahar's header read), a Wii U
  folder's `meta.xml` `title_id`, a PSP ISO's `PARAM.SFO` disc ID. Otherwise, match by name: the file's
  `RomNames.CleanTitle` equals the server game's name, or the ROM name the scope is built from (RetroArch, melonDS,
  ScummVM, Supermodel, Model 2 scope by the ROM's own base name). Both of 2026-10-20's games match by name
  (`Tomb Raider - The Last Revelation.chd`, `God Of War - Chains Of Olympus.iso`; compared ignoring case).
- No ROM found: the game stays unmapped as today, with the same *"set its save folder in Settings"* notice. Once
  its ROM is added, the next poll (every 20 s) sets it up.
- `EmulatorSaves.PlaceFor(emulator, release, includeGlobs)` → the primary folder and the extra folders (`states`, and
  any other key the reader declares), real paths, EmuDeck's setup first. Each reader answers from the same `Folders(…)`
  it scans with. Shape 1 (a file named after the ROM) and shape 2 (title-ID folders) are the emulator's shared folder.
  The scope (`includeGlobs`) already comes from the server and picks out the game's files.
- Readers whose place depends on more than the emulator: PCSX2 (this machine's first folder card; with only a file
  card, leave it unmapped and report the convert-to-folder fix), Switch (the existing title key and device/user
  alignment), Xenia (the profile slice), Vita3K and shadPS4 (user `00` / `1000`). Each uses the rule its *No save here
  yet* row already uses where one exists.
- Then the normal path takes over: the first pull writes the fleet's save into the emulator's folder (Decisions.md:
  a fresh machine takes the fleet's save without playing first). `ReconcileFoldersAsync` maps the `states` folder
  the same way.
- Every result still goes through `Safe()` (`SavePathGuard`, folder claims).
- **A game adopted before its first save here** (found 2026-10-20, shadPS4 *Dark Souls: Remastered*). The Deck took the
  server game at 11:34 with no folder, because it had no save yet and the PC's template
  (`<winAppData>/…/home/1004/savedata`) means nothing on Linux. For a tracked, unmapped game the scan only *suggests*
  a folder to the console (`UpdatePathCandidatesAsync` → `PathCandidateFor`), and only for a row with **the same
  name**. Nothing maps it automatically, though Add games still lists the row (an adopted game isn't
  `IsEnrolledHere`), and adding it joins by files. Fix: for an unmapped emulator game, a scan row from the same
  emulator with the same files (`Enroller.SameFiles`, D1) maps the game's folders automatically, the same way
  enrolling that row would join it. This match goes by files, not name, so it survives a renamed game (Phase 7). The
  name-only match stays for non-emulator games.
  - **Correction, same day:** this was not why the Deck showed no shadPS4 save at all. The Deck **test rig** runs
    the daemon with `XDG_DATA_HOME=~/savelocker-test-state` (`testenv-deck.sh`, to keep its state apart), and
    `EmulatorPaths.XdgData` reads `XDG_DATA_HOME` as the base of `~/.local/share`. So the test daemon looked for
    shadPS4 in `~/savelocker-test-state/shadPS4` and found nothing. A real install, with no such variable, is not
    affected. See *Phase 0*.

**Q4 — decided 2026-10-20: set it up automatically only where the emulator is installed and the game's ROM is
present.** The alternatives were every machine with the emulator installed, or listing the game in Add games for the
user to add by hand.

Tests: a server DuckStation/PPSSPP/PCSX2/Cemu game is adopted with the right folders and scope on a machine with no save;
it stays unmapped when the emulator isn't installed or the game names two emulators; PCSX2 with only a file card stays
unmapped with the fix message; a folder claimed by another game here stays unmapped; **no ROM → unmapped**, and the
game is set up on the first poll after its ROM appears; a ROM matched by identity beats a different ROM with the same
name; name matching ignores case and the extension.

## Phase 6 — DuckStation: a states-only game's card under DuckStation's own title

**What happened.** Windows enrolled *Tomb Raider* from its states alone (its card had no save yet), so the card was
declared under the bundled table's title, scope `Tomb Raider - The Last Revelation_{1,2}.mcd`. DuckStation names the card
after its own game database's title, which here includes the region: `Tomb Raider - The Last Revelation (Europe,
Australia)_1.mcd`. When the game is saved in-game, that card stays outside the scope and never syncs. This is the case
`StatesOnlyCandidate`'s comment already expects (*"When DuckStation picks another name… Change links it"*), but it
happens silently.

**Fix.** Two parts:
- A states-only DuckStation game also scopes `<title> (*)_1.mcd` / `_2.mcd`, since DuckStation's database titles add the
  region in parentheses. `<title> (` keeps *Tomb Raider II* out of *Tomb Raider*'s scope.
- On a later scan, when a card in the setup carries one of the game's codes but falls outside its scope, rescope the
  enrolled game to that card's real name, the same as a first in-game save of any other states-only game.

Two machines naming the same card differently is Phase 17's case (each machine's own file names, PR #78).

## Phase 7 — shadPS4: name a game by its installed title, not the save's own label

**What happened (2026-10-20).** *Dark Souls: Remastered* (`CUSA08495`) is listed as **"DARK SOULS: REMASTERED Savedata
(EU)"**. That's the save's `sce_sys/param.sfo` `MAINTITLE`, which the game writes itself; a PS4 game is free to put
anything there. The installed game's own `param.sfo` (`D:/Emulation/storage/shadps4/games/CUSA08495/sce_sys/param.sfo`)
has `TITLE` = "DARK SOULS™: REMASTERED".

**Source — decided 2026-10-20 (maintainer): the installed game's `param.sfo` `TITLE`, then the cleaned save label.**
A bundled title list like PS1/PS2's was looked for first. None we may ship exists: Redump and No-Intro have no PS4
set (libretro-database's `no-intro` folder has PSP and Vita only); Apollo Save Tool's game list is GPL-3.0, ruled out
by the same rule as PCSX2's `GameIndex.yaml` (Decisions.md, `ConsoleTitles`); psdevwiki's *Game Titles* table
(checked offline 2026-10-20) is GNU FDL 1.2 and far too thin: about 420 rows with only ~360 `CUSA` IDs (PS4 has
thousands, several per game by region), and *Dark Souls Remastered*'s row has no ID at all. The installed game's `TITLE` is Sony's own name for the game (what the
PS4 menu shows), and a save is only made on a machine where the game is installed, so the first machine to enrol a
game always has it.

**Fix.**
1. Name order: the installed game's `TITLE` first, then the save's `MAINTITLE` with a trailing save label and region
   tag removed (`Savedata`, `Save Data`, `SAVE DATA`, `(EU)`, `(US)`, `(JP)`, `(ASIA)`), then the title ID.
   Installed games are found in shadPS4's `config.toml` `[GUI] installDirs` and EmuDeck's
   `Emulation/storage/shadps4/games`, at `<dir>/<CUSA…>/sce_sys/param.sfo` (a `CUSA…-patch` folder is an update, not a
   name).
2. The server game keeps the name it was created with. Rename it in the console if wanted; the Deck joins it by files.

Tests: the installed title wins; the save label and region tag are stripped when no install is found; a patch folder
names nothing; the existing `MAINTITLE` fixture still reads.

## Phase 8 — Another dump of a ROM: join a revision, name an edition

**What happened (2026-10-20).** The PC saved *Metroid Prime - Hunters* in melonDS and RetroArch from
`Metroid Prime - Hunters (USA) (Rev 1).nds`. The Deck has `Metroid Prime - Hunters (USA).nds`, the first release.
The save files have different names (`…(USA) (Rev 1).srm` and `…(USA).srm`), so D1 (same files) did not join them.
The Deck's RetroArch save became a third game. Its name came from the third step of `Enroller.NamesFor`:
1. The clean title, `Metroid Prime - Hunters`: taken (PC, melonDS).
2. The title plus the emulator, `Metroid Prime - Hunters (RetroArch)`: taken (PC, RetroArch, Rev 1).
3. The ROM's file name plus the emulator, `Metroid Prime - Hunters (USA) (RetroArch)`: this one.

This was working as designed: joining another dump is done by hand in Add games' **Change** (Phase 17 of
emulator-saves; Decisions.md 2026-10-18). Two things read badly, though:
- The name shows `(USA)`, which both dumps share, and leaves out `Rev 1`, the one real difference.
- Which dump gets the short name depends only on which machine added it first.

**A — Name the edition, not the ROM (decided 2026-10-20).** Step 3 of `NamesFor` becomes the title, the emulator,
and only the tags that tell one dump from another: `Rev N`, `vN.N`, `Beta`, `Proto`, `Demo`, and the region. Hash
and quality tags (`[!]`, `[b]`) and language lists (`(En,Fr,De)`) are dropped. A ROM with no revision tag is the
first release, which No-Intro leaves untagged, so it is called `Rev 0` when another tag is needed to tell it apart.
- The Deck's save would be `Metroid Prime - Hunters (RetroArch, Rev 0)`. A European dump would be
  `Metroid Prime - Hunters (RetroArch, Europe)`.
- The name still comes from the save file alone, so every machine walks the same list (`NamesFor`'s rule).
- A server game keeps the name it was made with. The old long names stay until they are renamed in the console.

**B — Join a revision automatically (decided 2026-10-20).** The automatic choice (`EnrollLinks.For`'s `auto`, through
`Enroller.ServerNameFor`) becomes Phase 17's **rename** link when **all** of these hold:
- The server game's emulators include this save's emulator.
- No region differs: neither the release (`OtherRegion`) nor the ROM names' region tags.
- The two ROM names are the same once revision, version and hash/quality tags are removed. For example,
  `Metroid Prime - Hunters (USA)` and `Metroid Prime - Hunters (USA) (Rev 1)` are both `Metroid Prime - Hunters (USA)`.
- `RenamePairs` can pair every file, states included.

Anything else (another region, a hack, a Beta, a different emulator) stays a new game, and **Change** can still link it
by hand where Phase 17 allows. Add games shows the automatic link as *Joins “Metroid Prime - Hunters (RetroArch)” —
another revision* with the file names, and **Change → Keep as its own game** still works. The first sync asks which of
the two saves to keep, as a manual Phase 17 link does. This amends Decisions.md 2026-10-18 ("A link is made in Add
games"): a revision is linked automatically, everything else stays by hand. Record that in Decisions.md when it ships.

**Fix.**
1. `RomNames`: `EditionTags(romBase)` (revision, version, Beta/Proto/Demo, region) and `SameRelease(a, b)` (equal once
   revision, version and hash tags are removed).
2. `Enroller.NamesFor`: step 3 uses `EditionTags`, with `Rev 0` for an untagged first release. A ROM with no edition
   tags at all keeps today's step 3.
3. `Enroller.ServerNameFor` and `EnrollLinks.For`: with no D1 match, a server game that meets B's four rules is the
   automatic choice, linked as `Rename`. D1 still wins when it matches.
4. Agent UI: the automatic join's badge reads *Another revision*.

**Tests.** `Rev 1` on the server with an untagged save here auto-links with file pairs; `(USA)` against `(Europe)` stays
apart; another emulator's revision stays apart; a revision whose states can't be paired stays a new game; step-3 names:
`(RetroArch, Rev 0)`, `(RetroArch, Europe)`, and `[!]` and the language lists dropped; D1 still wins over a revision match.

**Verify (testenv).** On the Deck, stop tracking `Metroid Prime - Hunters (USA) (RetroArch)` and rescan. Add games
shows *Joins “Metroid Prime - Hunters (RetroArch)” — another revision*. Add it, sync, and keep the Deck's save when
asked. melonDS's `(USA).sav` joins `Metroid Prime - Hunters` the same way.
