#!/bin/bash
# SessionStart hook for Claude Code on the web.
#
# Handles PROJECT-level setup only — the pinned .NET SDK (global.json) and PowerShell Core now
# come from this environment's own Setup script (claude.ai/code -> environment settings), which
# is cached across sessions (~7 days) the way this hook, running fresh every session, cannot be.
# See https://code.claude.com/docs/en/cloud-environments#setup-scripts-vs-sessionstart-hooks.
# This hook restores the Linux-buildable .NET projects and runs `npm install` in both frontends
# (agent-ui/ in particular is the recurring gotcha in SaveLocker/CONTEXT.md — a fresh worktree
# with no node_modules silently breaks the Windows agent build and the web build/typecheck
# alike). Local sessions already have all of this; only Claude Code on the web needs it built.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

REPO_ROOT="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
cd "$REPO_ROOT"

{
  echo "export DOTNET_NOLOGO=1"
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
} >> "$CLAUDE_ENV_FILE"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# ---- Restore .NET deps ----
# Not `dotnet restore SaveLocker.sln`: src/Agent (the WinForms tray) targets net10.0-windows and
# cannot restore on Linux at all (NETSDK1100). Restoring Server + Agent.Linux pulls in Shared and
# Agent.Core transitively via project references — the same set CI's Linux jobs build.
# Guarded rather than left to `dotnet restore`'s own failure: the Setup script's cache can lag a
# global.json SDK bump, or an environment may have none configured at all, and either way we still
# want the npm installs below to run rather than aborting the whole hook under `set -e`.
if command -v dotnet >/dev/null 2>&1; then
  dotnet restore src/Server/SaveLocker.Server.csproj
  dotnet restore src/Agent.Linux/SaveLocker.Agent.Linux.csproj
else
  echo "session-start: dotnet not found on PATH — configure/refresh this environment's Setup script (see comment above)" >&2
fi

( cd web && npm install )
( cd agent-ui && npm install )
