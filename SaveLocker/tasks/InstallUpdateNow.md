# Task: "Install update now" in Game Mode, and honest words when there is no plugin

Planned 2026-08-15, from a real question after v0.5.7 was tagged: *"What action would start or restart
SaveLocker? The only thing I can think of is changing to Desktop mode."*

**Execute the phases in order, verify each, stop.** Phase 1 is agent-side and ships alone. Phase 2 is
the plugin and needs a plugin release. Phase 3 is words only and could ship with either.

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

## Phase 1 — Agent: publish that an update is STAGED, not merely available

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

## Phase 2 — The plugin: an "Install update now" button

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

## Phase 3 — Say something useful when there is no plugin

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

## Costs accepted

- **A third surface that can say "update"** — Game Mode UI, agent UI and the plugin. They must not
  disagree, which is why Phase 1 gives all three one field to read rather than each deciding.
- **The plugin gains the power to restart the agent.** It already runs the CLI for push/pull/doctor,
  so this is not a new class of privilege, but it is the first thing it does that stops the process
  serving it.
