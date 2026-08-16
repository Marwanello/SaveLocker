# Agent-update auto-fetch scheduling (tasks/UpdateScheduling.md).
#
# Exercises AutoFetchScheduler.ComputeNextRun through the real HTTP contract — POST a schedule,
# GET it back, and check the server's own next-run answer against an independently computed
# expectation. Isolated-state harness: owns its port, its SQLite DB, starts and stops its own server.
#
# Usage: .\tests\run-schedule-tests.ps1
param([int]$Port = 5184)

$ErrorActionPreference = "Continue"

$root    = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $root ".verify-schedule"
$url     = "http://localhost:$Port"

$inProgramFiles = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
$dotnet    = if (Test-Path $inProgramFiles) { $inProgramFiles } else { "dotnet" }
$serverDll = Join-Path $root "src/Server/bin/Debug/net10.0/SaveLocker.Server.dll"
if (-not (Test-Path $serverDll)) { Write-Host "Server not built: $serverDll"; exit 2 }

$pass = 0; $fail = 0
function Check($name, $cond) {
    if ($cond) { Write-Host "PASS: $name"; $script:pass++ }
    else        { Write-Host "FAIL: $name"; $script:fail++ }
}

Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $scratch "archives") | Out-Null

$env:ASPNETCORE_URLS      = $url
$env:Storage__DbPath      = Join-Path $scratch "savelocker.db"
$env:Storage__ArchiveRoot = Join-Path $scratch "archives"
$env:Backup__Enabled      = "false"
$env:Logging__EventLog__LogLevel__Default = "None"

$proc = Start-Process -FilePath $dotnet -ArgumentList @($serverDll) -PassThru -NoNewWindow `
    -RedirectStandardOutput "$scratch\srv.out.log" -RedirectStandardError "$scratch\srv.err.log"
$up = $false
foreach ($i in 1..60) {
    Start-Sleep -Milliseconds 700
    try { Invoke-RestMethod "$url/api/admin/status" -TimeoutSec 3 | Out-Null; $up = $true; break } catch { }
}
if (-not $up) {
    Get-Content "$scratch\srv.err.log" -ErrorAction SilentlyContinue | Select-Object -Last 20
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Write-Host "FAIL: server did not start on $url"
    exit 1
}

try {
    function Set-Schedule($body) {
        return Invoke-RestMethod "$url/api/settings/agent-update-auto-fetch" -Method Post `
            -ContentType "application/json" -Body ($body | ConvertTo-Json)
    }
    function Get-Settings { return Invoke-RestMethod "$url/api/settings" }

    # PowerShell's [DateTime] cast of an ISO "...Z" string silently reinterprets it as local-kind
    # WITHOUT correctly adjusting the clock value on Windows PowerShell 5.1 in some locales, which
    # made a real UTC instant compare as wildly wrong against a UTC expectation. RoundtripKind is
    # the one parse mode that preserves what the server actually said.
    function ConvertFrom-Utc($iso) {
        return [DateTime]::Parse($iso, [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind)
    }

    # ---- Back-compat: no schedule ever set reads as disabled hours mode, exactly like before ----
    $s = Get-Settings
    Check "fresh server: schedule mode defaults to hours" ($s.schedule.mode -eq "hours")
    Check "fresh server: hours default to 0 (disabled)"   ($s.autoFetchHours -eq 0)
    Check "fresh server: disabled hours has no next run"  ($null -eq $s.nextAutoFetchRunAt)

    # ---- Hours mode: unchanged behaviour, now going through the schedule endpoint ----
    $r = Set-Schedule @{ mode = "hours"; hours = 2; dayOfWeek = 0; dayOfMonth = 1; timeOfDay = "03:00" }
    $expected = (Get-Date).ToUniversalTime().AddHours(2)
    $actual = ConvertFrom-Utc $r.nextRunAt
    Check "hours mode: next run is ~2h from now" ([Math]::Abs(($actual - $expected).TotalMinutes) -lt 2)

    # ---- Weekly mode: next occurrence of a weekday+time, server-local ----
    $nowLocal = Get-Date
    $targetDow = [int][DayOfWeek]::Wednesday
    $r = Set-Schedule @{ mode = "weekly"; hours = 0; dayOfWeek = $targetDow; dayOfMonth = 1; timeOfDay = "14:30" }
    $nextRunLocal = (ConvertFrom-Utc $r.nextRunAt).ToLocalTime()
    Check "weekly mode: next run lands on the target weekday" ($nextRunLocal.DayOfWeek -eq [DayOfWeek]::Wednesday)
    Check "weekly mode: next run is at the configured time"   ($nextRunLocal.Hour -eq 14 -and $nextRunLocal.Minute -eq 30)
    Check "weekly mode: next run is strictly in the future"   ($nextRunLocal -gt $nowLocal)
    Check "weekly mode: next run is within 7 days"            (($nextRunLocal - $nowLocal).TotalDays -le 7.01)

    # ---- Weekly mode, "today already passed": target today's weekday at a time before now ----
    $todayDow = [int]$nowLocal.DayOfWeek
    $pastTime = $nowLocal.AddMinutes(-5)
    $r = Set-Schedule @{ mode = "weekly"; hours = 0; dayOfWeek = $todayDow; dayOfMonth = 1;
                          timeOfDay = $pastTime.ToString("HH:mm") }
    $nextRunLocal = (ConvertFrom-Utc $r.nextRunAt).ToLocalTime()
    Check "weekly mode, time already passed today: rolls to next week, not today" `
        ($nextRunLocal.Date -gt $nowLocal.Date)
    Check "weekly mode, time already passed today: still lands on the target weekday" `
        ($nextRunLocal.DayOfWeek -eq $nowLocal.DayOfWeek)

    # ---- Monthly mode: day 31 clamps to the last real day of whichever month it lands in ----
    $r = Set-Schedule @{ mode = "monthly"; hours = 0; dayOfWeek = 0; dayOfMonth = 31; timeOfDay = "09:00" }
    $nextRunLocal = (ConvertFrom-Utc $r.nextRunAt).ToLocalTime()
    $daysInThatMonth = [DateTime]::DaysInMonth($nextRunLocal.Year, $nextRunLocal.Month)
    Check "monthly mode, day 31: clamps to the target month's actual last day" `
        ($nextRunLocal.Day -eq $daysInThatMonth)
    Check "monthly mode: next run is strictly in the future" ($nextRunLocal -gt $nowLocal)

    # ---- Disabled mode: explicitly no next run ----
    $r = Set-Schedule @{ mode = "disabled"; hours = 0; dayOfWeek = 0; dayOfMonth = 1; timeOfDay = "03:00" }
    Check "disabled mode: no next run" ($null -eq $r.nextRunAt)

    # ---- Rejects bad input ----
    try {
        Invoke-RestMethod "$url/api/settings/agent-update-auto-fetch" -Method Post -ContentType "application/json" `
            -Body (@{ mode = "yearly"; hours = 0; dayOfWeek = 0; dayOfMonth = 1; timeOfDay = "03:00" } | ConvertTo-Json) | Out-Null
        Check "unknown mode is refused" $false
    } catch { Check "unknown mode is refused" ($_.Exception.Response.StatusCode.value__ -eq 400) }

    try {
        Invoke-RestMethod "$url/api/settings/agent-update-auto-fetch" -Method Post -ContentType "application/json" `
            -Body (@{ mode = "weekly"; hours = 0; dayOfWeek = 0; dayOfMonth = 1; timeOfDay = "not-a-time" } | ConvertTo-Json) | Out-Null
        Check "unparseable time-of-day is refused" $false
    } catch { Check "unparseable time-of-day is refused" ($_.Exception.Response.StatusCode.value__ -eq 400) }
}
finally {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host "==== SCHEDULE: $pass passed, $fail failed ===="
if ($fail -gt 0) { exit 1 }
