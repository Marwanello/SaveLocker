# Checkpoint UI Group 9: run the testenv checklist

Not started. Split out of `tasks/checkpoint-ui/` on 2026-10-04, when the Checkpoint UI task was closed as done.

Group 9 (the console's Backups tab, Configuration, Audit log, Help, What's new and sign-in) was checked in a browser
against a scratch server and has its security tests (BK-01, CFG-01). The pass through `tests/testenv.ps1` — a real
console container with real agents — is written as `tasks/checkpoint-ui/group-9-verification.md` and has never been
run.

## Done when

Every step in `group-9-verification.md` has been run through testenv, and any bug it finds is fixed.
