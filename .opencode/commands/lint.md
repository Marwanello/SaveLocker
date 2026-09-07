---
description: Lint web, agent-ui, and the .NET server
---

$ARGUMENTS

Run `npm run lint` (oxlint) in `web/` and in `agent-ui/`, applying safe auto-fixes. For C#, build `src/Server/SaveLocker.Server.csproj --no-incremental` (stop agent/server first — DLL lock) and report warnings as lint findings. List remaining issues grouped by project; do not commit anything.
