# Cross-OS CI: an emulator save round-trip — summary

Seeded 2026-10-25 from upstream issue [SkorcherX/SaveLocker#48](https://github.com/SkorcherX/SaveLocker/issues/48).
No plan file yet. **Priority: Medium.**

---

## Why

Emulator saves are the one kind that genuinely syncs Windows ↔ Linux byte for byte, and they are also the kind
most exposed to cross-OS differences: a game owns *part* of a shared folder (its include scope), and the restore
writes and deletes only inside that scope. Today that is proven by the two-machine xUnit suites
(`GroupBSyncTests`, `GroupCSyncTests`, `GroupDSyncTests`, `GroupISyncTests`, `RetroArchSyncTests` — both machines on
one OS), the `testenv` Windows ↔ WSL pass (`Build and Run` → *Testing emulator saves*) and Group H's real-game
checks — all manual or single-OS. `tests/cross-os/crossos.ps1`, the CI job that runs a real Windows agent and a
real Linux agent against one server, has no emulator fixture.

## What to add

A scoped-save scenario to the existing three legs (author → roundtrip → confirm), not a new job:

1. **author (Windows):** a fake EmuDeck `Emulation/saves/retroarch/saves` holding two ROMs' `.srm` files plus a
   state; enroll one game through the emulator reader, push.
2. **roundtrip (Linux):** the same tree with the *other* ROM's save present locally; pull, assert the game's
   bytes match **and the other ROM's save is untouched** (the scoped restore's delete pass); amend, push.
3. **confirm (Windows):** pull, byte-compare, assert the sibling save on this side is untouched too.

Optionally a second shape (a PCSX2 folder card, `BASLUS-*/**`) to cover a scope that is a folder, not a file name.

## Notes

- Reuse `testenv emu-fixture`'s tree layout and `SAVELOCKER_EMUDECK_PATH` so the agents scan only the fixture.
- Keep `crossos.ps1` ASCII-only and 5.1-compatible (its header explains why).
- Upstream #47 (variant decision) is already settled here — [[Decisions]], *An emulator game is found by its
  files* (D1) and *Saves from different emulators are never one game* — so nothing blocks this.
- Verify: the three CI jobs green, and the sibling-save assertion fails against a build whose restore ignores
  the include scope (mutation check).
