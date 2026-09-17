# Checkpoint — implementation plan

How to build what [[plan]] specifies, and an honest list of everything the prototype shows that does
not exist yet. Phases are ordered so each one ships on its own and leaves the app working.

Read [[plan]] first for tokens, type, motion and the colour rule, and
[[implementation-grouping]] before starting any phase — it regroups the list below **by surface**
rather than by phase number, because several phases edit the same components.

## Status (updated 2026-09-17)

| Phase | Status |
|---|---|
| 1 — Design system foundation, web half | ✅ Shipped 2026-09-17 (Group 1) |
| 1 — Design system foundation, agent half | ⏳ Not started (Group 3) |
| 2 — Console shell | ⏳ Not started (Group 2) |
| 3 — Sync all and progress | ⏳ Not started (Groups 2/3) |
| 4 — Appearance, and syncing it to the fleet | ⏳ Not started (Group 5) |
| 5 — Agent UI | ⏳ Not started (Groups 3/4) |
| 6 — Deck and Wayland | ⏳ Not started (Group 6); the Wayland item (6.4) still needs the open decision below made first |
| 7 — OS notifications | ⏳ Not started (Group 7) |
| 8 — Assets | 🚧 Partially shipped 2026-09-17 (Group 1) — see the note under Phase 8 below |

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
**The `web` half (items 1-5) shipped 2026-09-17 as Group 1** — item 6 is a decision, made below but
not yet executed on the agent side; that execution is Group 3.

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
6. **Decided, not yet executed: how `agent-ui` gets the same tokens.** It still has *no* Tailwind
   dependency and *no* authored `.css` file — the font self-hosting fix (item 3's gap, above) means
   it now imports third-party `@fontsource/*` CSS in `main.tsx`, which is a real precedent: Group 3
   should emit the tokens as a plain CSS custom-property file and import it the *exact* same way
   (`import './tokens.css'` in `agent-ui/src/main.tsx`), rather than adding Tailwind or inventing a
   second mechanism. Because `web` and `agent-ui` are two independent npm packages with no shared
   workspace, that file is duplicated (not built from one shared source) — `agent-ui/src/tokens.css`
   carries a header comment pointing back at `web/src/index.css`'s `@theme` block as the value
   source, and the two are kept in sync by hand when a token changes. The two apps also still differ
   on icons — `agent-ui` depends on `lucide-react`, `web` has no icon library — unaffected by this
   decision.

**Verify:** `npm run build` in `web/` — passes (`tsc -b && vite build`, clean); `npm run lint`
(`oxlint`) clean. Loaded live against a real throwaway server via `tests/testenv.ps1 build/up -Only
console`: `NavBar` renders in both themes (confirmed the resolved `--color-*` values under
`prefers-color-scheme: light` and `dark` match plan.md's tables exactly), the favicon serves as
`image/svg+xml` and 200s, `getComputedStyle` on a focused nav button shows `Archivo` as the resolved
font and a 2px solid accent-coloured outline. No layout shifted beyond `NavBar`, which is the one
surface converted. `agent-ui` untouched this phase — its half of item 6 is Group 3's job.

---

### Phase 2 — Console shell

| Item | Kind | Work |
|---|---|---|
| Two-line rows everywhere | UI only | Replace the sidebar rows in `GamesSidebar.tsx` |
| Games grid wall + list/grid switch | UI only | New `GamesGrid.tsx`; persist choice in `localStorage` (`sl_games_layout`) |
| Cover art in list and grid | UI only | No server work: `ArtService.Assets` already fetches `grid` at exactly `dimensions=600x900` plus `hero`/`logo`/`icon`, and `GridUrl`/`HeroUrl`/`LogoUrl`/`IconUrl` are already on the game DTO. Only a fallback tile for when SteamGridDB has nothing is new |
| Notifications bell + menu | Extend | Rebuild the `NavBar` dropdown as `NotificationsMenu.tsx`; keep the existing badge rule (Info never colours it). New: per-item actions that deep-link (conflict → that game with the resolve panel open; `savedir.missing` → that game's folder field), and Dismiss all (loop the existing dismiss endpoint) |
| Lock button + sign-in screen | **New** | Remove the password field from the header. `SignIn.tsx` renders whenever no credential is held or the server returns 401. Two options: (a) UI-only — keep `X-Admin-Password` in `localStorage`, the screen is just where you type it; (b) proper session — new `POST /admin/session` returning a signed cookie with a 30-day option, and `Tokens.cs` gains verification. (a) ships in a day and is honest; (b) is the right end state. Do (a) now, file (b) as a follow-up |
| Exclude patterns as chips | Extend | Same `POST /games/{id}/excludes`; chips + add field + "preview what is skipped" (needs a dry-run count — either compute client-side from the last version's file list via `/versions/{id}/stats`, or add `GET /games/{id}/excludes/preview`) |
| Server default excludes editor | Extend | `Sync:DefaultExcludeGlobs` already exists in settings; surface it in Configuration and show it as inherited chips on each game |
| Release history table | UI only | `releases/index.ts` already has every version; render the full list under the three newest |

---

### Phase 3 — Sync all and progress

1. **Console Sync all** *(Extend)* — enqueue a command per tracked game for the machine that owns it.
   Client-side loop over `POST /commands` works today; a `POST /commands/bulk` taking a list is
   nicer and avoids 7 round trips. Add the bulk endpoint.
2. **Console progress** *(Extend)* — poll `GET /commands` and derive "3 of 7"; the rail under the top
   bar is the only new UI. No new server state needed.
3. **Agent Sync all** *(UI only)* — `POST /api/sync` and `GET /api/activity` already exist and
   already report phase and bytes; the header just has to render them.
4. **Do not re-render the page on a progress tick.** The prototype originally rebuilt everything on
   every tick and replayed all entrance animations — a real bug the maintainer caught. In React,
   progress must live in its own component subscribing to the poll, and the surrounding view must not
   be a dependency of it. This is a correctness requirement, not polish.
5. Per-game **Sync this game** on the agent's game page — same endpoint with a game id.

---

### Phase 4 — Appearance, and syncing it to the fleet *(New)*

The largest genuinely-new piece.

1. Store the choice server-side in `AppSetting` via `SettingsService`: `Ui:Theme`, `Ui:Accent`,
   `Ui:Mark`, `Ui:PushToAgents`. `GET /settings` already returns settings; add these keys and a
   `POST /admin/appearance` (or reuse the existing settings write path).
2. **Agents follow the server.** The heartbeat response (`POST /agent/health`) is the cheapest
   carrier — add an `appearance` object to it, so no new poll is needed. `AgentConfig` persists it
   to `config.json`, and the agent UI reads it from `GET /api/config`.
3. Per-machine override: `Ui:Follow` in the agent's own config, exposed as **Follow the console** in
   agent Settings. When off, the agent keeps its local choice and ignores the pushed one.
4. The Deck UI reads the same config and maps the accent into `Ui/Theme.cs`.
5. App icon choice changes the favicon (`web/index.html` link swap), the tray icon
   (`src/Agent/AppResources.cs`, needs all three marks as embedded `.ico`), and the Deck header.

---

### Phase 5 — Agent UI

| Item | Kind | Work |
|---|---|---|
| Overview trimmed to quick info | UI only | Three stats, one status banner, Next up, last three events |
| **Games tab** | **New** | New view listing tracked games with cover art, grid or list, opening a per-game page: save size, last sync, versions, bytes sent last push, folder, process, launch command, Sync/Push/Pull. The **list renders from `GET /api/games` only**. `GET /api/games/{id}/sync-status` is *not* a list-view source — its own handler comment says it is "NOT cheap on disk: the local hash still walks and reads every file in the save folder," plus a full `GetStateAsync`. Polling it per game re-hashes every save folder on a timer. Allowed on a single opened game or an explicit "check now"; never on a timer, never for a whole list |
| Cover art in the agent | **New** | The agent cannot reach SteamGridDB. Either proxy `GET /api/games/{id}/art` through the agent to the server's `/art/...`, or have the agent UI point straight at the server URL it already knows. Proxy is better — it works when the browser cannot reach the server directly |
| Search in Add games | UI only | Filter by name and path, stacked on the existing filters |
| Two-line rows, motion, tokens | UI only | Same primitives as Phase 1 |

---

### Phase 6 — Deck and Wayland

1. `src/Agent.Linux/Ui/Theme.cs` — Checkpoint dark tokens, 2px accent focus ring plus the 4px halo,
   62px rows, 16px minimum body text.
2. Two-line rows in `Widgets.cs`; the button legend along the bottom (A Select · B Back · Y Sync now ·
   L1/R1 Switch section · ☰ Steam menu).
3. **Sync all in the Deck header**, bound to Y, using the existing sync path.
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
| Tray icon | 16/24/32/48 `.ico` | `src/Agent/AppResources.cs` | ⏳ Not done — needs the PNG export above first, then embedding into the Windows agent (that wiring belongs to whichever group actually consumes it — Group 5's appearance/mark-switcher, per its own note that it "needs Group 1's marks to exist as real icon files") |
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
