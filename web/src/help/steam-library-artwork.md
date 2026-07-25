# Steam Library artwork for SaveLocker (Steam Deck)

When you add SaveLocker to your Steam library as a non-Steam shortcut (so you can run its
gamepad-native UI in Game Mode — see [Installing the agent](#help/installing-the-agent)), Steam shows
it as a plain grey box. The Linux agent ships proper library art so you can replace that box in a
minute. Applying it is a one-time, manual step — Steam doesn't let an app set its own library art.

## Where the images are

The install puts them here:

```
~/.local/share/SaveLocker/artwork/
```

Four files, each already sized for its Steam slot:

- **`capsule.png`** — *Vertical capsule (grid)*: the portrait tile in your library grid.
- **`capsule-wide.png`** — *Horizontal capsule*: wide list views and the "Recent games" row.
- **`hero.png`** — *Hero*: the wide banner behind the game's header.
- **`logo.png`** — *Logo*: the transparent logo laid over the hero banner.

## Apply it

You can do this in **Desktop Mode** or **Game Mode** — the menu is the same.

1. Find **SaveLocker** in your library.
2. **Right-click it** (Desktop Mode) or press the **gear / Manage** button (Game Mode) → **Manage →
   Set Custom Artwork**.
3. Steam opens a file picker for one artwork slot at a time. Browse to
   `~/.local/share/SaveLocker/artwork/` and pick the matching file from the list above.
4. Repeat **Set Custom Artwork** for each of the four slots.

> **Can't see the folder in the picker?** It's under a hidden dot-folder (`.local`). In the KDE file
> picker press **Ctrl+H** to show hidden folders, or type the path
> `/home/deck/.local/share/SaveLocker/artwork` directly into the location bar (replace `deck` with
> your username if it differs).

That's it — SaveLocker now looks like a real entry in your library instead of a grey box.

## Notes

- **This is cosmetic only.** It changes nothing about syncing; skip it entirely if you don't care how
  the shortcut looks.
- **The art is per-user, stored by Steam** (under `userdata/<id>/config/grid/`). If you add SaveLocker
  to a second Deck or user, set the artwork again there.
- **After an agent update** (re-running `install.sh` from a newer tarball) the images are refreshed in
  place, but Steam keeps whatever you already applied — you don't have to redo this.
