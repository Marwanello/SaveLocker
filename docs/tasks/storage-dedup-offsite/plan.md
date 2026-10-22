# Task: Storage — off-site copies with rclone, then deduplication

Planned 2026-10-22 from [[tasks/storage-dedup-offsite/summary|the summary]] with the maintainer's answers (end of
this file). **Third of three linked tasks:** after [[tasks/save-playthroughs/plan|Playthroughs]] and
[[tasks/version-history-upgrades/plan|Version history upgrades]]. The mirror's layout is per playthrough, it always
copies labelled versions, and deduplication's cleanup counts trashed versions as live.

**This re-opens a [[Decisions]] entry.** *Per-file delta upload reconstructs by copy-forward, not content-addressable
storage* deferred deduplication "if storage cost ever becomes the complaint". On 2026-10-22 the maintainer chose to
build it. Add a dated note under that entry when Phase 8 starts. Data stays on the server's own disk: **rclone is
for off-site copies only**, never the primary store.

`plan.md` lists the phases. [[tasks/storage-dedup-offsite/implementation-grouping|implementation-grouping.md]] groups
them.

## Status

| Phase | Status |
|---|---|
| 1 — rclone in the server image + a job runner | ⏳ Not started |
| 2 — Off-site setup wizard (S3-compatible, SFTP, SMB, WebDAV, folder) | ⏳ Not started |
| 3 — OAuth providers (Google Drive, OneDrive, Dropbox) | ⏳ Not started — starts with a spike |
| 4 — Off-site server backups | ⏳ Not started |
| 5 — Version mirror: readable layout, its own history setting, size cap | ⏳ Not started |
| 6 — Remote full, expired sign-in and other failures: pause and ask | ⏳ Not started |
| 7 — Disaster recovery from the off-site copy | ⏳ Not started |
| 8 — Deduplicated blob store for new versions | ⏳ Not started |
| 9 — Converting existing versions (automatic, verified, with progress) | ⏳ Not started |
| 10 — Chunking for large single-file saves | ⏳ Not started — measurement decides |
| testenv pass | ⏳ Not run |

## Mockups

Clickable mockups, three variants per feature (rebuilt on the real UI 2026-10-22, revised from the maintainer's review 2026-10-23): <https://claude.ai/artifact/MCmZGmK6Bacjhm7zhgzmw3>.
The page lists this task's features and opens each in the agent, the console, or the Decky and Playnite page. Each
mockup has a strip at the top to pick the feature and variant, with notes, a “Mock only” switch for the failure cases,
and my pick.

The agent and console mockups are **the real apps** with a mock backend and the planned features built into copies
of the real components. Source: `agent-ui/mockup-features/` and `web/mockup-features/` (each has `overrides/` = copied
real components plus the feature, `features/` = the new pieces, `store.ts` = the feature list and variants). Run one
with `npx vite --config mockup-features/vite.config.ts` in `agent-ui/` or `web/`. Decky and Playnite can't run
outside Steam and Playnite, so those two screens are rebuilt by hand and are close, not exact. *Proposed* is my pick;
*Decision* is the maintainer's, and the build follows it.

| Feature | Where | Phases | A | B | C | Proposed | Decision |
|---|---|---|---|---|---|---|---|
| Set up off-site | Storage page | 1–3 | Wizard dialog | One long card | Provider tiles + summary | A | ✅ **A** (2026-10-23); then a tile per remote and *Add another remote* |
| Off-site status | Storage page | 4, 5 | Compact card + dialogs | Dashboard numbers + games table | Activity log with tabs | A | ✅ **A** (2026-10-23), one per remote; *What's on the remote* is a folder tree |
| Remote full / pause and ask | Every page | 6 | Banner + Resolve dialog | Banner + panel | Make-room card | A | ✅ **A** (2026-10-23) |
| Restore a lost server | First run / Storage / sign-in | 7 | First-run screen | On Storage | Link on sign-in | A | ✅ **A and B** (2026-10-23); B searches the remote already set up |
| Converting to save space | Storage page | 9 | Popup + progress card | Notice + per-game table | Stacked storage bar | A | ✅ **B with A's big progress bar** (2026-10-23), once after upgrading, per-game rows folded |

### From the review (2026-10-23)

- **One Storage tab.** The console's Backups tab is renamed **Storage** (shared with version-history-upgrades) and
  holds, in order: backups, the conversion's progress while it runs, **Space used**, **Off-site copies**, and Trash
  last. *Space used* is the storage-management card: the server's data folder split into save versions, server
  backups, trash and database, against the disk's free space; each remote's use against its cap and its free space;
  and the biggest games (folded). Off-site setup and status move here from Configuration.
- **More than one remote.** rclone's config holds any number of named remotes, and nothing stops SaveLocker running
  one copy job per remote, so the setup wizard can run again from **Add another remote**. Each remote has its own
  tile, status, history setting, size cap, games list, *Run now* / *Pause* / *Remove*, and its own ownership records
  (Phase 5). Jobs run one after another, never two at once against the server's disk.
- **What's on the remote** is a real folder tree (folders expand, files show date and size, *Download*), not a text
  listing.
- **Restore a lost server:** both the first-run screen (A) and *Restore from off-site…* on Storage (B). B lists the
  backups on the remote this server is already set up with at once, with *Look somewhere else* for another.
- **Converting:** shown once, on the first start after upgrading to the version that brings it (a notice), then a
  banner on every page while it runs and a big progress bar on Storage with the per-game rows folded under it.

---

## Why rclone is off-site only (research, 2026-10-22)

- **The database can't live on a network or rclone mount.** SQLite relies on file locks, which the SQLite
  project says are often broken on network filesystems. Projects like GoToSocial refuse to support it. An rclone
  mount also only uploads a file after it's closed, and the database is open the whole time the server runs.
- **Archives on a mount** need `--vfs-cache-mode writes` or `full` (without it: sequential writes only, no
  read+write opens). They'd also report "saved" before the upload happens, and turn the publish rename into a slow
  copy+delete on object stores.
- So the server's disk stays the only primary copy, and rclone runs as a **copy job**: `copyto` / `deletefile` /
  `about` / `lsjson`, never a mount, and **never `rclone sync`**, which deletes on the remote whatever is missing
  locally. That's the exact failure an off-site copy exists to survive.
- `rclone about` (free space) works on Google Drive, OneDrive, Dropbox, SFTP, SMB, WebDAV and a local folder, but
  **not S3 or B2** (rclone's overview table). On those, "full" is only known from an upload error or the user's
  own cap.

## What already exists (don't re-derive)

- `BackupService` writes `/data/backups/savelocker-<stamp>-<reason>.zip` (a `VACUUM INTO` copy of the database +
  every game's latest archive + `manifest.json`) under a `.tmp` name, renames it on success and keeps
  `RetentionCount` (default 7, max 365). `BackupScheduler` runs it. Restore exists.
- `SettingsService` stores secrets encrypted with ASP.NET Data Protection (the `Secrets` set; key ring under `/data`).
- `ArchiveStore`: `{root}/{gameId}/{versionId}.zip`, `.incoming/` staging, `OpenRead`, `PublishFile`, `TryDelete`.
  `SaveVersionFile(VersionId, Path, Sha256, Size)` already holds a per-file manifest for delta-capable pushes.
  `SaveArchive.HashDirectory` hashes paths + bytes only, never mtimes or zip metadata, so a zip rebuilt from blobs
  has the same `ContentHash`.
- The runtime image is `mcr.microsoft.com/dotnet/aspnet:10.0` with `curl` installed (`src/Server/Dockerfile`), and it
  runs as uid 1654 (or `SAVELOCKER_UID`).

---

## Phase 1 — rclone in the server image + a job runner

1. The Dockerfile's runtime stage installs a **pinned** rclone release (version + SHA-256 checked in the
   Dockerfile, the same "pin and bump together" rule as the SDK). A `Offsite:RclonePath` setting points dev on
   Windows at a local `rclone.exe`.
2. **No plaintext config on disk.** Remotes are stored in the DB, with secrets through `SettingsService`'s
   encrypted path. Each job passes its remote to rclone as `RCLONE_CONFIG_<NAME>_*` environment variables on the
   child process only.
3. `RcloneRunner`: start a job (`copyto`, `deletefile`, `about --json`, `lsjson`, `check`) with `--use-json-log
   --stats 2s`, parse progress and errors into typed results (`QuotaExceeded`, `AuthExpired`, `NotFound`,
   `Network`, `Other`), cancel cleanly. One job at a time per remote. Bandwidth limit setting (`--bwlimit`).
4. Unit tests against rclone's `local` backend in a temp folder. That covers CI with no account.

## Phase 2 — Off-site setup wizard (non-OAuth providers)

A new **Off-site copies** card on the **Storage** page (the Backups tab renamed, 2026-10-23), set up with a
step-by-step wizard in the console (mockup A, decided 2026-10-23):

1. **Where:** provider tiles: Google Drive, OneDrive, Dropbox (Phase 3); **S3-compatible** with presets (Backblaze
   B2, Cloudflare R2, Wasabi, MinIO, AWS, *Other*), **SFTP**, **SMB / Windows share**, **WebDAV** (Nextcloud
   preset), **Folder** (another disk or mounted share inside the container, with a note on adding the volume to the
   compose file).
2. **Sign in:** only that provider's fields, with help text and a "where do I find this?" link each. Secrets are
   masked and never sent back to the browser after saving.
3. **Folder on the remote:** default `SaveLocker`.
4. **What to copy:** *Server backups* and/or *Saves and their history* (Phase 5), the remote history setting, an
   optional size cap, and the games list (all on by default, with sizes, so big ones can be skipped).
5. **Advanced:** encryption (rclone `crypt`, **off by default**, so the copy stays readable by hand). When it's
   turned on: a show-once password, a downloadable recovery file and an "I saved it" tick, because the server's
   copy of the password dies with its disk. Also the bandwidth limit, and the schedule (after each backup /
   hourly / daily).
6. **Test connection:** write, read back, list and delete a probe file, and show free space where `about` works.
   Saving requires a passing test.

After setup the card shows **a tile per remote** (mockup A status, decided 2026-10-23): the state (*Up to date* /
*Copying 3 of 12* / *Paused: …*), last run, space used against the cap and the remote's free space, *Run now*,
*Pause*, *Saves and history…*, *What's on the remote* (a folder tree from the remote's `_index.json`, with
*Download* per file), *Edit*, *Remove* (asks whether to delete the remote's files or leave them; default leave).
**Add another remote** runs the wizard again: rclone's config holds any number of named remotes, so each gets its
own `OffsiteRemote` row (provider, folder, settings, cap, games) and its own copy job. Jobs run one after another.

## Phase 3 — OAuth providers (Google Drive, OneDrive, Dropbox)

The server is headless in Docker. rclone's built-in sign-in sends the browser to `127.0.0.1:53682` **on the machine
running rclone**, which the player's browser can't reach.

1. **Spike first** (half a session, a write-up in `docs/tasks/storage-dedup-offsite/oauth-spike.md`): find the flow
   with the fewest steps that works through the console. Candidates:
   - *Paste the redirect:* the console opens the provider's consent page; after consent the browser fails to load
     `127.0.0.1:53682/?code=…`, and the user pastes that address back so the server completes the exchange.
   - *Agent-assisted:* the SaveLocker agent on the user's PC (which has a browser) runs the sign-in and hands the
     token to the server over its key.
   - *Fallback, always available:* "run `rclone authorize "drive"` on any computer and paste the result", with the
     exact command copyable.
2. Build the chosen flow plus the fallback. Tokens are stored encrypted. Refresh-token expiry or revocation shows
   up as `AuthExpired` (Phase 6, *Reconnect*).
3. Google Drive: an *Advanced: your own client ID* field (rclone's shared ID is rate-limited), with a help article.

## Phase 4 — Off-site server backups

1. After `BackupService` finishes a backup, queue `copyto` to `<folder>/_server/<name>.zip`. Verify size + hash
   where the backend has one, otherwise size.
2. Remote retention for backups: its own count (default = the local count), deleting only backup files this
   server recorded uploading.
3. A failed copy never fails the local backup. It's shown on both cards and retried with backoff.

## Phase 5 — Version mirror: readable layout, its own history setting, size cap

1. **Readable layout.** Normal zips with human names, so anyone can unzip one into a save folder without SaveLocker:
   ```
   SaveLocker/
     README.txt                      how to restore by hand / into a new server
     Hollow Knight/
       Main/
         latest.zip                  a copy of the playthrough's head
         versions/2026-10-21 18-03 · Deck · Before the final boss.zip
       Hard mode run/ …
     _server/savelocker-20261022-030000-scheduled.zip
     _index.json                     version id ↔ file, content hash, size — for Phase 7
   ```
   Names are sanitized for every provider (no `\ / : * ? " < > |`, no trailing dots or spaces, length-capped).
   Renaming a game or playthrough becomes a server-side `moveto` on the remote.
2. **Its own history setting**, separate from the server's: *Latest only* / *Last N versions* / a thinning preset
   (version-history Phase 4's planner, reused), per remote with a per-game override. **Labelled versions always
   go.** An optional size cap in GB: when the next upload would pass it, the oldest unlabelled remote versions are
   dropped first, never a playthrough's `latest.zip`. If that still isn't enough, Phase 6 asks.
3. **Remote-safe deletes (no loss):**
   - Delete only objects this server **recorded uploading** (`OffsiteObject(RemotePath, VersionId, Size, Hash,
     UploadedAt)`), and only when the remote policy drops them. Never because a version is missing locally.
   - A fresh or restored server with an empty `OffsiteObject` table deletes **nothing** on the remote until Phase 7
     has adopted the existing files.
   - **Circuit breaker:** a run that would delete more than 20% of the remote's recorded objects, or more than
     5 GB, pauses and asks (Phase 6) instead of deleting.
   - A local prune or trash purge never deletes on the remote by itself. The remote keeps its own history.
4. Uploads write `name.partial` and then `moveto` the final name on backends where that's cheap. Otherwise they
   write straight to the final name, and `_index.json` is written last, so a half-finished run never claims a
   version it doesn't have.

## Phase 6 — Remote full, expired sign-in and other failures: pause and ask

1. **Before each run:** where `about` works, compare free space with the queued bytes + 10%. If it won't fit,
   don't start.
2. **During a run:** map provider errors to `QuotaExceeded` (Drive `storageQuotaExceeded`, OneDrive
   `quotaLimitReached` / 507, Dropbox `insufficient_space`, B2 `storage_cap_exceeded`, S3 provider-specific). Same
   for the user's own cap.
3. **Pause, never fail silently.** The off-site queue stops. Local sync, backups and pruning keep running. The
   Off-site card shows *Paused: remote full*, a console-wide banner appears (dismissable for 24 h, back while still
   paused), and the server health check reports it.
4. **Ask:** a *Resolve* dialog with:
   - *Trim remote history*, with a preview of exactly which files go and how much space it frees.
   - *Skip some games*, listed by remote size.
   - *Raise the cap*.
   - *I freed space — retry*.

   *Resolve* is also how the circuit breaker (Phase 5) asks.
5. **Expired sign-in:** paused with *Reconnect* (back to the wizard's sign-in step only). Network errors: retry with
   backoff, and ask only after 24 h of failures.

## Phase 7 — Disaster recovery from the off-site copy

Both entry points are built (A and B, decided 2026-10-23).

1. **First-run screen** on an empty server: *Restore from an off-site copy*. The same wizard connects, lists
   `_server/` backups, downloads the chosen one and runs the existing restore. Then it **adopts** the remote: reads
   `_index.json` into `OffsiteObject` so Phase 5's ownership rule covers the files already there.
2. *Import older versions from the mirror* (optional, after restore): downloads versioned zips the backup didn't
   hold back into history, verified by content hash.
3. **Restore from off-site…** on the Storage page, beside the local backups: with a remote already set up it lists
   that remote's `_server/` backups straight away (*Look somewhere else* opens the connect step); with none it starts
   at the connect step. It restores the same way, after the usual *Before restore* safety backup.
4. `README.txt` (written by Phase 5) explains the by-hand path, in case SaveLocker itself is gone.

## Phase 8 — Deduplicated blob store for new versions

1. **Layout:** `{root}/blobs/{sha[0..2]}/{sha}`, each file stored once, Deflate-compressed (zstd only if
   measurement shows a real gain worth a dependency). `SaveVersion.StorageKind` (`Zip` / `Blobs`).
   `SaveVersionFile` gains `ModifiedUtc` (and the folder key, if `Path` doesn't already carry it), so a rebuilt zip
   matches the original's file list and times.
2. **Write path:** uploads still arrive as a staged zip. The server stores each file as a blob (temp, fsync,
   rename; skip if present), then writes the rows and `StorageKind = Blobs` in **one transaction**, then deletes
   the staging file. A crash before the commit leaves only unreferenced blobs, which the sweep collects.
3. **Read path:** `ArchiveStore.OpenRead` for a `Blobs` version streams a zip built on the fly. Every blob is
   hashed as it's read, and a mismatch stops the stream with an error and a health alarm. Wrong bytes are never
   served. Delta copy-forward reads base files from blobs directly. Backups and the off-site mirror go through
   `OpenRead`, so they produce normal zips unchanged.
4. **Cleanup (the data-loss risk) is mark-and-sweep, not counters:**
   - **Live** = named by any `SaveVersionFile` row, **trashed rows included**, so a trash restore always works.
   - A blob is deleted only if it's unreferenced **and** last touched more than 24 h ago.
   - **The race:** an upload that finds an old unreferenced blob already present (a "dedup hit") could commit a
     reference just as the sweep deletes it. The guard: the writer touches the blob's mtime on a hit, and holds a
     shared lock from the touch until its commit. The sweep takes the exclusive lock only for its delete step, and
     re-checks each blob's mtime and references inside it.
   - Rows are always deleted before blobs (the same rule as `ReleaseArchivesAsync`).
5. **Verify storage:** a console action plus a weekly background scrub (throttled) that re-hashes every blob and
   checks every row's blobs exist. Results go on the Storage card and to health.
6. Tests: byte-identical file content and an identical `ContentHash` for every version written, a concurrent
   upload during a sweep (forced via a test hook), a crash at each step of the write path, a trash restore after a
   sweep. `run-delta-upload-tests` green.

## Phase 9 — Converting existing versions (automatic, verified, with progress)

1. **Starts by itself after the update**, as a background `ConversionService`. Startup never waits on it.
2. **Console** (mockup B with A's progress bar, decided 2026-10-23): on the first start after upgrading, a one-time
   notice ("Updated to SaveLocker 0.9: converting your saves… You can keep using it normally. Progress is on
   Storage."). While it runs, a banner on every page with the percentage and *Open Storage*. On Storage, a
   *Converting to save space* card: a big progress bar, versions converted / total, space saved so far, estimated
   time left, *Pause* / *Resume*, *Verify storage*, *Revert…*, and the per-game rows folded under *Per game*.
3. **Per version, oldest first:**
   1. Write its files as blobs.
   2. Rebuild the zip stream from the blobs and check every file's SHA-256 and the version's `ContentHash`
      against the original.
   3. Flip it with a **conditional** update: `StorageKind = Blobs WHERE Id = @id AND StorageKind = Zip AND
      DeletedAt IS NULL`.
   4. Move the original zip to trash (`DeletedReason = converted`) for the trash period.

   A version that fails verification stays a zip, is logged, and is shown in the Storage card's problem list.
4. **Sync never pauses.** A version is immutable, so converting it changes only how the same bytes are stored:
   - **A pull or download already reading the zip** keeps its open handle. The zip only moves after the flip. On
     Windows dev, where moving an open file fails, the move is retried later.
   - **A pull that starts after the flip** reads the blobs. A reader that resolved `Zip` just before the flip and
     finds the file gone re-reads the row once.
   - **A push** creates a new version, written straight as blobs (Phase 8). Its delta base reads from either form.
   - **A prune or delete racing the conversion** wins: the conditional flip matches nothing, and the blobs the
     conversion wrote are swept later.
5. **Disk space rises before it falls** (blobs + originals in trash). The conversion pauses when free space drops
   below max(5 GB, 2 × the largest remaining zip). The card says why and what to free.
6. **Undo while the originals are in trash:** *Revert conversion* flips versions back to their zips.
7. Trashed versions are not converted (they purge soon).

## Phase 10 — Chunking for large single-file saves

1. **Measure first:** from the blob store, report how many bytes sit in files over 8 MB that change between
   versions, and how much 64 KB-average content-defined chunks would save on the real data. Many games compress
   or encrypt their saves, so a small in-game change rewrites the whole file and chunking saves nothing. If the
   measured gain is under ~20% of those files' size, record the result here and drop the phase.
2. If built: FastCDC-style chunking (gear hash, min/avg/max 16/64/256 KB) for files over 8 MB. A file becomes an
   ordered chunk list (`SaveVersionFileChunk`). The same mark-and-sweep, lock and verification rules as Phase 8
   apply. The agent's local copy store (Playthroughs Phase 5) can reuse the chunker.

---

## Verify

- `run-offsite-tests.ps1` (new), against rclone's `local` backend and a MinIO container in testenv: backups and
  mirror round trip, the readable layout, per-game skip, the remote history setting, the cap, simulated quota
  errors → pause → each *Resolve* option, the circuit breaker, a fresh-server restore + adoption that deletes
  nothing.
- `run-dedup-tests.ps1` (new): Phase 8/9 test lists above; the conversion of a seeded 200-version store while a
  script pushes and pulls in parallel.
- `run-delta-upload-tests`, `run-health-tests`, `run-concurrency-tests` unchanged.
- testenv pass with the maintainer's own Google Drive: set up from the wizard, unzip `latest.zip` from Drive into a
  real game's folder and load it in-game, fill a capped remote, resolve from the console.

## Answers settled with the maintainer (2026-10-22)

- **Off-site scope:** server backups, then a mirror of every version. rclone is bundled in the server and set up
  from an easy console wizard.
- **Primary storage stays local.** rclone is for backup only.
- **Providers in the first version:** S3-compatible, SFTP, SMB, WebDAV, folder **and** Google Drive / OneDrive /
  Dropbox.
- **Encryption:** off by default, so the copy is browsable and usable by hand. An optional advanced toggle with a
  show-once recovery key.
- **Remote history:** its own setting + size cap. Labelled versions always go, and a playthrough's latest is never
  dropped.
- **Remote full:** pause and ask. Local sync keeps running.
- **Games:** all by default, opt-out per game.
- **Deduplication:** build it (re-opens the Decision), with no-data-loss safeguards. **Convert existing versions
  automatically**, with a popup and progress on the Config page (moved to the Storage page, 2026-10-23), and sync
  never paused.
