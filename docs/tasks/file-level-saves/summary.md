# File-level saves — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim, kept as the starting point for a future `plan.md`.

---

- **File-level saves — the 24 games the install-root guard now refuses.** A save location is a DIRECTORY throughout, so a manifest entry like `<base>/Save.dat` can only resolve to the whole install folder. Refusing that was right (it would archive the game, and restore's delete pass would prune another machine's installation), but it costs **8% of the manifest**: sweep went 99.0% → 90.9%, every new miss a `MISS(<base>)`. Recovering them properly means archiving the matching FILES rather than their containing directory — a change to the archive model, touching `SaveArchive`, the settle gate and restore. Measure before building: some of the 24 have another path that now wins instead (Cave Story+ did), so the true loss is smaller than 24.
  <br>**Manifest-wide sizing (2026-08-14):** of 21,061 entries with save paths, **702** have a save set that trims to `<base>` and nothing else — those are refused outright — and **169** more have `<base>` as one of several, so they lose a path but keep an answer. That 702 is the ceiling on what this item can recover. Counted from `data/manifest.yaml` by trimming each save template at its first wildcard, the same rule `PathResolver` applies.
