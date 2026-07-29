# Task — Windows Agent Bug Bounty

**Created:** 2026-07-26

**Target:** `src/Agent/`, `src/Agent.Core/`, and the Windows installer/runtime paths they invoke

**Goal:** fix the Windows Agent data-safety, trust, lifecycle, and thread-ownership defects found
during the 2026-07-26 static review, add regression coverage for each failure mode, and verify the
installed tray-agent shape on a real multi-user Windows machine.

The review covered the current working tree, including the in-progress shared-core changes from
`tasks/LinuxAgent-BugBounty.md`. Findings below describe defects still present after those changes;
they do not ask for the Linux work to be reverted.

The review command was:

```powershell
dotnet build src/Agent/SaveLocker.Agent.csproj --no-incremental
```

Result: **0 errors, 4 warnings**. Three `MSB3061` warnings reported that existing Agent output DLLs
could not be deleted, and one `MSB3277` warning reported an unresolved `WindowsBase` 4.0/5.0
reference conflict from WebView2. The output DLL timestamps were refreshed, but verification for
this task must start from an Agent-free process list and produce a clean non-incremental build so
stale output cannot mask a fix.

---

## Progress (updated 2026-07-27)

**ALL 12 findings fixed on `linux-agent-bugbounty`, one commit each. Not pushed.**
The code work is complete; the manual verification below is not. See "Still outstanding".

| ID | State | Commit |
|---|---|---|
| WA-01 | done | `34a7c75` |
| WA-02 | done | `d1cadf0` |
| WA-03 | done — **code only, see manual gates** | `382cd98` |
| WA-04 | done | `ae7fc2b` |
| WA-05 | done | `efe4335` (+ `98a1a68` recording the http/TLS decision it depends on) |
| WA-06 | done — **weakest evidence, see below** | `980e26d` |
| WA-07 | done | `ef4c621` |
| WA-08 | done | `c27197e` |
| WA-09 | done — **1 of 6 checks discriminates, see below** | `4a3ac4c` |
| WA-10 | done — 6 of 13 checks discriminate | `27d9a50` |
| WA-11 | done — 4 of 6 checks discriminate | `394b134` |
| WA-12 | done — 2 of 7 checks discriminate | (this commit) |

### Harness

`tests/run-winagent-tests.ps1` — **114 checks**, own server on :5189 (+ :5190–:5198), state in
`.verify-winagent`.
Each finding's block is verified to fail against pre-fix code by `git stash push -- src/`, rebuild,
re-run. Where that check is weaker than "N of N fail", the commit message says so explicitly.

### Where the evidence is weaker than the pass count suggests

Read these before trusting the suite as proof:

- **WA-12** — **2 of 7 fail pre-fix**, and they are the two that matter: the deep link being the
  navigation target, and the window not falling back to the home page. The other five are fixture
  steps (the game exists, another machine holds the lease, the tray is up, the launch was refused,
  the window opened) that must succeed for the assertion to mean anything — pre-fix the window still
  opens, it just opens on the wrong page.
  <br>The drive is the **lease-refused** deep link, not the first-run prompt. The prompt is a modal
  `MessageBox` and cannot be accepted headlessly; the refused launch goes through the identical
  first-window-creation path. Verification item 1 still covers the Settings route by hand, and it is
  the only place the *user-facing* symptom in the finding is confirmed.
- **WA-11** — **4 of 6 fail pre-fix**. Of the two that do not: "the unreadable save root really is
  unreadable" is a fixture sanity check by design (a Deny ACE that silently failed to apply would
  make the whole block pass for the wrong reason), and "the unreadable root yields nothing" passes
  **vacuously** pre-fix — the scan crashed, so it yielded nothing at all. It earns its place only
  once the scan survives.
  <br>Not covered: the Steam sources. `FindSteamPath` reads the real registry and is not injectable,
  so a fake Steam root cannot be substituted headlessly, and making a real Steam library unreadable
  to prove a point is not an acceptable test. `SteamShortcuts.ReadAllAsync`'s per-account isolation
  and the `.acf`/`libraryfolders.vdf` guards rest on code review. Verification item 9 covers them by
  hand.
- **WA-10** — **6 of 13 fail pre-fix**, and the seven that pass are not vacuous: writing and removing
  the Run entry worked before too, so those checks are shared ground rather than dead weight. One is
  worth naming — "the toggle stays OFF after a refused write" passes pre-fix because the Deny ACE
  stopped the write, so even the old permissive `IsEnabled` found nothing to report. It guards a
  regression rather than demonstrating the defect.
  <br>The pre-fix comparison **must keep `SAVELOCKER_RUNKEY_SUBPATH`** while reverting the three
  behaviours. Stashing all of `src/` removes the knob, and the harness then points a Deny ACE and a
  startup entry at the machine's **real** Run key. Shim the behaviours, not the plumbing.
- **WA-09** — **1 of 6 fails pre-fix**, and it is the owner assertion ("the UI owner is the WinForms
  context"). The other five — no cross-thread call, no collection-modified, the API answering
  throughout, the tray surviving, the poller adopting during the churn — **pass against pre-fix code
  too**, and that is the finding's nature rather than a gap in the harness: an off-thread menu
  rebuild usually gets away with it, because WinForms only throws once a handle exists and a
  `NotifyIcon`'s menu has none. They are worth keeping as a stress floor; they are not proof.
  <br>The pre-fix comparison also needed care. A first attempt showed 6 of 6 passing because the
  shim constructed the marshalling `Control` *before* reading `SynchronizationContext.Current` — and
  constructing a `Control` is itself enough to install the WinForms context. Reading `Current` first,
  exactly as the original constructor did, reproduces the defect and fails the check. Anyone
  re-verifying this must preserve that ordering or they will "confirm" a fix that is not there.
  <br>Not covered automatically at all: the folder picker (opening a modal dialog cannot be
  automated headlessly) and the WA-12 deep link. Verification items 1 and 3 still cover them by hand.
- **WA-06** — the central assertion ("no lease renewal after the engine is retired") does *not*
  discriminate against pre-fix code: `SAVELOCKER_LEASE_RENEW_SECONDS` is itself part of the fix, and
  at the production 3-hour interval there are no renewals to leak inside a test run. 2 of 4 fail
  pre-fix. The claim rests on code review plus a test of the *fixed* lifecycle.
- **WA-08** — the pre-fix run **aborts** at the first check (the endpoint does not exist, so the
  follow-up POST throws). 1 of 8 observed failing; the rest are unreachable, not passing.
- **WA-05** — 7 of 12 fail pre-fix. Two foreign-host checks pass vacuously there because
  `check-update` is itself part of the fix, so nothing runs to make a request.
- **WA-04** — "server A's TLS pin was dropped" is vacuous over http (no pin is ever recorded). Real
  pin behaviour is covered by `run-enrollment-tls-tests.ps1`.

### Settled by the maintainer, 2026-07-28

- The two **test-only environment variables** (`SAVELOCKER_LEASE_RENEW_SECONDS`,
  `SAVELOCKER_SYNC_LOCK_SECONDS`) are **kept for future testing and stay unadvertised** — vault only,
  never `cli-reference.md` or the release notes. Anything added in the same spirit follows the same
  rule (`Decisions.md`).
- The release is **v0.5.0**.
- **WA-03's multi-user verification is deferred**, not blocking. Revisit when a multi-user Windows
  box is available; until then the release notes describe the ACL change rather than assert the
  outcome.

### Still outstanding for every finding so far

**The entire Verification section below is unrun** apart from the automated suites. Nothing on a
clean VM, nothing with two accounts, nothing with a real game or a real Steam shortcut. In
particular these acceptance criteria are *implemented but not signed off*:

- WA-03's "an unrelated local user cannot read either credential" — needs a second Windows account.
  Also unproven: that the enrolled user can still sync **and take a silent update** after a reboot.
  **Deferred by the maintainer 2026-07-28** — assumed working for now, to be revisited on a
  multi-user box. Note the consequence: the ACL is demonstrated, the guarantee is not.
- WA-08's GUI enrollment of a real non-Steam shortcut (verification item 3).
- WA-06's tray two-server-lease transition (verification item 7) — a lease is only taken by the
  process watcher or the launch wrapper, neither drivable headlessly during a server change.
- WA-01's real-game timing (verification item 4).
- WA-12's first-run Settings deep link on a **cold WebView2 profile** (verification items 1 and 3).
  The automated block drives the same first-window-creation path through a refused launch, but the
  prompt itself is a modal `MessageBox` that cannot be accepted headlessly — so the exact symptom
  named in the finding is still unconfirmed on a real fresh install.

### What WA-09 changed, for whoever picks up WA-10…WA-12

`UiDispatcher` (new, `src/Agent/UiDispatcher.cs`) is now the single owner of every WinForms object.
It creates a control and forces its handle, which is what installs the WinForms synchronization
context on that thread — so it establishes the owner rather than capturing one that does not exist
yet. Background code reaches the UI through `_ui.Post(...)` or, for the modal surfaces, `await
_ui.InvokeAsync(...)`. **Any new UI work in `TrayApp.cs` must go through one of those two.**

Also landed with it: `FolderPicker` runs on the tray's own UI thread instead of a private STA thread
(`Main` is already `[STAThread]`); `ProcessWatcher` refuses overlapping polls; and the last direct
mutation of the live `List<TrackedGame>` (`AgentCli.SeedGamesAsync`) went copy-on-write like the
rest. WA-12 will need the deep-link queue to be applied on this dispatcher.

---

## Severity

| Severity | Meaning |
|---|---|
| **P1 — high** | Can overwrite an active game's saves, sync an unsafe directory, expose a long-lived credential, execute an unverified update, or defeat lease/serialization guarantees. Fix before the next Windows release. |
| **P2 — medium** | Can silently disable lifecycle sync, corrupt live in-memory state, break setup/discovery, or report a failed Windows action as successful. |
| **P3 — low** | Produces stale or misleading UI state without directly changing save data. |

## Findings

| ID | Severity | Finding | Primary locations |
|---|---|---|---|
| WA-01 | **P1** | Windows has no true pre-launch boundary: `ProcessWatcher` observes a game only after it is running, then `OnGameLaunchAsync` pulls and can restore files under the live process. Tray, dashboard-command, and CLI pull paths also have no central “game is active” refusal. | `src/Agent/TrayApp.cs:78`, `src/Agent/TrayApp.cs:402`, `src/Agent.Core/Watchers.cs:70`, `src/Agent.Core/SyncEngine.cs:294`, `src/Agent.Core/SyncEngine.cs:330`, `src/Agent.Core/CommandPoller.cs:375` |
| WA-02 | **P1** | Save-folder assignment accepts any string and the destructive sync path never invokes `SaveDirSanity`. A profile root, drive root, install tree, or other oversized/wrong directory can be archived and a force-pull can replace it. Server reconciliation can assign the same unsafe path without local confirmation. | `src/Agent.Core/AgentApiServer.cs:255`, `src/Agent.Core/CommandPoller.cs:123`, `src/Agent.Core/Enroller.cs:33`, `src/Agent.Core/SaveDirSanity.cs:20`, `src/Agent.Core/SyncEngine.cs:248` |
| WA-03 | **P1** | `%PROGRAMDATA%\SaveLocker` secrets are not private on Windows. `config.json` contains the machine API key and `api-token` grants the local management API, but both Windows permission branches write files and return without replacing inherited ACLs. | `src/Agent.Core/AgentConfig.cs:66`, `src/Agent.Core/AtomicFile.cs:29`, `src/Agent.Core/LocalAuth.cs:100`, `src/Agent.Core/LocalAuth.cs:119`, `installer/SaveLocker.iss:93` |
| WA-04 | **P1** | Changing `ServerUrl` is not a transactional identity change. The API persists an unvalidated value before constructing a client, so a typo can return 500 and leave an Agent that crashes on restart. A valid origin change retains and sends the old server's API key, machine ID, and TLS pin to the new origin. HTTPS registration through the local UI also ignores `ApiClient.ObservedPin`, so it does not establish a new TOFU identity. | `src/Agent.Core/AgentApiServer.cs:195`, `src/Agent.Core/AgentApiServer.cs:221`, `src/Agent.Core/AgentConfig.cs:13`, `src/Agent.Core/ApiClient.cs:27`, `src/Agent/TrayApp.cs:140` |
| WA-05 | **P1** | The Windows update channel is not bound to the current trusted connection. `UpdateChecker` snapshots the old base URL/key, bypasses `ApiClient.For` and its pin warning, accepts an arbitrary absolute download URL using a client whose default headers include the machine key, writes a predictable executable, performs no digest/AuthentiCode verification, and `TrayContext` keeps both the checker and an old available-result after a server change. | `src/Agent.Core/UpdateChecker.cs:55`, `src/Agent.Core/UpdateChecker.cs:96`, `src/Agent.Core/UpdateChecker.cs:108`, `src/Agent/TrayApp.cs:45`, `src/Agent/TrayApp.cs:180`, `src/Agent/TrayApp.cs:282`, `src/Agent/TrayApp.cs:379` |
| WA-06 | **P1** | Replacing `_engine` does not retire the previous engine. If the server or identity changes while a game lease is held, the old engine's timer can renew the lease on the old server indefinitely while exit uses the new engine and releases against the new server. `SyncEngine` has no disposal/cancellation boundary for its lease timers. | `src/Agent/TrayApp.cs:140`, `src/Agent.Core/SyncEngine.cs:22`, `src/Agent.Core/SyncEngine.cs:341`, `src/Agent.Core/SyncEngine.cs:355` |
| WA-07 | **P1** | The cross-process save lock times out after 30 seconds and deliberately proceeds unlocked, while a normal settle may hold it for 120 seconds and HTTP operations may run for 10 minutes. A tray sync and CLI one-shot can therefore overlap restore/archive/config operations in exactly the state the lock exists to prevent. | `src/Agent/Program.cs:19`, `src/Agent.Core/AgentStateLock.cs:40`, `src/Agent.Core/AgentStateLock.cs:65`, `src/Agent.Core/SyncEngine.cs:103`, `src/Agent.Core/SyncEngine.cs:248` |
| WA-08 | **P2** | Games enrolled through the Windows UI never receive `TrackedGame.ProcessNames`. The scanner knows a shortcut executable/install directory, but `ScanCandidate` carries no process-name result and `Enroller` leaves the list empty; `ProcessWatcher` then excludes the game, so lease, launch-pull, running-game suppression, and exit-push never run. Only the CLI's optional `--proc` path populates the field. | `src/Agent/GameScanner.cs:69`, `src/Agent.Core/ScanCandidate.cs:19`, `src/Agent.Core/Enroller.cs:40`, `src/Agent/TrayApp.cs:211`, `src/Agent.Core/AgentCli.cs:203` |
| WA-09 | **P2** | Windows thread ownership is undefined in three connected paths. `TrayContext` captures `SynchronizationContext.Current` before `Application.Run` installs the WinForms context and falls back to the thread-pool implementation; background callbacks then touch menus, forms, the icon, and watcher lists off the UI thread. Kestrel/poller continuations mutate the live `List<TrackedGame>` while UI and timer callbacks enumerate it. `FolderPicker` reads `Application.OpenForms` and passes a UI-thread `Form` as owner from a separate STA thread. | `src/Agent/TrayApp.cs:20`, `src/Agent/TrayApp.cs:52`, `src/Agent/TrayApp.cs:57`, `src/Agent/TrayApp.cs:92`, `src/Agent/TrayApp.cs:157`, `src/Agent.Core/CommandPoller.cs:103`, `src/Agent.Core/Enroller.cs:40`, `src/Agent/FolderPicker.cs:12`, `src/Agent/FolderPicker.cs:22` |
| WA-10 | **P2** | “Start with Windows” can report success when the registry action returned `false`. The local config endpoint ignores `IAutoStart.SetEnabled`'s result and always returns `{ ok: true }`; `IsEnabled` also treats any non-empty stale value as valid without checking that it launches the current executable. | `src/Agent.Core/AgentApiServer.cs:216`, `src/Agent/AutoStart.cs:17`, `src/Agent/AutoStart.cs:24` |
| WA-11 | **P2** | Windows discovery is all-or-nothing around filesystem access. Steam-user, library, manifest, and common-save-root enumerations catch parse errors but not access-denied, disappearing-drive, or ordinary I/O failures, so one unreadable location can fail the whole rescan/setup request instead of skipping that source. Registry keys are also not deterministically disposed. | `src/Agent/GameScanner.cs:54`, `src/Agent/GameScanner.cs:90`, `src/Agent/GameScanner.cs:170`, `src/Agent.Core/SteamShortcuts.cs:65` |
| WA-12 | **P2** | The first-run “Yes” path asks to open Settings but loses the deep link. `OpenWindow` calls `Navigate` before WebView2 exists, the call is discarded, and `OnLoad` later navigates to `/`; fresh installs land on Overview instead of the registration settings they accepted. | `src/Agent/TrayApp.cs:123`, `src/Agent/TrayApp.cs:224`, `src/Agent/TrayApp.cs:237`, `src/Agent/AgentWindow.cs:35`, `src/Agent/AgentWindow.cs:63` |

### Existing-work relationship

- `Backlog.md` already records the `%PROGRAMDATA%\SaveLocker` ACL concern. WA-03 raises it to P1
  because the two readable values are not ordinary preferences: one is a server credential and the
  other grants the machine-management API.
- `tasks/LinuxAgent-BugBounty.md` covers shared config/list ownership from the Linux daemon and
  separate Game Mode process. WA-04, WA-06, WA-07, and WA-09 must still be verified in the Windows
  tray + Kestrel + timer + CLI shape. A Linux regression alone does not close them.
- Do not “fix” WA-01 by merely baselining the first `ProcessWatcher` poll. Every later launch is
  still observed after the game has started, and dashboard/tray/CLI pulls still bypass it.
- Do not “fix” WA-02 only at the folder picker. Paths also arrive from the local API, enrollment,
  CLI, and server reconciliation; the destructive pull needs its own final backstop.

---

## Execution order

Implement only the work below, run the verification section, and stop.

### 1. Establish a real Windows game-activity safety boundary — WA-01

1. Make an explicit Windows lifecycle decision:
   - introduce a launch wrapper/integration that can pull before the game process starts; or
   - stop describing process polling as pre-launch and never restore after the process is observed.
2. Add a central pull/restore guard used by tray actions, dashboard commands, CLI commands, and
   automatic lifecycle handling. A host-specific UI check is insufficient.
3. Baseline processes already running when the Agent starts without emitting a synthetic launch
   restore.
4. Reject or defer dashboard pull/sync commands while their target game is active and report the
   real reason to the console.
5. Test a game that starts, opens its save, writes before the next 4-second poll, remains active
   through a dashboard force-pull request, and exits.

**Acceptance:** no code path replaces a save directory while a configured game process is active,
and an automatic pull advertised as pre-launch completes before the game can read or write saves.

### 2. Put destructive path safety in the sync boundary — WA-02

1. Define hard refusals for roots that can never be a save folder (drive root, user profile,
   Windows/system directories, SaveLocker state/install roots, and equivalent high-blast-radius
   paths).
2. Use `SaveDirSanity` for suspicious prefix/install/oversize mappings and require a deliberate,
   accurately worded override where false positives are possible.
3. Validate and canonicalize paths from:
   - the folder picker and typed local API;
   - enrollment and CLI add-game;
   - server `MachineSavePath` reconciliation.
4. Re-check immediately before archive creation and restore. Configuration-time validation cannot
   defend a later config edit, symlink/reparse-point change, or hostile server path update.
5. Test profile root, drive root, Program Files, the Agent state directory, a normal save folder,
   and a path that changes through a reparse point between mapping and sync.

**Acceptance:** a bad mapping cannot archive or replace a broad Windows directory even through a
force-pull or server-issued command.

### 3. Make Windows state private and test the installed ACL — WA-03

1. Decide whether machine state belongs to one enrolling Windows account or to an explicit local
   group. Do not inherit broad `ProgramData` read access accidentally.
2. Apply an ACL at directory creation time and preserve it across atomic replacement. At minimum,
   grant the intended owner, `SYSTEM`, and Administrators; remove ordinary local-user read access.
3. Cover `config.json`, `api-token`, queue, health, lease warnings, locks, logs, and temporary
   archives. The key/token files are the release blocker, but sibling files must not silently use a
   contradictory model.
4. Keep fresh installer enrollment and silent update writable by the de-elevated tray user.
5. Add an installed-agent test using a second non-admin Windows account that attempts to read the
   key/token and call the local API.

**Acceptance:** an unrelated local user cannot read either credential or authenticate to the local
API, while the enrolled user can still sync and update after reboot.

### 4. Make server changes transactional and origin-bound — WA-04

1. Parse and validate a candidate URL as an absolute `http` or `https` origin before mutating
   in-memory or on-disk config.
2. Build/probe the candidate connection before committing it, or roll back every field if the
   transition fails. A rejected request must leave a restartable prior config.
3. Treat API key, machine ID, and TLS pin as origin-bound. Do not send server A's credential to
   server B; require enrollment/registration for the new origin.
4. On HTTPS enrollment or registration, persist the newly observed pin transactionally with the
   returned machine identity.
5. Update connection consumers only after the new config is durable and internally consistent.
6. Test invalid URI text, unsupported schemes, unreachable HTTPS, server A → B, and a failed B
   registration. Inspect both servers to prove A's key never reaches B.

**Acceptance:** a settings typo cannot brick startup, and no request ever carries credentials or a
pin that belong to another origin.

### 5. Bind updates to the current server and verify the artifact — WA-05

1. Rebuild/retire the update client and clear cached update results whenever connection identity
   changes.
2. Route metadata checks through the same current TLS/pin policy as other Agent traffic.
3. Restrict downloads to the expected origin, or create a credential-free request for an explicitly
   allowed external origin. Never forward `X-Api-Key` to an arbitrary `DownloadUrl`.
4. Add an integrity field to update metadata and verify it after streaming and before execution.
   Prefer Authenticode verification as an additional publisher check once releases are signed.
5. Use a unique, owner-private temporary file, bound its maximum size, delete it on every failure,
   and reject non-executable/error-page payloads.
6. Add two-server tests:
   - cache an available update from A, switch to B, and assert A's result cannot be launched;
   - return an external URL, redirect, wrong digest, truncated payload, and valid payload, asserting
     the machine key never reaches the download host and only the valid installer reaches launch.

**Acceptance:** clicking Update can execute only the artifact authorized by the currently configured
server and verified after download.

If the update DTO changes, regenerate `web/src/api-types.ts` and `agent-ui/src/api-types.ts`, and
update the committed `src/Server/openapi.json` snapshot.

### 6. Give engines and leases an explicit lifetime — WA-06

1. Make `SyncEngine` disposable/cancellable and stop all lease timers when retired.
2. Record which connection/engine acquired each lease so exit releases through the same identity.
3. During a connection transition, either release active old-origin leases before retiring the
   engine or stop renewal and report that they will expire.
4. Prevent an async renewal callback already in flight from rescheduling or reporting success after
   disposal.
5. Test server A lease acquisition → settings change to B → game exit, then observe both servers for
   longer than a renewal interval accelerated for the test.

**Acceptance:** no retired engine makes another request and no old-origin lease is renewed after the
connection change completes.

### 7. Fail closed on save-lock contention — WA-07

1. Never proceed unlocked after the timeout for archive, restore, or sync-state writes.
2. Make lock acquisition cancellable and return a typed failure the tray/CLI/dashboard can report.
3. Size the wait policy around the real maximum operation or hold an ownership protocol that does
   not depend on an arbitrary 30-second guess.
4. Add a deterministic tray + CLI test where one process holds the game lock through a 120-second
   settle while the other attempts pull and push.

**Acceptance:** the second process waits or fails without touching the save directory, archive, or
per-game sync state.

### 8. Make Windows enrollment produce usable lifecycle metadata — WA-08

1. Carry a process-name candidate through discovery and enrollment where it is unambiguous
   (especially a non-Steam shortcut's executable).
2. For ambiguous Steam/install/save-root candidates, expose an editable process-name field or
   clearly mark process lifecycle sync as unconfigured.
3. Persist the value through local enrollment, server adoption, and later edits.
4. Test GUI enrollment of a shortcut and an ambiguous install, then verify the watcher map after
   restart.

**Acceptance:** the UI cannot claim automatic launch/exit sync for a game whose persisted process
mapping is empty.

### 9. Define one owner for WinForms and live Agent state — WA-09

1. Capture a real `WindowsFormsSynchronizationContext` only after it is installed, or create a
   hidden/control handle whose dispatcher owns every WinForms object.
2. Route menu/icon/window/watcher rebuilds and shutdown through that dispatcher.
3. Stop sharing a mutable `List<TrackedGame>` across Kestrel, poller, timer, and UI threads. Use
   immutable snapshots, a synchronization boundary, or host-thread application of results.
4. Invoke `FolderBrowserDialog` on the UI STA thread, or use an owner HWND without reading WinForms
   collections from the picker thread.
5. Dispose every `Process` returned by watcher polling and prevent overlapping timer polls.
6. Stress test enrollment, removal, reconciliation, folder selection, update-menu refresh,
   notification, and Exit while process/folder timers continue firing.

**Acceptance:** all WinForms objects have one owning message-loop thread, state enumeration cannot
race a mutation, and the stress test produces no cross-thread, collection-modified, or shutdown
exception.

### 10. Report Windows integration failures honestly — WA-10

1. Check `SetEnabled` before saving/responding success and return an actionable API error on failure.
2. Make `IsEnabled` verify that the Run value resolves to the current Agent executable, not merely
   that a non-empty value exists.
3. Keep the UI toggle reverted when registry creation/deletion fails.
4. Test access denied, missing executable, stale install path, enable success, and disable success
   through an injected registry/launcher abstraction.

**Acceptance:** the UI never says Start with Windows is enabled or disabled unless the effective Run
entry matches that state.

### 11. Isolate discovery-source failures — WA-11

1. Catch and log failures at the individual Steam user/library/manifest/root boundary.
2. Continue returning candidates from healthy sources when one drive disappears or one directory is
   unreadable.
3. Dispose registry keys deterministically.
4. Test an inaccessible Steam-user config, an ejected library between enumeration steps, malformed
   VDF, and an inaccessible common-save-root child.

**Acceptance:** one bad discovery source is reported and skipped; it cannot fail the complete rescan
or prevent manual setup.

### 12. Preserve first-open navigation intent — WA-12

1. Queue the requested route until WebView2 initialization completes.
2. Make later deep links replace the pending route deterministically.
3. Test the fresh first-run Yes path with a cold WebView2 profile.

**Acceptance:** accepting the registration prompt opens Settings on the first window creation, not
Overview.

---

## Verification

Stop every running Agent/server first. Always build with `--no-incremental`.

```powershell
dotnet build src/Agent/SaveLocker.Agent.csproj --no-incremental
.\tests\run-agent-tests.ps1
.\tests\run-local-api-tests.ps1
.\tests\run-concurrency-tests.ps1
.\tests\run-hardening-tests.ps1
.\tests\run-enrollment-tests.ps1
.\tests\run-enrollment-tls-tests.ps1
```

Add a Windows-specific bug-bounty harness for the process, updater, ACL, registry, and STA cases
that the existing CLI-oriented suites cannot express. It must run against isolated server ports and
state directories and fail against the pre-fix code.

Also verify on a clean Windows 11 VM with two non-admin users:

1. Install and enroll as user A; reboot; confirm autostart and a cold first-open Settings deep link.
2. As user B, fail to read `config.json` and `api-token`, fail to call the local API, and fail to
   interfere with user A's locks.
3. Enroll a non-Steam shortcut through the UI and confirm its process mapping survives restart.
4. Start the game before the Agent, while the Agent is running, and during an Agent restart/update;
   confirm no restore occurs under the live process.
5. Hold a save write open for longer than 30 seconds while tray, CLI, and dashboard sync requests
   overlap; confirm only one operation owns the game and no unlocked fallback occurs.
6. Map the profile root and Agent state directory through every available surface; confirm sync
   refuses both before reading or writing them.
7. Switch from isolated server A to B during an active lease and with an update cached; confirm no A
   request, lease renewal, credential, or installer execution occurs after the transition.
8. Deny HKCU Run-key writes and confirm the API/UI report failure and preserve the previous toggle.
9. Make one Steam library inaccessible and confirm healthy discovery sources still populate.

## Done when

- Every finding has a regression test that fails against the pre-fix code.
- No pull or restore can run under an active Windows game process.
- Save-root validation is enforced again at the destructive sync boundary.
- Windows credentials pass a real second-user ACL test after fresh install and silent update.
- Server changes and updater downloads are origin-bound, transactional, and integrity-verified.
- Retired engines make no requests; lock contention never falls through unlocked.
- GUI enrollment produces explicit lifecycle metadata or clearly reports that it cannot.
- WinForms and live tracked-game state have documented, tested ownership.
- All existing Windows, Linux, cross-OS, hardening, health, local-API, concurrency, and enrollment
  suites remain green.
- Any changed API has regenerated client types and updated OpenAPI snapshots.
- New Windows identity, ACL, process-lifecycle, and thread-ownership rules are recorded in
  `Decisions.md` or `Gotchas.md`.
