# Self-host the console fonts — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim, kept as the starting point for a future `plan.md`.

---

- **Self-host the console fonts.** The console loads Inter and JetBrains Mono from Google Fonts at runtime, so on a LAN box with no internet it renders in fallback fonts. CS-13 fixed the import being *discarded*, not the dependency. Needs woff2 subsets for five Inter weights; the Deck UI already vendors TTF Regular/SemiBold in `src/Agent.Linux/Ui/Fonts/` (SIL OFL).
