# Implementation grouping — Playthroughs

Written 2026-10-22 on top of `plan.md`'s phases, then cut down to **three groups of similar size**, each ending in
something testable end to end:

1. **The server, complete**: tested by scripts against the API, no UI needed.
2. **One device, complete**: tested on one agent against that server.
3. **Every other surface**: tested across devices, which is what the testenv pass does anyway.

Sibling repos (`SaveLocker-Decky`, `SaveLocker-Playnite`) still get their own worktree and PR inside Group 3, as
`conflict-resolution-ui/implementation-grouping.md` learned (Groups 6/7). They share a group here because each is
small, and both are only checkable once the console and agent already show playthroughs.

| Group | Contents | Status |
|---|---|---|
| 1 | Phases 1, 2, 3 — server model, sync scoping, API + per-device selection | ⏳ Not started |
| 2 | Phases 4, 5, 6 — agent switch, local copy store, agent UI + CLI | ⏳ Not started |
| 3 | Phases 7, 8, 9, 10 — console, Decky, Playnite, delete to trash | ⏳ Not started — delete needs version-history Group 1 (trash) |

## Groups

**Group 1 — the server (large).** Phase 1's migration leaves readers on `Game.HeadVersionId`, which is only safe
because Phase 2 moves them over in the same PR. Phase 3's routes are what the tests drive. Release-safe on its own:
every game still has exactly one playthrough until something creates a second.
*Test:* the server half of `run-playthrough-tests.ps1`. That covers migrating a seeded database,
per-playthrough push/pull/conflict/lease/delta, fan-out only to followers, old-agent compatibility, and per-playthrough
retention. `run-delta-upload-tests`, `run-concurrency-tests` and `run-health-tests` are unchanged.

**Group 2 — one device (large).** The switch (Phase 4) is written together with the store it falls back on
(Phase 5), so "push failed mid-switch" and "offline switch" are designed once, not patched in later. The agent UI
and CLI (Phase 6) are how they're exercised.
*Test:* the agent half of `run-playthrough-tests.ps1` (online and offline switches, a refused switch while
running, a failed push mid-switch, store write-order, verify-on-restore, sweep grace), then the tray against a dev
server in the browser pane.

**Group 3 — every other surface (large, three PRs).** The console card and device switching (Phase 7), the Decky
chip and QAM line (Phase 8), the Playnite menu (Phase 9), and delete to trash (Phase 10, once version-history
Group 1 has shipped trash). If trash hasn't shipped yet, Phase 10 waits and the group closes without it.
*Test:* the testenv pass from `plan.md`: Windows tray + WSL agent + Deck, fork on Windows, switch the Deck from
Decky, switch Playnite from its menu, play two playthroughs at once, switch back offline.

```
Group 1 (server)  →  Group 2 (one device)  →  Group 3 (console, Decky, Playnite, delete)
                                                 ↑ delete needs version-history Group 1 (trash)
```

## Re-evaluate before each new group

Re-read `plan.md`'s Status table and Phase 2's checklist before starting. If Group 1 found a reader of
`Game.HeadVersionId` the checklist missed, add it there first.
