# Surface duplicate-shortcut-name warnings outside doctor — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim (only `logs/` links repointed at `../logs/`), kept as the starting point for a future `plan.md`.

---

- **Surface duplicate-shortcut-name warnings outside `doctor`.** `../logs/2026-08-19_moondeck-save-detection.md` added a `doctor` note for a game name backed by two different Steam AppIDs (found on the maintainer's own Deck: HITMAN 3, Minit, Moving Out, Animal Crossing, Metal Gear, Waydroid) — Steam launches only one, and scan's dedupe can silently pick the dead one. `doctor` is CLI/headless-only; neither the agent-ui Add Games view nor the Game Mode UI reads its output at all, so a user who never opens a terminal never sees the warning. Worth a chip on the affected candidate once there is evidence this recurs for more than one Deck.
