# Emulator and console icons

The emulator and console icons in the console (`web/src/platformIcons.generated.ts`), the agent UI
(`agent-ui/src/platformIcons.generated.ts`) and the Deck's Game Mode UI (`src/Agent.Linux/Ui/EmulatorIcons.g.cs`) are
generated from these sources by `web/scripts/build-platform-icons.mjs` (`npm --prefix web run icons:build`), which records
the commit of each it used. Colours are replaced with the UI's own, and nothing else is changed.

| Icons | Source | Licence |
|---|---|---|
| Emulators: RetroArch, Dolphin (also PrimeHack), PPSSPP, DuckStation, Azahar, Eden, Citron, Yuzu, Suyu, Sudachi, Ryujinx, melonDS, Vita3K, Xenia | [Arcticons](https://github.com/Arcticons-Team/Arcticons) (`icons/white`), © the Arcticons contributors | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |
| Consoles (also standing in for PCSX2, shadPS4, RPCS3, Cemu, ScummVM, Model 2 and Supermodel) | RetroArch's Monochrome theme, [libretro/retroarch-assets](https://github.com/libretro/retroarch-assets) (`src/xmb/monochrome`), © libretro | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |

The share-alike terms of CC BY-SA 4.0 cover the Arcticons icon files as adapted here (the generated modules' icon data),
not the rest of SaveLocker's code. Console and emulator names and logos are their owners' trademarks; the icons only say
which console or emulator a save comes from.
