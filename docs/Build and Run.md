# Build and Run

Full deployment reference also in `README.md`.

## Prereqs
- .NET 9 SDK. If `dotnet` isn't found in an open shell: `$env:Path = "$env:ProgramFiles\dotnet;" + $env:Path` (or open a new shell).
- Node.js 22 (for React dashboard dev server and agent UI builds).
- Inno Setup 6 (for installer builds): `winget install JRSoftware.InnoSetup`.

## Build

```sh
# Full solution
dotnet build SaveLocker.sln

# Force server recompile (use this when server edits seem ignored — see Gotchas.md)
dotnet build src/Server/SaveLocker.Server.csproj --no-incremental
```

**Stop the running agent/server before building** — they lock the DLLs.

## Run server (local dev)

```sh
cd src/Server
dotnet run
# API: http://localhost:5179   Health: /health   Swagger: /swagger
```

Dev state under `src/Server/localstate/` (db + archives). Deterministic test run:

```sh
$env:ASPNETCORE_URLS="http://localhost:5179"
$env:Storage__DbPath="...\some\path\db.sqlite"
$env:Storage__ArchiveRoot="...\some\path\archives"
dotnet run --no-build --no-launch-profile
```

## Run React dashboard (local dev)

```sh
cd web
npm run dev
# Dashboard: http://localhost:5173 (proxies /api + /art to :5179)
# LAN access: http://192.168.68.58:5173 (Vite bound to 0.0.0.0)
```

Server must be running for API calls to work. Two Vite processes will claim ports 5173 + 5174 — kill duplicates with `Get-Process node | Stop-Process`.

## Run agent (local dev)

```sh
dotnet build src/Agent/SaveLocker.Agent.csproj --no-incremental
# Then launch the built exe, or run via dotnet for console output:
dotnet src/Agent/bin/Debug/net10.0-windows/SaveLocker.Agent.dll
```

Agent UI served at http://localhost:5178.

## Deploy on unRAID (Docker / CI)

`git push` → GitHub Actions builds and pushes `ghcr.io/skorcherx/savelocker:latest`. To deploy on unRAID:

```sh
docker compose pull && docker compose up -d
```

Manual one-off build (no Compose):
```sh
docker build -t savelocker -f src/Server/Dockerfile .
docker run -d --name savelocker-server -p 5080:8080 \
  -v /mnt/user/appdata/savelocker:/data savelocker
```

Dashboard: `http://unraid-ip:5080`.

| Setting | Env var | Default (Docker) |
|---|---|---|
| SQLite path | `Storage__DbPath` | `/data/savelocker.db` |
| Archive root | `Storage__ArchiveRoot` | `/data/archives` |
| Art root | `Storage__ArtRoot` | `/data/art` |
| Backup root | `Storage__BackupRoot` | `/data/backups` |
| Installer root | `Storage__AgentInstallerRoot` | `/data/agent-installer` |
| Versions kept/game | `Storage__RetainVersionsPerGame` | `10` |

> **Existing Docker deployments** may have `/data/localgamesync.db` from before the rename. Either rename the file on the unRAID share or set `Storage__DbPath=/data/localgamesync.db`.

### Rolling a release out to the fleet

Console and agents are two halves and a release usually needs both — doing one and not the other
leaves Config reporting a version split.

- **Neither fleet is offered anything until the package is uploaded in Config → Agent updates. The
  GitHub Release asset is NOT automatically what agents are offered.** This is the step that gets
  missed, and since 2026-08-15 there are **two rows** to fill — Windows and Linux.
- **Windows agents** then prompt in the tray and install when the user accepts.
- **Decks** then update themselves: the daemon stages the tarball and installs it at its next start.
  A hand-run `install.sh` still works and supersedes anything staged. **The first** update onto a
  pre-0.5.5 Deck still has to be done by hand, because the agent that would fetch it is the one
  being replaced.
- **Console** redeploy is the `docker compose pull` above.

### Securing the server

Everything here is opt-in configuration (compose `environment:` entries, or `Security__Key=` env
vars) unless it says *default*. The reasoning is in [[Decisions]]; each item is asserted in
`tests/run-console-security-tests.ps1`.

| Setting | Default | What it does |
|---|---|---|
| `SAVELOCKER_UID` / `SAVELOCKER_GID` | image's `app` (1654) | The unprivileged user the container runs as. The entrypoint hands `/data` to it once on first start (a root-created install upgrades in place). unRAID: `99` / `100`. A compose `user:` also works if `/data` is already that user's. |
| `Security__RequireAdminPasswordToRegister` | `true` | Registering ANY machine (not only re-registering) needs the admin password, once one is set. A machine key can read and write **every** game, so only set this to `false` deliberately, on a network where everyone able to reach the server is trusted. With no admin password set the endpoint stays open (first-run). Enrollment tokens are unaffected. Agents registering by hand: `savelocker register --name X --admin-password …`. |
| `Security__TrustedProxies` | *(unset)* | Proxy / tunnel address(es) or CIDR ranges, comma-separated (e.g. `172.18.0.0/16`). **Only** then is a forwarded client address believed, and only from a listed proxy. Without it, behind a proxy, every client shares the proxy's address and one person's typos lock everyone out of signing in. |
| `Security__ClientIpHeader` | `X-Forwarded-For` | Which header carries the client address (Cloudflare: `CF-Connecting-IP`). |
| `Security__MaxFailedAttempts` / `FailureWindowSeconds` / `LockoutSeconds` | `5` / `900` / `900` | Wrong admin passwords a client may make in the window before being refused (429 + `Retry-After`) for the lockout period, doubling per repeat up to 4 h. |
| `Security__GlobalMaxFailures` / `GlobalLockoutSeconds` | `100` / `300` | The backstop that bounds a guess spread over many addresses. |
| `Security__SessionIdleDays` / `SessionMaxDays` | `7` / `30` | Console session lifetime: idle expiry slides, the cap does not. |
| `Art__AllowedImageHosts` | `steamgriddb.com` | Hosts (and their subdomains) an artwork image may be fetched from. |

Locked yourself out? Lockouts live in memory, so restarting the container clears them. The admin
password itself can be reset by clearing the `Admin:PasswordHash` row from `Settings` in the database
while the server is stopped.

**Migrations:** only **v0.5.0** ever broke rollback — two schema migrations run on first start, so
**back up `/data` first** when crossing that boundary from anything older. Every release since has
been a clean container downgrade.

## Build agent installer

```sh
.\installer\build-installer.ps1
# Output: installer/dist/SaveLocker-Agent-Setup-{version}.exe (~47 MB, self-contained)
```

Stop the running agent first (holds the `SaveLocker.Agent` AppMutex). Version is derived by MinVer from the nearest git tag — push a `v*` tag to trigger the GitHub release CI instead of building locally for a real release.

## Linux agent (Proton / Steam Deck)

Build and test **inside WSL, on the ext4 home** (`~/SaveLocker`) — never from `/mnt/*`, where DrvFs breaks inotify, permissions, case-sensitivity and locking. `dotnet` lives in `~/.dotnet` and is not on a login shell's PATH; export it (or run a `.sh` file rather than fighting inline quoting — see `Gotchas.md`).

```sh
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
dotnet build src/Agent.Linux/SaveLocker.Agent.Linux.csproj --no-incremental

# Fake-game harness: no Steam, no Proton, no GPU, no Deck required.
# Starts its own server on :5179 with a throwaway DB, and runs against a fake HOME.
bash tests/linux/run-linux-tests.sh          # 216 checks
```

To pull Windows-side commits into the WSL clone:
```sh
git fetch /mnt/e/Projects/SaveLocker main && git checkout FETCH_HEAD
```

Game Mode UI, in a real window under WSLg — the loop that saves a Deck trip:
```sh
bash tests/linux/run-ui-wslg.sh --fixtures                     # populated screens, interactive
bash tests/linux/run-ui-wslg.sh --fixtures --nav-debug \
     --nav right --pointer 530,537 --screenshot /tmp/x.png     # scripted, unattended
```
`--nav` replays D-pad presses, `--nav-debug` overlays the live nav cursor, and `--pointer X,Y` parks
a stationary pointer. The last one exists because WSLg leaves the real pointer outside the window
while a Deck always has one — without it, hover-vs-focus bugs cannot reproduce off-device, and an
A/B comes back identical while proving nothing.

To see the Deck UI against the **live test rig** rather than fixtures - a real Sync all round trip, the
console's pushed look - sync the tree into the WSL clone (`.\tests\testenv.ps1 sync`), build the UI variant
there (`dotnet build src/Agent.Linux/SaveLocker.Agent.Linux.csproj -r linux-x64 --no-self-contained
--no-incremental`, which writes `bin/Debug/net10.0/linux-x64/` and leaves the running daemon's own DLL alone),
then run the binary with the rig's own config and the rig daemon's local API port (`DaemonApiPort` in that
config; `--port` on `ui`):
`savelocker ui --size 1280x800 --config ~/savelocker-test/SaveLocker/config.json --port 5187 --screen settings --nav y --screenshot out.png`.
A dead `--port` shows the failure path. Drive it from a script file ([[Gotchas]] -> *WSL environment*).
Release build (self-contained — SteamOS ships no .NET runtime):
```sh
bash packaging/linux/build-linux.sh          # -> artifacts/linux/savelocker-linux-x64.tar.gz
```
Build on the **oldest glibc** you intend to support (Ubuntu 24.04 → Deck is forward-compatible; the reverse is not).

## Decky plugin

**Lives in its own repository:** <https://github.com/SkorcherX/SaveLocker-Decky>. It is not a
subdirectory here and must not become one again — Decky's plugin database tracks plugins as
submodules whose *root* is the plugin, so a monorepo subdirectory cannot be packaged or listed.

Nothing in this repo depends on it *working*. The rule it relies on lives in
`Agent.Core/LaunchOptions.cs` and is covered by `run-linux-tests`; the plugin only reads the agent's
API and writes what it is told. Its own repo carries the build, the release workflow and the
install/debug notes.

**This repo does, however, distribute it.** The server hosts its release zip in a third installer
slot (`?platform=decky-plugin`, from `SkorcherX/SaveLocker-Decky`), and the Linux agent replaces the
files under `~/homebrew/plugins/SaveLocker` from it — see `Agent.Linux/DeckyPlugin.cs` and the
constraints in [[Gotchas]] → *Decky plugin*. To exercise that here, a fake plugin directory in the
fixture HOME is all `run-linux-tests` needs; no Decky, no Deck. To publish a plugin release to a real
server, upload the zip in **Config → Agent updates → Decky plugin** (type the version — Decky's
artifact is always `SaveLocker.zip`) or let the GitHub fetch read the tag.

## Throwaway test rig (`testenv`)

`.\tests\testenv.ps1 <command>`, run from Windows, drives a whole disposable fleet — a Windows
agent, a headless WSL agent, a dashboard container and (optionally) a real Steam Deck — all running
**beside**, never instead of, the installed release agent and any real save data. Commands:
`build`, `up`, `down`, `status`, `test`, `sync`, `logs`, `clean`, and `help` (`help <command>` for one command's whole entry). `-Only windows|linux|deck|console`
scopes `build`/`up` to one target; `down`/`status`/`logs`/`clean` always touch everything that's
configured. Isolation is `SAVELOCKER_STATE_ROOT`/`SAVELOCKER_TRAY_PORT`/`SAVELOCKER_RUNKEY_SUBPATH`
on Windows and `XDG_DATA_HOME` on Linux ([[Gotchas]] → *Test-only environment variables*) — never
the flags a real user session reads.

`clean` is the one command `down` is not: `down` only **stops** processes, and can leave a stale
container, its image and its data volume, and every test agent's on-disk state, behind. `clean`
stops everything first and then **deletes** it — Windows' whole `-StateRoot`, the WSL rig's state
dir, the Deck's deployed build and state, and the dashboard's container/image/volume. Rebuilding
after a `clean` is a plain `build` + `up` — nothing about the rig is meant to survive it, which is
the point of calling it something other than `down`.

**Steam Deck target.** Unlike Windows/WSL there is no "this machine" default. Either copy
`tests/testenv.local.ps1.example` to `tests/testenv.local.ps1` (gitignored — it names your LAN, not
the project's) and fill in the two lines by hand, or write it with the rig itself:

```powershell
.\tests\testenv.ps1 deck-config -DeckHost deck@192.168.68.67 -DeckServerUrl http://192.168.68.58:5080
.\tests\testenv.ps1 deck-config                                  # prints what's currently saved
.\tests\testenv.ps1 deck-config -DeckHost deck@192.168.68.99      # updates just the host, keeps the URL
```

`testenv.ps1` loads `testenv.local.ps1` automatically on every run, so a DHCP lease moving the
Deck's IP means one `deck-config` call (or one edited line), not a shell profile or a re-typed env
var. An explicit `-DeckHost`/`-DeckServerUrl` on the command line always wins over the saved file.

**Authentication is whatever your `ssh`/`scp` already do — the script never sees or stores a
password.** Every Deck call shells out to the Windows OpenSSH client exactly as if you'd typed it:
with a key in `~/.ssh/authorized_keys` on the Deck it connects silently; without one, `ssh`/`scp`
fall back to an interactive password prompt right there in the console, which works but asks again
on every single call (`up` alone makes three). Key auth is worth setting up once:

```powershell
ssh-keygen -t ed25519                                    # skip if you already have a key
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub | ssh deck@<deck-ip> "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
ssh deck@<deck-ip> echo ok                                # should now connect with no prompt
```

The last command asks for the Deck's password once, to write `authorized_keys`; nothing after that
does. SSH must already be enabled on the Deck (Desktop Mode → Konsole → `passwd` to set one if
you haven't, then `sudo systemctl enable --now sshd`).

**`build` never fetches or checks out anything by itself** — it builds whatever commit the WSL clone
already has checked out. Switching branches (or just wanting the latest commit) needs an explicit
`.\tests\testenv.ps1 sync` first, run from the branch you want on the Windows side; skipping it
silently ships a stale build with no error, since there's nothing to compare against and complain
about. Confirmed 2026-08-31: a `doctor` run against a `build -Only deck` that skipped `sync` was
missing an entire feature (the Phase 5 conflict-resolution "Session" block) that was very much
present in the source being tested.

`build -Only deck` cross-compiles a **self-contained** linux-x64 publish in WSL (the same packer
`packaging/linux/build-linux.sh` uses for a real release, just stamped with the test version) —
SteamOS ships no .NET runtime, so the WSL target's ordinary framework-dependent `dotnet build`
cannot run there. `up -Only deck` (or plain `up`/`build` once `SAVELOCKER_DECK_HOST` is set) `scp`s
the tarball to the Deck and installs it under `~/savelocker-test` with state in the sibling
`~/savelocker-test-state` — never `~/.local/share/SaveLocker`, which is both the real install's
binaries *and* its state ([[Gotchas]] → *Linux agent*). Leaving `SAVELOCKER_DECK_HOST` unset skips
the Deck silently wherever `-Only` defaults to `all`; passing `-Only deck` explicitly without it is
an error instead of a silent no-op. Because the Deck is only awake when woken (see the Deck
handoff section in [[CONTEXT]]), every SSH/`scp` call has a short connect timeout and reports
"unreachable" rather than hanging or aborting the rest of the rig.

### Testing a fresh install (`flow`)

The rest of the rig runs a pre-built agent beside the real one, so it never exercises what comes
**before** an agent exists: the join link's `.desktop` launcher, the release's
`SaveLocker-Installer.desktop`, `install.sh`, the menu entry's `savelocker://` handler and enrolling.
All of those write to `$HOME`, so `flow` runs each in a **throwaway HOME** (`~/savelocker-flow/…`), with
install.sh's test-only `SAVELOCKER_INSTALL_SANDBOX` keeping it off the one thing `$HOME` does not
separate — the user's systemd manager ([[Gotchas]] → *Test-only environment variables*). The real
`~/.local/share/SaveLocker`, `savelocker.service`, port 5178, menu entry and Steam are never touched.

```powershell
.\tests\testenv.ps1 sync; .\tests\testenv.ps1 build -Only console; .\tests\testenv.ps1 up -Only console
.\tests\testenv.ps1 flow -Only linux        # WSL, headless: both launchers run and are checked
.\tests\testenv.ps1 flow -Only deck         # stages two launchers on the Deck's desktop
.\tests\testenv.ps1 flow -Only deck -Headless
.\tests\testenv.ps1 flow -Only windows      # the real installer, in Windows Sandbox
.\tests\testenv.ps1 clean                   # also removes every flow sandbox
```

`flow` hosts the Deck build (built first if missing — `sync` first so it is this branch's) as the
console's Linux package, mints join links, and mirrors a GitHub release locally
(`SAVELOCKER_RELEASE_BASE`) for the GitHub installer. It refuses a build whose install.sh predates the
sandbox. **WSL** runs both launchers through GLib's own `.desktop` parser and checks: installed,
enrolled, daemon on the sandbox port, the `savelocker://` default written, the GitHub install ending at
the Join step; then `open savelocker://enroll?link=…` and the Join card's API call enroll that second
sandbox. **Deck**: in Desktop Mode, double-click `SaveLocker TEST - join link` and
`SaveLocker TEST - GitHub installer` on the desktop (KDE's trust prompt included), then run
`~/savelocker-flow/open-link.sh` for the handler; the pairing page's own *Download installer* must not
be used there — that one installs for real. **Windows**: the Inno installer writes Program Files, the
Run key and the installed agent's mutex, so it only runs in **Windows Sandbox** —
`flow -Only windows` builds it stamped `-Version`, hosts it, and writes
`artifacts\testenv-flow\SaveLocker-flow.wsb` (in the repo, not AppData: a terminal inside the Claude
desktop app has its AppData writes hidden from WSL and the sandbox), which opens a join link inside the sandbox (needs the
*Windows Sandbox* feature: `Enable-WindowsOptionalFeature -Online -FeatureName
Containers-DisposableClientVM -All` from an elevated shell, then a reboot).

Not covered by any sandbox: a real browser handing a `savelocker://` link to the desktop (the handler
is registered in the sandbox's own `mimeapps.list`, which the real session's browser does not read).

### Testing emulator saves (a fake EmuDeck folder)

```powershell
.\tests\testenv.ps1 sync                 # the Linux agent builds from the WSL clone (~/SaveLocker): bring it to this branch first
.\tests\testenv.ps1 build -Only console; .\tests\testenv.ps1 build -Only windows; .\tests\testenv.ps1 build -Only linux
.\tests\testenv.ps1 emu-fixture          # <StateRoot>\Emulation (Windows) + <StateRoot>\Emulation-wsl (WSL)
.\tests\testenv.ps1 up -Only console; .\tests\testenv.ps1 up -Only windows; .\tests\testenv.ps1 up -Only linux
```
The Windows test tray then scans **only** `Emulation` and the WSL daemon **only** `Emulation-wsl`
(`SAVELOCKER_EMUDECK_PATH`), never a real EmuDeck or RetroArch on the box. Emulators kept outside `Emulation`
(Supermodel, ScummVM) are read from a fixture home instead of the real one — `<StateRoot>\emu-home` for the tray
(EmuDeck for Windows' `AppData\Roaming\EmuDeck\Emulators\Supermodel`, `AppData\Roaming\ScummVM\scummvm.ini`) and
`emu-home-wsl` for the daemon (`~/.supermodel`, `~/.config/scummvm/scummvm.ini`) — through `SAVELOCKER_EMULATOR_HOME`.
One already set in the shell (a copy of real emulator folders) wins over the fixture home, and the rig leaves it set.
Group B adds, on both machines: melonDS *Pokemon - Platinum Version* (Windows has a state slot, WSL an older save),
Model 2 *Daytona USA* (Windows only; a state in slot 0, and *daytonam1.sta* — Daytona To The MAXX's slot 1 — must survive every Daytona pull), Supermodel *Scud Race* (Windows has a state; *lemans24* beside it must survive
every Scud pull) and ScummVM *Monkey Island 2* (*monkey.s00* beside it must survive). WSL's ScummVM config names
Monkey Island 2 differently, so the two machines joining one game is D1 at work. Windows has Chrono Trigger's save,
a state slot and its thumbnail; WSL has an older Chrono save and no states; both have their own Zelda save
and state, which no Chrono sync may touch. Add games lists `Chrono Trigger` etc. with *Emulator* and *RetroArch* chips;
the *Emulators* source chip narrows to them and opens an *Emulator* row (All / RetroArch). After adding one,
its game page shows *Emulator › RetroArch* with *SNES* and *EmuDeck* tags under the name, and the console's
game page shows one chip, or "2 sources" when the machines found it differently.
Group C adds, under `Emulation\saves` on both machines:
- **PCSX2:** a folder card `Mcd001.ps2` with *Kingdom Hearts II* (a game's folder is the card itself, scope `BASLUS-21005*/**`) (Windows has a state, plus a `.p2s.backup`
  that must never travel). Its neighbour must survive every Kingdom Hearts pull: *Grand Theft Auto III* on
  Windows, *Ookami* on WSL. So must each card's own `_pcsx2_superblock`. Windows also has *Warrior Within* and
  *The Two Thrones*, whose saves both say only "Prince of Persia": the rows must read *Prince of Persia - Warrior
  Within* and *Prince of Persia - The Two Thrones*, each with a *USA* chip — no region in the name. Once added, the
  game page's *USA* chip opens the details (game ID *SLUS-21022*, *PlayStation 2*, its languages). Windows also has a file card `Mcd002.ps2`, listed greyed out with the fix.
  **An unformatted card (pull safety):** WSL also has a new card `Mcd000.ps2` whose `_pcsx2_superblock` is 0 bytes, and a
  *Warrior Within* ROM. Add *Prince of Persia - Warrior Within* on Windows; within a minute or two WSL sets it up on
  `Mcd000.ps2` (*Set up … its ROM is on this machine*) and pulls it (press Sync on its game page if not). Then
  `Mcd000.ps2\BASLUS-21022WW\save` holds the Windows save and `_pcsx2_superblock` is 8192 bytes starting `Sony PS2 Memory
  Card Format`, and the daemon log says *formatted PCSX2 card "Mcd000.ps2"*.
- **DuckStation:** *Crash Bandicoot*, with a state on Windows. *Spyro* and *Tekken 3* stay put. A
  `shared_card_1.mcd` on Windows is greyed out.
- **Dolphin:** GameCube *Metroid Prime* and *Melee* (Windows), and Wii *Metroid: Other M* on both. Windows keeps it
  under `states`, WSL under `StateSaves`.
- **Metroid Prime Trilogy in Dolphin AND PrimeHack** on Windows: two rows, two server games. WSL's PrimeHack copy
  joins only the PrimeHack one.
- **Metroid Prime - Hunters in melonDS AND RetroArch** on Windows (WSL has melonDS's only): two server games that the
  console and both agents list as **one entry with a tab per emulator** (tasks/multi-emulator-games), as is Trilogy.
  On WSL the RetroArch tab reads *Not set up here*. **Copy to another emulator…** turns melonDS's `.sav` into
  RetroArch's `.srm` with the same bytes.

Group D adds, on both machines (each `PARAM.SFO` written byte-accurate by `New-ParamSfo`):
- **PPSSPP:** *Grand Theft Auto - Liberty City Stories* (two save folders, `ULUS10041*`; a state and its `.jpg` on
  Windows, an older save on WSL). Its neighbours must survive every pull: *Homebrew Hero* (Windows, a PSN ID named by its
  own SFO) and *SSX on Tour* (WSL). *God of War - Chains of Olympus* (Windows, `UCUS98653`) has a state and no save
  yet: it is listed with "Save states only so far", and adding it syncs the state.
- **RPCS3:** *Heavenly Sword* (`BCUS98132-GAMEAUTOSAVE`, the Deck's real save's layout; the save says "Heavenly Sword™",
  Redump's name wins). *Demon's Souls* beside it on Windows stays put.
- **Vita3K:** *Persona 4 Golden* (named by the save's own `sce_sys/param.sfo`) on both; *Tearaway* on Windows, named
  only by the installed game under `storage\Vita3K\ux0\app`.
- **shadPS4:** *Bloodborne* — the old layout (`saves\shadps4\saves\1\CUSA00900`) on Windows, the new one
  (`~/.local/share/shadPS4/home/1000/savedata`, in `emu-home-wsl`) on WSL. Both must join one game.

Group E adds (title-ID folders; the 3DS ROM headers written by `New-Ncsd`/`New-Ncch`):
- **Cemu:** *Mario Kart 8* (`saves\Cemu\saves\00050000\10145d00`, named by a `title_list_cache.xml` in the Windows
  fixture home); WSL has an older save and no cache, so it calls it *Wii U 10145D00* and must still join. *The Legend
  of Zelda Breath of the Wild* (Windows, in no cache: named by its game folder's `meta/meta.xml` in `roms\wiiu`) must
  survive every Mario Kart pull; *Wii U 10176900* (Splatoon) is named by nothing; the `00050010` system app is never listed.
- **Azahar:** *Super Mario 3D Land* (USA, from its ROM's product code `CTR-P-AREE` in `roms\n3ds`; a state
  `0004000000054000.01.cst` on Windows, an older save on WSL). *Metroid - Samus Returns* (Windows) is named from its
  installed `content\00000000.app`, which never syncs; WSL's *3DS 0016E300* (no ROM) must survive every Mario pull.
- **Xenia:** *Forza Horizon* (`4D5309C9`) under Windows' profile `E030000033894516`, with a DLC beside it that never
  syncs, and on WSL's old layout (`saves\xenia\saves\4D5309C9`); both must join one game. *Forza Horizon 2* (Windows)
  and *Plants vs. Zombies* (WSL, an Arcade title) stay put.
  *Rock Band 3* (`45410914`, Windows) has only a device save in `saves\xenia\saves\0000000000000000`: it is listed,
  its game page shows a second folder `device`, and its DLC beside it never syncs. WSL's *Halo 3* is in the 2023
  named-profile layout (`saves\xenia\saves\User\4D5307E6`) and is listed like any other.

Phase 5c adds **the Xenia profile checked at launch** (Windows):
- *Forza Horizon*'s saves are under `E030000033894516`, whose `Account` says **Driver**.
- `saves\xenia\xenia-canary.config.toml`, and Xenia Manager's own `saves\xenia\config\Forza Horizon.config.toml`, sign
  in as `E03000004A1C77B2` (**Player1**, with its own `Account`).
- WSL's *Forza Horizon* is in the layout from before profiles, so it is never asked.

Ask the gate the way Decky and Playnite do, against the Windows test agent (after adding *Forza Horizon* there). Run from the Claude desktop app, put the rig outside `%LOCALAPPDATA%` first: add
`$env:SAVELOCKER_TEST_ROOT = 'D:\SaveLocker-testenv'` to `tests\testenv.local.ps1` — Windows redirects a packaged app's
writes there, and WSL then never sees the fixture. The token is then in `D:\SaveLocker-testenv\SaveLocker\api-token`.
:
```powershell
$t = (Get-Content "$env:LOCALAPPDATA\SaveLocker-test\SaveLocker\api-token" -Raw).Trim()   # or <SAVELOCKER_TEST_ROOT>\SaveLocker\api-token
$h = @{ 'X-SaveLocker-Token' = $t }
$id = ((irm http://localhost:5188/api/games -Headers $h) | ? name -eq 'Forza Horizon').id
$r = irm -Method Post http://localhost:5188/api/games/$id/pre-launch-sync -Headers $h -ContentType application/json -Body '{}'
$r | ConvertTo-Json -Depth 5
```
1. `decision` is `Ask`. Its question is:
   - the title "Xenia would start as Player1";
   - the choices "Start as Driver" and "Start as Player1";
   - the default `profile`.

   Both config files now say `E030000033894516`.
2. Answer "keep":
   ```powershell
   irm -Method Post http://localhost:5188/api/games/$id/launch-answer -Headers $h -ContentType application/json -Body (@{ questionId = $r.question.id; choice = 'keep' } | ConvertTo-Json)
   ```
   Both configs go back to `E03000004A1C77B2`. The same answer sent again gets a 404.
3. Ask again: it asks again (an answer only carries over to the Linux wrapper's check in the same launch). Answer `profile` with `remember = $true`. Both configs stay on Driver, and the next
   `pre-launch-sync` answers `Proceed` with no question. The game's `xeniaProfileAtLaunch` in the test agent's
   `config.json` is `Switch`.
4. On Windows, open *Forza Horizon* → **Emulator settings** → **Xbox 360 profile**: it shows the saves under Driver and what Xenia starts as.
   Set *At launch, for this game* to **Leave Xenia as it is**, and set either Xenia config back to Player1.
   `pre-launch-sync` now answers `Proceed` and leaves the configs alone. Set it back to *This machine's setting* after.
5. On WSL, `pre-launch-sync` for *Forza Horizon* never asks: there, the saves are not under a profile.

Phase 5c adds **the DLC check and hand-over** (Forza Horizon, title `4D5309C9`):
- Windows has two DLC packs and a title update in `saves\xenia\saves\0000000000000000\4D5309C9`:
  - *Pre-Order Car Pack*, 0.3 MB;
  - *Rally Expansion Pack*, 6 MB;
  - *Title Update 2*.
- WSL has only *Pre-Order Car Pack*, in the old layout's `saves\xenia\saves\4D5309C9`.

UIs: the Windows test tray at http://localhost:5188, the WSL daemon at http://localhost:5187, the console at
http://localhost:5080. Add *Forza Horizon* on both machines first (Windows first, so its save is the latest).

1. **What a save was made with:**
   - On Windows, open *Forza Horizon* and **Push**.
   - The console's game page has an **Emulator settings** button with a count of 1. Its **DLC and title updates**
     section shows Windows *All 3*, and WSL *Needs 2 · 6 MB* with **Send from WinTest**.
   - On WSL, the game page's **Emulator settings** button shows 1; its DLC section lists the two packs as *Missing*.
   - The header shows the chip **Xbox 360 profile E030000033894516**.
2. **A held pull:**
   - On WSL, open *Forza Horizon* and **Pull latest**.
   - The pull is held: the WSL save file still reads *old forza horizon profile on the WSL machine*.
   - The page switches to **Emulator settings → DLC and title updates**, shows *Pull on hold*, and the dialog opens,
     with three choices: **Hand them over from \<Windows\>**, **Pull the save anyway**, **Not now**. Back on the game
     page, a *Pull on hold* banner's **Choose** opens it again.
   - WSL's Overview shows a banner for it. The activity feed has a *held the pull* line.
3. **Ask for a hand-over:**
   - Click **Hand them over from \<Windows\>**. WSL's page now shows *Asked … waiting for it to agree*.
   - The console's **Emulator settings → Hand-overs** (and **Configuration → DLC hand-overs**) lists it as *asked,
     not answered* until Windows' next poll.
4. **Sent without asking** (the default, *Send it automatically*):
   - Within about 20 s Windows answers by itself: no notice, no **Send** button. The console card shows *sending*, then *on the server*. The bytes held go up to about 6 MB.
   - Within the next poll, WSL downloads and installs it. Check the folders under
     `<StateRoot>\Emulation-wsl\saves\xenia\saves\4D5309C9\00000002` and `...\000B0000`: they now hold
     `BBBB…0B` and `tu00000002_00000000`, each with its `Headers\…\*.header`.
   - The console shows the hand-over as *delivered*, and **Held right now** is back to 0 B.
   - The `_handover` folder under the console's archive root is empty.
5. **The held pull goes ahead by itself:**
   - Within a minute of the install, WSL pulls. Its save file now reads the Windows save.
   - The hold, its banner and the Overview banner are gone, and the console's DLC check shows WSL *All 3*.
6. **Decline, and never send:**
   - On WSL, delete `BBBB…0B` and its header. Then push on Windows again, and pull on WSL: it is held again.
   - On Windows, open *Forza Horizon* → **Emulator settings** → **Sharing DLC** and set *Ask me first*. **Hand them
     over** again: within 20 s the Windows tray raises *\<WSL\> asks for Forza Horizon's DLC*, Windows' Overview
     shows a banner, its *Forza Horizon* page shows an **Answer** banner, and **Sharing DLC** shows **Send** /
     **Decline**.
   - Click **Decline**. WSL's page shows *declined*, with **Ask again**, and a notice says so.
   - Set *Never send it*. Ask again: it is declined without a prompt.
   - Set *Send it automatically* again. Ask again: it is sent without a prompt.
7. **Pull anyway:**
   - With a pull held again, choose **Pull the save anyway**. The save is restored without the pack, and the hold
     goes.
8. **Installed by hand:**
   - Hold it again. Then copy `BBBB…0B` and its header back into WSL's `4D5309C9` folder by hand.
   - Within a minute the held pull goes ahead without any hand-over.
9. **Cancel from the console:**
   - Ask again, then click **Cancel** in the console's **Emulator settings → Hand-overs**. Both agents show it as
     cancelled, and nothing is left under `_handover`.
   - With the pull held again, click **Send from WinTest** in the console's **DLC and title updates** instead: Windows
     sends, WSL installs, and the held pull goes ahead, as if WSL had asked.
10. **Profile at launch on the console:**
    - Run the `pre-launch-sync` steps above on Windows.
    - The console's *Forza Horizon* → **Emulator settings** → **Xbox 360 profile** shows the profile at launch. Windows' row reads *Xenia was set
      to Player1 (E03000004A1C77B2)*, *Switched, and asked*. After a *keep* answer it reads *Asked — kept the other
      profile*, in amber.
    - **Hand-overs** lists every request from steps 3–9.
11. **CLI** (Windows test agent): `SaveLocker.exe dlc status` lists Forza Horizon's packages and any hand-over id.
    `dlc ask "Forza Horizon"` and `dlc send <id>` / `dlc decline <id>` do what the buttons do.

Group F adds **Switch** (Phase 6):
- Windows: **Eden** (EmuDeck links, user *WinEden*) with *The Legend of Zelda: Tears of the Kingdom*
  (`0100F2C0115B6000`, named by the Ryujinx `metadata.json` in `storage\ryujinx\games`), and **Ryujinx** on the same
  PC with its own older copy in `saves\ryujinx\saves\0000000000000001\0` (and `\1`). A ROM
  `roms\switch\Metroid Dread [010093801237C000][v0].nsp` has no save anywhere.
- WSL: **Ryujinx only**, as on the Deck: an older TotK in `saves\ryujinx\saves\0000000000000005\0`, and *Undertale*
  (`0000000000000002`), which must survive every Zelda pull.
- Both: a homebrew title `01005A7E57E57000` the bundled eShop table doesn't know. Windows names it from its ROM
  (*SaveLocker Homebrew Test*, Eden save); WSL has only Ryujinx's save (`0000000000000003`) and no name for it.
- Names come from Ryujinx's `metadata.json`, then a ROM's file name, then the bundled eShop table
  (`src/Shared/SwitchTitles`), and only then `Switch <title ID>`.

1. **Add games, Windows:** the *Emulators* row has chips *Eden … Ryujinx*; the *Console* row a *Switch* chip. TotK is
   **one** row, *Eden*, `…\saves\eden\saves\0000000000000000\C4D7AB8B14D1F6F277CD40D6A0F00ECA\0100F2C0115B6000`
   (Eden's copy is the newer). *Metroid Dread* is a row with *No save here yet: … where Eden will find it*. Each Switch
   row has the *New game … on the server* line with **Change**; Change opens the choices (only the automatic one for a
   new title). Add TotK. On the console its icon is the eShop's (the server fetches it from `img-eshop.cdn.nintendo.net`,
   no SteamGridDB key needed).
2. **Add games, WSL:** TotK is a Ryujinx row named *The Legend of Zelda: Tears of the Kingdom* (from the eShop table:
   WSL has no metadata or ROM), and its line reads *Joins “The Legend of Zelda: Tears of the Kingdom”*; **Change** says
   every save of the game joins it. Add it: it joins Windows' game (*2 sources*: Eden and Ryujinx).
3. **Between machines:** Push on Windows, Pull latest on WSL. WSL's `…\0000000000000005\0\progress.sav` **and**
   `…\1\progress.sav` now read *totk on eden …*; Windows' `option.sav` arrived too; Undertale is untouched.
   Change WSL's `0\progress.sav`, push, pull on Windows: Eden's file has the change.
4. **Emulator settings (Windows):** TotK's page → **Emulator settings** shows *Switch emulators on this device*: *Eden —
   Syncs with the server*, *Ryujinx — Different save, older*, each with its user and folder.
   - **Copy the synced save here** on Ryujinx: the row turns *Same save*; Ryujinx's `0` and `1` hold Eden's files; a
     zip appears under the test agent's `emulator-copies\<game id>`.
   - Turn **Keep in sync** on. Edit Ryujinx's `0\progress.sav`: within ~20 s (the poller) Eden's file has the same
     text, and the tray pushes it. Edit Eden's file: within ~20 s Ryujinx's `0` and `1` follow.
   - Edit **both** files, then wait: neither changes, the row says **Both changed** with two buttons; pick one and both
     copies hold that one.
   - **Make this the synced copy** on Ryujinx: the page now says *syncs from Ryujinx*, Eden is the copy (still kept in
     sync), the console's game page shows Windows' folder as Ryujinx's.
   - Start any process named `Ryujinx`/`eden` (e.g. copy `notepad.exe` to `eden.exe` and run it): every copy button
     answers *Eden is running. Close it first.*, and keep-in-sync does nothing until it exits.
5. **No save yet:** add *Metroid Dread* on Windows. Its folder (shown on the row) doesn't exist yet; write any file into
   it and push: the save reaches the server. WSL lists no Dread row: Ryujinx has no save slot for a game it never ran.
6. **Enrolled from WSL first** (a fresh server, or before step 1 adds anything): on WSL, Add games lists
   *Switch 01005A7E57E57000*. Add it: the console shows a game of that name. On Windows, *SaveLocker Homebrew Test*'s
   line reads *Joins “SaveLocker Homebrew Test”* (Change: *It takes the name …*). Add it: the console's game is renamed
   *SaveLocker Homebrew Test* (Audit log: `game.rename`), and within ~20 s WSL's Games page shows the new name too.
7. **Picked by hand:** on the console, **Add game** *Metroid Dread* (no folder needed). On Windows' Add games, Dread's
   automatic line is *New game “Metroid Dread (Switch)”*; **Change** lists the console's *Metroid Dread* as *Takes this
   title*. Pick it and add: the console's *Metroid Dread* now has Windows' Eden folder and the title key, not a second game.

The Emulators chip then opens an Emulator row with twenty-two chips, and a Console row (PS1, PS2, PS3, PS4, PS Vita, PSP, GameCube, Wii, Wii U, 3DS, Xbox 360, Switch, …). `-EmuDeckPath <dir>` points it at any other folder — a **copy** of a real
`Emulation` folder is the safe way to try real saves. The Deck needs no override: its test daemon scans
the Deck's real EmuDeck install, so add only a game whose save you have backed up.

### Testing artwork (a stub SteamGridDB)

The test console has no SteamGridDB key, and a real one would put real traffic on a third party — so
artwork is exercised against `tests/sgdb-stub.py`, a stdlib-only stand-in that accepts any key and serves
generated covers and icons for every game name (12 covers over two API pages of 7 and 5; 8 icons, the
first transparent). The console *container* reaches it as `host.docker.internal`, which is what
`-ConsoleEnv` (KEY=VALUE, applied each time `up` starts the container) is for:

```sh
python tests/sgdb-stub.py --port 5217        # keep this running
.\tests\testenv.ps1 up -Only console -ConsoleEnv `
    'Art__ApiBaseUrl=http://host.docker.internal:5217/api/v2/','Art__AllowedImageHosts=host.docker.internal','Art__AllowInsecureImageUrls=true'
```

Then add games with no key set, and paste **any** key under Configuration → SteamGridDB: they fill in in
the background. Not 5216 — the security suite owns that port and refuses to start if it is taken.
The container also logs a few `Network is unreachable (host.docker.internal:5217)` lines: Docker Desktop
gives that name an IPv6 address the container cannot route, so a burst of parallel fetches occasionally
tries it first. A preview tile that says "no preview" once is that, not a bug; paging away and back
refetches it.

The stub returns **three** search candidates per query (the derived id first, then two alternatives with
their own art), so the picker's source row can be exercised by hand: open a game's picker, *Change*,
search, pick the second candidate — both strips reload from that entry's covers/icons, *Use automatic
match* returns to the first. Each stub game lists `Stub Game <id>` as its name via `games/id/{id}`.

## Tests

```sh
.\tests\run-agent-tests.ps1   # Windows agent; server must be on :5179
bash tests/linux/run-linux-tests.sh   # Linux agent; starts its own server (run in WSL)
```

Scratch state written to `.verify/` (Windows) and `.verify-linux/` (Linux), both git-ignored.

**Before running any suite, read [[Gotchas]] → Testing and Test harness.** Two traps recur: clear
`.verify/` and `src/Server/localstate/` *together*, and start the dev server with
`ASPNETCORE_ENVIRONMENT=Development` *and* explicit `Storage__*`, or it opens `/data/savelocker.db`
(i.e. `E:\data\`) and hangs on a stale migrations lock.

`run-console-security-tests.ps1` owns :5215 (server) and :5216 (a stub SteamGridDB it hosts), needs only
the **server** built in Debug (no agent), and reads/edits the SQLite file through WSL python3 for its
database-level checks (they print `SKIP` without it). It covers the console's bulk-command and
exclude-pattern endpoints, admin sessions, the PBKDF2 v1→v2 upgrade, the sign-in throttle (per-client,
global, trusted-proxy), registration gating, response headers, and the artwork-fetch hardening.
Confirmed to fail with the throttle, URL allowlist and image sniffing switched off (14 FAILs).

### Suite baseline

Quote these as a pair with the date — a bare number means nothing on its own.

| Where | Counts |
|---|---|
| Windows, local | **console/security 172** (105 when it was added 2026-09-20; +32 for the art picker/backfill/thumbnails; +12 for the PR #45 review fixes — oversized, corrupt and empty images, the page bound, the startup backfill; +23 for `UI-01` appearance, 2026-09-21 — `run-console-security-tests.ps1`) · **appearance consistency 20** (new 2026-09-21, `run-appearance-consistency-tests.ps1` — source only, no server, about a second) · win agent bug bounty **114** (reads **113/114** since 2026-08-14 — see [[Backlog]]) · server bug bounty **164** · agent 47 · delta upload **17** (new 2026-08-22, `run-delta-upload-tests.ps1`) · hardening 33 · local-api **85** (66 + §12 appearance, 2026-09-21; needs `agent-ui` built into the Linux bin and :5188 free — [[Gotchas]] → *Testing*) · concurrency 23 · health 19 · enrollment 18 · enrollment-TLS 6 |
| Linux, local (WSL ext4) | `run-linux-tests` **63** on `main`, **69** at `4c9f5f5`, **84** after Phase 2, **117** after Phase 3, **123** after Phase 4 of the auto-update work, **137** after Phase 1, **154** after Phase 2, **161** after the Deck hardware pass, **197** after Phase 5 and **208** once the agent UI read the plugin's state live, both of `logs/2026-08-15_decky-plugin.md`, then **216** after `logs/2026-08-15_install-update-now.md` (2026-08-15, same clone) |
| Linux, in CI | agent 43 · hardening 37 · local-api 66 · concurrency 23 · health 19 · enrollment 16 |
| Windows, in CI | console/security **172** (`console-security-tests` job on `windows-latest`; its SQLite checks use the runner's Python, not WSL) |
| Detection | sweep **271/298 (90.9%)** at the default 300 sample, 17 pinned |
| Notifications (Group 7, 2026-09-24) | `dotnet test tests/SaveLocker.Agent.Tests` **84** (66 of them the notification rules: once-per-condition, withdraw, the five minutes, the route→URL contract) · `run-appearance-consistency-tests` **33** (28 + the installer-AUMID and route ties) · `run-health-tests` **22** · `run-linux-tests` **192 pass / 2 fail under WSLg** (the two are its injected session — `Gotchas` → Testing; every notification check passes) |
| Console Games page (Group 8, 2026-09-28) | `run-console-security-tests` **189** (+17: `API-03`, `POST /commands/cancel`) · `run-appearance-consistency-tests` **35** (+2: no `alert`/`confirm`/`prompt` under `web/src/components/game`, and a guard that the scan found the files) · `web` `npm run build` + `npm run lint` clean |
| OS logos + machine picker (Group 8 follow-up, 2026-09-28) | `run-console-security-tests` **195** (+6: `UI-02`, the heartbeat's OS — lower-cased, capped, control characters out, kept across an older agent's beat) · `run-appearance-consistency-tests` **35** · `OsIdentityTests` **18** new (run against Agent.Core alone that day: the full `SaveLocker.Agent.Tests` project could not build while an elevated test tray held `src/Agent/bin` — see `Gotchas` → Testing) · `web` build + lint clean |
| Notifications review fixes (PR #50, 2026-09-27) | `dotnet test tests/SaveLocker.Agent.Tests` **105** (21 new: escalation filter + retry, the keyed `/open` link, `ServerReachability`, `OpenLinkKey`) · `run-appearance-consistency-tests` **33** · `run-linux-tests` **201 pass / 2 fail under WSLg** (+2: Game Mode identity keys dropped from the click's env; the 2 are the same injected-session pair) |
| Group 8 review fixes (PR #52, 2026-09-28) | `run-console-security-tests` **205** (+10: two cancels at once withdraw and audit once; Phase 5c, a 6 s lease — a lapsed claim is withdrawn, a live one reported running, the withdrawn one never handed out again, a late result a no-op; the OS name's cap never splits a surrogate pair) · `web` `npm run build` + `npm run lint` clean |
| Group 10 + PR #54 review fixes (2026-09-30) | `dotnet test tests/SaveLocker.Agent.Tests` **181** (now also CI's `unit-tests` job) · `run-local-api-tests` **110** on Windows (`SL_LOCALAPI_PORT` moves it off :5188 when the rig's tray holds that) · `run-health-tests` **33** · `run-agent-tests` 47 · `run-delta-upload-tests` 33 · `run-concurrency-tests` 26 · `run-appearance-consistency-tests` **46** · `npm --prefix web run export:art -- --check` clean · `agent-ui` and `web` build + lint clean |
| Multiple save paths Group A (2026-10-04) | `dotnet test tests/SaveLocker.Agent.Tests` **207** (+26: `MultiRootArchiveTests` 17, `IncludeGlobTests` 9; every archive guard mutation-checked) · `run-console-security-tests` **352** (+44, `SP-01`) · `run-server-bugbounty-tests` **216** · `run-health-tests` **33** · `run-hardening-tests` **33** · `run-delta-upload-tests` **33** · `web` + `agent-ui` build, `web` lint clean |
| Multiple save paths Group B (2026-10-05) | `dotnet test tests/SaveLocker.Agent.Tests` **223** (+16: `MultiPathAgentTests` 13, `EnrollDeclaredFoldersTests` 3 — the latter starts the server's own build output, which the test project now references for build order) · new `run-multipath-tests` **39** (:5199; without shadows 14 fail) · `run-agent-tests` 47 · `run-delta-upload-tests` 33 · `run-concurrency-tests` 26 · `run-hardening-tests` 33 · `run-local-api-tests` **113** · `run-linux-tests` in WSL **201 pass / 2 fail**, identical to `main` run the same way (the 2 are the WSLg session pair; both then stop at the root-only `chattr +i` Decky probe) · `run-linux-regression-tests` 15 · `web` + `agent-ui` build |
| Multiple save paths Group C (2026-10-05) | `dotnet test tests/SaveLocker.Agent.Tests` **247** (+19: `FolderSuggestionsTests` 11, `SavePathEditorTests` 4 against a real server, 3 "Also found" enrollment cases, 1 archive folder listing) · `run-local-api-tests` **119** (+6: the save-folder routes need the token; mutation-checked — with them carved out of the guard, the 4 token checks fail) · `run-multipath-tests` 39 (the CLI now goes through `SavePathEditor`) · `run-appearance-consistency-tests` 46 · detection sweep `--sample 300 --seed 1` **286/299 (95.7%)**, identical to `main` · `web` + `agent-ui` lint, typecheck and build clean · `openapi.json` and both `api-types.ts`: additions only · testenv (Windows + WSL + console) by hand, steps in the PR |
| Multiple save paths Group C, PR #58 review fixes (2026-10-06) | `dotnet test tests/SaveLocker.Agent.Tests` **256** (+9: `SavePathEditorTests` 2 — an existing key refused by Add, a folder another machine added first joined not defined twice; 6 enrollment join-rule cases; the lock probe test now waits out a scanner's brief hold) · `run-multipath-tests` 39 · `run-local-api-tests` **116/116** with `SL_LOCALAPI_PORT=5191` — the rig's trays held :5188 and :5187, and §10's server switch hardcodes :5187, so its 3 checks did not run here · `web` + `agent-ui` lint and build clean · `openapi.json` and both `api-types.ts`: additions only |
| Group 9 + PR #53 review fixes (2026-09-29) | `run-console-security-tests` **308** on Windows (BK-01 **84**: zip backups, download ticket, delete, restore + undo + the start's own setup, temp-file cleanup, the kept undo point, encryption at rest, before-upgrade; CFG-01 **17**; each new check mutation-checked) · `web` `npm run build` + `npm run lint` clean |
| Review fixes for PR #60: server-decided emulator names, scope-join refusal (2026-10-07) | `dotnet test tests/SaveLocker.Agent.Tests` **291** (+6 `EnrollNamingTests` — two/three machines against a real server; the refusal and the server-decided name each mutation-checked; +4 `GameSourcesTests`, +2 `RetroArchTests`, `AvoidNameClashes`'s test removed with it) · full solution build clean · `agent-ui` lint and build clean · `agent-ui/src/api-types.ts` regenerated from a dev tray on :5190 (`EnrollResponse.notes`, `CandidateDto.emulatorRom` — the only diff) · server `openapi.json` not regenerated: no server route or DTO shape changed (only constants and a static helper on `GameSourceDto`) |
| Emulator saves: game sources per machine, names without "(RetroArch)" (2026-10-07) | `dotnet test tests/SaveLocker.Agent.Tests` **280** (+5 `GameSourcesTests` — the stale-host save check mutation-checked; `RetroArchSyncTests` also asserts both machines' sources on the server and on the agent's game list) · full solution build clean · `web` + `agent-ui` lint and build clean · `openapi.json` and `web/src/api-types.ts` regenerated, additions only (`agent-ui/src/api-types.ts` edited by hand: `GameSourceDto`, `TrackedGameDto.source`, `GameDto.machineSource`) · console source chip + popover checked in the browser against a seeded dev server |
| Emulator saves merged onto multiple save paths + Phase 1b states (2026-10-06) | `dotnet test tests/SaveLocker.Agent.Tests` **247** (`main`'s 228 + `RetroArchTests` 17 + `RetroArchSyncTests` 2 — two machines against a real server; an unscoped states folder fails it) · `run-multipath-tests` **39** · `run-delta-upload-tests` **33** |
| Emulator saves Phase 16, linking by hand + Model 2 states (2026-10-07, `emulator-saves-group-b`) | `dotnet test tests/SaveLocker.Agent.Tests` **325** (+5 `LinkByHandTests` — two machines against a real server; the re-check of a pick and the here-first `TrackedFor` each mutation-checked; +1 `ArcadeSavesTests` for Model 2 states, slot names mutation-checked) · Windows agent and `Agent.Linux` build · `agent-ui` lint + build clean · `agent-ui/src/api-types.ts` regenerated from a scratch tray on :5190, additions only · Add games checked in the browser against a scratch server seeded as a Deck (join, keep as its own game, greyed-out different files) |
| Emulator saves Group C (2026-10-07, `emulator-saves-group-c`) | `dotnet test tests/SaveLocker.Agent.Tests` **361** (+29: `MemoryCardTests` 17 — the shared-card detection and the PCSX2 per-game scope each mutation-checked; `GroupCSyncTests` 5, two or three machines against a real server — PS2 folder card, PS1 card, Wii, and Dolphin vs PrimeHack kept apart, the emulator rule and the in-batch emulator each mutation-checked; `EmulatorChipTests` 7 — a missing chip fails it) · Windows agent, `Agent.Linux` and server build · `web` + `agent-ui` lint and build clean · `openapi.json`, `web/src/api-types.ts` and `agent-ui/src/api-types.ts`: additions only (`GameDto.emulators`; `CandidateDto.notSyncable`/`emulatorConsole`), the agent's regenerated from a scratch tray on :5193 · `testenv emu-fixture` written to a scratch folder and scanned with the dev CLI; Add games checked in the browser against a sandboxed tray (the live rig was in use, see CONTEXT) |
| Emulator saves Group C, names by serial (2026-10-08) | `dotnet test tests/SaveLocker.Agent.Tests` **363** (+2: the Prince of Persia case — two sequels whose `icon.sys` says only the series, and one game from two regions — mutation-checked; `ConsoleTitles` spellings; the four console tests now assert serial names and a fallback each) · solution build · `testenv.ps1` parses |
| Emulator saves Groups G, I, J (2026-10-18/19, `emulator-saves-group-.g-i-j`, PR #78) | `dotnet test tests/SaveLocker.Agent.Tests` **721** (+65 since 656: `StatesOnlyTests` 20, `GroupISyncTests` 5, `SrmShortcutTests` 21 (+1 gated on `SAVELOCKER_SRM_CAPTURE`, the Deck's real `shortcuts.vdf`), `FileNamesTests` 11, `GroupGSyncTests` 4 against a real server, SvgPath curves/arcs; the name swap mutation-checked) · solution build clean · `web` + `agent-ui` build and lint clean · `npm --prefix web run icons:check` 54 icons · `openapi.json`, `web/src/api-types.ts` regenerated (additions only), `agent-ui/src/api-types.ts` edited by hand · migration `AddFileNamesAndMerge` · testenv console + WSL agent: states-only row, icons in both UIs, merge preview |
| Emulator saves Group F follow-ups: eShop names and icons, placeholder renamed, Change on Switch rows (2026-10-14, `emulator-saves-group-f`) | `dotnet test tests/SaveLocker.Agent.Tests` **558** (+7: `SwitchNamesAndLinksTests` 4; `GroupFSyncTests` +3 against real servers — the WSL-first rename, a game picked by hand taking the title, the eShop icon from a stand-in host; both server changes mutation-checked) · solution build clean · `agent-ui` typecheck and lint clean · no API shape change · testenv: WSL-first homebrew renamed on Windows' add and followed on WSL; TotK's real eShop icon fetched by the rig's console |
| Emulator saves Group F: Switch (2026-10-13, `emulator-saves-group-f`) | `dotnet test tests/SaveLocker.Agent.Tests` **551** (+12 since 539: `SwitchSavesTests` 8 — the PC's real `imkvdb.arc`, the reversed profile UUID, one row per title with names, device saves, "No save yet", Ryujinx slots; `GroupFSyncTests` 3 against a real server — Eden ↔ Ryujinx as one game (the title-key match mutation-checked: with it off on both sides the test fails), a same-named other title kept apart, the same-machine copies kept in step / asked / switched; +1 chip label) · solution build clean · `web` + `agent-ui` typecheck and lint clean · `openapi.json`, `web/src/api-types.ts` and `agent-ui/src/api-types.ts` (regenerated from a scratch tray on :5193): additions only · migration `AddGameEmulatorKey` · `testenv emu-fixture` written to a scratch folder and scanned with the dev CLI; Add games and the Switch Emulator settings page clicked through against a scratch tray + server (copy here, keep in sync live through the poller) |
| Emulator settings, DLC sent automatically, 8 GB hand-overs (2026-10-13) | `dotnet test tests/SaveLocker.Agent.Tests` **523** (+6 since 517: an answer applies to one launch, a client that can ask asks again, a refused resolve says why (the bare-string body, mutation-checked), the default send policy sends (mutation-checked), the console asks for a machine and its agent installs what it lacked (the missing-pack filter mutation-checked), the 9 GB over-limit request) · server build clean · `web` + `agent-ui` lint and build clean · `openapi.json` and `web/src/api-types.ts`: additions only (`ConsoleTransferRequest`, `POST /api/games/{id}/transfers`); the agent API is unchanged · agent Emulator settings clicked through against the rig's Windows tray (all three sections); console Emulator settings against the new server on a scratch copy of the rig's database, including **Send from WinTest** creating a hand-over that **Hand-overs** then lists with **Cancel** |
| Phase 5c steps 5–14: the profile split by game, the core, the game's profile, DLC check and hand-over, held pulls, console cards (2026-10-13) | `dotnet test tests/SaveLocker.Agent.Tests` **517** (+5 since 513: the hand-over end to end between two machines against a real server — held, asked, sent, installed, delivered and deleted, the held pull then restores; never-send declines; a received zip installs only its own package at the listed size and refuses a path outside it; every launch check reports what it did) · solution build clean · `web` + `agent-ui` typecheck and lint clean · `openapi.json` and `web/src/api-types.ts`: additions only; `agent-ui/src/api-types.ts` edited by hand · migration `AddLaunchProfileReport` · `testenv emu-fixture` writes the packages with headers matching `XeniaContent.BuildHeader` byte for byte · Decky PR #5 `tsc` clean |
| Phase 5c steps 1–4: Xenia file formats, launch questions, the profile at launch (2026-10-12) | `dotnet test tests/SaveLocker.Agent.Tests` **495** (+21: `XeniaFormatsTests` 14 — XDBF layout and round trip, one row merged with the others kept and the totals recounted, Account with both keys, both header sizes, both DLC layouts, config edits that change only the value, Xenia Manager's per-game config; `XeniaLaunchProfileTests` 7 — ask with the default applied, keep puts it back and the wrapper's check after it honours that (mutation-checked), don't ask again, the wrapper switches silently, Leave / already right / Xenia running / pre-profile layout leave it, per-game config). With `SAVELOCKER_XENIA_CAPTURE` pointing at captured profile files, the real PC's and Deck's GPDs round-trip and their totals match · solution build clean · `agent-ui` typecheck clean, `agent-ui/src/api-types.ts` edited by hand (additions only: `Ask`, `LaunchQuestion`, `LaunchChoice`, `LaunchAnswerRequest`, `launch-answer`) · Decky PR #5 `tsc` + build clean · Playnite PR #8 **28** tests, Release build clean |
| Emulator saves Group E, Xenia device saves + named profiles (2026-10-12) | `dotnet test tests/SaveLocker.Agent.Tests` **474** (+3: a device-only game, the named layout, a device save between two machines with its DLC left) |
| Emulator saves Group E (2026-10-12, `emulator-saves-group-e`) | `dotnet test tests/SaveLocker.Agent.Tests` **471** (`main`'s 450 + 21: `TitleIdSavesTests` 16 — Cemu, Azahar with NCSD/NCCH headers, Xenia's two layouts, the title cache, `qt-config.ini` and `content_root`; `GroupESyncTests` 2 — Azahar with states and Xenia across two profiles, two machines against a real server, both scopes mutation-checked; 3 chip labels) |
| Region beside the name, not in it (2026-10-11, `emulator-saves-group-c`) | `dotnet test tests/SaveLocker.Agent.Tests` **387** (+4: another region's release takes the same name and never joins, release fields per reader; +3 `GameReleaseServerTests` against a real server — two regions of one name are two games, a region-less game takes the first release, an oversized release refused; the server's region match mutation-checked) · solution build clean · `web` + `agent-ui` typecheck, lint and build clean · `openapi.json` and both `api-types.ts`: additions only (`GameReleaseDto`, `GameDto.release`, `CreateGameRequest.release`; agent's `TrackedGameDto.release`/`saveFiles`, `CandidateDto.region`), the agent's regenerated from a scratch daemon on :5298 · migration `AddGameRelease` applied on a fresh database · console and agent pages checked in the browser against a seeded scratch server and daemon (two Warrior Withins, Homebrew's *Details* chip, Esc closes) |
| "N machines following" (2026-10-11, appearance follow-up b) | `run-health-tests` **35** (+2: a beat says the machine follows by default, and one with *Follow the console* off says so — both fail with the server's assignment removed) · `run-appearance-consistency-tests` 46 · solution build clean · `web` typecheck, lint and build clean · migration `AddAgentFollowsConsoleAppearance` applied on a fresh database · `openapi.json` and `web/src/api-types.ts`: additions only (`AgentHeartbeat`/`AgentHealthDto.followsConsoleAppearance`) |
| PR #62 review fixes (2026-10-09) | `SaveFileTree*` + `ArtCache*` **17/17** (+2: a name held twice in a zip — diff and hashes keep the later entry; `NotInHead` both ways and no head comparing nothing) · full suite **348/349** (the 1 failure is the known `ArcadeSavesTests` Supermodel EmuDeck-gamelist leak) · solution build clean · `web` + `agent-ui` typecheck and lint clean · `agent-ui/src/api-types.ts`: one addition, `LocalFileDto.notInHead` (edited by hand, matching the generator's shape); server `openapi.json` unchanged |
| Agent art cache (2026-10-08, PR #62) | `dotnet test tests/SaveLocker.Agent.Tests` **346/347** (+5 `ArtCacheTests` — same URL from disk, changed URL re-fetched, offline fallback, cleared art, warm + prune; three rules mutation-checked; the 1 failure is the same `ArcadeSavesTests` environment leak) · `run-local-api-tests` **122/123** with `SL_LOCALAPI_PORT=5191` (every §11 art-proxy check passes, hostile URLs included; the 1 failure is the candidate-folder setter, as before) · `Agent.Linux` builds |
| Save file trees (2026-10-08, PR #62) | `dotnet test tests/SaveLocker.Agent.Tests` **341/342** (+10 `SaveFileTreeTests` — unit + two machines against a real server; the scope split, a size-only diff and the "head moved, nothing changed here" rule each mutation-checked). The 1 failure, `ArcadeSavesTests.Supermodel_nvram_is_a_game_named_from_games_xml_with_its_states`, reads this PC's real EmuDeck ES-DE gamelists — an environment leak from #61, not this branch · `run-local-api-tests` **119/120** with `SL_LOCALAPI_PORT=5191` (+2: `files` needs the token, 404 for an unknown game; the 1 failure is the candidate-folder setter, untouched here) · `run-appearance-consistency-tests` **46** (palette now 15 per block; a drifted blue in `tokens.css` or `Theme.cs` each fail it) · `web` + `agent-ui` lint, typecheck and build clean · `Agent.Linux` builds · `openapi.json` and both `api-types.ts`: additions only |
| Emulator saves Group C, PR #63 review fixes (2026-10-09) | `dotnet test tests/SaveLocker.Agent.Tests` **366** (+3: a DuckStation card named after its game — multi-disc and an imported save — and the poller's folder guess, both mutation-checked; a card in slot 3 is no game; the sanity check also warns about a folder of per-game PS1 cards). `MultiPathAgentTests.One_watcher_covers_every_folder_with_one_delay` failed once in a full run under load (a timing flake from `main`): it waited a fixed 1.5 s for a fire whose watcher events and timer callback run on the busy thread pool. Now it waits up to 15 s for the fire, then 2 s of quiet, with a 1 s delay so both writes share one window, and also proves a change in the second folder alone fires (mutation-checked by watching only the first) — full suite 3/3 green afterwards · solution build (`--no-incremental`) · `agent-ui` lint and build clean · no API change |
| Emulator saves Group C, one game across card names (2026-10-11) | `dotnet test tests/SaveLocker.Agent.Tests` **388** (+1: one European Warrior Within on `PS2MC-1_converted.ps2` and on `MC1.ps2` is one game; `GroupCSyncTests`' PS2 case now uses two card names; with the card back in the scope both fail, and two scan tests) · no API change |
| Emulator saves Group C, review of the 2026-10-11 commits | `dotnet test tests/SaveLocker.Agent.Tests` **390** (+2: two releases of one name both watched for their process — throws with the old name-keyed map; a European DuckStation card with the USA card's name never joins its game — joins with region left out of `SameFiles`; both mutation-checked) · solution build (`--no-incremental`) clean |
| Conflict: keeping the cloud's save takes effect (2026-10-11) | `dotnet test tests/SaveLocker.Agent.Tests` **394** with the review fixes merged (+4 `ConflictResolveTests` against a real server: Cloud restores a never-synced loser and the next push has nothing to send; a save changed after the conflict is not replaced; keeping this device's needs no further conflict; a pause behind a conflict resolved elsewhere lifts — the forced pull and the pause lift each mutation-checked) · solution build (`--no-incremental`) · `agent-ui` lint, typecheck and build · `agent-ui/src/api-types.ts`: the local resolve route answers `ResolveConflictResponse` (additive), regenerated from the test tray on :5188 · rig: the pop-up returns after *Sync all* |
| Emulator saves Group D (2026-10-11, `emulator-saves-group-d`) | `dotnet test tests/SaveLocker.Agent.Tests` **427** after merging `main` (Group D's share: `SonySavesTests` 25 — `PARAM.SFO`, prefix regions, PPSSPP (states-only games, mutation-checked), RPCS3, Vita3K (every user's `savedata`, mutation-checked), shadPS4 both layouts; `GroupDSyncTests` 3, PPSSPP Deck Flatpak ↔ Windows memstick with states against a real server, the per-game scope mutation-checked, a states-only game joining and its first save following, a states-only push never emptying another machine's save — a whole-folder scope fails it; `EmulatorChipTests` +4 console labels) · `ParamSfo` checked against the Deck's real RPCS3 save · `Agent.Linux` build (`--no-incremental`) · `agent-ui` build + lint · no API change · `testenv emu-fixture -EmuDeckPath <scratch>` scanned with the dev CLI: all seven Group D games listed |
| Emulator saves Group B (2026-10-07, `emulator-saves-group-b`) | `dotnet test tests/SaveLocker.Agent.Tests` **319** (+28: `IdentityByFoldersTests` 4 — D1, both halves mutation-checked; `GamelistXmlTests` 4; `MelonDsTests` 7; `ArcadeSavesTests` 9 — the Supermodel seed filter mutation-checked; `ScummVmTests` 3; `GroupBSyncTests` 2, two machines against a real server) · `agent-ui` build + lint clean · `Agent.Linux` builds |
| Emulator saves Phase 1 (2026-10-04) | `dotnet test tests/SaveLocker.Agent.Tests` **203** (+22: `IncludeGlobTests` 9 — the three scoped-restore checks mutation-checked — and `RetroArchTests` 13) · `run-delta-upload-tests` **33** · `run-hardening-tests` **33** · `run-health-tests` **33** · full solution build at the 1-warning baseline |
| Archive UTC write times (2026-09-28) | `dotnet test tests/SaveLocker.Agent.Tests` **114** (+9: `SaveArchiveTimestampTests` — fail before the fix off UTC) · `run-delta-upload-tests` **33** (+4: section 10, newest change through full and delta pushes; mutation-checked — dropping the server copy fails 1, the old agent stamp fails 3) · `run-hardening-tests` **33** · `run-health-tests` **22** |
| Quick-wins batch: registration default, conflict latch, exit lease, sessions table, art cleanup, lockout notice, Sync all detail (2026-10-09, `fix/backlog-quick-wins`, merged up through PRs #62–#63) | `dotnet test tests/SaveLocker.Agent.Tests` **397/397** (`ConflictLatchTests` 3 — the stale-latch clear mutation-checked; `SaveFileTreeTests`' shared-server registration now carries the admin password, which the flipped default requires — without it that one test fails; the `ArcadeSavesTests.Supermodel_nvram_…` failure #61 carried is gone once #63 is merged in) · `run-console-security-tests` **364/364** (was 308; +56: the default-ON registration half and the OFF switch, the sessions table + revoke-by-id, the lockout list, art removed with its game) · `run-local-api-tests` **127/127** (post-exit-sync while a held Sync all answers 409 *and* still releases the lease; a stale candidate-folder check fixed — it expected the user profile accepted, which `SavePathGuard` refuses; do not name a variable `$home`; `SL_LOCALAPI_PORT` if another worktree's rig holds :5188) · `run-agent-tests` 47 · `run-delta-upload-tests` 33 · `run-health-tests` 33 · `openapi.json` + `web/src/api-types.ts` regenerated, additions only · `web` build + lint clean |
| Art picker source choice + Linux agent secret permissions (2026-10-12, `art-picker-and-linux-agent-secrets`) | `run-console-security-tests` **378/378** (+14: `art/source` GET/PUT validation and reset, `art/search` with years and an empty-query short-circuit, options/Refresh art following the chosen id, the wrong entry never asked again, and the startup backfill filling from a pinned source; the suite's stub now serves `games/id` and per-id art) · `dotnet test tests/SaveLocker.Agent.Tests` **450/450** · `run-agent-tests` 47/47 · `run-linux-tests` in WSL **280 pass / 2 fail** — the 2 are the documented WSLg-injected-session pair; +9 permission checks, 4 of which fail against the pre-change Agent.Core (mutation-checked); run in `unshare -n` because a foreign daemon held :5187 · Windows agent build clean · `web` (`tsc -b && vite build`) + `oxlint` clean · `agent-ui` untouched · `openapi.json` + `web/src/api-types.ts` regenerated, additions only · migration `AddGameSteamGridDbSource` |
| Games tab sorting & grouping (2026-10-12, `console-games-sort-group`, PR #69 review fixes) | new `run-organize-tests` **13/13** (`tests/run-organize-tests.ps1` + `organize-fixtures.cjs`: compiles the real `organize.ts` with the console's tsc and drives it in Node — size/date null-last both directions, name flips, rolling buckets, device overlap + the head uploader, a console through two emulators, source kinds in order, status headings matching each row's dot; the Size-ascending check fails against the PR's original `sortValueOf`) · runs in CI's `build-web` job · `web` `npm run build` + `npm run lint` clean |
| Save safety: emptied-folder hold + excluded files kept on pull (2026-10-20, `save-safety-empty-hold-and-excludes`) | `run-hardening-tests` **45** (+8 §10, 5 fail on `main`) · `run-health-tests` **51** (+20 §9, 10 fail on `main`) · `dotnet test tests/SaveLocker.Agent.Tests` **728** · `run-local-api-tests` **131** (`SL_LOCALAPI_PORT=5191`) · `run-agent-tests` **49** (fresh server) · `run-multipath-tests` 39 · `run-delta-upload-tests` 33 · `run-concurrency-tests` 26 · appearance consistency 50 · `agent-ui` + `web` typecheck/lint clean · `agent-ui/src/api-types.ts` regenerated from a scratch tray on :5193, additions only · `openapi.json` unchanged (no server shape change) · Decky plugin `tsc` + rollup build clean. Not run: `run-linux-tests`, testenv pass |
| Playthrough count rides push/pull (2026-10-28, `playthroughs-group-3`) | `dotnet test tests/SaveLocker.Agent.Tests` **885** (+2 `PlaythroughCountSyncTests` against a real server; **mutation-checked** — with the agent's take disabled both fail) · `run-playthrough-tests` **103** · `run-playthrough-agent-tests` **68** (`-Port 5397`; :5187 was held) · `run-delta-upload-tests` **33** · `openapi.json` and `web/src/api-types.ts`: additions only (`UploadResult.playthroughCount`); the download's `X-Playthrough-Count` header is not in the schema. Not run: testenv, a Deck |
| Playthroughs Group 3 Phase 7, PR #97 review fixes (2026-10-27/28, `playthroughs-group-3`) | `run-playthrough-tests` **103** (2026-10-28 +1: before its own first push, a device on a fork lists the version it was forked from) · (+3: Prune naming one playthrough cuts only that one, naming none still prunes them all; **mutation-checked** — with the playthrough filter removed, "leaves Main as it was" fails) · `web` typecheck, lint and build clean; mockup bundle builds · `openapi.json` and `web/src/api-types.ts`: additions only (`playthrough` query on `POST /api/games/{id}/prune`) · `run-appearance-consistency-tests` **not run** (no `pwsh` on this box) — its two view rules (same-string ternary, hardcoded colour) re-checked by grep; the ternary rule is what failed CI · console checked in the browser against a seeded scratch server: a fork's History, New-from-version after picking Start fresh, the new tab selected, archiving the open tab, an archived tab, Prune scoped |
| Playthroughs Group 2 — one device (2026-10-26, `playthroughs-group-2`) | new `run-playthrough-agent-tests` **68** (:5187, `-Port` if the rig's WSL relay holds it; starts and stops its own server; fork, switch, two lines at once, refused while running / on a conflict, start fresh, offline from a copy, copies off, tampered copy, only-until-uploaded; +8 from the PR #95 review — offline there and back, a push before the flush — all 6 new behaviour checks fail on the pre-review agent) · `dotnet test tests/SaveLocker.Agent.Tests` **883** (+15 `PlaythroughStoreTests`; the verify-on-restore check mutation-checked — disabling it fails 1; the two review tests each fail with their fix removed) · `run-playthrough-tests` **99** · `run-delta-upload-tests` **33** · `run-multipath-tests` **39** · `run-concurrency-tests` **26** · `run-health-tests` **51** · `run-hardening-tests` **45** · full solution build 0 errors (the Windows agent still shows its pre-existing MSB3277 **and** `LoopbackPeer.cs` CS8604, already on `main`) · `web` + `agent-ui` build and lint clean · `openapi.json` + `web/src/api-types.ts` +176/+175 lines (three agent routes), `agent-ui/src/api-types.ts` regenerated from the testenv tray · the console's `SwitchPlaythrough` command checked end to end against a scratch server and a real tray. Not run: `run-linux-tests`, `run-local-api-tests`, `run-winagent-tests`, `run-server-bugbounty-tests`, `run-console-security-tests`, `run-agent-tests`, a Deck, a controller |
| Playthroughs Group 1 — the server (2026-10-26, `playthroughs-group-1`) | new `run-playthrough-tests` **99** (:5186; +3 from the PR #94 review: the agent state route describes the device's own playthrough; the migration section needs `dotnet-ef` + python and is skipped, not failed, without them; **mutation-checked** — fan-out to every device instead of followers fails 1, retention protecting only the default's head fails 3) · `dotnet test tests/SaveLocker.Agent.Tests` **868** (+19 `PlaythroughRulesTests`) · `run-server-bugbounty-tests` **216** · `run-console-security-tests` **390** · `run-agent-tests` **49** (fresh server on :5179) · `run-health-tests` **51** · `run-hardening-tests` **45** · `run-multipath-tests` **39** · `run-delta-upload-tests` **33** · `run-concurrency-tests` **26** · full solution build 0 errors · `web` + `agent-ui` build clean · `openapi.json` + `web/src/api-types.ts` regenerated, additions only (9 routes, optional `playthrough` parameters, `AgentCommandType.SwitchPlaythrough`); `agent-ui/src/api-types.ts` +11 lines from a scratch daemon on :5190. Not run: `run-linux-tests`, `run-local-api-tests`, `run-winagent-tests`, testenv |
| Manifest path constraints + scheduled refresh, console-controlled (2026-10-14, `manifest-path-constraints`, not merged) | `dotnet test tests/SaveLocker.Agent.Tests` **571** (+19: `ManifestPathConstraintTests` 11 — traversal/link escapes and a hostile YAML entry, 4 of which flip with the guards removed; `ManifestRefreshTests` 8 — plausibility floor, size cap, stale/fresh window, failure keeps the cache, the console window + request bookkeeping, 3 of which flip with the guards removed) · `run-hardening-tests` **37/37 Windows** (+4: manifest traversal + link escape with a real junction, a clean-sibling control, escape target untouched; **2 fail against the pre-fix resolver**) · `run-agent-tests` **49/49** against a fresh scratch server (+2: hostile traversal entry refused with its target created, clean sibling still resolves); live wire check: `POST /api/settings/manifest` → `GET /api/settings` → heartbeat response carries `manifestRefreshDays`/`manifestRefreshRequestedAt`, 9999 refused 400 · detection harness unchanged (feed stays live — no pin) · `openapi.json` + `web/src/api-types.ts` regenerated, additions only · Server/Agent/Agent.Linux builds clean, `web` build + lint clean · **review fixes on the PR:** xunit **576/576** (`ManifestPathConstraintTests` 12 — a contained `..` now collapses and resolves; `ManifestRefreshTests` 12 — error-page wrap, attempt-gap `onStarted`, 304 window, clamp); a `YamlException` escaping the fire-and-forget task and a console press consumed with no download (mark-on-start, `!=` due stamps) fixed; download moved outside the manifest lock with an ETag conditional; the suites now pin the server's window (`Manifest__RefreshDays=0`) since the heartbeat's pushed value overrides the fixtures' agent-side `0`; the traversal fixtures re-targeted (agent-tests escapes to `C:\Users`, hardening asserts "never named"); `run-delta-upload-tests` 33/33, `run-local-api-tests` 128/128, `run-console-security-tests` **378/378**, `openapi.json` + `api-types.ts` regenerated (only change: `refreshDays` nullable; live: `{}` → 400, `5` → stored, `9999` → 400) |

The two platforms differ by design — each suite skips the other's cases. The detection drop from
99.0% is the install-directory guard and is deliberate ([[Backlog]] → file-level saves); `main` reads
394/396 at a **400**-sample run, so quote the sample size or the two are not comparable.

Server build 0/0. Console lint and build clean. The Windows agent build has **one** pre-existing
warning (MSB3277, a WindowsBase 4.0/5.0 conflict from WebView2) — a second one means something.

`run-winagent-tests.ps1` owns :5189 (+ :5190–:5198) and `.verify-winagent`. Slow by design (~5 min,
mostly real waits on lease renewal, lock contention and WebView2 startup). **It drives two real tray
processes**, so it needs an interactive desktop session and silently skips those blocks without one.

## Regenerate OpenAPI types (web dashboard)

```sh
# 1. Start the server: cd src/Server && dotnet run
# 2. In another terminal:
cd web && npm run gen:api
# Regenerates web/src/api-types.ts from http://localhost:5179/openapi/v1.json
```

Commit the updated `src/Server/openapi.json` snapshot alongside any API changes.
