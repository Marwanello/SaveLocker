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

- **A test build must not carry a RELEASE's numeric version, or it can never be updated and cannot
  be told apart from the real thing.** `build-linux.sh` stamps `AssemblyFileVersion` with the numeric
  part only (`${version%%-*}` — the attribute rejects a `-suffix`), so a tarball built as
  `0.5.6-deckytest` reports plain `0.5.6`: identical to the release in every heartbeat and in the
  console. `UpdateChecker` then compares with `System.Version` and `0.5.6 <= 0.5.6` is **up to date,
  forever** — the machine is pinned to the test build and the update channel says nothing is wrong.
  Cost one Deck a manual reinstall (2026-08-15). This is exactly why CI stamps **`9.9.9-ci`**: the
  numeric part cannot collide with a real release, and `versionSkew.ts` reads a newer-than-console
  version as a TEST BUILD. Use a 9.9.9 variant for any hand-built package.

- **A dev build's version number is not the code's version, and the two hosts lie differently.**
  With no git tag reachable, MinVer falls back to `MinVerMinimumMajorMinor` and the Windows agent
  stamps **0.1.0**; `Agent.Linux.csproj` carries **no MinVer reference at all**, so it stamps .NET's
  default **1.0.0**. Neither number says anything about the code, and both are older than any real
  release, so an agent pointed at a populated update channel would be offered an "update" that
  reinstalls over it. **A fork is the way into this state**: tags do not follow a fork on GitHub, so
  `git tag -l` reads empty until `git fetch upstream --tags` is run once. Check that before believing
  a version number a local build reports.
  <br>**`-p:Version=` does not fix the Windows one** — MinVer assigns Version/FileVersion/
  AssemblyVersion in a target that runs later and overwrites it. Use MinVer's own escape hatch,
  `MinVerVersionOverride` (an env var), exactly as `installer/build-installer.ps1` does. On Linux
  there is no MinVer to overwrite anything, so `-p:Version=` is the right lever there.
  <br>**Do not pass an `InformationalVersion` ending in `+`.** The SDK appends the source revision
  itself, and joins with a **dot** rather than a second `+` when the value already carries build
  metadata — `0.5.10-test+.55ddde9…`, which renders the commit as `.55ddde`. Pass `Version` alone and
  let the SDK add the hash (`UpdateChecker.ResolveBuildLabel` trims the separator anyway).
- **`UpdateChecker` publishes two version strings and they are not interchangeable.**
  `CurrentVersionText` is Major.Minor.Patch, is what the heartbeat sends, and is what the console
  compares **literally** — a suffix there reads as fleet version skew. `BuildLabel` is display-only
  (`0.5.10-test (55ddde9)`, from `AssemblyInformationalVersion`) and is what the agent UI header
  shows, because several builds share one version number and on a machine running a test build beside
  the installed one that is the whole question. A release has no prerelease suffix and falls back to
  the plain number, so nothing about a shipped build's screens changes. Never compare `BuildLabel`.

## Paths
- **Dev storage is `localstate/`, never `data/`** — Windows is case-insensitive, so
  `src/Server/Data/` (source) and a runtime `data/` collide. `Remove-Item data -Recurse` once
  deleted `Entities.cs`/`AppDbContext.cs`.
- **The agent is a WinExe** — launching the installed `.exe` from a shell shows no stdout/stderr.
  Redirect to a file, or read `%PROGRAMDATA%\SaveLocker\agent.log` (`SaveLocker.Agent.exe log`
  tails it).
- **`npm run gen:api` in `agent-ui/` hardcodes `:5178`** — the port an *installed* agent already
  listens on. On the maintainer's own machine this silently regenerates types from the installed
  release build, not the dev build just compiled. Start a dev daemon on a free port (e.g. 5190)
  and generate against that; grep the output for a symbol you just added before trusting it. A
  running daemon also locks the build output DLLs (`MSB3027 … locked by ".NET Host (pid)"`).
  <br>**The generated file is port-independent, so you never have to stop the installed agent.**
  `openapi-typescript` emits types only — no `servers` block, no URL — so running it by hand against
  another port produces a file byte-identical to what the `:5178` script would write, and CI's
  `gen:api -- --check` is satisfied. Verified 2026-08-15 by generating from a Linux daemon on 5186
  (in WSL, reachable from Windows through WSL2 localhost forwarding) and diffing: the only change
  was the route being added. `npx openapi-typescript http://localhost:<port>/openapi/v1.json -o src/api-types.ts`.

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
- **`SAVELOCKER_STATE_ROOT`** — moves the whole agent state root, i.e. what `AgentConfig.DefaultDir`
  is built from. **`--config` alone is not enough to run a second agent on Windows**: it moves
  `config.json` and everything derived from `StateDir`, but `agent.log`, the WebView2 profile
  directory and a fresh config's default `ManifestCachePath` are all built from `DefaultDir` and stay
  in the installed agent's folder — so a test tray's log interleaves with the real one's, and the
  1 MB rotation is a `File.Move` under a process-local lock that the other process cannot see (its
  writes are swallowed, silently). Linux already had this in `XDG_DATA_HOME`; Windows had nothing.
  Must be a **rooted** path or it is ignored — a relative one would resolve against whatever
  directory the process started in, which for a tray launched from Explorer is not knowable. Setting
  it makes `--config` optional: `DefaultConfigPath` moves with it.
- **`SAVELOCKER_RUNKEY_SUBPATH`** — moves the HKCU "Start with Windows" subkey (Windows only).
  WA-10's access-denied case needs a **Deny ACE**, and putting one on the real
  `Software\Microsoft\Windows\CurrentVersion\Run` would break auto-start for everything on the box if
  the suite died before its cleanup. The harness points the agent at a throwaway key instead and
  deletes it afterwards.

Set them per-process and clear them afterwards — leaving either set in a shell will make later runs
behave in ways that look like bugs.

## `tests/testenv.ps1` (throwaway test rig)
- **`build` (any `-Only` target) never fetches or checks out anything itself — it builds whatever
  commit the WSL clone already has.** `sync` is a separate command that does the fetch+checkout (see
  *Sync the WSL clone from `git status`* below); running `build -Only deck` right after switching
  branches on Windows, without an intervening `sync`, silently ships a stale build with no warning —
  there is nothing in `build` that compares the WSL clone's commit against anything. Confirmed
  2026-08-31: a `doctor` run against exactly this sequence was missing an entire feature that was
  very much present in the branch being "tested." Always `sync` before `build` when the branch under
  test just changed.
- **The Steam Deck target has no default and must be opted into** — `$env:SAVELOCKER_DECK_HOST` /
  `$env:SAVELOCKER_DECK_SERVER_URL`, unlike every other target here which has a workable Windows/WSL
  default. Leaving `SAVELOCKER_DECK_HOST` unset is the normal state for a session not touching the
  Deck and silently skips it under `-Only all`; only an explicit `-Only deck` with it unset is an
  error. Don't "fix" the skip by inventing a default IP — every LAN differs and a wrong guess would
  silently SSH nowhere useful, or somewhere unintended.
- **The Playnite target works the same way — no default, opt in with `-PlaynitePath` /
  `$env:SAVELOCKER_PLAYNITE_PATH`** (the folder containing `Playnite.DesktopApp.exe`; a portable
  `.7z` extraction is recommended over your real, everyday install). Unlike the Decky plugin, `build`
  produces no separate rewritten "stage" — the Playnite plugin's agent URL and state directory are
  ordinary settings (Add-ons → SaveLocker → Settings inside Playnite itself), not literals baked in
  at build time, so `up` installs straight from the plugin repo's own `src\bin\Release\net462`
  output. `-PlaynitePluginRepo` / `$env:SAVELOCKER_PLAYNITE_PLUGIN_REPO` overrides the sibling-repo
  guess (`Get-PlaynitePluginRepo`), same convention and same reasoning as `-DeckyPluginRepo`.
- **The Deck is only awake when the maintainer wakes it** (CONTEXT.md). Every SSH/`scp` call the
  rig makes carries `-o ConnectTimeout=5` and every caller wraps it in try/catch, reporting
  "unreachable" rather than hanging on the OS default TCP timeout or aborting `up`/`down`/`status`
  for the Windows and WSL targets too. A thrown, uncaught SSH failure here is a bug in the script,
  not a correct response to a sleeping Deck.
- **The Deck build must be self-contained** (`build -Only deck`) — SteamOS ships no .NET runtime,
  so the WSL target's own framework-dependent `dotnet build` would need a `dotnet` on the Deck that
  does not exist. It reuses `packaging/linux/build-linux.sh` (the same packer a real release uses)
  from inside WSL — cross-publishing `-r linux-x64 --self-contained` from Windows directly also
  works (verified 2026-08-19: restores and runs fine), but WSL is where `agent-ui/dist` is already
  staged for the other Linux target, so there is no reason to open a second path.
- **The Deck's install directory is never `~/.local/share/SaveLocker`.** That path is both the real
  agent's binaries *and* its state (see *Linux agent* below), so a test build there would overwrite
  the real install. The rig uses `~/savelocker-test` for the binary and `~/savelocker-test-state` as
  `XDG_DATA_HOME` (so real state lands at `~/savelocker-test-state/SaveLocker`), and every remote
  `rm -rf` in `testenv-deck.sh` first asserts its target path contains `savelocker-test` before
  touching anything, in case a mistyped `-DeckPrefix` ever resolved to something like `$HOME`.
- **The Deck test agent's UI is never reachable at `http://<deck-ip>:<port>` from another machine —
  not a firewall/setup gap, a hard-coded refusal.** `AgentApiServer.Start` binds Kestrel with
  `ListenLocalhost`, unconditionally, for every host including this test build: "binding it to a
  LAN interface would expose [machine control] to the whole network." Decisions.md records a
  `--lan` flag that existed once specifically to relax this and was withdrawn. The sanctioned route
  is an SSH tunnel (`ssh -L <port>:localhost:<port> user@host`, then browse `localhost:<port>` on
  the near end) — `Get-DeckUiHint` in testenv.ps1 prints exactly that. An earlier version of this
  printed a bare `http://<deck-ip>:<port>` as if it would just work; it never would have.
- **A `setsid ... & disown` daemon does NOT survive its launching SSH command returning, on a real
  systemd Deck** — found and fixed 2026-08-19 running `up` against real hardware for the first time.
  It answered the one check `up` makes, then was gone by the next `status`, with its own log showing
  a clean start and a served request — killed from outside, not a startup failure. The cause:
  `logind`'s `KillUserProcesses` tracks membership by **cgroup**, not POSIX session ID, and `setsid`
  only changes the latter — the process stays in the SSH login session's cgroup and dies with it.
  `nohup`/`disown` do not help either; they guard against SIGHUP from a closing terminal, a different
  mechanism. This never showed up on the WSL target because WSL processes aren't cgroup-scoped to a
  particular `wsl.exe` invocation the same way. The fix (`testenv-deck.sh`'s `cmd_up`):
  `systemd-run --user --scope --unit=<name> -- <daemon command>` hands the process to the Deck's own
  `user@<uid>.service` manager — already running from the local desktop login — instead of the SSH
  session's transient one, and it survives that session closing. Confirmed empirically (started a
  `sleep`, checked from a second SSH connection after the first one closed) before trusting it for
  the real daemon. `cmd_down` stops the scope directly (`systemctl --user stop <unit>.scope`) rather
  than relying only on `pkill` finding the process, so a leftover scope can never make the next `up`
  fail with "Unit already exists".
- **A background daemon's token file must be re-read on every poll iteration, not once before the
  loop.** `cmd_up` in both `testenv.sh` and `testenv-deck.sh` backgrounds the daemon, then reads
  `api-token` to auth the readiness check — reading it exactly once, immediately after backgrounding,
  races the daemon still writing that file: "No such file or directory" even though the daemon comes
  up fine a moment later. Windows's `Get-WinAgentState` already re-reads its token fresh on every
  poll; the bash side didn't, until this was traced to ground on 2026-08-19. Re-read inside the
  retry loop instead. **A plain `2>/dev/null` on the reading command does not suppress the error
  either** — a failed `<` redirect is raised by the shell itself, before the redirected command ever
  runs, so it needs the whole `{ tr ...; } 2>/dev/null` group form, confirmed by reproducing both
  ways side by side.
- **`grep` in a pipeline under `set -o pipefail` exits 1 on zero matches, and that becomes the whole
  function's (and caller's) exit code if it's the pipeline's last stage.** `testenv-deck.sh`'s
  `show_real_game_mappings` — used from `cmd_status` and `cmd_up` — reported the entirely normal "no
  game maps a real folder" case as the SSH command having failed, because nothing matched the
  `grep -o` feeding the warning loop. Needs `|| true` after the pipeline.
- **`sync` used to skip a NEW directory, a deleted file, and a renamed one — fixed 2026-09-20.** It built
  its file list from plain `git status --porcelain`, which reports an untracked directory as ONE entry
  (`?? agent-ui/src/components/ui/`); `testenv.sh`'s copy loop only takes regular files, so it printed
  `skip (gone): agent-ui/src/components/ui/` and the WSL clone built without any file in it (a compile
  error on the missing imports, or a silently stale tree). The same list also lost every deletion
  (`Test-Path` filtered it out, and the clone is first checked out at the COMMITTED tree, so the file
  lived on there) and every staged rename (`R  old -> new` matches no path — `Test-Path` even threw
  "Illegal characters in path" on the `>`). Now `git status --porcelain=v1 -z --untracked-files=all`:
  every untracked file is listed on its own, nothing is quoted (`"a b"` was), and a rename is two
  tokens. Each file is classified by what is on disk — present → `M<TAB>path` (copied), absent →
  `D<TAB>path` (removed from the clone). Reproduced with the old script and fixed on the same change:
  a nested new directory, a file with a space in its name, a deleted tracked file and a `git mv`.
  **Still true:** a file that was copied in while UNTRACKED and is later deleted on the Windows side
  without ever being committed survives in the clone — `checkout --force` does not remove untracked
  files and `git status` no longer mentions it (four files — `Toast.tsx` and `toast.ts` under both
  `agent-ui/src` and `web/src` — sat untracked in the clone on 2026-09-20 while existing in no tree and no
  commit on any branch). `rm` it in the clone by hand; a blanket `git clean` was not added because
  nothing proves the clone holds nothing un-ignored that it needs.
- **`conflict -Wsl` on its own seeds no conflict.** It creates the game and pushes once from WSL — the
  rig prints "only seeding WSL" — and by then the game has a head, so a following `-Windows -Wsl` puts
  the divergence on the wrong machine. Recovering takes a full `clean` and a rebuild. Start with
  `conflict -Windows -Wsl` on a clean rig whenever the agent under test is the WSL one: an agent only
  shows the conflicts it is itself a party to (`/api/conflicts` filters on `MachineId`).
- **The Windows test agent can be mapped to REAL save folders.** After a run against a console that
  already held games, `up` printed `the test agent maps 'Cyberpunk 2077' to a real folder` for eight of
  the maintainer's own games. Anything that syncs the whole fleet from that agent — **Sync all**, the
  tray's Sync All — is a pull from the throwaway console over real saves. Click through on the WSL agent
  (its games have no local folder, so nothing can be touched) or on a rig that has been `clean`ed first.
- **The test console container cannot reach a server bound to the host's loopback, and
  `host.docker.internal` has an IPv6 address it cannot route.** A stub SteamGridDB for the console
  (`tests/sgdb-stub.py`, via `-ConsoleEnv`) therefore listens on `0.0.0.0` and is addressed by that
  name. Docker Desktop lists both `192.168.65.254` and an `fdc4:…` address for it; a burst of parallel
  fetches occasionally tries the IPv6 one first and logs `Network is unreachable` (measured: 2 of 10
  picker previews on one load, 0 of 15 on three re-fetches). Harmless, and the picker shows a clickable
  "no preview" tile — but it is not a product bug, so do not chase it there. Port 5217 for the manual
  stub: 5216 is the security suite's own and it refuses to start if it is taken.

## Windows ACLs
- **`SetAccessRuleProtection(isProtected: true, preserveInheritance: true)` does not let you then
  strip the inherited rules.** The copies are materialised only when the descriptor is persisted, so
  `GetAccessRules(includeExplicit: true, includeInherited: false, …)` never sees them and the purge
  silently misses every one — `%PROGRAMDATA%`'s grant to Authenticated Users survives, and the
  directory *looks* protected (`AreAccessRulesProtected` is true) while still being world-readable.
  Pass `preserveInheritance: false` instead. Nothing is written until `SetAccessControl`, so there
  is no window with an empty ACL. Cost an hour and a confidently-passing wrong test.

## PowerShell / shell quoting
- **A non-ASCII character inside a *string literal* in a BOM-less `.ps1` breaks Windows PowerShell 5.1.** It reads the file
  as CP-1252, and the last byte of an em dash (`—`, `E2 80 94`) is `0x94`, which 5.1 treats as a closing curly quote — so
  the parse error points at a *different* line, several checks later. Comments are safe (the tests already carry `—` in
  them); string literals in code stay ASCII. CI runs `pwsh`, which reads UTF-8 correctly, so it only bites locally.
- **A function named after a built-in alias silently never runs.** In a test script, `function Pwd`
  was shadowed by the `pwd` alias (`Get-Location`) — aliases outrank functions — so every call
  failed with "positional parameter cannot be found", and because the abort left the pass count
  untouched the run still ended `0 failed`, exit 0. Avoid `Pwd`, `Cmd`, `Sort`, `Select`, `Where`,
  `Group`, `Sleep`, `Cd`, `Ls`… as helper names, and give a script's `try` a `catch` that FAILS the
  run: an aborted suite must never look like a passing one.
- **Windows PowerShell 5.1 strips the inner double quotes from a native command's argument.**
  `wsl ... python3 -c "print('ok')"` reaches bash as `print('ok')` unquoted and dies on the
  parenthesis — and a probe written that way reports "WSL is not usable" when it is fine. Probe with
  `python3 --version`, or write the code to a file (as `Invoke-Sqlite` in the security suite does).
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
- **An UNLAYERED CSS rule beats every Tailwind utility, whatever its specificity.** Tailwind v4 puts
  utilities in `@layer utilities`, and unlayered CSS always outranks layered CSS. So a plain
  `button:hover { opacity: .85 }` in `index.css` silently won over `disabled:opacity-50` (a disabled
  button jumped from 50% to 85% on hover, looking enabled), over `hover:opacity-100`, and a plain
  `input:focus { outline: none }` reduced the `focus-visible:outline-*` ring to nothing — three
  findings, one cause. Global element rules belong inside `@layer base`. The same rule is why the old
  `* { padding: 0 }` reset made every Tailwind padding/margin class a no-op until Group 1 layered it.
  **Related: a CSS animation with `animation-fill-mode: both` holds its FINAL keyframe forever and
  beats any normal declaration on the same element** — including an inline `style`. `animate-pop`
  (`opacity` 0→1) therefore erased the `opacity: .55` on a disabled game's tile; dim an inner wrapper,
  never the element that carries the entrance.
- **A view must take every colour from the tokens — no `#hex` in a `.tsx`.** Group 5 flipped the theme to follow the
  OS *because* the last hardcoded colours had gone; before that, the unmigrated views (game detail, configuration,
  audit log, agent updates: ~275 hex colours in inline styles) painted dark cards but inherited their TEXT colour from
  `<body>`, so every visitor whose OS prefers light got near-black text on them (measured 1.04–1.15:1 — invisible).
  One hardcoded colour is enough to bring that back for one widget, and `run-appearance-consistency-tests.ps1` now fails
  on it. Also: **never dim text with `opacity`** (a 0.65 on a count badge was fine on dark and 2.6:1 on light — use a
  token), and **content text is `--color-dim`, not `--color-faint`** (3.1–3.6:1 — only uppercase eyebrow labels).
  **Measure, don't eyeball:** walk every text
  node, resolve its effective foreground/background (a canvas turns `color-mix()` / `oklab()` results
  into RGB) and compute the contrast ratio — and turn transitions off first: on a hidden pane a
  `background-color` transition freezes at its old value while `color` (not transitioned) updates, which
  produced a convincing 1.01:1 "everything is invisible" false alarm. Do it in **both** schemes and, since Group 5,
  for **each accent** (the pane's `resize_window` `colorScheme` emulates the OS preference; **reload after switching** —
  see the next item).
- **`prefers-color-scheme` `change` events never fire in a hidden Browser pane.** Media-query change events are
  dispatched in the rendering step, which a minimised or hidden window does not run (screenshots time out for the same
  reason). `matchMedia().matches` flips, but neither the app's listener nor a control listener of your own is called, so a
  System-theme page keeps the accent it derived for the OLD scheme (Cobalt's light `#3a5cbe` on a dark page — a convincing
  "the accent is stale after an OS flip" that is not a bug). Reloading re-derives correctly; to test the handler itself,
  wrap `MediaQueryList.prototype.addEventListener` to capture the callback, flip the scheme, and call it. A visible
  window is expected to deliver the event (the spec queues it in the rendering step); **that was not observed** — no
  visible pane was available.
- **The dashboard's CSP forbids inline script and any third-party origin.** See `Decisions.md`. A new
  `<script>` in `index.html`, a CDN font/stylesheet, a remote image in a help article, or a
  `fetch` to another origin will be blocked in production while working perfectly under `npm run dev`
  (Vite serves no CSP). Check a change like that against the container, not the dev server.
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

- **A credential cannot be verified through a URL a CDN will cache.** SteamGridDB sits behind
  Cloudflare, which answers a repeated URL from the edge **without the origin ever seeing the
  `Authorization` header** — so `grids/game/13136?limit=1` came back `200` with `"success":true` for
  a key of deliberate nonsense, `cf-cache-status: HIT`, `age: 37422`: a ten-hour-old copy of
  somebody's valid-key response. Picking an *authenticated* endpoint is necessary and **not
  sufficient**; the request must also miss the cache, so `VerifyCandidateKeyAsync` appends a unique
  `&_=<guid>`. Two things that do **not** work: reading the body's `success` field (the cached body
  says `true`) and sending `Cache-Control: no-cache` (the edge ignores a request directive). This is
  the same bug CS-09 fixed, returning by a different route — the first version verified against an
  endpoint needing no key at all, this one against a response nobody re-fetched. Symptom either way:
  a typo silently replaces a working key. **Art fetches keep their cacheable URLs on purpose** — a
  cached image is the point there.
- **Never draw a big image small — ask the server for it at the size you draw.** A 600×900 cover in a
  38 px tile is a ~16× shrink, and a browser does that with a cheap filter that reads only a few source
  pixels: the result is jagged, shimmering diagonals and broken text (reproduced with a synthetic cover
  of 1 px lines beside a Lanczos-resampled copy — the copy was clean, the original was not). No CSS
  fixes it. `web/src/art.ts` builds `?w=` URLs and a `srcSet`; the server (`ArtThumbnails`) resamples
  once, in linear light, and caches it. Widths outside its allowlist are IGNORED and return the
  full-size original, so a typo in a width silently brings the aliasing back — keep `ART_WIDTHS` and
  `ArtThumbnails.Widths` identical. Also: `loading="lazy"` images in a tab that is not painted never
  load, so a check that reads `naturalWidth` from a hidden pane sees 0×0 (paint it first).
- **ImageSharp's `Image.Identify(...).PixelType.AlphaRepresentation` is not a transparency test.**
  An RGBA PNG with a single fully clear pixel identified as having no alpha, so the first "prefer an
  opaque icon" kept the transparent one. Decode and read the pixels (`ArtImages.IsFullyOpaque`); only
  JPEG can skip that. Caught by the suite's stub serving one 8×8 icon with a clear corner first.
- **ImageSharp 3.x's format errors are NOT `ImageProcessingException`s.** `UnknownImageFormatException` and
  `InvalidImageContentException` derive from `ImageFormatException`, a sibling type, so
  `catch (Exception ex) when (ex is ImageProcessingException ...)` lets the two most common failures on
  untrusted bytes straight through (measured on 3.1.12 with a probe project: HTML, a valid-magic PNG with
  junk after it, an .ico). It read as covered for a whole PR: every test image was a real one, and a
  sniff on the magic number kept most junk away from ImageSharp. Use `ArtImages.IsDecodeFailure`. Also
  `Image.Identify(new byte[0])` throws `ArgumentNullException`, not a format error, so an empty body needs
  its own guard. A PNG header plus junk is now refused at `StoreAsync` and the picker preview, not stored.
- **Regenerating `src/Server/openapi.json` from a scratch server rewrites `servers[0].url` to that
  server's own address.** The committed snapshot says `http://localhost:5179/`; restore that line or
  the diff carries an unrelated one-line change (and `gen:api` output shifts with it). Everything else
  in a regenerated snapshot should be additions for the routes you added — read the diff.

## Hosting / network
- **Container: `/data` ownership.** The server image runs unprivileged (uid 1654, or
  `SAVELOCKER_UID`/`GID`). Its entrypoint hands `/data` over once on first start after upgrading from
  the root-running image. If the server logs "unable to open database file" after an operator forced
  `user: "99:100"` (or `--user`) — the entrypoint refuses to start and prints the `chown` to run,
  because a non-root start cannot fix ownership itself. Verified 2026-09-20 on all four paths (fresh
  volume, root-created data upgrading, `SAVELOCKER_UID=99`, forced `--user` that cannot write).
- **Behind a proxy or tunnel, the sign-in throttle needs `Security:TrustedProxies`.** Without it every
  client looks like the proxy's address and one person's typos can lock everyone out of *signing in*
  (sessions and agents are unaffected). Set the proxy's IP/CIDR and `Security:ClientIpHeader`
  (`CF-Connecting-IP` for Cloudflare). The forwarded address is believed ONLY when the connection
  itself comes from a listed proxy.
- **A Cloudflare-proxied hostname kills any single request past ~100s — the free/pro edge timeout is
  fixed and not configurable.** Diagnosed 2026-08-18 (`tasks/`-less session, see `logs/`): Cyberpunk
  2077's save archive (100+ MB — one autosave per few minutes played, each several MB) took longer
  than that to reach the origin over a slow home upload link (~200 KB/s measured), and Cloudflare cut
  the connection mid-transfer every single time. The edge answers HTTP 524, but the .NET agent never
  sees that status — it only sees the TCP reset while it is still writing the request body, which
  surfaces as `HttpRequestException: Error while copying content to a stream.` (or, for a request that
  had not started writing yet, `SocketException 10054`/a failed TLS handshake). Neither message names
  Cloudflare or a timeout, so this reads exactly like a generic "server unreachable" fault. Confirmed
  with a direct `curl` upload that bypassed the .NET agent entirely: identical 524 at the identical
  ~100s mark, which is what ruled out an agent-side bug before writing one line of the fix.
  <br>The fix is chunked upload (`POST /upload/begin` → `PUT /upload/{id}/chunk?offset=` →
  `POST /upload/{id}/complete`, driven from `ApiClient.UploadAsync`): no single request ever carries
  more than `UploadChunkBytes` (4 MiB), which cannot take anywhere near 100s even on a very slow link.
  The old single-shot route is untouched and the client falls back to it on a 404 from Begin, so an
  agent updated ahead of its server (the two redeploy separately) degrades to today's behaviour rather
  than failing outright. **This still needs the SERVER redeployed** — an already-updated agent talking
  to an old server just uses the fallback and hits the exact same 524 it always did for a save this
  large; only a server that has the new routes actually fixes the upload.
  <br>A DNS-only (un-proxied) hostname would also fix this with zero code, at the cost of losing
  Cloudflare's WAF/DDoS masking for that record — a real alternative if a large save ever exceeds what
  chunking can practically cover.
- **A generic "server unreachable" / connection-reset agent log line can be a proxy timeout, not a
  real network fault.** `SyncEngine`'s catch-without-a-status-code branch logs only `ex.Message`, not
  the inner exception, so the log itself gives no hint of *why* the connection dropped. If it recurs
  for one large or slow-to-sync game specifically and not others, check archive size against measured
  upload bandwidth against the reverse proxy's fixed timeout before assuming it is the agent's or the
  server's own code.

## Testing
- **`run-local-api-tests.ps1` hardcodes its daemon to :5188 — the same port the `testenv` Windows tray owns.** With the rig up, the
  suite's token checks talk to the *tray* (a different token) and a dozen checks fail together: `token is accepted`, `the
  agent's own Origin`, the whole path-browser block. It looks like a regression and is not. `.\tests\testenv.ps1 down` first (the
  installed agent is untouched), or run a copy on scratch ports. Also build `agent-ui` **before** `dotnet build`-ing the Linux
  agent (`npm run build` in `agent-ui/`): its csproj copies `agent-ui/dist` into the bin only if it exists at build time, and
  `the UI is served with the token injected` needs it.
- **`systemd-analyze security` misreads a `--user` unit — do not act on its score.** Run against
  `savelocker.service` on a Deck (2026-08-15) it reports **8.5 EXPOSED** and, top of the list,
  *"Service runs as root user"*. It does not: a `--user` unit runs as the desktop user. The tool
  grades as though every unit were a system service, so an unset `User=` reads as root, and every
  capability row that follows from that premise (`CAP_SYS_ADMIN`, `CAP_SYS_MODULE`, `CAP_SYS_BOOT`…)
  is scored against privileges the user manager never grants in the first place.
  <br>Read the individual rows instead. All seven shipped settings are confirmed in effect there:
  `NoNewPrivileges`, `PrivateTmp`, `LockPersonality`, `RestrictSUIDSGID`, `UMask`, `PrivateMounts`
  and the address-family allow-list. Two rows look like failures and are not: **`ProtectSystem=`
  reads ✗ because it is `full` rather than `strict`** — its own description confirms it is set — and
  **`RestrictAddressFamilies=~AF_UNIX` / `~AF_(INET|INET6)` read ✗ because we deliberately allow
  those three**, while `~AF_PACKET`, `~AF_NETLINK` and `~…` read ✓. That is the allow-list working,
  not a hole.
  <br>The remaining big-ticket items — `ProtectHome`, `ProtectProc`, `MemoryDenyWriteExecute` — are
  refused on purpose and the unit file says why beside each. See [[Backlog]] before "improving" the
  number.
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
  `AgentInstallerService` creates every platform slot's root in its **constructor** (since v0.5.5
  that is `win-x64` plus a `linux-x64/` subdirectory), and the default resolves to
  `/data/agent-installer`, which a normal Linux user cannot create. The server then dies at startup
  *after* migrations have run — so `server.log` reads like a healthy boot right up to an unhandled
  `UnauthorizedAccessException`, and every server-dependent check fails as though the agent were
  broken. This is what made `run-linux-tests.sh` score 24/40 with 16 cascading failures; setting the
  variable took it to 40/40 with no code change. Windows never sees it, because there the same
  default happily creates `E:\data\`.
- **Give every test server its own `Storage__DbPath`/`Storage__ArchiveRoot`.** The suites are not
  idempotent against a dirty DB and they do not fail in a way that names the cause: a dirty dev DB
  fails `run-enrollment-tests.ps1` as though enrollment code were broken (12/16, starting at "mint
  returns a raw token"), and `run-agent-tests.ps1` uses fixed game names (`SyncGame`) so a second run
  inherits the first's versions and conflicts and fails ~16 checks.
- **Running the server DLL directly ignores `launchSettings.json`** — binds `:5000` in Production
  config instead of `:5179`/Development, and never reads `appsettings.Development.json`, so it opens
  `/data/savelocker.db` (i.e. `E:\data\…`) rather than `src/Server/localstate`. That stray DB carries
  a stale `__EFMigrationsLock`, so the server then hangs in migration **forever** and every
  server-dependent check in `run-agent-tests.ps1` fails as though the agent were broken.
  `cd src/Server && dotnet run` gets this right; the built DLL does not. A script that starts its own
  server must pass `ASPNETCORE_URLS` and `ASPNETCORE_ENVIRONMENT`/`Storage__*` explicitly; a test
  that restarts a server must own it (its own port + state dir).
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

## Agent UI (`agent-ui/`)
- **It has no Tailwind, so `tokens.css` is a hand-kept COPY of `web/src/index.css`'s `@theme` block.**
  Change a colour in one and change it in the other in the same commit; the names are identical so the
  two files can be diffed line for line. Hover / active / focus-visible cannot be inline styles, so the
  primitives in `components/ui/` carry `sl-` classes defined in `ui.css` — a new primitive needs its CSS
  there, and the states are not visible in a build, only in a browser.
- **Dark is the base and light is `data-theme="light"` only**, for the same reason as the console: the
  views not yet migrated (Add games, Settings, Conflicts, the plugin cards) hardcode dark hex colours.
  Group 5 flips it once nothing hardcoded is left.
- **`--color-faint` is not readable text.** It measures 3.31:1 on the dark panel and 3.55:1 on the light
  one — the plan's own values, so not something to "fix" locally, and it fails WCAG AA for the 10–12px
  text it is used on. Use it for eyebrow labels and decoration; anything that carries content (a hero
  detail line, an empty state, a timestamp) uses `--color-dim`. Measure in a browser rather than by eye:
  resolve each element's colours through a canvas (it turns `color-mix()` into RGB), composite backgrounds
  up the tree, and switch transitions off first, exactly as for the console.
- **A page that scrolls inside the fixed-height shell needs `.sl-page`, not a bare `overflow-y: auto`.**
  A bounded flex column shrinks its children instead of overflowing; `.sl-page > * { flex-shrink: 0 }` is
  the fix (same trap as the console's `.page-scroll`).
- **Progress must be read through `useActivity.ts`, not a fresh `api.activity()` poll.** The store keeps the
  previous object for whichever slice did not change and components subscribe to one slice
  (`useActivityBusy` is a boolean), so a byte tick re-renders the header's progress and nothing else.
  Measured 2026-09-20 over four ticks: 16 DOM mutations in the progress area, 0 in the page and the Sync
  all button. A second poller in a component would bring the re-renders back.
- **A callback that runs after a long `await` must not read state from the render that created it.**
  `handleSynced` used `view` directly and so could never see that the user had left for Conflicts during a
  sync; it reads a ref now. Any new "after the request finishes, decide based on where the user is now"
  logic wants the same.
- **`refreshActivity()` invalidates the poll already in flight; do not "simplify" that away.** A poll sent
  before a sync ended still answers "Pushing", and applying it after the header went idle put the busy
  state back on screen (a disabled "Syncing…" button) until the next tick. Dropping the refresh when a poll
  is running was the first attempt and only shortened it. It now bumps a generation, discards the stale
  answer and asks again once the poll settles. Measured against a mock agent that ends the sync while a
  poll is in flight: 480 ms of stale busy state before, 0 ms after.
- **Testing `agent-ui` against a mock agent in the Browser pane** (how that was measured; no agent needed):
  `vite dev` proxies `/api` to :5178, so first confirm nothing real is listening there (with the installed
  agent running, the proxy would forward to it and **Sync all would be a real sync**), then replace
  `window.fetch` in the page and answer everything yourself. Three traps: the pane hides itself and
  `document.hidden` stops `useActivity` polling, so a harness waiting on a poll hangs (pin `document.hidden`
  to false); swapping a source file under a running dev server for an A/B can leave the browser on the module
  it already had, so check which one is live (`(await import('/src/x.ts')).fn.toString()`) and restart the
  server; and a disabled button swallows `.click()` silently, so wait for `!disabled` first.

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
- **WSL usually has no native `node`, and `build-linux.sh` needs one.** It inherits the Windows
  `PATH`, finds `/mnt/c/Program Files/nodejs/npm`, and that fails on a UNC path with
  `error TS5083: Cannot read file 'C:/Windows/tsconfig.json'` — which reads like a TypeScript config
  problem and is not. Build `agent-ui` on Windows, copy `agent-ui/dist` into the clone, and package
  with `SAVELOCKER_SKIP_UI_BUILD=1` (it refuses to run unless `dist/index.html` is really there —
  packaging without it produces an agent whose UI is a blank page).
- **Sync the WSL clone from `git status`, not from a hand-written file list.** A hardcoded list in a
  helper script went stale mid-session and the clone silently built an *older* tree than the one
  under test — the failure surfaced as a compile error, but it just as easily would not have.
- **Never `rsync` the Windows working tree into the WSL clone to run `run-linux-tests.sh`.** The
  Windows tree is CRLF, and `tests/linux/slow-game.sh` with CRLF line endings misbehaves: the game
  exits with the wrong code and stops writing early, failing 6 checks across the launch wrapper, the
  settle gate and the `/proc` lock probe. They read as real wrapper bugs and are not. Copy the files
  you changed through `sed 's/\r$//'` instead, into a checkout reset to `origin/main`. Take the
  baseline on pristine `main` first — the numbers only mean something as a pair.

## Decky plugin (own repo: SkorcherX/SaveLocker-Decky)
- **Ship `package.json`, or Decky loads the plugin the wrong way.** It picks the load path from that
  file: `"type": "module"` selects `import()` (`ESMODULE_V1`), and **no `package.json` at all** falls
  back to `LEGACY_EVAL_IIFE`, which `eval`s the bundle as a classic script. The ESM bundle then dies
  on its own last line with `SyntaxError: Unexpected token 'export'`, thrown from inside Decky's
  loader with no hint that a file is missing. The tell is `version: null` for the plugin in
  `DeckyPluginLoader.plugins` — that field comes from the same file. All four of `plugin.json`,
  `package.json`, `main.py` and `dist/` must be installed.
- **The Quick Access panel only scrolls to things the D-pad can reach.** A column of plain `<div>`s
  is unreachable past the fold — with four games the list already overflowed with no way to scroll,
  which reads as "the panel is broken" rather than "nothing here is focusable". Wrap rows in
  `Focusable` from `@decky/ui`.
- **The Quick Access panel REMOUNTS when a Steam dropdown closes.** Every `useState` in the plugin
  goes back to its initial value, so a selection made in a dropdown is destroyed by the act of
  making it. Anything that must survive an interaction has to live at module scope. The tell is a
  render immediately after the selection reporting the PARENT's state as empty too — which is also
  the only way this was found, after three wrong guesses: **log state at render time, not in the
  handler.**
- **A read-only row needs `Field` with `focusable`, not a bare `Focusable`.** The QAM scrolls by
  MOVING FOCUS, so a run of rows the D-pad cannot land on is a hole it skips — it reveals a line,
  finds nothing focusable, and jumps to the back button, leaving the user toggling up/down. A
  `Focusable` with no handler is not reliably a target (it is resolved by matching props like
  `onActivate`); `Field`'s own `focusable` prop is.
- **`Field` puts children in a RIGHT-HAND column beside its label**, so a row with no label renders
  half-indented and truncated. `childrenLayout="below"` gives it the full width. Build every such
  row through one helper — this was fixed twice because two places built them by hand.
- **The plugin frontend logs nowhere on disk.** The Python backend writes to
  `~/homebrew/logs/<Plugin>/*.log`, but the frontend runs in Steam's `SharedJSContext`, so a render
  error exists only in CEF — and the backend log will happily say everything loaded. Tunnel it:
  `ssh -N -L 8080:127.0.0.1:8080 deck@<ip>`, then `curl http://localhost:8080/json` for targets and
  drive CDP over the websocket. `Runtime.evaluate` of `document.body.innerText` against the
  **QuickAccess** target prints whatever the panel is showing, error boundary included.
- **The agent may overwrite the plugin's files, but may not create new TOP-LEVEL ones.** Decky
  recursively chowns a non-`_root` plugin's *contents* to the desktop user, but leaves the plugin
  directory itself root-owned at 755 — and chowns `plugin.json` to root even for a non-`_root`
  plugin. So `main.py`, `package.json` and everything under `dist/` can be replaced (and `dist/`
  entries added or removed freely), `plugin.json` never can, and a future version adding a top-level
  `py_modules/` cannot be installed this way at all. `DeckyPlugin.cs` therefore proves every
  destination writable **before** writing one byte and refuses the whole package otherwise: a plugin
  half on one version and half on another is worse than an old one.
- **Hot reload needs the `debug` flag in `plugin.json`.** The loader's watchdog fires either way, but
  without the flag Decky logs *"Plugin X is already loaded and has requested to not be re-loaded"* —
  files updated, old code still running, nothing reported. It is the flag's only effect anywhere in
  the loader, and since `plugin.json` is the one file the agent cannot rewrite, **an installation
  predating that flag can never self-update** and needs one manual reinstall.
- **Do not ask for the `_root` flag.** Everything the backend needs belongs to the desktop user: the
  `api-token` is 0600 owned by them and the agent's API is loopback. Root buys nothing and a
  root-created file under `~/.local/share/SaveLocker` breaks the agent's next write as that user.
- **A non-Steam shortcut populates BOTH `strLaunchOptions` and `strShortcutLaunchOptions`**, with the
  same value (measured on a Deck, 2026-08-15). Reading either works; reading whichever is non-empty
  covers installed Steam games too.

## Vault hygiene
- **A vault doc can point at a task file, or a version, that no longer exists.** Check the
  filesystem/`git tag -l` before trusting a pointer in `CONTEXT.md`/`Backlog.md` — this vault has
  drifted before (a deleted task still listed as next action; a stale version number).
- **A `Docs:` commit runs no CI, by design.** `ci.yml` and `docker-publish.yml` both carry the same
  `paths-ignore` (`docs/**`, `CLAUDE.md`, `AGENTS.md`, `.agents/**`), so a vault-only push is
  skipped by both — a green tick is *absent*, not failing. Two things follow. **Never widen it to
  `'**.md'`:** `web/src/releases/*.md` are bundled into the console image and `web/src/help/*.md`
  are the Help KB, so both are code as far as the build is concerned. And **Actions does not expand
  YAML anchors**, so `ci.yml`'s two copies (one per trigger) must be edited together. If any CI job
  is ever made a *required* status check, the `pull_request` copy will block docs-only PRs forever
  — the comment in `ci.yml` says what to do about it.
