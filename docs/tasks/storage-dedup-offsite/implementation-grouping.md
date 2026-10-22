# Implementation grouping — Storage: off-site copies and deduplication

Written 2026-10-22 on top of `plan.md`. Two independent tracks: **off-site** (Phases 1–7) and **dedup**
(Phases 8–10). Off-site goes first: it protects data that already exists, while dedup rewrites how data is stored,
so a working off-site copy should be in place before any conversion runs.

| Group | Contents | Status |
|---|---|---|
| A | Phases 1, 2 | ⏳ Not started |
| B | Phase 3 (spike, then build) | ⏳ Not started |
| C | Phase 4 | ⏳ Not started |
| D | Phases 5, 6 | ⏳ Not started — after version-history Group B (labels, planner) |
| E | Phase 7 | ⏳ Not started |
| F | Phase 8 | ⏳ Not started — after version-history Group A (trash) |
| G | Phase 9 | ⏳ Not started — after Group F has shipped in a release |
| H | Phase 10 | ⏳ Not started — measurement decides |

## Groups

**Group A — rclone runner + wizard for key/password providers (medium-large).** The image change, `RcloneRunner`,
encrypted remote storage and the wizard with *Test connection*, verified against rclone's `local` backend and a
MinIO container. Nothing is copied yet. That lets the wizard's UX be reviewed on its own.

**Group B — OAuth (spike + medium).** The spike's write-up decides the flow. Verified with the maintainer's real
Google Drive in testenv (needs their sign-in, so it's a by-hand step).

**Group C — off-site backups (small).** The first useful result: a lost disk no longer loses the backups.

**Group D — version mirror + pause-and-ask (large).** Shipped together because the cap and the circuit breaker
can't be safe without the *Resolve* flow that answers them. Needs version-history Group B for labels and the
planner.

**Group E — disaster recovery (medium).** Restore from the remote on a fresh server, then adopt. Tested by wiping
a testenv server and recovering it.

**Group F — blob store for new versions (large).** Re-opens the Decision; add the dated note to [[Decisions]] in
this group. Needs trash (version-history Group A) because trashed rows are live for the sweep.

**Group G — conversion of existing versions (medium-large).** Only after Group F has run in a release on real data
without a verify failure. The popup, progress and pause-on-low-disk go here.

**Group H — chunking.** Starts with the measurement. Dropped (and recorded) if the gain is small.

```
Group A → Group B (OAuth)
       ↘ Group C (backups) → Group D (mirror + pause/ask) → Group E (recovery)
version-history A (trash) → Group F (blobs) → release → Group G (convert) → Group H (chunking?)
version-history B (labels, planner) → Group D
```

## Re-evaluate before each new group

Before Group G: confirm the off-site mirror (Group D) is running for the maintainer's data, so a conversion bug
would still have a readable copy elsewhere.
