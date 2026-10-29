# File-level saves — summary

**Planned 2026-10-29:** see [[tasks/file-level-saves/plan|plan.md]] and [[tasks/file-level-saves/implementation-grouping|implementation-grouping.md]].

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). The backlog entry verbatim, kept as the background for `plan.md`.

---

- **File-level saves — the 24 games the install-root guard now refuses.** A save location is a DIRECTORY throughout, so a manifest entry like `<base>/Save.dat` can only resolve to the whole install folder. Refusing that was right (it would archive the game, and restore's delete pass would prune another machine's installation), but it costs **8% of the manifest**: sweep went 99.0% → 90.9%, every new miss a `MISS(<base>)`. Recovering them properly means archiving the matching FILES rather than their containing directory — a change to the archive model, touching `SaveArchive`, the settle gate and restore. Measure before building: some of the 24 have another path that now wins instead (Cave Story+ did), so the true loss is smaller than 24.
  <br>**Manifest-wide sizing (2026-08-14):** of 21,061 entries with save paths, **702** have a save set that trims to `<base>` and nothing else — those are refused outright — and **169** more have `<base>` as one of several, so they lose a path but keep an answer. That 702 is the ceiling on what this item can recover. Counted from `data/manifest.yaml` by trimming each save template at its first wildcard, the same rule `PathResolver` applies.

---

## Competitor research (2026-10-18) — reuse include scopes

**Priority: High.** From [[logs/2026-10-18_competitor-gap-analysis]].

- **SaveLocker already has the building block.** Since the emulator work, a save folder can carry an **include
  scope** (`SaveRoot.IncludeGlobs`, stored on the server): the game owns only the files the scope matches, and
  the restore's delete pass touches nothing outside it. A `<base>/Save.dat` entry can become folder `<base>` with
  scope `Save.dat` — no change to the archive model, which the note above assumed was needed.
- What has to change instead: the install-root refusal (`PathResolver.IsInstallRoot`, an exact match on `<base>`) must allow an install
  folder **only** when an include scope is present, and the settle gate and folder watcher must watch only the
  scoped files (a game folder is busy with logs and caches).
- **OpenSave** (MIT, `internal/snapshot/zip_roots.go`, `internal/delta/manifest.go`) allows a save location to be
  a single file, restoring "as the file itself, not into a directory named after it". That is the alternative if
  scopes turn out awkward for some entries.
- **Depends on knowing the install folder.** On Windows that is Steam and Playnite today; Epic, GOG and the
  Microsoft Store are [[tasks/launcher-install-dirs/summary|Launcher install folders]].
- Measure first, as the note above says: run the detection sweep with scoped `<base>` paths and count how many of
  the 702 come back.
