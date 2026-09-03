# Task: Decky plugin — proper conflict resolution, not just refusals

Planned 2026-08-28; phase list expanded 2026-08-30 (this session, no separate task file — asked
directly whether any phase built a Decky-equivalent resolve popup on Windows or plain Linux,
outside Decky and outside a future Playnite plugin. It didn't: the earlier fork-blind plan already
had the answer — a Linux desktop/headless escalation ladder and a Windows in-app chooser — but it
had been left as reference material below, not folded into execution). From [[Backlog]] → High
priority. Supersedes the conflict-resolution-relevant parts of the earlier, fork-blind 8-document
pass at `reference/00-inventory.md` through `07-open-questions.md` (kept as reference, both now
alongside this file in this same `tasks/conflict-resolution-ui/` folder) for
everything **except** the Windows/Linux-headless UI material, which Phases 5–9 and 14 below now
fold in directly — see the *Scope note* at the end of this document. The conflict data model's
causal, non-clock-based detection and the Resolution API's layering are still unaffected and not
repeated here.

**Execute ONE phase per session, verify it, stop** — same discipline as
`logs/2026-08-15_decky-plugin.md`, the closest precedent for a plan spanning both this repo and the
separate `SaveLocker-Decky` plugin repo. Ten of the fourteen phases below are net-new as of
2026-08-30; the new numbering starts *after* Phase 4, deliberately, so Phases 0–4's existing
cross-references in [[CONTEXT]]/[[Backlog]] (which already cite them by number) do not need
updating — only Phase 5 onward is renumbered relative to the original 2026-08-28 plan.

## The problem

Today the Decky plugin's *only* way past a stuck sync is Force push/pull — a blunt override that
bypasses the server's own conflict bookkeeping. Concretely: `SyncEngine.PushAsync(force:true)` /
`PullAsync(force:true)` make `SyncService.PrepareUploadAsync` skip divergence detection outright
(`diverged = !force && …`), so a forced push past a real, already-flagged `ConflictFlag` never closes
that row, never protects the version it discards, and never tells the *other* machine involved to
catch up — it stays on its stale parent and will conflict again on its next save. Separately, there
is no conflict-aware UI anywhere in the plugin: `classifySyncOutput` folds "game running," "unsynced
local changes," "conflict," and "lock contention" into one generic `'blocked'` chip state, so a real
conflict looks exactly like a game merely being open elsewhere.

## What exists today — corrected against the real fork

Checked directly against `D:\Projects\SaveLocker\SaveLocker-Decky` (v0.2.3, forked from
`SkorcherX/SaveLocker-Decky`, origin `Marwanello/SaveLocker-Decky`), not assumed from the vault or
upstream's documented state. The fork is materially ahead of what the vault knew about — two merged
PRs (`05293a9`, `a0b5900`) added a Gaming Mode sync system the vault's own `logs/2026-08-15_decky-
plugin.md` never covered.

- **The chip/toast system** (`src/syncStatus.tsx`, `src/toast.tsx`). `ChipState { kind, text, pct,
  reason?, at }`, held in a **module-scope store**, not React state — deliberately, because Steam
  remounts `/library/app/:appid` from scratch on every navigation, and component-local chip state was
  tried first and lost the chip on exactly the moment it mattered most (returning from a play
  session). `kind` is `ToastKind`, shared verbatim with the toast system on purpose, so the chip and
  toasts read as one status language.
- **The library-page overlay** (`src/libraryOverlay.tsx`). `routerHook.addPatch('/library/app/
  :appid', …)` with a defensive two-step tree patch (mirrors the pattern real shipped plugins —
  SteamGridDB, Unifideck — use for this exact route, per its own doc comment), anchored on
  `appDetailsClasses.InnerContainer`'s class name (Steam mangles component `displayName`s in
  production builds, so class name is the only stable anchor), rendered `position: fixed` specifically
  so a shape mismatch can only look wrong, never break Steam's layout. Injects a chip and three
  `Focusable` Pull/Push/Sync buttons. A "sync on page open" feature re-checks on a time-windowed basis
  (not once-per-session — that was a real, hardware-found bug, fixed).
- **Gaming Mode launch interception** (`src/gamingSync.tsx`) — the most sophisticated piece, and the
  one the earlier fork-blind design got most wrong. Hooks `SteamClient.Apps.RegisterForGameActionStart`;
  on `'LaunchApp'`, synchronously (no `await` before this point, to beat Steam to `CreatingProcess`)
  calls `SteamClient.Apps.CancelGameAction(gameActionId)`, verifies the cancel actually won the race
  via `GetActiveGameActions()` (backs off without relaunching if it lost), awaits a real pull, then in
  a `finally` block **always** calls `SteamClient.Apps.RunGame(...)` — success, refusal, or exception
  alike. Verbatim design principle from its own doc comment: *"this plugin delaying a launch is
  acceptable, this plugin ever silently stranding one on a cancelled action without retrying is not."*
  A `selfRelaunching` guard prevents the re-triggered launch from re-entering the handler and
  cancelling itself forever. **Mutually exclusive, per game, with the separate C# launch-options
  wrapper** (`savelocker run -- %command%`, `src/Agent.Linux/ProtonRun.cs` in the main repo): if a
  game's launch options already carry the wrapper (`rows().appliedAt !== null`), this file explicitly
  does nothing for it, to avoid two independent pulls racing each other. This file exists for
  everything the wrapper can't safely compose with — Heroic, emulators, non-Steam commands.
- **The Force override** (`src/index.tsx`'s `Sync` panel) — a `ConfirmModal` ("Force push/pull?")
  is the only existing way past *any* refusal today, including a real conflict, and it shells out to
  `savelocker push/pull --force`, which is exactly the bookkeeping gap above.
- **Python backend** (`main.py`) — 18 existing `async def` methods, every one following one shape:
  read the 0600 `api-token`, `urllib.request` to `127.0.0.1:5178` with a 5s timeout, return
  `{"ok": True, "data": …}` or `{"ok": False, "reason": "unreachable"|"http-{code}"|"bad-response"|
  "no-agent"}` uniformly. No conflict-specific endpoint exists anywhere in this codebase yet.
- **What the fork's own session notes (`docs/session_summary.md`) already flag, unprompted**: a
  cheap "does my local save match the cloud's head, without downloading" check is a known, deferred
  gap — `GameStateDto.Head.ContentHash` exists server-side, nothing compares it against a local hash
  without a full pull. Also flagged: `CancelGameAction`'s race-winning reliability is **unverified on
  real hardware**, independent of anything in this plan.

**What the earlier, fork-blind design got wrong, corrected here**: it claimed no game-page route
patch or modal existed in this plugin, and recommended against building launch interception on
Decky's undocumented `SteamClient` API. Both are already built, already shipping, and already
hardware-tested here — the corrections above supersede that document's Decky-specific sections.

## Decisions taken at planning time (maintainer, 2026-08-27/28)

1. **Conflict resolution is always only in the agent; the server just stores.** The server never
   decides anything — it only moves the head when told to, and keeps whatever isn't the head as a
   labeled, recoverable backup. All resolution *logic* (evaluating a per-game policy, presenting a
   chooser to a human, deciding "mine or the cloud's") lives in `Agent.Core`, the one sync engine
   every host and every frontend already goes through. This reverses a piece of the earlier design,
   which had the server evaluate `NewestWins`/`PreferMachine` itself at ingest time.
2. **The comparison is always "my current local save vs. the cloud" — never device vs. device.**
   There is no bystander case: every device only ever compares itself to the server's current head.
   A conflict's "other side" is shown as "the cloud," with whichever machine last updated it as
   supporting context only, never the primary frame.
3. **A losing save becomes a separate, recoverable backup — not part of the regular version
   history.** "Keep both" means "also protect this as a downloadable backup, filed separately," not
   "leave it sitting in the normal Versions list." The dashboard gets a new "Backups" sub-menu, able
   to download a backup or promote it back to the head of the original tree.
4. **A genuinely confirmed conflict may now block a launch** — a deliberate change from the fork's
   existing "always launch, delay at most" philosophy, scoped narrowly: it fires *only* for a
   certain, already-detected conflict, never for "game running elsewhere" or any other refusal, which
   all still launch exactly as today. Firing this shows the resolve popup immediately and relaunches
   the game itself the instant a choice is made — never a dead end requiring a second Play press.
5. **"Keep both" ships as a simple toggle on the Deck's own resolve popup**, not dashboard-only.
6. **The cheap local-vs-cloud status check is built now**, alongside this work, since it needs the
   same kind of agent-side change and deploy cycle this whole pass already requires.
7. **Playnite gets the same launch-blocking behavior**, confirmed buildable (`IPlayniteAPI.
   StartGame(Guid gameId)` — "Starts game" — is a real, documented SDK method, so cancel → resolve →
   sync → relaunch works there exactly as on the Deck), even though its full plugin build-out is a
   later implementation pass.
8. **The escalation-ladder / non-Decky, non-Playnite UI work is folded into this plan, not left as
   deferred reference material.** Decided 2026-08-30, this session, on being asked directly whether
   a Decky-equivalent popup existed anywhere outside Decky/Playnite (it didn't — see the note at the
   top of this document). The Windows in-app chooser, the Linux Game Mode conflict screen, the
   shared `agent-ui` conflicts page, and the optional D-Bus desktop notification all become numbered
   phases (5–9, 14) below, so "everything has a way of working even without the Decky plugin or a
   Playnite plugin" is an explicit, planned deliverable rather than an implicit consequence of the
   API being frontend-agnostic.

## The corrected resolution architecture

`SyncService.IngestAsync` currently evaluates `Game.ConflictPolicy` itself (`NewestWins`/
`PreferMachine`) and moves the head unilaterally when it auto-wins, with no agent involved at all.
Under decision 1, this branch is **removed**. Every divergence unconditionally records or updates an
open `ConflictFlag` — the server no longer decides anything. `Game.ConflictPolicy`/
`PreferredMachineId` stay server-stored (one shared setting the whole fleet agrees on, editable from
the dashboard or the Decky settings page) but are now read and evaluated **by the agent**: whichever
agent's own push comes back `Conflict` fetches the policy, evaluates it locally, and — if it isn't
`Manual` — calls the resolve endpoint itself with the outcome it computed. A human resolving through
Decky/the CLI/a future Playnite dialog is the *same* code path with `Manual` as the "policy": the
agent still decides nothing on its own in that case, it only carries the human's choice to the same
mechanical resolve endpoint.

This still satisfies "one engine, many frontends, nobody reimplements conflict logic" — the shared
engine is `Agent.Core`, reached by every frontend (Decky's Python backend, a future Playnite plugin,
the CLI, `agent-ui`) through the existing local `:5178` API, never `SyncService` directly.

**The mechanical, server-side resolve**, given a conflict id and a winning version id:
1. Keeps the existing rewind-safety check (refuses to promote something older than the current head)
   — a server-authoritative fact the agent's own view could be stale on, kept as a validation, never
   a decision.
2. Moves the head to the winning version.
3. Protects the losing version **only if the caller asks to** (the "keep both" toggle) — no longer
   automatic, and no longer the only way the loser survives at all, since the loser is already a
   real, uploaded `SaveVersion` by the time a resolve is even possible (see "commit-before-choose"
   below) — the toggle only decides whether it's pinned against eventual retention pruning.
4. Queues the existing fleet-wide unforced-pull fan-out to every other machine syncing the game,
   **regardless of whether a human or an agent's own auto-policy evaluation triggered the call** —
   this is what lets a *second* machine learn about an auto-resolved conflict even though the
   decision happened entirely on the *first* machine's agent.

**Commit-before-choose is unchanged from the earlier design and still load-bearing**: whenever an
agent would otherwise just refuse a pull because of unsynced local changes, it first attempts an
ordinary push. Fast-forward or no-change resolves it with nothing further to do; a real divergence
becomes a genuine, hash-verified `ConflictFlag` with a real `SaveVersion` on both sides — never a
bare refusal with no recoverable local-side record.

**The Force push/pull bookkeeping fix** is unchanged in intent: a forced push landing while an open
conflict exists for that game now calls the same mechanical resolve path server-side, invisibly, with
no change to what pressing Force does or looks like.

## The dashboard's "Backups" sub-menu — no new schema needed

A version is "in the main tree" if it's an ancestor of the current head — walk `ParentVersionId`
back from `Game.HeadVersionId`. Anything else the game still has is a backup, almost always the
losing side of a past conflict. This is a **computed classification**, not a new column: confirmed
directly against `src/Server/Data/Entities.cs` — `SaveVersion.ParentVersionId`, `SaveVersion
.Protected`, and `Game.HeadVersionId` already carry everything this needs. The dashboard already
fetches the full version list and the head id (`GET /api/games/{id}/versions`), so splitting them
into "Versions" (the ancestor chain — what's shown today) and a new "Backups" tab (everything else)
needs **no new server endpoint at all** — purely a new client-side view in `web/src/components/
GameDetail.tsx`. The Backups tab reuses two endpoints that already exist —
`GET /api/versions/{versionId}/download` and `POST /api/games/{id}/set-latest` — so "download the
save" and "set one of it as the latest in the original tree" both work with zero new server-side
write logic.

## Server/agent changes

- `src/Server/Services/SyncService.cs` — remove the `NewestWins`/`PreferMachine` auto-win branch in
  `IngestAsync`; simplify `ResolveConflictAsync`'s protection logic to be toggle-driven.
- `src/Server/Program.cs` — new agent-group routes: `GET /api/agent/conflicts`,
  `GET /api/agent/conflicts/{id}`, `POST /api/agent/conflicts/{id}/resolve`,
  `GET`/`POST /api/agent/games/{id}/conflict-policy`, `GET /api/agent/games/{id}/sync-status`.
- `src/Shared/Contracts.cs` — `ConflictSideDto { VersionId, MachineName, CreatedAt, Size, FileCount,
  NewestFileWriteUtc, IsCurrentHead }`, `ConflictDetailDto { Id, GameId, GameName, Local, Remote,
  Count, CreatedAt, LastSeen, Escalated }` (`Local`/`Remote` naming already matches the local-vs-cloud
  framing — "Remote" is presented in every UI as "the cloud," with the machine name as supporting
  detail only), `ConflictResolveResult`, `SyncStatusDto { InSync, HasOpenConflict, ConflictId }`.
- `src/Agent.Core/SyncEngine.cs` — where the actual decision-making now lives: a new
  `PrepareLaunchAsync(game, ct) → LaunchGateResult { Decision: Proceed|ProceedSyncPaused|Blocked,
  Reason, OpenConflictId }`, plus a conflict-noticing path fed by an ordinary push's `Conflict`
  response that evaluates the fetched policy and auto-calls resolve when it isn't `Manual`.
- `src/Agent.Core/ApiClient.cs` — new methods mirroring `AcquireLeaseAsync`'s shape:
  `GetOpenConflictsAsync`, `GetConflictAsync`, `ResolveConflictAsync`, `GetConflictPolicyAsync`,
  `SetConflictPolicyAsync`, `GetSyncStatusAsync`.
- `src/Agent.Core/AgentApiServer.cs` — new local routes, same `Guard`/token pattern every existing
  route uses: `GET/POST /api/conflicts[...]`, `GET/POST /api/games/{id}/conflict-policy`,
  **`POST /api/games/{id}/pre-launch-sync`** (wraps `PrepareLaunchAsync` and performs the pull as its
  side effect, for a JS caller that can't call C# in-process), `GET /api/games/{id}/sync-status`.
- `src/Agent.Linux/ProtonRun.cs` — `ExecuteAsync` calls `PrepareLaunchAsync` in place of the bare
  `OnGameLaunchAsync` call, still inside the existing never-block-the-launch try/catch **except** now
  a `Blocked` decision is honored: an early return before `RunChildAsync`, the one place in this
  codebase a real launch refusal can happen today.

## Decky plugin changes

- `main.py` — new one-line `_request(...)` methods, matching the existing 18 exactly: `conflicts()`,
  `conflict(id)`, `resolve_conflict(id, keep_local, keep_both)`, `conflict_policy(game_id)` /
  `set_conflict_policy(...)`, `pre_launch_sync(game_id)`, `sync_status(game_id)`.
- `src/toast.tsx` — `ToastKind` gains `'conflict'`; a new `KIND_STYLE.conflict` entry visually
  distinct from generic `'blocked'` (a magenta-red, `FaCodeBranch` or similar "diverged" icon, vs.
  `'blocked'`'s amber lock). Flows automatically through `SyncChip` and toasts with no other change,
  since both already treat `kind` opaquely.
- **New `src/conflicts.tsx`** — a poller (structured `GET /api/conflicts`, never string-parsed CLI
  output — conflicts are now known facts, not something to infer from prose), reusing `gamingSync
  .tsx`'s existing game↔appId cache rather than re-fetching; chip-merge logic that never clobbers an
  actively-`'syncing'` chip; `openConflictResolveModal(conflictId)` and the resolve popup component
  itself (mockup below), calling the real resolve endpoint, never raw `--force`.
- `src/libraryOverlay.tsx` — `SyncChip` gains a conditional `onClick` → `openConflictResolveModal`
  when `state.kind === 'conflict'`.
- `src/index.tsx` — new `ConflictWarnings` panel in the QAM, structurally identical to the existing
  `LeaseWarnings` panel, so a conflict is reachable even without the library page open.
- `src/fullPage.tsx` — a per-game "Conflict policy" dropdown (Manual / Newest wins / Prefer this
  machine — the existing three values verbatim), same optimistic-then-refresh pattern as the existing
  `togglePull`.
- `src/gamingSync.tsx` — `handleGameActionStart` swaps its current `runPull` call for the new
  `pre_launch_sync` call in the launch path (see the exact sequence below); exports `gameIdToAppId`
  for `conflicts.tsx` to reuse the warm cache.

## Exactly when Decky blocks a launch, and the full sequence

**Trigger**: the player presses Play on a tracked game while the pre-launch check — gated on the
existing "sync before start" (`pullBeforeLaunchEnabled`) setting, unchanged — finds a genuine,
confirmed conflict. This applies **whether or not "sync on page open" is also on**: whether the chip
had already warned the player on the library page, or Play is the very first check this session,
the same sequence runs.

**One existing optimization needs a carve-out.** `gamingSync.tsx`'s launch handler currently skips
its own pre-launch check when a fresh "sync on page open" pull *just* ran for this game
(`pageOpenPullIsFresh`), to avoid pulling twice. That skip is only valid when the page-open check's
outcome was a plain success/no-op. **If the fresh page-open result was a conflict, the skip must not
apply** — Play must still cancel the launch and open the resolve popup, reusing the already-known
conflict detail from that fresh check rather than re-attempting the sync over the network.

**The sequence, once triggered**: Steam's launch is cancelled (the existing `CancelGameAction`/
race-check machinery, unchanged) → the resolve popup opens → the player picks a side → the app
performs the actual sync for that choice (push if keeping local, pull if keeping the cloud) →
**only then** does `SteamClient.Apps.RunGame(...)` start the game. The player never presses Play
twice — this is a *deferred* version of the existing "`finally` always relaunches" rule, not a
removal of it: the relaunch still always happens, it now just waits for the chosen sync to finish
first instead of firing immediately regardless of outcome.

**Playnite mirrors this exactly, and the missing piece is confirmed, not assumed** (`api.playnite
.link/docs/api/Playnite.SDK.IPlayniteAPI.html`): `IPlayniteAPI.StartGame(Guid gameId)` — "Starts
game" — is a real, documented method. `OnGameStarting` sets `args.CancelStartup = true` when a
conflict is found, the equivalent WPF dialog is shown via `PlayniteApi.MainView.UIDispatcher`, the
player picks a side, the app syncs, then calls `PlayniteApi.StartGame(args.Game.Id)` — no second
manual Play press there either.

**Resolve popup — mockup** (Decky, Big Picture-styled; matches `showModal`'s dark-panel convention,
`Focusable` cards navigable with D-pad left/right, A to choose, B to back out without launching):

```
┌──────────────────────────────────────────────────────────────┐
│  ⚠ Elden Ring — your save and the cloud have both changed     │
│                                                                │
│  You (and another device) both played this since you last     │
│  synced. Pick which save to keep — the other one is never      │
│  deleted right now, just set aside.                            │
│                                                                │
│  ┌─────────────────────────┐   ┌─────────────────────────┐   │
│  │  📱 THIS DEVICE          │   │  ☁ THE CLOUD             │   │
│  │  Played 12 minutes ago   │   │  Played 2 hours ago      │   │
│  │  14 files · 82 MB        │   │  14 files · 81 MB        │   │
│  │  Newest change: 12m ago  │   │  Newest change: 2h ago   │   │
│  │                          │   │  from "Living Room PC"   │   │
│  │  [ Keep This Device ]    │   │  [ Keep the Cloud ]      │   │
│  └─────────────────────────┘   └─────────────────────────┘   │
│                                                                │
│  ☐ Also keep the other one as a backup (never auto-deleted,   │
│     downloadable and restorable later from the dashboard)     │
│                                                                │
│              (B)  Decide later — don't launch yet             │
└──────────────────────────────────────────────────────────────┘
```

After a choice, the same panel collapses to a brief syncing state before the game starts:

```
┌──────────────────────────────────────────────────────────────┐
│  ⚠ Elden Ring                                                 │
│              ⟳  Syncing your choice…                          │
└──────────────────────────────────────────────────────────────┘
```

## Consequences of "block on confirmed conflict," stated plainly

A real, deliberate behavior change from the fork's own "always launch" philosophy — worth naming,
not burying. Mitigated two ways: it fires *only* for a certain, already-detected conflict — every
other refusal (game running, lock contention, a network hiccup) still launches exactly as today —
and the popup appears in the same moment the launch is refused, with the game starting itself the
instant a choice is made, so the player is never simply left confused with a cancelled launch and no
explanation.

## Implementation phases

**Phase 0/1 — Server + Agent.Core (must ship together, one release).** Removing the server's own
`NewestWins`/`PreferMachine` evaluation (Phase 0) before the agent-side replacement exists (Phase 1)
would be a regression — every game on those policies would silently stop auto-resolving. Safe to
*develop* separately (`SyncService.cs` vs `SyncEngine.cs`) but must merge and release together.
Delivers the mechanical resolve endpoint, agent-side policy evaluation, and the new
`GET/POST /api/conflicts[...]` local API — usable end-to-end from the CLI alone before any GUI work
below exists.

**Phase 2 — Force push/pull bookkeeping fix (server-side only).** Independently mergeable, depends
only on Phase 0/1's mechanical resolve internals, invisible to users, lowest-risk phase in this plan.

**Phase 3 — Dashboard "Backups" tab (client-side only).** Zero dependencies on anything else here —
needs no new server endpoint, just a new view over data that already exists. **Can ship first.**

**Phase 4 — Linux wrapper launch gate (`ProtonRun.cs`).** Depends on Phase 0/1. Independent of every
other phase below — can proceed in parallel with Phases 5–10 — except that Phase 8 later adds a pure
enhancement on top of it (bringing the Game Mode conflict screen to the foreground on `Blocked`, once
Phase 8 and Phase 5 both exist) and Phase 11 (Decky launch-gate wiring) depends on this phase's
`Blocked` decision already existing.

**Phase 5 — Linux environment-capability detection (small, standalone module).**
Depends on Phase 0/1 only. Pure, no behavior change on its own — prep work consumed by Phases 8 and
9 and by `doctor`. New module (e.g. `src/Agent.Linux/DesktopEnvironment.cs`) implementing rung 1 of
`03-platform-ux-flows.md`'s escalation ladder exactly as specified there:
- `$WAYLAND_DISPLAY`/`$DISPLAY` env vars (trivial) → is *some* graphical session present.
- `$DBUS_SESSION_BUS_ADDRESS` set **and** the socket actually connectable → a session bus exists.
- Querying `org.freedesktop.DBus` for whether `org.freedesktop.Notifications` is currently owned →
  distinguishes "bus present, no notification daemon" from "no bus at all," a real and distinct
  state the reference doc calls out explicitly (a Deck's Game Mode has a session but no notification
  daemon; Desktop Mode's KDE Plasma session has both).
- An attached, interactive TTY (`Console.IsInputRedirected`/an `isatty(0)` equivalent).
- Whether the current process is the `systemd --user` unit vs. an interactive CLI invocation
  (`$INVOCATION_ID`, or the process's own argv/TTY state — REPO_MAP already notes the daemon runs
  under `systemd --user` exclusively, so this is really "am I the unit or a human at a terminal").

Returns one `DesktopSessionInfo { HasGraphicalSession, HasSessionBus, NotificationDaemonPresent,
IsInteractiveTty, RunningAsSystemdUnit }` record from a single `Detect()` call — cheap enough to call
per-decision rather than caching, so a Deck switching between Game Mode and Desktop Mode is never
read stale.

**Verify:** unit tests feeding synthetic environment-variable/socket states (no real D-Bus needed to
exercise the "no bus" vs. "bus, nothing listening" distinction — that only needs a fake/absent
socket), asserting each of the five flags independently and in combination. Confirm on the real Deck
that Game Mode and Desktop Mode report differently from each other, since that's the entire reason
this phase exists rather than a single hardcoded assumption.

**Phase 6 — Shared `agent-ui` conflicts page (both hosts) + the `doctor`/log gap it closes.**
Depends on Phase 0/1 only — independent of Phase 5, can run in parallel. This is the single
highest-leverage new phase: `agent-ui` is served by **both** the Windows tray (WebView2) and the
Linux daemon (a plain browser at `:5178`), so one component built once gives two platforms a real
chooser surface simultaneously.
1. New `agent-ui/src/components/ConflictsView.tsx`, a fourth `Sidebar.tsx` nav entry (today's `NAV`
   array has only `overview`/`addGames`/`settings`; add `conflicts`, badge-numbered when the open
   count is non-zero, mirroring the dashboard's own conflict badge). This is genuinely new code, not
   a port of `web/src/components/GameDetail.tsx`'s conflict card — `agent-ui` has its own
   `api.ts`/`api-types.ts` generated from the **agent's own** OpenAPI (REPO_MAP: a distinct wire
   protocol from the dashboard's), so the fetch/DTO layer has to be written fresh even though the
   visual shape (machine, timestamp, size, file count, newest-change, Keep Local / Keep Cloud / Keep
   Both) is deliberately copied from the dashboard's already-shipped version for consistency.
2. Fetches `GET /api/conflicts` and posts `POST /api/conflicts/{id}/resolve` — both already live on
   the local API (`AgentApiServer.cs:632`, `:653`, shipped in Phase 0/1) — so this phase is
   client-only, no new server or local-API route.
3. **Closes a real, confirmed gap found while planning this expansion:** `Doctor.cs` has **no
   conflict output at all** today — Phase 0/1's own write-up never actually added the line the
   original reference plan called for (`05-phased-implementation-plan.md` Phase 1 item 5: "`doctor`
   gains a line per open conflict"). Add it now: one line per open conflict, plus — whenever a
   conflict is open — the URL to reach this new page (`http://127.0.0.1:5178/#conflicts` or
   equivalent), printed to both `doctor`'s output and `agent.log`. This is what makes rung 4 of the
   escalation ladder (the local web chooser, reached over an SSH tunnel on a headless box) actually
   *discoverable* rather than merely reachable — the reference doc is explicit that rung 2's desktop
   variant and rung 4 are literally the same surface, so this one page and this one doctor line cover
   both rungs at once.

**Verify:** extend `run-agent-tests.ps1` (already drives both hosts) with a scenario that opens a
real conflict and exercises the new route end to end — list, resolve, head actually moves — mirroring
`run-health-tests.ps1`'s existing conflict coverage. Manual check in a real browser against both a
running Windows tray and a Linux daemon at `:5178`, per this codebase's "verify live, not just built"
standard; confirm `doctor`'s new line and URL print correctly with a conflict open and disappear once
it's resolved.

**Phase 7 — Windows tray/agent UI: automatic chooser + bulk-queue wiring.**
Depends on Phase 6 (reuses `ConflictsView.tsx` as the single-conflict chooser) and Phase 0/1. This is
the phase that gives a plain Windows tray user — **no Playnite** — a real popup-at-the-moment-it-
matters experience for the first time.
1. Wire the four existing trigger points that already funnel through `SyncEngine` — `Sync All`,
   per-game Force Pull/Push (`TrayApp.cs`'s menu), `Sync now` (`OverviewView.tsx`/`ActivityCard.tsx`)
   — so that a result carrying `Conflict` raises `AgentWindow` (opening it if not already visible, via
   `UiDispatcher`, the single owning thread for every WinForms object per REPO_MAP) directly to the
   new Conflicts view, instead of requiring the user to notice a badge on their own.
2. Implement the bulk-operation queue from `03-platform-ux-flows.md`: **a queue, one conflict at a
   time, with an explicit "apply to all remaining" control that only appears after the first
   choice** — not a batched list of N independent judgment calls. Concretely: `Sync All` surfacing N
   open conflicts shows conflict 1 of N in `ConflictsView`; after the first resolve, a follow-up
   prompt offers "apply the same choice (`keep <machine>'s save`) to the other N-1 conflicts too?"
   (Yes/No/Review-each) — Yes resolves the rest via the same per-conflict resolve call, no new bulk
   endpoint needed.

**Verify:** a scripted multi-conflict scenario (three games, three divergent conflicts) driving
`Sync All` through the queue, confirming "apply to all remaining" resolves the other two via the same
API calls Phase 0/1 already covers server-side — the exact check the reference plan's own Phase 3
verification note describes.

**Flagged during Phase 8's review (2026-09-03), not yet built: no reusable version/stats
fetch-and-cache exists for a native (non-browser) surface.** Phase 8's `Ui/UiApp.cs` ended up
hand-rolling, in C#, the same "fetch a version + its stats once, cache forever, dedup in-flight
requests" protocol `agent-ui`'s `useConflictVersions.ts` already implements in TypeScript for the
browser surface — `Agent.Core`'s `ApiClient` (`GetVersionAsync`/`GetVersionStatsAsync`) is a bare
HTTP wrapper with no caching of its own. This phase's chooser needs the identical comparison (both
sides' machine/timestamp/size/file-count), so before wiring it, either reuse a small shared
`Agent.Core` cache component if one gets extracted from Phase 8's version first, or accept a third
from-scratch copy of the same protocol in `TrayApp.cs`/`AgentWindow`.

**Phase 8 — Linux Game Mode conflict screen (`savelocker ui`).**
Depends on Phase 0/1 only — independent of Phases 5–7, can run fully in parallel with any of them,
and with Phase 4. **This is the direct, literal answer to "a conflict popup like the Decky one, but
in the native Linux/Wayland UI, with no Decky installed"** — the whole reason this expansion exists.
1. New `Screen` enum value in `Ui/UiApp.cs`, drawn with the existing Dear ImGui/SDL widget set
   (`Widgets.cs`) and the existing gamepad-focus machinery (`ButtonDown`-driven nav, `Widgets.Hot()`)
   — no new rendering technology, matching both the reference plan's own recommendation and this
   codebase's standing "reuse the stack, don't add one" instinct already documented for this exact UI
   (`Gotchas.md` → ImGui/Deck UI).
2. Same two-sided comparison as the Decky mockup already specified earlier in this document —
   machine, timestamp, size/file count, newest-change — Keep Local / Keep Cloud as the two primary
   actions, a Keep Both toggle, and a "decide later" back-out that never silently defaults to a side.
3. Reachable proactively from the existing status screen whenever `GET /api/conflicts` (already
   shipped) returns a non-empty list for a tracked game (a small prompt/badge on that game's row) —
   no launch has to be attempted first to discover a conflict exists.
4. A pure enhancement layered on **after** this phase and Phase 5 both exist, not a dependency of
   either shipping first: once `DesktopEnvironment.Detect()` (Phase 5) can tell the wrapper a Game
   Mode session is the one running, `ProtonRun.ExecuteAsync` (Phase 4) can bring `savelocker ui`'s
   process to the foreground directly on this new screen on a `Blocked` decision, instead of merely
   refusing the launch with a log line. Phase 4 ships standalone first and keeps working exactly as
   scoped there if this enhancement never lands.

**Verify:** extend the existing `tests/linux/run-ui-wslg.sh` pattern (already drives `savelocker ui`
under WSLg) with a genuine conflict fixture, confirming the new screen renders and resolves through
the same local API Phase 6 already exercises; manual gamepad-navigation check on the real Deck (D-pad
moves focus, A activates, B backs out without resolving anything) — the identical requirement this
document already states for the Decky modal, applied here to the native screen instead.

**Two findings from Phase 8's review (2026-09-03), deliberately not fixed as part of that review's
minimal-edit pass — real, left for a future touch of this phase:**
1. **Shipped code deviates from this section's own item 3: it talks to the remote server directly,
   not through the local API.** `UiApp.cs`'s conflict polling builds its own in-process `ApiClient`
   straight to the remote SaveLocker server, not the `GET /api/conflicts` local route this plan
   originally specified (item 3 above) and Phase 6/7 both use. The PR's rationale (resolving a
   conflict is a stateless, mechanical server call, unlike "Sync now") is defensible, but it means
   `savelocker ui` and `savelocker daemon` (when both run, the normal Deck setup) now poll the same
   remote endpoint independently rather than the screen reading through the daemon's already-fetched
   state — the "view over shared state, not a second API client" rule `LeaseWarningStore`/
   `SyncActivityTracker` were built to. Worth reconciling before Phase 10/11 (Decky) copies whichever
   pattern this phase actually shipped.
2. **The whole screen (`PollConflictState`, `FetchOpenConflictsAsync`, `EnsureVersionLoaded`,
   `ResolveConflict`, `DrawConflicts`, `DrawConflictCard`, `DrawConflictSide` — ~250 lines, 10 fields)
   lives directly on `UiApp` instead of its own class.** `Ui/SettingsScreen.cs` is the established
   precedent for factoring a self-contained screen's state out of `UiApp` (held as a field, drawn via
   `_settings.Draw()`); this phase didn't follow it. A future pass extracting a `ConflictsScreen`
   class would also be the natural place to fix finding 1 above, since it's the same seam.

**Phase 9 — Desktop notification via D-Bus (optional).**
Depends on Phase 5 (must confirm a notification daemon is actually present before attempting a call —
this is precisely the distinction rung 1's detection exists to make) and Phase 6 (the action button's
target on a desktop session — Phase 8's Game Mode screen is the equivalent target on the Deck).

**Tooling decision made 2026-08-30 (Group 1), answering `07-open-questions.md` §3: `gdbus`, not
`Tmds.DBus` or a hand-rolled client.** Compared:
- `Tmds.DBus` — the most common .NET option, but a new NuGet dependency for a rarely-fired, entirely
  optional, best-effort feature (rungs 1-7 already guarantee safety without it — see the Scope note).
  Adding a persistent dependency for that trade isn't worth it.
- A minimal hand-rolled client — SaveLocker only ever needs one method call
  (`org.freedesktop.Notifications.Notify`), which sounds minimal, but the D-Bus wire protocol's
  binary marshalling (type-signature-driven alignment/padding, the variant type, the SASL auth
  handshake to even open the connection) is genuinely easy to get subtly wrong for real gain over
  the alternative below — correctness risk for low payoff.
- **`gdbus` (shelled out), chosen** — already the tool Phase 5's `DesktopEnvironment.NotificationsNameOwned`
  uses for the `NameHasOwner` probe, so this keeps the codebase to one external D-Bus tool rather than
  two. Specifically *not* `dbus-send`, despite `dbus-send` shipping with the same base `dbus` package
  that provides the bus itself (arguably an even safer bet to be present): `dbus-send`'s CLI syntax
  has poor, easy-to-get-wrong support for array and dict types, and `Notify`'s real signature needs
  both (`actions: as`, `hints: a{sv}`) — `gdbus`'s GVariant text format handles both natively and is
  the tool generally recommended for scripting a call with this shape. Process-spawn cost per call is
  irrelevant given how rarely this fires (once per conflict *becoming* open, not per poll).

**Corrected on real hardware 2026-09-02, after Group 3 shipped the above and it did not work:
`notify-send --wait`, not `gdbus call`, for *sending*. `gdbus` stays for the Phase 5 probe.** The
comparison above weighed the three options on argument syntax and dependency cost and never asked the
question that actually decides it: **who owns the notification once it is posted.** A notification
carrying `actions` belongs to the bus connection that sent it, and a notification server withdraws it
when that connection drops — nobody is left to receive `ActionInvoked`. `gdbus call` is one-shot by
construction (send, take the reply, exit), so it can *never* hold an actionable notification open. On
the maintainer's Deck in Desktop Mode this looked like success at every layer the agent can see: exit
code 0, a real notification id returned, `conflict notification: sent for 'X'` in the log — and a
popup that appeared and vanished inside a second. Bisected against the same daemon on the same bus,
one variable at a time: `expire_timeout=0` alone renders and persists; `app_name`/`icon` alone render
(`dialog-warning` correctly draws a caution icon); **adding the action button is what makes it flash
and disappear.** `notify-send --wait` holds its connection open for as long as the notification is
displayed, which fixes both the popup and the button, and prints the invoked action key on stdout —
so the second `gdbus monitor` connection the first implementation used to catch the click (which could
never have owned the notification anyway) is deleted rather than fixed. `notify-send` comes from
libnotify, present on any desktop that has a notification daemon at all, and was confirmed on the Deck
before the rewrite. The general lesson, worth more than the specific fix: **an exit code from a
fire-and-forget IPC call is not evidence the other end did anything** — this shipped green through a
build, a full agent test suite and a code review because nothing checked what was actually sent.

If built: fires once per conflict becoming open (a `HashSet<Guid>` of already-notified conflict ids,
cleared on resolve, so the 20s command-poller's own loop doesn't re-notify every tick) with an action
button that opens Phase 6's `agent-ui` page in the default browser (desktop session) or brings Phase
8's Game Mode screen to the foreground (Deck). Deliberately the lowest-priority phase before Phase 14:
rungs 5–7 of the escalation ladder (CLI, `doctor`, and the safe terminal state — an open
`ConflictFlag`, nothing auto-resolves under `Manual`) already guarantee a conflict is discoverable and
never silently mishandled with zero risk, regardless of whether this phase ever ships.

**Verify:** manual verification on the real Deck's Desktop Mode (KDE Plasma) session and a plain
desktop Linux box — screenshot the notification and the action button actually opening the right
surface, per this codebase's "verify live" standard. A scripted regression test is lower priority
here than elsewhere in this plan, since the D-Bus surface being exercised belongs to the OS's own
notification daemon, not to SaveLocker.

**Real-hardware correction, 2026-08-31:** this document (and `DesktopEnvironment.cs`'s own doc
comment) assumed "Game Mode has no session bus at all," used above to justify treating a desktop
notification as fundamentally a Desktop-Mode-only feature. A `doctor` run over SSH while a real Deck
was sitting in **Game Mode (Gamescope)** reported both a reachable D-Bus session bus AND something
already claiming `org.freedesktop.Notifications` ownership on it — SteamOS keeps one persistent
per-user bus alive via `systemd --user` regardless of graphical mode, and SSH reaches the same bus a
running Gamescope session already has. That assumption was never actually verified before being
written down, and it was wrong. **What is still unconfirmed:** who the actual claimant is (Steam's
own overlay? `xdg-desktop-portal`? something else?), and — the part that actually matters for this
phase — whether a real `Notify` call renders anything visible in Game Mode, or is silently accepted
and dropped by whatever holds the name. Until that's checked live on hardware, Phase 9 should still
target Desktop Mode as its verified surface, but a Game-Mode-visible desktop notification may turn
out to be reachable sooner than this plan assumed — worth a five-minute live check (fire one test
`Notify` call over SSH while genuinely in Game Mode, see if anything appears on screen) before ruling
it out.

**Shipped 2026-09-01** (`implementation-grouping.md`'s "Group 3"): `CommandPoller`'s new
`onConflictsPolled` hook and `src/Agent.Linux/ConflictNotifier.cs`.

**Verified on the maintainer's Deck 2026-09-02 — and it was broken, then fixed.** Desktop Mode was
confirmed as a working surface (the notification renders, persists, and its action button is
clickable), so this phase's target surface is no longer an assumption. Getting there took the
transport correction recorded above: the shipped `gdbus call` version reported success and flashed the
popup out of existence, and the first thing the live run proved was that the agent's own log line is
not evidence of anything a user can see. Game Mode remains unchecked. The rewrite carries a
regression test that could have caught this before hardware ever saw it — a fake bus, a fake `gdbus`
answering the `NameHasOwner` probe, and a fake `notify-send` recording its own argv, asserting the
command carries `--wait` and the action key. It was confirmed to FAIL against the pre-fix code
(`--wait` removed, argv recorded without it) rather than assumed to. Full detail: `CONTEXT.md` and
`Backlog.md`.

**Phase 10 — Decky: conflict display + resolve UI** (chip, QAM panel, resolve popup, policy
dropdown) — unchanged content, renumbered from the original Phase 5. Depends on Phase 0/1. Delivers
real, standalone value the moment it ships — a conflict can be seen and resolved from the Deck even
before Phase 11 wires it into the launch path. Can proceed in parallel with Phase 4 and Phases 5–9.

**Phase 11 — Decky: launch-gate wiring** (the cancel → popup → sync → relaunch sequence, plus the
fresh-page-open-conflict carve-out) — unchanged content, renumbered from the original Phase 6.
Depends on **both** Phase 4 (the wrapper is what actually decides `Blocked`; the plugin only displays
that decision, per this document's own "consistency between the plugin and the wrapper" section) and
Phase 10. Deliberately late: the highest-hardware-risk piece (`CancelGameAction`'s race-winning
reliability is already flagged unverified on real hardware, independent of this plan), so shipping
Phase 10 first means a working fallback already exists if this phase needs a hardware-driven
iteration cycle.

**Phase 12 — sync-status endpoint — consumer work only, the endpoint already shipped, but it is
NOT the cheap poll target this plan originally described it as.** Correction made 2026-08-30 while
starting Group 1 (`implementation-grouping.md`) and actually reading the handler before wiring
anything to it: `GET /api/games/{id}/sync-status` and `SyncStatusDto` were already delivered in
Phase 0/1 (`AgentApiServer.cs:696`), but its own doc comment says plainly what it costs — "cheap on
the **network** (no bytes cross the wire), NOT cheap on **disk**: the local hash still walks and
reads every file in the save folder, the same cost a push's own hash pays." It also calls
`ApiClient.GetStateAsync` internally — the exact same full-state fetch `AgentCli.cs`'s `status`
command already makes per game — so it is strictly *more* expensive than what a per-game status loop
does today, not less. **Earlier phrasing in this document and in `implementation-grouping.md`**
("agent-ui's `StatusHeader.tsx` can poll this one cheap endpoint... for the common 'am I in sync
right now' case") **was wrong and has been corrected here** — polling it on an interval or attaching
it to a passive per-game list refresh would re-hash a potentially large save folder on every tick,
which is exactly the performance footgun the whole per-file delta-upload feature exists to avoid
elsewhere in this codebase. This endpoint is for a genuine, **user- or wrapper-triggered, occasional**
"am I actually in sync, worth the disk cost to be sure" check — a manual button, a CLI flag, or a
one-shot call at a specific decision point (e.g. immediately before a launch gate decision) — never a
background poll. **Consumer work is correctly still open**, but needs an explicit trigger moment
decided first, not a UI badge wired to a timer; fold it into whichever of Phase 6, 8, or 10 first has
a natural "check now" action, rather than building one just to close this line out.

**Phase 13 — Playnite plugin** (new, separate project) — unchanged scope, renumbered from the
original Phase 8. Depends only on Phase 0/1's local API — independent of every Decky phase and of
Phase 3. This phase's own build-out is explicitly a later pass; this document only locks down the
*behavior* (confirmed buildable via `IPlayniteAPI.StartGame`) so that later work has a settled target.
**New implementation-time option, now that Phase 6 exists:** `OnGameStarting`'s WPF dialog no longer
has to be built from scratch — embedding a `Microsoft.Web.WebView2` control pointed at the local
`agent-ui` conflicts page trades a WebView2 dependency in the `.NET Framework 4.6.2` project for zero
duplicated chooser UI. Flagged as an option to weigh at implementation time, not a hard dependency —
this phase can still ship with its own minimal native WPF dialog (the original plan) if WebView2 in
that specific project turns out to be awkward.

**Phase 14 — Optional out-of-band notification (rung 6) and per-game "block launch" opt-in.**
Folds in the original reference plan's optional Phase 6 and `07-open-questions.md` §2, unchanged in
substance. Deferred, genuinely optional, depends only on Phase 0. Bundled here because neither piece
is required for any invariant in this design (`03-platform-ux-flows.md`: "rungs 1-5 already guarantee
the terminal state" / "a paused-sync launch is already fully safe"):
1. **Webhook/ntfy/email notify** for genuinely unattended boxes — a new, optional, per-server (not
   per-agent — the server already knows about every open conflict fleet-wide) `AppSetting` key
   `conflicts.webhook_url`, fired by the server itself on `ConflictFlag` creation/escalation.
2. **Per-game "block launch until resolved" setting** — `Game.BlockLaunchOnConflict`
   (nullable-with-global-default, same pattern as `RetainVersions`), for the minority of players who'd
   rather lose a play session than risk playing on stale data (e.g. a strict-permadeath run).
   Deliberately last: it's a settings-surface-multiplying feature (Decky's settings page,
   `agent-ui`'s `SettingsView.tsx`, a future Playnite settings page all need the toggle) for a use
   case not yet confirmed to exist, per `07-open-questions.md` §2.

```
Phase 0/1 (server + agent core — shipped)
        │
        ├─────────────┬─────────────┬─────────────┬─────────────┬─────────────┐
        ▼             ▼             ▼             ▼             ▼             ▼
    Phase 2       Phase 4       Phase 5       Phase 6       Phase 8       Phase 10
    (force-push   (Linux        (Linux env    (agent-ui     (Game Mode    (Decky UI,
     bookkeeping   wrapper       capability    conflicts     conflict      no launch
     fix, shipped) launch gate,  detection,    page +        screen)       wiring yet)
                   shipped)      shipped)      doctor line,
                                                shipped)
                                    │             │  │           │             │
                                    │      ┌──────┘  └───┐       │             │
                                    │      ▼             ▼       │             │
                                    │  Phase 7        Phase 9 ◄──┘             │
                                    │  (Windows tray   (D-Bus notify,          │
                                    │   automatic       shipped — needs       │
                                    │   chooser +        Phases 5 AND 6,       │
                                    │   bulk queue)      opens Phase 8 on      │
                                    │                    the Deck when it     │
                                    │                    exists)              │
                                    │                                          │
                                    └──────────────────────┬───────────────────┘
                                                            ▼
                                                        Phase 11
                                                (Decky launch-gate wiring —
                                                 needs Phase 4 AND Phase 10)

Phase 3 (dashboard Backups tab) — shipped; no dependencies, was buildable/shippable any time.
Phase 12 (sync-status consumer) — endpoint already shipped in Phase 0/1; low-priority, foldable
    into Phase 6 or Phase 10 when either is built.
Phase 13 (Playnite) — depends only on Phase 0/1; can optionally reuse Phase 6's page instead of a
    from-scratch WPF dialog, decided at implementation time.
Phase 14 (optional: webhook + block-launch setting) — depends only on Phase 0; last by design.
```

## Scope note (updated 2026-08-30)

This section used to say the Linux-headless escalation ladder (desktop notifications, a local web
chooser fallback, out-of-band webhook notify) from the earlier 8-document plan was future work, not
folded into this one. It now is: Phases 5–9 above bring rungs 1–4 of `03-platform-ux-flows.md`'s
escalation ladder into this plan directly as concrete, numbered, dependency-ordered phases, and
Phase 14 folds in rung 6 as an explicit opt-in. Rung 5 (CLI) and rung 7 (the safe terminal state — an
open `ConflictFlag`, nothing auto-resolves under `Manual`) were already covered, unchanged, by Phase
0/1's CLI commands and the server's existing behavior — no new phase was needed for either.

**Still deliberately out of scope, and not reconsidered by this update:**
- A new Unix-socket or D-Bus RPC interface *between* SaveLocker's own processes (wrapper↔daemon, or
  agent↔dashboard). `03-platform-ux-flows.md`'s own reasoning stands unchanged: the existing `:5178`
  HTTP API is already the one shared interface every frontend on a box uses, and Phase 9's D-Bus
  usage is strictly outbound — SaveLocker as a *client* of the desktop's own notification daemon,
  never a *server* of its own. This keeps the interface count at one.
- Cross-format save conversion/merging, multiple save paths per game, and registry-based saves — all
  separately scoped in [[Backlog]], unrelated to conflict *resolution* UI specifically.
- [[Backlog]]'s already-deferred "one state owner for the Linux agent" (wrapper→daemon IPC over a
  Unix socket) — a distinct question about state ownership between two local processes, not
  something any phase above needs answered in order to ship.
