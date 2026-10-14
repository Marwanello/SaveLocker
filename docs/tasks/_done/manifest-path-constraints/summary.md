# Constrain external manifest paths — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim, kept as the starting point for a future `plan.md`.

---

- **Constrain external manifest paths.** The Ludusavi manifest is downloaded from mutable `master`; expanded templates are not proven to stay inside the intended Proton prefix. Pin or integrity-verify an approved manifest revision, canonicalize resolved paths, reject `..`/symlink escapes outside allowed roots, test a hostile manifest entry. Preserve explicit manually mapped portable-save paths as a separate trusted-user path.
