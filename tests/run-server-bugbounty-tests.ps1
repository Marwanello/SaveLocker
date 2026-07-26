# Server / console bug-bounty regressions (tasks/Console-BugBounty.md).
#
# Isolated-state harness: it owns its port, its SQLite DB, and its archive store, and it starts and
# stops every server it uses. It never touches the :5179 dev server.
#
# Sections are added one finding at a time, in the task's execution order.
#
#   CS-01 (P0) - deleting a machine must not delete or hide its save history.
#     The point of this section is the UPGRADE path, not just current behaviour: a pre-fix database
#     has SaveVersions.MachineId as a required column with ON DELETE CASCADE, so proving the fix
#     means seeding a real pre-fix DB, migrating it, and then deleting the machine. A test that only
#     ever sees a freshly created schema would pass without the migration existing at all.
#     The pre-fix server is built from a git worktree, the same pattern verify-password-compat.ps1
#     uses. -BaselineRef pins the last commit BEFORE the CS-01 fix rather than something moving
#     like HEAD~1, so the suite keeps seeding a genuinely pre-fix schema as later findings land.
#     A wrong ref does not pass quietly: the "really is pre-fix" check fails.
#
# Prerequisites: server + agent built in Debug. WSL with python3 (used to read the raw schema).
# Usage:  .\tests\run-server-bugbounty-tests.ps1  [-BaselineRef <ref>] [-ShowPreFixBehavior]
param(
    [string]$BaselineRef = "961ad35",   # last commit before the CS-01 fix
    [int]$Port = 5183,
    [switch]$ShowPreFixBehavior
)

$ErrorActionPreference = "Continue"

$root    = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root ".verify-server-bugbounty"
$oldWt   = Join-Path $scratch "baseline"
$url     = "http://localhost:$Port"

$inProgramFiles = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
$dotnet    = if (Test-Path $inProgramFiles) { $inProgramFiles } else { "dotnet" }
$agentDll  = Join-Path $root "src/Agent/bin/Debug/net10.0-windows/SaveLocker.Agent.dll"
$serverDll = Join-Path $root "src/Server/bin/Debug/net10.0/SaveLocker.Server.dll"
if (-not (Test-Path $agentDll))  { Write-Host "Agent not built: $agentDll";   exit 2 }
if (-not (Test-Path $serverDll)) { Write-Host "Server not built: $serverDll"; exit 2 }

$pass = 0; $fail = 0
function Check($name, $cond) {
    if ($cond) { Write-Host "PASS: $name"; $script:pass++ }
    else        { Write-Host "FAIL: $name"; $script:fail++ }
}
function Agent { & $dotnet $agentDll @args 2>&1 }

git worktree prune
Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $scratch) {
    Write-Host "Could not wipe $scratch - a server from a previous run is probably still holding it."
    Write-Host "Find it with: Get-CimInstance Win32_Process -Filter ""Name='dotnet.exe'"" | ? CommandLine -like '*verify-server-bugbounty*'"
    exit 2
}
New-Item -ItemType Directory -Force (Join-Path $scratch "archives") | Out-Null
$db = Join-Path $scratch "savelocker.db"

# The port and DB path come from launchSettings.json, which only `dotnet run` reads - launching the
# DLL directly would bind :5000 and load the production config (see Gotchas.md).
$env:ASPNETCORE_URLS      = $url
$env:Storage__DbPath      = $db
$env:Storage__ArchiveRoot = Join-Path $scratch "archives"
$env:Backup__Enabled      = "false"
$env:Logging__EventLog__LogLevel__Default = "None"

# Tracked so the finally block can stop a server even when a check throws mid-section. A leaked
# server keeps a lock on the scratch DB and on the baseline worktree's DLLs, which makes the NEXT
# run fail on "worktree already exists" for reasons that have nothing to do with the code.
$script:servers = @()

function Start-TestServer($dll, $tag) {
    $p = Start-Process -FilePath $dotnet -ArgumentList @($dll) -PassThru -NoNewWindow `
        -RedirectStandardOutput "$scratch\$tag.out.log" -RedirectStandardError "$scratch\$tag.err.log"
    $script:servers += $p
    foreach ($i in 1..60) {
        Start-Sleep -Milliseconds 700
        try { Invoke-RestMethod "$url/api/admin/status" -TimeoutSec 3 | Out-Null; return $p } catch { }
    }
    Get-Content "$scratch\$tag.err.log" -ErrorAction SilentlyContinue | Select-Object -Last 20
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Write-Host "FAIL: server ($tag) did not start on $url"
    exit 1
}
function Stop-TestServer($p) {
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2   # the DLL keeps a lock on the SQLite file for a moment
}

# Invoke-RestMethod on Windows PowerShell hands a JSON array back as ONE object, so @(...) around it
# yields a single nested element and every .Count assertion silently measures 1. Parsing the body
# ourselves keeps a list a list.
function Get-Json($path) {
    return ((Invoke-WebRequest "$url$path" -UseBasicParsing).Content | ConvertFrom-Json)
}

# There is no sqlite3 CLI on the Windows box, so raw schema reads go through WSL python3 - the same
# approach verify-password-compat.ps1 uses.
function ConvertTo-WslPath($p) {
    $drive = $p.Substring(0, 1).ToLower()
    return "/mnt/$drive" + ($p.Substring(2) -replace '\\', '/')
}
function Invoke-Sqlite($sql) {
    $py = Join-Path $scratch "query.py"
    @'
import sqlite3, sys
con = sqlite3.connect(f"file:{sys.argv[1]}?mode=ro", uri=True)
for row in con.execute(sys.argv[2]):
    print("|".join("" if c is None else str(c) for c in row))
'@ | Set-Content -Path $py -Encoding utf8
    return (& wsl -d Ubuntu-24.04 -- python3 (ConvertTo-WslPath $py) (ConvertTo-WslPath $db) $sql)
}

$stamp = Get-Date -Format "HHmmss"
$pcName   = "BBPC-$stamp"
$lapName  = "BBLap-$stamp"
$gameName = "BBGame-$stamp"

$pcCfg   = Join-Path $scratch "pc.json"
$lapCfg  = Join-Path $scratch "lap.json"
$pcSave  = Join-Path $scratch "pc_save";  New-Item -ItemType Directory -Force $pcSave  | Out-Null
$lapSave = Join-Path $scratch "lap_save"; New-Item -ItemType Directory -Force $lapSave | Out-Null
foreach ($c in @($pcCfg, $lapCfg)) {
    @{ ServerUrl = $url; Games = @() } | ConvertTo-Json | Set-Content -Path $c -Encoding utf8
}

Write-Host ""
Write-Host "==== CS-01: machine deletion must preserve save history ===="

# =====================================================================================
# 1. Seed a REAL pre-fix database with the pre-fix server
# =====================================================================================
git worktree prune
git worktree add --quiet --detach $oldWt $BaselineRef
if ($LASTEXITCODE -ne 0) { Write-Host "FAIL: could not create baseline worktree at $BaselineRef"; exit 1 }

try {
    & $dotnet build (Join-Path $oldWt "src\Server\SaveLocker.Server.csproj") --no-incremental -v quiet --nologo | Out-Null
    $oldServerDll = Join-Path $oldWt "src\Server\bin\Debug\net10.0\SaveLocker.Server.dll"
    if (-not (Test-Path $oldServerDll)) { Write-Host "FAIL: baseline server did not build"; exit 1 }

    $srv = Start-TestServer $oldServerDll "prefix"

    # History worth losing: two versions from the PC (one of them protected, one of them the head),
    # plus a divergent push from the laptop that leaves an OPEN conflict referencing a PC version.
    Agent register --name $pcName  --config $pcCfg  | Out-Null
    Agent register --name $lapName --config $lapCfg | Out-Null

    "save v1" | Set-Content (Join-Path $pcSave "save.dat") -Encoding utf8
    Agent add-game --name $gameName --dir $pcSave  --config $pcCfg  | Out-Null
    Agent add-game --name $gameName --dir $lapSave --config $lapCfg | Out-Null
    Agent push $gameName --config $pcCfg | Out-Null

    Agent pull $gameName --config $lapCfg | Out-Null       # laptop syncs to head
    "save v2" | Set-Content (Join-Path $pcSave "save.dat") -Encoding utf8
    Agent push $gameName --config $pcCfg | Out-Null        # PC advances the head

    "laptop's divergent progress" | Set-Content (Join-Path $lapSave "save.dat") -Encoding utf8
    Agent push $gameName --config $lapCfg | Out-Null       # stale parent -> open conflict

    $game = (Get-Json "/api/overview") | Where-Object { $_.game.name -eq $gameName }
    $gameId = $game.game.id
    $before = @(Get-Json "/api/games/$gameId/versions")
    $pcMachine = (Get-Json "/api/machines") | Where-Object { $_.name -eq $pcName }

    # Protect the OLDEST PC version: retention exemption must survive the delete too.
    $oldestPc = ($before | Where-Object { $_.machineId -eq $pcMachine.id } | Sort-Object createdAt)[0]
    Invoke-RestMethod "$url/api/games/$gameId/versions/$($oldestPc.id)/protected?value=true" -Method Post | Out-Null

    $before      = @(Get-Json "/api/games/$gameId/versions")
    $stateBefore = Get-Json "/api/games/$gameId/state"
    $conflictBefore = @(Get-Json "/api/conflicts" | Where-Object { $_.gameId -eq $gameId })[0]

    # Bytes, not just row counts: the promise is that the archives stay downloadable.
    $hashesBefore = @{}
    foreach ($v in $before) {
        $out = Join-Path $scratch "before_$($v.id).zip"
        try {
            Invoke-WebRequest "$url/api/games/$gameId/versions/$($v.id)/download" -OutFile $out | Out-Null
            $hashesBefore[$v.id] = (Get-FileHash $out -Algorithm SHA256).Hash
        } catch {
            Write-Host "     seed download failed for $($v.id) (game $gameId): $($_.Exception.Message)"
        }
    }
    Check "seed: every seeded archive downloads" ($hashesBefore.Count -eq $before.Count)
    Write-Host "     seeded game $gameId with $($before.Count) versions: $(($before | ForEach-Object { $_.id }) -join ', ')"

    Check "seed: PC produced 2 versions, laptop 1"  ($before.Count -eq 3)
    Check "seed: the game has an open conflict"     ($null -ne $conflictBefore)
    Check "seed: a PC version is protected"         (@($before | Where-Object { $_.protected }).Count -eq 1)

    # -ShowPreFixBehavior deletes the machine on the PRE-FIX server instead of migrating, so the
    # failure this section guards against can be seen rather than taken on trust. It reports and
    # exits; it is not part of a normal run.
    if ($ShowPreFixBehavior) {
        Invoke-RestMethod "$url/api/machines/$($pcMachine.id)" -Method Delete | Out-Null
        $preFix      = @(Get-Json "/api/games/$gameId/versions")
        $preFixState = Get-Json "/api/games/$gameId/state"
        Write-Host ""
        Write-Host "PRE-FIX behaviour on $BaselineRef, after deleting one machine:"
        Write-Host "  versions:        $($before.Count) -> $($preFix.Count)"
        Write-Host "  head:            $($stateBefore.head.id) -> $(if ($null -eq $preFixState.head) { '(null)' } else { $preFixState.head.id })"
        Write-Host "  total storage:   $($stateBefore.totalStorageBytes) -> $($preFixState.totalStorageBytes)"
        Write-Host "  archives ON DISK: $(@(Get-ChildItem (Join-Path $scratch 'archives') -Recurse -Filter *.zip).Count) (orphaned)"
        Stop-TestServer $srv
        exit 0
    }

    Stop-TestServer $srv

    # The pre-fix schema is the whole premise of this section. If the baseline already had the
    # column, nothing below would be testing a migration.
    $cols = Invoke-Sqlite "SELECT name FROM pragma_table_info('SaveVersions')"
    Check "baseline ($BaselineRef) really is pre-fix (no MachineName column)" ($cols -notcontains "MachineName")

    # =====================================================================================
    # 2. The NEW server opens that same DB and migrates it
    # =====================================================================================
    $srv = Start-TestServer $serverDll "postfix"

    $afterMigrate = @(Get-Json "/api/games/$gameId/versions")
    Check "migration preserves the version count"      ($afterMigrate.Count -eq $before.Count)

    # =====================================================================================
    # 3. Delete the machine - the action that used to destroy the history
    # =====================================================================================
    Invoke-RestMethod "$url/api/machines/$($pcMachine.id)" -Method Delete | Out-Null

    $machines = @(Get-Json "/api/machines")
    Check "the deleted machine is gone (its key is revoked)" (@($machines | Where-Object { $_.id -eq $pcMachine.id }).Count -eq 0)

    $after      = @(Get-Json "/api/games/$gameId/versions")
    $stateAfter = Get-Json "/api/games/$gameId/state"

    Check "version count is unchanged"          ($after.Count -eq $before.Count)
    Check "the head still resolves"             ($null -ne $stateAfter.head)
    Check "the head is the same version"        ($stateAfter.head.id -eq $stateBefore.head.id)
    Check "the head content hash is unchanged"  ($stateAfter.head.contentHash -eq $stateBefore.head.contentHash)
    Check "total storage is unchanged"          ($stateAfter.totalStorageBytes -eq $stateBefore.totalStorageBytes -and $stateBefore.totalStorageBytes -gt 0)
    Check "the protected flag survives"         (@($after | Where-Object { $_.protected }).Count -eq 1)
    Check "the game still reports its conflict" ($stateAfter.hasOpenConflict -eq $true)
    
    $sizesKept = $true; $hashesKept = $true; $downloadsKept = $true
    foreach ($v in $before) {
        $now = $after | Where-Object { $_.id -eq $v.id }
        if ($null -eq $now) { $sizesKept = $false; continue }
        if ($now.size -ne $v.size -or $now.contentHash -ne $v.contentHash -or
            $now.createdAt -ne $v.createdAt -or $now.parentVersionId -ne $v.parentVersionId) { $sizesKept = $false }
        try {
            $out = Join-Path $scratch "after_$($v.id).zip"
            Invoke-WebRequest "$url/api/games/$gameId/versions/$($v.id)/download" -OutFile $out | Out-Null
            if ((Get-FileHash $out -Algorithm SHA256).Hash -ne $hashesBefore[$v.id]) { $hashesKept = $false }
        } catch { $downloadsKept = $false }
    }
    Check "every version keeps its size/hash/timestamp/lineage" $sizesKept
    Check "every archive is still downloadable"                 $downloadsKept
    Check "every archive returns byte-identical content"         $hashesKept

    # The uploader must still be nameable - an empty machine name in the console is the misreport
    # this finding also called out.
    $orphans = @($after | Where-Object { $null -eq $_.machineId })
    Check "the deleted machine's versions lose their live link" ($orphans.Count -eq 2)
    Check "the deleted uploader is still named honestly"        (@($orphans | Where-Object { $_.machineName -like "$pcName*(deleted)" }).Count -eq 2)
    $lapMachine = (Get-Json "/api/machines") | Where-Object { $_.name -eq $lapName }
    Check "the surviving machine's version keeps its live link" `
        (@($after | Where-Object { $_.machineId -eq $lapMachine.id -and $_.machineName -eq $lapName }).Count -eq 1)

    # =====================================================================================
    # 4. The conflict that referenced the deleted machine must still be resolvable
    # =====================================================================================
    # Resolution queues pulls for the machines behind the two versions; one of them no longer
    # exists, and queueing a command for it would violate the foreign key.
    $conflictAfter = @(Get-Json "/api/conflicts" | Where-Object { $_.gameId -eq $gameId })[0]
    Check "the open conflict survives the delete" ($conflictAfter.id -eq $conflictBefore.id)

    $resolved = $false
    try {
        Invoke-RestMethod "$url/api/conflicts/$($conflictAfter.id)/resolve?version=$($stateBefore.head.id)" -Method Post | Out-Null
        $resolved = $true
    } catch { Write-Host "     resolve failed: $($_.Exception.Message)" }
    Check "a conflict naming a deleted machine still resolves" $resolved
    Check "resolving it left the chosen version as head" `
        ((Get-Json "/api/games/$gameId/state").head.id -eq $stateBefore.head.id)

    # Raw schema reads come last: python3 cannot open the file while the server holds it in WAL mode.
    Stop-TestServer $srv

    $cols = Invoke-Sqlite "SELECT name FROM pragma_table_info('SaveVersions')"
    Check "migration adds the uploader-name snapshot"   ($cols -contains "MachineName")
    Check "migration makes the machine link nullable" `
        (@(Invoke-Sqlite "SELECT [notnull] FROM pragma_table_info('SaveVersions') WHERE name = 'MachineId'")[0] -eq "0")
    Check "the machine link is no longer a cascade" `
        (@(Invoke-Sqlite "SELECT on_delete FROM pragma_foreign_key_list('SaveVersions') WHERE [table] = 'Machines'")[0] -eq "SET NULL")
    $unnamed = @(Invoke-Sqlite "SELECT COUNT(*) FROM SaveVersions WHERE MachineName = ''")[0]
    Check "every version can name its uploader"         ($unnamed -eq "0")

    # =====================================================================================
    # CS-02: command delivery is crash-safe and single-claim
    # =====================================================================================
    # Driven straight through the HTTP API with a machine key rather than through the agent: the
    # failures being reproduced are "the agent never came back" and "two pollers claimed at once",
    # neither of which a cooperating agent process can be made to perform on cue.
    Write-Host ""
    Write-Host "==== CS-02: a claimed command must not be lost or double-claimed ===="

    # Own database, and a lease short enough to wait out. LeaseMinutes is config, so this is the
    # real expiry path, not a test-only shortcut.
    $env:Storage__DbPath        = Join-Path $scratch "commands.db"
    $env:Commands__LeaseMinutes = "0.05"          # 3 seconds
    $leaseWait = 5
    $srv = Start-TestServer $serverDll "commands"

    $reg = Invoke-RestMethod "$url/api/machines/register" -Method Post -ContentType "application/json" `
        -Body (@{ name = "CmdPC-$stamp" } | ConvertTo-Json)
    $agentHdr = @{ "X-Api-Key" = $reg.apiKey }
    $cmdGame  = Invoke-RestMethod "$url/api/games" -Method Post -ContentType "application/json" `
        -Body (@{ name = "CmdGame-$stamp" } | ConvertTo-Json)

    function Queue-Command($type) {
        return Invoke-RestMethod "$url/api/commands" -Method Post -ContentType "application/json" `
            -Body (@{ machineId = $reg.machineId; gameId = $cmdGame.id; type = $type; force = $false } | ConvertTo-Json)
    }
    function Claim-Commands {
        return @(((Invoke-WebRequest "$url/api/agent/commands" -Headers $agentHdr -UseBasicParsing).Content | ConvertFrom-Json))
    }
    function Report-Command($id, $status, $result) {
        Invoke-RestMethod "$url/api/agent/commands/$id/result" -Method Post -Headers $agentHdr `
            -ContentType "application/json" -Body (@{ status = $status; result = $result } | ConvertTo-Json)
    }
    function Admin-Command($id) {
        return @(Get-Json "/api/commands" | Where-Object { $_.id -eq $id })[0]
    }

    # ---- 1. Crash after the GET, before execution ----
    $c1 = Queue-Command "Pull"
    $claim1 = Claim-Commands
    Check "a queued command is delivered"            (@($claim1 | Where-Object { $_.id -eq $c1.id }).Count -eq 1)
    Check "delivery records the first claim"         ((Admin-Command $c1.id).claimCount -eq 1)

    # The agent has it and has not answered: a second poll inside the lease must not run it twice.
    Check "an in-flight command is not re-delivered"  (@(Claim-Commands | Where-Object { $_.id -eq $c1.id }).Count -eq 0)

    Start-Sleep -Seconds $leaseWait
    $reclaim = Claim-Commands
    Check "an unacknowledged command comes back after its lease" (@($reclaim | Where-Object { $_.id -eq $c1.id }).Count -eq 1)
    $c1After = Admin-Command $c1.id
    Check "the reclaim is visible in the command list"           ($c1After.claimCount -eq 2)
    Check "the reclaimed command is still not Done/Failed"       ($c1After.status -eq "Dispatched")
    Check "the reclaim is audited" `
        (@(Get-Json "/api/audit?limit=200" | Where-Object { $_.action -eq "command.reclaim" }).Count -ge 1)

    # ---- 2. Execution succeeded but the result POST was lost ----
    # The retry runs the command again (safe for every type) and reports it. Reporting twice must be
    # a no-op, not a reopened command.
    Report-Command $c1.id "Done" "pulled 1 game" | Out-Null
    $done = Admin-Command $c1.id
    Check "reporting a reclaimed command completes it" ($done.status -eq "Done")
    Check "completion clears the lease"                ($null -eq $done.leaseExpiresAt)

    $lateOk = $false
    try { Report-Command $c1.id "Failed" "late duplicate report" | Out-Null; $lateOk = $true } catch { }
    $stillDone = Admin-Command $c1.id
    Check "a late duplicate report is accepted"         $lateOk
    Check "a late report does not reopen a done command" ($stillDone.status -eq "Done" -and $stillDone.result -eq "pulled 1 game")

    Start-Sleep -Seconds $leaseWait
    Check "a completed command never comes back"        (@(Claim-Commands | Where-Object { $_.id -eq $c1.id }).Count -eq 0)

    # ---- 3. Two simultaneous polls with one machine identity ----
    # Both jobs spin until a shared wall-clock instant so the two GETs really do overlap; a
    # sequential pair would pass even against the old read-then-update.
    $c2 = Queue-Command "Push"
    $startAt = (Get-Date).AddSeconds(3).ToUniversalTime().Ticks
    $racer = {
        param($u, $key, $at)
        while ([DateTime]::UtcNow.Ticks -lt $at) { }
        try {
            ((Invoke-WebRequest "$u/api/agent/commands" -Headers @{ "X-Api-Key" = $key } -UseBasicParsing).Content)
        } catch { "ERROR: $($_.Exception.Message)" }
    }
    $jobs = 1..2 | ForEach-Object { Start-Job -ScriptBlock $racer -ArgumentList $url, $reg.apiKey, $startAt }
    $bodies = $jobs | Wait-Job -Timeout 60 | Receive-Job
    $jobs | Remove-Job -Force
    $winners = @($bodies | Where-Object { $_ -like "*$($c2.id)*" })
    Check "two simultaneous polls: no request errored"  (@($bodies | Where-Object { $_ -like "ERROR:*" }).Count -eq 0)
    Check "two simultaneous polls: exactly one claim"   ($winners.Count -eq 1)
    Check "the raced command was claimed exactly once"  ((Admin-Command $c2.id).claimCount -eq 1)

    # ---- 4. Explicit failure is terminal ----
    Report-Command $c2.id "Failed" "disk full" | Out-Null
    $failed = Admin-Command $c2.id
    Check "an explicit failure is recorded"             ($failed.status -eq "Failed" -and $failed.result -eq "disk full")
    Start-Sleep -Seconds $leaseWait
    Check "a failed command is not retried forever"     (@(Claim-Commands | Where-Object { $_.id -eq $c2.id }).Count -eq 0)

    Stop-TestServer $srv

    # =====================================================================================
    # CS-03: archive ingestion is transactional at the filesystem boundary
    # =====================================================================================
    # Every check here is about what is left BEHIND after something goes wrong: a truncated archive
    # at a real version path, a file no row names, or a row whose file is gone. The last one is the
    # only unrecoverable direction, so it is the one the ordering has to rule out.
    Write-Host ""
    Write-Host "==== CS-03: no half-ingested archives, in either direction ===="

    $env:Storage__DbPath        = Join-Path $scratch "uploads.db"
    $env:Storage__ArchiveRoot   = Join-Path $scratch "upload-archives"
    $env:Storage__MaxUploadMb   = "1"
    Remove-Item Env:Commands__LeaseMinutes -ErrorAction SilentlyContinue
    $archRoot = $env:Storage__ArchiveRoot
    New-Item -ItemType Directory -Force $archRoot | Out-Null

    $srv = Start-TestServer $serverDll "uploads"

    $ureg = Invoke-RestMethod "$url/api/machines/register" -Method Post -ContentType "application/json" `
        -Body (@{ name = "UpPC-$stamp" } | ConvertTo-Json)
    $uhdr = @{ "X-Api-Key" = $ureg.apiKey }
    $ugame = Invoke-RestMethod "$url/api/games" -Method Post -ContentType "application/json" `
        -Body (@{ name = "UpGame-$stamp" } | ConvertTo-Json)

    function Staged { @(Get-ChildItem (Join-Path $archRoot ".incoming") -Filter *.part -ErrorAction SilentlyContinue) }
    function ArchiveFiles { @(Get-ChildItem $archRoot -Recurse -Filter *.zip -ErrorAction SilentlyContinue) }
    function UploadVersions { @(Get-Json "/api/games/$($ugame.id)/versions") }

    # A real (tiny) archive, so the success path is proven with the same helper as the failures.
    $goodZip = Join-Path $scratch "good.zip"
    $zipSrc  = Join-Path $scratch "zipsrc"; New-Item -ItemType Directory -Force $zipSrc | Out-Null
    "hello" | Set-Content (Join-Path $zipSrc "save.dat") -Encoding utf8
    Compress-Archive -Path (Join-Path $zipSrc "*") -DestinationPath $goodZip -Force

    function Post-Upload($path, $hash, $parent) {
        # Returns the HTTP status code, whether it succeeded or not. A parent is passed on the happy
        # path so a second upload fast-forwards instead of opening a conflict, which would make the
        # version undeletable for reasons that have nothing to do with this section.
        $q = "hash=$hash"
        if ($parent) { $q += "&parent=$parent" }
        try {
            $r = Invoke-WebRequest "$url/api/games/$($ugame.id)/upload?$q" -Method Post `
                -Headers $uhdr -InFile $path -ContentType "application/octet-stream" -UseBasicParsing
            return [int]$r.StatusCode
        } catch {
            if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode }
            return -1
        }
    }

    # ---- 1. The success path still works, and leaves nothing staged ----
    Check "a normal upload is accepted"          ((Post-Upload $goodZip "hash-good-1") -eq 200)
    Check "the upload produced one version"      (@(UploadVersions).Count -eq 1)
    Check "a completed upload stages nothing"    (@(Staged).Count -eq 0)
    Check "the archive is at its final path"     (@(ArchiveFiles).Count -eq 1)

    # ---- 2. Oversized body: refused as 413, and nothing is left of it ----
    $bigFile = Join-Path $scratch "too-big.bin"
    $fs = [System.IO.File]::Create($bigFile)
    $fs.SetLength(3MB); $fs.Close()
    $status = Post-Upload $bigFile "hash-oversized"
    Check "an oversized upload is refused with 413" ($status -eq 413)
    Check "the oversized upload created no version" (@(UploadVersions).Count -eq 1)
    Check "the oversized upload left nothing staged" (@(Staged).Count -eq 0)
    Check "the oversized upload published no archive" (@(ArchiveFiles).Count -eq 1)

    # ---- 3. Client disconnects mid-body ----
    # Raw socket on purpose: it declares a Content-Length it never delivers and then vanishes, which
    # is what a killed agent or a dropped tunnel actually looks like. Invoke-WebRequest cannot.
    # ::new, not New-Object Type(a,b) — that form passes ONE array argument, finds no ctor, and the
    # whole disconnect test then passes vacuously because no request was ever made.
    $client = [System.Net.Sockets.TcpClient]::new("127.0.0.1", $Port)
    $stream = $client.GetStream()
    $head = "POST /api/games/$($ugame.id)/upload?hash=hash-truncated HTTP/1.1`r`n" +
            "Host: localhost:$Port`r`nX-Api-Key: $($ureg.apiKey)`r`n" +
            "Content-Type: application/octet-stream`r`nContent-Length: 900000`r`n`r`n"
    $headBytes = [System.Text.Encoding]::ASCII.GetBytes($head)
    $stream.Write($headBytes, 0, $headBytes.Length)
    $stream.Write((New-Object byte[] -ArgumentList 4096), 0, 4096)      # a fraction of what was promised
    $stream.Flush()
    Start-Sleep -Milliseconds 500
    $client.Close()                                       # and gone
    Start-Sleep -Seconds 2

    # The pre-fix server leaves a 4096-byte "archive" at a real version path here, with no row.
    Check "a disconnected upload created no version"  (@(UploadVersions).Count -eq 1)
    Check "a disconnected upload left nothing staged" (@(Staged).Count -eq 0)
    Check "a disconnected upload published no archive" (@(ArchiveFiles).Count -eq 1)
    Check "the server survived the disconnect" `
        ((Invoke-WebRequest "$url/api/admin/status" -UseBasicParsing).StatusCode -eq 200)

    # ---- 4. Deleting a version whose archive cannot be removed ----
    # The file is held open with no sharing, so File.Delete fails for real. The row must still go
    # (the admin asked for it) and the undeleted file must be recorded, not silently forgotten.
    $v1 = @(UploadVersions)[0]
    Check "the second upload fast-forwards, not conflicts" `
        ((Post-Upload $goodZip "hash-good-2" $v1.id) -eq 200)
    $victim = @(UploadVersions) | Where-Object { $_.contentHash -eq "hash-good-1" }
    $victimPath = (ArchiveFiles | Where-Object { $_.Name -like "*$($victim.id.Replace('-',''))*" }).FullName
    Check "the version to delete has an archive on disk" ($null -ne $victimPath)

    $held = [System.IO.File]::Open($victimPath, 'Open', 'Read', 'None')
    try {
        $delOk = $false
        try { Invoke-RestMethod "$url/api/games/$($ugame.id)/versions/$($victim.id)" -Method Delete | Out-Null; $delOk = $true } catch { }
        Check "deleting a version succeeds even if its file is locked" $delOk
        Check "the version row is gone"                 (@(UploadVersions | Where-Object { $_.id -eq $victim.id }).Count -eq 0)
        Check "the undeletable archive is audited as orphaned" `
            (@(Get-Json "/api/audit?limit=200" | Where-Object { $_.action -eq "archive.orphaned" }).Count -ge 1)
    } finally { $held.Close() }

    # ---- 5. No live version may point at a missing archive ----
    # The invariant the ordering exists to protect, checked across everything above.
    $dangling = 0
    foreach ($v in UploadVersions) {
        try { Invoke-WebRequest "$url/api/games/$($ugame.id)/versions/$($v.id)/download" -OutFile (Join-Path $scratch "chk.zip") -UseBasicParsing | Out-Null }
        catch { $dangling++ }
    }
    Check "every surviving version still downloads" ($dangling -eq 0)

    # ---- 6. Stale staging files are swept on startup ----
    $incoming = Join-Path $archRoot ".incoming"
    New-Item -ItemType Directory -Force $incoming | Out-Null
    $stale = Join-Path $incoming "00000000000000000000000000000000-dead.part"
    $fresh = Join-Path $incoming "11111111111111111111111111111111-live.part"
    "abandoned" | Set-Content $stale -Encoding utf8
    "in flight" | Set-Content $fresh -Encoding utf8
    (Get-Item $stale).LastWriteTimeUtc = (Get-Date).ToUniversalTime().AddHours(-4)

    Stop-TestServer $srv
    $srv = Start-TestServer $serverDll "uploads2"

    Check "startup sweeps an abandoned staging file"     (-not (Test-Path $stale))
    Check "startup leaves a recent staging file alone"   (Test-Path $fresh)

    Stop-TestServer $srv

    # =====================================================================================
    # CS-04: every authoritative head change reaches the fleet
    # =====================================================================================
    # Two real agents, because the failure is not "the pointer is wrong" — the pointer was always
    # right. It is that a machine's PARENT is not repaired, which only shows up as a conflict on that
    # machine's next push. So each case ends by pushing and checking it was accepted.
    Write-Host ""
    Write-Host "==== CS-04: a server-side head change must reach every machine ===="

    $env:Storage__DbPath      = Join-Path $scratch "heads.db"
    $env:Storage__ArchiveRoot = Join-Path $scratch "head-archives"
    Remove-Item Env:Storage__MaxUploadMb -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $env:Storage__ArchiveRoot | Out-Null
    $srv = Start-TestServer $serverDll "heads"

    $hpcName  = "HeadPC-$stamp"
    $hlapName = "HeadLap-$stamp"
    $hdeckName = "HeadDeck-$stamp"
    $hgameName = "HeadGame-$stamp"
    $hpcCfg   = Join-Path $scratch "hpc.json"
    $hlapCfg  = Join-Path $scratch "hlap.json"
    $hpcSave  = Join-Path $scratch "hpc_save";  New-Item -ItemType Directory -Force $hpcSave  | Out-Null
    $hlapSave = Join-Path $scratch "hlap_save"; New-Item -ItemType Directory -Force $hlapSave | Out-Null
    foreach ($c in @($hpcCfg, $hlapCfg)) {
        @{ ServerUrl = $url; Games = @() } | ConvertTo-Json | Set-Content -Path $c -Encoding utf8
    }

    Agent register --name $hpcName  --config $hpcCfg  | Out-Null
    Agent register --name $hlapName --config $hlapCfg | Out-Null
    $deckReg = Invoke-RestMethod "$url/api/machines/register" -Method Post -ContentType "application/json" `
        -Body (@{ name = $hdeckName } | ConvertTo-Json)

    "head v1" | Set-Content (Join-Path $hpcSave "save.dat") -Encoding utf8
    Agent add-game --name $hgameName --dir $hpcSave  --config $hpcCfg  | Out-Null
    Agent add-game --name $hgameName --dir $hlapSave --config $hlapCfg | Out-Null
    Agent push $hgameName --config $hpcCfg | Out-Null          # v1 becomes head
    Agent pull $hgameName --config $hlapCfg | Out-Null         # laptop is current on v1

    $hgame = ((Get-Json "/api/overview") | Where-Object { $_.game.name -eq $hgameName }).game
    $hpc   = (Get-Json "/api/machines") | Where-Object { $_.name -eq $hpcName }
    $hlap  = (Get-Json "/api/machines") | Where-Object { $_.name -eq $hlapName }

    # A third machine that syncs this game but has never uploaded: a Deck an admin has configured
    # and nothing more. It is displaced by a head change exactly like the other two.
    Invoke-RestMethod "$url/api/games/$($hgame.id)/paths/$($deckReg.machineId)?value=$([uri]::EscapeDataString((Join-Path $scratch 'hdeck_save')))" -Method Post | Out-Null

    function PendingPulls($machineId) {
        @(Get-Json "/api/commands" | Where-Object {
            $_.machineId -eq $machineId -and $_.gameId -eq $hgame.id -and
            $_.type -eq "Pull" -and $_.status -eq "Pending" })
    }

    # ---- 1. An automatic conflict policy must tell the machines it displaced ----
    Invoke-RestMethod "$url/api/games/$($hgame.id)/conflict-policy" -Method Post -ContentType "application/json" `
        -Body (@{ policy = "NewestWins" } | ConvertTo-Json) | Out-Null

    "laptop v2"  | Set-Content (Join-Path $hlapSave "save.dat") -Encoding utf8
    Agent push $hgameName --config $hlapCfg | Out-Null          # fast-forward: head = laptop v2

    "pc v3 (stale parent)" | Set-Content (Join-Path $hpcSave "save.dat") -Encoding utf8
    $autoPush = Agent push $hgameName --config $hpcCfg          # diverges; NewestWins hands it to PC
    Check "NewestWins accepted the divergent push"  (-not ("$autoPush" -match "CONFLICT"))
    Check "the policy advanced the head to the winner" `
        ((Get-Json "/api/games/$($hgame.id)/state").head.machineName -eq $hpcName)

    Check "the displaced machine is told to pull"     (@(PendingPulls $hlap.id).Count -eq 1)
    Check "the displaced Deck is told to pull"        (@(PendingPulls $deckReg.machineId).Count -eq 1)
    Check "the winning uploader gets no redundant pull" (@(PendingPulls $hpc.id).Count -eq 0)
    Check "the queued pull is UNFORCED"              (@(PendingPulls $hlap.id)[0].force -eq $false)

    # The point of the whole finding: the displaced machine repairs its parent, so its next save is
    # accepted instead of opening a conflict.
    Agent pull $hgameName --config $hlapCfg | Out-Null
    "laptop v4 after repair" | Set-Content (Join-Path $hlapSave "save.dat") -Encoding utf8
    $repaired = Agent push $hgameName --config $hlapCfg
    Check "the displaced machine's next push does not conflict" (-not ("$repaired" -match "CONFLICT"))

    # ---- 2. Set as Latest must do what the console promises ----
    # Back to Manual so the laptop's stale push opens a real conflict to be superseded.
    Invoke-RestMethod "$url/api/games/$($hgame.id)/conflict-policy" -Method Post -ContentType "application/json" `
        -Body (@{ policy = "Manual" } | ConvertTo-Json) | Out-Null

    Agent pull $hgameName --config $hpcCfg | Out-Null           # PC current again
    "pc v5"  | Set-Content (Join-Path $hpcSave "save.dat") -Encoding utf8
    Agent push $hgameName --config $hpcCfg | Out-Null           # head = pc v5
    "laptop divergent v6" | Set-Content (Join-Path $hlapSave "save.dat") -Encoding utf8
    Agent push $hgameName --config $hlapCfg | Out-Null           # CONFLICT (manual)

    Check "a manual conflict is open before Set as Latest" `
        ((Get-Json "/api/games/$($hgame.id)/state").hasOpenConflict -eq $true)

    # Drain every queued pull first, so the assertions below measure what Set as Latest itself
    # queued rather than leftovers from section 1. Claiming is how an agent drains, so that is how
    # this drains: each machine's own key, straight through the agent endpoint.
    $keys = @{
        $hpc.id             = (Get-Content $hpcCfg  -Raw | ConvertFrom-Json).ApiKey
        $hlap.id            = (Get-Content $hlapCfg -Raw | ConvertFrom-Json).ApiKey
        $deckReg.machineId  = $deckReg.apiKey
    }
    foreach ($m in $keys.Keys) {
        Invoke-WebRequest "$url/api/agent/commands" -Headers @{ "X-Api-Key" = $keys[$m] } -UseBasicParsing | Out-Null
    }
    Check "the queue is drained before measuring Set as Latest" `
        (@(PendingPulls $hlap.id).Count -eq 0 -and @(PendingPulls $hpc.id).Count -eq 0 -and @(PendingPulls $deckReg.machineId).Count -eq 0)

    # First a version the conflict does NOT offer: the disagreement is not the admin's to close, and
    # leaving it open is what keeps the "resolution may not rewind a newer Latest" guard reachable.
    $conf = @(Get-Json "/api/conflicts" | Where-Object { $_.gameId -eq $hgame.id })[0]
    $unrelated = @(Get-Json "/api/games/$($hgame.id)/versions" |
        Where-Object { $_.id -ne $conf.versionAId -and $_.id -ne $conf.versionBId })[0]
    Invoke-RestMethod "$url/api/games/$($hgame.id)/set-latest?version=$($unrelated.id)" -Method Post | Out-Null
    Check "a conflict the chosen version is not part of stays open" `
        ((Get-Json "/api/games/$($hgame.id)/state").hasOpenConflict -eq $true)

    # Now the version the conflict actually offers.
    $target = @(Get-Json "/api/games/$($hgame.id)/versions" | Where-Object { $_.id -eq $conf.versionBId })[0]
    Invoke-RestMethod "$url/api/games/$($hgame.id)/set-latest?version=$($target.id)" -Method Post | Out-Null

    Check "Set as Latest moved the head"              ((Get-Json "/api/games/$($hgame.id)/state").head.id -eq $target.id)
    Check "Set as Latest closed the conflict it decides" ((Get-Json "/api/games/$($hgame.id)/state").hasOpenConflict -eq $false)
    Check "superseding a conflict is audited" `
        (@(Get-Json "/api/audit?limit=200" | Where-Object { $_.action -eq "conflict.resolve_superseded" }).Count -ge 1)
    Check "Set as Latest queues a pull for every machine that syncs the game" `
        (@(PendingPulls $hlap.id).Count -eq 1 -and @(PendingPulls $hpc.id).Count -eq 1 -and @(PendingPulls $deckReg.machineId).Count -eq 1)
    Check "Set as Latest queues UNFORCED pulls" `
        (@(@(PendingPulls $hlap.id) + @(PendingPulls $hpc.id) | Where-Object { $_.force }).Count -eq 0)

    # ---- 3. Deduplication: a second head change must not stack a pull per click ----
    $target2 = @(Get-Json "/api/games/$($hgame.id)/versions")[0]
    Invoke-RestMethod "$url/api/games/$($hgame.id)/set-latest?version=$($target2.id)" -Method Post | Out-Null
    Check "a second head change does not stack pulls" `
        (@(PendingPulls $hlap.id).Count -eq 1 -and @(PendingPulls $hpc.id).Count -eq 1)

    Stop-TestServer $srv
}
finally {
    foreach ($p in $script:servers) { Stop-TestServer $p }
    git worktree remove --force $oldWt 2>&1 | Out-Null
    git worktree prune
    Remove-Item Env:ASPNETCORE_URLS, Env:Storage__DbPath, Env:Storage__ArchiveRoot, `
        Env:Backup__Enabled, Env:Logging__EventLog__LogLevel__Default, Env:Commands__LeaseMinutes, `
        Env:Storage__MaxUploadMb -ErrorAction SilentlyContinue
    Get-Job -ErrorAction SilentlyContinue | Remove-Job -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "==== SERVER BUG BOUNTY: $pass passed, $fail failed ===="
if ($fail -gt 0) { exit 1 }
