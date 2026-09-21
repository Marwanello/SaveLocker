# Console API + security hardening (SEC-*). Needs only the SERVER built in Debug — no agent.
#
# Each section proves an attack or a defect FAILS/IS FIXED, so most checks assert on the thing an
# attacker (or a bad input) would have gained, not just that the happy path still works.
#
#   API-01  POST /commands/bulk   validated up front, all-or-nothing, never stacks a duplicate Pending
#                                 command, can leave offline machines out.
#   API-02  exclude patterns      a pattern the matcher cannot evaluate (".." mid-pattern) is refused on
#                                 save AND preview instead of throwing inside every agent's hash; the
#                                 preview count is right against a real uploaded archive.
#   SEC-01  admin sessions        the console keeps a revocable random token, not the password; only
#                                 its hash is stored; Lock / sign-out-everywhere / a password change
#                                 end it; an expired one is refused.
#   SEC-02  PBKDF2                new hashes are v2 @ 600k; an old v1 hash still verifies and is upgraded
#                                 at the next sign-in.
#   SEC-03  throttle              wrong passwords lock a client out (429 + Retry-After) even for the
#                                 RIGHT password; the legacy header and re-registration are covered
#                                 too; forwarded headers are ignored by default; stale SESSION tokens
#                                 are not counted; the lockout lifts.
#   SEC-04  global backstop       a distributed guess is bounded.
#   SEC-05  trusted proxy         with Security:TrustedProxies declared, the forwarded address is what is
#                                 throttled — one client's lockout does not become everyone's.
#   SEC-06  registration          Security:RequireAdminPasswordToRegister closes the open first-time
#                                 registration; the default is unchanged.
#   SEC-07  response headers      CSP / nosniff / frame protection, and Swagger left working.
#   SEC-08  artwork fetch         a hostile image URL (foreign host, redirect off-list, credentials in
#                                 the URL, oversized, not-an-image, ".html" path) never puts anything
#                                 attacker-shaped under /art. Uses a stub SteamGridDB it hosts itself.
#   ART-01  art, not security     (same stub, same phase) a key added AFTER games exist fills in the games
#                                 with no art in the background and leaves a hand-picked cover alone;
#                                 the picker pages five at a time across the API's own page boundary and
#                                 costs no extra requests per page; previews are inline and shrunk; a
#                                 chosen image goes through the same hostile-URL rules; the default icon
#                                 is the first OPAQUE one; ?w= thumbnails are right-sized, cached,
#                                 allowlisted, never upscaled, and go stale when the cover is replaced.
#
# Checks that read or edit the SQLite file go through python's sqlite3 module (there is no sqlite3 CLI on
# the Windows box): a native Python 3 if there is one, else WSL's (as run-server-bugbounty-tests.ps1 does).
# With neither, those checks print SKIP. CI runs this suite on windows-latest, which has the first.
#
# Owns :5215 (server) and :5216 (stub) and .verify-console-security. Usage:
#   .\tests\run-console-security-tests.ps1 [-Port 5215] [-StubPort 5216] [-WslDistro Ubuntu]
param(
    [int]$Port = 5215,
    [int]$StubPort = 5216,
    [string]$WslDistro = "Ubuntu"
)

$ErrorActionPreference = "Continue"
Add-Type -AssemblyName System.Net.Http

$root      = Split-Path $PSScriptRoot -Parent
$scratch   = Join-Path $root ".verify-console-security"
$url       = "http://127.0.0.1:$Port"
$serverDll = Join-Path $root "src/Server/bin/Debug/net10.0/SaveLocker.Server.dll"
$inProgramFiles = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
$dotnet    = if (Test-Path $inProgramFiles) { $inProgramFiles } else { "dotnet" }
if (-not (Test-Path $serverDll)) { Write-Host "Server not built: $serverDll"; exit 2 }

foreach ($p in @($Port, $StubPort)) {
    if (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue) {
        Write-Host "Port $p is already in use - a server or stub from a previous run is probably still up."
        Write-Host "Find it with: Get-NetTCPConnection -LocalPort $p -State Listen | % { Get-Process -Id `$_.OwningProcess }"
        exit 2
    }
}
Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $scratch) { Write-Host "Could not wipe $scratch - a server from a previous run may still hold it."; exit 2 }
New-Item -ItemType Directory -Force $scratch | Out-Null

$pass = 0; $fail = 0
function Check($name, $cond) {
    if ($cond) { Write-Host "PASS: $name"; $script:pass++ }
    else        { Write-Host "FAIL: $name"; $script:fail++ }
}
function Skip($why) { Write-Host "SKIP: $why" }

# ---------------------------------------------------------------- HTTP helper
# Status, parsed body, and headers for BOTH success and error responses (Invoke-WebRequest throws on 4xx/5xx).
function Http($method, $path, $body = $null, $headers = @{}, $raw = $null) {
    $a = @{ Uri = "$url$path"; Method = $method; UseBasicParsing = $true; TimeoutSec = 120; Headers = $headers }
    if ($null -ne $raw)        { $a.Body = $raw; $a.ContentType = "application/json" }
    elseif ($null -ne $body)   { $a.Body = (ConvertTo-Json -InputObject $body -Depth 10 -Compress); $a.ContentType = "application/json" }
    try {
        $r = Invoke-WebRequest @a
        $status = [int]$r.StatusCode; $content = $r.Content; $hdrs = $r.Headers
    } catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { throw }
        $status = [int]$resp.StatusCode
        $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $content = $reader.ReadToEnd(); $hdrs = $resp.Headers
    }
    $json = $null
    try { $json = $content | ConvertFrom-Json } catch { }
    [pscustomobject]@{ Status = $status; Content = $content; Json = $json; Headers = $hdrs }
}
function Session($token) { return @{ "X-Admin-Session" = $token } }
function PwHeader($pw)        { return @{ "X-Admin-Password" = $pw } }
function Login($pw, $extra = @{}) { return (Http POST "/api/admin/session" @{ password = $pw } $extra) }

# ---------------------------------------------------------------- server phases
$script:proc = $null
$script:state = $null
function Start-Phase($name, [hashtable]$extraEnv = @{}) {
    $script:state = Join-Path $scratch $name
    New-Item -ItemType Directory -Force (Join-Path $script:state "archives") | Out-Null
    $set = @{
        ASPNETCORE_URLS = $url; Storage__DbPath = (Join-Path $script:state "savelocker.db")
        Storage__ArchiveRoot = (Join-Path $script:state "archives"); Backup__Enabled = "false"
        Logging__EventLog__LogLevel__Default = "None"; Art__BackfillOnStartup = "false"
    }
    foreach ($k in $extraEnv.Keys) { $set[$k] = $extraEnv[$k] }
    foreach ($k in $set.Keys) { Set-Item -Path "Env:$k" -Value $set[$k] }
    $script:proc = Start-Process -FilePath $dotnet -ArgumentList @($serverDll) -PassThru -NoNewWindow `
        -RedirectStandardOutput (Join-Path $script:state "out.log") -RedirectStandardError (Join-Path $script:state "err.log")
    foreach ($k in $set.Keys) { Remove-Item -Path "Env:$k" -ErrorAction SilentlyContinue }
    foreach ($i in 1..60) {
        Start-Sleep -Milliseconds 700
        try { Invoke-RestMethod "$url/api/admin/status" -TimeoutSec 3 | Out-Null; return } catch { }
    }
    Get-Content (Join-Path $script:state "err.log") -ErrorAction SilentlyContinue | Select-Object -Last 20
    Write-Host "FAIL: server ($name) did not start on $url"; Cleanup; exit 1
}
function Stop-Phase {
    if ($script:proc -and -not $script:proc.HasExited) { Stop-Process -Id $script:proc.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2   # the process keeps a lock on the SQLite file for a moment
}
$script:stubJob = $null
function Cleanup {
    Stop-Phase
    if ($script:stubJob) { Stop-Job $script:stubJob -ErrorAction SilentlyContinue; Remove-Job $script:stubJob -Force -ErrorAction SilentlyContinue }
}

# ---------------------------------------------------------------- SQLite via python (server must be STOPPED)
# A native Python 3 (its standard library carries sqlite3) is preferred; WSL's is the fallback for a
# Windows box that has only that. Native is what CI has: a hosted Windows runner has Python and no WSL distro.
$script:pyNative = $null
foreach ($cmd in @(Get-Command python, python3 -CommandType Application -All -ErrorAction SilentlyContinue)) {
    # The WindowsApps "python.exe" is a Store launcher, not an interpreter, whether or not Python is installed.
    if ($cmd.Source -like "*\WindowsApps\*") { continue }
    $ver = try { (& $cmd.Source --version 2>&1) -join "" } catch { "" }
    if ($ver -match "^Python 3") { $script:pyNative = $cmd.Source; break }
}
function ConvertTo-WslPath($p) { $d = $p.Substring(0, 1).ToLower(); return "/mnt/$d" + ($p.Substring(2) -replace '\\', '/') }
function Invoke-Sqlite($sql, [switch]$Write) {
    $py = Join-Path $scratch "query.py"; $sqlFile = Join-Path $scratch "query.sql"
    @'
import sqlite3, sys
db, sqlfile, mode = sys.argv[1], sys.argv[2], sys.argv[3]
sql = open(sqlfile, encoding="utf-8").read()
if mode == "rw":
    con = sqlite3.connect(db)
    con.executescript(sql)      # one call may carry several statements
    con.commit()
else:
    con = sqlite3.connect("file:" + db + "?mode=ro", uri=True)
    for row in con.execute(sql).fetchall():
        print("|".join("" if c is None else str(c) for c in row))
'@ | Set-Content -Path $py -Encoding utf8
    Set-Content -Path $sqlFile -Value $sql -Encoding utf8
    $db = Join-Path $script:state "savelocker.db"
    $mode = if ($Write) { "rw" } else { "ro" }
    if ($script:pyNative) { return (& $script:pyNative $py $db $sqlFile $mode 2>&1) }
    return (& wsl -d $WslDistro -- python3 (ConvertTo-WslPath $py) (ConvertTo-WslPath $db) (ConvertTo-WslPath $sqlFile) $mode 2>&1)
}
$wslOk = $false
# (No inline python here: PowerShell 5.1 strips the inner quotes before bash sees them.)
if (-not $script:pyNative) {
    try { $wslOk = ((& wsl -d $WslDistro -- python3 --version 2>&1) -join "") -match "^Python 3" } catch { }
}
$sqliteOk = [bool]$script:pyNative -or $wslOk
if (-not $sqliteOk) { Write-Host "NOTE: neither a native Python 3 nor WSL python3 ($WslDistro) is usable; database-level checks will be skipped." }

function Sha256Hex($s) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    return ([BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($s))) -replace '-', '').ToLower()
}
# A legacy "v1:salt:hash" password hash (100,000 iterations) — what every install before v2 stored.
function New-V1Hash($password) {
    $salt = New-Object byte[] 16; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($salt)
    $kdf = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($password, $salt, 100000, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    return "v1:" + [Convert]::ToBase64String($salt) + ":" + [Convert]::ToBase64String($kdf.GetBytes(32))
}
function NewCmd($machineId, $type = "Sync", $game = $null) { return @{ machineId = $machineId; gameId = $game; type = $type; force = $false } }
function Actions { return @((Http GET "/api/audit?limit=500" $null $script:auth).Json | ForEach-Object { $_.action }) }

try {
# =====================================================================================
Write-Host ""; Write-Host "==== Phase 1: console API (open server), sessions, PBKDF2, headers ===="
$script:auth = @{}
Start-Phase "main" @{ Security__MaxFailedAttempts = "1000" }

# ------------------------------------------------------------------ API-01: bulk commands
Write-Host ""; Write-Host "-- API-01: POST /commands/bulk"
$m1 = (Http POST "/api/machines/register" @{ name = "SEC-M1" }).Json
$m2 = (Http POST "/api/machines/register" @{ name = "SEC-M2" }).Json
$gid = (Http POST "/api/games" @{ name = "SEC Game" }).Json.id
$two = @{ commands = @((NewCmd $m1.machineId), (NewCmd $m2.machineId)) }

$b1 = Http POST "/api/commands/bulk" $two
Check "bulk: a command is queued per machine" ($b1.Status -eq 200 -and @($b1.Json.queued).Count -eq 2 -and @($b1.Json.skipped).Count -eq 0)
$b2 = Http POST "/api/commands/bulk" $two
$ids1 = (@($b1.Json.queued | ForEach-Object { $_.id }) | Sort-Object) -join ","
$ids2 = (@($b2.Json.queued | ForEach-Object { $_.id }) | Sort-Object) -join ","
Check "bulk: pressing it again answers with the SAME pending commands, not a second pair" ($b2.Status -eq 200 -and $ids1 -eq $ids2)
$pending = @((Http GET "/api/commands").Json | Where-Object { $_.status -eq "Pending" -and $_.type -eq "Sync" -and $null -eq $_.gameId })
Check "bulk: exactly one Pending Sync per machine exists afterwards" ($pending.Count -eq 2)

$before = @((Http GET "/api/commands").Json).Count
$mixed = Http POST "/api/commands/bulk" @{ commands = @((NewCmd $m1.machineId "Scan"), (NewCmd ([guid]::NewGuid().ToString()) "Scan")) }
$after = @((Http GET "/api/commands").Json).Count
Check "bulk: one unknown machine refuses the whole call (400) ..." ($mixed.Status -eq 400 -and $mixed.Content -match "Unknown machine")
Check "bulk: ... and the valid request beside it was NOT queued (all-or-nothing)" ($after -eq $before)
Check "bulk: an empty list is refused (400)" ((Http POST "/api/commands/bulk" @{ commands = @() }).Status -eq 400)
$tooMany = @(1..101 | ForEach-Object { NewCmd $m1.machineId "Scan" })
Check "bulk: more than 100 commands is refused (400)" ((Http POST "/api/commands/bulk" @{ commands = $tooMany }).Status -eq 400)
$numeric = '{"commands":[{"machineId":"' + $m1.machineId + '","gameId":null,"type":99,"force":false}]}'
Check "bulk: an undefined command type is refused (400)" ((Http POST "/api/commands/bulk" $null @{} $numeric).Status -eq 400)
Check "bulk: an unknown game is refused (400)" ((Http POST "/api/commands/bulk" @{ commands = @((NewCmd $m1.machineId "Pull" ([guid]::NewGuid().ToString()))) }).Status -eq 400)
Check "single /commands: an unknown machine is a 400, not an unhandled 500" `
    ((Http POST "/api/commands" (NewCmd ([guid]::NewGuid().ToString()))).Status -eq 400)

$skipAll = Http POST "/api/commands/bulk" @{ commands = @((NewCmd $m1.machineId "Scan"), (NewCmd $m2.machineId "Scan")); skipMachinesUnseenForSeconds = 0 }
Check "bulk: skipMachinesUnseenForSeconds=0 leaves out every machine (none seen 'this instant'), naming them" `
    ($skipAll.Status -eq 200 -and @($skipAll.Json.queued).Count -eq 0 -and @($skipAll.Json.skipped).Count -eq 2 -and $skipAll.Json.skipped[0].reason -eq "offline")
$skipNone = Http POST "/api/commands/bulk" @{ commands = @((NewCmd $m1.machineId "Scan"), (NewCmd $m2.machineId "Scan")); skipMachinesUnseenForSeconds = 3600 }
Check "bulk: a wide window skips nobody" ($skipNone.Status -eq 200 -and @($skipNone.Json.queued).Count -eq 2 -and @($skipNone.Json.skipped).Count -eq 0)

# ------------------------------------------------------------------ API-02: exclude patterns + preview
Write-Host ""; Write-Host "-- API-02: exclude patterns and the dry-run preview"
$dotdot = '..\..\x'
$bad = Http POST "/api/games/$gid/excludes" @($dotdot)
Check "excludes: a '..' pattern is refused on save (400) with the reason, not stored" ($bad.Status -eq 400 -and $bad.Content -match "Invalid exclude pattern")
$badPrev = Http POST "/api/games/$gid/excludes/preview" @($dotdot)
Check "preview: the same pattern is a 400 (not an unhandled 500)" ($badPrev.Status -eq 400 -and $badPrev.Content -match "Invalid exclude pattern")
Check "excludes: a line break inside a pattern is refused (400)" ((Http POST "/api/games/$gid/excludes" @("a`nb")).Status -eq 400)
$many = @(1..101 | ForEach-Object { "p$_*.x" })
Check "excludes: more than 100 patterns is refused (400)" ((Http POST "/api/games/$gid/excludes" $many).Status -eq 400)
Check "excludes: an over-long pattern is refused (400)" ((Http POST "/api/games/$gid/excludes" @("a" * 300)).Status -eq 400)
Check "excludes: a valid list saves (200)" ((Http POST "/api/games/$gid/excludes" @("*.tmp")).Status -eq 200)
Check "preview: an unknown game is 404" ((Http POST "/api/games/$([guid]::NewGuid())/excludes/preview" @("*.log")).Status -eq 404)
Check "preview: a game with no head version reports 0" ((Http POST "/api/games/$gid/excludes/preview" @("*.log")).Json.wouldExclude -eq 0)

# A real archive as the head: keep.sav, debug.log, sub/other.log, Sub/Deep/x.LOG
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $scratch "head.zip"
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, "Create")
foreach ($n in @("keep.sav", "debug.log", "sub/other.log", "Sub/Deep/x.LOG")) {
    $e = $zip.CreateEntry($n); $w = New-Object System.IO.StreamWriter($e.Open()); $w.Write("data $n"); $w.Dispose()
}
$zip.Dispose()
$up = Invoke-WebRequest "$url/api/games/$gid/upload?hash=sec-hash-1" -Method Post -InFile $zipPath -ContentType "application/zip" `
    -Headers @{ "X-Api-Key" = $m1.apiKey } -UseBasicParsing
Check "setup: the archive uploaded and became the head" ([int]$up.StatusCode -eq 200)
function Preview($patterns) { return (Http POST "/api/games/$gid/excludes/preview" $patterns).Json.wouldExclude }
Check "preview: a bare '*.log' counts every .log at ANY depth, case-insensitively (3 of 4)" ((Preview @("*.log")) -eq 3)
Check "preview: '*.sav' counts 1" ((Preview @("*.sav")) -eq 1)
Check "preview: an anchored 'sub/*.log' counts only that folder (1)" ((Preview @("sub/*.log")) -eq 1)
Check "preview: a pattern matching nothing counts 0" ((Preview @("nothing-here")) -eq 0)
Check "preview: '**' counts all 4" ((Preview @("**")) -eq 4)

# ------------------------------------------------------------------ SEC-07: response headers (server is still OPEN here)
Write-Host ""; Write-Host "-- SEC-07: response headers"
$root1 = Http GET "/"
$csp = "" + $root1.Headers["Content-Security-Policy"]
Check "dashboard: CSP allows scripts only from itself" ($csp -match "script-src 'self'" -and $csp -notmatch "script-src[^;]*unsafe")
Check "dashboard: CSP forbids framing, plugins and <base>" ($csp -match "frame-ancestors 'none'" -and $csp -match "object-src 'none'" -and $csp -match "base-uri 'none'")
Check "dashboard: X-Frame-Options DENY + nosniff + no-referrer" `
    (("" + $root1.Headers["X-Frame-Options"]) -eq "DENY" -and ("" + $root1.Headers["X-Content-Type-Options"]) -eq "nosniff" -and ("" + $root1.Headers["Referrer-Policy"]) -eq "no-referrer")
$api = Http GET "/api/admin/status"
Check "API responses carry nosniff too" (("" + $api.Headers["X-Content-Type-Options"]) -eq "nosniff")
$sw = Http GET "/swagger/index.html"
Check "Swagger UI is NOT put under the dashboard's CSP (it needs inline script)" ($sw.Status -eq 200 -and [string]::IsNullOrEmpty("" + $sw.Headers["Content-Security-Policy"]))
Check "openapi document is left alone too" ([string]::IsNullOrEmpty("" + (Http GET "/openapi/v1.json").Headers["Content-Security-Policy"]))

# ------------------------------------------------------------------ SEC-01: sessions
Write-Host ""; Write-Host "-- SEC-01: admin sessions"
$pw1 = "correct horse battery"; $pw2 = "a different passphrase"
Check "setup: setting the admin password succeeds" ((Http POST "/api/admin/password" @{ password = $pw1 }).Status -eq 200)
Check "password is now required" ((Http GET "/api/admin/status").Json.passwordRequired -eq $true)
Check "no credential -> 401" ((Http GET "/api/overview").Status -eq 401)
Check "a made-up session token -> 401" ((Http GET "/api/overview" $null (Session "not-a-real-token")).Status -eq 401)
$wrong = Login "nope"
Check "sign-in with the wrong password -> 401" ($wrong.Status -eq 401 -and $wrong.Json.error -match "Wrong password")
$ok = Login $pw1
$tok = $ok.Json.token
Check "sign-in with the right password -> 200 and a long random token + expiry" `
    ($ok.Status -eq 200 -and $tok.Length -ge 40 -and ([datetime]$ok.Json.expiresAt) -gt (Get-Date).ToUniversalTime().AddDays(6))
Check "the token authorises admin requests" ((Http GET "/api/overview" $null (Session $tok)).Status -eq 200)
Check "the legacy X-Admin-Password header still works (scripts)" ((Http GET "/api/overview" $null (PwHeader $pw1)).Status -eq 200)
Check "a wrong legacy header -> 401" ((Http GET "/api/overview" $null (PwHeader "nope")).Status -eq 401)
Check "a session token is NOT accepted as if it were the password" ((Http GET "/api/overview" $null (PwHeader $tok)).Status -eq 401)
$tokKeep = (Login $pw1).Json.token

Check "Lock: DELETE /admin/session ends this session (204)" ((Http DELETE "/api/admin/session" $null (Session $tok)).Status -eq 204)
Check "Lock: the ended token no longer works" ((Http GET "/api/overview" $null (Session $tok)).Status -eq 401)
Check "Lock: another browser's session is unaffected" ((Http GET "/api/overview" $null (Session $tokKeep)).Status -eq 200)

$tok2 = (Login $pw1).Json.token
$all = Http DELETE "/api/admin/sessions" $null (Session $tok2)
Check "sign out everywhere: reports how many were ended (>= 2)" ($all.Status -eq 200 -and $all.Json.ended -ge 2)
Check "sign out everywhere: every token is dead, including the caller's and the other browser's" `
    ((Http GET "/api/overview" $null (Session $tok2)).Status -eq 401 -and (Http GET "/api/overview" $null (Session $tokKeep)).Status -eq 401)

$tok3 = (Login $pw1).Json.token
Check "changing the password with a session works (200)" ((Http POST "/api/admin/password" @{ password = $pw2 } (Session $tok3)).Status -eq 200)
Check "a password change ends every session, the caller's included" ((Http GET "/api/overview" $null (Session $tok3)).Status -eq 401)
Check "the OLD password no longer signs in" ((Login $pw1).Status -eq 401)
$tokA = (Login $pw2).Json.token
Check "the NEW password signs in" ($tokA.Length -ge 40)
$script:auth = Session $tokA
$acts = Actions
Check "audit: sign-ins, sign-outs and 'everywhere' are recorded" (($acts -contains "admin.login") -and ($acts -contains "admin.logout") -and ($acts -contains "admin.logout_all"))

# ------------------------------------------------------------------ SEC-06 (default half): registration unchanged
Write-Host ""; Write-Host "-- SEC-06: registration (default configuration)"
$newDefault = Http POST "/api/machines/register" @{ name = "SEC-NewDefault" }
Check "default: a NEW machine may still register without the password (LAN convenience unchanged)" ($newDefault.Status -eq 200)
Check "default: re-registering an EXISTING machine still needs the password (401)" ((Http POST "/api/machines/register" @{ name = "SEC-M1" }).Status -eq 401)
Check "default: ... and works with it" ((Http POST "/api/machines/register" @{ name = "SEC-M1" } (PwHeader $pw2)).Status -eq 200)

# ------------------------------------------------------------------ SEC-01/02: database-level
Stop-Phase
Write-Host ""; Write-Host "-- SEC-01/02: what is actually stored"
if ($sqliteOk) {
    $hash = ("" + (Invoke-Sqlite "SELECT Value FROM Settings WHERE [Key]='Admin:PasswordHash'")).Trim()
    Check "SEC-02: a newly set password is stored as v2 at 600,000 iterations" ($hash -like "v2:600000:*")
    Check "SEC-01: the raw session token is NOT in the database" ((("" + (Invoke-Sqlite "SELECT COUNT(*) FROM AdminSessions WHERE TokenHash='$tokA'")).Trim()) -eq "0")
    Check "SEC-01: its SHA-256 is (and only that)" ((("" + (Invoke-Sqlite "SELECT COUNT(*) FROM AdminSessions WHERE TokenHash='$(Sha256Hex $tokA)'")).Trim()) -eq "1")

    # Age the live session out and put an OLD-format password hash back, exactly as an upgraded install looks.
    $v1 = New-V1Hash $pw2
    Invoke-Sqlite "UPDATE AdminSessions SET ExpiresAt='2000-01-01 00:00:00'; UPDATE Settings SET Value='$v1' WHERE [Key]='Admin:PasswordHash'" -Write | Out-Null
    Check "setup: the stored hash is now a legacy v1 one" ((("" + (Invoke-Sqlite "SELECT Value FROM Settings WHERE [Key]='Admin:PasswordHash'")).Trim()) -like "v1:*")

    Start-Phase "main" @{ Security__MaxFailedAttempts = "1000" }
    Check "SEC-01: an EXPIRED session is refused (401)" ((Http GET "/api/overview" $null (Session $tokA)).Status -eq 401)
    Check "SEC-02: the legacy-hash password still verifies (upgrade does not lock anyone out)" ((Login $pw2).Status -eq 200)
    Check "SEC-02: a wrong password against a v1 hash is still refused" ((Login "nope").Status -eq 401)
    Stop-Phase
    $upgraded = ("" + (Invoke-Sqlite "SELECT Value FROM Settings WHERE [Key]='Admin:PasswordHash'")).Trim()
    Check "SEC-02: the successful sign-in re-wrote the hash as v2 @ 600,000" ($upgraded -like "v2:600000:*")
    Check "SEC-01: the expired session row was swept by that sign-in" ((("" + (Invoke-Sqlite "SELECT COUNT(*) FROM AdminSessions WHERE ExpiresAt < '2001-01-01'")).Trim()) -eq "0")
} else { Skip "database-level checks need Python 3 (native, or WSL via -WslDistro $WslDistro)" }

# A hash that lives in CONFIG (env) rather than the database cannot be written back; it must still verify.
$cfgHash = New-V1Hash "config-pw"
Start-Phase "cfghash" @{ Admin__PasswordHash = $cfgHash; Security__MaxFailedAttempts = "1000" }
Check "SEC-02: a legacy v1 hash supplied through configuration verifies" ((Login "config-pw").Status -eq 200)
Check "SEC-02: ... and rejects a wrong password" ((Login "nope").Status -eq 401)
Stop-Phase

# =====================================================================================
Write-Host ""; Write-Host "==== Phase 2: SEC-03 per-client throttle ===="
Start-Phase "throttle" @{ Security__MaxFailedAttempts = "3"; Security__LockoutSeconds = "5"; Security__FailureWindowSeconds = "60"; Security__GlobalMaxFailures = "1000" }
$pw = "throttle-pw"
$t1 = (Http POST "/api/machines/register" @{ name = "SEC-T1" }).Json
Http POST "/api/admin/password" @{ password = $pw } | Out-Null
$keep = (Login $pw).Json.token
Check "setup: signed in before any wrong guess" ($keep.Length -ge 40)
Check "guess 1 -> 401" ((Login "g1").Status -eq 401)
Check "guess 2 -> 401" ((Login "g2").Status -eq 401)
Check "guess 3 -> 401 (this one starts the lockout)" ((Login "g3").Status -eq 401)
$locked = Login $pw
$ra = 0; [void][int]::TryParse("" + $locked.Headers["Retry-After"], [ref]$ra)
Check "the RIGHT password is now refused: 429" ($locked.Status -eq 429)
Check "the refusal carries a usable Retry-After (1..5 s)" ($ra -ge 1 -and $ra -le 5)
Check "a live session is unaffected by the lockout" ((Http GET "/api/overview" $null (Session $keep)).Status -eq 200)
Check "the legacy header is throttled too (right password, 429)" ((Http GET "/api/overview" $null (PwHeader $pw)).Status -eq 429)
Check "re-registering an existing machine is throttled too (right password, 429)" ((Http POST "/api/machines/register" @{ name = "SEC-T1" } (PwHeader $pw)).Status -eq 429)
$spoof = Login $pw @{ "X-Forwarded-For" = "198.51.100.77" }
Check "spoofing X-Forwarded-For does NOT escape the lockout (headers are ignored by default)" ($spoof.Status -eq 429)
Start-Sleep -Seconds 6
Check "the lockout lifts by itself" ((Login $pw).Status -eq 200)
$junk = 1..12 | ForEach-Object { (Http GET "/api/overview" $null (Session "stale-$_")).Status }
Check "stale SESSION tokens are 401, never 429 (they are not guesses)" (@($junk | Where-Object { $_ -ne 401 }).Count -eq 0)
Check "... and do not stop the owner signing in afterwards" ((Login $pw).Status -eq 200)
Login "w1" | Out-Null; Login "w2" | Out-Null
Check "a correct password clears earlier misses ..." ((Login $pw).Status -eq 200)
Login "w3" | Out-Null; Login "w4" | Out-Null
Check "... so two more misses do not lock (would have been the 4th miss)" ((Login $pw).Status -eq 200)
$auth2 = Session (Login $pw).Json.token
Check "audit: the lockout was recorded" ((@((Http GET "/api/audit?limit=500" $null $auth2).Json | ForEach-Object { $_.action }) -contains "admin.lockout"))
Stop-Phase

# =====================================================================================
Write-Host ""; Write-Host "==== Phase 3: SEC-04 global backstop ===="
Start-Phase "global" @{ Security__MaxFailedAttempts = "1000"; Security__GlobalMaxFailures = "4"; Security__GlobalLockoutSeconds = "5" }
Http POST "/api/admin/password" @{ password = $pw } | Out-Null
1..4 | ForEach-Object { Login "wrong$_" | Out-Null }
Check "after 4 misses from anyone, even the right password is refused (429)" ((Login $pw).Status -eq 429)
Start-Sleep -Seconds 6
Check "the global lockout lifts" ((Login $pw).Status -eq 200)
Stop-Phase

# =====================================================================================
Write-Host ""; Write-Host "==== Phase 4: SEC-05 trusted proxy ===="
Start-Phase "proxy" @{ Security__TrustedProxies = "127.0.0.1,::1"; Security__ClientIpHeader = "X-Forwarded-For"; Security__MaxFailedAttempts = "3"; Security__LockoutSeconds = "30" }
Http POST "/api/admin/password" @{ password = $pw } | Out-Null
$A = @{ "X-Forwarded-For" = "203.0.113.5" }; $B = @{ "X-Forwarded-For" = "203.0.113.9" }
1..3 | ForEach-Object { Login "bad$_" $A | Out-Null }
Check "client A is locked out after 3 misses" ((Login $pw $A).Status -eq 429)
Check "client B (a different forwarded address) is NOT locked by A's misses" ((Login $pw $B).Status -eq 200)
Stop-Phase

# =====================================================================================
Write-Host ""; Write-Host "==== Phase 5: SEC-06 require the password to register ===="
Start-Phase "register" @{ Security__RequireAdminPasswordToRegister = "true"; Security__MaxFailedAttempts = "1000" }
Check "no admin password set yet: registering is still open (nothing to require)" ((Http POST "/api/machines/register" @{ name = "SEC-R0" }).Status -eq 200)
Http POST "/api/admin/password" @{ password = $pw } | Out-Null
$noPw = Http POST "/api/machines/register" @{ name = "SEC-R1" }
Check "a NEW machine without the password is refused (401) ..." ($noPw.Status -eq 401 -and $noPw.Content -match "requires the admin password")
Check "... and was not created" ((@((Http GET "/api/machines" $null (PwHeader $pw)).Json | Where-Object { $_.name -eq "SEC-R1" })).Count -eq 0)
Check "a NEW machine with a wrong password is refused (401)" ((Http POST "/api/machines/register" @{ name = "SEC-R1" } (PwHeader "nope")).Status -eq 401)
$withPw = Http POST "/api/machines/register" @{ name = "SEC-R1" } (PwHeader $pw)
Check "a NEW machine WITH the password registers (200, gets a key)" ($withPw.Status -eq 200 -and $withPw.Json.apiKey.Length -ge 40)
Stop-Phase

# =====================================================================================
Write-Host ""; Write-Host "==== Phase 6: SEC-08 artwork fetch against a stub SteamGridDB ===="
$listenerProbe = [System.Net.HttpListener]::new()
$listenerProbe.Prefixes.Add("http://127.0.0.1:$StubPort/"); $listenerProbe.Prefixes.Add("http://localhost:$StubPort/")
$canBind = $true; try { $listenerProbe.Start(); $listenerProbe.Stop() } catch { $canBind = $false }
if (-not $canBind) {
    Skip "HttpListener could not bind 127.0.0.1/localhost:$StubPort (needs a urlacl or an elevated shell)."
} else {
    $stubLog = Join-Path $scratch "stub.log"
    $stubScript = {
        param($port, $log)
        $png = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==")
        # Real images for the picker/thumbnail checks. Saved as 32-bit ARGB, so even the "opaque" icon is an
        # RGBA PNG - which is exactly what makes a header-only transparency check wrong.
        Add-Type -AssemblyName System.Drawing
        function NewPng([int]$w, [int]$h, [bool]$clearCorner) {
            $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
            $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::DarkSlateBlue, [System.Drawing.Color]::OrangeRed, 60.0)
            $g.FillRectangle($brush, $rect)
            $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 1)
            for ($i = 0; $i -lt ($w + $h); $i += 7) { $g.DrawLine($pen, $i, 0, 0, $i) }
            $g.Dispose()
            if ($clearCorner) { $bmp.SetPixel(0, 0, [System.Drawing.Color]::FromArgb(0, 0, 0, 0)) }
            $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
            return , $ms.ToArray()
        }
        $bigPng = NewPng 400 600 $false
        $iconClear = NewPng 8 8 $true
        $iconSolid = NewPng 8 8 $false
        # 12000x12000 = 144 million pixels, but one bit each and all clear: a few KB of PNG. The shape a
        # decode-size cap exists for - it is small enough to be inlined, and enormous once decoded.
        $bombBmp = New-Object System.Drawing.Bitmap(12000, 12000, [System.Drawing.Imaging.PixelFormat]::Format1bppIndexed)
        $bombMs = New-Object System.IO.MemoryStream; $bombBmp.Save($bombMs, [System.Drawing.Imaging.ImageFormat]::Png); $bombBmp.Dispose()
        $bombPng = $bombMs.ToArray()
        # The right magic number and nothing behind it; and a 200 with no body at all.
        $corruptPng = [byte[]](@(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) + (1..40))
        $emptyBody = [byte[]]@()
        $l = [System.Net.HttpListener]::new()
        $l.Prefixes.Add("http://127.0.0.1:$port/"); $l.Prefixes.Add("http://localhost:$port/")
        $l.Start()
        $base = "http://127.0.0.1:$port"
        function Send($ctx, $status, $bytes, $type) {
            $ctx.Response.StatusCode = $status; $ctx.Response.ContentType = $type
            if ($bytes) { $ctx.Response.ContentLength64 = $bytes.Length; $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length) }
            $ctx.Response.Close()
        }
        function Json($ctx, $o) { Send $ctx 200 ([Text.Encoding]::UTF8.GetBytes(($o | ConvertTo-Json -Depth 6 -Compress))) "application/json" }
        while ($true) {
            $ctx = $l.GetContext()
            $path = $ctx.Request.Url.AbsolutePath; $hostName = $ctx.Request.Url.Host
            Add-Content -Path $log -Value "$($ctx.Request.HttpMethod) $hostName $path"
            try {
                if ($path -eq "/ping") { Send $ctx 200 ([Text.Encoding]::UTF8.GetBytes("pong")) "text/plain" }
                elseif ($path -like "/api/v2/search/autocomplete/*") {
                    $id = if ($path -like "*Good*") { 1 } elseif ($path -like "*Paged*") { 3 } elseif ($path -like "*Opaque*") { 4 } elseif ($path -like "*Bomb*") { 5 } elseif ($path -like "*Corrupt*") { 6 } else { 2 }
                    Json $ctx @{ success = $true; data = @(@{ id = $id }) }
                }
                # Picker fixtures. Game 3 has 12 covers spread over two API pages of 7 and 5 - deliberately not
                # the console's five-per-page, so a slice that straddles the API boundary is exercised. Game 4
                # has a real 400x600 cover and two icons, the FIRST of them transparent. Game 5's one cover is
                # the 144-megapixel image; game 6's are an image that is only a header, and an empty body.
                elseif ($path -match "^/api/v2/(grids|heroes|logos|icons)/game/([3456])$") {
                    $kind = $Matches[1]; $gid = [int]$Matches[2]
                    $items = @(); $total = 0
                    if ($gid -eq 3 -and $kind -eq "grids") {
                        $apiPage = [int]("0" + $ctx.Request.QueryString["page"])
                        $range = if ($apiPage -eq 0) { 1..7 } elseif ($apiPage -eq 1) { 8..12 } else { @() }
                        $items = @($range | ForEach-Object { @{ id = $_; url = "$base/img/good.png?n=$_"; thumb = "$base/img/big.png"; width = 600; height = 900; author = @{ name = "artist$_" } } })
                        $total = 12
                    }
                    elseif ($gid -eq 4 -and $kind -eq "grids") { $items = @(@{ id = 40; url = "$base/img/big.png"; thumb = "$base/img/big.png" }); $total = 1 }
                    elseif ($gid -eq 5 -and $kind -eq "grids") { $items = @(@{ id = 50; url = "$base/img/bomb.png"; thumb = "$base/img/bomb.png" }); $total = 1 }
                    elseif ($gid -eq 6 -and $kind -eq "grids") { $items = @(@{ id = 60; url = "$base/img/corrupt.png"; thumb = "$base/img/corrupt.png" }, @{ id = 61; url = "$base/img/empty.png"; thumb = "$base/img/empty.png" }); $total = 2 }
                    elseif ($gid -eq 6 -and $kind -eq "icons") { $items = @(@{ id = 62; url = "$base/img/corrupt.png" }); $total = 1 }
                    elseif ($gid -eq 4 -and $kind -eq "icons") { $items = @(@{ id = 41; url = "$base/img/icon-clear.png" }, @{ id = 42; url = "$base/img/icon-solid.png" }); $total = 2 }
                    Json $ctx @{ success = $true; total = $total; data = $items }
                }
                elseif ($path -match "^/api/v2/(grids|heroes|logos|icons)/game/(\d+)") {
                    $kind = $Matches[1]; $gid = [int]$Matches[2]
                    $u = if ($gid -eq 1) {
                        switch ($kind) {
                            "grids"  { "$base/img/good.png" }
                            "heroes" { "http://localhost:$port/img/hero.jpg" }        # host not on the allowlist
                            "logos"  { "$base/img/fake.png" }                         # allowed host, body is HTML
                            "icons"  { "$base/img/pngpage.html" }                     # allowed host, PNG bytes, .html path
                        }
                    } else {
                        switch ($kind) {
                            "grids"  { "$base/redir" }                                # allowed host -> 302 to a non-allowed one
                            "heroes" { "$base/img/huge-declared.png" }                # Content-Length 30 MB
                            "logos"  { "$base/img/huge-streamed.png" }                # chunked, 27 MB
                            "icons"  { "http://user:pw@127.0.0.1:$port/img/good.png" }# credentials in the URL
                        }
                    }
                    Json $ctx @{ success = $true; data = @(@{ url = $u }) }
                }
                elseif ($path -eq "/redir") { $ctx.Response.StatusCode = 302; $ctx.Response.RedirectLocation = "http://localhost:$port/img/good.png"; $ctx.Response.Close() }
                elseif ($path -eq "/img/good.png" -or $path -eq "/img/pngpage.html") { Send $ctx 200 $png "application/octet-stream" }
                elseif ($path -eq "/img/big.png") { Send $ctx 200 $bigPng "image/png" }
                elseif ($path -eq "/img/bomb.png") { Send $ctx 200 $bombPng "image/png" }
                elseif ($path -eq "/img/corrupt.png") { Send $ctx 200 $corruptPng "image/png" }
                elseif ($path -eq "/img/empty.png") { Send $ctx 200 $emptyBody "image/png" }
                elseif ($path -eq "/img/icon-clear.png") { Send $ctx 200 $iconClear "image/png" }
                elseif ($path -eq "/img/icon-solid.png") { Send $ctx 200 $iconSolid "image/png" }
                elseif ($path -eq "/img/fake.png") { Send $ctx 200 ([Text.Encoding]::UTF8.GetBytes("<html><script>alert(1)</script></html>")) "image/png" }
                elseif ($path -eq "/img/huge-declared.png") {
                    $ctx.Response.StatusCode = 200; $ctx.Response.ContentLength64 = 30MB
                    $chunk = New-Object byte[] 65536; [Array]::Copy($png, $chunk, $png.Length)
                    $ctx.Response.OutputStream.Write($chunk, 0, $chunk.Length); $ctx.Response.Abort()
                }
                elseif ($path -eq "/img/huge-streamed.png") {
                    $ctx.Response.StatusCode = 200; $ctx.Response.SendChunked = $true
                    $chunk = New-Object byte[] 1MB; [Array]::Copy($png, $chunk, $png.Length)
                    1..27 | ForEach-Object { $ctx.Response.OutputStream.Write($chunk, 0, $chunk.Length) }
                    $ctx.Response.Close()
                }
                else { Send $ctx 404 $null "text/plain" }
            } catch { try { $ctx.Response.Abort() } catch { } }
        }
    }
    $script:stubJob = Start-Job -ScriptBlock $stubScript -ArgumentList $StubPort, $stubLog
    $ready = $false
    foreach ($i in 1..30) { Start-Sleep -Milliseconds 500; try { if ((Invoke-RestMethod "http://127.0.0.1:$StubPort/ping" -TimeoutSec 2) -eq "pong") { $ready = $true; break } } catch { } }
    Check "setup: the stub SteamGridDB is listening" $ready

    if ($ready) {
        $artRoot = Join-Path $scratch "art"
        Start-Phase "art" @{
            Art__ApiBaseUrl = "http://127.0.0.1:$StubPort/api/v2/"; Art__AllowedImageHosts = "127.0.0.1"
            Art__AllowInsecureImageUrls = "true"; SteamGridDb__ApiKey = "test-key"; Storage__ArtRoot = $artRoot
        }
        # Creating a game triggers the art fetch synchronously.
        Http POST "/api/games" @{ name = "SEC Art Good" } | Out-Null
        Http POST "/api/games" @{ name = "SEC Art Bad" } | Out-Null
        $games = (Http GET "/api/overview").Json | ForEach-Object { $_.game }
        $good = $games | Where-Object { $_.name -eq "SEC Art Good" }
        $badG = $games | Where-Object { $_.name -eq "SEC Art Bad" }
        $log = @(Get-Content $stubLog)

        Check "art: a real image from an allowed host is stored (positive control)" ($good.gridUrl -like "/art/*/grid.png*")
        Check "art: the stored file's TYPE comes from its bytes: a PNG behind a '.html' URL is saved as .png" ($good.iconUrl -like "/art/*/icon.png*" -and $good.iconUrl -notlike "*.html*")
        Check "art: an image on a host that is not allowlisted is refused ..." ($null -eq $good.heroUrl)
        Check "art: ... without the server ever contacting it" (@($log | Where-Object { $_ -eq "GET localhost /img/hero.jpg" }).Count -eq 0)
        Check "art: an HTML body dressed up as .png is refused (not an image)" ($null -eq $good.logoUrl)
        $goodDir = Join-Path $artRoot ([guid]$good.id).ToString("N")
        $files = @(Get-ChildItem $goodDir -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name } | Sort-Object)
        Check "art: exactly grid.png and icon.png are on disk - no .html, nothing else" (($files -join ",") -eq "grid.png,icon.png")

        Check "art: a redirect off the allowlist is refused (grid)" ($null -eq $badG.gridUrl)
        Check "art: ... the off-list redirect target was never fetched" (@($log | Where-Object { $_ -eq "GET localhost /img/good.png" }).Count -eq 0)
        Check "art: a 30 MB declared body is refused (hero)" ($null -eq $badG.heroUrl)
        Check "art: a 27 MB streamed body is refused (logo)" ($null -eq $badG.logoUrl)
        Check "art: a URL carrying credentials is refused (icon) - only the good game's grid was fetched" `
            ($null -eq $badG.iconUrl -and @($log | Where-Object { $_ -eq "GET 127.0.0.1 /img/good.png" }).Count -eq 1)
        $badDir = Join-Path $artRoot ([guid]$badG.id).ToString("N")
        Check "art: nothing at all was written for the hostile game" (-not (Test-Path $badDir) -or @(Get-ChildItem $badDir -File).Count -eq 0)

        $served = Http GET ($good.gridUrl -replace '\?.*$', '')
        Check "art: served with nosniff and a sandboxing CSP" `
            ($served.Status -eq 200 -and ("" + $served.Headers["X-Content-Type-Options"]) -eq "nosniff" -and ("" + $served.Headers["Content-Security-Policy"]) -match "sandbox")
        Stop-Phase

        # ------------------------------------------------------------------------------------
        # Not security checks, but they need the stub above and the same hostile-URL rules apply to
        # everything the picker downloads: a key added after games exist, the cover/icon picker,
        # the opaque-icon preference, and the right-sized thumbnails (the anti-aliasing fix).
        Write-Host ""; Write-Host "-- art: a key added later, the picker, an opaque icon, thumbnails"
        Add-Type -AssemblyName System.Drawing
        function ImageSize([byte[]]$bytes) {
            $ms = New-Object System.IO.MemoryStream(, $bytes); $img = [System.Drawing.Image]::FromStream($ms)
            $s = "$($img.Width)x$($img.Height)"; $img.Dispose(); return $s
        }
        function GetArt($path) {
            $r = Invoke-WebRequest "$url$path" -UseBasicParsing -TimeoutSec 30
            return [pscustomobject]@{ Bytes = $r.RawContentStream.ToArray(); Type = ("" + $r.Headers["Content-Type"]); Status = [int]$r.StatusCode }
        }
        function StubCount($line) { @(Get-Content $stubLog | Where-Object { $_ -eq $line }).Count }
        function GameNamed($name) { (Http GET "/api/overview").Json | ForEach-Object { $_.game } | Where-Object { $_.name -eq $name } }

        $artRoot2 = Join-Path $scratch "art2"
        # NO key at start: the first games are created with nothing to fetch art with.
        Start-Phase "art2" @{
            Art__ApiBaseUrl = "http://127.0.0.1:$StubPort/api/v2/"; Art__AllowedImageHosts = "127.0.0.1"
            Art__AllowInsecureImageUrls = "true"; Storage__ArtRoot = $artRoot2
        }
        Http POST "/api/games" @{ name = "SEC Backfill Good" } | Out-Null
        Http POST "/api/games" @{ name = "SEC Keep Good" } | Out-Null
        $none = GameNamed "SEC Backfill Good"; $keep0 = GameNamed "SEC Keep Good"
        Check "backfill: with no key a new game has no art" ($null -eq $none.gridUrl -and $null -eq $none.iconUrl)

        # A cover chosen by hand BEFORE the key exists (choosing needs no key - only listing does).
        $pick0 = Http PUT "/api/games/$($keep0.id)/art/grid" @{ url = "http://127.0.0.1:$StubPort/img/good.png?n=99" }
        $keepGrid = "" + $pick0.Json.gridUrl
        Check "backfill: setup - a hand-picked cover is stored" ($pick0.Status -eq 200 -and $keepGrid -like "/art/*/grid.png*")

        $saved = Http POST "/api/settings/steamgriddb-key" @{ apiKey = "test-key" }
        Check "backfill: saving a key answers at once, and says art is being fetched in the background" `
            ($saved.Status -eq 200 -and $saved.Json.gamesQueued -eq 2 -and $saved.Json.message -match "background")

        $filled = $null
        foreach ($i in 1..60) { Start-Sleep -Milliseconds 500; $filled = GameNamed "SEC Backfill Good"; if ($filled.gridUrl -and $filled.iconUrl) { break } }
        Check "backfill: the game with no art gets a cover and an icon without anyone asking" `
            ($filled.gridUrl -like "/art/*/grid.png*" -and $filled.iconUrl -like "/art/*/icon.png*")
        $keep1 = GameNamed "SEC Keep Good"
        Check "backfill: a game that lacked only its icon gets the icon ..." ($keep1.iconUrl -like "/art/*/icon.png*")
        Check "backfill: ... and its hand-picked cover is left exactly as it was (not replaced by the default)" ($keep1.gridUrl -eq $keepGrid)
        $again = Http POST "/api/settings/steamgriddb-key" @{ apiKey = "test-key" }
        Check "backfill: saving a key when nothing is missing queues nothing" `
            ($again.Status -eq 200 -and $again.Json.gamesQueued -eq 0 -and $again.Json.message -notmatch "background")

        # ---- the picker: five per page, over an API that pages seven-then-five
        Http POST "/api/games" @{ name = "SEC Paged" } | Out-Null
        $paged = GameNamed "SEC Paged"
        $pgid = ([guid]$paged.id).ToString("N")
        $before = StubCount "GET 127.0.0.1 /api/v2/grids/game/3"
        $p0 = Http GET "/api/games/$($paged.id)/art/options?kind=grid&page=0"
        $p1 = Http GET "/api/games/$($paged.id)/art/options?kind=grid&page=1"
        $p2 = Http GET "/api/games/$($paged.id)/art/options?kind=grid&page=2"
        $p3 = Http GET "/api/games/$($paged.id)/art/options?kind=grid&page=3"
        function OptionNumbers($r) { (@($r.Json.options | ForEach-Object { [int](($_.url -split 'n=')[1]) })) -join "," }
        Check "picker: page 0 is five options, and there is more" ((OptionNumbers $p0) -eq "1,2,3,4,5" -and $p0.Json.hasMore -eq $true)
        Check "picker: page 1 straddles two API pages and is the NEXT five" ((OptionNumbers $p1) -eq "6,7,8,9,10" -and $p1.Json.hasMore -eq $true)
        Check "picker: the last page is short and says there is no more" ((OptionNumbers $p2) -eq "11,12" -and $p2.Json.hasMore -eq $false)
        Check "picker: past the end is empty, not an error" ($p3.Status -eq 200 -and @($p3.Json.options).Count -eq 0 -and $p3.Json.hasMore -eq $false)
        # The ICON listing for this game has not been fetched yet (the cover one has), so a request would show.
        $beforeFar = StubCount "GET 127.0.0.1 /api/v2/icons/game/3"
        $far = Http GET "/api/games/$($paged.id)/art/options?kind=icon&page=2000000000"
        Check "picker: an absurd page number is an empty page - no arithmetic overflow, and no request to SteamGridDB" `
            ($far.Status -eq 200 -and @($far.Json.options).Count -eq 0 -and $far.Json.hasMore -eq $false -and (StubCount "GET 127.0.0.1 /api/v2/icons/game/3") -eq $beforeFar)
        Check "picker: width, height and author are passed through" `
            ($p0.Json.options[0].width -eq 600 -and $p0.Json.options[0].height -eq 900 -and $p0.Json.options[0].author -eq "artist1")
        Check "picker: four pages cost two SteamGridDB requests (the listing is kept, not re-fetched per page)" `
            ((StubCount "GET 127.0.0.1 /api/v2/grids/game/3") - $before -eq 2)

        $prev = "" + $p0.Json.options[0].preview
        Check "picker: each option carries an inline data: preview (the console's CSP allows nothing else)" ($prev -match "^data:image/(png|jpeg);base64,")
        $prevBytes = [Convert]::FromBase64String(($prev -replace '^data:[^,]+,', ''))
        Check "picker: ... shrunk to its tile (a 400x600 thumb becomes 200x300), not sent whole" ((ImageSize $prevBytes) -eq "200x300")
        Check "picker: ... and an opaque cover preview is a JPEG, not a fat PNG" ($prev -match "^data:image/jpeg")
        Check "picker: an unknown art kind is refused" ((Http GET "/api/games/$($paged.id)/art/options?kind=hero").Status -eq 400)
        Check "picker: an unknown game is refused" ((Http GET "/api/games/$([guid]::NewGuid())/art/options?kind=grid").Status -eq 400)

        $applied = Http PUT "/api/games/$($paged.id)/art/grid" @{ url = $p1.Json.options[2].url }
        Check "picker: choosing an option stores it as the cover" `
            ($applied.Status -eq 200 -and $applied.Json.gridUrl -like "/art/*/grid.png*" -and $applied.Json.gridUrl -ne $paged.gridUrl)
        $heroBefore = StubCount "GET localhost /img/hero.jpg"
        $evil = Http PUT "/api/games/$($paged.id)/art/grid" @{ url = "http://localhost:$StubPort/img/hero.jpg" }
        Check "picker: a URL on a host that is not allowlisted is refused, and never contacted" `
            ($evil.Status -eq 400 -and (StubCount "GET localhost /img/hero.jpg") -eq $heroBefore)
        Check "picker: a URL carrying credentials is refused" `
            ((Http PUT "/api/games/$($paged.id)/art/grid" @{ url = "http://user:pw@127.0.0.1:$StubPort/img/good.png" }).Status -eq 400)
        Check "picker: only 'grid' and 'icon' can be chosen - not hero or logo" `
            ((Http PUT "/api/games/$($paged.id)/art/hero" @{ url = "http://127.0.0.1:$StubPort/img/good.png" }).Status -eq 400)
        Check "picker: the refused attempts changed nothing" ((GameNamed "SEC Paged").gridUrl -eq $applied.Json.gridUrl)

        # ---- an image that declares far more pixels than it has bytes (144 MP in a few KB) is refused everywhere
        Http POST "/api/games" @{ name = "SEC Bomb" } | Out-Null
        $bomb = GameNamed "SEC Bomb"
        $bo = Http GET "/api/games/$($bomb.id)/art/options?kind=grid&page=0"
        Check "oversized: the listing still answers, and offers the option ..." ($bo.Status -eq 200 -and @($bo.Json.options).Count -eq 1)
        Check "oversized: ... with NO inline preview (a tiny file of a huge image must not be handed to the browser)" ($null -eq $bo.Json.options[0].preview)
        $bp = Http PUT "/api/games/$($bomb.id)/art/grid" @{ url = "http://127.0.0.1:$StubPort/img/bomb.png" }
        Check "oversized: choosing it is refused (400) and nothing is stored" ($bp.Status -eq 400 -and $null -eq (GameNamed "SEC Bomb").gridUrl)

        # ---- bytes with the right magic number but nothing behind it, and a 200 with no body: refused quietly, never a 500
        Http POST "/api/games" @{ name = "SEC Corrupt" } | Out-Null
        $cor = GameNamed "SEC Corrupt"
        $co = Http GET "/api/games/$($cor.id)/art/options?kind=grid&page=0"
        Check "corrupt: a listing holding an undecodable image and an empty body still answers (200), not a 500" `
            ($co.Status -eq 200 -and @($co.Json.options).Count -eq 2)
        Check "corrupt: ... and neither is inlined as a preview" ($null -eq $co.Json.options[0].preview -and $null -eq $co.Json.options[1].preview)
        Check "corrupt: choosing the undecodable image is refused (400)" `
            ((Http PUT "/api/games/$($cor.id)/art/grid" @{ url = "http://127.0.0.1:$StubPort/img/corrupt.png" }).Status -eq 400)
        Check "corrupt: choosing the empty body is refused (400), not a 500" `
            ((Http PUT "/api/games/$($cor.id)/art/grid" @{ url = "http://127.0.0.1:$StubPort/img/empty.png" }).Status -eq 400)
        $cr = Http POST "/api/games/$($cor.id)/art/refresh"
        Check "corrupt: refreshing a game whose cover and icon are undecodable answers 400 (nothing to store), not a 500, and stores nothing" `
            ($cr.Status -eq 400 -and $null -eq (GameNamed "SEC Corrupt").gridUrl -and $null -eq (GameNamed "SEC Corrupt").iconUrl)

        # ---- a transparent icon and an opaque one: the opaque one wins, though it is second
        Http POST "/api/games" @{ name = "SEC Opaque" } | Out-Null
        $op = GameNamed "SEC Opaque"
        $gid = ([guid]$op.id).ToString("N")
        $bmp = New-Object System.Drawing.Bitmap((Join-Path $artRoot2 "$gid\icon.png")); $corner = $bmp.GetPixel(0, 0).A; $bmp.Dispose()
        Check "icon: of two candidates the first has a transparent pixel and the second none - the opaque one is kept" ($corner -eq 255)

        # ---- thumbnails: the anti-aliasing fix
        $orig = GetArt "/art/$gid/grid.png"
        Check "thumb: the plain URL still serves the untouched original (400x600 PNG)" ((ImageSize $orig.Bytes) -eq "400x600" -and $orig.Type -eq "image/png")
        $t96 = GetArt "/art/$gid/grid.png?w=96"
        Check "thumb: ?w=96 is a 96x144 copy" ((ImageSize $t96.Bytes) -eq "96x144")
        Check "thumb: ... an opaque cover goes out as JPEG, and a fraction of the original's size" `
            ($t96.Type -eq "image/jpeg" -and $t96.Bytes.Length -lt ($orig.Bytes.Length / 4))
        Check "thumb: ... and is cached on disk under thumbs/" (Test-Path (Join-Path $artRoot2 "$gid\thumbs\grid-96.jpg"))
        Check "thumb: a width not on the allowlist is ignored - the original comes back and nothing is written" `
            ((ImageSize (GetArt "/art/$gid/grid.png?w=97").Bytes) -eq "400x600" -and -not (Test-Path (Join-Path $artRoot2 "$gid\thumbs\grid-97.jpg")))
        Check "thumb: it never upscales - a 1x1 source asked for at 96 is returned as it is" ((ImageSize (GetArt "/art/$pgid/grid.png?w=96").Bytes) -eq "1x1")
        $h = Http GET "/art/$gid/grid.png?w=64"
        Check "thumb: served with nosniff and the sandboxing CSP, like every other /art file" `
            ($h.Status -eq 200 -and ("" + $h.Headers["X-Content-Type-Options"]) -eq "nosniff" -and ("" + $h.Headers["Content-Security-Policy"]) -match "sandbox")
        $rep = Http PUT "/api/games/$($op.id)/art/grid" @{ url = "http://127.0.0.1:$StubPort/img/good.png?n=1" }
        Check "thumb: once the cover is replaced, the old thumbnail is not served (freshness follows the source)" `
            ($rep.Status -eq 200 -and (ImageSize (GetArt "/art/$gid/grid.png?w=96").Bytes) -eq "1x1")
        Stop-Phase

        # ---- a key that arrives by CONFIGURATION (never saved in the dashboard) still backfills, at startup
        Write-Host ""; Write-Host "-- art: startup backfill (a key from configuration, games that predate it)"
        $artEnv3 = @{
            Art__ApiBaseUrl = "http://127.0.0.1:$StubPort/api/v2/"; Art__AllowedImageHosts = "127.0.0.1"
            Art__AllowInsecureImageUrls = "true"; Storage__ArtRoot = (Join-Path $scratch "art3")
        }
        Start-Phase "art3" $artEnv3          # no key: the games are created with nothing to fetch art with
        Http POST "/api/games" @{ name = "SEC Boot Good" } | Out-Null
        Http POST "/api/games" @{ name = "SEC Boot Keep Good" } | Out-Null
        $bootKeep0 = GameNamed "SEC Boot Keep Good"
        $bootGrid = "" + (Http PUT "/api/games/$($bootKeep0.id)/art/grid" @{ url = "http://127.0.0.1:$StubPort/img/good.png?n=77" }).Json.gridUrl
        Stop-Phase
        $bootEnv = $artEnv3.Clone()
        $bootEnv.SteamGridDb__ApiKey = "test-key"; $bootEnv.Art__BackfillOnStartup = "true"; $bootEnv.Art__BackfillStartupDelaySeconds = "0"
        Start-Phase "art3" $bootEnv          # the same database, restarted with a key and the startup pass on
        $bootFilled = $null
        foreach ($i in 1..60) { Start-Sleep -Milliseconds 500; $bootFilled = GameNamed "SEC Boot Good"; if ($bootFilled.gridUrl -and $bootFilled.iconUrl) { break } }
        Check "startup: a key from configuration fills in the games that predate it, with nobody saving a key" `
            ($bootFilled.gridUrl -like "/art/*/grid.png*" -and $bootFilled.iconUrl -like "/art/*/icon.png*")
        $bootKept = GameNamed "SEC Boot Keep Good"
        Check "startup: ... a game that lacked only its icon gets the icon ..." ($bootKept.iconUrl -like "/art/*/icon.png*")
        Check "startup: ... and its hand-picked cover is left exactly as it was" ($bootKept.gridUrl -eq $bootGrid)
        Stop-Phase
    }
}
}
catch {
    Write-Host "FAIL: the suite aborted with an unexpected error: $($_.Exception.Message)"
    Write-Host $_.ScriptStackTrace
    $script:fail++
}
finally { Cleanup }

Write-Host ""
Write-Host "==== $pass passed, $fail failed ===="
if ($fail -gt 0) { exit 1 } else { exit 0 }
