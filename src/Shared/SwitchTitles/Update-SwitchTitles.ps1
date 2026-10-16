<#
.SYNOPSIS
  Rebuilds switch-titles.tsv.gz — Switch title ID -> game name and eShop icon, from blawar/titledb (MIT).

.DESCRIPTION
  One line per base game: "<title ID>`t<name>`t<icon>", the name exactly as the eShop lists it (SaveLocker cleans
  the trademark signs when it reads it) and the icon as the hash in its img-eshop.cdn.nintendo.net/i/<hash>.jpg URL,
  empty when the eShop has none (the agent names games from it, the server fetches the icon as the game's). Regions are read in order and the first name a title ID gets is kept: the US
  eShop, then the UK one, then Japan's (in Japanese) for a game sold only there. Demos are left out, and so are
  updates and DLC (a title ID not ending in 000). Run it from anywhere; it writes next to itself and updates
  NOTICE.md's source commit.

  titledb's files are 80+ MB of pretty-printed JSON, more than Windows PowerShell's ConvertFrom-Json takes, so each
  entry is read by its layout: four spaces before an entry, eight before its fields.

.PARAMETER Commit
  The titledb commit to read (default: master, resolved to its SHA so NOTICE.md pins it).
#>
param([string]$Commit = 'master')

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$regions = 'US.en', 'GB.en', 'JP.ja'
$sha = (Invoke-RestMethod "https://api.github.com/repos/blawar/titledb/commits/$Commit" -Headers @{ 'User-Agent' = 'SaveLocker' }).sha

$entry = [regex]'(?ms)^    "[^"\r\n]+": \{(.*?)^    \}'
$idField = [regex]'(?m)^        "id": "([0-9A-Fa-f]{16})"'
$nameField = [regex]'(?m)^        "name": "((?:[^"\\]|\\.)*)"'
$demo = [regex]'(?m)^        "isDemo": true'
$iconField = [regex]'(?m)^        "iconUrl": "https://img-eshop\.cdn\.nintendo\.net/i/([0-9a-f]{64})\.jpg"'

$names = @{}
$tmp = Join-Path ([IO.Path]::GetTempPath()) "titledb-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $tmp | Out-Null
try {
    foreach ($region in $regions) {
        $file = Join-Path $tmp "$region.json"
        Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/blawar/titledb/$sha/$region.json" -OutFile $file
        $text = [IO.File]::ReadAllText($file, [Text.Encoding]::UTF8)
        $added = 0
        foreach ($m in $entry.Matches($text)) {
            $body = $m.Groups[1].Value
            if ($demo.IsMatch($body)) { continue }
            $id = $idField.Match($body); $name = $nameField.Match($body)
            if (-not $id.Success -or -not $name.Success) { continue }
            $tid = $id.Groups[1].Value.ToUpperInvariant()
            if (-not $tid.EndsWith('000') -or $names.ContainsKey($tid)) { continue }
            $title = ([regex]::Unescape($name.Groups[1].Value) -replace '\s+', ' ').Trim()
            $icon = $iconField.Match($body)
            if ($title) { $names[$tid] = "$title`t$(if ($icon.Success) { $icon.Groups[1].Value })"; $added++ }
        }
        Write-Host ("{0,-6} {1,6} new titles" -f $region, $added)
        Remove-Item $file
    }
} finally { Remove-Item -Recurse -Force $tmp }

$lines = foreach ($tid in ($names.Keys | Sort-Object -CaseSensitive)) { "$tid`t$($names[$tid])" }
$out = Join-Path $PSScriptRoot 'switch-titles.tsv.gz'
$bytes = (New-Object System.Text.UTF8Encoding $false).GetBytes(($lines -join "`n") + "`n")
$stream = [IO.File]::Create($out)
try {
    $gzip = New-Object IO.Compression.GZipStream($stream, [IO.Compression.CompressionLevel]::Optimal)
    $gzip.Write($bytes, 0, $bytes.Length)
    $gzip.Dispose()
} finally { $stream.Dispose() }

$notice = Join-Path $PSScriptRoot 'NOTICE.md'
$utf8 = New-Object System.Text.UTF8Encoding $false
[IO.File]::WriteAllText($notice,
    ([IO.File]::ReadAllText($notice, $utf8) -replace '(?<=blawar/titledb/tree/)[0-9a-f]{40}', $sha), $utf8)
Write-Host "$($names.Count) titles from titledb $sha -> $out"
