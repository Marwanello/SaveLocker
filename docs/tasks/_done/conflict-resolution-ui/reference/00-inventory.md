# Inventory of current behaviour

Read before any of the other documents in this folder. Everything below is verified against the
code at this branch's HEAD (not guessed from vault docs alone); file:line references are given
where useful. Ambiguities the code does not resolve are called out explicitly in §6 rather than
assumed.

## 1. Where sync is triggered today

Every trigger ultimately calls one of four `SyncEngine` (`src/Agent.Core/SyncEngine.cs`) methods:
`PushAsync`, `PullAsync`, `SyncAllAsync` (pull-then-push over every tracked game), and the
launch/exit pair `OnGameLaunchAsync` / `OnGameExitAsync`. There is no fifth path — every trigger
below is a caller of these four.

| Trigger | Surface | Method | Notes |
|---|---|---|---|
| Tray menu → per-game "Force Pull" / "Force Push" | Windows tray (`TrayApp.cs:203`) | `PullAsync(force:true)` / `PushAsync(force:true)` | Immediate, no settle gate. |
| Tray menu → "Sync All (pull then push)" | Windows tray (`TrayApp.cs:226`) | `SyncAllAsync` | Skips the pull (not the push) for a game currently running. |
| Agent UI → Overview "Sync now" button | Both hosts, `agent-ui` React SPA via `POST /api/sync` (`AgentApiServer.cs:601`) | `SyncAllAsync` | Single-flight (`_syncGate`): a second concurrent press is told "a sync is already running" rather than starting a second run. |
| Deck Game Mode → push/pull, per game or all | `savelocker ui` (`Ui/UiApp.cs`) | Same CLI code path as the `push`/`pull` commands | Runs the CLI in-process; inherits all its guards. |
| CLI `push [game\|all] [--force]` | `AgentCli.cs`, both hosts | `PushAsync` | Immediate — skips the settle gate (manual pushes always do). |
| CLI `pull [game\|all] [--force]` | `AgentCli.cs`, both hosts | `PullAsync` | Guarded: refuses to overwrite un-pushed local changes unless `--force`. |
| Dashboard → queued command (`Pull`/`Push`/`Sync`/`Scan`) | `POST /api/commands` (admin), polled by `CommandPoller.RunCommandsAsync` every ~20 s | `PullAsync` / `PushAsync` / `SyncAllAsync` | At-least-once delivery under a visibility lease (`AgentCommand.LeaseExpiresAt`/`ClaimToken`). |
| Automatic — process exit / folder watch (Windows) | `Watchers.cs` (`FileSystemWatcher` + `ProcessWatcher`, 4 s poll) | `PushAsync(settle:true)` | Debounced; waits for the save to go quiet (`SaveSettler`) before archiving. |
| Automatic — Steam launch wrapper (Linux) | `savelocker run -- %command%` (`ProtonRun.ExecuteAsync`) | `OnGameLaunchAsync(preLaunch:true)` before the child process starts, `OnGameExitAsync` after it exits | The **only** real pre-launch boundary in the codebase today — see §5. |
| Automatic — offline queue drain | `OfflineQueueDrainer`, 30 s timer | `PushAsync` (retried) | Only retries pushes that failed with `HttpRequestException`; never used for pulls or resolutions. |
| Command poller reconcile | `CommandPoller.ReconcileGamesAsync`, every ~20 s | No sync call itself — adopts/drops tracked games from the server's list | Not a sync trigger, but it's the same 20 s tick that also runs queued commands. |

**Windows has no pre-launch boundary.** `Decisions.md` (WA-01) is explicit and deliberate:
`ProcessWatcher` polls every 4 s, so by the time it notices a launch the game has already opened
its save files — restoring under that is what caused a real save-loss bug. Windows therefore never
pulls before launch; it only takes the lease and pushes on exit. This is the gap §4.2's
Playnite integration is meant to close, and it does **not** contradict the locked decision — the
decision's own closing line is "Adding a Windows launch wrapper would let the pull come back;
nothing here precludes it." A Playnite `OnGameStarting` hook is exactly such a wrapper.

## 2. Where conflicts are detected, represented, and resolved

**Detection happens in exactly one place, server-side, at upload time** —
`SyncService.PrepareUploadAsync` (`src/Server/Services/SyncService.cs:540`), shared by the
single-shot and chunked upload paths. The rule:

- Uploading machine sends the `parentVersionId` it last knew (its local idea of the head).
- `parentVersionId == null` (first-ever push) or `parentVersionId == server head.Id` → **fast-forward**, no conflict.
- Incoming content hash equals the head's own hash → **NoChange**, no-op.
- Otherwise (parent stale, i.e. the head moved since this machine last synced) → **diverged**, and — unless the game's `ConflictPolicy` auto-resolves it (see §4) — a `ConflictFlag` is recorded and the head is **left untouched**.

Detection is **causal, not clock-based already**: it compares the declared parent version id
against the server's actual current head, never a timestamp. There is no per-device vector clock
or generation counter — because the architecture is hub-and-spoke with one server as sole arbiter
(`Decisions.md`: "unRAID as hub, not peer-to-peer... single source of truth for conflict
resolution"), a linear parent-pointer chain checked against one authoritative head is equivalent to
what a vector clock would buy in a topology with no single arbiter, and simpler. This is a locked
architectural choice this design should keep, not reopen — see §7 of the conflict-model spec.

**Representation** — `ConflictFlag` (`src/Server/Data/Entities.cs:127`):

```csharp
public class ConflictFlag {
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid VersionAId { get; set; }      // the head at the moment of divergence
    public Guid VersionBId { get; set; }      // the newest divergent push (replaced in place if more arrive)
    public Guid? MachineId { get; set; }      // the machine that is stuck
    public int Count { get; set; } = 1;       // divergent pushes folded into this one row
    public DateTime LastSeen { get; set; }
    public ConflictStatus Status { get; set; } // Open | Resolved
    public DateTime CreatedAt { get; set; }
    public Guid? ResolvedVersionId { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
```

One open conflict per (game, machine) — a machine that keeps saving while conflicted bumps
`Count`/`VersionBId`/`LastSeen` on the same row rather than creating a new one (a real incident once
produced 75 rows for one unresolved divergence). Escalation is a pure function of age, not a stored
flag: `ConflictEscalationPolicy.IsEscalated` returns true once `CreatedAt` is more than
`Conflicts:EscalationAfterSeconds` (default 6h) in the past, computed on read.

**Resolution** — `SyncService.ResolveConflictAsync` (`SyncService.cs:1307`), reachable today only
from `POST /api/conflicts/{id}/resolve?version=&keepBoth=` on the **admin-only** route group
(`AdminPasswordFilter`), which only the React dashboard (`web/`) calls. There is no agent-group
equivalent, no CLI command, and no other frontend. Resolving:

1. Refuses if the chosen version is older than the *current* head (blocks an accidental rewind if
   Latest moved on since the conflict opened).
2. If `keepBoth`, marks **both** conflicting `SaveVersion` rows `Protected = true` (exempt from
   retention) — this is the existing "no destructive resolution" mechanism; nothing is ever deleted
   by a resolve, `keepBoth` or not.
3. Moves `Game.HeadVersionId` to the winner (`SetHeadAndPropagateAsync`).
4. Queues a deduplicated, **unforced** `Pull` command for every live machine syncing the game
   (`QueueResolutionPullsAsync`) — critical, because an agent's local parent only advances on its
   own push/pull, so without this every machine (including the winner, whose *content* matches but
   whose *pointer* doesn't) stays behind and re-conflicts on its next save.
5. Prunes retention now that the pinned versions are unpinned.

**A second, un-modeled "conflict"-shaped situation exists**, and it is the one the whole
launch-time redesign actually has to solve: `SyncEngine.PullAsync` refuses outright when local saves
hold changes never pushed (`hasUnsyncedLocal && !force`, `SyncEngine.cs:492`). This is **not** a
`ConflictFlag` — it never reaches the server at all. It is an `Alert` (toast + log) and an
`AgentEvent` (`AgentEventCodes.PullBlocked`) on the server's health table, with no structured
"here are both sides, pick one" payload — no version id for the local side (it was never uploaded),
no size, no file count. Today the *only* documented recovery is manual: push on the machine with
real progress, resolve the resulting conflict in the dashboard, then pull on the other machine
(`cli-reference.md` → "Safe first sync between two machines"). This gap, and how to close it without
inventing a second conflict model, is the central design question answered in
`01-conflict-model-spec.md`.

## 3. Dashboard conflict metadata — exactly what is shown, and where it comes from

`web/src/components/GameDetail.tsx` (~line 432 onward), one card per open conflict on the game:

- Header: `Conflict — <machine> cannot sync: choose the version to keep` (machine name resolved
  from `conflicts[].machineId` against the `machines` list) plus a link to the `Why did this
  happen?` help article.
- An **overdue** banner when `escalated` is true (server-computed, >6h open).
- A "Playing solo? Set to Newest wins" nudge when the game's policy is still `Manual` — this writes
  the policy directly (`api.setConflictPolicy(gameId, 'NewestWins')`) as a one-click fix.
- If `Count > 1`: "N divergent saves folded into this conflict — the newest is offered below."
- **Per side** (`VersionAId`, `VersionBId`), one card each, built from three separate calls the
  console makes (not one aggregate endpoint):
  - `machineName` and `createdAt`/`size` — from `GET /api/games/{id}/versions` (`SaveVersionDto[]`, already fetched for the Versions list).
  - `fileCount` and `newestFileWriteUtc` — fetched lazily, once per version id, from
    `GET /api/games/{id}/versions/{versionId}/stats` (admin) → `VersionStatsDto`, derived on demand
    from the zip's own entry metadata (`SaveArchive.GetArchiveStats`) and cached forever server-side
    once computed (an archive never changes after upload).
  - Whether this side **is** the current head (`— current Latest` suffix).
  - No content hash is shown anywhere in the UI. No device "labels" beyond the plain machine name
    string (no OS/platform badge on the conflict card itself, though `GameStateDto`/`CandidateDto`
    elsewhere carry `Store`/platform info that could be surfaced).
- Two buttons per side: **"Use as Latest"** (`resolveConflict(id, versionId, keepBoth:false)`) and
  **"Keep both · use this"** (`keepBoth:true`), each behind a `confirm()` dialog spelling out the
  consequence (a pull will be queued for every syncing machine; unpushed local changes on some
  machine will report blocked rather than being silently overwritten).

## 4. Existing auto-resolution options, verbatim, and where the setting lives

Verbatim from the dashboard's `<select>` (`GameDetail.tsx:402`):

```
Manual — resolve conflicts in the console
NewestWins — latest upload always wins
PreferMachine — one machine always wins
```

Backing enum, `ConflictPolicy` (`src/Shared/Contracts.cs:574`):

```csharp
public enum ConflictPolicy { Manual = 0, NewestWins = 1, PreferMachine = 2 }
```

Storage: **per-game only**, two non-nullable/nullable columns on `Game`
(`src/Server/Data/Entities.cs:48`) — `ConflictPolicy ConflictPolicy` (default `Manual`, no global
fallback exists today) and `Guid? PreferredMachineId` (used only when policy is `PreferMachine`).
Set via `POST /api/games/{id}/conflict-policy` (admin-only route,
`SetConflictPolicyRequest { Policy, PreferredMachineId? }`), applied in
`SyncService.IngestAsync` (`SyncService.cs:984`): when policy auto-resolves, the incoming version
becomes head immediately with **no `ConflictFlag` ever created**, and every other syncing machine
is queued an unforced pull (`propagate: true, exceptMachineId: machineId` — the uploader itself is
excluded since it already knows the new head from its own response).

There is no per-machine or per-user override of policy, and no global default — every new game
starts at `Manual` and must be changed per game.

## 5. Agent/daemon/client process split, IPC, and per-platform components

**Three process shapes exist today**, all built on the shared `src/Agent.Core` sync brain:

| Host | Binary | Lifetime | Owns a `SyncEngine`? |
|---|---|---|---|
| Windows tray | `SaveLocker.Agent.exe` (WinForms) | Long-lived, one per logged-in session | Yes — rebuilt whenever server connection settings change (WA-06). |
| Linux daemon | `savelocker daemon` (`Agent.Linux`) | Long-lived, `systemd --user` unit | Yes — same rebuild-on-change rule. |
| Linux launch wrapper | `savelocker run -- %command%` | **Short-lived**, one process per game session, spawned by Steam itself | Yes — its own separate `SyncEngine` instance, disposed (`RetireAsync`) when the wrapper exits. |

There is **no IPC between the daemon and the launch wrapper** beyond shared on-disk state, guarded
by cross-process primitives, because they are genuinely separate OS processes that can run
concurrently (a user can open the agent UI while a game launched through the wrapper is mid-sync):

- `config.json`, `offline-queue.json`, `lease-warnings.json` — read/written under a per-game `flock`
  (`AgentStateLock`) plus an in-process semaphore for same-process races. Every write is
  temp-file-then-rename (`AtomicFile`).
- The wrapper does not talk to the daemon's HTTP API at all — it builds its own `ApiClient` and
  talks directly to the **server**. The daemon and the wrapper are two independent clients of the
  server's API, coordinated only through shared config/state files and the server's own lease table.

**The "local management API"** (`AgentApiServer`, `src/Agent.Core/AgentApiServer.cs`) is the one
thing that could be called IPC in the traditional sense: an ASP.NET Core minimal API bound
**loopback-only** (`ListenLocalhost`), on port **`:5178`** for both the Windows tray and the Linux
daemon (the launch wrapper never runs it — it's a one-shot process). Auth is a bearer-style header
(`X-SaveLocker-Token`, read from a 0600 file beside `config.json`) plus a `Host`/`Origin` check with
deliberately **no CORS policy** — the intent is that nothing outside this machine, and no foreign
web page loaded in a browser on this machine, can reach it. It serves both a JSON API and the bundled
`agent-ui` React SPA (same origin, so the SPA can call its own API with the token baked into
`index.html` at serve time). The launch wrapper does not expose this API at all, since it is not
long-lived enough to usefully serve anything.

**The Decky plugin's IPC pattern is the closest existing precedent for "a frontend that isn't the
bundled UI talking to the daemon,"** and it should be the template for every new frontend this
design adds (`logs/2026-08-15_decky-plugin.md`):

- The plugin's **frontend** (React, running inside Steam's own `SharedJSContext`/CEF) **cannot**
  reach `:5178` at all — it is not loopback from Steam's perspective and carries no token, and the
  Guard rejects it outright by design. This is treated as a hard constraint, not a bug to route
  around.
- The plugin's **Python backend** (a normal Deck-user-owned process Decky itself hosts) reads the
  0600 `~/.local/share/SaveLocker/api-token` file directly off disk and makes ordinary HTTP calls to
  `127.0.0.1:5178` with that header. This is the only channel between the plugin and the daemon.
- The agent already exposes several small, purpose-built endpoints exactly for this kind of
  external, "knows nothing about SaveLocker's internals" consumer: `GET /api/decky` (plugin install
  state), `GET /api/launch-options` + `POST /api/launch-options/resolve` + `POST
  /api/launch-options/applied` (the launch-options merge protocol), `GET /api/games` (a stable,
  intentionally-never-renamed wire contract — see the doc comment on `TrackedGameDto`), and
  `POST /api/games/{id}/pull-before-launch` / `POST /api/games/{id}/alias` (per-game settings the
  plugin's own UI edits). **The design principle stated explicitly for this precedent — "the plugin
  knows nothing about SaveLocker; it reads a desired-state list from the agent's local API and
  applies it" — is the one to carry forward for conflict resolution.**

**Per-platform component inventory:**

- **Windows**: `src/Agent` (WinForms tray, `AgentWindow`/WebView2 hosting `agent-ui`), no CLI-only
  headless mode distinct from the tray beyond invoking the same exe with arguments.
- **Linux desktop/Deck**: `src/Agent.Linux` — `daemon` (headless host, serves `agent-ui` on `:5178`
  exactly like Windows), `run` (the Steam launch wrapper), `doctor` (CLI diagnostic, the only UI a
  fully headless box has), `Ui/` (**`savelocker ui`**, the SDL+OpenGL+Dear ImGui gamepad-native Game
  Mode surface — an **in-process view over `Agent.Core`**, not a second network client; it calls the
  same C# objects the daemon does, with no HTTP hop of its own).
- **Steam Deck, Decky plugin**: separate repository (`SkorcherX/SaveLocker-Decky`, not present in
  this workspace — its current source cannot be inventoried directly here; everything known about it
  comes from this repo's `LaunchOptions.cs`/`AgentApiServer.cs` server-side contracts and the
  write-up in `logs/2026-08-15_decky-plugin.md`). Today it has **only a Quick Access Menu (QAM)
  panel** — launch-options apply/status, push/pull per game or all, `doctor` output, lease warnings.
  It does **not** patch the per-game library page, and has never attempted to (no
  `routerHook.addPatch` usage exists in anything this repo documents) — the game-page chip/modal
  this design's brief asks for is a wholly new capability for that plugin, not an extension of a
  proven one. See the feasibility report for what needs external verification here.

## 6. Ambiguities found — flagged rather than guessed

1. **No CLI command exists today to view or resolve a conflict** (`savelocker conflicts`,
   `savelocker resolve` do not exist — confirmed against `cli-reference.md` and `AgentCli.cs`). The
   brief's §4.3 step 5 assumes these commands; they are new, not existing surface being extended.
2. **No global default conflict policy setting exists** — every game defaults to `Manual`
   independently. The brief's §4.3 "policy resolution order (per-game → global default → built-in
   default)" therefore requires a genuinely new setting, not a re-exposure of one.
3. **The Decky plugin's actual current frontend/backend code is not in this repository.** Everything
   about its game-page-patching feasibility, modal APIs, and Python-backend HTTP pattern beyond what
   `logs/2026-08-15_decky-plugin.md` already documents needs external verification against Decky
   Loader's own docs/source — done separately in `04-feasibility-report.md`.
4. **No pre-launch boundary exists on Windows at all** — not "a weak one," none. Any Windows launch
   gate (Playnite or otherwise) is new interception, not a strengthening of `ProcessWatcher`
   (`ProcessWatcher` remains the *only* signal for automatic exit-push and the "is it safe to
   pull/restore right now" check; it is deliberately never used as a pre-launch gate — see
   `Decisions.md` WA-01 — and this design does not change that).
5. **The "unsynced local vs. remote" case (§2 above) has no version id, no size, no file count for
   its local side** because nothing has uploaded it yet. Whether the new design should push it first
   (creating a real `ConflictFlag` before ever presenting a chooser) or invent a lighter-weight
   "preview" representation is a real design decision, resolved in `01-conflict-model-spec.md` §2 —
   flagged here because it is not something the existing code already decides either way.
6. **`AgentCommandType` has no `Resolve` member** — dashboard-issued commands are `Pull|Push|Sync|Scan`
   only. A command-channel path for "resolve this conflict" (as opposed to calling the resolution
   API directly) does not exist and was not assumed to be needed — see the Resolution API doc for why
   direct API calls, not the command queue, are the right shape for resolution.
