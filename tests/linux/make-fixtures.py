#!/usr/bin/env python3
"""Build the fake-game fixture tree for the Linux harness.

No Steam, no Proton, no GPU and no Deck are involved: the agent only ever reads shortcuts.vdf,
two environment variables, and the filesystem. Faking those exercises the entire real code path.

Two games, because there are two real save shapes:

  1. "Fake Prefix Game"   — writes INSIDE the Wine prefix (drive_c/users/steamuser/AppData/...).
                            Its AppID is deliberately NEGATIVE, to pin the signed/unsigned trap:
                            Steam stores the AppID signed but names compatdata/ with the unsigned
                            value. Code that uses the signed form looks for "-1234567890" and
                            silently finds nothing.
  2. "Fake Portable Game" — writes next to its own .exe. No prefix involved at all.

Plus three games staged in Heroic Games Launcher, which owns its own prefixes and does not obey
either Steam convention:

  3. "Heroic Prefix Game" — legendary (Epic), PROTON-shaped prefix (<winePrefix>/pfx, user
                            steamuser). It ALSO has a shortcuts.vdf entry, as Heroic's "Add to
                            Steam" creates — with no compatdata behind it, which is the whole bug:
                            the Steam path finds the game and can never resolve its saves.
  4. "Heroic Wine Game"   — sideloaded (what a GOG offline installer becomes), WINE-shaped prefix
                            (<winePrefix>/drive_c) whose Wine user is the Linux username, not
                            steamuser. Unresolvable by anything that assumes Steam's layout.
  5. "Heroic Linux Game"  — a native-Linux title. Must be SKIPPED: no prefix, out of scope.
  6. "Heroic Offline Game"— a prefix RENAMED on disk without updating Heroic's config, so the game's
                            files and the prefix it is configured to run in are two different
                            directories, both healthy-looking. Shipped as a swappable VARIANT of the
                            sideload library, because it is a deliberately broken machine.
"""
import json
import os
import struct
import sys

PREFIX_APPID_SIGNED = -1234567890          # what shortcuts.vdf stores
PREFIX_APPID_UNSIGNED = str(PREFIX_APPID_SIGNED & 0xFFFFFFFF)  # 3060399406 — the folder name
PORTABLE_APPID_SIGNED = 1234567890
PORTABLE_APPID_UNSIGNED = str(PORTABLE_APPID_SIGNED & 0xFFFFFFFF)
HEROIC_SHORTCUT_APPID_SIGNED = 1122334455

# An installed Steam game's appid. Always positive — the signed/unsigned trap is a shortcuts.vdf
# problem only; an .acf stores the id as text and needs no conversion.
INSTALLED_APPID = "440900"

# The Wine user for the wine-shaped prefix. Deliberately NOT "steamuser" — a plain Wine runner
# runs the game as the Linux user, and code that assumes steamuser resolves every token to a
# directory that does not exist.
HEROIC_WINE_USER = "deck"

# Heroic on a Deck is the Flatpak, so that is the layout under test.
HEROIC_FLATPAK_ID = "com.heroicgameslauncher.hgl"


def cstr(s: str) -> bytes:
    return s.encode("utf-8") + b"\x00"


def vdf_string(key: str, value: str) -> bytes:
    return b"\x01" + cstr(key) + cstr(value)


def vdf_int(key: str, value: int) -> bytes:
    return b"\x02" + cstr(key) + struct.pack("<i", value)


def shortcut(index: int, name: str, exe: str, start_dir: str, appid: int) -> bytes:
    body = (
        vdf_int("appid", appid)
        + vdf_string("AppName", name)
        + vdf_string("Exe", f'"{exe}"')          # Steam quotes these
        + vdf_string("StartDir", f'"{start_dir}"')
    )
    return b"\x00" + cstr(str(index)) + body + b"\x08"


def build_shortcuts_vdf(entries: bytes) -> bytes:
    # Root object: 0x00 "shortcuts" <children> 0x08, plus Steam's trailing 0x08.
    return b"\x00" + cstr("shortcuts") + entries + b"\x08" + b"\x08"


def write_json(path: str, data) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)


def build_heroic(home: str, games: str) -> dict:
    """Materialise a Heroic Flatpak config root with three installed games."""
    config_root = os.path.join(home, ".var", "app", HEROIC_FLATPAK_ID, "config", "heroic")
    heroic_games = os.path.join(games, "Heroic")
    prefixes = os.path.join(heroic_games, "Prefixes")

    # 3 — Epic/legendary, PROTON-shaped prefix. Saves under <winAppData> inside pfx/.
    epic_install = os.path.join(heroic_games, "HeroicPrefixGame")
    epic_prefix = os.path.join(prefixes, "HeroicPrefixGame")
    epic_save = os.path.join(
        epic_prefix, "pfx", "drive_c", "users", "steamuser",
        "AppData", "Roaming", "HeroicPrefixGame")
    os.makedirs(epic_save, exist_ok=True)
    os.makedirs(epic_install, exist_ok=True)

    # 4 — sideloaded, WINE-shaped prefix, non-steamuser user. Saves under <winDocuments>.
    wine_install = os.path.join(heroic_games, "HeroicWineGame")
    wine_prefix = os.path.join(prefixes, "HeroicWineGame")
    wine_save = os.path.join(
        wine_prefix, "drive_c", "users", HEROIC_WINE_USER, "Documents", "HeroicWineGame")
    os.makedirs(wine_save, exist_ok=True)
    os.makedirs(wine_install, exist_ok=True)

    # 6 — a RENAMED prefix, seen on a real Deck (2026-08-09).
    #
    # Heroic's sideload dialog seeds the prefix name from a title field that starts as the literal
    # word "Title", so an offline install lands in Prefixes/Title. Renaming that folder does not
    # update GamesConfig: Heroic finds nothing at the configured path, creates a fresh empty prefix
    # there, and runs the game in it. The old prefix keeps a full set of real saves that nothing
    # writes to any more.
    #
    # Both directories exist and both look healthy. The game must therefore get NO save path — the
    # stranded one is the more convincing of the two, and returning it would be worse than nothing.
    # No spaces: the harness ships these paths to bash through an eval'd KEY=VALUE line.
    renamed_prefix = os.path.join(prefixes, "HeroicOfflineGame")
    offline_install = os.path.join(renamed_prefix, "drive_c", "GOG Games", "OfflineGame")
    stranded_save = os.path.join(
        renamed_prefix, "drive_c", "users", HEROIC_WINE_USER, "Documents", "HeroicOfflineGame")
    os.makedirs(offline_install, exist_ok=True)
    os.makedirs(stranded_save, exist_ok=True)

    # What GamesConfig still points at: real, proton-shaped, freshly created, and empty.
    configured_prefix = os.path.join(prefixes, "Title")
    os.makedirs(
        os.path.join(configured_prefix, "pfx", "drive_c", "users", "steamuser"), exist_ok=True)

    # 5 — native Linux. Out of scope: it gets an install dir and no prefix, and must never appear.
    linux_install = os.path.join(heroic_games, "HeroicLinuxGame")
    os.makedirs(linux_install, exist_ok=True)

    write_json(os.path.join(config_root, "config.json"), {
        "defaultSettings": {
            "defaultInstallPath": heroic_games,
            "defaultWinePrefix": prefixes,
        }
    })

    write_json(os.path.join(config_root, "legendaryConfig", "legendary", "installed.json"), {
        "HeroicPrefixGame": {
            "app_name": "HeroicPrefixGame",
            "title": "Heroic Prefix Game",
            "install_path": epic_install,
            "platform": "Windows",
        },
        "HeroicLinuxGame": {
            "app_name": "HeroicLinuxGame",
            "title": "Heroic Linux Game",
            "install_path": linux_install,
            "platform": "Linux",
        },
    })

    healthy_sideload = {
        "app_name": "HeroicWineGame",
        "title": "Heroic Wine Game",
        "folder_name": wine_install,
        "install": {
            "executable": os.path.join(wine_install, "game.exe"),
            "platform": "Windows",
        },
    }
    renamed_sideload = {
        "app_name": "HeroicOfflineGame",
        "title": "Heroic Offline Game",
        "folder_name": offline_install,
        "install": {
            "executable": os.path.join(offline_install, "game.exe"),
            "platform": "Windows",
        },
    }

    # Case 6 ships as a VARIANT the harness swaps in, not as part of the default library. It is a
    # deliberately broken setup, and leaving it in place permanently would mean the suite could
    # never again assert that a healthy machine makes doctor exit 0.
    sideload_library = os.path.join(config_root, "sideload_apps", "library.json")
    write_json(sideload_library, {"games": [healthy_sideload]})
    write_json(sideload_library.replace(".json", ".renamed-prefix.json"),
               {"games": [healthy_sideload, renamed_sideload]})

    # Per-game prefixes. These are what make the two games resolvable at all — the global default
    # points at the Prefixes ROOT, which is not a prefix.
    for app, prefix, runner in (("HeroicPrefixGame", epic_prefix, "proton"),
                                ("HeroicWineGame", wine_prefix, "wine"),
                                # Still pointing at the pre-rename path — the whole point of case 6.
                                ("HeroicOfflineGame", configured_prefix, "proton")):
        write_json(os.path.join(config_root, "GamesConfig", f"{app}.json"), {
            app: {"winePrefix": prefix, "wineVersion": {"type": runner, "name": f"fake-{runner}"}}
        })

    # A native config root that exists but holds no config.json. Uninstalling Heroic leaves this
    # behind, and an empty shell must not be reported as an installation.
    os.makedirs(os.path.join(home, ".config", "heroic"), exist_ok=True)

    return {
        "config_root": config_root,
        "epic_install": epic_install,
        "epic_prefix": epic_prefix,
        "epic_save": epic_save,
        "wine_prefix": wine_prefix,
        "wine_save": wine_save,
        "linux_install": linux_install,
        "renamed_prefix": renamed_prefix,
        "configured_prefix": configured_prefix,
        "stranded_save": stranded_save,
        "sideload_library": sideload_library,
    }


def main() -> int:
    root = os.path.abspath(sys.argv[1])
    home = os.path.join(root, "home")
    steam = os.path.join(home, ".steam", "steam")
    games = os.path.join(root, "games")

    # --- Game 1: in-prefix ------------------------------------------------------------------
    prefix = os.path.join(steam, "steamapps", "compatdata", PREFIX_APPID_UNSIGNED)
    # The manifest's <winAppData> token must land exactly here.
    in_prefix_save = os.path.join(
        prefix, "pfx", "drive_c", "users", "steamuser",
        "AppData", "Roaming", "FakePrefixGame")
    os.makedirs(in_prefix_save, exist_ok=True)
    prefix_install = os.path.join(games, "FakePrefixGame")
    os.makedirs(prefix_install, exist_ok=True)

    # --- Game 2: portable (saves next to the .exe) -------------------------------------------
    portable_install = os.path.join(games, "FakePortableGame")
    portable_save = os.path.join(portable_install, "Saves")
    os.makedirs(portable_save, exist_ok=True)
    # A prefix exists for it too (Proton makes one for any shortcut), but its saves are NOT in it.
    os.makedirs(
        os.path.join(steam, "steamapps", "compatdata", PORTABLE_APPID_UNSIGNED, "pfx", "drive_c"),
        exist_ok=True)

    # --- Game 3: an INSTALLED Steam game, in a second library -------------------------------
    # Deliberately not in the main Steam root. An installed game keeps its prefix in the library it
    # is installed in — unlike a shortcut, whose prefix always sits in the main root — so a scanner
    # that reuses the root it was handed finds the game and never finds its saves. A Deck's SD card
    # is exactly this case.
    steam_library = os.path.join(root, "sdcard", "SteamLibrary")
    installed_steamapps = os.path.join(steam_library, "steamapps")
    installed_install = os.path.join(installed_steamapps, "common", "FakeInstalledGame")
    installed_prefix = os.path.join(installed_steamapps, "compatdata", INSTALLED_APPID)
    installed_save = os.path.join(
        installed_prefix, "pfx", "drive_c", "users", "steamuser",
        "AppData", "Roaming", "FakeInstalledGame")
    os.makedirs(installed_save, exist_ok=True)
    os.makedirs(installed_install, exist_ok=True)

    with open(os.path.join(installed_steamapps, f"appmanifest_{INSTALLED_APPID}.acf"), "w") as f:
        f.write(
            '"AppState"\n{\n'
            f'\t"appid"\t\t"{INSTALLED_APPID}"\n'
            '\t"name"\t\t"Fake Installed Game"\n'
            '\t"installdir"\t\t"FakeInstalledGame"\n'
            "}\n")

    # A runtime, not a game. Filtered by appid — it ships no toolmanifest.vdf, so it is the one
    # case the hardcoded list still has to carry.
    with open(os.path.join(installed_steamapps, "appmanifest_228980.acf"), "w") as f:
        f.write(
            '"AppState"\n{\n'
            '\t"appid"\t\t"228980"\n'
            '\t"name"\t\t"Steamworks Common Redistributables"\n'
            '\t"installdir"\t\t"Steamworks Shared"\n'
            "}\n")

    # A compat tool with an appid NOTHING can have hardcoded — that is the entire point. A Deck
    # listed Proton 9/10/11, Proton Hotfix and Steam Linux Runtime 4.0 as enrollable games because
    # the filter was a list of appids and Valve keeps minting new ones. The marker that identifies
    # it is toolmanifest.vdf in its install directory, which every compat tool has and no game does.
    tool_install = os.path.join(installed_steamapps, "common", "Proton 99.0")
    os.makedirs(tool_install, exist_ok=True)
    with open(os.path.join(tool_install, "toolmanifest.vdf"), "w") as f:
        f.write('"manifest"\n{\n\t"version"\t\t"2"\n\t"commandline"\t\t"/proton run"\n}\n')
    with open(os.path.join(installed_steamapps, "appmanifest_9999990.acf"), "w") as f:
        f.write(
            '"AppState"\n{\n'
            '\t"appid"\t\t"9999990"\n'
            '\t"name"\t\t"Proton 99.0"\n'
            '\t"installdir"\t\t"Proton 99.0"\n'
            "}\n")

    os.makedirs(os.path.join(steam, "steamapps"), exist_ok=True)
    with open(os.path.join(steam, "steamapps", "libraryfolders.vdf"), "w") as f:
        f.write(
            '"libraryfolders"\n{\n'
            '\t"0"\n\t{\n'
            f'\t\t"path"\t\t"{steam}"\n'
            "\t}\n"
            '\t"1"\n\t{\n'
            f'\t\t"path"\t\t"{steam_library}"\n'
            "\t}\n"
            "}\n")

    # --- Heroic ------------------------------------------------------------------------------
    heroic = build_heroic(home, games)

    # --- shortcuts.vdf ------------------------------------------------------------------------
    vdf_dir = os.path.join(steam, "userdata", "1234567", "config")
    os.makedirs(vdf_dir, exist_ok=True)
    entries = (
        shortcut(0, "Fake Prefix Game",
                 os.path.join(prefix_install, "game.exe"), prefix_install, PREFIX_APPID_SIGNED)
        + shortcut(1, "Fake Portable Game",
                   os.path.join(portable_install, "game.exe"), portable_install,
                   PORTABLE_APPID_SIGNED)
        # Heroic's "Add to Steam" entry for game 3. Steam has no compatdata for it — it does not
        # launch it — so this row alone can never produce a save path. It exists to prove the
        # Heroic pass wins the dedupe.
        + shortcut(2, "Heroic Prefix Game",
                   os.path.join(heroic["epic_install"], "game.exe"), heroic["epic_install"],
                   HEROIC_SHORTCUT_APPID_SIGNED)
    )
    with open(os.path.join(vdf_dir, "shortcuts.vdf"), "wb") as f:
        f.write(build_shortcuts_vdf(entries))

    # Report the paths the harness asserts against. Deliberately HOME_DIR, not HOME: the caller
    # eval's this, and clobbering $HOME mid-script would be a nasty surprise.
    print(f"HOME_DIR={home}")
    print(f"STEAM_ROOT={steam}")
    print(f"PREFIX={prefix}")
    print(f"PREFIX_APPID={PREFIX_APPID_UNSIGNED}")
    print(f"PREFIX_SAVE={in_prefix_save}")
    print(f"PORTABLE_APPID={PORTABLE_APPID_UNSIGNED}")
    print(f"PORTABLE_PREFIX={os.path.join(steam, 'steamapps', 'compatdata', PORTABLE_APPID_UNSIGNED)}")
    print(f"PORTABLE_SAVE={portable_save}")
    print(f"PORTABLE_INSTALL={portable_install}")
    print(f"INSTALLED_APPID={INSTALLED_APPID}")
    print(f"INSTALLED_LIBRARY={steam_library}")
    print(f"INSTALLED_PREFIX={installed_prefix}")
    print(f"INSTALLED_SAVE={installed_save}")
    print(f"HEROIC_CONFIG={heroic['config_root']}")
    print(f"HEROIC_EPIC_PREFIX={heroic['epic_prefix']}")
    print(f"HEROIC_EPIC_SAVE={heroic['epic_save']}")
    print(f"HEROIC_WINE_PREFIX={heroic['wine_prefix']}")
    print(f"HEROIC_WINE_SAVE={heroic['wine_save']}")
    print(f"HEROIC_LINUX_INSTALL={heroic['linux_install']}")
    print(f"HEROIC_RENAMED_PREFIX={heroic['renamed_prefix']}")
    print(f"HEROIC_CONFIGURED_PREFIX={heroic['configured_prefix']}")
    print(f"HEROIC_STRANDED_SAVE={heroic['stranded_save']}")
    print(f"HEROIC_SIDELOAD_LIBRARY={heroic['sideload_library']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
