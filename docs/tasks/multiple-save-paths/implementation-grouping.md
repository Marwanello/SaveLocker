# Implementation grouping — multiple save paths

Written 2026-10-04, alongside `plan.md`, which has the phase list and the design. This file says which
phases share a session, and in what order. There is one commit per phase, and both Status tables are
updated as each one ships.

## Status

| Group | Contents | Status |
|---|---|---|
| A — Foundation | Phases 1–2 (archive core, server model + wire) | ✅ Done 2026-10-04 — branch `multiple-save-paths-group-a`; nothing user-visible yet, every check automated |
| B — End to end | Phases 3–5 (agent sync core, reconcile/CLI/local API/doctor, scanners declare extra paths) | ⏳ Not started |
| C — Suggestions + UI | Phases 6–7 (manifest suggestions, agent-ui/dashboard/Deck) | ⏳ Not started |

## Groups

**A — Foundation (Phases 1–2), about one session.** Both phases are invisible to users, and automated
tests verify both completely: xUnit for the archive, and the server suites plus a migrated copy of a real
DB for the model. Phase 1 comes first because every later phase relies on the layout and restore rules
it sets. Phase 2 is additive on the wire, so nothing in the fleet notices it.

**B — End to end (Phases 3–5), one to two sessions.** The first group anyone can try. After Phase 4,
`add-path` gives a game a second folder that syncs across the testenv rig. The Phase 4 check is the
critical one: two full sync cycles on both machines with no rise in the version count, run with the path
mapped, then unmapped (shadow), then with the folder missing. Phase 5 is small, and it belongs here
rather than in C because it is exactly what `emulator-saves` Phase 1b needs. **Once B is merged,
`emulator-saves` is rebased** (`plan.md` → *Rebase notes*) and continues in parallel with C.

**C — Suggestions + UI (Phases 6–7), about one session.** Phase 6's "Also found" suggestion needs
somewhere to show up, so it ships with the UI. Verify in testenv with screenshots for each UI, plus the
Deck target.
