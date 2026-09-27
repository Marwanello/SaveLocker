# Group 7 (OS notifications) — live verification through testenv

Run from the repo root in PowerShell. Every step names what you should SEE; a step that does not match is a failure to report, not to work around. Rig: Windows tray agent + WSL Linux agent + Steam Deck (if `deck-config` is set) against one Docker server.

Notification rules are the same everywhere, so the checklist tests the rules once (Windows, where a toast is visible on the machine you sit at) and then proves each host delivers them.

## 0. Preconditions

1. Stop anything holding the build: `.\tests\testenv.ps1 down`. Leave your installed agent alone.
2. `.\tests\testenv.ps1 clean` — expect no error. Then confirm `%LOCALAPPDATA%\SaveLocker-test` is gone (known quirk: `clean` once left it behind and the next seed returned 401; delete it by hand if so and note it).
3. Windows Settings > System > Notifications: Focus/Do-not-disturb OFF, and "Get notifications from apps" ON.
4. `.\tests\testenv.ps1 build` then `.\tests\testenv.ps1 sync` (pushes the tree into the WSL clone and the Deck, if configured).
5. `.\tests\testenv.ps1 up` (all sides). `.\tests\testenv.ps1 status` — expect server healthy, Windows tray, WSL agent, and Deck agent all up.
6. Windows: the tray icon of the test agent appears (port 5187/5188 family, not your installed 5178).

## 1. Windows toast — conflict opened

1. Seed with Windows as the side that pushes LAST, because the server records a conflict against whoever pushes second and an agent only announces conflicts its own machine is party to. Verified recipe: `clean`, `build`, `conflict -Wsl` (or `-Deck`, alone), then `conflict -Windows`, then `up`. The usual `conflict -Windows -Wsl/-Deck` does the opposite: the conflict lands on WSL/the Deck and Windows correctly shows nothing.
2. Within one poll (about 15 s): ONE toast appears with the SaveLocker brand mark, title naming the game, body in plain voice (no jargon), buttons **Open game**-style link and **Later**.
3. Wait 60 s: NO second toast for the same conflict (announce once).
4. Click the primary button: a small "Opened in the SaveLocker window" tab flashes in the default browser and the SaveLocker agent window comes to the front (over the browser) at `#conflicts:queue` (or `#game:<id>`). If the window was already open it navigates without a reload; if it was minimized it is restored.
5. Resolve the conflict in the UI (or dashboard). Within one poll the toast is withdrawn from Action Center if it was still there.
6. Click **Later** on a fresh conflict: dismissed, does not come back on the next polls.

## 2. Windows toast — the other events

Trigger each and confirm exactly one toast, correct copy, and clearing when the condition ends.

| Event | How to trigger | Expect |
|-------|----------------|--------|
| Push rejected | `testenv conflict` on a game, then edit the local save on Windows so a push is refused | Error-severity toast (stays, reminder style), "Open game" |
| Pull refused (running) | Start a process the game matches (launch a dummy exe with the game's process name) while a newer cloud save exists | Warning toast; it is cleared when the game exits and the pull succeeds |
| Lease held elsewhere | Hold a lease on the WSL/Deck side (`testenv conflict` leaves one), start play on Windows | Toast names the other machine; cleared after the lease is released |
| Update ready | Point the rig at a newer version (`up -ConsoleEnv` with a fake release, or use the update-check test hook noted in Build and Run) | Toast tells you to use the tray "Update to vX" item (Windows has no Install-now button) |
| Server unreachable | `docker stop savelocker-test`, then `SAVELOCKER_UNREACHABLE_NOTICE_SECONDS=20` was set on the agent for the test | No toast at 10 s, ONE toast after the threshold, none repeated. `docker start savelocker-test`: toast withdrawn within ~20 s |

Negative checks (must NOT toast): a normal successful push/pull; a 30 s offline blip (drainer's "attempting drain" must be silent — the old balloon every 30 s is gone); startup with the server up.

## 3. Windows: retry + branding

1. Quit the agent while a conflict toast is up, restart it: the same standing conflict is announced again exactly once (fresh process state), not zero and not twice.
2. Dismiss a toast, leave the conflict open for 5 polls: it does not reappear.
3. Change the accent in Settings > Appearance: the next toast's mark uses the new accent. `%TEMP%` holds exactly one `savelocker-toast-logo-<port>-<look>.png`, and its name changes with each look (a fixed name was unreliable - the shell showed a stale picture). A toast already on screen or in Action Center keeps the old accent; only a NEW toast changes.
4. Header name: on the rig it shows the readable test AUMID; the INSTALLED header (shortcut with `AppUserModelID`) is NOT covered here — see section 8.

## 4. Linux (WSL) agent

WSLg injects a display and D-Bus, so a popup may not be visible and `notify-send` may have no daemon. Verify the logic through the log and the fake-daemon suite:

1. `-Wsl` alone only pushes WSL, and a single push never conflicts (same order-dependency as section 1) — confirm with `GET /api/conflicts?status=open` on the console that it's empty before blaming the code. Seed WSL as the side that pushes LAST instead: `clean`, `build`, `conflict -Windows` (alone), then `conflict -Wsl`, then `up`. Wait at least 20s after `up` (the daemon's own poll interval — its first tick fires that long after start, so checking `logs` sooner looks like nothing happened even on a real conflict). Then `.\tests\testenv.ps1 logs`: expect either `notification sent: '<title>'.` or `notification '<title>' not shown: no desktop notification daemon reachable — see doctor or the agent UI.` (the normal WSLg case) under **wsl agent.log** (not the "wsl daemon" section above it, which is only ASP.NET's console output and never shows a notification line) — once per standing conflict, not once per poll.
2. Resolve the conflict: expect no repeated log lines and, where a daemon exists, the notification closes.
3. `wsl -d <distro> -- bash tests/linux/run-linux-tests.sh`: expect 192 pass and only the two known WSLg "no session" failures. Anything else is a regression.
4. `.\tests\testenv.ps1 test` for the .NET + PowerShell suites: expect 84 unit tests (66 new), winagent 119/0, health 22/22, appearance consistency 33/33.

## 5. Steam Deck (Desktop Mode)

Requires `deck-config` and a reachable Deck.

1. `.\tests\testenv.ps1 conflict -Deck` (this also gives the game a Steam shortcut).
2. In Desktop Mode: a KDE notification appears with the info/warning icon, title, body and ONE button labelled by the notice. `.\tests\testenv.ps1 logs` shows `notification sent`.
3. Click the button: the default browser opens the agent UI at `#game:<id>` / `#conflicts:queue` on the Deck's agent port. Confirm the exact screen, and that clicking a second time on an already-open tab navigates it.
4. Resolve the conflict elsewhere (Windows UI): the Deck notification disappears on its own within a poll (withdraw by id + kill).
5. Dismiss it, wait 5 polls: does not return.
6. Stop the server: nothing for the first 5 minutes (or the short test threshold), then one notification; restart the server: it disappears.
7. Update-staged notice: after the Deck stages an update the notification says it is staged (Linux keeps its "Install now" flavour; check its wording differs from the Windows one).

## 6. Steam Deck (Game Mode)

Game Mode has no desktop notification daemon, so by design NO popup is expected from this agent.

1. With the Deck in Game Mode and a conflict open: confirm NO popup, NO crash, and `logs` shows the "no daemon reachable" reason once.
2. The Decky plugin still shows the conflict/health state (its own screen, unchanged by Group 7).
3. Switch back to Desktop Mode: the standing conflict is announced (retry-when-undeliverable) exactly once.

## 7. agent-ui deep links (any host)

In each agent's UI (Windows, WSL, Deck browser):

1. Open `#game:<a real game id>`: that game's detail opens. Invalid id: falls back to the list, no crash.
2. Open `#conflicts:queue`: the conflict queue opens. `#conflicts`, `#settings` etc. behave as before.
3. With the UI already open, paste a different hash and press Enter (no reload): the view changes (hashchange).
4. Reload on a hash: same view restores.

## 8. Not coverable here (record as still open)

- Installed-agent toast header (needs a real installer run and Start-menu shortcut with the AUMID).
- A true Linux desktop popup on a non-WSLg machine other than the Deck.
- Buttons that call back into the tray ("Retry now" / "Install now") — Backlog item.

## 9. Teardown

1. `.\tests\testenv.ps1 down`, then `.\tests\testenv.ps1 clean`.
2. Confirm: no stray test trays, no `SaveLocker.Test.*` entries under `HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings`, no leftover toast PNG in `%TEMP%`, installed agent (5178) untouched.
3. Report pass/fail per numbered step, with the log line or screenshot for any failure.
