# The other stores' cloud flags — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim, kept as the starting point for a future `plan.md`.

---

- **The other stores' cloud flags.** `ManifestLoader.ManifestCloud` parses only `steam`, because that is the only flag a surface acts on. The manifest also marks `gog` (3,232), `epic` (739), `origin` (239) and `uplay` (106). These matter for **Heroic** candidates, which are exactly the GOG/Epic/Amazon games currently flagged `HasSteamCloud: false` — correct as far as it goes, but it means a GOG game that GOG Galaxy already syncs is offered as if nothing covers it. Needs a per-store flag on `ScanCandidate` rather than a second bool, and a decision about whether the default view should hide those too (Galaxy sync is opt-in per game, unlike Steam Cloud — so probably not, which is why this is not high priority).
