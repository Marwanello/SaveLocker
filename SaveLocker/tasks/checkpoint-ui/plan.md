# Checkpoint — UI design spec

The agreed visual direction for the console, the agent window and the Deck UI. This file is the
*what*; [[implementation]] in this folder is the *how* and the phasing.

Interactive prototype: <https://claude.ai/code/artifact/b8f247f2-32e5-4808-8e4c-61ba0cc3406f>
(Console / Agent / Deck-Wayland / Notifications / Marks & art / Flows, light + dark, five accents.)

Brand kit: <https://claude.ai/code/artifact/b3e0c8a5-70a0-47bf-b4f2-d0dbf4f0b2d5> — also committed
beside this file as `brand-kit.html`, so it survives without the link.

## The idea in one line

Checkpoint dresses SaveLocker as something you own rather than something you administer: soft black
or warm bone, one accent that only ever means "a decision is waiting", and rows that put the name on
one line and everything else underneath.

## Decisions already taken

| Decision | Value | Why |
|---|---|---|
| Direction | Checkpoint (of five pitched) | Reads as a consumer app; keeps the density where it is needed |
| Typeface | **Archivo** for headings *and* data | Maintainer's pick; tabular figures replace the old monospace columns |
| Monospace | Only for code blocks, CLI output and log excerpts | Mono is explicitly out as the house data face |
| Accent | Ember `#e0533c` dark / `#c0432c` light, user-changeable | Ember is also the brand colour, so it can never decorate |
| Themes | Light and dark, both first-class | Separate palettes, not an inversion |
| Marks | Cartridge, Pixel lock, Memory card — user picks | All three approved; Pixel lock is the default |
| Steam art | Approved as drawn in the prototype | Capsule / wide / header / hero all derive from the chosen mark |
| Decky plugin | **Untouched** | Its Steam-native look is correct; this redesign does not apply to it |

## Tokens

Defined once, consumed everywhere. In `web/src/index.css` they become a Tailwind v4 `@theme` block;
the agent UI imports the same file; the Deck UI mirrors the dark set in `Ui/Theme.cs`.

```
                       dark            light
--ink      page        #0f0f10         #f5f3ef
--panel    card        #141416         #fffefc
--raise    control     #191a1c         #f0ede8
--tile     inset       #1b1c1f         #ebe7e0
--hover                #202124         #e8e3dc
--fg       text        #f0eee9         #191719
--dim      secondary   #a09d97         #57534d
--faint    tertiary    #6b6862         #8b8780
--line     border      #242427         #e2ddd5
--row      table rule  #1d1d20         #eeeae3
--safe     healthy     #7fa96a         #4c7b3e
--watch    warning     #d9a63f         #8a6212
--accent   decision    #e0533c         #c0432c
```

Every soft / border / ink step is derived, never hand-picked:

```css
--accent-soft: color-mix(in oklab, var(--accent) 14%, var(--panel));
--accent-line: color-mix(in oklab, var(--accent) 42%, var(--panel));
--accent-ink:  color-mix(in oklab, var(--accent) 80%, var(--fg));
```

The same three lines apply to `--safe` and `--watch`. Swapping an accent is therefore one variable,
and the five shipped options (Ember, Coolant, Arcade, Cobalt, Stealth) are just `{dark, light,
on-accent}` triples.

### Colour rule

- **Green / olive** — server and machine agree. No action is offered, because none is needed.
- **Amber** — something failed but will retry, and the copy says what happens next. Clears itself
  once that machine syncs the game cleanly again.
- **Accent** — a decision is waiting. Never used for emphasis, never for a healthy state.

An accent change must never make something read as healthy or broken, which is why none of the five
options sit in the green or amber hue ranges.

## Type

| Role | Face | Size / weight |
|---|---|---|
| Page title | Archivo 700 | 27px, `-0.035em` |
| Card title | Archivo 600 | 14px, `-0.02em` |
| Body | Archivo 400 | 13.5px |
| Row name | Archivo 600 | 13px |
| Row subtext, values, paths | Archivo 400 | 10–11.5px, `tabular-nums` |
| Eyebrow / column head | Archivo 400 | 9.5–10px, `0.12em`, uppercase |
| Code, CLI, log excerpts | any mono | 11px |

Numbers use `font-variant-numeric: tabular-nums` wherever they stack in a column — that is what
replaced the monospace data face, and it is not optional.

## Layout rules

- **Two-line rows.** Name on line one, everything else (size, last sync, path) on line two, status
  pinned right and centred across both. A three-column grid with two rows, not a flex row.
- **Games:** sidebar list by default, grid wall as an alternative, switch in the sidebar header.
  List is for working on one game, grid is for finding one.
- **Cards** carry 14px radii, controls 99px (pills), covers 9px. Tiles inset their cover by 8px.
- **Cover art** at 3:4 in the grid, 38px square in rows, 74×98 on the agent's game page.
- **No modals.** Conflicts, pickers, enrollment and resolution expand inline.
- **Destructive controls name their effect**: *Prune 5 versions*, *Force-release lease*, *Keep the
  Deck save*.

## Motion

One curve, `cubic-bezier(.2, .8, .25, 1)`, 160–300ms.

| Thing | Motion |
|---|---|
| Canvas sections | `rise` 260ms, 26ms stagger, first six only |
| Grid tiles | `pop` 300ms, 35ms stagger |
| Rows | 2px translate on hover |
| Buttons | −1px lift on hover, `scale(.96)` on press |
| Toggle knob | 260ms with a slight overshoot |
| Progress | width transition, never a jump |
| Toast | spring in, 2.6s dwell |

All of it disabled under `prefers-reduced-motion`. **Progress updates must not re-render the
surrounding view** — see [[implementation]] §3.

## Voice

Plain, specific, and never loud. "Both copies changed since the last sync. Keep one." A control says
exactly what happens; a toast confirms in the past tense. Errors name the cause first and the fix
second: "Save folder locked by the running game. Retrying in 5 minutes." No exclamation marks, no
"Oops", no passive voice.

## Surfaces

| Surface | Shell | Notes |
|---|---|---|
| Console | Top nav, games sidebar or grid | Bell menu + lock button top right, Sync all primary |
| Agent window | 196px left nav, status header | Overview is quick info only; Games tab owns the detail |
| Deck Game Mode | 236px nav, 62px rows, button legend | Dark only; focus ring is 2px accent + 4px halo |
| Wayland desktop | Header bar instead of browser chrome | Same agent UI otherwise |
| Decky plugin | **Steam's own** | Out of scope, deliberately |
