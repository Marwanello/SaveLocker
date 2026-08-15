# Gotchas

Operating traps for Claude/an AI working this repo — not a history of bugs already shipped and
fixed. If it's a one-time incident with no recurring workflow risk, it belongs in `logs/`, not
here.

## Builds
- **Always `--no-incremental`.** Stale DLL reuse has masked changes before (new endpoints 404
  at runtime). Stop the running agent/server first — they lock the DLLs.
- **`dotnet build a.csproj b.csproj` does not build both** — the second is silently skipped.
  Build each project in its own invocation. If a fix "doesn't seem to apply," check the output
  DLL's timestamp before debugging the code.
- **The SDK is pinned in `global.json`** (`rollForward: latestFeature`) — `dotnet build` silently
  uses the newest installed SDK otherwise. Bump the pin and the Dockerfile's `sdk:`/`aspnet:` tags
  **together**, or the Docker build fails (loudly, by design).
- **`SQLitePCLRaw.bundle_e_sqlite3` is pinned to 3.x on purpose** (CVE-2025-6965 in the 2.x the
  EF Core package resolves by default). Do not "simplify" the pin away. Drop it only once EF Core
  resolves 3.x by itself.
- **`dotnet-ef` must match the EF Core major.** A stale global tool can write a migration the
  runtime rejects, and `migrations remove` as a recovery reflex can delete the *wrong* (already
  committed) migration. `dotnet tool update --global dotnet-ef --version "10.*"` first; check
  `git status src/Server/Migrations/` before and after any `migrations remove`.
- **PBKDF2 params (`Tokens.HashPassword`) are part of the on-disk format**, not recorded in it —
  changing iteration count/salt/hash size invalidates every stored password. `tests/verify-password-compat.ps1`
  guards this; keep a `v1` verification path if it ever moves.

## Paths
- **Dev storage is `localstate/`, never `data/`** — Windows is case-insensitive, so
  `src/Server/Data/` (source) and a runtime `data/` collide. `Remove-Item data -Recurse` once
  deleted `Entities.cs`/`AppDbContext.cs`.
- **`dotnet` may be missing from an open shell** right after a winget install — open a new shell,
  or prepend `"$env:ProgramFiles\dotnet"` to `$env:Path`.
- **The agent is a WinExe** — launching the installed `.exe` from a shell shows no stdout/stderr.
  Redirect to a file, or read `%PROGRAMDATA%\SaveLocker\agent.log` (`SaveLocker.Agent.exe log`
  tails it).
- **`npm run gen:api` in `agent-ui/` hardcodes `:5178`** — the port an *installed* agent already
  listens on. On the maintainer's own machine this silently regenerates types from the installed
  release build, not the dev build just compiled. Start a dev daemon on a free port (e.g. 5190)
  and generate against that; grep the output for a symbol you just added before trusting it. A
  running daemon also locks the build output DLLs (`MSB3027 … locked by ".NET Host (pid)"`).

- **The agent state directory is ACL-locked to whoever created it** (WA-03). `StateDirSecurity`
  severs inheritance and grants only the enrolling user, SYSTEM and Administrators. This applies to
  whatever directory `--config` points at, so a **test scratch directory gets locked too** — that is
  intended and asserted, but `Remove-Item` on it only works as the same account (or elevated). If a
  suite ever runs as a different user than the one that created `.verify-*`, clean up elevated.

## Ludusavi manifest
- **The manifest never writes `false` — absence IS the negative.** A `cloud:` block lists only the
  stores whose PCGamingWiki "Save game cloud syncing" row is ticked, and the whole block is omitted
  when none are. So "no `cloud:` block" and "a block that does not name `steam`" say exactly the
  same thing, and both mean *no Steam Cloud*. Reading a missing block as "unknown" would throw away
  38k games' worth of real data. The same shape applies to `tags:` and `when:`, in the opposite
  direction: absent there means "unspecified", i.e. **applies** — read absence as exclusion and
  thousands of good entries vanish (`ManifestLoader.IsWindowsSave` carries that note).
- **`ManifestLoader.GameCount` is smaller than the manifest's key count, by design.** The manifest
  holds entries differing only in case (`Afterlife` / `afterlife`); `Parse` keeps the first and drops
  58 of 52,973. If a count derived from the manifest is short by a few dozen, that is this, not a
  parse failure — confirm before hunting.
- **PCGamingWiki 403s automated fetches.** Read a page through the browser tools, not `curl`/WebFetch.

## Test-only environment variables
These exist because the production values are far too slow to observe in a suite. They are read by
`Agent.Core` and are **not** user settings — nothing in the UI or docs offers them.
- **`SAVELOCKER_LEASE_RENEW_SECONDS`** — lease renewal interval (production: 3 hours). WA-06's test
  needs several renewals inside a few seconds.
- **`SAVELOCKER_SYNC_LOCK_SECONDS`** — how long to wait for another process's game lock
  (production: settle gate + upload window + margin, ~13 minutes). WA-07's test would otherwise
  wait that out twice.
- **`SAVELOCKER_TRAY_PORT`** — moves the tray's local API off :5178 (Windows only). WA-09's block is
  the only one that drives a **real tray**, and it must not collide with the installed agent. It
  scopes the single-instance mutex to match (`SaveLocker.Agent.<port>`), so the harness tray and a
  running installed tray coexist; unset, both the port and the mutex name are exactly as shipped.
- **`SAVELOCKER_RUNKEY_SUBPATH`** — moves the HKCU "Start with Windows" subkey (Windows only).
  WA-10's access-denied case needs a **Deny ACE**, and putting one on the real
  `Software\Microsoft\Windows\CurrentVersion\Run` would break auto-start for everything on the box if
  the suite died before its cleanup. The harness points the agent at a throwaway key instead and
  deletes it afterwards.

Set them per-process and clear them afterwards — leaving either set in a shell will make later runs
behave in ways that look like bugs.

## Windows ACLs
- **`SetAccessRuleProtection(isProtected: true, preserveInheritance: true)` does not let you then
  strip the inherited rules.** The copies are materialised only when the descriptor is persisted, so
  `GetAccessRules(includeExplicit: true, includeInherited: false, …)` never sees them and the purge
  silently misses every one — `%PROGRAMDATA%`'s grant to Authenticated Users survives, and the
  directory *looks* protected (`AreAccessRulesProtected` is true) while still being world-readable.
  Pass `preserveInheritance: false` instead. Nothing is written until `SetAccessControl`, so there
  is no window with an empty ACL. Cost an hour and a confidently-passing wrong test.

## PowerShell / shell quoting
- **PowerShell escapes with a backtick, not a backslash.** `"...\$HOME..."` does not escape
  `$HOME` in PowerShell — it expands PowerShell's own `$HOME`, backslashes get eaten, and bash
  receives garbage. Pass WSL commands in a **single-quoted** PowerShell string, or write a `.sh`
  file and run it — never hand-quote a multi-token command inline.
- **`Invoke-RestMethod` does not unroll a JSON array the same way on 5.1 vs pwsh 7.** Windows
  PowerShell 5.1 hands back a JSON array as a single wrapped item — `.Count` reads 1, every filter
  matches nothing. Go through `ConvertFrom-Json` instead, which unrolls identically on both hosts.
  If a list-endpoint filter mysteriously matches zero rows, dump the raw payload before touching
  server code.
- **`$x.Count` on a single object can silently read a DTO field named `count`** (property lookup
  is case-insensitive). Force a real collection: `@($x | Where-Object {...}).Length`.
- **Under `$ErrorActionPreference="Stop"`, native stderr terminates the script** — an expected
  warning (e.g. a CONFLICT message) aborts a test run. Use `Continue` and parse output text.
- **`-ErrorAction SilentlyContinue` does not suppress a web cmdlet's failure.** `Invoke-RestMethod` /
  `Invoke-WebRequest` raise a *terminating* error on a refused connection or a 4xx, and `-ErrorAction`
  only governs non-terminating ones — so a call aimed at a server the script just stopped kills the
  run. It does not look like a crash: `finally` still prints the tally, so the suite reports a
  **lower pass count with zero failures**, and every section after the throw silently never ran.
  (Cost a confused re-run on 2026-08-15: 161 → "144 passed, 0 failed".) Wrap every such call in
  `try/catch` — which is what the suites' own `InstallerStatus`-style helpers already do — and check
  a suite's total against its recorded baseline, not just its failure count.
- **`Copy-Item -Recurse src dst` copies *into* dst when dst exists** rather than overwriting —
  always `Remove-Item -Recurse -Force $dst` first.
- **`VAR=x cmd` (bash) does not export `VAR`** to `cmd` unless exported — a prefix assignment
  applies only to a bare command, not to `A=1 out=$(cmd)`. `export` it, run, `unset`.
- **WSL inherits the Windows `PATH`**, full of unquoted spaces/parens — re-exporting it unquoted
  is a bash syntax error. Always quote: `export PATH="$DOTNET_ROOT:$HOME/.local/bin:$PATH"`.
  Driving WSL from Git Bash also mangles Linux paths — drive it from PowerShell, or set
  `MSYS_NO_PATHCONV=1`.
- **`git fetch /mnt/e/...` from WSL fails with "dubious ownership.**"
  `git config --global --add safe.directory /mnt/e/Projects/SaveLocker/.git` fixes fetching the
  source — but build/test must still run on ext4 (`~/SaveLocker`), never `/mnt/*`.

## WSL environment
- **`dotnet`/`pwsh` look uninstalled from a non-interactive shell** (`wsl -d ... -- bash -lc`) —
  only the interactive profile adds `~/.dotnet`/`~/.local/bin` to PATH. Export both explicitly
  first; don't conclude WSL is unprovisioned and fall back to Windows-only testing (which skips
  every Linux-specific behavior).
- **Shell scripts must be LF.** Copying a `.sh` from the Windows working tree (CRLF) straight into
  WSL gives bash `bad interpreter: ...^M`. `.gitattributes` pins `*.sh`/`*.service` to `eol=lf` for
  committed files — strip `\r` by hand (`sed -i 's/\r$//'`) when hand-copying for a local test.
- **`savelocker ui` needs an explicit RID** (`-r linux-x64 --no-self-contained`) in dev, or Silk
  reports `SdlPlatform - not applicable`, which reads like "no display" but means the native
  `libSDL2` never got mapped into `deps.json`. `tests/linux/run-ui-wslg.sh` already does this.
  Releases (self-contained publish) are unaffected.

## Web console
- **A bounded flex column SHRINKS its children instead of overflowing.** The console root is a fixed
  `height: 100vh` + `overflow: hidden` so the page does not scroll and its panes do — but the naive
  version of that collapsed ConfigView's cards to ~4 px each rather than producing a scrollbar. The
  fix is `> * { flex-shrink: 0 }`, which `.page-scroll` in `index.css` carries: **any new full-page
  scroller wants that class, not a bare `overflowY: auto`.** (Shipped v0.5.4; the symptom before it
  was a games sidebar with no bounded height, so picking a game far down the list pushed its
  settings off screen.)
- **Candidate selection is by ABSOLUTE index in both Add Games UIs.** `Enroller` indexes into the
  *unfiltered* list, so a filter that renumbers what it shows enrolls the wrong game. Applies to
  `agent-ui` and the Game Mode mirror in `Ui/UiApp.cs`.
- **`@import` must precede every other rule.** `@import "tailwindcss"` expands into generated CSS,
  so anything imported after it is silently discarded by the browser — the Google Fonts line sat
  there for months, the console rendered in fallback fonts, and the production build warned about it
  on every run. Keep plain `@import url(...)` lines **above** the Tailwind import.
- **The health API returns a row for every machine**, so a missing row is not how you detect "never
  reported" — test `lastHeartbeat == null`. Checking for the absent row made that state unreachable
  and newly enrolled agents rendered as "offline since —".
- **Hash routing has one parser** (`viewFromHash` in `App.tsx`), used at startup *and* on
  `hashchange`. A `hashchange` handler that knows about only some views leaves Back/Forward changing
  the address bar without changing the page.

## Testing
- **Clear the server DB and `.verify/` together**, never one alone — `run-agent-tests.ps1` reuses
  whatever state both sides hold. Clearing only one produces confident, unrelated-looking
  failures (stale save files vs. lost version lineage). Isolating just the server's `Storage__DbPath`
  counts as "cleared the DB half" — still hits the same trap if `.verify/` is stale.
- **Every Windows suite drives `src/Agent/bin/DEBUG`.** Build a branch in Release only and the
  suites run a stale binary — reporting green while testing none of your changes. This cost a full
  round of "all suites green" on PR #36: the Debug dll was three days old. `dotnet build
  SaveLocker.sln -c Debug` before believing a Windows suite result, and stop any test server first
  or the Server project fails on a locked `SaveLocker.Shared.dll`.
- **A harness can be structurally incapable of catching the bug it exists for.** `tests/detection`
  built its fixtures with an oracle that used the same fake `<storeUserId>` on both sides, so it
  passed a fix that did not work on real hardware; separately it dropped `tags` when re-serialising
  a game, silently disabling the very filter under test. Both were found by checking against a real
  machine, not by reading the harness. When a harness defines its own ground truth, ask what it
  would fail to notice.
- **`Storage__AgentInstallerRoot` must be set too, and forgetting it only breaks Linux.**
  `AgentInstallerService` creates its root in its **constructor**, and the default resolves to
  `/data/agent-installer`, which a normal Linux user cannot create. The server then dies at startup
  *after* migrations have run — so `server.log` reads like a healthy boot right up to an unhandled
  `UnauthorizedAccessException`, and every server-dependent check fails as though the agent were
  broken. This is what made `run-linux-tests.sh` score 24/40 with 16 cascading failures; setting the
  variable took it to 40/40 with no code change. Windows never sees it, because there the same
  default happily creates `E:\data\`.
- **Give every test server its own `Storage__DbPath`/`Storage__ArchiveRoot`.** A dirty dev DB
  fails `run-enrollment-tests.ps1` in a way that reads like broken enrollment code (12/16
  failures starting at "mint returns a raw token").
- **Running the server DLL directly ignores `launchSettings.json`** — binds `:5000` in
  Production config instead of `:5179`/Development. A script that starts its own server must pass
  `ASPNETCORE_URLS` and `ASPNETCORE_ENVIRONMENT`/`Storage__*` explicitly; a test that restarts a
  server must own it (its own port + state dir).
- **The readiness probe must hit an unauthenticated route** — `/api/games` 401s without an API
  key, so a naive `curl -sf` loop burns its whole timeout. Use `/api/admin/status`.
- **A leaked test server (thrown before its `finally`) poisons every later run** — it keeps the
  port and state dir, the next run's cleanup fails silently, and its probe succeeds against the
  *stale* server. Prefer failing loudly (refuse to start if the port's in use or the state dir
  won't clear) over asserting against someone else's server.
- **A test whose setup silently fails passes vacuously.** Wrap only the truly optional step in
  `try/catch`; give every "this must succeed" fixture step its own assertion. When a bug is fixed,
  **revert the fix and confirm the test actually fails** — that's the only proof it tests anything.
- **A green test may never enter the state where the bug lives.** "No admin password set,"
  "nothing running," "first run," "empty directory" are the easy setups a harness defaults to —
  and exactly the states real bugs hide outside of. Ask what state the suite never creates.
- **A hand-rolled `HttpListener` stub is not a server.** A single-threaded accept loop is only
  listening while it is inside `GetContextAsync`; a request that arrives in the gap between
  iterations — or while the script is doing anything else between two pump calls — is never
  accepted, and the agent's HTTP call hangs there indefinitely with no error anywhere. WA-12's block
  lost an hour to this: the tray's lease POST never returned, so the launch handler never ran, and it
  read exactly like a broken process watcher. Use the suite's real server and real state (a lease
  genuinely held by a second registered machine) unless the point of the test *is* a malformed or
  hostile response, as in WA-05.
- **A concurrency test racing identical short-lived processes proves nothing** — process startup
  dominates and write windows never overlap. The discriminating shape is a long-lived process
  holding stale in-memory state vs. a short-lived one; order by waiting on observable state, not
  by hoping for a race.

## ImGui / Deck UI (`Ui/*`, `savelocker ui`)
- Drive gamepad nav from **`ButtonDown` events**, edge-triggered and queued — feeding held-button
  state every frame causes runaway auto-repeat; naive per-frame `.Pressed` polling drops fast taps.
- **Silk's per-frame `.Pressed` poll stays false unless something subscribes to `ButtonDown`/`ButtonUp`** —
  subscribe (even a no-op handler) or the poll is dead, even with a gamepad correctly detected.
- **ImGui.NET's binding omits the `imgui_internal` API** its own native `libcimgui`/`cimgui.dll`
  already exports (`igSetFocusID`, `igSetNavID`, `igFocusWindow`, …) — `[DllImport("cimgui")]`
  reaches them directly rather than needing a workaround. `SetKeyboardFocusHere` is tab-scoped and
  cannot move focus out of a different child window; use `igSetFocusID` instead.
- **`NavFlattened` does not support a scrolling child** (upstream-documented) — symptom is nav
  that works on some screens and not others, which reads like several unrelated bugs.
- **A child window needs `Border` or `AlwaysUseWindowPadding`**, or `WindowPadding` is silently
  zero and content drawn outside a widget's rect (e.g. focus rings) gets clipped at the edge.
- **`style.Alpha` only reaches ImGui's own widgets**, not anything drawn straight onto an
  `ImDrawList` with a packed color. Hand-painted widgets must route through a helper
  (`Widgets.U32`) that multiplies by `ImGui.GetStyle().Alpha`, or fades do nothing to them.
- **`SDL_GetError()` is sticky** — never cleared on success. A void SDL call that checks the
  error string afterward (e.g. `SDL_GL_GetDrawableSize`) can surface a stale, unrelated error as a
  fatal exception. Clear it every frame before use.
- **Don't upgrade ImGui.NET expecting a nav fix** — 1.91.6.1 was tried and reverted 2026-07-25;
  it didn't fix dead-Left and broke the working Right-cross. The nav fix lives in the internal-API
  binding above, on 1.90.8.1, and needs no newer package.
- **Hover and focus paint the same ring, so hover must be gated on `Widgets.PointerDrives`.** A Deck
  has a pointer whether or not anyone touched the trackpad — gamescope always supplies a position —
  and an ungated `IsItemHovered()` paints a SECOND selector, on a different pane from the real one,
  that the D-pad cannot move and A does not activate. It presents as "two things highlighted and A
  does nothing", not as a mouse problem, and it is only as reproducible as where the pointer happens
  to rest. Any new focusable widget must go through `Widgets.Hot()`.
- **Mouse position must be injected BEFORE `_controller.Update()`**, which is what calls `NewFrame` —
  and `NewFrame` is where `HoveredWindow` is resolved. Assigning `io.MousePos` after it still moves
  the hit-test rect, so it looks like it works, but no CHILD window will ever report hover: hover
  goes silently dead everywhere and an A/B comes back identical for the wrong reason.
- **A parked pointer still produces a non-zero `MouseDelta` every frame.** The injection races Silk,
  which writes the real position during the same `Update`, so the resolved position alternates. Read
  naively, the harness itself looks like frantic mouse movement and claims the cursor — the test
  measures the harness, not the code. `--pointer` skips the movement test entirely for this reason.

## Linux agent
- **`Environment.ProcessPath` is the *dotnet host* under `dotnet savelocker.dll`.** Any code that
  answers "where am I installed?" must use `AppContext.BaseDirectory` instead. This is not
  theoretical: `SystemdAutoStart` used `ProcessPath` and wrote `ExecStart=/…/dotnet daemon` — a unit
  that starts nothing — whenever it was run from a build tree. The installed agent is a
  self-contained apphost, so the two agree there and the fault is invisible on a real Deck.
- **`systemctl --user stop` kills the unit's whole cgroup.** An updater (or anything else) spawned
  by the daemon dies together with the unit it just stopped. That is why the update swap runs from
  `ExecStartPre` of the *next* invocation rather than from the daemon, and why `savelocker update`
  can restart the service safely — it runs in the user's shell session, not in the unit.
- **The Linux install prefix IS the state directory** (`~/.local/share/SaveLocker`), so
  `config.json` — this machine's server API key — sits inside the tree an update replaces. Anything
  that "replaces the install" must copy file-by-file, never swap or rename the directory.

## Test harness
- **`run-linux-tests.sh` reassigns `HOME` to the fixture tree.** So `"$HOME/.dotnet"` inside a check
  resolves into the fake home and finds nothing — derive a real `DOTNET_ROOT` from
  `command -v dotnet`. Only the framework-dependent apphost needs it; a released agent is published
  self-contained. Cost an hour: without it the launch wrapper never started, the "is a game running?"
  scan correctly found nothing, and the check it gated passed while testing nothing at all.
- **Assert on text only one outcome can produce.** `contains "$out" "installed"` also matched the
  rollback message *"was installed but never started successfully"* — so a run that had just UNDONE
  an install reported a successful one, and passed. Likewise an unanchored `grep ProtectHome`
  matched the comment explaining why `ProtectHome` is absent.
- **Never `rsync` the Windows working tree into the WSL clone to run `run-linux-tests.sh`.** The
  Windows tree is CRLF, and `tests/linux/slow-game.sh` with CRLF line endings misbehaves: the game
  exits with the wrong code and stops writing early, failing 6 checks across the launch wrapper, the
  settle gate and the `/proc` lock probe. They read as real wrapper bugs and are not. Copy the files
  you changed through `sed 's/\r$//'` instead, into a checkout reset to `origin/main`. Take the
  baseline on pristine `main` first — the numbers only mean something as a pair.
- **Start the dev server with `ASPNETCORE_ENVIRONMENT=Development`**, or it never reads
  `appsettings.Development.json` and opens `/data/savelocker.db` (i.e. `E:\data\...`) instead of
  `src/Server/localstate`. That stray DB carries a stale `__EFMigrationsLock`, so the server hangs
  in migration forever, and every server-dependent check in `run-agent-tests.ps1` fails as though
  the agent were broken. `cd src/Server && dotnet run` gets this right; running the built DLL
  directly does not.
- **`run-agent-tests.ps1` is not idempotent against a dirty server DB.** It uses fixed game names
  (`SyncGame`), so a second run inherits the first run's versions and conflicts and fails ~16
  checks. Point the server at a throwaway `Storage__DbPath` for a clean run.

## Vault hygiene
- **A vault doc can point at a task file, or a version, that no longer exists.** Check the
  filesystem/`git tag -l` before trusting a pointer in `CONTEXT.md`/`Backlog.md` — this vault has
  drifted before (a deleted task still listed as next action; a stale version number).
- **A `Docs:` commit runs no CI, by design.** `ci.yml` and `docker-publish.yml` both carry the same
  `paths-ignore` (`SaveLocker/**`, `CLAUDE.md`, `AGENTS.md`, `.agents/**`), so a vault-only push is
  skipped by both — a green tick is *absent*, not failing. Two things follow. **Never widen it to
  `'**.md'`:** `web/src/releases/*.md` are bundled into the console image and `web/src/help/*.md`
  are the Help KB, so both are code as far as the build is concerned. And **Actions does not expand
  YAML anchors**, so `ci.yml`'s two copies (one per trigger) must be edited together. If any CI job
  is ever made a *required* status check, the `pull_request` copy will block docs-only PRs forever
  — the comment in `ci.yml` says what to do about it.
