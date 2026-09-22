# Checkpoint — implementation plan

How to build what [[plan]] specifies, and an honest list of everything the prototype shows that does
not exist yet. Phases are ordered so each one ships on its own and leaves the app working.

Read [[plan]] first for tokens, type, motion and the colour rule, and
[[implementation-grouping]] before starting any phase — it regroups the list below **by surface**
rather than by phase number, because several phases edit the same components.

## Status (updated 2026-09-22)

| Phase | Status |
|---|---|
| 1 — Design system foundation, web half | ✅ Shipped 2026-09-17 (Group 1); theme default corrected 2026-09-20 (dark base, light opt-in — see `implementation-grouping.md`) |
| 1 — Design system foundation, agent half | ✅ Shipped 2026-09-20 (Group 3) — `tokens.css`, `ui.css`, Archivo, `components/ui/` |
| 2 — Console shell | ✅ Shipped 2026-09-18 (Group 2); sign-in moved to revocable sessions 2026-09-20 |
| 3 — Sync all and progress | ✅ Items 1, 2, 4 shipped 2026-09-18 (Group 2 — console side); item 3 (agent Sync all + progress) shipped 2026-09-20 (Group 3); item 5 (per-game Sync this game) ✅ shipped 2026-09-21 (Group 4) with the game page and a new per-game agent route |
| 4 — Appearance, and syncing it to the fleet | ✅ Shipped 2026-09-21 (Group 5); item 4 — the Deck's accent ➡️ shipped 2026-09-22 (Group 6). The theme default now follows the OS (every hex colour left the views first). See `implementation-grouping.md` → Groups 5/6 |
| 5 — Agent UI | ✅ Shipped 2026-09-21 (Groups 3–4): Overview trim, Games tab (list + grid), per-game page, art through the agent, Add-games search. Verified in a browser against the test rig; not verified in the WebView2 tray window or on a Deck |
| 6 — Deck and Wayland | 🚧 Items 1-3 ✅ shipped 2026-09-22 (Group 6) — verified live (real screenshots, pixel-sampled colours, `--nav`-scripted L1/R1 with `--nav-debug`) since `savelocker ui` runs on this Windows box without WSLg; a real focus-timing bug was found and fixed this way — see `implementation-grouping.md` → Group 6. No real Deck/gamescope pass yet. Item 4 (Wayland, 6.4) still needs the open decision below made first |
| 7 — OS notifications | ⏳ Not started (Group 7) |
| 8 — Assets | 🚧 Partially shipped 2026-09-17 (Group 1) — see the note under Phase 8 below |

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
| Release history table | UI only | `releases/index.ts` already has every version; render the full list under the three newest | ⏳ **Deliberately split off**, per `implementation-grouping.md`'s own pre-authorization to move this item out if size grows — `WhatsNewView.tsx`'s existing sidebar-click layout already technically exposes every release; turning it into "3 newest in full, a table of the rest below" is a distinct navigation redesign that shares no files with the rest of this group. Not started |

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
   nothing tested it, and the Deck cannot consume the look until this group. Write it with its caller and a test.
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
   dynamic, sourced from `AppearancePalette` via a new `AgentConfig.RefreshAppearance()`; `Safe`/`Watch`
   are fixed). Font face itself stays Inter/JetBrains Mono — no Archivo TTFs to embed in this
   environment, same asset gap as Phase 8.
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

---

### Phase 7 — OS notifications *(mostly New)*

`HealthReporter` already decides what is worth reporting; this is delivery.

- **Windows**: tray balloon exists today. Move to a proper toast (title, body, two actions) so
  "Resolve" can deep-link the console.
- **Linux/Wayland**: `DesktopEnvironment.cs` already probes `org.freedesktop.Notifications` and
  `NotificationDaemonPresent` — wire the actual notify call behind that check.
- **Headless**: unchanged. No session means no toast; those events reach the console badge and the
  audit log, which is the existing behaviour and the reason the bell menu matters.
- **Rules**: fire for conflict opened, lease held elsewhere, push failed after its last retry, pull
  refused, update staged, server unreachable past 5 minutes. Never for a successful push. A standing
  warning announces once, not per poll.

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
| Deck tile | 256 | `src/Agent.Linux/Ui/Art.cs` | ⏳ Not done — same rasterization gap; consumed by Group 6 |
| Library capsule | 600×900 | `store/` | ⏳ Not done |
| Wide capsule | 1920×620 | `store/` | ⏳ Not done |
| Header capsule | 460×215 | `store/` | ⏳ Not done |
| Library hero | 1920×620 | `store/` | ⏳ Not done |

All four Steam pieces are the same lockup at four crops, generated from whichever mark is selected —
keep them as SVG sources plus exported PNGs so an accent change is a re-export, not a redraw. The
marks are real SVG source files now; the four Steam crops are not yet built as files at all (only as
CSS/HTML mockup boxes inside `brand-kit.html`, which is a demo page, not an asset). Rasterizing any
of the above to PNG/ICO needs a tool this environment doesn't have — flagged here rather than
silently skipped, so whoever picks this up next knows to bring one (a local ImageMagick/Inkscape
install, or a `sharp`/`resvg` devDependency) rather than re-discovering the gap.

## Sequencing and risk

- Phase 1 gates everything. Do not start Phase 2 until the reset is layered and the primitives exist,
  or the inline-style problem simply reappears in new files.
- Phases 2, 3 and 5 are independent of each other once Phase 1 lands.
- Phase 4 touches the wire format: `AgentHeartbeatResponse` is today the single-field
  `record AgentHeartbeatResponse(ConflictEscalationDto[] EscalatedConflicts)`. Adding `appearance`
  to it means regenerating `src/Server/openapi.json` and `api-types.ts` in **both** front ends and
  committing all three, per `CLAUDE.md`.
- Phase 6 item 4 needs a decision before any code.
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
