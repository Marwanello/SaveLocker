# Resolution API — the one interface every frontend calls

Read `00-inventory.md` and `01-conflict-model-spec.md` first.

## Where this API lives, and why

The brief's invariant 7 ("one engine, many frontends... no platform reimplements conflict logic")
has to be reconciled with a fact `00-inventory.md` §5 already establishes: **the server, not any
one agent, is the sole arbiter of the fleet's conflict state** (`Decisions.md`: "single source of
truth for conflict resolution"). Moving the head is a server-side act by necessity — two machines
resolving "simultaneously" has to be decided somewhere both can see, and that's the server.

So "the core daemon" the brief means is not a new conflict-resolution engine that replaces the
server — it's **the per-machine agent daemon, acting as the one local gateway every frontend on
that machine goes through**, exactly the role it already plays for launch options and the Decky
plugin (`00-inventory.md` §5). Every non-dashboard frontend (Decky, Playnite, the Deck's own Game
Mode UI, the CLI, a local web chooser) is a frontend of the **agent's local API on `:5178`**, never
a direct caller of the server. The agent's local API, in turn, proxies to a small set of new
**agent-group** server routes using the machine's own API key — the exact pattern
`GET /api/agent/games/{id}/state` already establishes as the agent-authenticated twin of
`GET /api/games/{id}/state`.

This gives every invariant a clean home:
- **"No platform reimplements conflict logic"** — Decky's Python backend, Playnite's C# plugin, and
  `savelocker`'s CLI all call the *same* `:5178` JSON API. None of them re-derives what a conflict
  is or decides how to resolve one; they render what the agent tells them and post the user's choice
  back to it.
- **"No UI is load-bearing"** — the agent's local API is a thin, stateless-per-request proxy; if the
  server is unreachable it degrades explicitly (fails closed — §4 below) rather than any frontend
  inventing its own answer.
- **The dashboard is unaffected** — it already talks to the server's admin routes directly (fleet-
  wide, cross-machine view) and keeps doing so. It is one more consumer of the same server-side
  conflict data, not migrated onto the agent-local API.

## Layer 1 — new server (agent-group) routes

Agent-authenticated (`X-Api-Key`), scoped to the calling machine by construction (every method takes
the machine id from the authenticated principal, never from the request body — the same rule
`ApiKeyFilter` already enforces elsewhere).

```
GET  /api/agent/conflicts
     → ConflictDetailDto[]   -- every open conflict on a game THIS machine syncs
                                (mapped save path or has ever uploaded a version for it)

GET  /api/agent/conflicts/{id}
     → ConflictDetailDto | 404

POST /api/agent/conflicts/{id}/resolve
     body: { keepLocal: bool, keepBoth: bool }
     → ConflictResolveResult | 400 | 404 | 409

GET  /api/agent/games/{id}/conflict-policy
     → ConflictPolicyDto { policy: ConflictPolicy?, preferredMachineId: Guid?, effectivePolicy: ConflictPolicy }

POST /api/agent/games/{id}/conflict-policy
     body: SetConflictPolicyRequest { Policy, PreferredMachineId? }   -- reuses the EXISTING record verbatim
     → 200 | 400
```

Notes:
- `keepLocal`/`keepBoth` rather than `winningVersionId`/`keepBoth` (the admin route's shape): a
  machine-scoped caller thinks in "mine vs. theirs," not in raw version ids it would have to look up
  first. The server resolves `keepLocal: true` to whichever of `VersionAId`/`VersionBId` this
  machine actually produced (`ConflictFlag.MachineId == callingMachineId ? VersionBId : VersionAId`
  — recall `VersionBId` is always the newest divergent push, and per `IngestAsync` a conflict's
  `MachineId` is the machine that diverged, so a same-machine caller's own version is always
  `VersionBId`; a different machine resolving a conflict it didn't cause — e.g. a third machine
  cleaning up after two others diverged — passes `keepLocal: false` to mean "keep what's currently
  offered as the *other* side," documented explicitly since "local" is ambiguous for a bystander).
  Internally this maps straight onto the existing `SyncService.ResolveConflictAsync(conflictId,
  winningVersionId, resolvedBy, keepBoth)` — **no change to that method**, only a new caller that
  resolves the version id first.
- `resolvedBy` is populated from the machine's own name (`Machine.Name`), not a human's — the audit
  trail (`resolveDetail` string in `ResolveConflictAsync`) already reads "resolved by X," and X
  becomes e.g. `"ThunderHorse (Deck game-page chooser)"` — a new, small parameter threaded through so
  the audit log stays as legible as it is today; see the schema addition below.
- `ConflictResolveResult` is `{ ok: bool, error: string?, headVersionId: Guid? }` — the same
  (ok, error) shape `ResolveConflictAsync` already returns, plus the new head so a caller that wants
  to immediately show "you're now on version X" doesn't need a second round trip.
- `409` is returned (not `400`) when the conflict is no longer open by the time this request lands —
  the concrete answer to the risk register's "two devices resolving simultaneously": the second
  resolver's request loses the race inside `ResolveConflictAsync`'s own `Status != ConflictStatus.Open`
  check (already atomic per-request against SQLite's transaction), and gets back "someone already
  resolved this conflict" instead of a generic 400, so every frontend can render "already handled" rather
  than a raw error.

New field, additive, on the audit trail only:

```csharp
// SyncService.ResolveConflictAsync — new optional parameter, defaulted so the existing
// dashboard call site (admin route) needs no change
public async Task<(bool ok, string? error, Guid? headVersionId)> ResolveConflictAsync(
    Guid conflictId, Guid winningVersionId, string resolvedBy, bool keepBoth = false,
    string? resolvedVia = null)   // NEW: "dashboard" | "deck-chip" | "playnite" | "cli" | "web-chooser"
```

`resolvedVia` is folded into the existing `resolveDetail` audit string, not a new column — it's
provenance for a human reading the audit log ("who resolved what, from where"), not something any
code branches on.

## Layer 2 — the agent's local API (`:5178`), what every frontend actually calls

Token-gated (`X-SaveLocker-Token`), loopback-only, same `Guard` every existing route goes through.
Thin proxies to Layer 1, with the one piece of value-add every proxy in this codebase already has:
local-first behavior when the server is briefly unreachable.

```
GET  /api/conflicts
     → ConflictDetailDto[]        -- straight proxy to GET /api/agent/conflicts;
                                      cached (last-known-good) for offline read, see §4

GET  /api/conflicts/{id}
     → ConflictDetailDto | 404

POST /api/conflicts/{id}/resolve
     body: { keepLocal: bool, keepBoth: bool, resolvedVia: string }
     → ConflictResolveResult | 400 | 404 | 409 | 503   -- 503 = "can't reach the sync server; try again"

GET  /api/games/{id}/conflict-policy
POST /api/games/{id}/conflict-policy
     -- straight proxies, same shapes as Layer 1

POST /api/games/{id}/conflicts/notify-attempted     -- internal bookkeeping, §1 of the model spec
     body: { conflictId: Guid, channel: string }
     → 200
```

Every route above requires the token; there is deliberately no unauthenticated conflict route, even
though the risk of a rogue local process resolving a conflict on your behalf is lower-stakes than
the risk `Decisions.md` already accepted for the existing local API ("reaching it is equivalent to
owning the box") — consistency with the existing threat model matters more than a case-by-case
judgment call here.

## Layer 3 — the launch gate

Not an HTTP route — a method every launch-intercepting caller (the Linux wrapper today, a future
Playnite plugin, a future Decky pre-launch mechanism if one exists) calls in-process or through the
local API before starting a game, and a post-exit counterpart. This formalizes what
`SyncEngine.OnGameLaunchAsync`/`OnGameExitAsync` already do, extended per §2 of the model spec:

```csharp
// Agent.Core — SyncEngine, extended (not replacing OnGameLaunchAsync/OnGameExitAsync's
// existing lease behavior — this wraps it)
public async Task<LaunchGateResult> PrepareLaunchAsync(TrackedGame game, CancellationToken ct);

public enum LaunchDecision { Proceed, ProceedSyncPaused, Blocked }
public sealed record LaunchGateResult(
    LaunchDecision Decision,
    string Reason,               // human-readable, always populated
    Guid? OpenConflictId);       // set when Decision != Proceed and a conflict exists to show
```

Behavior, folding in §2 of the model spec:

1. Take the lease (existing `OnGameLaunchAsync` behavior, unchanged) — if held elsewhere, that's
   already a `Proceed` with a warning (today's behavior; a conflict is *possible*, not certain).
2. If local has unsynced changes, attempt the commit-before-choose push (model spec §2).
   - Fast-forward or NoChange → pull, `Proceed`.
   - Conflict → apply the effective policy (§1 of the model spec):
     - `NewestWins`/`PreferMachine` and this machine auto-wins → already resolved by the push path
       itself (`IngestAsync`'s existing auto-resolve branch) → pull the (now-own) head → `Proceed`.
     - `NewestWins`/`PreferMachine` and this machine auto-loses → the *other* machine's content is
       now head; pull it → `Proceed` (the local attempt is preserved as a `Protected`-eligible
       version exactly as `keepBoth` would leave it, since it's still the old head's chain — no,
       concretely: the losing push is NOT auto-protected today, only `keepBoth` on a *manual*
       resolve protects both sides. This is a real question, flagged in `07-open-questions.md`
       rather than silently decided here.)
     - `Manual` (or no frontend can ask under any policy) → **`Blocked` or `ProceedSyncPaused`**,
       decided by platform policy (`03-platform-ux-flows.md` per-platform), never a guess.
3. Return the result. The caller (wrapper, Playnite plugin, Decky pre-launch shim) is responsible for
   actually blocking or allowing the process start based on `Decision` — the gate never launches or
   kills anything itself, matching the existing separation (`ProtonRun` owns the process, `SyncEngine`
   only advises).

## Error cases, enumerated

| Condition | Layer 1 (server) | Layer 2 (agent local) |
|---|---|---|
| Conflict id doesn't exist / not on a game this machine syncs | 404 | 404 |
| Conflict already resolved (race) | 409 | 409 (passed through) |
| `keepLocal`/version doesn't correspond to a real side | 400 | 400 |
| Resolving would rewind a newer head (existing `ResolveConflictAsync` guard, unchanged) | 400, `error` names it | 400 (passed through) |
| Server unreachable | n/a | 503, `ConflictDetailDto[]` GETs fall back to last-known-good cache (§4) |
| Caller unauthenticated / wrong origin | 401/403 (existing `ApiKeyFilter`/`Guard`) | 401/403 (existing `Guard`) |
| `POST resolve` while offline | n/a | **Refused (503), never queued** — see §4 for why this is deliberate |

## §4 — Why resolution is not offline-queueable, and what "fail closed" means here concretely

The existing `OfflineQueue` retries **pushes** while the server is unreachable — safe, because a
push is inherently idempotent-by-content (the server dedupes by hash). **Resolution is not safe to
queue the same way**: it names a specific conflict id and a specific winning version, and by the
time connectivity returns, the state that resolution was computed against may no longer be true
(another machine may have already resolved it, or the head may have moved via an auto-policy on
another machine's push). Queueing a stale resolve and replaying it blind is exactly the "guess"
invariant 4 forbids.

So: a resolve attempted with no server connectivity returns `503` immediately, with a message every
frontend renders as-is ("Can't reach the sync server to resolve this right now — nothing was
changed. Try again once you're back online, or from another machine."). The conflict record itself,
and the fact that sync is paused for that game, are already durable and already visible from any
other frontend that *can* reach the server — this is the "resolution can happen minutes or weeks
later from any frontend" terminal state the brief asks for, achieved by **not** pretending
resolution succeeded, not by a retry queue.
