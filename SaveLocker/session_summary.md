# Session summary — 2026-08-28

Decky conflict-resolution design (planning only; no application code changed).

## What was asked

Design moving conflict resolution out of the SaveLocker web dashboard and into the per-platform
agents (Steam Deck/Decky, Windows/Playnite, Linux). First pass produced 8 design docs in
`docs/design/00`–`07` without the user's real Decky fork. After checking the actual fork at
`D:\Projects\SaveLocker\SaveLocker-Decky` (v0.2.3), the deliverable became **one** consolidated plan:
`SaveLocker/logs/2026-08-28_decky-conflict-resolution.md`, plus a `Backlog.md` entry.

## Corrected architecture (user's direction)

- **Server never decides a conflict.** It only moves the head when told and keeps non-head content as
  a labeled, recoverable backup. All resolution *logic* lives in **Agent.Core**, reached by every
  frontend through the local `:5178` API. This removes the server-side `NewestWins`/`PreferMachine`
  auto-win from `SyncService.IngestAsync`.
- **Conflicts are always local-vs-cloud**, never device-vs-device. Retires the bystander question.
- **"Keep both" = protect the losing save as a downloadable backup**, shown in a separate dashboard
  "Backups" sub-menu, not in normal history. Needs **no new schema** (verified against
  `Entities.cs`): a backup is any version not on the ancestor chain of `Game.HeadVersionId`; reuses
  existing download and set-latest endpoints.

## Launch-blocking behavior (settled)

With "sync before start" on, a confirmed conflict at Play time — regardless of whether "sync on page
open" is also on — cancels the launch, opens a resolve popup (local vs. cloud, optional "keep both"
toggle), syncs the choice, then auto-relaunches. No second Play press. The `pageOpenPullIsFresh` skip
must not apply when the fresh page-open result was a conflict. Playnite mirrors this via
`OnGameStarting`/`CancelStartup` → dialog → sync → `IPlayniteAPI.StartGame(Guid)`.

## Phasing

- **0/1** — server + Agent.Core, must ship together (removing server auto-policy without the agent
  replacement would be a regression).
- **2** — Force push/pull bookkeeping fix. **7** — cheap sync-status endpoint. **4** — Linux
  `ProtonRun.cs` gate. **5** — Decky UI. **8** — Playnite. All parallelizable off 0/1.
- **6** — Decky launch-gate wiring; depends on 5.
- **3** — dashboard Backups tab; zero dependencies, can ship first.

## Open item

Phase 0/1 claims "usable end-to-end from the CLI alone," but `savelocker conflicts` / `savelocker
resolve` were never added to `AgentCli.cs` in that scope. Either add them to Phase 0/1 or drop the
claim — **awaiting the user's choice**.

## Status

All deliverables written and on disk, **uncommitted**, on branch
`claude/savelocked-conflict-resolution-16e289`:

- `SaveLocker/logs/2026-08-28_decky-conflict-resolution.md` (new)
- `SaveLocker/Backlog.md` (modified — High-priority entry referencing the plan)
- `docs/design/00`–`07` (untracked — first-pass reference, partly superseded)
