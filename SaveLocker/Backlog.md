# Backlog

Not-yet-done work only. Shipped items are indexed in `logs/shipped-2026-07.md`
(full detail in `logs/sessions.md`).

---

## High priority

- **Device-verify fresh Windows installer enrollment.** The wizard shipped in v0.1.7; the upgrade path is well verified. The **fresh install** (clean box, no `%PROGRAMDATA%\SaveLocker`) has never been exercised. Scenarios archived in `logs/2026-07-14_installer-enrollment.md`:
  - Happy path: run installer, choose enrollment file → page shows server + machine name → install → machine appears online in Machines.
  - ACL trap: `icacls "%PROGRAMDATA%\SaveLocker"` — interactive user needs Modify.
  - Expired-token, skip, and `/SILENT /ENROLL="C:\path\policy.json"`.

## Medium priority

- **Windows: `%PROGRAMDATA%\SaveLocker` ACLs on a multi-user box.** The local API token (`api-token`) and `config.json` (machine key) both inherit the ACL set by the installer — another local user may be able to read them. Fix: tighten the directory ACL to the enrolling user + SYSTEM, or move mutable per-user state out of the machine-wide directory. `run-local-api-tests.ps1` only asserts the file exists on Windows — give it a real ACL assertion once the model is decided.

- **Linux agent secret permissions and state layout.** `config.json` contains a long-lived machine key; file privacy depends on the launching shell's umask. Enforce `0700` on private state directories and `0600` on config, queue, health, and log files in code, including CLI enrollment paths. Consider separating immutable app files from mutable XDG config/state so upgrades cannot overlap the executable tree.

- **Linux auto-update.** The update channel (`/api/agent/latest`) is installer-shaped and Windows-only. A Deck user currently re-runs `install.sh` from a newer tarball. Worth doing before there are many Deck users — a headless device that never updates is one nobody will notice is stale.

- **Harden the `systemd --user` unit.** Add and Deck-test: `UMask=0077`, `NoNewPrivileges=yes`, `PrivateTmp=yes`, `ProtectSystem=full`, `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6`, `RestrictSUIDSGID=yes`, `LockPersonality=yes`. Mirror in both `packaging/linux/savelocker.service` and `SystemdAutoStart.UnitFile()`. Do **not** add `ProtectHome` (save access), `ProtectProc` (Linux writer probe), or `MemoryDenyWriteExecute` (.NET JIT). Record `systemd-analyze --user security savelocker.service` before/after on a Deck.

- **Linux release provenance.** Pin GitHub Actions in `release.yml` to full commit SHAs; publish SHA-256 checksums and a GitHub artifact attestation for the tarball; use draft → attach all assets → publish flow. Document the verification command beside the Deck install instructions.

- **Constrain external manifest paths.** The Ludusavi manifest is downloaded from mutable `master`; expanded templates are not proven to stay inside the intended Proton prefix. Pin or integrity-verify an approved manifest revision, canonicalize resolved paths, reject `..`/symlink escapes outside allowed roots, test a hostile manifest entry. Preserve explicit manually mapped portable-save paths as a separate trusted-user path.

- **Deferred: one state owner for the Linux agent** — wrapper→daemon IPC over a Unix socket, standalone fallback when no daemon is up. The locking in `Decisions.md` §8 makes the current two-owner model *correct*; IPC would make it *simple*. Worth doing before the state files grow further.

## Planned / future

- **Linux add-game streamlining — Phase 3.** Phases 1–2 shipped in PR #24 (the dead "Set save folder" button, prefix-rooted browser, enrollment gating, and the copyable launch-command card). Phase 3 is the gamepad-native Game Mode UI (`savelocker ui`, SDL + Dear ImGui in the existing binary) so a Deck user never returns to Desktop Mode. Full design + rejected alternatives in `Linux-Agent-Streamline.md`. **Gated on a real Deck**: verify a stub window renders under gamescope (§3.3.1), Steam Input delivers a gamepad (§3.3.2), and the tarball size delta stays single-digit MB (§3.1) **before** writing any views — if any fails, re-cost.

- **Registry-based saves** — the Ludusavi manifest has a `registry:` section; only `files:` paths are handled.
- **Multi-directory saves** — some games list multiple save paths. The sync engine tracks one `SaveDirectory` per game; multi-dir support needs a schema change.
- **File-count / newest-mtime delta in conflict UI** — would help disambiguate conflict options. The server does not store it; needs computing at upload time or deriving from the archive on demand. Not done; everything else in conflict Tier 1 is complete.

_Dropped items (won't-do) are recorded in `logs/shipped-2026-07.md`._
