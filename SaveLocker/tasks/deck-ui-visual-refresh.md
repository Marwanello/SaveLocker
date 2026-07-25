# Task — Deck Game Mode UI: visual refresh + content parity

**Target:** `savelocker ui` (`src/Agent.Linux/Ui/`)
**Goal:** make the Game Mode surface look and feel like a mobile app on a 1280×800 Deck screen,
on the **same palette and typography as the console and the Windows agent UI**, and carry the
**same content** the agent UI provides (layout may differ; content should not shrink).

This is the revisit that `logs/2026-07-24_linux-agent-streamline.md` anticipated:

> "ImGui looks like a debug tool out of the box. Themeable... Judged an acceptable trade for four
> screens; **revisit if it grows**."

**Not in scope:** changing the stack. The rejected-alternatives table (Flatpak/WebKitGTK, Godot,
Avalonia, `steam://openurl`) stands — nothing has changed about size or the net10 TFM pin. Everything
below is reachable inside SDL + Dear ImGui, which is a full GPU vector renderer, not an ASCII grid.

---

## 0. Constraints that must survive

| Constraint | Where it comes from | Do not break it |
|---|---|---|
| One binary, no Deck-specific build | Decisions §2 amendment | Everything ships inside `savelocker` |
| Native libs load on demand | `csproj` comment | `savelocker daemon` on a headless box must still never touch SDL/GL |
| ~~Render loop must not spin~~ | design doc, "Costs the game framing introduces" | **Relaxed 2026-07-25 — see §0.1** |
| It is a **view**, not a second frontend | design doc | Keep calling `Agent.Core` in-process. No second API client (see §4 caveat) |
| Tarball delta stays single-digit MB | §3.1 gate | Budget below; measure at the end |

**Size budget for this task: ≤ 1.5 MB added to the uncompressed tarball.** Fonts and art are the
only real additions; both are subsettable/downscalable. Measure before and after.

### 0.1 Amendment (2026-07-25) — the battery constraint is relaxed

Phase 3 treated the render loop as a battery risk and hard-capped it at 30 fps
(`UiApp.cs:90`), calling it "a requirement, not a polish item". **The maintainer has revised this.**

The reasoning: this is a **configuration surface, not a game**. Realistic session length is minutes,
not hours — open it, add a game, set a folder, copy the launch string, quit. There is no 3D engine,
no asset streaming, no physics; the GPU cost is a few thousand textured triangles per frame against
a Deck APU that renders actual games. A handful of minutes at 60 fps is not a battery event.

**Therefore:** run the loop at a flat **60 fps**. Do **not** build the tween-aware dual-cap
(60-while-animating / 30-when-idle) the original Phase E called for — that was complexity bought
purely to service a constraint that no longer applies, and it would have added per-frame tween
bookkeeping to the window's pacing logic for no user-visible gain.

What is still worth keeping:
- **VSync stays on.** It is already set and it is what actually prevents an unbounded spin.
- Do not regress into an uncapped loop. 60 is a cap, not a removal of the cap.
- If the Deck ever shows a measurable drain in testing, the dual-cap idea is recorded here and can
  be revived. It is not being implemented on speculation.

---

## 1. The palette — single source of truth

Taken verbatim from `web/src/index.css` `@theme`, which the console and the agent UI both consume.

| Token | Hex | Used for |
|---|---|---|
| `BgGlobal` | `#2A3238` | window clear colour, content background |
| `BgCard` | `#1E252A` | cards, left rail, header bar, child panes |
| `BgTableHd` | `#222D34` | list headers, sub-panel headers |
| `BgRowSep` | `#252E35` | row separators |
| `TextPrimary` | `#ECEFF1` | body + headings |
| `TextMuted` | `#9CA3AF` | labels, secondary lines |
| `TextSecondary` | `#8B9AAA` | tertiary |
| `TextDim` | `#556070` | placeholders |
| `TextFaint` | `#64748B` | faint |
| `AccentGreen` | `#129271` | **primary accent** — active nav, connected state, primary buttons |
| `AccentAmber` | `#F4A60D` | warnings, "no save folder set", lease conflict |
| `AccentAmberLt` | `#FDCE63` | amber hover/emphasis |
| `Border` | `#494949` | all borders |

Derived tints already used by the agent UI — reproduce them, don't invent new ones:

- active nav background = `AccentGreen @ 14% alpha` (`rgba(18,146,113,0.14)`)
- server-URL chip = `AccentGreen @ 7%` fill, `AccentGreen @ 20%` border
- lease warning banner = `AccentAmber @ 12%` fill, `AccentAmber @ 45%` border

**Conversion:** `new Vector4(r/255f, g/255f, b/255f, a)` — no gamma conversion. The GL backend is not
sRGB-framebuffer, so straight division matches what the browser renders. If colours look washed out,
the framebuffer picked up sRGB — fix the framebuffer, do not fudge the constants.

Every one of these goes in `Ui/Theme.cs` as `static readonly Vector4`. **No literal colour may appear
anywhere else in `Ui/`.** The seven ad-hoc `new Vector4(...)` calls currently in `UiApp.cs`
(lines 223, 236, 238, 272, 349, 377, 387, 440, 498) all get replaced by token references.

---

## 2. Typography

Console uses **Inter** (300–700) and **JetBrains Mono** (400/500). Both are SIL OFL — vendorable.

- Vendor static TTFs to `src/Agent.Linux/Ui/Fonts/`.
  **✅ Done (Phase A):** Inter 4.1 statics (402 + 410 KB) and JetBrains Mono 2.304 (267 KB),
  **unsubset**, ~1.05 MB total — inside the §0 budget, so `pyftsubset` and its `fonttools`
  dependency were skipped. Subsetting to Latin would cut this to ~180 KB; worth doing if the budget
  ever tightens, not worth a build-time dependency today.
- Embed as assembly resources (`<EmbeddedResource>`), not loose files. No `install.sh` change, no
  path resolution, works identically from a dev build and an installed tarball.
- ImGui bakes one atlas entry per (face, size). **Keep the scale short** — each bake costs atlas area:

| Role | Face | px |
|---|---|---|
| Display (stat values) | Inter SemiBold | 30 |
| Title (screen heading) | Inter SemiBold | 22 |
| Body | Inter Regular | 16 |
| Body-strong (buttons, labels) | Inter SemiBold | 16 |
| Caption (stat labels, hints) | Inter Regular | 13 |
| Mono (paths, launch command) | JetBrains Mono | 14 |

Sizes are ~15% up from the console's web sizes: 1280×800 at arm's length is a smaller angular size
than a desktop monitor. Validate on the Deck, not on a dev monitor.

**Wiring:** Silk.NET's `ImGuiController` takes a font config / `onConfigureIO` callback in its
constructor — that is the hook for `io.Fonts.AddFontFromMemoryTTF` before the atlas is built.
⚠️ Verify the exact overload signature for Silk.NET 2.22.0 before writing against it.

Expose as `Theme.FontDisplay`, `Theme.FontTitle`, … `ImFontPtr`s, with `Theme.PushFont(...)` helpers.

---

## 3. Layout — use the 1280×800

The current screen is one flat full-bleed window with a text list; most of the panel is empty. The
new shell mirrors the agent UI's structure so the two read as the same product:

```
┌────────────────────────────────────────────────────────────────────┐
│  [logo]   ● CONNECTED            [server-chip 192.168.68.55:5080]  │  header 64 px, BgCard
├──────────┬─────────────────────────────────────────────────────────┤
│          │                                                         │
│ Overview │                                                         │
│ Add game │                content area — BgGlobal                  │
│ Settings │                24 px gutters                            │
│ Launch   │                                                         │
│          │                                                         │
│  rail    │                                                         │
│  220 px  │                                                         │
│  BgCard  │                                                         │
├──────────┴─────────────────────────────────────────────────────────┤
│  Ⓐ Select   Ⓑ Back   LB/RB Switch tab            SaveLocker 0.3.6  │  hint bar 44 px
└────────────────────────────────────────────────────────────────────┘
```

- **Left rail 220 px**, matching `agent-ui/src/components/Sidebar.tsx` (212 px): icon + label rows,
  active row = `AccentGreen @14%` fill + 2 px `AccentGreen` left border + SemiBold `AccentGreen` text.
- **Header 64 px**, matching `StatusHeader.tsx`: "AGENT STATUS" caption in `TextMuted` at 0.13em
  tracking, status dot with a glow, `CONNECTED`/`DISCONNECTED` in accent, server chip on the right.
- **Gamepad hint bar** at the bottom — a Deck-native affordance the agent UI has no equivalent for,
  and the single cheapest "this is a real console app" signal. Add it.
- **No horizontal scrolling anywhere.** Long lists scroll vertically inside a child pane.
- gamescope on the Deck presents a clean 1280×800 with no overscan — full-bleed is safe. Keep 24 px
  content gutters anyway for optical breathing room.
- Content wider than ~900 px should use **two columns**, not one long line. This is the manual-cursor-
  math part; put the column helper in `Widgets.cs` once rather than repeating it per screen.

---

## 4. Content parity with the agent UI

Enumerated from `agent-ui/src/components/`. **✅ = already in the Deck UI, ❌ = missing today.**

**Header / Overview** (`StatusHeader.tsx`, `OverviewView.tsx`)
- ✅ connected state, machine name, server URL, games tracked, saves pushed, last sync
- ❌ **agent version string** (`AgentStateDto.currentVersion`) — trivial, add to the hint bar
- ❌ **three stat cards** — Games Tracked / Saves Backed Up / Last Sync, 28 px tabular-nums values,
  green / primary / muted respectively. Currently a flat text list. This is the headline visual.
- ❌ **lease-warning banners** — amber card, `AlertTriangle`, "`{holder}` already has this game
  checked out", dismiss ×. **See the caveat below — this is the one real architectural item.**
- ✅ launch setup card

**Settings** (`SettingsView.tsx`) — **the whole screen is missing on the Deck**
- ❌ Connection: server URL, machine name, connection status *(read-only on the Deck — no text
  fields, no on-screen keyboard; `UiApp.OnLoad` deliberately calls `StopTextInput`. Show the values
  and point at Desktop Mode / the console for edits.)*
- ❌ Sync Safety: settle quiet seconds — **editable**, as a `-`/`+` stepper. Gamepad-friendly, no
  keyboard needed. This is the one setting worth making changeable in Game Mode.
- ❌ Currently Tracked Games: name + path, amber "No save folder set", **remove selected**
- ❌ Start on boot toggle (`startWithWindows` → `SystemdAutoStart` on Linux)

**Add game** (`AddGamesView.tsx`) — ✅ present; repaint as cards, keep the enroll gate
**Path browser** (`PathBrowserModal.tsx`) — ✅ present; keep the two-pane dirs/files split, it is good

> ✅ **Decided (2026-07-25) — lease warnings persist to disk.**
> `savelocker ui` is a **separate process from `savelocker daemon`** and reads `AgentConfig` from
> disk in-process. Lease warnings live in the *daemon's* memory and are served by `AgentApiServer`,
> so the UI cannot see them.
>
> **Resolution: the daemon persists lease warnings to a small JSON beside `config.json`**, and both
> `AgentApiServer` and `UiApp` read from that store. Rejected: an HTTP call from the UI to
> `localhost:5178`, which would have made the UI a second API client and broken the rule the whole
> Phase 3 design rests on.
>
> This is an improvement in its own right, not a workaround: warnings currently die with the daemon
> process, so a crash or a restart silently drops a conflict notice the user never saw. Persisting
> them fixes that for the agent UI too.
>
> **Implementation notes for Phase D:**
> - New file `%SaveLockerData%/lease-warnings.json` — same directory as `config.json` and
>   `offline-queue.json`. Follow `OfflineQueue.cs` for the write/atomic-replace pattern; it already
>   solves this exact problem in this codebase.
> - Writer: wherever the daemon raises a lease warning today (the path feeding
>   `AgentStateDto.leaseWarnings`). Dismissal deletes the entry.
> - Readers: `AgentApiServer` serves from the store instead of memory; `UiApp` reads it directly.
> - **Staleness:** a warning is only meaningful until the next successful sync of that game. Stamp
>   each entry with a UTC timestamp and drop entries older than 24 h on read, so a stale file cannot
>   nag forever.
> - Dismissal from the Deck UI must write through, so the same warning does not reappear in the
>   agent UI afterwards.

---

## 5. Artwork

Available, already tracked in git (masters are gitignored, `dist/` ships):

| File | Size | Use |
|---|---|---|
| `packaging/linux/artwork/dist/logo.png` | 482×720 | portrait mark — too tall as-is |
| `packaging/linux/artwork/dist/hero.png` | 1920×506 | **header banner** — crops beautifully to 1280 wide |
| `packaging/linux/artwork/dist/capsule.png` | 593×788 | Steam library art — not for in-UI use |
| `packaging/linux/artwork/dist/capsule-wide.png` | 782×430 | Steam library art — not for in-UI use |

**Do not embed these at full size** — 2.1 MB against an 8.8 MB delta is not a good trade.

Re-export a UI subset to `packaging/linux/artwork/dist/ui/`, checked in:
- `logo-64.png` — 64 px tall, for the header mark (~10 KB)
- `hero-1280.png` — cropped/scaled to 1280×160, for a Status-screen banner (~120 KB)

Embed **those two** as assembly resources. Upload to GL textures once at load, draw with
`ImGui.Image`. Keep the hero banner subtle — it sits behind the header, it is not the content.

Cover art per game (SteamGridDB, via `ArtService`) is a **later** enhancement: the `ui` process has
no server credentials of its own and the art cache is server-side. Note it in `Backlog.md`; do not
attempt it here.

---

## 6. Phases

Each phase must build and run before the next starts.

### Phase A — `Ui/Theme.cs` + the WSLg loop
**Start with `tests/linux/run-ui-wslg.sh` and the window-size override (§7.1)** — it pays for itself
across every later phase.

Then: palette tokens, type scale + font loading, spacing/rounding constants
(`Rounding.Card = 8`, `Rounding.Button = 6`, `Rounding.Pill = 999`, `Space.Xs/Sm/Md/Lg/Xl`),
and an `Apply(ImGuiStyle)` that sets `FrameRounding`, `ChildRounding`, `WindowRounding`,
`FramePadding`, `ItemSpacing`, `ScrollbarSize`, and the full `ImGuiCol` table.
Replace every literal colour in `UiApp.cs` with a token.

**Gate:** existing four screens render unchanged in structure, but in Inter on the console palette.
Screenshot it. This alone should kill most of the homebrew read.

**✅ Phase A complete (2026-07-25).** Delivered: `Ui/Theme.cs`, vendored fonts, the WSLg loop, and
`Ui/Screenshot.cs` — a built-in framebuffer→PNG capture (`ui --screenshot out.png`) added because no
screenshot tool was available in WSL and `apt` needed sudo. It works identically on the Deck, so
on-hardware appearance can be reviewed without photographing the panel. Three gotchas found and
recorded in `Gotchas.md` (RID requirement, SDL's sticky error, WSL PATH quoting).

### Phase B — `Ui/Widgets.cs`
`Card`, `PillButton` (primary/secondary/danger), `StatTile`, `ListRow` (icon + title + subtitle +
trailing), `Toggle`, `Stepper`, `Badge`, `Banner`, `SectionHeader`, `TwoColumn`, and the icon atlas.
Painted with `ImDrawList` over `InvisibleButton` where ImGui's stock widget can't carry the look.

**Icons — plan superseded.** The spec called for pre-rasterizing lucide SVGs into a packed PNG atlas.
**Rejected during Phase B:** no SVG rasterizer is available in this environment (`apt` needs sudo),
and a bitmap atlas is the worse artifact anyway — it bakes one resolution, and this UI draws the same
glyph from 14 px to 40 px.

**Shipped instead: `Ui/Icons.cs` draws them as `ImDrawList` vector paths** on lucide's own 24×24
grid, so the shapes stay readable against the upstream SVGs. No image assets, no build tooling, and
they re-tessellate per frame at whatever size is asked for. 15 glyphs plus an animated spinner.

⚠️ Icons must be drawn as **closed outlines, not radiating strokes**. The first gear radiated spokes
from a circle and read as a *sun* above ~24 px. Check every glyph at 40 px in the gallery, not just
at 24 — that is what the size strip at the bottom of the icon section is for.

**Gate:** a scratch gallery screen showing every widget in every state.

**✅ Phase B complete (2026-07-25).** `Ui/Icons.cs`, `Ui/Widgets.cs`, `Ui/Gallery.cs`.
Widgets are painted over `InvisibleButton` rather than styling ImGui's stock controls — that keeps
full gamepad-nav participation (focus, activation, clipping) while allowing any appearance. The
tween helper is keyed by ImGui item ID and uses exponential decay (`1 - e^(-speed·dt)`) rather than
a naive lerp, which overshoots and oscillates when a frame stalls.

Gallery is reachable with `ui --gallery` and pairs with `--screenshot`, so a theme change can be
reviewed against the whole vocabulary in one capture. It is a dev surface; nothing links to it.

### Phase C — repaint the four screens into the 1280×800 shell
Header + rail + content + hint bar. Status becomes stat tiles + hero banner. Add game becomes cards.
Launch setup becomes a mono-font command block with a real Copy pill.

**Gate:** side-by-side screenshot against the agent UI. Same palette, same information hierarchy.

**✅ Phase C complete (2026-07-25).** Shell is header + rail + content + hint bar; all four screens
repainted. `Ui/Art.cs` added — a baseline PNG **decoder** (the encoder in `Screenshot.cs` only
writes), so embedded artwork can become GL textures. Header mark is a 96 px re-export at
`packaging/linux/artwork/dist/ui/logo-96.png`, linked from the csproj rather than copied so there is
one master. This also unlocks the hero banner and per-game cover art later.

⚠️ **ImGui child windows ignore `WindowPadding` unless they have a border or
`ImGuiChildFlags.AlwaysUseWindowPadding`.** All four shell children rendered flush to the window
edges — stat tiles ended exactly at 1280 px, the server chip and version string were clipped. This
is the single easiest way to break this layout; check it first if content touches an edge.

Dev-loop additions, all verified: `--screen <name>` opens any screen, `--fixtures` reuses the Linux
harness's `make-fixtures.py` so **populated** screens render (a dev box has no Proton prefixes and no
enrolment, so without it Add game and Set save folder only ever show empty/gated states — which is
exactly where the layout bugs were), `--autoscan` starts the scan unattended, and capture now waits
for pending work plus a settle window before reading the framebuffer.

### Phase D — content parity
Settings screen, tracked-games list with remove, autostart toggle, settle-seconds stepper, version
string, **and the `lease-warnings.json` store per the §4 decision** (daemon writer + both readers +
24 h staleness + write-through dismissal).

**Gate:** walk the §4 table; every ❌ is either ✅ or explicitly deferred in writing. Lease warning
raised on machine A appears on the Deck UI, is dismissed there, and does not reappear in the agent
UI at `localhost:5178`.

**✅ Phase D complete (2026-07-25).** `Ui/SettingsScreen.cs` + `Agent.Core/LeaseWarningStore.cs`.
Every ❌ in §4 is now ✅. Connection fields are read-only (no keyboard in Game Mode) and point at
Desktop Mode; settle-seconds is a stepper, auto-start a toggle that reflects what systemd actually
did rather than what was asked, and game removal marks rows and needs a second press — on a gamepad
an accidental A is easy and untracking is not recoverable from that screen.

> 🔴 **The lease-warning work was larger than "surface it in the UI".** `ProtonRun.cs:56` called
> `OnGameLaunchAsync` and **discarded its result**. On Linux the launch wrapper is a separate,
> short-lived process from the daemon, so a warning it raised reached no UI at all — the only trace
> was an `Alert` line in a console log that Game Mode never displays. **A Deck user who launched a
> game another machine had checked out was never told, anywhere.** The store fixes the feature, not
> just its presentation, and the same file now backs the Windows agent UI so a daemon restart no
> longer drops an unseen conflict notice.

Two layout defects the fixture screenshots caught, both now fixed in `Widgets`:
- **`ImDrawList.AddText` neither wraps nor clips.** A long save path ran straight off the window.
  `ListRow` measures its trailing furniture first and elides into the remainder; **subtitles elide in
  the middle**, because the leaf folder identifies the game and the `compatdata/pfx/drive_c` middle
  does not. Any hand-painted widget drawing variable-length text needs this.
- A wrapped banner body claimed the full width, pushing the dismiss button out of the banner
  entirely. Reserve the control's width before wrapping, not after.

### Phase E — motion
A tween helper keyed by `ImGuiID` (`float Tween(id, target, speed)`), lerped on frame delta. Apply
to: nav row active-fill, button hover/press scale, screen cross-fade, scan spinner, banner slide-in.

**Frame pacing: raise the flat cap from 30 to 60 fps** (`UiApp.cs:90-91` — both `FramesPerSecond`
and `UpdatesPerSecond`). Keep VSync on. Per §0.1 the dual-cap is **explicitly not being built** —
do not add tween-aware pacing.

**Gate:** on the Deck — animations are smooth at 60, and no thermal or drain surprise over a
realistic ~15 minute configuration session.

**✅ Phase E built (2026-07-25) — motion is UNVERIFIED IN MOTION, see below.**
Most of Phase E had already landed: the tween helper was wired into every interactive widget in
Phase B, and the 60 fps cap in Phase A. This added the screen cross-fade (ease-out cubic, 160 ms)
and the banner entrance (fade + settle down).

⚠️ **The enabling fix, and the trap for anyone adding animation here: `PushStyleVar(Alpha)` does
nothing to hand-painted widgets.** ImGui applies `style.Alpha` inside its *own* widget code, but
everything in `Widgets.cs` paints through `ImDrawList` with explicit colours ImGui never touches — so
a fade would silently be a no-op across the majority of this UI. `Widgets.U32` now folds `Alpha` in,
and `Icons` routes through it. **Any new hand-painted colour must go through `Widgets.U32`**, not
`ImGui.ColorConvertFloat4ToU32`, or it will not fade with everything else.

Screenshot capture treats an in-progress fade as busy, so an unattended capture cannot land on a
dim, offset frame — verified: steady-state captures are full opacity and complete in ~2.5 s, which
also proves the ramp reaches 1 rather than stalling.

🔴 **What is NOT verified: the animations as animations.** A settled screenshot cannot show a
transition, and the WSLg loop is capture-based. The fade is verified to *terminate* and not to
regress steady state; whether it *feels* right is a judgement that needs someone watching it, either
interactively under WSLg or on the Deck. Treat this as the first thing to eyeball on device.

---

## 7. Verification

### 7.1 WSLg as the primary visual loop — build this FIRST, in Phase A

On-hardware testing is vital but tedious. WSLg (Ubuntu 24.04 under WSL already has a Wayland/X
display server and a GL stack via Mesa + d3d12) can run `savelocker ui` in a real window on the dev
box. **Every Phase A–C iteration should happen here**, with the Deck reserved for what only hardware
can prove.

**Deliverable: `tests/linux/run-ui-wslg.sh`** — one command that builds and launches the UI in WSL.
It must:
- export `DOTNET_ROOT` / `PATH` (they are not on a non-interactive PATH — `CONTEXT.md`)
- build with `--no-incremental`
- force a 1280×800 window so what you see matches the Deck **exactly** — this is the whole point.
  Add a `--size WxH` / `SAVELOCKER_UI_SIZE` override to `UiApp` rather than hardcoding, so the same
  binary can be windowed on a dev box and native on the Deck
- print a clear line if no display is available, rather than dying in a native SDL stack trace

**What WSLg validates:** layout, palette, typography, spacing, art, widget states, animation feel.
Keyboard nav works (ImGui's keyboard nav shares the focus system with gamepad nav), so the *shape*
of navigation is testable — flow, focus order, dead ends.

**What WSLg does NOT validate — Deck only, no exceptions:**
- gamepad input via Steam Input (the `ButtonDown`-event workaround in `HookPad` exists precisely
  because this behaves differently on real hardware — see `Gotchas.md`)
- gamescope compositing, and the on-screen-keyboard suppression `StopTextInput` handles
- clipboard crossing into Steam's Launch Options field
- real font legibility at arm's length on a 7" panel — a 1280×800 window on a 27" monitor is
  **not** the same angular size, and this is the single easiest thing to get wrong
- thermals and battery

⚠️ Expect Mesa/d3d12 under WSLg to differ from the Deck's native Mesa on GL edge cases. Treat a
WSLg-only rendering artifact as suspect until reproduced on hardware, and vice versa.

- **Gamepad, gamescope, and final sign-off: the real Deck.** Push the branch, take the CI
  tarball artifact (stamped `9.9.9-ci`, kept 14 days), re-run `install.sh`. Still required at the
  end of Phase C and Phase E — WSLg reduces the number of Deck trips, it does not remove them.
- **Size gate:** measure uncompressed tarball delta before and after. Must stay within the §0 budget
  and the overall single-digit-MB §3.1 gate.

  **✅ Measured 2026-07-25** — clean A/B, both built in WSL Ubuntu 24.04 (same glibc as CI):

  | | compressed | uncompressed |
  |---|---|---|
  | `main` (e2d9256) | 54,752,239 | 124,006,399 |
  | `deck-ui-visual-refresh` | 55,331,108 | 125,195,375 |
  | **delta** | **+565 KB** | **+1.13 MB** |

  Inside the ≤1.5 MB budget, and it reconciles against what was added: fonts 1,079 KB + logo 9 KB +
  ~100 KB of IL. ⚠️ Do **not** A/B against the `artifacts/linux/savelocker-9.9.9-ci-*` tarball dated
  2026-07-23 — it predates Phase 3 and contains no ImGui or SDL at all, so it overstates the delta
  by roughly 5 MB.
- **Regression:** `bash tests/linux/run-linux-tests.sh` (27 checks) must still pass — the UI touches
  `Enroller` and `LinuxGameScanner`, which that suite covers.
- **Headless check:** confirm `savelocker daemon` still starts on a box with no GL — the on-demand
  native-lib load must not have been broken by moving font/texture loading around.

---

## 7.2 Interface sounds (added 2026-07-25, outside the original spec)

`Ui/Sound.cs` + `Ui/Wav.cs`. Two sources, in order:

1. **SteamOS's own Game Mode sounds**, read from the user's existing Steam install
   (`{steamRoot}/steamui/sounds/deck_ui_*.wav`). These are **Valve's assets and are deliberately not
   bundled** — reading files already on the machine is not redistribution. When present the UI
   sounds like the rest of Game Mode, which is the point.
2. **Synthesised oscillator blips** otherwise — zero bytes, no licensing question. Fallback is
   **per cue**, so a renamed Valve file degrades one sound rather than all four.

Playback rides the SDL already loaded for windowing and input: no new dependency. Mute is
`AgentConfig.UiSoundsMuted`, toggled in Settings.

Verified in WSLg (needed `libpulse0` installed — a stock WSL image has no audio backend at all,
which SDL reports as a bare init failure):
- SDL opens a 48 kHz stereo device ✅
- no Steam present → `Source: synthesised` ✅
- fixtures plant 44.1 kHz **mono** WAVs → `Source: SteamOS` ✅, which proves decode + resample +
  channel fan-out. The fixture WAVs are written by Python's `wave` module so the test cannot pass by
  sharing a bug with our own writer.
- no audio backend → clear message, UI still starts ✅

🔴 **Not verified: that it is audible, or that the cues fire on the right interactions.**
Both need a person listening. First thing to check on device.

## 8. Docs to update on completion

- `Decisions.md` §2 — amend the "will never look like the React UI" line; record the lease-warning
  persistence decision (§4) and the relaxed battery constraint (§0.1) so neither resurfaces.
- `Gotchas.md` — anything learned about ImGui font atlases, sRGB, or WSLg GL.
- `CONTEXT.md` dev quick-reference — add the `run-ui-wslg.sh` row.
- `REPO_MAP.md` — `src/Agent.Linux/Ui/` gains `Theme.cs`, `Widgets.cs`, `Fonts/`; note
  `lease-warnings.json` in the runtime config-paths table.
- `CONTEXT.md` — status row + next action.
- `Backlog.md` — per-game cover art as a follow-up.
- `web/src/help/` — refresh any Deck screenshots that go stale.
- Move this file to `SaveLocker/logs/` with a date prefix.
