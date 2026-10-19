# Emulator pulls PCSX2 can't see, and pulls under a running emulator

Found 2026-10-19 during the Group H check of PCSX2 (`tasks/emulator-saves/plan.md` → *Phase 18*): *Prince of Persia:
Warrior Within* (`SLES-52822`) was synced from the Deck to Windows. The save's four files arrived byte-for-byte, but
PCSX2 on Windows showed no save. Two separate faults are behind it, and each gets its own fix below.

## Status

| Phase | Status |
|---|---|
| 1 — Capture PCSX2's formatted superblock | ⏳ Not started |
| 2 — Pull into an unformatted folder card | ⏳ Not started — Q1, Q2 decided 2026-10-19 |
| 3 — No pull while the game's emulator is open | ⏳ Not started — Q3 decided 2026-10-19 |
| 4 — Help, testenv, Group H re-check | ⏳ Not started |

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
