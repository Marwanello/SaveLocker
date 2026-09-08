# Device-verify fresh Windows installer enrollment — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim (only `logs/` links repointed at `../logs/`), kept as the starting point for a future `plan.md`.

---

- **Device-verify fresh Windows installer enrollment.** The wizard shipped in v0.1.7; the upgrade path is well verified. The **fresh install** (clean box, no `%PROGRAMDATA%\SaveLocker`) has never been exercised. Scenarios archived in `../logs/2026-07-14_installer-enrollment.md`:
  - Happy path: run installer, choose enrollment file → page shows server + machine name → install → machine appears online in Machines.
  - ACL trap: `icacls "%PROGRAMDATA%\SaveLocker"` — interactive user needs Modify.
  - Expired-token, skip, and `/SILENT /ENROLL="C:\path\policy.json"`.
