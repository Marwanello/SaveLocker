# One console entry per game, one save tree per emulator

The console lists a game once for each emulator that saves it. *Metroid Prime - Hunters* shows twice: once for
melonDS and once as *Metroid Prime - Hunters (RetroArch)* for RetroArch's melonDS DS core. *Metroid Prime Trilogy*
is the same for Dolphin and PrimeHack. This plan makes such a game **one entry in the console**. Its page holds
**one version tree per emulator**, and each tree syncs exactly as it does today.

**Mockup:** [`mockup.html`](mockup.html). Open it in a browser. It is clickable and shows three variations
(A Emulator tabs, B Side-by-side trees, C Nested in the list), with Metroid Prime - Hunters and Metroid Prime Trilogy
as the grouped examples. **Variation A (Emulator tabs) was picked on 2026-10-20**; B and C stay as
design history.

## Status

| Phase | Status |
|---|---|
| 0 — Pick a variation (mockup) | ✅ Shipped 2026-10-20 — maintainer picked **A, Emulator tabs** |
| 1 — Server: titles that group games | ✅ Shipped 2026-10-21 (branch `multi-emulator-games`) — `GameTitle`, `TitleNames`, `TitleGrouping`, backfill; migration `AddGameTitles` |
| 2 — Console list: one row per title | ✅ Shipped 2026-10-21 — `titles.ts`; organize fixtures +8 |
| 3 — Console game page: emulator tabs | ✅ Shipped 2026-10-21 — `#game=<id>` keeps the tab (not `#emulator=`: it also names the game, so a refresh reopens it) |
| 4 — Group and split by hand | ✅ Shipped 2026-10-21 — a lone game can also join a titled one (*Other emulators* card) |
| 5 — Help, testenv check, docs | ✅ Shipped 2026-10-26 — help article, Decisions and docs; maintainer marked the testenv check done (#85) |
| 6 — Agent UI, and copying a save to another emulator | ✅ Shipped 2026-10-21 — as [`phase6-mockup.html`](phase6-mockup.html); copy runs on the server, offered in the agent and the console (Decisions) |

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

## Decisions (maintainer, 2026-10-20)

All three were confirmed by the maintainer on 2026-10-20.
1. **Existing pairs are grouped automatically**: once by a backfill, then on every new enrolment.
2. **A title takes its name and art from its first game** (the oldest). It has no rename of its own yet, since the
   console has no game rename either. If the first game is split out, the next oldest takes over.
3. **Two trees from one emulator can share a title.** This happens with another dump that stays its own game
   (pull-safety Phase 8). The tab says which tree is which: *RetroArch · Rev 0*.

## Phases

Branch `multi-emulator-games`, cut from `main` after `emulator-saves-group-h` is merged. One commit per phase.

### 1 — Server: titles that group games

**Data.**
- `Data/Entities.cs`:
  - New `GameTitle` (`Id`, `CreatedAt`, `PlatformKey`, `BaseName`, `RegionKey`).
  - `Game.TitleId` (`Guid?`, FK, `SetNull` on delete), indexed.
  - `Game.TitleLocked` (`bool`): set when a game is split out by hand, so automatic grouping never takes it back.
  - A title holds no name or art of its own (decision 2). The console reads them from its first game.
- Migration `AddGameTitles`. The backfill (`TitleGrouping.BackfillAsync`) runs at startup and is idempotent, like the
  other one-time fills.

**Grouping rule** (`Services/TitleGrouping.cs`; pure and unit-tested):
- **Base name.** Remove a trailing `(<emulator>…)` group from the game's name, where `<emulator>` is one its sources
  report (`MachineGameSources`, kind `emulator`). Then remove a region tag that equals the game's region, or any
  region tag when no region is recorded. Examples:
  - `Metroid Prime - Hunters (RetroArch)` → `Metroid Prime - Hunters`
  - `Metroid Prime - Hunters (RetroArch, Rev 0)` → `Metroid Prime - Hunters`
  - `Metroid Prime - Hunters (USA) (RetroArch)` → `Metroid Prime - Hunters`
- **Platform.** The source's console tag (`DS`, `Wii`, …, as the agent reports it in `tags`), else `ReleaseJson`'s
  console.
- **Two games group when** the platform, the base name (ignoring case) and the region are equal, **and** they come
  from different emulators or are different dumps (decision 3).
- **Never grouped:**
  - a game with no emulator source (a PC game);
  - a Switch-keyed game, which is already one game across the Switch emulators (D2);
  - a game with `TitleLocked`;
  - two games of different regions.
- **When it runs:** when a game is created, when a game gets its first emulator source (the enrol path in
  `SyncService` and `RecordSourceAsync`), and in the backfill. It only ever sets `TitleId`. It never renames
  anything, moves versions or touches sync.

**API.**
- `GameDto` gains `TitleId` (trailing, nullable). `GameStateDto` gains two fields:
  - `Emulator`: the emulator its sources name, null for a PC game.
  - `Edition`: the tags that set it apart inside its title (for example `Rev 0`), else null.
- `GET /api/overview` keeps its shape, one `GameStateDto` per game. The console groups the games by `titleId`, so
  agents and older consoles see no change.
- New admin routes for Phase 4: `POST /api/games/{id}/title/split` and `POST /api/games/{id}/title/join?into={id}`.
- Regenerate `openapi.json` and `web/src/api-types.ts`.

**Verify.**
- Tests for the grouping rule:
  - Hunters (melonDS + RetroArch) groups. Trilogy (Dolphin + PrimeHack) groups.
  - Two regions don't group. A Switch game doesn't. A PC game of the same name doesn't. A locked game stays out.
  - The backfill is idempotent.
- Add route auth checks to `run-console-security-tests.ps1`.
- Start the server on a copy of the test server's database and check that the three Hunters games (melonDS, RetroArch,
  and the Deck's `(USA) (RetroArch)`) form one title, and the Trilogy pair forms another.

### 2 — Console list: one row per title

- `web/src/components/game/titles.ts` (new, pure): `groupTitles(GameStateDto[]) → TitleSummary[]`. A `TitleSummary`
  has:
  - `id`: the title ID, or the game ID for a game with no title;
  - `name` and art from the first game;
  - `trees`: its `GameStateDto`s, ordered by emulator name, then edition;
  - `status`: the worst tree's (`standingKey`, conflict first);
  - `lastSynced`: the newest head;
  - `size`: the sum of the trees;
  - `machineIds` and `sources`: the union of the trees'.
- `GamesView`, `GamesSidebar`, `GamesGrid`: render `TitleSummary` rows, and select by the title's `id`. The row's
  second line shows the emulator badges and *2 emulators · Today 09:15* (mockup A).
- `organize.ts`: group and sort `TitleSummary` rows.
  - Platform grouping lists a title under each of its emulators' sub-headings, the way a game on two machines is
    listed under both devices.
  - Search matches the title's name and its emulators.
- `run-organize-tests.ps1` and `organize-fixtures.cjs`: add the Hunters and Trilogy titles.

### 3 — Console game page: emulator tabs

- `GameDetail` takes a `TitleSummary` and the picked tree. A one-tree title renders exactly as today, with no tabs.
- `game/EmulatorTabs.tsx` (new): one tab per tree. Each tab shows:
  - the emulator's icon (`platformIcon.ts`);
  - its name and edition;
  - *N versions · last synced*;
  - the tree's status dot.

  Arrow keys move between tabs.
- **The picked tab is kept in the URL hash** (`#emulator=retroarch`), so a link or a refresh opens the right tab. A
  link with an old game ID opens its title on that game's tab.
- **What follows the tab:** everything under the tabs gets the picked tree's `GameStateDto`, unchanged: stats,
  Versions, Save folders, Rules, Exclude patterns, DLC, Launch profile, Merge and Remote commands. The head (cover,
  name, platform, region) is the title's.
- **Confirm texts name the emulator:** Restore, Delete version, Delete game and Merge. For example: *Restore this
  RetroArch version to WinTest?* Delete game deletes the tree, not the title.
- **A conflict in a closed tab** shows on that tab (red dot), and in the head's status chip as *Conflict in PrimeHack*.

### 4 — Group and split by hand

- **＋ Group with…** at the end of the tab strip (mockup A) lists same-platform games that aren't in a title, with
  their emulators. Picking one calls `title/join`. No versions move, so it needs no typed confirm (unlike Merge).
- **Split out**, in each tab's ⋯ menu, calls `title/split`. It sets `TitleLocked`, and the game becomes its own row
  again.
- Both actions are recorded in the audit log.

### 5 — Help, testenv check, docs

- **Help:** a KB article in `web/src/help/` on games saved with several emulators.
- **Decisions.md:** "A game saved with several emulators is one console entry, with one tree per emulator. Sync still
  keeps the trees apart." This changes how the 2026-10-07 decision is displayed, not its sync rule.
- **testenv check:**
  - Hunters and Trilogy each show as one row, with a tab per emulator.
  - Restore a RetroArch version and check that the melonDS folder is unchanged.
  - Split Trilogy out, then group it back.

### 6 — Agent UI, and copying a save to another emulator

**Mockup:** [`phase6-mockup.html`](phase6-mockup.html). It shows the agent UI as the Windows tray and as the Deck in Game
Mode (controller hints), and the Copy to another emulator dialog. Its open questions are listed under the window.

- The agent UI (Deck and tray) shows titles the same way.
- **Copy this save to another emulator**, where the formats match (DS `.sav` ⇄ RetroArch `.srm`, Dolphin ⇄
  PrimeHack). This needs its own plan and its own decision.
