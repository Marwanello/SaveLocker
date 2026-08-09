# Heroic Games Launcher — install-path detection (Linux)

**Status:** steps 1–8 **implemented, green locally, and confirmed on a real Deck** (2026-08-09) —
Absolute Drift via Heroic + GOG, found by `scan` from Game Mode with the correct save path. Hardware
has exercised the **gogdl** runner only; see Verification for what that leaves open.
**Written:** 2026-08-09.

`run-linux-tests.sh` is **51/51**, up from 40 — 11 new checks, all Heroic. Detection `pinned` all
pass, sweep 295/298 (the default 300-game sample; `PathResolver.Proton` is unchanged behaviourally,
so this is not a regression). Solution builds with only the one documented MSB3277 warning.

> [!note] Step 8's third bullet was dropped deliberately
> The plan called for `tests/detection` cases scoring a wine-shaped prefix with a non-`steamuser`
> user. That turned out to be redundant: `Proton()` now *delegates* to `Wine()`, so the whole
> 300-game sweep already exercises the wine code path for every token. The shape-specific behaviour
> — which of `pfx/drive_c` and `drive_c` is real, and which user — lives in `WinePrefix.Locate`,
> which is filesystem inspection the detection harness deliberately fakes. It is covered end-to-end
> in `run-linux-tests.sh` instead, against a real wine-shaped fixture tree.

## Context

Deck users stage Epic / GOG / Amazon / sideloaded games in **Heroic Games Launcher**, then use
Heroic's "Add to Steam" to surface them in the Steam library. SaveLocker sees those games — they are
real `shortcuts.vdf` entries — but never resolves a save folder for them, so every one arrives with
an empty save path and the user types it by hand.

The cause is in [`LinuxGameScanner.SuggestSaveDirAsync`](../../src/Agent.Linux/LinuxGameScanner.cs):
the prefix is looked up as `SteamRoots.CompatDataPath(root, s.AppId)`, i.e.
`steamapps/compatdata/<appid>`. Heroic does not launch through Steam's compatdata — it owns its own
prefix under `~/Games/Heroic/Prefixes/`. So the lookup returns null, the candidate falls through to
`PortableSaveDir`, and that almost never hits.

There is a second, quieter blocker.
[`PathResolver.Proton`](../../src/Shared/PathResolver.cs) hardcodes two things that are only true of
Steam: that the prefix is `<compatdata>/pfx/drive_c`, and that the Wine user is `steamuser`. Heroic
uses `<winePrefix>/pfx` **only when the runner is Proton**; for a plain Wine/GE runner the prefix is
`<winePrefix>` directly and the Wine user is the Linux username. A Heroic game on a Wine runner is
unresolvable today even if we hand the resolver the right prefix root.

### What Heroic actually does (verified against `main`, 2026-08-09)

`src/backend/constants/paths.ts`:

| Thing | Value |
|---|---|
| Default install root | `~/Games/Heroic` (`/home/deck/Games/Heroic` on a Deck) |
| Default prefix root | `~/Games/Heroic/Prefixes` (`shared/`, `default/`) |
| Config root | `app.getPath('appData')/heroic` |

Config root in practice: `~/.config/heroic` native, and
`~/.var/app/com.heroicgameslauncher.hgl/config/heroic` for the Flatpak — which is what a Deck has.

Library files under the config root — **this is the query surface**, not the default path:

| Runner | File | Notes |
|---|---|---|
| Epic | `legendaryConfig/legendary/installed.json` | object keyed by app_name; `install_path`, `title`, `platform` |
| Epic 3rd-party | `legendaryConfig/legendary/third-party-installed.json` | same shape |
| Amazon | `nile_config/nile/installed.json` | array; `id`, `path` |
| GOG | `gog_store/installed.json` | electron-store: `{ "installed": [ { appName, install_path, platform } ] }` |
| GOG titles | `gog_library.json` | cache: `{ "games": [ { app_name, title } ] }` — GOG's installed rows carry no title |
| Sideloaded | `sideload_apps/library.json` | `{ "games": [ { app_name, title, install: { executable, platform } } ] }` |
| Global settings | `config.json` | `defaultSettings.defaultInstallPath` / `.winePrefix` / `.wineVersion` |
| **Per-game settings** | `GamesConfig/<app_name>.json` | keyed by app_name; **`winePrefix`**, `wineVersion.type` — authoritative |

Prefix shape, from `src/backend/launcher.ts:1457-1461`:
- `wineVersion.type === 'proton'` → real prefix is `<winePrefix>/pfx`, Wine user `steamuser`
- anything else → real prefix is `<winePrefix>`, Wine user is the **Linux username**

## Scope

**In:** Windows-platform games installed through Heroic on Linux, across all four runners
(legendary / gogdl / nile / sideload), on both native and Flatpak Heroic.

**Out — native-Linux Heroic games** (maintainer's call, 2026-08-09). They have no prefix and write to
`~/.config` / `~/.local/share`; `Decisions.md §1` already puts native-Linux builds out of scope and
this does not widen it. Entries whose platform is `linux` (or `osx`/`mac`/`browser`) are skipped.

**Out:** Windows. Heroic runs there too, but it installs games natively — there is no prefix, and
`ResolveWindowsAsync` already covers the shape. Revisit only if asked.

`<root>` has no Heroic meaning and is passed as null rather than invented. `<storeUserId>` is
Steam-specific; a manifest path needing it resolves to nothing, which per v0.5.2 beats a guess.

## Steps

### 1. `src/Shared/WinePrefix.cs` (new)

`WinePrefixLayout(Root, DriveC, UserName)` + `WinePrefix.Locate(prefixRoot)`:
- `DriveC` = `<root>/pfx/drive_c` if it exists, else `<root>/drive_c`, else **null return** —
  "there is no prefix here" must be distinguishable from "prefix with nothing in it".
- `UserName` = `steamuser` when `users/steamuser` exists; else the single non-`Public` directory
  under `users/`; else `steamuser`.

This is the only filesystem-probing piece. `PathResolver` itself stays pure.

### 2. `PathResolver.Wine(driveC, userName, installDir, storeRoot, storeUserId)`

Lift the token map out of `Proton()`, which becomes a two-line delegate passing
`<compatDataPath>/pfx/drive_c` and `steamuser`. **`Proton()`'s behaviour must not change** — it is
what the detection harness scores against, and it must keep building paths without touching disk.

### 3. `src/Agent.Linux/HeroicRoots.cs` (new)

- `Find()` → existing config roots, in order: `$XDG_CONFIG_HOME/heroic`, `~/.config/heroic`,
  `~/.var/app/com.heroicgameslauncher.hgl/config/heroic`. A root counts only if it holds
  `config.json`. **An empty list is the "Heroic is not installed" answer** — no process probe, no
  `which`, nothing that depends on Heroic running.
- `PrefixFor(configRoot, appName)` → `(path, isProton)` from `GamesConfig/<appName>.json`
  (`winePrefix`, `wineVersion.type`), falling back to `config.json` → `defaultSettings`, then
  `~/Games/Heroic/Prefixes/default`.
- `BrowseRoots()` → the Heroic install root, so the path browser can reach it.

### 4. `src/Agent.Linux/HeroicLibrary.cs` (new)

Parse the six library files into `HeroicGame(AppName, Title, InstallPath, Runner, Platform)`.
Plain `System.Text.Json`, one `try`/`catch` **per file** — Heroic is a moving target and a format
change to one runner must degrade to "found nothing for that runner", never kill the scan.

### 5. Wire into `LinuxGameScanner`

New `ScanSource.Heroic`. After the Steam shortcut loop, walk Heroic's library; for each game resolve
via `WinePrefix.Locate` → `PathResolver.Wine` → `Detection`, with `InstallDir` = Heroic's
`install_path` (that is `<base>`), then fall back to a portable save dir beside the executable.

Dedupe: a Heroic game added to Steam appears in **both** lists. The existing
`GroupBy(NormalizeName)` collapses them and prefers the entry that has a save dir — which will be the
Heroic one. Assert this rather than rely on it incidentally.

### 6. Un-hardcode the two prefix descents

`AgentApiServer.PrefixStart` and `UiApp` both walk a literal `pfx/drive_c/users/steamuser`. Route
both through `WinePrefix.Locate` so a Heroic Wine prefix opens the browser in the right place instead
of at the prefix root.

### 7. `Doctor.cs`

A `Heroic` section: detected or not, which config root, install root, count found per runner, count
resolved, and per game the prefix + whether it is Proton- or Wine-shaped. On a Deck this is the only
diagnostic surface there is.

### 8. Tests

- `tests/linux/make-fixtures.py`: a fake Heroic config root in **both** layouts, a legendary game on a
  Proton prefix, a sideloaded game on a Wine prefix, a native-Linux entry that must be **skipped**,
  and a `shortcuts.vdf` entry colliding with a Heroic title (the dedupe case).
- `tests/linux/run-linux-tests.sh`: new checks for the above.
- `tests/detection`: cases scoring the resolver inside a Wine-shaped prefix with a non-`steamuser`
  user — the shape `Proton()` cannot express.

## Verification

1. `tests/linux/run-linux-tests.sh` — green, including the new Heroic checks.
2. `tests/detection` `pinned` — green; `sweep` no worse than 394/396.
3. **On hardware (Deck) — PASSED 2026-08-09, via gogdl.** **Absolute Drift**, downloaded through
   Heroic with a logged-in GOG account, was found by `savelocker scan` from **Game Mode** with the
   save path correct.

   That verifies the **gogdl** parser: `gog_store/installed.json` for the install path, joined to
   `gog_library.json` for the title. The join is confirmed by the title alone — GOG's installed rows
   carry no title, so a broken join would have listed the game under its install-folder name.

   > [!warning] What hardware has NOT covered
   > **gogdl only.** The `legendary`, `nile` and `sideload` parsers are covered by
   > `run-linux-tests.sh` and by nothing else. The sideload gap is the one that matters most, because
   > it is what a **GOG offline installer** becomes — see below.

   The original plan for this gate was Quake II from a GOG *offline installer*, which is installed as
   a **sideloaded** app (`sideload_apps/library.json`, prefix from `GamesConfig/<app_name>.json`) and
   would therefore have covered a different parser. It was **abandoned: the game would not launch
   under Heroic at all**, for reasons unrelated to SaveLocker. Worth retrying with any sideloaded
   title, since that path is also the likeliest to need the portable-save fallback — an offline GOG
   install often writes saves beside its own executable rather than into the prefix.

## Notes

- **`Prefixes/Title` is Heroic's, verbatim — not a placeholder, and not ours.** Heroic's sideload
  dialog initialises its title field to the literal word `Title`
  (`useState(t('sideload.field.title', 'Title'))` in `InstallModal/index.tsx`) and seeds the prefix
  name from the title, so a prefix created before the real title is typed is literally named
  `Title`. SaveLocker never invents a prefix name: it reads `winePrefix` out of
  `GamesConfig/<app_name>.json`, and its only fallbacks are `Prefixes/default` and
  `Prefixes/shared`. Anyone who sees `Prefixes/Title` and assumes a bug on our side is wrong.

- **A sideloaded game's executable can live in a different prefix than the one it runs in.** Hit for
  real on a Deck (2026-08-09): uninstalling Quake III left `Prefixes/Quake III` and a complete
  `quake3.exe` behind, the reinstall went to `Prefixes/Title`, and Heroic's "pick the executable"
  step was answered with the leftover copy. Two full copies of the game, Heroic launching the stale
  one's `.exe` inside the fresh one's prefix. Nothing complains, because Wine reaches any Linux path
  through `Z:`. Renaming a prefix folder without updating `GamesConfig` produces the same shape.

  Saves follow the prefix the game **runs** in, so SaveLocker reads the right tree and the machine
  works — which is why `Doctor` reports this as a **note** and keeps exit code 0. What it costs is
  `<base>`: the manifest's most common placeholder would resolve into the copy the game is not
  running. Covered by the split-prefix checks in `run-linux-tests.sh`.

  > [!note] Both sideload path fields agreed
  > `folder_name` and `install.executable` pointed at the same (leftover) location, so the parser's
  > preference between them was not implicated. An earlier guess that `folder_name` goes stale
  > independently was wrong — checked against the real file, and no parser change was made.

  > [!warning] A fallback that looked obviously right and was not
  > The first fix here made the scanner fall back to the prefix the game is *installed* in when the
  > configured prefix resolved nothing. It was **removed before landing**. The only scenario in which
  > it ever fires is the renamed-prefix one — and there it returns the stranded pre-rename save
  > folder: a real directory, full of real saves, that the game has stopped writing to. It is more
  > convincing than the right answer, which is exactly what v0.5.2 ruled out. `Doctor` reports the
  > disagreement instead; `WinePrefix.ContainingPrefix` exists for that check alone.

- Heroic's default install path is a **fallback**, not the mechanism. The user can override it
  globally and per game, and issues #2431 / #2800 show Heroic itself ignoring the global override —
  so read the library files and trust `install_path`.
