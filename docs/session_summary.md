# Session summary — 2026-10-04 / 10-05 — Multiple save paths Group A

Standalone: the facts below do not need the rest of the vault.

Branch `multiple-save-paths-group-a`, PR Marwanello/SaveLocker#56 (open). Plan: `docs/tasks/multiple-save-paths/plan.md`.

## What was built, in plain terms

Until now SaveLocker assumed one save folder per game. Many games use more: PC games that save to both Documents and
AppData, and emulators like RetroArch that keep save files in one folder and save states in another. Group A lays the
groundwork for several save folders per game.

- **The save package (Phase 1).** A backup can now hold several folders, each in its own labelled compartment
  (`states`, `appdata`, …).
  - One-folder games are packed exactly as before, so every existing backup still restores.
  - Older agents can read and pass along the new packages without losing anything.
  - A restore checks every folder first. If any check fails, nothing is written.
  - A folder can be limited to certain files (for example only `*.srm`), so two folders can share one directory without clashing.
- **The server (Phase 2).** The console can remember a game's extra folders.
  - It stores each folder's name, label, generic location (template) and file filter, plus where each machine keeps it.
  - Existing databases upgrade automatically; every current folder becomes the game's `main` folder.
  - Bad input is refused: a malformed folder name, a path that escapes its folder, or an agent sending a
    machine-specific path where a template is required.
  - A removed folder's name is retired, so rolling back to an old backup can't put files in the wrong place.

## UI

None in this group. The only dashboard file that changed is the generated `web/src/api-types.ts`, and nothing on screen
uses it yet. The screens come in Group C (Phase 7):
- agent app: a list of a game's folders, with add, change and remove;
- dashboard: one section per folder on the Save folders card;
- Deck: a list of a game's folders, using the existing folder browser.

Group B (Phases 3–5) comes first and makes the agents actually sync extra folders, through the CLI `add-path`.

## Verified

- **Tests:**

  | Suite | Result |
  |---|---|
  | Unit | 207/207 |
  | Console-security | 352/352 |
  | Server bug-bounty | 216 |
  | Health | 33 |
  | Hardening | 33 |
  | Delta upload | 33 |

- **Database upgrade:** a database created by `main`'s server was upgraded by the new one. Old leftover rows were
  removed, and every path became the `main` folder.
- **testenv steps given for a manual check:**
  - `sync`, `build`, `up`;
  - a normal sync between Windows and WSL still works, with one new version and no conflict;
  - extra folders added, refused and removed by hand through `/api/games/{id}/save-paths`;
  - `testenv.ps1 test`.
- **Not testable until Group B:** a second folder syncing between machines.
