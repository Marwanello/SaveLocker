# Implementation grouping — Version history upgrades

Written 2026-10-22 on top of `plan.md`. Each phase is small and mostly independent. Sessions are grouped by shared
code (`PruneVersionsAsync`, the Versions cards), and ordered by the cross-task dependency on
[[tasks/save-playthroughs/implementation-grouping|Playthroughs]].

| Group | Contents | Status |
|---|---|---|
| A | Phase 1 (trash) | ⏳ Not started — can go before Playthroughs |
| B | Phases 2, 3 | ⏳ Not started — after Playthroughs Group A |
| C | Phase 4 | ⏳ Not started |
| D | Phase 5 | ⏳ Not started — after Playthroughs Group B |

## Groups

**Group A — trash (medium, fully verifiable here).** No playthrough dependency. It changes the delete path that
every later prune uses, so it lands first and on its own. Playthroughs Group G waits on it.

**Group B — labels + planner + preview (medium).** Both touch the Versions cards and `PruneVersionsAsync`, which
the planner rewrites as "plan, then trash", so they share a review. Must come after Playthroughs Group A so the
planner is written per playthrough from day one.

**Group C — thinning (small-medium).** Only a new policy inside Group B's planner, plus the console control.
Mostly unit tests.

**Group D — single-file restore (medium).** Server route, agent engine, both UIs and the CLI. Needs Playthroughs
Group B (the switch's guards and the local store it reuses for undo).

```
Group A (trash)  ──────────────►  Playthroughs Group G
Playthroughs A  →  Group B (2, 3)  →  Group C (4)
Playthroughs B  →  Group D (5)
```

## Re-evaluate before each new group

Check Playthroughs' Status table: if its Group A slipped, Group B here waits too. Writing the planner against a
single head and then reworking it is exactly what this ordering avoids.
