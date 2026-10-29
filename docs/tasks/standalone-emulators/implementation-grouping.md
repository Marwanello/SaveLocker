# Implementation grouping — More standalone emulators

Written 2026-10-29 alongside [[tasks/standalone-emulators/plan|plan.md]]. One PR per group, one commit per phase.

**Order.** Group 1 comes first: it fixes a silent gap in a reader that has already shipped (RetroArch with "sort saves by
core"). Groups 2–4 add the EmuDeck emulators, cheapest first. Group 5 widens RetroArch's scopes, which needs a design yes
from the maintainer. Group 6 is the hardware pass and always comes last.

**What can be verified where.** Everything up to the hardware pass runs on the dev box: xUnit, and the `testenv` Windows tray +
WSL daemon with `emu-fixture`. Real saves need the Deck and EmuDeck for Windows (Group 6). Phases 10 and 12 each start with a
read-only capture on the Deck (ares' states location, BigPEmu's file names).

| Group | Contents | Status |
|---|---|---|
| 1 | Phases 1–3 — RetroArch cores: tests, know the core + warning, follow a core switch | ⏳ Not started |
| 2 | Phases 4–7 — several save extensions, mGBA, RMG, Lime3DS | ⏳ Not started |
| 3 | Phase 8 — Flycast (+ Dreamcast serial table) | ⏳ Not started |
| 4 | Phases 9–12 — MAME (+ set title table), ares, xemu row, BigPEmu | ⏳ Not started |
| 5 | Phases 13–15 — RetroArch extra files, shared-file rows, copy pairs | ⏳ Not started |
| 6 | Phase 16 — real-game checks (no code) | ⏳ Not started |
| — | Phase 17 — non-EmuDeck standalones | ⏳ Later, its own task |

## Groups

### Group 1 — RetroArch cores (Phases 1–3)
- **Why together:** Phase 1's tests define what Phases 2–3 change.
- **Files:** `RetroArchSaves.cs`, `RetroArchConfig.cs`, `Enroller.cs`, `GameSources.cs`, the game page in both UIs, and
  `SwitchCopies.cs` (reused for the remap).

### Group 2 — saves named after the ROM (Phases 4–7)
- **Why together:** Phase 4 is a small change in `RomSaves.cs` that Phase 6 needs, and mGBA and Lime3DS are a row each.
- **Files:** `RomSaves.cs` plus each existing row's constructor call, new `MgbaSaves.cs` and `RmgSaves.cs`, `AzaharSaves.cs`,
  and the checklist in the plan.

### Group 3 — Flycast (Phase 8)
- **Why alone:** Flycast has its own reader with three save shapes (arcade, per-game VMU, shared VMU), and the Dreamcast
  serial table changes `Update-ConsoleTitles.ps1` and the bundled `console-titles.tsv.gz`.

### Group 4 — MAME, ares and the not-supported rows (Phases 9–12)
- **Why together:** each one needs a capture or a new table; xemu is a one-row phase.
- **Watch for:** the MAME table's size. Report it in the PR and keep it under about 1 MB gzipped.

### Group 5 — RetroArch extras (Phases 13–15)
- **Before starting:** get the maintainer's answer to Phase 13's design point (scope widening).
- **Why late:** Phase 14 reuses Group 3's NotSyncable wording, and Phase 15 needs Group 2's mGBA reader.

### Group 6 — real-game checks (Phase 16)
The maintainer, on the Deck and on Windows, with the table in the plan.
