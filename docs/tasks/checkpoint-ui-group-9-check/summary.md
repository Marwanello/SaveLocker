# Checkpoint UI Group 9: run the testenv checklist

In progress. Split out of `tasks/checkpoint-ui/` on 2026-10-04, when the Checkpoint UI task was closed as done.

**Run 1 (2026-10-14, `backlog-plans-testenv-mockup` worktree):** sections 0–3 + 3b + 3c run against
current `main` and passed, with two findings — see `run-2026-10-14.md`. Sections 4–9 (and 1.8, which
needs 4.6's password) remain. The rig is left running for the continuation.

Group 9 (the console's Backups tab, Configuration, Audit log, Help, What's new and sign-in) was checked in a browser
against a scratch server and has its security tests (BK-01, CFG-01). The pass through `tests/testenv.ps1` — a real
console container with real agents — is written as `tasks/checkpoint-ui/group-9-verification.md` and has never been
run.

## Done when

Every step in `group-9-verification.md` has been run through testenv, and any bug it finds is fixed.
