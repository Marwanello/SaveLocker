# Implementation grouping — Storage: off-site copies and deduplication

Written 2026-10-22 on top of `plan.md`, then cut down to **four groups of similar size**: two for off-site copies,
two for deduplication. Off-site goes first: it protects data that already exists, while dedup rewrites how data is
stored, so a readable copy should be off the box before any conversion runs.

| Group | Contents | Status |
|---|---|---|
| 1 | Phases 1, 2, 3, 4 — rclone runner, setup wizard for every provider, off-site backups | ⏳ Not started |
| 2 | Phases 5, 6, 7 — version mirror, pause-and-ask, disaster recovery | ⏳ Not started — after version-history Group 2 (labels, planner) |
| 3 | Phase 8 + Phase 10's measurement — blob store for new versions | ⏳ Not started — after version-history Group 1 (trash) |
| 4 | Phase 9 + Phase 10's build — converting existing versions, chunking if measured worth it | ⏳ Not started — after Group 3 has run in a release |

## Groups

**Group 1 — set up off-site and copy the backups (large).** The image change, `RcloneRunner`, the wizard for every
provider, and the backup copy that's the first useful result. OAuth (Phase 3) is in this group so the wizard is
built and reviewed once with all its provider tiles, not reopened later. Its spike opens the group.
*Test:* the wizard against rclone's `local` backend and a MinIO container, then the maintainer's real Google
Drive in testenv (their sign-in, so a by-hand step). After each, a backup appears on the remote and *Test
connection* reports free space where the provider supports it.

**Group 2 — mirror the history, survive a full remote and a lost server (large).** The mirror (Phase 5) can't be
safe without the *Resolve* flow (Phase 6) that answers its cap and circuit breaker. Recovery (Phase 7) is the
reason the mirror exists, and is how its layout and `_index.json` are proven. Needs labels and the planner
(version-history Group 2) for the remote history setting.
*Test:* `run-offsite-tests.ps1` (layout, per-game skip, history setting, cap, simulated quota errors → each
*Resolve* option, circuit breaker). Then wipe a testenv server, recover it from the remote, and confirm adoption
deleted nothing. Also unzip `latest.zip` from Drive into a real game's folder and load it in the game.

**Group 3 — blob store for new versions (large).** Re-opens the Decision; add the dated note to [[Decisions]] here.
Needs trash (version-history Group 1), because trashed rows count as live for the sweep. Phase 10's
**measurement** comes along: it reads data this group's store already indexes, and its result decides Group 4's
size.
*Test:* `run-dedup-tests.ps1`: byte-identical content and an identical `ContentHash` for every new version, a crash
at each step of the write path, a concurrent upload during a sweep, a trash restore after a sweep, and *Verify
storage*. `run-delta-upload-tests` green.

**Group 4 — convert what's already there (large).** Only after Group 3 has run in a release on real data without a
verify failure, and with Group 2's mirror running for the maintainer's data. Then a conversion bug still has a
readable copy elsewhere. Chunking is built here only if Group 3's measurement passed. Otherwise it's recorded as
dropped, and this group is just the conversion (the popup, progress, pause on low disk, revert).
*Test:* convert a seeded 200-version store while a script pushes and pulls in parallel, with a forced verify
failure, low disk and a revert.

```
Group 1 (setup + backups)  →  Group 2 (mirror, pause/ask, recovery)  ← version-history Group 2
version-history Group 1 (trash)  →  Group 3 (blob store + measure)  →  release  →  Group 4 (convert + chunking?)
                                                        Group 2 running for real data ↗
```

## Re-evaluate before each new group

Before Group 4: confirm the off-site mirror is up to date for the maintainer's data and Group 3 has had no verify
failures in a release.
