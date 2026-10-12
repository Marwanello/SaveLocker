# Decide the registration default (security)

Not started. Moved out of `Backlog.md` on 2026-10-01; the notes below are as they stood there.

A machine key reads and writes EVERY game, and first-time registration is open even with an admin password set — so on any server reachable beyond the household the password guards the dashboard but not the saves. `Security:RequireAdminPasswordToRegister` closes it and is asserted by `run-console-security-tests.ps1`, but it ships **off** because turning it on changes how every new agent enrolls (`--admin-password`, or enrollment tokens). Maintainer call: flip the default, or document loudly → `Decisions.md` (fleet-scoped machine keys). Per-machine game scoping was considered and not built (product change).
