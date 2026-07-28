# Windows agent bug bounty - regression harness for tasks/WinAgent-BugBounty.md.
#
# The existing suites are CLI- and API-oriented. These are the Windows-specific cases they cannot
# express: a live game process, the updater, ProgramData ACLs, the HKCU Run key, and STA ownership.
# Every check here must FAIL against the pre-fix code.
#
# Findings covered so far:
#
#   WA-01. No pull or restore under a live game process.
#          A pull is the only destructive direction - it replaces the save directory. On Windows
#          ProcessWatcher notices a game up to 4 s AFTER it started, so "pre-launch pull" was a
#          restore underneath a process that already had the save open; the game then wrote over it
#          at exit and the pulled save was silently gone. The fix refuses the pull instead, at the
#          engine (the backstop nothing can route around) and at each surface (for a readable
#          reason). The dashboard-command path is tested too, because that is the one where the
#          person clicking cannot see that the game is open.
#
#   WA-02. A broad Windows directory can never be archived or replaced.
#          Save-folder assignment took any string, and the destructive sync path never checked one
#          at all. A drive root, a user profile, Program Files or the agent's own state directory
#          could be archived - and a force-pull would REPLACE it. Paths arrive from five places
#          (picker, typed local API, enrollment, CLI, server reconciliation), so each validates,
#          and SyncEngine validates again at the archive/restore boundary: a mapping accepted last
#          week can be hand-edited, pushed by the server, or turned into a junction afterwards.
#
#   WA-03. The state directory is private to the enrolling account.
#          %PROGRAMDATA% grants every authenticated user read access and it INHERITS, so any local
#          account could read config.json (this machine's server API key) and api-token (which
#          grants the local management API). The fix severs inheritance and grants only the
#          enrolling user, SYSTEM and Administrators. A real second-account login is in the task's
#          manual verification; what is asserted here is what decides that test's outcome.
#
#   WA-04. A server change is transactional and origin-bound.
#          An unvalidated URL was written to disk BEFORE a client was built from it, so a typo
#          persisted and every later start crashed on the same unusable value. Separately, ApiKey,
#          MachineId and ServerPin survived an origin change, so server A's live machine key was
#          sent to server B. Both are asserted here, the second by inspecting what B RECEIVED.
#
#   WA-05. An update is bound to the current server and verified before it runs.
#          This path ends in EXECUTING a binary, and on the default configuration the transport
#          proves nothing about what arrived (Decisions.md: plain http is supported, no certs are
#          shipped). So the published SHA-256 is the whole control. The updater also accepted an
#          arbitrary absolute DownloadUrl using a client whose default headers carried the machine
#          key, so an off-origin URL was handed this machine's credential.
#
#   WA-06. A lease renewer does not outlive the engine that started it.
#          Replacing _engine dropped the old one; its lease timers are rooted by the runtime timer
#          queue, so they renewed against the OLD server forever while the game's exit released
#          against the NEW one. The old server's lease was then held in perpetuity by a machine that
#          had stopped talking to it, locking every other machine out of that game. Driven through
#          `savelocker run`, the one path that actually holds a lease for a session.
#
#   WA-07. Save-lock contention fails closed.
#          The cross-process lock timed out after 30 s and handed back an UNHELD handle, so the
#          caller proceeded - the precise state the lock exists to prevent. And 30 s was shorter
#          than one normal operation: the settle gate alone may hold it for 120 s, so a healthy
#          exit-push routinely outlasted the wait and the other process barged in.
#
#   WA-08. Enrollment produces usable lifecycle metadata, or says it cannot.
#          A game enrolled through the UI never received ProcessNames, so ProcessWatcher excluded it
#          outright - no lease, no push on quit, and (since WA-01) no refusal to overwrite saves
#          while it is running. The scanner knew the shortcut's executable and threw it away. Where
#          discovery genuinely cannot know - an installed Steam game, a save-root match - the UI now
#          says "launch/exit sync not configured" instead of implying it works.
#
#   WA-09. One thread owns the WinForms objects and the live game list.
#          TrayContext captured SynchronizationContext.Current in its constructor - which runs as
#          the ARGUMENT to Application.Run, before the loop installs the WinForms context. Every
#          "marshal to the UI thread" post in the tray therefore went to the thread pool, silently,
#          including the ones WA-01, WA-05 and WA-06 added. This block drives a real tray on its own
#          port and asserts the owner; see the task file for what the stress checks do and do not
#          prove.
#
# Owns its server on :5189 (and :5190-:5196 for daemons, stub servers and the WA-09 tray) and its
# state under .verify-winagent, so it never collides with a real agent, a dev server, or another
# suite.
# Usage: .\tests\run-winagent-tests.ps1

$ErrorActionPreference = "Continue"

$onWindows = if ($null -eq $IsWindows) { $true } else { $IsWindows }
if (-not $onWindows) { Write-Host "SKIP: Windows-only suite."; exit 0 }

$root    = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root ".verify-winagent"
$server  = "http://localhost:5189"

$inProgramFiles = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
$dotnet = if (Test-Path $inProgramFiles) { $inProgramFiles } else { "dotnet" }
$dll    = Join-Path $root "src/Agent/bin/Debug/net10.0-windows/SaveLocker.Agent.dll"
if (-not (Test-Path $dll)) { Write-Host "Agent not built: $dll"; exit 2 }

$serverDll = Join-Path $root "src/Server/bin/Debug/net10.0/SaveLocker.Server.dll"
if (-not (Test-Path $serverDll)) { Write-Host "Server not built: $serverDll"; exit 2 }

Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $scratch | Out-Null

$pass = 0; $fail = 0
function Check($name, $cond) {
    if ($cond) { Write-Host "PASS: $name"; $script:pass++ }
    else        { Write-Host "FAIL: $name"; $script:fail++ }
}
function Agent { & $dotnet $dll @args 2>&1 }

# ---- This suite owns its server (see Gotchas.md: the DLL ignores launchSettings.json) ----
$state = Join-Path $scratch "state"
New-Item -ItemType Directory -Force (Join-Path $state "archives") | Out-Null
$env:ASPNETCORE_URLS      = $server
$env:Storage__DbPath      = Join-Path $state "savelocker.db"
$env:Storage__ArchiveRoot = Join-Path $state "archives"
$env:Backup__Enabled      = "false"
$env:Logging__EventLog__LogLevel__Default = "None"
# Pinned so the WA-05 tampering check knows exactly which file the server is serving. Left to its
# default it lands beside the server binary, which is shared with every other run.
$env:Storage__AgentInstallerRoot = Join-Path $state "agent-installer"

$serverProc = Start-Process -FilePath $dotnet -ArgumentList $serverDll -PassThru -WindowStyle Hidden
$up = $false
foreach ($i in 1..40) {
    Start-Sleep -Milliseconds 700
    try { Invoke-RestMethod "$server/api/admin/status" -TimeoutSec 3 | Out-Null; $up = $true; break } catch { }
}
if (-not $up) { Write-Host "FAIL: test server did not start on $server"; exit 1 }

# ---- A fake "game": a copy of powershell.exe under a name nothing else on the box uses. ----
# The process NAME is the whole point - that is what TrackedGame.ProcessNames matches on - so a
# renamed copy of a real, long-running executable is a faithful stand-in and needs no build step.
$fakeExe  = Join-Path $scratch "slfakegame.exe"
$fakeName = "slfakegame"
Copy-Item (Get-Process -Id $PID).Path $fakeExe -Force
function Start-FakeGame {
    $p = Start-Process -FilePath $fakeExe -ArgumentList @("-NoProfile", "-Command", "Start-Sleep 600") `
         -PassThru -WindowStyle Hidden
    # Do not race the assertion against process creation.
    foreach ($i in 1..40) {
        Start-Sleep -Milliseconds 250
        if (Get-Process -Name $fakeName -ErrorAction SilentlyContinue) { return $p }
    }
    return $p
}
function Stop-FakeGame($p) {
    if ($p) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    foreach ($i in 1..40) {
        Start-Sleep -Milliseconds 250
        if (-not (Get-Process -Name $fakeName -ErrorAction SilentlyContinue)) { return }
    }
}

try {
    # =================================================================================
    # WA-01. NO PULL OR RESTORE UNDER A LIVE GAME PROCESS
    # =================================================================================
    $stamp    = Get-Date -Format "HHmmss"
    $gameName = "WinAgentGame-$stamp"

    # Two "machines": A publishes the server head, B is the one that would be overwritten.
    $cfgA  = Join-Path $scratch "a.json"
    $cfgB  = Join-Path $scratch "b.json"
    $saveA = Join-Path $scratch "a_save"; New-Item -ItemType Directory -Force $saveA | Out-Null
    $saveB = Join-Path $scratch "b_save"; New-Item -ItemType Directory -Force $saveB | Out-Null
    foreach ($c in @($cfgA, $cfgB)) {
        @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $c -Encoding utf8
    }
    Agent register --name "WinA-$stamp" --config $cfgA | Out-Null
    Agent register --name "WinB-$stamp" --config $cfgB | Out-Null

    "SERVER VERSION" | Set-Content (Join-Path $saveA "save.dat") -Encoding utf8
    Agent add-game --name $gameName --dir $saveA --config $cfgA | Out-Null
    Agent push $gameName --config $cfgA | Out-Null

    # B maps the same game to its own folder and declares the process that owns it.
    "LOCAL VERSION" | Set-Content (Join-Path $saveB "save.dat") -Encoding utf8
    Agent add-game --name $gameName --dir $saveB --proc $fakeName --config $cfgB | Out-Null

    $listed = Agent list --config $cfgB | Out-String
    Check "WA-01 the game records its process name" ($listed -match [regex]::Escape($fakeName))

    # --- The game is running: a force-pull must not touch the save directory ---
    $game = Start-FakeGame
    Check "WA-01 the fake game is running" ($null -ne (Get-Process -Name $fakeName -ErrorAction SilentlyContinue))

    $out = Agent pull $gameName --force --config $cfgB | Out-String
    $after = Get-Content (Join-Path $saveB "save.dat") -Raw

    Check "WA-01 force-pull is refused while the game runs" ($out -match "(?i)refused")
    Check "WA-01 the refusal names the running process"     ($out -match [regex]::Escape($fakeName))
    Check "WA-01 the save directory is UNCHANGED"           ($after.Trim() -eq "LOCAL VERSION")

    # --- The same refusal must reach the dashboard-command path ---
    # This is the surface that matters most: the person issuing it is not at the machine and cannot
    # see that the game is open. The command is drained by the `daemon` host, which lives in the
    # Linux project but runs on Windows and hosts the very same Agent.Core CommandPoller the tray
    # does (the same reason run-local-api-tests.ps1 uses it here).
    $machineB = (Invoke-RestMethod "$server/api/machines") |
                Where-Object { $_.name -eq "WinB-$stamp" }
    # /api/games is agent-authenticated; /api/overview is the admin view of the same games. Its
    # rows are GameStateDto, so the game itself is one level down.
    $gameRow  = (Invoke-RestMethod "$server/api/overview").game |
                Where-Object { $_.name -eq $gameName } | Select-Object -First 1
    Invoke-RestMethod "$server/api/commands" -Method Post `
        -Body (@{ machineId = $machineB.id; gameId = $gameRow.id; type = "Pull"; force = $true } | ConvertTo-Json) `
        -ContentType "application/json" | Out-Null

    $linuxDll = Join-Path $root "src/Agent.Linux/bin/Debug/net10.0/savelocker.dll"
    $daemon = Start-Process -FilePath $dotnet `
        -ArgumentList @($linuxDll, "daemon", "--port", "5190", "--config", $cfgB) `
        -PassThru -WindowStyle Hidden
    # One poller tick is 20 s; 45 s covers the first tick plus the reconcile ahead of it.
    $pullCmd = $null
    foreach ($i in 1..45) {
        Start-Sleep -Seconds 1
        $pullCmd = (Invoke-RestMethod "$server/api/commands") |
                   Where-Object { $_.type -eq "Pull" -and $_.status -ne "Pending" } | Select-Object -First 1
        if ($pullCmd) { break }
    }
    Stop-Process -Id $daemon.Id -Force -ErrorAction SilentlyContinue

    $stillLocal = (Get-Content (Join-Path $saveB "save.dat") -Raw).Trim()
    Check "WA-01 dashboard force-pull leaves the save UNCHANGED" ($stillLocal -eq "LOCAL VERSION")
    Check "WA-01 the dashboard command was executed"    ($null -ne $pullCmd)
    Check "WA-01 the dashboard is told the real reason" ($pullCmd.result -match "(?i)running")

    # --- With the game closed, the very same pull must succeed ---
    # Without this the suite would pass if pull were simply broken.
    Stop-FakeGame $game
    Check "WA-01 the fake game has exited" ($null -eq (Get-Process -Name $fakeName -ErrorAction SilentlyContinue))

    Agent pull $gameName --force --config $cfgB | Out-Null
    $restored = (Get-Content (Join-Path $saveB "save.dat") -Raw).Trim()
    Check "WA-01 the pull succeeds once the game is closed" ($restored -eq "SERVER VERSION")

    # =================================================================================
    # WA-02. A BROAD WINDOWS DIRECTORY CAN NEVER BE ARCHIVED OR REPLACED
    # =================================================================================
    # Configuration-time refusals. Each is a path a force-pull would otherwise DELETE INTO.
    $cfgP = Join-Path $scratch "p.json"
    @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $cfgP -Encoding utf8
    Agent register --name "WinP-$stamp" --config $cfgP | Out-Null

    $bad = [ordered]@{
        "a drive root"          = "C:\"
        "the user profile"      = $env:USERPROFILE
        "the profile container" = (Split-Path $env:USERPROFILE -Parent)
        "Program Files"         = $env:ProgramFiles
        "the Windows directory" = $env:WINDIR
        "the agent state dir"   = $scratch
    }
    foreach ($label in $bad.Keys) {
        $out = Agent add-game --name "Bad-$label-$stamp" --dir $bad[$label] --config $cfgP | Out-String
        Check "WA-02 add-game refuses $label" ($out -match "(?i)refus")
    }

    # A normal folder is still accepted - without this the suite would pass if add-game were broken.
    $okSave = Join-Path $scratch "ok_save"; New-Item -ItemType Directory -Force $okSave | Out-Null
    "ok" | Set-Content (Join-Path $okSave "s.dat") -Encoding utf8
    $okGame = "OkGame-$stamp"
    Agent add-game --name $okGame --dir $okSave --config $cfgP | Out-Null
    $okList = Agent list --config $cfgP | Out-String
    Check "WA-02 an ordinary save folder is still accepted" ($okList -match [regex]::Escape($okGame))

    # --- The sync boundary is the check that actually protects the user ---
    # config.json is hand-editable and the server can push a path, so validating only where the UI
    # accepts a folder defends nothing. Repoint a tracked game at the profile behind the agent's
    # back, exactly as a hand-edit would, and both directions must refuse.
    $raw = Get-Content $cfgP -Raw | ConvertFrom-Json
    ($raw.Games | Where-Object { $_.Name -eq $okGame }).SaveDirectory = $env:USERPROFILE
    $raw | ConvertTo-Json -Depth 10 | Set-Content $cfgP -Encoding utf8

    $pushOut = Agent push $okGame --config $cfgP | Out-String
    $pullOut = Agent pull $okGame --force --config $cfgP | Out-String
    Check "WA-02 push refuses a hand-edited profile mapping" ($pushOut -match "(?i)REFUSED")
    Check "WA-02 pull refuses a hand-edited profile mapping" ($pullOut -match "(?i)REFUSED")
    Check "WA-02 the refusal names the user profile"         ($pullOut -match "(?i)user profile")

    # --- A path that BECOMES a reparse point after the mapping was accepted ---
    # Config-time canonicalization stores the link's resolved target, so re-pointing a junction the
    # user picked is already inert. The case that remains is the stored path itself turning into a
    # link - the literal string in config.json never changes, so a string comparison would pass it
    # forever. That is what the sync-time re-canonicalization is for.
    $linkSave = Join-Path $scratch "link_save"; New-Item -ItemType Directory -Force $linkSave | Out-Null
    "link" | Set-Content (Join-Path $linkSave "s.dat") -Encoding utf8

    $linkGame = "LinkGame-$stamp"
    Agent add-game --name $linkGame --dir $linkSave --config $cfgP | Out-Null
    $linkList = Agent list --config $cfgP | Out-String
    Check "WA-02 an ordinary folder maps normally" ($linkList -match [regex]::Escape($linkGame))

    Remove-Item $linkSave -Force -Recurse -ErrorAction SilentlyContinue
    New-Item -ItemType Junction -Path $linkSave -Target $env:USERPROFILE | Out-Null
    $linkPull = Agent pull $linkGame --force --config $cfgP | Out-String
    $linkPush = Agent push $linkGame --config $cfgP | Out-String
    Check "WA-02 a path that became a junction is refused at pull" ($linkPull -match "(?i)REFUSED")
    Check "WA-02 a path that became a junction is refused at push" ($linkPush -match "(?i)REFUSED")
    Remove-Item $linkSave -Force -Recurse -ErrorAction SilentlyContinue

    # --- A hostile or mistaken SERVER path must not repoint the agent ---
    # This is the only path source with no local confirmation step at all.
    $machineP = (Invoke-RestMethod "$server/api/machines") | Where-Object { $_.name -eq "WinP-$stamp" }
    $okRow    = (Invoke-RestMethod "$server/api/overview").game |
                Where-Object { $_.name -eq $okGame } | Select-Object -First 1
    $encoded  = [uri]::EscapeDataString($env:USERPROFILE)
    Invoke-RestMethod "$server/api/games/$($okRow.id)/paths/$($machineP.id)?value=$encoded" -Method Post | Out-Null

    # Put the local mapping back to something sane, so the only way it can become the profile is by
    # the agent ACCEPTING the server's value.
    $raw2 = Get-Content $cfgP -Raw | ConvertFrom-Json
    ($raw2.Games | Where-Object { $_.Name -eq $okGame }).SaveDirectory = $okSave
    $raw2 | ConvertTo-Json -Depth 10 | Set-Content $cfgP -Encoding utf8

    $daemon2 = Start-Process -FilePath $dotnet `
        -ArgumentList @($linuxDll, "daemon", "--port", "5191", "--config", $cfgP) `
        -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 30   # one reconcile tick (20 s) plus margin
    Stop-Process -Id $daemon2.Id -Force -ErrorAction SilentlyContinue

    $afterCfg = Get-Content $cfgP -Raw | ConvertFrom-Json
    $mapped = ($afterCfg.Games | Where-Object { $_.Name -eq $okGame }).SaveDirectory
    Check "WA-02 a server-issued profile path is NOT adopted" ($mapped -ne $env:USERPROFILE)
    Check "WA-02 the previous mapping survives the refusal"   ($mapped -eq $okSave)

    # =================================================================================
    # WA-03. THE STATE DIRECTORY IS PRIVATE TO THE ENROLLING ACCOUNT
    # =================================================================================
    # %PROGRAMDATA% grants every authenticated user read access and that INHERITS, so any local
    # account could read config.json (the machine's server API key) and api-token (which grants the
    # local management API). These checks are about the ACL, not about the files' contents.
    #
    # A real two-account test needs a second Windows login and is listed in the task's manual
    # verification. What is asserted here is the thing that decides the outcome of that test: the
    # inherited grant to ordinary users is gone, and only the three intended identities remain.
    $aclDir = Join-Path $scratch "acl_state"
    $aclCfg = Join-Path $aclDir "config.json"
    New-Item -ItemType Directory -Force $aclDir | Out-Null

    # A file written BEFORE the agent ever runs, carrying the permissive inherited ACL. A fresh fix
    # that only protects newly created files would leave this one readable.
    "stale" | Set-Content (Join-Path $aclDir "agent.log") -Encoding utf8

    @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $aclCfg -Encoding utf8
    Agent register --name "WinAcl-$stamp" --config $aclCfg | Out-Null

    # api-token is minted by the local API server, not the CLI, so the daemon has to run once for
    # the second credential to exist at all.
    $daemon3 = Start-Process -FilePath $dotnet `
        -ArgumentList @($linuxDll, "daemon", "--port", "5192", "--config", $aclCfg) `
        -PassThru -WindowStyle Hidden
    foreach ($i in 1..30) {
        Start-Sleep -Milliseconds 500
        if (Test-Path (Join-Path $aclDir "api-token")) { break }
    }
    Stop-Process -Id $daemon3.Id -Force -ErrorAction SilentlyContinue

    $acl = Get-Acl $aclDir
    Check "WA-03 the state directory stops inheriting from ProgramData" ($acl.AreAccessRulesProtected)

    # The identities that must NOT appear. Resolved from well-known SIDs rather than names, because
    # the display names are localised and would silently pass on a non-English Windows.
    $strangers = @('S-1-5-11', 'S-1-5-32-545', 'S-1-1-0')  # Authenticated Users, Users, Everyone
    $present = $acl.Access | ForEach-Object {
        try { $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value }
        catch { $null }
    }
    foreach ($sid in $strangers) {
        Check "WA-03 the state directory does not grant $sid" (-not ($present -contains $sid))
    }

    $me = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value
    Check "WA-03 the enrolling account keeps access" ($present -contains $me)
    Check "WA-03 SYSTEM keeps access"                ($present -contains 'S-1-5-18')
    Check "WA-03 Administrators keep access"         ($present -contains 'S-1-5-32-544')

    # The two credentials themselves. Both must end up with the directory's rules and nothing wider.
    foreach ($leaf in @("config.json", "api-token", "agent.log")) {
        $f = Join-Path $aclDir $leaf
        if (-not (Test-Path $f)) { Check "WA-03 $leaf exists to be checked" $false; continue }
        $facl = Get-Acl $f
        $fpresent = $facl.Access | ForEach-Object {
            try { $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value }
            catch { $null }
        }
        $leaked = @($strangers | Where-Object { $fpresent -contains $_ })
        Check "WA-03 $leaf is not readable by other local users" ($leaked.Count -eq 0)
    }

    # The agent must still be able to use its own state after locking it down - a fix that made the
    # credential unreadable to the agent too would pass every check above.
    $who = Agent whoami --config $aclCfg | Out-String
    Check "WA-03 the agent can still read its own config" ($who -match "WinAcl-$stamp")

    # =================================================================================
    # WA-04. A SERVER CHANGE IS TRANSACTIONAL AND ORIGIN-BOUND
    # =================================================================================
    # Two separate defects. First: an unvalidated URL was written to disk before a client was built
    # from it, so a typo persisted and every later start crashed on it. Second: ApiKey, MachineId
    # and ServerPin were kept across an origin change, so server A's live credential was sent to
    # server B.
    $svCfg = Join-Path $scratch "server_change.json"
    @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $svCfg -Encoding utf8
    Agent register --name "WinSv-$stamp" --config $svCfg | Out-Null

    $beforeCfg = Get-Content $svCfg -Raw | ConvertFrom-Json
    $keyA = $beforeCfg.ApiKey
    Check "WA-04 the agent holds a key for server A" (-not [string]::IsNullOrWhiteSpace($keyA))

    # --- A typo must not reach disk, and must not brick the next start ---
    foreach ($badUrl in @("htp://typo", "not a url", "ftp://host:21", "localhost:5189")) {
        $out = Agent set-server --url $badUrl --config $svCfg | Out-String
        $now = (Get-Content $svCfg -Raw | ConvertFrom-Json).ServerUrl
        Check "WA-04 '$badUrl' is refused"                 ($out -match "(?i)valid server URL")
        Check "WA-04 '$badUrl' never reached disk"         ($now -eq $server)
    }

    # The agent still starts and still knows who it is - that is what "cannot brick startup" means.
    $whoSv = Agent whoami --config $svCfg | Out-String
    Check "WA-04 the agent still starts after the typos" ($whoSv -match "WinSv-$stamp")

    # --- A cosmetic edit is not an origin change and must keep the enrollment ---
    Agent set-server --url "$server/" --config $svCfg | Out-Null
    $sameOrigin = Get-Content $svCfg -Raw | ConvertFrom-Json
    Check "WA-04 a trailing slash keeps the machine key" ($sameOrigin.ApiKey -eq $keyA)

    # --- A real origin change drops the credentials issued by the old server ---
    Agent set-server --url "http://localhost:5999" --config $svCfg | Out-Null
    $afterMove = Get-Content $svCfg -Raw | ConvertFrom-Json
    Check "WA-04 the server URL moved"                  ($afterMove.ServerUrl -eq "http://localhost:5999")
    Check "WA-04 server A's API key was dropped"        ([string]::IsNullOrWhiteSpace($afterMove.ApiKey))
    Check "WA-04 server A's machine id was dropped"     ($null -eq $afterMove.MachineId)
    Check "WA-04 server A's TLS pin was dropped"        ([string]::IsNullOrWhiteSpace($afterMove.ServerPin))

    # --- and A's key must never be presented to B ---
    # A throwaway listener stands in for server B. What is asserted is what it RECEIVED: the old
    # key must not appear in any header of any request the agent makes after the switch.
    $listenerB = [System.Net.HttpListener]::new()
    $listenerB.Prefixes.Add("http://localhost:5999/")
    $listenerB.Start()
    $sawKeyA = $false
    $sawAny  = $false
    try {
        $svDaemon = Start-Process -FilePath $dotnet `
            -ArgumentList @($linuxDll, "daemon", "--port", "5193", "--config", $svCfg) `
            -PassThru -WindowStyle Hidden
        $deadline = (Get-Date).AddSeconds(45)
        $ctx = $listenerB.GetContextAsync()
        while ((Get-Date) -lt $deadline) {
            if ($ctx.Wait(200)) {
                $c = $ctx.Result
                $sawAny = $true
                foreach ($h in $c.Request.Headers.AllKeys) {
                    if ($c.Request.Headers[$h] -eq $keyA) { $sawKeyA = $true }
                }
                $c.Response.StatusCode = 404; $c.Response.Close()
                $ctx = $listenerB.GetContextAsync()
            }
        }
        Stop-Process -Id $svDaemon.Id -Force -ErrorAction SilentlyContinue
    }
    finally { $listenerB.Stop(); $listenerB.Close() }

    # Reported, not asserted. Whether B is contacted at all depends on which subsystem ticks first
    # and is not the point; the point is what a request CARRIED. Printing it stops a future reader
    # assuming this check passed because nothing was ever sent.
    Write-Host "       (server B received $(if ($sawAny) { 'at least one' } else { 'no' }) request)"
    Check "WA-04 server A's key never reached server B" (-not $sawKeyA)

    # =================================================================================
    # WA-05. AN UPDATE IS BOUND TO THE CURRENT SERVER AND VERIFIED BEFORE IT RUNS
    # =================================================================================
    # This is the path that ends in EXECUTING a binary, and on the default configuration the
    # transport proves nothing about what arrived (Decisions.md: plain http is supported and ships
    # no certs). So the digest is the whole control, and these assert it is actually enforced.
    $upCfg = Join-Path $scratch "update.json"
    @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $upCfg -Encoding utf8
    Agent register --name "WinUp-$stamp" --config $upCfg | Out-Null

    # An "installer": a real PE file so the MZ check passes on the happy path. A renamed copy of a
    # system exe is the cheapest way to get genuine executable bytes.
    $fakeInstaller = Join-Path $scratch "SaveLocker-Agent-Setup-9.9.9.exe"
    Copy-Item (Get-Process -Id $PID).Path $fakeInstaller -Force
    $trueHash = (Get-FileHash $fakeInstaller -Algorithm SHA256).Hash.ToLowerInvariant()

    # Upload it as the hosted installer, at a version far above the running agent's.
    # Built by hand rather than with Invoke-RestMethod -Form, which is PowerShell 7+ only and this
    # suite has to run under Windows PowerShell 5.1.
    Add-Type -AssemblyName System.Net.Http
    $uploaded = $false
    try {
        $client  = New-Object System.Net.Http.HttpClient
        $content = New-Object System.Net.Http.MultipartFormDataContent
        $bytes   = [System.IO.File]::ReadAllBytes($fakeInstaller)
        $part    = New-Object System.Net.Http.ByteArrayContent (,$bytes)
        $part.Headers.ContentType =
            [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")
        $content.Add($part, "file", [System.IO.Path]::GetFileName($fakeInstaller))
        $resp = $client.PostAsync("$server/api/admin/agent-installer?version=9.9.9", $content).GetAwaiter().GetResult()
        $uploaded = $resp.IsSuccessStatusCode
        if (-not $uploaded) { Write-Host "upload failed: $([int]$resp.StatusCode) $($resp.Content.ReadAsStringAsync().GetAwaiter().GetResult())" }
        $client.Dispose()
    } catch { Write-Host "upload failed: $_" }
    Check "WA-05 the installer uploaded" $uploaded

    $status = Invoke-RestMethod "$server/api/admin/agent-installer"
    Check "WA-05 the server recorded a SHA-256 at upload"  ($status.sha256 -eq $trueHash)

    # The digest must reach the agent, or it has nothing to verify against.
    $latest = Invoke-RestMethod "$server/api/agent/latest" -Headers @{ "X-Api-Key" = (Get-Content $upCfg -Raw | ConvertFrom-Json).ApiKey }
    Check "WA-05 /api/agent/latest publishes the digest"   ($latest.sha256 -eq $trueHash)

    # --- The happy path: same origin, correct digest, real executable ---
    $ok = Agent check-update --download --config $upCfg | Out-String
    Check "WA-05 a correct installer is VERIFIED"          ($ok -match "VERIFIED")

    # --- A tampered artifact must be refused ---
    # The bytes on disk are changed behind the server's back, so the published digest no longer
    # describes what a download returns. This is the substituted-payload case, and the one the
    # digest exists for.
    $hosted = Get-ChildItem $env:Storage__AgentInstallerRoot -Filter "*.exe" | Select-Object -First 1
    Check "WA-05 the hosted installer file was found" ($null -ne $hosted)
    if ($hosted) {
        Add-Content -Path $hosted.FullName -Value "tampered" -Encoding utf8
        $bad = Agent check-update --download --config $upCfg | Out-String
        Check "WA-05 a tampered installer is REFUSED" ($bad -match "REFUSED")
        Check "WA-05 the refusal names the checksum"  ($bad -match "(?i)checksum")
    }

    # --- An off-origin download with no digest is refused before any request is made ---
    # AgentUpdate:DownloadUrl can point anywhere. A second server instance is started with the
    # config-fallback path aimed at a foreign listener and NO AgentUpdate:Sha256 - the exact shape
    # of a hand-configured update source with no integrity control.
    $extPort  = 5994
    $altPort  = 5195
    $altUrl   = "http://localhost:$altPort"
    $extHits  = 0
    $extSawKey = $false

    $extListener = [System.Net.HttpListener]::new()
    $extListener.Prefixes.Add("http://localhost:$extPort/")
    $extListener.Start()

    $altState = Join-Path $scratch "alt-state"
    New-Item -ItemType Directory -Force (Join-Path $altState "archives") | Out-Null

    # Start-Process -Environment is PowerShell 7.4+, so the variables are set on THIS process (which
    # the child inherits) and put back immediately after. Everything the main server needs is
    # re-established below, because these names overlap.
    $savedDb = $env:Storage__DbPath; $savedArch = $env:Storage__ArchiveRoot
    $savedInst = $env:Storage__AgentInstallerRoot; $savedUrls = $env:ASPNETCORE_URLS
    $env:ASPNETCORE_URLS             = $altUrl
    $env:Storage__DbPath             = Join-Path $altState "savelocker.db"
    $env:Storage__ArchiveRoot        = Join-Path $altState "archives"
    $env:Storage__AgentInstallerRoot = Join-Path $altState "agent-installer"
    $env:AgentUpdate__LatestVersion  = "9.9.9"
    $env:AgentUpdate__DownloadUrl    = "http://localhost:$extPort/SaveLocker-Agent-Setup-9.9.9.exe"
    $altProc = Start-Process -FilePath $dotnet -ArgumentList $serverDll -PassThru -WindowStyle Hidden
    $env:ASPNETCORE_URLS             = $savedUrls
    $env:Storage__DbPath             = $savedDb
    $env:Storage__ArchiveRoot        = $savedArch
    $env:Storage__AgentInstallerRoot = $savedInst
    Remove-Item Env:AgentUpdate__LatestVersion, Env:AgentUpdate__DownloadUrl -ErrorAction SilentlyContinue

    try {
        $altUp = $false
        foreach ($i in 1..40) {
            Start-Sleep -Milliseconds 700
            try { Invoke-RestMethod "$altUrl/api/admin/status" -TimeoutSec 3 | Out-Null; $altUp = $true; break } catch { }
        }
        Check "WA-05 the fallback-config server started" $altUp

        $extCfg = Join-Path $scratch "ext.json"
        @{ ServerUrl = $altUrl; Games = @() } | ConvertTo-Json | Set-Content -Path $extCfg -Encoding utf8
        Agent register --name "WinExt-$stamp" --config $extCfg | Out-Null

        $extOut = Agent check-update --download --config $extCfg | Out-String
        Check "WA-05 an off-origin update with no digest is REFUSED" ($extOut -match "REFUSED")
        Check "WA-05 the refusal names the foreign host"             ($extOut -match "localhost")

        # Drain anything the listener received. Refused BEFORE any request means zero.
        $t = $extListener.GetContextAsync()
        while ($t.Wait(500)) {
            $c = $t.Result
            $extHits++
            if ($c.Request.Headers["X-Api-Key"]) { $extSawKey = $true }
            $c.Response.StatusCode = 404; $c.Response.Close()
            $t = $extListener.GetContextAsync()
        }
        Check "WA-05 nothing was downloaded from the foreign host" ($extHits -eq 0)
        Check "WA-05 the machine key never reached the foreign host" (-not $extSawKey)
    }
    finally {
        Stop-Process -Id $altProc.Id -Force -ErrorAction SilentlyContinue
        $extListener.Stop(); $extListener.Close()
    }

    # =================================================================================
    # WA-06. A LEASE RENEWER DOES NOT OUTLIVE THE ENGINE THAT STARTED IT
    # =================================================================================
    # Replacing _engine dropped the old one on the floor. Its lease timers are rooted by the
    # runtime's timer queue, so they kept renewing against the OLD server indefinitely, while the
    # game's exit ran through the NEW engine and released against the NEW server - which had never
    # issued anything. The old server's lease was then held forever by a machine that had stopped
    # talking to it, locking every other machine out of that game.
    #
    # Driven through the real launch wrapper (`savelocker run`), which is the one code path that
    # actually takes a lease and holds it for a session. The renew interval is accelerated by env
    # var, because the interval is the very thing under test - at the production three hours nothing
    # observable happens.
    $leasePort = 5996
    $leaseUrl  = "http://localhost:$leasePort"
    $leaseHits = @{ acquire = 0; renew = 0; release = 0 }

    $leaseListener = [System.Net.HttpListener]::new()
    $leaseListener.Prefixes.Add("$leaseUrl/")
    $leaseListener.Start()

    # Answers just enough for the wrapper to get through acquire -> renew -> exit -> release.
    function Pump-Lease($seconds) {
        $deadline = (Get-Date).AddSeconds($seconds)
        $t = $leaseListener.GetContextAsync()
        while ((Get-Date) -lt $deadline) {
            if (-not $t.Wait(200)) { continue }
            $c = $t.Result
            $path = $c.Request.Url.AbsolutePath
            $method = $c.Request.HttpMethod
            $body = "{}"

            if ($path -match '/lease/renew$')      { $script:leaseHits.renew++ }
            elseif ($path -match '/lease$' -and $method -eq 'POST') {
                $script:leaseHits.acquire++
                $body = '{"granted":true,"lease":{"gameId":"00000000-0000-0000-0000-000000000000","holderMachineId":null,"holderMachineName":null,"acquiredAt":null,"expiresAt":null}}'
            }
            elseif ($path -match '/lease$' -and $method -eq 'DELETE') { $script:leaseHits.release++ }

            $c.Response.StatusCode = 200
            $c.Response.ContentType = "application/json"
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
            $c.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            $c.Response.Close()
            $t = $leaseListener.GetContextAsync()
        }
    }

    try {
        $leaseCfg  = Join-Path $scratch "lease.json"
        $leaseSave = Join-Path $scratch "lease_save"
        New-Item -ItemType Directory -Force $leaseSave | Out-Null
        "save" | Set-Content (Join-Path $leaseSave "s.dat") -Encoding utf8

        @{
            ServerUrl = $leaseUrl; MachineName = "WinLease-$stamp"
            ApiKey = "lease-test-key"; MachineId = [guid]::NewGuid().ToString()
            Games = @(@{
                GameId = [guid]::NewGuid().ToString(); Name = "LeaseGame"
                SaveDirectory = $leaseSave; SteamAppId = "424242"
            })
        } | ConvertTo-Json -Depth 5 | Set-Content -Path $leaseCfg -Encoding utf8

        # 2 s renewals; the child lives ~12 s, so several must land before it exits.
        $env:SAVELOCKER_LEASE_RENEW_SECONDS = "2"
        $env:SteamAppId = "424242"
        $runProc = Start-Process -FilePath $dotnet -WindowStyle Hidden -PassThru `
            -ArgumentList @($linuxDll, "run", "--config", $leaseCfg, "--",
                            $fakeExe, "-NoProfile", "-Command", "Start-Sleep 12")
        Remove-Item Env:SAVELOCKER_LEASE_RENEW_SECONDS, Env:SteamAppId -ErrorAction SilentlyContinue

        # Long enough to cover the child (~12 s), then the exit push's settle gate (10 s) and the
        # upload, and only then the release. Cutting this short reads as "release never happened".
        Pump-Lease 45
        $runProc.WaitForExit(20000) | Out-Null

        Check "WA-06 the wrapper acquired a lease"          ($leaseHits.acquire -ge 1)
        Check "WA-06 the lease was renewed while playing"   ($leaseHits.renew -ge 2)
        Check "WA-06 the lease was released on exit"        ($leaseHits.release -ge 1)

        # THE assertion. The wrapper has exited, so its engine is disposed. A timer that survived
        # disposal would fire roughly every 2 s; over 15 s that is ~7 renewals. Zero is required.
        $renewsAtExit = $leaseHits.renew
        Pump-Lease 15
        Check "WA-06 NO renewal happens after the engine is retired" ($leaseHits.renew -eq $renewsAtExit)
    }
    finally {
        $leaseListener.Stop(); $leaseListener.Close()
        Remove-Item Env:SAVELOCKER_LEASE_RENEW_SECONDS, Env:SteamAppId -ErrorAction SilentlyContinue
    }

    # =================================================================================
    # WA-07. SAVE-LOCK CONTENTION FAILS CLOSED
    # =================================================================================
    # The cross-process lock timed out after 30 s and then handed back an UNHELD handle so the
    # caller proceeded anyway - the precise state the lock exists to prevent. Worse, 30 s is shorter
    # than one normal operation: the settle gate alone may hold it for SettleMaxWaitSeconds (120 by
    # default), so a perfectly healthy exit-push routinely outlasted the wait and the second process
    # concluded the first was stuck and barged in.
    #
    # Here process A holds the game lock the way a real settle does, and process B must refuse
    # rather than touch the save directory.
    $lockCfg  = Join-Path $scratch "lock.json"
    $lockSave = Join-Path $scratch "lock_save"
    New-Item -ItemType Directory -Force $lockSave | Out-Null
    "ORIGINAL" | Set-Content (Join-Path $lockSave "s.dat") -Encoding utf8

    @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $lockCfg -Encoding utf8
    Agent register --name "WinLock-$stamp" --config $lockCfg | Out-Null
    $lockGame = "LockGame-$stamp"
    Agent add-game --name $lockGame --dir $lockSave --config $lockCfg | Out-Null
    Agent push $lockGame --config $lockCfg | Out-Null

    # Diverge locally, so a force-pull WOULD rewrite the folder if it got through.
    "LOCAL EDIT" | Set-Content (Join-Path $lockSave "s.dat") -Encoding utf8

    $lockGameId = (Get-Content $lockCfg -Raw | ConvertFrom-Json).Games[0].GameId
    $lockFile = Join-Path $scratch "locks\game-$($lockGameId -replace '-','').lock"

    # Stand in for a process mid-settle: hold the exact lock file with the same share mode the
    # agent uses. No agent needs to be running - what is under test is the waiting side.
    $holder = [System.IO.File]::Open($lockFile, 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        # The real policy is ~13 minutes (settle gate + upload window + margin), which a suite cannot
        # wait out twice. Shortened here so the WAIT is still observable but the test is not.
        $env:SAVELOCKER_SYNC_LOCK_SECONDS = "20"
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $pullOut = Agent pull $lockGame --force --config $lockCfg 2>&1 | Out-String
        $pushOut = Agent push $lockGame --force --config $lockCfg 2>&1 | Out-String
        $sw.Stop()

        $content = (Get-Content (Join-Path $lockSave "s.dat") -Raw).Trim()

        Check "WA-07 a contended pull does NOT touch the save directory" ($content -eq "LOCAL EDIT")
        Check "WA-07 the contended pull reports being blocked" (
            $pullOut -match "(?i)another SaveLocker process|Busy")
        Check "WA-07 the contended push reports being blocked" (
            $pushOut -match "(?i)another SaveLocker process|Busy")

        # It must have WAITED, not failed instantly - a lock that gives up at once would pass the
        # assertions above while being useless for real contention.
        # Two operations at a 20 s wait each: anything under ~30 s means it gave up early rather
        # than genuinely blocking, which would pass the assertions above while being useless.
        Check "WA-07 the waiter actually waited" ($sw.Elapsed.TotalSeconds -ge 30)
    }
    finally {
        $holder.Dispose()
        Remove-Item Env:SAVELOCKER_SYNC_LOCK_SECONDS -ErrorAction SilentlyContinue
    }

    # Once the holder lets go, the very same operations succeed - without this the suite would pass
    # if syncing were simply broken.
    $after = Agent pull $lockGame --force --config $lockCfg 2>&1 | Out-String
    $restored = (Get-Content (Join-Path $lockSave "s.dat") -Raw).Trim()
    Check "WA-07 the pull succeeds once the lock is released" ($restored -eq "ORIGINAL")

    # =================================================================================
    # WA-08. ENROLLMENT PRODUCES USABLE LIFECYCLE METADATA, OR SAYS IT CANNOT
    # =================================================================================
    # A game enrolled through the UI never received ProcessNames, so ProcessWatcher excluded it
    # outright: no lease, no push when you quit, and - since WA-01 - no refusal to overwrite saves
    # while it is running. The scanner knew the shortcut's executable and threw it away.
    #
    # Driven through the local API, which is what the UI actually calls.
    $enrCfg = Join-Path $scratch "enroll_proc.json"
    @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $enrCfg -Encoding utf8
    Agent register --name "WinEnr-$stamp" --config $enrCfg | Out-Null

    # Added BEFORE the daemon starts: a running daemon holds the game list in memory and does not
    # re-read config.json, so a CLI write behind its back is invisible to it.
    $enrGame = "EnrGame-$stamp"
    $enrSave = Join-Path $scratch "enr_save"; New-Item -ItemType Directory -Force $enrSave | Out-Null
    "s" | Set-Content (Join-Path $enrSave "s.dat") -Encoding utf8
    Agent add-game --name $enrGame --dir $enrSave --config $enrCfg | Out-Null

    $enrPort = 5194
    $enrBase = "http://localhost:$enrPort"
    $enrDaemon = Start-Process -FilePath $dotnet `
        -ArgumentList @($linuxDll, "daemon", "--port", "$enrPort", "--config", $enrCfg) `
        -PassThru -WindowStyle Hidden
    try {
        $enrToken = $null
        foreach ($i in 1..40) {
            Start-Sleep -Milliseconds 700
            $tp = Join-Path $scratch "api-token"
            if (Test-Path $tp) { $enrToken = (Get-Content $tp -Raw).Trim() }
            try { Invoke-RestMethod "$enrBase/api/state" -Headers @{ "X-SaveLocker-Token" = $enrToken } -TimeoutSec 3 | Out-Null; break } catch { }
        }
        $H = @{ "X-SaveLocker-Token" = $enrToken }

        # A game with no process mapping - the state every GUI enrollment used to land in. The API
        # must report it as such rather than let the UI imply sync is working.
        $glist = Invoke-RestMethod "$enrBase/api/games" -Headers $H
        $row = $glist | Where-Object { $_.name -eq $enrGame }
        Check "WA-08 the local API exposes processNames"        ($null -ne $row -and $null -ne $row.processNames)
        Check "WA-08 an unmapped game reports NO process names" ($row.processNames.Count -eq 0)

        # The user supplies it, the way the Settings row's "Set game process" does.
        Invoke-RestMethod "$enrBase/api/games/$($row.id)/processes" -Method Post -Headers $H `
            -Body (@{ processNames = @("MyGame.exe", " other.exe ") } | ConvertTo-Json) `
            -ContentType "application/json" | Out-Null

        $glist2 = Invoke-RestMethod "$enrBase/api/games" -Headers $H
        $row2 = $glist2 | Where-Object { $_.name -eq $enrGame }
        Check "WA-08 process names are accepted"        ($row2.processNames.Count -eq 2)
        Check "WA-08 the .exe extension is stripped"    ($row2.processNames -contains "MyGame")
        Check "WA-08 surrounding whitespace is trimmed" ($row2.processNames -contains "other")
    }
    finally { Stop-Process -Id $enrDaemon.Id -Force -ErrorAction SilentlyContinue }

    # It must SURVIVE A RESTART - the mapping lives in config.json, and a value the watcher rebuilds
    # from on every start is worthless if it only existed in memory.
    $persisted = (Get-Content $enrCfg -Raw | ConvertFrom-Json).Games |
                 Where-Object { $_.Name -eq $enrGame }
    Check "WA-08 the mapping is persisted to config.json" ($persisted.ProcessNames -contains "MyGame")

    $listAfter = Agent list --config $enrCfg | Out-String
    Check "WA-08 the watcher map is rebuilt after restart" ($listAfter -match "procs=MyGame")

    # And the derivation the scanner relies on: a shortcut's exe path becomes a process name.
    # Asserted through the CLI, since a real shortcuts.vdf is not available in this harness.
    $derCfg = Join-Path $scratch "derive.json"
    @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $derCfg -Encoding utf8
    Agent register --name "WinDer-$stamp" --config $derCfg | Out-Null
    Agent add-game --name "DerGame-$stamp" --dir $enrSave --proc "C:\Games\Foo\foo.exe" --config $derCfg | Out-Null
    $derList = Agent list --config $derCfg | Out-String
    Check "WA-08 an executable path is reduced to a process name" ($derList -match "procs=foo")

    # =================================================================================
    # WA-09. ONE THREAD OWNS THE WINFORMS OBJECTS AND THE LIVE GAME LIST
    # =================================================================================
    # TrayContext is constructed as the ARGUMENT to Application.Run, so its constructor ran before
    # the message loop installed WindowsFormsSynchronizationContext. The captured context therefore
    # fell through to `new SynchronizationContext()` and every _ui.Post in the tray - the menu
    # rebuild, the balloon, the first-run prompt, the deep link, and the ones WA-01/05/06 added -
    # was a plain thread-pool post. Nothing announced it: WinForms only throws on a cross-thread
    # call once a handle exists.
    #
    # This is the one block that drives a REAL tray, because the defect is in the tray's startup
    # sequence and nothing else reproduces it. It runs on its own port so it cannot collide with an
    # installed agent (SAVELOCKER_TRAY_PORT, which scopes the single-instance mutex too).
    #
    # Read the honesty note in tasks/WinAgent-BugBounty.md before trusting this block: the owner
    # assertion is the only one that discriminates against pre-fix code. The stress assertions are
    # real but probabilistic - an off-thread menu rebuild often gets away with it.
    if (-not [Environment]::UserInteractive) {
        Write-Host "SKIP: WA-09 needs an interactive desktop session for the tray."
    } else {
        $trayPort = 5196
        $trayBase = "http://localhost:$trayPort"
        $trayCfg  = Join-Path $scratch "tray.json"
        @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $trayCfg -Encoding utf8
        Agent register --name "WinTray-$stamp" --config $trayCfg | Out-Null

        $trayGame = "TrayGame-$stamp"
        $traySave = Join-Path $scratch "tray_save"; New-Item -ItemType Directory -Force $traySave | Out-Null
        "t" | Set-Content (Join-Path $traySave "t.dat") -Encoding utf8
        Agent add-game --name $trayGame --dir $traySave --proc $fakeName --config $trayCfg | Out-Null

        # The tray logs to the REAL %PROGRAMDATA%\SaveLocker\agent.log - AgentLogger has no --config
        # override - so only the tail this run appends is examined.
        $logPath = Join-Path $env:ProgramData "SaveLocker\agent.log"
        $logMark = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }
        function Get-TrayLog {
            if (-not (Test-Path $logPath)) { return "" }
            $fs = [System.IO.File]::Open($logPath, 'Open', 'Read', 'ReadWrite')
            try {
                if ($fs.Length -lt $logMark) { $script:logMark = 0 }  # rotated
                $fs.Seek($logMark, 'Begin') | Out-Null
                $sr = New-Object System.IO.StreamReader($fs)
                return $sr.ReadToEnd()
            } finally { $fs.Dispose() }
        }

        $env:SAVELOCKER_TRAY_PORT = "$trayPort"
        $tray = Start-Process -FilePath $dotnet -ArgumentList @($dll, "--config", $trayCfg) `
                -PassThru -WindowStyle Hidden
        $trayFake = $null
        try {
            $trayToken = $null
            $trayUp = $false
            foreach ($i in 1..40) {
                Start-Sleep -Milliseconds 700
                $tp = Join-Path $scratch "api-token"
                if (Test-Path $tp) { $trayToken = (Get-Content $tp -Raw).Trim() }
                try {
                    Invoke-RestMethod "$trayBase/api/state" -Headers @{ "X-SaveLocker-Token" = $trayToken } -TimeoutSec 3 | Out-Null
                    $trayUp = $true; break
                } catch { }
            }
            Check "WA-09 the tray started and serves its local API" $trayUp
            $TH = @{ "X-SaveLocker-Token" = $trayToken }

            # THE discriminating assertion. Pre-fix there is no owner at all to name, and the context
            # that was captured is the thread-pool default.
            Check "WA-09 the UI owner is the WinForms context" (
                (Get-TrayLog) -match "UI owner:.*WindowsFormsSynchronizationContext")

            # A game created on the server by ANOTHER machine, so the tray's CommandPoller adopts it
            # on a background tick - a MutateGames from the poller thread while the API thread below
            # enumerates the same list. This is the collection-modified race in its real shape.
            $otherCfg = Join-Path $scratch "tray_other.json"
            @{ ServerUrl = $server; Games = @() } | ConvertTo-Json | Set-Content -Path $otherCfg -Encoding utf8
            Agent register --name "WinTrayB-$stamp" --config $otherCfg | Out-Null
            $adopted = "Adopted-$stamp"
            Agent add-game --name $adopted --dir $traySave --config $otherCfg | Out-Null

            # And a live game process, so ProcessWatcher raises launch/exit into the engine and the
            # menu while all of the above is in flight.
            $trayFake = Start-FakeGame

            # The stress itself: ~30 s, long enough for at least one 20 s reconcile tick and several
            # 4 s process polls, with the local API hammered throughout.
            $enumErrors = 0
            $sw = [Diagnostics.Stopwatch]::StartNew()
            $rows = $null
            while ($sw.Elapsed.TotalSeconds -lt 30) {
                try {
                    $rows = Invoke-RestMethod "$trayBase/api/games" -Headers $TH -TimeoutSec 5
                    Invoke-RestMethod "$trayBase/api/state" -Headers $TH -TimeoutSec 5 | Out-Null
                    $target = @($rows) | Where-Object { $_.name -eq $trayGame } | Select-Object -First 1
                    if ($target) {
                        Invoke-RestMethod "$trayBase/api/games/$($target.id)/processes" -Method Post `
                            -Headers $TH -TimeoutSec 5 `
                            -Body (@{ processNames = @($fakeName, "spin-$($sw.ElapsedMilliseconds)") } | ConvertTo-Json) `
                            -ContentType "application/json" | Out-Null
                    }
                    Invoke-RestMethod "$trayBase/api/config" -Method Post -Headers $TH -TimeoutSec 5 `
                        -Body (@{ settleQuietSeconds = (2 + ($sw.Elapsed.Seconds % 5)) } | ConvertTo-Json) `
                        -ContentType "application/json" | Out-Null
                }
                catch { $enumErrors++ }
                if ($sw.Elapsed.TotalSeconds -gt 12 -and $trayFake) { Stop-FakeGame $trayFake; $trayFake = $null }
                Start-Sleep -Milliseconds 200
            }

            $trayLog = Get-TrayLog
            Check "WA-09 no cross-thread WinForms call was made" (
                $trayLog -notmatch "(?i)cross-thread|InvalidAsynchronousStateException|Invoke or BeginInvoke")
            Check "WA-09 the live game list was never enumerated during a mutation" (
                $trayLog -notmatch "(?i)Collection was modified")
            Check "WA-09 the local API answered throughout the churn" ($enumErrors -eq 0)
            Check "WA-09 the tray survived the stress" (-not $tray.HasExited)

            # The poller's adoption really did happen - otherwise the race above never ran and the
            # three checks are vacuous.
            Check "WA-09 the poller adopted a server game during the churn" (
                $null -ne (@($rows) | Where-Object { $_.name -eq $adopted }))
        }
        finally {
            if ($trayFake) { Stop-FakeGame $trayFake }
            Stop-Process -Id $tray.Id -Force -ErrorAction SilentlyContinue
            Remove-Item Env:SAVELOCKER_TRAY_PORT -ErrorAction SilentlyContinue
        }
    }
}
finally {
    Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
    Get-Process -Name $fakeName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item Env:ASPNETCORE_URLS, Env:Storage__DbPath, Env:Storage__ArchiveRoot, `
                Env:Backup__Enabled, Env:Logging__EventLog__LogLevel__Default -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "$pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
exit 0
