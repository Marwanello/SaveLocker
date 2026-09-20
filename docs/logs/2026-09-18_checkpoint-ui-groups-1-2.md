# Checkpoint UI redesign — Groups 1 and 2 handoff (2026-09-17 / 2026-09-18)

Moved out of `CONTEXT.md` on 2026-09-20 during the review pass: that file's own header says it is a
short orientation and detail belongs elsewhere, and these two entries were ~125 lines of it. Text is
verbatim as written by the sessions that shipped the work. The 2026-09-20 corrections to it (dark base,
sessions, the review findings) are in `tasks/checkpoint-ui/implementation-grouping.md` and
`CONTEXT.md`.

---

**Checkpoint UI redesign, Group 1 shipped (2026-09-17, branch `claude/ui-redesign-gaps-group-1-96445b`,
`tasks/checkpoint-ui/` — plan.md/implementation.md/implementation-grouping.md all updated in the same
session, per this file's own `## Status` table convention).** Asked directly to first fold in any
implementation that had drifted ahead of the plan since it was written 2026-09-02, then build Group 1.
**The gap found**: an unrelated session (2026-09-08/09, PR #35, "easy wins") had already self-hosted
both apps' fonts via `@fontsource/inter` + `@fontsource/jetbrains-mono` imported in each `main.tsx`,
for the standing LAN-offline reason in [[Decisions]] — but Phase 1 item 3 still read "swap the Google
Fonts import," which no longer existed to swap. Folded into `implementation.md`/`implementation-
grouping.md` directly (self-host `@fontsource/archivo` the same way, not a CDN import) rather than
left for whoever hit it next; it moved no work between groups, only *how* each app's font swap
happens. No other part of the plan had drifted — grepped `web/src` for the plan's own token names,
motion primitives and `ui/` folder; none existed yet.
<br>**Shipped**: `web/src/index.css`'s reset moved into `@layer base`; the `@theme` block replaced
with the full Checkpoint token set for both themes (light `:root` default, dark under
`prefers-color-scheme` or an explicit `data-theme` override for Phase 4 to drive later; old token
names kept as aliases, though grep confirmed they were dead code even before this — no component had
ever consumed them); the font swapped to self-hosted `@fontsource/archivo` in `web/src/main.tsx`
(`@fontsource/jetbrains-mono` untouched, still the code/CLI face); motion primitives (`rise`/`pop`/
`toast-in` keyframes, shared `--ease`, a `prefers-reduced-motion` block that turns all three off);
seven new primitives under `web/src/components/ui/` (`Card`, `Chip`, `Button`, `Stat`, `Row`, `Seg`,
`Toast`), each with a visible 2px accent `focus-visible` ring; and `NavBar.tsx` rewritten against the
new tokens/primitives as the one proof-of-usability surface the plan's own "done when" calls for,
with the severity/problem-badge colour logic remapped onto the plan's three-hue rule (safe/watch/
accent) instead of the old ad hoc blue/amber/red. Also shipped, partially and said so rather than
claimed in full: the three marks (Cartridge, Pixel lock, Memory card) as real SVG files under
`web/src/assets/marks/`, and an SVG favicon (`web/public/favicon.svg`, Pixel lock on the Ember accent
tile, wired ahead of the existing PNG fallbacks) — but the four Steam store crops and every PNG/ICO
export (sized favicons, the Windows tray icon, the Deck tile) are **not** done: this environment has
no SVG rasterizer at all (`magick`/`inkscape`/`rsvg-convert` all absent, confirmed by checking), so
that half of Phase 8 is flagged as a follow-up in `implementation.md` rather than silently skipped.
<br>**Verified against a real throwaway server, not just built**: `web`'s `npm run build`
(`tsc -b && vite build`) and `npm run lint` (`oxlint`) both clean; then `tests/testenv.ps1 build -Only
console` (rebuilds the Docker image from this worktree, so it genuinely picks up the changes) and
`up -Only console` on `:5080`, confirmed live in the browser: the resolved `--color-*` custom
properties matched plan.md's light and dark tables exactly under emulated `prefers-color-scheme`,
`/favicon.svg` served `image/svg+xml` and 200, and a focused nav button's computed style showed
`Archivo` as the resolved font and a `2px solid rgb(224, 83, 60)` (`--color-accent`, dark) outline —
confirming the Tailwind utilities actually compiled rather than silently no-opping. Screenshotted the
console once for a visual sanity check (NavBar in the new Ember/soft-black palette, the rest of the
app still in its old teal — the expected, deliberately partial look for a foundation-only group);
follow-up focus/theme checks used computed-style assertions instead of more screenshots, since the
Browser pane's screenshot call was intermittently timing out in this session unrelated to the app
itself. Console torn down afterward (`testenv.ps1 down -Only console`) to leave a clean state.
<br>**One real Tailwind footgun caught and fixed while building the primitives, not left in**: a
first draft built the agent-problems badge colour via `` `border-${tone}` `` string interpolation —
Tailwind's scanner reads source text at build time, not runtime template results, so that would have
silently generated no CSS for any of the three tones. Fixed with a `Record<Tone, string>` of full
literal class strings (the same pattern already used in `Chip.tsx`), and separately caught two spots
where a component's own class and a one-off override targeted the same CSS property (ambiguous
cascade order) — fixed by moving those two overrides to inline `style`, which always wins regardless
of Tailwind's generation order.
<br>`agent-ui` **untouched this session** — Group 1 is scoped `web`-only by design; Group 3 executes
the token-delivery decision made here (a plain `agent-ui/src/tokens.css`, imported from
`agent-ui/src/main.tsx` the exact way that file already imports its self-hosted fonts) and the
matching Archivo font swap on that side. Groups 2 (console shell) and 6 (Deck) are next and don't
depend on each other; full status table in `tasks/checkpoint-ui/implementation-grouping.md`.

---

**Checkpoint UI redesign, Group 2 shipped (2026-09-18, same branch, `tasks/checkpoint-ui/` docs
updated in the same session again).** Asked directly to start Group 2 (console shell + console
Sync all) once Group 1 landed. Console-side only, per the grouping doc's own scope.
<br>**Shipped**: `GamesSidebar.tsx` rows converted to the new `ui/Row` primitive; a new
`GamesGrid.tsx` + `ui/Seg` list/grid switch persisted to `localStorage` (`sl_games_layout`), with a
cover-art fallback tile; `NotificationsMenu.tsx` (extracted and extended from Group 1's inline
dropdown) with per-item deep links and a "Dismiss all"; `SignIn.tsx` + a `NavBar` Lock button
replacing the old always-visible password field, per plan.md's "remove the password field from the
header"; `SyncAllProgress.tsx`, a self-polling progress rail that owns its own state so a tick never
re-renders `NavBar`; and the exclude-pattern editor in `GameDetail.tsx` rebuilt as removable chips
with a live "would additionally exclude N files" dry run.
<br>**Two gaps found and folded into the plan before building, same as Group 1's font gap.** (1)
Phase 3 item 1 said Sync all should "enqueue a command per tracked game for the machine that owns
it" — tracing `CommandPoller.ExecuteAsync` found `TargetGames(cmd.GameId)` already treats a null
`GameId` as "every game this machine tracks," the same mechanism the tray's own local Sync All uses.
Built the simpler, correct way: one `Sync` command per **machine** (`GameId: null`) through a new
`POST /commands/bulk` (`SyncService.EnqueueCommandsAsync`), not one per game. (2) The exclude-chip
"preview what is skipped" item assumed `/versions/{id}/stats` could drive a client-side dry run —
its DTO turned out to be `(FileCount, NewestFileWriteUtc)` only, no file list at all. Built a new
`POST /games/{id}/excludes/preview` instead (`SyncService.PreviewExcludesAsync`), reading the head
archive's own zip directory and running it through a `SaveArchive.FilterExcluded` extracted from the
agent's own `EnumerateRelativeFiles` — so the preview can never drift from what the agent actually
does, and a new `SaveArchive.ListArchiveEntries` reads the zip directory the same way
`GetArchiveStats` already does. Both gaps, and a third smaller one (`Sync:DefaultExcludeGlobs` is
`IConfiguration`-only, never wired into `SettingsService`'s DB-backed override — the "editor" item
shipped read-only), are written up in `implementation.md`/`implementation-grouping.md` rather than
left as a surprise for whoever reads them next.
<br>**One item deliberately split off, exactly as `implementation-grouping.md` pre-authorized**: the
release-history table. `WhatsNewView.tsx`'s existing sidebar-click layout already technically
surfaces every release; turning it into "3 newest in full, a table of the rest below" is a distinct
navigation redesign sharing no files with the rest of this group, left for a short follow-up session.
<br>`openapi.json`/`web/src/api-types.ts` regenerated against a real dev server on `:5179` and
diffed — only the two new routes/schemas (`ExcludesPreviewDto`, `/commands/bulk`,
`/games/{id}/excludes/preview`) appear.
<br>**Verified live against a real throwaway rig, not just built**: `tests/testenv.ps1 build` (all
three: Windows agent, WSL agent, console) → `conflict -Windows -Wsl` → `up`. The **Windows** side of
that conflict-seed failed with a 401 on `add-game` — not chased down (looked like a testenv/CLI
auth-timing issue, not a console bug, and the WSL side alone was enough to test with) — so
verification ran against one real WSL machine ("LinuxTest") with one real tracked game ("Conflict
Game") instead of a genuine two-sided conflict. Confirmed live: the list/grid switch and its
persistence, the "no art" fallback tile, Sync all's bulk enqueue actually completing a real command
on the real daemon (watched `GET /commands` flip to `Done` with a real result string), Lock → SignIn
→ reconnect with a blank password, and the exclude preview returning "would additionally exclude 1
file" after adding `**` as a draft pattern against the one real seeded file — an exact match. The
notifications menu's conflict deep-link and "Dismiss all" were not exercised live (no real
problem/conflict existed in the seeded data) — build- and code-review-verified only, noted as such in
`implementation.md` rather than claimed as tested.
<br>**A pre-existing, unrelated test-suite bug, found and fixed in this session after all**: `run-server-
bugbounty-tests.ps1` failed the same 6 checks (all in CS-02, "reporting a reclaimed command completes
it" and its dependents) twice in a row against a freshly wiped `.verify-server-bugbounty` — confirmed
via `git diff` that nothing this session's own code changes overlap the command-claim/lease/report
code path (`SyncService.cs`/`Program.cs`/`Contracts.cs` changes there are 100% additive). Root cause:
the `Report-Command` test helper never sent a `claimToken`, a field the CS-02 hardening fix
(`b313e6f`, 2026-09-07) added to `CompleteCommandAsync`'s fencing check — every real agent
(`CommandPoller.cs`) threads it through, but this helper predates that fix (`d46dcd6`) and was never
updated, so every report silently no-opped instead of completing the command. Fixed by capturing the
`claimToken` each `Claim-Commands` call returns and passing it through the three `Report-Command`
call sites; 216/216 now passes clean. Separately,
`run-agent-tests.ps1` looked broken the same way at first (3 failures around "Laptop pull restores
save") but turned out to be **this session's own fault**: `.verify/`'s client-side agent configs
carried a stale `LastSyncedHash` from an earlier run that a fresh server DB didn't know about —
exactly the trap `Gotchas.md` already documents ("clear the server DB and `.verify/` together, never
one alone"), which this session should have read before running suites and didn't. Wiping both
together gets the documented 47/47 clean. No new Gotchas.md entry needed — it already says this.
