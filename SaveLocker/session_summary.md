# Session summary — 2026-08-29

Conflict resolution **Phase 0/1** implemented (server + Agent.Core) and shipped as PR #20, all CI green.

## What was asked

Implement Phase 0/1 of the Decky conflict-resolution plan
(`logs/2026-08-28_decky-conflict-resolution.md`): move the conflict-resolution *decision* out of the
server and into the agent. Server + Agent.Core only — no UI/Decky/Playnite (later phases). Then:
create a kebab-case branch `save-conflicts-phase-0-1`, open a PR, and investigate/fix its CI failures.

## The architectural change, in code

- **Server no longer decides.** Removed the `autoWins` (`NewestWins`/`PreferMachine`) branch from
  `SyncService.IngestAsync` — every divergence now unconditionally records a `ConflictFlag`.
- **The agent decides.** `SyncEngine.TryPolicyResolveAsync` (wired into the `UploadStatus.Conflict`
  path before the CONFLICT alert) fetches the game's policy, evaluates it locally
  (`thisMachineWins = NewestWins || (PreferMachine && PreferredMachineId == this machine)`), and if
  this machine wins, calls the resolve endpoint itself. Its success log deliberately avoids the word
  "conflict."
- **Comparison is always local-vs-cloud**, never device-vs-device.
- **`resolverMachineId` fan-out exclusion.** `ResolveConflictAsync` gained a `resolverMachineId`
  param: an agent auto-resolving its own push excludes itself from the fleet pull fan-out (it already
  has the winning bytes); the admin/dashboard path passes `null` → everyone is told. This is what lets
  a *second* machine learn about a conflict the *first* machine's agent auto-resolved.

## Surfaces added

- **Server agent-group routes** (X-Api-Key): `GET /agent/conflicts`, `GET /agent/conflicts/{id}`,
  `POST /agent/conflicts/{id}/resolve`, `GET/POST /agent/games/{id}/conflict-policy`.
- **Agent local API** (:5178, token-gated): `GET /api/conflicts`, `GET /api/conflicts/{id}`,
  `POST /api/conflicts/{id}/resolve` (`LocalResolveRequest`), `GET/POST /api/games/{id}/conflict-policy`,
  and `GET /api/games/{id}/sync-status` (Phase 7, folded in — local hash vs. head hash, no download).
- **CLI** (`AgentCli.cs`): `conflicts` and `resolve-conflict` (`--keep local`/`--keep cloud`) — makes
  Phase 0/1 usable end-to-end from the CLI alone, closing the design's open item.
- **Contracts**: `ConflictPolicyDto`, `SyncStatusDto`. **ApiClient**: five new client methods.

## CI failure and fix

`package-linux` failed at `agent-ui`'s `npm run gen:api -- --check`: the committed
`agent-ui/src/api-types.ts` had **Windows** schema ordering. `openapi-typescript` emits
`components.schemas` in the OpenAPI document's key order, and .NET's OpenAPI generator orders schemas
by **reflection order, which differs between Windows and Linux** — one unrelated schema
(`ResolveLaunchOptionsRequest`) sat in a different position; content identical. Fixed by regenerating
against a real **Linux** daemon (built + run in WSL) so it matches what CI produces; verified
`gen:api -- --check` exits 0 there before pushing (`778a874`). The other generated artifacts
(`src/Server/openapi.json`, `web/src/api-types.ts`) were pure additions with no reordering.

**Footgun for next time:** any hand-regenerated `agent-ui/src/api-types.ts` must be generated against
a **Linux** daemon (WSL), not the Windows tray, or `package-linux` fails on the schema-order diff.

## Verification

All CI checks on PR #20 green (build-dotnet, build-web, build-agent-ui, docker-build, package-linux,
agent-tests-linux, crossos chain). Design preserves both load-bearing assertions: CS-04 ("winning
uploader gets no redundant pull") via `resolverMachineId` exclusion, and run-agent-tests ("resolving
queued a pull for BOTH machines") via the admin path's `null`.

## Status

PR #20 open and green on branch `save-conflicts-phase-0-1`:

- `41236fd` Conflict resolution Phase 0/1: move the decision into the agent
- `778a874` Fix CI: regenerate agent-ui api-types on Linux for canonical schema order

Phase 0/1 is complete. Later phases (2–8: Force-fix, Backups tab, Linux gate, Decky UI/launch wiring,
Playnite) remain unbuilt — see `logs/2026-08-28_decky-conflict-resolution.md`.
