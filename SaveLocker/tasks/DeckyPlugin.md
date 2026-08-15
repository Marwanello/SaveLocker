# Task: Decky plugin — launch options applied automatically, and a Game Mode status surface

From [[Backlog]] → Medium → *Decky plugin*. Planned 2026-08-15.

**Four phases. Execute ONE phase per session, verify it, stop.** Phases 1–2 are agent-side and ship
on their own with no plugin in existence, fully covered by `run-linux-tests`. Phase 3 is the first
one that needs Decky. Phase 4 is the status surface and is optional — stopping after 3 leaves a
coherent feature.

---

## The problem

Pasting `savelocker run -- %command%` into **Properties → Launch Options** is the only manual,
per-device step left in Deck setup, and it is the single most-missed action in the whole flow
([[Backlog]] → *Interactive setup guide*, which says the same thing from the console side). Nothing
today notices it was skipped: the game simply never syncs, and the user has no reason to connect
that to a text field they never opened. `agent-ui/src/components/LaunchSetupCard.tsx` exists purely
to soften this, and its warning box documents two ways it still goes wrong — a short command that
Game Mode cannot resolve because `~/.local/bin` is not on PATH, and a non-Steam shortcut with no
forced compatibility tool.

## Why a plugin, and not the agent doing it itself

**The agent cannot write launch options.** They live in Steam's `localconfig.vdf` / `shortcuts.vdf`,
which Steam holds in memory and rewrites wholesale on exit — an agent-side edit made while Steam is
running is silently discarded, and one made while it is closed races the next launch. This is the
same class of trap `SteamShortcuts.cs` already documents for reading.

A Decky plugin's frontend runs inside Steam's own JS context, so it can call
`SteamClient.Apps.SetAppLaunchOptions(appId, str)` and let Steam persist it through its normal path.
That is the whole justification for the dependency — nothing else here needs Decky.

## Decisions taken at planning time (maintainer, 2026-08-15)

1. **The plugin knows nothing about SaveLocker.** It reads a desired-state list from the agent's
   local API and applies it. Every rule, path and string lives in the agent, where it is testable
   without Decky, without Steam and without hardware. A change to the launch command must never
   require a plugin release.
2. **Decky is an accelerator, never the supported path.** `LaunchSetupCard` and the copy-paste
   instructions in `web/src/help/deck-supported-games.md` stay exactly as they are. A Deck without
   Decky Loader loses nothing it has today.
3. **Substitute, never concatenate** (see the table in Phase 1). Users run mangohud, gamemoderun and
   per-game arguments; appending would break all three.
4. **Never touch a game SaveLocker does not track.** The apply set is exactly the tracked games that
   carry a `SteamAppId`. No heuristics, no "we think you want this".
5. **Do not write launch options for Steam-installed titles by default.** They are hidden from Add
   Games for the Steam Cloud reason ([[Decisions]]); if a user deliberately enrolled one it is
   tracked, so rule 4 already covers it and no extra rule is needed.

## Three things that will bite if forgotten

- **The local API is token-gated and refuses foreign origins.** `LocalAuth` requires
  `X-SaveLocker-Token` on every `/api` route, rejects a non-loopback `Host`, and there is
  deliberately **no CORS policy**. The plugin's *frontend* runs on `https://steamloopback.host` and
  therefore **cannot call `:5178` at all** — this is working as designed, not a bug to route around.
  All agent calls must go from the plugin's **Python backend**, which reads the token from
  `~/.local/share/SaveLocker/api-token` (mode 0600). Do not weaken the Guard, do not add a CORS
  exemption, do not add a second unauthenticated port.
- **The signed→unsigned AppID trap.** `SteamShortcuts.cs` already documents it. `shortcuts.vdf`
  stores the non-Steam AppID as a signed 32-bit value; Steam's JS API and `SteamAppId` in the
  launch environment use the unsigned form. The two must be compared in one representation or every
  non-Steam shortcut — the entire target population — silently fails to match. Normalise in the
  agent (Phase 1), so the plugin never sees the question.
- **Decky plugin backends run as root.** Reading a `deck`-owned 0600 token from root works, but the
  plugin must not write anything into `~/.local/share/SaveLocker/` — a root-created file there would
  break the agent the next time it tries to rewrite it as `deck`. The plugin is read-only against
  the agent's state directory, and mutates only through the API.

---

## Phase 1 — Agent: the launch-option rewrite rule, as a pure function — **DONE 2026-08-15**

Ships alone. Nothing calls it yet; this phase is the rule and its tests.

**Outcome:** `run-linux-tests` 123 → **137/137** (14 new checks; baseline updated in [[Build and Run]]).
Both hosts build — `Agent.Linux` 0 warnings, `Agent` with only the pre-existing MSB3277. The checks
were run against a deliberately naive append-instead-of-substitute build first: **10 of the 14 fail**
there, including every idempotence check, so they discriminate rather than merely pass.

**Deviation from the plan below, taken deliberately.** The CLI seam went into
`Agent.Linux/Program.cs`, not `AgentCli.cs` as Phase 2 step 4 says. `AgentCli` is the *shared*
surface and launch options are meaningless on Windows (`LaunchCommandDto.Command` is null there), so
a shared `launch-options` would have been a command that cannot answer on half the fleet. Phase 2
should extend the Linux one rather than move it. It arrived in Phase 1 because the repo has no unit
test project — every suite drives a binary — so a pure function needs a CLI seam to be testable at
all: `savelocker launch-options [--preview "<existing>"] [--wrapper <path>]`.

`Daemon.LinuxLaunchCommand` now builds its string through `LaunchOptions.Invocation`, so the command
the agent UI shows and the command the rule writes cannot drift — and the daemon's copy picked up
the quoting it never had.

### Steps

1. New `src/Agent.Core/LaunchOptions.cs` — a pure static class, no I/O, no Steam knowledge:
   - `static string Apply(string? existing, string wrapperCommand)` — returns the launch-options
     string a game should have.
   - `static bool IsApplied(string? existing, string wrapperCommand)` — true when the wrapper is
     already present, so callers can no-op.

   The rule, in this order:

   | Existing | Result |
   |---|---|
   | null / empty / whitespace | `<wrapper> run -- %command%` |
   | already contains the wrapper path + ` run -- ` | unchanged (`IsApplied` true) |
   | contains `%command%` | the `%command%` token is replaced by `<wrapper> run -- %command%` |
   | non-empty, no `%command%` | `<wrapper> run -- %command% <existing>` |

   Row 3 is what preserves outer wrappers (`mangohud %command%` → `mangohud <wrapper> run --
   %command%`), leading `VAR=x` env assignments, and trailing game arguments, each in the position
   Steam expects. Row 4 is correct because Steam appends a `%command%`-less string to the game's own
   argv, which is exactly where it lands after the substitution.

   `Apply` must be **idempotent** — it will run on a timer, not once. Assert that in a test:
   `Apply(Apply(x)) == Apply(x)` for every row.

2. A **stale wrapper path** is a distinct case from row 2. If `existing` contains
   ` run -- %command%` preceded by a path that ends in `savelocker` but is *not* the current wrapper
   path, replace that path and leave everything else alone. This is what repairs a hand-typed short
   `savelocker run -- %command%` — the documented Game-Mode-PATH failure — into the working absolute
   form, and it is the single most valuable repair the feature performs.

3. Quote handling: the wrapper path may contain spaces (a non-default `--config` deployment, or a
   user who installed elsewhere). Emit it quoted when it contains whitespace, and treat a quoted
   occurrence as a match in `IsApplied`.

### Verify

`tests/linux/run-linux-tests.sh` — add a block driving the new class through the CLI (Step 4 of
Phase 2 gives it a command to drive; until then, exercise it from a small
`savelocker launch-options --dry-run` path or fold this verification into Phase 2 and keep Phase 1's
proof to the unit-level checks the harness already runs inline). Cover every row of the table, the
idempotence assertion, the stale-path repair, the short-command repair, and a quoted path.

Baseline: `run-linux-tests` 123 → expect **~135**. Update the baseline in [[Build and Run]].
`run-winagent-tests` must stay 114/114 — this file is in Agent.Core and compiles into both hosts.

---

## Phase 2 — Agent: `GET /api/launch-options` publishes desired state

Ships alone. A new read-only endpoint plus a CLI command; the console and the plugin are unaffected.

### Steps

1. `src/Agent.Core/AgentApiServer.cs` — add `GET /api/launch-options`, token-gated like everything
   else, returning one row per tracked game that has a `SteamAppId`:

   ```
   [{ "steamAppId": 2748531234, "gameId": "…", "name": "…", "desired": "\"/home/deck/…/savelocker\" run -- %command%" }]
   ```

   `steamAppId` is emitted as the **unsigned** 32-bit form, normalised in the agent — see the trap
   above. Windows returns an empty array (there is no wrapper command there; `/api/launch-command`
   already returns null and `LaunchSetupCard` renders nothing).

   The `desired` string is built from the same source `/api/launch-command` uses, so the two can
   never disagree.

2. Add `POST /api/launch-options/applied` — the plugin reports back `{ steamAppId, applied: bool,
   error?: string }`. This is what lets `savelocker doctor` say *"launch options are not set for
   this game"* instead of the user discovering it as a save that never synced. Store it on the
   tracked game (a nullable `LaunchOptionsAppliedAt` / `LaunchOptionsError`), and treat absence as
   "unknown", not "broken" — most users have no plugin.

3. `Doctor.cs` — report the new state per tracked game. Unknown is not a failure; a recorded error
   is. This is the phase's user-visible payoff even with no plugin installed.

4. `AgentCli.cs` — `savelocker launch-options` prints the desired list, and `--check` exits non-zero
   if anything is unapplied. Gives the harness something to drive and gives a desktop-mode user a
   way to see the same thing.

5. Regenerate the agent OpenAPI surface and hand-update `agent-ui/src/types.ts` if the agent UI is
   to show any of this (it need not, in this phase).

### Verify

`run-linux-tests` — the endpoint returns a row for a tracked shortcut and none for a tracked game
with no AppID; the AppID is unsigned; `--check` exits non-zero before and zero after a reported
apply; `doctor` names an unapplied game; the endpoint is refused without the token and with a
foreign `Origin`. Baseline → expect **~148**.

---

## Phase 3 — The plugin: read, diff, apply

First phase that requires Decky Loader. Built from
`https://github.com/SteamDeckHomebrew/decky-plugin-template`.

### Steps

1. New top-level `decky/` directory (a separate build; it is **not** in `SaveLocker.sln`). Add a
   line to [[REPO_MAP]] and a note in [[Build and Run]] for how to build and side-load it.
2. **Python backend** — reads `~/.local/share/SaveLocker/api-token`, calls `GET /api/launch-options`
   on `127.0.0.1:5178` with the header, exposes the result to the frontend, and posts results back
   to `/api/launch-options/applied`. Read-only against the state directory (see the root trap).
   Degrade quietly when the agent is not installed or not running — the plugin must not error-toast
   on a machine where SaveLocker was never set up.
3. **Frontend** — for each row, read the game's current launch options
   (`SteamClient.Apps.RegisterForAppDetails` → `strLaunchOptions`), compare against `desired`, and
   call `SteamClient.Apps.SetAppLaunchOptions` only where they differ. The diff is the agent's job
   in principle, but the *current* value only exists in Steam, so the comparison happens here —
   compare against the agent's `desired` string verbatim, never re-derive it.
4. Run on plugin load and on a slow timer (a few minutes is plenty — enrollment is rare), plus a
   manual "Apply now" button. Idempotence from Phase 1 is what makes the timer safe.
5. A visible list: game, applied / not applied / error. Nothing silent — a plugin that edits Steam
   settings without showing what it did is one nobody will trust.

### Verify

**Hardware only** — this cannot be covered by `run-linux-tests`, which is exactly why Phases 1 and 2
carry the logic. On a Deck: a fresh non-Steam shortcut gets the wrapper; one with `mangohud
%command%` is wrapped, not clobbered; one with a hand-typed short command is repaired to the
absolute path; a second run changes nothing; the game then actually syncs on launch. Record the
before/after launch-options strings in the write-up. Fold this into the **Deck hardware pass**
already queued in [[CONTEXT]] → Next action.

---

## Phase 4 — Optional: the Game Mode status surface

Only worth doing if Phase 3 lands well. Ranked by what is genuinely missing today:

1. **Lease warnings as a Steam notification.** `LeaseWarningStore` is on disk precisely so the
   warning survives to *some* UI, but today that UI is the agent UI or the console. A QAM toast is
   the only thing that reaches a user in Game Mode about to launch a game their desktop has checked
   out. This may be worth more than the launch options.
2. **`savelocker doctor` in the QAM.** Doctor is "the only UI a Deck has" ([[REPO_MAP]]), and
   reaching it means desktop mode and a terminal. Rendering its output in the QAM makes the whole
   troubleshooting story Game-Mode-native.
3. **Sync status** — last push, offline-queue depth, agent up/down. `/api/state` already returns it.
4. **Update-staged notice.** `Ui/UiApp.cs` already says it; a QAM toast is strictly more visible.
   Overlaps, low effort.

Enrollment from the QAM is deliberately **not** on this list — `savelocker ui` covers it and the
on-screen keyboard is the actual friction, so a plugin removes nothing.

---

## Costs the maintainer accepted at planning time

- **A second thing on a Deck that can be stale.** Auto-update was just finished for the agent; the
  plugin has its own release cadence and no update channel of ours. Mitigated by decision 1 — the
  plugin holds no rules, so it should rarely need a release.
- **`SteamClient.*` is undocumented Valve internals** and breaks on Steam client updates. That
  breakage lands on users. Mitigated by decision 2 — when the plugin breaks, the copy-paste path is
  still there and still supported, and Phase 2's `doctor` reporting still names the problem.
- **Decky Loader is a hard dependency** and will never be assumed.
