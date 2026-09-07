# Task: Linux / Steam Deck agent auto-update

From [[Backlog]] → Medium → *Linux auto-update*. Planned 2026-08-14.

**Four phases. Execute ONE phase per session, verify it, stop.** Each phase is independently
shippable and leaves the fleet working: 1 changes nothing for existing agents, 2 is read-only, 3
is the first one that moves files, 4 is policy and coverage.

---

## Decisions taken at planning time (maintainer, 2026-08-14)

1. **Stage automatically, apply on next daemon start.** The daemon downloads, verifies and stages
   whenever the server offers a newer version, but the file swap happens at the *next* start
   (boot/login). No mid-session restart, no prompt surface required on a device that has none, and a
   Deck is never more than one boot stale. `savelocker update` forces it immediately for anyone who
   wants it now.
2. **Do not move the state directory.** `AgentConfig.DefaultDir` and the install prefix are both
   `~/.local/share/SaveLocker`, so `config.json`, `api-token`, `offline-queue.json` and `agent.log`
   live *inside* the tree an update replaces. Apply is therefore an **in-place per-file copy**, never
   a directory swap — a swap would destroy this machine's server API key. Splitting app files from
   XDG state stays a separate Backlog item.
3. **Fold in two adjacent Backlog items:** *Harden the `systemd --user` unit* (Phase 3 already edits
   both unit sources) and *Linux release provenance* (Phase 4 — same trust story).

## Two things that will bite if forgotten

- **`systemctl --user stop` kills the entire cgroup.** An updater the daemon spawns as a child dies
  with the unit it just stopped — mid-swap. This is why the apply runs from `ExecStartPre` of the
  *next* invocation and not from a child of the daemon.
- **Overwriting the running tree is only safe via unlink-then-write.** Linux refuses to write a file
  it is executing (`Text file busy`), and the managed DLLs beside it *are* writable while mapped, so
  overwriting them SIGBUSes the running daemon. `cp --remove-destination` gives each replacement a
  **new inode**, so a live `savelocker run` wrapper keeps the old one and survives. `install.sh`
  already learned this the hard way — read its comments before writing the apply step.

---

## Phase 1 — Server: the installer store becomes platform-aware — **DONE 2026-08-15**

Ships alone. An agent that sends no `platform` gets exactly what it gets today.

**Outcome:** `run-server-bugbounty-tests.ps1` 145 → **164/164** (19 new checks, baseline updated in
[[Build and Run]]); `run-winagent-tests.ps1` unchanged. Server build 0/0, console lint and build
clean. Two things worth carrying into Phase 2: `?platform=` is bound as a real handler parameter
rather than read off `HttpRequest`, so it is documented in `openapi.json` — and `/api/agent/latest`
now returns a `downloadUrl` that **names the platform**, which is what stops an agent following it
to the other OS's package and failing a digest check it could never pass. The config fallback for
Linux lives under `AgentUpdate:Linux:*` (env `AgentUpdate__Linux__DownloadUrl`) — deliberately not a
dashed section name, because `AgentUpdate__linux-x64__…` is not a legal bash identifier and the
Linux harness has to export it.

**The hosted path and the config-fallback path pick a platform separately, and only one of them was
covered.** A rename left the fallback comparing the *raw* (null) parameter instead of the normalised
one, so every agent that sends no `?platform=` — which is all of them until Phase 2 — was routed to
the empty Linux section and got 204: the entire Windows fleet would have gone silently "up to date"
forever. Every platform check above still passed, because with a hosted installer present the
fallback never runs. WA-05's off-origin block caught it. Both paths now have their own checks, and
the fallback one was proven to fail against the broken build (204) and pass against the fixed one.

### Steps

1. `src/Server/Services/AgentInstallerService.cs` — introduce a platform slot.
   - Keep the current root as the **`win-x64` slot, untouched**, and add a `linux-x64/`
     subdirectory. Zero migration; `BackfillDigestAsync` and every deployed server keep working.
   - Thread a `platform` parameter through `GetInfo`, `SaveAsync`, `DeleteAsync`,
     `FetchLatestFromGitHubAsync`, `GetInstallerPath`, `SweepIncoming`, `BackfillDigestAsync`.
   - Per-platform validation, replacing the hardcoded `.exe` checks: `win-x64` → `*.exe`;
     `linux-x64` → `*.tar.gz`. The cleanup glob (`Directory.GetFiles(_root, "*.exe")`) is per-slot
     and per-extension too.
   - Reject an unknown platform string outright — it must never resolve to a path.
   - The single `SemaphoreSlim` still covers every writer. Do not split it per platform.
2. GitHub asset matching: `SaveLocker-Agent-Setup*.exe` for `win-x64`,
   `savelocker-*-linux-x64.tar.gz` for `linux-x64` (that is what `release.yml`'s `build-linux` job
   already attaches). `AgentInstallerPollerService` polls **both** slots per tick.
3. `src/Server/Program.cs` — `?platform=` on `/api/agent/latest`,
   `/api/agent/installer/download`, and the four `/api/admin/agent-installer*` routes.
   **Absent means `win-x64`**, so an agent from before this change is unaffected.
4. `src/Shared/Contracts.cs` — add `Platform` to `AgentInstallerStatus`.
5. `web/src/api.ts` + `web/src/components/ConfigView.tsx` — the Agent Updates card grows a Windows
   row and a Linux row (hosted version, size, uploaded-at, upload / fetch / delete per row).
6. Regenerate `src/Server/openapi.json` and `web/src/api-types.ts`; commit both (CLAUDE.md).

### Verify

- `tests/run-server-bugbounty-tests.ps1` — extend the installer block: a `.tar.gz` uploaded to the
  win slot is **refused**; an `.exe` to the linux slot is **refused**; an unknown `?platform=` is
  **refused**; `/api/agent/latest` with **no** platform param still answers with the Windows
  installer; the two slots do not overwrite each other.
- Suite must stay green at its recorded baseline otherwise. `Storage__AgentInstallerRoot` must be
  set for the run ([[Gotchas]] → Testing).

---

## Phase 2 — Agent: check-only, on both platforms — **DONE 2026-08-15**

Read-only. Nothing is executed or replaced. Safe to ship on its own.

**Outcome:** `run-linux-tests.sh` 69 → **84/84** (15 new checks), `run-winagent-tests` unchanged,
solution build 1 warning (the known MSB3277) / 0 errors, agent-ui build clean.

Both groups of new checks were proven to fail against the pre-change code — the discipline is worth
keeping for this feature specifically, because most of what it adds is *refusals*, and a refusal
test passes trivially against code that never gets far enough to refuse. Reverting only
`UpdateChecker.cs` fails 12 of 15; reverting only `Daemon.cs` fails exactly the 2 daemon checks.

Three things worth carrying into Phase 3:
- **The daemon's `/api/agent-version` is token-gated** (Decisions.md — reaching the local API is
  equivalent to owning the box). A test that forgets `X-SaveLocker-Token` gets a 401, which asserts
  identically to "the daemon has no update to report". Read the token from the config's state dir.
- The one check that does **not** discriminate on its own is "a good tarball downloads and
  VERIFIES": pre-change the agent asked with no platform, got the Windows `.exe`, and that passed
  the old MZ check. It is a happy-path check, not a guard; checks 1–3 are what pin the platform.
- The first update check runs on a **5 s** timer after daemon start, not inline — long enough for
  the API server and first reconcile to settle, short enough that opening the agent UI right after a
  restart does not show a blank update line.

**Not visually verified:** the agent UI's Updates panel renders only against a live agent on
`:5178`, and nothing was running. Its data contract is verified end-to-end by the suite (the daemon
really answers `updateAvailable: true` with the Linux version) and the component type-checks and
builds, but no browser has drawn it. Fold it into the Deck hardware pass ([[Backlog]]).

### Steps

1. `src/Agent.Core/UpdateChecker.cs`:
   - Send `?platform=` on `/api/agent/latest`, derived once from `RuntimeInformation`.
   - Replace `VerifyLooksExecutable`'s MZ assertion with a per-platform payload check — gzip magic
     `1f 8b` for the tarball. Keep the existing framing: it is a *sanity* check that turns a
     captive-portal HTML page into a comprehensible error; the digest is what proves authenticity.
     Carry the Authenticode TODO across unchanged.
   - Temp file extension follows the platform, and on Linux it is created `0600`.
   - Everything else stays: TOFU-pinned client for same-origin, no credential and a mandatory digest
     off-origin, 300 MB cap, delete on every failure path (Decisions → WA-05).
2. `src/Agent.Linux/Daemon.cs:85` — replace `getUpdateResult: () => null` (and its
   "self-update is Windows-only" comment) with a real update-result provider.
3. `src/Agent.Linux/Doctor.cs` — a line for current version vs. what the server offers, and (from
   Phase 3) whether an update is staged and pending a restart.
4. `agent-ui` Settings/Overview — surface the available version. The DTO
   (`AgentVersionDto`) already exists.

### Verify

- `tests/linux/run-linux-tests.sh` (currently 69 on this branch's baseline) gains, against its own
  fake server: `check-update` reports an offered version; `check-update --download` **VERIFIES** a
  good tarball; a digest mismatch is **REFUSED**; an off-origin URL with no digest is **REFUSED**
  (the Linux mirror of WA-05); a non-gzip payload is **REFUSED**.
- Confirm each new check fails against the pre-change binary — otherwise it tests nothing
  ([[Gotchas]] → Testing).

---

## Phase 3 — Agent: stage and apply — **DONE 2026-08-15**

The first phase that moves files. Read `install.sh` end to end before starting.

**Outcome:** `run-linux-tests.sh` 84 → **117/117** (33 new checks). Linux agent build 0/0. No shared
or Windows code was touched — `Agent.Linux`, `packaging/` and the Linux suite only — so the Windows
suites are unaffected and were not re-run.

**The two guards that matter were mutation-tested, not just exercised.** Disabling the tar
path-escape check and the smoke test's version comparison fails 15 checks. That is the test that
counts here: nearly everything Phase 3 adds is a refusal, and a refusal test passes trivially
against code that never reaches the refusal.

What the shape ended up being, and why:
- **The daemon never applies.** It stages — download, verify, unpack, smoke-test — and stops. The
  swap happens in `ExecStartPre` of the next invocation, a fresh process in the new cgroup with the
  old daemon already gone. `savelocker update` is the "now" path, and it is safe to restart from
  there precisely because it runs in the user's shell session rather than in the unit.
- **Old files are moved aside, not deleted.** A rename in the same directory is free, gives the
  replacement a new inode (so a `savelocker run` wrapper mid-game keeps running from the copy it
  mapped), and leaves a complete previous version to roll back to.
- **`applied.json` surviving into the next start IS the failure signal.** The daemon clears it once
  it is genuinely up; if the new build cannot start, the next `ExecStartPre` sees it and reverts.
  Rollback also removes files the failed version *added*, which is why the marker records them —
  restoring only what was replaced leaves the failed version's files scattered through the install.
- **`AppContext.BaseDirectory`, never `Environment.ProcessPath`.** Under `dotnet savelocker.dll` the
  process path is the dotnet host. The updater got this right from the start; `SystemdAutoStart` did
  not, and was writing `ExecStart=/…/dotnet daemon` — a unit that starts nothing. The suite caught
  it. Both now resolve the agent beside its own assemblies.
- **One systemd unit.** `packaging/linux/savelocker.service` is embedded in the assembly and
  `SystemdAutoStart` substitutes the exec path, so the two writers of that file cannot drift again.
  The hardening from [[Backlog]] is folded in; `ProtectHome`, `ProtectProc` and
  `MemoryDenyWriteExecute` are absent on purpose and the unit says why.
- **`install.sh` discards a staged update.** A hand-run install supersedes it — otherwise the next
  start would install a version the user did not choose, and a leftover `applied.json` would read
  as "the last update never started" and roll the hand-install back.

Traps this phase hit, all worth knowing before Phase 4:
- The suite reassigns `HOME` to the fixture tree, so `"$HOME/.dotnet"` inside a test resolves into
  the fake home. Derive a real `DOTNET_ROOT` from `command -v dotnet` instead. The
  framework-dependent apphost needs it; a released agent is self-contained and does not.
- `contains "$out" "installed"` matched the **rollback** message ("…was installed but never started
  successfully"), so a run that had just undone an install reported a successful one. Anchor
  assertions on text that only one outcome can produce.
- An unanchored `grep ProtectHome` matched the comment explaining why `ProtectHome` is absent.

**Deviation from the plan above:** the hostile-tarball coverage lives in `run-linux-tests.sh` rather
than `run-hardening-tests.ps1`. The extractor is Linux-only code and the Linux suite is where a real
hostile tarball can be built and fed to it; splitting it across a PowerShell suite that would have
to drive the Linux agent anyway buys nothing. Both cases named in the plan are covered — path escape
*and* a symlink entry, the latter refused outright rather than resolved.

### Steps

1. **Stage** (new code in `Agent.Linux`, driven by the daemon and by `savelocker update`):
   - Download + verify digest via the Phase 2 `UpdateChecker`.
   - Extract into `<StateDir>/update/staged/`. **The tarball is hostile input** — same trust class
     as a pulled save archive, and worse in that it becomes code. Reject `..`, absolute paths and
     symlink escapes; cap entry count and total bytes. Reuse the `SaveArchive` rules rather than
     writing a second set.
   - **Smoke-test before committing:** run `staged/savelocker --version` and require it to start and
     print the expected version. A staged binary that cannot execute is precisely the failure that
     leaves a Deck permanently offline with nobody watching.
   - Write `<StateDir>/update/apply.json` — version, staged path, staged-at.
2. **Apply** — `savelocker apply-update`:
   - Idempotent; a no-op with no marker; refuses a staged version that is not newer than the running
     one; clears the marker on success.
   - Copies **per file** with unlink-then-write semantics into `~/.local/share/SaveLocker`, touching
     only paths the tarball carries. `config.json`, `api-token`, `offline-queue.json`,
     `lease-warnings.json` and `agent.log` are never in the tarball and must survive — assert this.
   - Keeps a rollback copy. A marker still present on the *next* start means the last apply's start
     failed → revert and report.
3. **Restart orchestration:**
   - `ExecStartPre=…/savelocker apply-update` in **both** `packaging/linux/savelocker.service` and
     `SystemdAutoStart.UnitFile()`. These two currently write different units and have been drifting;
     make them one source of truth.
   - The daemon calls `systemctl --user restart --no-block savelocker.service` and exits. The apply
     then runs in the **new** invocation — a fresh process, not the cgroup being killed.
   - **No systemd (hand-started daemon):** do **not** attempt a self-restart. Leave the update staged
     and report "staged; run `savelocker apply-update`" through the health channel, `doctor` and the
     agent UI. A daemon that stops itself and cannot come back is a Deck going quietly offline —
     the exact failure `install.sh` shouts about today.
4. **Safety gate:** stage at any time; **apply only when no game is running** — no held lease and no
   live `savelocker run` wrapper. Otherwise defer to the next start.
5. **Fold in the unit hardening** ([[Backlog]] → Medium) while both unit sources are open:
   `UMask=0077`, `NoNewPrivileges=yes`, `PrivateTmp=yes`, `ProtectSystem=full`,
   `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6`, `RestrictSUIDSGID=yes`, `LockPersonality=yes`.
   **Not** `ProtectHome` (save access), **not** `ProtectProc` (the Linux writer probe), **not**
   `MemoryDenyWriteExecute` (.NET JIT). Note `PrivateTmp` interacts with the staging path — stage
   under `<StateDir>`, not `/tmp`.
6. No bootstrap problem to solve: `install.sh` always rewrites the unit, so the manual tarball
   upgrade to the release carrying this feature is what installs the `ExecStartPre` unit.

### Verify

- `tests/linux/run-linux-tests.sh`: apply swaps the tree and the new binary reports the new version;
  config/API key/queue/log survive an apply; a staged tree containing `../escape` is refused; a
  staged binary that cannot start is refused and nothing is swapped; apply is refused while a fake
  game is running; a marker left over from a failed start triggers rollback.
- `tests/run-hardening-tests.ps1`: tar zip-slip and symlink-escape on the update tarball.
- On the Deck: `systemd-analyze --user security savelocker.service` **before and after**, recorded in
  the write-up. Then a real end-to-end update against the live server.

---

## Phase 4 — Policy, surfaces, provenance, docs — **DONE 2026-08-15**

**Outcome:** `run-linux-tests.sh` 117 → **123/123**, `run-winagent-tests` **114/114** (this phase
touched shared code — `AgentEventCodes`, `AgentConfig` — so the Windows suite mattered), console
lint and build clean.

`AutoUpdate: false` was mutation-tested: it is a *negative* assertion ("stages nothing"), which
passes if staging failed to happen for any reason at all. Removing the gate fails exactly that one
check, which is what makes it worth having. It is paired with a positive one — the daemon must still
report being behind — so "opted out" cannot be confused with "stopped checking".

Everything in the plan landed except one deliberate substitution: the "Windows vs Linux" comparison
in the help article is a bullet list, not a table.

**Found while verifying: no markdown table renders anywhere in the console.** `HelpView.tsx:113` and
`WhatsNewView.tsx:100` use bare `<ReactMarkdown>`, and tables are a GFM extension needing
`remark-gfm`. `cli-reference.md` is almost entirely tables and is currently a wall of raw pipes for
every user. Pre-existing and out of scope here, so it is spun off as its own task rather than fixed
in passing — but the new article was written without tables so it is not shipping broken today.

### Steps

1. Schedule + opt-out: reuse `AgentConfig.LastUpdateCheck` and the 24 h cooldown shape the tray
   already uses; add a config toggle to disable auto-staging. `SkipVersion` already exists and
   applies unchanged.
2. `src/Shared/AgentEventCodes.cs` — codes for update staged / applied / failed, reported through
   `HealthReporter`. **This is the load-bearing surface on a Deck**: there is no toast, so the
   console is where an update outcome becomes visible (Decisions §2).
3. Game Mode `src/Agent.Linux/Ui/UiApp.cs` — the existing "Next step" card carries
   "Update staged — applies on next start".
4. **Linux release provenance** ([[Backlog]] → Medium), folded in: pin `release.yml`'s actions to
   full commit SHAs, publish SHA-256 checksums and a GitHub artifact attestation for the tarball,
   draft → attach all assets → publish. Document the verification command beside the Deck install
   instructions.
   <br>Worth stating plainly so nobody mis-sequences this: **the update channel does not depend on
   it.** The server hashes the bytes it stored, so the digest exists either way. This buys a user
   verifying a manual download, and it is what an off-origin `AgentUpdate:DownloadUrl` would need.
5. Docs:
   - `web/src/help/agent-update.md` — currently opens with "Auto-update is a Windows feature" and
     ends with a manual "Updating the Linux agent" section. Rewrite both.
   - `web/src/help/cli-reference.md` — `savelocker update`; `apply-update` is internal, mention it
     only as the unit's `ExecStartPre` hook.
   - [[Decisions]] — one entry for the apply mechanism (stage now / apply on next start via
     `ExecStartPre`, and *why* not a child process).
   - [[Gotchas]] — the cgroup-kill trap and the prefix/state-dir collision.
   - Release notes for whichever version ships it.

### Verify

Full suite sweep at the recorded baselines in [[Build and Run]], plus a real Deck update: an old
tarball installed by hand, the server serving a newer one, and the Deck arriving at the new version
across one restart with its enrollment, queue and tracked games intact.
