# Phased implementation plan

Ordered; each phase is independently shippable (leaves the system in a coherent, working state, per
this codebase's own house rule of "one phase per session, verify it, stop" — see
`logs/2026-08-15_decky-plugin.md` for the precedent this plan deliberately follows). Core daemon work
first, platform frontends after, matching the brief's own instruction.

## Phase 0 — Data model + core daemon: the Resolution API

**No platform-specific code.** Ships alone, fully covered by the existing suite style
(`tests/run-*-tests.ps1`), with no plugin/extension in existence yet — exactly how Phases 1–2 of the
launch-options work shipped.

1. Migration: `Game.ConflictPolicy` → nullable; new `AppSetting` key `conflict.default_policy`
   (`01-conflict-model-spec.md` §1).
2. New DTOs: `ConflictSideDto`, `ConflictDetailDto`, `ConflictResolveResult` (§3 of the model spec,
   Layer 1 of the Resolution API).
3. `ResolveConflictAsync` gains the optional `resolvedVia` parameter (default `null`, so the existing
   dashboard call site needs no change) and returns `headVersionId` alongside `(ok, error)`.
4. New agent-group server routes: `GET /api/agent/conflicts`, `GET /api/agent/conflicts/{id}`,
   `POST /api/agent/conflicts/{id}/resolve`, `GET`/`POST /api/agent/games/{id}/conflict-policy`.
5. Regenerate `openapi.json` + `web/src/api-types.ts` (dashboard is a consumer of the unchanged
   admin routes, but the schema file is shared — CI's `--check` catches drift either way).

**Verify:** new server-side tests mirroring `run-server-bugbounty-tests.ps1`'s existing conflict
coverage — a second machine's `POST /api/agent/conflicts/{id}/resolve` returns `409` after the first
resolves it; a rewind attempt still refuses (existing guard, exercised through the new route); policy
fallback (`Game.ConflictPolicy == null` reads the `AppSetting` default) covered directly.

## Phase 1 — Agent-local API + the commit-before-choose launch gate

Still no plugin/extension. This is where the actual safety behavior changes land.

1. `AgentApiServer` gains Layer 2 (`02-resolution-api.md`): `GET/POST /api/conflicts[/…]`,
   `GET/POST /api/games/{id}/conflict-policy`, proxying to Phase 0's server routes with the
   local-cache-on-`503` behavior.
2. `SyncEngine.PrepareLaunchAsync` (Resolution API §Layer 3) — the commit-before-choose logic
   (model spec §2), wired into `ProtonRun.ExecuteAsync` in place of the bare `OnGameLaunchAsync`
   call it makes today.
3. `ProtonRun.ExecuteAsync` respects `LaunchDecision.Blocked` (refuses to start the child process,
   logs why, exits non-zero) — the one behavior change to an already-shipping code path.
4. New CLI commands: `savelocker conflicts`, `savelocker resolve <game> --keep-local|--keep-remote
   [--keep-both]` (`AgentCli.cs`, both hosts — this is genuinely shared surface, unlike
   `launch-options`, since resolving a conflict is meaningful on Windows too).
5. `doctor` gains a line per open conflict.

**Verify:** extend `run-linux-tests.sh`'s existing fake-game harness with a genuine conflict fixture
(two machines pushing divergent content against the same fake server) exercised through the new CLI
commands and the launch wrapper; confirm `Blocked` actually prevents the child process from starting
(the harness already has the pattern for "does the wrapper refuse," from the WA-01 pull-blocked
tests). This phase can ship and be genuinely useful (headless rungs 5 and 7 of the escalation ladder)
with zero platform-specific frontend work — worth calling out, since it's the first point the whole
feature is "real" for a terminal-only user.

## Phase 2 — Linux GUI + headless escalation ladder (rungs 1–2, 4)

Depends on Phase 1. Independently shippable from Phases 3/4 below (different code, different
platform surface).

1. `savelocker ui` (`Ui/UiApp.cs`) gains a conflict screen (new `Screen` enum value) — the chip-
   equivalent in the existing status screen, and a chooser reachable from it. Reuses the existing
   ImGui widget/nav machinery with no new rendering technology.
2. `agent-ui` gains a conflicts page (both hosts — Windows tray users benefit too, not just Linux;
   the local web chooser and the Windows in-app prompt can share this one React page rather than
   the tray needing its own).
3. Environment-capability detection (rung 1: `$WAYLAND_DISPLAY`, session D-Bus presence, TTY,
   systemd-unit-vs-interactive) — a small, pure, independently-testable module.

**Verify:** existing `tests/linux/run-ui-wslg.sh` pattern extended to drive the new screen under
WSLg; `agent-ui` conflicts page covered the same way `GameDetail.tsx`'s conflict card already is,
adapted for the new one-call `ConflictDetailDto` shape.

## Phase 3 — Windows in-app prompts (bulk queue) and the Windows chooser dialog

Depends on Phase 1 only, not Phase 2 (different host entirely) — **can run in parallel with Phase
2**.

1. Tray/agent UI gains the single-conflict chooser dialog and the queue-with-"apply to all
   remaining" flow (`03-platform-ux-flows.md` → Bulk-operation conflict presentation), wired into
   the four existing trigger points (`Sync All`, per-game Force Pull/Push, `Sync now`).
2. No pre-launch gate yet on Windows — that's Phase 4 (Playnite), and Windows already has none today,
   so this phase does not regress anything by shipping without it.

**Verify:** a scripted multi-conflict scenario (three games, three divergent conflicts) driving
`Sync All` through the queue UI, confirming "apply to all remaining" resolves the other two via the
same API calls Phase 0 already tested server-side.

## Phase 4 — Playnite extension

Depends on Phase 1 (the local `:5178` API it calls) but is otherwise a fully separate codebase/
project — **can run in parallel with Phases 2 and 3**, and is the natural point to stop and ship if
resourcing runs short, since Phases 0–3 already deliver a complete, working feature on every other
surface.

1. New project (outside `SaveLocker.sln`, `.NET Framework 4.6.2`, `PlayniteSDK` NuGet) —
   `GenericPlugin` with `OnGameStarting`/`OnGameStartupCancelled`/`OnGameStopped`.
2. `OnGameStarting` calls `PrepareLaunchAsync` via the local API (HTTP, same pattern as Decky's
   Python backend); sets `CancelStartup` on `Blocked`, using `UIDispatcher.Invoke` for any WPF
   dialog it shows.
3. `OnGameStopped` triggers a push, as a *belated* trigger alongside — not instead of — the existing
   `Watchers.cs` exit-push, given the documented reliability caveat (`03-platform-ux-flows.md`).
4. Package as `.pext`, submit to `JosefNemec/PlayniteAddonDatabase` once verified on hardware —
   mirrors this codebase's own existing "don't claim done until the hardware pass" discipline.

**Verify:** hardware-only, following this codebase's existing pattern for Decky (`logs/2026-08-15_decky-plugin.md`'s "Status before the plugin was loaded" section as the template for what a
verification write-up here should look like) — a real Playnite install, a real conflict, confirming
`CancelStartup` actually blocks the launch and the plugin degrades gracefully with the agent not
running.

## Phase 5 — Decky game-page chip + modal

Depends on Phase 1 (the local `:5178` API) and is independent of Phases 2–4 — **can run in parallel
with any of them**, in the separate `SaveLocker-Decky` repository, following that repo's own release
process.

1. Chip states + the per-game `sync-summary` composite endpoint (`03-platform-ux-flows.md`) —
   agent-side addition, ships in Phase 1/2's `agent-ui`/local-API work if convenient, or here.
2. QAM "Conflicts" row (the guaranteed fallback) — build and verify **first**, since it needs no
   route-patch feasibility to be resolved and delivers the whole feature to the Deck on its own.
3. Game-page route patch + `showModal`/`ConfirmModal` chooser — build **after** step 2 ships and is
   verified, exactly mirroring how this codebase always treats a Decky-route-patch-dependent feature
   as the riskier, optional layer over a QAM-based baseline that already works.
4. Per-game auto-resolution selector on the plugin's settings page, reading/writing the same
   `ConflictPolicy` enum via the Layer 2 proxy — no new option set, per the brief's own constraint.

**Verify:** hardware-only, same discipline as Phase 4 — this codebase's history is explicit that a
Decky feature "compiles" is not "works" (`logs/2026-08-15_decky-plugin.md`: "Do not treat this phase
as done until the hardware checks below pass").

## Phase 6 — Optional: out-of-band notification (rung 6) and per-game "block launch" opt-in

Deferred, genuinely optional, depends only on Phase 0 (server-side webhook fires off the same
`ConflictFlag` creation/escalation events already computed). Not required for any invariant in this
design — see the risk register and open questions for why it's last.

---

## What can run in parallel, summarized

```
Phase 0 (server) ── Phase 1 (agent-local API + launch gate)
                          │
             ┌────────────┼────────────┬──────────────┐
             ▼            ▼             ▼              ▼
        Phase 2        Phase 3      Phase 4         Phase 5
      (Linux GUI/      (Windows     (Playnite,      (Decky,
       headless)        in-app)      separate        separate
                                     repo/project)    repo)
                                            │
                                     Phase 6 (optional, after Phase 0)
```

Phases 2–5 share no code with each other and touch no common files once Phase 1's API surface is
frozen — a real justification for running them concurrently, not just a scheduling convenience.
