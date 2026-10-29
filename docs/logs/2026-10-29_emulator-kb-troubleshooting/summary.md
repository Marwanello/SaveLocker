# Help KB: where emulator saves are looked for, and "my game isn't listed" — summary

Seeded 2026-10-25 from upstream issue [SkorcherX/SaveLocker#50](https://github.com/SkorcherX/SaveLocker/issues/50).
No plan file yet. **Priority: Low.**

---

## Why

`web/src/help/emulator-backups.md` already covers most of #50 — which emulators Add games finds, naming,
shared memory cards, save states, closing the emulator. What it does not say is **where** SaveLocker looks, so a
user whose emulator save is not listed has nothing to check. `troubleshooting.md` has no emulator entry at all.

## What to add

- A section (or a short article, registered in `web/src/help/index.ts`) listing, per emulator, the folders read
  on Windows and on Linux/Deck: EmuDeck's `Emulation/saves/<emulator>` first, then the standalone/Flatpak
  (`~/.var/app/<id>/…`) and config-file fallbacks. Source it from `src/Agent.Core/EmulatorPaths.cs` /
  `EmulatorPlaces.cs` and each reader, not from memory.
- A **"My emulator game isn't in Add games"** checklist: the emulator kept its saves somewhere custom; only an
  in-game save makes a game a candidate (except the states-only emulators); a shared memory card shows greyed
  out; the save is under a different emulator than expected (different emulators are never one game); map the
  folder by hand as a fallback. Link it from `troubleshooting.md`.
- Tables render (`remark-gfm` is in), so upstream's no-tables workaround does not apply.

## Notes

- `docs/CLI Reference.md`-style rule: the KB article is the source of truth; keep it in sync when
  [[tasks/standalone-emulators/summary|More standalone emulators]] adds readers.
- Verify: the article renders in the console's Help view (both themes), and every path it lists matches the code.
