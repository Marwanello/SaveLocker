# Session Log (archived)

Condensed history — what shipped, in reverse-chronological order.
Full commit detail in `git log`. Active backlog in `Backlog.md`.

---

## 2026-08-15 — v0.5.7, and a Deck that updated itself

**Tagged v0.5.7** — the Decky plugin self-update (Phase 5), the SteamGridDB key fix, and the agent
UI/KB work that tells people the plugin exists.

**The rollout closed two gaps that had never been observed on hardware.** The Deck **staged and
applied an update entirely by itself**: it noticed v0.5.7, downloaded and verified it, and installed
it at the next start with nobody typing anything. Every piece of that had been proven separately
since v0.5.5 and the unattended run never had — it was the standing caveat in v0.5.7's own release
notes. The **Game Mode "update is ready" notice** (`Ui/UiApp.cs`) was seen for the first time in the
same pass, by the maintainer, which is also what produced `tasks/InstallUpdateNow.md`.

**The trigger was a plain reboot**, chosen over Desktop mode because it needs no terminal. That is
exactly the argument the new task is built on: the notice tells a Game Mode user the update is ready
and gives them nothing to press, and the fallback everyone will actually reach for is the power menu.

Console redeployed and the Windows agent took it from the tray's *Check for updates*. The
**decky-plugin** row of Config → Agent updates is still empty — it needs a release cut from the
plugin's own repo — so nothing about the plugin updating itself has run on hardware yet.

---

## 2026-08-15 — Decky Phase 5: the agent keeps the plugin updated

**The problem was distribution, not code.** Decky holds exactly one custom-store URL and it
*replaces* the official store while set — so the only route to plugin update prompts costs the user
every other plugin's updates, nobody leaves it there, and in practice nobody was ever prompted about
ours. The agent now does it, through the channel it already uses for its own updates.

**It is a third `AgentPlatform` slot, not new machinery.** `decky-plugin` rides the existing
installer routes and inherits upload, digest, sidecar, atomic replace, delete, GitHub fetch, the
poller, the download route and a console card wholesale; the agent asks
`/api/agent/latest?platform=decky-plugin`. The plan called for a separate `GET /api/agent/plugin/latest`
and that would have thrown all of it away. The only genuinely new thing a slot needed is a
**per-slot GitHub repo**, because the plugin releases from `SkorcherX/SaveLocker-Decky`.

**Writing the files IS the install** — Decky chowns a non-`_root` plugin's contents to the desktop
user and hot-reloads on change, so there is no sudo, no `systemctl` and no Steam restart. What
shapes `Agent.Linux/DeckyPlugin.cs` is the other half of that: the plugin *directory* stays
root-owned 755 and `plugin.json` is root's outright. So every destination is proven writable before
one byte is written, `plugin.json` is skipped by name rather than discovered by failing, and
`package.json` is written last — the plugin reloading mid-update should run new code briefly claiming
the old version, never the reverse. The plan asked the package to carry a file manifest; a zip
already is one, so the entry list is used directly and nothing new has to be kept in sync.

**And it now says so.** Nothing told a user the plugin existed, so the agent UI got a
collapsed-by-default card on Overview and the console got a KB article. The card reads real state
from a new `GET /api/decky` — local file reads, no network, re-read per request — so it says
"INSTALLED v0.2.0" instead of offering install steps to someone who already followed them. What had
deferred that was regenerating `api-types.ts`, believed to need the installed agent stopped; it does
not. `openapi-typescript` emits no server URL, so generating from a daemon on any port is
byte-identical to the `:5178` script's output. In [[Gotchas]].

**`run-linux-tests` 161 → 197/197 → 208/208**, with a fake `~/homebrew/plugins/SaveLocker` in the fixture HOME.
Against a build with the guard removed the refusal checks fail as they should — but the two
"wrote NOTHING" assertions beside them still passed, surviving only on enumeration order. They are
recorded as order-dependent in the write-up rather than trusted.

**None of it has run on hardware**, which is why this is not called shipped. The reload mechanism was
observed by hand during Phase 4, but the agent doing it by itself, and the server hosting a real
plugin zip, are both unexercised. Write-up: `logs/2026-08-15_decky-plugin.md`.

**Also fixed: CS-09 had regressed, by a route nobody would have guessed.** A bogus SteamGridDB key
was being accepted and stored again (`run-server-bugbounty-tests` 162/164, reproduced on pristine
`main` with the tree stashed, so not from this change). The first hypothesis — that SteamGridDB now
answers 200 for a bad key, so read the body's `success` flag — was **wrong**, and following it would
have shipped a fix that fixed nothing. The response headers gave it away: `cf-cache-status: HIT`,
`age: 37422`. Cloudflare was serving a **ten-hour-old copy of somebody's valid-key response** to a
request carrying deliberate nonsense; the origin never saw the `Authorization` header at all, and the
cached body says `"success":true`. `Cache-Control: no-cache` does not help either — the edge ignores
a request directive. The fix is a unique `&_=<guid>` on the verification probe, which forces
`BYPASS`; the origin then answers 401 and says which kind of wrong the key is (`Invalid key format`
/ `Invalid API key` / `Authentication Required`), so the console now passes that on. Art fetches keep
their cacheable URLs deliberately. **This is the second time this bug has arrived** — CS-09's version
verified against an endpoint needing no key at all — so the general shape is now in [[Gotchas]]: a
credential cannot be verified through a URL a CDN will cache.

---

## 2026-08-15 — v0.5.6 + the Decky plugin, and a save-loss bug the Deck found

**The release exists because of one bug, and only hardware could have found it.** A tracked game
with no recorded `SteamAppId` matched nothing in `ProtonRun`'s resolver, so it played with **no sync
at all** — logging "no tracked game matches this launch" to a file nobody reads while the game was
tracked, its folder mapped and its launch options correct. Two of four games on the maintainer's Deck
were in that state; a Khazan session on 2026-08-11 was never pushed. A Proton prefix is *named* for
the AppID that launches into it, so the save path already held the answer: `SteamLayout.CompatDataIdIn`
(promoted out of Doctor, now numeric-only) plus `TrackedGame.ResolveSteamAppId`, resolved everywhere
that matches or reports, and recorded by the daemon.

**The Decky plugin, Phases 1–4 of `tasks/DeckyPlugin.md`.** It sets the Steam launch options the
agent *cannot* — Steam rewrites `localconfig.vdf`/`shortcuts.vdf` on exit, so only code inside Steam
can — and its Quick Access panel now shows lease warnings, status, push/pull per game or all, and
`doctor` on demand. The rule lives in `Agent.Core/LaunchOptions.cs` and the plugin holds none of it,
so the launch command can change without a plugin release. It moved to its own repository
(`SkorcherX/SaveLocker-Decky`, v0.2.0) because Decky tracks plugins as submodules whose root is the
plugin. **Not submitted to the Decky store:** its PR template requires attesting that generative AI
did not write a majority of the code, which cannot honestly be claimed here.

**Four bugs were caught by the plan's own rules before they shipped**, which is the argument for
those rules: the "stale path repair" would have rewritten a working `WINEDLLOVERRIDES` line on a real
game; idempotence keyed on the binary's *filename* would have re-wrapped forever under
`dotnet savelocker.dll`; the plugin would have overwritten every user's mangohud line had it compared
against `desired` instead of round-tripping through `/resolve`; and a missing `package.json` made
Decky eval an ESM bundle as a classic script.

**The UI lessons cost the most time and are all in `Gotchas.md` → Decky.** The QAM scrolls by moving
focus, so non-focusable rows are holes the D-pad skips; `Field`'s `focusable` prop is the way, not a
bare `Focusable`. `Field` parks children in a right-hand column unless told otherwise. And the panel
**remounts when a Steam dropdown closes**, destroying component state — which is why a game selection
never stuck, found only by logging state at *render* time after three wrong guesses, one of them
caused by reading a console buffer that `Runtime.enable` had replayed.

Also: `install.sh` over a running daemon verified twice on hardware; `run-linux-tests` 123 → 161.

---

## 2026-08-10 — v0.5.4: enrollment filters + a console that scrolls in panes

Three changes, one theme — a large library was unnavigable in both surfaces.

**The console page no longer scrolls; its panes do.** The root was `minHeight: 100vh`, so the page
grew and the games sidebar never got a bounded height: picking a game far down the list pushed its
settings off screen. Root is now a fixed `height: 100vh` + `overflow: hidden`. The trap that cost
the time is in `Gotchas.md` → Web console — a bounded flex column shrinks its children rather than
overflowing, so ConfigView's cards collapsed to ~4 px each instead of producing a scrollbar.

**Add Games has filter chips** — Suggested / All / Steam / Added to Steam / Heroic / Needs path,
with Heroic storefront sub-chips fed by a new `Store` on `ScanCandidate` and `CandidateDto`. Zero-
count chips are not rendered. `Suggested` is the old hide-Steam-Cloud default, now named and
reversible. Mirrored into Game Mode (`Ui/UiApp.cs` → `AddFilter`), minus the store axis.

**The Linux agent now discovers installed Steam games** — see `Decisions.md` for the reversal and
why it is one. `SteamTextVdf` moved `src/Agent` → `src/Agent.Core` to make it reachable.
`run-linux-tests.sh` 59 → 62: the new fixture puts the installed game in a SECOND library with its
own compatdata, the only shape that catches a scan reusing the main Steam root.

**Deck-tested (24 candidates, real library).** One real bug found there — a compat tool is
identified by `toolmanifest.vdf`, never by appid; recorded in `Decisions.md` → Linux discovery
(`run-linux-tests` → 63). Shipped without hardware coverage on two surfaces: the Heroic store
sub-chips (the test Deck has no Heroic games, so the chip correctly did not render) and the Game
Mode filter row's gamepad navigation. Neither can lose save data, which is why they shipped.

## 2026-08-09 — v0.5.3: Heroic Games Launcher detection

Games staged in Heroic are detected with their save paths. Full write-up, including the four
runners verified on hardware and the two Steam conventions that turned out to be only Steam's:
`logs/2026-08-09_heroic-detection.md`. `run-linux-tests.sh` 40 → 59.

One finding was unrelated to Heroic and outlived it: games that save as loose files beside their own
executable resolve to the INSTALL DIRECTORY, i.e. the whole game. Syncing that would let a restore
prune another machine's installation, so it is refused. The guard costs 8% of the manifest and was
kept deliberately — `Backlog.md` → file-level saves carries the sizing.

## 2026-08-08 — PR #36 `save-path-autodetection` (→ `637d11f`)

Save-path autodetection went from resolving **57.5%** of sampled manifest games to **99.5%**, and
from ~4% confidently-wrong answers to **zero**. The reasoning behind each fix now lives in
`ManifestLoader.cs` / `PathResolver.cs` comments, which are the copy that cannot drift:

1. `PathResolver` implemented 10 of the manifest's 13 placeholders; the three missing ones appear in
   more save paths than everything else combined (`<base>` alone outnumbers every other token).
   `<storeUserId>` is **discovered from disk, not derived** — Steam exposes both a 32-bit account id
   and a SteamID64 and games use either.
2. `tags` were ignored, so a `config`-tagged path could beat the real save path on hash order.
   Returning nothing now beats returning a wrong path presented as certain.
3. Name matching ignores punctuation as well as case, while deliberately keeping word boundaries —
   deleting non-alphanumerics collapses "Dragon Quest I & II" onto "III" and the manifest holds both.
4. Enrollment creates games under the manifest's canonical title, so two machines spelling one
   shortcut differently stop creating two server games that can never sync.
5. Windows installed-Steam games never consulted the manifest at all.

**New suite `tests/detection`** — materialises dummy save trees at the paths the real manifest claims
and scores the production resolver against them. No Steam, Proton, GPU or Deck needed. See
`tests/detection/README.md`.

**`run-linux-tests.sh` passed for the first time — 40/40.** It had never worked:
`Storage__AgentInstallerRoot` was unset, so the server died at startup *after* migrations and all 16
server-dependent checks failed as though the agent were broken. One variable.

Two verification traps from this work are in `Gotchas.md`: the Windows suites drive
`src/Agent/bin/DEBUG` (a Release-only build reports green while testing nothing), and a harness can
be structurally incapable of catching the bug it was written for (the detection oracle used the same
fake `<storeUserId>` on both sides, and dropped `tags` when re-serialising).

## 2026-08-05 — Fleet version strings + the Deck double highlight (PR #32)

Two field reports, one root cause each, and a testing lesson that outlasts both. Merged to `main`
(`69e9691`); **unreleased — no tag carries it**.

**"Why do we have v0.5.0 and v0.5.0.0?"** We didn't — one build reported itself two ways.
`HealthReporter` sent `Version.ToString()`, which prints as many components as it was parsed from,
and the platforms parse from different places: the Windows PE version resource is four-part, the
Linux `AssemblyFileVersion` attribute three-part. Every other call site already used `.ToString(3)`;
the heartbeat, the one thing the console displays, was the sole exception. It presented as a fleet
running mixed versions — which is a real fault with real consequences — rather than as a formatting
bug. One `UpdateChecker.CurrentVersionText` now feeds every surface, and `normalizeVersion` collapses
already-recorded strings console-side so a fleet mid-upgrade is not flagged.

**The Deck's double highlight.** Hover and focus paint the same ring on purpose, so a mouse-driven
dev session looks like a gamepad-driven one. But gamescope always supplies a pointer position whether
or not anyone touched the trackpad, so a *stationary* pointer painted a second selector on whatever
it rested over — one the D-pad could not move and A did not activate. That also fully explains "A
does nothing": focus was on Overview all along, so A re-selected the screen already showing while the
eye was on the Quit ring. No frame-0 nav bug was needed to explain the report.

**The lesson: the first A/B proved nothing, and looked like it proved everything.** Before and after
came back pixel-identical, because WSLg leaves the real pointer outside the window (hover dead) and
captures after settle (frame-0 race already repaired). Reproducing the symptom needed a new
affordance — `--pointer X,Y` — and that took two wrong turns, each of which *looked* like success:
queued through `AddMousePosEvent` it raced Silk's own write and manufactured a delta every frame, so
the harness claimed the cursor and the test measured the harness; assigned to `io.MousePos` after
`NewFrame` it silently killed hover everywhere, because `HoveredWindow` is resolved inside `NewFrame`
— producing another matching, meaningless pair. Three `Gotchas.md` entries came out of this.

**What was deliberately not shipped.** The rail's first-frame `SetKeyboardFocusHere` seed was removed
on sound reasoning and then restored, because WSLg cannot reproduce the failure it supposedly causes
and an unverified change was not worth carrying. The suspicion lives at the call site.

---

## 2026-07-29 — v0.5.0: three bug bounties shipped

WA-09…WA-12 finished the Windows bounty, then all three bounties merged (PR #30), the notes landed
(PR #31) and `v0.5.0` was tagged. 34 findings across console, Deck agent and Windows agent.

**WA-09** was the one the others rested on. `TrayContext` captured `SynchronizationContext.Current`
in its constructor — which runs as the *argument* to `Application.Run`, before the loop installs the
WinForms context — so the capture fell through to the thread-pool default and every "marshal to the
UI thread" call in the tray was a plain pool post. Nothing failed loudly, because WinForms only
throws on a cross-thread call once a handle exists and a `NotifyIcon` menu has none. `UiDispatcher`
forces a control handle instead, which is itself what installs the context: it makes the owner rather
than hoping to find one.

That fix nearly certified itself. The first pre-fix comparison showed **6 of 6 passing** — because
the shim constructed the marshalling `Control` before reading `Current`, and constructing a `Control`
installs the context on its own. Reading `Current` first reproduces the defect. A "confirmed" fix
that was never needed was one ordering away.

**WA-10** and **WA-11** were both the same shape as the console bounty's theme: a report describing
an intention rather than an outcome. The startup toggle answered ok whether or not Windows accepted
the change, and `IsEnabled` accepted any non-empty Run value — so an entry left by an uninstalled
copy read as enabled. Discovery caught only *parse* errors, so one unreadable folder failed the whole
scan and took the manual setup path down with it.

**WA-12**'s fix was four lines; its test cost an hour. The first version used a hand-rolled
`HttpListener` stub, which cannot work: a single-threaded accept loop is only listening while it is
inside `GetContextAsync`, so the tray's lease POST arrived in a gap, was never accepted, and hung
forever with no error anywhere — reading exactly like a broken process watcher. Instrumenting the
watcher and the launch handler found it; the block now uses the real server with a lease genuinely
held by a second registered machine. Now in `Gotchas.md`.

Two test-only environment variables were added under the rule settled the day before
(`SAVELOCKER_TRAY_PORT`, `SAVELOCKER_RUNKEY_SUBPATH`). The second exists so the access-denied branch
can be tested with a real Deny ACE without putting one on the machine's actual Run key.

CI's first-ever run of the branch caught a Linux-only check the Windows box always skips: `doctor
names a prefix-root save path`. Not a regression — WA-02 moved that defence earlier, so `add-game`
now refuses a Wine prefix before the game is created and doctor had nothing left to describe. The
test asserted both halves instead.

**Two mistakes worth recording.** A `git checkout` meant to drop debug probes also reverted half the
WA-12 fix, and the suite still passed — the other half alone was enough — so only the commit's file
list caught it. And the release notes were verified by *looking at the rendered page*, which is how a
`<br>` carried over from this vault's Obsidian markdown was found rendering as literal text, along
with an angle-bracketed placeholder that had vanished entirely. The build was perfectly happy with
both.

Shipped unverified, deliberately: no Deck run, no second Windows account, no clean VM, and the Linux
suite still unrun. The notes are worded to match — the credentials bullet describes the permission
change rather than promising the guarantee, and Known Issues says why.

---

## 2026-07-27 — Console / server bug bounty: all 13 findings, one commit each

The P0 was real and worse in practice than on paper: deleting a machine cascaded through every save
version it had uploaded, taking a game's Latest with it — 3 versions to 1, head nulled, archives
still on disk with nothing left to reach them. `SaveVersion.MachineId` is nullable now, with a name
snapshot so history can still say who made it.

The rest clustered into one theme: **decide first, commit second**. Command delivery marked a job
Dispatched and never looked again, so a crashed agent took the job with it (now a visibility lease).
Archive and installer uploads wrote straight to their final path, so a disconnect left a truncated
file where a real one belonged (now staged and renamed). A SteamGridDB key was stored and then
checked. An enrollment token was burnt and then redeemed. Lease acquisition read, decided, inserted —
and gave one of two simultaneous launches a 500, six times out of six.

Every finding was reproduced against the pre-fix build before being fixed, which paid for itself
twice: it caught three checks that were passing **vacuously** (a socket built with
`New-Object Type(a,b)`, which never connects), and it turned up a defect the review had not:
the SteamGridDB "verification" probed a public endpoint, so 25 characters of nonsense verified fine.

Two fixes were narrowed after an existing suite objected — Set-as-Latest closing conflicts it had no
business closing, and a loopback enrollment URL refusal that broke the same-box setup both
enrollment suites rely on. In both cases the suite was right and the first design was too broad.

CS-06 was scoped down by the maintainer: no tunnel, no HTTPS, LAN-only, so the forwarded-header half
guards a topology that does not exist. What survived is the part that bites on a plain LAN.

New harness `tests/run-server-bugbounty-tests.ps1`, 145 checks. Archived write-up, including what is
deliberately untested and why: `logs/2026-07-27_console-bugbounty.md`.

---

## 2026-07-25 — Deck UI navigation, fixed by reading the binding instead of ImGui

Menu navigation had been the Deck UI's biggest obstacle across three sessions. The recorded blocker —
"ImGui 1.90.8 exposes no public API to set the nav cursor directly" — was true of **ImGui.NET's
managed assembly** and false of the `libcimgui.so` it ships, which exports the whole `imgui_internal`
surface. Four `DllImport`s later, `igSetFocusID` places the cursor and the workarounds from the two
previous attempts came out.

Three reported symptoms turned out to be one mechanism: a focus request re-asserted for 45 frames,
whose `SetKeyboardFocusHere` overrode nav movement and swallowed every press for ~0.75 s. Two of the
three diagnoses written from source reading were wrong, and a debug overlay (`ui --nav-debug`) built
first is what corrected them. Also fixed: focus rings clipped away inside zero-padding child windows
(shipped in v0.4.0), a ring that pulsed too faintly to notice, separators drawn under the panes that
covered them, a stepper whose horizontal +/− fought D-pad order, and B — which had never been
implemented at all, only inherited from ImGui's incidental `NavCancel`.

Shipped as v0.4.1. Full record: `logs/2026-07-25_deck-ui-navigation-fix.md`.

---

## 2026-07-23 — the Octopath conflict storm, and everything it exposed

One weekend of real play produced **75 open conflicts and 2.66 GB on a game set to retain 5**, and
escaping it needed `curl` against the admin API. Four defects stacked, each hidden behind the
previous one. The server was correct on every single request. Narrative:
`logs/2026-07-23_conflict-storm.md`.

**Two releases.** v0.3.3 (agent, auto-updates Windows) carried the root-cause fix; v0.3.4 (server)
carried everything else. Five PRs: #18–#22.

- **0.0 — the daemon pushed from state the launch wrapper superseded.** Two processes own
  `config.json`; the daemon loaded it once at boot and never re-read, so after the wrapper pushed on
  game exit every watch-push presented the boot-time parent. It conflicted on *every* save until
  restarted, on a fleet of one machine. `Decisions.md` §10 amends §8, which had fixed only the write
  half. **Device-verified on a Deck: 4 saves, zero conflicts.** `logs/2026-07-23_agent-stale-parent.md`.
- **0.4 — resolving reached only the database.** Now enqueues a *guarded* pull for **both** machines;
  the winner is stuck too, since its pointer still names the parent it presented.
- **0.1 / 0.2 — conflicts dedupe** (75 → 1, keyed per machine, carrying `Count`/`LastSeen`/`MachineId`)
  **and retention runs while conflicted.** Migration `20260723220958_AddConflictDedupe`.
- **Tier 1 — the console.** Every conflict shown newest-first (was `.find()` over an oldest-first
  list), machine/time/size per option, a confirmation naming what breaks, **Prune now**, version
  **Download**, and conflict alerts offering **Resolve** instead of Dismiss.

**Suites: `run-agent-tests` 20 → 35, `run-concurrency-tests` 12 → 17.** Every new check was proven to
fail against pre-fix code before being accepted.

⚠️ **Three lessons worth carrying, all the same shape — the thing that looked verified was not:**
1. **A rule 17 callers must remember is not a rule.** `Save()`'s "don't write sync state" lived in a
   doc comment; `CommandPoller` forgot. Fixed by making the primitive safe, not the caller careful.
2. **The tests caught what review did not, twice** — including a destructive interaction where 0.2's
   prune deleted the data 0.4 needed, an hour after 0.4 merged green.
3. **`docker-publish.yml` did not trigger on tags**, so v0.3.4's image shipped stamped
   `0.3.3+11.9ae9307` — correct code, amber "not a release" chip. Same class as the `fetch-depth: 0`
   trap: CI version derivation fails *quietly*.

Also: `AGENTS.md` had drifted from `CLAUDE.md` and still said *"EF Core pinned to 9.0.x — do not
upgrade to 10.x"*, four months after the net10 migration. Both agent files are now tracked and
generated from `CLAUDE.md`.

---

## 2026-07-18 — Linux/Deck security hardening → v0.2.0 (PR #8)

Closed all three high-priority hardening items, shipped v0.2.0, and completed the operational
follow-up (container update + fleet key rotation). Code-signing was explicitly set aside.

**Three fixes, each with a test proven to fail against the pre-fix code:**

- **The local agent API was an unauthenticated management API.** `AgentApiServer` — shared by the
  Windows tray and the Linux daemon — rewrites `config.json`, re-registers the machine and changes
  what syncs, but shipped with no auth, `AllowAnyOrigin`, and the machine's server API key in
  `/api/state` and `/api/config`. "Only binds loopback" is not authentication: every process running
  as that user reaches it, as does any web page the user has open. Now: 32-byte token
  (`{configDir}/api-token`, 0600) on every `/api/*`, fixed-time compared; Host + Origin validated so
  a DNS-rebinding page is refused even with a correct token; no CORS policy at all; the key is never
  serialized. The token reaches the bundled UI by injection into `index.html`.
  **`daemon --lan` was removed** — it bound all of the above to every interface. It now exits
  non-zero with an SSH-tunnel instruction rather than being silently ignored.
- **A machine could conflict with itself.** The agent is not one process: autorun keeps the daemon
  alive while Steam starts `savelocker run` as a second one. Each held its own `AgentConfig` loaded
  at startup, and a whole-object `Save()` erased the `LastKnownVersionId` the other had just
  recorded. The next push then presented a stale parent and the server rejected it as a conflict —
  **indistinguishable in the dashboard from genuine two-machine divergence.** Fixed with a
  per-game cross-process lock (`AgentStateLock`) held *alongside* the in-process semaphore (a flock
  is per-process, so two threads in one process both acquire it), atomic writes (`AtomicFile`), and
  read-merge-write for config sync state, the offline queue and health events.
- **A pulled archive could overwrite files outside the save folder.** Phase 6 made the restore's
  *delete* pass no-follow but left the *copy* pass following links: with `linkdir -> /elsewhere` in
  the target and `linkdir/secret.txt` in the archive, `File.Copy` wrote straight through. Proven
  exploitable — the test fails exactly there against pre-fix code. Destination paths now refuse to
  traverse a link below the save root (the root itself is *followed*, deliberately: it is
  user-chosen, and symlinking saves onto an SD card is legitimate). Added zip-bomb caps (100k
  entries, 2 GB uncompressed) checked against declared sizes *and* bytes written.

**Incidental fixes:** agent state resolved to the machine-default dir rather than beside its
`--config`; a one-shot CLI `push` never queued a failed upload.

**Verified on Windows and Linux** (WSL Ubuntu 24.04, ext4 — real symlinks and real `flock`, not just
Windows junctions and share-deny): hardening 28/28, concurrency 12/12, local-API 15/15, health 17/17,
enrollment 16/16, Linux harness 33/33. Two new suites wired into CI.

**Three testing lessons, all recorded in `Gotchas.md` — every one produced a green result that meant
nothing:**
1. The first concurrency test raced four identical `push` processes and **passed against fully
   reverted code**: process startup dominates, so the writes never overlapped. Rewritten around a
   long-lived daemon vs. a short-lived process, which makes the stale copy deterministic.
2. The first archive tests **passed vacuously** — the upload planting the hostile archive was 404ing
   inside a bare `catch {}`, so the server had no archive at all. Every fixture step that must
   succeed is now its own assertion.
3. A queue assertion aimed at a game the daemon *watched* proved nothing, since its folder watcher
   had already pulled that game into memory.
   **Rule: revert the fix and confirm the test fails.** Nothing else caught these.

**Environment lessons:** WSL is a working test bed — `dotnet`/`pwsh` are simply absent from a
non-interactive PATH, which made it look unprovisioned; `dotnet build a.csproj b.csproj` silently
does not build both (a stale `Shared.dll` made the fix look broken on Linux); a dirty dev DB fails
the enrollment suite 12/16 in a way that reads as a code regression.

**CI gap found by CI:** `agent-tests-linux` never built `agent-ui`, and the agent csproj's copy target
is conditional on `dist` existing — so it silently skipped, the daemon served no UI, and the
token-injection assertion failed. Invisible locally where `dist` already existed.

**Ops (maintainer, same day):** container updated; both Windows agents upgraded 0.1.8 → 0.2.0 and
re-registered with the admin password. Rotation had to come *after* the agent upgrade — rotating on
0.1.8 would have re-exposed the new key through the same hole. Verified beforehand that v0.1.8 →
v0.2.0 changed **nothing** in `Contracts.cs`, `src/Server/` or `ApiClient.cs`, so container and agent
upgrades were order-independent.

---

## 2026-07-15 — Agent local API and generated UI types

- Replaced the raw `HttpListener` and anonymous JSON responses with an in-process ASP.NET Core minimal API shared by the Windows tray and Linux daemon.
- Added named request/response contracts and `/openapi/v1.json`; `agent-ui/src/api-types.ts` is generated with `openapi-typescript`, while `types.ts` keeps only UI aliases and numeric normalization.
- Preserved loopback-only Windows hosting, opt-in LAN hosting on Linux, SPA fallback, native folder picking, registration, scanning, and the existing synchronous host lifecycle.
- Extended the Linux package CI smoke test to start the installed daemon and verify `/`, `/api/state`, OpenAPI, and generated-type drift.
- Verified Linux and Windows builds, agent UI TypeScript/Vite build, and a live isolated API host. The Windows build retains its pre-existing WebView2 `WindowsBase` warning.
- Device verification confirmed both the 5e exclude-glob behavior and the save-in-use settle gate; removed those completed checks from the backlog.

---

## 2026-07-12 — Scheduled GitHub installer auto-poll

- Added `AgentInstallerPollerService`, a server `BackgroundService` following the existing `IServiceScopeFactory` scheduler pattern.
- Added opt-in `AgentUpdate:AutoFetchHours` (`0` by default; Docker env form: `AgentUpdate__AutoFetchHours`). Enabled schedules check immediately at startup, then at the configured hour interval.
- Reused `AgentInstallerService.FetchLatestFromGitHubAsync` with an `onlyIfNewer` mode so metadata is polled without repeatedly downloading the same installer. Manual dashboard fetches retain force-refresh behavior.
- Verified with `dotnet build src/Server/SaveLocker.Server.csproj --no-incremental` (0 warnings, 0 errors) and an isolated startup smoke check showing the scheduler disabled by default. The smoke host later hit a local Windows Event Log permission failure unrelated to the feature.
- Follow-up: the Configuration → Agent Updates card now sets the interval directly. The server stores it in `AppSetting`, exposes it through the settings contract, and re-reads it within a minute so changes apply without a restart.

---

## 2026-07-12 (session 2) — Per-game exclude globs + upload cap (hygiene 5e)

**Commits:** `1b571f8` (Shared), `d28b635` (server), `bd39588` (agent), `5e7e273` (web). Task brief: `logs/002_glob_filters.md`.

- **Decisions:** exclude-only • 200 MB configurable cap • global defaults + per-game overrides.
- **Shared** — `SaveArchive.HashDirectory`/`CreateArchive` take optional `excludeGlobs`, applied through one shared file enumeration (`Microsoft.Extensions.FileSystemGlobbing`) so the hash always matches the archive. `CreateArchive` builds the zip entry-by-entry (not `CreateFromDirectory`) to skip excluded files. Verified: excluding `*.log`+`cache/**` yields the same hash as a dir without them; archive omits them.
- **Server** — `Game.ExcludeGlobs` (newline-separated) + migration `AddGameExcludeGlobs`. `GlobConfig` helper (parse/join/global-defaults/effective). Agent `GET /api/games` returns the **effective** set (global ∪ per-game); dashboard endpoints return per-game. `POST /api/games/{id}/excludes`. Upload endpoint lifts Kestrel's 30 MB to `Storage:MaxUploadMb` (200). `ServerSettingsDto.DefaultExcludeGlobs`. `Sync:DefaultExcludeGlobs` config. Live-tested: dashboard sees per-game, agent gets merged+deduped effective, settings expose defaults.
- **Agent** — `TrackedGame.ExcludeGlobs`; reconcile keeps it in sync (silent) + sets on adoption; `SyncEngine` Push/Pull pass it to hash + archive.
- **Web** — `api.setExcludes`; GameDetail exclude-patterns textarea + read-only global-defaults display; editor resets on game switch via render-phase state reset (no clobber on poll).
- **Pending:** agent release so the runtime applies excludes, then device verification.
- **Tooling installed this session:** `dotnet-ef` 9.0.9 (global), plus `Microsoft.Extensions.FileSystemGlobbing` package ref in Shared.

---

## 2026-07-12 — v0.1.2 fully verified + sync-toast reduction

**Commit:** `777b9ab`. `gh` CLI installed + authed (SkorcherX, keyring) — now available by full path `"$env:ProgramFiles\GitHub CLI\gh.exe"`.

- **v0.1.2 fully verified on device** — all three v0.1.1 auto-update bugs confirmed fixed: agent version display (`0.1.2`), silent auto-relaunch, and installer persistence across a Docker update.
- **Sync toaster spam → one toast** — a dashboard sync fired 4 balloons (pull result, push result, a folder-watcher auto-push tripped by the restored files, and the command summary). Root cause: `SyncEngine`'s single callback both logged and toasted, so every routine progress line popped a balloon. Split it into `log` (agent.log, always) + `notify` (toast). Routine progress is now log-only; only conflicts, blocked pulls, offline-queue retries, and lease-checkout warnings toast. Dashboard commands emit one summary that includes the latest save timestamp (single-game). Tray Force Pull/Push get a one-line confirmation. Pre-launch/post-exit auto-syncs are now silent on success (by design). Not yet in a tagged release.

---

## 2026-07-11 (session 2) — v0.1.2 auto-update fixes + fetch-from-GitHub

**Commits:** `3902505`, `f8accb4`, `303fdfc` (fixes), `639bce1` (feature). Tag `v0.1.2` force-moved onto `303fdfc`.

- **Agent version bug (the hard one)** — UI showed `0.0.0`, then `0.1.0` after a first attempt. Root cause: MinVer assigns `Version`/`FileVersion`/`AssemblyVersion` **inside an MSBuild target**, and target property assignments override command-line `--property` globals — so neither `--property:Version` nor `--property:AssemblyVersion` ever won (FileVersion fell back to the `MinVerMinimumMajorMinor` floor `0.1.0`). Fixed two ways: (1) `build-installer.ps1` now sets the `MinVerVersionOverride` env var (MinVer's own escape hatch, stamps all fields); (2) `UpdateChecker.CurrentVersion` reads `FileVersion` via `Environment.ProcessPath` instead of `AssemblyVersion` (`Assembly.Location` is empty for single-file exes). Verified locally: `FileVersion=0.1.2.0`.
- **Installer persistence** — added `Storage:AgentInstallerRoot=/data/agent-installer` to `appsettings.json` (was defaulting inside the container, wiped on every Docker update).
- **Silent auto-relaunch** — `skipifsilent` already removed from `SaveLocker.iss [Run]`; still needs a real end-to-end test.
- **Fetch installer from GitHub (feature)** — new `POST /api/admin/agent-installer/fetch-github`: calls the GitHub Releases API, finds the `SaveLocker-Agent-Setup-*.exe` asset, downloads it, and hosts it via `AgentInstallerService.FetchLatestFromGitHubAsync` — automating the manual download+upload. Repo configurable via `AgentUpdate:GitHubRepo`. "Fetch latest from GitHub" button in Config → Agent Updates. `openapi.json` + `api-types.ts` regenerated.
- **Note:** `v0.1.2` tag was force-moved 3× as fixes landed. The GitHub Release object wasn't regenerated (no `gh`/token locally) — CI overwrites the installer asset but release notes may be stale.

---

## 2026-07-11 — MinVer versioning + release CI + server installer + console UI

**Commits:** `0a8f2fc`, `c9c6fee`, plus namespace/build fixes through `6cf06a6`

- **MinVer (Task A)** — removed hardcoded `<Version>0.1.0</Version>` from `SaveLocker.Agent.csproj`; added MinVer package. Version now derived from nearest git tag. `MinVerMinimumMajorMinor=0.1` floors untagged dev builds at `0.1.x-alpha`.
- **Release CI (Task B)** — `.github/workflows/release.yml`: triggers on `v*` tags, runs on `windows-latest`, installs Inno Setup via Chocolatey, runs `build-installer.ps1`, uploads exe to GitHub Release via `softprops/action-gh-release@v2`.
- **Server installer hosting (Task C)** — `AgentInstallerService` stores installer binary in `data/agent-installer/` with a sidecar `installer-info.json`. `GET /api/agent/latest` checks filesystem first before falling back to static config. New public `GET /api/agent/installer/download` streams the binary. Admin endpoints: `GET/POST/DELETE /api/admin/agent-installer`. `AgentInstallerStatus` record added to `Contracts.cs`. Kestrel body limit raised to 200 MB for installer uploads.
- **Console admin UI (Task D)** — "Agent Updates" card in Configuration page: shows hosted version/size/date, download link, Delete button, upload form (version auto-parsed from filename).
- **Live version in agent UI (Task E)** — `currentVersion` added to `/api/state`. Hardcoded `"Agent v1.0"` replaced with live value from state.
- **Namespace rename fix** — 42 source files with `LocalGameSync` → `SaveLocker` namespace changes were never committed; Docker builds were failing. Staged and committed all in `29e3237`. Also fixed Dockerfile and ci.yml csproj refs in `e95f1bb`.

---

## 2026-07-10 — Security patch + agent versioning + auto-update

**Commits:** `0bf04a1`, `809716b`

- **SixLabors.ImageSharp** bumped 3.1.7 → 3.1.12 (GHSA-rxmq-m78w-7wmc, GIF decoder DoS).
- **UpdateChecker.cs** (new) — queries `GET /api/agent/latest`, compares with `System.Version`, respects `AgentConfig.SkipVersion`, streams installer to `%TEMP%`, launches with `/SILENT /FORCECLOSEAPPLICATIONS /NORESTART`.
- **TrayApp.cs** — startup check 5 s after launch; 24 h `System.Threading.Timer` re-check; 24 h cooldown via `AgentConfig.LastUpdateCheck`. Tray menu item "Check for Updates" / "Update to vX.Y.Z…". Balloon → confirm dialog: Update Now / Skip This Version / Remind Me Later.
- **AgentApiServer.cs** — `GET /api/agent-version` → `{ currentVersion, latestVersion, updateAvailable }`.
- **Server** — `GET /api/agent/latest` reads `AgentUpdate:{LatestVersion,DownloadUrl}` from `appsettings.json`; returns 204 when unconfigured. `AgentVersionInfo` record added to `Contracts.cs`.

---

## 2026-07-08 — Hygiene #5a–#5d

See `logs/hygiene-2026-07-06.md` for the full review. Items shipped:

- **#5a** (`0015cda`) — `BackupService` + `BackupScheduler`: nightly `VACUUM INTO` SQLite snapshot, newest-N retention (default 7), `POST /api/admin/backup` + `GET /api/admin/backups`. Startup catch-up. Backups land at `/data/backups`.
- **#5b** (`1782367`) — OpenAPI contract (`AddOpenApi`/`MapOpenApi`, `/openapi/v1.json`, `/swagger`). Web dashboard types generated from it (`openapi-typescript` → `web/src/api-types.ts`). Snapshot committed at `src/Server/openapi.json`.
- **#5c** (`82d0f71`) — `LeaseSweeperService` (`BackgroundService`) runs hourly via `IServiceScopeFactory`, clears leases where `ExpiresAt < UtcNow`. Docker HEALTHCHECK added (`curl /health`).
- **#5d** (`ecf35b5`) — agent-ui toolchain bump (Vite 6→8, TS 5.8→6, react/types); `oxlint` added. New `.github/workflows/ci.yml`: three parallel jobs (build-dotnet, build-web, build-agent-ui) on every PR + main push.

---

## 2026-07-06 — Repo hygiene pass

See `logs/hygiene-2026-07-06.md` for the full plan + findings.

- **#1–3** (`2597cf1`, `14e3320`, `98d8f34`) — removed spent design prototypes; brought `.verify` tests + dev config into repo under `tests/`; docs refresh.
- **#4a** (`71f83ec`) — `MachineSavePaths` folded into EF (entity + `DbSet` + `AddMachineSavePaths` migration); raw SQL replaced with LINQ. Existing DBs adopted via migration stamp on startup.
- **#4b** (`bf67cc3`) — machine-key rotation guard: re-registering an existing name requires `X-Admin-Password` when a password is set. First-time registration stays open.

---

## 2026-06-26–27 — Lease heartbeat, installer artwork, bug fixes

- **Lease heartbeat** (`ee27a57`) — `SyncEngine` 3 h renew timer calls `POST /api/games/{id}/lease/renew`. Dashboard lease-conflict warning UI.
- **Installer artwork** (`21b0bb9`) — `WizardBg` (164×314) + `WizardSmall` (55×58) regenerated: `#1E252A` bg, logo centred, green separator, bold title.
- **Bug fixes** (`47f6a3b`, `d381f74`, `73e9100`, `8eae726`) — enrollment 401 (agent enrollment misrouted to admin group); stats/timezone mismatch; agent window black bars; art volume moved from `wwwroot/art/` to `/data/art/` (survives container updates).

---

## 2026-06-25 (session 4) — Hero downscaling, storage display, retention, version delete

**Commits:** `57cd313`, `6e146f3`, `8b65b54`

- **Hero downscaling** — `ImageSharp 3.1.7`; max 920 px wide, JPEG q85.
- **Storage display** — `GameStateDto.TotalStorageBytes`; dashboard shows per-game + grand total MB.
- **Per-game retention** — `Game.RetainVersions` (nullable); Configuration page "Save retention" card; `POST /games/{id}/retain`.
- **Manual version delete** — `DELETE /games/{id}/versions/{versionId}` (refuses head + open-conflict).

---

## 2026-06-25 (session 3) — Offline retry queue

**Commit:** `9baadf7`

- `OfflineQueue.cs` + `OfflineQueueDrainer.cs`. `SyncEngine.PushAsync` catches `HttpRequestException` and enqueues to `%PROGRAMDATA%\SaveLocker\offline-queue.json`. 30 s drain timer. Deduped by `GameId`; `force=true` sticky; retry count + last-attempt timestamp. Verified end-to-end.

---

## 2026-06-25 (session 2) — Admin password auth, favicon, git hygiene

**Commits:** `adb48c5`, `bfd608d`, `4f30d8d`

- **Admin auth** — `AdminPasswordFilter` + PBKDF2-SHA256 (100k iterations, salted). `GET /api/admin/status` (public). Route groups split: agent keeps `ApiKeyFilter`, dashboard uses `AdminPasswordFilter`. Set from ConfigView.
- **Favicon** — replaced broken set with full modern set: `.ico`, `32×32`, `16×16`, `apple-touch-icon`, PWA Android set + `site.webmanifest`.
- **Git hygiene** — removed 6 binary/stale files from tracking; added `src/Server/wwwroot/` to `.gitignore`.

---

## 2026-06-25 (session 1) — Cleanup, full user-visible rename, agent UI polish

- Deleted dead WinForms files (replaced by React agent UI).
- All remaining "LocalGameSync" user-visible strings → "SaveLocker".
- `installer/LocalGameSync.iss` → `installer/SaveLocker.iss`; branded wizard images.
- **Per-machine save paths** — `MachineSavePaths` table, SyncService CRUD, server endpoints, agent two-way sync, dashboard table. Verified on ThunderHorse + Wideboy.
- **Folder picker STA fix** — `ShowFolderPickerAsync` now spawns a dedicated STA thread; parents dialog to `Application.OpenForms[0]`.
- **Audit log view** — `GET /api/audit?limit=200`, `AuditView.tsx`, "Audit Log" nav tab.
- **Settings input clobber fix** — `dirtyFields` Set prevents the 10 s state poll from overwriting in-progress user input.

---

## 2026-06-24 — Agent UI revamp + CI/CD + SaveLocker branding

- **Agent UI** — replaced WinForms `AddGamesForm`/`SettingsForm` with React/WebView2 SPA (`agent-ui/`). `AgentApiServer.cs` (HttpListener :5178) + `AgentWindow.cs` (WinForms + WebView2). Three views: Overview, Add Games, Settings. Design tokens from SaveLocker handoff. MSBuild targets build + copy `agent-ui/dist/` on build and publish.
- **SaveLocker branding** — config dir, mutex, registry key, installer, tray/balloon text all renamed.
- **GitHub repo** created at https://github.com/SkorcherX/SaveLocker.
- **CI/CD** — `docker-publish.yml` builds + pushes `ghcr.io/skorcherx/savelocker:latest` on every `main` push. Watchtower on unRAID auto-deploys. Multi-stage Dockerfile bakes React `web/dist/` into `wwwroot/`.
- **React dashboard** (`web/`) — Vite 8, React 19, TypeScript, Tailwind v4. All API endpoints wired. Verified live against real DB. Dashboard at http://unraid-ip:5080.

---

## 2026-06-23 — Second machine (Wideboy) + real-world fixes

- Installed agent on Wideboy; diagnosed and fixed OneDrive `Directory.Move` access denied (file-by-file copy to `_tempDir`).
- `AgentLogger.cs` — rolling 1 MB log at `%PROGRAMDATA%\SaveLocker\agent.log`; `log` CLI sub-command to tail it.
- Dashboard auto-refresh collapse fix — `openDetails` Set preserves open panels across `render()`.

---

## 2026-06-22 — UX phase workstreams 2–5 + installer

- **WS2** — Steam VDF readers (`SteamVdf.cs`, `SteamTextVdf.cs`), `GameScanner.cs`, `scan` CLI, tray "Add games…" picker (`AddGamesForm.cs`).
- **WS3** — `ArtService.cs`; SteamGridDB search + fetch + cache; dashboard cover thumbnails. User-confirmed cover art rendering.
- **WS4** — `/api/machines`, enable/disable, set-latest; dashboard rebuilt with Machines table, initial-sync wizard, Set as Latest badge.
- **WS5** — `AgentCommand` queue; `GET /api/agent/commands` + result reporting; `CommandPoller.cs`; dashboard action buttons + command log. Verified end-to-end (dashboard Scan → agent ran it → Done).
- **Save-folder mapping** — `Game.SuggestedSaveDir`, `/save-dir` endpoint, agent reconcile auto-maps/backfills.
- **Machine deletion** — `DELETE /api/machines/{id}` with self-delete guard.
- **Inno Setup installer** — machine-wide, UAC, auto-start task, uninstall reverts Run key, asks about config dir.
- **Product name locked: SaveLocker.**

---

## 2026-06-21 — PoC complete (phases 0–5)

Built and verified end-to-end with real Octopath Traveler 0 saves. Phase 0: scaffold (3 projects). Phase 1: server (EF/SQLite, REST, lease/conflict, Dockerfile). Phase 2: Ludusavi manifest detection. Phase 3: agent (tray, CLI, watchers, sync engine). Phase 4: admin dashboard. Phase 5: hardening (atomic restore, retention, per-machine tokens). Tray WS1 first slice: Settings/Connect window, DPI fix, clipboard STA fix.
