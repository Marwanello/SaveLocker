# Playnite plugin: surfaces not yet checked on hardware

Not started. Split out of `tasks/playnite-plugin/plan.md` on 2026-10-01, when the plugin task was closed as done.

The add-on database submission (2026-09-17) lists these as checked against a real Playnite: the pre-launch pull and
block on a real conflict, the "Link to SaveLocker" popup, and the right-click Sync now / Resolve conflict items. These
were fixed or built later and are not in that list:

- **Fullscreen mode:** the resolve window when a conflict blocks a launch from Playnite's Fullscreen mode (fixed late
  in Group 3, never re-confirmed).
- **Lease held elsewhere:** the block when another machine (the WSL test agent) holds the game's lease. The Group 3
  seed script authenticated as the wrong machine; it was fixed and never re-run.
- **The link nudge (Group 4):** shows once for a game that cannot be matched automatically, stays quiet while the agent
  is down, and a cancelled link stays cancelled.
- **Alias backfill:** a game linked through the popup matches automatically on its next launch.

## How

`tests/testenv.ps1 up -PlaynitePath D:\Projects\SaveLocker\Playnite-Test`, a conflict seeded with
`testenv.ps1 conflict -Windows -Wsl`, then the steps in SaveLocker-Playnite's `docs/Build and Run.md` ("Manual
verification, step by step"; steps 7 to 14 cover the nudge).

## Done when

Each item above is seen working, or its bug is fixed.
