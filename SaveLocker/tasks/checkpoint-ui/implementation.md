# Checkpoint — implementation plan

How to build what [[plan]] specifies, and an honest list of everything the prototype shows that does
not exist yet. Phases are ordered so each one ships on its own and leaves the app working.

Read [[plan]] first for tokens, type, motion and the colour rule, and
[[implementation-grouping]] before starting any phase — it regroups the list below **by surface**
rather than by phase number, because several phases edit the same components.

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

1. **Fix the reset that forces inline styles.** `web/src/index.css` has an unlayered
   `* { box-sizing; margin: 0; padding: 0 }` which beats every Tailwind utility regardless of
   specificity — which is why `NavBar.tsx` and most of `GameDetail.tsx` are written with inline
   style objects. Move the reset into `@layer base` so utilities win, then the rest of this plan can
   use classes. Note the scale before planning a session around it: `web/src` has **388** inline
   `style={{` sites against 4 `className=`, so layering the reset *unblocks* utilities but converts
   nothing. The migration rides along inside later phases, one surface at a time — see
   [[implementation-grouping]].
2. Replace the `@theme` block with the Checkpoint tokens (both themes), and add the `color-mix`
   derivations. Keep the old token names as aliases for one release so nothing breaks mid-migration.
3. Swap the Google Fonts import: Archivo only (400/500/600/700). Drop Inter and JetBrains Mono from
   the import; keep a mono stack for `<code>`/`<pre>` in the help renderer.
4. Add the motion primitives — `rise`, `pop`, the shared easing variable, the
   `prefers-reduced-motion` block.
5. Build the shared primitives the rest of the phases assume: `Card`, `Chip`, `Button` (primary /
   quiet / alert / small), `Stat`, `Row` (the two-line grid), `Seg` (list/grid switch), `Toast`.
   These are new files under `web/src/components/ui/`.
6. **Decide how `agent-ui` gets the same tokens.** It has *no* Tailwind dependency and *no* `.css`
   file at all — it is 215 inline `style={{` sites and zero classNames — so there is no reset to fix
   there and no utility layer to unblock. Either add Tailwind to it, or emit the tokens as a plain
   CSS custom-property file both apps import. The plain file is recommended: the agent's existing
   inline styles can consume `var(--…)` immediately without being converted first. The two apps also
   differ on icons — `agent-ui` depends on `lucide-react`, `web` has no icon library.

**Verify:** `npm run build` in `web/` and `agent-ui/`, then load both and confirm no layout shifted
except type and colour.

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

### Phase 8 — Assets *(New, design work already done)*

Numbered last, but [[implementation-grouping]] pulls it forward into the first group: the art is
already designed, exporting it is cheap, and the favicon is the cheapest end-to-end proof that the
token and mark pipeline works.

Ship the three marks and the Steam art from the prototype as real files:

| Asset | Size | Where |
|---|---|---|
| Favicon | 32 / 180 | `web/public/` |
| Tray icon | 16/24/32/48 `.ico` | `src/Agent/AppResources.cs` |
| Deck tile | 256 | `src/Agent.Linux/Ui/Art.cs` |
| Library capsule | 600×900 | `store/` |
| Wide capsule | 1920×620 | `store/` |
| Header capsule | 460×215 | `store/` |
| Library hero | 1920×620 | `store/` |

All four Steam pieces are the same lockup at four crops, generated from whichever mark is selected —
keep them as SVG sources plus exported PNGs so an accent change is a re-export, not a redraw.

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
