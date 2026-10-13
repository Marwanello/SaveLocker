# Linux agent: separate app files from mutable state — plan (not started)

Planned 2026-10-12 from the research done for the permissions hardening
(`summary.md`; branch `art-picker-and-linux-agent-secrets`). The permissions half shipped there;
this is the second half of the original backlog note: *"Consider separating immutable app files
from mutable XDG config/state so upgrades cannot overlap the executable tree."*

## Why

Today the Linux agent's install prefix and state directory are the same folder
(`~/.local/share/SaveLocker`). The self-updater is safe only by convention: it copies the files the
tarball names and never enumerates the destination, so state survives — but nothing structural
prevents a future package from shipping a file whose name collides with a state file (a
`config.json` in the tarball would overwrite the enrollment). A split gives:

- **Upgrades that cannot touch state by construction**, not by a file list.
- `remove the app` vs `delete my data` as two distinct, predictable operations (uninstall, backup,
  cleanup).
- The standard XDG shape (`~/.config/savelocker`, `~/.local/state/savelocker`), so the KB and
  `doctor` read like every other Linux app.

## Proposed layout

| Data | Now | After |
|---|---|---|
| Program (`savelocker`, DLLs, `agent-ui/`, bundled `artwork/`) | `~/.local/share/SaveLocker` | unchanged (the install prefix) |
| `config.json`, `api-token` | in the prefix | `${XDG_CONFIG_HOME:-~/.config}/savelocker/` |
| `agent.log`, queues, health events, lease warnings, sync activity, `locks/`, `tmp/`, `shadow/`, `art-cache/`, `manifest.yaml`, markers, `update/`, `plugin-update/` | in the prefix | `${XDG_STATE_HOME:-~/.local/state}/savelocker/` |

## Upgrade of an existing install (the risky half)

Migration must run **before any `AgentConfig.Load`** (including `apply-update`, which the systemd
unit runs before the daemon), because `Load` creates a fresh `config.json` at the new default path
when absent — running it after makes "new config exists" ambiguous and can lose the enrollment.
Move, only when the old path exists and the new one does not:

- `config.json` + `api-token` → config dir (preserve 0600; rewrite the persisted
  `ManifestCachePath`, which points into the old directory).
- `agent.log[.old]`, `manifest.yaml`, `offline-queue.json`, `health-events.json`,
  `lease-warnings.json`, `sync-activity.json`, `shadow/`, `art-cache/`, `artwork/`, markers →
  state dir.
- Delete rather than move: `tmp/`, `update/` (a staged payload must not be applied after the split
  moved its config), `plugin-update/`, `locks/` (recreate empty — locks are handles, not state).
- `install.sh` and the code-side migration must agree; `install.sh` already discards
  `${prefix}/update` today and must also clean the new staging path.

### Decisions this plan still needs (measure before building)

1. **Rollback across the split.** If an update is applied and the daemon never confirms it, the next
   boot rolls the *binaries* back. That older binary resolves the old paths and would boot
   unenrolled — the worst outcome `Updater.cs` is designed to prevent. Options: leave compatibility
   links/config at the old path during the transition (old binaries still read it), or accept that a
   rollback across the split needs a re-enroll. Must be decided first.
2. **Decky plugin compatibility.** The plugin (separate repo, already shipped) reads
   `~/.local/share/SaveLocker/api-token` directly. A split needs either a compatibility symlink or a
   plugin release that reads the new location *before* the agent migrates. `testenv.ps1` rewrites
   the plugin's literals with exact `Contains` checks, so those must be updated together.
3. **`--config` semantics.** Explicit `--config /path/config.json` is the multi-identity/test
   mechanism; today it makes that directory the state dir too. Either keep "explicit config ⇒ all
   state beside it" (smallest change) and use the split only for the default path, or introduce an
   explicit `--state-dir` (cleaner; touches every suite). The suites in `tests/linux/` depend on the
   current contract (`run-linux-tests.sh`'s update section writes the config *inside* the fake
   prefix on purpose).
4. **`agent.log`'s static path.** `AgentLogger.LogPath` is a static built from `DefaultDir` and is
   used before any config load; it must resolve to the new state dir deterministically, and the
   `--config` quirk (`doctor` already documents it) should be resolved in the same pass rather than
   left half-moved.

## Test rig and blast radius

Both rigs isolate the agent by redirecting exactly one variable today:
`tests/testenv.sh` (`XDG_DATA_HOME=$HOME/savelocker-test`, WSL) and `tests/testenv-deck.sh`
(`XDG_DATA_HOME=$PREFIX-state`, Deck). After a split, every rig must also export
`XDG_CONFIG_HOME`/`XDG_STATE_HOME` to guarded `savelocker-test*` paths — otherwise a test run writes
into the real `~/.config`/`~/.local/state`, the "rig polluted the real agent" class of incident.
Also to update: `testenv.ps1`'s `logs` command (hardcodes `~/savelocker-test/SaveLocker/agent.log`),
its Decky literal rewrites, `run-linux-tests.sh`'s update/rollback assertions (they encode
state==prefix), `run-ui-wslg.sh`'s lease-warnings fixture, `seed-test-conflict.sh`, `agent-ui`'s dev
server token lookup (`vite.config.ts`), the KB articles that print the paths, `doctor`'s output,
`REPO_MAP.md`, `Gotchas.md`, `Build and Run.md`.

Rough size: ~30 source files plus tests/CI/docs. This is a multi-session task with its own PR, not a
rider on a permissions fix.

## Explicitly out of scope

- **One state owner (IPC between the wrapper and the daemon)** — separate deferred task
  (`tasks/linux-single-state-owner/summary.md`); the split does not change the two-owner model or
  its locking.
- Anything Windows — `%PROGRAMDATA%` is already a separate state root there.
