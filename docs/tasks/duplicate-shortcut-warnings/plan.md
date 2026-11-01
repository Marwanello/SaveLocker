# Duplicate-shortcut warnings outside doctor — plan

Planned 2026-10-14. **Built 2026-10-14** (branch `duplicate-shortcut-warnings`, rebased onto
`main@3150f05`): the shared detector (`SteamShortcuts.DuplicateNameGroups`/`DuplicatesFor`) now backs
the doctor note and both scanners; `ScanCandidate.DuplicateShortcuts` / `DuplicateShortcut` ride the
agent's wire (`CandidateDto`); agent-ui gained the `DuplicateChip` (ReleaseChip pattern) + the row
note, and Game Mode the warning tile + caption; `run-linux-tests.sh` gained the three
`/api/candidates` checks. Deviations from this plan as first written: the popover shows each
shortcut's own name, AppID and tracked marker only — the mockup's per-entry save-folder line is not
on the wire; the Game Mode caption shows on addable rows only. Suites: `run-linux-tests` **214/71**
vs untouched `main`'s **208/74** on this box — **zero new failures**, the 3 new checks pass, and the
71 are a pre-existing registration/startup cascade on this machine (identical leading failures on
`main`). Server, Agent.Linux, Windows Agent and `agent-ui` build clean; `api-types.ts` regenerated
from a scratch daemon (`--port 5190`). Research below was against `main` (`4290a2a`).

## Status

| Phase | Status |
|-------|--------|
| 1. Shared detector, `Doctor` refactor, `ScanCandidate` field, Linux scanner, API assertion | ✅ Shipped 2026-10-14 (PR #75) |
| 2. Windows scanner | ✅ Shipped 2026-10-14; live in WebView2 2026-11-01 |
| 3. agent-ui chip, Game Mode caption, `api-types.ts` | 🚧 Built; WebView2 passed 2026-11-01 (popover fix on `claude/recursing-spence-500fce`). **Open:** the Deck pass, see *Live check* |
| 4. Docs | ❌ Dropped: no KB change needed |

## Goal

Surface the doctor's duplicate-shortcut note where a user actually adds a game — a warning on the
affected Add Games candidate in `agent-ui` (and, small, the Game Mode row) — instead of CLI-only.
`doctor` stays as it is.

## What exists today (verified)

- **Detection is doctor-only, Linux-only, recomputed at run time:**
  `src/Agent.Linux/Doctor.cs:97–116` groups `ReadShortcutsAsync` results (all Steam roots) by
  `ManifestLoader.NormalizeName(x.Shortcut.AppName)` and reports groups whose distinct `AppId`s
  exceed one. The message is a note, not a problem (`Doctor.cs:103–105`), so exit code is unaffected
  — asserted by `tests/linux/run-linux-tests.sh:429–432`.
- **Windows has no doctor**, and `src/Agent/GameScanner.cs` (`ScanSteamShortcutsAsync:152–170`,
  `MergeDuplicates:85–93`) silently dedupes duplicates by `DedupeKey`.
- **`ScanCandidate`** (`src/Agent.Core/ScanCandidate.cs`) has sibling warning fields —
  `NotSyncable` (line 152), `UntouchedSeed` (146), `StatesOnly` (158) — but nothing for this.
  Losers are collapsed by `DedupeKey`; only the winner survives (`LinuxGameScanner.cs:42`,
  `GameScanner.ScanAsync:70–74`).
- **Wire:** `AgentApiServer.ToCandidateDtos:1866–1891` maps to `CandidateDto:2007–2037` (and already
  drops `SteamAppId`); agent-ui renders chips at `AddGamesView.tsx:526–541` and notes at 542–596;
  Game Mode's `DrawCandidateRow` (`Ui/UiApp.cs:1898–1978`) uses a `StatusTile` (Check / AlertTriangle
  / SearchCheck) and has no chips.
- **Fixture + test already exist** for the detection: `tests/linux/make-fixtures.py:536–540`,
  `run-linux-tests.sh:429–432`.
- **The backlog's evidence gate is met**: the MoonDeck investigation found six duplicated shortcuts
  on the maintainer's own Deck (HITMAN 3, Minit, Moving Out, Animal Crossing, Metal Gear, Waydroid).

## Design

1. **One shared detector in `Agent.Core`.** Add a helper on `SteamShortcuts` that takes the per-root
   shortcut lists both scanners and `Doctor` already read and returns duplicate groups
   (normalized name → distinct AppIds). Refactor `Doctor.cs:97–116` onto it — no behaviour change,
   existing string assertion stays green.
2. **`ScanCandidate` gains a trailing optional field** `IReadOnlyList<DuplicateShortcut>? DuplicateShortcuts = null`,
   one entry per same-named shortcut: `{ AppId, Name, Tracked }` — the shortcut's own vdf name, and
   `Tracked` on the dedupe winner (the one whose save folder resolves here; `SteamAppId` is already dropped
   on the wire). The dedupe winner carries the list; the losers still collapse as today. Trailing optional
   keeps every existing constructor call compiling.
3. **Both scanners set it.** Linux sets it from the rows it already has before the `DedupeKey` group;
   Windows sets it in `MergeDuplicates`. Same PR — the helper makes parity one line.
4. **Wire:** `CandidateDto` grows `DuplicateShortcuts` (additive, nullable). `ToCandidateDtos` maps it.
   `agent-ui/src/api-types.ts` is regenerated from the AGENT's OpenAPI — run a scratch daemon on a
   free port and generate against `:5190`, never the installed agent's `:5178` (see Gotchas).
   No `SaveLocker.Server` change, so its `openapi.json` is untouched.
5. **UI:**
   - `AddGamesView.tsx`: a `warn`-tone chip on the name row — `2 shortcuts · different AppIDs` — that
     behaves like the emulator `ReleaseChip` (`agent-ui/src/components/ReleaseChip.tsx`): hovering (or
     clicking, which pins it) opens a popover listing every same-named shortcut with its own name, its
     AppID (mono) and a **Will be tracked** marker on the winner; **no tooltips**. One note line in the
     existing note stack keeps the consequence short and names the tracked AppID (`map the other with
     --dir` to switch). The row stays selectable: this is a warning, not `NotSyncable`.
   - Game Mode (`Ui/UiApp.cs`): `AlertTriangle` as the row's status tile plus the same one-line
     caption when the field is set.
6. **Tests:** extend `run-linux-tests.sh` (or its API checks) to assert `duplicateShortcuts` — the
   AppIds, the shortcuts' own names, and the tracked flag — on `/api/candidates` for the existing
   fixture duplicate, alongside the doctor string check.

## Phases

1. Agent.Core helper + `Doctor` refactor + `ScanCandidate` field + Linux scanner + API assertion.
2. Windows scanner sets the same field.
3. agent-ui chip + Game Mode caption + `api-types.ts` regeneration.
4. Docs: update the Deck-hardware note? Not needed — behavior only becomes visible; no KB change.

Each phase is commit-sized; one PR for 1–3 is reasonable, with 4 optional.

## Verification

- `run-linux-tests.sh` — new assertion fails against pre-change code (mutation check), doctor string
  assertion unchanged.
- `run-agent-tests.ps1` (needs a fresh `Storage__DbPath`/`ArchiveRoot` — see CONTEXT note).
- `agent-ui` `tsc -b && vite build` + `oxlint`; regenerate `api-types.ts` and diff (only the added
  field).
- Windows parity: `run-winagent-tests.ps1` is the only suite that drives the Windows scanner
  (needs an interactive desktop — note when run).
- Live: the deck fixture aside, drive Add Games on the testenv Windows/WSL agents and confirm the
  chip appears only for the duplicated shortcut.

## Open questions

- Field shape: `DuplicateShortcuts` records `{ AppId, Name, Tracked }` (chosen, and mocked up) vs a
  bare id list. Records, because the UI shows each shortcut's own name and which one is tracked;
  roots stay doctor's concern.
- Should the chip list the AppIds? Yes — in the hover/click popover (name + AppID + tracked marker),
  the emulator `ReleaseChip` pattern; the chip itself stays short.
- One duplicate usually resolves a save path while the other does not: the warning still matters
  (the resolvable prefix may be the dead one). No special-case.

## Review fixes (2026-10-14, PR #75 review)

No blockers; seven smaller findings, all fixed on the branch.

- **Clicks inside the popover ticked the game.** An Add-games row is a `<label>`, and a click on the
  popover (a non-interactive descendant) activates its checkbox — selecting the game while the user
  only read it. The popover now swallows the click (`preventDefault`), the guard the row's other
  controls already carry.
- **The card clipped the popover.** `.sl-card` is `overflow: hidden`, so a duplicate row near the
  bottom of the list had its popover cut off. `.sl-card:has(.sl-release__pop) { overflow: visible; }`
  — the codebase's own `:has()` idiom, as `.sl-gamehead` already uses.
- **`Tracked` could name a shortcut the scan does not follow.** Windows' `MergeDuplicates` backfilled
  the list from a *loser* (only reachable when the winner is not a shortcut), and a MoonDeck shortcut
  reclassified `SteamInstalled` marked its own synthetic AppID while the candidate carried the real
  one. Both now mark no entry tracked, and the chip/note/Game Mode caption say so ("resolved its save
  folder another way") instead of naming an id the tracked game does not carry.
- **The popover interaction was a third copy** of ReleaseChip's hover/pin/outside/Esc logic; both now
  share `usePopover`, which also closes on leave with a 150 ms grace period — the pointer crossing the
  8 px gap between chip and popover used to close it before it could be read.
- **Counts.** The chip said "N shortcuts" while `DuplicatesFor` counts distinct AppIDs; the copy now
  says AppIDs, matching what the list shows. The redundant `length > 1` guards became `> 0`,
  consistent with the API's own `Count: > 0`.
- **"Hover the chip"** is wrong on touch; "Open the chip" now.
- **Tests.** New `DuplicateShortcutTests` (7; flipping the tracked marker fails two of them), and the
  suite's `/api/candidates` checks now assert the exact AppIDs, names and tracked flags instead of
  field presence.

**Verified:** `dotnet test` **559/559**; Agent.Core, Windows Agent and Agent.Linux build clean;
`agent-ui` `tsc -b && vite build` + `oxlint` clean; `run-appearance-consistency-tests` **50/50**;
`run-linux-tests` **282/2** as root in `unshare -n` (the 2 are the documented WSLg-session pair —
"no session: graphical session / D-Bus session bus reported no" — and both duplicate-shortcut checks
pass); both UI fixes proven in headless Chrome against the real CSS/DOM (the card clip and the
label-tick each reproduced with their fix removed, including a real trusted click). **Not verified:**
a live Deck/WebView2 pass of the chip. The 71-failure cascade the PR body's earlier numbers carried
was this box's stray `~/SaveLocker` daemon holding :5187; the netns run is the workaround.
Ready to move to `tasks/_done/` once the PR merges.

## Live check (2026-11-01)

**Windows agent UI (WebView2): passes after one fix.** Run through `testenv up -Only windows` on
v0.8.2-test (commit `1a773e2`), driven over WebView2's DevTools port (no desktop keystrokes), 900×600.

- Fixture: a throwaway `userdata\999999999\config\shortcuts.vdf` in the real Steam folder with Steam
  closed, removed afterwards. There is no Steam-root override, so this is the only way to feed the
  Windows scanner without touching the account's own `shortcuts.vdf`. It held a second `Minit` beside the
  account's real one, two `SaveLocker Dup Test`, two with a long name, and one single-shortcut control.
- `/api/candidates` and the rendered rows agree. `Minit` follows its real shortcut (AppID 2222329534,
  *Will be tracked*); the second is *Duplicate*. The note names the tracked AppID. The control and
  `EmulationStationDE` have no chip, so it appears only on duplicated shortcuts.
- Hover opens the popover, a click pins it, Esc closes it, a click inside it does not tick the row.
- **Bug found and fixed:** the popover opened behind Add games' sticky foot bar, hiding the second
  shortcut's marker and the whole note at the default window size; near the bottom of the page the
  window cut it off as well. The 2026-10-14 review fixed the card's clip, not this. The card now rises
  above the bar while a popover is open, and `usePopover` opens on the side with room, limits the height
  to it (scrolling inside) and shifts left if it would leave the page. `ReleaseChip` shares it.
  Re-measured with the row at the top, middle and bottom of the page for three games: all inside the
  window, and the note is on top or reachable by scrolling the popover. agent-ui has no unit-test
  harness, so this has no automated test. `tsc -b`, `vite build` and `oxlint` are clean.

**Deck: not verified.** Unreachable on 2026-11-01 at the start and again at the end: `deck@192.168.1.16`
is gone, nothing on 192.168.1.0/24 but this PC has port 22 open, and it is not on Tailscale. So the Deck
agent UI and the Game Mode tile and caption have never been seen live, and the task stays open.

Still to do on the Deck, with it awake and `testenv deck-config` pointing at it:

1. Agent UI: chip, note and popover on the real duplicates (HITMAN 3, Minit, Moving Out, Animal
   Crossing, Metal Gear, Waydroid), including the popover fix.
2. `savelocker ui`, Add games: the warning tile and caption.
3. Addable rows only. From the code, not from the screen: in `DrawCandidateRow` the caption is in the
   addable branch, and the tile loses to the *tracked* and *not syncable* tiles. So a tracked or
   not-syncable row gets no duplicate warning in Game Mode, which is what this plan says. agent-ui
   differs slightly: the note is hidden on enrolled rows, but the chip still shows on them.
