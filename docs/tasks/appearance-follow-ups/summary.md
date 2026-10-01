# Appearance follow-ups (left by Group 5)

Not started. Moved out of `Backlog.md` on 2026-10-01; the notes below are as they stood there.

Source: `tasks/checkpoint-ui/implementation-grouping.md` → Group 5. (a), the Deck's accent, shipped with Group 6 on 2026-09-22.

- (b) *"N machines following"* — the prototype's console line needs the server to know who follows: an optional `FollowsConsoleAppearance` on the heartbeat and a column on `AgentHealth` (a migration).
- (c) *The tray icon follows Windows' taskbar theme only at start and on a look change* — it does not subscribe to `SystemEvents.UserPreferenceChanged`, so flipping the taskbar between light and dark mid-session leaves a Stealth icon low-contrast until the next change.
- (d) *`--color-faint` itself* is still 3.31:1 dark / 3.55:1 light — content text now uses `--color-dim`, but the uppercase eyebrow labels that keep `faint` are 10 px; lifting the token means `web/src/index.css`, `agent-ui/src/tokens.css` and `Ui/Theme.cs` together, and `run-appearance-consistency-tests` compares all three (the Deck's copy since the PR #49 review).
- (e) `web/src/assets/SaveLocker_Logo_crop.png` (1.1 MB) is now referenced by nothing in the console, and the agent UI's copy likewise — safe to delete once the README and the docs are checked for it 
- (f) `AgentWindow.BackColor` (`src/Agent/AgentWindow.cs`) is a fixed dark colour shown before WebView2's first paint, so with the theme now following the OS a light-preferring user sees a dark flash each time the agent window opens — it should take the effective look's panel colour (`AgentConfig.EffectiveAppearance`) instead.
