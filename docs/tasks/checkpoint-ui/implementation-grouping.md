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

## Status (updated 2026-09-20)

| Group | Contents | Status |
|---|---|---|
| 1 | Phase 1 (web half) + Phase 8 assets | 🚧 In progress — web foundation ✅ Shipped 2026-09-17 (reset layered, tokens, self-hosted Archivo, motion primitives, `ui/` primitives, `NavBar.tsx` converted as proof). Assets partial: 3 marks + SVG favicon shipped; Steam art crops and all PNG/ICO rasterization deferred — no rasterizer available in this environment, see `implementation.md` Phase 8 |
| 2 | Phase 2 + Phase 3.1/2/4 + `POST /commands/bulk` | ✅ Shipped 2026-09-18 — every item except the release-history table, split off as pre-authorized below (still ⏳). See `implementation.md` Phase 2/3 for the per-item table |
| 3 | Phase 1 (agent half) + Phase 5 overview trim + Phase 3.3 (agent Sync all) | ✅ Shipped 2026-09-20 — tokens, primitives, Archivo, status header with Sync all + live progress, trimmed Overview, converted shell. **Phase 3.5 (per-game "Sync this game") ➡️ moved to Group 4**: it needs a game page and a per-game agent route, neither of which exists yet. See the Group 3 write-up below |
| 4 | Phase 5 Games tab + art proxy + search + **Phase 3.5 (per-game Sync this game)** | ✅ Shipped 2026-09-21 — see the Group 4 write-up below |
| 5 | Phase 4 appearance + fleet sync | ⏳ Not started — **must also flip the theme default to follow the OS** (see below), and only once Groups 3–4 have converted the remaining inline-styled views |
| 6 | Phase 6 items 1-3 (Deck) | ⏳ Not started |
| 7 | Phase 7 (notifications) | ⏳ Not started |

**2026-09-20 review pass (a code review of Groups 1–2, all findings fixed on branch
`console-review-fixes-and-security-hardening`).** Two things here change what later groups may assume:
1. **The theme default was wrong and is corrected.** Group 1 shipped light as the `:root` base with dark
   under `prefers-color-scheme`. Every view not yet migrated hardcodes dark colours but inherits text
   colour from `<body>`, so light-preferring visitors got near-black text on dark cards — game titles,
   machine names, headings, most of the Audit Log (measured 1.04–1.15:1). Dark is the base again;
   light is `data-theme="light"` only. **Group 5 (appearance) flips it** — after which every
   remaining `#hex` in `web/src` must already be gone, which is the actual acceptance test: run the
   contrast walk described in `Gotchas.md` → *Web console* in both schemes.
2. **Sign-in shipped as "option (a)" (the password kept in `localStorage`) and was upgraded to option
   (b)**: a revocable session (`POST /api/admin/session`, `X-Admin-Session`). Group 3's agent UI has its
   own local API and is unaffected; anything new in `web/` must go through `api.ts`, never read a
   credential itself.

## Corrections to `implementation.md`, found while writing this (and one found later)

These were checked against the source, not assumed. `implementation.md` has been amended.

**1. `agent-ui` has no Tailwind and no authored CSS file.** `implementation.md` Phase 1 said "the
agent UI imports the same file". It can't — `agent-ui/package.json` has no `tailwindcss`, and there
is no `.css` file *written by this project* anywhere in `agent-ui/src/`. It is styled *entirely* by
inline style objects with hardcoded hex values. So "fix the reset so utilities win" is a
**`web`-only** fix; the agent needs a different mechanism for the same tokens, and that decision
gates its foundation work. The two front ends also don't share an icon story: `agent-ui` depends on
`lucide-react`, `web` has no icon library at all.
<br>**Narrowed 2026-09-17, see correction 4 below:** `agent-ui/src/main.tsx` now imports third-party
CSS (`@fontsource/*`), so "no CSS at all" is no longer literally true — there's a real, already-
working precedent in this exact codebase for importing a `.css` file from `main.tsx` in `agent-ui`.
Group 3 should use it for the tokens file rather than treating the mechanism as unproven.

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

**4. Fonts stopped being a Google Fonts import between this file being written and Group 1
starting — found 2026-09-17, before Group 1 began.** `implementation.md` Phase 1 item 3 said "swap
the Google Fonts import: Archivo only... drop Inter and JetBrains Mono from the import." Two weeks
after this grouping file was written, an unrelated session (2026-09-08/09, branch `easy-wins`,
PR #35) self-hosted both apps' fonts via `@fontsource/*` packages imported in each `main.tsx`, so the
console and agent no longer render in fallback fonts on a LAN box with no internet — a real
constraint for this product (`Decisions.md` → self-hosted, bring-your-own-TLS, LAN threat model).
Neither app's `index.html` has a font `<link>` any more. Swapping to Archivo the way item 3
originally described it — editing a Google Fonts import — would have reintroduced the exact bug
that fix closed. Folded into `implementation.md` Phase 1 item 3 directly (self-host
`@fontsource/archivo` instead) rather than left as a surprise; it doesn't move any work between
groups, `web`'s half is still Group 1 and the agent's half is still Group 3, only *how* each swaps
its font changed. This is also the reason correction 1 above was narrowed: the fix touched
`agent-ui/src/main.tsx` too, which is the same file Group 3 will use for the tokens decision.

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
✅ **Shipped 2026-09-17.** Phase 1 (the `web` half) + all of Phase 8. Assets move *up* from last to
first: the marks and Steam art are already designed, they're cheap to export, and the favicon is the
cheapest possible end-to-end proof that the token pipeline works. Shipped: the reset moved into
`@layer base`; the `@theme` block replaced with the Checkpoint tokens for both themes (old names kept
as aliases); the font swapped to self-hosted Archivo (`@fontsource/archivo`, not a Google Fonts
import — see correction 4 above); the motion primitives (`rise`/`pop`/`toast-in`, `--ease`,
`prefers-reduced-motion`); `web/src/components/ui/` (`Card`, `Chip`, `Button`, `Stat`, `Row`, `Seg`,
`Toast`); and `NavBar.tsx` converted to prove the primitives are usable end to end (both themes,
keyboard focus visible).
<br>**Assets shipped partially, and said so rather than silently claiming the whole of Phase 8: the
three marks (real SVG files) and an SVG favicon, both wired in. The four Steam store crops and every
PNG/ICO export (sized favicons, the Windows tray icon, the Deck tile) are NOT done** — this
environment has no SVG rasterizer at all (`magick`, `inkscape`, `rsvg-convert` all absent) — see
`implementation.md` Phase 8 for the exact list and what the next session needs to bring.

The one decision this group had to make, because everything downstream depends on it: **how
`agent-ui` gets the same tokens** — add Tailwind to it, or emit a plain CSS custom-property file both
apps import. **Decided: the plain CSS file**, imported from `agent-ui/src/main.tsx` the same way that
file already imports `@fontsource/*` CSS (a working precedent in this exact codebase, not a novel
mechanism) — `web`'s tokens live in `web/src/index.css`'s `@theme` block as the source of truth, and
`agent-ui/src/tokens.css` is a hand-kept-in-sync copy, per `implementation.md` Phase 1 item 6. Group 3
executes this; Group 1 only decided it, since Group 1 is scoped `web`-only.

*Done when:* both apps still build, nothing has moved except type and colour, and one converted
surface (`NavBar.tsx`) proves the primitives are usable. — **met**: `web` builds and lints clean;
`agent-ui` untouched (its half of Phase 1 is Group 3); `NavBar.tsx` verified live in both themes via
a real throwaway server (`tests/testenv.ps1`), not just built.

**Group 2 — Console shell, including console Sync all.**
✅ **Shipped 2026-09-18** — every item except the release-history table (below). Phase 2 **plus**
Phase 3 items 1, 2 and 4. Folded together deliberately: the Sync all button and the progress rail
live in the same top bar Phase 2 is already rewriting, and the notifications bell is in that bar
too — all landed together in one `NavBar.tsx` rewrite, not two. Server work turned out to be two
small endpoints, not one: `POST /commands/bulk` (as planned) plus `POST /games/{id}/excludes/preview`
(the exclude-chip dry run needed a real endpoint — `/versions/{id}/stats` has no file list to compute
one client-side, contrary to `implementation.md`'s original hedge). Sign-in shipped as option (a)
from `implementation.md`; the real session endpoint (option b) stays a follow-up.
<br>**"Sync all" is one command per machine, not per game** — see `implementation.md`'s "gap found
while building Group 2": `CommandPoller.TargetGames` already treats a null `GameId` as "every game
this machine tracks," so a single `Sync`/`GameId: null` command per machine does what the plan's
phrasing implied would need one command per game. Verified live end to end against a real seeded
test machine — the queued command actually ran and completed.
<br>**Split off, as pre-authorized below**: the release-history table. `implementation.md`'s Phase 2
table has the full per-item detail, including two honestly-scoped simplifications — the default
excludes "editor" shipped read-only (`Sync:DefaultExcludeGlobs` was never wired into the DB-backed
settings override the SteamGridDB key uses, so there was nothing to write to yet), and the
notifications menu's deep link opens the right game but doesn't scroll/focus a specific field for
`savedir.missing`.
<br>**Verified live, not just built**: a real throwaway server via `tests/testenv.ps1` (a seeded
WSL machine + one tracked game with a real save) — list/grid switch and its `localStorage`
persistence, the cover-art fallback tile, Sync all's bulk enqueue and real completion, Lock →
SignIn → reconnect, and the exclude-chip preview count against the game's actual archive (added
`**` as a draft pattern, got back "would additionally exclude 1 file," matching the seed's one
file exactly). The notifications menu's deep-link and "Dismiss all" were not exercised live — no
real problem/conflict event existed in the seeded data — verified by build and code review only.

This is the biggest user-visible payoff in the plan and it was fully verifiable here, as expected.

**Group 3 — Agent foundation, Overview, and agent progress.**
✅ **Shipped 2026-09-20 (`2705ce4`) — every item except Phase 3 item 5, which moved to Group 4.** Written
up after the original scope below, so the two read side by side.
<br>**Shipped:** `agent-ui/src/tokens.css` (dark base, `data-theme="light"` opt-in — the same rule as
`web`, for the same reason) and `ui.css` (the reset, the scrollbar, the motion keyframes and the `sl-`
classes behind the primitives — `agent-ui` has no Tailwind, and hover/active/focus-visible cannot be
inline styles), both imported from `main.tsx` beside the `@fontsource` CSS; `@fontsource/archivo`
replaces Inter (`@fontsource/jetbrains-mono` untouched); `components/ui/` — `Button`, `Card`, `Chip`,
`Stat`, `Banner`, `Toast`, the same API as `web`'s so Group 4's `Row`/`Seg` port the same way. **Sync all
and its progress are `StatusHeader.tsx`, now the strip under the top bar on every page** (badge, one line
of status, a primary Sync all, the meter): determinate from `bytesDone/bytesTotal` for a push,
a sweeping indeterminate bar for a pull, a settle wait, or the gap between two games — there is no honest
percentage for those, so none is drawn. `POST /api/sync` and `GET /api/activity` needed no change, as
predicted. The Overview is three stats, one status banner (not connected → conflict → per-game lease
warnings → "Nothing needs you"), Next up and Recent; `ActivityCard` is deleted; the launch-setup, Decky
and Playnite cards moved to Settings, where the prototype puts them.
<br>**Item 4 is structural, not a matter of care.** `useActivity.ts` is ONE shared poll behind
`useSyncExternalStore`, and each hook subscribes to one slice — `useActivityCurrent` (every tick),
`useActivityBusy` (a boolean, flips twice a sync), `useActivityRecent` (unchanged by a tick). A poll keeps
the previous object for whichever slice did not change. **Measured** in the running app over four
byte-progress ticks: 16 DOM mutations in the progress area, **0** in the Overview page and **0** around the
Sync all button.
<br>**Deliberate departures from the prototype, all toward the plan's own text or the shipped console:**
the current nav item is a neutral tile, not accent-soft (plan.md: accent means "a decision is waiting";
`web`'s `Button` already has a `selected` variant for the same reason), and the progress meter fills with
`--color-safe` like Group 2's `SyncAllProgress` rather than the prototype's accent; the hero badge is an
icon, not the prototype's monospace "IDLE/HOLD" text (plan.md: mono is not the data face, and "HOLD" would
claim sync is paused, which it is not for the other games); stats are the three the agent really has —
Tracked here, Saves backed up, Last sync — not the prototype's "Sent today", for which no number exists.
**Recent expands inline to the full 50-entry log** ("Show all N") — trimming the Overview must not delete
the only place the rolling log was visible, and plan.md forbids a modal. The agent's Sync all confirms
with the server's own "Sync all complete." as a toast (2.6 s; a failure stays until dismissed), because `SyncAllAsync` returns no counts
or byte totals and none were invented.
<br>**One bug found in the code being moved, fixed rather than carried:** `handleSynced` read `view` from
the render in which Sync was pressed, so its "do not pop the overlay on top of Conflicts" guard could never
see a later navigation. With the button on every page, navigating mid-sync is the normal case. It reads a
ref now. Verified both ways in the real app: pressed from Overview it opens the pop-up; pressed from
Overview with the user then going to Conflicts (sync slowed to 3 s to allow it) it does not.
<br>**A finding the maintainer should decide on, not fixed here:** `--color-faint` measures **3.31:1** on
the dark panel and **3.55:1** on the light one — the plan's own values, and the ones `web` already uses —
so 10–12px text in it fails WCAG AA in both themes. In the agent, only the seven 10px eyebrow labels still
use it; every piece of content text (hero detail, empty state, timestamps, the sidebar footer, progress
numbers) uses `--color-dim`, and a contrast walk over all 43 text nodes of the shell and Overview, in both
themes, finds nothing else under 4.5:1. Lifting `--faint` itself is a token change that has to happen in
`web/src/index.css`, `agent-ui/src/tokens.css` and `Ui/Theme.cs` together.
<br>**Verified live** (via `tests/testenv.ps1`, on the WSL agent): a genuine two-machine conflict seeded
with `conflict -Windows -Wsl`; the hero, its chip, the sidebar count and the banner all read from the real
API; the **real** Sync all button went busy (disabled, "Syncing…"), raised the "Sync all complete." toast,
dismissed it at 2.6 s, and opened the conflict pop-up 7 ms after the toast; every tab stop had a visible 2px
accent focus ring; both themes rendered; the launch-setup and Decky cards render in Settings on the Linux
agent and are gone from Overview. The states that need special data (a push at 19 of 25 MB, a pull with no
byte total, a not-connected machine, a lease warning) were checked by intercepting `fetch` in the page
against the Windows test agent's UI, and are **not** a substitute for a real push: loopback finishes a
push too fast to screenshot mid-transfer, as `CONTEXT.md` already records. **Not verified:** the WebView2
tray window itself (the same bundle was loaded in a browser), and a real Steam Deck. `agent-ui` `tsc -b &&
vite build` and `oxlint` are clean (the same two pre-existing warnings); no C# changed, so no test suite
was re-run and `api-types.ts` was not regenerated.
<br>**Original scope, kept for the record:**
Phase 1 (the agent half, per Group 1's decision) + Phase 5's Overview trim + Phase 3 items 3 and 5.
Grouped by file again: `StatusHeader.tsx` is where the progress lands and `OverviewView.tsx` is what
gets trimmed, and both are small. `POST /api/sync` and `GET /api/activity` already report phase and
bytes, so the agent's Sync all is genuinely UI-only. Phase 3 item 4 — *a progress tick must not
re-render its surroundings* — applies here as a correctness requirement, not polish: progress lives
in its own component subscribing to the poll.
<br>Two concrete tasks Group 1 left decided-but-undone, per its own write-up: create
`agent-ui/src/tokens.css` (the Checkpoint tokens, hand-kept in sync with `web/src/index.css`'s
`@theme` block — no shared workspace links the two npm packages) and import it from
`agent-ui/src/main.tsx`; and swap that file's `@fontsource/inter/*.css` imports to
`@fontsource/archivo/*.css` (400/500/600/700), keeping `@fontsource/jetbrains-mono` as-is. Both
follow the exact pattern already proven working in that same file — no new mechanism to invent.

**Group 4 — Agent Games tab.**
✅ **Shipped 2026-09-21 (branch `claude/group-4-ui-redesign-fe56ca`).** Written up after the original scope below.
<br>**Shipped:** three new local-API routes — `POST /api/games/{id}/sync` (one route, `mode` = `sync`/`push`/`pull`; one sync at a time **per game** (deliberately not behind the global gate - see the review fixes below), a second press on the same game being a **409**, since a message reading "watch the activity feed" would look like success for a game the caller named; never forced, so a pull still refuses to overwrite unsynced local changes and a diverged push still becomes a conflict), `GET /api/games/{id}/state` (a proxy of the server's per-game state: head version, lease, open conflict — one small request, no disk work) and `GET /api/games/{id}/art?kind=grid|icon&w=` (the art proxy). Behind them `SyncEngine.SyncGameAsync` and `ApiClient.GetArtAsync`, which fetches a server-supplied URL only if it *resolves* to `/art/` on the same origin — a hostile server must not be able to aim this machine at another host (hardened after review; see below). In `agent-ui`: a Games nav item, `GamesView` (list and grid, search by name or folder, mode remembered in `localStorage`), `GameDetailView` (cover, chips, Sync this game / Push now / Pull latest / Check now, "On the server" and "On this device" cards), `Row` and `Seg` ported from `web`, and a search box in Add games.
<br>**Cover art needs a fetch-and-blob, not an `<img src>`:** the local API wants `X-SaveLocker-Token`, which an image request cannot carry. `useArt.ts` fetches with the header and shows a blob URL, one shared entry per game/kind/width (four requests at a time, entries expire - see the review fixes below). The proxy caches a game's art URLs for 5 minutes so a grid of covers is not one state request per image. No art (no SteamGridDB key) shows the game's first letter on a tile.
<br>**The `sync-status` trap was obeyed:** the list renders from `GET /api/games` only and never hashes a folder; the game page loads `/state` (cheap) on open and calls `sync-status` only from **Check now**.
<br>**Honest gaps against the plan's wish-list for the game page:** *versions* and *bytes sent last push* are not shown — no agent route returns a version list and nothing records last-push bytes, and inventing either was not on the table; the *launch command* is per-machine (Settings' Steam card), not per game. What is shown instead is what the agent and server really know. A per-game sync surfaces the same conflict pop-up as Sync all, but only for **that** game's conflict — the pop-up queues whatever it is handed, and another game's old conflict is not what the press was about.
<br>**A real bug caught by running it, not building it:** `.Produces<byte[]>(…, "image/*")` throws at startup (ASP.NET refuses wildcard content types), which killed the whole daemon — `dotnet build` and `tsc` were both clean. Also found live: `PullAsync` returns `false` for "already up to date" as well as for a refusal, so the first wording ("was not pulled") read as a failure on an ordinary no-op; the messages now say what is actually known.
<br>**Verified:** `dotnet build` (Linux agent, Windows agent), `agent-ui` `tsc -b && vite build` and `oxlint` (the same two pre-existing warnings), `api-types.ts` regenerated from a live daemon on :5190 and diffed (additive except one `@default Manual` on an enum that `GameDto` newly pulls into the agent's schema). Live against `tests/testenv.ps1` (Windows tray + the Docker console + `sgdb-stub.py`): the empty state, list and grid, search (including no match), the game page loading real server state, all three sync modes plus `bogus` → 400, unknown game → 404, bad `kind` → 400, `Check now` → in sync, and art arriving as a 96×96 blob through the proxy. **Not verified:** the WebView2 tray window, a Deck, light theme (Group 5 flips the default). Verified afterwards, see the follow-up note below: the new routes (`run-local-api-tests.ps1` §11) and a per-game sync landing on a real two-machine conflict.
<br>**Follow-up (2026-09-21, same branch):** `run-local-api-tests.ps1` §11 covers the three routes — 53 checks (was 30), 53/53 passing on this box. Token gate on all three; 404 for an untracked game and 400 for a bogus mode or `kind`, before the server is asked anything; `/state` relaying the server's record; an image coming through byte-identical with its content type and the thumbnail width asked for; and the part with an attack surface, a stub server naming `//evil.example/…`, a path climbing out of `/art/`, and a `text/html` body as a game's art — all refused, and the stub's own request log shows it was never asked for the hostile paths. Also the single gate: one game's pull is held open on the stub, and a second per-game sync gets **409 "already running"**, then the gate frees. **Mutation-checked:** with `ApiClient.GetArtAsync`'s two guards removed, three checks fail (the scheme-relative URL, the non-image, the stub-log one) — the traversal case alone still answered 404 because the stub 404s the collapsed path, and it is the request-log check that catches it. Not put in `run-agent-tests.ps1`: that suite drives the CLI, not the agent's local API. **Live, on the seeded two-machine conflict (`testenv conflict -Windows -Wsl`), on the WSL agent's UI:** the game page shows the Conflict chip and the "waiting on you" banner, **Sync this game** answers "has a conflict. Choose a side in Conflicts.", the pop-up opens with **only that game's** conflict, and *Decide later* lands on Conflicts. Ran the suite on scratch ports (5168/5167/5177/5178): its own defaults, 5188 and 5187, collide with `testenv.ps1`'s Windows tray and WSL's forwarded listener, which is a pre-existing overlap, not fixed here. Still open: version list and bytes-sent (now in [[Backlog]]), light theme (Group 5).
<br>**Review fixes (PR #46, 2026-09-21).** A code review of this PR found eight issues; all are fixed. `run-local-api-tests.ps1` §11 grew from 23 to 36 checks (the suite 53 → 66, 67 with a scanned candidate), and the new ones were **mutation-checked**: with the PR-head `ApiClient` / `AgentApiServer` / `SyncEngine` swapped back in they fail.
- **The art proxy was not the guard its comment claimed.** It followed a `302` to another origin *and took the machine key with it*; it buffered a chunked body of any size (the 16 MiB cap only read `Content-Length`); and its `..` string check passed `%2e%2e`. Now: its own client that **refuses redirects and sends no key at all** (`/art/` is served to anyone, so there is nothing to authenticate), the URL judged **resolved** (same authority, path under `/art/`), the body read against a cap, an image-type allowlist (png / jpeg / gif / webp / ico — never SVG, which can carry script), and a 20 s budget that covers the body as well as the headers (`ApiClient.GetArtAsync`).
- **A one-game sync must not hold the global gate.** `pre-launch-sync` and `post-exit-sync` answer 409 whenever it is held, on the premise "the running sync still converges" — true of Sync all, false of one game's sync. Holding it dropped an *unrelated* game's exit push and, with it, `OnGameExitAsync`'s lease release (a game's lease renewer stops nowhere else on exit). The route now gates **per game** — a second press on the same game is still a 409 — and is no longer tied to the request's lifetime: like `/api/sync`, a page that is reloaded or closed cannot abort a push halfway through its upload. A cancellation from an engine retirement (the server connection changed) no longer logs a stack trace.
- **Art did not refresh.** Art URLs carry `?v={ticks}`, but the agent's URL cache slid its 5-minute expiry on every hit and `useArt`'s cache never expired, so a cover re-picked in the console never appeared while the window lived. Now a fixed 5-minute expiry in both places (30 s for a failure, so "no art" is re-asked soon).
- **A grid of covers could starve the page.** One unbounded fetch per game, against an `ApiClient` timeout of 10 minutes, could fill a browser's few connections to the agent and queue the 1.5 s activity poll behind them whenever the server was unreachable (a Deck away from home). `useArt` now runs four at a time, drops a request nobody waits for any more (aborting one already in flight, which reaches the agent), and the art route has a 20 s budget. **Reasoned from the code, not reproduced** — there was no unreachable-server rig.
- Also: `SyncAllAsync` and `SyncGameAsync` share `PullThenPushAsync`; `GameSyncMode` lives beside `LaunchDecision`; `ConflictCard` uses `format.ts`'s time helpers (one UTC normaliser instead of three, which also fixes `formatTime` on a negative UTC offset); this plan's `plan.md` status table was brought up to date (the repo rule: the same session a group ships); §11's ports moved to 5211–5213 (5196 and 5186 sat inside `run-winagent-tests`' range and other suites' ports).
- **Not changed, still open:** `post-exit-sync` answering 409 while **Sync all** holds the gate skips `OnGameExitAsync` for that game — no exit push, no lease release, renewer left running. Pre-existing (Playnite Phase 5), not part of this PR's diff; now in [[Backlog]].
<br>**Original scope, kept for the record:**
The rest of Phase 5: the new Games view (list and grid, cover art, per-game page with Sync/Push/Pull),
the art proxy, and search in Add games. Split from Group 3 because it's the single largest *new* UI
in the plan and it doesn't fit alongside the foundation work. Needs Group 3's tokens **and primitives**
(`agent-ui/src/components/ui/`, `ui.css`); `Row` and `Seg` are not ported yet — port them from `web`'s.
**Also owns Phase 3 item 5**, moved here from Group 3 on 2026-09-20: "Sync this game" is a button on the
per-game page this group builds, and it is *not* UI-only after all — the agent's local API has no per-game
sync route (`POST /api/sync` takes no game filter; `pre-launch-sync` and `post-exit-sync` are launch-gate
routes with their own single-flight and fail-open contracts, not a manual sync). It, and the page's Push
now / Pull latest, need new `AgentApiServer` routes, an `api-types.ts` regeneration in `agent-ui`, and a
per-game entry point on `SyncEngine` (`PushAsync`/`PullAsync` already exist; `SyncAllAsync` loops them).
Obey the
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
