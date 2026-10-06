# Session summary — 2026-10-06/07 — Emulator saves: RetroArch states, game sources, plain names

Standalone: the facts below do not need the rest of the vault.

**Branch:** `emulator-saves-phase-1` (renamed from `emulator-saves`). PR on the fork `Marwanello/SaveLocker`.
Worktree: `.claude/worktrees/emulator-saves`.

### Request sequence

1. Merge *Multiple save paths* (then on `main`) into the emulator branch, implement what was left (Phase 1b, save
   states), and revise the include-glob implementation if multiple save paths covers it. Create a worktree if
   missing, and give detailed testenv verification steps.
   - The branch still existed; a worktree was created for it.
   - Multiple save paths' per-folder scope (`SaveRoot.IncludeGlobs`) replaced the branch's own server column,
     DTO fields, validation and `SaveArchive` include code. The merge kept `main`'s version of every shared file.
2. Reported that the WSL agent UI's Add games was empty. Cause: the WSL clone `~/SaveLocker` was still on another
   branch; `testenv.ps1 sync` fixed it.
3. Asked whether the Add games status icons and Hide enrolled were in the previous PR. Yes: PR #58
   (SkorcherX/SaveLocker), squash-merged as `567d686`.
4. Merge `main` again (Group C) and sync WSL. Done (`3b88d4a`). Also fixed `9fd5f96`: a game adopted from the
   server with no folder here was shown as "enrolled" and hidden by Hide enrolled.
5. Drop "(RetroArch)" from names, and show each game's source per device on the game page of the console and the
   agent, in two levels (an emulator game: "Emulator", then which emulator). Mockup first, with variations of
   placement. Published a clickable mockup: https://claude.ai/artifact/7sEYQmakCTqd59xUJjxY9b
6. Picked **variation B on all three screens**, and asked for an Emulators chip in the agent's Add games with a new
   chip row of emulators between the source row and the Save folder row, extended as emulators are added.
7. Mark multiple save paths done in the Backlog and everything implemented in emulator saves as done, append the
   summary to `progress.md`, save this file, rename the branch and open a PR on the fork.

### What was built

**Phase 1b — RetroArch save states** (`9ed512b`, `7200484`):
- `RetroArchConfig.Folders()` returns each setup's (saves, states) pair: EmuDeck's `saves/retroarch/{saves,states}`,
  or a `retroarch.cfg`'s `savefile_directory` / `savestate_directory` (defaults `<root>/saves`, `<root>/states`).
- Each RetroArch game declares a second folder, key `states`, scoped to `<rom>.state*` (slots, `.state.auto`,
  thumbnails). It is declared even when the folder does not exist yet, so every machine defines the game alike.
- `StatesDirFor` picks the folder that already holds the ROM's states, then the save's core folder, then the root.
- `SaveDirSanity.Inspect`/`Measure` take the folder's scope, so doctor and the folder check measure only this
  ROM's files.
- `testenv.ps1 emu-fixture` writes a second tree for the WSL daemon (`Emulation-wsl`), and `Invoke-Wsl` points the
  daemon at it, so the Windows ↔ WSL round trip runs on the rig.

**Game sources and plain names** (`e54a22a`):
- **Server:** table `MachineGameSources` (migration `AddMachineGameSources`, one row per machine and game).
  `PUT /api/agent/source/{gameId}` (agent), `GET /api/games/{id}/sources` (admin), and `GameDto.MachineSource` on
  the agent's game list so the poller re-sends a source the server lacks. Display only.
- **Agent:** `GameSources` (Agent.Core) writes the words once — "Emulator › RetroArch" with SNES / Flatpak /
  EmuDeck tags, "Steam › Installed game" with AppID / Proton, "Heroic › Epic Games", "Playnite › …",
  "Save-folder scan › Saved Games", "Added by hand › Folder picked in the agent" or "› savelocker add-game".
  `TrackedGame.Source` is set at enrollment, backfilled by a scan that finds an older game at the same folder, and
  kept across a stale host's `Save` (`??=`).
- **Console:** a chip under the game name; "N sources" when machines differ; a popover lists each machine.
- **Agent UI:** a chip with tags under the game name.
- **Game Mode:** a pill beside the sync status, "Emulator · RetroArch · SNES" (its font is Latin-1 only).
- **Names:** the title alone. An emulated game whose name a PC candidate on the same machine also has gets its
  console appended ("Chrono Trigger (SNES)"), before the scan merges candidates by name.
- **Emulator filter:** agent UI *Emulators* chip; while on, an *Emulator* row (All / RetroArch) between the source
  row and the Save folder row, one chip per emulator in `EMULATORS`. Game Mode: an *Emulators* pill.

### Verification

- `dotnet test tests/SaveLocker.Agent.Tests`: **280** passed. New: `GameSourcesTests` 5 (the stale-host check
  mutation-checked), `RetroArchSyncTests` (two machines against a real server, now also asserting both machines'
  sources), `RetroArchTests` 17.
- Full solution build clean; `web` and `agent-ui` lint and build clean.
- `openapi.json` and `web/src/api-types.ts` regenerated, additions only. `agent-ui/src/api-types.ts` edited by hand.
- Console source chip and popover checked in the browser against a dev server seeded with three machines.
- testenv Windows ↔ WSL round trip for Phases 1 and 1b, with the fixtures.

### Not done / left open

- The agent UI and Game Mode source chips have not been looked at on the rig. Run `testenv clean` first: games
  added earlier as "… (RetroArch)" no longer match.
- The real EmuDeck hardware pass (Deck + Windows) for Phases 1 and 1b.
- Emulator saves Phases 2–6 (PCSX2/Dolphin/DuckStation, `gamelist.xml` names, PrimeHack, RPCS3/Xenia, Switch) stay
  in the Backlog; the per-console filter breakdown comes back with Phase 2.

### Vault changes

- *Multiple save paths per game* marked done: removed from `Backlog.md`, folder moved to
  `logs/2026-10-06_multiple-save-paths/`, indexed in `logs/shipped-2026-10.md`, links updated.
- `tasks/emulator-saves/plan.md`: Phases 1, 1b, 7 and 7b marked shipped; section *Game sources and names — as
  built*. The Backlog line now reads "Emulator saves: the other emulators (RetroArch is done)".
- `Decisions.md` (names without the suffix; source per machine), `API Reference.md`, `Build and Run.md`,
  `CONTEXT.md`.
