# Linux agent secret permissions and state layout — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`).

---

- **Linux agent secret permissions and state layout.** `config.json` contains a long-lived machine key; file privacy depends on the launching shell's umask. Enforce `0700` on private state directories and `0600` on config, queue, health, and log files in code, including CLI enrollment paths. Consider separating immutable app files from mutable XDG config/state so upgrades cannot overlap the executable tree.

---

## Status

**Permissions enforced — branch `art-picker-and-linux-agent-secrets` (2026-10-12), PR pending.**
`StateDirSecurity.Protect` now runs on Linux too: 0700 on the state directory and 0600 on the
private state files (`config.json`, `api-token`, `agent.log`/`.old`, the queue, health events, lease
warnings, sync activity, the manifest cache), applied by the same `AtomicFile` choke point every
restricted write already goes through, by `AgentLogger`'s first write (so a CLI-only `register` /
`enroll` — which never starts the local API — covers it) and by `LocalAuth`; pre-existing 0644 files
are tightened in passing. `agent.log` is now created 0600 instead of with the shell's umask.
Verified by 9 new checks in `tests/linux/run-linux-tests.sh` (4 of them fail against the pre-change
code — mutation-checked), plus 450/450 xunit, the Windows agent build and the full Linux suite
(280 pass / 2 fail, the two being the documented WSLg-injected-session pair).

**State-layout split planned, not built — see `plan-split.md`.** The research (upgrade migration,
rollback-across-the-split, the Decky plugin's hard-coded `api-token` path, and the test rigs'
single-variable isolation) is written down there with the decisions a session must make first.
