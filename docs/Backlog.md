# Backlog

Not-yet-done work only — one line per item, each pointing at its task folder under `tasks/`. Shipped items are indexed in `logs/shipped-2026-07.md`, `logs/shipped-2026-08.md` and `logs/shipped-2026-09.md` (full detail in `logs/sessions.md`). Dropped items (won't-do) are recorded in `logs/shipped-2026-07.md`.

---

## High priority

- **Decide the registration default (security).** A machine key reads and writes EVERY game, and first-time registration is open even with an admin password set — so on any server reachable beyond the household the password guards the dashboard but not the saves. `Security:RequireAdminPasswordToRegister` closes it and is asserted by `run-console-security-tests.ps1`, but it ships **off** because turning it on changes how every new agent enrolls (`--admin-password`, or enrollment tokens). Maintainer call: flip the default, or document loudly → `Decisions.md` (fleet-scoped machine keys). Per-machine game scoping was considered and not built (product change).
- **Flip the console's theme default to follow the OS — only after the migration ends.** Dark is the base and light is `data-theme="light"` opt-in because ~275 hex colours in the unmigrated views (game detail, configuration, audit log, agent updates) are still hardcoded dark → `tasks/checkpoint-ui/implementation-grouping.md` (Group 5). Acceptance is the contrast walk in `Gotchas.md` → *Web console*, run in both schemes.
- **Playnite plugin (16 phases, own task since 2026-09-14).** A pre-launch/post-exit sync gate for Windows (Playnite is the first host to give Windows a genuine pre-launch boundary), automatic game matching, a link/enroll popup with Ludusavi manifest search, a status-and-actions panel in Playnite's UI, and an agent-driven self-updater. Sized at ~18–24 sessions full scope, ~7–8.5 for the MVP cut (Groups 1 and 3); none started → `tasks/playnite-plugin/plan.md`.
- **v0.5.4 surfaces without hardware coverage.** Heroic store sub-chips + Game Mode filter-row gamepad nav → `tasks/unverified-ui-surfaces/summary.md`.
- **Emulator saves.** Per-ROM detection + sync (RetroArch, PCSX2, Dolphin, …), 7 phases, none started → `tasks/emulator-saves/plan.md`.
- **Native Linux save support.** Scoped; needs `Game.Platform` isolation before detection lands → `tasks/native-linux-saves/summary.md`.
- **Multiple save paths per game.** Needs schema/archive redesign plus an ambiguity policy; maintainer scope call → `tasks/multiple-save-paths/summary.md`.
- **Registry-based saves.** Needs manifest field + Wine `.reg` parser + archive story; restore needs its own threat model → `tasks/registry-saves/summary.md`.
- **File-level saves.** Recover the ≤702 `<base>`-only manifest entries via file-scoped archives → `tasks/file-level-saves/summary.md`.

## Medium priority

- **Console: list and revoke individual signed-in browsers.** Sessions exist (`AdminSession`: created-at, last-used, client address) but the only controls are Lock and "Sign out everywhere". A small table under *Configuration → Admin password* would make a stolen session findable.
- **Console: per-game detail when a Sync all machine fails.** The toast names the machine and the agent's own reason for the first failure; a batch with several failures only counts the rest.
- **Sign-in throttle: surface lockouts in the console.** `admin.lockout` is in the audit log, but nothing raises it as a notification the way an agent problem is.
- **Interactive setup guide.** First-run console walkthrough ending at a syncing game → `tasks/interactive-setup-guide/summary.md`.
- **Decky Phase 5 hardware proof.** Upload plugin v0.2.1 zip, watch the Deck self-update → `tasks/decky-phase5-proof/summary.md`.
- **QAM left-stick scrolling.** Collapse doctor output behind an expander for fewer focus stops → `tasks/decky-qam-scrolling/summary.md`.
- **Duplicate-shortcut warnings outside doctor.** Chip on affected Add Games rows → `tasks/duplicate-shortcut-warnings/summary.md`.
- **Linux agent secrets.** Enforce 0700/0600 in code; separate app files from mutable state → `tasks/linux-agent-secrets/summary.md`.
- **Constrain manifest paths.** Pin/verify manifest revision, canonicalize, reject escapes → `tasks/manifest-path-constraints/summary.md`.
- **One state owner (deferred).** Wrapper→daemon Unix-socket IPC → `tasks/linux-single-state-owner/summary.md`.

## Planned / future

- **Checkpoint UI redesign.** 8-phase console/agent/Deck visual redesign; Groups 1 (web foundation), 2 (console shell) and 3 (agent foundation, Overview, agent Sync all) shipped 2026-09-17/18/20; Groups 4-7 + the release-history table open (Group 4 also owns per-game "Sync this game", which needs new agent routes) → `tasks/checkpoint-ui/plan.md`.
- **`testenv.ps1 sync` skips new directories.** It reads `git status --porcelain`, which collapses an untracked directory to one entry, and the copy loop takes only files — so a new folder never reaches the WSL clone. One-flag fix (`--untracked-files=all`); today's workaround is `git add` first → [[Gotchas]] → *`tests/testenv.ps1`*.
- **Other stores' cloud flags.** GOG/Epic/Origin/Uplay flags for Heroic candidates → `tasks/other-stores-cloud-flags/summary.md`.
