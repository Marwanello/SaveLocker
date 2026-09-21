# Appearance consistency (checkpoint-ui Phase 4 / Group 5). Reads SOURCE only - no server, no agent, no build,
# so it runs in a second and works under both Windows PowerShell 5.1 and pwsh.
#
# The look (theme / accent / mark) is defined in several places that cannot import from one another:
#
#   src/Shared/Appearance.cs            the closed id lists the server validates against
#   src/Agent.Core/AppearancePalette.cs the accent colours the Windows tray icon (and, in Group 6, the Deck) draws
#   web/src/appearance.ts               the console's copy: ids, accent triples, mark geometry
#   agent-ui/src/appearance.ts          the agent UI's copy of the same
#   web/src/index.css                   the palette tokens (source of truth)
#   agent-ui/src/tokens.css             the hand-kept copy of those tokens
#
# A drifted copy is silent: an accent that is one shade off on the tray, a token changed in one app, a sixth
# accent the server accepts and an agent draws as Ember. Each check below names the two files it ties together.
#
# It also holds the group's acceptance line: no hardcoded #hex colour is left in any view (the views must take
# every colour from the tokens, or the OS-following theme leaves dark text on dark cards - Gotchas -> Web console).
#
# Usage: .\tests\run-appearance-consistency-tests.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$pass = 0; $fail = 0
function Check($name, $cond) {
    if ($cond) { Write-Host "PASS: $name"; $script:pass++ }
    else        { Write-Host "FAIL: $name"; $script:fail++ }
}
function Read-Src($rel) { return [System.IO.File]::ReadAllText((Join-Path $root $rel)) }
function Same($a, $b) { return (@($a) -join "|") -ceq (@($b) -join "|") }

# ---- the closed id lists ---------------------------------------------------------------------------------
$cs = Read-Src "src/Shared/Appearance.cs"
function Cs-List($name) {
    if ($cs -match ($name + '\s*=\s*\{([^}]*)\}')) { return @([regex]::Matches($Matches[1], '"([a-z]+)"') | ForEach-Object { $_.Groups[1].Value }) }
    return @()
}
$csThemes = Cs-List "Themes"; $csAccents = Cs-List "Accents"; $csMarks = Cs-List "Marks"
Check "Appearance.cs: found 3 themes, 6 accents, 3 marks" ($csThemes.Count -eq 3 -and $csAccents.Count -eq 6 -and $csMarks.Count -eq 3)

# ---- accent colours: C# palette vs both TS copies ---------------------------------------------------------
$pal = Read-Src "src/Agent.Core/AppearancePalette.cs"
$palRows = @([regex]::Matches($pal, '\["([a-z]+)"\]\s*=\s*new\(0x([0-9A-Fa-f]{6}), 0x([0-9A-Fa-f]{6}), 0x([0-9A-Fa-f]{6}), 0x([0-9A-Fa-f]{6})\)') |
    ForEach-Object { ($_.Groups[1].Value + " " + (($_.Groups[2].Value, $_.Groups[3].Value, $_.Groups[4].Value, $_.Groups[5].Value | ForEach-Object { "#" + $_.ToLower() }) -join " ")) })

function Ts-Accents($rel) {
    $t = Read-Src $rel
    return @([regex]::Matches($t, "(\w+):\s*\{ name: '[^']+',\s*dark: '(#[0-9a-f]{6})', dOn: '(#[0-9a-f]{6})', light: '(#[0-9a-f]{6})', lOn: '(#[0-9a-f]{6})' \}") |
        ForEach-Object { ($_.Groups[1].Value + " " + (($_.Groups[2].Value, $_.Groups[3].Value, $_.Groups[4].Value, $_.Groups[5].Value) -join " ")) })
}
$webAcc = Ts-Accents "web/src/appearance.ts"
$agentAcc = Ts-Accents "agent-ui/src/appearance.ts"
Check "accents: web/appearance.ts lists six, with all four colours each" ($webAcc.Count -eq 6)
Check "accents: web and agent-ui carry the SAME ids and colours, in the same order" (Same $webAcc $agentAcc)
Check "accents: AppearancePalette.cs (tray icon / Deck) carries the same ids and colours as the console" (Same $palRows $webAcc)
Check "accents: the ids are exactly the ones the SERVER validates against (Appearance.cs)" (Same @($webAcc | ForEach-Object { ($_ -split " ")[0] }) $csAccents)

# ---- themes and marks: ids -------------------------------------------------------------------------------
function Ts-Ids($rel, $startMarker, $endMarker) {
    $t = Read-Src $rel
    $a = $t.IndexOf($startMarker); $b = $t.IndexOf($endMarker, $a)
    return @([regex]::Matches($t.Substring($a, $b - $a), "id: '([a-z]+)'") | ForEach-Object { $_.Groups[1].Value })
}
foreach ($app in @("web", "agent-ui")) {
    $rel = "$app/src/appearance.ts"
    Check "themes: $app carries the ids the server validates against" (Same (Ts-Ids $rel "export const THEMES" "/**") $csThemes)
    Check "marks:  $app carries the ids the server validates against" (Same (Ts-Ids $rel "export const MARKS" "const has =") $csMarks)
}

# ---- mark geometry: web vs agent-ui, and vs the shipped SVG files -----------------------------------------
function Ts-Marks($rel) {
    $t = Read-Src $rel
    $a = $t.IndexOf("export const MARKS"); $b = $t.IndexOf("const has =", $a)
    return ($t.Substring($a, $b - $a) -replace "\s+", " ")
}
Check "mark geometry: web and agent-ui draw byte-identical shapes" ((Ts-Marks "web/src/appearance.ts") -ceq (Ts-Marks "agent-ui/src/appearance.ts"))

function Geometry($text) {
    return @([regex]::Matches($text, '\b(x|y|width|height|rx|cx|cy|r|d)="([^"]*)"') | ForEach-Object { $_.Groups[1].Value + "=" + $_.Groups[2].Value })
}
$svgFile = @{ pixel = "pixel-lock.svg"; cartridge = "cartridge.svg"; memcard = "memory-card.svg" }
$webTs = Read-Src "web/src/appearance.ts"
foreach ($id in $csMarks) {
    $a = $webTs.IndexOf("id: '$id'")
    $next = $webTs.IndexOf("id: '", $a + 5); if ($next -lt 0) { $next = $webTs.IndexOf("const has =", $a) }
    $tsGeo = Geometry $webTs.Substring($a, $next - $a)
    $svgGeo = Geometry (Read-Src ("web/src/assets/marks/" + $svgFile[$id]))
    # The SVG files carry a width/height/viewBox on the root <svg>; drop those two, keep the shapes.
    $svgGeo = @($svgGeo | Where-Object { $_ -ne "width=32" -and $_ -ne "height=32" })
    Check "mark geometry: '$id' in appearance.ts matches web/src/assets/marks/$($svgFile[$id])" ((Same $tsGeo $svgGeo) -and $tsGeo.Count -gt 3)
}

# ---- tokens: web/src/index.css vs agent-ui/src/tokens.css --------------------------------------------------
function Tokens($rel) {
    $t = Read-Src $rel
    return @([regex]::Matches($t, '(--color-[a-z-]+):\s*(#[0-9a-fA-F]{6});') | ForEach-Object { $_.Groups[1].Value + " " + $_.Groups[2].Value.ToLower() })
}
$webTok = Tokens "web/src/index.css"; $agentTok = Tokens "agent-ui/src/tokens.css"
Check "tokens: each file defines the palette three times (dark base, data-theme=light, the OS-light media query)" ($webTok.Count -eq 42 -and $agentTok.Count -eq 42)
Check "tokens: web/src/index.css and agent-ui/src/tokens.css hold identical names and values" (Same $webTok $agentTok)
Check "tokens: within web the two light blocks are identical" (Same $webTok[14..27] $webTok[28..41])
Check "tokens: within agent-ui the two light blocks are identical" (Same $agentTok[14..27] $agentTok[28..41])
foreach ($rel in @("web/src/index.css", "agent-ui/src/tokens.css")) {
    $t = Read-Src $rel
    Check "theme default: $rel follows the OS unless a theme is pinned (:root:not([data-theme]) inside prefers-color-scheme: light)" `
        ($t -match '@media \(prefers-color-scheme: light\)\s*\{\s*:root:not\(\[data-theme\]\)')
}

# ---- acceptance: no hardcoded colour left in a view ----------------------------------------------------------
# Comment lines are skipped; a hash route like '#config' is not a colour and does not match (a hex run has to end
# at a word boundary, and "#config" runs into an 'o').
$leftovers = @()
foreach ($dir in @("web/src", "agent-ui/src")) {
    Get-ChildItem (Join-Path $root $dir) -Recurse -Filter *.tsx | ForEach-Object {
        $n = 0
        foreach ($line in [System.IO.File]::ReadAllLines($_.FullName)) {
            $n++
            if ($line -match '^\s*(//|\*|/\*)') { continue }
            if ($line -match '#[0-9a-fA-F]{3,8}\b') { $leftovers += ($_.FullName.Substring($root.Length + 1) + ":" + $n) }
        }
    }
}
Check "views: no hardcoded #hex colour in any .tsx under web/src or agent-ui/src ($($leftovers.Count) found)" ($leftovers.Count -eq 0)
if ($leftovers.Count -gt 0) { $leftovers | Select-Object -First 10 | ForEach-Object { Write-Host "      $_" } }

Write-Host ""
Write-Host "==== $pass passed, $fail failed ===="
if ($fail -gt 0) { exit 1 }
exit 0
