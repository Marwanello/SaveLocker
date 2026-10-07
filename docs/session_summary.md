# Session summary — 2026-10-07 — Emulator saves Group H: real-game checks (no code)

Standalone: the facts below do not need the rest of the vault.

**PR:** #61 on `Marwanello/SaveLocker`, *Emulator saves Group B: melonDS, Supermodel, Model 2, ScummVM, identity by
folders, linking by hand*. Head branch `emulator-saves-group-b`, base `main`. This change is docs only, commit `f4af2d8`.

## What was asked

Add a group at the end of the emulator-saves task with **no implementation, only tests with real games**, for
every emulator that shipped without one. The maintainer has no games for **melonDS, ScummVM, Supermodel or Model 2**
yet, and will keep adding each phase's emulator they couldn't test, so the group stays last.

## What was done

- **`docs/tasks/emulator-saves/implementation-grouping.md`:**
  - **Status table:** a new row, *H — Real-game checks (no code)*: ⏳ waiting for games.
  - **Write-up:** Group H is always the last group, so new emulators can keep joining it.
  - **New rule** under "Every group's checks": an emulator the maintainer has no real game for is added to Group H
    when its group ships, instead of holding that group back.
- **`docs/tasks/emulator-saves/plan.md`:**
  - **Status table:** a new row for **Phase 18**.
  - **New Phase 18 section** at the end of the groups, covering:
    - **Back up first:** every save folder listed, because a test agent that adds a game pushes and pulls real files.
    - **Rig:**
      - Deck: `.\tests\testenv.ps1 build -Only deck`, then `up -Only deck`. The test daemon scans the Deck's real
        EmuDeck install.
      - Windows: point the test tray at a **copy** of `Emulation`, plus `%APPDATA%\EmuDeck\Emulators\{Supermodel,m2emulator}`
        and `scummvm.ini`. Use `SAVELOCKER_EMULATOR_HOME` and `up -Only windows -EmuDeckPath <copy>`. Edit `savepath` in
        the copied `scummvm.ini` so it points into the copy.
    - **Five steps for every emulator:**
      1. Save on the Deck, add the game there and sync.
      2. Add it on Windows; the row should read "Joins “…”". Sync, and check the emulator loads the save.
      3. Play further on Windows, sync, and check the Deck loads the newer save.
      4. Check the neighbouring saves are untouched on both machines.
      5. Record the results.
    - **Checklist table:** one row per emulator, each with how to make a save and a state, where the files are on
      the Deck and on Windows, what to watch for, and separate ⏳ boxes for the Deck and Windows.
- **`docs/CONTEXT.md`:** mentions Group H and that later untested emulators are added there.

## The four rows and what each must confirm

| Emulator | What the code had to guess, to confirm with a real game |
|---|---|
| melonDS | Which file the Flatpak actually writes (`melonDS.ini` vs `.toml` paths); a save written beside the ROM |
| ScummVM | The real save file names for each engine (only four fixed-name engines are confirmed); two machines with different target names |
| Supermodel | That a set never played stays hidden (EmuDeck preinstalls 29 NVRAM files); the title from `Games.xml` |
| Model 2 | Which key saves a state (slots are the number keys 0–9); whether `NVDATA/<set>.DAT` changes on every exit; `STATES` created on the first save |

## How it's used

When the maintainer gets a game, they test it on both machines and tick its row. A row is ticked only when both
pass. When a later phase ships an emulator they couldn't test, a row is added to the Phase 18 table. Any problem
found becomes a fix in that emulator's own group, in a follow-up PR.
