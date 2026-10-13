# Controller support in the agent UI — replacing the Deck Game Mode SDL UI

| | |
|---|---|
| **Status** | **Built** — the controller layer is in the real `agent-ui` (Phases 1–5) and Game Mode opens it via `savelocker open`, with the Steam entry added/repaired automatically (Phase 6). Phase 7 (the hardware checklist) is in progress on a real Deck; Phase 8 (retiring the SDL UI) waits on it. Status board in §6 |
| **Branch** | `gamepad-agent-ui` (worktree `.claude/worktrees/gamepad-agent-ui`) |
| **Mockup** | `docs/tasks/gamepad-agent-ui/mockup.html` — the **real agent-ui** (real components, real CSS, real data shapes) with a fetch mock, plus the working controller layer. Open it in a browser; a real gamepad works via the Gamepad API, the keyboard drives the same path (arrows move, Enter = A, Backspace = B, X/Y, Q/E = LB/RB). Rebuild: `cd agent-ui && npm run mockup` |
| **Asked** | 2026-10-13 — "I don't like the Deck wayland UI. It doesn't match the style of the agent and has to be developed for every feature. Can we add support for controller in the normal agent showing the glyphs of the Xbox or PlayStation controller and use it instead of the Wayland UI? We already have a shortcut for the app on the Steam Deck. Create a full detailed clickable mockup with a plan of how to implement this and what needs to be put into consideration." |
| **Supersedes** | [[Decisions]] → *Linux UI* / *Desktop Mode window*: "Game Mode has no browser, so `savelocker ui` ... covers Status/Add game/Set folder/Launch setup" and "the launcher stays out of Steam, Game Mode keeps `savelocker ui`". Both were true premises when written; this plan changes them. |

---

## 1. The ask, and what it removes

The Deck Game Mode surface (`savelocker ui`, `src/Agent.Linux/Ui/`) is a **second, parallel UI**: about **6,500 lines of C#** (UiApp 2,760; Widgets 1,271; Theme 292; Icons 276; Sound/Wav 407; SettingsScreen 243; plus SvgPath, ImGuiInternal, AppMark, Screenshot, NavDebug, Gallery, Fonts), reading the same daemon through a second client, with its own palette copy, its own icon tracing, its own nav engine — and **every feature has to be built twice** (the emulator-saves work is a recent example: folder lists, include scopes and card warnings landed in the console, the agent UI and the SDL UI independently).

Meanwhile the **React agent UI (`agent-ui/`) already has every screen** the SDL UI has, is the shared surface on Windows (WebView2 tray), Desktop Mode (chromium `--app=` window via `savelocker open`) and the browser — and already runs on the Deck in Desktop Mode.

The proposal, in one line: **make `agent-ui` controller-navigable, show real controller glyphs, and point the Steam Deck's Game Mode shortcut at `savelocker open` instead of `savelocker ui`.** After a transition window, delete the SDL UI and its toolkit dependencies.

What this buys:

1. **One UI.** A feature is built once and appears in the console, the Windows tray, Desktop Mode and Game Mode.
2. **The Checkpoint look everywhere.** Game Mode currently approximates it (Group 6 ported tokens by hand into `Ui/Theme.cs`, guarded by a consistency test); the browser gets it exactly.
3. **The existing Steam shortcut keeps working** — only its launch command changes from `ui` to `open`.
4. **A free bonus on every other surface**: a controller plugged into a Windows PC or a Desktop Mode session also drives the agent UI. Controller mode is additive — mouse and keyboard stay primary on desktop.

Not in scope: changing the Decky plugin (it stays: launch-option setting, QAM sync chip, lease warnings, conflict popups), the launch gate (`savelocker run`), or the agent UI's wire protocol.

## 2. What exists today (verified against the worktree)

| Surface | Host | UI | Input today |
|---|---|---|---|
| Windows tray | `src/Agent` (WinForms + WebView2) | `agent-ui` | mouse/keyboard |
| Linux daemon | `src/Agent.Linux` on `:5178` | `agent-ui` | mouse/keyboard (KDE) |
| Desktop Mode window | `savelocker open` → chromium `--app=` (Flatpak included), else `xdg-open` | `agent-ui` | mouse/keyboard |
| **Game Mode** | Steam shortcut → `savelocker ui` | **SDL + ImGui** | D-pad/stick, A/B/X/Y, LB/RB |

**The SDL UI's full inventory** (what parity must cover, and what the mockup shows): Overview (stat tiles, lease banners, live sync activity, tracked-games rows, staged-update notice); Tracked games with **cover art**; Add a game (scan, filter pills, live enroll progress, "Also found" ticks, shared-card greyed rows); Conflicts (local-vs-cloud cards, immediate resolve); the game page (Sync/Push/Pull, folders, versions); a **folder browser** seeded inside the Proton prefix; Steam setup (launch command + clipboard); Activity + offline queue; Settings (settle stepper, sounds toggle, autostart, remove games); plus interface sounds, a battery/clock header, appearance refresh from the console, and dev affordances (`--nav`, `--screenshot`, `--gallery`).

Every one of those screens already exists in `agent-ui` (GamesView, GameDetailView, AddGamesView, ConflictsView, ActivityView, SettingsView, PathBrowserModal, LaunchSetupCard, useArt covers, `useActivity`, enroll progress, folder lists). The **actual gap is only the gamepad layer plus glyphs** — see the parity table in §7.

**Machinery that already exists and is load-bearing for this plan:**

- `savelocker open` (`Program.cs`, `AppWindow.cs`, `AppWindowPlan.cs`) — finds a Chromium-family browser (PATH, then Flatpak), opens `--app={url}?app#route`; needs `DesktopEnvironment.DesktopStartInfo()` (a display); notification clicks already route through it.
- The **Steam shortcut** — created manually today per `install.sh` step 4 (Exe `~/.local/share/SaveLocker/savelocker`, Launch Options `ui`). The test rig's `DevSteamShortcut` (`dev-shortcut-add --kind ui`, gated behind `SAVELOCKER_ALLOW_TEST_COMMANDS`) can create/remove one programmatically, with art.
- `agent-ui`'s deep-link routes (`#conflicts:queue`, `#game:<id>`, `#games:folders`) and the `?app` header mode — both reused as-is.
- `LocalAuth`/token: the page already authenticates every call; nothing about input changes the security model.

## 3. Architecture of the change

```
Steam Input (Deck pad, or DualSense over BT)
        │  presents a virtual X360-class device (uinput)
        ▼
Chromium-family browser  ──  Gamepad API (navigator.getGamepads poll)
        │                             │
        │  agent-ui (one React app)   ├─ pad layer   (edge detect, repeat, deadzone)
        │                             ├─ nav engine  (spatial focus, scroll-into-view)
        │                             ├─ hint bar    (per-route binding table)
        │                             └─ glyph set   (auto-detect / manual override)
        ▼
Keydown fallback: arrows/Tab/Enter  ←→  mouse/touch (unchanged, primary on desktop)
        ▼
Local API :5178 (unchanged)          Remote server (unchanged)
```

**Controller mode is a layer, not a mode.** No routing fork, no second shell. What changes when a pad is connected (or the user turns it on):

- a `data-savelocker-pad` attribute on `<html>` — enables the hint bar's visibility, slightly larger focus treatment, and disables "hover-only" affordances;
- the **focus ring** (reusing the Checkpoint accent outline) becomes the primary pointer;
- the hint bar at the bottom (a new `HintBar` component) describes the current screen's bindings (`A Select · B Back · Move · X <contextual> · Y Sync all · LB/RB Sections · Start Quick menu · ☰ Steam menu`).

The mockup (in the repo beside this file) is the executable specification of this layer: focus ring, hint bar, glyph sets (Xbox / PlayStation / Auto), the conflict queue flow (pick side → resolve → "apply to all remaining"), the two-step conflicts list, the folders prompt, the quick menu (Start), the OSK fallback, blocked rows excluded from focus, per-route B semantics — and a "Notes" overlay tying every element to the implementation note below it.

## 4. New code (agent-ui only, until Phase 6)

**Status (2026-10-13): the layer is now IN the real app, not only the mockup.** `App.tsx` mounts `<PadLayer/>` once; the hint-bar and on-screen-keyboard styles live in `ui.css` (the mockup's copy is gone) together with `html.sl-pad-mode`, which reserves the strip's height so nothing hides under it; and Settings gained a **Controller** card (hint bar Auto/On/Off, glyph set / style / tone, size slider, live preview, and the keyboard bindings for anyone without a pad). Inert on a machine with no gamepad: nothing renders, nothing changes. The mockup remains as the harness — it drives the same components through the mock backend and both test suites (83 jsdom + 29 real-browser checks). Files:

- `agent-ui/src/gamepad/pad.ts` — gamepad poll (edge detection, 400 ms/90 ms repeat, 0.55 stick deadzone, hidden-window pause, right-stick scroll stream shaped for fine control) + the keyboard fallback on the same action vocabulary (capture-phase, so a component with its own arrow handling cannot double-act).
- `agent-ui/src/gamepad/nav.ts` — spatial focus over the real DOM (`button`, `[role=button]`, inputs, …), overlay scope = topmost full-viewport fixed layer (a focus trap by construction; also detects a declared full-inset box so it works without layout), focus re-resolution across re-renders, scroll-into-view, right-stick scrolling of the focused element's own scroll container, per-route B (overlay cancel/skip → back button → nothing), LB/RB section stepping, Y = Sync all, X = Search (Games) / Filters (Add Games), R3 = List/Grid on Games and the focused row's folder action on Add Games, and the on-screen-keyboard state.
- `agent-ui/src/gamepad/glyphs.tsx` + `PadGlyph.tsx` — glyph images from **Julio Cacko's "Free Input Pack"** (P5Gamepad set for PlayStation, XGamepad for Xbox; `src/gamepad/pack/`, normalized names, copied and documented by `agent-ui/mockup/copy-glyphpack.cjs`; not vendored from npm — see the pack's README). The copy script also decodes every PNG (pure Node zlib) and writes `pack/bounds.ts` with each sprite's opaque content box, so the `Glyph` component scales every glyph's **content** into the same square box — equal visual size regardless of the pack's transparent padding. Five **styles** per set (Default / Light / Alt / Alt 2 / Retro), two **tones** (White default — the coloured faces fight the Checkpoint palette — and Color per device), and a **size in px** for the box (18–56, default **18** on controls and previews, **15** in the hint bar so the strip stays compact). `PadGlyph` renders a binding glyph **on the control it acts on** and only while a controller is active (`null` on mouse/keyboard, so the desktop is untouched); a control carrying one is skipped by arrow navigation. The mockup toolbar exposes set/style/tone/size; these become a Controller group in Settings when the layer is ported into the app. Navigation is scoped to `.sl-app` only — mockup chrome can neither take the ring nor swallow keys.

**Bindings, as built:**

| Input | Action |
|---|---|
| D-pad / left stick | Move focus (spatial, **pane-aware**: vertical stays in the column the ring is in; horizontal crosses panes only at an edge). **Tile pages** (one game's page, Settings): the big blocks are the first level — A opens a block, B closes it, arrows stay inside. A control carrying a binding glyph is skipped — the binding is the way in — as is anything `data-pad-skip` (the conflict card's per-side "Keep this"). The **side rail is never a stop** — LB/RB own it |
| Right stick | Scroll the focused view's own container |
| A | Activate; enter a tile; OSK on a text field |
| B | Back: OSK backspace → overlay cancel/skip → the Add Games filter lock → the open tile → the page back button. Leaving the **game page** re-focuses the row it was opened from; closing a **modal** (the path browser) returns focus to the control it covered |
| X | **Search** (Games) · **Sync this game** (game page) · **Filters ⇄ list** (Add Games — the filter block, all axes, is unreachable until X unlocks it and locks the arrows inside; X or B leaves) |
| View / Share | **Search** on any page with a search field (glyph beside the input) |
| L3 | **Rescan** in Add Games (glyph on the button) |
| Start | **Hide enrolled** in Add Games (glyph on the switch) |
| LT | **Keep the other one as a backup** on a conflict card (glyph on the checkbox) · **Push now** (game page) · **Clear** the selection (Add Games) |
| RT | **Resolve** on a confirm-mode conflict card (glyph on the button) · **Pull latest** (game page) · **Add games** (Add Games) |
| R3 | **List/Grid** (Games) · **Change**, cycling a candidate row's folder Change then its server-game Change link (glyph on both buttons) |
| Y | Sync all (single-flight; disabled while a run is going) |
| LB / RB | Previous / next section (wraps) — the only way the rail is navigated |
| Keyboard | Arrows move (**Up/Down leave a focused text field**; Left/Right keep the caret) · Enter A · Backspace/Esc B · X/Y · Q/E bumpers · Z LT · R RT · F View · G L3 · V R3 · PageUp/PageDown scroll · T Start. Only real text fields swallow letters; Ctrl/Meta/Alt shortcuts are never consumed |

Every glyph is also drawn **on the control it acts on** while a controller is active: Sync all (Y), List/Grid (R3), the search rows (X / View), Rescan (L3), Hide enrolled (Start), the keep-both checkbox (LT), Resolve (RT), Change/Set save folder and the server-game Change link (R3), and the game page's Sync/Push/Pull (X/LT/RT).

- `agent-ui/src/gamepad/HintBar.tsx` — the bottom strip (A's label follows the focused control; X appears only where it does something; a press flashes its own hint).
- `agent-ui/src/gamepad/Osk.tsx` — QWERTY fallback text entry; writes through the native value setter + input event so React-controlled fields see a normal keystroke.
- `agent-ui/src/gamepad/PadLayer.tsx` / `index.ts` — one mount that wires it all.
- `agent-ui/mockup/` — the mockup itself: fetch mock (`fetchMock.ts`, `fixtures.ts`), chrome (`MockFrame.tsx`), Vite config + single-file inliner (`inline.cjs`), wired to `npm run mockup`.
- `agent-ui/src/components/ConflictCard.tsx` — the two side panels gained `role="button" tabIndex={-1}` `aria-disabled` + `data-pad-label` (an accessibility fix the engine needs; they were plain clickable divs).

The rest of this section describes the remaining pieces to port into the app itself.

All new modules live under `agent-ui/src/gamepad/` and `agent-ui/src/components/HintBar.tsx`; nothing in `Agent.Core`, the server, or the wire protocol changes.

Original design sketch (kept for the reasoning; the status list above is authoritative — `navStore`/`focusScope`/`bindings`/`actions` collapsed into `nav.ts`, `QuickMenu` is the mockup's quick sheet, and the START menu / sounds remain open items):

| Module | Responsibility | Key decisions |
|---|---|---|
| `gamepad/pad.ts` | rAF poll of `navigator.getGamepads()`; standard-mapping button/axis table; edge detection; held-key repeat (≈400 ms delay, ≈90 ms interval, matching the SDL UI's own numbers); left-stick deadzone (0.5) with its own repeat; connect/disconnect events; pause while `document.hidden` | one pad drives the UI at a time (first connected wins; newer one takes over on press when the first is idle is *not* attempted — keep the simple rule) |
| `gamepad/nav.ts` | The spatial engine: collect visible `[data-focusable]` in the active scope, geometric scoring (primary distance + 1.7× lateral), scroll-into-view, adjustable capture (segmented/steppers consume left/right), disabled/low-priority skipping | pure functions (`nextFocus(from, dir, rects)`) so the scoring is unit-testable |
| `gamepad/navStore.ts` | `useSyncExternalStore`-style store the pad layer writes into; consumers (`Router`, `HintBar`) subscribe. **The pad loop must not re-render the app on every tick** | same pattern `useActivity.ts` already uses for the activity feed |
| `gamepad/glyphs.tsx` | `gamepad.id` → glyph set detection (`dualsense|dualshock|playstation|054c|wireless controller` → PlayStation, else Xbox; the Deck's own pad reports X360-class); manual override (Auto/Xbox/PlayStation) persisted in `localStorage` (device-local, correct); inline SVG glyph components (A/B/X/Y faces, LB/RB, D-pad, stick, Start, Back, Steam bars) | glyphs are inline SVG like the existing lucide usage; **no image assets** |
| `components/HintBar.tsx` + `gamepad/bindings.ts` | Per-route binding descriptors (`view → hints`), the live render, contextual-X registration (a small registry the active view can set, e.g. "Sync this game" on the game page) | X is contextual; Y is global Sync all; catalog is a table, not per-component markup |
| `gamepad/actions.ts` | Activation by element semantics: A on a button/row → click; A on a checkbox-style row → toggle; A on a text field → open OSK; B → close overlay, else route-back stack; Start → quick menu | B's routes: game page → list, any list → previous screen; **never silent** |
| `components/OnScreenKeyboard.tsx` | Fallback QWERTY, D-pad navigable (rows as focusables), A inserts, B deletes, Done/Cancel; **used only until the Steam+X OSK is proven on hardware** (Phase 0) | also the pattern for all text entry if Steam's keyboard can't reach the browser |
| `components/QuickMenu.tsx` | Start menu sheet: glyph style, interface sounds, test-controller readout, close | the mockup's `#ov-sheet` |
| `gamepad/focusScope.tsx` | Overlay focus trap + restore (wrap the existing modals); focus stability across React re-renders: re-resolve the focused node each frame, fall back to the scope's first focusable when it disappears | solves the SPA problem the SDL UI never had: data polls replacing DOM nodes |
| Integration edits | `App.tsx` (mount pad layer + HintBar), `Sidebar.tsx` (`data-focusable`), GamesView/GameDetailView/AddGamesView/ConflictsView/ActivityView/SettingsView (mark focusables, `data-first` per page, register X actions), `PathBrowserModal.tsx` (pad nav), `ReleaseChip.tsx` (popup trap), `SettingsView.tsx` (Controller card: glyph style, sounds, test readout) | mechanical, view by view |

Interface sounds (optional, off by default): a tiny WebAudio synth (three blips: navigate/activate/back), ported from the SDL UI's `Sound.cs` behaviour. The SDL UI could read SteamOS's own `.wav`s; a browser cannot read that folder — the plan keeps the *synthesised* variant only, and the setting. Default off until the maintainer decides otherwise (open question).

Battery: `navigator.getBattery()` where Chromium exposes it (`054c`-era Flatpak builds may not); render the topbar indicator only when available — never fake it.

## 5. Launch path in Game Mode (the part that changes files outside agent-ui)

**As built (2026-10-13).** All five points below are done:

1. **`savelocker open` prefers the session that launched it.** `DesktopEnvironment.OwnSessionStartInfo()` (this process's env, when it names a display) now wins over the user-manager's desktop-session overlay, which is only the fallback for processes without one (the daemon, an SSH shell). That overlay can be a stale snapshot of the OTHER mode — preferring it unconditionally is what once sent a Game-Mode-launched window to a compositor that was not running. Desktop Mode and the notification-click path are unchanged in practice (`run-linux-tests`' assertions hold).
2. **The Steam entry is automatic.** `DeckShortcut.EnsureAtStartup` runs on every daemon start (see the class doc for the exact rules): installed agent only; Steam present; **Steam not running** (its exit rewrite would drop the edit — the moment is a boot where the agent beats Steam); absent and never offered before; or present-but-stale (`ui` from an older Deck) and then **repaired in place** — name, icon, custom Launch Options and hand-set artwork preserved, the world `ui` → `open`, the AppID and the four grid pictures renamed with it. An entry with the same name that is not ours is never touched.
3. **By hand:** `savelocker deck-shortcut status|add|remove`. `add` is also the way to bring it back after removing it from Steam (the offer-once marker respects that choice).
4. **`install.sh` step 4 rewritten** (automatic entry; manual "Add a Non-Steam Game" as the alternative; the old `ui` launch-option note) and step 5 now says the shortcut's art is painted for you.
5. **testenv:** the rig's "SaveLocker Test" entry runs `savelocker open --port <n>` (`dev-shortcut-add --kind app`; `--kind ui` still exists for comparing against the SDL UI); `clean` removes both. The writer class was renamed `DevSteamShortcut` → **`SteamShortcutWriter`** (it serves the shipped entry too) and gained `Inspect`/`Retarget`; its unit tests grew from 7 to 12 (`SteamShortcutWriterTests`).

## 6. Phases

**Status board (2026-10-13).** Phases 1–5 are built and live in the real app, verified off-device; 0, 6 and 7 are not started (0/7 need the Deck, 6 does not); 8 waits on 7 plus one decision.

| Phase | Status |
|---|---|
| 0 — Hardware spike | ⬜ Not run — needs the Deck awake + SSH |
| 1 — Input layer | ✅ Built (`pad.ts`) — mounted in `App.tsx`; keyboard path verified in-browser |
| 2 — Spatial navigation + focus | ✅ Built (`nav.ts`: pane-aware, tile pages, focus restore) — browser-verified |
| 3 — Glyphs + hint bar | ✅ Built (pack + bounds normalization, `HintBar`, `PadGlyph`, Settings → Controller) |
| 4 — Text input | ✅ In-app OSK built and wired; the Steam+X question waits for Phase 0 |
| 5 — Overlays and queue flows | ✅ Built — conflict queue, folder prompt, path browser (autofocus + restore) verified |
| 6 — Game Mode launch path | 🟡 Built — `open` uses the launching session (gamescope included); `DeckShortcut` adds/repairs the Steam entry at daemon start; `savelocker deck-shortcut` by hand; installer + KB updated. Deck confirmation belongs to Phase 7 |
| 7 — Hardware checklist | ⬜ Not run — needs the Deck, a controller (DualSense nice-to-have), the maintainer |
| 8 — Retire the SDL UI | ⛔ Blocked on 7 + the escape-hatch timing decision |

Each phase ends in something observable; hardware spikes that would invalidate the design come first.

### Phase 0 — Hardware spike on the real Deck (no product code)

Do **not** build the layer before this; every assumption below is unverified and some are load-bearing:

| Question | Why it matters | Fallback if the answer is "no" |
|---|---|---|
| Does a Chromium-family browser exist on the stock Deck / is Flatpak Chrome-family reliable under gamescope? (SteamOS ships Firefox; the rig's Deck already had a Chromium-family for the KWin `--app=` check — confirm which) | no browser → no app window | (a) Firefox `--kiosk <url>` as a second-class window (no `--app`, still chromeless); (b) keep SDL UI as fallback one release; (c) document installing a Flatpak browser |
| Does the browser's Gamepad API see the Steam Input virtual pad **in Game Mode** (not only Desktop)? | the whole feature | if not: `--enable-features`/env workarounds, or SDL-side stays for input while web UI shows |
| Does Steam's on-screen keyboard (Steam + X) deliver text into a browser `<input>` in Game Mode? | decides OSK strategy | in-app OSK (built anyway, Phase 4) |
| Window fit under gamescope (1280×800, scaling, cursor, exit via Steam button; suspend/resume) | UX basics | window flags (`--start-fullscreen`, `--window-size`) |
| Memory/perf of a persistent browser window on the Deck | Gate is not a game; it may sit open | accept, or unload aggressively (`document.hidden` pauses the poll) |
| Does the shortcut-launched (`game`-categorised) browser get the Steam overlay/QAM + a clean exit? | matches Steam norms | reclassify/launch flags |

Output: a short write-up in this folder (spike results + the browser/OSK decisions) before any Phase 1 code merges. The mockup already assumes the *preferred* answers.

### Phase 1 — Input layer (`pad.ts`, `navStore.ts`)

Keyboard/mouse untouched; a connected pad starts driving focus. Deliverable: connect an Xbox or DualSense pad on any OS the agent-ui runs on (Windows tray, Desktop Mode, dev browser) and the focus ring responds, with debug readout in Settings → Controller.

Verify: unit tests for the pure parts (edge detection, repeat timing, deadzone, id→glyph mapping); live check of connect/disconnect/hotplug and `document.hidden` pausing; **no behaviour change when no pad is present** (the desktop default).

### Phase 2 — Spatial navigation + focus

`nav.ts` + `focusScope.tsx` + `data-focusable` marking of every interactive element across the six views and the folder browser. Implement per-route B, LB/RB section stepping, adjustable capture, scroll-into-view, overlay traps, and focus stability across the 10 s/15 s polls.

Verify: vitest on the scoring function; a scripted keyboard walk of every screen (keyboard uses the same code path as the pad — this is also the accessibility story); a live check that a conflict appearing mid-session does not steal or drop focus; PathBrowserModal fully drivable.

### Phase 3 — Glyphs + hint bar

`glyphs.tsx`, `HintBar.tsx`, `bindings.ts`, Settings → Controller card, contextual X registry per view.

Verify: Auto/Xbox/PlayStation switching; a DualSense over BT shows PlayStation faces without configuration; the hint bar matches the mockup's table per screen; keyboard-only users can toggle hints visibility in Settings.

### Phase 4 — Text input

OSK component + wiring for search, Server URL, folder key/include fields; decide in/after Phase 0 whether Steam + X substitutes anywhere (if yes, the OSK becomes fallback + the pattern for the path browser's "Choose"/confirm flows).

Verify: type a search, a server URL and a folder include pattern on the Deck with the pad alone; a real USB keyboard still types natively; Escape/B semantics inside fields are correct.

### Phase 5 — Overlays and queue flows

Sync-conflict queue (immediate resolve, apply-to-all, decide-later), conflicts list (two-step), folder-suggestions prompt, quick menu, toasts as announcements only, "remove game" two-step confirm.

Verify: the exact flows the mockup demonstrates, against a seeded scratch conflict (`tests/seed-test-conflict.sh` unchanged); a resolve performed pad-only; badge counts update; nothing resolves without a press.

### Phase 6 — Game Mode launch path

The §5 changes: `open` session rule + tests; `install.sh` text; testenv shortcut kind; release notes action for existing shortcuts.

Verify: on the Deck, Game Mode → the SaveLocker shortcut → the browser app window opens at the Overview; the pad drives it; exiting returns to Steam. Desktop Mode and the notification-click path keep their current behaviour (`run-linux-tests` unchanged assertions).

### Phase 7 — Hardware verification checklist (ran by the maintainer, on the real Deck)

1. Game Mode shortcut opens the agent UI; window fills 1280×800; Steam button + QAM work.
2. Move through all six sections + a game page + the folder browser with D-pad only; both sticks; deadzone fine; no runaway repeat.
3. Sync all from Y; cancel while running; a conflict appears → resolve "keep this device" pad-only; verify on the console that the head moved.
4. Type a search with the chosen text-entry path.
5. Xbox-style pad and DualSense (BT) → glyphs and hint bar switch correctly.
6. Battery indicator present or honestly absent.
7. Sleep/resume; agent update staged → notice visible; shortcut still works after update.
8. Desktop Mode unaffected; `savelocker ui` still runs (transition fallback).
9. The **launch gate** is independent: `savelocker run` still blocks a launch on a genuine conflict regardless of this UI.

### Phase 8 (later, deliberate) — Retire the SDL UI

After one release where both paths work and the checklist above is green on hardware:

- Swap the documented shortcut to `open` (already done in 6) and make `savelocker ui` print a pointing note (or leave it working, undocumented).
- Delete `src/Agent.Linux/Ui/` (~6.5k lines) and its dependencies (Silk.NET/ImGui packages, embedded fonts); keep `SvgPath.cs`'s test interest in mind; keep `Art/` (Steam art pipeline) — it is independent.
- Follow-ups this forces, each small but real:
  - `run-appearance-consistency-tests.ps1` currently guards the **fourth copy of the palette** in `Ui/Theme.cs` — that check retires with the file (keep the three-way console/agent-ui/Windows checks);
  - `Gotchas.md`'s ImGui/ImDeck sections become historical;
  - `docs/Architecture.md`, `REPO_MAP.md`, `CLI Reference` and the KB articles that describe `savelocker ui` are updated in the same change;
  - the ImGui 1.90.8.1 pin and `ImGuiInternal.cs` nav-fix notes leave the repo with the package.

## 7. Parity table (what the SDL UI has → where it lives in agent-ui)

| SDL UI feature | agent-ui equivalent | Gap to close |
|---|---|---|
| Overview: stats, lease banners, activity, tracked rows, staged update | `OverviewView`, `StatusHeader`, `useActivity`, `LeaseWarning` banners | pad focusables only |
| Tracked games + cover art | `GamesView`, `useArt` (offline cache) | pad focusables; grid vs list both navigable |
| Game page: sync/push/pull, folders, conflict banner, versions | `GameDetailView` (+ folder list, save files) | pad focusables; X context |
| Add game: scan, filters, progress, "also found", shared-card warnings | `AddGamesView`, enroll progress, blockers | pad toggles; blocked rows excluded (already `aria-disabled`) |
| Conflicts | `ConflictsView`, `ConflictCard`, `SyncConflictModal` | pad bindings; never pre-select (already the rule) |
| Folder browser | `PathBrowserModal` (keyboard-focus managed today) | pad navigation rows |
| Steam setup + clipboard | `LaunchSetupCard` | `navigator.clipboard` needs user activation — a pad "press" is a real activation, but verify on Deck; fallback textarea+select |
| Activity + offline queue | `ActivityView` | pad focus (read-only rows) |
| Settings: settle stepper, sounds, autostart, remove games | `SettingsView` | pad adjustables; sounds new |
| Battery + clock header | none | new, optional (§4) |
| Interface sounds | none | new, optional (§4) |
| Wordmark/theme follows console | `appearance.ts` live look | parity already (console-pushed accent works on the web) |
| `--nav`/`--screenshot` scripted verification | none | keyboard path + vitest + jsdom; real pad only on hardware |

## 8. Testing strategy (and what cannot be tested off-device)

The Gamepad API **cannot be synthesized**: neither Playwright nor CDP can inject gamepad state (only keyboard/mouse/touch), and `navigator.getGamepads()` returns real devices only. This is the single biggest testing constraint and it shapes everything:

1. **Pure logic, no JS test runner was needed after all.** The plan originally wanted vitest; what shipped is a built-mockup jsdom harness plus the real-browser pass, which together cover the same ground (scoring, repeat/deadzone behavior, glyph detection, binding tables) against the actual components. Revisit vitest only if the layer grows logic worth unit-testing in isolation.
2. **The keyboard path is the same code path.** Every pad action has a key equivalent (the mockup's mapping: arrows/Enter/Backspace/X/Y/Q/E/T). A scripted keyboard walk of every screen therefore exercises nav, activation, overlays and B semantics — and doubles as the accessibility story.
3. **jsdom smoke tests** for the shell. The built `mockup.html` was verified this way — 83 checks covering boot, the folder-suggestions prompt, the glyph pack (image source, 30 px size, set/style/tone/size switching), Sync all → conflict pop-up → LT keep-both → resolve, the conflict page's RT resolve and its button glyph, every Add Games filter axis, X unlocking/leaving the filter block, the new bindings (View search, L3 rescan, Start hide-enrolled, R3 cycling a row's folder Change then its server-game Change link), LT clear / RT add in Add Games, X searching, R3 flipping List/Grid, the games list → game page → back path **restoring focus to the row it left**, the OSK writing into a real field, and B closing the path browser.
4. **A real-browser pass (Playwright + system Edge, headless)** — 29 checks against the actual layout, which is where geometry bugs live. It has caught every regression jsdom could not: the search box as a focus trap, the sidebar winning vertical geometry, a disconnected row reference on back-restore, a dropped trailing mutation burst (a modal rendering in one go and going idle, so the hint bar never learned the "Keep backup" control had appeared), keyboard Enter never recording the game-page's origin row, and the path picker opened from a game-page card focusing the card behind it. Re-run this for any nav change: `playwright` is not a repo dependency — the harness lives outside the tree.
5. **`testenv` rig (WSL + console):** everything except the pad works there — enroll, conflict seeding, folder prompts, OSK typing via keyboard.
6. **Real hardware only:** Steam Input delivery in Game Mode, glyph auto-detection with a real DualSense, Steam + X keyboard, gamescope window behaviour, battery, suspend/resume. Phase 7's checklist is the gate; nothing ships as "working on Deck" before it runs.

Regression protection for the existing surfaces: no server/API/Agent.Core changes anywhere in this plan, so `run-agent-tests`, `run-local-api-tests`, `run-health-tests`, xunit etc. are untouched by construction — **except** Phase 6's `open` session rule, which `run-linux-tests` covers today (the notification-click/desktop assertions) and must keep passing with an added gamescope case.

**Phase 6 verification (2026-10-13).** `dotnet test` **456/456** (12 in `SteamShortcutWriterTests`, up from 7). `run-linux-tests` on this Windows box reads **23 failures — identical, name for name, to the pristine baseline** (verified by checking out the base commit in the WSL clone and re-running): 3 daemon aborts in the update section and everything cascading from them, plus this WSL's 2 WSLg session-detection failures already recorded in [[Gotchas]]. Every `open`/`xdg-open`/desktop-session assertion passes on the branch. The 23 are this machine's environment, not the code — the recorded 251/256-pass baselines come from the Linux container. Deck confirmation of the shortcut itself (tap it in Game Mode) is Phase 7.

## 9. What needs to be put into consideration

### A. Input and hardware

- **Steam Input template.** A non-Steam shortcut defaults to a gamepad template, but the user can set it to "Desktop" layout (keyboard/mouse emulation) — with that template the Gamepad API sees nothing. Release notes/help must say: leave the shortcut's controller layout on a gamepad template. Phase 0 verifies the default.
- **The Deck's own controls** appear as a virtual X360-class pad (gamepad `id` will look like `Microsoft X-Box 360 pad`), not "Steam Deck" — so **Auto glyph detection shows Xbox glyphs on the Deck itself**, which is correct (the Deck's buttons are A/B/X/Y) but must not be "fixed" into PlayStation by a naive heuristic.
- **DualSense/DualShock over BT** → PlayStation glyphs; over USB the id can differ. Detection is best-effort by id pattern; the manual override exists precisely because id strings are a zoo.
- **Steam/Guide button is not readable** (and shouldn't be — it opens Steam's menu). Keep it in the hint bar as an informational "☰ Steam menu", exactly like the SDL UI already does.
- **Multiple pads / hotplug:** one owner, first connected; a disconnect mid-hold must release repeat state and not leave focus wedged. Keep the rule simple; document it.
- **Trackpads and mouse coexist.** On the Deck in Game Mode the right trackpad/mouse can still drive real clicks; a click should move the focus ring (the mockup does this). The SDL UI's gamescope pointer-hover trap does not exist in a browser (hit-testing is normal), so no equivalent of `TrackCursorOwner` is needed — verify in Phase 0 rather than assume.
- **Deadzone and repeat numbers** (0.5 stick; ~400 ms/90 ms repeat) are copied from the SDL UI so muscle memory transfers; make them constants, not magic numbers scattered around.

### B. Browser and runtime on the Deck

- **Which browser.** `AppWindowPlanner` searches PATH for Chrome/Chromium/Edge/Brave, then Flatpak ids, then falls back to `xdg-open`. Stock SteamOS has neither Chrome nor Chromium — it ships Firefox (Flatpak). "A browser exists" ≠ "a Chromium-family browser exists"; the plan must either treat Firefox `--kiosk` as a supported second-class window or document the Flatpak Chromium/Chrome requirement. Flatpak Chromium under gamescope also needs a Phase 0 check (GPU/portal/wayland quirks).
- **`xdg-open` fallback is a plain tab** — acceptable in Desktop Mode, poor in Game Mode (tabs, URL bar). Do not silently land there in Game Mode; print/log the reason and fall back to the SDL UI (if still present) or instruct.
- **Text entry and the OSK** (see Phases 0/4): Steam's keyboard is the best outcome but unproven for a browser field; the in-app OSK is the guaranteed path and also the only one that works in Desktop Mode without touching Steam settings.
- **Clipboard** (`LaunchSetupCard` "Copy"): `navigator.clipboard.writeText` requires a secure context + user activation — loopback HTTP is a secure context, and a pad press is a user gesture, but verify on the Deck; keep a `document.execCommand('copy')` fallback on a hidden textarea.
- **Battery API** may be missing in Flatpak builds; hide, don't fake.
- **Window lifetime:** the browser profile persists (settings like glyph override live in localStorage and survive), updates of the *browser* move underneath the shortcut, and the agent's own update (tarball self-update) must not invalidate the shortcut's Exe path — it doesn't (same binary path), confirm in checklist 7.
- **Suspend/resume, `document.hidden`:** the pad poll pauses when hidden (battery, CPU on Deck), resumes on visibility — verify no stuck repeat on resume.
- **Memory:** a Chromium window is heavier than the ImGui app; on a 16 GB Deck this is fine, but it is a real difference and the reason the poll loop must be cheap and idle-pausing.

### C. Design and the Checkpoint system

- **Glyph art is settled, not a decision any more.** The input pack's White tone is the default (coloured faces clash with the Checkpoint palette); a per-device Color toggle stays for anyone who wants it, and the five pack styles (Default/Light/Alt/Alt 2/Retro) are switchable from the mockup toolbar (Settings, later). Glyphs are ~4 KB PNGs bundled through Vite — the real app only fetches the ones actually drawn.
- **Glyphs come from `gamepad.id`, not from Steam** — Steam knows the true controller and draws its own glyphs; a web page cannot ask it. Auto + override is the honest ceiling.
- **Hint bar budget:** seven fixed bindings + the informational Steam menu fit 1280 px at 11 px; a per-screen descriptor table keeps it honest (X's label changes, Y is global). Do not let views inject arbitrary entries — layout breaks first, meaning second.
- **Focus ring style:** reuse the accent outline (`:focus-visible` already styles it) but add a `[data-savelocker-pad]` variant with 2 px offset and a subtle scroll shadow where the ring meets a scroll edge. "A decision is waiting" colour rules stay intact (accent = decision, not decoration).
- **Hit targets and density:** the agent UI was built for desktop sizes; the Deck at 800p is fine (Group 6 already retuned the SDL UI to 16 px min body), but check the Games grid, the conflict panels and the Add-games rows in pad mode; the mockup's sizes are the reference.
- **Theme:** Game Mode is dark; agent-ui's base is dark and follows the OS. Nothing to do, but do not let the console's light pin leak (the Deck has no light preference).
- **Motion:** keep the existing entrance animations; they do not fight scrolling because focus scrolling uses `block: 'nearest'`.

### D. Behaviour and edge cases

- **A SPA re-renders around the focused node** (10 s state poll, 15 s conflicts poll, activity ticks). The focused element can be replaced; without the stability rule in `focusScope.tsx` the ring dies mid-walk. This is the single most likely runtime bug class in this design — the mockup simulates the fix (re-resolve + first-focus fallback) and must be ported carefully.
- **Never pre-select a conflict side, never resolve without a press** — both already true in the web card; keep them under pad input (no "held A on the card resolves on release" logic).
- **Dangerous actions stay two-step** (remove games, force pull): the pad must not create a one-press destructive path; the existing confirm patterns become focus traps with B = cancel.
- **Disabled controls are skipped** (Sync all while busy, Resolve before a side is chosen): nav must not land on them; the mockup's `focusables()` filters `disabled` — port that rule, including `aria-disabled` buttons that still look focusable.
- **Long lists** (versions, activity feed) scroll within the content pane only; `scrollIntoView({block:'nearest'})` against `overflow-y: auto` panes, never the page (the shell is fixed-height — the bounded-flex-column trap in [[Gotchas]] applies).
- **Deep links still land mid-flow** (`#conflicts:queue` from a toast, `#game:<id>` from the tray): the pad layer must focus the raised overlay when one opens from outside; the mockup's `openOverlay` → first-focus pattern is the rule.
- **Offline/unreachable:** errors surface as banners/toasts (unchanged); pad users must be able to read and dismiss them (toasts never steal focus; banners are focusable).
- **Leave `savelocker ui` reachable during the transition** so a Deck whose browser experiment fails is never bricked by this change.

### E. Security

- **Nothing changes.** The page keeps the `X-SaveLocker-Token`, loopback Host/Origin checks, no CORS. A pad is just another local input source; it cannot reach the API beyond what the page itself already can.
- The keyboard/OSK path must not introduce clipboard snooping (only the explicit Copy button touches it).
- The `?app`/`/open` handling (`LocalAuth.OpenLinkKey`) is untouched — the shortcut launches locally with the user's own token exactly as today.

### F. Packaging, rollout, docs

- **Existing decks must edit one line.** The shortcut already exists; its Launch Options change `ui` → `open`. Release notes carry this; `install.sh` step 4 text is updated for new installs. Consider the `savelocker deck-shortcut` verb (open question 4) to remove the manual step.
- **`agent-ui` gains vitest** — a new dev dependency and one CI step; call it out in the release notes process as tooling-only.
- **Docs to update when code lands:** `Build and Run.md` (*Throwaway test rig*, Deck notes), the KB `cli-reference.md` (`open`/`ui` fate), `Architecture.md` (Game Mode surface), `REPO_MAP.md` (Ui/ deletion later), `Decisions.md` (amend *Linux UI* + *Desktop Mode window*), `Gotchas.md` (gamescope/browser traps found in Phase 0), `Backlog.md` (link this plan), CONTEXT.md (session entry).
- **Release notes must cover:** controller support and glyphs; the Game Mode shortcut change (one-line action); the in-app on-screen keyboard; settings for glyph style/sounds; the phase where the SDL UI is deprecated (later release, separately announced).

### G. What "done" should not claim

- Do not claim Steam's OSK works until Phase 0 proves it; do not claim PlayStation glyphs on a Deck's own pad; do not claim the browser window is as light as the ImGui one; do not claim Game Mode verification from WSLg — gamepad input and gamescope compositing are precisely what WSLg cannot validate (the same caveat every prior Deck UI group carried).

## 10. Open questions for the maintainer

1. ~~**Glyph style:** platform-coloured or monochrome?~~ **Settled by the mockup pass (2026-10-13): the Free Input Pack, White tone by default, Color as an optional per-device toggle; five styles (Default/Light/Alt/Alt 2/Retro) switchable.**
2. **Interface sounds:** default off or on? (The SDL UI had them on, with a SteamOS-source indicator that a browser cannot reproduce.)
3. **X semantics:** contextual per screen (mockup's choice: "Sync this game" on games, "Rescan" on Overview, "Keep both" on conflicts) or reserved/unbound like the SDL UI's X?
4. **Shortcut automation:** promote `DevSteamShortcut` into a production `savelocker deck-shortcut add|remove` so existing Decks can be migrated by the agent, or keep the documented manual edit?
5. **SDL UI afterlife:** delete after one combined release (Phase 8 as written), or keep `savelocker ui` indefinitely as an escape hatch?
6. **Browser dependency:** accept "Firefox `--kiosk` as second-class" or require/guide a Chromium-family Flatpak for the full `--app=` experience in Game Mode?

## 11. File map when implementation starts

| File | Change |
|---|---|
| `agent-ui/src/gamepad/*.ts(x)` | new: `pad`, `nav`, `navStore`, `glyphs`, `bindings`, `actions`, `focusScope` |
| `agent-ui/src/components/HintBar.tsx`, `QuickMenu.tsx`, `OnScreenKeyboard.tsx` | new |
| `agent-ui/src/components/ui/*` | focus treatment additions; nothing visual beyond that |
| `agent-ui/src/App.tsx`, `Sidebar.tsx`, `GamesView.tsx`, `GameDetailView.tsx`, `AddGamesView.tsx`, `ConflictsView.tsx`, `ActivityView.tsx`, `SettingsView.tsx`, `PathBrowserModal.tsx`, `ReleaseChip.tsx`, `ConflictCard.tsx` | mark focusables, register X actions, pad-aware confirms |
| `agent-ui/package.json`, CI | vitest + one test job |
| `src/Agent.Linux/AppWindow.cs` / `Program.cs` (`open`) | gamescope-session rule + tests |
| `packaging/linux/install.sh` | step 4: `open` instead of `ui` |
| `src/Agent.Linux/DevSteamShortcut.cs`, `tests/testenv-deck.sh` | shortcut kind runs `open`; keep one SDL-UI shortcut during transition |
| `docs/*` (see F) | when code lands, not before |

## 12. References

- Mockup: `docs/tasks/gamepad-agent-ui/mockup.html` (self-contained; Gamepad API + keyboard).
- SDL UI inventory source of record: `src/Agent.Linux/Ui/` (UiApp/UiApp.Games/UiApp.Folders, Widgets, Theme, SettingsScreen, Sound).
- Launch machinery: `src/Agent.Linux/AppWindow.cs`, `AppWindowPlan.cs`, `DesktopEnvironment.cs`, `DevSteamShortcut.cs`, `packaging/linux/install.sh`.
- agent-ui design system: `agent-ui/src/tokens.css`, `ui.css`, `components/ui/`, `StatusHeader.tsx`; routes `route.ts`; activity store `useActivity.ts`.
- Related history: [[Decisions]] → *Linux UI*, *Desktop Mode window*; `tasks/checkpoint-ui/implementation-grouping.md` → Group 6, Group 10d; [[Gotchas]] → *ImGui / Deck UI*, *`savelocker ui` needs an explicit RID*.

