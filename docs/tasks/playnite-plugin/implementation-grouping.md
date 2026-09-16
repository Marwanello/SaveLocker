# Implementation grouping — how to actually work through the Playnite plugin's 16 phases

Written 2026-09-14, same day the task moved out of `conflict-resolution-ui` to stand on its own;
regrouped the same day into fewer, larger groups after review. Same two-document split that task
already established: `plan.md` is the phase list with real dependencies; this file is the practical
execution plan on top of it — which phases share a session, in what order, and why — driven by the
same three things `conflict-resolution-ui`'s own grouping document weighs together:

1. **Real dependencies** (`plan.md`'s own diagram — some phases genuinely can't start before others).
2. **What this environment can actually verify.** This session runs on a real Windows box with
   Playnite already installed, unlike the cloud/remote sessions that made "needs Windows + Playnite"
   a real gate for the original Phase 13 scoping. That gate is gone here — every phase below is
   attemptable in this environment. What still varies is *how* a phase is verified: the agent-side
   track has an automated suite (`run-agent-tests` etc.); the plugin-side track does not, and is
   manual/hardware-checked every time.
3. **Session-cost precedent**, sized against this project's own history rather than guessed — see
   `plan.md`'s own "Size estimate" section for the full per-phase breakdown this grouping is built
   from (~7–9 sessions agent-side, ~11–15 plugin-side, ~18–24 total — unchanged by this regrouping,
   only the bucketing changed).

## Status (updated 2026-09-16 — Group 5/Phases 13-15+17 built, see `plan.md`)

| Group | Contents | Status |
|---|---|---|
| 1 | Phases 1–3 (launch gate, `SteamAppId`, pull-toggle move) | ✅ Done 2026-09-14 |
| 2 | Phases 4–7 (exit-push, new routes, self-update plumbing) | ✅ Done 2026-09-14 — Phase 7's self-updater is code-complete but unverified against a real package (`SaveLocker-Playnite` doesn't exist yet) |
| 3 | Phases 8–11 (scaffold, settings, core gate, matching) | ✅ Done 2026-09-15 — hardware-verified on a portable Playnite (Harmony theme); Fullscreen-mode popup and the WSL "lease held elsewhere" case were fixed late this session, re-confirmation on hardware still pending |
| 4 | Phase 12 (enroll/link popup) | ✅ Built 2026-09-15 — builds clean against the real Playnite SDK; NOT yet hardware-verified. Also picked up Tier 4 of `GameMatcher`'s matching chain (the link nudge), deferred from Group 3 since it needed this picker. See `SaveLocker-Playnite`'s own `docs/CONTEXT.md`/`docs/logs/2026-09-15_group-4-link-popup.md` |
| 5 | Phases 13–15 + 17 (status chip/buttons, self-update consumption, test infra, release CI) | ✅ Built 2026-09-16, on branch `playnite-plugin-group-5` — code-complete, builds clean, `dotnet test` 25/25; NOT yet hardware-verified inside a running Playnite, and no real tag has been pushed to exercise Phase 17's workflow. A small companion agent-side change (`GET /api/playnite-plugin`, main repo commit `31f8b9b` on `claude/group-5-playnite-plugin-3d3aae`) was verified live against a real scratch server + agent. See `SaveLocker-Playnite`'s own `docs/CONTEXT.md` |
| 6 | Phase 16 (add-on database submission) | ⏳ Not started |

## Groups

**Group 1 — agent-side, launch gate + matching prep. Medium, ~2–2.5 sessions.**
`Phase 1` (Windows launch-gate rewiring), `Phase 2` (`SteamAppId` population), and `Phase 3`
(`PullBeforeLaunchEnabled` moves server-side). Grouped together because all three are foundational,
low-to-medium-complexity agent-side changes with no Playnite dependency at all — Phase 1 and 3 already
touch the same method (`SyncEngine.PrepareLaunchAsync`) and the same call site (`TrayApp.cs`'s
`prepareLaunch` delegate), and Phase 2, while it touches a different surface (`GameScanner`'s
Steam-manifest parsing and a startup backfill path), is small enough and closely enough related in
purpose — strengthening the same matching/gate infrastructure — to ride in the same session rather
than earning a group of its own. Fully verifiable with `run-agent-tests` plus a manual HTTP call and a
real Steam-installed tracked game showing a resolved AppID after a rescan; no Playnite needed at all.
Worth doing early regardless of sequencing, since Phase 2 is what makes Phase 11's matching chain
actually strong on day one.

**Group 2 — agent-side, exit-push + new routes + plugin update plumbing. Medium-high, ~4.5–6 sessions.**
`Phase 4` (`PushAfterExitEnabled`), `Phase 5` (`post-exit-sync`, `candidates/lookup`,
`manifest/search`), `Phase 6` (`AgentPlatform.PlaynitePlugin` slot), and `Phase 7` (Windows
self-updater). Grouped as "everything else agent-side" — Phase 5's `post-exit-sync` route is the
direct consumer of Phase 4's toggle, so building them apart would mean Phase 5 either hard-codes
"always push" temporarily or waits on Phase 4 anyway; Phase 6 and 7 are exactly how Decky's own
equivalent pair shipped (a platform slot with nothing yet to fetch is dead weight, and a self-updater
with no slot to ask has nothing to check against), and both are close copies of `decky-plugin`'s
existing slot and `DeckyPlugin.cs`'s existing shape — lower risk than the session count suggests. No
Playnite required for any of it; independent of Group 1 and startable in either order.

**Agent-side subtotal: ~7–9 sessions**, matching `plan.md`'s own size estimate, now in two groups
instead of four.

---

**Group 3 — plugin-side, the spine: scaffold, settings, core gate, matching. High risk, ~5–6 sessions.**
`Phase 8` (scaffold + load), `Phase 9` (local API client + settings page), `Phase 10` (core pre-launch
gate — `OnGameStarting`/`Stopped`, `ActivateGlobalProgress`, the conflict dialog), and `Phase 11`
(automatic matching chain). This is the plugin's actual reason for existing, bundled into one group on
purpose: Phase 8/9 answer "does it load and can it talk to the agent," which only matters once Phase 10
proves the launch gate actually works end to end, and Phase 11's matching is what Phase 10's gate
needs to know *which* tracked game it's even gating — none of the four stands alone as a meaningfully
shippable slice without the others. **This is also the highest-uncertainty work in the whole plan** —
first-ever use of `.NET Framework 4.6.2`, the Playnite SDK, `Toolbox` packaging, and
`ActivateGlobalProgress`/native-dialog work in this environment, and the first phase whose manual
verification actually proves the whole feature's reason for existing (a real blocked launch on a real
conflict, resolved, correctly matched to the right tracked game). Budget slack here specifically —
every other group's estimate assumes the toolchain itself behaves, which this group is what actually
tests. Depends on Group 1 and 2's agent-side work already having landed, to verify against something
real rather than a stub.

**Group 4 — plugin-side, the enroll/link popup. Medium-high, ~2 sessions.**
`Phase 12` alone — the largest single UI surface in this plan (five states, three of which make
network calls: search, pick-existing, enroll-new). Kept separate from Group 3 rather than folded in,
since it's substantial enough on its own and depends on Group 3's matching chain having already
proven out what "no match found" actually looks like in practice, plus agent-side Group 2's two
lookup/search routes.

**Group 5 — plugin-side, status surface + self-update + test infra + release CI. Medium, ~4.5–6 sessions.**
`Phase 13` (status chip + push/pull/sync buttons via `GetGameViewControl`/`GetGameMenuItems`),
`Phase 14` (self-update consumption — checks + restart prompt), `Phase 15` (test infrastructure:
portable-Playnite `testenv` target, Windows `seed-test-conflict`, a stub-server test project), and
`Phase 17` (release CI workflow for `SaveLocker-Playnite`, added 2026-09-15). Grouped as "everything
that rounds out the plugin once the spine and the popup already exist" — the status chip's value is
mostly latent until Groups 3–4 exist (a chip that only ever says "not linked," with no popup to act on
it, teaches the player to ignore it), and the self-update/test-infra/release pieces are supporting work
that's more pleasant to build once there's a real, working plugin to test against, update, or release,
not a hard dependency of any of them. Phase 17 belongs here rather than with Group 6: it's genuine build
work with a concrete, verifiable output (a workflow file, a produced release), not a third-party review
process with an uncontrolled timeline — the exact distinction Group 6 draws for itself below.

**Group 6 — plugin-side, add-on database submission. Low effort, uncontrolled timeline, ~0.5–1 session.**
`Phase 16` alone, deliberately last and kept separate rather than folded into Group 5: writing the
manifest and opening the PR is genuinely small, but the review turnaround afterward is a third party's
timeline, not this project's, and nothing else here waits on it — Phase 7/14's self-update path is
what covers players in the meantime. Worth keeping visible as its own line rather than buried in a
polish group, precisely because "submit and don't wait on it" is a different kind of task from
building something.

**Plugin-side subtotal: ~12–16 sessions**, matching `plan.md`'s own size estimate (now including
Phase 17), still in four groups instead of seven.

## Recommended order and the MVP cut

```
Group 1 ── Group 2                                               agent-side, no Playnite needed
 (gate + SteamAppId +    (exit-push, new routes,                  ~7–9 sessions, either order
  pull-toggle move)       update plumbing)                        internally

                ↓ (Group 1/2 should land before Group 3 is worth verifying against)

Group 3 ────────── Group 4 ────────── Group 5                     plugin-side spine
 (scaffold, settings,     (enroll/link           (status chip/
  core gate, matching)     popup)                 buttons, self-
                                                   update, tests,
                                                   release CI)
                                                        │
                                                        └── Group 6 (submit to add-on DB, last,
                                                             don't wait on it)
```

**MVP — a genuinely shippable first slice: Group 1 and Group 3.** Roughly 7–8.5 sessions (2–2.5 +
5–6, matching `plan.md`'s own "Size estimate" recommendation), delivering the actual core value — a
safe pre-launch pull/block on Windows, with automatic matching — with no enrollment popup, no status
chip, and no self-updater yet. Groups 2, 4, 5, and 6 are real value on top, each independently useful,
each independently deferrable. Verify the MVP on real hardware before committing to the rest, the same
way Decky's own optional Phase 4/5 only got scoped after Phase 1–3 proved out live.

## Re-evaluate before each new group

This grouping assumes the environment and dependency picture as of 2026-09-14. If a session finds the
toolchain in Group 3 behaves differently than expected (the one real unknown here), re-check this
file's session-cost estimates against what actually happened before planning Group 4 onward — same
discipline `conflict-resolution-ui/implementation-grouping.md` applied to its own Phase 12 correction
and its own Group 6/7 split.
