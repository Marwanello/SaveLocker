# Storage: off-site copies, then deduplication — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]). **Planned
2026-10-22: see `plan.md` and `implementation-grouping.md` beside this file.**
**Priority: Low.** [[Decisions]] → *Per-file delta upload reconstructs by copy-forward, not
content-addressable storage* defers deduplication "if storage cost ever becomes the complaint". The 2026-10-22
plan re-opens it at the maintainer's request (local blob store; rclone for off-site copies only). The text below is
the original seed.

---

## Where SaveLocker is today

Every version is a complete zip (`ArchiveStore`: `{root}/{gameId}/{versionId}.zip`). Delta upload saves
**bandwidth**, not disk: ten versions of a 2 GB save take about 20 GB. Server backups (`BackupService`: the
database plus every game's latest save in one zip) are written to the same disk (`/data/backups/`), so a lost
NAS disk loses the backups too.

## What competitors do

- **Hoard** (AGPL — ideas only): every file is stored once per user under its SHA-256; a version is a list of
  references (`crates/hoard-server/src/routes/cas.rs`, ADR 0018). Large single-file saves are split with
  content-defined chunking (`chunking.rs`, a gear-hash rolling hash like FastCDC), so a small edit stores
  only the chunks around it. zstd compression. Blob storage can be local disk or any S3-compatible bucket
  (`s3.rs`: MinIO, Backblaze B2, Cloudflare R2, Garage, `rclone serve s3`).
- **Game-Save-Genie** (MIT): content-addressed blobs in the cloud, each version a small JSON manifest uploaded
  **last** so a version only appears once all its data is there.
- **OpenSave** (MIT): optional cloud copies to Google Drive, Dropbox, OneDrive, WebDAV or a folder.

## Proposed shape

1. **Off-site copy of server backups** (the cheap, useful step): after `BackupService` writes a backup, copy it
   to a configured target — an S3-compatible bucket first (one small client, covers B2/R2/MinIO/rclone), a
   second folder (a different disk or share) as the simplest option. Retention applies there too. Console:
   a target field on the Backups card and the last copy's result.
2. **Age-based retention** from [[tasks/version-history-upgrades/summary|Version history upgrades]] — reduces
   disk use with far less machinery than deduplication.
3. **Only if disk becomes the complaint:** content-addressed blobs, then chunking for large single files,
   behind the same `ArchiveStore` interface so restore/download keep producing a zip. Needs reference-counted
   cleanup, which is the cost the Decision weighed.

## Verify

Step 1: a backup appears at the target; a failed copy is reported on the console and does not fail the local
backup. Step 3 (if built): byte-identical round trip of every version, and `run-delta-upload-tests` green.
