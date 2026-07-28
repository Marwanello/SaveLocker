# Backlog

Not-yet-done work only. Shipped items are indexed in `logs/shipped-2026-07.md`
(full detail in `logs/sessions.md`).

---

## High priority

- **Linux agent bug bounty.** Fix the 2026-07-26 review findings before the next Linux release:
  server-change split brain, removed-game resurrection, non-durable local untracking, stale folder
  watchers, partial/concurrent enrollment, stale Game Mode config writes, false systemd success,
  and misleading `doctor` output. Full bounded task and verification gates:
  `tasks/LinuxAgent-BugBounty.md`.

- **~~Console / server bug bounty~~ — DONE 2026-07-27.** All 13 findings fixed on
  `linux-agent-bugbounty` (not pushed), one commit each, with a new 145-check harness
  (`tests/run-server-bugbounty-tests.ps1`). Archived: `logs/2026-07-27_console-bugbounty.md`.
  **Two follow-ups it leaves behind**, neither blocking:
  - the manual LAN enrollment-URL check on the real deployment (see that log → Verification);
  - the console loads Inter and JetBrains Mono from Google Fonts at runtime, so on a LAN box with no
    internet it still renders in fallback fonts. CS-13 fixed the import being *discarded*, not the
    dependency. Self-hosting needs woff2 subsets for five Inter weights; the Deck UI already vendors
    TTF Regular/SemiBold in `src/Agent.Linux/Ui/Fonts/` (SIL OFL).

- **Windows agent bug bounty — 8 of 12 done (2026-07-27), WA-09…WA-12 remain.** Fixed on
  `linux-agent-bugbounty` (not pushed): live-game restores, unsafe save roots, readable machine
  credentials, non-transactional server changes, the unverified/stale update channel, orphaned lease
  renewal, unlocked sync after lock timeout, and missing process mappings. **Left:** WA-09 WinForms
  and live-state thread ownership (the big one — the tray's `SynchronizationContext` is captured
  before WinForms installs it, so every UI-marshal in `TrayApp.cs` is currently a thread-pool post),
  WA-10 honest autostart reporting, WA-11 discovery-source isolation, WA-12 first-open deep link.
  Progress table, weak-evidence notes and outstanding manual gates: `tasks/WinAgent-BugBounty.md`
  → Progress.
  **Two follow-ups it leaves behind:**
  - **Blocking for release: WA-03's second-account ACL test.** The credentials are now ACL-locked
    to the enrolling account, but "an unrelated local user cannot read them" has only been proven by
    inspecting the ACL, not by logging in as a second user. Also unproven: that the enrolled user can
    still sync *and take a silent update* after a reboot.
  - **A maintainer decision, unanswered:** two test-only env vars now live in production code
    (`SAVELOCKER_LEASE_RENEW_SECONDS`, `SAVELOCKER_SYNC_LOCK_SECONDS`). Keep, promote to real
    settings, or remove and mark those tests manual?

- **Device-verify fresh Windows installer enrollment.** The wizard shipped in v0.1.7; the upgrade path is well verified. The **fresh install** (clean box, no `%PROGRAMDATA%\SaveLocker`) has never been exercised. Scenarios archived in `logs/2026-07-14_installer-enrollment.md`:
  - Happy path: run installer, choose enrollment file → page shows server + machine name → install → machine appears online in Machines.
  - ACL trap: `icacls "%PROGRAMDATA%\SaveLocker"` — interactive user needs Modify.
  - Expired-token, skip, and `/SILENT /ENROLL="C:\path\policy.json"`.

## Medium priority

- **Linux agent secret permissions and state layout.** `config.json` contains a long-lived machine key; file privacy depends on the launching shell's umask. Enforce `0700` on private state directories and `0600` on config, queue, health, and log files in code, including CLI enrollment paths. Consider separating immutable app files from mutable XDG config/state so upgrades cannot overlap the executable tree.

- **Linux auto-update.** The update channel (`/api/agent/latest`) is installer-shaped and Windows-only. A Deck user currently re-runs `install.sh` from a newer tarball. Worth doing before there are many Deck users — a headless device that never updates is one nobody will notice is stale.

- **Harden the `systemd --user` unit.** Add and Deck-test: `UMask=0077`, `NoNewPrivileges=yes`, `PrivateTmp=yes`, `ProtectSystem=full`, `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6`, `RestrictSUIDSGID=yes`, `LockPersonality=yes`. Mirror in both `packaging/linux/savelocker.service` and `SystemdAutoStart.UnitFile()`. Do **not** add `ProtectHome` (save access), `ProtectProc` (Linux writer probe), or `MemoryDenyWriteExecute` (.NET JIT). Record `systemd-analyze --user security savelocker.service` before/after on a Deck.

- **Linux release provenance.** Pin GitHub Actions in `release.yml` to full commit SHAs; publish SHA-256 checksums and a GitHub artifact attestation for the tarball; use draft → attach all assets → publish flow. Document the verification command beside the Deck install instructions.

- **Constrain external manifest paths.** The Ludusavi manifest is downloaded from mutable `master`; expanded templates are not proven to stay inside the intended Proton prefix. Pin or integrity-verify an approved manifest revision, canonicalize resolved paths, reject `..`/symlink escapes outside allowed roots, test a hostile manifest entry. Preserve explicit manually mapped portable-save paths as a separate trusted-user path.

- **Deferred: one state owner for the Linux agent** — wrapper→daemon IPC over a Unix socket, standalone fallback when no daemon is up. The locking in `Decisions.md` §8 makes the current two-owner model *correct*; IPC would make it *simple*. Worth doing before the state files grow further.

## Planned / future

- ~~**Linux add-game streamlining — Phase 3.**~~ **Done — shipped 2026-07-24** (PR #25 + artwork PR #26). Gamepad-native `savelocker ui` (SDL + Dear ImGui in the existing binary), verified on a real Deck through the full cold flow. Archived design + rejected alternatives in `logs/2026-07-24_linux-agent-streamline.md`; §2 amendment in `Decisions.md`.

- **Game Mode UI reflects a stale game list.** `savelocker ui` only *reads* local `config.json`; it never reconciles with the server (only the daemon does, every 20s — `CommandPoller.ReconcileGamesAsync`). So a game deleted in the console still shows in Game Mode until the daemon runs, and there is no in-UI way to untrack. Deferred 2026-07-24 (maintainer chose to keep Phase 3 lean). Fix when revisited: reconcile-on-launch (+ periodic) in `savelocker ui`, optionally a per-game "Stop tracking" that also deletes server-side so the daemon does not re-adopt it (`CommandPoller.cs:157`).

- **Registry-based saves** — the Ludusavi manifest has a `registry:` section; only `files:` paths are handled.
- **Multi-directory saves** — some games list multiple save paths. The sync engine tracks one `SaveDirectory` per game; multi-dir support needs a schema change.
- **File-count / newest-mtime delta in conflict UI** — would help disambiguate conflict options. The server does not store it; needs computing at upload time or deriving from the archive on demand. Not done; everything else in conflict Tier 1 is complete.

_Dropped items (won't-do) are recorded in `logs/shipped-2026-07.md`._
