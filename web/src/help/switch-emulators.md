# Nintendo Switch (Eden, Citron, Yuzu, Ryujinx)

SaveLocker reads Switch saves from **Eden**, **Citron**, **Yuzu**, **Suyu**, **Sudachi** and **Ryujinx**, through EmuDeck or a standalone install, on Windows and Linux.

## One game per Switch title, whichever emulator saved it

A Switch save is the title's own folder of files, the same in every emulator. So SaveLocker keeps **one server game per title** (its title ID, like `0100F2C0115B6000`), whichever emulator each machine uses. Eden on a Steam Deck and Ryujinx on a PC sync the same save.

The game is named after the first machine that adds it: Ryujinx's own name for the title, else the ROM's file name in `Emulation/roms/switch`, else *Switch &lt;title ID&gt;*. Every other machine joins it by the title, whatever it calls it.

**Where each emulator keeps it:**

- **Eden, Citron, Yuzu and the other forks:** `nand/user/save/0000000000000000/<user>/<title ID>`. A device save, like Animal Crossing's island, is under the all-zero user.
- **Ryujinx:** `bis/user/save/<number>/0`. The number is handed out per install, so it differs on every machine. SaveLocker reads which number is which title from Ryujinx's own index and never changes that index. After a pull, Ryujinx's working copy (`…/1`) is made the same as the save.

**A ROM with no save yet:** in a Yuzu fork, Add games lists it as *No save here yet*. Adding it puts the server's save where that emulator will look for it. Ryujinx only makes a game's save folder the first time the game runs, so start the game in Ryujinx once before adding it there.

## Several Switch emulators on one machine

Maybe you play a game in Eden one day and in Ryujinx the next, for performance. One emulator's copy syncs with the server. Open the game's page → **Emulator settings** to see every Switch emulator on this machine, with what each one holds:

- **Copy the synced save here:** gives that emulator the synced save, once.
- **Keep in sync:** whichever copy you played last is copied over the other. A change in the other emulator is taken into the synced copy and pushed.
- **Make this the synced copy:** that emulator's copy syncs with the server from now on. If it holds a different save, the next sync pushes it.

**Safety:**

- Nothing is copied while any Switch emulator is open.
- A save about to be overwritten is zipped first, in the agent's `emulator-copies` folder. The last five per game are kept.
- If both copies changed since they were last the same, nothing is copied. The page marks it **Both changed** and asks which one to keep.

## Users

A Switch save's files don't record which user they belong to. Each emulator files them under a user: a Yuzu fork by the user's folder, Ryujinx in its index. A copy goes under that emulator's current user (Yuzu's *current user*, Ryujinx's last-opened profile), which the Emulator settings page shows. Nothing else about the user needs to match.
