# Backlog

Not-yet-done work only — one line per item, each pointing at its task folder under `tasks/`. Shipped items are indexed in `logs/shipped-2026-07.md`, `logs/shipped-2026-08.md` and `logs/shipped-2026-09.md` (full detail in `logs/sessions.md`). Dropped items (won't-do) are recorded in `logs/shipped-2026-07.md`.

---

## High priority

- **Decky conflict resolution (phases 0–14).** Phases 0–9 plus Groups 4–5 shipped; phases 10, 11, 13–14 remain open → `tasks/conflict-resolution-ui/plan.md`.
- **v0.5.4 surfaces without hardware coverage.** Heroic store sub-chips + Game Mode filter-row gamepad nav → `tasks/unverified-ui-surfaces/summary.md`.
- **Emulator saves.** Per-ROM detection + sync (RetroArch, PCSX2, Dolphin, …), 7 phases, none started → `tasks/emulator-saves/plan.md`.
- **Native Linux save support.** Scoped; needs `Game.Platform` isolation before detection lands → `tasks/native-linux-saves/summary.md`.
- **Multiple save paths per game.** Needs schema/archive redesign plus an ambiguity policy; maintainer scope call → `tasks/multiple-save-paths/summary.md`.
- **Registry-based saves.** Needs manifest field + Wine `.reg` parser + archive story; restore needs its own threat model → `tasks/registry-saves/summary.md`.
- **File-level saves.** Recover the ≤702 `<base>`-only manifest entries via file-scoped archives → `tasks/file-level-saves/summary.md`.

## Medium priority

- **Interactive setup guide.** First-run console walkthrough ending at a syncing game → `tasks/interactive-setup-guide/summary.md`.
- **Decky Phase 5 hardware proof.** Upload plugin v0.2.1 zip, watch the Deck self-update → `tasks/decky-phase5-proof/summary.md`.
- **QAM left-stick scrolling.** Collapse doctor output behind an expander for fewer focus stops → `tasks/decky-qam-scrolling/summary.md`.
- **Duplicate-shortcut warnings outside doctor.** Chip on affected Add Games rows → `tasks/duplicate-shortcut-warnings/summary.md`.
- **Linux agent secrets.** Enforce 0700/0600 in code; separate app files from mutable state → `tasks/linux-agent-secrets/summary.md`.
- **Constrain manifest paths.** Pin/verify manifest revision, canonicalize, reject escapes → `tasks/manifest-path-constraints/summary.md`.
- **One state owner (deferred).** Wrapper→daemon Unix-socket IPC → `tasks/linux-single-state-owner/summary.md`.

## Planned / future

- **Checkpoint UI redesign.** 8-phase console/agent/Deck visual redesign; design done, no phase started → `tasks/checkpoint-ui/plan.md`.
- **Other stores' cloud flags.** GOG/Epic/Origin/Uplay flags for Heroic candidates → `tasks/other-stores-cloud-flags/summary.md`.
