# Xbox 360 (Xenia): profiles and DLC

Xenia keeps a game's saves under an Xbox **profile**, and some saves only load with the **DLC** or **title update** they were made with. SaveLocker handles both so a save made on one machine works on another.

## The game's profile

Each Xbox 360 game belongs to one profile, the one its saves were under on the first machine that added it. The console shows it on the game's page as **Xbox 360 profile**.

**What syncs with each game:**

- The game's saves.
- Its achievements file (`<title ID>.gpd`).
- Its row of the profile's main file: unlocked achievements and gamerscore for that game.

A pull merges that one row and recounts the totals, so achievements another game unlocked since are never rolled back.

**The first pull on a machine without the profile** also copies the profile itself: the account (gamertag) and its settings. A profile already on that machine is never overwritten.

**A machine that has the game under its own profile:** Add games puts it under the game's profile instead and says so. That machine's own profile and its saves stay where they are.

## Which profile Xenia starts as

Xenia signs in as the profile named in its config (`logged_profile_slot_0_xuid`). If that is another profile, the game starts without its saves. Before a game starts, SaveLocker checks it. Set what it does in the agent's **Settings → Xbox 360 (Xenia)**:

- **Switch, and ask where I can (default):**
  - SaveLocker switches Xenia to the game's profile.
  - With the Decky plugin in Game Mode, or Playnite on Windows, you're asked first and can keep the other profile. *Don't ask again* remembers your answer for that game.
  - Started any other way, it switches without asking.
- **Switch without asking.**
- **Leave Xenia as it is.**

The check runs whenever SaveLocker is in the launch path:

- Steam's launch options `savelocker run -- %command%` (shown in the Decky plugin);
- Playnite's plugin;
- a pull before launch.

Xenia Manager's per-game config is switched along with the main one. Nothing is written while Xenia is running.

**Not covered:** a game started from EmulationStation (ES-DE) or Xenia directly, without SaveLocker in the launch path, gets no check. Its saves still sync while the agent runs. To check the profile anyway, add the game to Steam with the launch options above.

The console's game page shows **Xbox 360 profile at launch**: for each machine, what Xenia was set to at the game's last launch and what SaveLocker did. A row in amber means the game started as another profile, without its saves.

## DLC and title updates

Every push records which DLC packs and title updates are installed, by name, ID and size only. The packs themselves are never stored.

**If the latest save needs packs this machine doesn't have,** the pull is **held**: your save here stays, and the game's page shows which packs are missing. You can choose:

- **Hand them over from \<machine\>:** the machine that made the save sends the packs through your server.
- **Pull anyway:** restore the save without them. The game may refuse it, or leave out what came from the packs.
- **Not now:** install the packs yourself. The pull goes ahead by itself once they're there (checked every minute).

An automatic pull (before launch, from the console, or in the background) never asks. It holds the pull and sends a notification.

**What happens to a hand-over:**

1. **The other machine is asked.** It sees the request within about 20 seconds while its agent runs. In **Settings → Xbox 360**, each machine chooses to send automatically (default), ask first, or never send. Set to ask first, it shows a notification and the request on the game's page, with **Send** and **Decline**.
2. **The packs are uploaded in pieces.** An interrupted upload or download resumes where it stopped.
3. **They're downloaded, checked and installed.** Only the asking machine can download them. They install while Xenia is closed, where that machine's Xenia reads them. A pack whose size doesn't match what was listed is refused.
4. **They're deleted from the server** as soon as they are installed, or after the request expires (7 days by default).

**Server limits** (console **Configuration → DLC hand-overs**):

- `Storage:MaxHandoverMb`: the largest hand-over, 2048 MB by default. Separate from the save upload limit.
- `Storage:HandoverExpiryDays`: how long a request waits.

The card also shows what the server holds right now, and lets you cancel any hand-over.

**From the command line:** `savelocker dlc status`, `dlc ask <game>`, `dlc send <id>` and `dlc decline <id>`. See **Agent CLI reference**.
