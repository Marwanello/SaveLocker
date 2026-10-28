# Playthroughs, the SERVER half (tasks/save-playthroughs, Group 1): a game's several lines of saves, each with its own
# head, versions, conflicts, lease and retention, and a device that follows exactly one.
#
# Isolated-state harness: it owns its port, its SQLite DB and its archive store and starts and stops its own server.
# It speaks the wire protocol directly (no agent), so it runs anywhere the server builds. It never touches :5179.
#
# What each section proves:
#   0. MIGRATION. A database at the PREVIOUS schema (built with `dotnet ef database update <previous migration>`,
#      seeded with games, a version chain, a lease, an open conflict and mapped machines) is brought up to this build:
#      every game gets one default playthrough "Main" holding its versions, conflicts and lease; the head mirror holds;
#      the machines that synced it are recorded on Main; a fresh machine then does a full push/pull round trip on it.
#      Skipped (not failed) when the dotnet-ef tool or python is missing.
#   1. COMPATIBILITY. A client that never names a playthrough (every agent that predates this release) lands on Main,
#      and the console's game state still reports the default's head.
#   2. FORKS. From a playthrough's latest, from a version, and empty: nothing is copied, the head just points at the
#      version, and the version stays in the playthrough it was made in. Names are unique ignoring case, 1-60 long.
#   3. TWO LINES AT ONCE. Two machines on two playthroughs push in turn, each fast-forwarding, no conflict; a machine
#      that named no playthrough keeps pushing to the one it follows, never to Main by accident.
#   4. CONFLICTS ARE PER PLAYTHROUGH. A stale push to one playthrough opens a conflict there only.
#   5. LEASES ARE PER PLAYTHROUGH. Two machines hold two playthroughs of one game; a third is refused on a held one.
#   6. FAN-OUT ONLY TO FOLLOWERS. Set as Latest on a playthrough queues a pull for the devices on it and no others.
#   7. RETENTION PER PLAYTHROUGH. The game's limit applies to each separately; a version that is another playthrough's
#      head is never pruned out from under it.
#   8. ARCHIVE / DEFAULT. Frozen means frozen (push, pull, select, lease all refused); the default cannot be archived;
#      a device with a recorded playthrough does not follow a change of default, a new device does; and the agent state
#      route (`savelocker status`, save-vs-cloud, the DLC check) describes the device's own playthrough, not the default.
#   9. DELTA. A per-file delta is only negotiated against the head of the SAME playthrough.
#  10. THE SWITCH COMMAND. Refused to a machine whose agent is too old to parse it, and never handed to one.
#
# Usage:  .\tests\run-playthrough-tests.ps1        (server built in Debug: dotnet build src/Server)

param([int]$Port = 5186)

$ErrorActionPreference = "Continue"

$root    = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root ".verify-playthroughs"
$url     = "http://localhost:$Port"

$inProgramFiles = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
$dotnet    = if (Test-Path $inProgramFiles) { $inProgramFiles } else { "dotnet" }
$serverDll = Join-Path $root "src/Server/bin/Debug/net10.0/SaveLocker.Server.dll"
if (-not (Test-Path $serverDll)) { Write-Host "Server not built: $serverDll"; exit 2 }

Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $scratch | Out-Null

Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression
$http = New-Object System.Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromSeconds(60)

$pass = 0; $fail = 0
function Check($name, $cond) {
    if ($cond) { Write-Host "PASS: $name"; $script:pass++ }
    else        { Write-Host "FAIL: $name"; $script:fail++ }
}

# ---- wire helpers --------------------------------------------------------------------------------------------------
function Call([string]$method, [string]$path, [string]$key = "", $body = $null, [byte[]]$bytes = $null) {
    $req = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($method)), "$url$path"
    if ($key) { $req.Headers.Add("X-Api-Key", $key) }
    if ($null -ne $bytes) {
        $req.Content = New-Object System.Net.Http.ByteArrayContent (, $bytes)
        $req.Content.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue "application/octet-stream"
    } elseif ($null -ne $body) {
        $json = ConvertTo-Json -InputObject $body -Depth 10 -Compress
        $req.Content = New-Object System.Net.Http.StringContent $json, ([System.Text.Encoding]::UTF8), "application/json"
    }
    $resp = $http.SendAsync($req).GetAwaiter().GetResult()
    $raw = $resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    $text = [System.Text.Encoding]::UTF8.GetString($raw)
    $parsed = $null
    try { if ($text.TrimStart().StartsWith("{") -or $text.TrimStart().StartsWith("[")) { $parsed = $text | ConvertFrom-Json } } catch { }
    [pscustomobject]@{ Status = [int]$resp.StatusCode; Text = $text; Json = $parsed; Bytes = $raw }
}
function Admin-Get($path)             { (Call "GET" $path).Json }
function Admin-Post($path, $body=$null) { Call "POST" $path "" $body }

function New-Zip([hashtable]$files) {
    $ms = New-Object System.IO.MemoryStream
    $zip = New-Object System.IO.Compression.ZipArchive $ms, ([System.IO.Compression.ZipArchiveMode]::Create), $true
    foreach ($name in ($files.Keys | Sort-Object)) {
        $e = $zip.CreateEntry($name)
        $w = New-Object System.IO.StreamWriter ($e.Open())
        $w.Write([string]$files[$name]); $w.Dispose()
    }
    $zip.Dispose()
    , $ms.ToArray()
}
function Sha([byte[]]$b) {
    $h = [System.Security.Cryptography.SHA256]::Create()
    -join ($h.ComputeHash($b) | ForEach-Object { $_.ToString("x2") })
}
function Zip-Text([byte[]]$zipBytes, [string]$entry = "save.txt") {
    $ms = New-Object System.IO.MemoryStream (, $zipBytes)
    $zip = New-Object System.IO.Compression.ZipArchive $ms
    $e = $zip.GetEntry($entry)
    if ($null -eq $e) { $zip.Dispose(); return $null }
    $r = New-Object System.IO.StreamReader ($e.Open()); $t = $r.ReadToEnd(); $r.Dispose(); $zip.Dispose(); $t
}

# A single-shot push. $pt = "" names none, exactly as an agent that predates playthroughs does.
function Push($key, $game, [string]$text, $parent = $null, [string]$pt = "", [bool]$force = $false) {
    $zip = New-Zip @{ "save.txt" = $text }
    $q = "hash=$(Sha $zip)"
    if ($parent) { $q += "&parent=$parent" }
    if ($pt) { $q += "&playthrough=$pt" }
    if ($force) { $q += "&force=true" }
    Call "POST" "/api/games/$game/upload?$q" $key $null $zip
}
function Pull($key, $game, [string]$pt = "") {
    $q = if ($pt) { "?playthrough=$pt" } else { "" }
    $r = Call "GET" "/api/games/$game/download$q" $key
    if ($r.Status -eq 200) { $r | Add-Member -NotePropertyName Content -NotePropertyValue (Zip-Text $r.Bytes) }
    $r
}
function Register([string]$name) { (Call "POST" "/api/machines/register" "" @{ name = $name }).Json }
function Playthroughs($game) { @(Admin-Get "/api/games/$game/playthroughs") }
function Pt($game, [string]$name) { Playthroughs $game | Where-Object { $_.name -eq $name } | Select-Object -First 1 }
function New-Game([string]$name) { (Admin-Post "/api/games" @{ name = $name }).Json }
function Clear-Pending($game) {
    $ids = @(@(Admin-Get "/api/commands") | Where-Object { $_.gameId -eq $game -and $_.status -eq "Pending" } | ForEach-Object { $_.id })
    if ($ids.Count -gt 0) { Admin-Post "/api/commands/cancel" @{ ids = $ids } | Out-Null }
}
function Pending-Pulls($game) {
    @(Admin-Get "/api/commands") | Where-Object { $_.gameId -eq $game -and $_.status -eq "Pending" -and $_.type -eq "Pull" } |
        ForEach-Object { $_.machineName }
}

# ---- server --------------------------------------------------------------------------------------------------------
$state = Join-Path $scratch "state"
New-Item -ItemType Directory -Force (Join-Path $state "archives") | Out-Null
$dbPath = Join-Path $state "savelocker.db"

# ======================================================================================================================
# 0. Migration: build a database at the previous schema, seed it, and let this build take it over
# ======================================================================================================================
$migrated = $false
$seed = @{}
$previous = "20261021152439_AddGameTitles"
$python = (Get-Command python -ErrorAction SilentlyContinue)
if (-not $python) { $python = (Get-Command python3 -ErrorAction SilentlyContinue) }
$efOk = $false
try { & $dotnet ef --version *> $null; $efOk = ($LASTEXITCODE -eq 0) } catch { }
if ($python -and $efOk) {
    & $dotnet ef database update $previous --project (Join-Path $root "src/Server") --no-build --connection "Data Source=$dbPath" *> "$scratch\ef.log"
    if ($LASTEXITCODE -eq 0 -and (Test-Path $dbPath)) {
        $seedPy = Join-Path $scratch "seed.py"
        @'
import sqlite3, sys, uuid, zipfile, os, datetime, json
db, archives = sys.argv[1], sys.argv[2]
con = sqlite3.connect(db)
con.execute("PRAGMA foreign_keys=ON")
gid = lambda: str(uuid.uuid4()).upper()
stamp = lambda minutes=0: (datetime.datetime.utcnow() + datetime.timedelta(minutes=minutes)).strftime('%Y-%m-%d %H:%M:%S.%f')[:-3]
def insert(table, **vals):
    cols = con.execute(f'PRAGMA table_info("{table}")').fetchall()
    row = {}
    for _, name, typ, notnull, dflt, pk in cols:
        if name in vals: row[name] = vals[name]
        elif notnull and dflt is None: row[name] = 0 if typ.upper() == 'INTEGER' else ''
    keys = list(row)
    con.execute('INSERT INTO "%s" (%s) VALUES (%s)' % (table, ",".join('"%s"' % k for k in keys), ",".join("?" * len(keys))), [row[k] for k in keys])

old_pc, old_deck = gid(), gid()
for mid, name in ((old_pc, "OldPC"), (old_deck, "OldDeck")):
    insert("Machines", Id=mid, Name=name, ApiKeyHash="x", CreatedAt=stamp(-500), LastSeen=stamp(-5))

g1, g2 = gid(), gid()
insert("Games", Id=g1, Name="Seeded Game", Enabled=1)
insert("Games", Id=g2, Name="Seeded Empty Game", Enabled=1)

chain = []
parent = None
for i in range(1, 4):
    vid = gid()
    rel = f"{g1.replace('-', '').lower()}/{vid.replace('-', '').lower()}.zip"
    path = os.path.join(archives, rel.replace("/", os.sep))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with zipfile.ZipFile(path, "w") as z: z.writestr("save.txt", f"seeded-{i}")
    insert("SaveVersions", Id=vid, GameId=g1, MachineId=old_pc, MachineName="OldPC", CreatedAt=stamp(-300 + i * 10),
           ContentHash=f"seed-hash-{i}", Size=os.path.getsize(path), ParentVersionId=parent, Protected=0, ArchivePath=rel)
    chain.append(vid); parent = vid
con.execute('UPDATE "Games" SET "HeadVersionId" = ? WHERE "Id" = ?', (chain[-1], g1))
# A rival save from the other machine, diverged from the first version: an open conflict.
rival = gid()
rrel = f"{g1.replace('-', '').lower()}/{rival.replace('-', '').lower()}.zip"
rpath = os.path.join(archives, rrel.replace("/", os.sep))
with zipfile.ZipFile(rpath, "w") as z: z.writestr("save.txt", "seeded-rival")
insert("SaveVersions", Id=rival, GameId=g1, MachineId=old_deck, MachineName="OldDeck", CreatedAt=stamp(-200),
       ContentHash="seed-hash-rival", Size=os.path.getsize(rpath), ParentVersionId=chain[0], Protected=0, ArchivePath=rrel)
insert("Conflicts", Id=gid(), GameId=g1, VersionAId=chain[-1], VersionBId=rival, MachineId=old_deck, Count=1,
       LastSeen=stamp(-200), Status=0, CreatedAt=stamp(-200))
insert("Leases", Id=gid(), GameId=g1, MachineId=old_pc, AcquiredAt=stamp(-1), ExpiresAt=stamp(300))
insert("MachineSavePaths", MachineId=old_pc, GameId=g1, PathKey="main", SavePath="C:/saves/seeded")
insert("MachineSavePaths", MachineId=old_deck, GameId=g1, PathKey="main", SavePath="/home/deck/seeded")
insert("MachineSavePaths", MachineId=old_deck, GameId=g2, PathKey="main", SavePath="/home/deck/empty")
con.commit()
print(json.dumps({"g1": g1, "g2": g2, "head": chain[-1], "first": chain[0], "rival": rival, "oldPc": old_pc, "oldDeck": old_deck}))
'@ | Set-Content -Path $seedPy -Encoding utf8
        $seedOut = & $python.Source $seedPy $dbPath (Join-Path $state "archives")
        if ($LASTEXITCODE -eq 0) { $seed = $seedOut | ConvertFrom-Json; $migrated = $true }
        else { Write-Host "SKIP migration section: seeding the old database failed: $seedOut" }
    } else { Write-Host "SKIP migration section: could not build the old-schema database (see $scratch\ef.log)" }
} else { Write-Host "SKIP migration section: needs the dotnet-ef tool and python" }

$env:ASPNETCORE_URLS      = $url
$env:Storage__DbPath      = $dbPath
$env:Storage__ArchiveRoot = Join-Path $state "archives"
$env:Backup__Enabled      = "false"
$serverProc = Start-Process -FilePath $dotnet -ArgumentList $serverDll -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput "$scratch\server.out.log" -RedirectStandardError "$scratch\server.err.log"
$up = $false
foreach ($i in 1..80) {
    Start-Sleep -Milliseconds 500
    try { Invoke-RestMethod "$url/api/admin/status" -TimeoutSec 3 | Out-Null; $up = $true; break } catch { }
}
if (-not $up) {
    Write-Host "FAIL: server did not start on $url"
    Get-Content "$scratch\server.err.log" -ErrorAction SilentlyContinue | Select-Object -Last 20
    Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

try {
    if ($migrated) {
        Write-Host ""
        Write-Host "==== 0. A database from the previous release becomes Main ===="
        $p1 = @(Playthroughs $seed.g1)
        $p2 = @(Playthroughs $seed.g2)
        Check "each seeded game has exactly one playthrough" ($p1.Count -eq 1 -and $p2.Count -eq 1)
        $main = $p1[0]
        Check "it is called Main and is the default" ($main.name -eq "Main" -and $main.isDefault -eq $true)
        Check "its head is the game's old head" ($main.head.id -eq $seed.head)
        Check "all four versions (the chain and the rival save) are Main's" ($main.versionCount -eq 4)
        $versions = @(Admin-Get "/api/games/$($seed.g1)/versions")
        Check "every version names Main as its playthrough" (@($versions | Where-Object { $_.playthroughId -eq $main.id }).Count -eq 4)
        Check "the game with no versions has a Main with no head" ($p2[0].name -eq "Main" -and $null -eq $p2[0].head -and $p2[0].versionCount -eq 0)
        $names = @($main.devices | ForEach-Object { $_.machineName }) | Sort-Object
        Check "the machines that synced it are recorded on Main" (($names -join ",") -eq "OldDeck,OldPC")
        $state1 = @(Admin-Get "/api/overview") | Where-Object { $_.game.id -eq $seed.g1 } | Select-Object -First 1
        Check "the game state still reports the default's head (the mirror)" ($state1.head.id -eq $seed.head)
        Check "the game state counts one playthrough" ($state1.playthroughCount -eq 1)
        Check "the old lease is a lease on Main" ($state1.lease.playthroughId -eq $main.id -and $state1.lease.holderMachineName -eq "OldPC")
        $conflict = @(Admin-Get "/api/conflicts") | Where-Object { $_.gameId -eq $seed.g1 } | Select-Object -First 1
        Check "the old open conflict is a conflict on Main" ($conflict.playthroughId -eq $main.id -and $main.hasOpenConflict -eq $true)

        $new = Register "MigratedPC"
        $r = Push $new.apiKey $seed.g2 "fresh-after-migration"
        Check "a fresh machine pushes to the migrated game without naming a playthrough" ($r.Status -eq 200 -and $r.Json.status -eq "Created")
        $back = Pull $new.apiKey $seed.g2
        Check "and pulls the same bytes back" ($back.Status -eq 200 -and $back.Content -eq "fresh-after-migration")
        $lease = (Call "POST" "/api/games/$($seed.g1)/lease" $new.apiKey).Json
        Check "the migrated lease still excludes a second machine" ($lease.granted -eq $false -and $lease.lease.holderMachineName -eq "OldPC")
    }

    # ---- fixtures for the rest ----
    $a = Register "PtA"; $b = Register "PtB"; $c = Register "PtC"
    $game = (New-Game "Playthrough Game $(Get-Date -Format HHmmss)").id

    Write-Host ""
    Write-Host "==== 1. A client that names no playthrough lands on Main ===="
    $list = @(Playthroughs $game)
    Check "a new game starts with one playthrough, Main, the default" ($list.Count -eq 1 -and $list[0].name -eq "Main" -and $list[0].isDefault -eq $true -and $null -eq $list[0].head)
    $main = $list[0]
    $r1 = Push $a.apiKey $game "v1"
    Check "first push (no playthrough named) is created" ($r1.Json.status -eq "Created")
    Check "it is a version of Main" ($r1.Json.version.playthroughId -eq $main.id)
    $v1 = $r1.Json.version.id
    $v2 = (Push $a.apiKey $game "v2" $v1).Json.version.id
    Check "the overview still reports Main's head as the game's head" ((@(Admin-Get "/api/overview") | Where-Object { $_.game.id -eq $game }).head.id -eq $v2)
    $pullB = Pull $b.apiKey $game
    Check "another machine naming nothing pulls Main's latest" ($pullB.Content -eq "v2")
    $devs = @((Pt $game "Main").devices | ForEach-Object { $_.machineName }) | Sort-Object
    Check "both machines are now recorded on Main (the default can change later without moving them)" (($devs -join ",") -eq "PtA,PtB")

    Write-Host ""
    Write-Host "==== 2. Forks point at a version; nothing is copied ===="
    $hardR = Admin-Post "/api/games/$game/playthroughs" @{ name = "Hard mode"; fromPlaythroughId = $main.id }
    $hard = $hardR.Json
    Check "fork from Main's latest: created" ($hardR.Status -eq 200 -and $hard.name -eq "Hard mode")
    Check "its head is Main's latest version and it was forked from it" ($hard.head.id -eq $v2 -and $hard.forkedFromVersionId -eq $v2)
    Check "that version still belongs to Main, and the fork has no versions of its own yet" ($hard.head.playthroughId -eq $main.id -and $hard.versionCount -eq 0 -and $hard.isDefault -eq $false)
    $dupe = Admin-Post "/api/games/$game/playthroughs" @{ name = "  hard MODE " }
    Check "a name is unique ignoring case and spaces at the edges" ($dupe.Status -eq 409)
    Check "an empty name is refused" ((Admin-Post "/api/games/$game/playthroughs" @{ name = "   " }).Status -eq 400)
    Check "a 61-character name is refused" ((Admin-Post "/api/games/$game/playthroughs" @{ name = ("x" * 61) }).Status -eq 400)
    Check "a 60-character name is fine" ((Admin-Post "/api/games/$game/playthroughs" @{ name = ("y" * 60) }).Status -eq 200)
    Check "naming a version and a playthrough both is refused" ((Admin-Post "/api/games/$game/playthroughs" @{ name = "Both"; fromVersionId = $v1; fromPlaythroughId = $main.id }).Status -eq 400)
    $old = (Admin-Post "/api/games/$game/playthroughs" @{ name = "From v1"; fromVersionId = $v1 }).Json
    Check "fork from an older version: its head is that version" ($old.head.id -eq $v1)
    $fresh = (Admin-Post "/api/games/$game/playthroughs" @{ name = "Fresh start" }).Json
    Check "an empty playthrough has no head" ($null -eq $fresh.head -and $null -eq $fresh.forkedFromVersionId)
    Check "an unknown version is a 404" ((Admin-Post "/api/games/$game/playthroughs" @{ name = "Ghost"; fromVersionId = [guid]::NewGuid() }).Status -eq 404)
    $renamed = Admin-Post "/api/games/$game/playthroughs/$($old.id)/rename" @{ name = "Nuzlocke" }
    Check "rename works and keeps the head" ($renamed.Json.name -eq "Nuzlocke" -and $renamed.Json.head.id -eq $v1)
    Check "renaming onto another's name (any case) is refused" ((Admin-Post "/api/games/$game/playthroughs/$($old.id)/rename" @{ name = "MAIN" }).Status -eq 409)

    Write-Host ""
    Write-Host "==== 3. Two machines, two lines, at once ===="
    $sel = Call "POST" "/api/agent/games/$game/playthroughs/$($hard.id)/select" $b.apiKey
    Check "B selects Hard mode" ($sel.Status -eq 200 -and (@($sel.Json.devices | ForEach-Object { $_.machineName }) -contains "PtB"))
    $mainDevs = @((Pt $game "Main").devices | ForEach-Object { $_.machineName })
    Check "B is on Hard mode only; A stays on Main" ($mainDevs -contains "PtA" -and $mainDevs -notcontains "PtB")
    $pullHard = Pull $b.apiKey $game
    Check "B, naming nothing, now pulls Hard mode's head" ($pullHard.Content -eq "v2")
    $forkList = @((Call "GET" "/api/agent/games/$game/versions" $b.apiKey).Json)
    Check "before its own first push, B's list is the version Hard mode was forked from (Main's)" ($forkList.Count -eq 1 -and $forkList[0].id -eq $v2 -and $forkList[0].playthroughId -eq $main.id -and $forkList[0].machineName)
    $h1r = Push $b.apiKey $game "h1" $v2
    Check "B pushes (naming nothing) and it becomes Hard mode's first version of its own" ($h1r.Json.status -eq "Created" -and $h1r.Json.version.playthroughId -eq $hard.id)
    $h1 = $h1r.Json.version.id
    $v3r = Push $a.apiKey $game "v3" $v2
    Check "A fast-forwards Main at the same time: no conflict" ($v3r.Json.status -eq "Created" -and $v3r.Json.version.playthroughId -eq $main.id)
    $v3 = $v3r.Json.version.id
    Check "Main's head and Hard mode's head are different versions" ((Pt $game "Main").head.id -eq $v3 -and (Pt $game "Hard mode").head.id -eq $h1)
    Check "the game's head (the mirror) follows only the default" ((@(Admin-Get "/api/overview") | Where-Object { $_.game.id -eq $game }).head.id -eq $v3)
    Check "B, naming nothing, pulls Hard mode's latest, not Main's" ((Pull $b.apiKey $game).Content -eq "h1")
    Check "A, naming nothing, pulls Main's latest" ((Pull $a.apiKey $game).Content -eq "v3")
    Check "a push naming another game's/unknown playthrough is a 404" ((Push $a.apiKey $game "zz" $v3 ([guid]::NewGuid().ToString())).Status -eq 404)
    $vh = @(Admin-Get "/api/games/$game/versions?playthrough=$($hard.id)")
    Check "the version list can be narrowed to one playthrough" ($vh.Count -eq 1 -and $vh[0].id -eq $h1)
    $agentVersions = @((Call "GET" "/api/agent/games/$game/versions" $b.apiKey).Json)
    Check "an agent's own list is its playthrough's" ($agentVersions.Count -eq 1 -and $agentVersions[0].id -eq $h1)

    Write-Host ""
    Write-Host "==== 4. A conflict belongs to one playthrough ===="
    $cx = Push $c.apiKey $game "stale-hard" $v2 $hard.id
    Check "a stale push to Hard mode is a conflict" ($cx.Json.status -eq "Conflict")
    Check "the conflict names Hard mode and its head" ($cx.Json.conflict.playthroughId -eq $hard.id -and $cx.Json.conflict.versionAId -eq $h1)
    Check "Hard mode shows the conflict, Main does not" ((Pt $game "Hard mode").hasOpenConflict -eq $true -and (Pt $game "Main").hasOpenConflict -eq $false)
    $v4r = Push $a.apiKey $game "v4" $v3
    Check "Main keeps fast-forwarding under Hard mode's open conflict" ($v4r.Json.status -eq "Created")
    $v4 = $v4r.Json.version.id
    $again = Push $c.apiKey $game "stale-hard-2" $v2 $hard.id
    Check "a second stale push folds into the same conflict" ($again.Json.status -eq "Conflict" -and $again.Json.conflict.id -eq $cx.Json.conflict.id -and $again.Json.conflict.count -eq 2)
    $resolve = Admin-Post "/api/conflicts/$($cx.Json.conflict.id)/resolve?version=$h1"
    Check "resolving it keeps Hard mode's own head" ($resolve.Status -eq 200 -and (Pt $game "Hard mode").head.id -eq $h1 -and (Pt $game "Main").head.id -eq $v4)

    Write-Host ""
    Write-Host "==== 5. A lease is per playthrough ===="
    $la = (Call "POST" "/api/games/$game/lease?playthrough=$($main.id)" $a.apiKey).Json
    Check "A holds Main" ($la.granted -eq $true -and $la.lease.playthroughId -eq $main.id)
    $lb = (Call "POST" "/api/games/$game/lease" $b.apiKey).Json
    Check "B, naming nothing, holds Hard mode at the same time" ($lb.granted -eq $true -and $lb.lease.playthroughId -eq $hard.id)
    $lc = (Call "POST" "/api/games/$game/lease?playthrough=$($main.id)" $c.apiKey).Json
    Check "C is refused Main, held by A" ($lc.granted -eq $false -and $lc.lease.holderMachineName -eq "PtA")
    $lc2 = (Call "POST" "/api/games/$game/lease?playthrough=$($old.id)" $c.apiKey).Json
    Check "C can hold a third playthrough" ($lc2.granted -eq $true)
    Check "renewing without naming a playthrough renews the machine's lease" ((Call "POST" "/api/games/$game/lease/renew" $a.apiKey).Status -eq 200)
    (Call "DELETE" "/api/games/$game/lease/force?playthrough=$($hard.id)").Status | Out-Null
    $lb2 = (Call "POST" "/api/games/$game/lease?playthrough=$($main.id)" $b.apiKey).Json
    Check "forcing one playthrough's lease free leaves the others held" ($lb2.granted -eq $false -and $lb2.lease.holderMachineName -eq "PtA")
    Call "DELETE" "/api/games/$game/lease" $a.apiKey | Out-Null
    $lb3 = (Call "POST" "/api/games/$game/lease?playthrough=$($main.id)" $b.apiKey).Json
    Check "releasing without naming a playthrough frees the machine's lease" ($lb3.granted -eq $true)
    Call "DELETE" "/api/games/$game/lease/force" | Out-Null
    Check "forcing with no playthrough frees every lease of the game" ((Call "POST" "/api/games/$game/lease?playthrough=$($main.id)" $c.apiKey).Json.granted -eq $true)
    Call "DELETE" "/api/games/$game/lease/force" | Out-Null

    Write-Host ""
    Write-Host "==== 6. Fan-out goes only to the devices on the playthrough ===="
    Clear-Pending $game   # the conflict resolved above already queued a pull for Hard mode's devices
    $sl = Admin-Post "/api/games/$game/set-latest?version=$v3"
    Check "Set as Latest on a Main version succeeds" ($sl.Status -eq 200)
    $queued = @(Pending-Pulls $game)
    Check "a pull is queued for A (on Main) and not for B (on Hard mode)" ($queued -contains "PtA" -and $queued -notcontains "PtB")
    Check "the head moved to that Main version" ((Pt $game "Main").head.id -eq $v3 -and (Pt $game "Hard mode").head.id -eq $h1)
    $sl2 = Admin-Post "/api/games/$game/set-latest?version=$h1"
    $queued2 = @(Pending-Pulls $game)
    Check "Set as Latest on a Hard-mode version queues B (and A already had one pending)" ($sl2.Status -eq 200 -and $queued2 -contains "PtB")
    Check "the version went to Hard mode's head, not Main's" ((Pt $game "Hard mode").head.id -eq $h1 -and (Pt $game "Main").head.id -eq $v3)

    Write-Host ""
    Write-Host "==== 7. Retention counts each playthrough, and keeps other lines' heads ===="
    $g2 = (New-Game "Retention Game $(Get-Date -Format HHmmss)").id
    Admin-Post "/api/games/$g2/retain?value=2" | Out-Null
    $pm = Register "PtRet"
    $prev = $null; $ids = @()
    foreach ($t in 1..3) { $x = (Push $pm.apiKey $g2 "m$t" $prev).Json.version; $ids += $x.id; $prev = $x.id }
    $main2 = Pt $g2 "Main"
    Check "Main keeps its newest 2 of 3" ($main2.versionCount -eq 2 -and $main2.head.id -eq $ids[2])
    $keep = (Admin-Post "/api/games/$g2/playthroughs" @{ name = "Keep"; fromVersionId = $ids[1] }).Json
    $x = (Push $pm.apiKey $g2 "m4" $prev).Json.version
    Check "a fourth Main push would prune the second, but it is Keep's head, so it stays" (@(@(Admin-Get "/api/games/$g2/versions?playthrough=$($main2.id)") | Where-Object { $_.id -eq $ids[1] }).Count -eq 1)
    Check "Main therefore holds 3 (newest two plus Keep's head)" ((Pt $g2 "Main").versionCount -eq 3)
    $side = (Admin-Post "/api/games/$g2/playthroughs" @{ name = "Side"; fromPlaythroughId = $main2.id }).Json
    $sprev = $x.id
    foreach ($t in 1..4) { $s = (Push $pm.apiKey $g2 "s$t" $sprev $side.id).Json.version; $sprev = $s.id }
    Check "Side keeps its own newest 2 of 4 (the game's limit applies to each playthrough)" ((Pt $g2 "Side").versionCount -eq 2)
    Check "Main's versions were not spent on Side's" ((Pt $g2 "Main").versionCount -eq 3)
    $delHead = Call "DELETE" "/api/games/$g2/versions/$($ids[1])"
    Check "deleting a version that is another playthrough's head is refused" ($delHead.Status -ge 400)
    # The console's Saves card prunes from one playthrough's tab: naming it must leave the other lines alone.
    Admin-Post "/api/games/$g2/retain?value=10" | Out-Null
    foreach ($t in 5..6) { $s = (Push $pm.apiKey $g2 "s$t" $sprev $side.id).Json.version; $sprev = $s.id }
    Admin-Post "/api/games/$g2/retain?value=1" | Out-Null
    $mainBefore = (Pt $g2 "Main").versionCount
    $pr = Admin-Post "/api/games/$g2/prune?playthrough=$($side.id)"
    Check "Prune naming Side cuts Side to its newest version" ($pr.Status -eq 200 -and (Pt $g2 "Side").versionCount -eq 1)
    Check "... and leaves Main as it was" ((Pt $g2 "Main").versionCount -eq $mainBefore)
    Admin-Post "/api/games/$g2/prune" | Out-Null
    Check "Prune naming no playthrough still prunes Main (keeping Keep's head)" ((Pt $g2 "Main").versionCount -eq 2)

    Write-Host ""
    Write-Host "==== 8. Archive freezes; the default cannot be archived; a device keeps its playthrough ===="
    Check "the default playthrough cannot be archived" ((Admin-Post "/api/games/$game/playthroughs/$($main.id)/archive").Status -eq 400)
    $arch = Admin-Post "/api/games/$game/playthroughs/$($hard.id)/archive"
    Check "a playthrough a device is on cannot be archived (B is on Hard mode)" ($arch.Status -eq 409 -and $arch.Text -match "PtB")
    Call "POST" "/api/agent/games/$game/playthroughs/$($main.id)/select" $b.apiKey | Out-Null
    $arch2 = Admin-Post "/api/games/$game/playthroughs/$($hard.id)/archive"
    Check "once B is back on Main it can be archived" ($arch2.Status -eq 200 -and $null -ne $arch2.Json.archivedAt)
    Check "pushing to the archived playthrough is a 409" ((Push $c.apiKey $game "late" $h1 $hard.id).Status -eq 409)
    Check "pulling from it is a 409" ((Pull $c.apiKey $game $hard.id).Status -eq 409)
    Check "selecting it is a 409" ((Call "POST" "/api/agent/games/$game/playthroughs/$($hard.id)/select" $c.apiKey).Status -eq 409)
    Check "leasing it is a 409" ((Call "POST" "/api/games/$game/lease?playthrough=$($hard.id)" $c.apiKey).Status -eq 409)
    Check "its versions can still be listed" (@(@(Admin-Get "/api/games/$game/versions?playthrough=$($hard.id)") | Where-Object { $_.id -eq $h1 }).Count -eq 1)
    Check "archived playthroughs sort last" ((Playthroughs $game | Select-Object -Last 1).id -eq $hard.id)
    Check "unarchiving brings it back" ((Admin-Post "/api/games/$game/playthroughs/$($hard.id)/unarchive").Json.archivedAt -eq $null)

    $dev = Register "PtNewcomer"
    $oldDefault = (Pull $dev.apiKey $game)
    Check "before any change of default, a brand-new device gets Main" ($oldDefault.Content -eq "v3")
    $dev2 = Register "PtNewcomer2"
    $mk = Admin-Post "/api/games/$game/playthroughs/$($hard.id)/default"
    Check "Hard mode becomes the default" ($mk.Json.isDefault -eq $true -and (Pt $game "Main").isDefault -eq $false)
    Check "the mirror now reports Hard mode's head as the game's" ((@(Admin-Get "/api/overview") | Where-Object { $_.game.id -eq $game }).head.id -eq $h1)
    Check "a device that already followed Main stays on Main" ((Pull $dev.apiKey $game).Content -eq "v3")
    $devState = (Call "GET" "/api/agent/games/$game/state" $dev.apiKey).Json
    Check "and its agent state reports Main's head, not the new default's" ($devState.head.id -eq $v3)
    Check "an agent's state can name another playthrough" ((Call "GET" "/api/agent/games/$game/state?playthrough=$($hard.id)" $dev.apiKey).Json.head.id -eq $h1)
    Check "an agent's state for an unknown game is a 404" ((Call "GET" "/api/agent/games/$([guid]::NewGuid())/state" $dev.apiKey).Status -eq 404)
    Check "a device seen for the first time now gets the new default" ((Pull $dev2.apiKey $game).Content -eq "h1")
    Check "exactly one playthrough is the default" (@(Playthroughs $game | Where-Object { $_.isDefault }).Count -eq 1)
    Admin-Post "/api/games/$game/playthroughs/$($main.id)/default" | Out-Null

    Write-Host ""
    Write-Host "==== 9. A delta is negotiated only against the same playthrough's head ===="
    $g3 = (New-Game "Delta Game $(Get-Date -Format HHmmss)").id
    $pd = Register "PtDelta"
    function Chunked($key, $game, [hashtable]$files, $parent, [string]$pt, [bool]$complete = $true) {
        $zip = New-Zip $files
        $manifest = @($files.Keys | Sort-Object | ForEach-Object {
            $bytes = [System.Text.Encoding]::UTF8.GetBytes([string]$files[$_])
            @{ path = $_; sha256 = (Sha $bytes); size = $bytes.Length } })
        $body = @{ contentHash = (Sha $zip); parentVersionId = $parent; force = $false; files = $manifest }
        if ($pt) { $body.playthroughId = $pt }
        $begin = Call "POST" "/api/games/$game/upload/begin" $key $body
        if ($begin.Json.sessionId -and $complete) {
            Call "PUT" "/api/games/$game/upload/$($begin.Json.sessionId)/chunk?offset=0" $key $null $zip | Out-Null
            $done = Call "POST" "/api/games/$game/upload/$($begin.Json.sessionId)/complete" $key
            return [pscustomobject]@{ Begin = $begin.Json; Done = $done.Json }
        }
        [pscustomobject]@{ Begin = $begin.Json; Done = $null }
    }
    $files = @{ "slot1.sav" = "one"; "slot2.sav" = "two"; "slot3.sav" = "three" }
    $first = Chunked $pd.apiKey $g3 $files $null ""
    Check "the first chunked push is a full archive" ($first.Begin.useDeltaPath -eq $false -and $first.Done.status -eq "Created")
    $mainD = Pt $g3 "Main"
    $headD = $first.Done.version.id
    $files2 = @{ "slot1.sav" = "one"; "slot2.sav" = "TWO"; "slot3.sav" = "three" }
    $probe = Chunked $pd.apiKey $g3 $files2 $headD "" $false
    Check "a push to the same playthrough negotiates a delta of exactly the changed file" ($probe.Begin.useDeltaPath -eq $true -and @($probe.Begin.needPaths).Count -eq 1 -and $probe.Begin.needPaths[0] -eq "slot2.sav")
    $other = (Admin-Post "/api/games/$g3/playthroughs" @{ name = "Elsewhere" }).Json
    $cross = Chunked $pd.apiKey $g3 $files2 $null $other.id $false
    Check "a push to an empty playthrough never copies forward from Main's head" ($cross.Begin.useDeltaPath -eq $false)
    $forked = (Admin-Post "/api/games/$g3/playthroughs" @{ name = "Forked"; fromPlaythroughId = $mainD.id }).Json
    $viaFork = Chunked $pd.apiKey $g3 $files2 $headD $forked.id $false
    Check "a fork can delta against the version it points at (its head), whose baseline is shared" ($viaFork.Begin.useDeltaPath -eq $true)

    Write-Host ""
    Write-Host "==== 10. The switch command only goes to an agent that can run it ===="
    $sw = Register "PtSwitcher"
    $body = @{ machineId = $sw.machineId; gameId = $game; type = "SwitchPlaythrough"; force = $false; playthroughId = $hard.id }
    Check "refused before the machine has reported any agent version" ((Admin-Post "/api/commands" $body).Status -eq 400)
    $beat = @{ agentVersion = "0.8.1"; platform = "windows" }
    Call "POST" "/api/agent/health" $sw.apiKey $beat | Out-Null
    $tooOld = Admin-Post "/api/commands" $body
    Check "refused for an agent older than the first that can switch" ($tooOld.Status -eq 400 -and $tooOld.Text -match "older")
    Call "POST" "/api/agent/health" $sw.apiKey @{ agentVersion = "9.9.9-ci"; platform = "windows" } | Out-Null
    $queuedSwitch = Admin-Post "/api/commands" $body
    Check "queued for a test build that reports 9.9.9-ci" ($queuedSwitch.Status -eq 200 -and $queuedSwitch.Json.playthroughId -eq $hard.id -and $queuedSwitch.Json.type -eq "SwitchPlaythrough")
    Call "POST" "/api/agent/health" $sw.apiKey @{ agentVersion = "0.8.0"; platform = "windows" } | Out-Null
    $got = @((Call "GET" "/api/agent/commands" $sw.apiKey).Json)
    Check "an agent that has since fallen behind is never handed it" (@($got | Where-Object { $_.type -eq "SwitchPlaythrough" }).Count -eq 0)
    Call "POST" "/api/agent/health" $sw.apiKey @{ agentVersion = "0.9.0"; platform = "windows" } | Out-Null
    $got2 = @((Call "GET" "/api/agent/commands" $sw.apiKey).Json)
    Check "once it reports 0.9.0 it is handed the command, with the playthrough" (@($got2 | Where-Object { $_.type -eq "SwitchPlaythrough" -and $_.playthroughId -eq $hard.id }).Count -eq 1)
    Check "a switch to an unknown playthrough is refused" ((Admin-Post "/api/commands" @{ machineId = $sw.machineId; gameId = $game; type = "SwitchPlaythrough"; force = $false; playthroughId = [guid]::NewGuid() }).Status -eq 400)
}
finally {
    Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue
    Remove-Item Env:ASPNETCORE_URLS, Env:Storage__DbPath, Env:Storage__ArchiveRoot, Env:Backup__Enabled -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Playthroughs (server): $pass passed, $fail failed"
if ($fail -gt 0) { Write-Host "Server log: $scratch\server.err.log"; exit 1 }
exit 0
