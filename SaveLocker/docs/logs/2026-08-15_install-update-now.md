# Task: "Install update now" in Game Mode, and honest words when there is no plugin

Planned 2026-08-15, from a real question after v0.5.7 was tagged: *"What action would start or restart
SaveLocker? The only thing I can think of is changing to Desktop mode."*

**Execute the phases in order, verify each, stop.** Phase 1 is agent-side and ships alone. Phase 2 is
the plugin and needs a plugin release. Phase 3 is words only and could ship with either.

**ALL THREE PHASES DONE 2026-08-15.** `run-linux-tests` 208 → **216/216** (8 new checks). Phase 2
shipped as **SaveLocker-Decky v0.2.1**. Phase 1 and 3 are in the agent and **not yet in a release** —
until an agent carrying `stagedVersion` reaches a Deck, the plugin's new section stays hidden, which
is the correct behaviour on an older agent and is why the plugin release was safe to cut first.

**Nothing here has run on hardware.** See *What is still unproven* at the bottom.

---

## The problem

A Deck with a staged update shows this in Game Mode, and nothing else:

> SaveLocker 0.5.7 is ready and will be installed the next time this device starts SaveLocker.
> Nothing to do.

Every word is true and it is still the wrong answer for someone who wants the update *now*:

1. **"starts SaveLocker" is opaque.** It does not mean a boot — it means the `savelocker.service`
   **systemd `--user` unit** starting, which is where `ExecStartPre=… apply-update` performs the
   swap. Nothing on screen says that, so the user guesses. The maintainer guessed "Desktop mode",
   which is *probably* right and **conditionally** so — see Phase 3.
2. **"Nothing to do" reads as "you cannot do anything."** In Game Mode that is currently accurate:
   `Ui/UiApp.cs` prints the notice with no button, deliberately ("the whole point of the design is
   that the user does nothing and the swap happens at the next start"). That design is right for the
   passive case and leaves the active case with no route at all.
3. **The only routes today need a terminal** — `savelocker update`, or
   `systemctl --user restart savelocker.service` — so Desktop mode or SSH, neither of which is where
   the notice is being read.

## What does and does not restart the unit

Established from the unit file and `install.sh`; the lingering half is **unverified on hardware** and
Phase 3 depends on it.

| Action | Applies a staged update? |
|---|---|
| `savelocker update` | Yes — stages, applies forced, restarts the service |
| `systemctl --user restart savelocker.service` | Yes — `ExecStartPre` does the swap in the new invocation |
| Reboot | Yes, always |
| Switch to Desktop mode / back | **Only when lingering is off** (the default). It is a logout/login, so the user manager and its units cycle. With `loginctl enable-linger` set, the user manager persists and the unit is untouched. |
| Sleep / wake | No |
| "Restart Steam" in the power menu | **No** — that is Steam's own processes, not a systemd user unit. It is the natural thing to reach for, so say so explicitly. |
| Closing a game | No |

---

## Phase 1 — Agent: publish that an update is STAGED, not merely available — **DONE 2026-08-15**

**One deviation, and it is the reason Phase 2 works at all.** The step said `StagedVersion` (one
nullable string) injected as a `Func<string?>`. It is **two** fields — `StagedVersion` and
`StagedBlockedReason` — behind one `Func<StagedUpdateInfo?>`, because bite 4 of Phase 2 needs to know
whether a game is running *at the same instant* it learns something is staged. Two separate
injections would let a caller read "staged, nothing blocking" from two different moments and offer a
button that quietly does nothing. `StagedBlockedReason` is a finished sentence built in the agent
(`Updater.BlockedReason`), not a flag: three surfaces can now say "update", and the *Costs accepted*
section is right that the way they avoid disagreeing is to read one field rather than each phrasing
its own.

**`Updater.RunningGame` now names the game.** It returned `"pid 4131"`, which is not an answer anyone
can act on — the task's own example sentence is *"Khazan is running"*. It resolves the wrapper
process's `SteamAppId` out of `/proc/<pid>/environ` (readable: the wrapper runs as the same user) and
matches it against tracked games, falling back to `"A game"`. The deferral log line moved the name to
the front so both forms read as English.

**Step 4 was not optional in practice.** The agent UI's Updates panel said *"Download the new tarball
on this device and re-run install.sh… it does not install updates by itself yet"* — untrue since
v0.5.5, and `doctor` carried the same stale sentence. Both now distinguish staged from available, and
the panel shows the agent's blocked-reason verbatim.

**Verified, and the checks discriminate.** `run-linux-tests` 208 → 216. The `available is not staged`
check runs against the daemon whose offered payload is the MZ-executable one the suite already
refuses at stage time — so it is genuinely in the "offered but not on disk" state, and an
implementation filling `stagedVersion` from the update *result* fails it (confirmed by mutation).

### Original plan

`GET /api/agent-version` returns `AgentVersionDto(CurrentVersion, LatestVersion, UpdateAvailable)`,
built from `_getUpdateResult()`. That is **"the server is offering something newer"**, which is not
the state this button needs.

The distinction is the whole feature:

- **Available** — nothing is downloaded. Acting on it needs network, a download, a digest check and a
  smoke test. Could take a while; could fail.
- **Staged** — already downloaded, verified against the published SHA-256, unpacked and smoke-tested
  by `Updater.StageAsync`. Applying it is a file copy and a restart. Works **offline**, and cannot
  fail for any of the reasons a download can.

Only the second should offer "Install now". `Ui/UiApp.cs` already reads the right thing
(`Updater.PendingVersion(_config)`); the API does not expose it.

### Steps

1. Add `StagedVersion` (nullable string) to `AgentVersionDto`. Null when nothing is staged.
2. Fill it from the host, the way `launchInfo` and `deckyStatus` are injected — `Updater` lives in
   `Agent.Linux` and `AgentApiServer` is in `Agent.Core`, so this is a `Func<string?>` the Linux
   daemon supplies and the Windows tray does not (Windows stages nothing; its updater runs the
   installer).
3. Regenerate `agent-ui/src/api-types.ts`. **This no longer requires stopping an installed agent** —
   see [[Gotchas]]: `openapi-typescript` emits no server URL, so generating against a daemon on any
   port produces a byte-identical file.
4. Optional, and cheap: surface it in the agent UI's Updates panel too, which today says "available"
   for both states.

### Verify

`run-linux-tests` already stages an update in the *stage and apply* block. Assert `/api/agent-version`
reports `stagedVersion: null` before staging and the version after — the harness has both moments.

---

## Phase 2 — The plugin: an "Install update now" button — **DONE 2026-08-15, released as v0.2.1**

Shipped in <https://github.com/SkorcherX/SaveLocker-Decky>. **Not run on hardware.**

**Bite 2 — the systemd session — is answered by construction rather than by measurement, and that is
the one thing to watch on the Deck.** The backend sets `XDG_RUNTIME_DIR=/run/user/$(id -u)`
explicitly rather than hoping to inherit it, which is what the plan said to do if the environment is
not inherited; doing it unconditionally costs nothing and removes the question. Root was never
considered — `savelocker.service` is a `--user` unit belonging to the desktop user, and root's
systemd has never heard of it, so the non-`_root` decision from the Decky-plugin task is what makes
this possible at all rather than merely tidy. **If it still fails on hardware, systemctl's own text
is passed through**, because "Failed to connect to bus" and "Unit savelocker.service not found" are
completely different problems and only it can tell them apart.

**Bite 3 — the API disappearing — needed one thing the plan did not mention.** Polling until the new
`currentVersion` appears cannot work: the agent prints `Major.Minor.Patch` and the server's version
string need not agree on component count (this is exactly why `Updater.SmokeTest` compares with
`SameVersion` rather than string equality). Success is the **staged marker being gone**, which only
happens once the swap ran, and the version is then read back rather than predicted.

**A bug the plan could not have anticipated, found while writing it: success unmounts the section.**
`stagedVersion` goes null the instant the swap lands, so the component rendering the button — and the
result — disappears with it, and the user's press ends with a row silently vanishing, which looks
exactly like a press that did nothing. The outcome now keeps the section mounted by itself.

**A game running removes the button rather than disabling it**, and the agent's sentence takes its
place. The 30 s status poll brings it back when the game closes.

### Original plan

Needs Decky. Lives in <https://github.com/SkorcherX/SaveLocker-Decky> and needs a plugin release.

**Show the button only when `stagedVersion` is non-null.** With merely-available it would promise
something it cannot deliver quickly or offline.

### The four things that will bite

1. **Restart the unit — do NOT run `savelocker update`.** `systemctl --user restart savelocker.service`
   is strictly better here: `ExecStartPre` performs the swap in a fresh invocation with the old daemon
   already gone, which is the whole reason the design puts it there. `savelocker update` instead
   **re-checks the server** (so it fails when offline, even though a verified payload is already on
   disk) and applies while the daemon is live. Same outcome on a good day, worse on every other.

2. **Does Decky's backend have a usable systemd `--user` session?** Everything else depends on this
   and it is unverified. The backend runs as the desktop user (non-`_root`, per the Phase 3 decision
   in `logs/2026-08-15_decky-plugin.md`), but a `systemctl --user` call also needs
   `XDG_RUNTIME_DIR` and a session bus, and a plugin host is not a login shell. **Check this first**;
   if the environment is not inherited, set `XDG_RUNTIME_DIR=/run/user/$(id -u)` explicitly rather
   than reaching for root.

3. **The button kills the API the plugin is talking to.** `:5178` disappears mid-restart. The UI must
   show "restarting…" and poll `/api/agent-version` until it answers with the *new* `currentVersion`
   — not error-toast the instant the socket drops, which is what a naive fetch will do and which
   would report a successful update as a failure.

4. **A game may be running.** `Updater.Apply` defers a staged update while a `savelocker run` wrapper
   is alive, so a restart in that state is safe but does **nothing visible** — the worst outcome for a
   button. Check before restarting and say so: *"Khazan is running — the update will install when you
   close it."* Do not force it; the non-forced deferral is correct.

### Keeping decision 1 intact

The plugin still holds no SaveLocker knowledge: it asks the agent what is staged and asks the unit to
restart. No version comparison, no update rules, nothing that would need a plugin release to change.

### Verify

Hardware only. On a Deck with a staged update: the button appears only when staged, the panel
survives the API going away and comes back reporting the new version, `savelocker version` confirms
the swap, and a second press with nothing staged offers nothing. Also try it with a game running and
confirm the message rather than a silent no-op.

---

## Phase 3 — Say something useful when there is no plugin — **DONE 2026-08-15**

**Option 1 taken: lingering is probed.** `SystemdAutoStart.LingerEnabled()` reads logind's own marker
(`/var/lib/systemd/linger/$USER`) rather than shelling out to `loginctl show-user` — that is what
logind keys on, it costs no process, and it answers on a box with no session bus, which is the SSH
case the rest of that class already has to survive. The sentence lives in `Updater.ApplyInstruction()`
so the Game Mode screen and `doctor` cannot word it differently.

Game Mode now reads: *"SaveLocker 0.5.8 is downloaded and ready. Restart your device to install it,
or switch to Desktop mode and back. Restarting Steam will not."* — the last clause because the power
menu's **Restart Steam** is the control actually on screen and the first thing anyone reaches for.

**The stated verification does not exist.** *"`run-linux-tests` drives `savelocker ui --screenshot`
already"* — it does not; there is no `ui` invocation in that harness at all (`run-ui-wslg.sh` is a
separate, interactive script). Verified through **`doctor`** instead, which wanted the staged state
anyway: doctor is the only diagnostic a Deck has, and someone reading it about a waiting update wants
the same answer the Game Mode screen gives. The lingering branch is asserted against whichever state
the box running the suite is actually in, so it is a real check on either.

`doctor` reports the staged version **before** the registration guard, deliberately: a machine whose
enrollment has gone can still be holding a verified update, and that is exactly when someone runs
doctor.

### Original plan

Most Decks have no Decky, and they get the notice with no button. "Nothing to do" should become an
instruction that is **always true**, which means **reboot**.

**Do not promise the Desktop-mode switch.** It only works when lingering is off. It is the default and
it is what `install.sh` assumes, but `sudo loginctl enable-linger $USER` is documented in three KB
articles as the fix for "the agent stops when I log out" — so a user who followed that advice would be
told to do something that does nothing.

Two options, in order of preference:

1. **Read the lingering state and tailor the sentence.** The agent can check it cheaply
   (`/var/lib/systemd/linger/$USER`, or `loginctl show-user`). Lingering off → "Restart your Deck, or
   switch to Desktop mode and back." Lingering on → "Restart your Deck." Precise, and it is the kind
   of thing only the device can know.
2. **Say "Restart your Deck to install it."** Always true, one line, no new probing.

Either way, drop "Nothing to do" — it is what makes the screen a dead end.

**Also update the KB**, which nowhere explains what "starts SaveLocker" means:
`web/src/help/agent-update.md` and `installing-the-agent.md` should carry the table above, including
the "Restart Steam does not do it" row, which is the wrong guess everyone will make first.

### Verify

`run-linux-tests` drives `savelocker ui --screenshot` already; a staged update plus a screenshot
assertion on the new wording is enough. The lingering variant, if taken, needs both states faked.

---

## What is still unproven (2026-08-15)

Everything below the API. The harness covers the agent's half and cannot reach any of it.

1. **Does Decky's backend get a usable `systemctl --user`?** The one genuine unknown. It is answered
   by naming `XDG_RUNTIME_DIR` rather than by measurement, and a failure surfaces as systemctl's own
   text in the panel — so the first press on a Deck either works or says exactly why.
2. **The panel surviving its own API going away.** Written for it, never watched.
3. **A game running.** `Updater.RunningGame` naming a tracked game from `/proc/<pid>/environ` is
   exercised by the harness only in its fallback form (`"A game"` — the fixture wrapper has no
   `SteamAppId`). The name path has never run.
4. **The Game Mode wording, on a screen.** It is a string change in a screen that was itself only
   first seen on 2026-08-15.
5. **The lingering-on branch.** Faking it needs root, so the suite asserts whichever state its box is
   in — which has always been *off*.

Also: the plugin's section stays hidden until an agent publishing `stagedVersion` reaches the device.
Cutting v0.2.1 first is deliberate — it exercises the plugin update channel (`logs/2026-08-15_decky-plugin.md`
Phase 5, which had never run on hardware) with a change that cannot misbehave if it does not appear.

## Costs accepted

- **A third surface that can say "update"** — Game Mode UI, agent UI and the plugin. They must not
  disagree, which is why Phase 1 gives all three one field to read rather than each deciding.
- **The plugin gains the power to restart the agent.** It already runs the CLI for push/pull/doctor,
  so this is not a new class of privilege, but it is the first thing it does that stops the process
  serving it.
