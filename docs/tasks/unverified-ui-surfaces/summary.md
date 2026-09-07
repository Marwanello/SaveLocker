# v0.5.4 surfaces without hardware coverage — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim, kept as the starting point for a future `plan.md`.

---

- **v0.5.4 surfaces that shipped without hardware coverage.** Neither can lose save data — worst case is a list that filters oddly — which is why they shipped, but both are unverified: the Heroic **store** sub-chips (the test Deck has no Heroic games, so the chip correctly never rendered) and the Game Mode filter row's gamepad navigation. Nothing drives the agent-ui React chips in any suite either.
