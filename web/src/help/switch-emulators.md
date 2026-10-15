# Nintendo Switch (Eden, Citron, Yuzu, Ryujinx)

SaveLocker reads Switch saves from **Eden**, **Citron**, **Yuzu**, **Suyu**, **Sudachi** and **Ryujinx**, through EmuDeck or a standalone install, on Windows and Linux.

## One game per Switch title, whichever emulator saved it

A Switch save is the title's own folder of files, the same in every emulator. So SaveLocker keeps **one server game per title** (its title ID, like `0100F2C0115B6000`), whichever emulator each machine uses. Eden on a Steam Deck and Ryujinx on a PC sync the same save.

The game is named after the first machine that adds it: Ryujinx's own name for the title, else the ROM's file name in `Emulation/roms/switch`, else the eShop's name for it (SaveLocker carries a list of Switch games, from [titledb](https://github.com/blawar/titledb)), and only for a game that list doesn't know, *Switch &lt;title ID&gt;*. A game added under its title ID takes the first real name another machine brings. Every other machine joins it by the title, whatever it calls it, and its icon is the eShop's.

On **Add games**, a Switch row says which server game it joins. **Change** shows why: a title already on the server is always that game. A new title can instead join a game of the same name that was added by hand, which then becomes this title's game.

**Where each emulator keeps it:**

- **Eden, Citron, Yuzu and the other forks:** `nand/user/save/0000000000000000/<user>/<title ID>`. A device save, like Animal Crossing's island, is under the all-zero user.
- **Ryujinx:** `bis/user/save/<number>/0`. The number is handed out per install, so it differs on every machine. SaveLocker reads which number is which title from Ryujinx's own index and never changes that index. After a pull, Ryujinx's working copy (`…/1`) is made the same as the save.

**A ROM with no save yet:** in a Yuzu fork, Add games lists it as *No save here yet*. Adding it puts the server's save where that emulator will look for it. Ryujinx only makes a game's save folder the first time the game runs, so start the game in Ryujinx once before adding it there.

## Several Switch emulators on one machine

Maybe you play a game in Eden one day and in Ryujinx the next, for performance. One emulator's copy syncs with the server: a cloud save goes into it, and it's what uploads. Open the game's page → **Emulator settings**.

**The status line** compares that emulator's save with the cloud's and names the emulator in every button: *The cloud has a newer save than Yuzu here*, **Put the cloud save in Yuzu**, **Keep Yuzu's and upload it**. Putting the cloud save in zips the emulator's own save first; uploading keeps the cloud's old one under Versions.

**On this device, you play it in** has one card per Switch emulator here, with what each one holds, plus any emulator your other devices play the game in that isn't installed here. Pick one to make it the emulator that syncs:

- **One with a save:** switch and keep its save, or switch and put the cloud save in it. The emulator you left keeps its save as it was.
- **One with no save of the game yet:** switch and put the cloud save in it.
- **Ryujinx before it has started the game:** start the game in Ryujinx once, then pick it.
- **One not installed here:** the steps to install it with EmuDeck. **Tell me when it's ready** sends a notification once it's installed and can take the game.

**Copies in the other emulators:**

- **Copy the synced save here:** gives that emulator the synced save, once.
- **Keep in sync:** whichever copy you played last is copied over the other. A change in the other emulator is taken into the synced copy and pushed.

**Safety:**

- Nothing is copied while any Switch emulator is open.
- A save about to be overwritten is zipped first, in the agent's `emulator-copies` folder. The last five per game are kept.
- If both copies changed since they were last the same, nothing is copied. The page marks it **Both changed** and asks which one to keep.

## Users

A Switch save's files don't record which user they belong to. Each emulator files them under a user: a Yuzu fork by the user's folder, Ryujinx in its index. A copy goes under that emulator's current user (Yuzu's *current user*, Ryujinx's last-opened profile), which the Emulator settings page shows. Nothing else about the user needs to match.
