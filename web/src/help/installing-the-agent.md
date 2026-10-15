# Installing the agent

The agent is the piece that runs on each gaming machine: it watches your save folders, uploads
changes, and pulls the latest save before you play. The server (this console) is the hub; every PC,
laptop and Steam Deck runs an agent.

Downloads are on the [GitHub Releases page](https://github.com/Marwanello/SaveLocker/releases):

| Machine | Download |
|---|---|
| Windows | `SaveLocker-Agent-Setup-<version>.exe` |
| Linux / Steam Deck | `savelocker-<version>-linux-x64.tar.gz` |

## First: create a join link

Do this **before** touching the other machine. It works the same for Windows and Linux, and it means
**no API key is ever copied by hand**.

1. In this console: **Configuration → Enroll a machine**.
2. Optionally name the machine (e.g. `Steam Deck`). Naming it *binds* the link to that name, so it
   cannot be used to claim a different machine's identity.
3. Click **Create join link**. A short code and a link appear — for example
   `http://your-server:5080/join/K7QM-4H2X`.
4. Get that link to the new machine: open it there (it is a web page), paste it into the agent's
   Settings tab, or send it to yourself in a chat. On another device with a browser, the **QR code**
   opens the same page.

The link carries a **single-use code that expires in about 15 minutes**, not a permanent key. It is
shown **once** — the server only keeps a hash of it. Lost or expired? Create another; it is one click.
The link itself is a small page served by your server: it knows which installer the device needs and
hands it over already enrolled.

---

# Windows

## Install and enroll — one flow

The easiest route starts at the **pairing page**: open the join link in this machine's browser and
the page walks you through it. Its **Download & enroll** button starts the installer download *and*
copies the join link to the clipboard, so the installer has everything it needs.

1. On the pairing page, click **Download & enroll** (or run the installer you already downloaded).
2. **Windows will warn you that the publisher is unknown.** Click **More info → Run anyway**. The
   installer is not code-signed yet, so SmartScreen has no publisher to check against. This is
   expected, and worth knowing rather than being surprised by.
3. The installer needs **administrator rights** (it installs to `C:\Program Files\SaveLocker Agent`
   and registers the auto-start entry, so the uninstaller can cleanly remove both).
4. On the **Enroll this machine** page, leave **"Enroll this machine now"** selected and paste the
   join link (**Ctrl+V** — the pairing page copied it for you; otherwise copy it from the console).
   The page shows **which server and code** the link names — check the server address is one you
   trust before continuing. The machine is enrolled as the install finishes, so it is **online in
   this console before the installer window closes**.

The agent then starts in the **system tray**. Double-click the icon to open its window, and confirm
the machine appears under **Configuration → Machines** here, showing as **online**.

> **Enrolling later instead.** Choose **"Skip — I'll enroll later"** on that page to install without
> enrolling. When you have the link, enroll from the agent's **Settings** tab (paste it into
> *Join a server*), or from a terminal:
>
> ```
> "C:\Program Files\SaveLocker Agent\SaveLocker.Agent.exe" enroll --url http://your-server:5080/join/K7QM-4H2X
> ```
>
> Either way, if the link has **expired or was already used** the installer says the enrollment did
> not complete — just create a new link from the console.

> **Deploying to many machines?** Pass the link on the command line for an unattended install:
> `SaveLocker-Agent-Setup-<version>.exe /SILENT /ENROLL-URL="http://your-server:5080/join/K7QM-4H2X"`.

## Notes

- **Start with Windows** is a toggle in the agent's Settings. It uses a registry Run-key entry, which
  the uninstaller removes.
- **Auto-update works on Windows.** When the console hosts a newer installer, the agent offers it and
  can install it silently.
- Uninstall from **Add or remove programs**. It asks before deleting `%PROGRAMDATA%\SaveLocker`
  (your API key and tracked games) — answer **No** if you plan to reinstall.

---

# Linux and Steam Deck

The Linux agent is **headless by design** — meaning no tray icon and no pop-ups, since in Game Mode a
Deck has no desktop to put them on. **This console is where the agent reports problems**, because a
Deck cannot raise a toast to tell you itself.

It is *not* true that the agent has no UI of its own. The daemon serves the same web UI the Windows
tray shows at `http://localhost:5178` — reachable in Desktop Mode, and the easiest way to map a save
folder. See [Reaching the agent's own UI](#help/installing-the-agent) below.

The download is **self-contained**: it bundles its own .NET runtime, so there is nothing to install
first. (It needs glibc 2.27 or newer; every SteamOS 3 release is well past that.)

## Install

On a Steam Deck, switch to **Desktop Mode** first (**Steam button → Power → Switch to Desktop**).

The easiest way in is a join link from the console (see [Enroll it](#help/installing-the-agent)
below) — it installs and enrolls together. Without one, use the release's double-click installer:
download **`SaveLocker-Installer.desktop`** from the latest release on GitHub, double-click it in
the file manager (choose **Execute** when asked whether to trust it), and a terminal installs the
newest agent — checking the download against the release's checksum — then opens the agent on its
**Join a server** card, ready for the link. Running it again later updates the agent in place.

Or by hand, from the release's tarball:

```bash
tar -xzf savelocker-<version>-linux-x64.tar.gz
./SaveLocker/install.sh
```

This installs to **`~/.local/share/SaveLocker`**, links `savelocker` into `~/.local/bin`, and enables
a `systemd --user` service so the agent starts with your session.

> **Nothing is installed to `/usr`, and that is deliberate.** SteamOS's root filesystem is
> **immutable and wiped on every system update** — anything installed there would silently vanish.
> Everything lives in your home directory, which survives updates.

If `install.sh` warns that `~/.local/bin` is not on your `PATH`, add it (or use the full path
`~/.local/share/SaveLocker/savelocker`).

## Enroll it

Open the join link in this device's browser (on a Deck, in Desktop Mode) and pick **Linux & Steam
Deck**. **Download installer** saves `Install SaveLocker.desktop`; double-click it in the file manager
and a terminal opens that downloads the agent, installs it and enrolls it in one go. The first time,
the file manager asks whether to trust a downloaded launcher — choose **Execute** (KDE, the Deck's
desktop) or **Allow Launching** (GNOME). The launcher holds nothing but the link: the code inside is
spent by the install it runs, and a launcher opened after the link expired just says so.

The same page shows the **one-line installer** for a terminal or an SSH session — it does exactly what
the launcher does. Or do each half by hand:

```bash
tar -xzf savelocker-<version>-linux-x64.tar.gz && ./SaveLocker/install.sh
savelocker enroll --url http://your-server:5080/join/K7QM-4H2X
savelocker doctor
```

(That is the same command the one-liner runs. The code inside the link is single-use and expires in
~15 minutes; the agent fetches what it needs from your server itself.)

**Agent already installed?** The pairing page's **Open the agent and finish** opens the SaveLocker
window on the **Join a server** card with the link filled in — one click to enroll. On Linux it is a
`savelocker://` link, handled by the agent's application-menu entry, and it starts the agent's service
first if it is not running. An agent older than these links ignores it; use the page's *open it here
instead* link while that agent is running.

Enrollment over HTTPS also records the server's TLS public key. If that identity later changes, the
agent warns but does not stop syncing — a legitimate certificate renewal may rotate the key, and a
hard failure would strand a headless Deck. If you expected the certificate or reverse-proxy change,
inspect the current pin and accept the new one:

```bash
savelocker trust
savelocker trust --accept
```

If you did not expect the change, stop and verify the server URL, DNS, tunnel, and certificate before
accepting it. A pull restores files into your save folders, so an unexpected server identity matters.
Plain `http://` connections have no TLS identity to pin.

**`savelocker doctor` is the command to remember.** On a machine with no UI, it is how you find out
what is wrong: it checks the whole chain — server reachable, Steam found, shortcuts parsed, Proton
prefixes located, save folders present and writable — and prints a `✗` next to anything broken. If
you ever ask for help, paste its output.

## Sync a game: the launch wrapper

Tell Steam to run the game *through* the agent. In the game's **Properties → Launch Options**:

```
/home/deck/.local/bin/savelocker run -- %command%
```

Replace `deck` with your username if it differs. **Use the full path.** Game Mode does not put
`~/.local/bin` on `PATH`, so the short form `savelocker run -- %command%` silently prevents the
game from launching — you get a black screen and the game never opens.

> **You don't have to type this.** The agent UI shows a **Steam launch setup** card — on the
> **Overview** tab, and again right after you enrol a game — with this exact command (resolved to
> your real install path) and a **Copy** button. The command is identical for every game, so once
> one game is set up you can copy it straight from that game's launch options into the next, and the
> card is only really needed for the first game on the device.

That is the whole integration. The agent pulls the latest save before the game starts, waits for it
to exit, waits for the save to finish being written, and pushes it.

> **If you use Decky Loader, you can skip this step entirely.** SaveLocker's optional Decky plugin
> sets these launch options for you — merging with any `mangohud` line or arguments you already
> have — and adds lease warnings, sync controls and `doctor` to the Quick Access panel. See
> **The Decky plugin (Steam Deck)**. It is an accelerator, not a requirement: everything on this
> page works without it, and stays supported.

> ### ⚠️ The step everyone misses
> For a **non-Steam game**, you must also tick **"Force the use of a specific Steam Play
> compatibility tool"** (Properties → Compatibility) and pick a Proton version.
>
> Without it, Steam launches the game without Proton, **no Wine prefix is ever created**, and there is
> nothing for the agent to sync. `savelocker doctor` will tell you the prefix is missing.

## What SaveLocker syncs on Linux — read this before you file a bug

- ✅ **Non-Steam games added to Steam as shortcuts**, run through Proton. This is the case SaveLocker
  exists for.
- ⚠️ **Games you bought on Steam** — found, but hidden until you ask for them. They already have
  **Steam Cloud**, so they are kept out of the default list rather than burying the games you came
  to add. Pick the **Steam** filter (or **All**) in Add games to see them.
- ❌ **Native Linux builds** of a game. A Proton game *is* a Windows game — it writes Windows-format
  saves — so a Deck save and a Windows PC save are **the same save**, and can be synced between them
  freely. A *native Linux* build writes a different format, and syncing that into a Windows install
  would corrupt it. So we don't.

## Adding games from Game Mode — no Desktop Mode

On a Steam Deck you don't need Desktop Mode to add a game. The agent ships a **gamepad-native app**,
`savelocker ui`, that you launch from your Steam library like any game and drive entirely with the
D-pad.

**One-time setup (in Desktop Mode):**

1. Steam → **Add a Non-Steam Game** → **Browse** → `~/.local/share/SaveLocker/savelocker`
2. Open its **Properties → Launch Options** and set them to just: `ui`
3. Rename the shortcut **SaveLocker**.

That is the last Desktop Mode step you have to take. From then on, launch **SaveLocker** from your
library in Game Mode:

- **Add game** — scan for non-Steam games, tick one, and set its save folder from a browser that
  opens **inside that game's own Proton prefix**, so you are not hunting the Windows file tree. A side
  pane lists the files in each folder, so you can confirm the save is actually there before choosing
  it. Enrollment is blocked until a save folder is set.
- **Steam launch setup** — the `run -- %command%` launch command with a **Copy** button, ready to
  paste into a game's launch options. It is identical for every game.

The save folder you pick is reported to this console automatically — there is nothing to configure a
second time here.

## Reaching the agent's own UI

The daemon serves the same web UI the Windows tray shows, on port **5178**. In Desktop Mode, browse to
`http://localhost:5178`.

**"Headless" here means no tray icon and no pop-ups — not that there is no UI.** There is a full one,
and this is the easiest way to **map a save folder without typing a path**: under
**Settings → Currently Tracked Games**, a game with no folder shows **Set save path**, which opens a
folder browser. Drive it in Desktop Mode the usual way — right stick or trackpad moves the cursor,
left stick scrolls, click to open a folder. It starts on the path the scan guessed and is limited to
your home directory, Steam libraries and mounted SD cards. See [Adding games](#help/adding-games).

The UI listens on **localhost only, and that is deliberate**. It is a management interface — it can
re-point this machine at another server, re-register it, and change what it syncs — so putting it on
your network would let anything on that network do the same. There is no `--lan` flag any more.

To reach it from your PC or phone, forward the port over SSH, which authenticates you and encrypts
the traffic:

```sh
# from the other device
ssh -L 5178:localhost:5178 deck@<deck-ip>
# then browse to http://localhost:5178 on that device
```

On the Deck, SSH is off by default: `sudo systemctl enable --now sshd` (and set a password with
`passwd` first, since SteamOS ships without one).

## Notes

- **The Linux agent updates itself.** It downloads and verifies a newer version, then installs it the
  next time the agent starts — see [Agent auto-update](#help/agent-update), which also explains what
  does and does not count as "the next time the agent starts". Installing by hand still works:
  download the newer tarball and run `install.sh` again, which installs over the top and keeps your
  configuration.
- The agent stops when you log out unless *lingering* is enabled. On a Deck that is usually fine (you
  are logged in whenever you are playing). To keep it running regardless:
  `sudo loginctl enable-linger $USER`. Note that with lingering on, **switching to Desktop mode and
  back no longer applies a waiting update** — only a reboot does. Same table as above.
- Log file: `~/.local/share/SaveLocker/agent.log` (not `%PROGRAMDATA%` — Linux state lives under
  XDG paths). Tail it with `savelocker log`.

---

## Confirming it worked

On either platform, the machine should appear in this console under **Configuration → Machines**,
showing **online**, with its agent version and last sync time. If a machine is failing to sync,
it says so there — and a ⚠ badge appears in the top bar.

Next: **[Adding games & mapping save folders](#help/adding-games)**.
