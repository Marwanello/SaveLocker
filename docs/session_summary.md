# Session summary — 2026-10-05 — Multiple save paths Group B

Standalone: the facts below do not need the rest of the vault.

Branch `multiple-save-paths-group-b`, PR Marwanello/SaveLocker#57 (open). Plan: `docs/tasks/multiple-save-paths/plan.md`.

## What was built, in plain terms

Group B makes the agents actually sync several save folders per game. Group A only made the save package and the server
able to hold them.

- **Any folders, anywhere.** A game keeps its main folder (`main`) and can have any number of extra folders. Each extra
  folder has a **key**: 1–32 lower-case letters, digits or `-` (for example `settings`, `appdata`, `profile`).
  - The key is only how machines recognise the same folder. Each machine has its own path for it, just as the main
    folder already did.
  - The demo used `states`, but nothing is emulator-specific.
  - All of a game's folders sync together as one version.
- **Hidden copies ("shadows").** A machine with no path for a folder keeps a hidden copy in its own data folder, under
  `shadow/<game>/<key>/`. It receives that folder's files and sends them back on every push.
  - Without it, that machine would push versions missing the folder, and others would push it back, forever.
  - Example: Windows describes a folder as `<winLocalAppData>/…`. Plain WSL has no Proton prefix to resolve that in, so
    WSL holds a hidden copy until it is given a folder.
- **CLI (Phase 4).**
  - `add-path <game> --key <k> --dir <folder>` adds a folder for every machine, or maps one this machine only holds as
    a shadow. An empty target gets the shadow's files moved in; identical files map as-is.
  - If the folder already holds different files, it refuses until you pass `--keep local` or `--keep cloud`.
  - `remove-path <game> --key <k>` stops syncing that folder on every machine, within about 20 seconds. Files stay on disk.
  - `status`, `list` and `doctor` show every folder; `doctor` gives the shadow's size.
- **Other machines (Phase 4).** A newly added folder is adopted on every machine at once. A machine uses a folder when the
  template points at one that exists there; otherwise it keeps a shadow.
- **Scanners (Phase 5).** A game scanner can now declare extra folders, and they are adopted at enrollment. The
  `emulator-saves` branch needs this next.

## Technical notes

- `TrackedGame` gains `IncludeGlobs`, `ExtraPaths` (`TrackedSavePath`) and `RemovedPathKeys`. `Roots()` and `LocalHash()`
  cover every folder, using the shadow for an unmapped key.
- Every sync path covers all folders: push, pull, hash, settle, the open-file probe, the folder watchers, safety checks,
  `hash`, `resolve-conflict` and the Deck's keep-local. A pull that meets an unknown key adopts it as a shadow.
- `SyncEngine.MapSavePathAsync` holds the push semaphore and the game lock. Config writes keep any mapping already on
  disk, so a daemon can't undo one the launch wrapper just made.
- New route `DELETE /api/agent/games/{id}/save-paths/{key}`, audited under the machine. Local API: `TrackedGameDto.paths`,
  plus `key`/`keep` on `/folder`. Event `savedir.needs_choice`. `openapi.json` and both `api-types.ts` changed by
  additions only; the Decky contract (`id`/`path`) is unchanged.
- Decisions (in `Decisions.md`): an agent-added folder is adopted fleet-wide at once; `remove-path` is fleet-wide; a
  folder no template describes is added without a template.
- **Review fixes** (commit `5970ff2`):
  - Mapping onto files when this machine never received the folder now asks first, instead of ending in a conflict.
  - `ExtraPaths` is swapped, never edited in place, across threads; removal runs under the game lock.
  - An unanswered keep-local/cloud choice is not re-hashed every poll.
  - A local mapping the server has not heard of is re-reported, not reverted.

## Ludusavi manifest

The manifest does not feed this yet. The scanner still uses the first of a game's manifest locations that exists, so
other locations need `add-path` by hand.
- **Phase 6 (Group C)** will offer them: "Also found: … — add as a save folder", for new and tracked games. Nothing is
  added until the user confirms.
- **Not covered by this design:**
  - single-file entries: an `--include` filter on the file's folder approximates them;
  - registry saves: `tasks/registry-saves`;
  - a location nested inside another location.

## UI

None. The screens are Phase 7 (Group C).

## Verified

- **Tests:**

  | Suite | Result |
  |---|---|
  | Unit | 228/228 (+21: `MultiPathAgentTests` 18, `EnrollDeclaredFoldersTests` 3 against a real server) |
  | New `run-multipath-tests` (two machines, real CLI) | 39/39 |
  | Agent | 47 |
  | Delta upload | 33 |
  | Concurrency | 26 |
  | Hardening | 33 |
  | Local API | 113 |
  | Linux regression | 15 |
  | Linux (WSL) | 201 pass / 2 fail, identical to `main` (the known WSLg pair) |
  | `web` + `agent-ui` build | clean |

- **Mutation check:** with shadows disabled, `run-multipath-tests` fails 14 checks and two sync cycles create 3 extra
  versions.
- **testenv rig:** steps are in the PR description. The demo folders are deleted (`C:\SLDemo`, `%LOCALAPPDATA%\SLDemo`,
  WSL `~/sldemo`). `%LOCALAPPDATA%\SaveLocker-test` remains; run `.\tests\testenv.ps1 clean` to reset it.
- **Not verified:** a full testenv pass by the maintainer, and anything on the real Deck.

## Open

- Rename the demo key `states` to something neutral (for example `settings`) in the PR steps and test script: offered,
  not decided.
- Phase 6 confirm-before-adding versus automatic adoption: recommended confirm, not decided.
- Next: testenv pass → merge #57 → rebase `emulator-saves` → Group C.
