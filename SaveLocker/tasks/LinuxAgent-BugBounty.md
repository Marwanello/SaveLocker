# Task — Linux Agent Bug Bounty

**Created:** 2026-07-26  
**Target:** `src/Agent.Linux/` and the `src/Agent.Core/` paths it invokes  
**Goal:** fix the Linux/Steam Deck correctness defects found during the 2026-07-26 static review,
add regression coverage for each failure mode, and verify the real daemon + launch-wrapper
multi-process shape on Linux.

The project builds cleanly; these are runtime and state-ownership bugs rather than compiler errors.
The review command was:

```powershell
dotnet build src/Agent.Linux/SaveLocker.Agent.Linux.csproj --no-incremental
```

Result: **0 warnings, 0 errors**.

---

## Severity

| Severity | Meaning |
|---|---|
| **P1 — high** | Can direct sync traffic to the wrong server or reverse an explicit user action. Fix before the next Linux release. |
| **P2 — medium** | Can silently disable a sync trigger, lose setup state, crash a UI flow, or report a failed system action as successful. |
| **P3 — low** | Produces misleading diagnostics but does not directly change save data. |

## Findings

| ID | Severity | Finding | Primary locations |
|---|---|---|---|
| LA-01 | **P1** | Changing `ServerUrl` does not rebuild the daemon's `SyncEngine`; polling can use the new server while watcher pushes and queue drains keep using the old one. | `src/Agent.Core/AgentApiServer.cs:189`, `src/Agent.Linux/Daemon.cs:43`, `src/Agent.Linux/Daemon.cs:71` |
| LA-02 | **P1** | `SaveGameSyncState` explicitly re-adds a game another process removed, contradicting its own comment and reversing the Deck UI's removal. | `src/Agent.Core/AgentConfig.cs:223` |
| LA-03 | **P2** | Game Mode's **Remove selected** is not durable: the daemon retains its watcher and `CommandPoller` re-adopts every server game missing locally. | `src/Agent.Linux/Ui/SettingsScreen.cs:182`, `src/Agent.Core/CommandPoller.cs:157` |
| LA-04 | **P2** | Setting a folder through the local agent API does not rebuild daemon folder watchers; a game unmapped at startup can remain permanently unwatched. | `src/Agent.Core/AgentApiServer.cs:235`, `src/Agent.Linux/Daemon.cs:86`, `src/Agent.Linux/Daemon.cs:130` |
| LA-05 | **P2** | Batch enrollment saves config only after every selected game succeeds; a later failure or UI exit loses earlier local additions and their Steam AppIDs. | `src/Agent.Core/Enroller.cs:28`, `src/Agent.Core/Enroller.cs:56` |
| LA-06 | **P2** | Native UI enrollment mutates `_config.Games` on a continuation thread while the render loop enumerates the same `List<TrackedGame>`. | `src/Agent.Linux/Ui/UiApp.cs:1073`, `src/Agent.Core/Enroller.cs:35` |
| LA-07 | **P2** | The native UI never reloads `config.json`; saving sounds or settle time can serialize a stale game list and overwrite daemon reconciliation changes. | `src/Agent.Linux/Ui/SettingsScreen.cs:77`, `src/Agent.Core/AgentConfig.cs:136` |
| LA-08 | **P2** | The systemd disable path ignores `systemctl`'s exit code and always reports success. | `src/Agent.Linux/SystemdAutoStart.cs:41`, `src/Agent.Linux/Program.cs:65` |
| LA-09 | **P3** | `doctor --config <path>` reports/probes the default state directory, and an unenrolled agent's expected 401 is mislabeled as “server unreachable.” | `src/Agent.Linux/Doctor.cs:27`, `src/Agent.Linux/Doctor.cs:35` |

### Existing-backlog relationship

`Backlog.md` already records that Game Mode displays a stale game list. LA-03 and LA-07 are more
serious than that display limitation:

- The current UI now exposes a **Remove selected** action and says the games were removed from this
  device, but the daemon and poller can reverse the action.
- Saving an unrelated setting can write the stale list back to disk, so stale state is no longer
  read-only.

Do not close LA-03 or LA-07 by merely refreshing the displayed list.

---

## Execution order

Implement only the work below, then run the verification section and stop.

### 1. Eliminate split-brain server configuration — LA-01

1. Add a configuration-change callback to `AgentApiServer` that identifies connection-affecting
   changes (`ServerUrl`, API key, server pin, machine identity).
2. In both hosts, rebuild the `SyncEngine` after a connection-affecting change.
3. Ensure `CommandPoller`, folder pushes, and `OfflineQueueDrainer` all obtain an engine/client for
   the same current configuration.
4. Add a regression test with two isolated servers:
   - start the daemon against server A;
   - change `ServerUrl` through `/api/config`;
   - trigger a folder push and a queued retry;
   - assert both reach server B and neither reaches server A.

**Acceptance:** after the settings response returns, no new request uses the previous server URL.

### 2. Make local untracking durable — LA-02 and LA-03

This requires one explicit ownership decision before coding:

- **Per-machine opt-out:** persist that this machine does not track a server game and make
  reconciliation respect it; or
- **Server-side removal:** make the UI action delete/disable the server game and clearly label the
  fleet-wide consequence.

Do not keep the current local-only deletion while displaying “Removed from this device.”

Then:

1. Fix `AgentConfig.SaveGameSyncState` so a completed in-flight sync does **not** re-add an entry
   absent from the on-disk config.
2. Stop and dispose the removed game's daemon watcher.
3. Make `CommandPoller.ReconcileGamesAsync` respect the chosen ownership model.
4. Ensure queued pushes for an untracked game are removed without resurrecting it.
5. Add a real cross-process regression:
   - daemon holds game A and starts a push;
   - a second process removes/untracks game A;
   - the push finishes;
   - assert `config.json` still omits game A, no watcher remains, and the next poll does not re-add it.

**Acceptance:** when the UI reports a game is no longer tracked here, neither the wrapper, daemon,
queue drainer, nor reconciliation can silently restore it.

If the chosen model changes an API, regenerate `web/src/api-types.ts` and update the committed
`src/Server/openapi.json` snapshot.

### 3. Rebuild watchers after local path changes — LA-04

1. Notify the host after `/api/games/{id}/folder` changes a tracked path.
2. Rebuild watchers only after the new config is durably saved.
3. Dispose the watcher for the old path.
4. Report the selected path to the server rather than waiting for a later poll.
5. Add a daemon integration test:
   - start with a tracked but unmapped game;
   - set its folder through the local API;
   - write a save into that folder;
   - assert the daemon pushes without restart or an unrelated reconciliation change.

**Acceptance:** a locally selected folder is watched immediately, and the previous folder is not.

### 4. Make enrollment incremental, durable, and render-thread safe — LA-05 and LA-06

1. Do not let `Enroller` mutate the render loop's live `AgentConfig.Games` list from a continuation
   thread.
2. Return immutable per-candidate results and apply them on the UI/render thread, or introduce an
   explicit synchronization boundary shared by every reader and writer.
3. Persist each successful candidate before starting the next candidate.
4. Preserve its Steam AppID and chosen save path even if a later candidate fails.
5. Make cancellation/window close leave every already-successful candidate in a recoverable,
   internally consistent state.
6. Add regression tests for:
   - first candidate succeeds, second fails;
   - UI closes after the first success;
   - render frames continue while a multi-game enrollment is in flight.

**Acceptance:** batch enrollment may report partial success, but it cannot lose a completed game,
lose its Steam AppID, corrupt the list, or throw from concurrent enumeration.

### 5. Prevent stale native-UI saves — LA-07

1. Before changing a scalar setting, re-read the current config under the config lock.
2. Apply only the intended field (`SettleQuietSeconds` or `UiSoundsMuted`) to the fresh state.
3. Update the UI's in-memory view from the merged result.
4. Do not overwrite the game list, save directories, enrollment identity, or per-game sync state.
5. Add a two-process regression:
   - UI process loads config containing game A;
   - daemon process adds game B or changes A's save path;
   - UI toggles sounds or settle time;
   - assert both the daemon change and the UI setting survive.

**Acceptance:** scalar settings are field-level updates, not whole-config replacement from a stale
snapshot.

### 6. Report systemd failures honestly — LA-08

1. Return the exit status from `systemctl --user disable --now savelocker.service`.
2. Make the CLI check the return value before printing “Auto-start disabled.”
3. Keep the native UI toggle reverted when disabling fails.
4. Capture stderr for a useful log message without risking a redirected-stream deadlock.
5. Test with a fake systemctl runner or injected process executor for success and failure paths.

**Acceptance:** neither CLI nor Game Mode claims the service is disabled unless systemctl succeeded.

### 7. Correct `doctor` diagnostics — LA-09

1. Use `config.StateDir` for the displayed state directory, writability probe, and lock probe when
   `--config` is supplied.
2. Keep the log path description accurate if logs intentionally remain in the default directory.
3. Do not call an authenticated game-list route with no API key and then label the 401 as a network
   failure.
4. Distinguish at least:
   - server unreachable;
   - server reachable but machine unenrolled/unauthorized;
   - authenticated and healthy.
5. Add checks using a config file outside `AgentConfig.DefaultDir`.

**Acceptance:** `doctor` never reports the wrong state root and never describes an authentication
failure as a connectivity failure.

---

## Verification

Stop any running agent/server first. Always build with `--no-incremental`.

```powershell
dotnet build src/Agent.Linux/SaveLocker.Agent.Linux.csproj --no-incremental
.\tests\run-local-api-tests.ps1
.\tests\run-concurrency-tests.ps1
```

In WSL on the ext4 clone, with the documented Linux PATH setup:

```bash
dotnet build src/Server/SaveLocker.Server.csproj src/Agent.Linux/SaveLocker.Agent.Linux.csproj --no-incremental
bash tests/linux/run-linux-tests.sh
bash tests/linux/run-ui-wslg.sh 1280x800 --fixtures --autoscan --screen add --screenshot /tmp/linux-agent-bugbounty.png
```

Also verify on a real Steam Deck:

1. Change the configured server and confirm all daemon traffic moves to it.
2. Map an initially unmapped game and confirm a save change pushes without restarting the daemon.
3. Remove/untrack a game while the daemon is running; wait through at least two poll intervals and
   confirm it remains untracked.
4. Disable autostart once with a working user bus and once from a context where the user bus is
   unavailable; confirm the UI reflects the real outcome.
5. Run `doctor --config <non-default-path>` and confirm every reported state path is correct.

## Done when

- Every finding above has a regression test that fails against the pre-fix code.
- All existing Windows and Linux suites remain green.
- The Linux fake-game harness covers daemon + wrapper concurrency for removal and enrollment.
- Deck verification passes for watcher creation, durable untracking, server switching, and systemd
  failure reporting.
- Any changed API has regenerated TypeScript types and an updated OpenAPI snapshot.
- New cross-process/state rules are recorded in `Decisions.md` or `Gotchas.md`.
