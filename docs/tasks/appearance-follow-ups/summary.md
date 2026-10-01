# Appearance follow-ups (left by Group 5)

Source: `tasks/checkpoint-ui/implementation-grouping.md` → Group 5. Moved out of `Backlog.md` on 2026-10-01.

## Status

| Item | Status |
|---|---|
| (a) The Deck's accent | ✅ Shipped 2026-09-22 (Group 6) |
| (b) "N machines following" | ⏳ Not started — needs a heartbeat field, a migration and a console line |
| (c) Tray icon follows a taskbar light/dark flip mid-session | ✅ Shipped 2026-10-01 (PR #54): `SystemEvents.UserPreferenceChanged` redraws when `SystemUsesLightTheme` flips |
| (d) `--color-faint` contrast | ✅ Shipped 2026-10-01 (PR #54): `#817e78` dark / `#79756f` light, 4.55:1 / 4.54:1 on `--panel` (was 3.31 / 3.55), in `web/src/index.css`, `agent-ui/src/tokens.css` and `Ui/Theme.cs` |
| (e) Unused `SaveLocker_Logo_crop.png` | ✅ Deleted 2026-10-01 (PR #54), console and agent UI copies |
| (f) Dark flash before WebView2's first paint | ✅ Shipped 2026-10-01 (PR #54): `AgentWindow` background and `DefaultBackgroundColor` are the page's `--color-ink` for the theme in effect (`AppsUseLightTheme` for "system") |

## Open

- (b) *"N machines following"* — the prototype's console line needs the server to know who follows: an optional `FollowsConsoleAppearance` on the heartbeat and a column on `AgentHealth` (a migration).
