# Duplicate-shortcut warnings outside doctor — plan

Planned 2026-10-14. Not started. Research below is against `main` (`4290a2a`).

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
