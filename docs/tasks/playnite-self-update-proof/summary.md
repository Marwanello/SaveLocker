# Playnite plugin: prove the self-update against a real release

Not started. Split out of `tasks/playnite-plugin/plan.md` on 2026-10-01, when the plugin task was closed as done.

The plugin's update path is code-complete but has never run end to end:

- **Agent side (Phase 7):** `PlaynitePlugin.CheckAsync` / `InstallAsync` (`src/Agent/PlaynitePlugin.cs`) has never
  fetched, hash-checked and installed a real package from the server's Agent Updates config (`playnite-plugin` row).
  Only the first-time install from the agent UI card (`InstallFirstTimeAsync`, Phase 19) has run, with v0.1.0.
- **Plugin side (Phase 14):** `OnApplicationStarted` calls `GET /api/playnite-plugin` and should show a restart notice
  when a newer package is waiting. The route was checked against a scratch server; the notice has never been seen inside
  a running Playnite.

## Steps

1. With `tests/testenv.ps1 up -PlaynitePath D:\Projects\SaveLocker\Playnite-Test`, install an older release (v0.1.1)
   through the agent UI card.
2. Point the test server's Agent Updates `playnite-plugin` row at a newer release (v0.1.2).
3. Start Playnite: the restart notice should appear. Close Playnite: the agent should install the new package.
4. Restart Playnite: `playnite.log` should show `Loaded plugin: SaveLocker, version 0.1.2`, and the settings page should
   show 0.1.2.
5. Check a bad hash too: the agent must refuse a package that does not match `SHA256SUMS.txt`.

## Done when

The steps above pass through testenv, and any bug they surface is fixed.
