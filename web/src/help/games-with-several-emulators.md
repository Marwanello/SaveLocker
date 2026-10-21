# Games saved with more than one emulator

Some games run in more than one emulator. *Metroid Prime - Hunters* runs in **melonDS** and in **RetroArch** (its melonDS DS core). *Metroid Prime Trilogy* runs in **Dolphin** and in **PrimeHack**. SaveLocker lists such a game **once**, and its page has **one tab per emulator**.

## Each emulator keeps its own save

Two emulators never share a save folder, and they don't always write the same file:

- melonDS writes `Metroid Prime - Hunters (USA).sav`;
- RetroArch writes `Metroid Prime - Hunters (USA).srm`, in another folder.

So each emulator's save is still its own game on the server, with its own:

- versions and Latest;
- save folders on each machine;
- rules, exclude patterns and conflicts.

The game's entry groups them, and nothing else changes: each emulator's save syncs exactly as before.

## In the console

**Games list.** A game saved with two emulators is one row. Its second line shows each emulator's icon and *2 emulators*. Its status is the worst of its emulators' saves, so a conflict in any of them shows. Its size is the sum, and its date the newest save.

**Grouping.** *Platform* lists the game under each of its emulators. *Device* lists it under every machine that has any of its saves.

**The game's page** has one tab per emulator, under the cover and name. Everything below the tabs belongs to the open tab:

- the versions, and Set as Latest;
- the save folders, rules and exclude patterns;
- Delete game and Merge.

Each confirmation names the emulator, for example *Only the RetroArch save changes*. Deleting a game deletes that emulator's save only; the other tabs stay.

A tab has its own status dot, so a conflict in a tab you haven't opened still shows. The status chip at the top then says *Conflict in PrimeHack*.

The address bar remembers the tab you're on, so a refresh or a shared link opens the same one.

## Grouping and splitting by hand

SaveLocker groups games on its own when their names match once the emulator is taken off the end ("(RetroArch)"). The console, the region and the name must all match, and a Switch game is already one game whichever Switch emulator saves it, so it's never grouped.

- **Group with…** at the end of the tabs adds another emulator's game of the same console. On a game with only one emulator, it's under *Other emulators*. No version moves, so there's nothing to confirm.
- **Split *emulator* out** takes the open tab's save back out. It becomes its own entry again, and SaveLocker won't group it on its own again.

## On the agent (Windows tray and Steam Deck)

The agent lists the game once too. Its row says which emulators are **not set up here**. The game's page has the same tabs:

- an emulator that's set up on this device syncs from its tab: *Sync melonDS save*, *Push now*, *Pull latest*;
- one that isn't says *Not set up here* and offers **Choose save folder**;
- on a controller, **R3** moves to the next emulator's tab. LB and RB still move between sections.

## Copying a save to another emulator

Played on the Deck in melonDS, and want to carry on in RetroArch on your PC? Open the game, pick the melonDS tab, and use **Copy to another emulator…** on the agent, or **Copy to…** beside a version in the console.

You pick the version to copy and the emulator it goes to, and SaveLocker shows what each file becomes:

```
Metroid Prime - Hunters (USA).sav  →  Metroid Prime - Hunters (USA).srm
```

The copy becomes the other emulator's **new Latest**, and every device that plays it there pulls it on its next sync.

- **Nothing is lost.** The other emulator's old Latest stays in its versions, so you can set it back.
- **The save you copied from doesn't change.**

**Which emulators can copy.** Only pairs whose saves are the same format:

- **melonDS ⇄ RetroArch** (a DS save: the same bytes, `.sav` renamed `.srm`);
- **Dolphin ⇄ PrimeHack** (the same Wii or GameCube save folder).

Any other emulator is listed but greyed, with the reason.

**When it waits.** A copy is refused while:

- the other emulator's save has an open conflict (resolve it first);
- a machine is playing the game in that emulator;
- on the agent, that emulator has the game open on this device.
