# Implementation grouping — how to actually work through Phases 1–8

Written 2026-09-02. The practical execution plan on top of [[implementation]]'s phase list: which
phases to do in one session, in what order, and why. Driven by three things weighed together:

1. **Real dependencies** — Phase 1 genuinely gates the rest; most of the others don't gate each other.
2. **File overlap**, which matters more here than in any previous effort. This is a reskin: several
   phases edit *the same components*. Two phases that both rewrite `NavBar.tsx` are one session or
   they're a merge conflict with extra steps — so the groups below regroup the phase list **by
   surface**, not by phase number. That's the main thing this file adds over `implementation.md`.
3. **What this environment can verify**, and **session-cost precedent from this repo's history**:
   Phase 2+3 of the conflict work (~426 insertions, 5 files, one clean round) was comfortable;
   Phase 4+6 (~1,683 insertions, 31 files) shipped fine as one PR; Phase 0/1 (~3,077 insertions,
   23 files, multiple review rounds) was a heavy session. Treat ~1,500 insertions as the ceiling for
   a group that still gets reviewed properly.

## Three corrections to `implementation.md`, found while writing this

These were checked against the source, not assumed. `implementation.md` has been amended.

**1. `agent-ui` has no Tailwind and no CSS file at all.** `implementation.md` Phase 1 said "the agent
UI imports the same file". It can't — `agent-ui/package.json` has no `tailwindcss`, and there is no
`.css` file anywhere in `agent-ui/src/`. It is styled *entirely* by inline style objects with
hardcoded hex values. So "fix the reset so utilities win" is a **`web`-only** fix; the agent needs a
different mechanism for the same tokens, and that decision gates its foundation work. The two
front ends also don't share an icon story: `agent-ui` depends on `lucide-react`, `web` has no icon
library at all.

**2. The scale of the inline-style problem is the real Phase 1 risk.** Counted:

| | inline `style={{` | `className=` |
|---|---|---|
| `web/src` (11 files) | **388** | 4 (three of them in the markdown renderers) |
| `agent-ui/src` (13 files) | **215** | **0** |

Tailwind is installed in `web` and effectively unused. That reframes Phase 1: layering the reset
*unblocks* utilities but converts nothing — ~600 call sites don't migrate in a session, and a group
that tries will blow the sizing ceiling above. **Phase 1 ships the tokens and primitives; the
migration rides along inside Groups 2–4, one surface at a time.** Never open a "migrate all inline
styles" session.

**3. The grid wall needs zero server work.** `implementation.md` Phase 2 said the 600×900 `grid`
kind needs fetching "alongside the existing kinds". It's already there — `ArtService.Assets` fetches
`grid` at exactly `dimensions=600x900`, plus `hero`, `logo` and `icon`, and `GridUrl`/`HeroUrl`/
`LogoUrl`/`IconUrl` are already on the game DTO in `Contracts.cs`. The grid view is pure UI.

### And one trap to not walk into

`implementation.md` Phase 5 says the agent Games tab's data "is already in `GET /api/games` +
`GET /api/games/{id}/sync-status`". Half true, and the dangerous half. `sync-status`' own handler
comment says it is "NOT cheap on disk: the local hash still walks and reads every file in the save
folder," and it calls `ApiClient.GetStateAsync` on top. **A list view must not poll it per game** —
that re-hashes every save folder on a timer. This is the identical mistake the conflict-resolution
plan caught and pulled its Phase 12 for; see that folder's `implementation-grouping.md`. The Games
*list* renders from `GET /api/games` only. `sync-status` is allowed on a single opened game, on an
explicit "check now", never on a timer and never for a whole list.

## What this environment can verify

This is a **local Windows** session, which is a better position than the cloud Linux container the
conflict-resolution work ran in — but there is **no frontend test runner in either app** (no vitest,
no jest, no playwright in either `package.json`). "Verified" for UI work therefore means: builds
clean, loaded in a real browser, checked in light *and* dark, keyboard focus visible. There is no
unit-test safety net to lean on, which is another reason to keep groups small.

| Group | Buildable here? | Verifiable here? |
|---|---|---|
| 1, 2, 3, 4, 5 | Yes | **Yes** — `npm run build` + dev server + browser, both themes |
| 6 (Deck ImGui) | Yes — it compiles | Build only; real gamepad nav needs the Deck or a WSLg box |
| 7 (Windows toast) | Yes | **Yes** — Windows agent runs here |
| 7 (Linux freedesktop) | Yes | **No** — needs a live desktop session with a notification daemon |
| Wayland window | Blocked | Blocked — needs a decision first, see below |

## Groups

**Group 1 — Foundation and assets. `web` only. Do this first; it gates everything.**
Phase 1 (the `web` half) + all of Phase 8. Assets move *up* from last to first: the marks and Steam
art are already designed, they're cheap to export, and the favicon is the cheapest possible
end-to-end proof that the token pipeline works. Contents: move the reset into `@layer base`, replace
the `@theme` block with the Checkpoint tokens for both themes (keeping the old names as aliases for
one release), swap the font import to Archivo *above* the Tailwind import, add the motion primitives,
build `web/src/components/ui/`, and ship the mark and Steam files.

The one decision this group must make, because everything downstream depends on it: **how `agent-ui`
gets the same tokens** — add Tailwind to it, or emit a plain CSS custom-property file both apps
import. Recommend the plain CSS file: it's the smaller change, it works for the agent's inline-style
components *today* via `var(--…)` without converting anything, and it keeps one source of truth.
Decide it here, in Group 1, or Group 3 will re-litigate it.

*Done when:* both apps still build, nothing has moved except type and colour, and one converted
surface (`NavBar.tsx`) proves the primitives are usable.

**Group 2 — Console shell, including console Sync all.**
Phase 2 **plus** Phase 3 items 1, 2 and 4. Folded together deliberately: the Sync all button and the
progress rail live in the same top bar Phase 2 is already rewriting, and the notifications bell is in
that bar too. Splitting them means editing `NavBar.tsx` twice. Server work is one small endpoint,
`POST /commands/bulk`, so the client isn't making seven round trips. Sign-in ships as option (a) from
`implementation.md` (the screen is where you type the existing `X-Admin-Password`); file the real
session endpoint as a follow-up rather than growing this group.

This is the biggest user-visible payoff in the plan and it is fully verifiable here. Watch its size —
if it's running past ~1,500 insertions, the release-history table and the exclude-pattern chips are
the two cleanest things to split into a Group 2b.

**Group 3 — Agent foundation, Overview, and agent progress.**
Phase 1 (the agent half, per Group 1's decision) + Phase 5's Overview trim + Phase 3 items 3 and 5.
Grouped by file again: `StatusHeader.tsx` is where the progress lands and `OverviewView.tsx` is what
gets trimmed, and both are small. `POST /api/sync` and `GET /api/activity` already report phase and
bytes, so the agent's Sync all is genuinely UI-only. Phase 3 item 4 — *a progress tick must not
re-render its surroundings* — applies here as a correctness requirement, not polish: progress lives
in its own component subscribing to the poll.

**Group 4 — Agent Games tab.**
The rest of Phase 5: the new Games view (list and grid, cover art, per-game page with Sync/Push/Pull),
the art proxy, and search in Add games. Split from Group 3 because it's the single largest *new* UI
in the plan and it doesn't fit alongside the foundation work. Needs Group 3's tokens. Obey the
`sync-status` trap above. The art proxy (`GET /api/games/{id}/art` through the agent) is the right
call over pointing the browser at the server — it still works when the browser can't reach the server
directly.

**Group 5 — Appearance, and syncing it to the fleet.**
Phase 4, alone. The only group that changes the wire format:
`AgentHeartbeatResponse` is currently the single-field `record AgentHeartbeatResponse(
ConflictEscalationDto[] EscalatedConflicts)`, and this adds an `appearance` object to it. That means
regenerating `src/Server/openapi.json` **and** `api-types.ts` in *both* front ends and committing all
three, per `CLAUDE.md`. It needs Groups 1–4 to have landed, because there's no point pushing a theme
choice to surfaces that don't read tokens yet, and it needs Group 1's marks to exist as real icon
files. Keep it alone — a wire change plus a settings surface plus tray/favicon swapping is a full
session on its own.

**Group 6 — Deck. Code-only from here.**
Phase 6 items 1–3: Checkpoint dark tokens in `src/Agent.Linux/Ui/Theme.cs`, two-line rows in
`Widgets.cs`, the button legend, and Sync all bound to Y. Independent of Groups 2–5 — it shares no
files with them — so it can slot in any time after Group 1 fixes the token values. Compiles and gets
reviewed here; flag gamepad verification as pending a WSLg or real-Deck pass, the same honest way
every other hardware-gated feature in this project has shipped.

**Group 7 — Notifications. Windows half here, Linux half deferred.**
Phase 7. The Windows toast is buildable *and* verifiable on this machine. The Linux freedesktop call
is buildable here but only observable on a real desktop session — `DesktopEnvironment.cs` already
probes `org.freedesktop.Notifications` and exposes `NotificationDaemonPresent`, so the check exists
and only the call is missing. Ship the rules (fire on conflict opened, lease held elsewhere, push
failed after final retry, pull refused, update staged, server unreachable past 5 minutes; never on a
successful push; a standing warning announces once, not per poll) in the same group, since they're
shared logic rather than per-platform.

## Open decision, blocking nothing yet

**Phase 6 item 4 — the Wayland desktop window.** Host the existing agent web UI in a small GTK/WebKit
window with a header bar, or accept the browser. This is the one item in the whole plan with no
obvious right answer, and it needs deciding before any code is written for it. It blocks nothing
else — Groups 1–7 all proceed without it — so don't let it hold up the queue, but don't let it drift
into a session unexamined either.

```
Group 1  (P1-web + P8 assets)          gates everything; makes the agent-token decision
   ├─ Group 2  (P2 + P3.1/2/4 + bulk)  console shell + console sync all   ← biggest payoff
   ├─ Group 3  (P1-agent + P5 overview + P3.3/5)   agent foundation
   │     └─ Group 4  (P5 Games tab + art proxy + search)
   └─ Group 6  (P6.1-3 Deck)           independent; code-only here

Groups 1-4 all landed  →  Group 5  (P4 appearance + fleet sync)   ← wire change, alone
Any time after Group 1 →  Group 7  (P7 notifications)             ← Linux half unverifiable here
Needs a decision first →  Wayland window (P6.4)
```

Inline-style migration is **not** a group. It rides inside Groups 2, 3 and 4, converting only the
surfaces those groups are already rewriting. Anything not touched by a group stays inline until a
later group has a reason to touch it, and that is fine.

## Re-evaluate before each new group

This assumes the phase list in `implementation.md` and the environment as of 2026-09-02. Re-check the
verifiability table if Deck hardware, a WSLg box, or a Linux desktop session becomes available, and
re-check the two counted tables in "Corrections" if a group has already migrated a large number of
call sites — the sizing advice above is calibrated to ~600 inline styles remaining.
