# SaveLocker — Agent Instructions

## Session start (mandatory)

Read `SaveLocker/CONTEXT.md` and `SaveLocker/REPO_MAP.md` before doing anything else. Do not read any other files, ask clarifying questions, or begin work until both files are loaded. These two files define the current project state and codebase layout.

## Vault structure

The Obsidian vault is at `SaveLocker/` (repo root). It is flat by design — no deep nesting.

| File | When to read it |
|------|----------------|
| `SaveLocker/CONTEXT.md` | Every session start — project state, quick-ref commands, gotchas |
| `SaveLocker/REPO_MAP.md` | Every session start — codebase layout, auth model, key paths |
| `SaveLocker/Architecture.md` | When touching system design, data model, or sync flow |
| `SaveLocker/Decisions.md` | Before proposing a direction that might already be settled |
| `SaveLocker/Gotchas.md` | Before touching builds, paths, or the running server |
| `SaveLocker/API Reference.md` | When adding or changing server endpoints |
| `SaveLocker/Build and Run.md` | When running builds, Docker, or the test suite |
| `web/src/help/cli-reference.md` | When touching agent CLI commands (KB article; `SaveLocker/CLI Reference.md` is a stub pointing here) |
| `SaveLocker/Backlog.md` | When prioritizing what to work on next |
| `SaveLocker/tasks/*.md` | Feed one task file at a time — execute only its steps, then stop |
| `SaveLocker/logs/sessions.md` | When asked about project history |

## Task execution

When a `SaveLocker/tasks/` file exists for the current work:
1. Read the task file.
2. Execute **only** the steps listed.
3. Verify via the method specified in the task file.
4. Stop and report — do not continue to the next task unless instructed.

## Session handoff (end of session)

Before closing, update the vault so the next session starts clean:
1. Update `SaveLocker/CONTEXT.md` — current status, any new gotchas, next action.
2. Move completed `tasks/*.md` files to `SaveLocker/logs/` with a date prefix.
3. Update `SaveLocker/Backlog.md` if priorities shifted.
4. Commit the vault changes with a `Docs:` prefix commit message.

## Coding conventions

- Build server with `--no-incremental`; stop agent/server first (DLL lock).
- Dev storage is `src/Server/localstate/` — never `data/` (case-collision with `Data/`).
- Target framework is **net10.0** (LTS). EF Core tracks it at 10.0.x. The SDK is pinned in `global.json` — bump it and the Dockerfile's `sdk`/`aspnet` tags together.
- After any API change: regenerate `web/src/api-types.ts` and commit the updated `src/Server/openapi.json` snapshot.
- No comments unless the WHY is non-obvious. No trailing summaries after diffs.

## Build commands (exact)

### Server
```sh
dotnet build src/Server/SaveLocker.Server.csproj --no-incremental
cd src/Server && dotnet run
# API: http://localhost:5179   Health: /health   Swagger: /swagger
```

### Windows agent (build → run)
```sh
dotnet build src/Agent/SaveLocker.Agent.csproj --no-incremental
# Tray:
src/Agent/bin/Debug/net10.0-windows/SaveLocker.Agent.exe
# CLI:
src/Agent/bin/Debug/net10.0-windows/SaveLocker.Agent.exe status
```

### Web dashboard (dev)
```sh
cd web && npm install && npm run dev
# Dashboard: http://localhost:5173 (proxies /api + /art to :5179)
```

### Linux agent (in WSL on ext4 home)
```sh
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Agent.Linux/SaveLocker.Agent.Linux.csproj --no-incremental
bash tests/linux/run-linux-tests.sh
```

### Release builds
```sh
# Windows installer (requires Inno Setup 6)
.\installer\build-installer.ps1

# Linux tarball (build on OLDEST glibc — Ubuntu 24.04)
bash packaging/linux/build-linux.sh
```

### Regenerate OpenAPI types (after API changes)
```sh
# 1. Start server: cd src/Server && dotnet run
# 2. In another terminal:
cd web && npm run gen:api
# Regenerates web/src/api-types.ts from http://localhost:5179/openapi/v1.json
# Commit the updated src/Server/openapi.json snapshot alongside any API changes
```

## Critical gotchas (read before any build/test/path work)

- **`dotnet build a.csproj b.csproj` builds only the first** — the second is silently skipped. Build each project separately.
- **`npm run gen:api` in `agent-ui/` hardcodes `:5178`** — start a dev daemon on a free port (e.g., 5190) and generate against that: `npx openapi-typescript http://localhost:5190/openapi/v1.json -o src/api-types.ts`
- **Dev DB location**: `src/Server/localstate/savelocker.db` (not `data/` — case collision deletes source files).
- **Test isolation**: Clear `.verify/` and `src/Server/localstate/` *together* before running suites. Give every test server its own `Storage__DbPath`/`Storage__ArchiveRoot`.
- **Test build version**: Use `9.9.9-ci` pattern so `UpdateChecker` doesn't confuse it with a real release.
- **Windows ACLs**: Test state dir gets locked to creator (WA-03). Clean up elevated if suite runs as different user.
- **WSL**: Never build from `/mnt/*` (breaks inotify, permissions, case-sensitivity). Use `~/SaveLocker` on ext4.
- **Fresh worktree trap**: New clone's `agent-ui` and `web` need `npm install` before Windows agent build or web typecheck.

## Auth model

| Surface | Auth |
|---------|------|
| Agent routes (server) | `X-Api-Key: <machine key>` (issued at registration) |
| Admin routes (server) | `X-Admin-Password: <password>` (set in dashboard; open if unset) |
| Agent's own API (:5178) | `X-SaveLocker-Token` from `api-token` file + loopback `Host` + `Origin` check |

## Test suites (run from repo root)

```sh
# Windows agent (needs server on :5179)
.\tests\run-agent-tests.ps1

# Linux agent (in WSL, starts its own server)
bash tests/linux/run-linux-tests.sh

# Other suites (each starts its own server on distinct port)
.\tests\run-winagent-tests.ps1        # WA-* bug bounty, needs interactive desktop
.\tests\run-server-bugbounty-tests.ps1
.\tests\run-delta-upload-tests.ps1
.\tests\run-hardening-tests.ps1
.\tests\run-local-api-tests.ps1
.\tests\run-concurrency-tests.ps1
.\tests\run-health-tests.ps1
.\tests\run-enrollment-tests.ps1
.\tests\run-enrollment-tls-tests.ps1  # LOCAL ONLY (needs trusted dev cert)
.\tests\verify-password-compat.ps1
.\tests\cross-os\crossos.ps1          # Cross-OS round-trip
```

## Architecture highlights

- **Server**: ASP.NET Core (net10.0) + EF Core + SQLite in Docker. Single source of truth.
- **Agents**: Windows (WinForms tray + WebView2) and Linux (headless daemon + `savelocker run` launch wrapper). Both share `Agent.Core` sync engine.
- **UIs**: Web dashboard (`web/`), Agent UI (`agent-ui/` served by both agents on :5178), Game Mode UI (`savelocker ui` — SDL + ImGui).
- **Sync model**: Hub-and-spoke. Agents only make outbound HTTP; lease/checkout prevents conflicts; content-hash + parent lineage detects divergence.
- **Data flow**:
  - Push: `POST /api/games/{id}/upload` → `SyncService` → `ArchiveStore` + SQLite
  - Pull: `GET /api/games/{id}/download` → `SyncService` → `ArchiveStore` → zip stream
  - Dashboard: `GET /api/overview` → `SyncService` → `GameStateDto[]`

## Key runtime paths

| Item | Path |
|------|------|
| Agent config (Windows) | `%PROGRAMDATA%\SaveLocker\config.json` |
| Agent state (Linux) | `~/.local/share/SaveLocker/` (also the install prefix) |
| Agent log | `<state dir>\agent.log` |
| Local API token | `<state dir>\api-token` (0600) |
| Server DB (dev) | `src/Server/localstate/savelocker.db` |
| Server DB (Docker) | `/data/savelocker.db` |
| Archives (Docker) | `/data/archives/` |
| Installer store (Docker) | `/data/agent-installer/` (win-x64) + `/data/agent-installer/linux-x64/` |

## Release rollout (two halves)

1. Tag `vX.Y.Z` → CI builds both agents + Docker image.
2. **Upload packages in Config → Agent updates** (both Windows and Linux rows — GitHub Release assets are NOT auto-offered).
3. Windows agents prompt in tray; Decks stage + install at next start.
4. Redeploy console: `docker compose pull && docker compose up -d`.

## Repo-specific conventions

- **MinVer** derives version from `v*` tags. Windows agent uses `MinVerTagPrefix=v`; Linux agent has no MinVer (stamps explicitly in `build-linux.sh`).
- **`openapi.json` is committed** — regenerate after API changes via `web: npm run gen:api`.
- **PBKDF2 params are part of on-disk format** — changing them invalidates all passwords. `verify-password-compat.ps1` guards this.
- **`SQLitePCLRaw.bundle_e_sqlite3` pinned to 3.x** — CVE-2025-6965 in 2.x. Do not remove pin until EF Core resolves 3.x.
- **ImGui.NET stays at 1.90.8.1** — nav fix is in `Agent.Linux/Ui/ImGuiInternal.cs` via direct `cimgui` P/Invoke.
- **Decky plugin lives in separate repo** (`SkorcherX/SaveLocker-Decky`). This repo distributes it via server's installer slot `?platform=decky-plugin`.

## Test environment variables (test-only, not user settings)

| Variable | Purpose |
|----------|---------|
| `SAVELOCKER_LEASE_RENEW_SECONDS` | Shortens 3-hour lease renewal for tests |
| `SAVELOCKER_SYNC_LOCK_SECONDS` | Shortens ~13-min cross-process lock wait |
| `SAVELOCKER_TRAY_PORT` | Moves Windows tray API off :5178 (scopes mutex too) |
| `SAVELOCKER_STATE_ROOT` | Moves entire agent state root (Windows) |
| `SAVELOCKER_RUNKEY_SUBPATH` | Moves HKCU Run key for access-denied tests |

Set per-process and clear afterwards — leaving them set makes later runs behave like bugs.