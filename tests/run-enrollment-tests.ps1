# Enrollment integration tests (join links) - runs on BOTH Windows and Linux.
#
#   Windows: Windows PowerShell 5.1 or pwsh, drives src/Agent (net10.0-windows).
#   Linux:   pwsh (PowerShell Core), drives src/Agent.Linux (net10.0).
#
# Enrollment is now a join link (tasks/enrollment-options, option B): the console mints an
# 8-character code, GET /join/{code} is the pairing page, GET /join/{code}/policy is what the
# agent's `enroll --url` fetches, and the code is spent once against POST /api/enroll. `enroll`
# lives in Agent.Core, so it is the SAME code on both hosts - which is the point.
#
# What this does NOT cover: TLS pinning (the harness server is plain http, which has no identity
# to pin - the agent must record NO pin, which is asserted below). See
# tests/run-enrollment-tls-tests.ps1 for the pin-and-warn path. Link expiry is not waited out
# here either (a 1-minute minimum TTL would make the suite take minutes); that predicate is the
# same one the used/revoked paths below exercise.
#
# Prerequisites: server running on http://localhost:5179 (no admin password), agent built in Debug.
# Usage:  .\tests\run-enrollment-tests.ps1   /   pwsh tests/run-enrollment-tests.ps1

$ErrorActionPreference = "Continue"

# $IsWindows is auto-defined by PowerShell Core but NOT by Windows PowerShell 5.1,
# where its absence is itself the signal that we are on Windows.
$onWindows = if ($null -eq $IsWindows) { $true } else { $IsWindows }

$root    = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root ".verify"
$server  = "http://localhost:5179"

if ($onWindows) {
    $inProgramFiles = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
    $dotnet = if (Test-Path $inProgramFiles) { $inProgramFiles } else { "dotnet" }
    $dll    = Join-Path $root "src/Agent/bin/Debug/net10.0-windows/SaveLocker.Agent.dll"
} else {
    $dotnet = "dotnet"
    $dll    = Join-Path $root "src/Agent.Linux/bin/Debug/net10.0/savelocker.dll"
}

if (-not (Test-Path $dll)) {
    Write-Host "Agent not built: $dll"
    exit 2
}

New-Item -ItemType Directory -Force $scratch | Out-Null

$pass = 0; $fail = 0
function Check($name, $cond) {
    if ($cond) { Write-Host "PASS: $name"; $script:pass++ }
    else        { Write-Host "FAIL: $name"; $script:fail++ }
}
function Agent { & $dotnet $dll @args 2>&1 }

# Every mint states its server URL. This suite runs the agent on the same box as the server, so the
# link really does need to say localhost — and the server refuses to INFER a loopback address,
# because a link minted from a console opened at localhost cannot work on the machine it is for
# (Console-BugBounty CS-06). Saying it explicitly is how a same-box setup declares itself.
function Mint($body) {
    if (-not $body.ContainsKey("serverUrl")) { $body["serverUrl"] = $server }
    Invoke-RestMethod -Uri "$server/api/admin/enrollments" -Method Post `
        -ContentType "application/json" -Body ($body | ConvertTo-Json)
}

$stamp = Get-Date -Format "HHmmss"

# ---- A game must exist on the server for the policy to carry one ----
$gameName = "EnrollGame-$stamp"
$game = Invoke-RestMethod -Uri "$server/api/games" -Method Post -ContentType "application/json" `
    -Body (@{ name = $gameName; manifestKey = $null; customPathsJson = $null } | ConvertTo-Json)

# A second game whose save location is stored as a TEMPLATE rather than a literal path. A literal
# cannot mean the same folder on two machines — different user, different drive, or a Proton prefix —
# and two agents disagreeing about a game's save root by one segment is what makes a restore nest a
# folder under itself and delete the correctly-placed copy. Enroll is the Deck's whole setup, so it
# is the first thing that has to expand one.
$tmplName = "EnrollTemplate-$stamp"
$tmplGame = Invoke-RestMethod -Uri "$server/api/games" -Method Post -ContentType "application/json" `
    -Body (@{ name = $tmplName; manifestKey = $null; customPathsJson = $null } | ConvertTo-Json)

if ($onWindows) {
    $tmplExpected = Join-Path $env:APPDATA "$tmplName\Saves"
    New-Item -ItemType Directory -Force $tmplExpected | Out-Null
    Invoke-RestMethod ("$server/api/games/" + $tmplGame.id + "/save-dir?value=" +
        [uri]::EscapeDataString("<winAppData>/$tmplName/Saves")) -Method Post | Out-Null
}

# ---- 1. Mint: a short code, a link, and a pairing page that renders ----
$deckName = "phase4-deck-$stamp"
$mint = Mint @{ machineName = $deckName; ttlMinutes = 15 }

Check "mint returns an 8-character join code"  ($mint.code -and $mint.code.Length -eq 8)
Check "the code avoids the confusable letters" ($mint.code -notmatch "[ILOU]")
Check "joinUrl points at /join/<code>"         ($mint.joinUrl -eq "$server/join/$($mint.code)")

$page = Invoke-WebRequest -UseBasicParsing -Uri "$server/join/$($mint.code)"
Check "the pairing page renders"               ($page.StatusCode -eq 200 -and
                                                $page.Content -match "Pair this device" -and
                                                $page.Content -match [regex]::Escape($mint.code))

# ---- 2. The policy behind the link describes the server right now ----
$policy = Invoke-RestMethod -Uri "$server/join/$($mint.code)/policy"
Check "policy carries the code"                ($policy.token -eq $mint.code)
Check "policy carries the server URL"          ($policy.serverUrl -eq $server)
Check "policy binds the machine name"          ($policy.machineName -eq $deckName)
Check "policy carries the server's games"      ($policy.games.gameId -contains $game.id)

# The two child routes the page's buttons use, while the code is valid: the Linux one-liner is
# served with the code in it, and the Windows button lands on the installer (or says none is
# hosted — a fresh server has none, a dev box with one gets the redirect).
$oneLiner = Invoke-WebRequest -UseBasicParsing -Uri "$server/join/$($mint.code)/install.sh"
Check "the one-liner is served for a valid code" ($oneLiner.StatusCode -eq 200 -and
                                                $oneLiner.Content -match "enroll --url" -and
                                                $oneLiner.Content -match [regex]::Escape($mint.code))

Add-Type -AssemblyName System.Net.Http -ErrorAction SilentlyContinue
$noRedirect = New-Object System.Net.Http.HttpClientHandler
$noRedirect.AllowAutoRedirect = $false
$http = New-Object System.Net.Http.HttpClient($noRedirect)
try {
    $setup = $http.GetAsync("$server/join/$($mint.code)/setup.exe").GetAwaiter().GetResult()
    $redirects = ($setup.StatusCode -eq [System.Net.HttpStatusCode]::Found) -and
                 ($setup.Headers.Location.OriginalString -eq "/api/agent/installer/download?platform=win-x64")
    $noInstaller = $setup.StatusCode -eq [System.Net.HttpStatusCode]::NotFound
    Check "setup.exe points at the installer, or names its absence" ($redirects -or $noInstaller)
} finally {
    $http.Dispose(); $noRedirect.Dispose()
}

# ---- 3. Enroll by link: the agent trades the code for a real machine key ----
# --name is deliberately WRONG here: the link was minted for $deckName, and a bound code must win.
# Otherwise a leaked link could be spent to claim any machine's identity.
$cfg = Join-Path $scratch "enroll-cfg-$stamp.json"
$out = Agent enroll --url $mint.joinUrl --name "an-imposter-name" --config $cfg
$enrollExit = $LASTEXITCODE
$saved = Get-Content $cfg -Raw | ConvertFrom-Json

Check "enroll --url succeeds"                  ($enrollExit -eq 0)
Check "enroll stored a machine API key"        ($saved.ApiKey -and $saved.ApiKey.Length -gt 20)
Check "enroll stored the machine id"           ($null -ne $saved.MachineId)
Check "the bound code wins over --name"        ($saved.MachineName -eq $deckName)
Check "enroll pre-seeded the server's game"    ($saved.Games.GameId -contains $game.id)

if ($onWindows) {
    $tmplTracked = $saved.Games | Where-Object { $_.GameId -eq $tmplGame.id }
    Check "enroll expanded a templated save path" ($tmplTracked.SaveDirectory -eq $tmplExpected)
    Check "the raw template was not stored"       ($tmplTracked.SaveDirectory -notlike "*<winAppData>*")
    Remove-Item $tmplExpected -Recurse -Force -ErrorAction SilentlyContinue
}
Check "plain http records no TLS pin"          ($null -eq $saved.ServerPin)

# ---- 3b. The typable form: a bare code plus the server address ----
# This is what a person does when they read the code off the console and type it on the Deck; the
# CLI assembles the same link out of it (tasks/enrollment-options, option A's motion).
$bare = Mint @{ ttlMinutes = 15 }
$cfgBare = Join-Path $scratch "enroll-cfg-bare-$stamp.json"
$outBare = Agent enroll --code $bare.code --server $server --name "bare-code-$stamp" --config $cfgBare
$savedBare = Get-Content $cfgBare -Raw | ConvertFrom-Json
Check "enroll --code + --server succeeds"      ($LASTEXITCODE -eq 0 -and $savedBare.MachineName -eq "bare-code-$stamp")

# ---- 4. Single-use: the same link cannot be spent twice ----
$cfg2 = Join-Path $scratch "enroll-cfg2-$stamp.json"
$replay = Agent enroll --url $mint.joinUrl --config $cfg2
Check "replaying the link is refused"          ($LASTEXITCODE -ne 0 -and "$replay" -match "already been used")

# The policy is the other route a spent code reaches: it must say 404, not serve a de-facto
# credential to a replay.
try {
    Invoke-RestMethod -Uri "$server/join/$($mint.code)/policy" | Out-Null
    Check "the policy for a used code is refused (404)" $false
} catch {
    Check "the policy for a used code is refused (404)" ([int]$_.Exception.Response.StatusCode -eq 404)
}

$pageUsed = Invoke-WebRequest -UseBasicParsing -Uri "$server/join/$($mint.code)"
Check "the pairing page says it was used"      ($pageUsed.StatusCode -eq 200 -and
                                                $pageUsed.Content -match "already been used")

# ---- 5. Revoked link is refused ----
$revokable = Mint @{ machineName = "phase4-revoked-$stamp"; ttlMinutes = 15 }
Invoke-RestMethod -Uri "$server/api/admin/enrollments/$($revokable.id)" -Method Delete | Out-Null

$revOut = Agent enroll --url $revokable.joinUrl --config (Join-Path $scratch "enroll-cfg3-$stamp.json")
Check "revoked link is refused"                ($LASTEXITCODE -ne 0 -and "$revOut" -match "not valid")

# ---- 6. A forged code is refused ----
# Correct shape, never minted. Z is in the Crockford alphabet, so this passes local normalization
# and must be refused by the SERVER - proving the check is not just the local clock or format.
$forgedCode = "ZZZZ1ZZZ"
$forgedOut = Agent enroll --url "$server/join/$forgedCode" --config (Join-Path $scratch "enroll-cfg4-$stamp.json")
Check "forged code is refused by the server"   ($LASTEXITCODE -ne 0 -and "$forgedOut" -match "not valid")

# ---- 7. The console can see the spent code, and who spent it ----
$list = Invoke-RestMethod -Uri "$server/api/admin/enrollments"
$mine = $list | Where-Object { $_.id -eq $mint.id }
Check "console shows the code as redeemed"     ($null -ne $mine.redeemedAt -and $mine.redeemedByMachineName -eq $deckName)

# ---- 8. The short code is only safe because guessing is throttled ----
# Its own server with a deliberately tiny limit, so this never locks the suite's main server (or a
# later suite) out for the real 15-minute window. This is THE security property of an 8-character
# code; without it the whole option would be guessable at wire speed.
$throttleState = Join-Path $scratch "throttle-$stamp"
New-Item -ItemType Directory -Force $throttleState | Out-Null
$throttle = "http://localhost:5184"
$serverDll = Join-Path $root "src/Server/bin/Debug/net10.0/SaveLocker.Server.dll"
if (Test-Path $serverDll) {
    $env:ASPNETCORE_URLS = $throttle
    $env:Storage__DbPath = Join-Path $throttleState "savelocker.db"
    $env:Storage__ArchiveRoot = Join-Path $throttleState "archives"
    $env:Backup__Enabled = "false"
    $env:Security__JoinMaxFailures = "3"
    $env:Security__JoinFailureWindowSeconds = "120"
    $env:Security__JoinLockoutSeconds = "60"

    $proc = if ($onWindows) {
        Start-Process -FilePath $dotnet -ArgumentList $serverDll -PassThru -WindowStyle Hidden
    } else {
        Start-Process -FilePath $dotnet -ArgumentList $serverDll -PassThru
    }
    try {
        $up = $false
        foreach ($i in 1..30) {
            Start-Sleep -Milliseconds 700
            try { Invoke-RestMethod -Uri "$throttle/api/admin/status" -TimeoutSec 3 | Out-Null; $up = $true; break } catch { }
        }
        if ($up) {
            $unknown = Invoke-WebRequest -UseBasicParsing -Uri "$throttle/join/ZZZZ1ZZZ"
            Check "an unknown code renders the invalid page" ($unknown.StatusCode -eq 200 -and
                                                             $unknown.Content -match "not valid")

            $statuses = @()
            $retryAfter = $null
            foreach ($i in 1..8) {
                try {
                    Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$throttle/api/enroll" `
                        -ContentType "application/json" -Body '{"token":"ZZZZ1ZZZ"}' | Out-Null
                    $statuses += 200
                } catch {
                    $statuses += [int]$_.Exception.Response.StatusCode
                    if ($null -eq $retryAfter) {
                        # GetValues works on Windows PowerShell's WebHeaderCollection AND pwsh's
                        # HttpResponseHeaders; the string indexer is $null/throws on pwsh, which
                        # is why this check alone failed on Linux CI. Latch only a real value —
                        # the first exception here is the 401, which has no Retry-After at all.
                        $ra = try { @($_.Exception.Response.Headers.GetValues('Retry-After'))[0] } catch { $null }
                        if ($ra) { $retryAfter = $ra }
                    }
                }
            }
            Check "hammering unknown codes is throttled (429)" ($statuses -contains 429)
            Check "the 429 says how long to wait"              ($null -ne $retryAfter -and $retryAfter -match "^\d+$")
        } else {
            Check "throttle server came up" $false
        }
    } finally {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
        Remove-Item Env:ASPNETCORE_URLS, Env:Storage__DbPath, Env:Storage__ArchiveRoot, `
            Env:Backup__Enabled, Env:Security__JoinMaxFailures, Env:Security__JoinFailureWindowSeconds, `
            Env:Security__JoinLockoutSeconds -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "SKIP: throttle checks - server not built at $serverDll"
}

Write-Host ""
Write-Host "Enrollment: $pass passed, $fail failed."
if ($fail -gt 0) { exit 1 }
