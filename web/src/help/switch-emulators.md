# Nintendo Switch (Eden, Citron, Yuzu, Ryujinx)

SaveLocker reads Switch saves from **Eden**, **Citron**, **Yuzu**, **Suyu**, **Sudachi** and **Ryujinx**, through EmuDeck or a standalone install, on Windows and Linux.

## One game per Switch title, whichever emulator saved it

A Switch save is the title's own folder of files, the same in every emulator. So SaveLocker keeps **one server game per title** (its title ID, like `0100F2C0115B6000`), whichever emulator each machine uses. Eden on a Steam Deck and Ryujinx on a PC sync the same save.

The game is named after the first machine that adds it: Ryujinx's own name for the title, else the ROM's file name in `Emulation/roms/switch`, else the eShop's name for it (SaveLocker carries a list of Switch games, from [titledb](https://github.com/blawar/titledb)), and only for a game that list doesn't know, *Switch &lt;title ID&gt;*. A game added under its title ID takes the first real name another machine brings. Every other machine joins it by the title, whatever it calls it, and its icon is the eShop's.

On **Add games**, a Switch row says which server game it joins. **Change** shows why: a title already on the server is always that game. A new title can instead join a game of the same name that was added by hand, which then becomes this title's game.

**Where each emulator keeps it:**

- **Eden, Citron, Yuzu and the other forks:** `nand/user/save/0000000000000000/<user>/<title ID>`. A device save, like Animal Crossing's island, is under the all-zero user.

**Account saves and device saves.** A Switch game saves either per user (an *account save*, under the user's folder) or once for the whole console (a *device save*, under the all-zero user), and some games keep both. Animal Crossing keeps everything in its device save: that's the island. A game with both syncs as two folders, *main* (the account save) and *device*. When you add a game on a second device, SaveLocker files its folders the way your other devices already do, so each one always lands in the same kind of save. If one device has a stray copy under a user folder (an old island, say) and another has only the device save, the device save is the one that syncs, and the stray copy is left alone.
- **Ryujinx:** `bis/user/save/<number>/0`. The number is handed out per install, so it differs on every machine. SaveLocker reads which number is which title from Ryujinx's own index and never changes that index. After a pull, Ryujinx's working copy (`…/1`) is made the same as the save.

**A ROM with no save yet:** in a Yuzu fork, Add games lists it as *No save here yet*. Adding it puts the server's save where that emulator will look for it. Ryujinx only makes a game's save folder the first time the game runs, so start the game in Ryujinx once before adding it there.

## Several Switch emulators on one machine

Maybe you play a game in Eden one day and in Ryujinx the next, for performance. One emulator's copy syncs with the server: a cloud save goes into it, and it's what uploads. Open the game's page → **Emulator settings**.

**The status line** compares that emulator's save with the cloud's and names the emulator in every button: *The cloud has a newer save than Yuzu here*, **Put the cloud save in Yuzu**, **Keep Yuzu's and upload it**. Putting the cloud save in zips the emulator's own save first; uploading keeps the cloud's old one under Versions.

**On this device, you play it in** has one card per Switch emulator install here, with what each one holds. Eden, Citron, Yuzu and Ryujinx are always listed, and so is any emulator your other devices play the game in. When one emulator is installed twice (EmuDeck's and a standalone one), each install is its own card, marked *EmuDeck* or *Standalone*.

Pick the emulator you'll play in, and your save moves to it:

- **The cloud has nothing newer:** the save you were playing moves into the new emulator, and uploads so your other devices get it.
- **The cloud has a newer save:** that one goes into the new emulator.
- **Both changed:** you pick which one to carry on from.
- **Keep its own save** is there when the emulator you pick already has a save you'd rather use.
- **Ryujinx before it has started the game:** start the game in Ryujinx once, then pick it.
- **Not installed here:** the steps to install it with EmuDeck. **Tell me when it's ready** sends a notification once it's installed and can take the game.

The emulator you leave keeps its save as it was.

**Safety:**

- Nothing is copied while any Switch emulator is open.
- A save about to be overwritten is zipped first, in the agent's `emulator-copies` folder. The last five per game are kept.

## Game versions: "The save data is damaged"

A Switch game writes its save in its own version's format, and an older version of the game can't read it. Animal Crossing says the save data is damaged; other games refuse it, crash, or ask for an update. The save itself is fine: it opens again in the version that made it. So **every device must run the same update of the game** (on PC and Deck alike, the update is installed in the emulator: *File → Install Files to NAND* in a Yuzu fork, *Manage Title Updates* in Ryujinx).

SaveLocker reads which version each emulator runs: a Yuzu fork logs it each time it starts a game, Yuzu's game list also notes an installed update, and Ryujinx logs it too. So an emulator that has never started the game shows no version yet. Every upload says the version it was made with, and:

- **Emulator settings** shows the version on each emulator's card, and warns when the cloud save is from a newer version than the emulator here runs, or when picking an emulator would put a save from a newer version into it.
- **A pull is held** when the latest save is from a newer version than the one here, instead of putting in a save the game can't open. The game's page says which update to install. Once you've installed it, pull the save from the game's page. Holding off until the emulator has started the game with the new update also works: the pull then goes ahead by itself.
- **A newer version here than on your other devices:** once you play here, the save is in the newer format, and the other devices can't open it until they have that update too.

## Users

Each emulator files a save under a user: a Yuzu fork by the user's folder, Ryujinx in its index. A copy goes under that emulator's current user (Yuzu's *current user*, Ryujinx's last-opened profile), which the Emulator settings page shows.

Most games don't record the user in their save files, so nothing else about the user needs to match. Some do: Animal Crossing links each island resident to the ID of the user who made them. On an emulator whose user has another ID, the game can't find that user and asks to link the resident to a user here, and the next device then asks again.

### One Switch user on every device

**Settings → One Switch user on every device** gives every device's Yuzu-based emulators (Eden, Citron, Yuzu and the other forks) the same user ID. A Switch game's **Emulator settings** shows it too, while your devices' users differ.

- **You pick the user.** The card lists each device's users with their IDs, and SaveLocker never picks one for you. For Animal Crossing, pick the user whose device the island was made on.
- **Every install switches**, on this device at once and on the others at their next check, never while a Switch emulator is open. The current user of each Yuzu-based install takes the chosen ID and keeps its name. Its saves move to the new ID's folder, so every game still finds its save, and the games SaveLocker syncs follow, on the server too.
- **Backups:** the emulator's users file and every save about to move or be replaced are zipped first in the agent's `switch-users` folder.
- **A game saved under both users** on one device waits. The card asks which one to keep, and the other is in the backup.
- **Put back this device's original user** undoes it on this device from the backup, and this device then keeps its own user. **Use it here too** switches it again. **Stop sharing a user** stops devices switching; none is put back.
- Ryujinx keeps the user ID in its own save list too, so it's left as it is.
