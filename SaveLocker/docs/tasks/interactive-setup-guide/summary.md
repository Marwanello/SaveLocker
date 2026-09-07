# Interactive setup guide in the console — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim, kept as the starting point for a future `plan.md`.

---

- **Interactive setup guide in the console.** Raised 2026-08-14 alongside the savelocker.com plan. A first-run walkthrough that takes a new user from an empty server to a syncing game, in the console itself rather than in prose: mint an enrollment file → wait for the machine to appear → add a first game → confirm a push landed, each step self-checking against live state (Machines, GameState) so it advances on its own and cannot claim a step is done when it is not. Two reasons this is not documentation: the marketing site's Get Started has to end *somewhere*, and the natural place is "open the console, it takes it from here" — and the Deck's launch-option step (`savelocker run -- %command%`) is the single most-missed action in the whole flow, with nothing today that notices it was skipped. Re-runnable from Help, and resumable — a user enrolls the Deck days after the PC. Reuses the Help KB shell (`web/src/help/`) for the article surface; the checks are `/api/overview` + machines. Deck Game Mode already has a "Next step" card (`Ui/UiApp.cs`) — same idea, wider scope.
