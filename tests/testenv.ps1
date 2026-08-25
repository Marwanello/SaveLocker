# One place to drive the throwaway test environment: a Windows agent, a headless WSL agent, a
# Steam Deck (or any SSH-reachable Linux box) and a dashboard container, all running beside — never
# instead of — the installed release agent. When the Deck is configured, `build`/`up` also build
# and install the SaveLocker-Decky plugin from the sibling repo, staged and installed as a SEPARATE
# Decky plugin ("SaveLocker-Test") — never the real "SaveLocker" entry — pointed at this rig's test
# daemon (:5177 / ~/savelocker-test-state), never the real installed agent (:5178). See
# Get-DeckyPluginRepo / New-DeckyPluginTestStage below for exactly what that isolation covers.
#
#   .\tests\testenv.ps1 build     build all four from this working tree (+ the Decky plugin, if -Only
#                                 is 'all'/'deck' and a Deck is configured)
#   .\tests\testenv.ps1 up        start them (registers on first run); installs/reinstalls the test
#                                 Decky plugin on the Deck alongside the test daemon
#   .\tests\testenv.ps1 down      stop them; the installed agent is never touched
#   .\tests\testenv.ps1 status    what is running, and which build
#   .\tests\testenv.ps1 test      run the suites
#   .\tests\testenv.ps1 sync      copy this tree's uncommitted changes into the WSL clone
#   .\tests\testenv.ps1 logs      tail every log the rig writes
#   .\tests\testenv.ps1 clean     down, then DELETE every test build and all its state — WSL,
#                                 Windows, the Deck, the test Decky plugin and the dashboard
#                                 container/image/volume
#   .\tests\testenv.ps1 deck-config -DeckHost deck@<ip> [-DeckServerUrl http://<lan-ip>:5080]
#                                 write/update tests/testenv.local.ps1 (below) instead of hand-
#                                 editing it; no args just prints what's currently saved
#
# Separation rests on three test-only variables (Gotchas.md → Test-only environment variables):
# SAVELOCKER_STATE_ROOT moves all Windows state, SAVELOCKER_TRAY_PORT moves the local API *and*
# scopes the single-instance mutex, SAVELOCKER_RUNKEY_SUBPATH keeps "Start with Windows" off the
# real Run key. Linux needs none of them — XDG_DATA_HOME already does the whole job, and the Deck
# target reuses that same trick over SSH: a self-contained build lands in its own directory pair
# (-DeckPrefix and "$DeckPrefix-state"), never ~/.local/share/SaveLocker, which is both the real
# install's binaries AND its state (Gotchas.md → Linux agent).
#
# The Deck has no universal default — every LAN differs — so it only runs when configured. Copy
# tests/testenv.local.ps1.example to tests/testenv.local.ps1 (gitignored) and fill in:
#   $env:SAVELOCKER_DECK_HOST        deck@192.168.68.67-style SSH target
#   $env:SAVELOCKER_DECK_SERVER_URL  this PC's LAN IP, e.g. http://192.168.68.58:5080 — the Deck
#                                     cannot dial "localhost" and reach a container on another
#                                     machine. Needed for `up`, not for `down`/`status`/`clean`.
# testenv.local.ps1 is loaded automatically below if present — re-edit it whenever the Deck's IP
# changes rather than re-typing an env var every shell. An explicit -DeckHost/-DeckServerUrl on the
# command line always wins over it. Leaving both unset skips the Deck everywhere `-Only` defaults
# to 'all'; passing `-Only deck` explicitly without them is an error instead of a silent no-op.
#
# Installing the Decky plugin needs root (~/homebrew/plugins is root-owned), and this script's SSH
# calls are non-interactive — no PTY, so sudo cannot prompt for a password and just fails with "a
# terminal is required to read the password". One-time fix, ON THE DECK (Desktop Mode, a terminal):
#   sudo visudo -f /etc/sudoers.d/zz-savelocker-testenv
# and add (adjust the /home/deck paths if your Deck's user differs):
#   deck ALL=(ALL) NOPASSWD: /usr/bin/rm -rf /home/deck/homebrew/plugins/SaveLocker-Test
#   deck ALL=(ALL) NOPASSWD: /usr/bin/cp -r /home/deck/.savelocker-testenv-decky-stage /home/deck/homebrew/plugins/SaveLocker-Test
#   deck ALL=(ALL) NOPASSWD: /usr/bin/chown -R deck\:deck /home/deck/homebrew/plugins/SaveLocker-Test
#   deck ALL=(ALL) NOPASSWD: /usr/bin/systemctl restart plugin_loader
# The chown line matters, not just for tidiness: plugin backends run as the deck user, not root
# (confirmed on hardware via `ps` — every loaded plugin's main.py, including the real SaveLocker's,
# runs as deck), so a `sudo cp -r` copy left root-owned never actually loads — Decky just silently
# has no process for it. sudoers needs the colon in "deck:deck" escaped as "deck\:deck" or visudo's
# parser rejects the line. Scoped to exactly these four commands, not blanket passwordless sudo -
# the same trade every
# `sudo cp ...; sudo systemctl restart plugin_loader` in this project's own README makes, just made
# non-interactive so `up`/`clean` can run unattended. The `zz-` PREFIX MATTERS, not just the
# filename: /etc/sudoers.d/ is read in ASCII order, sudo uses the LAST matching rule for a given
# command, and stock SteamOS ships /etc/sudoers.d/wheel granting the wheel group (deck's own group)
# `ALL=(ALL) ALL` — password required. A file named e.g. "savelocker-testenv" sorts BEFORE "wheel"
# alphabetically and gets silently overridden by it (confirmed on hardware: sudo -l correctly listed
# the NOPASSWD rules, and sudo still prompted anyway) — "zz-" sorts after everything else present by
# default, so this file's NOPASSWD wins instead. The
# Deck is "awake only when the maintainer wakes it" (CONTEXT.md), so every SSH call has a short
# connect timeout and reports unreachable rather than hanging or aborting the rest of the rig.
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'up', 'down', 'status', 'test', 'sync', 'logs', 'clean', 'deck-config')]
    [string]$Command = 'status',

    # Never a released version number. A test build stamped with one compares equal to the real
    # release forever, so the update channel can never move it (Gotchas.md). Auto-derived from the
    # latest release tag (patch bumped, "-test" appended) rather than hardcoded, so nobody has to
    # remember to bump a fixed string after every release — a stale one silently reintroduces the
    # exact bug this comment warns about. Falls back to a fixed placeholder if no v*.*.* tag is
    # reachable at all (a shallow clone, or one with no tags fetched).
    [string]$Version = $(
        $tag = git tag -l 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname 2>$null | Select-Object -First 1
        if (-not $tag) {
            '0.0.0-test'
        } else {
            $parts = $tag.TrimStart('v') -split '\.'
            $parts[-1] = [string]([int]$parts[-1] + 1)
            ($parts -join '.') + '-test'
        }
    ),

    [string]$StateRoot = $(if ($env:SAVELOCKER_TEST_ROOT) { $env:SAVELOCKER_TEST_ROOT }
                           else { Join-Path $env:LOCALAPPDATA 'SaveLocker-test' }),
    [int]$ConsolePort = 5080,
    [int]$WinPort     = 5188,
    # Not 5188: WSL2 publishes Linux listeners onto Windows localhost, so they really do collide.
    [int]$LinuxPort   = 5187,
    [string]$Distro   = 'Ubuntu',
    [string]$WslRepo  = '$HOME/SaveLocker',

    # SSH target for a real Deck, "user@host" — e.g. deck@192.168.68.67. No default: unlike the
    # Windows/WSL targets there is no "this machine", so an unset host means "don't touch the Deck"
    # rather than a guess. Set once via the env var so it never has to be typed.
    [string]$DeckHost = $env:SAVELOCKER_DECK_HOST,
    # Distinct from both the WSL rig's :5187 and the real Deck agent's :5178 — a real, separate
    # machine, so nothing here collides on the wire the way WSL2's localhost forwarding does.
    [int]$DeckPort = 5177,
    # Remote directory for the extracted self-contained build. State lives in the sibling
    # "$DeckPrefix-state" (set as XDG_DATA_HOME by testenv-deck.sh) — see header comment.
    [string]$DeckPrefix = '~/savelocker-test',
    [string]$DeckServerUrl = $env:SAVELOCKER_DECK_SERVER_URL,
    # SaveLocker-Decky is its own repo, not a subdirectory of this one (Decky's plugin database
    # requires a plugin's repo root to BE the plugin). Defaults to the sibling checkout every
    # contributor already has next to this one; override for a non-standard layout. Not saved by
    # `deck-config` — unlike DeckHost/DeckServerUrl this doesn't name your LAN, so an env var or an
    # explicit flag is enough.
    [string]$DeckyPluginRepo = $env:SAVELOCKER_DECKY_PLUGIN_REPO,

    [ValidateSet('all', 'windows', 'linux', 'deck', 'console')]
    [string]$Only = 'all',

    [ValidateSet('all', 'agents', 'dashboard')]
    [string]$Suite = 'all',

    # Real trays, real waits, ~5 minutes, and it needs an interactive desktop.
    [switch]$SkipWinAgentSuite,
    [switch]$AgentsOnly
)

$ErrorActionPreference = 'Continue'

# Deck settings live here, not just in $env: vars set for one shell — a home LAN's DHCP lease can
# move the Deck's IP at any time, so this needs to be a place you go edit ONE line and have every
# future session pick it up, not a shell profile you forget you edited. Gitignored (tests/*.local.ps1):
# it names your LAN, not the project's. testenv.local.ps1.example is the committed template.
# Explicit -DeckHost/-DeckServerUrl on the command line still win over whatever this sets.
$deckConfig = Join-Path $PSScriptRoot 'testenv.local.ps1'
if (Test-Path $deckConfig) {
    . $deckConfig
    if (-not $PSBoundParameters.ContainsKey('DeckHost') -and $env:SAVELOCKER_DECK_HOST) {
        $DeckHost = $env:SAVELOCKER_DECK_HOST
    }
    if (-not $PSBoundParameters.ContainsKey('DeckServerUrl') -and $env:SAVELOCKER_DECK_SERVER_URL) {
        $DeckServerUrl = $env:SAVELOCKER_DECK_SERVER_URL
    }
}

$root      = Split-Path $PSScriptRoot -Parent
$agentDll  = Join-Path $root 'src\Agent\bin\Debug\net10.0-windows\SaveLocker.Agent.dll'
$container = 'savelocker-test'
$image     = 'savelocker:test'
$volume    = 'savelocker-test-data'
$serverUrl = "http://localhost:$ConsolePort"
$winState  = Join-Path $StateRoot 'SaveLocker'

# The directory name Decky loads plugins by AND the display name plugin.json/the bundle carry once
# staged — both deliberately different from the real plugin ("SaveLocker"), so the two can be
# installed on the same Deck at once without Decky treating them as the same plugin and without a
# toast leaving you guessing which install just fired.
$deckyTestPluginName = 'SaveLocker-Test'
# Where `build` leaves the staged, rewritten plugin for `up` to install — same shape as the Deck
# agent's own $env:TEMP\savelocker-testenv-artifacts tarball: build writes it once, up/clean read it,
# neither rebuilds. A fixed path rather than a return value threaded through, so 'up' on its own
# (without a fresh 'build' first) can say plainly what's missing instead of silently rebuilding.
$deckyStagePath = Join-Path $env:TEMP 'savelocker-testenv-decky-stage'

$inProgramFiles = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (Test-Path $inProgramFiles) { $dotnet = $inProgramFiles } else { $dotnet = 'dotnet' }

function Say  { param([string]$m) Write-Host "== $m" -ForegroundColor Cyan }
function Warn { param([string]$m) Write-Host "!! $m" -ForegroundColor Yellow }

function Use-TestEnvVars {
    $env:SAVELOCKER_STATE_ROOT     = $StateRoot
    $env:SAVELOCKER_TRAY_PORT      = "$WinPort"
    $env:SAVELOCKER_RUNKEY_SUBPATH = 'Software\SaveLocker\TestRun'
}
function Clear-TestEnvVars {
    # Leaving either set makes later runs in this shell behave in ways that look like bugs.
    Remove-Item Env:\SAVELOCKER_STATE_ROOT, Env:\SAVELOCKER_TRAY_PORT, Env:\SAVELOCKER_RUNKEY_SUBPATH `
        -ErrorAction SilentlyContinue
}

# Not `wslpath`: wsl.exe strips the backslashes out of a Windows path argument before the Linux side
# ever sees it, so "D:\a\b" arrives as "D:ab". Converting here is deterministic and needs no round trip.
function ConvertTo-WslPath {
    param([string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    return '/mnt/' + $full.Substring(0, 1).ToLower() + ($full.Substring(2) -replace '\\', '/')
}

function Invoke-Wsl {
    param([string]$Sub, [string]$StdinFile, [string]$ArtifactDir)

    $sh = Join-Path $PSScriptRoot 'testenv.sh'
    if (-not (Test-Path $sh)) { throw "missing $sh" }
    $shWsl = ConvertTo-WslPath $sh

    # WIN_REPO is passed for every subcommand, not just sync: the build stages agent-ui/dist from it.
    $vars = @(
        "SAVELOCKER_WSL_REPO='$WslRepo'",
        "SAVELOCKER_LINUX_PORT=$LinuxPort",
        "SAVELOCKER_SERVER_URL='$serverUrl'",
        "SAVELOCKER_TEST_VERSION='$Version'",
        "SAVELOCKER_WIN_REPO='$(ConvertTo-WslPath $root)'"
    )
    if ($Sub -eq 'sync') {
        # The COMMON git dir, not this worktree: a worktree's .git is a file pointing at a Windows
        # path that WSL cannot follow, while branches live in the shared object store.
        $common = (& git -C $root rev-parse --path-format=absolute --git-common-dir)
        $vars += "SAVELOCKER_WIN_GITDIR='$(ConvertTo-WslPath (Split-Path $common -Parent))'"
        $vars += "SAVELOCKER_BRANCH='$(& git -C $root rev-parse --abbrev-ref HEAD)'"
    }
    if ($ArtifactDir) {
        # build-deck's only: where to drop the finished tarball so Windows can find it without
        # guessing at a \\wsl$\ UNC path.
        $vars += "SAVELOCKER_ARTIFACT_DIR='$(ConvertTo-WslPath $ArtifactDir)'"
    }

    # A .sh copied from this (CRLF) tree gives bash "bad interpreter: ...^M", so strip CR on the way
    # in rather than trusting the checkout's line endings.
    $cmd = "sed 's/\r`$//' '$shWsl' > ~/.savelocker-testenv.sh; " +
           ($vars -join ' ') + " bash ~/.savelocker-testenv.sh $Sub"
    if ($StdinFile) {
        $cmd += " < '$(ConvertTo-WslPath $StdinFile)'"
    }
    & wsl -d $Distro -- bash -c $cmd
}

function Test-DeckConfigured { return [bool]$DeckHost }

# The agent's local API — including the Deck test daemon's — is loopback-only, ALWAYS
# (AgentApiServer.Start: "binding it to a LAN interface would expose [machine control] to the
# whole network"; Decisions.md records a `--lan` flag that existed once and was withdrawn). So
# "http://<deck-ip>:<port>" is not reachable from this PC no matter what's set up on the Deck -
# there is nothing to set up, it refuses on principle. The daemon's own startup message says the
# sanctioned way to reach it remotely: an SSH tunnel.
function Get-DeckUiHint { return "ssh -L ${DeckPort}:localhost:${DeckPort} $DeckHost   then open http://localhost:$DeckPort" }

# Gates build/up (which respect -Only): explicit '-Only deck' with no host configured is an error,
# 'all' with no host configured is a silent skip so the default all-in-one flow still works for
# whoever hasn't wired up a Deck. down/status/logs/clean run unconditionally whenever a host IS
# configured instead — see the switch block, matching how 'down' already ignores -Only entirely.
function Use-Deck {
    if ($Only -eq 'deck') {
        if (-not $DeckHost) { throw "No Deck configured - set -DeckHost or `$env:SAVELOCKER_DECK_HOST (e.g. deck@192.168.68.67)." }
        return $true
    }
    if ($Only -eq 'all') {
        if ($DeckHost) { return $true }
        Warn "deck skipped - set -DeckHost or `$env:SAVELOCKER_DECK_HOST to include it"
        return $false
    }
    return $false
}

# Runs a subcommand of testenv-deck.sh over SSH — the same shape as Invoke-Wsl, just over the
# network instead of wsl.exe. A short ConnectTimeout matters here in a way it does not for WSL: the
# Deck is only awake when the maintainer wakes it (CONTEXT.md), so a cold Deck must fail in seconds,
# not hang on the default TCP timeout. Every caller wraps this in try/catch and Warn — a sleeping
# Deck must never abort the rest of the rig.
function Invoke-Deck {
    param([string]$Sub)

    $sh = Join-Path $PSScriptRoot 'testenv-deck.sh'
    if (-not (Test-Path $sh)) { throw "missing $sh" }

    # Same CRLF trap as Invoke-Wsl: scp copies this working tree's bytes as-is, so strip CR on the
    # remote side rather than trusting the checkout's line endings.
    & scp -o ConnectTimeout=5 -q $sh "${DeckHost}:/tmp/.savelocker-testenv-deck.raw"
    if ($LASTEXITCODE -ne 0) { throw "cannot reach $DeckHost (asleep?)" }

    $vars = @(
        "SAVELOCKER_DECK_PREFIX='$DeckPrefix'",
        "SAVELOCKER_DECK_PORT=$DeckPort",
        "SAVELOCKER_SERVER_URL='$DeckServerUrl'",
        "SAVELOCKER_TEST_VERSION='$Version'"
    )
    $cmd = "sed 's/\r`$//' /tmp/.savelocker-testenv-deck.raw > /tmp/.savelocker-testenv-deck.sh; " +
           ($vars -join ' ') + " bash /tmp/.savelocker-testenv-deck.sh $Sub"
    & ssh -o ConnectTimeout=5 $DeckHost $cmd
    if ($LASTEXITCODE -ne 0) { throw "deck '$Sub' failed (exit $LASTEXITCODE)" }
}

function Build-Deck {
    Say "building the Deck agent as $Version (self-contained linux-x64)"
    Build-AgentUi
    $artifactDir = Join-Path $env:TEMP 'savelocker-testenv-artifacts'
    New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
    Invoke-Wsl 'build-deck' -ArtifactDir $artifactDir
    $tarball = Join-Path $artifactDir "savelocker-$Version-linux-x64.tar.gz"
    if (-not (Test-Path $tarball)) { throw "deck build did not produce $tarball" }
    Write-Host "  built: $tarball"
}

function Start-Deck {
    $tarball = Join-Path (Join-Path $env:TEMP 'savelocker-testenv-artifacts') "savelocker-$Version-linux-x64.tar.gz"
    if (-not (Test-Path $tarball)) { throw "not built - run: .\tests\testenv.ps1 build -Only deck" }
    if (-not $DeckServerUrl) {
        throw "no -DeckServerUrl / `$env:SAVELOCKER_DECK_SERVER_URL - the Deck can't reach 'localhost', it needs this PC's LAN IP (e.g. http://192.168.68.58:$ConsolePort)"
    }

    Say "pushing $(Split-Path $tarball -Leaf) to $DeckHost"
    & scp -o ConnectTimeout=5 -q $tarball "${DeckHost}:/tmp/.savelocker-test.tar.gz"
    if ($LASTEXITCODE -ne 0) { throw "scp to $DeckHost failed (asleep?)" }

    Invoke-Deck 'up'
}

function Stop-Deck { Invoke-Deck 'down' }

# Resolves and validates the sibling SaveLocker-Decky checkout. Guessed as ..\SaveLocker-Decky next
# to the MAIN checkout (how every contributor's checkout is actually laid out today) rather than
# defaulted to nothing like DeckHost — unlike a LAN address there IS a "this machine" answer worth
# trying first. "Next to the main checkout", not next to $root: $root is THIS worktree's own
# directory when testenv.ps1 runs from one (e.g. .claude\worktrees\<name>), and guessing off that
# would look one level above the worktrees folder instead of one level above the repo. The git
# COMMON dir is shared by every worktree and always resolves back to the main checkout, same trick
# 'sync' above already uses for SAVELOCKER_WIN_GITDIR.
# Same convention as the top-level $Version default: never a real release number, so Decky's own
# update check can't ever compare it equal to (or newer than) whatever's actually published, and the
# "-test" suffix makes the plugin browser's own "INSTALLED vX.Y.Z" line say, at a glance, that this
# is the isolated test build talking to the test daemon - not the real SaveLocker plugin's version
# talking to the real, live agent.
function Get-DeckyTestPluginVersion {
    param([Parameter(Mandatory)][string]$CurrentVersion)
    $parts = $CurrentVersion -split '\.'
    $parts[-1] = [string]([int]$parts[-1] + 1)
    return ($parts -join '.') + '-test'
}

function Get-DeckyPluginRepo {
    $candidate = if ($DeckyPluginRepo) { $DeckyPluginRepo }
                 else {
                     $commonDir = (& git -C $root rev-parse --path-format=absolute --git-common-dir)
                     $mainCheckout = Split-Path $commonDir -Parent
                     Join-Path (Split-Path $mainCheckout -Parent) 'SaveLocker-Decky'
                 }
    if (-not (Test-Path (Join-Path $candidate 'plugin.json'))) {
        throw "no SaveLocker-Decky checkout at '$candidate' (no plugin.json there) - " +
              "set -DeckyPluginRepo or `$env:SAVELOCKER_DECKY_PLUGIN_REPO"
    }
    return $candidate
}

function Build-DeckyPlugin {
    $repo = Get-DeckyPluginRepo
    $pluginJsonPath = Join-Path $repo 'plugin.json'
    $original = Get-Content $pluginJsonPath -Raw

    Say "building the Decky plugin from $repo as '$deckyTestPluginName'"
    Push-Location $repo
    try {
        # @decky/rollup (node_modules/@decky/rollup/src/index.js) reads plugin.json AT BUILD TIME and
        # bakes its "name" straight into the bundle - as the `@decky/manifest` virtual module, and
        # into every asset/sourcemap URL (`http://127.0.0.1:1337/plugins/${manifest.name}/`). That
        # baked-in name is what Decky Loader appears to use to route this plugin's own callable() IPC
        # calls, NOT the plugin.json shipped alongside dist/ afterward. Renaming only the staged copy
        # post-build (the original version of this function) left the bundle itself still identifying
        # as "SaveLocker" - confirmed on hardware: every callable() from the "test" build's frontend
        # was answered by the REAL SaveLocker's backend, so the panel showed the real agent's 34
        # tracked games instead of the test daemon's zero. So the rename has to happen on the real
        # plugin.json, before the build sees it - and be restored immediately after regardless of
        # outcome, so the repo's own working tree never actually carries the test name.
        $renamed = $original.Replace('"name": "SaveLocker"', "`"name`": `"$deckyTestPluginName`"")
        if ($renamed -eq $original) { throw 'plugin.json: "name": "SaveLocker" not found - build aborted' }
        Set-Content $pluginJsonPath $renamed -NoNewline

        if (-not (Test-Path 'node_modules')) { & npm install --no-audit --no-fund | Out-Null }
        & npm run build
        $buildExitCode = $LASTEXITCODE
    } finally {
        Set-Content $pluginJsonPath $original -NoNewline
        Pop-Location
    }
    if ($buildExitCode -ne 0) { throw 'decky plugin build failed' }
}

# Stages plugin.json/package.json/main.py/dist into a throwaway directory and rewrites exactly the
# handful of literal strings that matter for isolation and labelling — never the checkout itself, so
# the real repo never carries a test-only string and a build for the Deck can't accidentally leave
# something behind in source.
#
#   - plugin.json "name" and the bundle's own "SaveLocker" literal (its QAM title AND every toast) ->
#     $deckyTestPluginName, so the two installs are visibly distinct everywhere the plugin speaks.
#   - main.py's AGENT constant: :5178 (the real agent) -> :5177 (testenv-deck.sh's daemon port).
#   - main.py's state dir: ~/.local/share/SaveLocker (the real install's state) -> the same
#     "$DeckPrefix-state/SaveLocker" testenv-deck.sh itself uses (XDG_DATA_HOME=$DeckPrefix-state).
#   - main.py's agent-binary search: only "$DeckPrefix/savelocker" - the real fallback paths would
#     find the real installed binary and defeat the whole point.
#   - main.py's restart_agent: short-circuited. The test daemon is a transient
#     `systemd-run --user --scope`, not the savelocker.service unit - letting this method reach for
#     that name from the test plugin would restart the real, live agent instead of doing anything to
#     this one.
#
# Every substitution targets an exact, current literal from main.py (verified against this repo's
# copy of the string, not guessed) rather than a loose pattern, specifically so a future edit to
# main.py's wording makes this silently NOT match — and the `throw` after each one below turns that
# into a loud failure instead of a stage that quietly ships the real agent's port to a "test" plugin.
# Called from the `build` switch case ONLY - `up` installs whatever this last left at
# $deckyStagePath and never calls this itself, same division of labour as Build-Deck/Start-Deck.
function New-DeckyPluginTestStage {
    $repo = Get-DeckyPluginRepo
    # | Out-Null, not a bare call: npm's own stdout is otherwise the LAST thing written to the
    # success stream before `return $stage` below, and a caller doing `$s = New-DeckyPluginTestStage`
    # would silently get npm's build log instead of the stage path - reproduced and fixed while
    # testing this function directly (Build-DeckyPlugin is fine called on its own where nothing
    # captures its return value).
    Build-DeckyPlugin | Out-Null

    $stage = $deckyStagePath
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item (Join-Path $repo 'plugin.json') $stage
    Copy-Item (Join-Path $repo 'package.json') $stage
    Copy-Item (Join-Path $repo 'main.py') $stage
    Copy-Item (Join-Path $repo 'dist') (Join-Path $stage 'dist') -Recurse

    $pluginJsonPath = Join-Path $stage 'plugin.json'
    $pluginJson = Get-Content $pluginJsonPath -Raw
    $pluginJsonNew = $pluginJson.Replace('"name": "SaveLocker"', "`"name`": `"$deckyTestPluginName`"")
    if ($pluginJsonNew -eq $pluginJson) { throw 'plugin.json: "name": "SaveLocker" not found - stage aborted' }
    Set-Content $pluginJsonPath $pluginJsonNew -NoNewline

    # Decky's plugin browser reads its "INSTALLED vX.Y.Z" line straight off package.json's "version" -
    # bump it the same way build/up/tests stamp the agent and dashboard, so a glance at the Deck's
    # plugin list is enough to tell this apart from a real release.
    $packageJsonPath = Join-Path $stage 'package.json'
    $packageJson = Get-Content $packageJsonPath -Raw
    if ($packageJson -notmatch '"version":\s*"([^"]+)"') {
        throw 'package.json: "version" not found - stage aborted'
    }
    $testVersion = Get-DeckyTestPluginVersion -CurrentVersion $Matches[1]
    $packageJsonNew = $packageJson -replace '"version":\s*"[^"]+"', "`"version`": `"$testVersion`""
    Set-Content $packageJsonPath $packageJsonNew -NoNewline
    Say "staged package.json version: $testVersion"

    # Both quote styles: the toaster.toast() titles in index.tsx are single-quoted string literals
    # and survive the build that way, but the QAM titleView's JSX text child - <div>SaveLocker</div>
    # - compiles through a different path and comes out double-quoted instead. Missing this one was
    # exactly why the panel's own header kept reading "SaveLocker" after every other fix landed.
    $bundlePath = Join-Path $stage 'dist\index.js'
    $bundle = Get-Content $bundlePath -Raw
    $bundleNew = $bundle.Replace("'SaveLocker'", "'$deckyTestPluginName'").Replace('"SaveLocker"', "`"$deckyTestPluginName`"")
    if ($bundleNew -eq $bundle) { throw "dist/index.js: literal 'SaveLocker' not found - stage aborted" }
    Set-Content $bundlePath $bundleNew -NoNewline

    $mainPyPath = Join-Path $stage 'main.py'
    $mainPy = Get-Content $mainPyPath -Raw

    $agentOld = 'AGENT = "http://127.0.0.1:5178"'
    $agentNew = 'AGENT = "http://127.0.0.1:5177"'
    if (-not $mainPy.Contains($agentOld)) { throw "main.py: '$agentOld' not found - stage aborted" }
    $mainPy = $mainPy.Replace($agentOld, $agentNew)

    $stateDirOld = '    return os.path.join(home, ".local", "share", "SaveLocker")'
    $stateDirNew = '    return os.path.join(home, "savelocker-test-state", "SaveLocker")'
    if (-not $mainPy.Contains($stateDirOld)) { throw "main.py: _state_dir's return line not found - stage aborted" }
    $mainPy = $mainPy.Replace($stateDirOld, $stateDirNew)

    $binaryOld = @"
    for candidate in (
        os.path.join(home, ".local", "share", "SaveLocker", "savelocker"),
        os.path.join(home, ".local", "bin", "savelocker"),
    ):
"@
    $binaryNew = @"
    for candidate in (
        os.path.join(home, "savelocker-test", "savelocker"),
    ):
"@
    if (-not $mainPy.Contains($binaryOld)) { throw "main.py: _agent_binary's candidate tuple not found - stage aborted" }
    $mainPy = $mainPy.Replace($binaryOld, $binaryNew)

    # `r`n, not `n: main.py is checked out CRLF (Windows git), and .Contains/.Replace are literal -
    # an LF-only needle never matches a CRLF haystack, which is exactly how the first version of
    # this silently no-op'd instead of throwing (caught by actually running this against a real
    # checkout, not by inspection).
    $restartOld = "    async def restart_agent(self):`r`n"
    $restartGuard = "    async def restart_agent(self):`r`n" +
        "        # Test build (testenv.ps1): the Deck test daemon is a transient systemd --user scope, not`r`n" +
        "        # savelocker.service, so this must never reach for that unit - it would restart the real,`r`n" +
        "        # live agent instead of anything belonging to this plugin.`r`n" +
        "        return {`"ok`": False, `"reason`": `"test-build-no-restart`"}`r`n"
    if (-not $mainPy.Contains($restartOld)) { throw "main.py: restart_agent's def line not found - stage aborted" }
    $mainPy = $mainPy.Replace($restartOld, $restartGuard)

    Set-Content $mainPyPath $mainPy -NoNewline
    return $stage
}

function Install-DeckyPluginOnDeck {
    if (-not $DeckHost) { throw "No Deck configured - set -DeckHost or `$env:SAVELOCKER_DECK_HOST." }
    if (-not (Test-Path $deckyStagePath)) {
        throw "decky plugin not built - run: .\tests\testenv.ps1 build -Only deck (then up again)"
    }

    Say "installing '$deckyTestPluginName' to $DeckHost (separate from any real SaveLocker plugin)"
    $remoteStage = '~/.savelocker-testenv-decky-stage'
    & ssh -o ConnectTimeout=5 $DeckHost "rm -rf $remoteStage"
    if ($LASTEXITCODE -ne 0) { throw "cannot reach $DeckHost (asleep?)" }
    & scp -o ConnectTimeout=5 -q -r $deckyStagePath "${DeckHost}:$remoteStage"
    if ($LASTEXITCODE -ne 0) { throw "scp to $DeckHost failed" }

    # sudo, not the plugin_loader restart alone: ~/homebrew/plugins is root-owned (Gotchas.md ->
    # Decky plugin), same as the README's own manual-install command this mirrors. This is a
    # non-interactive `ssh host cmd` (no PTY), so sudo can only succeed here without a password
    # prompt - i.e. the Deck needs a NOPASSWD sudoers rule for exactly these four commands. See the
    # header comment for the exact /etc/sudoers.d snippet; without it this throws with sudo's own
    # "a terminal is required to read the password" rather than hanging.
    #
    # chown back to deck:deck after the copy - plugin backends run as the deck user, not root
    # (confirmed on hardware: `ps` shows every loaded plugin's main.py running as deck, including
    # the real SaveLocker's own), and `sudo cp -r` leaves its copies root-owned. Decky's own
    # installer does this chown itself for a normal install (Gotchas.md: "Decky recursively chowns a
    # non-root plugin's contents to the desktop user") - skipping it here isn't a cosmetic gap, it's
    # the difference between the plugin loading at all and Decky silently never starting it: a
    # root-owned SaveLocker-Test directory produced NO running process whatsoever, confirmed via
    # `ps -eo user,pid,cmd | grep -i savelocker` showing only the real, untouched SaveLocker.
    $remoteTarget = "~/homebrew/plugins/$deckyTestPluginName"
    & ssh -o ConnectTimeout=5 $DeckHost "sudo rm -rf $remoteTarget && sudo cp -r $remoteStage $remoteTarget && sudo chown -R deck:deck $remoteTarget && sudo systemctl restart plugin_loader"
    if ($LASTEXITCODE -ne 0) {
        throw "install on $DeckHost failed - if sudo asked for a password, see this file's header " +
              "comment for the NOPASSWD sudoers rule this step needs"
    }
    Write-Host "  installed as '$deckyTestPluginName' - open Decky's menu on the Deck to find it, listed separately from any real SaveLocker entry"
}

function Remove-DeckyPluginFromDeck {
    $remoteTarget = "~/homebrew/plugins/$deckyTestPluginName"
    & ssh -o ConnectTimeout=5 $DeckHost "sudo rm -rf $remoteTarget && sudo systemctl restart plugin_loader"
    if ($LASTEXITCODE -ne 0) {
        throw "removing '$deckyTestPluginName' from $DeckHost failed - if sudo asked for a password, " +
              "see this file's header comment for the NOPASSWD sudoers rule this step needs"
    }
    Write-Host "  removed '$deckyTestPluginName' from $DeckHost"
}

# The installed release agent runs as SaveLocker.Agent.exe, so a dotnet.exe host with the agent DLL
# on its command line can only ever be a build from this tree. Nothing here can reach the real one.
function Get-TestTray {
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -like '*SaveLocker.Agent.dll*' }
}

function Test-ConsoleUp {
    try { Invoke-RestMethod -Uri "$serverUrl/api/admin/status" -TimeoutSec 4 } catch { $null }
}

function Get-WinAgentState {
    $tokenPath = Join-Path $winState 'api-token'
    if (-not (Test-Path $tokenPath)) { return $null }
    $token = (Get-Content $tokenPath -Raw).Trim()
    try {
        Invoke-RestMethod -Uri "http://localhost:$WinPort/api/state" `
            -Headers @{ 'X-SaveLocker-Token' = $token } -TimeoutSec 4
    } catch { $null }
}

# The Windows agent has exactly ONE pre-existing warning (MSB3277, a WindowsBase 4.0/5.0 conflict
# from WebView2) and a second one means something. So: stay quiet at the expected count, and dump
# the whole log the moment it changes.
function Invoke-Build {
    param([string]$Project, [int]$ExpectedWarnings)

    $out = & $dotnet build $Project --no-incremental -v q --nologo 2>&1
    $code = $LASTEXITCODE
    $text = $out -join "`n"
    $m = [regex]::Match($text, '(\d+) Warning\(s\)')
    if ($m.Success) { $warnings = [int]$m.Groups[1].Value } else { $warnings = 0 }

    if ($code -ne 0 -or $warnings -ne $ExpectedWarnings) {
        $out | ForEach-Object { Write-Host $_ }
        if ($code -ne 0) { throw "build failed: $Project" }
        Warn "expected $ExpectedWarnings warning(s), got $warnings - the log is above"
    } else {
        Write-Host "  build succeeded ($warnings warning(s), as expected)"
    }
}

# The rig is only isolated while it maps nothing real. A test agent enrolls from a scan of THIS
# machine, so it can map a real save folder - and then a pull from the test console would restore
# over the actual save. This only ever REPORTS: what the rig tracks is the operator's call, and a
# script that quietly untracked things would be making save-data decisions on its own.
function Show-RealGameMappings {
    $cfg = Join-Path $winState 'config.json'
    if (-not (Test-Path $cfg)) { return }
    $games = (Get-Content $cfg -Raw | ConvertFrom-Json).Games
    foreach ($g in $games) {
        if ($g.SaveDirectory -and -not $g.SaveDirectory.StartsWith($StateRoot, 'OrdinalIgnoreCase')) {
            Warn "the test agent maps '$($g.Name)' to a real folder: $($g.SaveDirectory)"
            Warn "  a pull from the test console would restore over it."
        }
    }
}

function Build-Windows {
    Say "building the Windows agent as $Version"
    if (Get-TestTray) { Stop-Windows }
    # SaveLocker.Agent.csproj runs its OWN "npm run build" (BeforeTargets="Build", unconditionally,
    # no staleness check) to stage the WebView2 UI — it has no idea whether node_modules exists. A
    # fresh worktree has never had `npm install` run in agent-ui/, so without this the csproj's own
    # npm step dies with "'tsc' is not recognized" before a single line of C# compiles.
    $agentUiDir = Join-Path $root 'agent-ui'
    if (-not (Test-Path (Join-Path $agentUiDir 'node_modules'))) {
        Say 'agent-ui has no node_modules yet - installing first'
        Push-Location $agentUiDir
        try { & npm install --no-audit --no-fund | Out-Null } finally { Pop-Location }
    }
    # -p:Version does NOT work here: MinVer assigns Version/FileVersion/AssemblyVersion in a later
    # target and overwrites it. MinVerVersionOverride is its own escape hatch.
    $env:MinVerVersionOverride = $Version
    try {
        Invoke-Build (Join-Path $root 'src\Agent\SaveLocker.Agent.csproj') 1
    } finally {
        Remove-Item Env:\MinVerVersionOverride -ErrorAction SilentlyContinue
    }
    Write-Host "  stamped: $((Get-Item $agentDll).VersionInfo.ProductVersion)"
}

# The Windows agent's csproj builds this itself; the Linux one cannot (no native node in WSL), so
# the dist has to exist here before it can be staged across.
function Build-AgentUi {
    $dist = Join-Path $root 'agent-ui\dist\index.html'
    $src  = Join-Path $root 'agent-ui\src'
    $needed = -not (Test-Path $dist)
    if (-not $needed) {
        $newest = (Get-ChildItem $src -Recurse -File | Sort-Object LastWriteTime -Descending |
                   Select-Object -First 1).LastWriteTime
        if ($newest -gt (Get-Item $dist).LastWriteTime) { $needed = $true }
    }
    if (-not $needed) { Write-Host '  agent-ui/dist is current'; return }

    Say 'building agent-ui (needed before the Linux agent can serve it)'
    Push-Location (Join-Path $root 'agent-ui')
    try {
        if (-not (Test-Path 'node_modules')) { & npm install --no-audit --no-fund | Out-Null }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw 'agent-ui build failed' }
    } finally { Pop-Location }
}

function Build-Console {
    Say "building the dashboard image as $Version"
    $commit = (& git -C $root rev-parse --short HEAD)
    $built  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    & docker build -t $image -f (Join-Path $root 'src\Server\Dockerfile') `
        --build-arg "SAVELOCKER_VERSION=$Version" `
        --build-arg "SAVELOCKER_COMMIT=$commit" `
        --build-arg "SAVELOCKER_BUILT_AT=$built" $root
    # Check the exit code explicitly: a piped docker build reports the pipe's status, which has
    # masked a genuine "failed to fetch oauth token" as success before.
    if ($LASTEXITCODE -ne 0) { throw 'docker build failed' }
}

function Start-Console {
    Say "console on :$ConsolePort"
    & docker rm -f $container 2>$null | Out-Null
    & docker run -d --name $container -p "${ConsolePort}:8080" -v "${volume}:/data" $image | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker run failed' }
    for ($i = 0; $i -lt 40; $i++) {
        $s = Test-ConsoleUp
        if ($s) { Write-Host "  $($s.build.version) @ $($s.build.commit)"; return }
        Start-Sleep -Milliseconds 800
    }
    Warn "console did not answer on :$ConsolePort"
}

function Start-Windows {
    if (-not (Test-Path $agentDll)) { throw "not built — run: .\tests\testenv.ps1 build" }
    Stop-Windows | Out-Null
    Use-TestEnvVars
    try {
        $cfg = Join-Path $winState 'config.json'
        $registered = (Test-Path $cfg) -and ((Get-Content $cfg -Raw) -match '"apiKey"')
        if (-not $registered) {
            Say "registering the Windows test agent against $serverUrl"
            & $dotnet $agentDll set-server --url $serverUrl | Out-Null
            & $dotnet $agentDll register --name 'WinTest' | Select-Object -First 1
        }
        Say "tray on :$WinPort"
        $p = Start-Process $dotnet -ArgumentList @($agentDll) -PassThru
        for ($i = 0; $i -lt 40; $i++) {
            $s = Get-WinAgentState
            if ($s) { Write-Host "  pid $($p.Id) — v$($s.buildLabel) — connected=$($s.connected)"; return }
            Start-Sleep -Milliseconds 700
        }
        Warn "the test tray did not answer on :$WinPort (log: $winState\agent.log)"
    } finally { Clear-TestEnvVars }
}

function Stop-Windows {
    $procs = @(Get-TestTray)
    if ($procs.Length -eq 0) { Write-Host 'no test tray running'; return }
    foreach ($p in $procs) {
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Host "stopped test tray (pid $($p.ProcessId))"
    }
}

function Stop-Console {
    & docker stop $container 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Host "stopped $container" } else { Write-Host 'no console container running' }
}

function Invoke-Suites {
    $results = [ordered]@{}

    if ($Suite -eq 'all' -or $Suite -eq 'agents') {
        Say 'agents — Linux suite (WSL)'
        Invoke-Wsl 'test'
        $results['linux agent'] = $LASTEXITCODE

        if ($SkipWinAgentSuite) {
            Warn 'skipping the Windows agent bug bounty (-SkipWinAgentSuite)'
        } else {
            Say 'agents — Windows bug bounty (~5 min, needs an interactive desktop)'
            & powershell -NoProfile -File (Join-Path $PSScriptRoot 'run-winagent-tests.ps1')
            $results['windows agent'] = $LASTEXITCODE
        }
    }

    if ($Suite -eq 'all' -or $Suite -eq 'dashboard') {
        Say 'dashboard — server bug bounty (owns its own port and DB)'
        & powershell -NoProfile -File (Join-Path $PSScriptRoot 'run-server-bugbounty-tests.ps1')
        $results['server'] = $LASTEXITCODE

        Say 'dashboard — agent-update scheduling (owns its own port and DB)'
        & powershell -NoProfile -File (Join-Path $PSScriptRoot 'run-schedule-tests.ps1')
        $results['scheduling'] = $LASTEXITCODE

        Say 'dashboard — console lint and build'
        Push-Location (Join-Path $root 'web')
        try {
            # A fresh worktree has no node_modules, and npm's failure there reads like a lint failure.
            if (-not (Test-Path 'node_modules')) { & npm install --no-audit --no-fund | Out-Null }
            & npm run lint
            $lint = $LASTEXITCODE
            & npm run build
            if ($LASTEXITCODE -ne 0 -or $lint -ne 0) { $results['console web'] = 1 } else { $results['console web'] = 0 }
        } finally { Pop-Location }

        Say 'dashboard — agent UI build (this is what type-checks the generated API types)'
        Push-Location (Join-Path $root 'agent-ui')
        try {
            if (-not (Test-Path 'node_modules')) { & npm install --no-audit --no-fund | Out-Null }
            & npm run build
            $results['agent ui'] = $LASTEXITCODE
        } finally { Pop-Location }
    }

    Write-Host ''
    Say 'summary'
    foreach ($k in $results.Keys) {
        if ($results[$k] -eq 0) { Write-Host "  PASS  $k" -ForegroundColor Green }
        else { Write-Host "  FAIL  $k (exit $($results[$k]))" -ForegroundColor Red }
    }
}

switch ($Command) {

    'build' {
        if ($Only -eq 'all' -or $Only -eq 'windows') { Build-Windows }
        if ($Only -eq 'all' -or $Only -eq 'linux')   { Build-AgentUi; Invoke-Wsl 'build' }
        if (Use-Deck) {
            Build-Deck
            try { New-DeckyPluginTestStage | Out-Null } catch { Warn "decky plugin: $($_.Exception.Message)" }
        }
        if ($Only -eq 'all' -or $Only -eq 'console') { Build-Console }
    }

    'up' {
        if ($Only -eq 'all' -or $Only -eq 'console') { Start-Console }
        if ($Only -eq 'all' -or $Only -eq 'windows') { Start-Windows }
        if ($Only -eq 'all' -or $Only -eq 'linux')   { Invoke-Wsl 'up' }
        if (Use-Deck) {
            try { Start-Deck } catch { Warn "deck: $($_.Exception.Message)" }
            try { Install-DeckyPluginOnDeck } catch { Warn "decky plugin: $($_.Exception.Message)" }
        }
        Show-RealGameMappings
        Write-Host ''
        Write-Host "console      $serverUrl"
        Write-Host "windows UI   http://localhost:$WinPort"
        Write-Host "linux UI     http://localhost:$LinuxPort   (WSL2 forwards it to Windows)"
        if ($DeckHost) { Write-Host "deck UI      $(Get-DeckUiHint)" }
    }

    'down' {
        Say 'stopping the test rig (the installed agent is not touched)'
        Stop-Windows
        Invoke-Wsl 'down'
        if (Test-DeckConfigured) {
            try { Stop-Deck } catch { Warn "deck: $($_.Exception.Message)" }
        }
        if (-not $AgentsOnly) { Stop-Console }
    }

    'status' {
        $c = Test-ConsoleUp
        if ($c) { Write-Host "console      v$($c.build.version) @ $($c.build.commit)  ($serverUrl)" }
        else    { Write-Host "console      down ($serverUrl)" }

        $w = Get-WinAgentState
        if ($w) { Write-Host "windows      v$($w.buildLabel)  connected=$($w.connected)  :$WinPort" }
        else    { Write-Host "windows      down (:$WinPort)" }

        Invoke-Wsl 'status'
        Show-RealGameMappings

        if (Test-DeckConfigured) {
            try {
                Invoke-Deck 'status'
                Write-Host "deck UI      $(Get-DeckUiHint)"
                $installed = & ssh -o ConnectTimeout=5 $DeckHost `
                    "test -d ~/homebrew/plugins/$deckyTestPluginName && echo yes || echo no"
                Write-Host "decky plugin '$deckyTestPluginName' installed: $installed"
            } catch { Warn "deck: $($_.Exception.Message)" }
        } else {
            Write-Host 'deck         not configured (set -DeckHost or $env:SAVELOCKER_DECK_HOST)'
        }

        $release = Get-Process SaveLocker.Agent -ErrorAction SilentlyContinue
        if ($release) {
            $ver = (Get-Item $release[0].Path).VersionInfo.FileVersion
            Write-Host "installed    v$ver  pid $($release[0].Id)  (untouched by this script)"
        } else {
            Write-Host 'installed    not running'
        }
    }

    'test' { Invoke-Suites }

    'sync' {
        # From git status, never a hand-written list: a hardcoded one went stale mid-session once and
        # the clone silently built an older tree than the one under test.
        $changed = & git -C $root status --porcelain |
            ForEach-Object { $_.Substring(3).Trim('"') } |
            Where-Object { $_ -and (Test-Path (Join-Path $root $_)) }
        if (-not $changed) { Write-Host 'nothing changed to sync'; break }
        $list = Join-Path $env:TEMP 'savelocker-testenv-sync.txt'
        Set-Content -Path $list -Value $changed -Encoding ASCII
        Say "syncing $($changed.Count) changed file(s) into the WSL clone"
        Invoke-Wsl 'sync' $list
        Remove-Item $list -ErrorAction SilentlyContinue
    }

    'logs' {
        $winLog = Join-Path $winState 'agent.log'
        if (Test-Path $winLog) { Say "windows test agent — $winLog"; Get-Content $winLog -Tail 20 }
        # The bash fallback text must be SINGLE-quoted, not double: PowerShell 5.1 mis-serialises an
        # embedded " inside a single-quoted PS string when building a native command line for
        # wsl.exe, and the far side receives "...echo "(none)"" with the quoting broken - a plain
        # bash syntax error, reproduced and confirmed fixed 2026-08-19.
        Say 'wsl daemon'
        & wsl -d $Distro -- bash -c 'tail -20 ~/.savelocker-testenv-daemon.log 2>/dev/null || echo ''(none)'''
        Say 'wsl suite'
        & wsl -d $Distro -- bash -c 'tail -20 ~/.savelocker-testenv-suite.log 2>/dev/null || echo ''(none)'''
        if (Test-DeckConfigured) {
            Say 'deck daemon'
            try { Invoke-Deck 'logs' } catch { Warn "deck: $($_.Exception.Message)" }
        }
    }

    'clean' {
        Say "deleting the test rig's builds and state (the installed agent and any real save data are never touched)"

        Stop-Windows
        if (Test-Path $StateRoot) {
            Remove-Item $StateRoot -Recurse -Force
            Write-Host "  removed $StateRoot"
        }

        Invoke-Wsl 'clean'

        if (Test-DeckConfigured) {
            try { Invoke-Deck 'clean' } catch { Warn "deck: $($_.Exception.Message)" }
            try { Remove-DeckyPluginFromDeck } catch { Warn "decky plugin: $($_.Exception.Message)" }
        }

        if (-not $AgentsOnly) {
            Say 'removing the dashboard container, image and data volume'
            & docker rm -f $container 2>$null | Out-Null
            & docker volume rm $volume 2>$null | Out-Null
            & docker rmi $image 2>$null | Out-Null
            Write-Host "  removed $container / $volume / $image"
        }
    }

    'deck-config' {
        # $DeckHost/$DeckServerUrl already reflect explicit flag > saved file > env var by this
        # point (the loader above), so writing them straight back merges a one-sided edit — e.g.
        # `deck-config -DeckHost deck@newip` alone keeps whatever SAVELOCKER_DECK_SERVER_URL was
        # already saved instead of blanking it.
        if ($PSBoundParameters.ContainsKey('DeckHost') -or $PSBoundParameters.ContainsKey('DeckServerUrl')) {
            if (-not $DeckHost) { throw 'no -DeckHost given and none saved yet' }
            $lines = @(
                "# Written by: testenv.ps1 deck-config — edit by hand or re-run the command."
                "`$env:SAVELOCKER_DECK_HOST = '$DeckHost'"
            )
            if ($DeckServerUrl) { $lines += "`$env:SAVELOCKER_DECK_SERVER_URL = '$DeckServerUrl'" }
            Set-Content -Path $deckConfig -Value $lines -Encoding UTF8
            Say "saved to $deckConfig"
        }

        Write-Host "deck host:        $(if ($DeckHost) { $DeckHost } else { '(not set)' })"
        Write-Host "deck server url:  $(if ($DeckServerUrl) { $DeckServerUrl } else { '(not set)' })"
    }
}
