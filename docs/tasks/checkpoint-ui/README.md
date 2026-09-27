# Checkpoint UI — working folder

Everything for the console / agent / Deck visual redesign lives here. Like
`tasks/conflict-resolution-ui/`, this is **not** an ordinary single-file task
— it's a living, multi-session design doc set for a still-open, 14-phase effort (8 at first; the 2026-09-27 gap audit added 9–14). The design phase
completed 2026-09-02; Group 1 (the `web`-side foundation — tokens, reset, motion primitives, the
`ui/` component library, and a first pass at Phase 8's assets) shipped 2026-09-17, and Group 2 (the
console shell — sidebar rows, games grid, notifications, sign-in, console Sync all, exclude-pattern
chips) shipped 2026-09-18, and Group 3 (the agent's tokens and primitives, the status header with Sync
all and live progress, the trimmed Overview) shipped 2026-09-20, see [[implementation-grouping]]'s
status table for the rest (Groups 4–7 shipped by 2026-09-24; Groups 8–16 were added by the 2026-09-27 gap audit). Don't treat
`plan.md` as "read once, execute its steps, stop" — it's the canonical reference to re-read at the
start of every session that touches this work, and it gets amended in place as phases ship.

| File | What it is | Read it when |
|---|---|---|
| `plan.md` | The canonical design spec — tokens, type, colour rule, motion, layout, voice, per-surface shells | Any session touching the redesign, before doing anything else |
| `implementation.md` | What already exists vs. what doesn't, all 14 phases with the work each needs, and the 2026-09-27 gap audit | After `plan.md`, to see what a phase actually involves |
| `implementation-grouping.md` | Which phases to lump into one session, in what order, and why — regrouped **by surface**, because several phases edit the same components | Before starting any new phase, to decide this session's scope |
| `brand-kit.html` | The brand kit as a standalone page — marks, colour, type, components, motion, Steam art, voice, paste-ready tokens | When building any new UI, in this project or beside it |
| `prototype.html` | The interactive mockup — Console, Agent, Deck/Wayland, Notifications, Marks & art, Flows, both themes, five accents, three marks, all real data | To see the redesign working end to end, or to lift a specific screen's markup while implementing a phase |
| `identity-options.html` | The five identity pitches this direction was chosen from (Cold Storage / Checkpoint / Ledger / Shelter / Hangar) | For the rationale behind picking Checkpoint over the other four |

Open any of the three `.html` files directly in a browser — they're static, no build step or server
needed. They also stay live at these mirrors, which is only useful for sharing a link, not as a
second source of truth: [prototype](https://claude.ai/code/artifact/b8f247f2-32e5-4808-8e4c-61ba0cc3406f) ·
[brand kit](https://claude.ai/code/artifact/b3e0c8a5-70a0-47bf-b4f2-d0dbf4f0b2d5).

**Start here:** `implementation-grouping.md`'s status table, to see what's already shipped. Groups 1–7
are done (Phases 1–7, except Phase 6 item 4). **A gap audit on 2026-09-27** compared the shipped UI with
`prototype.html` screen by screen and found that most *pages* behind the new shell were recoloured, not
re-laid-out — the console's game page, Configuration, Audit log, Help and What's new; the agent's Settings,
Add games and Conflicts; the Deck's rail, header, stats and rows — that the console has **no Backups tab at
all**, and that the Wayland window was never decided. That work is **Phases 9–14** in `implementation.md`
(*The 2026-09-27 audit* lists every gap and, just as important, the differences that are deliberate and must
not be rebuilt) and **Groups 8–16** in `implementation-grouping.md`. Next up: **Group 8** (the console page
kit and top bar — it gates 9–12) or **Group 13** (the agent UI — independent); **Group 16** (assets) fits any
short session. Group 15 (Wayland) waits on the maintainer confirming one of the options written out under
Phase 6 item 4. One older finding is still a maintainer decision rather than a task: `--color-faint` fails
WCAG AA (3.31:1 dark, 3.55:1 light) — see the Group 3 write-up.

**Once every phase ships**, move this whole folder to `docs/logs/` with a date prefix
(e.g. `logs/2026-MM-DD_checkpoint-ui/`), per the normal task-completion convention.
