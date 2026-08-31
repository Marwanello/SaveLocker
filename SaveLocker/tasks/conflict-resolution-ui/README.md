# Conflict resolution UI — working folder

Everything for the "proper conflict resolution, not just refusals" effort lives here now (moved
2026-08-30 from `SaveLocker/logs/` and repo-root `docs/design/` — a stub at the old `logs/` path
points here). This folder is **not** an ordinary `SaveLocker/tasks/*.md` single-file task — it's a
living, multi-session design doc for a still-open, 15-phase effort (Phases 0–3 shipped; 4–14 open).
Don't treat `plan.md` as "read once, execute its steps, stop" the way a normal task file is — it's
the canonical reference to re-read at the start of every session that touches this work, and it gets
amended in place as phases ship or the plan changes.

| File | What it is | Read it when |
|---|---|---|
| `plan.md` | The canonical, current design + all 15 phases (0–14), their dependencies, and what's already shipped | Any session touching conflict resolution, before doing anything else |
| `implementation-grouping.md` | Which phases to lump into one session, in what order, and why — the practical execution plan on top of `plan.md`'s dependency graph | Before starting any new phase, to decide what this session's scope is |
| `reference/00-inventory.md` … `07-open-questions.md` | The earlier, fork-blind first-pass design (superseded by `plan.md` for Decky/Playnite specifics, but still the source for the Linux escalation-ladder and Windows-chooser material `plan.md`'s Phases 5–9 build from) | When a phase's `plan.md` entry cites one of these by name |

**Once every phase in `plan.md` ships**, move this whole folder to `SaveLocker/logs/` with a date
prefix (e.g. `logs/2026-MM-DD_conflict-resolution-ui/`), per the normal task-completion convention —
don't leave it in `tasks/` indefinitely once nothing here is still open.
