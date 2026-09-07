# Open questions

Things this pass could not settle from the repository, the vault, or external documentation.
Each is phrased as a specific question with the options considered, for the maintainer to decide
before or during implementation.

## 1. Should an auto-resolved conflict's losing version be protected from retention?

Today, `SyncService.IngestAsync`'s auto-resolve branch (`NewestWins`/`PreferMachine`) advances the
head with **no** `ConflictFlag` and **no** `Protected` flag on the version it displaced — only a
manual `keepBoth` resolve protects both sides. This design's pre-launch commit-before-choose
mechanism (`01-conflict-model-spec.md` §2) means auto-resolution now fires on every pre-launch check
under an auto-policy, not just on explicit pushes — so a losing version is discarded (eligible for
ordinary retention pruning) far more often than today.

- **Option A — leave it exactly as today.** Simplest; consistent with the existing, deliberate
  design of `NewestWins`/`PreferMachine` as "I don't want to be asked, ever" policies. A user who
  picked an auto-policy has already said the losing side doesn't matter to them.
- **Option B — protect every auto-resolved loser too, same as `keepBoth`.** Safer against surprise,
  at the cost of `RetainVersionsPerGame` no longer being a hard ceiling for a game under an
  auto-policy that diverges often (a Deck and a PC both played daily could accumulate many protected
  "loser" versions nobody ever prunes).
- **Option C — protect it, but only for a bounded window** (e.g. 7 days), then let ordinary retention
  reclaim it. Splits the difference; needs a new field and a sweep, more machinery than A or B.

**Recommendation if forced to pick: A**, since it's what the existing policies already promise and
changing it would be a behavior change for existing installs, not just new code — but this is
explicitly the maintainer's call, not assumed.

## 2. Should "block launch until resolved" be a real, shippable setting, or deferred entirely?

`03-platform-ux-flows.md` recommends `ProceedSyncPaused` as the default and names "block launch" as
an optional per-game/global opt-in for strict-permadeath-style players, without specifying where
that setting lives or whether it's worth building in phase 1.

- **Option A — build it now**, as a per-game boolean alongside `ConflictPolicy` (`Game.BlockLaunchOnConflict`, nullable-with-global-default, same pattern as `RetainVersions`).
- **Option B — defer it.** Nothing in the brief's invariants requires it (a paused-sync launch is
  already fully safe), and it adds a setting surface across every platform's settings UI (Decky
  settings page, Windows agent UI, Linux `agent-ui`) for a use case not confirmed to exist yet.

## 3. Which D-Bus client library, if desktop notifications (rung 3) are built at all?

No .NET D-Bus dependency exists in this codebase today. `Tmds.DBus` is the most commonly used option
in the .NET ecosystem for talking to `org.freedesktop.Notifications`, but this wasn't independently
evaluated against alternatives (raw `dbus-send`/`gdbus` shelling out, a smaller hand-rolled client
given SaveLocker only needs one method call) — worth a short spike before committing, and worth
weighing against just deferring rung 3 outright (per the phased plan, nothing depends on it).

## 4. Should a bystander machine (neither side of the conflict) be allowed to resolve it?

`02-resolution-api.md`'s `keepLocal` semantics handle a third machine resolving a conflict it wasn't
party to by treating `keepLocal: false` as "keep whichever side isn't this machine's own most recent
push" — but it's not clear a bystander resolving at all is a scenario worth supporting in v1 versus
restricting non-dashboard resolution to the two machines actually named in the conflict (with a
bystander directed to the dashboard, which already handles the fleet-wide view correctly today).
Affects whether the Deck chip/Playnite dialog need to handle "this conflict isn't about me" at all,
or can assume they're always shown a conflict where `MachineId` is themselves.

## 5. Is Playnite integration worth building against 10.x now, given Playnite 11 is an unknown, unreleased rewrite?

The SDK researched throughout this design is the current public 10.x line; Playnite 11 has no public
repository or documentation as of this research. Building Phase 4 now means real, working value for
every current Playnite user, at the cost of possibly needing a second integration pass whenever 11
ships and if its plugin model differs. Waiting means the Windows story stays at "no pre-launch gate
at all" for however long 11 takes to arrive with no announced timeline. This is a scheduling/risk-
tolerance call, not something the research could resolve either way.

## 6. What's the actual distribution plan for the enhanced Decky plugin?

This codebase's own history already found the *existing* plugin can't honestly be submitted to
Decky's official store (its PR template requires attesting AI wasn't used for a majority of the
code, and this plugin was largely AI-written — `CONTEXT.md`). This design adds a materially larger,
more safety-relevant feature (game-page conflict resolution) to that same plugin. Worth an explicit
decision on whether that changes the calculus (e.g., a human doing a full manual rewrite/review pass
specifically to qualify for store submission, given the feature's importance) or whether the
existing custom-store/direct-zip distribution stays acceptable indefinitely.

## 7. Should the "Why did this happen?" help article be rewritten for the new pre-launch-detected case?

The existing article (linked from the dashboard's conflict card today) presumably explains
push-time divergence. Once pre-launch commit-before-choose (model spec §2) makes conflicts appear at
launch time too, the explanation "you pushed while someone else's save was newer" needs a second,
equally clear framing: "we tried to sync before letting you play, and found your last local save and
the server's copy have both changed since you last synced." Content work, not architecture — flagged
so it isn't forgotten as a documentation-only gap once the code ships.

## 8. Does "Keep both" belong in the Deck's compact game-page modal, or only in the QAM/dashboard?

The dashboard offers "Keep both" as a distinct button per side. The Deck modal
(`03-platform-ux-flows.md`) proposes collapsing it into a toggle to save space, but it's an open
question whether it should be offered in the tight modal at all — a Deck user who wants "keep both"
may already be well served by pausing there and switching to the QAM panel or dashboard, which have
more room to explain what "keep both, protected from pruning" actually means before they commit to
it. Affects modal layout, not the API (the API supports it either way).
