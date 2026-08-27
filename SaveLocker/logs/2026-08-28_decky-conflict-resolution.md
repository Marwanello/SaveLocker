# Task: Decky plugin — proper conflict resolution, not just refusals

Planned 2026-08-28. From [[Backlog]] → High priority. Supersedes the conflict-resolution-relevant
parts of the earlier, fork-blind 8-document pass at
`docs/design/00-inventory.md` through `07-open-questions.md` (kept as reference; their platform-
neutral server pieces — the conflict data model's causal, non-clock-based detection, and the
Windows/Linux-headless material outside Decky/Playnite — are unaffected and not repeated here).

**Execute ONE phase per session, verify it, stop** — same discipline as
`logs/2026-08-15_decky-plugin.md`, the closest precedent for a plan spanning both this repo and the
separate `SaveLocker-Decky` plugin repo.

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
Decky-specific phase below — can proceed in parallel with Phases 5–6.

**Phase 5 — Decky: conflict display + resolve UI** (chip, QAM panel, resolve popup, policy dropdown).
Depends on Phase 0/1. Delivers real, standalone value the moment it ships — a conflict can be seen
and resolved from the Deck even before Phase 6 wires it into the launch path. Can proceed in parallel
with Phase 4 and Phase 8.

**Phase 6 — Decky: launch-gate wiring** (the cancel → popup → sync → relaunch sequence, plus the
fresh-page-open-conflict carve-out). Depends on **both** Phase 0/1 and Phase 5. Deliberately last: the
highest-hardware-risk piece (`CancelGameAction`'s race-winning reliability is already flagged
unverified on real hardware, independent of this plan), so shipping Phase 5 first means a working
fallback already exists if this phase needs a hardware-driven iteration cycle.

**Phase 7 — Cheap sync-status endpoint.** Same shape of change as Phase 0/1 and touches the same
files — cheapest to build alongside it, though functionally independent.

**Phase 8 — Playnite plugin** (new, separate project). Depends only on Phase 0/1's local API —
independent of every Decky phase and Phase 3. Can run fully in parallel with all of them. This
phase's own build-out is explicitly a later pass; this document only locks down the *behavior*
(confirmed buildable via `IPlayniteAPI.StartGame`) so that later work has a settled target.

```
Phase 0/1 (server + agent core — ship together)
        │
   ┌────┼────────────┬─────────────┬─────────────┐
   ▼    ▼             ▼             ▼             ▼
Phase 2 Phase 7      Phase 4      Phase 5       Phase 8
(force  (status,    (Linux       (Decky UI,    (Playnite,
 fix)    optional    wrapper)     no launch     separate
         w/ 0/1)                  wiring yet)    project)
                                       │
                                  Phase 6
                                  (Decky launch
                                   gate wiring)

Phase 3 (dashboard Backups tab) — no dependencies, buildable/shippable any time, including first.
```

## Out of scope for this pass

The Linux-headless escalation ladder (desktop notifications, a local web chooser fallback, out-of-
band webhook notify) from the earlier 8-document plan is unaffected by this fork-specific check and
remains future work — referenced there, not repeated here.
