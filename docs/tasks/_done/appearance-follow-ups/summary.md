# Appearance follow-ups (left by Group 5)

Source: `tasks/_done/checkpoint-ui/implementation-grouping.md` → Group 5. Moved out of `Backlog.md` on 2026-10-01.

## Status

| Item | Status |
|---|---|
| (a) The Deck's accent | ✅ Shipped 2026-09-22 (Group 6) |
| (b) "N machines following" | ✅ Shipped 2026-10-11 — `run-health-tests` 35/35 (the 2 new checks mutation-checked); the testenv check below was not run before closing |
| (c) Tray icon follows a taskbar light/dark flip mid-session | ✅ Shipped 2026-10-01 (PR #54): `SystemEvents.UserPreferenceChanged` redraws when `SystemUsesLightTheme` flips |
| (d) `--color-faint` contrast | ✅ Shipped 2026-10-01 (PR #54): `#817e78` dark / `#79756f` light, 4.55:1 / 4.54:1 on `--panel` (was 3.31 / 3.55), in `web/src/index.css`, `agent-ui/src/tokens.css` and `Ui/Theme.cs` |
| (e) Unused `SaveLocker_Logo_crop.png` | ✅ Deleted 2026-10-01 (PR #54), console and agent UI copies |
| (f) Dark flash before WebView2's first paint | ✅ Shipped 2026-10-01 (PR #54): `AgentWindow` background and `DefaultBackgroundColor` are the page's `--color-ink` for the theme in effect (`AppsUseLightTheme` for "system") |

## (b) What was built (2026-10-11)

- `AgentHeartbeat.FollowsConsoleAppearance` (appended, optional `bool?`) — `HealthReporter` sends
  `config.FollowConsoleAppearance`. Taken as sent, null included: an older agent cannot follow.
- `AgentHealth.FollowsConsoleAppearance` column (migration `AddAgentFollowsConsoleAppearance`), echoed on
  `AgentHealthDto`. `openapi.json` and `web/src/api-types.ts`: additions only.
- Configuration → Appearance: the chip beside the switch moved under it, as in the prototype — *N machines
  following* + their names (offline ones marked), then *own look: …* for machines that opted out; *No machine
  following* when nobody does; *Not shared* / *each machine keeps its own* when pushing is off. A machine whose
  agent predates the field is in neither list. Not built: the prototype's *applied 2m ago* — the server
  does not know when an agent applied a look.

## testenv check (not yet run — closed as done by the maintainer's call)

1. `.	ests	estenv.ps1 sync`, then `build -Only console`, `build -Only windows`, `build -Only linux`, and
   `up` for the same three.
2. Console → Configuration → Appearance: within ~20 s it reads *2 machines following* and names both test agents.
3. Windows test agent's UI → Settings → turn *Follow the console* off. Next beat: *1 machine following*, and
   *own look: <that machine>*.
4. Turn *Push appearance to agents* off: *Not shared · each machine keeps its own*. Back on restores step 3's line.
5. `.	ests	estenv.ps1 down -Only linux` (or wait out the 5-minute window): the WSL machine reads *(offline)*.
