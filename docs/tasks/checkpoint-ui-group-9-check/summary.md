# Checkpoint UI Group 9: run the testenv checklist

Done in code terms; one step left. Split out of `tasks/checkpoint-ui/` on 2026-10-04, when the Checkpoint UI task was closed as done.

**Run (2026-10-14, `backlog-plans-testenv-mockup` worktree):** the whole checklist was run against
current `main` — here: sections 0–3, 3b, 3c, 4 (except 4.6), 5, 6, 7, 8; by the maintainer: 4.6 and
1.8. All passed, with eight findings (F1–F8) — see `run-2026-10-14.md`. **Only section 9
(`testenv clean`) is left**, deliberately not run so the rig stays up for a last look; run
`.\tests\testenv.ps1 clean` when done. The findings worth a maintainer call: F1 (keep-count can't
return to 7), F5 (revoking an enrollment removes its row), F6 (`-test` builds classify as releases),
F8 (`Fetch from GitHub` defaults to the upstream repo).

Group 9 (the console's Backups tab, Configuration, Audit log, Help, What's new and sign-in) was checked in a browser
against a scratch server and has its security tests (BK-01, CFG-01). The pass through `tests/testenv.ps1` — a real
console container with real agents — is written as `tasks/checkpoint-ui/group-9-verification.md` and has never been
run.

## Done when

Every step in `group-9-verification.md` has been run through testenv, and any bug it finds is fixed.
