# The Decky plugin (Steam Deck)

SaveLocker has an optional [Decky Loader](https://decky.xyz) plugin for the Steam Deck. It removes the one manual step in Deck setup and puts SaveLocker's controls inside Game Mode.

**It is optional, and it will stay optional.** A Deck without it loses nothing — the copy-paste launch-options path is the supported one and is not going away. If the plugin ever breaks after a Steam client update, that path still works and `savelocker doctor` still tells you what is wrong.

## What it does for you

**It sets launch options for you.** This is the one thing the agent genuinely cannot do. Launch options live in Steam's own config, which Steam holds in memory and rewrites wholesale when it exits — so an edit made by the agent while Steam is running is silently discarded, and one made while Steam is closed races the next launch. Only code running *inside* Steam can set them, which is what a Decky plugin is.

It is careful about it:

- It **merges, never replaces**. If a game already has `mangohud %command%`, environment variables, or its own arguments, they all keep working — the wrapper is substituted into the right position rather than appended.
- It **repairs the most common mistake**: a hand-typed short `savelocker run -- %command%` becomes the full path. Game Mode does not put `~/.local/bin` on `PATH`, so the short form silently prevents the game from launching, and nothing else notices.
- It **only touches games SaveLocker already tracks** that have a Steam AppID. No heuristics, and nothing you did not enroll.

**It warns you before you cause a conflict.** If another machine has a game checked out, you get a notification in Game Mode *before* you launch it. Without the plugin that warning exists only in the agent UI or this console — neither of which you are looking at while holding a Deck.

**It puts sync in the Quick Access panel.** Status (server, last sync, games, saves, agent version), push and pull per game or all at once, and `savelocker doctor` on demand. Those all run the same CLI the desktop does, so they inherit the same guards — a pull still refuses while the game is running, and still refuses to overwrite un-pushed changes unless you force it.

## Installing it

You need Decky Loader itself first — see [decky.xyz](https://decky.xyz).

Then, once, in Decky &rarr; **Install Plugin from URL**:

```
https://github.com/SkorcherX/SaveLocker-Decky/releases/latest/download/SaveLocker.zip
```

That is the whole installation. The agent UI on your Deck (`http://localhost:5178`, Overview tab) shows the same link with a Copy button.

## Staying updated

**The agent updates the plugin for you.** Once it is installed, your SaveLocker agent replaces the plugin's files from this server on the same schedule it checks for its own updates, and Decky reloads it within about a second — no sudo, no Steam restart, nothing to click.

For that to work, upload the plugin's release zip in **Configuration &rarr; Agent updates &rarr; Decky plugin**, the same way you upload the Windows and Linux agent packages. Nothing is offered to a Deck until you do.

This exists because the alternative is worse. Decky can hold exactly **one** custom store URL, and setting it *replaces* the official store — so pointing it at SaveLocker's would cost you every other plugin's updates. Nobody leaves it that way, which in practice means nobody is ever told a SaveLocker plugin update exists. Letting the agent do it avoids the trade entirely.

The `AutoUpdate` setting governs this exactly as it governs the agent's own updates: turn it off and you still get told you are behind, but nothing is replaced until you run `savelocker plugin-update`.

### Two things the agent will not do

**It will not perform the first install.** Creating the plugin's directory needs root, which the agent does not have and should not want. That is why the one-paste step above stays manual.

**It will not half-install an update.** Decky hands a plugin's files to the desktop user but keeps the plugin *directory* — and `plugin.json` — owned by root. So the agent can replace files that exist but cannot create new top-level ones. Rather than discovering that partway through and leaving you with a plugin that is half one version and half another, it checks every file it would write **before** writing any of them, and refuses the whole update if it cannot satisfy one. If that happens you will see it in `savelocker doctor` and in this console's audit log, and the fix is to reinstall the plugin once from the URL above.

An installation made before the plugin shipped its hot-reload flag also cannot self-update, for the same reason: that flag lives in the one file the agent may not rewrite. One manual reinstall fixes it permanently.

## Checking on it

`savelocker doctor` on the Deck reports whether Decky is present, which plugin version is installed, and whether a newer one is waiting. It also reports, per game, whether anything has confirmed that game's launch options were set — so a game the plugin has not reached is visible rather than silently unsynced.

## Why it is not in the Decky store

Decky's store submission requires attesting that generative AI did not write a majority of the submitted code. This plugin was largely AI-written, so that attestation cannot honestly be made. Installing from the URL above is the supported route, and the agent's own update channel is why not being in the store costs you nothing after the first install.
