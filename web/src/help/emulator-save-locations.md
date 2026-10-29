# Where emulator saves are looked for

**Add games** lists emulator games by reading each emulator's save folders on this machine. It only knows the folders below: if your emulator keeps its saves somewhere else, its games never show up. This article lists every folder SaveLocker reads, in the order it tries them, and ends with a checklist for a game that isn't listed.

How to read the paths:

- `Emulation` is EmuDeck's folder (see the next section). The same folders are read inside it on Windows, SteamOS and any other Linux.
- `%APPDATA%` is `C:\Users\<you>\AppData\Roaming`, `%LOCALAPPDATA%` is `…\AppData\Local`, and `Documents` is your Documents folder wherever you moved it.
- `~` is your home folder. On Linux, `~/.config` and `~/.local/share` are `$XDG_CONFIG_HOME` and `$XDG_DATA_HOME` when you set those, except where a row says otherwise.
- A *Flatpak* folder is the app's own home, `~/.var/app/<app id>`.

SaveLocker reads **every** place that exists: EmuDeck's folders first, then the standalone ones, even on a machine that has EmuDeck. A folder reached two ways (EmuDeck's `Emulation/saves` entries are links into the emulator's own folders) counts once, and Add games shows the folder where the files really are, which may not be inside `Emulation`.

## Finding EmuDeck's Emulation folder

Every `Emulation` folder found is read, so an internal one and one on an SD card both count.

| | Windows | Linux / Steam Deck |
|---|---|---|
| 1. EmuDeck's own setting | `$emulationPath` in `~\EmuDeck\settings.ps1`, then in `%APPDATA%\EmuDeck\settings.ps1` | `emulationPath` in `~/.config/EmuDeck/settings.sh`, then in `~/emudeck/settings.sh` |
| 2. Your home folder | `~\Emulation` | `~/Emulation` |
| 3. Other drives | `Emulation` at the root of every fixed or removable drive (`D:\Emulation`). Network drives are skipped | An SD card: `/run/media/<card>/Emulation` and `/run/media/<user>/<card>/Emulation` |

## Each emulator's folders

The **EmuDeck** column is the folder inside `Emulation`. The other columns are the standalone installs, tried in order; most of them name a config file whose setting can move the saves, and the default folder is used only when that setting is empty.

### RetroArch

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/retroarch/saves`, states in `Emulation/saves/retroarch/states` |
| **Windows** | `retroarch.cfg` in `%APPDATA%\RetroArch`, then in `~\emudeck\EmulationStation-DE\Emulators\RetroArch` |
| **Linux / Deck** | `retroarch.cfg` in `~/.config/retroarch` (always this folder, whatever `$XDG_CONFIG_HOME` says), then in the Flatpak's `~/.var/app/org.libretro.RetroArch/config/retroarch` |
| **Setting** | `savefile_directory` and `savestate_directory`; unset or `default` means `saves` and `states` beside `retroarch.cfg` |
| **Files** | `<rom>.srm` (and `.rtc`), states `<rom>.state*`. With *Sort saves by core* on, one level of core folders is read too |

### melonDS (Nintendo DS)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/melonds/saves`, states in `Emulation/saves/melonds/states` |
| **Windows** | The config in `%APPDATA%\EmuDeck\Emulators\melonDS`, then `%LOCALAPPDATA%\melonDS`, then `%APPDATA%\melonDS` |
| **Linux / Deck** | The config in `~/.config/melonDS`, then in the Flatpak's `~/.var/app/net.kuribo64.melonDS/config/melonDS` |
| **Setting** | `SaveFilePath` and `SavestatePath` under `[Instance0]` in `melonDS.toml`, or in the older `melonDS.ini` when there is no `melonDS.toml`. Left empty, melonDS saves beside the ROM, and SaveLocker reads the last ROM folder you opened (`LastROMFolder`) |
| **Files** | `<rom>.sav`, states `<rom>.ml1`…`.ml8` |

A folder with neither config file is skipped: start melonDS once so it writes one.

### Supermodel (Sega Model 3)

Supermodel has no EmuDeck saves folder.

| | Where SaveLocker looks |
|---|---|
| **Windows** | `%APPDATA%\EmuDeck\Emulators\Supermodel\NVRAM` only. A standalone copy can be anywhere, so it isn't looked for |
| **Linux / Deck** | `~/.supermodel/NVRAM` (where EmuDeck keeps it), then `~/.local/share/supermodel/NVRAM`, then the Flatpak's `~/.var/app/com.supermodel3.Supermodel/data/supermodel/NVRAM` |
| **Files** | `NVRAM/<set>.nv`, states `Saves/<set>.st<slot>` beside it |

### Model 2 Emulator (Sega Model 2)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/roms/model2/NVDATA` — the emulator lives with its ROMs, on SteamOS and Windows alike |
| **Windows** | `%APPDATA%\EmuDeck\Emulators\m2emulator\NVDATA` |
| **Files** | `NVDATA/<set>.DAT`, states `STATES/<set><slot>.sta` beside it |

### ScummVM

ScummVM's games are the ones in its launcher, read from `scummvm.ini`. With no `scummvm.ini`, nothing is found.

| | Where SaveLocker looks |
|---|---|
| **Windows** | `%APPDATA%\ScummVM\scummvm.ini` |
| **Linux / Deck** | `~/.config/scummvm/scummvm.ini`, then the older `~/.scummvmrc`, then the Flatpak's `~/.var/app/org.scummvm.ScummVM/config/scummvm/scummvm.ini` |
| **Setting** | `savepath=` in the game's own section, else in `[scummvm]` (EmuDeck sets it to `Emulation/saves/scummvm/saves`), else the default: `%APPDATA%\ScummVM\Saved games`, `~/.local/share/scummvm/saves`, or the Flatpak's `…/data/scummvm/saves` |
| **Files** | `<target>.s##` or `<target>.###`. No save states |

### PCSX2 (PlayStation 2)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/pcsx2/saves`, states in `Emulation/saves/pcsx2/states` |
| **Windows** | `inis\PCSX2.ini` in `%APPDATA%\EmuDeck\Emulators\PCSX2-Qt`, then in `Documents\PCSX2` |
| **Linux / Deck** | `inis/PCSX2.ini` in `~/.config/PCSX2`, then in the Flatpak's `~/.var/app/net.pcsx2.PCSX2/config/PCSX2` |
| **Setting** | `MemoryCards` and `Savestates` under `[Folders]`; default `memcards` and `sstates` in the same folder |
| **Files** | A *folder* memory card, one subfolder per save (`BASLUS-21050…`). A card *file* is shown greyed out — see [Emulator backup best practices](#help/emulator-backups) |

### DuckStation (PlayStation)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/duckstation/saves`, states in `Emulation/saves/duckstation/states` |
| **Windows** | `settings.ini` in `%APPDATA%\EmuDeck\Emulators\duckstation`, then `Documents\DuckStation`, then `%LOCALAPPDATA%\DuckStation` |
| **Linux / Deck** | `settings.ini` in `~/.local/share/duckstation`, then in the Flatpak's `~/.var/app/org.duckstation.DuckStation/data/duckstation` |
| **Setting** | `Directory` under `[MemoryCards]` and `SaveStates` under `[Folders]`; default `memcards` and `savestates` in the same folder |
| **Files** | One card per game (`<title>_1.mcd` or `<serial>_1.mcd`). A shared card is shown greyed out |

### Dolphin and PrimeHack (GameCube and Wii)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/dolphin/GC` and `…/dolphin/Wii`; PrimeHack's in `Emulation/saves/primehack/…`. States in `StateSaves` (SteamOS) or `states` (Windows) beside them |
| **Windows** | Dolphin's user folder: `%APPDATA%\EmuDeck\Emulators\Dolphin-x64\User`, then `Documents\Dolphin Emulator`, then `%APPDATA%\Dolphin Emulator`. PrimeHack: `%APPDATA%\EmuDeck\Emulators\primehack\User` |
| **Linux / Deck** | Dolphin: `~/.local/share/dolphin-emu`, then `~/.dolphin-emu`, then the Flatpak's `~/.var/app/org.DolphinEmu.dolphin-emu/data/dolphin-emu`. PrimeHack: its Flatpak's `~/.var/app/io.github.shiiion.primehack/data/dolphin-emu` |
| **Inside** | `GC/<region>/Card A` (a GCI folder), `Wii/title`, `StateSaves` |
| **Not read** | A custom GCI folder or Wii NAND folder set in Dolphin's settings |

The raw card `GC/MemoryCardA.<region>.raw` is shown greyed out.

### PPSSPP (PSP)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/ppsspp/saves`, states in `Emulation/saves/ppsspp/states` |
| **Windows** | `%APPDATA%\EmuDeck\Emulators\PPSSPP\memstick\PSP`, then `Documents\PPSSPP\PSP` |
| **Linux / Deck** | `~/.config/ppsspp/PSP`, then the Flatpak's `~/.var/app/org.ppsspp.PPSSPP/config/ppsspp/PSP` |
| **Inside** | `SAVEDATA`, states in `PPSSPP_STATE` |
| **Not read** | A portable copy's `memstick` anywhere else; RetroArch's PPSSPP core, which keeps PSP saves in its own folder |

### RPCS3 (PlayStation 3)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/rpcs3/saves` |
| **Windows** | `%APPDATA%\EmuDeck\Emulators\RPCS3`. A standalone RPCS3 can be anywhere, so it isn't looked for |
| **Linux / Deck** | `~/.config/rpcs3`, then the Flatpak's `~/.var/app/net.rpcs3.RPCS3/config/rpcs3` |
| **Setting** | `/dev_hdd0/` in `vfs.yml` (or `config/vfs.yml`); default `dev_hdd0` in the emulator's folder. With no `vfs.yml`, RPCS3 has never run there and nothing is read |
| **Inside** | `dev_hdd0/home/<user>/savedata`, every user |

### Vita3K (PS Vita)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/Vita3K/saves`, and `Emulation/storage/Vita3K/ux0/user/<user>/savedata` |
| **Windows** | `config.yml` in `%APPDATA%\EmuDeck\Emulators\Vita3K`, then `%APPDATA%\Vita3K\Vita3K\config.yml` |
| **Linux / Deck** | `~/.config/Vita3K/config.yml` |
| **Setting** | `pref-path`; default `%APPDATA%\Vita3K\Vita3K` or `~/.local/share/Vita3K/Vita3K` |
| **Inside** | `ux0/user/<user>/savedata`, every user |

### shadPS4 (PlayStation 4)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/shadps4/saves/<user>` |
| **Windows** | `%APPDATA%\EmuDeck\Emulators\ShadPS4-qt\user`, then `%APPDATA%\shadPS4` |
| **Linux / Deck** | `~/.local/share/shadPS4` |
| **Inside** | `savedata/<user>` (older builds) and `home/<user>/savedata` (newer builds) |
| **Not read** | A `user` folder beside a shadPS4 exe anywhere else |

### Cemu (Wii U)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/Cemu/saves` |
| **Windows** | `settings.xml` in `%APPDATA%\EmuDeck\Emulators\cemu`, then in its `portable` folder, then in `%APPDATA%\Cemu` |
| **Linux / Deck** | `~/.config/Cemu/settings.xml` (data in `~/.local/share/Cemu`), then the Flatpak's `~/.var/app/info.cemu.Cemu/config/Cemu/settings.xml` (data in `…/data/Cemu`) |
| **Setting** | `<mlc_path>`; default `mlc01` in the data folder |
| **Inside** | `usr/save/00050000/<title ID>` |

### Azahar and Citra (3DS)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/azahar/saves`, states in `Emulation/saves/azahar/states` |
| **Windows** | `%APPDATA%\EmuDeck\Emulators\azahar\user`, then `%APPDATA%\Azahar`, then Citra's `%APPDATA%\Citra` |
| **Linux / Deck** | `~/.local/share/azahar-emu` (config in `~/.config/azahar-emu`), the Flatpak `io.github.azahar.Azahar`, then Citra's `~/.local/share/citra-emu` and the Flatpak `org.citra_emu.citra` |
| **Setting** | `sdmc_directory` in `qt-config.ini`, when `use_custom_storage` is on; default `sdmc` in the data folder |
| **Inside** | `sdmc/Nintendo 3DS/<id>/<id>/title/00040000/<title ID>/data`, states in `states` |

### Xenia (Xbox 360)

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/xenia/saves` |
| **Windows** | `%APPDATA%\EmuDeck\Emulators\xenia`, then `Documents\Xenia` |
| **Linux / Deck** | `~/.local/share/Xenia` (a native Linux build) |
| **Setting** | `content_root` in `xenia-canary.config.toml` or `xenia.config.toml`; default `content` |
| **Not read** | A portable Xenia (with `portable.txt`) anywhere else |

See [Xbox 360 (Xenia): profiles and DLC](#help/xbox-360-profile-and-dlc) for what inside `content` syncs.

### Switch: Eden, Citron, Yuzu, Suyu, Sudachi and Ryujinx

| | Where SaveLocker looks |
|---|---|
| **EmuDeck** | `Emulation/saves/<emulator>/saves` — `eden`, `citron`, `yuzu`, `suyu`, `sudachi` or `ryujinx` |
| **Windows** | Yuzu and its forks: EmuDeck's portable `%APPDATA%\EmuDeck\Emulators\eden-windows-msvc\user`, `…\citron\user` or `…\yuzu\yuzu-windows-msvc\user`, then `%APPDATA%\<emulator>`. Ryujinx: `%APPDATA%\EmuDeck\Emulators\Ryujinx\portable`, then `%APPDATA%\Ryujinx` |
| **Linux / Deck** | Yuzu and its forks: `~/.config/<emulator>/qt-config.ini` with data in `~/.local/share/<emulator>`, plus the Flatpaks `org.yuzu_emu.yuzu` and `org.suyu_emu.suyu`. Ryujinx: `~/.config/Ryujinx`, then the Flatpak's `~/.var/app/org.ryujinx.Ryujinx/config/Ryujinx` |
| **Setting** | Yuzu and its forks: `nand_directory` in `qt-config.ini`; default `nand` in the data folder |
| **Inside** | `nand/user/save` (Yuzu and its forks), `bis/user/save` (Ryujinx) |

See [Nintendo Switch (Eden, Citron, Yuzu, Ryujinx)](#help/switch-emulators) for how one game is shared across these emulators.

## My emulator game isn't in Add games

Work down this list; most games turn up by step 3.

1. **Rescan.** Add games keeps the list from its last scan: press **Rescan** at the top. If the game is already added on this machine it's hidden — turn off **Hide enrolled**. If an emulator chip is selected, check it's the right emulator.
2. **Save in the game once.** Only an in-game save makes a game appear: playing, or loading a ROM, is not enough. A game you've only played from **save states** is listed too, marked *Save states only so far*, for RetroArch, PCSX2, DuckStation, Dolphin, PrimeHack, PPSSPP and Azahar; for the others, save in the game, quit, and rescan. On SteamOS, EmuDeck installs Supermodel and Model 2 with NVRAM files for many games already in place: an untouched one doesn't count until the game writes its own (quit the game normally, or take a save state in Model 2).
3. **Check where the emulator saves.** Open the emulator's settings and compare its save or memory card folder with the section for it above. A folder set with the **Setting** listed there is followed wherever it points. A folder SaveLocker doesn't read — a portable copy in its own folder, a folder set some other way, anything under **Not read** — is never found. Point the emulator at one of the folders above (EmuDeck's is simplest), move the saves there, and rescan.
4. **A greyed-out row is a shared memory card.** PCSX2's card file, Dolphin's raw card and DuckStation's shared card hold every game's saves, so they can't be added as one game. The row says how to switch the emulator to a card per game; [Emulator backup best practices](#help/emulator-backups) has the steps.
5. **Look under the other emulator.** A game is listed under the emulator that saved it: the same title played in RetroArch's core and in the standalone emulator is two rows, two games, and they never sync with each other (Switch emulators are the exception). See [Games saved with more than one emulator](#help/games-with-several-emulators).
6. **Played on another machine, never here?** For PCSX2, DuckStation, PPSSPP, melonDS and Supermodel, SaveLocker sets the game up by itself once the emulator is installed here and the game's ROM is in `Emulation/roms/<system>`. For the other emulators, play and save once here, then add it.
7. **Check the log.** Each emulator is scanned on its own; when one fails, `agent.log` says `Scan source '<emulator> saves'` with the error, and the other emulators' games are still listed. `savelocker scan --no-prompt` lists what the scan finds from a terminal.
8. **Map the folder by hand, as a last resort.** [Adding games & mapping save folders](#help/adding-games) covers mapping any folder yourself. A hand-mapped folder syncs *everything* in it, so only map a folder that holds this one game's saves — never a folder every game shares.
