# Task — Deck UI: fix pane navigation, separator z-order, and motion legibility

Follow-up to `logs/2026-07-25_deck-ui-visual-refresh.md`. That task shipped the visual refresh in
v0.4.0 and left navigation half-working, recorded as a stretch item in `Backlog.md`. On-device
testing (2026-07-25) found the navigation worse than the vault claims and turned up three smaller
defects alongside it.

**This task supersedes the `Backlog.md` stretch item "Deck UI: Left-to-menu navigation".** The
approach recorded there — drawing both panes in the root window and hand-rolling clipping and
scrolling — is **not** what this task does, and should not be attempted. See §1.

---

## 0. Constraints that must survive

- **Do not upgrade ImGui.NET.** 1.91.6.1 was measured and reverted: it did not fix Left and it broke
  the Right cross that works on 1.90.8. Everything in this task works on **1.90.8.1**, which is the
  point. The warning in `Backlog.md` stands unchanged.
- No new NuGet packages. No new native dependencies.
- `Theme.cs` stays the single source of truth for colour, type and metrics. No literal colour
  anywhere else.
- Any new hand-painted colour goes through `Widgets.U32`, never
  `ImGui.ColorConvertFloat4ToU32` — otherwise it will not participate in fades. (Trap from the
  previous task; still live.)
- Nothing here may regress the Right cross out of the rail, or Up/Down staying inside their pane.

---

## 1. The finding that unblocks this

The stretch item's premise — *"ImGui 1.90.8 exposes no public API to set the nav cursor directly"* —
is true of the **managed binding** but **not of the native library already being shipped**.

`libcimgui.so` in ImGui.NET **1.90.8.1** exports the full `imgui_internal` surface. Verified present
in the exact .so that gets published
(`~/.nuget/packages/imgui.net/1.90.8.1/runtimes/linux-x64/native/libcimgui.so`):

```
igSetFocusID   igSetNavID   igFocusWindow   igFindWindowByName
igNavInitWindow   igNavMoveRequestSubmit   igSetNavWindow   igNavMoveRequestCancel
```

The managed `ImGui.NET.dll` binds none of them — in either 1.90.8.1 or 1.91.6.1 it exposes only
`igSetKeyboardFocusHere`, `igSetItemDefaultFocus`, `igSetWindowFocus`. That gap, not an ImGui
limitation, is what blocked the eight attempts.

C prototypes, from cimgui's generated header:

```c
CIMGUI_API void          igSetFocusID(ImGuiID id, ImGuiWindow* window);
CIMGUI_API void          igFocusWindow(ImGuiWindow* window, ImGuiFocusRequestFlags flags);
CIMGUI_API ImGuiWindow*  igGetCurrentWindow(void);
CIMGUI_API ImGuiWindow*  igFindWindowByName(const char* name);
CIMGUI_API void          igSetNavID(ImGuiID id, ImGuiNavLayer nav_layer, ImGuiID focus_scope_id, const ImRect_c rect_rel);
```

**Use `igSetFocusID`.** Called immediately after the target item is submitted, it sets `NavId` and
`NavWindow` and derives the nav rect from the last item — it is the function ImGui uses internally to
place the cursor, and it carries none of `SetKeyboardFocusHere`'s restrictions.

⚠️ **Do not reach for `igSetNavID` first.** It passes `ImRect` by value (4 floats, SysV classes it
into SSE registers). P/Invoke handles that correctly, but it is ABI surface with no upside here.
Keep it as a fallback only if `igSetFocusID` proves insufficient.

### Why the eight previous approaches could not have worked

Two upstream constraints, both documented:

1. **`SetKeyboardFocusHere()` only acts within the active focus scope** —
   [ocornut/imgui#7226](https://github.com/ocornut/imgui/issues/7226). It is implemented as a
   *tabbing* request. No amount of re-asserting, window focusing or source suppression makes it
   cross a scope.
2. **`NavFlattened` is documented "only use on child that have no scrolling."** Every flattened
   child in this UI is a fixed-height scrolling one: `candidates` (`UiApp.cs:981`), Settings' `games`
   (`SettingsScreen.cs:148`), and both halves of `TwoColumn` (`Widgets.cs:912`) — which is where
   Overview's tracked-game list lives. The UI depends on a configuration upstream says not to use,
   which is why behaviour varied per screen instead of failing consistently.

---

## 2. Symptoms and root causes

Device report, 2026-07-25. **Left is dead on every screen**, including Overview — the vault and
`CONTEXT.md` both still claim Overview works. They are stale; Overview's list sits in a scrolling
`TwoColumn` child and is subject to the same constraint. Correct those claims as part of this task.

### 2.1 Left never returns to the rail — any screen
Cause: §1, both constraints. Fix: `igSetFocusID` (Phase 3).

### 2.2 Down at the bottom of the content lands on "Quit" in the rail
**Same root cause, and the most useful thing found.** `UiApp.cs:593`:

```csharp
if (_focusZone != Zone.Rail && !Widgets.FocusRequestPending)
    railFlags |= ImGuiWindowFlags.NoNav;
```

The rail's `NoNav` gate is *suspended* while a focus request is pending, and a request stays armed
for **45 frames** (`FocusRequestLifetimeFrames`, `Widgets.cs:72`). A Left press arms a request that
never lands, so for ~0.75 s both panes are live in one flattened nav scope and ImGui's geometric
scoring finds "Quit" as the best candidate below. The reported sequence — Right, Left (nothing),
Down, Down → Quit — is exactly that window standing open. `_focusZone` is also left out of sync for
its duration, which is why Left/Right stop registering as pane crossings mid-sequence.

Once a request lands in a single frame the 45-frame re-assert is unnecessary and the gate becomes
unconditional. **One fix resolves 2.1, 2.2 and the "must press B to escape" behaviour on Add game,
Steam setup and Settings.**

> **Refined by Phase 1 (observed, not inferred).** The armed request does something worse than open
> the gate: it **eats every directional move for its whole lifetime**. `ClaimFocus` re-asserts
> `SetKeyboardFocusHere()` on the target every frame while the request is armed, and
> `SetKeyboardFocusHere` overrides nav movement — the behaviour `UiApp.cs:324` already documents for
> frame 0. So after a failed Left, the next ~45 frames of D-pad input are silently discarded.
>
> This is what "you have to press Down twice" actually is: the first press lands inside the armed
> window and is thrown away. Verified under WSLg — `--nav right,left,down,down` on Overview ends with
> the cursor still on the item Right landed on, the trail showing only the Right move, and the
> request still armed with 15 frames left. **Two of the three symptom reports in §2.1/§2.2 are this
> one mechanism**, and it is fixed by the same change (request lands in one frame, so nothing is
> swallowed).

### 2.3 The footer is reachable by Down — ✅ ANSWERED by Phase 1
**It is not the footer.** The `hints` child is `NoNav` (`UiApp.cs:713`) and none of its widgets call
`ClaimFocus`; it was never reachable. What is actually happening: when Down runs out of items in the
content pane, **the nav cursor lands on the content child window itself as a nav container**, and
ImGui draws that container's focus highlight — whose bottom edge is a solid accent-green line
spanning the full content width, sitting immediately above the hint bar. That line is what reads as
"the footer is selected, and it has no options in it".

Observed directly (`--screen add --nav right,down,down,down,down,down`, no candidates scanned, so
Down exhausts the pane):

```
zone    Content
focus   <uninstrumented item>      <- ImGui.IsAnyItemFocused() true, no widget reported focus
window  -
request none                       <- a clean state; NOT a side effect of 2.2
```

`IsAnyItemFocused()` true with nothing reporting is the container signature, and the cropped capture
confirms the green rule above the hint bar.

**Fixed in Phase 3, but not by the predicted mechanism.** It did **not** disappear with the hand-off
rewrite, so it needed its own fix — and the container theory turned out to be wrong. A probe was added
(`NavDebug.NoteContainer`, called right after each `EndChild`: a child window that ImGui treats as a
nav container submits itself as an item, so `IsItemFocused` there answers the question through the
public API) and it reported **no** container focused — `container: -` on rail, content, candidates and
both `TwoColumn` halves, while `IsAnyItemFocused()` stayed true. So the cursor was not on a pane; it
was on an id that **no submitted item answers for** — a stale/ghost `NavId`.

That ruled out `NoNavInputs`, which would have been the wrong fix for the wrong cause. What went in
instead is the invariant a gamepad UI actually needs: `UiApp.RecoverStrandedCursor` — *if no submitted
item claimed the cursor this frame, put it back on the last one that did.* On a Deck the focus ring
**is** the cursor, so "focused on nothing" is never a legal state, whatever stranded it: an item that
stopped being submitted, a list that emptied, a screen whose ids all changed. `igSetFocusID` is what
makes it expressible.

Verified: five Downs past the last control on Overview, Add game and Settings now end on
`Steam setup`, `Scan for games` and `toggle:Start SaveLocker on boot` respectively, with
`container: -` and no `<uninstrumented item>`.

### 2.4 The header separator is cut off
`DrawHeader` draws the line on the root window's draw list (`UiApp.cs:572`), but child windows' draw
lists merge **after** the parent's. The rail and content `ChildBg` fills both start at exactly
`y = HeaderHeight` and paint over the 1 px line. It survives only in the 1 px gutter at
`x = RailWidth`, which is precisely the reported "remnants at the tops of the vertical lines".

Fix: draw both separators on the **foreground** draw list after the children are submitted, and
start the children at `HeaderHeight + 1`.

### 2.5 Motion is present but below the noticing threshold
Resolved by device check: the focus ring **does** breathe, but so faintly the maintainer had never
noticed it until asked to look. So the animation plumbing is sound — the loop renders continuously
at 60 with VSync (`UiApp.cs:226`) and `Widgets.U32` folds `Alpha` in correctly. This is a **tuning**
problem, not a wiring one.

`Widgets.FocusRing` (`Widgets.cs:151`) pulses the glow alpha over `0.225 … 0.375` — a 15-point swing
on a soft outer glow, at arm's length, on a 7" panel. Both the amplitude and the travel are too
small to read.

Maintainer's direction: **increase the intensity, or increase the space the ring breathes in** (and
make whatever layout/spacing adjustments that needs). Both levers are in scope.

The 160 ms ease-out cross-fade is likewise unnoticed and is a candidate for lengthening, but it was
never the thing being asked about — treat it as secondary to the ring.

### 2.6 A/B glyphs in the hint bar sit 1–2 px left of centre
New, unrelated to navigation. `Widgets.GamepadHint` (`Widgets.cs:854`) centres the letter with
`dl.AddText(centre - bs / 2f, …)`, where `bs = CalcTextSize(button)`. `CalcTextSize` returns the
glyph's **advance width**, not its ink extent — for "A" and "B" the right-side bearing exceeds the
left, so centring on the advance pushes the ink left. Compounded by `centre` and the resulting text
position both landing on fractional pixels.

Fix: centre on the ink box, or apply a measured per-glyph nudge, and round the final text position to
whole pixels. Verify by zooming a `--screenshot` capture, not by eye.

> **Note on an open question.** The "focus ring legibility at arm's length" item carried over from
> the previous task refers to the **green focus cursor ring** — the thing that shows which control is
> selected — and to the UI's overall type sizes, both of which were scaled from the desktop by
> estimate and have never been judged on hardware. It is *not* about the A/B hint circles (that is
> 2.6, a separate defect). **Still unanswered:** at normal Deck holding distance, is the green
> selection ring unmistakable, and is the body text comfortable? Answer this while verifying Phase 5.

---

## 3. Phases

### Phase 1 — nav debug overlay — ✅ BUILT, GATE MET (2026-07-25)
`savelocker ui --nav-debug`: a corner overlay printing, per frame, the focused item id, the owning
window's name, `_focusZone`, and whether a focus request is armed and for how many more frames.

**Built as `Ui/NavDebug.cs`.** Draws on the foreground draw list and submits no window and no items,
so it cannot perturb the layout or nav it is measuring. Widgets report through the existing
`Feedback` funnel (one added `label` argument); container scopes are pushed beside each `BeginChild`
so the overlay can name the child that owns the cursor. Also wired into `run-ui-wslg.sh --nav-debug`,
so it composes with `--nav` and `--screenshot` for unattended evidence.

Two design points worth keeping: it reports `ImGui.IsAnyItemFocused()` alongside our own bookkeeping —
the disagreement between them is what identified 2.3 — and it deliberately bypasses `Widgets.U32`,
because an instrument that fades with the screen cross-fade is blind exactly when a transition is
what you are watching.

**Findings:** the desync in 2.1 (`zone: Rail` while the cursor sat in `content > banner`), the
move-eating mechanism now recorded in 2.2, and the container-highlight answer to 2.3. All three were
guesses from source before this existed.

Every diagnosis above except 2.4, 2.5 and 2.6 rests on inference from source. This replaces that
with observation, and it is what settles 2.3. It also stays useful afterwards — this is the third
task to touch this navigation code.

**Gate — ✅ met.** Under WSLg, `--fixtures --nav-debug --nav right,left --screenshot` reproduces the
dead Left and the overlay shows `request 0x2CFAC09D  27 frames left` with
`gate BOTH PANES NAVIGABLE (request armed)` while the cursor is still in `content > banner`. 2.2
confirmed directly rather than argued.

### Phase 2 — `Ui/ImGuiInternal.cs` — ✅ BUILT, GATE MET (2026-07-25)
`[DllImport("cimgui")]` shims for `igSetFocusID`, `igGetCurrentWindow`, `igFindWindowByName`,
`igFocusWindow`. `ImGuiWindow*` stays an opaque `IntPtr` — nothing here needs to read the struct.

The library name **must** be `"cimgui"`, matching what ImGui.NET binds, so the already-loaded module
is reused rather than a second copy mapped.

⚠️ **Guard the entry points.** Resolve them once behind a `try`/`catch (EntryPointNotFoundException)`
(or `NativeLibrary.TryGetExport`) and fall back to today's `SetKeyboardFocusHere` path if absent. A
Deck must degrade to the current half-working navigation, never crash on a missing symbol. Log the
fallback once so `doctor` output can say which path is live.

**Gate — ✅ met, all three shapes.** `ui --nav-debug` prints the probe result **before the window
opens**, so it is readable from a run that cannot open a display — which is how this gets checked on
a Deck over SSH.

| Shape | Result |
|-------|--------|
| Dev build (`-r linux-x64 --no-self-contained`) | `nav api: cimgui internal nav API available (4 exports)` |
| **Self-contained `-r linux-x64` publish** (what ships) | same; `libcimgui.so` present in the publish output |
| Guard tripped (bogus symbol patched into `RequiredExports`) | `nav api: cimgui lacks igNoSuchExportXyz — using SetKeyboardFocusHere fallback`, UI still runs and captures |

The third row matters as much as the first two: a fallback nobody has exercised is not a fallback. It
was tested by patching a bogus name into the WSL copy only, then restoring — no test scaffolding
shipped.

**Deliberately not wired into `doctor`.** `ImGuiInternal.Status` exists for it, but calling it from
`doctor` would load `libcimgui.so` in a headless process, and `Program.cs` keeps native SDL/GL/ImGui
loading confined to the `ui` command on purpose (a daemon never touches a GPU). Wire it only if
`doctor` gains a UI section that already pays that cost.

### Phase 3 — rewire the hand-off on `igSetFocusID` — ✅ BUILT, GATE MET (2026-07-25)

**Two things the plan did not anticipate.**

**(a) The request had to move from before the item to after it.** `ClaimFocus` is called *before* a
widget submits — right for `SetKeyboardFocusHere`, which targets the item about to be submitted, and
useless for `igSetFocusID`, which derives the nav rect *from* the submitted item. So serving a request
moved into `Feedback` (which every widget already calls immediately after its `InvisibleButton`), and
`ClaimFocus` now only acts on the fallback path.

**(b) B stopped working, and it turned out B was never implemented.** There is no B handler anywhere
in the UI — "Back" was purely ImGui's built-in `NavCancel`, working by accident. With the cursor
app-placed, `RecoverStrandedCursor` restores whatever `NavCancel` clears in the same frame, so B went
dead. B is now an explicit crossing alongside Left.

That also exposed a **shipped bug in v0.4.0 nobody had noticed**: the hint bar promises "Cancel" on
Set save folder, but `NavCancel` only ever moved the nav cursor and left the screen up — the on-screen
Cancel button was the only way out. B now genuinely leaves that screen.

⚠️ **The SetFolder branch of that is UNVERIFIED.** `--screen folder --autoscan` under the fixtures
lands on Add game, not Set save folder (no candidate needing a folder), so the B-cancel-from-SetFolder
path was never actually entered under test. The code mirrors the existing Cancel button, but treat it
as untested — check it on the Deck.

- `Widgets.ClaimFocus` calls `igSetFocusID(id, igGetCurrentWindow())` for the requested id
  immediately after that item is submitted, in place of `SetKeyboardFocusHere`.
- `FocusRequestLifetimeFrames` 45 → 1. The request lands the frame it is made; it no longer needs to
  survive.
- Make the `NoNav` gates on both panes **unconditional** (`UiApp.cs:593`, `UiApp.cs:653`) — drop the
  `&& !Widgets.FocusRequestPending` suspension that 2.2 depends on.
- **Delete** `BeginHandoffSource` / `EndHandoffSource` (`UiApp.cs:483-515`), `_handoffActive`,
  `_savedDisabledAlpha`, and the `_focusWindow` / `SetWindowFocus` machinery
  (`UiApp.cs:80`, `368-371`, `470`, `480`). All of it exists solely to work around the primitive
  Phase 2 provides. Leaving it in place risks it fighting the new path.
- Keep `BeginFocusScan`/`EndFocusScan` and `_bestContentId` — the "topmost control in the pane"
  choice is still ours to make, and `_pendingCrossFrames` still covers crossing into a pane whose
  content has not been measured yet (mid-scan).

**Gate — ✅ met.** Scripted under WSLg with `--fixtures --nav-debug`, reading the overlay out of each
capture. Populated fixtures matter: an empty Overview does not exercise the flattened scrolling
children that cause the bug.

| Case | Result |
|------|--------|
| `right,left` on **Overview** | cursor → `rail:Overview`, window `rail` |
| `right,left` on **Add game** | cursor → `rail:Add game` |
| `right,left` on **Steam setup** | cursor → `rail:Steam setup` |
| `right,left` on **Settings** | cursor → `rail:Settings` |
| `right` (regression guard) | still lands on the pane's topmost control |
| `right,down×5` on Overview | walks `Hollow Knight → Hades → Add a game → Steam setup`, crossing `status.l` into `status.r`; **never a rail entry, never Quit** |
| `right,down×5` on Add game / Settings | stays in the content pane; ends on a real control |
| `right,left,down,right` (full circuit) | out, back to rail, down the rail, back in — every press registered, none eaten |
| `right,b` | cursor → `rail:Overview`, zone `Rail` |

In every case `request: none` and `gate: one pane navigable` — the hand-off lands in one frame, so
the both-panes-live window that caused 2.2 no longer exists at all.

Regression: `tests/linux/run-linux-tests.sh` — **33 passed, 0 failed**.

**✅ VERIFIED ON THE DECK (2026-07-25, `9.9.9-ci.navfix`).** Left returns to the rail from all four
screens; Down at the bottom of a screen stays put and no longer reaches Quit; B backs out everywhere.
This is the first time Steam Input has confirmed it — the WSLg loop cannot.

Two amendments came out of that session, below: the footer flash (accepted) and B in the folder
browser (changed).

#### Accepted quirk — a one-frame green flash on the hint bar
Pressing Down at the bottom of a screen flashes the hint bar green for a split second. That is
`RecoverStrandedCursor` working: ImGui strands the cursor for exactly the frame before the recovery
request lands, and the stranded-state highlight is drawn that frame. **The maintainer accepted this
explicitly and does not want it pursued** — the confusing part (a pane-sized highlight that persists,
reading as a selectable footer with nothing in it) is gone, and a single-frame blink does not mislead.
Closing it would mean placing the cursor before the frame is drawn rather than after, which is a
larger change than the symptom is worth.

#### Changed — B in the folder browser is "up a directory", not "cancel"
Deck feedback: inside a file tree, B reads as *back up a level*, and cancelling is already reachable
two other ways (the on-screen Cancel button, or Left out to the rail), so spending B on a third exit
wasted it.

`ResolvePaneCrossing` now climbs to `_listing.Parent` while there is a parent, and only leaves the
screen when already at the browse roots — where there is nothing above to climb to, so B would
otherwise be a dead button.

This also required hardening `RecoverStrandedCursor`: browsing into another folder retires every row
id, so the last-good id can be *gone* rather than merely unfocused, and re-requesting it every frame
could never land. It now falls back to the pane anchor on the second consecutive stranded frame.

⚠️ **Both of these are built and WSLg-clean but NOT yet Deck-verified.**

### Phase 4 — separator z-order — ✅ DONE (2026-07-25)
All three shell separators (header rule, rail divider, hint-bar rule) now draw in one place —
`UiApp.DrawSeparators`, on the **foreground** draw list, after every pane is submitted. Each used to
be drawn on the root window's list as its own pane was built, which put it under the panes.

Children did **not** need to start 1 px lower: drawing last puts the rule on top of the pane
backgrounds, which is where a separator belongs, and avoids opening a 1 px gap.

**Gate — ✅ met.** A 4× crop along the full width at y = 54…76 shows the header rule continuous edge
to edge, meeting the rail divider cleanly at the T-junction. Confirmed independently by the
maintainer against a fresh capture.

### Phase 5 — ring legibility and motion

#### Phase 5.0 — clearance budget FIRST (blocker, added 2026-07-25 from Deck feedback)

**The ring is already being clipped today, before any increase.** Maintainer: widgets sitting close to
the boundary between panes "look perfect, until you highlight them and the breathe effect starts —
that ring then gets cut off". So the ring cannot simply be made bigger; the space it needs has to
exist first.

**Measured, and confirmed against captures.** Current `Widgets.FocusRing` geometry:

| Part | Rect | Thickness | Extent beyond the widget |
|------|------|-----------|--------------------------|
| Outer glow | `min-4 … max+4` | 3 px | **5.5 px** |
| Solid edge | `min-1 … max+1` | 2 px | 2 px |

A widget therefore needs **≥6 px of clear space on every side** just for today's ring.

What actually clips it is each child window's clip rect, and **ImGui gives a child `WindowPadding`
only when it has `Border` or `AlwaysUseWindowPadding` — otherwise its padding is zero**:

| Child | Flags | Horizontal padding | Verdict |
|-------|-------|--------------------|---------|
| `content` | `AlwaysUseWindowPadding` | `Gutter` = 24 px | fine |
| `rail` | `AlwaysUseWindowPadding` | `Space.Sm` = 8 px | tight — survives 5.5, fails any increase |
| `BeginCard` | `Border` | `Space.Lg` = 20 px | fine |
| `banner` | `Border` | `Space.Md` = 14 px | fine |
| **`candidates`** (`UiApp.cs`) | `None` | **0 px** | **clipped** |
| **`games`** (`SettingsScreen.cs`) | `None` | **0 px** | **clipped** |
| **`TwoColumn` `l`/`r`** (`Widgets.cs`) | `None` | **0 px** | **clipped** |

The zero-padding three are exactly where the reported checkboxes and rows live. It is worse than
padding alone suggests, because `ListRow` and `CheckRow` size themselves to
`GetContentRegionAvail().X` — a full-width row in a zero-padding child touches both child edges, so
its horizontal glow is **entirely outside the clip rect**.

Confirmed on a 5× crop of the focused `Hollow Knight` row in Settings: the glow band is visible below
the row (vertical space is available mid-list) and **absent on both left and right**, cut flush at the
child boundary. That is the reported symptom, and it is present in shipped v0.4.0.

**So, in order:**
1. Give the three zero-padding children real padding (`AlwaysUseWindowPadding`, or an explicit push)
   sized to the *new* ring extent, which insets their full-width rows automatically. Watch the
   scrollbar: padding plus scrollbar must not squeeze the row text.
2. Raise the rail's horizontal padding from 8 px to at least the new extent.
3. Check vertical clearance too — the first and last row of a list need top/bottom room, which
   `ItemSpacing` (10 px) currently supplies between rows but not at the ends.
4. **Only then** pick the new glow numbers, and record the chosen extent beside `FocusRing` as the
   clearance every focusable container must honour, so the next widget added near an edge does not
   quietly reintroduce this.

**Gate — ✅ met.** `Theme.Layout.FocusClearance = 10 px` is now the documented contract, and
`candidates`, `games` and both `TwoColumn` halves take `AlwaysUseWindowPadding` with that padding. The
rail's horizontal padding went 8 px → 10 px for the same reason (its entries are full-width, so the
padding is the only room the ring has). A 5× crop of the focused Settings row shows the glow band
continuous through the corners on both sides — the same crop that showed it cut flush before.

### Phase 5 — ring, motion, glyphs — ✅ DONE (2026-07-25)

**The ring breathes by SIZE, not only opacity.** The old version pulsed alpha alone over 0.15–0.30 on
a 3 px band; it measured as present and read as absent. Now the glow band moves in and out between
4 px and 8 px at 4 px thick, with alpha 0.40–0.70, on a ~2 s cycle (slower than the old 4 rad/s — a
fast pulse on a wider band reads as a flicker rather than as breathing). Outermost extent is
`GlowSpreadMax + GlowThickness / 2` = **10 px**, which is exactly `FocusClearance`; the relationship
is commented at both ends so neither can be changed alone.

**Cross-fade 0.16 s → 0.30 s.** Ease-out cubic spends most of its time near full opacity, so at 160 ms
the visible part was roughly 80 ms — the same "measured as working, never noticed" failure as the ring.

**A/B glyphs centre on their INK box** (`Widgets.GlyphCentre`). `CalcTextSize` returns the *advance*
width including side bearings, and for "A" and "B" the right bearing is larger, so centring on the
advance pushed the letter left — the reported pixel-or-two skew. Now uses the glyph's own X0/X1/Y0/Y1
and rounds to whole pixels (a half-pixel text origin resamples the glyph and reads as smeared at this
size). Falls back to the advance approximation for multi-character labels or an unbaked font.

**Verified:** layout intact across screens, `run-linux-tests.sh` 33/33, and the full Phase 3 nav matrix
still passes after the padding changes.

🔴 **Deck-only judgements still open:** whether the ring's breathing is now noticeable without being
distracting, whether the longer cross-fade reads well, whether the glyphs look centred, and the
outstanding §2 question — ring and body-type legibility at arm's length. Plus the two unverified
behaviours from Phase 3 (B up-a-directory, and the roots-level exit).

Per 2.5. Raise the pulse amplitude and/or the ring's breathing room in `Widgets.FocusRing`, adjusting
surrounding spacing if the ring needs more room. Then consider lengthening the cross-fade.

Also 2.6: recentre the A/B glyphs.

**Gate:** on the Deck, at normal holding distance — the selection ring is unmistakable at a glance
and the pulse is noticeable without being distracting. Also answer the open type/ring-size question
in §2. This gate is a human judgement and cannot be closed under WSLg.

---

## 4. Verification

`tests/linux/run-ui-wslg.sh` drives scripted D-pad input through the same queue the pad writes to, so
the whole Phase 3 matrix is testable on the dev box. Use `--fixtures` — an empty Overview and an
empty discovered-games list do not exercise the flattened scrolling children that cause the bug.

Needs `-r linux-x64` (the script does this) or SDL reports "isn't applicable" — see `Gotchas.md`.

**Deck-only, no exceptions:** gamepad input via Steam Input (the `ButtonDown` workaround in `HookPad`
exists because this differs on hardware), gamescope compositing, and every judgement in Phase 5.

Regression suites: `tests/linux/run-linux-tests.sh` (27 checks). The UI is not covered by them, but
`Agent.Linux` must still build and pass.

---

## 5. Docs to update on completion

- `Backlog.md` — **remove** the "Stretch — Deck UI: Left-to-menu navigation" section; its premise is
  wrong and its recommended approach must not be attempted. Keep the ImGui.NET upgrade warning,
  reworded so it does not read as blocking this fix.
- `CONTEXT.md` — the "Known issue shipped knowingly" note claims Left works from Overview. It does
  not. Replace with the outcome.
- `Gotchas.md` — two entries worth their own lines: (a) ImGui.NET's managed binding omits the
  `imgui_internal` API that the native `libcimgui.so` it ships **does** export, reachable by
  `DllImport("cimgui")`; (b) `NavFlattened` on a **scrolling** child is documented-unsupported and
  produces exactly this per-screen-divergent nav behaviour.
- `logs/` — archive this file as `logs/2026-07-2x_deck-ui-navigation-fix.md` with what the overlay
  actually showed for 2.3.
