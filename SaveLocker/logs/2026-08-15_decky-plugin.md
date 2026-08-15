# Task: Decky plugin — launch options applied automatically, and a Game Mode status surface

From [[Backlog]] → Medium → *Decky plugin*. Planned 2026-08-15.

**Execute ONE phase per session, verify it, stop.** Phases 1–2 are agent-side and ship on their own
with no plugin in existence, fully covered by `run-linux-tests`. Phase 3 is the first one that needs
Decky. **Phases 1–3 are done and verified on hardware.** Phase 4 (a Game Mode status surface) and
Phase 5 (the agent keeping the plugin updated) are both optional and independent — stopping after 3
leaves a coherent, working feature. Of the two, **Phase 5 is the one users would feel**: without it
the only route to update prompts is Decky's custom-store setting, which replaces the official store
while it is set.

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
- **Do not ask for the `_root` flag** (corrected 2026-08-15 — the original plan assumed root and was
  wrong). Everything the backend needs is the desktop user's own: the `api-token` is 0600 owned by
  that user, and the agent's API is loopback. Root buys nothing, and a root-created file under
  `~/.local/share/SaveLocker/` would break the agent the next time it rewrote that file as the
  desktop user. The plugin stays read-only against the agent's state directory and mutates only
  through the API. **Phase 5 depends on this:** Decky recursively chowns a non-`_root` plugin's files
  to the desktop user, which is the only reason the agent can update the plugin at all.

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

## Phase 2 — Agent: `GET /api/launch-options` publishes desired state — **DONE 2026-08-15**

Ships alone. A new read-only endpoint plus a CLI command; the console and the plugin are unaffected.

**Outcome:** `run-linux-tests` 137 → **154/154** (17 new; baseline in [[Build and Run]]).
`run-local-api-tests` **30/30**, solution builds clean.

**A third route the plan was missing, and Phase 3 could not have worked without it.** The plan had
the plugin compare a game's current options against `desired` and write when they differ — but
`desired` is only correct for a game that has *nothing* set. Following it literally, the plugin would
have **overwritten every user's `mangohud`, environment variables and per-game arguments** the first
time it ran. The merge needs the current value (only Steam has it) and the rule (only the agent has
it), so neither side can do it alone: `POST /api/launch-options/resolve` takes a batch of current
values and returns each game's target plus a `changed` flag. Phase 3's plugin should use *that*, and
treat `desired` as the empty-game shortcut. Decision 1 survives intact — the plugin still holds no
rule.

**A real bug in Phase 1, found by the harness rather than by reading it.** `Apply` recognised its own
output by the wrapper being *named* `savelocker`, so for a wrapper with any other name it did not
recognise it and wrapped it again on every pass — unbounded growth on a timer. Not hypothetical: run
from a build tree the agent is `dotnet savelocker.dll`, so the resolved path is the **dotnet host**,
which is exactly the shape the harness runs in. An occurrence now counts as ours if the path is
named `savelocker` **or** equals the wrapper being applied; the first half still finds a stale one to
repair, the second keeps idempotence independent of the file's name. Pinned by its own check.

Also: `doctor` gained per-game launch-option state (unknown is not a failure; a recorded error is,
and it makes doctor exit non-zero), `savelocker launch-options` gained `--list`/`--check`, and
`agent-ui/src/api-types.ts` was regenerated — **CI runs `gen:api -- --check`, so adding a route
without regenerating fails the build.** The KB article `web/src/help/cli-reference.md` now documents
the command; Phase 1 should have done that and did not.

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

## Where the plugin lives

**Its own repository:** <https://github.com/SkorcherX/SaveLocker-Decky>, extracted 2026-08-15 with
`git subtree split` so its history came along. It was never viable as a subdirectory here — Decky's
plugin database tracks plugins as submodules whose *root* is the plugin.

**It is not on the Decky store, and cannot honestly be submitted as written.** The store's PR
template requires attesting that generative AI was not used for a majority of the submitted code;
this plugin was largely AI-written. Store listing would also need verification on both Stable and
Beta SteamOS and a third-party testing report. Distribution is therefore direct: a **custom Decky
store URL** (`store/plugins` in that repo, regenerated by its release workflow with the artifact's
SHA-256) for people who want update prompts, or a plain release-zip URL for people who would rather
not change their store setting. The custom store replaces the official one while set — that trade is
documented in the plugin's README.

## Phase 3 — The plugin: read, diff, apply — **DONE, VERIFIED END-TO-END ON HARDWARE 2026-08-15**

**The write path is proven on a real Deck.** Dragon Quest III was deliberately set to a bare
`savelocker run -- %command%` — the documented Game Mode failure — and the plugin repaired it to the
absolute path. Verified four ways: Steam's own API, `shortcuts.vdf` **on disk** (so Steam really
persisted it), the agent's record, and `doctor`/`--check` (`0 of 4 not confirmed`, exit 0). The other
three games came back byte-identical, both `WINEDLLOVERRIDES` strings intact.

**The field question is answered, and the fear was unfounded.** A non-Steam shortcut populates
**both** `strLaunchOptions` and `strShortcutLaunchOptions`, with the same value. Reading cannot come
back empty for a game that has options, so the clobber risk the dry-run was built to guard against
does not exist on this Steam version. The dry-run stays: it is what made it safe to find that out,
and it is worth having when Valve changes something.

**Three things the plan did not anticipate:**

1. **A missing `package.json` broke loading**, as `SyntaxError: Unexpected token 'export'` thrown
   from inside Decky's loader. Decky picks the load path from that file, and without it evals an ESM
   bundle as a classic script. The install docs listed three files; four are required. In [[Gotchas]].
2. **The QAM would not scroll** — rows must be `Focusable` or the D-pad cannot reach them, so
   anything past the fold is unreachable. Four games already overflowed.
3. **`_root` was wrong to ask for** and has been dropped. The token is the desktop user's own 0600
   file; root bought nothing and carried the state-dir hazard the plugin's own docstring warned about.

**Deck state after this session:** running an unreleased `0.5.6-deckytest` agent; plugin installed at
`~/homebrew/plugins/SaveLocker`, owned by `deck` so it can be updated over SSH without sudo. Backup
of `config.json`/`shortcuts.vdf`/`localconfig.vdf` at `~/savelocker-decky-backup-20260815-112213`.

---

### Status before the plugin was loaded (kept — the agent-side findings are the valuable part)

**Hardware pass, first session (2026-08-15, Deck at 192.168.68.67 over SSH).** Everything on the
agent side is now proven on a real Deck. The plugin itself is still unloaded: `~/homebrew/plugins`
is root-owned and there is no passwordless sudo, so the final install is a manual step. Files are
staged at `~/savelocker-plugin-stage` and a backup of `config.json`, `shortcuts.vdf` and
`localconfig.vdf` sits in `~/savelocker-decky-backup-<stamp>`.

**Two bugs found, neither visible to any suite** — both fixed and covered (`b31e160`):

1. **A tracked game with no recorded `SteamAppId` synced nothing, silently.** Two of the four games
   on the Deck. The wrapper logged "no tracked game matches this launch" and played the whole
   session unsynced. A four-minute Khazan session on 2026-08-11 was never pushed. See the commit.
2. **The stale-path repair would have rewritten working launch options** — the maintainer's OCTOPATH
   carries `WINEDLLOVERRIDES=… /home/deck/.local/bin/savelocker run -- %command%`, and
   `~/.local/bin/savelocker` is the symlink `install.sh` creates. Only a *relative* path is repaired
   now.

**Verified on the Deck:**
- `install.sh` over a **running** daemon: survived, came back active, config/API key/games intact.
  Done twice. (First time this path has been exercised on hardware.)
- `/api/launch-options` returns all four games with **unsigned** AppIDs.
- `resolve` against the real `shortcuts.vdf` values returns `changed: false` for both games that
  already had options, leaving `WINEDLLOVERRIDES` and the symlink path untouched.
- The AppID backfill recorded both missing ids on daemon start.

**Still unverified, and the reason this phase is not done:** everything inside Steam. Whether the
plugin loads, whether `strShortcutLaunchOptions` is the field a Deck populates, and whether
`SetAppLaunchOptions` persists. The dry-run default (`8ed58f9`) exists so the first attempt cannot
destroy anything while answering the field question.

---

### Original plan (written before the hardware pass)

First phase that requires Decky Loader. Built from
`https://github.com/SteamDeckHomebrew/decky-plugin-template`.

**Status: the code exists and builds; nothing has run it.** `npm run build` produces
`decky/dist/index.js` and `tsc --noEmit` is clean against the real `@decky/api` / `@decky/ui`
packages — but that proves it *compiles*, not that it works. It has never been loaded by Decky,
never talked to a running agent, and never written a launch option. **Do not treat this phase as
done until the hardware checks below pass.**

What compiling does and does not buy: the `SteamClient` signatures were taken from
`decky-frontend-lib`'s own `globals/steam-client/App.ts`
(`SetAppLaunchOptions(appId: number, launchOptions: string)`,
`RegisterForAppDetails(appId, cb): Unregisterable`) and declared locally in `src/steam.d.ts`, so
they match the library's view. They are still undocumented Valve internals, and the library's view
can be wrong or stale.

**The known unknown to check first.** A non-Steam shortcut keeps its options in
`strShortcutLaunchOptions` while an installed Steam game uses `strLaunchOptions`. The plugin reads
whichever is non-empty. That is a guess about which field a Deck actually populates for the
shortcuts SaveLocker targets — and if it is wrong, the plugin reads an empty string, believes the
game has no options, and **overwrites a user's mangohud line**. Verify this before letting it near a
game whose options you care about.

Two other things worth knowing before the hardware pass: `@decky/rollup` exports a **default**
`deckyPlugin()`, not the `defineConfig` the template's docs imply (the first config written here was
wrong and failed to build), and `decky/dist` + `decky/node_modules` are gitignored.

### Steps

1. New top-level `decky/` directory (a separate build; it is **not** in `SaveLocker.sln`). Add a
   line to [[REPO_MAP]] and a note in [[Build and Run]] for how to build and side-load it.
2. **Python backend** — reads `~/.local/share/SaveLocker/api-token`, calls `GET /api/launch-options`
   on `127.0.0.1:5178` with the header, exposes the result to the frontend, and posts results back
   to `/api/launch-options/applied`. Read-only against the state directory (see the root trap).
   Degrade quietly when the agent is not installed or not running — the plugin must not error-toast
   on a machine where SaveLocker was never set up.
3. **Frontend** — for each row, read the game's current launch options
   (`SteamClient.Apps.RegisterForAppDetails` → `strLaunchOptions`), **POST them all to
   `/api/launch-options/resolve`**, and call `SteamClient.Apps.SetAppLaunchOptions` only for the rows
   that come back `changed: true`, using the `desired` string verbatim. Do **not** compare against
   the `desired` from `GET /api/launch-options` and write on a difference: that value assumes the
   game has nothing set, so doing so would wipe out every user's `mangohud`, env vars and per-game
   arguments. The merge is a round trip precisely because neither side can do it alone.
4. Run on plugin load and on a slow timer (a few minutes is plenty — enrollment is rare), plus a
   manual "Apply now" button. Idempotence from Phase 1 is what makes the timer safe.
5. A visible list: game, applied / not applied / error. Nothing silent — a plugin that edits Steam
   settings without showing what it did is one nobody will trust.

### Verify

**Hardware only** — this cannot be covered by `run-linux-tests`, which is exactly why Phases 1 and 2
carry the logic. Fold it into the **Deck hardware pass** already queued in [[CONTEXT]] → Next action.

Run these in order, and **on a throwaway shortcut first** — the field question above means the early
runs can destroy real launch options:

1. The plugin loads in the QAM and says something sensible with **no agent running** ("The SaveLocker
   agent is not running"), then with one running.
2. `strShortcutLaunchOptions` vs `strLaunchOptions`: add a non-Steam shortcut, set
   `mangohud %command%` by hand, and confirm the plugin **reads it back**. This is the check that
   everything else depends on.
3. A fresh shortcut with empty options gets the wrapper, and the game then actually syncs on launch.
4. One with `mangohud %command%` ends up wrapped, not clobbered.
5. One with a hand-typed bare `savelocker run -- %command%` is repaired to the absolute path.
6. A second pass changes nothing and toasts nothing.
7. `savelocker doctor` now reports those games as confirmed, and `launch-options --check` exits 0.

Record the before/after launch-options strings for each in the write-up.

---

## Phase 4 — The Game Mode status surface — **DONE, VERIFIED ON HARDWARE 2026-08-15**

Shipped as **SaveLocker-Decky v0.2.0**. Needed **no agent or server change**: `/api/state` already
carried lease warnings, last-sync and counts, `/api/agent-version` already knew about a staged
update, and `doctor` is executed rather than fetched.

**What it has:** lease warnings (toasted once each, dismissable) · status (server, last sync, games,
saves, agent version + update waiting) · launch options (Phase 3) · **push/pull, per game or all** ·
`doctor` on demand.

**Sync controls were added beyond the original list**, at the maintainer's request: forcing a save
one direction is exactly the situation a Deck user cannot resolve without Desktop Mode. They run the
CLI rather than reimplementing anything, so they inherit its guards — a pull refuses while the game
is running (CLI *and* SyncEngine), a plain pull refuses to overwrite un-pushed changes, a diverged
push becomes a conflict. `--force` is the only way to lose data and sits behind a destructive-styled
confirmation naming the game; deliberately **not** a toggle, which could be left on.

**Three UI bugs, all found only by using it on the Deck** — all now in [[Gotchas]] → Decky:
the panel remounts when a Steam dropdown closes (destroying component state, which is why the game
selection never stuck); a read-only row needs `Field focusable`, not a bare `Focusable`, or the QAM
cannot scroll to it; and `Field` renders children in a right-hand column unless told otherwise.

The third took four attempts. What finally solved it was logging state **at render time** rather than
reasoning about the handler — the first three attempts were guesses, and one of them ("the plugin is
reloading constantly") was an artefact of `Runtime.enable` replaying the console buffer. **Measure
live, discard the buffer first.**

---

## Phase 4 — original plan

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

## Phase 5 — The agent keeps the plugin updated — **DONE 2026-08-15 (harness only; no hardware yet)**

**Outcome:** `run-linux-tests` 161 → **197/197** (36 new checks; baseline in [[Build and Run]]).
Solution builds clean — `Agent.Linux` 0 warnings, `Agent` with only the pre-existing MSB3277 — and
`web` typechecks. `run-server-bugbounty-tests` reads **162/164**: both failures are the SteamGridDB
key-verification pair, **reproduced on pristine `main` with the tree stashed**, so they are not from
this change; they are now in [[Backlog]].

**The refusal checks discriminate — and the two beside them do not, which is worth knowing.** Run
against a build with the plan-before-write guard removed (`CanReplace` no longer consulted,
everything queued unconditionally), the run reads **195/197**: *a package needing a new top-level
file is REFUSED* and *the refusal names the reinstall route* both fail, because without the guard
there is no refusal at all — the copy loop simply throws when it reaches the unwritable path.

But *the refused package wrote NOTHING* and *not even the file it could have written* **still
passed**, which they should not have. They survived only because `Directory.EnumerateFiles` happened
to yield `py_modules/helper.py` before `dist/index.js`, so the throw came before anything was
written. Reverse that order and the same broken build leaves a half-installed plugin those two
checks would call clean. They are worth keeping — they are the assertion that matters — but they are
**order-dependent**, and the refusal pair is what actually pins the behaviour. Do not read a green
run of just those two as proof the guard is there.

### Two deviations from the plan below, taken deliberately

1. **No `GET /api/agent/plugin/latest`, and no new machinery.** The plugin is a **third
   `AgentPlatform` slot** (`decky-plugin`), so it inherits the upload, digest, sidecar, atomic
   replace, delete, GitHub fetch, poller, download route and console card wholesale, and the agent
   asks the route it already asks: `/api/agent/latest?platform=decky-plugin`. The step said this
   would be "mostly threading a third value through the platform enum" — it was, and inventing a
   parallel route would have thrown that away. The one genuinely new thing a slot needed is a
   **per-slot GitHub repo**, because the plugin releases from `SkorcherX/SaveLocker-Decky`.
   `decky-plugin` is not a RID, unlike its two neighbours; that is documented where the constant is.

2. **The file manifest is the package's own entry list**, not a manifest file the package ships. The
   step called for the package to "carry a file manifest" — but a zip already is one, and requiring a
   new file inside it would have made every check depend on a plugin-side release landing first. The
   agent enumerates the extracted payload, resolves each destination and proves it writable (probed
   for real, not read off mode bits — SteamOS mounts things read-only), and refuses the whole package
   if any is not. Same guarantee, nothing new to keep in sync.

Two smaller things worth knowing. `plugin.json` is **skipped by name**, not discovered by failing —
the whole point is that nothing is attempted that could fail partway. And `package.json` is written
**last**: every write trips Decky's watcher, so the plugin may reload mid-update, and it is far
better for it to run new code briefly reporting the old version than to report the new version while
running the old code.

`UpdateChecker` was extended rather than duplicated, as the step required: `FetchLatestAsync(platform)`
was factored out of `CheckAsync` (the plugin's comparison is against the *plugin's* installed version,
not the agent's), and `DownloadInstallerAsync` took a `PackageKind` so the payload shape check knows a
zip from a tarball. Everything else about the download — the off-origin rule, the credential rule, the
size cap, the digest — is untouched and shared.

### Still unverified, and the reason this is not "shipped"

**None of it has run on hardware.** The harness proves the agent's half against a fake
`~/homebrew/plugins/SaveLocker`; what it cannot prove is the part that makes the feature work at all
— that Decky notices the files change and reloads the plugin. That mechanism *was* observed during
Phase 4 (a `touch` as the desktop user, and repeated `scp`s of real builds), so this is not a guess,
but the agent doing it by itself has never happened on a Deck. Also unexercised on hardware: the real
release zip's shape (the harness builds its own), and the server hosting a plugin package at all.

Before the first real rollout: publish a plugin release, upload it in **Config → Agent updates →
Decky plugin**, and watch a Deck pick it up. Keep the backup route in mind — a manual reinstall
through Decky always works and is what the refusal path tells the user to do.

---

## Phase 5 — original plan

**The goal: a user installs the plugin once and never thinks about it again.** Today the only way to
get update *prompts* is Decky's custom-store setting, and Decky holds exactly one store URL — so
while it points at ours, the user sees no other plugins and is told about no other plugins' updates.
They will not leave it there, which means in practice they are never prompted about ours either.

The agent can do this instead, through the channel it already uses for its own updates.

### Why this works — verified in Decky's source, 2026-08-15

Three facts, and the feature falls out of them:

1. **The plugin's files belong to the desktop user, not root.** `browser.py` does
   `chown(plugin_dir, …HOST_USER, recursive=True)` for any plugin **without** the `_root` flag —
   which is why dropping that flag in Phase 3 matters more than it looked at the time. Only the
   top-level plugin directory stays root-owned, at 755.
2. **Hot reload is on by default.** The loader runs a `watchdog` observer over the plugins directory
   and reloads a plugin when its files change; `get_live_reload()` reads `LIVE_RELOAD` and
   **defaults to `"1"`**. It is not a developer-mode feature. So writing the files IS the install —
   no `systemctl`, no root, no Steam restart.
3. **Decky reads the plugin's version from `package.json`**, the same file being overwritten, so its
   own UI reports the new version afterwards without being told.

### Corrections from the Phase 4 hardware pass (2026-08-15)

Both were found by accident while deploying Phase 4, and both would have made this phase silently
ineffective:

1. **`plugin.json` is root-owned** — Decky chowns *it* to the effective user even for a non-`_root`
   plugin, while everything else goes to the desktop user. The agent can replace `main.py`,
   `package.json` and anything under `dist/`, but **never `plugin.json`**. Design the package so it
   does not need to.
2. **Hot reload requires the `debug` flag in `plugin.json`.** Without it the watcher fires and Decky
   logs *"Plugin X is already loaded and has requested to not be re-loaded"* — files updated, old
   code still running, nothing reported. It is the flag's only effect anywhere in the loader. The
   plugin now ships with it, and since `plugin.json` cannot be rewritten by the agent, **an
   installation predating that flag can never self-update** and needs one manual reinstall.

**The mechanism itself is proven.** With the flag set, `touch`ing a file as the desktop user made
Decky unload and reload the plugin in about a second — no sudo, no `systemctl`, no Steam restart —
and repeated `scp`s of a real build did the same throughout Phase 4's development.

### Scope, and the one thing this cannot do

**First install still needs Decky or sudo**, because creating `~/homebrew/plugins/SaveLocker/`
requires root. That stays a one-time *Install Plugin from URL* paste — which is *better* than the
custom store, because it never touches the store setting at all. This phase is updates only.

### The constraint to design around, not discover

The top-level plugin directory is root-owned and 755, so the agent can **overwrite existing files but
cannot create new top-level ones**. `main.py`, `plugin.json`, `package.json` and everything under
`dist/` are fine — `dist/` is itself user-owned, so files there can be added and removed freely. But
a future plugin version that adds, say, a top-level `py_modules/` could not be installed this way.

So: the package carries a **file manifest**, the agent checks it can satisfy every path *before*
writing anything, and reports "reinstall needed" rather than half-applying an update. A partially
written plugin is worse than an old one.

### Steps

1. **Server** — a plugin slot beside `win-x64` / `linux-x64` in `AgentInstallerService`. It is the
   same shape (upload, digest, sidecar, atomic replace), so this is mostly threading a third value
   through the platform enum rather than new machinery. `GET /api/agent/plugin/latest` returns
   `{ version, downloadUrl, sha256 }` or 204. A third row in **Config → Agent updates**.
2. **Agent (Linux only)** — on start and on the existing update timer: if
   `~/homebrew/plugins/SaveLocker/package.json` exists, compare its `version` against the server's.
   Newer → download, verify the SHA-256 the server published, check the manifest against what is
   writable, then write. Reuse `UpdateChecker`'s download-and-verify wholesale; do not write a second
   one.
3. **Respect `AutoUpdate: false`** exactly as the agent's own updates do — report being behind,
   change nothing.
4. **Report it.** A `plugin.updated` / `plugin.update_failed` event through `HealthReporter`, so the
   console is told. On a Deck the console is the only place anyone would see it ([[Decisions]] §2).
5. **`doctor`** — report the installed plugin version, or that Decky is present but the plugin is
   not (with the one-paste install URL), or nothing at all when Decky is absent.

### Verify

`run-linux-tests`, with a fake `~/homebrew/plugins/SaveLocker` in the fixture HOME — no Decky needed,
the same trick the whole harness already uses. Cover: an update is applied; the version comparison
does not downgrade; a **wrong digest is refused and nothing is written**; a manifest naming a path
the agent cannot create is refused *before* any write; `AutoUpdate: false` reports but does not
apply; and Decky absent is silent rather than an error.

### The coupling to accept

SaveLocker would be writing into another application's directory. Decky's own design hands those
files to the user, so nothing is being weakened — but if Decky ever changes ownership or adds an
integrity check on load, this breaks. The fallback is the custom store or a manual reinstall, and
both keep working, so the failure is degraded rather than broken.

---

## Costs the maintainer accepted at planning time

- **A second thing on a Deck that can be stale.** Auto-update was just finished for the agent; the
  plugin has its own release cadence and no update channel of ours. Mitigated by decision 1 — the
  plugin holds no rules, so it should rarely need a release.
- **`SteamClient.*` is undocumented Valve internals** and breaks on Steam client updates. That
  breakage lands on users. Mitigated by decision 2 — when the plugin breaks, the copy-paste path is
  still there and still supported, and Phase 2's `doctor` reporting still names the problem.
- **Decky Loader is a hard dependency** and will never be assumed.
