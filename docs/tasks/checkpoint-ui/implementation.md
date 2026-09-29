# Checkpoint — implementation plan

How to build what [[plan]] specifies, and an honest list of everything the prototype shows that does
not exist yet. Phases are ordered so each one ships on its own and leaves the app working.

Read [[plan]] first for tokens, type, motion and the colour rule, and
[[implementation-grouping]] before starting any phase — it regroups the list below **by surface**
rather than by phase number, because several phases edit the same components.

## Status (updated 2026-09-29)

| Phase | Status |
|---|---|
| 1 — Design system foundation, web half | ✅ Shipped 2026-09-17 (Group 1); theme default corrected 2026-09-20 (dark base, light opt-in — see `implementation-grouping.md`) |
| 1 — Design system foundation, agent half | ✅ Shipped 2026-09-20 (Group 3) — `tokens.css`, `ui.css`, Archivo, `components/ui/` |
| 2 — Console shell | ✅ Shipped 2026-09-18 (Group 2); sign-in moved to revocable sessions 2026-09-20. The release-history table (split off) ➡️ Group 9 |
| 3 — Sync all and progress | ✅ Items 1, 2, 4 shipped 2026-09-18 (Group 2 — console side); item 3 (agent Sync all + progress) shipped 2026-09-20 (Group 3); item 5 (per-game Sync this game) ✅ shipped 2026-09-21 (Group 4) with the game page and a new per-game agent route |
| 4 — Appearance, and syncing it to the fleet | ✅ Shipped 2026-09-21 (Group 5); item 4 — the Deck's accent ➡️ shipped 2026-09-22 (Group 6). The theme default now follows the OS (every hex colour left the views first). See `implementation-grouping.md` → Groups 5/6 |
| 5 — Agent UI | ✅ Shipped 2026-09-21 (Groups 3–4): Overview trim, Games tab (list + grid), per-game page, art through the agent, Add-games search. Verified in a browser against the test rig; not verified in the WebView2 tray window or on a Deck |
| 6 — Deck and Wayland | 🚧 Items 1-3 ✅ shipped 2026-09-22 (Group 6) — verified live (real screenshots, pixel-sampled colours, `--nav`-scripted L1/R1 with `--nav-debug`) since `savelocker ui` runs on this Windows box without WSLg; a real focus-timing bug was found and fixed this way — see `implementation-grouping.md` → Group 6. No real Deck/gamescope pass yet. Item 4 (Wayland) ⏳ Group 10 (part 10d) — **decided 2026-09-28: option 1**, a chrome-less browser app window (see below); a short Deck measurement comes first |
| 7 — OS notifications | ✅ Shipped 2026-09-24 (Group 7) — shared rules, a real Windows toast and the generalised Linux notifier. Buttons are links to the agent UI, not callbacks (measured: Windows' toast host refuses freshly registered URL schemes); "Retry now"/"Install now" did not survive that. See Phase 7 below |
| 8 — Assets | 🚧 Partially shipped 2026-09-17 (Group 1). Remainder (Steam library art, PNG favicons, the installer icon — all still the pre-Checkpoint brand) ⏳ Group 10 (part 10c) |
| 9 — Console page kit and top bar | ✅ Shipped 2026-09-28 (Group 8a) — the `ui/` kit, pill tabs, the always-present bell with per-item actions, the SVG lock, the conflict pill, the full-width rail with Cancel (`POST /commands/cancel`, new `Cancelled` status). The Backups tab arrives with Group 9 |
| 10 — Console Games page | ✅ Shipped 2026-09-28 (Groups 8b + 8c) — split into `components/game/`, then re-laid out; no `alert()`/`confirm()`/`prompt()` left on the page (guarded by `run-appearance-consistency-tests`). Departures: see `implementation-grouping.md` → Group 8 |
| 11 — Backups tab and Configuration | 🚧 Items 11.1–11.5 and 11.8 ✅ shipped 2026-09-29 (Group 9a) — the Backups tab, status / download / settings routes, before-upgrade snapshot, KB article. 11.6–11.7 (Configuration) ⏳ Group 9b |
| 12 — Console Audit log, Help, What's new, sign-in | ⏳ Not started — Group 9 |
| 13 — Agent UI, completed | ⏳ Not started — Group 10 |
| 14 — Deck Game Mode, completed | ⏳ Not started — Group 10 |

## The 2026-09-27 audit — what the prototype shows that did not ship

Groups 1–7 shipped every phase this file listed, but several of those phases were scoped as the
**shell** — tokens, top bars, rows, primitives, Sync all. Comparing `prototype.html` screen by screen
with the code found that most of the *pages* behind the shell were never laid out to the prototype:
Group 5 migrated them to the tokens (every `#hex` gone, so the theme could follow the OS) and they
otherwise kept their pre-Checkpoint structure. The measure used: a page built from the primitives has
almost no inline styles, so a view that still carries dozens of `style={{` was recoloured, not redesigned.

| View | inline `style={{` | Laid out to the prototype? |
|---|---|---|
| `web` `GameDetail.tsx` | 116 | No — the old vertical stack of cards, 26 `alert()`/`confirm()` calls |
| `web` `ConfigView.tsx` + `AgentUpdatesCard.tsx` | 112 + 61 | No — one column of old-style cards, 31 more `alert()`/`confirm()` calls between them |
| `web` `AuditView.tsx` / `WhatsNewView.tsx` / `HelpView.tsx` | 22 / 10 / 9 | No — no search or machine filter in the audit log, no release-history table |
| `web` **Backups tab** | — | **Does not exist.** `BackupService` snapshots nightly and `GET /admin/backups` / `POST /admin/backup` exist, but nothing in `web/src/api.ts` calls them — a snapshot can only be seen or taken with curl |
| `web` top bar, sidebar, grid | — | Partly: tabs are buttons not pill tabs, the bell is a "⚠ N" text button that disappears when quiet, no progress rail or Cancel, the grid is squeezed into a 340 px sidebar |
| `agent-ui` `SettingsView.tsx` / `AddGamesView.tsx` / `ConflictCard.tsx` | 37 / 29 / 21 | Partly — no Activity tab, no hero "N of M"/Cancel/summary, no game-page stats or versions |
| Deck `savelocker ui` | — | Tokens, rows, legend and Sync all yes; the rail's sections, the header, the stat tiles, the rows' art and content, and two screens no |
| Wayland desktop window | — | Not started — the decision it waits on was never taken |

The gaps are **Phases 9–14** below, plus three older items still open: Phase 2's release-history table,
Phase 6 item 4 and Phase 8's remainder. All of it is grouped in [[implementation-grouping]] → Groups 8–10.

### Deliberate departures — not gaps, do not rebuild

Each of these differs from the prototype on purpose, and was decided when its group shipped.

- **Console Sync all is one command per machine** (Phase 3), so its progress counts machines, not games.
- **Notification buttons open a page; they never call back** (Group 7, measured on Windows 11).
- **The Deck rail's current entry is a neutral tile, not the accent** (Group 6), and **the Deck legend's
  glyphs stay neutral** where the prototype tints A/B/Y safe/accent/watch: the colour rule wins over the
  mockup — the accent means a decision is waiting and never decorates.
- **The unread-release-notes dot is `--safe`**, not the prototype's accent, for the same reason.
- **Real cover art, icons in list rows and the art picker** (2026-09-20) replace the prototype's generated
  gradient tiles; the gradient-with-initials tile survives only as the no-art fallback.
- **The conflict card keeps the conflict-resolution-ui look** (the approved mockup in
  `tasks/conflict-resolution-ui/`). Phase 10's inline resolve panel and Phase 13's Conflicts page wrap it;
  neither redraws it.
- **Prototype-only, drop:** the typeface-pairing switcher, "six marks" (three exist), the Decky Quick Access
  panel and Steam toasts (out of scope in [[plan]]), the Deck legend's `gamescope 1280×800 · Wayland`
  caption, and numbers the product does not have — playtime ("96h played"), "last push skipped 312 files".
  Do not invent a figure to fill a slot.

## A gap found while building Group 2 (2026-09-18)

Phase 3 item 1 below says console "Sync all" should "enqueue a command per tracked game for the
machine that owns it." That undersells what the wire protocol already does: `EnqueueCommandRequest.
GameId` is nullable, and `CommandPoller.ExecuteAsync`'s `TargetGames(cmd.GameId)` already treats a
null `GameId` as "every game this machine tracks" — the same mechanism the tray's own local "Sync
All" and the agent-ui's Sync-now already use. So "Sync all" needed **one `Sync` command per machine**
(`GameId: null`), never one per game — the agent's own poller does the per-game fan-out on receipt.
Building it the way the sentence originally read would have queued needless per-game commands the
wire was never designed to take. Implemented the simpler, correct way; verified live end to end (see
Phase 3 below) — a queued command really did sync every game on the machine that received it.

## A gap found and folded in before Group 1 started (2026-09-17)

Two sessions after this plan was written (2026-09-08/09, branch `easy-wins`, PR #35, unrelated to
this task — see `logs/2026-09-08_self-hosted-fonts/`), the console and agent UI both stopped loading
Inter/JetBrains Mono from Google Fonts and switched to self-hosted `@fontsource/*` npm packages
imported in each app's `main.tsx`, because SaveLocker is a self-hosted LAN service that has to keep
working with no internet reachable (`Decisions.md` → "Plain HTTP is the default... the threat model
is the household network"). Neither `index.html` carries a font `<link>` any more in either app.

Phase 1 item 3 below originally read "swap the Google Fonts import" — written before that fix
landed, and wrong twice over by the time Group 1 started: there is no Google Fonts import left to
swap, and reintroducing one to get Archivo would silently undo the LAN fix. The fix has been folded
into Phase 1 item 3 (self-host Archivo via `@fontsource/archivo` in both apps' `main.tsx`, same
mechanism) rather than left as a surprise for whoever picked up Group 1. It changes nothing about
which group does the work — Group 1 still does the `web` half, Group 3 still does the agent half —
only the mechanics of *how* each swaps its font.

## What already exists

Do not rebuild these — the prototype is a reskin of them, not a new feature:

| Prototype element | Already backed by |
|---|---|
| Problems / notifications list | `GET /admin/health/events`, `POST /admin/health/events/{id}/dismiss`, `HealthService` dedupe, and the existing dropdown in `web/src/components/NavBar.tsx` (severity colours, Info excluded from the badge) |
| Conflict resolve, keep-both, policies | `GET /conflicts`, `POST /conflicts/{id}/resolve`, `ConflictEscalationPolicy` |
| Versions, protect, prune, set-latest | `GET /games/{id}/versions`, version protect/delete endpoints, retention in `SyncService` |
| Per-game exclude patterns | `POST /games/{id}/excludes` + `GlobConfig`; the editor exists in `GameDetail.tsx` |
| Remote commands | `GET/POST /commands`, `AgentCommand` entity, `CommandPoller` (20s) |
| Save paths per machine, templates | `/games/{id}/paths`, `POST /agent/games/{id}/template`, `MachineSavePath` |
| Cover art | `ArtService` + SteamGridDB key, cached to `Storage:ArtRoot`, served at `/art/{gameId}/{kind}.jpg` |
| Backups tab | `GET /admin/backups`, `POST /admin/backup`, `BackupService` |
| Release notes | `web/src/releases/*.md` + `index.ts`, `WhatsNewView.tsx`, `versionSkew.ts` |
| Agent sync + live progress | agent `POST /api/sync`, `GET /api/activity` (phase + bytes), `SyncActivityStore` |
| Agent scan filters | `AddGamesView.tsx` — Suggested / All / Steam / Added to Steam / Heroic × Detected / Not detected × store |
| Deck gamepad UI | `src/Agent.Linux/Ui/` (ImGui): `UiApp.cs`, `Theme.cs`, `Widgets.cs`, `SettingsScreen.cs` |

## What does not exist yet

Grouped by phase. **New** = no code today. **Extend** = endpoint or component exists, needs work.
**UI only** = no server change at all.

---

### Phase 1 — Design system foundation *(UI only)*

Nothing user-visible changes except type and colour. Everything after this depends on it.
**The `web` half (items 1-5) shipped 2026-09-17 as Group 1**; item 6, the agent half, was a decision made
there and executed 2026-09-20 as Group 3 (see below).

1. ✅ **Fix the reset that forces inline styles.** `web/src/index.css` had an unlayered
   `* { box-sizing; margin: 0; padding: 0 }` which beat every Tailwind utility regardless of
   specificity — which is why `NavBar.tsx` and most of `GameDetail.tsx` were written with inline
   style objects. Moved into `@layer base` so utilities win. Note the scale before planning a
   session around any further conversion: `web/src` had **388** inline `style={{` sites against 4
   `className=` before this phase, so layering the reset *unblocks* utilities but converts nothing
   on its own — `NavBar.tsx` is the one surface Group 1 actually converted, to prove the utilities
   work; everything else still renders from its old inline styles until Groups 2-4 reach it. See
   [[implementation-grouping]].
2. ✅ Replaced the `@theme` block with the Checkpoint tokens (both themes: light is the `:root`
   default, dark applies under `prefers-color-scheme` or an explicit `data-theme` override for
   Phase 4/Group 5 to drive later), and added the `color-mix` derivations. The old token names
   (`--color-bg-global` etc.) are kept as aliases pointing at the new ones — they were dead code
   even before this change (grepped: never consumed by any component), kept anyway per the plan's
   own "one release" rule in case something starts reading them mid-migration.
3. ✅ **Self-host Archivo, not "swap the Google Fonts import".** This item's original wording
   assumed a Google Fonts `<link>`/`@import` that no longer exists by the time Group 1 started — see
   "A gap found and folded in" above. Both `web/src/main.tsx` now imports `@fontsource/archivo`
   (400/500/600/700) in place of `@fontsource/inter`; `@fontsource/jetbrains-mono` stays for
   `<code>`/`<pre>` and the help renderer, unchanged. No CDN import was reintroduced.
4. ✅ Added the motion primitives — `rise`, `pop`, `toast-in` keyframes, the shared `--ease`
   variable, and a `prefers-reduced-motion` block that turns all three off.
5. ✅ Built the shared primitives the rest of the phases assume: `Card`, `Chip`, `Button` (default /
   primary / quiet / alert, default / sm sizes), `Stat`, `Row` (the two-line grid), `Seg` (list/grid
   switch), `Toast` — new files under `web/src/components/ui/`. Each carries a visible
   `focus-visible` ring (`outline: 2px solid var(--color-accent)`), per plan.md's Deck-derived focus
   spec and this phase's own "keyboard focus visible" definition of done.
6. ✅ **How `agent-ui` gets the same tokens — decided by Group 1, executed 2026-09-20 by Group 3.** It has
   no Tailwind dependency, so the tokens are a plain CSS custom-property file imported from
   `agent-ui/src/main.tsx` the exact way that file already imports its `@fontsource/*` CSS
   (`import './tokens.css'`) — no second mechanism. Because `web` and `agent-ui` are two independent npm
   packages with no shared workspace, `agent-ui/src/tokens.css` is a hand-kept copy of `web/src/index.css`'s
   `@theme` block (same names, so the two diff line for line), with a header comment saying so; it is dark by
   default with `data-theme="light"` as the opt-in, mirroring `web`. Alongside it, `agent-ui/src/ui.css`
   holds the reset, the motion keyframes and the `sl-` classes behind the new `components/ui/` primitives
   (`Button`, `Card`, `Chip`, `Stat`, `Banner`, `Toast`): hover, active and focus-visible cannot be inline
   styles, and there is no Tailwind to write them as utilities. Fonts: `@fontsource/archivo` 400/500/600/700
   in place of Inter, `jetbrains-mono` kept. The two apps still differ on icons — `agent-ui` uses
   `lucide-react`, `web` has none. The inline styles in the views this group did not touch (Add games,
   Settings, Conflicts, the plugin cards, the pop-up) are unchanged and still hardcode the old palette.

**Verify:** `npm run build` in `web/` — passes (`tsc -b && vite build`, clean); `npm run lint`
(`oxlint`) clean. Loaded live against a real throwaway server via `tests/testenv.ps1 build/up -Only
console`: `NavBar` renders in both themes (confirmed the resolved `--color-*` values under
`prefers-color-scheme: light` and `dark` match plan.md's tables exactly), the favicon serves as
`image/svg+xml` and 200s, `getComputedStyle` on a focused nav button shows `Archivo` as the resolved
font and a 2px solid accent-coloured outline. No layout shifted beyond `NavBar`, which is the one
surface converted. `agent-ui` untouched this phase — its half of item 6 is Group 3's job.

---

### Phase 2 — Console shell — ✅ shipped 2026-09-18 (Group 2), one item deliberately split off

| Item | Kind | Work | Status |
|---|---|---|---|
| Two-line rows everywhere | UI only | Replace the sidebar rows in `GamesSidebar.tsx` | ✅ Shipped — `GamesSidebar.tsx` now renders the `ui/Row` primitive |
| Games grid wall + list/grid switch | UI only | New `GamesGrid.tsx`; persist choice in `localStorage` (`sl_games_layout`) | ✅ Shipped — `Seg` switch in the sidebar header, verified live: toggling flips the sidebar between 220px list and 340px grid and persists across reload |
| Cover art in list and grid | UI only | No server work: `ArtService.Assets` already fetches `grid` at exactly `dimensions=600x900` plus `hero`/`logo`/`icon`, and `GridUrl`/`HeroUrl`/`LogoUrl`/`IconUrl` are already on the game DTO. Only a fallback tile for when SteamGridDB has nothing is new | ✅ Shipped — confirmed no server work was needed; the "no art" fallback tile verified live |
| Notifications bell + menu | Extend | Rebuild the `NavBar` dropdown as `NotificationsMenu.tsx`; keep the existing badge rule (Info never colours it). New: per-item actions that deep-link (conflict → that game with the resolve panel open; `savedir.missing` → that game's folder field), and Dismiss all (loop the existing dismiss endpoint) | ✅ Shipped as `NotificationsMenu.tsx`. The deep-link is honest about its actual reach: it navigates to Games and selects the named game (a conflict's card is already unconditionally open on `GameDetail`, so that fully covers it); `savedir.missing` lands on the right game's page but does not scroll/focus the folder field specifically — that finer targeting isn't built. Not exercised live this session (no real problem/conflict event existed in the seeded test data) — verified by build + code review only |
| Lock button + sign-in screen | **New** | Remove the password field from the header. `SignIn.tsx` renders whenever no credential is held or the server returns 401. Two options: (a) UI-only — keep `X-Admin-Password` in `localStorage`, the screen is just where you type it; (b) proper session — new `POST /admin/session` returning a signed cookie with a 30-day option, and `Tokens.cs` gains verification. (a) ships in a day and is honest; (b) is the right end state. Do (a) now, file (b) as a follow-up | ✅ Shipped as option (a). Verified live end to end: Lock → SignIn full-screen → Connect with a blank password (this test server has none set) → back to the authenticated app. Option (b) remains a follow-up, not started |
| Exclude patterns as chips | Extend | Same `POST /games/{id}/excludes`; chips + add field + "preview what is skipped" (needs a dry-run count — either compute client-side from the last version's file list via `/versions/{id}/stats`, or add `GET /games/{id}/excludes/preview`) | ✅ Shipped. `/versions/{id}/stats` turned out to carry no file list at all (`VersionStatsDto(int FileCount, DateTime? NewestFileWriteUtc)` — count only), so this needed the second option: new `POST /games/{id}/excludes/preview`, backed by `SyncService.PreviewExcludesAsync` reading the head archive's own zip directory and running it through the same `Matcher`-based filter the agent uses (extracted from `SaveArchive.EnumerateRelativeFiles` into a new public `SaveArchive.FilterExcluded`, so the preview can never drift from what the agent actually does). One-directional by construction — it can only ever find newly-caught files among what's currently tracked, never ones an already-saved pattern already hides — and the UI says so ("would **additionally** exclude"). Verified live: added `**` as a draft pattern against a real one-file seeded save and got back "Would additionally exclude 1 file", matching exactly |
| Server default excludes editor | Extend | `Sync:DefaultExcludeGlobs` already exists in settings; surface it in Configuration and show it as inherited chips on each game | 🚧 Shipped as **read-only display**, not a true editor — `Sync:DefaultExcludeGlobs` turned out to be `IConfiguration`-only (appsettings.json/env var), never wired into `SettingsService`'s DB-backed override the way the SteamGridDB key is, so there was nothing to write to yet. Both a new Configuration card and each game's own chip list now show it; making it console-writable is a real follow-up, not built here |
| Release history table | UI only | `releases/index.ts` already has every version; render the full list under the three newest | ⏳ ➡️ **Group 9** (Phase 12 item 3, 2026-09-27). **Deliberately split off**, per `implementation-grouping.md`'s own pre-authorization to move this item out if size grows — `WhatsNewView.tsx`'s existing sidebar-click layout already technically exposes every release; turning it into "3 newest in full, a table of the rest below" is a distinct navigation redesign that shares no files with the rest of this group. Not started |

---

### Phase 3 — Sync all and progress — ✅ items 1–4 shipped (console 2026-09-18, agent 2026-09-20); item 5 ➡️ Group 4

1. **Console Sync all** *(Extend)* — ✅ shipped. **Not** one command per tracked game — see "A gap
   found while building Group 2" above: one `Sync` command per **machine**, `GameId: null`, which
   `CommandPoller.TargetGames` already fans out to every game that machine tracks. `POST
   /commands/bulk` added (`SyncService.EnqueueCommandsAsync`), taking the whole machine list in one
   call. Verified live: clicking Sync all queued one real command for the one seeded test machine,
   which the daemon picked up and completed within seconds — confirmed via `GET /commands` showing
   `status: "Done"` and a real result string.
2. **Console progress** *(Extend)* — ✅ shipped as `SyncAllProgress.tsx`, polling `GET /commands` on
   its own 2s timer (see item 4). No new server state — it just watches the ids the bulk call handed
   back until they're all `Done`/`Failed`.
3. **Agent Sync all** *(UI only)* — ✅ shipped 2026-09-20 (Group 3) as `StatusHeader.tsx`, the strip under
   the top bar on every agent page: a primary **Sync all** (`POST /api/sync`, single-flight on the agent),
   plus what is syncing now from `GET /api/activity` — a determinate bar with bytes and percent for a push
   (`bytesTotal > 0`), a sweeping indeterminate bar for a pull, a settle wait, or the gap between two games,
   because there is no honest percentage for those. It shows busy for a sync it did not start itself (the
   tray, a game exit) and disables the button then, since a second press would only be told a sync is already
   running. Not built, because there is nothing behind it: the prototype's "Cancel" (no agent endpoint) and
   "Syncing 2 of 6" (the activity snapshot names the current game, not the run's position in the list).
   The result is the server's own "Sync all complete." as a toast — `SyncAllAsync` returns no counts or byte
   totals, so the prototype's "Synced 6 games — 19.3 MB sent" is not derivable and was not invented.
4. **Do not re-render the page on a progress tick.** — ✅ satisfied by construction, on both sides.
   Console: `SyncAllProgress` owns its own poll and its own `doneCount` state; `NavBar` only ever hands it
   an immutable `commandIds` array set once per click, so a tick's `setDoneCount` re-renders nothing above
   it. Agent: `agent-ui/src/useActivity.ts` is one shared poll behind `useSyncExternalStore`; each hook
   subscribes to a single slice and a poll keeps the previous object for any slice that did not change.
   `StatusHeader` reads only `useActivityBusy()` (a boolean), so it re-renders when a sync starts or ends;
   only `HeroStatus` reads the per-tick `current`. Measured live over four ticks: 16 DOM mutations in the
   progress area, 0 in the Overview page, 0 around the Sync all button.
5. Per-game **Sync this game** on the agent's game page — ✅ **shipped 2026-09-21 (Group 4)**: `POST /api/games/{id}/sync` (`{mode: sync|push|pull}`, one at a time per game - a second press on the same game is a 409 - and deliberately not behind the global sync gate the launch routes share; never forced) over the new `SyncEngine.SyncGameAsync`. Originally moved here from Group 3 (2026-09-20): Two things
   this item assumed do not exist: an agent game page (Group 4 builds the Games tab it lives on), and a
   per-game sync route — `POST /api/sync` takes no game filter, and `pre-launch-sync`/`post-exit-sync` are
   launch-gate routes with their own single-flight and fail-open contracts, not a manual sync. So it is not
   "UI only" as listed: it needs `AgentApiServer` routes, an `agent-ui` `api-types.ts` regeneration and a
   per-game entry point (`SyncEngine.PushAsync`/`PullAsync` exist; `SyncAllAsync` loops them). Building a
   button in Settings' tracked-games list now would have been thrown away when the Games tab replaced it.

---

### Phase 4 — Appearance, and syncing it to the fleet *(New)*

The largest genuinely-new piece.

1. ✅ **Shipped.** Store the choice server-side in `AppSetting` via `SettingsService`: `Ui:Theme`, `Ui:Accent`,
   `Ui:Mark`, `Ui:PushToAgents`. `GET /settings` already returns settings; add these keys and a
   `POST /admin/appearance` (or reuse the existing settings write path). — Built as `ServerSettingsDto.appearance` +
   `POST /api/settings/appearance` (the "existing settings write path", beside `steamgriddb-key`); ids are validated against
   closed lists and settable from env (`Ui__Accent`); the wire carries ids, never colours.
2. ✅ **Shipped.** **Agents follow the server.** The heartbeat response (`POST /agent/health`) is the cheapest
   carrier — add an `appearance` object to it, so no new poll is needed. `AgentConfig` persists it
   to `config.json`, and the agent UI reads it from `GET /api/config`. — The agent UI reads **`GET /api/appearance`** instead
   (`AgentConfigDto` is read by the Decky plugin; this is polled by every page). Null on the heartbeat means "the console is not
   sharing" and an agent keeps what it last applied.
3. ✅ **Shipped.** Per-machine override: `Ui:Follow` in the agent's own config (`FollowConsoleAppearance`), exposed as **Follow the console** in
   agent Settings. When off, the agent keeps its local choice and ignores the pushed one — pushes are still *stored*, so
   turning it back on shows the console's current look. Turning it off changes nothing on screen.
4. ➡️ **Moved to Group 6.** The Deck UI reads the same config and maps the accent into `Ui/Theme.cs`. — `Theme.cs`'s `AccentGreen` is both the accent and the healthy colour at 68 sites, so this needs Group 6's token split. Ready for it:
   `AgentConfig.EffectiveAppearance`, `Agent.Core/AppearancePalette.cs`. **Group 6 must also add the refresh** — `savelocker ui`
   loads `config.json` once and never reloads it, so without one the Deck's accent only follows the console after a restart. An
   earlier `RefreshAppearance()` (adopt what is on disk, `AgentStateLock.TryAcquire` with a zero timeout because it runs from the
   render loop, raise `AppearanceChanged` when the effective look moved) was removed in the PR #47 review: nothing called it and
   nothing tested it, and the Deck cannot consume the look until this group. Write it with its caller and a test. **Done in Group 6:** `AgentConfig.RefreshFromDisk()` (the game list and the look from one read of the file), with `tests/SaveLocker.Agent.Tests/AgentConfigRefreshTests.cs`.
5. ✅ **Shipped, except the Deck header (Group 6).** App icon choice changes the favicon (`web/index.html` link swap), the tray icon
   (`src/Agent/AppResources.cs`, needs all three marks as embedded `.ico`), and the Deck header. — The favicon is redrawn in
   place (a data URL of the mark on an accent tile); the tray **and window** icons are drawn at runtime (`Agent/MarkIcon.cs`),
   so no `.ico` files and no rasterizer are needed; the in-app brand position in both top bars and the sign-in screen draws
   the chosen mark (`ui/Mark.tsx`).

---

### Phase 5 — Agent UI — ✅ Overview trim shipped 2026-09-20 (Group 3); Games tab, art and search shipped 2026-09-21 (Group 4)

| Item | Kind | Work |
|---|---|---|
| Overview trimmed to quick info | UI only | ✅ **Shipped (Group 3).** Three stats, one status banner, Next up, last three events. The stats are the three the agent really has (Tracked here, Saves backed up, Last sync) — the prototype's "Sent today" has no data behind it. The banner is one of: not connected → conflict → per-game lease warnings (each still dismissible) → "Nothing needs you." "Recent" expands in place to the full 50-entry log so the trim does not delete the only view of it. The launch-setup, Decky and Playnite cards moved to Settings, where the prototype puts them |
| **Games tab** | **New** | New view listing tracked games with cover art, grid or list, opening a per-game page: save size, last sync, versions, bytes sent last push, folder, process, launch command, Sync/Push/Pull. The **list renders from `GET /api/games` only**. `GET /api/games/{id}/sync-status` is *not* a list-view source — its own handler comment says it is "NOT cheap on disk: the local hash still walks and reads every file in the save folder," plus a full `GetStateAsync`. Polling it per game re-hashes every save folder on a timer. Allowed on a single opened game or an explicit "check now"; never on a timer, never for a whole list |
| Cover art in the agent | **New** | The agent cannot reach SteamGridDB. Either proxy `GET /api/games/{id}/art` through the agent to the server's `/art/...`, or have the agent UI point straight at the server URL it already knows. Proxy is better — it works when the browser cannot reach the server directly |
| Search in Add games | UI only | Filter by name and path, stacked on the existing filters |
| Two-line rows, motion, tokens | UI only | Same primitives as Phase 1 |

---

### Phase 6 — Deck and Wayland — ✅ Items 1-3 shipped 2026-09-22 (Group 6); item 4 still open

1. ✅ `src/Agent.Linux/Ui/Theme.cs` — Checkpoint dark tokens, 2px accent focus ring plus the 4px halo,
   62px rows, 16px minimum body text. Shipped as the `Safe`/`Accent` split Group 5 flagged (`Accent` is
   dynamic, sourced from `AppearancePalette` via a new `AgentConfig.RefreshFromDisk()`; `Safe`/`Watch`
   are fixed). The font face is Archivo + JetBrains Mono, matching the console and agent-ui (Archivo
   embedded 2026-09-23, once the two static TTFs were supplied; Inter is gone).
2. ✅ Two-line rows in `Widgets.cs`; the button legend along the bottom (A Select · B Back · Y Sync now ·
   L1/R1 Switch section · ☰ Steam menu). Shipped: `ListRow`'s two-line height is now a fixed 62px
   constant, the rail widened to 236px, and the legend gained the three new entries via a new
   `Widgets.GamepadHintWide` and `Icons.Menu`.
3. ✅ **Sync all in the Deck header**, bound to Y, using the existing sync path. Shipped: a visible
   primary button in the header (new `Icons.Sync` glyph) plus a global Y/L1/R1 gamepad binding
   (`HandleGlobalGamepadActions`) — Y triggers the same `SyncNowAsync()` the header button and the
   Overview's own "Sync now" share, L1/R1 step through the rail's screens in order.
4. Wayland desktop session: the agent window currently has no native chrome of its own. Either host
   the existing web UI in a small GTK/WebKit window with a header bar, or accept the browser. Decide
   before building — this is the one item in the plan with no obvious right answer.
   <br>**✅ Decided 2026-09-28 by the maintainer: option 1** (below). Group 10, part 10d, builds it; the Deck
   measurement at the end of this item comes first and only shapes the header bar (Window Controls Overlay or
   not). If the Deck has no Chromium-family browser, the launcher's `xdg-open` fallback is option 2 in effect.
   <br>**Options, written out 2026-09-27 (Group 10, part 10d, builds option 1).** Today Desktop Mode
   has no SaveLocker entry at all: the UI is reached by typing `localhost:5178` into a browser, or by
   clicking a notification (`DesktopNotifier.Open` → `xdg-open`).
   1. **A chrome-less browser app window — ✅ chosen.** A new `savelocker open` verb, and a `.desktop`
      launcher `install.sh` writes to `~/.local/share/applications/` (so SaveLocker is in KDE's menu — but
      not added to Steam: Steam shortcuts are what Game Mode shows, and there the gamepad `savelocker ui`
      stays the entry point; the two sit side by side over the same daemon), open `http://localhost:5178/` in a Chromium-family browser's
      `--app=` mode when one is installed — on a Deck usually a Flatpak (`com.google.Chrome`,
      `org.chromium.Chromium`, `com.microsoft.Edge`, `com.brave.Browser`) via `flatpak run` — and fall
      back to `xdg-open`. agent-ui draws the prototype's header bar (mark, "SaveLocker", "<machine> —
      desktop session", Sync all) only when it is not inside browser chrome (`display-mode: standalone`,
      or an `?app` flag the launcher passes); a real web manifest (today's `site.webmanifest` has an empty
      name and a white theme) makes it installable as a PWA from any Chromium too. Nothing new to ship
      or install, and the page is the same one every other surface uses. The notification click should go
      through the same launcher so it lands in the app window, not a new browser tab.
   2. **Accept the browser.** Ship only the `.desktop` launcher (`xdg-open`), no header bar. Cheapest.
   3. **`savelocker ui` in a window.** The ImGui Game Mode surface already opens as a native window in
      Desktop Mode. No dependency at all, but it is the gamepad UI, not "the same agent UI" the plan asks for.
   4. **WebKitGTK.** Already rejected in [[Decisions]] → *Linux UI* (Flatpak + WebKitGTK is 665 MB+, and
      SteamOS's immutable rootfs rules out a system package). Listed only so nobody re-proposes it.

   **Measure on the Deck before building option 1:** which Chromium-family browsers the maintainer's
   Deck actually has; whether `--app=` gives a window KWin decorates with only its own title bar; and
   whether that browser supports Window Controls Overlay on KDE. Without it, KWin's title bar sits above
   the in-page header — the header then drops the prototype's − □ × glyphs, which are KWin's to draw,
   not ours.

---

### Phase 7 — OS notifications — ✅ shipped 2026-09-24 (Group 7)

`HealthReporter` already decides what is worth reporting; this is delivery.

- ✅ **Windows**: a real toast (`src/Agent/ToastPresenter.cs`) — title, body, the brand mark in the current
  accent, a primary button and a dismiss. **Deviation:** the primary button opens the agent UI at the exact
  screen (`http://localhost:<port>/open?view=route`: the browser hits a page that asks the tray to raise its own window); it does not deep-link the console
  and it cannot call back into the tray. "Retry now" became "Open game" (the game page has *Push now*);
  "Install now" became a toast that names the tray menu's *Update to vX…*. Measured why: a registered custom
  URL scheme is refused by the toast host on Windows 11 25H2 (Discord's, Steam's and `ms-settings:` launch; a
  new one — ten variants tried — gets "Get an app to open this link"). The header's name and icon come from
  a Start-menu shortcut with the toast's AUMID, so `installer/SaveLocker.iss` now stamps one; the mark is
  written to `%TEMP%` (the only place the toast's AppContainer can read). The agent's TFM is now
  `net10.0-windows10.0.19041.0` for the WinRT projection (+~8 MB on the exe), output folder pinned.
- ✅ **Linux/Wayland**: `ConflictNotifier` (conflict-resolution Phase 9, hardware-verified) was already the
  freedesktop call — it is generalised into `DesktopNotifier`, keeping `notify-send --wait --print-id`
  exactly. One action button, not the mock-up's two (the verified shape; a second duplicates the dismiss).
- ✅ **Headless**: unchanged. No session means no toast; those events reach the console badge and the
  audit log, which is the existing behaviour and the reason the bell menu matters. A host that could not show
  a notification says so once in the log and tries again on the next poll (a Deck moving to Desktop Mode).
- ✅ **Rules** (`Agent.Core/Notifications.cs`, shared by both hosts): fire for conflict opened, lease held
  elsewhere, push rejected (`push.failed` — a network drop has no "last retry": the queue retries forever, and
  is covered by the next rule), pull refused, update ready, **server unreachable past 5 minutes** (measured in
  the poller, the one place that hears every failed round trip). Never for a successful push. A standing
  warning announces once per condition and is withdrawn when the condition ends. Everything else the engine
  reports stays in the log and the console.
- ➡️ **Not done, on purpose:** the Deck's Game Mode Steam toast (the Decky plugin, its own repo — out of scope
  in `plan.md`); a button that acts ("Retry now", "Install now") — needs a COM activator plus a shortcut
  carrying `ToastActivatorCLSID`, see [[Backlog]].

---

### Phase 8 — Assets *(New, design work already done)* — 🚧 partially shipped 2026-09-17 (Group 1)

Numbered last, but [[implementation-grouping]] pulls it forward into the first group: the art is
already designed, exporting it is cheap, and the favicon is the cheapest end-to-end proof that the
token and mark pipeline works.

Ship the three marks and the Steam art from the prototype as real files:

| Asset | Size | Where | Status |
|---|---|---|---|
| Marks (Cartridge, Pixel lock, Memory card) | vector | `web/src/assets/marks/*.svg` | ✅ Shipped — exact path data from `brand-kit.html`'s draw functions, body/punch fills as `var(--color-accent/on-accent, <fallback>)` |
| Favicon | SVG (scales) | `web/public/favicon.svg`, linked ahead of the existing PNG fallbacks in `web/index.html` | ✅ Shipped — Pixel lock, monochrome per brand-kit's own "no punch, one colour" rule for a tray-like context, on the Ember accent tile |
| Favicon | 32 / 180 PNG | `web/public/` | ⏳ Not done — the existing pre-Checkpoint PNGs are untouched; this environment has no SVG rasterizer (`magick`/`inkscape`/`rsvg-convert` all absent, confirmed) |
| Tray icon | 16/24/32/48 `.ico` | `src/Agent/AppResources.cs` | ✅ Shipped 2026-09-21 (Group 5) **differently**: drawn at runtime from the chosen mark and accent (`src/Agent/MarkIcon.cs`, GDI+, transparent punch-outs, light/dark taskbar aware, at the shell's own icon size) — so it needs no `.ico` and follows the Appearance setting. The packaged `SaveLocker.ico` remains the installer/exe icon and the fallback. Not reacting to a Windows taskbar-theme change while running ([[Backlog]]) |
| Deck tile | 256 | `src/Agent.Linux/Ui/Art.cs` | ❌ Dropped 2026-09-23 — the Deck header draws the live mark as vectors (`Ui/AppMark.cs`), so there is no raster to ship; `Art.cs` and `logo-96.png` are deleted |
| Library capsule | 600×900 | `packaging/linux/artwork/dist/capsule.png` | ⏳ Group 10 |
| Wide capsule | 920×430 | `packaging/linux/artwork/dist/capsule-wide.png` | ⏳ Group 10 |
| Header capsule | 460×215 | — | ❌ Dropped 2026-09-27 — Steam's *Set Custom Artwork* for a non-Steam shortcut takes no header capsule |
| Library hero | 1920×620 | `packaging/linux/artwork/dist/hero.png` | ⏳ Group 10 |
| Library logo | transparent, ≤1280×720 | `packaging/linux/artwork/dist/logo.png` | ⏳ Group 10 — not in the prototype, but it is the fourth file `install.sh` tells the user to set |
| Installer / exe icon | 16–256 `.ico` | `src/Agent/Assets/SaveLocker.ico`, `web/public/favicon.ico` | ⏳ Group 10 — still the pre-Checkpoint brand (the tray is drawn at runtime; the installer, the exe in Explorer and the `.ico` favicon fallback are not) |

**Corrected 2026-09-27:** the Steam art does not belong in a new `store/` folder. `install.sh` already ships
four PNGs from `packaging/linux/artwork/dist/` and tells the user to set them on the Game Mode shortcut
— but they are the **pre-Checkpoint brand** (the green-and-orange circuit-board lockup) at sizes that
are not Steam's own (782×430, 593×788, 1920×506). The Checkpoint lockups replace those files in place,
at Steam's sizes, with the wide capsule at 920×430 rather than the prototype's "1920×620 grid" (that
size is the hero's). The tiles follow the default mark and Ember: a Steam shortcut's art is a file on
disk, not something the Appearance setting can repaint.

All four Steam pieces are the same lockup at four crops, generated from whichever mark is selected —
keep them as SVG sources plus exported PNGs so an accent change is a re-export, not a redraw. The
marks are real SVG source files now; the four Steam crops are not yet built as files at all (only as
CSS/HTML mockup boxes inside `brand-kit.html`, which is a demo page, not an asset). Rasterizing any
of the above to PNG/ICO needs a tool this environment doesn't have — flagged here rather than
silently skipped, so whoever picks this up next knows to bring one (a local ImageMagick/Inkscape
install, or a `sharp`/`resvg` devDependency) rather than re-discovering the gap.
<br>**Decided 2026-09-27 for Group 10 (part 10c):** the `resvg` route — `@resvg/resvg-js` as a `web` devDependency behind
an `npm run export:art` script that renders the SVG sources (the marks, the four Steam lockups, the favicon
tiles) to every PNG and `.ico` above. A script in the repo makes the export reproducible on any machine and
in CI; an ImageMagick install on one laptop does not.

---

### Phase 9 — Console page kit and top bar *(UI only, one small route)*

Every console page after this is built from these. Without them each re-layout re-invents `style={{`.

| Item | Kind | Work |
|---|---|---|
| 9.1 Page primitives | UI only | New in `web/src/components/ui/`: `PageHead` (27 px title, mono sub-line, actions slot), `DataTable` (the prototype's `table.t`: mono uppercase heads, row rules, row hover, `k`/`m`/`n`/`wrap` cell kinds), `KV` (the `dl.kv` grid), `PathField` (mono inset path), `Banner` (accent / watch / safe: title, one line, action), `EmptyState`, `SearchField` (pill with icon), `FilterChips` (`fchip`, with counts), `Meter`, and `InlineConfirm` (10.6). CSS from `prototype.html`. `agent-ui` already has most of these as `sl-` classes (`sl-kv`, `sl-banner`, `sl-empty`, `sl-search`, `sl-meter`) — use the same names where they overlap |
| 9.2 Page canvas and motion | UI only | One `Page` wrapper every view renders into: 22/24 px padding, 16 px gap, and the `rise` entrance staggered over the first six children in 26 ms steps. Today only `SignIn` and two dialogs animate on entry |
| 9.3 Tabs | UI only | `NavBar`'s tabs become the prototype's pill tabs (transparent at rest, `--raise` on hover, `accent-soft` + `accent-line` when current), not `Button variant="selected"`. The **Backups** tab (between Audit log and Help) is added by Group 9 together with its page — a tab that opens nothing would be a dead end |
| 9.4 Bell | Extend | `NotificationsMenu` as drawn: a bell glyph (lucide `bell`) with a count badge — accent when any Error, watch otherwise, none for Info only — and **always present**: the quiet state is the menu's "Nothing to report / A healthy fleet is quiet…" empty state, where Group 2 hid the control. Header "Notifications" + an "N open" chip + Dismiss all; a severity dot instead of the severity chip; title, body, and a mono `code · machine · time` line; footer line + **Open audit log**. Per-item actions: `sync.conflict` → Resolve (today); `savedir.missing` → **Set folder** (opens the game *and* focuses that machine's folder field — Group 2 only opened the game); `push.failed` → **Retry** (queues a `Push` for that machine and game through the existing `POST /commands`); `update.staged` → no button, since the console cannot make an agent restart |
| 9.5 Header tools | UI only | The lock as an SVG (lucide `lock`), not 🔒. The conflict pill for **any** open conflict, with its age ("1 conflict · 4h", `accent-soft`) — today only escalated conflicts show, as "Overdue conflicts: N". Drop the ↻ button; every view already polls |
| 9.6 Progress rail and Cancel | Extend | The prototype's 3 px rail across the full width under the top bar (accent fill, width transition, the sweep) replaces the 80 px green mini-bar in the tools, which show a live-dot chip "Syncing 2 of 3 machines" and **Cancel** instead of the button. Cancel needs a route: `POST /commands/cancel { ids }` withdraws commands still waiting to be leased; one already dispatched runs to completion, and the toast says how many were withdrawn and how many were already running. New endpoint → regenerate `openapi.json` and `web/src/api-types.ts` |

---

### Phase 10 — Console Games page *(UI only)*

| Item | Kind | Work |
|---|---|---|
| 10.1 Sidebar | UI only | Header: a `GAMES · N` eyebrow and the list/grid `Seg` with its icons. **+ Add game** moves to a footer. The second line of a row becomes `last sync · size` (today `size · head id · leased by`); the end slot is a state dot — safe, watch (a machine reported a problem for this game), accent (conflict), faint (disabled) — and a live phase chip while a Sync all is working on that game's machine. The current row takes `accent-soft` + `accent-line` |
| 10.2 Grid wall | UI only | Grid becomes a full-width page (`split solo`), not a 340 px sidebar: `PageHead` "Games · N tracked · art from SteamGridDB" with the layout switch and Add game; tiles `minmax(184px, 1fr)`, cover inset 8 px, a Conflict / Retrying chip pinned top-right, a meta line (dot, last sync, size), the `pop` stagger and a 4 px hover lift. Opening a tile shows the game page with **← All games** |
| 10.3 Page head and stats | UI only | `PageHead`: the name; `steam:<appid> · N versions · size`; chips for state, policy and keep N; Push / Pull (per machine — an inline machine picker when there is more than one) and Refresh art. The cover and its pen/art picker stay. Then four `Stat`s: Latest version (+ when, from which machine), Stored (+ versions kept), Machines (+ names), Lease (Held / Free + holder and age, Force-release beside it) |
| 10.4 Conflict panel | UI only | A `Banner` ("Both machines wrote since vN", its age, **Resolve**) that expands in place into the resolve panel: the two sides as the conflict card already draws them, Keep this save / Keep both, and the "set a policy" hint. Replaces today's per-conflict cards and keeps their semantics — one per open conflict, never a pre-selected side |
| 10.5 Two-column body | UI only | `grid31`. Left: **Versions** — a `DataTable` (version, when, machine, size, a Latest / Conflicting / Protected / Kept chip, Set as Latest), an "N kept" chip and **Prune N versions** naming the real count (today "Prune now"); the Backups sub-list stays behind a `Seg`. Right, stacked: **Save folders** (per machine: path, last upload, Push / Pull, Use as template, edit — merging today's separate *Machines* table and *Save paths* card); **Rules** (conflict policy + preferred machine, keep N, both editable in place); **Exclude patterns** (the shipped chip editor restyled: own chips, dashed inherited chips, add field, Preview, **Glob syntax** → Help `glob-patterns`). **Remote commands** full width below: a `DataTable` with state chips, and an `EmptyState` |
| 10.6 No modals | UI only | Each of the 26 `alert()` / `confirm()` calls in `GameDetail.tsx` (18 / 8) (delete a version, Set as Latest, resolve, prune, force-release, delete the game, template, unprotect…) becomes `InlineConfirm`: the control expands into its consequence sentence and one button that names the effect — "Prune 5 versions", "Keep the Deck save". Failures go to a `Toast`, not an `alert` |
| 10.7 Split the file | — | `GameDetail.tsx` (58 KB) splits into one component per card under `components/game/`; the page file only lays them out. That split is also what keeps Group 8 reviewable |

The add-game dialog stays a dialog: plan.md's no-modals list is conflicts, pickers, enrollment and
resolution, and adding a game is a form about something not yet on screen.

---

### Phase 11 — Backups tab and Configuration *(New + Extend)*

The only page the prototype has that the console lacks outright. `BackupService` has taken nightly
`VACUUM INTO` snapshots since the start, and nobody can see them without curl.

| Item | Kind | Work |
|---|---|---|
| 11.1 Backups page | New (UI) | `BackupsView.tsx`: `PageHead` "Backups · nightly snapshots of the database · keeps the newest N", a last-run chip (safe when the newest snapshot is under ~26 h old; watch when older or the last run failed, with the reason) and **Back up now** (primary; `POST /admin/backup`, answered in the past tense). Four `Stat`s: Snapshots (+ oldest), On disk (+ the backup folder), Archives (+ "not included in snapshots" — a snapshot is the version graph, not the save files, and the page must say so), Next run (+ a countdown, or "Scheduled backups are off"). **Recent snapshots**: a `DataTable` of file, taken, size, a Nightly / Manual / Before upgrade chip, and **Download** |
| 11.2 Backup status | Extend | `GET /admin/backups` grows to `{ enabled, retentionCount, hourOfDay, nextRunAt, backupRoot, lastError, archivesBytes, backups[] }` (or a sibling `/admin/backups/status` if changing the list's shape is too much churn); `BackupInfo` gains `Reason`. The reason rides in the file name after the timestamp (`savelocker-20260927-030000-manual.db`), so the ordinal newest-first sort and the `savelocker-*.db` prune pattern keep working untouched, and existing names read as Nightly. The scheduler keeps its last failure for the page |
| 11.3 Download | New | `GET /admin/backups/{file}` — admin only, the name matched against the listing and never joined into a path, streamed. **A snapshot holds every machine's API-key hash, the admin password hash, the session-token hashes and the SteamGridDB key in plain text** (the console only ever shows it masked), so it is audited (`backup.download`), sent `Cache-Control: no-store`, and fetched by `api.ts` with the session header into a blob — never a bare `<a href>`, which cannot carry `X-Admin-Session`, and never a credential in a query string. The page says what the file contains beside the button. `run-console-security-tests.ps1` gains: unauthenticated → 401; `..`, absolute and percent-encoded names → 404; a real-looking name not in the listing → 404; the audit row written |
| 11.4 Before-upgrade snapshot | New | At startup, **before** the schema fix-ups and `db.Database.Migrate()` (`Program.cs`, ~L160–224), take a `before-upgrade` snapshot when the running build differs from the version recorded at the last start (an `AppSetting`, written after a successful start). Migrations are the one moment the server itself can damage the DB, and today the only copy is last night's. Nothing to snapshot on a fresh install |
| 11.5 Backup settings | Extend | `Backup:Enabled` / `RetentionCount` / `HourOfDay` become DB-backed through `SettingsService` (DB overrides appsettings, like the SteamGridDB key), with an admin write route; `BackupScheduler` reads them each loop instead of once at startup. That is what makes the prototype's **Nightly database backup** toggle real |
| 11.6 Configuration layout | UI only | `PageHead` "Configuration · server settings · appearance · enrollment · storage" + a "Connected as admin" chip, then `grid2` rows: **Server** (public URL, storage root, build, the conflict-escalation window, a storage `Meter` — "X of Y across N games" — and the SteamGridDB key) beside **Appearance**; **Enroll a machine** (the shipped flow restyled, its history as a `DataTable` with Used / Expired chips) beside **Defaults & maintenance** (the agent auto-update toggle — today buried in `AgentUpdatesCard` —, the nightly-backup toggle from 11.5, default keep, settle, the default exclude patterns from 11.7, Change password); then **Machines** (`DataTable`: machine, platform, agent, a last-seen chip, games, Force-release lease), with **Agent updates** and **Admin password** restyled underneath. Their 31 `alert()` / `confirm()` calls (`ConfigView` 21, `AgentUpdatesCard` 10) become `InlineConfirm` and toasts, as in 10.6. The meter needs the volume's size: add `DriveInfo` total/free for the archive root to the settings DTO |
| 11.7 Editable default excludes | Extend | Phase 2 shipped these read-only because `Sync:DefaultExcludeGlobs` was never wired into `SettingsService`. Wire it (DB overrides config), add `POST /api/settings/default-excludes` running the same validation as the per-game route (the `..` pattern that once threw inside the matcher — [[Gotchas]] → *Web console*), and reuse 10.5's chip editor in the Defaults card. Agents already receive the effective list per game, so no agent change — assert that in the test rather than assume it |
| 11.8 Help | UI only | A KB article, `database-backups.md`: what a snapshot contains and does not (archives), where it lives, restoring one by hand (stop the container, swap the file, start it), and that a downloaded snapshot is as sensitive as the admin password |

---

### Phase 12 — Console Audit log, Help, What's new, sign-in *(UI only, three small extends)*

| Item | Kind | Work |
|---|---|---|
| 12.1 Audit log | UI only | `PageHead` "N of M events · newest first" with a `SearchField` (action, game, detail) and Export CSV; machine `FilterChips` (All + each machine); a `DataTable` whose action cell is `accent-ink` for failures and conflicts and `dim` otherwise; `EmptyState` "Nothing matches that filter". Filtering runs over the 200 rows `GetAuditLogAsync` returns, and the page says so when the cap is hit rather than implying it searched everything |
| 12.2 Help | UI only | `PageHead` "N articles · ships with the console, works offline" with the search in its actions; the `docs` grid — a 240 px article-list card (current article `accent-soft`) beside a prose card; `.help-content` restyled to the prototype's `.prose` (h5 eyebrows, the accent-ruled blockquote, `code` chips). Categories stay |
| 12.3 What's new | Extend | The three newest releases in full (`rel` rows) with **Running** / **Available** chips; the **Release history** table below them (Phase 2's split-off item — every entry in `releases/index.ts`); an **Agent versions** card — machine, agent version (heartbeat), installer hosted for its platform, and Current / Behind / Update staged. "Staged" needs `AgentHeartbeat` to carry an optional `StagedVersion`, appended like the fields before it. "vX available" for the console itself needs the newest release tag `AgentInstallerPollerService` already reads from the repo, exposed on `ServerBuildInfo` |
| 12.4 Sign-in | Extend | The prototype's two-column screen: the accent wash, "Unlock SaveLocker" + the one-password lead, a labelled field with its error sentence beneath, **Remember this browser for 30 days** (unchecked: the session token in `sessionStorage`, gone with the browser; checked: today's 7-day idle / 30-day max in `localStorage`), the host · version footer, a forgotten-password hint that names the real recovery path — there is no `savelocker-server passwd` verb, so either write that verb or point at the documented reset, not both — and the "While it is locked" aside. **Not** the prototype's live chips ("3 agents connected", "1 conflict waiting"): the lock screen is unauthenticated, and fleet state is not for a stranger on the LAN |

---

### Phase 13 — Agent UI, completed *(Extend: local routes + UI)*

| Item | Kind | Work |
|---|---|---|
| 13.1 Activity tab | Extend | A sixth nav entry, **Activity**: the full feed (the Overview keeps its short *Recent*), an **Offline queue** card listing what waits (game, queued at, size, attempts) with its empty state, and **Open agent.log**. New local routes: `GET /api/offline-queue` (reads `OfflineQueue` under `AgentStateLock` — the launch wrapper shares the file) and `POST /api/open-log` (Windows: select the file in Explorer; Linux: `xdg-open` with the borrowed session environment `DesktopNotifier.Open` already uses; no desktop: 409 carrying the path, which the page shows with Copy) |
| 13.2 Sidebar counts | UI only | Counts on Games (tracked), Add games (suggested candidates from the last scan — never a scan triggered by navigating), Conflicts (today) and Activity (warnings since it was last opened) |
| 13.3 Hero progress | Extend | "Syncing 2 of 6 — Hades II": the activity snapshot gains `index` / `total` during a Sync all. A phase · MB sent · % legend. **Cancel** — `POST /api/sync/cancel`, cooperative: the game in progress finishes or is abandoned before any byte of a restore is written, never mid-restore, and the rest are skipped. The done state "Synced 6 games — 19.3 MB sent · 2 uploaded, 4 already current" — `SyncAllAsync` returns per-game outcomes and bytes (`SyncEngine` already knows both; Group 3 left this out only because nothing returned them). Line two: machine · last push · settle Ns · offline queue N |
| 13.4 Overview | Extend | A "Sent today" stat (a per-day byte counter persisted in `config.json` beside the pushed-saves count), "of N on the server" under Tracked here, Next up's Queue line from the real queue, and Rescan library |
| 13.5 Game page | Extend | Four `Stat`s (save size here, last sync + policy, versions + keep, sent on the last push) and **Versions on the server** — the Backlog's *Agent game page: version list and bytes sent on the last push*: a `GET /api/games/{id}/versions` proxy of the server's per-game list, and the push's bytes recorded per game in `SyncEngine.PushCoreAsync`. The per-game actions the prototype puts on this page — Change folder (PathBrowser), Open folder, Set game process, Stop tracking — which today live only in Settings' tracked-games list; the Excludes and Launch rows under *On this device* |
| 13.6 Add games | UI only | The rest of the prototype: `fchip`-style filter chips with counts, the line explaining the current filter ("Everything except games Steam Cloud already backs up", plus "Matching "x" — n of m"), check rows with Steam Cloud / Detected / Not detected chips, and a footbar "N selected · they start syncing after the next time you quit each game" with Clear and **Add N games** (today "Enroll selected") |
| 13.7 Settings | UI only | Cards in the prototype's order: **Connection** (machine name, server URL, the TOFU pin as a chip — "Pinned on first connect" / "Not pinned (plain HTTP)" — from `ServerTrust`, Save, **Test connection**), Appearance (shipped), **Sync safety** (settle; start with Windows / at login; install agent updates automatically), then Steam launch setup beside the Decky and Playnite cards. The form's 37 inline styles go |
| 13.8 Conflicts page | Extend | `PageHead` "Sync paused · N conflicts · open 4h 12m" around the shipped `ConflictCard` (unchanged); a **Why did this happen?** card per conflict — which machine pushed which version when, on top of which base, all in the conflict DTO and its two versions; after the last one resolves, a **Recently resolved** table (game, kept, when, by), which needs the server's resolved conflicts on an agent route (`GET /agent/conflicts?resolvedSince=`) |

---

### Phase 14 — Deck Game Mode, completed *(C#, uses Phase 13's routes)*

| Item | Kind | Work |
|---|---|---|
| 14.1 Rail | UI only | The prototype's sections plus the one the Deck genuinely needs: Overview · **Tracked games** · Add a game · Conflicts · **Activity** · Settings. *Steam setup* moves into Settings, as agent-ui already did with its launch-setup card. L1/R1 follow the rail — `RailEntries` is already the single list both read |
| 14.2 Header | UI only | The mark plus the **SaveLocker** wordmark (accent "Locker"); a CONNECTED chip in place of today's eyebrow-and-text group; on the right, after Sync all + Y: the machine name, battery % (`/sys/class/power_supply/BAT*/capacity`, hidden when there is none) and the clock. New glyphs come from lucide's real path data through `SvgPath`, never hand-drawn |
| 14.3 Stats | UI only | Four tiles with sub-lines, as drawn: Agent Status (CONNECTED + server host), Games Tracked ("on this Deck"), Saves Backed Up (+ "X GB on the server", from the daemon), Last Sync (+ which game, "paused" when it is in conflict). Today: three tiles, no sub-lines |
| 14.4 Rows with art | Extend | Tracked-game rows as drawn: a 38 px cover, the name, `size · last sync · path`, an end chip (Conflict / Needs setup / Synced). The art is the real work: the daemon's `/api/games/{id}/art` proxy already serves it; the Deck needs a PNG/JPEG decode into a GL texture for `ImGui.Image` — a pure-C# decoder such as StbImageSharp (no native library; check the tarball size) — cached per game, released on screen change, with the initials tile as the fallback |
| 14.5 Tracked games and a game screen | Extend | The new rail section lists those rows; A opens a per-game screen: Sync this game / Push / Pull (the daemon's per-game `POST /api/games/{id}/sync`, Group 4), Change folder (the existing Set folder flow), the server's head and lease, and the versions from 13.5's route. Today a row does nothing unless the game is in conflict |
| 14.6 Activity screen | Extend | The full log and the offline queue, from 13.1's routes |

---

## Sequencing and risk

- Phase 1 gates everything. Do not start Phase 2 until the reset is layered and the primitives exist,
  or the inline-style problem simply reappears in new files.
- Phases 2, 3 and 5 are independent of each other once Phase 1 lands.
- Phase 4 touches the wire format: `AgentHeartbeatResponse` is today the single-field
  `record AgentHeartbeatResponse(ConflictEscalationDto[] EscalatedConflicts)`. Adding `appearance`
  to it means regenerating `src/Server/openapi.json` and `api-types.ts` in **both** front ends and
  committing all three, per `CLAUDE.md`.
- Phase 6 item 4 needs a decision before any code — the options and a recommendation are under Phase 6.
- **Phases 9–14 (2026-09-27):** Phase 9 gates 10, 11 and 12 (their pages are built from its kit);
  13 is independent of all three; 14 reuses 13's new local routes, so it follows 13. Five of them add
  routes — `POST /commands/cancel` (9), the backup status/download/settings and default-excludes routes
  (11), `StagedVersion` on the heartbeat and the newest tag on `ServerBuildInfo` (12), and the agent's
  offline-queue / open-log / cancel / versions / resolved-conflicts routes (13) — so each of those groups
  regenerates `src/Server/openapi.json` and the `api-types.ts` of whichever front end it touches, per `CLAUDE.md`.
- **The backup download (11.3) is the riskiest new route in the plan**: it hands a copy of every credential
  hash the server holds to whoever holds an admin session. Admin-only, audited, no query-string credential,
  and covered by `run-console-security-tests.ps1` before it merges — the same bar the artwork proxy was held to.
- **The before-upgrade snapshot (11.4) must run before `Database.Migrate()`**, or it snapshots the already
  migrated file and protects nothing.
- Watch the known gotchas: build the server with `--no-incremental` and stop the agent and server
  first (DLL lock); dev storage is `src/Server/localstate/`, never `data/`; the `@import` of the font
  stylesheet must stay above `@import "tailwindcss"` or it is silently dropped.

## Definition of done per phase

Each phase ends with: both front ends building clean, `dotnet test` green, the affected screens
checked in light *and* dark, keyboard focus visible on every new control, and — for anything with a
progress or empty state — that state actually reachable in the running app rather than only in the
prototype.

There is **no frontend test runner in either app** (no vitest, jest or playwright in either
`package.json`), so every UI claim above rests on a real browser check. There is no unit-test safety
net here; keep sessions small enough that a manual pass is actually feasible.
