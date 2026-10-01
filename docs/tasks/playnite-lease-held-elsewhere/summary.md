# Playnite plugin: launching a game another machine has open

Not started. Split out of `tasks/playnite-plugin/plan.md` on 2026-10-01, when the plugin task was closed as done. The
other unchecked surfaces listed then (the Fullscreen-mode resolve window, the link nudge, alias backfill) were confirmed
on hardware by the maintainer on 2026-10-01.

The one case never seen on hardware: Play in Playnite while another machine holds the game's lease. Expected
(`ProceedSyncPaused`): the game launches anyway, without a pull, and a Playnite notification names the machine that has
it. The Group 3 attempt used a seed script that authenticated as the wrong machine; it was fixed and never re-run.

## How

The lease is just a server record, so the other machine does not have to run the game; it only has to ask for the
lease. A lease lasts 6 hours.

1. `tests/testenv.ps1 up` (Windows + WSL test agents on the same test console), with Playnite-Test and "Conflict Game"
   linked. No conflict is needed; resolve any open one first so the launch is not blocked for that reason.
2. In WSL, take the lease as the WSL test agent: read `ServerUrl` and `ApiKey` from
   `~/savelocker-test/SaveLocker/config.json`, find the game's id from `GET {ServerUrl}/api/games`, then
   `POST {ServerUrl}/api/games/{id}/lease` with the `X-Api-Key` header. The answer should say the lease was granted.
3. Click Play on "Conflict Game" in Playnite: it launches, and the notification names the WSL machine.
4. Release it: `DELETE {ServerUrl}/api/games/{id}/lease` with the same key.

A `testenv.ps1 lease -Wsl|-Deck [-Release]` command would make steps 2 and 4 one line.

## Done when

Step 3 behaves as expected, or its bug is fixed.
