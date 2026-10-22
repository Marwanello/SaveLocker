# Implementation grouping — Version history upgrades

Written 2026-10-22 on top of `plan.md`, then cut down to **two groups of similar size**. They're split where the
dependency on [[tasks/save-playthroughs/implementation-grouping|Playthroughs]] falls: Group 1 needs nothing from it,
and Group 2 needs its server and agent work.

| Group | Contents | Status |
|---|---|---|
| 1 | Phases 1, 2 — trash, labels | ⏳ Not started — can go any time, before Playthroughs if wanted |
| 2 | Phases 3, 4, 5 — retention planner + preview, thinning, single-file restore | ⏳ Not started — after Playthroughs Groups 1 and 2 |

## Groups

**Group 1 — what happens to a version (medium-large).** Trash changes the delete path every later prune uses.
Labels add the second "never prune this" rule beside `Protected`. Both touch the version row, the Versions cards
and `PruneVersionsAsync`'s skip list, so they share a review. Neither has a playthrough dependency (a label belongs
to a version). Playthroughs Group 3's delete waits on this group.
*Test:* prune → trash → restore → purge round trip, labelled versions surviving a prune, *Empty trash*, and the
trash size on health. `run-delta-upload-tests` unchanged.

**Group 2 — which versions are kept, and getting one file back (medium-large).**
- **The planner** (Phase 3) is the function the previews and thinning (Phase 4) are built on, so they come
  together. Both need Playthroughs Group 1 so they're written per playthrough from day one.
- **Single-file restore** (Phase 5) needs Playthroughs Group 2 (the pull guards and the local store it uses for
  undo). It's in this group so both groups are a similar size, and it's tested in the same pass.

*Test:* unit tests for the planner (gaps of weeks, a minute with many pushes, DST, a week crossing a year boundary,
pinned versions beyond the cap). The preview must equal the prune on a seeded 90-day history. A single-file restore
must be refused while the game runs and from outside the root. Then a testenv pass: thin from the console, restore
one slot file to the Windows tray from the console, and undo it.

```
Group 1 (trash, labels)  ──────────────►  Playthroughs Group 3 (delete)
Playthroughs Groups 1, 2  →  Group 2 (planner, thinning, file restore)
```

## Re-evaluate before each new group

Before Group 2, check Playthroughs' Status table. If its Group 2 slipped, Phases 3–4 can still go ahead and
Phase 5 waits.
