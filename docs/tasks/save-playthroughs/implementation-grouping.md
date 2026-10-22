# Implementation grouping — Playthroughs

Written 2026-10-22 on top of `plan.md`'s phases. Sequencing is driven by real dependencies (the server model before
anything that reads it), by what one session can verify, and by keeping each sibling repo (`SaveLocker-Decky`,
`SaveLocker-Playnite`) in a group of its own, as `conflict-resolution-ui/implementation-grouping.md` learned
(Groups 6/7).

| Group | Contents | Status |
|---|---|---|
| A | Phases 1, 2 | ⏳ Not started |
| B | Phases 3, 4 | ⏳ Not started |
| C | Phase 5 | ⏳ Not started |
| D | Phases 6, 7 | ⏳ Not started |
| E | Phase 8 (Decky repo) | ⏳ Not started |
| F | Phase 9 (Playnite repo) | ⏳ Not started |
| G | Phase 10 | ⏳ Not started — after version-history Group A (trash) |

## Groups

**Group A — server model and sync scoping (large, fully verifiable here).** Phases 1 and 2 together: the migration
alone leaves every reader still on `Game.HeadVersionId`, which is only safe because Phase 2 moves them over in the
same PR. Ships with `run-playthrough-tests.ps1`'s server half. A release here changes nothing visible: every game
has one playthrough.

**Group B — API, selection, and the agent switch (medium-large).** Phase 3's routes and Phase 4's
`SwitchPlaythroughAsync` are tested against each other. The CLI part of Phase 6 (three commands) comes along as the
test driver, so the switch is exercisable before any UI exists. Verified with two scratch agents against one dev
server.

**Group C — local copy store (medium).** Independent of B's UI and self-contained in `Agent.Core`. It slots into
step 2/3 of the switch, which Group B writes with a "no store" path that this group fills in. Unit tests for
write-order, verify-on-restore and sweep grace.

**Group D — agent UI + console (medium).** Phases 6 and 7 share the API types and the naming rules, so they're
reviewed together. Checked in the browser pane against a seeded two-playthrough game, then on the tray.

**Group E — Decky.** Its own worktree in `SaveLocker-Decky`. Hardware-verified on the Deck (gamepad-only).

**Group F — Playnite.** Its own worktree in `SaveLocker-Playnite`. Verified in the portable `Playnite-Test`
through `testenv.ps1`.

**Group G — delete to trash.** Small. It waits for version-history Group A to ship trash, then adds the delete
route and the console action.

```
Group A (1, 2)  →  Group B (3, 4 + CLI)  →  Group D (6, 7)  →  Group E (Decky)
                          ↘ Group C (5)  ↗                 ↘  Group F (Playnite)
version-history Group A (trash)  →  Group G (10)
```

testenv pass after Groups D–F, before the release.

## Re-evaluate before each new group

Re-read `plan.md`'s Status table and the Phase 2 checklist before starting a group. If Group A found a reader of
`Game.HeadVersionId` the checklist missed, add it there first.
