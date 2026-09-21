# Backlog

Not-yet-done work only — one line per item, each pointing at its task folder under `tasks/`. Shipped items are indexed in `logs/shipped-2026-07.md`, `logs/shipped-2026-08.md` and `logs/shipped-2026-09.md` (full detail in `logs/sessions.md`). Dropped items (won't-do) are recorded in `logs/shipped-2026-07.md`.

---

## High priority

- **Decide the registration default (security).** A machine key reads and writes EVERY game, and first-time registration is open even with an admin password set — so on any server reachable beyond the household the password guards the dashboard but not the saves. `Security:RequireAdminPasswordToRegister` closes it and is asserted by `run-console-security-tests.ps1`, but it ships **off** because turning it on changes how every new agent enrolls (`--admin-password`, or enrollment tokens). Maintainer call: flip the default, or document loudly → `Decisions.md` (fleet-scoped machine keys). Per-machine game scoping was considered and not built (product change).
- **Appearance follow-ups (left by Group 5, none blocking).** (a) *The Deck's accent* — `Ui/Theme.cs` still uses `AccentGreen` for both "accent" and "healthy" (68 sites), so pointing a Checkpoint accent at it would make Ember read as a healthy state; Group 6's token split is the fix, and `AgentConfig.EffectiveAppearance` / `RefreshAppearance()` / `Agent.Core/AppearancePalette.cs` are ready for it. (b) *"N machines following"* — the prototype's console line needs the server to know who follows: an optional `FollowsConsoleAppearance` on the heartbeat and a column on `AgentHealth` (a migration). (c) *The tray icon follows Windows' taskbar theme only at start and on a look change* — it does not subscribe to `SystemEvents.UserPreferenceChanged`, so flipping the taskbar between light and dark mid-session leaves a Stealth icon low-contrast until the next change. (d) *`--color-faint` itself* is still 3.31:1 dark / 3.55:1 light — content text now uses `--color-dim`, but the uppercase eyebrow labels that keep `faint` are 10 px; lifting the token means `web/src/index.css`, `agent-ui/src/tokens.css` and `Ui/Theme.cs` together, and `run-appearance-consistency-tests` compares the first two. (e) `web/src/assets/SaveLocker_Logo_crop.png` (1.1 MB) is now referenced by nothing in the console, and the agent UI's copy likewise — safe to delete once the README and the docs are checked for it → `tasks/checkpoint-ui/implementation-grouping.md` → Group 5.
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
- **Art picker: choose which SteamGridDB game to pull from.** Options come from the first name match, so a game whose name matches the wrong entry offers that entry's art with no way out short of renaming the game. A search box in the picker (`search/autocomplete`, remember the chosen id per game) would close it.
- **Art files outlive their game.** `DeleteGameAsync` never removes `/data/art/{gameId}/` (covers, icons, and now `thumbs/`). Small, but it only grows.
- **Interactive setup guide.** First-run console walkthrough ending at a syncing game → `tasks/interactive-setup-guide/summary.md`.
- **Decky Phase 5 hardware proof.** Upload plugin v0.2.1 zip, watch the Deck self-update → `tasks/decky-phase5-proof/summary.md`.
- **QAM left-stick scrolling.** Collapse doctor output behind an expander for fewer focus stops → `tasks/decky-qam-scrolling/summary.md`.
- **Duplicate-shortcut warnings outside doctor.** Chip on affected Add Games rows → `tasks/duplicate-shortcut-warnings/summary.md`.
- **Linux agent secrets.** Enforce 0700/0600 in code; separate app files from mutable state → `tasks/linux-agent-secrets/summary.md`.
- **Constrain manifest paths.** Pin/verify manifest revision, canonicalize, reject escapes → `tasks/manifest-path-constraints/summary.md`.
- **One state owner (deferred).** Wrapper→daemon Unix-socket IPC → `tasks/linux-single-state-owner/summary.md`.

## Planned / future

- **Checkpoint UI redesign.** 8-phase console/agent/Deck visual redesign; Groups 1 (web foundation), 2 (console shell), 3 (agent foundation, Overview, agent Sync all), 4 (agent Games tab, per-game page, art proxy) and 5 (appearance + fleet sync, and the theme following the OS) shipped 2026-09-17/18/20/21; Groups 6-7 + the release-history table open → `tasks/checkpoint-ui/plan.md`.
- **Agent game page: version list and bytes sent on the last push.** Group 4's game page (`agent-ui/src/components/GameDetailView.tsx`) shows the server's head, size and lease but not the version history or how much the last push sent — the plan wanted both. Neither has an agent route behind it: versions need a proxy of the server's per-game version list (`SyncService` already has one for the console), and "bytes sent" needs either the agent to record it per push (`SyncEngine.PushCoreAsync` knows it; the delta path already logs "N of M files") or the server to expose it from the `upload.delta` audit entry. Small either way; not in any Checkpoint UI group → `tasks/checkpoint-ui/implementation-grouping.md` → Group 4.
- **`post-exit-sync` while Sync all runs.** `POST /api/games/{id}/post-exit-sync` answers 409 whenever the agent's global sync gate is held (Sync all, or another launch-gate call), on the premise that the running sync converges. It converges the push but never `OnGameExitAsync`'s lease release and renewer stop, so a Playnite/Decky game that exits mid Sync all keeps its lease renewed on this machine (locking the fleet out) unless the caller retries. Found in the PR #46 review; pre-existing → `tasks/checkpoint-ui/implementation-grouping.md` → Group 4 review fixes.
- **Other stores' cloud flags.** GOG/Epic/Origin/Uplay flags for Heroic candidates → `tasks/other-stores-cloud-flags/summary.md`.
