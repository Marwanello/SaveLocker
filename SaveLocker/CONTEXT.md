# SaveLocker — Session Context

**What:** Self-hosted Windows/Linux game save sync. Hub-and-spoke: a tray/headless agent on each
PC or Steam Deck syncs saves through an ASP.NET Core server (Docker on unRAID). React admin
dashboard + embedded React agent UI + a gamepad-native Deck Game Mode UI. See [[Architecture]].

**Repo:** https://github.com/SkorcherX/SaveLocker | **Branch:** main

**Current released version:** **v0.5.0** (released 2026-07-29). GHCR package `savelocker` is
**public** — see [[Gotchas]].

---

## Status

**In flight: `fleet-version-and-deck-focus` (4 commits, local, not pushed, no PR).** Two reported
faults, both fixed, plus the harness change that proved one of them.

1. **The fleet reported three agent versions for two releases** — `v0.5.0` and `v0.5.0.0` are the
   same build. The heartbeat sent `Version.ToString()`, which prints as many components as it parsed
   from: Windows reads the four-part PE version resource, Linux the three-part
   `AssemblyFileVersion`. Everything now goes through one `UpdateChecker.CurrentVersionText`
   (`Major.Minor.Patch`), and `versionSkew.normalizeVersion` collapses the old strings console-side
   so a fleet mid-upgrade stops being flagged as mixed. `v0.4.1.0` is a genuinely old agent — that
   machine still needs the update pushed.
2. **The Deck's double highlight** — hover and focus paint the same ring, and gamescope always hands
   the app a pointer position, so a stationary pointer painted a second selector the D-pad could not
   move and A did not activate. Gated on `Widgets.PointerDrives`. This alone explains "A does
   nothing": focus was on Overview the whole time, so A re-selected the screen already shown.
3. **`--pointer X,Y`** added to `savelocker ui` + `run-ui-wslg.sh`, because none of §2 can reproduce
   under WSLg without it. See [[Build and Run]] and the three new [[Gotchas]] → ImGui entries.

**Verified under WSLg, A/B against pre-fix hover semantics** (screenshots, `--nav-debug` overlay
read out of each): pointer on Quit → before, Overview *and* Quit ringed while focus reads
`rail:Overview`; after, Overview only. `--nav right` with the pointer on a game row → before, the
banner's dismiss X *and* the row; after, the focused control only.

⚠️ **One change in that branch is unproven: the removal of the rail's first-frame
`SetKeyboardFocusHere` seed.** The reasoning is sound (it cannot place a cursor from a
`NavFlattened` child, and it resolved on top of `RecoverStrandedCursor`'s request in the same frame)
but **WSLg does not reproduce the failure** — a settled screenshot is captured long after the
recovery invariant has repaired it, and both builds open on `focus: rail:Overview, request: none`.
`RecoverStrandedCursor` covers the case either way. Drop that commit if you would rather not carry
an unverified change; the hover fix does not depend on it.

**v0.5.0 is out and everything is merged to `main`.** It carries three bug bounties — console/server
(13 findings), Linux/Deck agent (9), Windows agent (12) — as PR #30, plus the release notes as
PR #31. Tag `v0.5.0` → `4944d8f`; both release jobs green; the GitHub Release carries the
hand-written notes and both installers.

**It shipped with its manual verification unrun.** That was a deliberate call, not an oversight, and
the release notes are worded accordingly. Paying that debt down is the next work — see below.

Everything before this is indexed in `logs/shipped-2026-07.md` + `logs/sessions.md`.

## Next action

**Land `fleet-version-and-deck-focus` first** — decide on the unproven seed-removal commit above,
open the PR, and add the Deck-only checks below to the verification list. Then continue with the
post-release work.

**Post-release verification, in this order.** Nothing here is a code change; it is confirming that
what shipped does what the notes claim.

0. **The two Deck fixes on hardware.** WSLg proved the hover fix and cannot prove the frame-0 one.
   On device: open the UI cold and confirm exactly one ring with A working immediately, then rest a
   trackpad cursor somewhere and confirm no second selector appears. Once agents are updated,
   Console → Config must show a single fleet version.

1. **Run `tests/linux/run-linux-tests.sh` in WSL** (40 checks). It holds the *only* tests for the
   Linux auto-start and `doctor` fixes, both of which shipped in v0.5.0 untested. Needs the ext4
   clone — see [[Gotchas]] → WSL.
2. **Deck verification** of the Linux bounty — the five scenarios in
   `logs/2026-07-29_linuxagent-bugbounty.md` → Verification. Hardware is available (2026-07-19).
3. **The second-Windows-account ACL test** (WA-03). This is the one with a user-visible consequence:
   until it runs, the v0.5.0 notes describe the permission change rather than promising other users
   cannot read the credentials, and Known Issues says so. **Reword `web/src/releases/0.5.0.md` once
   it passes** — that file is both the console page and the GitHub Release body, so editing it fixes
   both at once.
4. The rest of the Windows manual gates: fresh-VM install, a real game, a real non-Steam shortcut,
   the first-run Settings deep link on a cold WebView2 profile.
5. **The LAN enrollment-URL check** on the real deployment (`logs/2026-07-27_console-bugbounty.md`
   → Verification).

Missing regression tests (not blocking, but they are the reason some of the above is manual):
LA-04/05/06/07 have code fixes and no tests.

## Deploying v0.5.0

**Back up `/data` first.** Two schema migrations run on first start, and this is the first release
where downgrading the container is not a clean rollback. `ghcr.io/skorcherx/savelocker:latest`
already carries them — it was published on merge, before the tag.

The console must be redeployed. Windows agents self-update once the installer is uploaded to the
console (the GitHub Release asset is *not* automatically what the fleet is offered). Deck users
re-run `install.sh` from the newer tarball.

## Open work

See [[Backlog]] for the prioritized list. Nothing is in flight on `main`, and `SaveLocker/tasks/` is
empty **here** — but see the parked task below before assuming there is none.

### Parked on a branch — `offline-backoff-task` (`bee3116`, pushed, not merged)

**`tasks/OfflineBackoff.md` and its Backlog entry exist only on that branch**, by choice
(2026-07-29): the task was written but the work was deferred, and the maintainer did not want it
landing on `main` yet. `git checkout offline-backoff-task` to pick it up.

The task: an agent that cannot reach the server retries forever at a fixed cadence — the offline
drainer every 30 s, *re-archiving the save folder each time before it finds out*, and the command
poller every 20 s carrying the reconcile, the command fetch and the heartbeat. Roughly five failed
requests a minute, indefinitely. On a Deck away from home that is battery and metered traffic spent
on nothing. Note the scope: **backing off only the drainer does not achieve the goal**, which is why
the task covers all three loops.

### Suite baseline (all green at 2026-07-29)

Windows, local: **win agent bug bounty 114** · server bug bounty 145 · agent 45 · hardening 33 ·
local-api 30 · concurrency 23 · health 19 · enrollment 18 · enrollment-TLS 6.
Linux, in CI: agent 43 · hardening 37 · local-api 30 · concurrency 23 · health 19 · enrollment 16.
The two platforms differ by design — each suite skips the other's cases.

Server build 0/0. Console lint and build clean; the Windows agent build has **one** pre-existing
warning (MSB3277, a WindowsBase 4.0/5.0 conflict from WebView2) — a second one means something.

`run-winagent-tests.ps1` owns :5189 (+ :5190–:5198) and `.verify-winagent`. It is slow by design —
~5 minutes, most of it real waits on lease renewal, lock contention and WebView2 startup. **It drives
two real tray processes**, so it needs an interactive desktop session and skips those blocks without
one.

**Before running any suite, read [[Gotchas]] → Testing and Test harness.** Two traps cost hours this
session and both are documented there: clear `.verify/` and `src/Server/localstate/` *together*, and
start the dev server with `ASPNETCORE_ENVIRONMENT=Development` *and* explicit `Storage__*` or it
opens `/data/savelocker.db` (i.e. `E:\data\`) and hangs on a stale migrations lock.

---

## Where to look

| Topic | File |
|-------|------|
| Codebase map | [[REPO_MAP]] |
| System design | [[Architecture]] |
| Locked decisions (don't re-litigate) | [[Decisions]] |
| Recurring traps | [[Gotchas]] |
| REST endpoints | [[API Reference]] |
| Dev build & run commands, test suites | [[Build and Run]] |
| Agent CLI | `web/src/help/cli-reference.md` (KB article) |
| Active backlog | [[Backlog]] |
| Shipped release notes | `web/src/releases/*.md` (the console page *and* the GitHub Release body) |
| Windows bounty write-up | `logs/2026-07-29_winagent-bugbounty.md` |
| Linux bounty write-up | `logs/2026-07-29_linuxagent-bugbounty.md` |
| Console bounty write-up | `logs/2026-07-27_console-bugbounty.md` |
| Session history | `logs/sessions.md` |
