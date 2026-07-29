# Task — Console / Server Bug Bounty — **COMPLETE 2026-07-27**

All 13 findings fixed, one commit each, on `linux-agent-bugbounty` (**not pushed**):

| ID | Commit | ID | Commit |
|---|---|---|---|
| CS-01 | `a761f3f` (+ notes `c8db787`) | CS-07 + CS-08 | `60ed218` |
| CS-02 | `d46dcd6` | CS-09 + CS-10 | `400a4fa` |
| CS-03 | `32ffe8e` | CS-11 + CS-12 + CS-13 | `99648cb` |
| CS-04 | `7f4fa4a` | | |
| CS-05 | `7e12fc5` | | |
| CS-06 | `312ed77` (**scoped** — see §6) | | |

`tests/run-server-bugbounty-tests.ps1` is new: **145 checks, all green**. Every finding except the
ones noted below was reproduced against the pre-fix build at `961ad35` before being fixed.

**Two things this task changed its mind about, both because an existing suite objected:**
- **CS-04** first closed *every* open conflict on Set as Latest, which broke six `run-agent-tests`
  checks that deliberately cover Set-as-Latest-then-resolve. Narrowed to conflicts that offer the
  chosen version, so the "resolution may not rewind a newer Latest" guard stays armed.
- **CS-06** first refused *any* loopback enrollment URL, which broke `run-enrollment-tests` (14/18)
  and `run-enrollment-tls-tests` (0/6) — agent and server on one box is a real setup. Narrowed to
  *inferred* loopback; a stated one is honoured, and both suites now state their URL.

**Found while fixing, not in the original review:** the SteamGridDB key "verification" probed
`search/autocomplete`, which SteamGridDB serves **without a key** — 25 characters of nonsense
returned 200 with real data, so it only ever proved the site was reachable. Now probes
`grids/game/{id}`. (`curl` and `Invoke-WebRequest` get 401 there; that is Cloudflare rejecting their
user agent, not the API rejecting the key. Do not use either to test this.)

**Deliberately not covered by tests**, both by agreement rather than oversight:
- an injected database failure mid-upload (CS-03) and mid-registration (CS-10). The rollback and the
  transaction are what make them safe; testing them needs a fault-injection hook in production code.
- CS-12 hash navigation and CS-13 the font import — there is no front-end test runner in `web/`.
  Both were verified in the running console; CS-13 is also enforced by the build being warning-free.

**Still outstanding:** the one manual LAN check in Verification below (mint an enrollment file from
the real console at its LAN address and confirm `policy.serverUrl`), and the console redeploy.

---

**Created:** 2026-07-26
**Target:** `src/Server/`, `web/`, and the agent command/update paths coupled to their APIs
**Goal:** fix the server and web-console correctness defects found during the 2026-07-26 review,
preserve existing save history during upgrades, add regression coverage for every failure mode, and
verify behavior through the real HTTP API.

The reviewed server and console compile cleanly. The defects below are runtime, persistence,
concurrency, proxy, and UI-state failures rather than compiler errors.

Review verification:

```powershell
dotnet build src/Server/SaveLocker.Server.csproj --no-incremental
cd web
npm run lint
npm run build
```

Results:

- Server: **0 warnings, 0 errors**.
- Console lint: **passed**.
- Console production build: **passed**, with one CSS warning recorded as CS-13.
- Focused HTTP probes reproduced CS-01 and CS-02 against an isolated SQLite database.

---

## Severity

| Severity | Meaning |
|---|---|
| **P0 — critical** | Deletes or makes server save history inaccessible while the console explicitly promises it will be preserved. Fix before any further release or machine deletion. |
| **P1 — high** | Can lose a requested sync action, defeat conflict prevention, strand agents on stale heads, break enrollment/update URLs, or exhaust persistent storage. |
| **P2 — medium** | Can discard working configuration, consume a credential without completing enrollment, or leave a feature in a misleading/broken state. |
| **P3 — low** | Misreports state, breaks browser navigation, or causes a visible build/UI-quality regression without directly changing save data. |

## Findings

| ID    | Severity | Finding                                                                                                                                                                                                                                                                                                                                                        | Primary locations                                                                                                                                                            |
| ----- | -------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| CS-01 | **P0**   | Deleting a machine cascades into its `SaveVersion` rows even though the service and confirmation dialog promise those versions are kept. The game can retain a dangling head, version history becomes inaccessible, and archive files are orphaned.                                                                                                            | `src/Server/Services/SyncService.cs:87`, `src/Server/Data/Entities.cs:67`, `src/Server/Migrations/AppDbContextModelSnapshot.cs:543`, `web/src/components/ConfigView.tsx:151` |
| CS-02 | **P1**   | Agent commands are marked `Dispatched` before execution and are never reclaimed. A crash or lost result response permanently loses the command. The read-then-update claim is also not atomic across two agent processes, so the same command can be returned twice.                                                                                           | `src/Server/Services/SyncService.cs:892`, `src/Agent.Core/CommandPoller.cs:335`, `src/Agent.Core/CommandPoller.cs:420`                                                       |
| CS-03 | **P1**   | Save uploads write directly to their final archive path before the database transaction succeeds. Cancellation, a 413, or a DB failure can leave partial/orphan archives; file deletion before DB commits can create the inverse state. Repeated rejected uploads can fill persistent storage.                                                                 | `src/Server/Services/ArchiveStore.cs:37`, `src/Server/Services/SyncService.cs:414`, `src/Server/Services/SyncService.cs:541`                                                 |
| CS-04 | **P1**   | Head changes outside manual conflict resolution do not propagate. `NewestWins` / `PreferMachine` advance the head without queuing pulls, and **Set as Latest** only moves the pointer despite the console promising machines will pull. Stale agents can immediately re-conflict or auto-promote stale content on their next push.                             | `src/Server/Services/SyncService.cs:451`, `src/Server/Services/SyncService.cs:808`, `src/Server/Services/SyncService.cs:849`, `web/src/components/GameDetail.tsx:176`        |
| CS-05 | **P1**   | Lease acquisition is a read-then-insert against a unique game index. Simultaneous launches that both observe no lease can make one request fail with a database exception/500 instead of returning `Granted=false`, exactly when lease-based conflict prevention is needed most.                                                                               | `src/Server/Data/AppDbContext.cs:28`, `src/Server/Services/SyncService.cs:324`, `src/Server/Services/SyncService.cs:338`                                                     |
| CS-06 | **P1**   | Enrollment files and hosted-installer metadata build absolute URLs from Kestrel's request scheme/host without forwarded-header handling. Behind an HTTPS tunnel or reverse proxy, policies and update responses can contain an internal or `http://` URL that agents cannot reach or trust.                                                                    | `src/Server/Program.cs:355`, `src/Server/Program.cs:595`                                                                                                                     |
| CS-07 | **P1**   | The installer upload endpoint explicitly removes the request-body limit even though the API promises 200 MB. `ReadFormAsync` can buffer an arbitrarily large multipart body to memory/temp storage; before an admin password is configured, any reachable client can drive this.                                                                               | `src/Server/Program.cs:619`, `SaveLocker/API Reference.md:88`                                                                                                                |
| CS-08 | **P2**   | Replacing the hosted installer deletes every existing `.exe` before the new stream finishes, writes directly to the final name, updates metadata separately, and has no lock shared by manual upload, manual GitHub fetch, and the background poller. A failed or concurrent replacement can remove the last working installer or publish mismatched metadata. | `src/Server/Services/AgentInstallerService.cs:34`, `src/Server/Services/AgentInstallerPollerService.cs:69`, `src/Server/Program.cs:619`                                      |
| CS-09 | **P2**   | A SteamGridDB key is persisted before it is verified. The endpoint still returns HTTP 200 with `{ ok: false }`, and the console ignores `ok`, clears the input, and refreshes into a misleading “configured” state. A bad paste also overwrites a previously working key.                                                                                      | `src/Server/Program.cs:457`, `src/Server/Services/SettingsService.cs:43`, `web/src/components/ConfigView.tsx:103`                                                            |
| CS-10 | **P2**   | Enrollment redemption burns the single-use token in a separate transaction before machine registration/key issuance. If registration or the later DB writes fail, the client receives no machine key but the policy can never be retried.                                                                                                                      | `src/Server/Services/EnrollmentService.cs:181`, `src/Server/Services/EnrollmentService.cs:193`                                                                               |
| CS-11 | **P3**   | The console's “never reported” machine state is unreachable. The health API returns a DTO for every machine, so `!h` is false even when `lastHeartbeat` is null; newly enrolled agents render as `offline since —`.                                                                                                                                            | `src/Server/Services/HealthService.cs:175`, `web/src/components/ConfigView.tsx:708`                                                                                          |
| CS-12 | **P3**   | Hash navigation only handles Help and What's New changes after mount. Browser Back/Forward between Games, Configuration, and Audit changes the URL without changing the rendered view.                                                                                                                                                                         | `web/src/App.tsx:23`, `web/src/App.tsx:73`                                                                                                                                   |
| CS-13 | **P3**   | The production CSS build warns that the Google Fonts `@import` occurs after generated rules, so the browser discards it and silently uses fallback fonts.                                                                                                                                                                                                      | `web/src/index.css:1`                                                                                                                                                        |

### Reproduced failures

#### CS-01 — machine deletion destroys version records

Against an isolated server/database:

1. Register one machine.
2. Create one game.
3. Upload one version from that machine.
4. Delete the machine through `DELETE /api/machines/{id}`.
5. Query overview and versions again.

Observed:

```text
versions before delete: 1
versions after delete:  0
head DTO after delete:  null
total storage after:    0
archive files on disk:  1
```

The SQLite model snapshot explicitly configures the required `SaveVersion.MachineId` relationship
with `DeleteBehavior.Cascade`. `DeleteMachineAsync` never deletes the archive, so the database loses
the only index needed to reach a file the service promised to preserve.

#### CS-02 — a claimed command is lost without acknowledgement

Against an isolated server/database:

1. Queue a Pull command.
2. Poll `/api/agent/commands` once and do not post a result, simulating a crash/network loss.
3. Poll again.
4. Inspect `/api/commands`.

Observed:

```text
first poll:       command returned
second poll:      empty
persisted status: Dispatched
completed:        false
```

There is no expiry/requeue path for `Dispatched`. The agent comment that a failed report “will be
retried implicitly only if still pending” cannot apply because the GET already persisted
`Dispatched`.

---

## Execution order

Implement only the work below, then run the verification section and stop.

### 1. Preserve save history when deleting a machine — CS-01

1. Change the `SaveVersion` → `Machine` persistence model so deleting a machine cannot delete or
   invalidate its versions.
2. Choose and implement one durable historical-owner representation:
   - nullable `MachineId` with `ON DELETE SET NULL`, plus a stored uploader-name snapshot; or
   - a non-FK historical uploader ID/name that survives machine deletion.
3. Add a real EF migration for existing SQLite deployments. Do not edit only the model snapshot.
4. Preserve valid game heads, open-conflict version references, sizes, hashes, timestamps, archive
   paths, download behavior, and retention behavior.
5. Make DTO/UI rendering name the deleted uploader honestly instead of emitting an empty machine
   name.
6. Add a migration + HTTP regression:
   - seed a database with head, non-head, protected, and open-conflict versions from a machine;
   - migrate it;
   - delete the machine;
   - assert all versions and archives remain downloadable and the head/conflict still resolves.
7. Update the machine-deletion confirmation text only if the final behavior differs from the current
   preservation promise.

**Acceptance:** deleting a machine revokes its key and removes live machine-owned state, but cannot
change any game's version count, head content, conflict choices, storage total, or archive
downloadability.

### 2. Make command delivery crash-safe and single-claim — CS-02

1. Replace the permanent `Pending` → `Dispatched` claim with a visibility lease:
   - an unacknowledged command becomes eligible again after a bounded timeout;
   - completed/failed commands never reappear.
2. Claim commands atomically so two processes polling with one machine identity cannot both receive
   the same command in the same lease window.
3. Decide and document idempotency behavior for Pull, Push, Sync, and Scan. A retry after an unknown
   outcome must not silently turn a safe command into destructive duplication.
4. Keep claim attempts/lease expiry observable in the admin command list and audit trail.
5. Correct the false retry comment in `CommandPoller.SafeReportFailure`.
6. Add regressions for:
   - crash after GET but before execution;
   - execution succeeds but result POST is lost;
   - two simultaneous GETs;
   - normal completion and explicit failure.

**Acceptance:** a dashboard command is eventually completed/failed or remains visibly retryable; it
cannot disappear forever in `Dispatched`, and one lease window cannot hand it to two callers.

### 3. Make save-archive ingestion transactional at the filesystem boundary — CS-03

1. Stream uploads to a unique temporary file under the target archive volume.
2. Enforce the configured byte limit while copying, not only through Kestrel metadata.
3. Flush and atomically rename only after the stream completes.
4. Delete temporary/final files when cancellation, validation, or database persistence fails.
5. For delete/prune operations, commit database state and filesystem cleanup in a recoverable order.
   Record/log cleanup debt if a file deletion fails rather than leaving a live DB row pointing at a
   missing archive.
6. Clean stale temporary upload files on startup or a bounded maintenance sweep.
7. Add disconnect, oversized-body, canceled-copy, DB-failure, and prune/delete-failure regressions.

**Acceptance:** every committed `SaveVersion` has one complete archive; every incomplete or rejected
upload is removed; a DB failure cannot leak an unindexed final archive or leave a live version whose
file was already deleted.

### 4. Propagate every authoritative head change — CS-04

1. Centralize head advancement so normal upload, automatic conflict policy, manual conflict
   resolution, rollback, and **Set as Latest** share propagation rules.
2. For `NewestWins` and `PreferMachine`, enqueue an unforced Pull for every displaced live machine
   that needs its parent repaired. The winning uploader already updates its parent from the upload
   response and does not need a redundant command.
3. For **Set as Latest** / rollback, enqueue deduplicated unforced Pull commands for all relevant
   live machines.
4. Preserve the existing safety rule: automatic pulls stay unforced so unsynced local work is
   reported as blocked, not overwritten.
5. Decide how **Set as Latest** interacts with open conflicts; do not leave stale conflict rows that
   claim the newly selected head is unresolved.
6. Add two-machine tests proving the losing/displaced agent repairs its parent before its next push.

**Acceptance:** after any server-authoritative head change, every relevant agent either adopts that
head or reports a blocked pull; no agent silently remains on a stale parent until game launch.

### 5. Make lease acquisition atomic — CS-05

1. Acquire/replace an expired lease in one transaction or retry the unique-key race.
2. Return a normal `LeaseAcquireResponse` for the losing caller; do not surface a 500.
3. Ensure concurrent expiry cleanup cannot delete a freshly renewed/replaced lease.
4. Add a barrier-based integration test issuing simultaneous acquire requests from two machines.

**Acceptance:** exactly one simultaneous caller receives `Granted=true`; every other caller receives
`Granted=false` with the winner's lease, and no request fails with a database exception.

### 6. Produce reachable enrollment and update URLs — CS-06 (**scoped down 2026-07-27**)

**Scope decision (maintainer, 2026-07-27):** there is no Cloudflare Tunnel and no HTTPS. SaveLocker
is LAN-only over plain HTTP, syncing save files — not financial data or PII. The proxy half of this
finding therefore protects a topology that does not exist, and building it would add trust
configuration with nothing to test it against. Steps 1 (forwarded-header half), 2, 3 and the
reverse-proxy/HTTPS parts of 6 are **dropped**, along with the manual HTTPS-tunnel check in
Verification and its "Done when" line. Recorded in `Decisions.md`.

What remains is a real failure on a plain LAN: the URL is built from the request origin, so opening
the console at `http://localhost:5080` on the server box mints a policy telling the new machine to
sync with itself, and hands agents a `localhost` installer URL.

1. ~~Trusted-proxy / forwarded-header policy~~ — dropped. A single configured public base URL
   (`Server:PublicBaseUrl`) is implemented, and is also the answer if a proxy is ever added.
2. ~~Do not blindly trust `X-Forwarded-*`~~ — dropped; they are not read at all.
3. ~~Relative installer URL~~ — dropped; the installer URL shares the same resolver.
4. Make the enrollment console display the exact effective URL that will be written to the
   downloaded policy. ✅
5. Reject invalid, non-absolute enrollment overrides before minting a token. ✅
6. Add direct-HTTP, custom-port, explicit-override and configured-public-URL tests. ✅
   ~~HTTPS reverse-proxy tests~~ — dropped.
7. **Added:** refuse to mint when an *inferred* URL is loopback, since that file cannot work on the
   machine it is for. A stated loopback URL (override or `Server:PublicBaseUrl`) is honoured — agent
   and server on one box is a real setup, and both enrollment suites rely on it. ✅

**Acceptance:** the downloaded enrollment policy and update metadata always name an origin other
machines on the LAN can reach; an unusable one is refused at mint time rather than discovered when
enrollment fails.

### 7. Bound and atomically replace hosted installers — CS-07 and CS-08

1. Enforce the documented 200 MB request limit and a matching per-file limit before/during copy.
2. Validate `.exe`, a non-empty normalized filename, and a parseable supported version before
   touching the current installer.
3. Serialize manual upload, manual GitHub fetch, background polling, and delete with one shared gate.
4. Download/write to a unique temporary file, then atomically publish installer + metadata only
   after the new file is complete.
5. Keep the previous working installer and metadata intact on cancellation or failure.
6. Bound GitHub response size as well as browser uploads.
7. Add regressions for 200 MB boundary, oversized multipart, canceled upload, invalid file/version,
   concurrent manual/poller writes, and failed replacement with an existing installer.

**Acceptance:** no request can buffer/write an unbounded installer, and a failed/concurrent
replacement cannot remove or mismatch the last known-good installer.

### 8. Commit settings and enrollment credentials only on success — CS-09 and CS-10

1. Verify a candidate SteamGridDB key before replacing the stored key.
2. On rejection/network failure, retain the previous effective key and return a non-success HTTP
   result with a structured error.
3. Make the console inspect the response result, retain the user's input on failure, and refresh only
   after a confirmed save.
4. Make enrollment token consumption and machine registration/key issuance one atomic operation, or
   provide a safe compensating transaction that restores usability on failure.
5. Preserve the single-winner guarantee when two agents redeem one policy concurrently.
6. Add tests for invalid key over valid key, unreachable SteamGridDB, registration DB failure, and
   simultaneous redemption.

**Acceptance:** a rejected settings change cannot replace working configuration, and an enrollment
policy is never consumed unless one caller receives its machine API key.

### 9. Correct console state and navigation — CS-11, CS-12, and CS-13

1. Treat `lastHeartbeat == null` as **never reported** even though a health DTO exists.
2. Route every supported hash (`games`, `config`, `audit`, `help`, `whats-new`, and detail hashes)
   through one parser used at startup and on `hashchange`.
3. Verify browser Back/Forward updates both the highlighted navigation item and rendered view.
4. Move the external font import before generated CSS, or self-host/remove it. The production build
   must emit no discarded-`@import` warning.
5. Add lightweight component/browser tests for never-reported state and hash history.

**Acceptance:** a new machine says **never reported**, browser history never disagrees with the
visible page, and the production CSS build is warning-free.

---

## Verification

Stop any running agent/server first. Always build the server with `--no-incremental`.

```powershell
dotnet build src/Server/SaveLocker.Server.csproj --no-incremental
cd web
npm run lint
npm run build
cd ..
.\tests\run-server-bugbounty-tests.ps1
.\tests\run-agent-tests.ps1
.\tests\run-enrollment-tests.ps1
.\tests\run-health-tests.ps1
.\tests\run-concurrency-tests.ps1
```

`run-server-bugbounty-tests.ps1` is a new isolated-state harness required by this task. It must own
its ports and scratch directories, start/stop its own server, and cover at minimum:

1. migrated machine deletion with retained/downloadable versions;
2. crash-safe and concurrent command claiming;
3. partial/oversized archive and installer uploads;
4. two-machine automatic-policy and Set-as-Latest propagation;
5. simultaneous lease acquisition;
6. trusted reverse-proxy URL generation;
7. failed installer replacement preserving the old binary;
8. failed SteamGridDB verification preserving the old key;
9. failed/concurrent enrollment redemption.

~~Also perform one deployment-shaped manual check through the real HTTPS tunnel/reverse proxy.~~
**Dropped 2026-07-27** — there is no tunnel and no HTTPS (see CS-06 above). The LAN-shaped
equivalent, still worth doing once by hand on the real deployment:

1. open the console at the server's LAN address;
2. mint an enrollment file with the URL override blank;
3. confirm `policy.serverUrl` is that LAN address, not `localhost`;
4. query `/api/agent/latest` and confirm its download URL uses the same origin;
5. enroll a disposable agent and download the hosted installer.

If an API or DTO changes, regenerate `web/src/api-types.ts` and commit the updated
`src/Server/openapi.json` snapshot.

## Done when

- Every finding above has a regression that fails against the pre-fix implementation.
- The machine-delete migration preserves existing save history on a copy of a pre-fix SQLite DB.
- All new server-bug-bounty checks and existing agent/enrollment/health/concurrency suites pass.
- Server build, console lint, and console production build complete with no warnings.
- ~~HTTPS proxy verification passes without manually overriding the enrollment URL.~~ Dropped
  2026-07-27; replaced by the LAN check above.
- API/OpenAPI/generated TypeScript artifacts are synchronized.
- New persistence, command-delivery, proxy-trust, and file-publication rules are recorded in
  `Architecture.md`, `Decisions.md`, or `Gotchas.md` as appropriate.
