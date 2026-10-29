# Competitor gap analysis — 2026-10-18

Asked directly: what is the gap between SaveLocker and the other save-sync tools, ranked by priority, and
where does SaveLocker place once features and how well they are built are scored. The first pass worked from
READMEs only. The second pass (this file) cloned each repository and read the code, then checked every gap
against SaveLocker's own source and task plans. Fifteen items came out of it and are now in [[Backlog]], each
with its own task file; this file is the evidence they point back to.

## What was read

Shallow clones, read only. Nothing from them was built or run.

| Project | Commit read | Stack · licence | ★ | Model |
|---|---|---|---|---|
| [OpenSave](https://github.com/Liquid-co/OpenSave) | `346d9bd` 2026-10-02 | Go + Wails/Svelte · MIT | 231 | Peer to peer, plus an encrypted relay and optional cloud copies |
| [Hoard](https://github.com/rleeon/hoard) | `05496ac` 2026-10-18 | Rust + Tauri/Svelte · **AGPL-3.0** | 182 | Self-hosted server (or their hosted cloud) |
| [Ludusavi](https://github.com/mtkennerly/ludusavi) | `95b651a` 2026-10-17 | Rust · MIT | 6.4k | Backup tool, rclone for cloud; no real sync |
| [BlockSlot](https://github.com/datbird/blockslot) | `3b9c3bf` 2026-10-12 | Python · MIT | 3 | Self-hosted server + Decky plugin — the closest twin to SaveLocker |
| [EmuSync](https://github.com/emu-sync/EmuSync) | `64650b3` 2026-02-23 | C# + Electron · **GPL-3.0** | 144 | Google Drive / Dropbox / OneDrive folder |
| [Game-Save-Genie](https://github.com/Vasanthdev2004/Game-Save-Genie) | `aa65aa1` 2026-10-10 | Python · MIT | 8 | Ludusavi + rclone |
| [Worldline](https://github.com/agustinmorantes/worldline) | `6809d1c` 2026-10-15 | Tauri/React · MIT | 17 | Shared folder, branching checkpoints |

**Licence rule for every task below:** SaveLocker is PolyForm Noncommercial. Hoard (AGPL) and EmuSync (GPL)
are **ideas only** — no code copied, not even translated line by line. MIT projects may be ported with their
copyright notice kept (`NOTICE.md`), though in practice everything here is a different language and gets
rewritten in C#.

## Findings that changed the plan

- **Two data-safety gaps in SaveLocker itself, found by comparing against OpenSave's code.** Both are from
  reading the code; neither has been reproduced yet. Each task starts with a test that should fail today.
  - An emptied save folder is pushed as an ordinary new version (`SyncEngine.PushCoreAsync` only checks
    `Directory.Exists`), and every other machine's pull then deletes its copy. OpenSave holds such a game
    back and asks (`internal/p2p/syncengine/hold.go`). → [[logs/2026-10-20_empty-save-hold/summary|empty-save-hold]]
  - A pull deletes local files that match the game's exclude patterns: they are never in the archive, and the
    restore's delete pass (`SaveArchive.cs`, the loop under "This is the most dangerous loop in the codebase")
    removes everything in scope that the archive lacks. The default excludes include `*.bak`, which is what
    many games call their own backup saves. OpenSave sets excluded files aside and puts them back
    (`internal/snapshot/keep.go`). → [[logs/2026-10-20_keep-excluded-files-on-pull/summary|keep-excluded-files-on-pull]]
- **Registry saves need no archive changes.** OpenSave writes the registry keys into a `registry.json` file
  inside an extra save folder that it owns, so hashing, conflicts and delta upload work on it unchanged
  (`internal/winreg/materialize.go`). SaveLocker already has extra save folders. Nobody restores a Windows
  registry save into a Proton prefix: OpenSave is Windows-only, and Ludusavi on Linux just copies the
  prefix's whole `*.reg` files. → amends [[tasks/registry-saves/summary|registry-saves]]
- **Switch: neither competitor splits Ryujinx per title.** Hoard says so in a comment and syncs
  `bis/user/save` whole; OpenSave does the same. Both do split the yuzu family (title-ID folders), and
  OpenSave also re-homes a save onto *this* machine's profile ID (`internal/presets/switchsave.go`) and names
  games from the emulators' own caches (`switchtitles.go`). → amends `emulator-saves/plan.md` Group F
- **Native Linux: BlockSlot already ships what our plan proposes** — one history per OS family, where Proton
  counts as Windows (`engine/savepick.py` `save_family`, `engine/slotd.py` `decide`). This confirms the
  plan rather than changing it. → amends [[tasks/native-linux-saves/summary|native-linux-saves]]
- **Dropped from the first pass:** "no code signing". No competitor Authenticode-signs its Windows installer;
  Hoard signs only its updater payloads with minisign, which SaveLocker matches with digest-verified updates
  plus build attestation. Also not adopted: OpenSave's Steam-emulator repack save paths (Goldberg, CODEX…).

## Scores

0–5 per area, weighted to a total out of 100. Re-scored after reading the code. BlockSlot is scored from
its README plus the one module read.

| Area (weight) | SaveLocker | OpenSave | Hoard | BlockSlot | Ludusavi | EmuSync |
|---|---|---|---|---|---|---|
| Sync automation (10) | 5 | 4.5 | 4 | 4.5 | 1.5 | 3.5 |
| Conflict and data safety (12) | 4.5 | 4.5 | 3.5 | 4 | 1 | 1 |
| History / rollback (8) | 3.5 | 5 | 4.5 | 4 | 4 | 1.5 |
| PC save detection (12) | 3 | 5 | 4 | 4 | 5 | 3 |
| Emulators (10) | 4 | 4.5 | 4.5 | 3 | 2 | 2.5 |
| Steam Deck / Game Mode (12) | 4.5 | 4 | 3 | 4 | 2.5 | 3.5 |
| Platforms (8) | 2.5 | 4 | 4 | 4 | 4.5 | 3 |
| Storage and transport (10) | 3 | 4 | 4.5 | 3.5 | 3 | 3 |
| Setup / distribution (10) | 2 | 4.5 | 4 | 3 | 4.5 | 4 |
| Fleet, admin, security (8) | 5 | 2 | 3 | 3 | 1 | 1.5 |
| **Total** | **74.4** | **85.0** | **77.6** | 74.4 | 57.6 | 53.6 |

**Placement:** OpenSave 85.0 → Hoard 77.6 → **SaveLocker 74.4** (tied with BlockSlot) → Ludusavi 57.6 →
EmuSync 53.6. SaveLocker scores 4.5 on conflict and data safety until the two issues above are fixed (5
after).

**Where SaveLocker already leads:** conflict safety (leases, the launch gate, the same conflict choice on
every screen), running a fleet (console, health, audit, enrollment), self-updating agents and plugins, the
Playnite plugin, the controller-friendly Game Mode UI, how far its emulator identity goes (Redump serials,
shared memory cards), and security hardening.

**Path to first:** the High items (registry, file-level, Switch, setup, the two safety fixes) add roughly 8
points; the Medium items (launcher install folders, history, travel mode) roughly 4 more — about 86, ahead of
OpenSave.

## The fifteen items

| # | Priority | Task | New or amended |
|---|---|---|---|
| 1 | High | [[logs/2026-10-20_empty-save-hold/summary\|Hold back a save folder that was emptied]] | New |
| 2 | High | [[logs/2026-10-20_keep-excluded-files-on-pull/summary\|A pull keeps this machine's excluded files]] | New |
| 3 | High | [[tasks/registry-saves/summary\|Registry-based saves]] | Amended |
| 4 | High | [[tasks/_done/emulator-saves/plan\|Emulator saves: Switch (Group F)]] | Amended |
| 5 | High | [[tasks/file-level-saves/summary\|File-level saves]] | Amended |
| 6 | High | [[tasks/native-linux-saves/summary\|Native Linux save support]] | Amended |
| 7 | High | [[tasks/interactive-setup-guide/summary\|Setup guide + Unraid template]] | Amended |
| 8 | Medium | [[tasks/launcher-install-dirs/summary\|Epic, GOG, Microsoft Store, Lutris and Bottles installs]] | New |
| 9 | Medium | [[tasks/offline-backoff/summary\|Travel mode: back off when the server is unreachable]] | Amended (was only on a branch) |
| 10 | Medium | [[tasks/household-accounts/summary\|Household accounts]] | New |
| 11 | Medium | [[tasks/version-history-upgrades/summary\|Version history upgrades]] | New |
| 12 | Low | [[tasks/storage-dedup-offsite/summary\|Storage: off-site copies, then deduplication]] | New |
| 13 | Low | [[tasks/playtime-tracking/summary\|Playtime and "last played"]] | New |
| 14 | Low | [[tasks/standalone-emulators/plan\|More standalone emulators]] | New |
| 15 | Low | [[tasks/macos-agent/summary\|macOS agent]] | New |

Also fixed the same session: `emulator-saves/plan.md` and `implementation-grouping.md` still showed
Groups C and E (and Phase 5c) as 🚧 Built although PRs #63 and #68 had merged them.
