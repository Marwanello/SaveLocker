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
**Fixed so far:** the machine-deletion data loss (P0) — `a761f3f`; lost dashboard commands (P1) —
`d46dcd6`; half-written save archives (P1) — `32ffe8e`; head changes that never reached the
machines (P1) — `7f4fa4a`; the two-machine launch race (P1) — `7e12fc5`; unusable enrollment URLs (P1, scoped to
LAN — see the task file) — `312ed77`; unbounded and non-atomic installer uploads (P1+P2)

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
- **Uploading an agent installer can no longer break the update channel.** Any file was accepted,
  whatever it was: uploading a text file by mistake replaced the working installer and left every
  machine being offered *that* as an update. Uploads are now checked before anything is replaced —
  it must be a `.exe` with a version the server understands — and the new installer is only put in
  place once it has arrived in full. A rejected or interrupted upload leaves the one you already had
  serving. There is also a real size limit again (200 MB, configurable): the limit had been removed
  outright rather than raised, so a large upload could fill the disk, and that endpoint is open until
  you set an admin password.
- **Enrollment files can no longer be created with an address the new machine cannot use.** If you
  opened the console on the server itself — at `localhost` — the file told the new machine to sync
  with *itself*, and the only sign was enrollment failing for reasons that pointed nowhere. The
  console now shows the exact address the file will carry before you create it, refuses to create
  one that cannot work, and rejects a mistyped address without spending the single-use token on it.
  There is a new optional `Server:PublicBaseUrl` setting if you want to pin the address agents
  should use regardless of how you reach the console.
- **Launching the same game on two machines at once no longer errors.** Whichever one got there
  first takes the checkout; the other is now told who holds it, which is what it was always supposed
  to say. Previously one of the two got a server error instead — at exactly the moment the checkout
  is the thing protecting your save. Ending a session also releases the checkout reliably, and an
  expired one can no longer be cleaned up out from under a machine that has just renewed it.
- **"Set as Latest" now actually reaches your machines.** It promised every machine would pull the
  chosen save and then told none of them: the choice was recorded on the server and the machines
  carried on from the save they already had, hitting a conflict on their very next save. The same
  gap applied to rollback and to the automatic conflict settings ("newest wins" / "always prefer this
  machine") — the machine that lost was never told it had lost. All of them now queue a sync for
  every machine that syncs that game. As everywhere else, that sync will not overwrite local changes
  you have not pushed: those machines report the pull as blocked so you can decide.
- **Choosing a Latest clears the argument it settles.** Open conflicts on that game are marked
  resolved in its favour, instead of the console continuing to flag the very save you just chose.
- **An interrupted upload no longer leaves a broken save on the server.** If a machine lost its
  connection part-way through sending a save, the incomplete file was written straight to the place a
  real save lives, and repeated failures quietly filled the disk. Saves are now assembled aside and
  only put in place once complete, so what the server keeps is either a whole save or nothing.
- **A save that is too large is refused clearly.** It now comes back as "too large" against the
  configured limit rather than a generic server error, so the agent can say something useful.
- **Deleting a save version cannot leave the server offering one it no longer has.** The record and
  the file are removed in an order that cannot strand the record, and a file the server fails to
  delete is written to the audit log instead of being forgotten.
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
