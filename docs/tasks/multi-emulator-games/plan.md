# One console entry per game, one save tree per emulator

The console lists a game once for each emulator that saves it. *Metroid Prime - Hunters* shows twice: once for
melonDS and once as *Metroid Prime - Hunters (RetroArch)* for RetroArch's melonDS DS core. *Metroid Prime Trilogy*
is the same for Dolphin and PrimeHack. This plan makes such a game **one entry in the console**. Its page holds
**one version tree per emulator**, and each tree syncs exactly as it does today.

**Mockup:** [`mockup.html`](mockup.html). Open it in a browser. It is clickable and shows three variations
(A Emulator tabs, B Side-by-side trees, C Nested in the list), with Metroid Prime - Hunters and Metroid Prime Trilogy
as the grouped examples.

## Status

| Phase | Status |
|---|---|
| 0 — Pick a variation (mockup) | 🚧 In progress — mockup drawn 2026-10-20, waiting for the maintainer's pick |
| 1 — Server: titles that group games | ⏳ Not started |
| 2 — Console list: one entry per title | ⏳ Not started |
| 3 — Console game page: a tree per emulator | ⏳ Not started |
| 4 — Group and split by hand | ⏳ Not started |
| 5 — Later: agent UI, and copying a save to another emulator | ⏳ Not started |

## Why the saves stay apart

This plan does **not** reopen "Saves from different emulators are never one game" (Decisions.md, 2026-10-07):
- Two emulators write different files. melonDS writes `…(USA).sav`; RetroArch writes `…(USA).srm`, in another folder.
- Pulling one emulator's save into another's folder would overwrite or confuse it.
- Restoring a version only makes sense for the emulator that wrote it.

So every emulator keeps its own server `Game`, with its own versions, folders, rules, conflicts and Latest. Nothing in
sync, the agents, or the save formats changes. The new piece is a **title**: a console-side group over those games.
The console shows the title once, and each game in it is drawn as a tree named after its emulator.

The Switch exception stays as it is: a Switch title is already one game across every Switch emulator (D2,
`switch:<title ID>`), so it shows as one entry with one tree.

## What the console shows

- **List and grid:** one row per title, with its emulators' icons. A game with one emulator looks as it does today.
- **Name:** the title's name, without the *(RetroArch)* suffix the server adds to tell same-named games apart. Inside a
  title, the emulator name tells the trees apart.
- **Status:** the worst of its trees (a conflict anywhere shows), with a hint saying which tree.
- **Last synced:** the newest of its trees. **Size:** the sum.
- **Sort and group** (console-games-sort-group): the Platform grouping's per-emulator sub-headings list the title under
  each of its emulators. Device grouping is the union of the trees' machines.
- **Game page:** one shared head (cover, name, platform, region). Versions, Save folders, Rules, Exclude patterns and
  Remote commands belong to one tree, so they follow the emulator picked. How the trees are laid out is the
  variation choice below.
- **Restore, Delete version, Keep forever:** act on one tree only, and say which emulator in the confirm text.

## The three variations (Phase 0)

| | A — Emulator tabs | B — Side-by-side trees | C — Nested in the list |
|---|---|---|---|
| List | One row, emulator icons on it | One row, emulator icons on it | A parent row that opens to one child row per emulator |
| Page | A tab per emulator under the head; every card follows the tab | All trees side by side as lanes in one card; a picked version opens its files below | The parent opens a title overview (one card per tree); a child opens today's page with a breadcrumb and sibling switcher |
| Good at | Closest to today's page; fewest new parts | Comparing emulators at a glance, e.g. which one has the newer save | Keeps today's page untouched; the list itself shows the trees |
| Costs | A tree's state is hidden until its tab is opened (the tab shows a status dot) | Wide; three or more emulators need a scroll; other cards still need a tree picker | Two levels to click through; the list grows when expanded |

## Phases

### 1 — Server: titles that group games

- New table `GameTitles` (`Id`, `Name`, `Region`, `ReleaseJson`, art URLs) and a nullable `Games.TitleId`. A game with
  no title is shown alone, as today. Migration plus backfill.
- **Grouping is automatic** when a game is enrolled, or found by the backfill. The rule: another game with the same
  platform and release (the same `ReleaseJson` game IDs, else the same `Name` without the emulator suffix and the same
  `Region`), found through a **different** emulator. The same emulator never groups; that is Phase 17's merge.
- `GameDto` and the overview add `titleId`. A new `GET /api/titles` (or the overview grouped by title) gives the title
  rows with their games. Regenerate `openapi.json` and `web/src/api-types.ts`.
- **Verify:** `run-console-security-tests.ps1` covers the new route's auth. A backfill test checks that the
  Hunters (melonDS + RetroArch) and Trilogy (Dolphin + PrimeHack) pairs group, and that two regions or two Switch
  emulators do not.

### 2 — Console list: one entry per title

- `GamesSidebar`, `GamesGrid`, `organize.ts`: rows are titles. Status, last synced and size come from their trees, as
  above. Search matches the title and its emulators.
- **Verify:** extend `run-organize-tests.ps1`; check the console on the test server through testenv.

### 3 — Console game page: a tree per emulator

- `GameDetail` takes a title and the picked tree (in the URL, `?emulator=retroarch`, so a link opens the right tree).
  The chosen variation's switcher drives which `Game` the existing cards get. The cards are unchanged.
- Confirm texts name the emulator.
- **Verify:** testenv; restore a RetroArch version and check that the melonDS tree is untouched.

### 4 — Group and split by hand

- **Split out** a tree into its own entry, and **Group with…** another game of the same platform. These only change
  `TitleId`. No versions move, so they can be undone and need no typed confirm (unlike Merge, Phase 17).
- A game split by hand is never grouped again automatically.

### 5 — Later

- The agent UI (Deck and tray) shows titles the same way.
- **Copy this save to another emulator**, where the formats match (DS `.sav` ⇄ RetroArch `.srm`, Dolphin ⇄ PrimeHack).
  This is a separate plan, with its own decision.

## Open questions

1. Which variation: A, B or C?
2. Group the existing pairs automatically (recommended), or only when the user groups them by hand?
3. Does the title take its name and art from the first tree, or can it be renamed on its own?
