# Checkpoint UI — working folder

Everything for the console / agent / Deck visual redesign lives here. Like
`tasks/conflict-resolution-ui/`, this is **not** an ordinary single-file task
— it's a living, multi-session design doc set for a still-open, 8-phase effort. The design phase
completed 2026-09-02; Group 1 (the `web`-side foundation — tokens, reset, motion primitives, the
`ui/` component library, and a first pass at Phase 8's assets) shipped 2026-09-17, see
[[implementation-grouping]]'s status table for the rest. Don't treat `plan.md` as "read once, execute
its steps, stop" — it's the canonical reference to re-read at the start of every session that touches
this work, and it gets amended in place as phases ship.

| File | What it is | Read it when |
|---|---|---|
| `plan.md` | The canonical design spec — tokens, type, colour rule, motion, layout, voice, per-surface shells | Any session touching the redesign, before doing anything else |
| `implementation.md` | What already exists vs. what doesn't, and all 8 phases with the work each needs | After `plan.md`, to see what a phase actually involves |
| `implementation-grouping.md` | Which phases to lump into one session, in what order, and why — regrouped **by surface**, because several phases edit the same components | Before starting any new phase, to decide this session's scope |
| `brand-kit.html` | The brand kit as a standalone page — marks, colour, type, components, motion, Steam art, voice, paste-ready tokens | When building any new UI, in this project or beside it |
| `prototype.html` | The interactive mockup — Console, Agent, Deck/Wayland, Notifications, Marks & art, Flows, both themes, five accents, three marks, all real data | To see the redesign working end to end, or to lift a specific screen's markup while implementing a phase |
| `identity-options.html` | The five identity pitches this direction was chosen from (Cold Storage / Checkpoint / Ledger / Shelter / Hangar) | For the rationale behind picking Checkpoint over the other four |

Open any of the three `.html` files directly in a browser — they're static, no build step or server
needed. They also stay live at these mirrors, which is only useful for sharing a link, not as a
second source of truth: [prototype](https://claude.ai/code/artifact/b8f247f2-32e5-4808-8e4c-61ba0cc3406f) ·
[brand kit](https://claude.ai/code/artifact/b3e0c8a5-70a0-47bf-b4f2-d0dbf4f0b2d5).

**Start here:** `implementation-grouping.md`'s status table, to see what's already shipped. Group 1
is done; Group 2 (console shell) and Group 3 (agent foundation) are next and independent of each
other — Group 1 already made the one decision (how `agent-ui` receives the design tokens: a plain
CSS file imported the same way `main.tsx` already imports its self-hosted fonts) that would
otherwise get re-litigated when Group 3 starts.

**Once every phase ships**, move this whole folder to `docs/logs/` with a date prefix
(e.g. `logs/2026-MM-DD_checkpoint-ui/`), per the normal task-completion convention.
