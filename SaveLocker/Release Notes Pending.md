# Release Notes — Pending

Accumulating draft for the **next release**. Three bug bounties are in flight — Console, Windows
agent, Linux agent — and they will ship together or close together, so the user-facing notes are
drafted here as each lands rather than reconstructed from git at tag time.

**Lift the "Draft notes" sections into `web/src/releases/<version>.md` when tagging.** That file is
also the GitHub Release body (`release.yml` `body_path`), so it is written once and cannot drift.
Match the voice of `0.4.1.md`: plain language, what the user noticed, no finding IDs.

> ⚠️ **Nothing below is released, and nothing is pushed.** Do not publish a bullet whose fix has not
> been verified on the platform it affects — see "Not yet verified" under each section.

---

## Linux / Steam Deck agent — done, unpushed

**Branch:** `linux-agent-bugbounty` (off `main`, 2 commits, not pushed)
**Commits:** `ba3f153` the fixes · `ab1f568` an unrelated vault condensation split out of it
**Source:** `tasks/LinuxAgent-BugBounty.md` — 9 findings (2 P1, 6 P2, 1 P3), all fixed
**Date:** 2026-07-26

### Draft notes

Nothing here changes the server, so the console does not need redeploying for these. On Steam Deck,
download the newer tarball and re-run `install.sh`.

#### Fixed

- **Changing the server address now moves all sync traffic to it.** Only some of it moved before:
  the agent kept uploading saves to the *old* server while talking to the new one about everything
  else, with nothing reporting a problem.
- **Removing a game from a device now actually removes it.** Game Mode said "Removed from this
  device" and the background service quietly added it straight back, usually within a minute. The
  game stays on the server for your other machines — this only stops *this* device syncing it.
- **`savelocker remove-game --name "<game>"`** — a new command, because on a Deck there was no way
  to stop syncing a game without the Game Mode UI.
- **Setting a save folder takes effect immediately.** The folder was saved but not watched until the
  agent restarted, so a game mapped after setup could sit there never syncing. The choice is also
  reported to the console right away instead of on the next check-in.
- **Adding several games at once no longer loses the ones that worked.** If a later game failed, or
  the window was closed part-way, the earlier ones were discarded — including the Steam AppID that
  makes launch-time syncing work.
- **Adding games no longer crashes the Game Mode window.** Adding while the list was on screen could
  close the app outright.
- **Changing a setting in Game Mode no longer undoes other changes.** Toggling interface sounds or
  the sync delay wrote back the whole configuration as the app had found it at launch, discarding
  anything that happened since.
- **Turning off "Start on boot" reports the truth.** It always claimed success, even where it could
  not work at all — so the service stayed enabled and running while the toggle showed it off.
- **`savelocker doctor` gives correct answers.** With a custom config path it reported and tested
  the wrong folder, and it described an un-enrolled device as "server unreachable", sending people
  to debug a network that was fine. It now separates *cannot reach the server*, *reached it but this
  device is not enrolled*, and *connected and healthy*.

#### For the curious

Two of these had the same shape: a long-lived background process holds the configuration it read at
startup, and writes it back later over changes it never saw. The fixes push the rule down into the
storage layer rather than asking every caller to remember it — the same approach taken for the
one-machine conflict loop in v0.3.x, applied to the game list.

### Not yet verified — do not publish these bullets without it

- **The Linux test suite has not been run.** `tests/linux/run-linux-tests.sh` (33 → 40 checks) carries
  the only tests for the *auto-start* and *doctor* fixes, and needs WSL on the ext4 clone.
- **No Steam Deck verification.** All five scenarios in `tasks/LinuxAgent-BugBounty.md` → Verification
  are outstanding.
- Regression tests are still missing for the folder-watcher, multi-game-add, window-crash and
  settings-write fixes. The first two P1 fixes *are* covered, and both tests were confirmed to fail
  against the pre-fix code.

Windows suites green at time of writing: agent 45, concurrency 23, local-api 30, health 19,
enrollment 18, hardening 33.

---

## Windows agent — not started

**Source:** `tasks/WinAgent-BugBounty.md`

Some Linux fixes are in shared code (`Agent.Core`) and therefore already apply to the Windows tray:
the server-address fix, the durable game removal, the crash-on-add fix, and the immediate
save-folder watching. Decide at tag time whether to describe those once or per platform — they are
one code change, not two.

## Console / server — in progress

**Branch:** `linux-agent-bugbounty` (the console fixes are landing on the same branch, not pushed)
**Source:** `tasks/Console-BugBounty.md` — 13 findings (1 P0, 6 P1, 3 P2, 3 P3)
**Fixed so far:** the machine-deletion data loss (P0) — `a761f3f`; lost dashboard commands (P1)

**This section changes the server, so the console must be redeployed** (`docker compose pull &&
docker compose up -d`). The Linux section above explicitly says it does not; do not merge the two
into one "no redeploy needed" statement at tag time.

**There is a database migration.** It runs automatically on first start and rebuilds the save-version
table. Take the usual backup before upgrading (`/data/backups/` holds the nightly ones).

### Draft notes

#### Fixed

- **Removing a machine no longer deletes its saves.** The confirmation dialog promised the version
  history would be kept, and it was not: removing a machine silently destroyed every save version
  that machine had uploaded. If one of them was the current Latest, the game was left with no Latest
  at all — its stored saves still on the server's disk, but nothing left to reach them with. Storage
  totals dropped by the same amount. Existing history is preserved by the upgrade; anything already
  lost this way cannot be recovered from the database, though the archive files themselves were never
  deleted.
- **A Pull, Push, Sync or Scan sent from the dashboard no longer goes missing.** If the machine
  dropped off between picking the job up and finishing it — a reboot, a crash, the network going
  away mid-report — the job sat in "Dispatched" forever and nothing ever ran it or said so. Jobs are
  now handed out on a time limit: one that is not confirmed goes back in the queue and the next
  check-in picks it up. The dashboard shows "retried ×2" when that happens, and says whether a job
  is running on the machine or waiting to be handed out again.
- **Saves uploaded by a machine you later removed still say who made them.** They are listed as
  "<machine> (deleted)" rather than a blank name, and they stay downloadable, keep their protected
  flag, and can still be chosen when resolving a conflict.

### Not yet verified — do not publish these bullets without it

- The deployment-shaped check through the real HTTPS tunnel (`tasks/Console-BugBounty.md` →
  Verification) has not been run; it is only needed once the proxy-URL finding lands.

Windows suites green at time of writing: server bug bounty 27, agent 45, concurrency 23, health 19,
enrollment 18.

---

## Version not yet chosen

Last released: **v0.4.1**. These are fixes only, no new capability, so v0.4.2 fits unless the
Console or Windows bounties turn up something user-visible enough to warrant v0.5.0.

The console bounty carries a schema migration, which argues for v0.5.0 on its own: it is the first
release where downgrading the container is not a clean rollback.
