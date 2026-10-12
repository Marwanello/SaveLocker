# Games tab — sorting & grouping: three options

**Status:** Option A implemented as v1 on `console-games-sort-group` (PR open). The two other options
stayed mockups — keep this file as the design history.

**Mockup:** [`mockup.html`](mockup.html) — open in a browser. Self-contained, clickable, all three
options live. The mockup still shows **Date added**; v1 deliberately does not (see below).

## Why

The console's Games tab (`web/src/components/GamesView.tsx`) showed one flat list in name order
(server returns `OrderBy(g => g.Name)`) or a wall, and the only persisted preference was List / Grid
(`sl_games_layout`, `web/src/components/game/gameState.ts`). Real libraries now mix Steam/Heroic/Playnite
games with saves from ten emulator families across PS2, GameCube, PSP, PS3, SNES, DS and ScummVM — and a
fleet of machines. Finding "what is on the Deck" or "which platform needs attention" meant scrolling.

## v1 as built (2026-10-12) — Option A "Toolbar"

Decisions taken with the maintainer before building:

- **Devices overlap** — a game on two machines appears under both; group counts sum above the library size.
- **No "Date added" in v1** — `Game` has no created column; adding one is a migration. Sorting offers
  Name / Recently synced / Size; grouping offers Date **synced**, not added. The column is the obvious
  follow-up (mockup's Shared notes carry the shape).
- **Rolling date buckets** — Today / Yesterday / Earlier this week (≤7 d) / This month (≤30 d) / Earlier.
- **Both layouts** — sidebar list (sticky headings) and grid wall (band headings).

### Server (additive; `openapi.json` + `web/src/api-types.ts` regenerated)

`GameStateDto` gained two trailing fields:

| Field | Meaning | Source |
|---|---|---|
| `machineIds` | Machines that have the game — a stored save folder, a recorded source, or the head uploader | `MachineSavePaths` ∪ `MachineGameSources`, batched in `GetOverviewAsync`; per-game queries on the single-state routes |
| `sources` | How the machines found it, distinct by kind+detail, ordered | `MachineGameSources`, same batching |

No migration, no new route. `GetGameStateAsync` keeps its per-game fallback so `/api/games/{id}/state`
answers the same fields without the overview's bulk load.

### Web

- `web/src/components/game/organize.ts` — pure grouping/sorting (Device, Platform with per-emulator
  sub-headings, Source, Date synced, Status) + `GameSummary` sort comparators. Games with no saves
  sort last in either direction.
- `web/src/components/game/OrganizeControls.tsx` — the two dropdowns (Group · value, Sort · value),
  keyboard-dismissible menus, direction row in the Sort menu, natural default direction per key.
- `web/src/components/game/gameState.ts` — `standingKey()` extracted so the Status grouping and the
  row's own dot can never disagree.
- `GamesSidebar` / `GamesGrid` — sticky, collapsible group headers (grid: full-width bands); with
  "None" the screen is exactly what it was before.
- `GamesView` — the arrangement persists in `localStorage` (`sl_games_organize`, validated value by
  value, unknown shape falls back to None/Name); collapsed headings are per-visit only (group ids die
  with the arrangement, a persisted set would silently apply to different headings).

Verified: server `dotnet build` clean (baseline warnings only); `web` `tsc -b && vite build` + `oxlint`
clean; `run-console-security-tests.ps1` **364/364** (run on :5225/:5226 — :5216 is a Windows reservation
on this box); `organize()` driven in Node against 9 fixtures (device overlap, GameCube → Dolphin +
PrimeHack sub-headings, source kinds, date buckets, null-last sorting) — all pass.

## Manual verification through `testenv` (nothing is verified in a real browser yet)

```powershell
# 1. From the repo root, a clean rig (a reused rig maps REAL save folders; always clean first)
.\tests\testenv.ps1 clean
.\tests\testenv.ps1 build
.\tests\testenv.ps1 up            # Windows tray + WSL agent + Docker console; prints the console URL

# 2. Seed something worth grouping: add the emulator fixture games and sync a few
.\tests\testenv.ps1 emu-fixture
#    In the console -> Add games: add the fixture games (they arrive through the WSL agent);
#    on the test tray: Sync All (or the header's Sync all in the agent UI)

# 3. The checklist (console, Games tab)
#    - None + Name: exactly the old list/wall, in name order.
#    - Group = Device: one heading per machine; a game on both sits under each.
#    - Group = Platform: PC first, console groups sorted; a console reached through two emulators
#      (GameCube: Dolphin + PrimeHack) shows emulator sub-headings; an emulator game with no release
#      falls under "Emulator".
#    - Group = Source: Steam / Heroic / Playnite / Emulator / Added by hand; unrecorded games land in
#      "No source recorded".
#    - Group = Date synced: Today / Yesterday / Earlier this week / This month / Earlier, "Never synced"
#      last (a freshly added game with no push).
#    - Group = Status: Conflict first, then Needs attention, Synced, No saves yet, Disabled — and the
#      group a game lands in matches its row dot.
#    - Sort: Recently synced + the arrow flips (oldest first); Size + arrow (largest/smallest); a
#      no-saves game stays last in every direction.
#    - Collapse a heading, switch List <-> Grid: the same headings, the grid shows sticky bands.
#    - Reload the page: the arrangement is remembered; collapsed headings reset.
#    - Sanity: Sync all, a queued command ("Queued"/"Syncing" chip) and counts behave exactly as
#      before with grouping on.
```

**Known not-verified / accepted for v1:** a real browser pass (this session has no browser tool —
everything above is a build + logic check), a screen reader/keyboard pass on the menus beyond Escape,
and "Date added".

## Original open questions — resolved in v1

1. ~~Which option~~ → **A** (Toolbar); B/C stay mockups.
2. Device model → **overlap** (under every machine).
3. Date buckets → **rolling windows**.
4. Collapsed headings → **reset per visit** (not persisted).
5. Sidebar list → **grouped too**, same arrangement as the wall.
