# Backlog

Not-yet-done work only. Shipped items are indexed in `logs/shipped-2026-07.md`
(full detail in `logs/sessions.md`).

---

## High priority

- **Device-verify fresh Windows installer enrollment.** The wizard shipped in v0.1.7; the upgrade path is well verified. The **fresh install** (clean box, no `%PROGRAMDATA%\SaveLocker`) has never been exercised. Scenarios archived in `logs/2026-07-14_installer-enrollment.md`:
  - Happy path: run installer, choose enrollment file → page shows server + machine name → install → machine appears online in Machines.
  - ACL trap: `icacls "%PROGRAMDATA%\SaveLocker"` — interactive user needs Modify.
  - Expired-token, skip, and `/SILENT /ENROLL="C:\path\policy.json"`.

## Medium priority

- **Windows: `%PROGRAMDATA%\SaveLocker` ACLs on a multi-user box.** The local API token (`api-token`) and `config.json` (machine key) both inherit the ACL set by the installer — another local user may be able to read them. Fix: tighten the directory ACL to the enrolling user + SYSTEM, or move mutable per-user state out of the machine-wide directory. `run-local-api-tests.ps1` only asserts the file exists on Windows — give it a real ACL assertion once the model is decided.

- **Linux agent secret permissions and state layout.** `config.json` contains a long-lived machine key; file privacy depends on the launching shell's umask. Enforce `0700` on private state directories and `0600` on config, queue, health, and log files in code, including CLI enrollment paths. Consider separating immutable app files from mutable XDG config/state so upgrades cannot overlap the executable tree.

- **Linux auto-update.** The update channel (`/api/agent/latest`) is installer-shaped and Windows-only. A Deck user currently re-runs `install.sh` from a newer tarball. Worth doing before there are many Deck users — a headless device that never updates is one nobody will notice is stale.

- **Harden the `systemd --user` unit.** Add and Deck-test: `UMask=0077`, `NoNewPrivileges=yes`, `PrivateTmp=yes`, `ProtectSystem=full`, `RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6`, `RestrictSUIDSGID=yes`, `LockPersonality=yes`. Mirror in both `packaging/linux/savelocker.service` and `SystemdAutoStart.UnitFile()`. Do **not** add `ProtectHome` (save access), `ProtectProc` (Linux writer probe), or `MemoryDenyWriteExecute` (.NET JIT). Record `systemd-analyze --user security savelocker.service` before/after on a Deck.

- **Linux release provenance.** Pin GitHub Actions in `release.yml` to full commit SHAs; publish SHA-256 checksums and a GitHub artifact attestation for the tarball; use draft → attach all assets → publish flow. Document the verification command beside the Deck install instructions.

- **Constrain external manifest paths.** The Ludusavi manifest is downloaded from mutable `master`; expanded templates are not proven to stay inside the intended Proton prefix. Pin or integrity-verify an approved manifest revision, canonicalize resolved paths, reject `..`/symlink escapes outside allowed roots, test a hostile manifest entry. Preserve explicit manually mapped portable-save paths as a separate trusted-user path.

- **Deferred: one state owner for the Linux agent** — wrapper→daemon IPC over a Unix socket, standalone fallback when no daemon is up. The locking in `Decisions.md` §8 makes the current two-owner model *correct*; IPC would make it *simple*. Worth doing before the state files grow further.

## Stretch — Deck UI: Left-to-menu navigation

**Symptom.** In `savelocker ui`, pressing Left returns the cursor to the left rail only from
Overview. From Add game, Steam setup and Settings it does nothing; **B** works as the way back.
Everything else about the navigation is correct: Right crosses into the content and lands on its
topmost control, and Up/Down stay inside the pane they started in.

**Why it is not a small fix.** The rail and the content are separate ImGui child windows.
`SetKeyboardFocusHere` cannot reliably take the nav cursor away from a widget in a *different*
flattened child, and ImGui 1.90.8 exposes no public API to set the nav cursor directly. Eight
approaches were tried and measured (2026-07-25): re-asserting the request across frames, suppressing
the source pane with `NoNav`, `SetWindowFocus` on the target, disabling the source pane for the
hand-off frame, and upgrading ImGui.NET. Each either did nothing or traded one working case for
another. Full record in `logs/2026-07-25_deck-ui-visual-refresh.md`.

> ⚠️ **Do not retry the ImGui.NET upgrade expecting a fix.** 1.91.6.1 was tried and reverted. It
> restores and runs fine (Silk.NET 2.22's controller is runtime-compatible, and the API renames are
> mechanical), but it did **not** fix Left **and it broke the Right cross** that works on 1.90.8.

**The approach worth trying.** Stop using child windows for the rail and the content: draw both
directly in the root window with manual positioning. One nav scope, no flattening, so
`SetKeyboardFocusHere` behaves as it does at start-up — the one case that has always worked. The
cost is hand-rolling the clipping and scrolling the child windows currently provide free, which the
discovered-games and tracked-games lists both need.

## Planned / future

- ~~**Linux add-game streamlining — Phase 3.**~~ **Done — shipped 2026-07-24** (PR #25 + artwork PR #26). Gamepad-native `savelocker ui` (SDL + Dear ImGui in the existing binary), verified on a real Deck through the full cold flow. Archived design + rejected alternatives in `logs/2026-07-24_linux-agent-streamline.md`; §2 amendment in `Decisions.md`.

- **Game Mode UI reflects a stale game list.** `savelocker ui` only *reads* local `config.json`; it never reconciles with the server (only the daemon does, every 20s — `CommandPoller.ReconcileGamesAsync`). So a game deleted in the console still shows in Game Mode until the daemon runs, and there is no in-UI way to untrack. Deferred 2026-07-24 (maintainer chose to keep Phase 3 lean). Fix when revisited: reconcile-on-launch (+ periodic) in `savelocker ui`, optionally a per-game "Stop tracking" that also deletes server-side so the daemon does not re-adopt it (`CommandPoller.cs:157`).

- **Registry-based saves** — the Ludusavi manifest has a `registry:` section; only `files:` paths are handled.
- **Multi-directory saves** — some games list multiple save paths. The sync engine tracks one `SaveDirectory` per game; multi-dir support needs a schema change.
- **File-count / newest-mtime delta in conflict UI** — would help disambiguate conflict options. The server does not store it; needs computing at upload time or deriving from the archive on demand. Not done; everything else in conflict Tier 1 is complete.

_Dropped items (won't-do) are recorded in `logs/shipped-2026-07.md`._
