# Decky plugin: QAM left-stick scrolling — plan

Planned 2026-10-14. Not started. The code lives in the Decky plugin repo
(`D:\Projects\SaveLocker\SaveLocker-Decky`, `Marwanello/SaveLocker-Decky`), not in this repo — this
plan is recorded here beside its backlog entry and should be carried over (or linked) when the work
starts there.

## The complaint

In Steam's QAM the left stick scrolls by **moving focus**, so the stick steps between focus targets
rather than scrolling freely. `src/shared.tsx`'s own `ReadOnlyRow` doc comment records the same
mechanism ("a block of them is a hole the D-pad skips, and scrolling up into it jumps to the back
button"). The backlog entry says `doctor` output is one focus target per line, which makes the panel
steppy — cosmetic, only noticed after running doctor.

## Finding — the premise moved since the entry was written (2026-09-08)

Verified against the plugin's `main` (`44fefea`):

- **`doctor` output no longer renders in the QAM at all.** PR #1 moved it to the full-screen page
  (`src/fullPage.tsx` → Diagnostics section, lines 490–507), where each output line is a plain
  `div` — not a focus target, so no D-pad stops at all there.
- **The many-stop output block that remains in the QAM is the sync result** (`src/index.tsx:483–500`):
  one `ReadOnlyRow` label row plus up to 12 `ReadOnlyRow` lines of `result.output`, each its own
  focus stop — about 13 stops after every sync, exactly the "one target per line" shape the entry
  describes.

So the entry's target has moved but the experience it describes is still real, at the sync result.

## Options

| Option | What | Verdict |
|---|---|---|
| A. Collapse the sync output behind a "Show output" control | One extra focus stop replaces 12 | **Recommended** — same lever the entry asks for, applied to the block that actually has the problem today |
| B. Collapse the whole result to one summary row | Lose the agent's own words | Rejected — `index.tsx:491` explains why the raw output is shown ("a refusal explains itself; paraphrasing would lose the reason") |
| C. Close the item as obsolete | Nothing to do — doctor already left the QAM | Rejected as stated — the 13-stop block remains; would leave the actual complaint live under a closed ticket |

## Design (option A)

In `src/index.tsx`'s `Sync` component (~lines 440–503), the only code change:

- Keep the outcome row: `{label} — ok / exit N`.
- Replace the 12 line rows with a single toggle control — a `ButtonItem` (already imported) reading
  `Show output · N lines` / `Hide output`. Expanded, render the same `ReadOnlyRow` lines, still cut
  to the last 12 with the existing truncation behaviour.
- New `useState(false)` in `Sync`; **auto-collapse when a new sync starts** so the panel never
  re-grows its stops unexpectedly.
- **Default expanded when `exitCode !== 0`** (recommended): a failed sync's reason is the point of
  the panel; an OK sync's detail is optional reading.

## Files

- `src/index.tsx` — `Sync` component only.
- `docs/session_summary.md` (in the Decky repo) — its QAM description (lines 8–13) should note the
  collapsed output when this ships.

## Verification

- `npm run build` in the Decky repo (rollup + strict `tsc`; the plugin has no unit test runner).
- On the Deck (or any QAM visible in Desktop Mode): run a sync, then count D-pad downs past the
  result row — collapsed it must be ≤ 2 stops; expand, scroll the lines, collapse; repeat with a
  sync that fails and confirm the output is already open.

## Open questions

- Default-expanded-on-failure: recommend yes; trivial to reverse.
- Implementation happens in the Decky repo (own branch/worktree there); the plugin's own release
  pipeline carries it (not part of this item).
