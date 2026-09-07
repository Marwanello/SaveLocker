# SaveLocker — Claude Instructions

## Session start (mandatory)

Read `SaveLocker/docs/CONTEXT.md` and `SaveLocker/docs/REPO_MAP.md` before doing anything else. Do not read any other files, ask clarifying questions, or begin work until both files are loaded. These two files define the current project state and codebase layout.

## Vault structure

The Obsidian vault is at `SaveLocker/` (repo root). Content lives under `SaveLocker/docs/` — one folder per backlog item under `docs/tasks/` (`plan.md` or `summary.md` plus supporting files), write-ups under `docs/logs/`. Task folders are fed one file at a time per the Backlog line, not always as a single flat file.

| File | When to read it |
|------|----------------|
| `SaveLocker/docs/CONTEXT.md` | Every session start — project state, quick-ref commands, gotchas |
| `SaveLocker/docs/REPO_MAP.md` | Every session start — codebase layout, auth model, key paths |
| `SaveLocker/docs/Architecture.md` | When touching system design, data model, or sync flow |
| `SaveLocker/docs/Decisions.md` | Before proposing a direction that might already be settled |
| `SaveLocker/docs/Gotchas.md` | Before touching builds, paths, or the running server |
| `SaveLocker/docs/API Reference.md` | When adding or changing server endpoints |
| `SaveLocker/docs/Build and Run.md` | When running builds, Docker, or the test suite |
| `web/src/help/cli-reference.md` | When touching agent CLI commands (KB article; `SaveLocker/docs/CLI Reference.md` is a stub pointing here) |
| `SaveLocker/docs/Backlog.md` | When prioritizing what to work on next |
| `SaveLocker/docs/tasks/<name>/*.md` | Feed one task file at a time — execute only its steps, then stop |
| `SaveLocker/docs/logs/sessions.md` | When asked about project history |

## Task execution

When a `SaveLocker/docs/tasks/` folder exists for the current work:
1. Read the task file.
2. Execute **only** the steps listed.
3. Verify via the method specified in the task file.
4. Stop and report — do not continue to the next task unless instructed.
5. We should have a commit for each task we successfully complete.

## Session handoff (end of session)

Before commiting, update the vault so the next session starts clean:
1. Update `SaveLocker/docs/CONTEXT.md` — current status, any new gotchas, next action.
2. Move completed `docs/tasks/<name>/` folders to `docs/logs/` with a date prefix.
3. Update `SaveLocker/docs/Backlog.md` if priorities shifted.
4. Commit the vault changes with a `Docs:` prefix commit message.

## Coding conventions

- Build server with `--no-incremental`; stop agent/server first (DLL lock).
- Dev storage is `src/Server/localstate/` — never `data/` (case-collision with `Data/`).
- Target framework is **net10.0** (LTS). EF Core tracks it at 10.0.x. The SDK is pinned in `global.json` — bump it and the Dockerfile's `sdk`/`aspnet` tags together.
- After any API change: regenerate `web/src/api-types.ts` and commit the updated `src/Server/openapi.json` snapshot.
- No comments unless the WHY is non-obvious. No trailing summaries after diffs.
