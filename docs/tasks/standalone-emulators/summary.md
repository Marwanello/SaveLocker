# More standalone emulators — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]). No plan
file yet. **Priority: Low.** Separate from [[tasks/_done/emulator-saves/plan|Emulator saves]] (whose remaining work
is Switch, Groups G–I) because these are mostly cheap: a ROM-named save in a shared folder is one more row of
`RomSaves` (`RomSaveRules`), the D4 "shape 1" reader.

---

## Candidates

From OpenSave's preset table (`internal/presets/presets.go`, MIT) and Hoard's catalogue
(`crates/hoard-agent/src/emulators.rs`, AGPL — paths are facts, not code), minus what SaveLocker already reads:

| Emulator | System | Save shape | Notes |
|---|---|---|---|
| Flycast | Dreamcast | VMU files, per game or shared | Ships with EmuDeck; check whether VMUs are per game before adding (shared → `NotSyncable`, like PCSX2's shared card) |
| mGBA | GB/GBC/GBA | ROM-named `.sav` | Ships with EmuDeck |
| xemu | Original Xbox | Saves inside the HDD image (`xbox_hdd.qcow2`) | **Probably not syncable per game** — record as not supported rather than syncing the whole disk image |
| Lime3DS | 3DS | Same layout as Azahar | A folder-root row for the existing Azahar reader |
| Project64, Mupen64Plus, simple64 | N64 | ROM-named `.eep`/`.sra`/`.fla`/`.mpk` | |
| DeSmuME | DS | ROM-named `.dsv` | |
| SameBoy | GB/GBC | ROM-named `.sav` | |
| Snes9x, bsnes | SNES | ROM-named `.srm` | |
| FCEUX, Mesen | NES | ROM-named `.sav` | |
| BlastEm | Genesis | per-ROM folders | |
| MAME, ares, BigPEmu | various | to research | In EmuDeck's list; neither competitor covers them |

## Rules

- **EmuDeck first** (the standing design rule): start with the emulators EmuDeck installs, on its folder
  layout, on the Deck and on EmuDeck for Windows; standalone installs are a bonus. Check EmuDeck's current
  emulator list before picking the order.
- Each new emulator gets its chip in the Add Games *Emulators* row (agent UI and Game Mode).
- Each new emulator joins Group H's real-game checks.

## Touch points

`src/Agent.Core/RomSaves.cs` (rows), `EmulatorSaves.cs` (the table), `EmulatorPaths.cs`, `ConsoleTitles`
only if a system needs serial naming, agent-ui `AddGamesView.tsx` chips, `tests/linux/make-fixtures.py`.
