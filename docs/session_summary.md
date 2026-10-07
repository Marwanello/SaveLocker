# Session summary — 2026-10-07 — Review of PR #60 (emulator saves), all findings fixed

Standalone: the facts below do not need the rest of the vault.

**PR:** #60 on the fork `Marwanello/SaveLocker`, *Emulator saves: RetroArch saves and states, game source per machine,
Emulators filter*. Head branch `emulator-saves-phase-1`, base `main`.
**Where the fixes were made:** local branch `emulator-saves-review-fixes` in `.claude/worktrees/multiple-save-paths-group-c-5cda33`.
It tracks `origin/emulator-saves-phase-1` and was pushed there. The PR branch's own worktree,
`.claude/worktrees/emulator-saves`, is still at the pre-fix commit: `git pull` there before working in it.

## What was asked

1. Review PR #60 thoroughly. Verdict: **request changes**, with 2 blocking, 2 important and 5 minor findings. CI was green.
2. Fix all of them, push to the PR, append a summary to `docs/progress.md`, and write this file.

## The two problems that mattered

**One ROM could become two server games.** The server identifies a game by its name. The first version named an
emulator save by its cleaned title and added the console ("Chrono Trigger (SNES)") only when *that machine's own
scan* had another game with that name. On a Deck, Steam ROM Manager (part of EmuDeck) adds a Steam shortcut for
every ROM, and every shortcut is a scan candidate. So the Deck said "Chrono Trigger (SNES)" while a PC without those
shortcuts said "Chrono Trigger", and the two never synced.

**A PC game could join an emulator game and never be backed up.** This was proven before fixing, with a two-machine
test against a real server:
- Machine A added the RetroArch save "Chrono Trigger".
- Machine B then added the Steam game "Chrono Trigger".
- B joined A's game and inherited its include patterns (`Chrono Trigger (USA).srm`, `.rtc`).
- The files that would sync from B's save folder: none. Every screen showed B as in sync.

## The naming decision (the maintainer's choice)

Asked with three options; the maintainer picked **"plain names, the server decides"**.
- An emulator save may take, in order:
  1. its title (`Chrono Trigger`);
  2. the title plus the emulator (`Chrono Trigger (RetroArch)`);
  3. the save file's own name plus the emulator (`Chrono Trigger (Japan) (RetroArch)`).
- All three come from the save file alone. At enrollment it takes the one whose server game already holds exactly
  this ROM's files, otherwise the first name no game has.
- Every machine sees the same server, so the same ROM ends up in the same game whichever machine adds it first.
- Accepted gap: if an emulator save takes the plain title first, a PC game with that title added later is refused,
  with the reason shown. It is never mixed into the emulator game.

The rejected options were always adding "(RetroArch)" (fully deterministic, but brings back the suffix the
maintainer had removed) and always adding the console (not deterministic when one machine lacks the ROM folder).

## Everything that changed

- **Names** — `Enroller.NamesFor` and `Enroller.ServerNameFor`. The game list is read from the server once per batch
  and updated as games are created.
  - `GameSources.AvoidNameClashes` is deleted.
  - `ScanCandidate.DedupeKey` stops the scanners merging an emulator save with any other candidate.
  - `Enroller.TrackedFor` (same name *and* same include patterns) answers "is this already set up here?" in the
    Enroller, Add games, Game Mode and the source backfill.
- **Joining** — a candidate with no include patterns is refused when its folder has files and none of them match the
  server game's patterns.
  - It still joins when its folder is empty, or when some of its files match (patterns an admin set in the console
    for that PC game). That case has its own test, so the fix doesn't break it.
- **Refusal reasons are shown** — `EnrollResponse.notes`. Add games prints "Not added: …" instead of counting
  everything as "already tracked"; Game Mode adds the reasons to its status line.
- **Sending the source** — `GameSources.ReportAsync` now says whether the server answered. The poller sends each
  game's source once per value, instead of every 20 seconds for every game against a console that predates the
  route. `GameSourceDto.SameAs` compares values the way the server stores them (trimmed).
- **Two ROMs with one title** (two regions, or one game on two consoles) are now two Add games rows, each showing its
  save file's name. Only one ROM's save found in two core folders collapses to the newer one.
- **The "EmuDeck" tag** comes from the scanner (`RetroArchFolders.EmuDeck` → `ScanCandidate.ViaEmuDeck`). The folder
  is stored by its real path, and EmuDeck's saves folder links into the RetroArch Flatpak's, so reading the path
  never showed "EmuDeck" on real hardware.
- **"Added by hand"** is no longer stamped on a game that was already set up here when its folder is moved, so the
  backfill can still record the real source.
- **Server limits** — `PUT /api/agent/source` accepts a kind up to 32 characters, a detail up to 200, and at most 16
  tags of 64; each must be one line. The agent trims to the same limits.
- **Smaller fixes**
  - A failed source report has its own log line.
  - `retroarch.cfg`'s configured saves folder replaces the default one instead of adding to it.

## Verification

- `dotnet test tests/SaveLocker.Agent.Tests`: **291/291** (was 280).
  - New: `EnrollNamingTests`, 6 tests against a real server.
  - Mutation-checked: disabling the join refusal, and forcing the plain name, each make the intended tests fail.
- The full solution builds clean with `--no-incremental`; `agent-ui` lint and build pass.
- `agent-ui/src/api-types.ts` was regenerated from a dev tray on port 5190 with scratch state, stopped afterwards
  with no registry leftovers. The only diff is `emulatorRom` and `notes`.
- No server route or data shape changed, so `openapi.json` and `web/` were not touched.
- **Not done:** no testenv pass and no real-hardware pass.

## Next

`testenv clean`, because games added by earlier builds won't match. Then check the agent UI and Game Mode source
chips, try a same-titled PC game plus emulator save to see the refusal reason, and do the EmuDeck hardware pass on the
Deck and on Windows. After that PR #60 can merge.
