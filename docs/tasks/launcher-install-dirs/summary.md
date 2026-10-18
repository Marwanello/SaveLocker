# Epic, GOG, Microsoft Store, Lutris and Bottles installs — summary

Seeded 2026-10-18 from the competitor gap analysis ([[logs/2026-10-18_competitor-gap-analysis]]). No plan
file yet. **Priority: Medium.** It also unblocks most of [[tasks/file-level-saves/summary|File-level saves]]
on Windows.

---

## The problem

A manifest save path that starts with `<base>` (the game's install folder) can only resolve when the agent
knows where the game is installed. Today that is known for:

- **Windows** (`src/Agent/GameScanner.cs`): Steam libraries, Steam shortcuts, and Playnite's library. A game
  installed by the Epic launcher, GOG Galaxy or the Microsoft Store/Xbox app is only found when its save lives
  under a common save root (the save-root heuristic) **or** when Playnite is installed and has imported it.
- **Linux** (`src/Agent.Linux/LinuxGameScanner.cs`): `shortcuts.vdf`, Steam libraries and Heroic
  (Legendary/gogdl/nile). Lutris and Bottles prefixes are not scanned at all, so neither their `<base>` paths
  nor their in-prefix saves are found.

## What competitors do

- **Hoard** (AGPL — ideas only) `crates/hoard-agent/src/launchers.rs`: Epic from the `.item` JSON manifests
  under `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\`; GOG Galaxy from the read-only SQLite
  `%LOCALAPPDATA%\GOG.com\Galaxy\storage\galaxy-2.0.db`, probing several table shapes because the schema
  drifts between Galaxy releases; Microsoft Store/Game Pass from
  `HKCU\Software\Microsoft\GamingServices\PackageRepository\Package\*` → `Root`. `wine_prefixes.rs`: Lutris
  per-runner prefixes and Bottles bottles (native and Flatpak).
- **Ludusavi** (MIT) `src/scan/launchers/lutris.rs` reads Lutris's game YAMLs (`name`, `game.working_dir` or
  `game.exe`); `heroic/*` covers the Heroic stores; it treats a user-chosen "store root" as a folder of
  installs.
- **OpenSave** (MIT) `internal/presets/launchers.go`, `wineprefix.go`: Heroic, Lutris, Bottles prefixes on
  any drive, including SD cards.

## Proposed shape

One source per launcher, each isolated the way `ScanAsync` already isolates sources (WA-11: one failing source
never fails the scan), each returning install folders the existing `<base>` resolution can use.

1. **Windows: Epic** (`.item` manifests; JSON, no new dependency) and **GOG Galaxy** (the SQLite db, opened
   read-only; the server already ships SQLite, the agent would need `Microsoft.Data.Sqlite`). Fixture tests
   in `tests/SaveLocker.Agent.Tests` with a synthetic `.item` and a synthetic db.
2. **Windows: Microsoft Store / Game Pass** install folders from the registry. Note: most Game Pass saves live
   in the encrypted `wgs` containers, which nobody syncs; this only helps titles that save to a normal folder.
3. **Linux: Lutris** (game YAMLs + per-runner prefixes) and **Bottles** (native and Flatpak), feeding the same
   `WinePrefix` resolution Heroic already uses.
4. Add Games shows the source (the `SourceChip` already exists), and `doctor` names each launcher found or
   not found.

## Touch points

`src/Agent/GameScanner.cs`, `src/Agent.Linux/LinuxGameScanner.cs`, new readers beside `HeroicLibrary.cs`,
`ScanCandidate.cs` (`ScanSource` says how a game was found, `GameStore` which store it is from), agent-ui `AddGamesView.tsx` source filter, `Doctor.cs`,
`tests/SaveLocker.Agent.Tests`, `tests/linux/make-fixtures.py`.

## Verify

Unit fixtures per reader; `run-linux-tests` with Lutris and Bottles fixtures; a detection sweep
(`tests/detection/`) before and after to measure how many more `<base>` games resolve.
