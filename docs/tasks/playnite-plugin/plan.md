# Task: Playnite plugin

Planned 2026-09-10, moved out to its own standalone task 2026-09-14 (asked directly — it had grown
well past a single sub-phase of another task). Originally scoped as `tasks/conflict-resolution-ui/
plan.md`'s Phase 13 and `implementation-grouping.md`'s Group 7, which locked down the *behavior*
(`IPlayniteAPI.StartGame`-buildable, depends only on that task's Phase 0/1 local API) but were never
the build-out. Both of those files now just point here — this task has **its own phase numbering,
independent of the conflict-resolution-ui task's**, and its own grouping document
(`implementation-grouping.md`, beside this file), following the exact same two-document pattern that
task already established: this file (`plan.md`) is the phase list with real dependencies; the
grouping document decides which phases share a session and in what order.

Written before any code changes, per this repo's "one phase per session, verify, stop" discipline
(`logs/2026-08-15_decky-plugin.md`).

**New, separate repository:** `SaveLocker-Playnite`, at `D:\Projects\SaveLocker\SaveLocker-Playnite`
— sibling to `SaveLocker-Decky`, which already lives at that same level on this machine. Same
reasoning as the Decky split: its own toolchain (`net462` + the Playnite SDK, no `SaveLocker.sln`
entanglement) and, longer-term, Playnite's own add-on distribution expects an addressable repo of
its own, not a subdirectory here.

**This session's environment is a real Windows box** (`win32`, Playnite already installed under
both `%LocalAppData%\Playnite` and `%AppData%\Playnite`, `dotnet 10.0.400` present) — unlike the
cloud/remote sessions `implementation-grouping.md` wrote its "Windows + Playnite installed" caveat
against. Phase 1 below (scaffold + load) is genuinely attemptable here, not just plannable.

## Status (updated 2026-09-16 — Group 5/Phases 13-15+17 built, not yet hardware-verified)

Same status-table convention `conflict-resolution-ui/plan.md` established — kept current as phases
ship, not written once and left stale. See `implementation-grouping.md` for which phases share a
session and why.

| Phase | Status |
|---|---|
| 1 — Windows launch-gate rewiring | ✅ Shipped 2026-09-14 |
| 2 — Populate `SteamAppId` on Windows | ✅ Shipped 2026-09-14 |
| 3 — `PullBeforeLaunchEnabled` moves server-side | ✅ Shipped 2026-09-14 |
| 4 — `PushAfterExitEnabled` | ✅ Shipped 2026-09-14 |
| 5 — Three small new local-API routes | ✅ Shipped 2026-09-14 |
| 6 — `AgentPlatform.PlaynitePlugin` slot | ✅ Shipped 2026-09-14 |
| 7 — Windows plugin self-updater | ✅ Shipped 2026-09-14 — code-complete; no real plugin package exists yet to test an actual install against |
| 8 — Scaffold + "hello world" load | ✅ Shipped 2026-09-15 — hardware-verified, loads clean in the real portable Playnite |
| 9 — Local API client + settings page | ✅ Shipped 2026-09-15 — hardware-verified against a real test agent |
| 10 — Core pre-launch/post-exit gate | ✅ Shipped 2026-09-15 — hardware-verified: block/resolve on a genuine conflict, post-exit push, fail-open when the agent is down. Resolve window is theme-driven for both Desktop and Fullscreen mode, but the Fullscreen window and the WSL-driven "lease held elsewhere" case were fixed late this session and not yet re-confirmed on hardware |
| 11 — Automatic matching chain | ✅ Shipped 2026-09-15 — hardware-verified (a real Steam-installed game matched via AppID) |
| 12 — "Link to SaveLocker" popup | ✅ Built 2026-09-15 — code-complete, builds clean against the real Playnite SDK; not yet hardware-verified (see `SaveLocker-Playnite`'s own `docs/CONTEXT.md`) |
| 13 — Status chip + action buttons | ➡️ Moved to menu items — the `GetGameViewControl` status chip was built, then confirmed dead code and removed: Playnite only calls `GetGameViewControl` for a plugin registered via `AddCustomElementSupport` (SaveLocker never has) whose active theme's XAML names a matching slot (no stock theme, Default included, defines one for SaveLocker). `GetGameMenuItems`'s "Sync now"/"Resolve conflict…"/"Link to SaveLocker" and the `SaveLocker: Linked` Tag are the surviving, genuinely theme-independent surfaces. One deliberate deviation still applies to "Sync now": one item instead of separate Push/Pull, since the local API has no per-game push-only/pull-only route |
| 14 — Plugin-side self-update consumption | ✅ Built 2026-09-16 — new agent-side `GET /api/playnite-plugin` route verified live against a real scratch server + registered Windows agent; the plugin's own consumption (`OnApplicationStarted`) is code-complete but not yet hardware-verified inside a running Playnite |
| 15 — Test infrastructure | ✅ Built 2026-09-16 — the portable-Playnite `testenv` target and Windows `seed-test-conflict` equivalent this phase called for already shipped in Group 3's test-rig integration (commit `9397e9d`); this session added the remaining gap, automated coverage (`tests/SaveLocker.Playnite.Tests`, xUnit — GameMatcher + LocalApiClient against an HttpListener stub), 25/25 passing |
| 16 — Official add-on database submission | 🚧 In progress — prep staged 2026-09-16 (`SaveLocker-Playnite` branch `playnite-plugin-group-6`, `docs/addon-submission/`), PR deliberately NOT opened yet: blocked on a real version tag being pushed so the installer manifest's `PackageUrl` points at something that exists. `RequiredApiVersion` confirmed against the real `Playnite.SDK.dll` bundled in Playnite's current release, not guessed |
| 17 — Release CI workflow (`SaveLocker-Playnite`) | ✅ Built 2026-09-16 — `.github/workflows/release.yml`; the Playnite.SDK.dll-fetch step and a build against the freshly-fetched SDK were both actually run and verified, but pushing a real tag to exercise a genuine release end to end was not attempted (needs the user's go-ahead). Re-confirmed 2026-09-16 while prepping Phase 16 that it already publishes the `.pext` asset the "Installation" section and Phase 19 both need — no addendum was actually required, unlike this file briefly assumed below |
| 18 — Agent-side Playnite library reader + `agent-ui` tab (no plugin required) | ✅ Shipped 2026-09-16 — `src/Agent/PlayniteLibrary.cs` + `GameScanner` 4th source + `agent-ui` Playnite filter chip. Hardware-verified against this machine's real, installed Playnite — which caught and fixed two real bugs the risk section below anticipated but got wrong in the first pass: the real `games.db` is LiteDB 4's on-disk format, not 5's, and the collection is named `Game`, not `games`. 11/11 new unit tests passing |
| 19 — `agent-ui` "Playnite plugin" card: suggest by default, one-click direct install as an explicit opt-in | ✅ Shipped 2026-09-16 — `PlaynitePluginCard.tsx` + `GET /api/playnite-plugin/status` + `POST /api/playnite-plugin/install` (`PlaynitePlugin.InstallFirstTimeAsync`). Verified live against a real dev daemon on this machine (genuinely has Playnite without the plugin): card rendered the real not-installed state, and the install button correctly round-tripped through the new route |

---

## What the plugin actually does, and why it's needed at all

The Windows tray already syncs every tracked game's saves regardless of how it's launched — Steam,
Epic, GOG, or a bare `.exe` — through `ProcessWatcher` + `FileSystemWatcher` (`TrayApp.cs`). Playnite
is not required for ordinary push/pull. So the plugin's value is not "make sync work on Windows" —
it already does. Its value is one specific, narrow thing Windows has never been able to do safely:
**pull or block *before* a game starts.**

### The gap this closes

`SyncEngine.OnGameLaunchAsync`'s own doc comment explains why, and it is worth quoting because it is
the entire justification for this phase:

> `preLaunch` is the whole distinction between the two hosts, and it is not a tuning knob. Linux's
> `savelocker run -- %command%` runs *instead of* the game and starts it itself, so it can restore
> with certainty that nothing has the save open: true. Windows only has `ProcessWatcher`, which
> notices a game up to a poll interval after it started and after it opened its saves: false.
> Calling this "pre-launch" on Windows was the bug — the restore landed under a live process and the
> game overwrote it at exit.

That bug already happened once and was fixed by hard-coding `preLaunch: false` everywhere on
Windows — including in `AgentApiServer`'s own `/api/games/{id}/pre-launch-sync` route, which exists
today (scaffolded in Phase 11 as "The Decky/Playnite launch gate") but is wired, on Windows, to the
same never-pull-never-block delegate the reactive tray uses (`TrayApp.cs:114-128`). Windows has
never had a boundary certain enough to trust with a real pull-before-launch or a hard block on a
confirmed conflict — Phase 7's "automatic chooser" only reacts *after* a game is already running.

Playnite's `OnGameStarting` hook (`GenericPlugin`, fires before Playnite spawns the game's process)
is exactly that missing boundary — the Windows-side structural equivalent of Steam's `%command%`
wrapper on Linux. That is the whole plugin: a thin caller of an already-designed server-side
decision, from the one place on Windows that can call it safely.

### Required companion change, in *this* repo, not the new one

Because nothing calling `/api/games/{id}/pre-launch-sync` on Windows has ever been a genuine
pre-launch boundary before, `TrayApp.cs` deliberately does not wire in the real gate
(`SyncEngine.PrepareLaunchAsync`, what Linux's `ProtonRun.cs`/`Daemon.cs` use) — doing so ahead of a
real boundary caller would have been the exact same WA-01-class mistake for whatever called it next.
Playnite becomes that first genuine caller, so **this plan needs a small Phase 1 here first**: swap
`TrayApp.cs`'s `prepareLaunch` delegate from
`_engine.OnGameLaunchAsync(game, preLaunch: false, ct)` to `_engine.PrepareLaunchAsync(game, ct)` —
mirroring `Daemon.cs:171`'s existing Linux wiring exactly. Confirmed safe today: grepped the whole
repo and nothing currently calls this route (no agent-ui button, no CLI command) — it is unused
groundwork until a real caller exists. Testable without Playnite at all (existing `run-agent-tests`
plus a manual HTTP call proving the route now actually pulls and can return `Blocked`), and a clean
one-session unit on its own.

### The experience, end to end

1. Two PCs, both running the SaveLocker Windows agent + this plugin, both using Playnite as the
   frontend.
2. Player closes *Hollow Knight* on PC A; the tray's ordinary post-exit push uploads the new save —
   unchanged, the plugin is not involved in this half at all.
3. Player opens Playnite on PC B and starts *Hollow Knight*.
4. Before Playnite spawns the process, `OnGameStarting` fires and the plugin calls
   `POST /api/games/{id}/pre-launch-sync`:
   - **Common case** — no conflict: PC B pulls the newer save (a short "Syncing…" indicator; typically
     sub-second) and the game starts already up to date. The player does nothing.
   - **Conflict case** — both machines changed the save since the last sync (e.g. offline play): the
     plugin shows a resolve dialog and Playnite does **not** start the game until the player picks a
     side or cancels.
   - **Failure case** — agent not running, server unreachable, timeout: fail **open**, exactly the
     route's documented contract. The game launches anyway. SaveLocker is never the reason a game
     won't start.
5. Player plays; on exit the tray's existing post-exit watcher pushes as it always has. The plugin
   has no post-exit role — it is purely a pre-launch gate.

**What it deliberately does not do:** discover games, set save paths, replace the agent UI, or do
anything for a game launched any other way (a raw shortcut, Steam directly, a different launcher) —
for those, coverage is exactly what it is today. Strictly additive, same as Decky was to the
copy-paste launch-options path.

---

## Decisions carried over from the Decky plugin, unchanged

1. **The plugin knows nothing about SaveLocker's rules.** It calls one route and acts on the
   `LaunchDecision` it gets back; every rule (what counts as a conflict, when to pull, when to block)
   stays in `SyncEngine`, testable without Playnite.
2. **Fail open, religiously.** The route's own contract (`AgentApiServer.cs:780-786`) already says
   this; the plugin must never let a transport error, timeout, or 5xx read as a reason to block a
   launch. Only an explicit `Blocked` may.
3. **No CORS problem, unlike Decky.** Decky's frontend runs in a browser context
   (`steamloopback.host`) and cannot reach `:5178` at all, forcing a Python-backend split. A Playnite
   plugin is a plain in-process C# `HttpClient`, not a browser — `LocalAuth.IsAllowedOrigin` already
   treats an absent `Origin` header (any non-browser caller) as allowed. One assembly, no split.

## The one open design question Decky didn't have: matching a Playnite game to a tracked game

Decky's plugin matches on Steam AppID, which is authoritative there. Playnite's `Game` objects don't
share an ID space with `TrackedGame.GameId`. What's available today:

- `TrackedGame.InstallDir` — populated on every platform, vs. Playnite's `Game.InstallDirectory`.
- `TrackedGame.SteamAppId` — **null on Windows today** (its own doc comment: "Set for non-Steam
  shortcuts on Linux… Null on Windows"), vs. Playnite's `Game.GameId` for Steam-sourced entries.

**Recommendation: normalized-`InstallDir` matching for v1.** It needs no agent-side change, works
across every source Playnite supports (Steam, Epic, GOG, a manual install), and is the same kind of
path-based matching `LinuxGameScanner`/`PathResolver` already rely on elsewhere in this codebase. If
it proves ambiguous in practice (two Playnite entries pointing at one install, or a moved install),
add an explicit override the same way Decky's `Alias`/`PullBeforeLaunchEnabled` already solved this
exact class of problem — a `PlayniteGameId` field on `TrackedGame`, written back through the local
API by the plugin, so it survives a plugin reinstall or settings reset. **Don't build the override
until InstallDir matching is actually seen to fail** — same discipline Phase 14 already applies
elsewhere in this plan.

---

## Sync trigger controls — pull-before-launch / push-after-exit (added 2026-09-13, asked directly)

Mirrors Decky's existing pull-before-launch toggle, plus a new, symmetric push-after-exit toggle
that doesn't exist anywhere in this codebase yet.

**Pull before launch — reuses `TrackedGame.PullBeforeLaunchEnabled` and `/api/games/{id}/pull-
before-launch` as-is; no new field.** That toggle already exists and is already server-shared
(every machine syncing the game agrees on it), but today nothing in `SyncEngine` actually reads
it — `AgentApiServer.cs:357`/`1014-1016` only expose it for **Decky's own frontend** to decide,
client-side, whether to trigger a pull itself; `SyncEngine.PrepareLaunchAsync` (the gate both the
Linux wrapper and, after this plan's Phase 1, Windows/Playnite call) pulls **unconditionally** at
its last line, with no check of the flag at all.

**Recommendation: move the check into `PrepareLaunchAsync` itself, not the plugin.** Consistent
with decision 1 (the plugin holds no rules) — a client-side skip would mean Decky and Playnite each
re-implement the same "off by default for Steam Cloud, unless overridden" heuristic, and could
drift. Concretely, guard only the final `PullAsync` call:

```
if (EffectivePullBeforeLaunch(game))
    await PullAsync(game, force: false, ct);
```

where `EffectivePullBeforeLaunch` is `game.PullBeforeLaunchEnabled ?? !(game.HasSteamCloud ==
true)` — explicit override wins; absent one, off for a confirmed Steam Cloud game (avoids racing
Steam's own Cloud sync, the existing documented reasoning), on otherwise. **The open-conflict
short-circuit (line 819) and the commit-before-choose push (lines 835-852) stay unconditional** —
disabling the pull only skips fetching newer data, it must never also skip the safety check for a
genuinely diverged save. This is a small, surgical change, testable without either plugin (a
scratch call to `pre-launch-sync` for a `HasSteamCloud: true` game, asserting no pull happened but a
real conflict still blocks).

**Push after exit — new.** No existing field or route does this; `SyncEngine.OnGameExitAsync`
pushes unconditionally today, called by the tray's own reactive watcher independent of any plugin.
Add a `TrackedGame.PushAfterExitEnabled` (nullable `bool`, same pattern as `PullBeforeLaunchEnabled`
— null means "no override," defaulting to **true**, i.e. today's existing unconditional behavior,
so nothing changes for anyone who never touches the setting) plus a matching
`POST /api/games/{id}/push-after-exit` route and `TrackedGameDto` field, mirroring the pull-side
plumbing exactly (`AgentConfig.SaveGamePushAfterExit` → `MutateGameUnderLock`). Enforced the same
way, in `SyncEngine.OnGameExitAsync`:

```
try
{
    if (game.PushAfterExitEnabled ?? true)
        await PushAsync(game, force: false, settle: true, ct: ct);
}
finally { /* lease release, unchanged — must run whether or not a push happened */ }
```

The lease release in `finally` is unconditional regardless of the flag — skipping the push must
never leave the game checked out to this machine.

**Why per-game, server-shared, not a plugin-local setting:** same reasoning `PullBeforeLaunchEnabled`
already established — a player who disables push-after-exit for one game (e.g. a permadeath run
where they don't want a bad ending auto-uploaded, or a game whose saves are known-flaky) wants that
respected from every machine that syncs it, and wants it to survive a plugin reinstall. Neither
toggle should live in the Playnite plugin's own settings file.

**Because both toggles now live in `SyncEngine` rather than client-side, they apply everywhere for
free** — the Linux wrapper, Decky (once it's updated to stop doing its own client-side skip, which
is a small fix worth doing there too, out of scope for this repo), and the new Playnite plugin all
get consistent behavior from one place. The Playnite plugin's own job shrinks to: surface the two
per-game toggles somewhere in its UI (a checkbox per game, or reuse the existing local-API routes
from a settings page listing tracked games) and call `POST .../pull-before-launch` /
`POST .../push-after-exit` when the player changes them — it never needs to know the *default*
heuristic itself, only that it can override it.

**Verify:** extend `run-agent-tests`/a new block — pull skipped but conflict still blocks for a
`HasSteamCloud: true` game with no override; an explicit `true` override forces the pull anyway;
push skipped for `PushAfterExitEnabled: false` but the lease still releases; both default to today's
existing behavior when neither field is ever set (regression safety for every game tracked before
this field existed).

---

## "Link to SaveLocker" — enrolling a merely-installed game from the plugin (researched 2026-09-14)

**Yes, this is buildable, and it reuses almost the entire existing enrollment pipeline** — traced
`Enroller.cs`, `GameScanner.cs`, and `AgentApiServer.cs`'s candidate routes to confirm exactly how
much. The one new piece is small; everything after it (folder override, the actual enroll call) is
unmodified, pre-existing machinery.

### What already exists, and why it's reusable

`GameScanner.ScanInstalledSteamGamesAsync`'s per-game resolution is already a clean, source-agnostic
primitive, decoupled from *how* a game was discovered:

```
var save = await SuggestSaveDirAsync(trimmed, ct, installPath, libraryRoot);
// → _detection.ResolveWindowsAsync(name, installDir, storeRoot, ct) — first existing dir the
//   Ludusavi manifest knows for this name, given <base>/<root> hints.
```

This takes exactly the three things Playnite already hands the plugin for any installed game —
**name, install directory, and (for a Steam entry) which library root it's under** — and needs
nothing about *how* the game was found. A `ScanCandidate` built from Playnite's own data is
structurally identical to one `ScanInstalledSteamGamesAsync` already builds; it only needs a new
`ScanSource.Playnite` to say where it came from.

Downstream of that, `AgentApiServer.cs` already has everywhere this needs to go:

- `_candidateCache` (a plain `IReadOnlyList<ScanCandidate>?` field) is what `/api/enroll` reads by
  index — it does not care whether the list came from a full `/api/candidates/rescan` sweep or
  somewhere else.
- `POST /api/candidates/{id}/folder-pick` and `POST /api/candidates/{id}/folder` already let a
  candidate's `SuggestedSaveDir` be corrected — by a native folder dialog or a typed path — **before**
  enrollment, going through the same `SavePathGuard.Check` safety net either way.
- `POST /api/enroll` (`Enroller.EnrollAsync`) is completely unmodified by any of this — it only ever
  sees a `ScanCandidate` with a resolved, guard-checked `SuggestedSaveDir`.

### The one new piece

A narrow local-API route — e.g. `POST /api/candidates/lookup` — taking
`{ name, installDir, steamAppId?, store? }`: runs *only* the per-game resolution
(`Detection.ResolveWindowsAsync` for the save dir, `HasSteamCloudAsync`/`CanonicalNameAsync` for the
rest — the same three calls `ScanInstalledSteamGamesAsync` already makes per game), builds one
`ScanCandidate`, appends or replaces it in `_candidateCache`, and returns its id plus whether a save
dir resolved. **Deliberately not a full `/api/candidates/rescan`** — that re-enumerates every Steam
shortcut, every installed Steam game, and every common save root, which is slow and would surface
hundreds of unrelated candidates for what should be a single, targeted "is this one game known?"
lookup.

### Manifest search — the missing middle tier, added 2026-09-14 (asked directly: can a manual
*name* search find the right manifest entry when automatic matching finds nothing?)

**Yes, and it needs one new small route — nothing like this exists today.** `ManifestLoader` already
holds every one of the manifest's ~53,000 names in memory as `GameNames` (a plain
`IEnumerable<string>`), because the whole manifest is loaded once and cached — a substring search
over it is trivial and touches no disk or network beyond what's already resident:

```
GET /api/manifest/search?q=civilization
→ every manifest name containing "civilization" (case-insensitive), e.g.
  "Sid Meier's Civilization VII", "Sid Meier's Civilization VI", "Sid Meier's Civilization V"…
```

This is exactly the missing piece for the "Civ VII" case: the automatic lookup fails silently
(previous point) because the two names share no normalized tokens, but a human recognizes
"Civilization" instantly. Once the player picks the right entry from the search results, the flow
re-runs resolution **using the chosen manifest name in place of Playnite's own title** —
`Detection.ResolveWindowsAsync(chosenName, installDir, storeRoot, ct)`, then the same confirm-the-
resolved-path step as the automatic case. One detail worth calling out because it may surprise
someone expecting Playnite's own title to carry through: **the game is enrolled server-side under
the manifest's canonical spelling, not Playnite's** — `Enroller.EnrollAsync` already always prefers
`ManifestKey` over the discovered name (`c.ManifestKey ?? c.Name`) specifically so two machines
spelling a game differently still converge on one server-side game. Playnite's own library entry
keeps showing "Civ VII" regardless — that's Playnite's own metadata and untouched — only
SaveLocker's own tracked-game name changes.

**One risk this tier introduces that automatic matching doesn't have: picking the wrong
similarly-named entry** (Civilization VI instead of VII, a remaster vs. the original). Mitigated the
same way the automatic path already is — never enroll straight off a name pick; always show the
actual resolved path in a confirm step first ("Track 'Civ VII' as **Sid Meier's Civilization VII** —
save found at {path}?"), so a wrong pick is visible before anything is created, not after.

### The popup itself — searches first, enrolls only as the true last resort

Matches what you asked for directly: click "Link to SaveLocker" on an unmatched game, and it tries,
in order:

1. **Search already-tracked games first** — the exact same automatic chain designed above (Steam
   AppID → InstallDir → name/`Alias`), just run on demand instead of silently at launch time. If one
   confident match turns up, one click writes `Alias` (existing `/api/games/{id}/alias` route) and
   the popup closes — no enrollment involved, because the game was already tracked.
2. **If nothing matches: call the new lookup route** with Playnite's own title. If it resolves a
   save directory, show a single confirm — *"Track '{name}' — save found at {path}?"* — and Enroll
   calls the existing `/api/enroll` unmodified.
3. **If the automatic lookup resolves nothing: offer the manifest search above** — a search box,
   defaulting to Playnite's title as the starting query, showing manifest name matches to pick from.
   Picking one re-runs resolution under that name and shows the same confirm step.
4. **If even a chosen manifest entry resolves no existing save folder (or the game isn't in the
   manifest at all): fall back to fully manual** — the existing Add Games folder browse/type-a-path
   flow (`folder-pick`/`folder` routes, `SavePathGuard`), reusing the existing `agent-ui` Add Games
   view in a small WebView2 popup rather than rebuilding it natively in WPF.
5. **A manual "pick an existing tracked game instead" option stays available at every step** — for
   the case where the player *knows* it's the same game as something already tracked under a very
   different name, which no automatic heuristic could have found.

### Problems this genuinely runs into

1. **"Installed but never launched" often has no save folder to find yet — and this is not fixable
   by better matching.** `ResolveWindowsAsync`/`SuggestSaveDirAsync` picks the first *existing*
   directory a manifest path template names — if the game has never run, most save-path templates
   (an `<AppData>\SaveGames\` folder the game itself creates on first save) genuinely do not exist on
   disk yet. This is not a Playnite-specific gap; it is true for every enrollment path in this
   codebase today (CLI, agent-ui, Decky) — `Enroller.EnrollAsync` explicitly **refuses** to enroll a
   candidate with no resolved save dir at all (`SuggestedSaveDir` null/empty is skipped, not
   enrolled with a placeholder). So for a genuinely never-launched game, "install-only enrollment"
   will frequently mean: SaveLocker confirms the *title* is recognized, but cannot yet enroll it —
   the honest popup message is something like *"SaveLocker knows this game but hasn't found a save
   folder — this usually means it hasn't been run yet. Launch it once, then try Link to SaveLocker
   again, or browse manually if you already know the folder."* Framing this honestly matters more
   than trying to engineer around it — there is nothing to point at yet in the common case.
2. **The game may not be in the Ludusavi manifest at all** (a very new release, an obscure indie
   title, a manually-added `.exe` with no store metadata). Nothing can auto-resolve these regardless
   of source; manual path entry is required, and the player has to actually know or go find the save
   folder. A hard data-coverage limit, not an implementation gap.
3. **Title-matching accuracy against the manifest — worth being precise about what the existing
   normalization actually covers, because it's narrower than it sounds.** Read `ManifestLoader.cs`
   directly: `NormalizeName` lowercases and collapses punctuation to spaces, then `TryGetGame`/
   `CanonicalName` do an **exact lookup** against that normalized form — nothing fuzzy, no
   similarity scoring. It bridges *"DRAGON QUEST III HD 2D Remake"* vs *"Dragon Quest III HD-2D
   Remake"* (same words, different case/punctuation) perfectly. It does **not** bridge *"Civ VII"*
   vs the manifest's *"Sid Meier's Civilization VII"* — those share no normalized tokens at all
   ("civ" and "civilization" are different words to this algorithm), so the lookup returns nothing,
   silently, exactly as if the game weren't in the manifest at all. Playnite is especially prone to
   this because its titles are community-maintained/user-editable metadata, not the storefront's own
   string — abbreviations, dropped subtitles, and franchise nicknames are common. **This is inherited
   from the existing scanner, not introduced here** — a Steam shortcut named "Civ VII" by its owner
   would fail the exact same way today — but Playnite very likely hits it *more often* than Steam
   shortcuts do, simply because more of its entries carry non-canonical titles. There is also a
   deliberate, related safety choice worth knowing: if two *different* manifest entries normalize to
   the *same* key, the lookup is built to resolve to **neither** rather than guess — silence over a
   wrong answer, by design (`BuildLooseIndex`'s own comment).
4. **A resolved path can still be refused by `SavePathGuard`** (too broad — an install root, a
   profile folder) — again the existing, shared safety net, but the popup needs to surface *that*
   specific failure reason too, not just silently drop back to "nothing found."
5. **Cross-machine duplicate-game risk is inherited, not introduced.** If the same title is already
   tracked on another machine under a spelling different enough that server-side name-matching in
   `CreateGameAsync` doesn't recognize it as the same game, enrolling "fresh" from Playnite could, in
   principle, create a second `Game` row for one real game — exactly the **already-known, already-
   documented gap** ("two Games already diverged under different spellings," `Backlog.md` → *One
   game, several real sources*). This feature does not make that risk worse than the CLI, agent-ui,
   or Decky enrollment paths already carry today, and fixing it is out of scope here — just worth
   knowing it isn't uniquely solved by this feature either.

### Recommendation

Build it — the reuse is high enough (one new, narrow route; zero changes to `Enroller`,
`SavePathGuard`, or the folder-override routes) that the main cost is the popup UI, not new agent
logic. Fold it into **Phase 3** alongside the last-resort `Alias`-linking flow they share a UI
surface with, rather than a separate phase — a player hitting "Link to SaveLocker" shouldn't have to
know in advance whether they want to search or enroll; one popup offering both, in the priority order
above, is the right shape.

---

## Game page: status chip + push/pull/sync buttons (researched 2026-09-14)

Confirmed against the Playnite SDK's own extension points — this does not need a custom Playnite
theme or any cooperation from the user's chosen theme, both of the following are plugin-side and
work regardless of theme:

- **`GenericPlugin.GetGameViewControl(GetGameViewControlArgs args)`** — lets a plugin inject its own
  WPF control directly into the built-in game details view. This is the right home for a compact
  status chip ("In sync" / "Conflict" / "Not linked" / "Agent offline") plus three buttons — **Push
  now**, **Pull now**, **Resolve conflict** (only enabled/visible when relevant) — scoped to
  whichever game's details the player currently has open. Refreshes from the same local-API
  `sync-status`/`state` data the agent-ui's own Overview and Conflicts views already read, so the
  three surfaces (dashboard, agent-ui, Playnite) never disagree about what "in sync" means.
- **`GenericPlugin.GetGameMenuItems(GetGameMenuItemsArgs args)`** — the right-click context menu on a
  game (in the grid or details view). A lighter-weight companion to the chip: "SaveLocker → Push
  now" / "Pull now" / "Resolve conflict…" / "Link to SaveLocker game…" (the last-resort picker from
  above), reachable without opening the details view at all — useful for bulk-style workflows
  (right-click several games) the chip panel can't offer.
- **Tags, as a secondary, lower-fidelity option — not the primary mechanism.** Playnite's own
  colored Tags are visible in the library grid/list without opening a game's details at all
  (`Database.Tags` + `Game.TagIds`), which the `GetGameViewControl` chip cannot be, since it only
  renders once a game is selected/open. Worth adding later as a purely-visual "⚠ SaveLocker
  conflict" tag for at-a-glance grid scanning, but the chip + buttons in the details view remain the
  primary, actionable surface — tags carry no click target of their own.
- Both `GetGameViewControl` and `GetGameMenuItems` run in Playnite's own UI thread/process, so no
  polling loop of their own is needed beyond a light refresh (e.g. on the details view being shown,
  and after any action completes) — consistent with the "read local state cheaply, sync data lives
  server-side" pattern the agent-ui already follows.

---

## Post-exit is a genuine boundary here too — not just pre-launch

The plan so far (2026-09-10 draft) left post-exit entirely to the tray's existing reactive
`ProcessWatcher`, on the reasoning that push-after-exit is inherently safe regardless of *how* the
exit is noticed. True, but revisited given the buttons/feedback work above: Playnite's own
`OnGameStopped` hook is a second, independent, *faster and more certain* exit signal than polling for
a process to disappear, and — more importantly — it is what lets the plugin show the "your save was
uploaded" feedback in the same place and the same moment the player is actually looking (back in
Playnite, right after quitting the game), rather than relying on a Windows tray toast that focus
assist can easily swallow. **No local-API route for this exists yet** — only in-process callers do
today (`TrayApp.cs:539`, `ProtonRun.cs:152`, both calling `SyncEngine.OnGameExitAsync` directly).

Add `POST /api/games/{id}/post-exit-sync`, mirroring `pre-launch-sync` exactly: wraps
`_engine.OnGameExitAsync(game, ct)` (which already respects the `PushAfterExitEnabled` guard above),
same single-flight `_syncGate`, same "game removed mid-flight" re-check, same fail-open-on-transport-
error contract (the only difference from the pre-launch route: there is no `Blocked` decision to fail
*out of* — a post-exit push either succeeds, is skipped by the toggle, or fails softly and gets
logged, never something that should surface as an error the player must act on). The tray's own
reactive exit-push stays exactly as it is, as a safety net for every game not launched through
Playnite (or on the rare chance the plugin's own hook doesn't fire) — calling `OnGameExitAsync` twice
in close succession is harmless (`PushCoreAsync` bails on a hash comparison alone when nothing
changed since the last push, no network call).

---

## Size estimate (2026-09-14)

Sized the way `implementation-grouping.md` already sizes the rest of this codebase's work — against
this repo's own session history, not calendar days — because that is the only credible yardstick
available. Scope has grown considerably over this planning conversation: what started as "a pre-
launch gate plugin" is now a pre-launch gate **plus** post-exit hooks, a three-tier automatic
matching chain, a five-tier link/enroll popup with manifest search, a status-chip-and-buttons panel,
per-game sync toggles, and an agent-driven self-updater. Worth being honest that this is now
comparable in size to a large fraction of the entire 14-phase conflict-resolution-ui effort, not a
small side task.

**Agent-side work (this repo — C#/net10.0, fast iteration, mostly mirrors proven patterns):**

| Piece | Complexity | Est. sessions |
|---|---|---|
| `TrayApp.cs` launch-gate rewiring (Phase 1) | Low | 0.5 |
| Populate `SteamAppId` on Windows (`GameScanner` + backfill) | Medium — touches enrollment/reconcile | 1 |
| `PullBeforeLaunchEnabled` moved server-side into `PrepareLaunchAsync` | Low-medium — small diff, but on a conflict-safety-critical path | 0.5–1 |
| `PushAfterExitEnabled` field + route + `OnGameExitAsync` gate | Low — mirrors the field above closely | 0.5–1 |
| `POST /api/games/{id}/post-exit-sync` | Low — mirrors `pre-launch-sync` closely | 0.5 |
| `POST /api/candidates/lookup` (single-candidate resolve) | Medium — new `_candidateCache` merge semantics | 1 |
| `GET /api/manifest/search` | Low | 0.5 |
| `AgentPlatform.PlaynitePlugin` slot + Agent Updates row | Low-medium — mirrors `decky-plugin`'s own addition | 1 |
| Windows plugin self-updater (`DeckyPlugin.cs` equivalent) | Medium — new class, but closely modeled, actually *simpler* than Decky's (no root/chown constraints) | 1–2 |
| `PlayniteLibrary.cs` direct LiteDB reader + `agent-ui` chip (Phase 18, added 2026-09-16) | Medium-high — first use of LiteDB here, and the shared-connection/version-match risk needs confirming on real hardware before the mapping logic is worth writing | 1.5–2 |
| `PlaynitePluginCard.tsx` + install/status routes + Phase 17 `.pext` addendum (Phase 19, added 2026-09-16) | Medium — mostly assembly of already-shipped pieces (Phase 7's `InstallAsync`, Phase 18's detection probe), but the first real hardware exercise of `InstallAsync` against a running Playnite is genuine unverified risk | 1–1.5 |
| **Subtotal** | | **~9.5–12.5 sessions** |

**New repo / plugin itself (unfamiliar toolchain here: `.NET Framework 4.6.2` + Playnite SDK + WPF,
first use of any of it in this codebase; verification is hardware-only, no CI):**

| Piece | Complexity | Est. sessions |
|---|---|---|
| Scaffold + "hello world" load (Phase 1) | Low logic, but **new-toolchain friction risk** — first NuGet/Toolbox/`.pext` packaging pass in this environment | 1–2 |
| Local API client + settings page (Phase 2) | Low-medium | 1 |
| Core pre-launch gate: `OnGameStarting`/`Stopped`, `ActivateGlobalProgress`, conflict dialog | Medium-high — first WebView2/native-dialog work | 2 |
| Automatic matching chain (client-side consumption) | Medium | 1 |
| "Link to SaveLocker" popup — 5-tier flow, own state machine | Medium-high — the largest single UI piece here | 2 |
| Status chip + buttons (`GetGameViewControl`) + context menu (`GetGameMenuItems`) | Medium | 1–2 |
| Plugin-side self-update consumption (checks + restart prompt) | Low-medium | 0.5–1 |
| Test infra: portable-Playnite `testenv` target, Windows `seed-test-conflict`, a stub-server test project | Medium — genuinely new infra, nothing to extend | 2 |
| Release CI workflow (Phase 17, added 2026-09-15) | Low-medium — small in scope, but no existing workflow in this codebase to closely copy (`SaveLocker-Decky` has none either) | 1 |
| **Subtotal** | | **~12–16 sessions** |

**Total, full scope as now designed: roughly 22–29 sessions** (the two subtotals above, now
including Phases 18 and 19). At this project's own recent cadence
(often close to one substantial session per day), that is realistically **3–4.5 weeks** of active
work — not a quick add-on.

**What actually drives the uncertainty**, beyond raw size: zero prior experience with the Playnite
SDK/WPF/WebView2 in this codebase (this project's own history — the Deck `testenv` rollout, the
MoonDeck save-detection investigation — repeatedly shows that "it compiles" and "it works on real
hardware" are different claims, discovered only on the first real pass); everything Playnite-side is
manual/hardware-verified, no automated suite the way `run-agent-tests` covers the agent; and the
add-on-database PR review timeline is outside anyone's control here.

**Recommended cut for a first, genuinely shippable slice — matches this codebase's own "don't build
for a case not yet confirmed to exist" discipline:**

1. Phases 1–3 — launch-gate rewiring, `SteamAppId` population, `PullBeforeLaunchEnabled` move
   (~2–2.5 sessions; Group 1 in `implementation-grouping.md`)
2. Phases 8–11 — scaffold + settings + core pre-launch gate + automatic matching only — **no
   enrollment popup, no chip/buttons, no self-updater yet** (~5–6 sessions; Group 3)

**≈ 7–8.5 sessions for a working pre-launch conflict gate with automatic matching** — the actual
core value proposition (Windows finally gets a safe pre-launch pull/block, the way Linux/Decky
already have). Enrollment, the status chip/buttons, and the self-updater are real value on top, but
each is independently useful and independently deferrable — build and verify the core on real
hardware first, then decide which of the rest earns a follow-up phase, same as Decky's own Phase
4/5 were scoped after Phase 1–3 proved out.

## Phases

Own numbering, independent of `conflict-resolution-ui/plan.md`. Two tracks — **agent-side** (this
repo, `SaveLocker.sln`, C#/net10.0, fast iteration, `run-agent-tests`-verifiable) and **plugin-side**
(the new `SaveLocker-Playnite` repo, `.NET Framework 4.6.2` + Playnite SDK, hardware-verified only).
Each phase names what it depends on; anything not listed as a dependency can be built in either
order. See `implementation-grouping.md` for which phases share a session.

**Agent-side track (this repo):**

- **Phase 1 — Windows launch-gate rewiring.** `TrayApp.cs`'s `prepareLaunch` delegate swaps from
  `OnGameLaunchAsync(preLaunch: false)` to `_engine.PrepareLaunchAsync(game, ct)`, mirroring
  `Daemon.cs`'s existing Linux wiring. No dependencies. Verify: `run-agent-tests` plus a manual
  before/after call to `/api/games/{id}/pre-launch-sync` proving it now pulls and can return
  `Blocked` on Windows. *(Full rationale: "The one thing that must change…" section above.)*
- **Phase 2 — Populate `SteamAppId` on Windows.** `GameScanner` resolves and records it for
  Steam-sourced tracked games (mirroring the Linux scanner), plus a startup backfill for games
  already tracked before this field existed. No dependencies, but strengthens Phase 11's matching
  chain the most of anything in this plan. Verify: `run-agent-tests`/`run-winagent-tests` extended;
  a real Steam-installed tracked game shows a non-null `SteamAppId` via `/api/games` after a rescan.
- **Phase 3 — `PullBeforeLaunchEnabled` moves server-side into `SyncEngine.PrepareLaunchAsync`.**
  Currently read only by Decky's own frontend, client-side; move the off-for-Steam-Cloud default
  into the shared gate itself so every caller (Linux, Decky, and Windows once Phase 1 lands) gets it
  consistently. Depends on nothing structurally, but has no visible effect on Windows until Phase 1
  ships. *(Full design: "Sync trigger controls" section above.)* Verify: a scratch call to
  `pre-launch-sync` for a `HasSteamCloud: true` game shows no pull, but a real open conflict still
  blocks.
- **Phase 4 — `PushAfterExitEnabled`.** New field, `POST /api/games/{id}/push-after-exit` route,
  `OnGameExitAsync` gate (lease still releases regardless). No dependencies. Verify: extend
  `run-agent-tests` — push skipped when disabled, lease still releases; unset defaults to today's
  existing unconditional-push behavior.
- **Phase 5 — Three small new local-API routes.** `POST /api/games/{id}/post-exit-sync` (wraps
  `OnGameExitAsync`, mirrors `pre-launch-sync`; depends on Phase 4 existing to have a toggle worth
  respecting), `POST /api/candidates/lookup` (single-candidate resolve into `_candidateCache`, no
  full rescan), `GET /api/manifest/search` (in-memory substring search over `ManifestLoader
  .GameNames`). Independent of each other otherwise. Verify: `run-local-api-tests` extended for all
  three; the lookup route resolves a real installed game's save dir; the search route finds
  "Sid Meier's Civilization VII" for a query of "civilization".
- **Phase 6 — `AgentPlatform.PlaynitePlugin` slot.** Fourth slot beside `win-x64`/`linux-x64`/
  `decky-plugin` in `Contracts.cs`; a fourth row appears in Config → Agent Updates for free. No
  dependencies. Verify: upload a dummy package through the console, confirm the row behaves like the
  other three (hash verification, GitHub fetch).
- **Phase 7 — Windows plugin self-updater.** `PlaynitePlugin.cs` (agent-side, `DeckyPlugin.cs`
  shape: `PluginUpdateState`, plan-before-write, digest verification) — genuinely simpler than
  Decky's, since `%AppData%\Playnite\Extensions\<id>\` is entirely user-owned. Depends on Phase 6.
  Verify: `run-winagent-tests` extended with a fake Extensions directory, the same trick
  `run-linux-tests` already uses for Decky.

**Plugin-side track (new repo):**

- **Phase 8 — Scaffold + "hello world" load.** `extension.yaml`, an SDK-style `net462` csproj
  referencing `Playnite.SDK`, a `GenericPlugin` that does nothing but load. Pack with `Toolbox` into
  a `.pext`, install into the real Playnite on this box, confirm it loads with no errors. No
  dependencies — the highest toolchain-risk phase in this whole plan (first use of this SDK/`.pext`
  packaging here), budget accordingly.
- **Phase 9 — Local API client + settings page.** Read the Windows token file, confirm the agent is
  reachable and this machine is registered, surface a one-line status — mirrors
  `DeckyPluginCard.tsx`'s three states. Also the settings UI for a configurable agent URL (testenv
  support). Depends on Phase 8.
- **Phase 10 — Core pre-launch/post-exit gate.** `OnGameStarting` → `pre-launch-sync` inside
  `ActivateGlobalProgress`; `OnGameStopped` → `post-exit-sync`; the resolve dialog on `Blocked`
  (native WPF or `WebView2` pointed at the agent-ui conflicts page). Depends on Phase 9, and needs
  agent-side Phases 1, 3, 4, 5 to actually be *worth* calling on Windows — buildable earlier, but
  verification against a real conflict needs those shipped first. *(Full walkthrough: "Step by
  step" section above.)*
- **Phase 11 — Automatic matching chain.** Steam AppID → InstallDir → name/`Alias`, run against the
  local API's tracked-games list. Depends on Phase 9; full strength depends on agent-side Phase 2.
  *(Full design: "Automatic game matching" section above.)*
- **Phase 12 — "Link to SaveLocker" popup.** The five-tier flow: search tracked games → automatic
  manifest lookup → manual manifest search → manual folder browse → pick an existing tracked game.
  Depends on Phase 11 (tier 1) and agent-side Phase 5 (tiers 2–3's routes). The largest single UI
  piece in this plan. *(Full design: "Link to SaveLocker" and "Manifest search" sections above.)*
- **Phase 13 — Status chip + action buttons.** `GetGameViewControl` (chip + Push/Pull/Resolve
  buttons on the game details page) and `GetGameMenuItems` (the same actions in the right-click
  menu). Depends on Phase 9; most useful once Phases 11–12 exist so an unmatched game's chip has
  something to do. *(Full design: "Game page" section above.)*
- **Phase 14 — Plugin-side self-update consumption.** Checks the installed extension version,
  writes files when newer, surfaces a "restart Playnite to finish updating" notice (no hot-reload for
  compiled plugins — confirmed). Depends on agent-side Phases 6–7 existing to have something to poll.
- **Phase 15 — Test infrastructure.** A portable-Playnite `testenv.ps1` target, a
  `seed-test-conflict.ps1` (Windows analogue of the existing bash script), and a small stub-server
  test project for the plugin's matching/HTTP logic. Depends on Phase 8 (needs the plugin project to
  exist to build against) and benefits from Phase 10 existing to have something worth testing
  end-to-end. *(Full design: "Testing plan" section above.)*
- **Phase 16 — Official add-on database submission (optional, last).** `extension.yaml` manifest +
  a PR to `JosefNemec/PlayniteAddonDatabase`. Depends on Phase 10 at minimum actually working: submit
  something that functions, not a scaffold. Review timeline is outside this project's control — see
  "Where this genuinely differs from Decky" above. Phase 7/14's self-update path is what covers users
  in the meantime, so this is not blocking for anything else.
- **Phase 17 — Release CI workflow for `SaveLocker-Playnite`.** Found 2026-09-15, answering a direct
  question about how the plugin handles releases: today there is **no `.github/workflows` at all** in
  that repo — a release means manually running `dotnet build`, hand-zipping per `Build and Run.md`,
  and uploading somewhere by hand. That's the one piece Phase 6/7's self-updater (which polls GitHub
  releases at `Marwanello/SaveLocker-Playnite`) and Phase 16's add-on-database listing (whose
  `InstallerManifestUrl` also has to point at something real) both silently assume exists. Add a
  GitHub Actions workflow, triggered on a version tag, that builds the plugin (`dotnet build -c
  Release`), zips `extension.yaml` + the DLL the same way `Install-ToPortable.ps1`/`Build and Run.md`
  already do by hand, and publishes it as a GitHub Release with a `SHA256SUMS.txt` — matching the
  hash-verification contract `UpdateChecker.DownloadInstallerAsync` already expects from every other
  platform slot. Depends on Phase 8 (needs the plugin project to exist to build against); most useful
  once Phase 10 exists (something worth actually releasing), but not gated on it. **No existing
  precedent to closely mirror** — checked, and `SaveLocker-Decky` doesn't have an automated release
  workflow either (its own repo "has no releases" per `progress.md`'s 2026-08-26 entry) — so this is
  genuinely new infrastructure for this codebase's Windows/Playnite side, not a close copy the way
  most of this plan's phases are. Verify: push a test tag, confirm the workflow produces a
  correctly-shaped `.zip` + checksum on the release, and that the agent's self-updater (Phase 7) can
  actually fetch and verify it end to end — the first real exercise of that code path.
- **Phase 18 — Agent-side Playnite library reader + `agent-ui` tab, no plugin required.** Added
  2026-09-16, asked directly: does the agent already know which games Playnite has installed, the way
  it knows Steam's and (on Linux) Heroic's, and can it show them as a filter chip in Add Games for
  enrollment straight from the agent — no. Every phase above discovers Playnite games through the
  **plugin**, running live inside Playnite and calling the agent's local API — Phase 11's automatic
  matching chain and Phase 12's "Link to SaveLocker" popup both depend on that plugin being installed
  and Playnite being open; `ScanSource.Playnite` today only ever means a single targeted
  `POST /api/candidates/lookup` the plugin asked for, never a broad sweep. This phase is the
  Windows-side structural equivalent of `HeroicLibrary.cs` — a new `src/Agent/PlayniteLibrary.cs`
  that `GameScanner.ScanAsync` calls as a **fourth broad-sweep source**, alongside Steam
  shortcuts/installed/save-roots, so a Playnite-only game shows
  up in Add Games with zero dependency on the plugin ever being installed — matching the Linux agent's
  own relationship to Heroic (there is no companion "Heroic plugin"; the agent just reads Heroic's own
  files).
  - **What it reads.** Playnite stores its library in an embedded LiteDB database, conventionally
    `%AppData%\Playnite\library\games.db` (portable installs keep it beside the executable instead —
    probe both, skip cleanly if neither exists, the same "steamPath is null → skip entirely" shape
    `GameScanner.ScanAsync` already applies to the whole Steam branch). Read via the `LiteDB` NuGet
    package, projecting only `Id`, `Name`, `InstallDirectory`, `IsInstalled`, `GameId` (the owning
    store's own per-game id — carries the Steam AppID string when the owning plugin is Steam), and
    `PluginId` (a fixed GUID naming which library plugin owns the entry — Steam/GOG/Epic/Amazon each
    have a stable, well-known id). Skip anything with `IsInstalled == false` or no
    `InstallDirectory` — an uninstalled entry has nothing on disk to resolve a save folder against,
    the same filter `HeroicLibrary.Read`'s own `Where(g => !string.IsNullOrWhiteSpace(g.InstallPath))`
    already applies.
  - **The one real risk, and it must be confirmed on real hardware before anything else in this
    phase**: Playnite holds `games.db` open while it runs, and LiteDB's on-disk format has had
    breaking changes across its own major versions — a NuGet package version that doesn't match what
    the installed Playnite currently bundles is a silent, hard-to-diagnose read failure, not a clean
    error. Two things to verify first, in this order, before writing the mapping logic: (1) what
    LiteDB version the real Playnite install on this box actually bundles (its own installed files,
    or release notes, are the authority — do not assume); (2) that opening the file with a
    shared/read-only LiteDB connection string actually succeeds while Playnite is running and holding
    it open, rather than throwing a lock exception. This is this phase's equivalent of Phase 8's
    "highest toolchain-risk" flag — budget accordingly, and if the shared-connection read cannot be
    made reliable, the honest fallback is skipping the source only while Playnite is detected running
    (`GameActivity`-style process check) rather than shipping a scan that intermittently loses the
    Playnite chip.
  - **Mapping to `ScanCandidate`.** `SuggestedSaveDir` resolves the same way
    `ScanInstalledSteamGamesAsync` already does — `Detection.ResolveWindowsAsync(name, installDir,
    storeRoot, ct)` — passing `storeRoot: null` normally, but `GameScanner.FindSteamPath()`'s existing
    library-root list when the owning `PluginId` is Steam's, so a Steam-owned Playnite entry resolves
    exactly as strongly as a native Steam-installed candidate already does since Phase 2. `SteamAppId`
    is set from `GameId` under the same condition. `Store` comes from a small fixed `PluginId →
    GameStore` map (Steam/Epic/Gog/Amazon covered; everything else — itch.io, a manually-added
    executable, a plugin this map doesn't know — falls to `GameStore.Unknown`, mirroring Heroic's own
    fallback exactly). Tag `Source: ScanSource.Playnite` — reusing, not duplicating, the existing enum
    value; its doc comment widens to cover both "the plugin asked about one game" and "the agent swept
    the whole library," since both are conceptually "this is how Playnite told us," exactly parallel
    to how one `Heroic` value already covers four different runners with `GameStore` as the finer
    axis.
  - **Fault isolation and dedup are free.** Wrap the whole source in `ScanAsync`'s existing
    `SafeSourceAsync("Playnite library", …)` — a missing file, a lock, or a schema Playnite has since
    changed degrades to "no Playnite candidates this scan," never takes down Steam/shortcut/save-root
    discovery alongside it, the same non-negotiable `ScanAsync`'s own doc comment already states for
    every source (WA-11). The existing `GroupBy(ManifestLoader.NormalizeName)` + `MergeDuplicates`
    dedup already handles a game visible through more than one source (a Steam game also present in
    Playnite) with no new merge logic.
  - **`agent-ui`.** One new `FILTERS` entry in `AddGamesView.tsx`, copied from the Heroic row —
    `{ id: 'playnite', label: 'Playnite', hint: 'Games in your Playnite library', match: c => c.source
    === 'Playnite' }`. The chip auto-hides on a machine with none (existing `chips` filter already
    does this), the per-row badge already renders `c.source` generically, and the `stores` memo widens
    from `filter === 'heroic'` to also match `'playnite'` so Steam/Epic/GOG sub-filtering — already
    built for Heroic — works for a Playnite-sourced list too, for free.
  - **Relationship to Phases 8–17, stated explicitly so the two don't get confused later:** this does
    not replace the plugin's own matching (Phase 11) or "Link to SaveLocker" popup (Phase 12) — those
    remain the more accurate path while Playnite is actually running with the plugin installed,
    since they read Playnite's *official* SDK object model, not a reverse-engineered on-disk format.
    Phase 18 covers what the plugin architecture structurally cannot: a machine where the plugin isn't
    installed at all, and browsing/enrolling Playnite games from the agent-ui the same uniform way
    Steam and Heroic already work.
  - **Dependencies:** none structurally — touches only this repo (`GameScanner.cs`,
    `ScanCandidate.cs`, `AddGamesView.tsx`), not the `SaveLocker-Playnite` repo, and doesn't depend on
    any of Phases 1–17. Strengthened by Phase 2 (`SteamAppId` population, already shipped): a
    Steam-owned Playnite entry with a resolved AppID lines up with Phase 11's plugin-side matching
    chain too, so the two independent discovery paths agree on the same game rather than only one of
    them recognizing it.
  - **Verify:** unit-test the LiteDB parsing/mapping against a small fixture database built in the
    test itself (an actual advantage over `HeroicLibrary`'s manual-only verification — LiteDB is cheap
    to construct as a fixture, unlike Heroic's real Electron JSON files). Then, on this real Windows
    box with a real Playnite install already present: confirm a rescan surfaces genuine Playnite
    library games tagged `Playnite`, resolves save folders for the ones the manifest knows, enrolls
    correctly end to end, de-dupes correctly against anything also visible via Steam, and — the
    specific failure mode this phase is riskiest for — confirm Playnite itself keeps running normally
    with `games.db` open while a scan reads it, with no lock contention in either direction.

- **Phase 19 — `agent-ui` "Playnite plugin" card: suggest by default, one-click direct install as an
  explicit opt-in.** Added 2026-09-16, asked directly as a follow-up to Phase 18: today nothing in
  `agent-ui` tells the user the plugin exists at all — `Playnite` appears nowhere in `agent-ui/src`
  except the generated `api-types.ts`. Phases 6/7/14 already built the server-side hosting slot
  (`playnite-plugin` row, `AgentUpdatesCard.tsx`) and the agent's *update*-checking half
  (`GET /api/playnite-plugin`), but there is no surface that offers the plugin to a user who does not
  already have it, and nothing that installs it the first time. Resolved by asking directly (see the
  question this phase was scoped from): build both halves, at once — a suggest-only card by default,
  plus an explicit, separately-clicked "Install automatically" affordance for whoever wants it,
  rather than picking one philosophy for everyone.
  - **New `PlaynitePluginCard.tsx`**, added to `OverviewView.tsx` beside `DeckyPluginCard` — same
    file, same three-state shape (`no Playnite detected` / `Playnite but no plugin` /
    `plugin installed`), same collapsed-by-default treatment. Unlike `DeckyPluginCard` (which still
    renders when Decky itself is absent, since Decky is a near-universal Deck accelerator worth
    pointing a Deck user at), this card renders **nothing at all** when Playnite isn't detected on
    this machine — Playnite is a much more optional, minority choice on a Windows desktop than Decky
    is on a Deck, and a card explaining a launcher the user doesn't have is clutter, not a nudge.
    Detecting "is Playnite here" reuses Phase 18's own data-folder probe (`%AppData%\Playnite\` /
    portable-beside-exe) rather than duplicating it — the same check `PlayniteLibrary.cs` already has
    to make to find `games.db` answers this too.
  - **New local-API route, `GET /api/playnite-plugin/status`**, mirroring `/api/decky`'s shape exactly
    (`applicable`, `pluginInstalled`, `pluginVersion`, `latestVersion`, `installUrl`) — local reads
    plus one cheap check against the already-polled installer-status cache Phase 7's `CheckAsync`
    already maintains, no new network call of its own.
  - **The suggest half** (always shown when applicable): explains what the plugin adds (pre-launch
    conflict gate, "Link to SaveLocker," status/sync menu items — Phases 10–13), and a download link
    for the manual, double-click `.pext` path (`## Installation` above, unchanged). **Checked, not a
    gap after all**: this paragraph originally assumed Phase 17's release workflow only publishes a
    `.zip` and would need a small addendum to also produce a `.pext`. Re-read the actual
    `SaveLocker-Playnite/.github/workflows/release.yml` while building this phase (2026-09-16): its
    `Package` step already produces and publishes both `SaveLocker.zip` and `SaveLocker.pext`
    (identical bytes, a renamed copy) — Phase 17's own build session anticipated this need
    independently. No addendum needed; the plan text above was simply out of date with the code.
  - **The install half** (opt-in, one explicit button click, never automatic on its own): calls a new
    `POST /api/playnite-plugin/install`, which reuses Phase 7's own `InstallAsync`
    plan-before-write/digest-verification machinery wholesale rather than duplicating it — the only
    difference from an ordinary self-update is that the destination directory
    (`%AppData%\Playnite\Extensions\SaveLocker\`) does not exist yet. This is the deliberate reversal
    of the plan's earlier "first install is always manual" line ("Installation" section above): that
    line was written against Decky's precedent, where it is a **technical** wall (Decky root-owns the
    plugin's top-level directory, so the agent literally cannot create one). Confirmed at Phase 8/the
    "Installation" section that Playnite's `Extensions\` folder carries **no such constraint** — it is
    plain user-owned, exactly like every file the self-updater already writes for an *update*. So the
    only thing that was actually stopping a first install was the plan's own policy choice to mirror
    Decky's UX regardless — and the direct answer to "can it be implemented," asked directly, is yes.
    What stays intentional is the **consent boundary**: install only ever fires from this one explicit
    button, never from a background poll or an automatic suggestion, so the agent still never silently
    drops files into a third-party app's plugin folder without a deliberate per-install click.
  - **Same restart caveat as an ordinary update** (Phase 14's own finding, unchanged): no hot-reload
    for a compiled `GenericPlugin`, so a first install still ends with "restart Playnite to finish
    installing SaveLocker," identical wording to the update case, not a new UX pattern.
  - **One thing this inherits, not introduces**: Phase 7 is marked shipped but explicitly
    "code-complete; no real plugin package exists yet to test an actual install against" — so neither
    an update *nor* Phase 19's first-install button has yet had `InstallAsync` exercised against a
    real, running Playnite process. Verifying that (file-locking behavior if Playnite has the plugin's
    DLL loaded, the actual restart-prompt UX) is this phase's real hardware-risk, not new machinery —
    worth doing for the first-install case specifically, since a first install into a **not-yet-loaded**
    directory is the easier of the two (nothing has the new DLL open yet), so it is also the better
    case to prove the underlying write/verify logic works before trusting it for an update that
    replaces already-loaded files.
  - **Dependencies:** Phase 18 (shares its Playnite-detection probe; not a hard technical dependency —
    could duplicate the small probe instead if built first, but sharing it is the point). Also depends
    on Phases 6/7/14 (already shipped) for the hosting slot and install machinery, and needs the small
    Phase 17 addendum above before the manual `.pext` link is genuinely double-click-clean.
  - **Verify:** on this real Windows box with Playnite already installed — confirm the card renders
    correctly in all three states (uninstall the plugin to see the "suggest" state, reinstall to
    confirm "installed" reads its real version), confirm the manual `.pext` download link actually
    double-click-installs cleanly once Phase 17's addendum ships, and confirm the "Install
    automatically" button writes a working extension, Playnite loads it correctly after the prompted
    restart, and a second click when already installed correctly reads as "up to date" rather than
    reinstalling redundantly.

```
Phase 1 ──┐                         Phase 6 ── Phase 7
Phase 2 ──┼── (independent)         Phase 4 ── Phase 5
Phase 3 ──┘

Phase 8 ── Phase 9 ── Phase 10 ── Phase 16 (needs 10 working)
              │           │
              ├── Phase 11 ── Phase 12
              │       │
              └── Phase 13 (wants 11/12 too)
              │
              └── Phase 15 (also needs Phase 8)

Phase 14 needs Phases 6/7 (agent) + Phase 9 (plugin)

Phase 17 needs Phase 8; most useful once Phase 10 exists, not gated on it

Phase 18 — independent of everything above; strengthened by Phase 2 (already shipped)

Phase 19 needs Phases 6/7/14 (already shipped) + the small Phase 17 addendum; shares Phase 18's
Playnite-detection probe but does not hard-depend on it
```

---

## Testing plan: a parallel instance via `testenv`

Model this directly on two things that already exist and already work:

1. **`testenv.ps1`'s existing Windows-agent isolation** — a `$StateRoot` that must contain
   `SaveLocker-test`, a distinguishable test build version, and its own local-API port (`:5177`,
   already distinct from the real installed agent's `:5178`) registered against the test dashboard
   server. Nothing about this needs to change; the plugin is simply a second local client of the
   *same* test agent already started by `testenv.ps1 up`.
2. **The Decky precedent** — `Build-DeckyPlugin` stages a fixture plugin under a fake
   `~/homebrew/plugins/SaveLocker`, and `tests/seed-test-conflict.sh` seeds a genuine two-machine
   divergence against a throwaway server purely from one dev box (two machine *identities*, not two
   physical machines).

### New pieces this phase needs

1. **A portable Playnite instance under `$StateRoot`.** Playnite's *portable* build stores its
   config, library, and `Extensions` folder entirely inside its own directory — no `%AppData%`
   involvement — so it runs fully side by side with the real installed Playnite on the same box with
   zero interference. `testenv.ps1 build -Only playnite` downloads/unzips Playnite Portable once
   (cached), builds the plugin project from the new repo, and copies the raw extension folder into
   `<portable>\Extensions\SaveLocker\` — packing an actual `.pext` is only needed for distribution;
   Playnite loads an unpacked extension directory fine for development.
2. **A configurable agent URL in the plugin's own settings**, defaulting to `127.0.0.1:5178`, rather
   than a hardcoded constant patched at build time (the Decky plugin's `main.py` string-replace
   approach). A Playnite plugin gets a real settings page for near-zero cost, so this avoids ever
   touching source for a test build: `testenv.ps1 up -Only playnite` writes the test instance's
   plugin settings file directly, pointing it at the test agent's `:5177`.
3. **No new server-side test infrastructure.** `testenv.ps1 up` already starts a test Windows agent
   registered against the test dashboard; the portable Playnite + plugin is just another local
   caller of it.
4. **`tests/seed-test-conflict.ps1`** — the Windows analogue of the existing bash script, seeding a
   genuine two-machine divergence against the test server the same way. The manual verification path
   becomes: run the seed script, launch the tracked game through the *test* Playnite instance, and
   watch the resolve dialog actually appear and block the launch — the one thing no automated suite
   can prove, exactly like Decky Phase 3's hardware-only note.
5. **Automated coverage stays cheap, in the new repo:** a small test project (or a `run-playnite-
   tests.ps1` in the style of this repo's own suites) drives the plugin's matching logic and its HTTP
   client against a stub local-API server, never against real Playnite — the same split Decky Phases
   1–2 used to keep the discriminating logic testable without the host app, leaving only "does it
   really fire the hook and really block" for the one manual pass.

No second physical machine is ever required — `seed-test-conflict` already proves the divergence
itself needs only two machine *identities* against one server. What genuinely needs the real,
installed Playnite once is confirming `OnGameStarting` actually fires and the dialog actually blocks
the launch — the Playnite-side equivalent of Decky Phase 3's "does Steam actually persist this."

---

## Installation, and the Agent Updates section (Config → Agent Updates)

**First install is always manual**, conceptually identical to Decky's one-time "Install Plugin from
URL" — creating `%AppData%\Playnite\Extensions\SaveLocker\` the first time is a Playnite-side action,
not something the agent can do unattended. Two standard Playnite mechanics, neither built by
SaveLocker:

1. **Double-click a `SaveLocker.pext`** (from a GitHub release, the SaveLocker server, or a console
   help page) — Playnite is registered as the `.pext` handler and shows its own install prompt.
2. **Inside Playnite: Add-ons → Browse → search "SaveLocker"** — but only once listed in Playnite's
   **official** add-on database (`JosefNemec/PlayniteAddonDatabase`), a PR-based submission process,
   not a store SaveLocker runs itself.

### Where this genuinely differs from Decky, favorably — confirmed 2026-09-13

Decky's own store submission was refused outright — the store's PR template requires attesting
generative AI was not used for a majority of the code, which this codebase can't honestly attest
(`logs/2026-08-15_decky-plugin.md`). **Checked directly against `JosefNemec/PlayniteAddonDatabase`'s
own README**: submission is a manifest YAML (`AddonId`, `Type`, `Name`, `Author`,
`ShortDescription`, `InstallerManifestUrl`, `SourceUrl`) plus a pull request, reviewed and merged by
a maintainer. **No AI-authorship clause, no license attestation, nothing beyond the technical
manifest format** — the blocker that killed the Decky store path does not exist here. Getting listed
in Playnite's official "Add-ons → Browse" is a realistic primary path, not just a bonus attempt.

**If and once listed**, Playnite's own built-in "Check for add-on updates" (Settings → Updates)
handles version checking and prompts entirely by itself, reading the version straight from the
database. SaveLocker would not need to build or maintain any update-delivery mechanism at all for
players who install this way — a real simplification over Decky, where the self-update machinery
exists specifically *because* nothing else was checking on our behalf.

### Before a database listing exists (or for a player who installs the `.pext` directly and skips it)

Nothing checks for updates on their behalf — Decky's exact "not on the store" situation, and exactly
why Decky Phase 5 exists. Recommend building the identical mechanism, reusing the infrastructure
wholesale rather than duplicating it:

- Add a fourth `AgentPlatform` slot, `PlaynitePlugin = "playnite-plugin"`, beside `Windows`/`Linux`/
  `DeckyPlugin` in `Contracts.cs`. The array, `Describe()`, and a per-slot GitHub repo (pointing at
  wherever `SaveLocker-Playnite` ends up hosted) are the only genuinely new pieces — upload, digest,
  sidecar, atomic replace, delete, GitHub fetch, the poller, and the download route are all inherited
  exactly as `decky-plugin` inherited them from `win-x64`/`linux-x64`.
- A **fourth row appears in Config → Agent Updates automatically** once the slot exists — the card is
  already generic over the platform list (three rows today), with the same bulk "fetch all,"
  per-package upload/verify controls, and GitHub `SHA256SUMS*.txt` hash verification the other three
  rows already have. No new UI component, just a new entry in `AgentPlatform.All`.
- The **Windows** agent (Playnite's own host, unlike Decky's Linux-only plugin) gains the
  update-checking half `DeckyPlugin.cs` implements for Linux — same shape (`PluginUpdateState`,
  `CheckAsync`/`InstallAsync`, plan-before-write, digest verification) but genuinely **simpler**:
  `%AppData%\Playnite\Extensions\<id>\` is entirely user-owned — no Decky-style root-owned top-level
  directory, no `plugin.json`-can-never-be-rewritten constraint, no chown dance. **Confirmed
  2026-09-13: no hot reload for a compiled plugin extension** (unlike Decky's `watchdog`-driven live
  reload) — Playnite's "Reload Scripts" hot-reload applies only to script extensions; a
  `GenericPlugin`-based one always needs Playnite restarted to pick up new files, and the installer
  flow already prompts for that restart on an ordinary update. So the self-updater just writes the
  files (same plan-before-write safety as Decky) and surfaces a "restart Playnite to finish updating
  SaveLocker" notice — no hot-reload timing race to design around at all, strictly simpler than
  Decky's `debug`-flag/watcher dance.
- No `doctor`-equivalent CLI report is needed here (there is no `savelocker doctor` on Windows) — the
  Windows story is already UI-first (the tray, the agent UI), so this is a collapsed status card in
  the plugin's own settings page, mirroring `DeckyPluginCard.tsx`, not a new CLI surface.

---

## Step by step: what the player actually sees, click to exit (2026-09-14)

Grounded in `Playnite.SDK.IDialogsFactory.ActivateGlobalProgress` (a native, blocking, cancelable
progress dialog Playnite already ships — confirmed via the SDK docs) for the one moment that must
block, and `INotificationsAPI` for everything that must not.

**Click Play:**

1. `OnGameStarting` fires. The plugin has already matched this game automatically (Steam AppID /
   InstallDir / Alias, above) — no UI yet, this is instant and silent.
2. Plugin calls `POST /api/games/{id}/pre-launch-sync`. While the call is in flight, it opens
   Playnite's native `ActivateGlobalProgress` dialog, indeterminate, captioned something like
   *"SaveLocker: checking for a newer save…"* — this briefly **blocks Playnite's own UI**, which is
   correct here: the game must not start until the answer comes back, exactly the same guarantee the
   Linux wrapper gives by running instead of the game.
3. Three outcomes:
   - **`Proceed` (no changes, or a pull already ran) — by far the common case.** The dialog updates
     to *"Save up to date"* for a beat (or simply closes) and the game launches immediately. Total
     added delay: whatever the pull itself cost — for a small save, well under a second; the dialog
     exists mainly for the rare larger pull, not to manufacture ceremony around the common case.
   - **`ProceedSyncPaused`** (another machine currently holds the lease). Dialog closes, game
     launches, but a `INotificationsAPI` entry is added (non-blocking, sits in Playnite's
     notification list): *"Launched without checking for updates — saves are checked out by
     '{machine}'. A conflict may occur if that machine is also playing."* — informational, not an
     error, matching this codebase's existing "not an alert, this is designed behaviour" stance
     toward the equivalent tray-side case.
   - **`Blocked`** (a genuine, confirmed conflict). The progress dialog is replaced by the resolve
     UI (a modal window — either a native WPF dialog or, per the option already noted, a `WebView2`
     control pointed at the agent-ui conflicts page) showing the same "this device / the cloud" card
     the dashboard and agent-ui already use. **The game does not launch** until the player picks a
     side or explicitly cancels — canceling just returns to the library with nothing changed, no
     partial launch, no data touched.
   - **Anything else (agent unreachable, timeout, 5xx, the plugin itself erroring)** — fails open,
     per the route's own documented contract. The dialog closes and the game launches exactly as if
     the plugin were not installed. **No error popup** — a network hiccup must never read as "your
     game is broken." The only trace is the persistent status chip on the game's details page
     quietly reading "SaveLocker: agent unreachable" next time the player looks, never an interrupt.

**While playing:** nothing. The plugin has no role once the game is running.

**After you exit:**

4. `OnGameStopped` fires. If `PushAfterExitEnabled` is off for this game (the toggle above), nothing
   happens at all — no popup, this was a deliberate choice. Otherwise the plugin calls
   `POST /api/games/{id}/post-exit-sync`.
5. This is **not** blocking — the player is back in Playnite's library already, there's nothing to
   wait on. A brief `INotificationsAPI` entry appears and self-clears: *"SaveLocker: save uploaded"*
   (or, if nothing had actually changed since the last sync — the common case for a short session —
   nothing is shown at all, since there's nothing to report; matches `PushCoreAsync`'s existing cheap
   no-op path).
6. If the push itself fails (server unreachable, disk full, whatever) it is logged, and the status
   chip on the game's details page reflects it next time it's viewed ("SaveLocker: last save upload
   failed") — again no interruptive popup for something the player can't act on in the moment. The
   tray's own reactive exit-push (unchanged, still running independently) gets a second chance at it
   shortly after, same as it would for a game not launched through Playnite at all.

**Net feel, by design:** near-silent on the happy path (a sub-second pause on launch at most, a
quiet self-clearing notice on exit), genuinely interruptive exactly once — a real, confirmed
conflict — and never punishing for a transient failure. The chip on the game page is where a
player goes if they want to check anything; nothing is ever forced in front of them uninvited except
the one case that must be.

---

## Open questions — resolved 2026-09-13

1. **Playnite add-on database PR requirements** — ✅ resolved: no AI-authorship clause, no license
   attestation, purely a manifest + PR review. "Get listed" is the primary distribution path.
2. **Hot-reload vs. restart** — ✅ resolved: compiled plugins always need a restart; the self-updater
   writes files and prompts for one, matching Playnite's own existing update-restart flow.
3. **InstallDir-vs-override matching** — still open, discussed below.

### Automatic game matching (revised 2026-09-14, asked directly to make this effortless and push
manual linking to a genuine last resort)

Decky already solves this exact problem for its own host, and its solution is a **priority chain of
automatic signals, with a manual override that is barely ever needed** — not a "pick from a
dropdown" step. `AgentApiServer.cs:386-397` and `TrackedGame.Alias`'s own doc comment spell out
Decky's hierarchy precisely: **(1) Steam AppID, exact match, fully automatic — the primary path for
the large majority of tracked games; (2) name matching, still automatic, falling back to it only
when no AppID resolved; (3) `Alias`, a manual name-override, and only ever needed as a rare
correction when a game's SaveLocker name and its Steam display name genuinely disagree.** Nothing
about Decky's flow normally shows the player a picker at all — Alias is a fix-up, not a routine step.
Playnite should mirror that shape exactly, adapted for the extra identity signals Playnite exposes
that Decky's flat Steam-only environment doesn't have:

1. **Steam AppID — automatic, exact, for every Steam-sourced Playnite entry.** Playnite's own
   `Game.GameId` *is* the Steam AppID (as a string) when `Game.Source.Name == "Steam"` — no
   heuristic needed on the Playnite side at all. The one gap: `TrackedGame.SteamAppId` is
   **currently null on every Windows-tracked game** (its own doc comment: "Set for non-Steam
   shortcuts on Linux… Null on Windows" — nothing has ever needed it there before). Closing this gap
   — having the Windows `GameScanner` resolve and record it for Steam-sourced tracked games, the same
   way the Linux scanner already does — is the single highest-value piece of work in this whole
   matching design, because most Playnite libraries are majority-Steam. This is Phase 2 below, and
   it's worth doing early and not deferring — it is what makes "automatic and effortless" actually
   true for most players on day one, rather than aspirational.
2. **Normalized `InstallDir`** — the fallback for every non-Steam source (Epic, GOG, Xbox, a manual
   `.exe` entry) where SaveLocker's own resolved `InstallDir` points at the same folder Playnite's
   `Game.InstallDirectory` does. Still fully automatic, no player action.
3. **Name matching, reusing `TrackedGame.Alias` exactly as it exists today — no new field.** For
   anything neither of the above resolves (a game whose save lives entirely outside its install
   folder — common; plenty of Ludusavi-manifest saves live under `<winAppData>`/`<winDocuments>`),
   compare `Game.Name` against `TrackedGame.Name` first, and `TrackedGame.Alias` if one has already
   been set — by *either* plugin. Because `Alias` already lives on the shared, server-synced
   `TrackedGame` record rather than in either plugin's own local settings, **a correction made once
   from a Deck's Decky plugin is automatically picked up by this same game's Playnite match on a
   different machine, and vice versa**, with zero extra setup — a genuine, unplanned win from reusing
   the existing field instead of inventing a parallel `PlayniteGameId`.
4. **Last resort, only when 1–3 all fail or produce a genuine tie (two candidates, no clear winner):
   a low-friction, dismissible nudge — never a blocking step.** The game still launches normally
   either way (an unmatched game is exactly like an untracked one today — no gate, no pull, no
   block). The nudge itself: a small notification the *first* time an unmatched tracked-in-Playnite
   game would have been eligible for the gate ("SaveLocker couldn't automatically match '{name}' —
   link it to sync this game" with a one-click picker), not a modal, not repeated every launch, and
   silently skippable forever if the player never acts on it. Picking a game writes `Alias` back
   through the existing `/api/games/{id}/alias` route — no new route needed even here.

Net effect: for a normal, majority-Steam Playnite library, matching is 100% automatic from the
moment the plugin is installed and the machine is registered — no setup screen, no per-game picker,
nothing to configure. The last-resort path exists, but is designed to be rarely seen and never
required.
