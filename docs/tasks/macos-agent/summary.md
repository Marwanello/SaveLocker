# macOS agent — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]). No plan
file yet. **Priority: Low** — SaveLocker is aimed at a Windows PC plus a Steam Deck, but every main competitor
(OpenSave, Hoard, Ludusavi, BlockSlot) has a macOS build, so it is the one platform gap that shows up in any
comparison.

---

## What it would take

`Agent.Core` is already `net10.0` and platform-neutral; the Linux agent proves a headless host plus the
embedded agent UI works. A macOS host would be a third, small host project:

1. **Host**: a daemon like `Agent.Linux/Daemon.cs`, started by a `launchd` LaunchAgent (`IAutoStart`
   implementation), the agent UI opened in the default browser or an app window.
2. **Discovery** (`IGameScanner`): Steam on macOS (`~/Library/Application Support/Steam`), native macOS saves
   from the manifest (`os: mac` entries, `<home>/Library/...` paths). Shares the platform-isolation rule from
   [[tasks/native-linux-saves/summary|Native Linux save support]]: a native macOS save never shares a lineage
   with a Windows one — so that task's data-model step must land first.
3. **Notifications** (`INotificationPresenter`) via `osascript` or a small helper.
4. **Packaging**: a signed and notarized `.pkg` or `.app` (an unsigned one is blocked by Gatekeeper by
   default — BlockSlot ships ad hoc signed and says so), plus the server's installer slot and update path.

## What competitors do

BlockSlot (MIT) ships `Blockslot.app` with a LaunchAgent and keeps secrets in the login Keychain; OpenSave
ships an unsigned `.dmg` plus a CLI tarball; Hoard builds through Tauri.

## Dependencies

[[tasks/native-linux-saves/summary|Native Linux save support]] (step 2, the per-platform lineage) first.
