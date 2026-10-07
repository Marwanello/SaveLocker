# Implementation grouping — emulator saves

Written 2026-10-07. The maintainer added Cemu, Azahar, Model 2, PPSSPP and the Switch family (Yuzu, Ryujinx and
Citron alongside Eden), plus five bonus emulators (Supermodel, melonDS, ScummVM, shadPS4, Vita3K), and asked for
every remaining phase in as few groups as keeps each PR a reasonable size, with alike work together. `plan.md` has
the phases, the research behind them and the design points D1–D4. This file says which phases share a PR and in
what order. There is one commit per phase and one PR per group, and both Status tables are updated as each one
ships.

## Status

| Group | Contents | Status |
|---|---|---|
| A — RetroArch | Phases 1, 1b, 7, 7b | ✅ Shipped 2026-10-07 — PR #60 (`emulator-saves-phase-1`). The testenv pass of the source chips and the EmuDeck hardware pass are still to do |
| B — Saves named after the ROM | Phases 3 (identity by folders, D1), 8 melonDS\*, 9 Model 2 + Supermodel\*, 10 ScummVM\*, 16 linking by hand (same files) | 🚧 Built 2026-10-07 — PR #61 (`emulator-saves-group-b`), one commit per phase; review fixes on the same PR (D1 on the
save file only, *Keep it as its own game* kept apart, untouched seeds can join). D1 confirmed; Model 2 kept (seeds hidden by hash, maintainer's choice); captured on the real Deck (found EmuDeck's 29 seeded Supermodel NVRAM files). The testenv pass and a played-save hardware pass are still to do |
| C — Memory cards | Phases 2 (PCSX2, Dolphin, DuckStation + the shared-card warning), 4 PrimeHack, the per-console row | ⏳ Not started |
| D — Sony (`PARAM.SFO`) | Phases 11 PPSSPP, 5a RPCS3, 12 Vita3K\*, 13 shadPS4\* | ⏳ Not started |
| E — Title-ID folders | Phases 14 Cemu, 15 Azahar, 5b Xenia | ⏳ Not started |
| F — Switch | Phase 6 (Yuzu, Citron, Eden, Ryujinx) + the server title key (D2) | ⏳ Not started |
| G — Each machine's own file names | Phase 17 (link a save to a server game whose files are named differently: another dump, another emulator) | ⏳ Not started — added 2026-10-07; open questions in plan.md → *Phase 17* |

\* Bonus. If a group grows too big, a bonus emulator is the first thing to move to a later PR.

## How the groups were cut

- **By save shape, because the shape is the code** (`plan.md` → D4). An emulator is a config-root table plus a
  name rule on top of one of three readers: a file named after the ROM in a shared folder, a folder per title ID
  in a shared folder, or Ryujinx's opaque folders plus an index. Emulators of one shape share a reader, a test
  layout and their review questions, so they share a PR.
- **Five groups, not fewer.** Merging any two puts seven or eight emulators in one PR, together with either the
  shared-card safety check (C) or a server migration (F). Group A, one emulator with all its foundation and UI,
  was already a full PR; each later group is three or four emulators on a reader that is mostly there.
- **The two design points land where they are first needed.** D1, finding an emulator game by its folders, comes
  in B, and every later group's machine-local names rely on it. D2, the server title key, comes in F, the only
  group that needs it. Confirm each with the maintainer at the start of that group.

## Groups, in order

**B — Saves named after the ROM, first.** It is the closest to what has shipped: `RetroArchSaves` becomes a
shared reader, and melonDS, Supermodel, Model 2 and ScummVM are each a folder table and a name rule on it. It goes
first, although three of its emulators are bonus, because it lands D1. The key check is a two-machine test where
one machine names the game from a `gamelist.xml` and the other has none, and they must end up as one server game
(mutation-checked). Model 2 needs a capture first: what its `NVDATA` files are called, and which of them EmuDeck
copied in itself. If the capture is not ready, B ships without Model 2.

**C — Memory cards.** PCSX2, Dolphin with PrimeHack (one more config root), and DuckStation run the most-played
consoles after RetroArch's, and they carry the task's one real data-safety risk: a card shared by every game, so
restoring one game's version restores them all. The shared-card `SaveDirSanity` warning is tested against shared
and per-game layouts before any reader in the group ships. The per-console row that Phase 7 left out comes here,
when PS1, PS2, GameCube and Wii join RetroArch's consoles.

**D — Sony.** One `ParamSfo.cs` names four emulators. For three of them (PSP, PS3, PS4) the SFO is inside the save,
so the name is the same on every machine. D also builds D4's shape-2 helper, a folder per game ID in a shared
folder, which E reuses. PPSSPP is the main one. shadPS4 is the first to slip, because its save layout moved
recently and EmuDeck still links the old folder.

**E — Title-ID folders: Cemu, Azahar, Xenia.** These have D's shape with hex title IDs. The names come from the
emulator's own cache or the ROM header, which only one machine may have, so they are safe only after D1. The
details to get right are Azahar's scope (`<low>/data/**`, never the installed game beside it) and Cemu's account
folders.

**F — Switch, last.** It is the only group with a server change (D2, a title key) and the only binary index
(Ryujinx's `imkvdb.arc`). It is also in the most volatile part of the ecosystem: Eden's and Citron's layouts, and
Ryujinx's index, must be captured from real installs first. It comes last so it can reuse D's and E's helpers and
build on the most settled design. It is also what makes Switch saves sync between emulators, from Eden on the Deck
to Ryujinx on Windows.

**G — Each machine's own file names.** Added 2026-10-07 when Phase 16 (linking by hand, same files) shipped with
Group B. It is the only group that changes what every push, pull and hash does, so it gets its own PR and its own
two-machine tests per emulator family, with renamed states, a conflict and a restore. Its open questions (across
emulators or not, where the map lives, the console's display, merging two existing games) are in plan.md →
*Phase 17* and are settled with the maintainer before it starts. It does not block C–F.

**Every group's checks:**
- fixtures and unit tests per reader;
- one two-machine test against a real server;
- one chip per emulator in agent-ui's `EMULATORS`;
- testenv Windows + WSL fixtures, with screenshots of Add games;
- the hardware pass on whatever is installed (EmuDeck on the Deck, EmuDeck for Windows).
