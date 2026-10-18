# Session summary — 2026-10-18 — Emulator saves Group F (Switch) done (PR #73)

Standalone: the facts below do not need the rest of the vault.

**PR:** [#73 on `Marwanello/SaveLocker`](https://github.com/Marwanello/SaveLocker/pull/73), *Emulator saves, Group F: Switch*. Branch `emulator-saves-group-f`, base `main` (merged in, mergeable, every CI check green). **Group F is done**, and so is the Switch row of the real-game checks (Group H): Animal Crossing: New Horizons 3.0.3 now opens with the island on both the PC (Eden) and the Deck (Eden on the SD card's NAND), synced through the test rig (`tests/testenv.ps1`). Agent tests **656**, all passing.

### What this session found and fixed

1. **"The save data is damaged" (Animal Crossing).** Not corruption: the island was saved by game version 3.0.3 and opened by 1.6.0 / the base game. The island's header revision maps to the version (rev 34 = 3.0.3). `SwitchGameVersion` reads each install's version (the Yuzu-fork log's `Booting game` line, the game list's `.pv.txt` cache, Ryujinx's log); the DLC check treats the update as missing, and a pull into an older game is held with a message saying which update to install. Animal Crossing also ties each resident to the user ID (`main.dat`, an 8-slot user table), so other games are not affected by the version check unless their saves change format, and the user ID matters only to games like this one.
2. **One Switch user on every device** (maintainer: always ask, every Yuzu-based install, this PR). **Settings → One Switch user on every device** (`SwitchUserSharing`): pick a user ID; every Yuzu-based install on every device adopts it (`profiles.dat` rewritten, the user's saves moved to the new ID's folder, games re-pointed here and on the server, everything zipped first); a title saved under both users waits for a pick; each device can put its original user back. Server: the heartbeat reports each device's users (`AgentHealth.SwitchUsersJson`, migration `AddSwitchUsers`); the setting `Switch:SharedUser` is handed back on the heartbeat. Ryujinx is left alone.
3. **One Eden listed twice on the Deck.** EmuDeck's link `Emulation/saves/eden/saves` ends in `/`, Eden's `nand_directory` doesn't, so the two spellings of one folder were two installs. `EmuDeckRoots.RealPath` drops the trailing separator (commit `5d33631`).
4. **Eden on the Deck started a new island.** Animal Crossing keeps its island as a **device save** (`…/0000000000000000/000…000/<title>`, Eden's log confirms it opens that one). "Copy here" / "make it the one that syncs" put the island under Eden's user instead. Fixed for every route (commits `ddae01f`, `0f27218`):
   - Copies follow the synced folder's kind (`SwitchCopies.SlotFor`).
   - The server records a device-save game as `EmulatorProfile = "switch:device"` (no schema change; the first machine that enrolls it from a device save sets it, and a machine already syncing it from a fork's device save fills it in for older games).
   - A machine joining such a game uses its own device save, even with no save yet or only a copy under a user (`AlignToGameFolders`).
   - A machine that already keeps it under a user moves it to the device save on the poller's tick once no Switch emulator runs, the server told first, both sides zipped (`MoveToDeviceSaveAsync`). On the Deck this ran by itself at 16:02 and the island loaded.
   - Not covered: Ryujinx folders are left to the page (they come from its index), and a game with both an account and a device save where one machine has only the account save.
5. **Rig gotcha.** `testenv.ps1 build` builds the Deck and WSL agents from the WSL clone, which only takes new commits on `testenv.ps1 sync`. Without it the Deck kept running an older build (`ddae01f`).

### Next

Merge PR #73. Left in the emulator-saves task: Groups G (each machine's own file names), H (real-game checks for the other emulators), I (games found by states alone) and J (icons). Ryujinx on the Switch row was not played against a real game.
