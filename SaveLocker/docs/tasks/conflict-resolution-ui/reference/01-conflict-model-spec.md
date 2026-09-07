# Conflict model spec

Read `00-inventory.md` first. This document says what changes in the data model and what stays —
most of the hard safety mechanics the brief asks for (§3 of the prompt) **already exist**; the job
is closing one real gap (pre-launch divergence has no durable, resolvable record) and adding the
handful of fields every new frontend needs that today only the dashboard's three-call dance
assembles client-side.

## 0. What's already correct, and is not being redesigned

State this plainly so the rest of the doc doesn't re-litigate settled ground:

- **Snapshot = `SaveVersion`.** Content-hashed, immutable once written, parent-chained
  (`ParentVersionId`), exemptable from retention (`Protected`). This is already "no destructive
  resolution" in data-model form: nothing in the codebase ever deletes a `SaveVersion` except
  retention pruning, and pruning already skips the head, both sides of every open conflict, and
  anything `Protected`. **Kept as-is.**
- **Manifest = `SaveVersionFile`.** Per-file `(path, sha256, size)` baseline per version, already
  used for delta uploads. **Kept as-is** — the resolution UI's "which files actually differ between
  these two saves" (a nicer answer than "N files, newest change at T") can be built directly on top
  of two versions' `SaveVersionFile` sets with no schema change; flagged as a possible UX
  enhancement in the phased plan, not required for the invariants.
- **Lineage = the parent-pointer chain, checked against one server-held head.** The brief asks for
  "per-device generation counters / vector clock, parent snapshot id." **Recommendation: keep the
  parent-pointer model; do not add vector clocks or per-device generation counters.** A vector clock
  earns its complexity when there is no single arbiter and any two replicas might need to compare
  histories directly (true peer-to-peer). SaveLocker is deliberately hub-and-spoke — `Decisions.md`
  calls the server "single source of truth for conflict resolution" and rejected peer-to-peer
  (Syncthing) for exactly this reason. Under that topology, "does the head I'm pushing against match
  the server's actual current head" is a complete, correct answer to "did I diverge," and it's what
  the code already does (`PrepareUploadAsync`). **Rejected alternative:** a full vector clock per
  machine — adds a data structure and a comparison algorithm with no case it handles that the parent
  check doesn't, given one arbiter.
- **Lease = `Lease`.** Already exactly the "advisory remote lock/lease with TTL and heartbeat while a
  game is running" the brief asks for under conflict *prevention*: one row per game, 3h duration,
  renewed on a timer while a game is running, atomic acquire/release/force-release, "in use on
  &lt;device&gt;" surfaced today as a toast + `LeaseHeldElsewhere` health event. **Kept as-is.** The
  per-platform UX doc adds a Deck chip *state* for it ("in-use-elsewhere") but that is presentation,
  not a data-model change.
- **Auto-resolution policy = `ConflictPolicy` enum (`Manual`/`NewestWins`/`PreferMachine`).** Kept
  verbatim — the brief requires the Decky settings page draw from "the exact same option set that
  exists in the app today," and there is no reason to add a fourth option nobody asked for.
- **Exclusion of transient files** (invariant: "exclusion of lock/temp/shader-cache files from the
  save manifest") is already the `ExcludeGlobs` mechanism (`Game.ExcludeGlobs`, newline-separated,
  layered over `Sync:DefaultExcludeGlobs`), applied by `SaveArchive.ComputeManifest`/`CreateArchive`
  before anything is hashed or archived. **Kept as-is** — not a conflict-resolution concern, already
  solved upstream of it.

## 1. What's new: a per-game global default for `ConflictPolicy`

Mirrors the exact precedent `Game.RetainVersions` already sets (nullable per-game override, global
`AppSetting` fallback):

```csharp
// Entities.cs — Game
public ConflictPolicy? ConflictPolicy { get; set; } = null;   // was: non-nullable, default Manual
```

A new `AppSetting` key (same table already used for `defaultExcludeGlobs`, `autoFetchHours`, etc.):
`conflict.default_policy`, value one of `Manual|NewestWins|PreferMachine`, defaulting to `Manual` if
absent. Resolution order at ingest time (`SyncService.IngestAsync`):

```
effective policy = Game.ConflictPolicy ?? SettingsService.Get("conflict.default_policy") ?? Manual
```

`PreferredMachineId` stays per-game only — a global "preferred machine" makes no sense across games
different machines actually play.

**"Always ask" degrades to `Manual`.** There is no new state for this — `Manual` *is* "always ask."
When the effective policy is `Manual` and no frontend is available to ask (the headless escalation
ladder in `03-platform-ux-flows.md` reaches its terminal rung), the existing behavior — record the
`ConflictFlag`, leave the head untouched — **is** the fail-closed answer. No code path may
substitute a guess for an unanswerable "ask."

## 2. What's new: closing the pre-launch/unsynced-local gap

This is the one real gap identified in `00-inventory.md` §2 and §6.5. Today, `SyncEngine.PullAsync`
finding `hasUnsyncedLocal && !force` just refuses and alerts — there is no durable, two-sided,
resolvable record, because the local side was never uploaded and so has no `SaveVersion` at all.

**Decision: commit-before-choose. The local side becomes a real `SaveVersion` before any chooser is
ever shown, by attempting an ordinary push.** This reuses the *entire* existing conflict machinery
instead of inventing a parallel "pending decision" model:

1. At any point the agent would otherwise refuse a pull because of unsynced local changes (pre-launch
   check, a manual `sync`, a scheduled reconcile), it first attempts `PushAsync(force:false,
   settle:false)` for that game.
2. One of three things happens, and all three are already-handled outcomes:
   - **Fast-forward.** The server head hadn't moved; the local push becomes the new head, no
     conflict, pull is now a no-op. This is the common case — "unsynced local" often just means
     "nobody had pushed today yet," not a real divergence.
   - **NoChange.** Local content already matches the head; nothing to resolve.
   - **Conflict.** The server head *had* moved (a genuine divergence: two machines both played). A
     real `ConflictFlag` is recorded, with a real `SaveVersion` on each side — machine name, size,
     content hash, and (via `SaveVersionFile`) a per-file manifest, all populated by the existing
     upload path. **This is now identical in shape to a conflict detected any other way**, and every
     frontend built against the Resolution API (`02-resolution-api.md`) handles it with no special
     case for "how did this conflict come to exist."
3. Only in the `Conflict` outcome does a chooser ever need to appear. In the other two, the pull
   proceeds (or is a no-op) exactly as it does today.

**Why this is safe under every invariant:**
- *No destructive resolution*: the push in step 1 happens **before** anything is overwritten, so the
  losing side (whichever the user doesn't ultimately keep) is a durable, hash-verified `SaveVersion`
  the moment the conflict is detected — not a snapshot taken defensively "just in case," but the
  actual pre-existing mechanism doing its actual job.
- *Atomic and resumable*: step 1 is an ordinary push through the existing staged-upload path
  (`ArchiveStore`, staging-dir-then-rename), already interruption-safe.
- *Fail closed*: if the push itself cannot complete (server unreachable), the launch gate has a real,
  already-existing signal (`HttpRequestException`) to fall back on — see §3.

This also directly answers the brief's §4.3 question "may a game still be launched while a conflict
is pending, and under what safeguard": **"allow launch after auto-snapshotting local state" is not a
separate option to design — it's what step 1 already does whenever launch proceeds past a detected
conflict**, because the local state is committed as a real version before the game is ever allowed
to start. The other two options the brief lists ("block launch" / "allow launch with sync paused")
become the two things a resolvable `ConflictFlag` can mean for the *pull* half of the launch gate,
covered per-platform in `03-platform-ux-flows.md`.

## 3. What's new: a unified conflict-detail shape

The dashboard today assembles a conflict's display data from three separate calls
(`versions`, two `versions/{id}/stats` lookups). Every new frontend (Deck chip modal, Playnite
dialog, CLI, local web chooser) needs the same data in **one** call, since several of them run over
a constrained channel (SSH tunnel, Deck's local network hop to the daemon) where three round trips
is real latency. New DTO, additive, no change to any existing wire shape:

```csharp
// Shared/Contracts.cs — new
public record ConflictSideDto(
    Guid VersionId,
    string MachineName,
    DateTime CreatedAt,
    long Size,
    int FileCount,
    DateTime? NewestFileWriteUtc,
    bool IsCurrentHead);

public record ConflictDetailDto(
    Guid Id,
    Guid GameId,
    string GameName,
    ConflictSideDto Local,      // this machine's side — the one the caller can actually preview/diff
    ConflictSideDto Remote,     // the server's other side
    int Count,
    DateTime CreatedAt,
    DateTime LastSeen,
    bool Escalated);
```

`Local`/`Remote` rather than `VersionA`/`VersionB`: every frontend this design adds is inherently
scoped to *one machine's* point of view (the Deck asking "what's my situation"), unlike the
dashboard's fleet-wide view. The agent-local API resolves which of `VersionAId`/`VersionBId` is
"local" by comparing against `TrackedGame.LastKnownVersionId`; the server-side agent-group endpoint
takes the calling machine's id as the reference point for the same reason. No content hash is added
to the DTO — it was never shown in the dashboard either, and it's not a decision-relevant number for
a human; `02-resolution-api.md` (same folder) covers where a hash-level diff *would* live if ever
needed (out of scope for phase 1).

## 4. What's new: a lightweight, session-scoped escalation record (Linux/headless only)

Not a database entity — this is agent-local, in-memory-plus-a-log-line state, because it answers a
different question than `ConflictFlag` does. `ConflictFlag` answers "does a conflict exist"; this
answers "have I already tried every way of telling a human about it, and if I fell back to a
webhook or a URL in the logs, when did I do that, so I don't spam it every 20s poll." Sketch (see
`03-platform-ux-flows.md` for the ladder itself):

```csharp
// Agent.Core — new, not persisted to disk beyond the agent log
public sealed record ConflictNotificationState(
    Guid ConflictId,
    DateTime LastNotifiedUtc,
    string LastChannel);   // "dbus" | "web-chooser-logged" | "webhook" | "none-available"
```

Kept out of the durable data model deliberately: it is a courtesy against notification spam, not a
safety property. If the agent restarts and forgets it notified once, the worst outcome is one
duplicate notification — never a lost conflict, since the `ConflictFlag` itself is what's durable
and authoritative.

## 5. Summary of schema changes

| Change | Kind | Migration needed? |
|---|---|---|
| `Game.ConflictPolicy` → nullable | Column type change | Yes — small, same shape as prior nullable-column migrations in this codebase (e.g. `AddGameRetainVersions`). |
| New `AppSetting` key `conflict.default_policy` | No schema change (existing key/value table) | No. |
| `ConflictSideDto` / `ConflictDetailDto` | New DTOs only, additive | No (OpenAPI regen only). |
| Pre-launch commit-before-choose behavior | Code change in `SyncEngine`, not schema | No. |
| Notification-state record | In-memory/log only | No. |

Nothing here touches `SaveVersion`, `SaveVersionFile`, `Lease`, `AgentCommand`, or the archive
storage layout. The conflict model is extended, not replaced.
