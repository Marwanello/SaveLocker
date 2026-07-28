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
# Owns its server on :5189 and its state under .verify-winagent, so it never collides with a real
# agent, a dev server, or another suite.
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
