# Playnite plugin: prove the self-update against a real release

Done 2026-11-01. Split out of `tasks/_done/playnite-plugin/plan.md` on 2026-10-01, when the plugin task was closed.

The plugin's update path was code-complete but had never run end to end: the agent side (Phase 7,
`PlaynitePlugin.CheckAsync` / `InstallAsync`) and the restart notice inside Playnite (Phase 14).

## Result

Run through testenv (console + Windows tray, `-StateRoot D:\SaveLocker-testenv`) against the portable
`D:\Projects\SaveLocker\Playnite-Test`, with the real release zips of SaveLocker-Playnite.

| Step | Result |
|------|--------|
| 1. Install v0.1.1 through the agent UI card | Pass: *Install automatically*, 2 files. Reports 0.1.0, see below |
| 2. Host v0.1.2 in the `playnite-plugin` slot | Pass. Hashes match the release's `SHA256SUMS.txt` |
| 3. Notice in Playnite | Pass: seen in Playnite's notification panel |
| 3. Exit Playnite, agent installs | **Failed, fixed.** After the fix, installed within 10 s of exit |
| 4. Restart Playnite | Pass: `Loaded plugin: SaveLocker, version 0.1.2`; settings page shows v0.1.2; installed DLL's hash equals the release's |
| 5. Bad hash | Pass: refused, v0.1.2 and its files untouched, staging folder removed |
| Extra: recovery | Pass: genuine bytes restored, next exit installed v0.1.3 |

## Bugs found and fixed

- **Exiting Playnite installed nothing.** The only check ran at agent start and every 24 h, so the update waited for
  a tick that happened to fall while Playnite was shut. `TrayApp.OnPlayniteExitTick` now samples every 30 s and runs the
  check on the running → stopped edge. Also covers a version published after the agent's last tick.
- **"Close Playnite" did not close it.** `CloseToTray` is on, so the X left the process running and the agent still
  refused. The message (agent-side, so no plugin release is needed) now says to exit from the menu or tray icon.

## Worth knowing

- **v0.1.1's manifest says `Version: 0.1.0`.** CI only began stamping the tag at v0.1.2. Harmless (0.1.2 is newer
  either way), so `Loaded plugin … 0.1.0` for v0.1.1 is expected.
- **What the agent checks is the server's digest, not `SHA256SUMS.txt`.** Step 5 corrupted the stored file behind the
  server, whose digest was still the published one. A tampered package *uploaded* through the console gets its own digest,
  so an agent would accept it; the check there is the console's verify, which answered `mismatch` for it (and
  `unknown`, not `mismatch`, for one uploaded under another filename). The server is the trust root: not changed.
- `testenv down -Only windows` also stops the console.

## Reviewed 2026-11-01 (PR #103), fixed

- An exception in the exit check (it listed processes outside any `try`) would have ended the agent: it was a timer
  callback, the one path here with no catch above it.
- With AutoUpdate off, the notice still said exiting would install the update. It now says updates are off.
- The first install's refusal and the *started during the update* failure still said to close Playnite, the same trap.
- Two installs could share the staging folder; one now stands aside (`InstallGate`).
- The check listed processes every 30 s on machines with no plugin, and its first sample came 30 s after start, which
  could miss an exit.

## Not verified

The `PluginUpdateFailed` event reaching the console. `CheckAsync`'s path with AutoUpdate off, past the notice's text. The
exit check's edge has unit tests (`PlaynitePluginTests`); the timer around it was exercised by hand only, before the review.
