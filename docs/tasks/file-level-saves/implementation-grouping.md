# Implementation grouping — File-level saves

Written 2026-10-29 on top of `plan.md`'s phases. Three groups, each ending in something testable end to end:

1. **The engine**: install-folder saves resolve, sync and stay safe — no new UI, tested by scripts.
2. **The UI**: designed on mockups first, then hand-picked files and the narrowing offer.
3. **Close-out**: docs, help article, testenv pass.

| Group | Contents | Status |
|---|---|---|
| 1 | Phases 0, 1, 2, 3 — measure, resolver scope, install-folder safety + version gate, enrolment/adoption | ⏳ Not started |
| 2 | Phases 4, 5, 6 — mockups, hand-picked files, offer to narrow | ⏳ Not started — 5 and 6 wait on the maintainer's mockup pick |
| 3 | Phase 7 — docs, help, release notes, testenv pass | ⏳ Not started |

## Groups

**Group 1 — the engine (large).** Phase 0's harness change lands first so the before/after numbers are honest.
Phase 1 alone would be unsafe to release: a scoped `<base>` folder is only safe once Phase 2 stops the watcher,
probes and enumeration walking the whole install, adds the Program Files exception and gates old agents. Phase 3
makes the second machine adopt the scope and fixes the scope-mismatch join. Release-safe at the end: games that
resolve today are untouched (hybrid reach).
*Test:* detection sweep (400 sample) before/after + `pinned`; `dotnet test tests/SaveLocker.Agent.Tests`;
`run-hardening-tests` (new install-folder section, must fail on pre-fix code); a two-machine file-level suite;
`run-multipath-tests`, `run-delta-upload-tests`, `run-agent-tests` (fresh server), `run-linux-tests`.

**Group 2 — the UI (medium).** Mockups in `agent-ui/mockup-features/` and `web/mockup-features/` (Phase 4); the
maintainer picks; then the file-picking path browser and agent scope route (Phase 5) and the narrowing offer on
agent and console (Phase 6). Controller navigation on every new control.
*Test:* agent-ui/web build + lint, regenerated api-types, local API suite, the tray and console in the browser pane.

**Group 3 — close-out (small).** Phase 7 plus the testenv pass: a fake install folder with `Save.dat` on the Windows
tray and the WSL agent (and the Deck if available), push on one, pull on the other, the install's own files untouched.
