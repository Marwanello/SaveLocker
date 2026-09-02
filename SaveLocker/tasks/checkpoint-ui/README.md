# Checkpoint UI — working folder

Everything for the console / agent / Deck visual redesign lives here. Like
`tasks/conflict-resolution-ui/`, this is **not** an ordinary `SaveLocker/tasks/*.md` single-file task
— it's a living, multi-session design doc set for a still-open, 8-phase effort (nothing shipped yet
as of 2026-09-02; the design phase is complete and no implementation phase has started). Don't treat
`plan.md` as "read once, execute its steps, stop" — it's the canonical reference to re-read at the
start of every session that touches this work, and it gets amended in place as phases ship.

| File | What it is | Read it when |
|---|---|---|
| `plan.md` | The canonical design spec — tokens, type, colour rule, motion, layout, voice, per-surface shells | Any session touching the redesign, before doing anything else |
| `implementation.md` | What already exists vs. what doesn't, and all 8 phases with the work each needs | After `plan.md`, to see what a phase actually involves |
| `implementation-grouping.md` | Which phases to lump into one session, in what order, and why — regrouped **by surface**, because several phases edit the same components | Before starting any new phase, to decide this session's scope |
| `brand-kit.html` | The brand kit as a standalone page — marks, colour, type, components, motion, Steam art, voice, paste-ready tokens | When building any new UI, in this project or beside it |

Two live links, both mirrors of what's in this folder rather than extra sources of truth:
[prototype](https://claude.ai/code/artifact/b8f247f2-32e5-4808-8e4c-61ba0cc3406f) ·
[brand kit](https://claude.ai/code/artifact/b3e0c8a5-70a0-47bf-b4f2-d0dbf4f0b2d5).

**Start here:** `implementation-grouping.md` Group 1. It gates every other group, and it carries the
one decision (how `agent-ui` receives the design tokens, given it has no Tailwind and no CSS file)
that Groups 3 and 4 will otherwise re-litigate.

**Once every phase ships**, move this whole folder to `SaveLocker/logs/` with a date prefix
(e.g. `logs/2026-MM-DD_checkpoint-ui/`), per the normal task-completion convention.
