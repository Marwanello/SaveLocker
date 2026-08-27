# Per-platform UX flows

Read `00-inventory.md`, `01-conflict-model-spec.md`, and `02-resolution-api.md` first. Every flow
below is a frontend of the local `:5178` API (§Layer 2/3 of the Resolution API doc) — none of them
invents its own conflict logic.

## Steam Deck — Decky plugin

### Chip states

The existing status chip (today: synced / not-yet-verified) gains states, using the exact palette
already fixed in `web/src/index.css` / `Ui/Theme.cs` (REPO_MAP: "Palette is lifted from
`web/src/index.css` so console + agent UI + Deck stay in lockstep" — these colors are load-bearing
consistency, not a free choice):

| State | Trigger | Visual | Icon |
|---|---|---|---|
| **Synced** | No open conflict, no lease held elsewhere, last sync succeeded | Green `#129271` (matches the dashboard's "in sync" badge, `GameDetail.tsx:340`) | Plain cloud |
| **Syncing** | A push/pull is in-flight for this game (`ActivitySnapshotDto.Phase != Idle` for this game, already exposed by `GET /api/activity`) | Neutral/blue, animated (spinner or pulsing) | Cloud with arrow |
| **Conflict** | An open `ConflictFlag` exists for this game | Amber `#f4a60d` (matches the dashboard's conflict banner) | Cloud with caution mark — as specified in the brief |
| **Conflict, escalated** | Same, `Escalated: true` (>6h open) | Red `#e5534b` (matches the dashboard's escalated border) | Cloud with caution mark, filled/bold |
| **Paused** | Sync paused for this game without an open conflict (e.g. `savedir.unsafe`, `savedir.missing`, or `sync.busy` reported as an `AgentEvent`) | Gray, muted | Cloud with pause bars |
| **In use elsewhere** | Lease held by another machine (`LeaseWarningStore`/`LeaseHeldElsewhere`) | Amber, same as Conflict but a lock icon instead of caution — deliberately distinct from Conflict, since it's a warning about what *might* happen on exit, not a decision pending right now | Cloud with lock |
| **Error** | `ServerUnreachable`, `PushFailed`, or any other open, non-conflict `AgentEvent` at `Error` severity for this game | Red, same as escalated conflict but with an "!" instead of a caution triangle to keep the two visually distinguishable at a glance | Cloud with exclamation |

State priority when more than one applies (escalated conflict > conflict > error > in-use-elsewhere >
paused > syncing > synced) — a conflict always outranks a lease warning, because a conflict is a
decision the user must make and a lease warning is merely informational.

Data source: **one poll**, `GET /api/state`-equivalent extended with a per-game summary (today
`/api/state` is agent-wide, not per-game — a new `GET /api/games/{id}/sync-summary` on the local
API, `{ state: ChipState, conflictId: Guid?, leaseHolder: string? }`, is the concrete new route; it
composes existing data — `ConflictDetailDto[]`, `ActivityDto`, `LeaseWarningDto[]` — server-side in
the agent daemon so the plugin's Python backend makes one call per game, not four).

### Game-page chip → modal

On selecting the chip (game-page route patch — feasibility in `04-feasibility-report.md`), a modal
built from `showModal`/`ConfirmModal` (`@decky/ui`) shows both sides of `ConflictDetailDto`
side-by-side: machine name, timestamp, size, file count, newest-change time — the exact fields the
dashboard already shows (`00-inventory.md` §3), fetched with **one** call to
`GET /api/conflicts/{id}` (Layer 2) instead of the dashboard's three. Two primary actions ("Keep
this Deck's save" / "Keep the other machine's save"), a tertiary "Keep both" checkbox/toggle
(mirroring the dashboard's separate button, collapsed into one to fit a gamepad-navigable modal more
compactly), and a "Why did this happen?" link that opens the same help article the dashboard links
today (`#help/conflicts`) — reachable from Game Mode by pointing the system browser at the agent
UI's help page over the loopback port, or, more simply, showing the article's text inline in a
second modal panel, since Game Mode has no general-purpose browser (the same reasoning that ruled
out `steam://openurl` for `savelocker ui`, per `Decisions.md`).

Choosing a side posts `POST /api/conflicts/{id}/resolve` with `resolvedVia: "deck-chip"`; the modal
shows the `ConflictResolveResult`, closes on success, and on `503` shows the exact fail-closed
message from `02-resolution-api.md` §4 rather than a generic error.

### Quick Access Menu fallback

The existing QAM panel (`00-inventory.md` §5 — the plugin's only proven surface today) gains a
"Conflicts" row whenever `GET /api/conflicts` (Layer 2) returns a non-empty list — same data, same
resolve call, rendered as a list of `Field`s (not bare `Focusable`s — the exact gotcha already
recorded for this codebase's own QAM work: "A read-only row needs `Field` with `focusable`... or the
QAM scrolls past it") rather than a modal, since the QAM is a persistent panel, not a one-shot
dialog. **This fallback is not optional** — it is the answer to "what happens when the game-page
patch breaks" (`04-feasibility-report.md`): the QAM panel does not depend on `routerHook` at all, so
a Steam client update that breaks route patching degrades the feature to "one extra step to reach
the same chooser," never to "the user can no longer resolve a conflict from the Deck."

### Gamepad navigation

Every interactive element in the modal is a `Focusable`/`Field` (never a bare `<div onClick>`), the
same requirement the existing QAM work already discovered the hard way
(`Gotchas.md` → Decky: "The Quick Access panel only scrolls to things the D-pad can reach"). No
touch/pointer assumption anywhere in the flow — D-pad moves focus, A activates, B backs out of the
modal without resolving anything (never a silent "keep local" default on cancel).

### Backend communication and daemon-down behavior

Unchanged from the existing precedent (`00-inventory.md` §5): the plugin's Python backend reads the
0600 `api-token` file and calls `127.0.0.1:5178`. When the daemon is down (connection refused), the
chip renders as **Error** (not Synced, not silently hidden) with the message "SaveLocker agent is
not running," matching the existing "The SaveLocker agent is not running" wording the launch-options
work already established for this exact failure mode.

### Launch interception

**Decky Loader itself has no official, supported pre-launch hook** — confirmed against `@decky/api`'s
`RouterHook` and the wiki's plugin-dev docs; no such API is documented anywhere, and no proposal for
one turned up in `decky-loader`'s own issue tracker (`04-feasibility-report.md` has the full citation
trail). **But a real, currently-shipping mechanism exists that a plugin's frontend can reach anyway:**
because Decky plugin frontends share Steam's own `SharedJSContext`, they can call the same
undocumented internal `window.SteamClient` object Steam's own UI uses. The Decky plugin **MoonDeck**
(a real, shipping streaming plugin this codebase's own save-detection work has already had to reckon
with, `Decisions.md`) uses exactly this to intercept and cancel launches:

```ts
SteamClient.Apps.RegisterForGameActionStart((gameActionId, gameId, action) => {
  if (action === "LaunchApp") callback(gameId, () => SteamClient.Apps.CancelGameAction(gameActionId));
});
```

This fires before the game process is created and `CancelGameAction` genuinely blocks it — so a
JS-side pre-launch gate is *possible*, not merely theoretical. **Recommendation: do not build the
launch gate on this.** One line each for why: it is Valve-internal and un-versioned (the same class
of instability this codebase already named as an accepted cost for the launch-options JS calls,
`logs/2026-08-15_decky-plugin.md`: "`SteamClient.*` is undocumented Valve internals and breaks on
Steam client updates. That breakage lands on users"), and — the sharper reason — it only exists
while the Decky plugin is installed and Decky Loader itself is running; the existing
Steam-launch-options wrapper works with **no Decky dependency at all**, matching this whole feature's
own stated design principle ("Decky is an accelerator, never the supported path... a Deck without
Decky Loader loses nothing it has today"). Building the gate on a Decky-only mechanism would violate
that principle for the single most safety-critical part of the feature. The gate therefore stays the
existing Steam-launch-options wrapper (`savelocker run -- %command%`), extended to call
`PrepareLaunchAsync` (Resolution API §Layer 3) before starting the child process, exactly where
`ProtonRun.ExecuteAsync` already calls `OnGameLaunchAsync` today — with the `SteamClient.Apps`
mechanism noted here only as a possible *future* belt-and-braces addition (e.g. a last-resort catch
for a game launched before its launch options were ever applied), never the primary gate.

**Consistency between the plugin and the wrapper**: the plugin never duplicates the gate's logic —
it only *displays* what the wrapper's gate decided, read back through `GET /api/conflicts`. If the
gate blocks a launch (`LaunchDecision.Blocked`), the wrapper process itself has to refuse to start
the child — a new, small addition to `ProtonRun.ExecuteAsync`: on `Blocked`, print the reason to
stderr/`agent.log` and exit non-zero *without* calling `RunChildAsync`, which Steam surfaces as the
game failing to launch. This is a real UX cost (a cryptic "won't start" from Steam's perspective
unless the user already has the QAM open) — mitigated by the chip already having shown **Conflict**
before the user pressed Play, and covered further in the trade-off discussion below.

**Per-game launch-option editing vs. a global compat tool — recommendation: keep the existing
per-game launch-options wrapper; do not build a compat-tool shim.** One line each:
- *Compat tool shim* (a `toolmanifest.vdf`-registered fake "Proton" that wraps the real one): would
  make the gate automatic for every Steam-launched game with zero per-game setup — but it
  intercepts fewer failure modes than it seems to (native Linux titles and non-Proton launches don't
  go through a compat tool at all, and this codebase's whole Linux scope is already Proton-only by
  design, `Decisions.md` §1), and it reopens exactly the "Valve mints a new appid/compat-tool
  identity per release" fragility this codebase already fought once (`Decisions.md`'s compat-tool
  discovery bug) — now as something SaveLocker itself has to keep registered and discoverable
  correctly, rather than something it merely reads.
- **Rejected**, in favor of keeping the wrapper: it's already proven on hardware, already the
  documented supported path, and the plugin's whole value proposition here (Phase 1-3's existing
  design, `logs/2026-08-15_decky-plugin.md`) is specifically automating *setting* the launch option,
  not replacing the mechanism — extending that automation to also insert the gate call is strictly
  additive to a working system, where a compat tool would be a second, parallel launch path to keep
  correct.

### Conflict detected while a game is already running, Steam UI behind the game

This can only happen for the *push-time* detection path (a manual `sync`/push while playing, or the
20s command-poller running a dashboard-issued command) — the launch gate above has already run by
definition before the game starts. When it happens: the chip flips to **Conflict** underneath the
running game (no interruption — Steam's overlay/QAM remains reachable via its own hotkey regardless
of what's in the foreground, which is exactly why the QAM fallback above matters here too, not only
for the route-patch-broken case) and, per `02-resolution-api.md` §Layer 3 point 2, automatic pushes
stop after `ConflictUploadLimit` (3, existing constant) attempts — the existing behavior, unchanged.
No modal interrupts gameplay; the user resolves it from the QAM whenever they choose to check, or on
their next launch, whichever comes first.

---

## Windows — in-app prompts + Playnite

### Bulk-operation conflict presentation

**Recommendation: a queue, one conflict at a time, with an explicit "apply to all remaining"
control that only appears after the first choice — not a batched list.** One line for the rejected
alternative: a single screen listing every conflict with a per-row choice looks efficient but asks
the user to make N independent judgment calls with no ability to establish a pattern first ("I
always want my desktop's save") before committing to it — a queue lets the first resolution
teach the "apply to all remaining" option what pattern to offer (same machine wins? newest wins?)
rather than presenting it as an undifferentiated blanket toggle up front. Concretely: `Sync All`
finds N open conflicts → shows conflict 1 of N in the existing single-conflict chooser UI (reused
verbatim, not a second UI) → after the user picks, if N > 1, a follow-up prompt: "Apply the same
choice (`keep <machine>'s save`) to the other N-1 conflicts too?" with Yes/No/Review-each — Yes
resolves the rest via the same `POST /api/conflicts/{id}/resolve` call per conflict id, No/Review-
each continues the queue one at a time. This directly reuses the Resolution API with no bulk-
specific endpoint needed.

### The chooser itself

A dialog raised by `Sync All`, `Sync single game`, `Pull all`, or `Pull single game` whenever the
operation surfaces an open conflict — reusing the same modal/dialog component regardless of which
of the four triggered it (all four already funnel through `SyncEngine`, per `00-inventory.md` §1;
the tray/agent UI wraps each with "if the result carries a conflict, show the chooser" rather than
four separate implementations).

### Playnite integration

Confirmed against the current public Playnite SDK (10.x; Playnite 11 is a private rewrite with no
public API surface as of this writing — treat anything 11-specific as unverified):

- `GenericPlugin` (`Playnite.SDK.Plugins`) is the correct base type — it extends behavior without
  adding a library source, matching this integration exactly.
- `OnGameStarting(OnGameStartingEventArgs args)` — `args.CancelStartup = true` blocks the launch;
  confirmed real and settable. This is the Windows pre-launch boundary `00-inventory.md` §1 notes
  does not exist today (`ProcessWatcher` is explicitly never used as one). A companion
  `OnGameStartupCancelled(OnGameStartupCancelledEventArgs args)` fires when a startup is cancelled
  (its args expose only `Game`, no reason field — the plugin has to remember why it cancelled, e.g.
  in a field set just before setting `CancelStartup`).
- `OnGameStopped(OnGameStoppedEventArgs args)` — confirmed, carries `ElapsedSeconds` and
  `ManuallyStopped`. **Caveat that matters for reliability**: Playnite's own docs and a real,
  previously-filed issue both document that exit detection depends on a per-game "Tracking Mode"
  setting and is **not** reliable for every emulator/wrapper shape out of the box (games launched
  through an emulator or a launcher Playnite merely wraps can fail to report as stopped without the
  user manually setting Tracking Mode to `Folder`). For SaveLocker's purposes this mostly doesn't
  bite — the games this integration targets are launched directly by Playnite, not re-wrapped through
  a second launcher — but it means `OnGameStopped` cannot be the *only* signal an exit-push relies
  on; keep the existing `Watchers.cs`/`ProcessWatcher` exit-push as a fallback for exactly the cases
  where `OnGameStopped` might not fire, rather than replacing it outright.
- **Target framework: .NET Framework 4.6.2** (current `PlayniteSDK` NuGet, 6.16.0) — this plugin is
  a *separate* project/assembly from `src/Agent` (net10.0-windows); it cannot share code directly and
  must talk to the tray agent the same way Decky's Python backend talks to the Linux daemon: HTTP to
  `127.0.0.1:5178` with the local token. This is a second confirmation that the "external frontend
  talks to `:5178`, never links against `Agent.Core`" pattern is the right one architecturally, not
  just convenient for Decky.
- **UI thread safety: confirmed not fully thread-safe**, with an official, named escape hatch —
  `IPlayniteAPI.MainView.UIDispatcher` (`System.Windows.Threading.Dispatcher`). Any WPF dialog this
  plugin shows from `OnGameStarting`/`OnGameStopped` must be marshalled through
  `PlayniteApi.MainView.UIDispatcher.Invoke(...)`, mirroring exactly the lesson this codebase's own
  tray already learned the hard way (`Decisions.md` WA-09, `UiDispatcher` — the parallel is close
  enough to be worth naming explicitly for whoever builds this).
- Distribution: a `.pext` package via Playnite's official, GitHub-hosted add-on database
  (`JosefNemec/PlayniteAddonDatabase`) — a real, existing marketplace, unlike Decky's plugin, which
  this repo's own history already found could not honestly be submitted to its store
  (`CONTEXT.md`). Playnite's submission bar (a PR with a manifest) is materially lower than Decky's
  (which requires attesting AI was not used for a majority of the code) — worth noting since it
  changes the realistic distribution story for this half of the feature.

### Pre-start PowerShell script vs. Playnite extension vs. launcher-agnostic wrapper

| | Setup ease | Reliability | Can actually block launch | Maintenance |
|---|---|---|---|---|
| Pre-start PowerShell script | Low — per-game, manual, easy to typo | Low — a script exiting non-zero to block launch is undocumented/unsupported behavior for Playnite's script action type and was never verified to actually stop a launch | Unconfirmed | High — one script per game, drifts silently |
| **Playnite `GenericPlugin`** | **High — one install, applies to every Playnite-launched game** | **High for launch (`CancelStartup` is a documented, real mechanism); moderate for exit (`OnGameStopped` caveat above)** | **Yes, confirmed** | **Low — one codebase, Playnite's own update/add-on-database channel** |
| Launcher-agnostic wrapper (a SaveLocker-authored `.exe` set as the game's target, mirroring the Linux `savelocker run` idea) | Moderate — one wrapper, but Windows games are launched a dozen different ways (Steam, Epic, GOG, standalone .exe, Playnite) with no single "set the launch target" convention as uniform as Steam's Launch Options field | High where it applies | Yes, by construction | Moderate — one codebase, but N different "how do I get this in front of the launch" integrations per launcher |

**Recommendation: the Playnite `GenericPlugin`, for the games launched through Playnite; nothing
replaces it for games that aren't.** State plainly, since the brief asks for it: **Playnite only
covers games launched through Playnite.** What covers the rest is exactly what already covers it
today — the tray's `ProcessWatcher`-driven exit-push, with no pre-launch pull on Windows at all
(`Decisions.md` WA-01, unchanged by this design) — plus the manual chooser surfaced whenever a
`Sync All`/`Pull` operation (tray menu, agent UI, CLI) hits an open conflict. A game launched
outside Playnite and outside Steam's Linux wrapper simply has no automatic pre-launch gate; the
conflict is still caught (at push time, or the next time any sync operation runs) and still
resolvable from the tray/agent UI/CLI — it just isn't prevented from launching with stale local
state. This is a real, named gap, not a silently-accepted one — see the risk register.

---

## Linux desktop and headless — capability-detection and escalation ladder

The brief's own ordering, confirmed against what's actually available to build each rung with:

1. **Detect environment.** `$WAYLAND_DISPLAY`/`$DISPLAY` (env vars, trivial), session D-Bus
   (`$DBUS_SESSION_BUS_ADDRESS` set and the socket connectable), a running notification daemon
   (query `org.freedesktop.DBus` for `org.freedesktop.Notifications` ownership — a connectable
   session bus with no owner for that name means "bus exists, no notification daemon," a real and
   distinct state from "no bus at all"), an attached TTY (`Console.IsInputRedirected` /
   `isatty(0)` equivalent), and whether running under `systemd --user` (`$INVOCATION_ID` set, or
   `sd_booted()`/checking `/run/systemd/system` — this codebase already runs under `systemd --user`
   exclusively for the daemon, so this specific check is more about "am I the CLI running
   interactively" vs. "am I the unit," which the process's own argv/TTY state already answers more
   directly than querying systemd).
2. **Native modal in the Wayland UI, when a session is present.** This is `savelocker ui`'s existing
   `Screen` enum (`UiApp.cs`) gaining a conflict screen/modal, drawn with the existing Dear ImGui
   widget set (`Widgets.cs`) — no new rendering technology, reusing the exact gamepad-focus machinery
   (`ButtonDown`-driven nav, `Widgets.Hot()` for hover-gating) this codebase already built and
   documented the pitfalls of (`Gotchas.md` → ImGui/Deck UI). This rung serves both the Deck's Game
   Mode and a KDE desktop session running the daemon's bundled `agent-ui` — for a desktop session,
   "native modal" is more naturally the existing browser-based `agent-ui` at `:5178` gaining a
   conflicts page, since that's already how Desktop Mode is served (`00-inventory.md` §5: "Linux UI:
   headless daemon serving the existing React UI on `:5178`").
3. **Desktop notification with action buttons** (`org.freedesktop.Notifications`, the standard
   `org.freedesktop.Notifications.Notify` D-Bus method with an `actions` array) — needs a .NET D-Bus
   client; this codebase has none today (needs verification/decision — see the feasibility report
   and open questions — a library such as `Tmds.DBus` is the realistic option, a new dependency this
   design should flag rather than assume). The action button opens the chooser — i.e. rung 2's
   surface (the `agent-ui` conflicts page, opened in the default browser) or, on the Deck
   specifically, brings `savelocker ui` to the foreground if it's already running.
4. **Local web chooser bound to `127.0.0.1`.** This is not new infrastructure — it is the *existing*
   `agent-ui` on `:5178`, which is already loopback-only and already reachable over an SSH tunnel
   (the exact pattern this codebase's own docs already tell users to use: `ssh -L
   5178:localhost:5178 deck@<ip>`, per `cli-reference.md`'s `daemon` command doc). The only new work
   is a conflicts page in `agent-ui` itself (shared by every rung above rung 4 that can display a
   browser) and surfacing its URL prominently — in `agent.log`, and in `doctor`'s output — whenever a
   conflict opens and no richer rung was reachable.
5. **CLI/TUI.** New commands, since none exist today (`00-inventory.md` §6.1):
   ```
   savelocker conflicts                                   # list, one line per open conflict
   savelocker resolve <game> --keep-local|--keep-remote [--keep-both]
   ```
   Both are thin CLI wrappers over Layer 2 of the Resolution API (`GET /api/conflicts`,
   `POST /api/conflicts/{id}/resolve`), run through `AgentCli.cs` exactly like `push`/`pull` today.
   `doctor` gains a line per open conflict too, since it's "the only UI a headless Deck install has
   outside Game Mode" (REPO_MAP) and must not stay silent about the one condition that pauses sync
   indefinitely.
6. **Optional out-of-band notify** (webhook / ntfy / email) for genuinely unattended boxes — a new,
   optional per-server (not per-agent — the server already knows about every open conflict fleet-
   wide) setting, `AppSetting` key `conflicts.webhook_url`, fired by the server itself when a
   conflict is created or escalates, not by the agent — the server is the only party guaranteed to
   be reachable regardless of which specific machine is stuck. This is new scope, explicitly flagged
   as optional/deferred in the phased plan, since it's the least safety-critical rung (rungs 1-5
   already guarantee the terminal state below).
7. **Terminal state, always reachable.** This is not a new mechanism — it is simply
   `ConflictFlag.Status == Open` plus `SyncEngine` refusing to auto-resolve under `Manual` (or an
   unreachable "ask"), which is what the codebase does **today**, unchanged. Nothing about this
   design weakens it; every rung above is purely about *surfacing* the same durable, already-safe
   state faster and from more places.

### Policy resolution order

Covered in `01-conflict-model-spec.md` §1: `Game.ConflictPolicy ?? global default ?? Manual`.
"Always ask" degrading to "nobody can be asked" is not a special case — it is `Manual` (or a policy
that names a machine that isn't the one deciding) reaching rung 7 because rungs 1-6 were all
unavailable or timed out. No code path substitutes an automatic pick for an unanswerable ask.

### May a game launch while a conflict is pending?

Per `02-resolution-api.md` §Layer 3: `LaunchDecision` is `Proceed` (fast-forward/no-op, or an
auto-resolving policy applies), `ProceedSyncPaused`, or `Blocked`. **Recommendation:
`ProceedSyncPaused` under `Manual` policy on Linux (Deck and desktop alike), never `Blocked`, unless
the user has explicitly opted into "block launch until resolved" as a per-game or global
setting.** One line each for the other two options: *Block launch* is the safest against data loss
but directly contradicts this whole project's premise (Steam/Epic Cloud resolve at play time, they
don't refuse to let you play) and would be uniquely punishing on a Deck, where "just play it" is the
entire point; *allow launch after auto-snapshotting local state* is not actually a distinct third
option once the model spec's commit-before-choose mechanism (§2) is adopted — snapshotting already
happens as a side effect of attempting the pre-launch push, so what's left to decide is only whether
the *pull* half proceeds (fast-forward/no-op) or is skipped (`ProceedSyncPaused`, when a real
conflict was found) — which is exactly `ProceedSyncPaused`. A per-game "block until resolved" opt-in
exists for the minority of players who would rather lose a play session than risk playing on stale
data (e.g. a strict-permadeath run) — flagged as a small, optional setting in the phased plan, not a
default.

### `savelocker wrap`/`savelocker run` headless behavior

Must never hang waiting for input — this is already true of the existing wrapper (`ProtonRun.cs`
never prompts; every failure is logged and the game launches anyway, "a save-sync tool that prevents
you playing is worse than one that misses a sync"). The new gate call preserves this exactly:
`PrepareLaunchAsync` is a bounded, non-interactive method — it never shows UI itself, only returns a
decision — so there is nothing in the wrapper's own code path that could block on a human. The one
new failure mode to guard explicitly: the commit-before-choose push (model spec §2) must have the
same bounded timeout behavior the existing push already has (the existing `HttpClient` 100s default
plus the existing retry-via-offline-queue path) — not a new, unbounded wait.

### The shared D-Bus/socket interface

There isn't a new one — this design deliberately does **not** introduce a Unix-socket or D-Bus RPC
interface *between* SaveLocker's own processes. The existing `:5178` HTTP API (Layer 2 of the
Resolution API) is already the shared interface every frontend on the box uses, exactly as it is
today for launch options and Decky status. The only D-Bus usage this design adds is **outbound**,
from the agent to the desktop's own notification daemon (rung 3) — SaveLocker is a D-Bus *client*
of `org.freedesktop.Notifications`, never a D-Bus *server* of its own. This keeps the interface
count at one (`:5178`) rather than two, and avoids reopening `Backlog.md`'s already-deferred "one
state owner for the Linux agent... wrapper→daemon IPC over a Unix socket" item, which is a distinct
question (state ownership between the wrapper and daemon processes) this design does not need to
answer to ship conflict resolution.
