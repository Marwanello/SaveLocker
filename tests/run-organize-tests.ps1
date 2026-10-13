# Games tab arrange logic (web/src/components/game/organize.ts). Pure view logic (grouping, sorting,
# the rolling date buckets), so the fixtures run the real module headlessly: the one entry point is
# compiled with the console's own tsc into .verify/organize and tests/organize-fixtures.cjs drives it.
# Needs web/node_modules (npm install in web/ - the fresh-worktree trap, [[Gotchas]]).
#
# Runs under Windows PowerShell 5.1 and pwsh (CI's build-web job runs it on ubuntu).

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$web = Join-Path $root "web"
$out = Join-Path (Join-Path $root ".verify") "organize"
$tsc = Join-Path $web "node_modules/typescript/bin/tsc"

if (-not (Test-Path $tsc)) { throw "web/node_modules is missing - run npm install in web/ first." }

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Force -Path $out | Out-Null

# --ignoreConfig: there is no tsconfig at the repo root. --jsx/--lib cover the .tsx and DOM types the
# type-only imports of organize.ts reach; the emit itself is one plain CommonJS module graph.
& node $tsc --ignoreConfig --jsx react-jsx --lib es2022,dom,dom.iterable --outDir $out --rootDir (Join-Path $web "src") `
    --target es2022 --module commonjs --moduleResolution node10 --ignoreDeprecations '6.0' --skipLibCheck --esModuleInterop `
    (Join-Path $web "src/components/game/organize.ts")
if ($LASTEXITCODE -ne 0) { throw "tsc failed to compile organize.ts" }

& node (Join-Path $PSScriptRoot "organize-fixtures.cjs") $out
exit $LASTEXITCODE
