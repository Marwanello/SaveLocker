#!/bin/bash
# SessionStart hook for Claude Code on the web.
#
# Installs the toolchain the rest of this repo assumes is already on PATH: the pinned .NET SDK
# (global.json), PowerShell Core (the tests/*.ps1 suite runs under pwsh on Linux exactly as it
# does in CI's ubuntu-latest — see .github/workflows/ci.yml), and both frontends' npm deps
# (agent-ui/ in particular is the recurring gotcha in SaveLocker/CONTEXT.md — a fresh worktree
# with no node_modules silently breaks the Windows agent build and the web build/typecheck
# alike). Local sessions already have all of this; only Claude Code on the web needs it built.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$REPO_ROOT"

SUDO=""
if [ "$(id -u)" -ne 0 ]; then
  SUDO="sudo"
fi

# ---- .NET SDK, pinned by global.json (rollForward: latestFeature within one major.minor) ----
# Ubuntu 24.04's own archive carries dotnet-sdk-<major.minor> packages directly, so this reads
# the pin instead of hardcoding "10.0" — Gotchas.md: "bump the pin and the Dockerfile's sdk:/
# aspnet: tags together, or the Docker build fails" applies here too. Deliberately not
# dotnet-install.sh: it fetches from builds.dotnet.microsoft.com, which some sandboxed network
# policies (this repo's own dev container included) reject; the Ubuntu package needs no such
# extra feed, and mixing in Microsoft's own dotnet feed alongside Ubuntu's is a known source of
# apt conflicts, so PowerShell below gets Microsoft's feed and .NET does not.
DOTNET_MAJOR_MINOR="$(python3 -c "
import json
print('.'.join(json.load(open('global.json'))['sdk']['version'].split('.')[:2]))
")"
if ! command -v dotnet >/dev/null 2>&1 || ! dotnet --list-sdks 2>/dev/null | grep -q "^${DOTNET_MAJOR_MINOR}\."; then
  $SUDO apt-get update -qq
  $SUDO apt-get install -y -qq "dotnet-sdk-${DOTNET_MAJOR_MINOR}"
fi

{
  echo "export DOTNET_NOLOGO=1"
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
} >> "$CLAUDE_ENV_FILE"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# ---- PowerShell Core ----
# tests/run-agent-tests.ps1, run-enrollment-tests.ps1, run-health-tests.ps1,
# run-hardening-tests.ps1, run-local-api-tests.ps1 and run-concurrency-tests.ps1 all run under
# pwsh on Linux in CI (REPO_MAP.md: "PowerShell Core runs on Linux, so the same script drives the
# Windows agent and the Linux agent"). GitHub's ubuntu-latest runner ships pwsh already; this
# container does not, so install it the way Microsoft documents for Ubuntu.
if ! command -v pwsh >/dev/null 2>&1; then
  UBUNTU_VERSION="$(. /etc/os-release && echo "$VERSION_ID")"
  curl -sSL -o /tmp/packages-microsoft-prod.deb \
    "https://packages.microsoft.com/config/ubuntu/${UBUNTU_VERSION}/packages-microsoft-prod.deb"
  $SUDO dpkg -i /tmp/packages-microsoft-prod.deb
  rm -f /tmp/packages-microsoft-prod.deb
  $SUDO apt-get update -qq
  $SUDO apt-get install -y -qq powershell
fi

# ---- Restore/install deps, structured to reuse the cached container layer on later sessions ----
# Not `dotnet restore SaveLocker.sln`: src/Agent (the WinForms tray) targets net10.0-windows and
# cannot restore on Linux at all (NETSDK1100). Restoring Server + Agent.Linux pulls in Shared and
# Agent.Core transitively via project references — the same set CI's Linux jobs build.
dotnet restore src/Server/SaveLocker.Server.csproj
dotnet restore src/Agent.Linux/SaveLocker.Agent.Linux.csproj

( cd web && npm install )
( cd agent-ui && npm install )
