# Emulator and console icons

The emulator and console icons in the console (`web/src/platformIcons.generated.ts`), the agent UI
(`agent-ui/src/platformIcons.generated.ts`) and the Deck's Game Mode UI (`src/Agent.Linux/Ui/EmulatorIcons.g.cs`) are
generated from these sources by `web/scripts/build-platform-icons.mjs` (`npm --prefix web run icons:build`), which records
the commit of each it used. Colours are replaced with the UI's own; beyond that, only the PlayStation marks are composed (logo plus the wordmark's number or lettering) and Xenia's glyph traced.

| Icons | Source | Licence |
|---|---|---|
| Emulators: RetroArch, Dolphin (also PrimeHack), PPSSPP, DuckStation, Azahar, Eden, Citron, Yuzu, Suyu, Sudachi, Ryujinx, melonDS, Vita3K | [Arcticons](https://github.com/Arcticons-Team/Arcticons) (`icons/white`), © the Arcticons contributors | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |
| Consoles other than PlayStation (also standing in for Cemu, ScummVM, Model 2 and Supermodel) | RetroArch's Monochrome theme, [libretro/retroarch-assets](https://github.com/libretro/retroarch-assets) (`src/xmb/monochrome`), © libretro | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) |
| PlayStation, PS2, PS3, PS4, PSP and PS Vita (also standing in for PCSX2, RPCS3 and shadPS4): the PlayStation logo with the number or lettering cut out of each console's own wordmark | [Simple Icons](https://github.com/simple-icons/simple-icons) 16.35.0 (`icons/playstation*.svg`) | [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/) |
| Xenia | Xenia's updated logo, [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Xenia_Emulator_Updated_Logo.svg), by Takuyakoz; that file is a picture, so its chip was traced once into `web/scripts/icon-sources/xenia.svg` | [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |

The share-alike terms of CC BY-SA 4.0 cover the Arcticons icon files and the traced Xenia glyph as adapted here (the generated modules' icon data),
not the rest of SaveLocker's code. Console and emulator names and logos (the PlayStation logo and wordmarks are Sony Interactive Entertainment's) are their owners' trademarks; the icons only say
which console or emulator a save comes from.
