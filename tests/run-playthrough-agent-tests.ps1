# Playthroughs, the AGENT half (tasks/save-playthroughs, Group 2): one device switching between a game's lines of saves,
# end to end through the real CLI against a real server. (The server half is run-playthrough-tests.ps1.)
#
# Isolated-state harness: it owns its port, its SQLite DB and its archive store, and starts and stops its own server (the
# offline sections stop it on purpose). It never touches the :5179 dev server.
#
# What each section proves:
#   1. FORK. `playthrough new --no-switch` makes a second line whose head IS the current latest; nothing is uploaded.
#   2. SWITCH ONLINE. The unsaved edit is pushed to ITS OWN line first, then the other line's latest lands in the folder;
#      the server learns which line the device is on.
#   3. TWO LINES AT ONCE. One device plays line B while another pushes line A: no conflict on either.
#   4. SWITCH BACK. Each line kept exactly what was pushed to it.
#   5. REFUSED WHILE THE GAME RUNS. Nothing changes.
#   6. A CONFLICT BLOCKS THE SWITCH. The device stays put with its edit intact.
#   7. START FRESH. The folder is emptied except the kept file; the first push makes the new line's first version.
#   8. OFFLINE SWITCH (Keep for offline switching). Works with no server from the device's copy; the outgoing save and the
#      selection go up when the server is back.
#   9. COPIES OFF. An offline switch is refused and the folder is untouched.
#  10. VERIFY ON RESTORE. A tampered copy is refused and the folder is untouched.
#  11. ONLY UNTIL UPLOADED. Once the server has the outgoing save, nothing is left on disk.
#  12. THERE AND BACK OFFLINE. A save played offline, switched away from, back onto and away again without playing is
#      still owed to the server, and goes up when it is back; back on that line, the flush moves the device's base with it,
#      so its next push is no conflict with its own save.
#  13. A PUSH BEFORE THE FLUSH. Back on a line restored from its kept copy, a push of the folder carries that save: the flush
#      then sends nothing stale, and no conflict appears.
#
# Prerequisites: server + Windows agent built in Debug.
# Usage:  .\tests\run-playthrough-agent-tests.ps1

param([int]$Port = 5187)

$ErrorActionPreference = "Continue"

$root    = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root ".verify-playthrough-agent"
$url     = "http://localhost:$Port"

$inProgramFiles = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
$dotnet    = if (Test-Path $inProgramFiles) { $inProgramFiles } else { "dotnet" }
$agentDll  = Join-Path $root "src/Agent/bin/Debug/net10.0-windows/SaveLocker.Agent.dll"
$serverDll = Join-Path $root "src/Server/bin/Debug/net10.0/SaveLocker.Server.dll"
if (-not (Test-Path $agentDll))  { Write-Host "Agent not built: $agentDll";   exit 2 }
if (-not (Test-Path $serverDll)) { Write-Host "Server not built: $serverDll"; exit 2 }

Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $scratch | Out-Null

$pass = 0; $fail = 0
function Check($name, $cond) {
    if ($cond) { Write-Host "PASS: $name"; $script:pass++ }
    else        { Write-Host "FAIL: $name"; $script:fail++ }
}
function Agent { & $dotnet $agentDll @args 2>&1 }
function Plt { Agent playthrough @args --config $pcCfg }
function Get-Json($path) { (Invoke-WebRequest "$url$path" -UseBasicParsing).Content | ConvertFrom-Json }

$state = Join-Path $scratch "state"
New-Item -ItemType Directory -Force (Join-Path $state "archives") | Out-Null
$env:ASPNETCORE_URLS      = $url
$env:Storage__DbPath      = Join-Path $state "savelocker.db"
$env:Storage__ArchiveRoot = Join-Path $state "archives"
$env:Backup__Enabled      = "false"

$script:serverProc = $null
function Start-Server {
    $script:serverProc = Start-Process -FilePath $dotnet -ArgumentList $serverDll -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$scratch\server.out.$((Get-Date).Ticks).log" -RedirectStandardError "$scratch\server.err.$((Get-Date).Ticks).log"
    foreach ($i in 1..60) {
        Start-Sleep -Milliseconds 500
        try { Invoke-RestMethod "$url/api/admin/status" -TimeoutSec 3 | Out-Null; return $true } catch { }
    }
    $false
}
function Stop-Server {
    if ($script:serverProc) {
        Stop-Process -Id $script:serverProc.Id -Force -ErrorAction SilentlyContinue
        $script:serverProc.WaitForExit(10000) | Out-Null
    }
    # The offline sections assert what happens with NO server: wait until it really stops answering.
    foreach ($i in 1..20) {
        try { Invoke-RestMethod "$url/api/admin/status" -TimeoutSec 1 | Out-Null; Start-Sleep -Milliseconds 250 } catch { break }
    }
}
if (-not (Start-Server)) {
    Write-Host "FAIL: server did not start on $url"
    Stop-Server
    exit 1
}

try {
    function New-Dir($name) { $d = Join-Path $scratch $name; New-Item -ItemType Directory -Force $d | Out-Null; $d }
    function Set-File($dir, $name, $text) {
        New-Item -ItemType Directory -Force $dir | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $dir $name), $text)
    }
    function Read-File($dir, $name) {
        $p = Join-Path $dir $name
        if (Test-Path $p) { [System.IO.File]::ReadAllText($p) } else { $null }
    }
    function Config-Game($cfg) { ((Get-Content $cfg -Raw | ConvertFrom-Json).Games | Where-Object { $_.Name -eq $game }) }
    function Set-Config($cfg, [scriptblock]$edit) {
        $c = Get-Content $cfg -Raw | ConvertFrom-Json
        & $edit $c
        $c | ConvertTo-Json -Depth 20 | Set-Content -Path $cfg -Encoding utf8
    }
    function Set-Copies($cfg, [string]$mode) { Set-Config $cfg { param($c) $c.PlaythroughCopies = $mode } }
    function Playthroughs { @((Get-Json "/api/games/$gameId/playthroughs") | ForEach-Object { $_ }) }
    function Pt([string]$name) { Playthroughs | Where-Object { $_.name -eq $name } | Select-Object -First 1 }
    function Versions-Of([string]$name) { (Pt $name).versionCount }
    function Has-Conflict { (Get-Json "/api/games/$gameId/state").hasOpenConflict -eq $true }
    function Device-On([string]$ptName, [string]$machine) { @((Pt $ptName).devices | Where-Object { $_.machineName -eq $machine }).Count -eq 1 }
    function Store-Files($stateDir) {
        $d = Join-Path $stateDir "playthrough-store"
        if (Test-Path $d) { @(Get-ChildItem $d -Recurse -File | Where-Object { $_.Directory.Name -ne "tmp" }) } else { @() }
    }

    $pcCfg  = Join-Path (New-Dir "pc-state") "config.json"
    $lapCfg = Join-Path (New-Dir "lap-state") "config.json"
    foreach ($c in @($pcCfg, $lapCfg)) {
        @{ ServerUrl = $url; Games = @(); PlaythroughCopies = "UntilUploaded" } | ConvertTo-Json | Set-Content -Path $c -Encoding utf8
    }
    $pcStateDir = Split-Path $pcCfg -Parent
    $pcSave  = New-Dir "pc_save"
    $lapSave = New-Dir "lap_save"

    $game = "Playthrough-$(Get-Date -Format HHmmss)"
    Agent register --name "PtPC"  --config $pcCfg  | Out-Null
    Agent register --name "PtLap" --config $lapCfg | Out-Null

    Write-Host ""
    Write-Host "==== 1. Fork: a second line whose head is the current latest ===="
    Set-File $pcSave "slot1.sav" "level 5"
    Set-File $pcSave "profile.ini" "name=Sara"
    Agent add-game --name $game --dir $pcSave --config $pcCfg | Out-Null
    $gameId = (Config-Game $pcCfg).GameId
    Agent push $game --config $pcCfg | Out-Null
    Agent add-game --name $game --dir $lapSave --config $lapCfg | Out-Null
    Agent pull $game --config $lapCfg | Out-Null
    Check "the laptop pulled Main's latest" ((Read-File $lapSave "slot1.sav") -eq "level 5")
    $out = Plt new $game "Hard mode" --no-switch
    Check "playthrough new --no-switch succeeds" ($LASTEXITCODE -eq 0)
    Check "the game now has two playthroughs" ((Playthroughs).Count -eq 2)
    $main = Pt "Main"; $hard = Pt "Hard mode"
    Check "the fork's head is the version it was forked from" ($hard.head.id -eq $main.head.id)
    Check "nothing was uploaded to the fork" ($hard.versionCount -eq 0)
    Check "the device stayed on Main" ((Config-Game $pcCfg).PlaythroughName -eq "Main")
    $listing = Plt list $game | Out-String
    Check "playthrough list marks Main as this device and names both" ($listing -match "\* Main" -and $listing -match "Hard mode")

    Write-Host ""
    Write-Host "==== 2. Switch online: the unsaved edit goes to ITS line first ===="
    Set-File $pcSave "slot1.sav" "level 6 unsaved"
    $out = Plt switch $game "Hard mode" | Out-String
    Check "the switch succeeds" ($LASTEXITCODE -eq 0)
    Check "the folder holds the fork's save" ((Read-File $pcSave "slot1.sav") -eq "level 5")
    Check "Main received the unsaved edit as a new version" ((Versions-Of "Main") -eq 2)
    Check "the fork received nothing" ((Versions-Of "Hard mode") -eq 0)
    Check "the device records the new line locally" ((Config-Game $pcCfg).PlaythroughName -eq "Hard mode")
    Check "the server shows the device on the fork" (Device-On "Hard mode" "PtPC")
    Check "...and no longer on Main" (-not (Device-On "Main" "PtPC"))
    Agent pull $game --config $lapCfg | Out-Null
    Check "the laptop (still on Main) gets the edit" ((Read-File $lapSave "slot1.sav") -eq "level 6 unsaved")
    Check "no conflict" (-not (Has-Conflict))
    Check "no copy is kept once the server has the outgoing save" (@(Store-Files $pcStateDir | Where-Object { $_.Name -like "*.json" }).Count -eq 0)

    Write-Host ""
    Write-Host "==== 3. Two lines at once: no conflict ===="
    Set-File $pcSave "slot1.sav" "hard run 1"
    Agent push $game --config $pcCfg | Out-Null
    Set-File $lapSave "slot1.sav" "level 7"
    Agent push $game --config $lapCfg | Out-Null
    Set-File $pcSave "slot1.sav" "hard run 2"
    Agent push $game --config $pcCfg | Out-Null
    Check "the fork has its own two versions" ((Versions-Of "Hard mode") -eq 2)
    Check "Main has the laptop's push too" ((Versions-Of "Main") -eq 3)
    Check "neither line conflicted" (-not (Has-Conflict) -and -not (Pt "Main").hasOpenConflict -and -not (Pt "Hard mode").hasOpenConflict)

    Write-Host ""
    Write-Host "==== 4. Switch back and forth: each line kept what was pushed to it ===="
    Set-File $pcSave "slot1.sav" "hard run 3 unsaved"
    Plt switch $game "Main" | Out-Null
    Check "back on Main the folder holds the laptop's level 7" ((Read-File $pcSave "slot1.sav") -eq "level 7")
    Check "the fork got the unsaved run" ((Versions-Of "Hard mode") -eq 3)
    Plt switch $game "Hard mode" | Out-Null
    Check "on the fork again, its last push is back" ((Read-File $pcSave "slot1.sav") -eq "hard run 3 unsaved")
    Check "an extra file kept by both lines came along" ((Read-File $pcSave "profile.ini") -eq "name=Sara")

    Write-Host ""
    Write-Host "==== 5. Refused while the game runs ===="
    $me = (Get-Process -Id $PID).ProcessName
    Set-Config $pcCfg { param($c) ($c.Games | Where-Object { $_.Name -eq $game }).ProcessNames = @($me) }
    Set-File $pcSave "slot1.sav" "edit while running"
    $out = Plt switch $game "Main" 2>&1 | Out-String
    Check "the switch is refused" ($LASTEXITCODE -ne 0 -and $out -match "open")
    Check "the folder is untouched" ((Read-File $pcSave "slot1.sav") -eq "edit while running")
    Check "the device is still on the fork" ((Config-Game $pcCfg).PlaythroughName -eq "Hard mode")
    Set-Config $pcCfg { param($c) ($c.Games | Where-Object { $_.Name -eq $game }).ProcessNames = @() }
    Agent push $game --config $pcCfg | Out-Null

    Write-Host ""
    Write-Host "==== 6. A conflict in the line you are leaving blocks the switch ===="
    Plt switch $game "Main" | Out-Null
    Agent pull $game --config $pcCfg | Out-Null
    Set-File $lapSave "slot1.sav" "level 8"
    Agent push $game --config $lapCfg | Out-Null
    Set-File $pcSave "slot1.sav" "pc diverged"
    $out = Plt switch $game "Hard mode" 2>&1 | Out-String
    Check "the switch is refused" ($LASTEXITCODE -ne 0 -and $out -match "conflict")
    Check "the device's edit is intact" ((Read-File $pcSave "slot1.sav") -eq "pc diverged")
    Check "the device is still on Main" ((Config-Game $pcCfg).PlaythroughName -eq "Main")
    Check "the conflict is Main's only" ((Pt "Main").hasOpenConflict -and -not (Pt "Hard mode").hasOpenConflict)
    Agent resolve-conflict $game --keep local --config $pcCfg | Out-Null
    Check "resolved" (-not (Has-Conflict))

    Write-Host ""
    Write-Host "==== 7. Start fresh: emptied except the kept file; the first push is the line's first version ===="
    Agent pull $game --config $pcCfg | Out-Null
    $out = Plt new $game "Clean slate" --fresh --keep "profile.ini" | Out-String
    Check "new --fresh succeeds" ($LASTEXITCODE -eq 0)
    Check "the kept file is there" ((Read-File $pcSave "profile.ini") -eq "name=Sara")
    Check "the save file is gone" ($null -eq (Read-File $pcSave "slot1.sav"))
    Check "the device is on the new line" ((Config-Game $pcCfg).PlaythroughName -eq "Clean slate")
    Check "the new line has no save yet" ($null -eq (Pt "Clean slate").head)
    Check "Main still has what the device pushed before leaving it" ((Versions-Of "Main") -ge 4)
    Set-File $pcSave "slot1.sav" "fresh 1"
    Agent push $game --config $pcCfg | Out-Null
    Check "the first push is the line's first version" ((Versions-Of "Clean slate") -eq 1)
    Check "no conflict" (-not (Has-Conflict))

    Write-Host ""
    Write-Host "==== 8. Offline switch from the device's copy (Keep for offline switching) ===="
    Set-Copies $pcCfg "KeepForOffline"
    Plt switch $game "Main" | Out-Null
    Plt switch $game "Clean slate" | Out-Null
    Check "both lines now have a copy on this device" ((Store-Files $pcStateDir).Count -ge 2)
    Set-File $pcSave "slot1.sav" "fresh offline edit"
    $mainBefore = Versions-Of "Main"; $freshBefore = Versions-Of "Clean slate"
    Stop-Server
    $out = Plt switch $game "Main" 2>&1 | Out-String
    Check "the switch works with no server" ($LASTEXITCODE -eq 0 -and $out -match "copy")
    Check "the folder holds Main's copy" ((Read-File $pcSave "slot1.sav") -eq "pc diverged")
    Check "the device records Main" ((Config-Game $pcCfg).PlaythroughName -eq "Main")
    Check "the selection is marked as not yet told" ((Config-Game $pcCfg).PlaythroughSelectPending -eq $true)
    Check "the outgoing save is kept, not uploaded yet" (@(Store-Files $pcStateDir | Where-Object { $_.Name -like "*.json" }).Count -ge 2)
    $null = Start-Server
    Plt flush | Out-Null
    Check "the outgoing save reached its own line when the server was back" ((Versions-Of "Clean slate") -eq ($freshBefore + 1))
    Check "...and not Main" ((Versions-Of "Main") -eq $mainBefore)
    Check "the server now shows the device on Main" (Device-On "Main" "PtPC")
    Check "the selection is no longer pending" ((Config-Game $pcCfg).PlaythroughSelectPending -ne $true)
    Check "no conflict from the offline round trip" (-not (Has-Conflict))
    Plt switch $game "Clean slate" | Out-Null
    Check "switching back online restores the pushed offline edit" ((Read-File $pcSave "slot1.sav") -eq "fresh offline edit")

    Write-Host ""
    Write-Host "==== 9. With copies off an offline switch is refused ===="
    Set-Copies $pcCfg "Off"
    Set-File $pcSave "slot1.sav" "edit with copies off"
    Stop-Server
    $out = Plt switch $game "Main" 2>&1 | Out-String
    Check "refused" ($LASTEXITCODE -ne 0 -and $out -match "reach the server")
    Check "the folder is untouched" ((Read-File $pcSave "slot1.sav") -eq "edit with copies off")
    Check "the device is still on Clean slate" ((Config-Game $pcCfg).PlaythroughName -eq "Clean slate")
    $null = Start-Server
    Agent push $game --config $pcCfg | Out-Null

    Write-Host ""
    Write-Host "==== 10. A tampered copy is refused ===="
    Set-Copies $pcCfg "KeepForOffline"
    Plt switch $game "Main" | Out-Null
    Plt switch $game "Clean slate" | Out-Null
    Set-File $pcSave "slot1.sav" "edit before tamper"
    foreach ($f in (Get-ChildItem (Join-Path $pcStateDir "playthrough-store\blobs") -Recurse -File | Where-Object { $_.Directory.Name -ne "tmp" })) {
        [System.IO.File]::WriteAllText($f.FullName, "tampered")
    }
    Stop-Server
    $out = Plt switch $game "Main" 2>&1 | Out-String
    Check "refused" ($LASTEXITCODE -ne 0)
    Check "the folder is not left half-restored" ((Read-File $pcSave "slot1.sav") -eq "edit before tamper")
    Check "the device is still on Clean slate" ((Config-Game $pcCfg).PlaythroughName -eq "Clean slate")
    $null = Start-Server

    Write-Host ""
    Write-Host "==== 11. Only until uploaded: nothing stays on disk ===="
    Set-Copies $pcCfg "UntilUploaded"
    Agent push $game --config $pcCfg | Out-Null
    $null = Plt flush
    Remove-Item (Join-Path $pcStateDir "playthrough-store") -Recurse -Force -ErrorAction SilentlyContinue
    Set-File $pcSave "slot1.sav" "edit then switch"
    Plt switch $game "Main" | Out-Null
    Check "the switch succeeded" ((Config-Game $pcCfg).PlaythroughName -eq "Main")
    Check "no snapshot is left once the server confirmed the push" ((Store-Files $pcStateDir | Where-Object { $_.Name -like "*.json" }).Count -eq 0)

    function Pending-Copies {
        $d = Join-Path $pcStateDir "playthrough-store\snapshots"
        if (-not (Test-Path $d)) { return 0 }
        @(Get-ChildItem $d -Recurse -Filter *.json | Where-Object { (Get-Content $_.FullName -Raw | ConvertFrom-Json).Uploaded -eq $false }).Count
    }

    Write-Host ""
    Write-Host "==== 12. Offline there and back: the save played offline is still owed, and goes up without a false conflict ===="
    Set-Copies $pcCfg "KeepForOffline"
    Plt switch $game "Clean slate" | Out-Null
    Plt switch $game "Main" | Out-Null
    Set-File $pcSave "slot1.sav" "main played offline"
    $mainBefore = Versions-Of "Main"
    Stop-Server
    Plt switch $game "Clean slate" | Out-Null
    Plt switch $game "Main" | Out-Null
    Check "back on Main with the save played offline" ((Read-File $pcSave "slot1.sav") -eq "main played offline")
    Plt switch $game "Clean slate" | Out-Null
    $listOut = Plt list $game | Out-String
    Check "away again without playing: Main's offline save is still waiting to go up" ($listOut -match "copy on this device: Main\s+\(not uploaded yet\)")
    $null = Start-Server
    Plt flush | Out-Null
    Check "it reached Main when the server was back" ((Versions-Of "Main") -eq ($mainBefore + 1))
    Plt switch $game "Main" | Out-Null
    Check "and Main's latest is that save" ((Read-File $pcSave "slot1.sav") -eq "main played offline")

    Set-File $pcSave "slot1.sav" "main second offline"
    $mainBefore = Versions-Of "Main"
    Stop-Server
    Plt switch $game "Clean slate" | Out-Null
    Plt switch $game "Main" | Out-Null
    $null = Start-Server
    Plt flush | Out-Null
    Set-File $pcSave "slot1.sav" "main after reconnect"
    Agent push $game --config $pcCfg | Out-Null
    Check "back on Main from its kept copy: the next push after the flush is no conflict" (-not (Has-Conflict))
    Check "...the flush and the push are Main's two new versions" ((Versions-Of "Main") -eq ($mainBefore + 2))

    Write-Host ""
    Write-Host "==== 13. A push before the flush carries the kept save ===="
    Set-File $pcSave "slot1.sav" "main third offline"
    Stop-Server
    Plt switch $game "Clean slate" | Out-Null
    Plt switch $game "Main" | Out-Null
    $null = Start-Server
    Set-File $pcSave "slot1.sav" "main played on after reconnect"
    Agent push $game --config $pcCfg | Out-Null
    Check "the push marked the kept copy as sent" ((Pending-Copies) -eq 0)
    Plt flush | Out-Null
    Check "the flush sent nothing stale: no conflict" (-not (Has-Conflict))
}
finally {
    Stop-Server
    Remove-Item Env:ASPNETCORE_URLS, Env:Storage__DbPath, Env:Storage__ArchiveRoot, Env:Backup__Enabled -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "==== PLAYTHROUGH AGENT RESULT: $pass passed, $fail failed ===="
if ($fail -gt 0) { exit 1 }
exit 0



