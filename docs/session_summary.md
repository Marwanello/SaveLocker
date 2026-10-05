# Session summary — 2026-10-05 — Multiple save paths Group C

Standalone: the facts below do not need the rest of the vault.

Branch `multiple-save-paths-group-c` (off `main` at Group B, #57), PR to the fork (Marwanello/SaveLocker). Plan:
`docs/tasks/multiple-save-paths/plan.md`, Phases 6–7. Commits: `930fb74` Phase 6, `435b42f` Phase 7, `7bbca5c` fixes
from the testenv pass, then this docs commit.

## What was built, in plain terms

- **SaveLocker notices a game's other save folders.** When the save database (the Ludusavi manifest) lists more than
  one location for a game and they exist on this machine, the first stays the main folder (exactly as before). The
  others are offered as **Also found**. Nothing is added on its own: the database can't tell a second save folder from
  a settings folder or another store's copy (DRAGON QUEST III's second location is its `Config`).
- **Where it is offered.**
  - **Add games:** the extra folders are listed under the game, ticked; untick to leave one out.
  - **The game's page** in the agent: **Add** / **Don't sync**.
  - **Once per start**, for games tracked before this release: an OS notification, and in the agent window a
    one-game-at-a-time **More save folders found** prompt with every folder ticked. **Skip for now** leaves them on the
    game's page and out of the prompt.
  - The CLI's `scan` and `status` print *also found*, with the `add-path` command for each.
- **Agent game page:** the save folders as a list (Main + extras), each with Change, and Remove on the extras (every
  device; files stay). **Add save folder…** asks for a name and, optionally, "only these files". When this device's
  files and the cloud's copy differ, it asks which to keep.
- **Console:** one section per folder (template, include patterns, each machine's folder, what Latest holds with a
  file list), add/remove for the fleet. Machines on an agent ≤ 0.6.0 are flagged *agent too old for several folders*.
- **Deck:** every folder listed with its own Choose/Change, the browser sets the chosen one and asks *This Deck's files /
  The cloud's copy*; Also found with Add / Don't sync.

## Maintainer decisions (recorded in Decisions.md)

1. Suggestions stay on the agent (no server table), plus the start-up prompt; the console lists the folders and their
   files.
2. Minimum agent 0.7.0 → implemented as "flag ≤ 0.6.0" (the compare value is 0.6.1), because every build since v0.6.0
   reports 0.6.1 and has the feature. Same flags for the released fleet.
3. Add games: extra folders pre-ticked.

## Technical notes

- `ManifestLoader.ResolveSaveLocations` keeps each folder's template; `ResolveSaveDirectories` projects it.
- `ScanCandidate.AlternateSaveDirs` from both scanners; `FolderSuggestions` (keys, per-game suggestions, resolver per
  tracked game); `TrackedGame.IgnoredFolders` / `DeferredFolders` via `AgentConfig.SaveGameFolderChoices`.
- `SavePathEditor` holds add/remove for the CLI, the local API and the prompt. A *suggested* key moves on to the next free
  one when the server has or had it.
- `Enroller` adds the ticked folders. One the fleet already has (same key or template) is joined, never duplicated — a
  test caught `appdata-2` before the fix.
- New local routes: `GET /api/folder-suggestions`, `POST /api/folder-suggestions/answer`, `POST /api/games/{id}/paths`,
  `DELETE /api/games/{id}/paths/{key}`. New server route: `GET /api/games/{id}/versions/{versionId}/folders`.
- Notice `NoticeCatalog.FoldersFound` → agent-ui route `#games:folders`.

## Verified

| Check | Result |
|---|---|
| Unit | 247/247 (+19) |
| `run-local-api-tests` | 119/119 (+6; with the new routes carved out of the token guard, 4 fail) |
| `run-multipath-tests` | 39/39 |
| `run-appearance-consistency-tests` | 46/46 |
| Detection sweep, 300 games, seed 1 | 286/299 (95.7%) — identical to `main` |
| `web` + `agent-ui` | lint, typecheck, build clean |
| testenv (Windows + WSL + console) | steps 1–9 of the PR run by hand, with screenshots; it found two bugs, fixed in `7bbca5c` |

Not verified: the Deck target (none configured here), and Add games' pre-ticked list against a real Steam/Playnite
candidate (automated tests cover the enrollment side).

## Open

- `-Only deck` pass, then merge (Group B's PR #57 first if it is still open), then rebase `emulator-saves`.
- `docs/tasks/multiple-save-paths/` stays in `tasks/` until then — `emulator-saves/plan.md` links into it.
