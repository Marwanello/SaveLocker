# Linux Agent — Streamlining the add-game flow

**Status:** Phases 1 + 2 **done** — landed in PR #24 (branch `linux-agent-streamline-phase1`),
merged 2026-07-24, full CI green including `agent-tests-linux` and the cross-OS chain. Phase 3
(gamepad-native Game Mode UI) **not started** — a separate PR, gated on three on-device checks
(§3.3.1 stub window under gamescope, §3.3.2 Steam Input delivering a gamepad, §3.1 tarball size
delta) that need a real Deck. **Written:** 2026-07-23.
Linked from `Backlog.md` now that Phase 3 is the scheduled remainder.

> [!note] What actually shipped vs. this plan (Phases 1–2)
> - 1.1 folder-pick is shared via `agent-ui/src/useFolderPicker.ts`; the candidate setter is
>   `POST /api/candidates/{id}/folder`, and Add Games' native pick still uses the existing
>   `candidates/{id}/folder-pick` (dialog + cache write) — both endpoints coexist.
> - 1.2 `CandidateDto.PrefixPath` is computed in `ToCandidateDtos` (deepest existing dir under
>   `pfx/drive_c/users/steamuser`); `PathBrowser` now hides only links resolving *outside* the roots.
> - 1.4 `AgentStateDto.Platform` drives the "Start on login" label.
> - 2 `GET /api/launch-command`; the Linux path is resolved from `/proc/self/exe`. New
>   `agent-ui/src/components/LaunchSetupCard.tsx`, shown on Overview + the Add Games success state.
> - `run-local-api-tests.ps1` grew from 22 to **27** checks (symlink-inside listed/traversable,
>   candidate folder setter, launch-command). The Decisions §2 amendment + rejected-alternatives
>   table belong to **Phase 3** and are not yet written.

---

## Context

Adding a third game on the Deck (2026-07-23) took: Desktop Mode → agent UI → Rescan → tick game →
**a "Set save folder…" button that does nothing** → Settings (game absent, because it was never
enrolled) → Konsole → `savelocker scan` → manually hunting the Wine prefix for the save folder →
SSH from a PC because the resulting path was too long to type on the Deck → `savelocker add-game`.
It worked, and it is not something an average user would survive.

Three distinct problems, and they compound:

1. **The Add Games "Set save folder…" button is genuinely dead on Linux.**
   `agent-ui/src/components/AddGamesView.tsx:50` calls `api.candidateFolderPick(id)`, which hits
   `POST /api/candidates/{id}/folder-pick`. That endpoint calls `_pickFolder()`, and
   `Agent.Linux/Daemon.cs:70` passes `pickFolder: null` — headless boxes have no native dialog — so
   it resolves to `null` and the handler returns `{ path: null }`. The UI does nothing with a null
   and shows no error. **The button is enabled, clickable, and inert.**
   `SettingsView.tsx:140` has the correct pattern (`pickFolderFor`: native dialog first, fall
   through to `PathBrowserModal`); the 2026-07-19 Deck work landed it in **Settings only**.
2. **The scan's prefix knowledge never reaches the UI.** `Agent.Linux/LinuxGameScanner.cs:28`
   computes the exact compatdata prefix for a shortcut and then **discards it** — only
   `SuggestedSaveDir` survives onto `ScanCandidate`. So when the guess is null (the normal case for
   a game absent from the Ludusavi manifest) the UI has nothing, and the browser opens at `$HOME`.
   That is the "hunt around the Windows file structure" step.
3. **Nothing about Game Mode.** The agent UI is a web page on `localhost:5178`, so reaching it means
   installing a browser in Desktop Mode and adding it as a non-Steam shortcut. And the launch-options
   string a user must paste into Steam is printed by `install.sh` once, at install time, and never
   shown again anywhere.

**Intended outcome:** after `install.sh` runs once in Desktop Mode, a Deck user never returns to
Desktop Mode. They launch **SaveLocker** from the Steam library, tick a game, pick its save folder
from a browser that already opens inside that game's prefix, and copy the launch command.

### Two questions settled up front

- **Why can the save path be set in two places?** There is no reason for two. **Add Games** becomes
  the only place a path is *first* set (and enrollment is gated on it); **Settings** keeps editing
  only, renamed **Change save path**. Decided by the maintainer, 2026-07-23.
- **Should we write Steam's `shortcuts.vdf`?** **No** — read-only, show + copy only. Steam rewrites
  that file from memory on exit, so writing it under a running Steam is silently reverted.

---

## Phase 1 — the blocking bugs (ship first; no new dependencies)

### 1.1 Make "Set save folder…" work in Add Games

Extract the native-first-then-browser logic that already exists at `SettingsView.tsx:140` into a
shared hook, `agent-ui/src/useFolderPicker.ts`, and use it from **both** views. The two surfaces
drifting apart is precisely what caused this bug; one implementation removes the possibility.

- `AddGamesView` gets `PathBrowserModal` on the same terms as Settings: `api.candidateFolderPick`
  first (the Windows tray returns an Explorer path), and on `null` open the browser.
- Applying a browsed path for a *candidate* needs a server-side setter, because the candidate cache
  lives in `AgentApiServer._candidateCache`. Add `POST /api/candidates/{id}/folder` taking
  `FolderRequest`, mirroring `POST /api/games/{id}/folder` (`AgentApiServer.cs:232`) and reusing the
  `with { SuggestedSaveDir = … }` cache rewrite already in the `folder-pick` handler
  (`AgentApiServer.cs:246`).
- Add an inline **Set save folder** button on any candidate row with no path, matching the unmapped
  row already in `SettingsView.tsx:302`. The toolbar button stays for the tick-one-then-click flow,
  but the row button is what a Deck user will actually hit.

### 1.2 Open the browser inside the game's own Proton prefix

- Add `PrefixPath` to `ScanCandidate` (`src/Agent.Core/ScanCandidate.cs`). `LinuxGameScanner.ScanAsync`
  already holds it in its local `prefix` variable (`LinuxGameScanner.cs:28`) — pass it through. The
  Windows scanner leaves it null.
- Surface it on `CandidateDto` as the **deepest existing** directory of
  `…/pfx/drive_c/users/steamuser`, falling back to the prefix root, falling back to null. Computed
  in `ToCandidateDtos` so the UI never builds Wine paths itself.
- The browser's start path becomes: candidate's `path` → `prefixPath` → root list.

> [!warning] `PathBrowser` currently hides every symlinked subdirectory
> `src/Agent.Core/PathBrowser.cs:91` skips all links, and a Wine prefix is full of them —
> `My Documents`, `Application Data` and friends under `steamuser` are links. Change the rule from
> *"skip all links"* to *"skip links whose resolved target is outside the roots"*, reusing the
> existing `RealPath` + `IsUnder` helpers. **The containment guarantee is unchanged; only the
> display rule loosens.** The existing symlink-escape checks in `run-local-api-tests.ps1` must still
> pass, and a new check must prove a link *inside* the roots is now listed and traversable.

### 1.3 Gate enrollment on a save folder

`AddGamesView.enroll` already refuses and prints a message (`AddGamesView.tsx:61`) — but only *after*
the click. Disable **Enroll selected** while any ticked candidate has no path, and name the offending
games in the status line. A game cannot be enrolled into a broken state, which is the state that
produced the silent Deck failures.

`Enroller.EnrollAsync` already carries `SteamAppId` onto the tracked game (`Enroller.cs:42`) — that is
what lets `ProtonRun` match the launch later, and it needs no change.

### 1.4 One name per action in Settings

- Delete the duplicate footer **Set save folder…** button (`SettingsView.tsx:325`) and its
  `setGameFolder` handler. Selection there is for **Remove selected**.
- The per-row button reads **Change save path** for a mapped game and **Set save path** for an
  unmapped one (a game enrolled before 1.3 existed). Neither string collides with Add Games.
- While in this file: **`Start with Windows` is shown on a Deck.** Drive the label from the platform
  — add `Platform` to `AgentStateDto` (already reported on the heartbeat) and render "Start on
  login" on Linux.

---

## Phase 2 — the launch command, visible and copyable

The string only ever appears in `install.sh`'s closing banner (`packaging/linux/install.sh:135`).
Put it in the UI.

- New `GET /api/launch-command` on the agent API returning `{ command: string | null, note: string | null }`.
  Linux resolves the **real installed path** rather than assuming `$HOME` (`/proc/self/exe` → the
  `~/.local/bin/savelocker` symlink target); Windows returns nulls and the UI hides the card.
- A **Steam launch setup** card on Overview and again on the Add Games success state: the command in
  monospace with a **Copy** button (`navigator.clipboard.writeText`, falling back to a hidden
  `<textarea>` + `document.execCommand('copy')` — webview clipboard permissions vary).
- **The card must say the command is identical for every game.** The maintainer's working method in
  Game Mode is to double-tap an already-configured game's Launch Options field, copy, and paste into
  the next game — so once one game is set up SaveLocker is not needed for the rest. Say that
  outright: this card is only load-bearing for the **first** game on a device.
- Carry over the two warnings `install.sh` already gets right: use the **full path** because Game
  Mode does not put `~/.local/bin` on `PATH`, and tick **Force the use of a specific Steam Play
  compatibility tool** on a non-Steam shortcut or Proton never creates a prefix.
- KB: update `web/src/help/adding-games.md` and `web/src/help/installing-the-agent.md`.

---

## Phase 3 — Launch SaveLocker from the Steam library (gamepad-native UI, no browser)

### The reframe: build it as a *game-shaped app*, not a desktop app

gamescope is a compositor built to run games. Every open risk in a desktop-toolkit approach is an
input-or-compositor question — does the X11 backend behave, does DPI scale, does focus work, does
the on-screen keyboard reach a text field — and a game-shaped app does not answer those questions
better so much as **stop them arising**, because that is the workload gamescope is tested against.

**Controller input is the decisive argument.** `CONTEXT.md` records that the D-pad does nothing in
the path browser and files it as a Desktop Mode quirk. It is not a quirk: it is what happens to any
desktop-toolkit app receiving Steam Input's *desktop* layout, where the right stick becomes a cursor
and the D-pad becomes nothing. A gamepad-native app receives the real device instead.

**And the Deck flows need essentially zero text entry.** Save paths are *browsed*, games are
*ticked*, the launch command is *copied*; server URL and machine name arrive in the enrollment file.
The entire Game Mode surface is list navigation plus toggles plus one copy action — so the on-screen
keyboard question largely evaporates, and gamepad navigation covers 100% of it.

### Rejected alternatives — recorded with their numbers so they do not resurface

| Approach | Added size | Controller nav | Why rejected |
|---|---|---|---|
| **Flatpak + WebKitGTK** | **665 MB – 1.5 GB** installed (~325 MB download) | hand-built | Size. See below. |
| **Godot + C#** | ~60–80 MB | good (built-in focus nav) | TFM conflict + new CI toolchain. See below. |
| **Avalonia** | ~20–30 MB | **hand-built** — inherits the dead-D-pad problem | Bigger *and* worse at the input model that matters. |
| **`steam://openurl`** | 0 | n/a | Game Mode does not open browser windows on a URL request — the same reason games cannot open browsers there. |

**Flatpak, in detail.** It works in Game Mode (Heroic, Lutris and Moonlight all ship that way) and
`flatpak install --user` needs no root, which suits `Decisions.md` §5. But **a Flatpak app targets
exactly one runtime and gets all of it** — extensions only *add*, and there is no supported way to
take a subset. WebKitGTK is **not** in `org.freedesktop.Platform`; it ships **only** inside
`org.gnome.Platform`, measured at roughly 325 MB compressed download and 665 MB–1.5 GB installed
depending on branch. `org.kde.Platform` with QtWebEngine is comparable or larger. On a stock Deck
nothing else uses either runtime, so none of it is shared. **Rejected on size.**

**Godot, in detail.** It is the expensive version of a good idea. Godot has required **.NET 8
minimum since 4.4**, works on net9, and **.NET 10 is only a proposal for 4.6** — while `global.json`
pins this repo to net10.0. So a Godot C# project cannot reference `Agent.Core` without splitting
target frameworks or downgrading the pin, which forces the UI to talk to the agent over the local
API instead of in-process. That is a real architectural tax for polish four screens do not need,
before counting export templates as a new CI dependency.

**A Flatpak'd *agent* was never on the table**, only a UI shell: `savelocker run -- %command%` is
executed *by Steam* and must spawn Proton and the Steam Linux Runtime, and nesting that inside
bubblewrap is the documented failure mode for Flatpak'd launchers under gamescope
(`ValveSoftware/gamescope#1341`).

### Ship: SDL + Dear ImGui, inside the existing binary

- **Dear ImGui's gamepad navigation exists precisely for this** — driving a UI on a console with no
  mouse attached (`io.ConfigFlags |= ImGuiConfigFlags_NavEnableGamepad`, plus
  `ImGuiBackendFlags_HasGamepad` on the backend). It is a config flag, not a feature to build.
- **Smallest option on the table.** We already pay for the .NET runtime — the Linux tarball is a
  self-contained publish. ImGui.NET's native `cimgui` plus SDL and a renderer backend should land in
  **single-digit MB**. ⚠️ *Estimated, not measured* — 3.1 gates on the real number.
- **Zero host dependencies, zero extra downloads.** No browser, no runtime, no Flatpak. It rides the
  tarball the user already downloads.
- **It is `savelocker ui` — the same binary**, dispatched in `Agent.Linux/Program.cs` alongside
  `daemon` / `run` / `doctor`. Native libs load on demand, so `savelocker daemon` on a headless box
  never touches SDL or a GPU.
- The non-Steam shortcut is trivially simple: target
  `/home/<user>/.local/share/SaveLocker/savelocker`, Launch Options `ui`.

> [!important] One binary, not a Deck-specific build
> A separate Deck build would double CI and release assets and create **fleet version skew**, which
> this project has already paid for more than once. It would also fork the daemon and the `run`
> wrapper — and those are the *sync-correctness* path, which must stay identical between a Deck and
> a Linux PC. **"Deck-specific" is a mode, not a build.** A Linux desktop user can run
> `savelocker ui` too, or keep using the browser at `localhost:5178`.

**The second-frontend objection, and how it stays small.** The app is a *view*, not a
reimplementation: it calls `Agent.Core` **in-process** — `LinuxGameScanner`, `PathBrowser`,
`Enroller`, `AgentConfig` — the same services `AgentApiServer` wraps as HTTP. No second API client,
no token handling, no duplicated sync logic. Four screens:

1. **Status** — connected, machine name, last sync, games tracked. Reads `AgentConfig`.
2. **Add game** — `LinuxGameScanner.ScanAsync()`, tick, set folder, `Enroller.EnrollAsync()`.
   Enrollment gated on a save folder here too (Phase 1.3's rule, enforced in `Enroller`).
3. **Set save folder** — a directory list over `PathBrowser`, seeded at the candidate's `PrefixPath`
   from Phase 1.2.
4. **Steam launch setup** — the Phase 2 command with a Copy button (`SDL_SetClipboardText`).

`Decisions.md` §2 must be amended. Its reasoning was *"Desktop Mode is just KDE with a browser"* —
true, and it is *why* the React UI stays the Desktop Mode and console surface. **Game Mode has no
browser at all**, which §2 did not account for. The React UI remains the only full frontend; this is
a Game-Mode-only subset.

### Costs the game framing introduces — accept these knowingly

- **Steam will show you as In-Game** and accrue playtime on the shortcut while the UI is open.
  Cosmetic, but visible to friends.
- **A render loop burns battery.** It must idle on events or cap hard — not spin at 60 fps drawing a
  static list. This is a requirement, not a polish item.
- **ImGui looks like a debug tool** out of the box. Themeable, but it will never look like the React
  UI. Judged an acceptable trade for four screens; revisit if it grows.

### 3.1 Build it

- New `src/Agent.Linux/Ui/`; `Program.cs` gains a `ui` case.
  `SaveLocker.Agent.Linux.csproj` takes `ImGui.NET` and SDL bindings.
- Sizes and hit targets assume a 1280×800 handheld at arm's length. Touch works, but **gamepad is
  the primary input** — every screen must be fully operable with D-pad + A/B and nothing else.
- **Verify the published size delta before writing any views.** If `packaging/linux/build-linux.sh`
  output grows materially beyond single-digit MB, stop and re-cost.

### 3.2 Ship it

- `build-linux.sh` needs no structural change; confirm the SDL and `cimgui` native libraries land in
  the publish output and survive `install.sh`'s `cp -r --remove-destination` loop.
- `install.sh`'s closing banner prints the one-time Desktop Mode step:
  - Steam → **Add a Non-Steam Game** → Browse → `~/.local/share/SaveLocker/savelocker`
  - Properties → Launch Options: `ui`
  - Rename it **SaveLocker**

  That is the last Desktop Mode step a user ever takes. We are not writing `shortcuts.vdf`.

### 3.3 On-device verification, in this order

1. **A stub window renders under gamescope and launches from the library.** SDL under gamescope is
   the most-tested path there is, so this should be uneventful — but it is still the first thing to
   build, before any views.
2. **Steam Input delivers a gamepad.** Confirm a non-Steam shortcut gets a *gamepad* template rather
   than the desktop layout, and that D-pad + A/B navigate every widget. This is the assumption the
   whole approach rests on; if Steam picks a desktop layout by default, the KB must tell users to
   change the controller layout, and that instruction belongs in `install.sh`'s banner too.
3. **Clipboard crosses the app boundary.** Copy the launch command, then paste into a game's Launch
   Options (Manage → Properties). Game Mode's clipboard is **confirmed working within Steam**
   (double-tap the field, copy, paste into the next game — the maintainer's current method), and
   both Steam's UI and our app are gamescope clients sharing one selection.
   **Fallback if it does not cross:** structurally none needed. Get the string into the first game by
   any means; every game after is copied from the first.
4. **Idle power.** Leave the UI open and confirm it is not pinning the GPU or draining battery.
5. **The full flow, cold:** install a fourth game, launch it once, then **from Game Mode only** —
   open SaveLocker, scan, tick it, set the folder from inside the prefix, enroll, copy the command,
   paste it into the game's launch options, relaunch, confirm a save syncs.

---

## Files touched

| Area | Files |
|---|---|
| Agent UI | `agent-ui/src/components/AddGamesView.tsx`, `SettingsView.tsx`, `OverviewView.tsx`, new `useFolderPicker.ts`, `api.ts`, `types.ts`, regenerated `api-types.ts` |
| Agent API | `src/Agent.Core/AgentApiServer.cs` (2 new endpoints, `CandidateDto`/`AgentStateDto` fields), `ScanCandidate.cs`, `PathBrowser.cs` |
| Linux agent | `src/Agent.Linux/LinuxGameScanner.cs`, `Program.cs`, new `src/Agent.Linux/Ui/*`, `SaveLocker.Agent.Linux.csproj` |
| Packaging | `packaging/linux/install.sh`, `build-linux.sh` |
| Docs | `web/src/help/adding-games.md`, `installing-the-agent.md`, `deck-supported-games.md`; vault `CONTEXT.md`, `Backlog.md`, `Decisions.md` (§2 amendment + the rejected-alternatives table above, with its numbers) |

> [!warning] Regenerate `agent-ui/src/api-types.ts` against a dev daemon on a **free port**, not :5178
> `npm run gen:api` hardcodes :5178 (`agent-ui/package.json:9`) and will silently generate types from
> the *installed* agent, dropping every new schema with no error. This is the documented trap in
> `Gotchas.md`. No **server** API changes here, so `src/Server/openapi.json` is untouched.

---

## Verification

**Automated**

- `.\tests\run-local-api-tests.ps1` — extend beyond its current 22 checks:
  `POST /api/candidates/{id}/folder` sets the cached path and rejects an out-of-range id; a symlink
  **inside** the roots is now listed and traversable; a symlink pointing **outside** the roots is
  still refused (both directions matter — 1.2 loosens a security-adjacent rule); `/api/launch-command`
  returns a path on Linux.
- `bash tests/linux/run-linux-tests.sh` in WSL — the scanner change must not alter candidate counts
  or dedupe. Export `DOTNET_ROOT`/`PATH` and copy `agent-ui/dist/` in per `CONTEXT.md`; never build
  from `/mnt/*`.
- `.\tests\run-agent-tests.ps1` (35 checks) as a regression gate. Clear `.verify/` **and** the server
  DB together — isolating one without the other reproduces the documented false failures.
- **Prove 1.1 fails first:** against pre-fix code, a scripted `candidateFolderPick` against a headless
  daemon returns `{path:null}` and no path is ever set. That is the bug, asserted.

**Manual, on the Deck** — Phase 3.3 is the acceptance test. Phases 1 and 2 are independently
verifiable in Desktop Mode against a browser at `localhost:5178` before any Phase 3 work starts.

**Sequencing** — Phases 1 and 2 are one PR: small, no new dependencies, releasable on their own, and
the difference between "the button does nothing" and a working flow regardless of whether Game Mode
launching ever lands. Phase 3 is a separate PR gated on three cheap checks in order: a stub window
under gamescope (3.3.1), Steam Input delivering a gamepad (3.3.2), and the tarball's size delta
staying in single-digit MB (3.1). If any fails, stop and re-cost before writing views — the whole
argument for this approach is that it is small **and** gamepad-native.
