---
description: Run a SaveLocker test suite and diagnose failures
---

$ARGUMENTS

Pick the suite matching the request (default: ask which). Suites live in `tests/` (e.g. `run-agent-tests.ps1`, `run-server-bugbounty-tests.ps1`, `run-hardening-tests.ps1`; Linux suites via `bash tests/linux/run-linux-tests.sh`).
Before running: clear `.verify/` and `src/Server/localstate/` together, give the test server its own `Storage__DbPath`/`Storage__ArchiveRoot`, and use a `9.9.9-ci` build version. Windows agent suites need the server on `:5179`.
Run it, report pass/fail per suite, and for failures show the failing output and suggest fixes. Never leave `SAVELOCKER_*` test env vars set afterwards.
