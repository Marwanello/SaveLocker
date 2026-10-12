# Session summary — 2026-10-11/12 — Emulator saves Group D shipped (PR #65)

Standalone: the facts below do not need the rest of the vault.

**PR:** #65 on `Marwanello/SaveLocker`, *Emulator saves Group D: PPSSPP, RPCS3, Vita3K, shadPS4, PARAM.SFO names,
per-game ID folders*. Head branch `emulator-saves-group-d`, base `main`. `main` (PR #64) is merged in, and the PR merges
cleanly.

**Status:** Group D is marked **✅ Shipped 2026-10-12** in `docs/tasks/emulator-saves/implementation-grouping.md`,
and its phases 5a, 11, 12 and 13 are marked the same in `plan.md`. Two things are still to do: the testenv pass
(`Build and Run` → *Testing emulator saves*), and the real-game checks of all four emulators in Group H, Phase 18.

## What shipped

Group D syncs PSP, PS3, PS Vita and PS4 saves. In every one of these emulators, a game's saves are folders named by
its game ID inside a save folder all games share, so each game syncs only its own folders.

- **`ParamSfo`:** reads Sony's `PARAM.SFO` file.
- **`TitleFolders`:** the shared helper for "a folder per game ID", which Group E reuses. It gives each game its own
  scope: `<ID>*/**` when the game has several folders (PSP, PS3), `<ID>/**` when it has one (Vita, PS4).
- **PPSSPP (PSP):**
  - Saves are `PSP/SAVEDATA`. States are `PPSSPP_STATE`, scoped to `<ID>_*`.
  - It reads EmuDeck on SteamOS and Windows, standalone installs and the Flatpak.
- **RPCS3 (PS3):**
  - Saves are under `dev_hdd0/home/*/savedata`, for every RPCS3 user.
  - It follows a `dev_hdd0` moved elsewhere in `vfs.yml`.
- **Vita3K (PS Vita, bonus):**
  - Saves are under `ux0/user/*/savedata`, for every Vita3K user, found through `pref-path` in `config.yml`.
  - The name comes from the save's own `param.sfo`, else from the installed game's.
- **shadPS4 (PS4, bonus):**
  - It reads both layouts: the old one (`savedata/<user>`, which EmuDeck still links) and the new one
    (`home/<user>/savedata`).
  - A save on one layout joins a save on the other.
- **Names and regions:**
  - PSP and PS3 games are named by Redump first, then by the save's `PARAM.SFO`. Redump's PSP set was added to the
    bundled title table.
  - Vita and PS4 games are named by the SFO title, with ™, ® and © removed.
  - The region comes from the game ID's prefix. A PS4 game shows no region, because its ID has none.
- **Add games:** four emulator chips, and PSP, PS Vita and PS4 in the Console row.
- **Help article:** how Sony games are named.

## Follow-ups on the same PR (2026-10-12)

- **Emulator users** (researched in each emulator's own source):
  - No emulator ties a save to a user. RPCS3 and shadPS4 write `ACCOUNT_ID` as zeros and never check it.
  - RPCS3, Vita3K and shadPS4 do keep saves in a folder per user. Only `<ID>…` inside that folder syncs, so different
    users on two machines still join one game.
  - **Fix:** Vita3K used to read only user `00`; it now reads every user.
  - **Help note:** after switching the emulator's user, click **Change** on the game's folder and pick the new user's
    `savedata` folder.
- **PSP games played from save states only:**
  - A PSP game with a state but no in-game save yet is now listed, marked *Save states only so far*.
  - Its first in-game save joins the same game.
  - A machine that pushed only states never empties another machine's real save, because that pull is refused.
  - The same for RetroArch, melonDS, PCSX2, DuckStation, Dolphin and PrimeHack is new **Phase 19 / Group I**.
- **"(Beta)" in a name is right:** the PC's real PPSSPP save is `UCET00844`, which Redump lists as *God of War - Chains
  of Olympus (Beta)*, a pre-release build. The retail European disc is `UCES-00842`.
- **Group H (Phase 18):**
  - Every Sony row now names its console: PPSSPP (PSP), RPCS3 (PS3), Vita3K (PS Vita), shadPS4 (PS4).
  - The shadPS4 row now covers the new layout, shadPS4's offer to move old saves into `home/1000`, and joining across
    layouts.
  - The Vita3K row now covers a second Vita3K user.

## Tests

- `dotnet test tests/SaveLocker.Agent.Tests`: **427** passed.
  - `SonySavesTests`: 25.
  - `GroupDSyncTests`: 3, run against a real server.
  - Mutation-checked: the per-game scope, reading every Vita3K user, and states-only detection.
- `Agent.Linux` builds and `agent-ui` lints clean.
- The server API is unchanged. The agent's `CandidateDto` gains `statesOnly`, an additive field.
- testenv `emu-fixture` has Group D saves on Windows and WSL. They include shadPS4 on both layouts, and *Chains of
  Olympus* on Windows with a state and no save.

## Next

1. Merge PR #65.
2. Run the Group D testenv pass.
3. Do the real-game checks in Group H when games are at hand.
4. Group I (states-only games for the other emulators) after Groups A–C are merged.

## Files

`src/Agent.Core/{ParamSfo,TitleFolders,PpssppSaves,Rpcs3Saves,Vita3KSaves,ShadPs4Saves,ConsoleTitles,EmulatorSaves,GameSources,ScanCandidate,AgentApiServer}.cs`,
`src/Agent.Core/ConsoleTitles/*`, `agent-ui/src/{api-types.ts,components/AddGamesView.tsx}`,
`tests/SaveLocker.Agent.Tests/{SonySavesTests,GroupDSyncTests,ConsoleSaveFixtures,EmulatorChipTests}.cs`,
`tests/testenv.ps1`, `web/src/help/emulator-backups.md`, `docs/{CONTEXT,REPO_MAP,Build and Run,progress}.md`,
`docs/tasks/emulator-saves/{plan,implementation-grouping}.md`, this file.
