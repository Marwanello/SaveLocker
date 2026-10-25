# Task: Version history upgrades

Planned 2026-10-22 from [[tasks/version-history-upgrades/summary|the summary]] with the maintainer's answers (end of
this file). **Second of three linked tasks:** after [[tasks/save-playthroughs/plan|Playthroughs]], before
[[tasks/storage-dedup-offsite/plan|Storage]]. Everything here works **per playthrough**: retention, previews and
labels act on one line of versions at a time. One exception: **trash** (Phase 1) has no playthrough dependency and
ships first, because Playthroughs' delete (its Phase 10) needs it.

`plan.md` lists the phases. [[tasks/version-history-upgrades/implementation-grouping|implementation-grouping.md]]
groups them into sessions.

## Status

| Phase | Status |
|---|---|
| 1 — Trash before the hard delete | ⏳ Not started |
| 2 — Labels (imply Protect) | ⏳ Not started |
| 3 — Retention planner + prune preview | ⏳ Not started |
| 4 — Age-based thinning (presets + custom) | ⏳ Not started |
| 5 — Single-file restore (agent UI, console, CLI) | ⏳ Not started |
| testenv pass | ⏳ Not run |

## Design (decided)

Every design question was settled on clickable mockups of the real apps (2026-10-22 to 2026-10-24). The phases
below describe only the chosen designs. In the console, a game's versions live in the **Saves** card (one tab per
playthrough, Playthroughs Phase 7), which replaces the Versions card.

| Feature | Where | Phase | Chosen |
|---|---|---|---|
| Trash | Console › Storage | 1 | **A Trash card**, always last on the Storage page (the Backups tab, renamed) |
| Name a version | Agent Versions + console Saves | 2 | **A name dialog with suggestions**; the console's rows get a *Name* column with a pencil |
| How much to keep | Console game page + Configuration | 3, 4 | **A timeline of kept / removed** behind *Keep history…* on the Saves card; the server-wide default in Configuration |
| Restore files | Agent + console version files | 5 | **Tick several files or folders**; in the console it makes a new save (**Restore and create save**) |

- **Backups becomes Storage.** The console's Backups tab is renamed *Storage* and holds everything about where the
  data lives, in this order: backups, *Space used* (the server's data folder by kind, and each off-site remote's
  use; storage-dedup-offsite), *Off-site copies*, and **Trash last**. Storage use is shown there only, not on
  Configuration's Server card.
- **Restore files in the console creates a save** rather than sending files to one device: every device that syncs
  the game pulls it, and the old latest stays in history.

Mockups: every pick on at once <https://claude.ai/artifact/R3BrsWpzSicoRt4xqqVDtJ>; every variant that was compared
<https://claude.ai/artifact/8VkXiktxDYXL929DVrj4qo> (source in `agent-ui/mockup-features/` and
`web/mockup-features/`).

---

## What already exists (don't re-derive)

- `SyncService.PruneVersionsAsync` keeps the newest `Game.RetainVersions ?? _retainPerGame` (default 10). It skips
  the head, open-conflict versions and `Protected`, then deletes rows **first** and archives **second**
  (`ReleaseArchivesAsync`, which audits an orphan as `archive.orphaned`). It runs after each upload, on conflict
  resolution and from *Prune now* (`POST /games/{id}/prune`).
- `DeleteVersionAsync` refuses the head and open-conflict versions. `SetVersionProtectedAsync` toggles `Protected`.
- `ArchiveStore` keeps `{root}/{gameId}/{versionId}.zip`, `.incoming/` staging and `SweepIncoming`.
  `LeaseSweeperService` / `BackupScheduler` are the background-service patterns.
- The console's Versions card (`web/src/components/game/VersionsCard.tsx`) and `VersionFiles.tsx` show each
  version's changes and full file tree (`GET /games/{id}/versions/{versionId}/folders`, `.../changes`). The agent's
  `VersionsCard.tsx` and `SaveFilesCard.tsx` do the same on the device.
- Pull guards: game not running, `SavePathGuard`, no symlink traversal, the empty-save hold, keep-excluded-files.
- A delta base is always the current head (of the playthrough, after Playthroughs Phase 2), and heads are never
  pruned, so retention can't break a delta in progress. `run-delta-upload-tests` keeps that true.

---

## Phase 1 — Trash before the hard delete

1. `SaveVersion.DeletedAt?` + `DeletedReason` (`pruned` / `deleted` / `playthrough-deleted`). An EF global query
   filter hides trashed rows everywhere. The few places that must see them (trash list, restore, purge) use
   `IgnoreQueryFilters()`.
2. **Move, don't delete:** pruning and manual delete set `DeletedAt` and move the archive to
   `{root}/.trash/{gameId}/{versionId}.zip`. The order stays row first, file second: a crash between them leaves a
   trashed row whose archive is still in the live folder, and restore and purge both look in both places.
3. **Purge:** a `TrashSweeper` (hourly, the `LeaseSweeperService` pattern) hard-deletes rows and archives older
   than `Storage:TrashDays`. That's a console setting, 1–90, default **14**. A purge audits a count and the bytes
   freed.
4. **Restore:** brings the row back, moves the archive back, and refuses if its playthrough is gone (unless the
   playthrough is restored with it, Playthroughs Phase 10). A restored version is never made head automatically.
   *Set as Latest* stays a separate, explicit step.
5. **Console:** a **Trash** card, last on the **Storage** page (the Backups tab renamed): list (game,
   playthrough, time, size, reason, days left), Restore, *Empty trash* (confirmation naming the count and size), the
   *Keep deleted versions for* setting, and the trash's size in *Space used*. The Saves card says "Deleted
   versions go to trash for 14 days".
6. Health: trash size counts towards disk use. A disk-space warning names it as the first thing to free.
7. Server backups never include trash.

## Phase 2 — Labels (imply Protect)

1. `SaveVersion.Label?` (1–80 chars, trimmed). A labelled version is **never pruned**, whatever `Protected` says.
   Clearing the label leaves `Protected` as it was set.
2. Named from a dialog with suggestions: the console's Saves card versions table gets a *Name*
   column whose pencil opens it; the agent's rows open it from "Name…" (agent-key route
   `PUT /api/agent/games/{id}/versions/{versionId}/label`). Shown as a chip on every version line, conflict card
   and *Set as Latest* dialog. Audit `version.label` with old and new.
3. CLI: `label <game> <version> "Before the final boss"` / `--clear`.
4. The off-site mirror (storage task) always copies labelled versions, and names their zips with the label.

## Phase 3 — Retention planner + prune preview

1. Extract the decision into a **pure** `RetentionPlanner.Plan(versions, policy, now, pinned) → (keep, delete,
   reason per version)`. `pinned` = head + open-conflict + protected + labelled. `PruneVersionsAsync` becomes "plan,
   then trash the deletes", so preview and prune can never disagree.
2. `GET /games/{id}/prune/preview?playthroughId=&mode=&…` returns what would go per playthrough, with the bytes
   freed and why each is kept ("labelled", "newest of 2026-10-14", "within the last 3 days").
3. Console: the retention setting shows the preview **before** saving a change ("This removes 14 versions, 3.2 GB,
   to trash"), and *Prune now* shows it as its confirmation.
4. Unit tests in `tests/SaveLocker.Agent.Tests` (it already references the server project).

## Phase 4 — Age-based thinning (presets + custom)

1. `Game.RetentionMode` (`Count` / `Thinning`, null = the global default) + `ThinningJson`. The global default mode
   and values are on the Config page.
2. **Rule:** keep everything newer than **A** days; then the newest version of each calendar day for **D** days;
   then the newest of each ISO week for **W** weeks (0 = forever); delete the rest. The count, if set, still caps the
   total ("and never more than N"). Pinned versions are kept outside the cap. Days and weeks use the server's
   configured time zone (the compose file's `TZ`), so "one per day" matches the player's day.
3. **Presets**, plus *Custom*, which exposes the three numbers:

   | Preset | All for | Daily for | Weekly for |
   |---|---|---|---|
   | Short | 2 days | 14 days | 8 weeks |
   | Balanced | 3 days | 30 days | 26 weeks |
   | Long | 7 days | 90 days | forever |

4. The console's retention control: **Keep history…** on a game's Saves card opens
   it: *Use the default* or *Its own setting*; then *Count* or *Thinning* → preset or custom, with Phase 3's preview
   live under it and the timeline of which versions survive. The server-wide default is a *History to keep* row in
   Configuration › Defaults & maintenance, with the same planner behind *Change…*.
5. Planner tests: gaps of weeks, many pushes in one minute, DST changes, a week split across a month or year
   boundary, pinned versions beyond the cap, an empty playthrough.

## Phase 5 — Single-file restore (agent UI, console, CLI)

1. **Server:** agent-key `GET /api/agent/games/{id}/versions/{versionId}/file?key=&path=` streams one entry from the
   version (any playthrough of the game), with its SHA-256. Admin `GET` of the same for the console's *Download this
   file*.
2. **Agent:** `SyncEngine.RestoreFileAsync(game, versionId, key, path)`. Same guards as a pull: game not running,
   `SavePathGuard`, no symlink traversal, the path must resolve inside that folder key's root. Write to temp, verify
   the hash, then replace. The previous file goes into the local copy store (Playthroughs Phase 5) when that mode
   keeps copies, so the restore itself can be undone. The head is unchanged.
3. **Agent UI:** tick files or folders in the version file tree, then *Restore N files*, with a
   dialog that names the source version
   (and playthrough, if it isn't the Selected one) and says "Your save will then differ from the latest; the next
   push makes a new version."
4. **Console:** tick files or folders in `VersionFiles.tsx` (opened from a version's chevron in the Saves card), then **Restore and create save**.
   The server makes a new version of that playthrough: the head's files, with the ticked ones taken from the old
   version (each checked against its SHA-256), labelled "Restored N files from <date>". It becomes the head, and
   every device that syncs the game pulls it like any new save (the closed-game check included). The old head stays
   in history, so *Set as Latest* undoes it. Admin `POST /api/admin/games/{id}/versions/{versionId}/restore-files`
   `{ paths, playthroughId }`, audited. *Download this file* goes beside it.
5. **Folders:** ticking a folder ticks every file under it, sub-folders included (half-ticked when only
   some are). Files go back at the same path relative to the save folder (or extra folder key), so a folder restores
   the same on every device. A folder the target doesn't have is created. Each picked file is *replaced*, *added*
   or *already the same* (hash match, skipped), and the dialog lists that per file before anything runs. Files the
   target has in a **fully ticked** folder that the old version didn't are left alone by default; *Make the ticked
   folders exactly as they were* removes them too (into the undo copy on the agent, left out of the new save in the
   console). Files outside the ticked ones are never touched.
6. **CLI:** `restore-file <game> <version> <path> [--folder <key>]` ([[CLI Reference]] article); a path ending in
   `/` restores that folder, `--exact` makes it exactly as it was.

---

## Verify

- New suite sections in `run-playthrough-tests.ps1` (shared with Playthroughs) or a new `run-version-history-tests.ps1`:
  trash round trip (prune → restore → purge), labels survive prune, preview equals prune, thinning across a seeded
  90-day history, single-file restore refused while running and from outside the root.
- `run-delta-upload-tests` and `run-health-tests` unchanged.
- testenv pass: label a version on the Deck, prune with thinning from the console, restore one slot file to the
  Windows tray from the console.

## Answers settled with the maintainer (2026-10-22)

- **Thinning:** a mode picker with presets plus a custom option. The count stays as an optional cap.
- **Labels imply Protect.** Clearing a label leaves Protect as it was.
- **Single-file restore:** agent UI + console + CLI. The console's restore creates a new save that every device
  pulls (changed from "queued to a chosen device" in review, 2026-10-23).
- **Trash:** 14 days by default, a console setting, size shown, *Empty trash* with confirmation.
- **Ordering:** after Playthroughs, except trash, which goes first.
