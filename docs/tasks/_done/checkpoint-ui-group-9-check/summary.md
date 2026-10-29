# Checkpoint UI Group 9: run the testenv checklist

**Done 2026-10-14.** Split out of `tasks/_done/checkpoint-ui/` on 2026-10-04, when the Checkpoint UI task was closed as done.

**Run (2026-10-14, `backlog-plans-testenv-mockup` worktree): the whole checklist was run through
`testenv` against current `main` — here: sections 0–3, 3b, 3c, 4 (except 4.6), 5, 6, 7, 8; by the
maintainer: 4.6 and 1.8; and section 9 (`testenv clean`) ran at close. All passed.** Eight findings
(F1–F8), each a maintainer decision and none a blocker; the report records them in full:
`run-2026-10-14.md`. The ones worth a call: F1 (keep-count can't return to 7 once changed), F5
(revoking an enrollment removes its row), F6 (`-test` builds classify as releases, so no Dev-build /
Running chips in a testenv run), F8 (*Fetch from GitHub* defaults to the upstream repo). F2/F3/F4/F7
are checklist staleness (guarded console pulls, `localgamesync.db`, the enrollment LAN guard, the
renamed Help article), not product bugs.

Group 9 (the console's Backups tab, Configuration, Audit log, Help, What's new and sign-in) was checked in a browser
against a scratch server and has its security tests (BK-01, CFG-01). The pass through `tests/testenv.ps1` — a real
console container with real agents — is written as `tasks/_done/checkpoint-ui/group-9-verification.md` and has never been
run.

## Done when

Every step in `group-9-verification.md` has been run through testenv, and any bug it finds is fixed.
