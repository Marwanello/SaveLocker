# Session summary — 2026-10-11/12 — Emulator saves Group D (PR #65, awaiting verification)

Standalone: the facts below do not need the rest of the vault.

**PR:** #65 on `Marwanello/SaveLocker`, *Emulator saves Group D: PPSSPP, RPCS3, Vita3K, shadPS4, PARAM.SFO names,
per-game ID folders*. Head branch `emulator-saves-group-d`, base `main`.

**Status:** built, awaiting verification — the testenv pass (`Build and Run` → *Testing emulator saves*) and the
real-game checks (Group H) are still to do.

## What shipped

- **`ParamSfo`:** reads Sony's `PARAM.SFO` (UTF-8 and int32 entries, 64 KB cap). Checked against the Deck's real
  RPCS3 save (*Heavenly Sword™*).
- **`TitleFolders`:** D4's shape-2 helper, a folder per game ID in a shared folder, with a per-game scope:
  `<ID>*/**` when a game owns several folders (PSP, PS3), `<ID>/**` when it owns one (Vita, PS4). Group E reuses it.
- **Phase 11, PPSSPP:** `PSP/SAVEDATA` plus states (`PPSSPP_STATE`, scoped `<ID>_*`); EmuDeck, standalone Windows
  and Linux, Flatpak.
- **Phase 5a, RPCS3:** `dev_hdd0/home/*/savedata`, a custom `dev_hdd0` from `vfs.yml`; EmuDeck, standalone, Flatpak.
- **Phase 12, Vita3K (bonus):** `ux0/user/<user>/savedata/<ID>` via `config.yml`'s `pref-path`; named from the
  save's `param.sfo`, else the installed game's.
- **Phase 13, shadPS4 (bonus):** the old layout (`savedata/<user>`) and the new one (`home/<user>/savedata`);
  the scope has no user folder, so the two layouts join.
- **Naming (maintainer's call):** PSP/PS3 by Redump (PSP set added, 3,798 rows, same pinned commit), then
  `PARAM.SFO`; Vita/PS4 by the SFO title with ™/®/© removed. Region from the ID prefix; none for PS4's `CUSA`.
- **Add games:** four emulator chips; PSP, PS Vita and PS4 in the Console row. **Help:** how Sony games are named.
- **testenv `emu-fixture`:** Group D saves on Windows and WSL, including shadPS4 on both layouts and untouched
  neighbour games.

## Follow-up (2026-10-12): emulator users

- No emulator ties a save to a user: RPCS3 and shadPS4 write `ACCOUNT_ID` as zeros and never check it; Vita3K saves
  are plain. PPSSPP has no users at all.
- RPCS3, Vita3K and shadPS4 do keep saves in per-user folders. Each machine records its own user folder and only
  `<ID>…` inside it syncs, so different users on two machines join one game.
- **Fixed:** Vita3K read only user `00`; it now reads every `ux0/user/*/savedata` (EmuDeck storage and standalone).
  New test, mutation-checked against the old reader.
- **Help note:** saves sync from the user folder found when the game was added; after switching the emulator's user,
  click **Change** on the game's folder and pick the new user's `savedata`. Two users with the same game: Add games
  offers the one written last.
- Not done: shadPS4's custom home folder setting isn't read (only the default place).

## Tests

 `dotnet test tests/SaveLocker.Agent.Tests` **420** passed (`SonySavesTests` 23, `GroupDSyncTests` 1 —
PPSSPP Deck Flatpak ↔ Windows memstick against a real server, the per-game scope mutation-checked). `Agent.Linux`
builds, `agent-ui` lint/build clean, no API change. One Group C sync test failed once under full-suite load and
passed on every rerun.

## Next

 the testenv pass of Group D, then merge PR #65.

## Files

`src/Agent.Core/{ParamSfo,TitleFolders,PpssppSaves,Rpcs3Saves,Vita3KSaves,ShadPs4Saves,ConsoleTitles,EmulatorSaves,GameSources}.cs`,
`ConsoleTitles/*`, `agent-ui/src/components/AddGamesView.tsx`, `tests/SaveLocker.Agent.Tests/{SonySavesTests,GroupDSyncTests,ConsoleSaveFixtures,EmulatorChipTests}.cs`,
`tests/testenv.ps1`, `web/src/help/emulator-backups.md`, `docs/{CONTEXT,REPO_MAP,Build and Run}.md`,
`docs/tasks/emulator-saves/{plan,implementation-grouping}.md`, `progress.md`, this file.

## Addendum (2026-10-12): states-only PSP games, Phase 19, merge with `main`

- PPSSPP now lists a game with a save state and no in-game save yet, marked *Save states only so far*; its first
  in-game save joins the same game. A states-only push never empties another machine's real save (pull refused).
- The same for RetroArch, melonDS, PCSX2, DuckStation, Dolphin and PrimeHack is new **Phase 19 / Group I** in
  `docs/tasks/emulator-saves/plan.md`.
- *God of War - Chains of Olympus (Beta)* is the correct name for the PC's real `UCET00844` save (a pre-release build).
- `main` (PR #64) merged into the branch. Unit **427** passing.
