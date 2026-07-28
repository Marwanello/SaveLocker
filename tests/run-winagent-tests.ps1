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
