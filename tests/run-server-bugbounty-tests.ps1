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
}
finally {
    foreach ($p in $script:servers) { Stop-TestServer $p }
    git worktree remove --force $oldWt 2>&1 | Out-Null
    git worktree prune
    Remove-Item Env:ASPNETCORE_URLS, Env:Storage__DbPath, Env:Storage__ArchiveRoot, `
        Env:Backup__Enabled, Env:Logging__EventLog__LogLevel__Default -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "==== SERVER BUG BOUNTY: $pass passed, $fail failed ===="
if ($fail -gt 0) { exit 1 }
