# Task — Emulator save detection and sync

**Created:** 2026-08-26

**Target:** `src/Agent.Core/ScanCandidate.cs`, `src/Agent.Core/SaveDirSanity.cs`,
`src/Agent.Core/AgentApiServer.cs` (`CandidateDto`), `src/Shared/SaveArchive.cs`, new readers under
`src/Agent.Core/` (one per emulator family, listed per phase below), `src/Agent/GameScanner.cs`,
`src/Agent.Linux/LinuxGameScanner.cs`, `agent-ui/src/components/AddGamesView.tsx`,
`src/Agent.Linux/Ui/UiApp.cs`. No server/database changes in any phase (see Decisions §1).

**Goal:** detect and sync emulator saves (RetroArch, PCSX2, Dolphin, DuckStation, PrimeHack, RPCS3,
Xenia, and Nintendo Switch via Eden) the way EmuDeck and Steam ROM Manager / EmulationStation
actually set machines up — one server `Game` per ROM, individually trackable — with an equally
reliable detection path on Windows via EmuDeck for Windows, and without silently syncing a
save file/memory card that is actually shared across an entire console's library.

This is a **multi-phase task, mirroring the multi-session structure other large items in this
vault use** (see `Backlog.md` → *Native Linux save support*). Execute **one phase per session**,
verify it per that phase's own Verify section, and commit before moving to the next — do not
continue past a phase's own stopping point unless explicitly instructed to.

---

## Motivation

This is `Backlog.md` → **"Emulator saves"**, scoped in full and widened at the maintainer's request
to also cover Nintendo Switch (Eden — Ryujinx/Yuzu are dead upstream, see Phase 6), RPCS3 (PS3),
PrimeHack (a Dolphin fork), and Xenia (Xbox 360). Ludusavi (the manifest SaveLocker's whole existing
detection model is built on) has **zero emulator coverage**, so none of this is visible to discovery
today, despite being — per the maintainer's own framing — "a large share of what people actually
play" on a Deck. Keep it simple: reuse every existing primitive this codebase already has rather
than inventing new mechanisms, and cut real scope (save states, Ryujinx) where the cost doesn't
justify it.

---

## Background — what's already confirmed, so it isn't re-derived mid-implementation

**Existing architecture this task reuses, not replaces:**
- `IGameScanner.ScanAsync()` (`src/Agent.Core/Platform.cs`) is the discovery contract both hosts
  implement. Every new emulator source below is wired in as one more independent, failure-isolated
  source inside `GameScanner.ScanAsync` (Windows) / `LinuxGameScanner.ScanAsync` (Linux) — same rule
  as WA-11 ("discovery is per-source best-effort; one bad source cannot fail the scan").
- `ScanCandidate` (`src/Agent.Core/ScanCandidate.cs`) is a record that has grown additively before
  (`PrefixPath`, `Store`, `SuggestedProcessName`) — this task adds three more fields the same way
  (§ScanCandidate additions, below).
- **Heroic's integration (`HeroicRoots.cs` + `HeroicLibrary.cs`) is the direct structural precedent**
  for "a third-party library format, read by one small static reader per source, each independently
  fault-isolated." Every new emulator reader in this task follows that shape, not a shared
  polymorphic interface — `HeroicLibrary` already proves one parser per source degrades gracefully
  (a malformed file yields zero games from *that* source, never a failed scan) where one abstraction
  covering RetroArch's playlists, PCSX2's directory walk, and RPCS3's ID-keyed folders would not.
- `SteamShortcuts.MoonDeckAppId` is the precedent for "a shortcut's `Exe` launches a wrapper, not
  the real game — recover identity from `LaunchOptions`." This is structurally the same problem as a
  Steam ROM Manager shortcut (`Exe` = the emulator, not the ROM) — deliberately **not** built in this
  task (see Phase 7's deferred item) because SRM's actual argument format hasn't been captured from
  a real install yet, and — per the EmuDeck research below — it turns out to only matter for a
  subset of installs anyway.
- `SavePathGuard` (hard floor) / `SaveDirSanity` (heuristic tier, overridable) is the existing
  two-tier validation this task's one new safety check (the shared-memory-card warning, Phase 2)
  plugs into — no new validation mechanism is introduced.
- **`Game.Platform` is deliberately NOT added.** A RetroArch `.srm`, a PS2 memory-card block, or a
  PS3/Xbox 360/Switch title-ID save folder all encode the *emulated console's* native save format —
  a spec the emulator itself must reproduce byte-identically on every host OS. This is unlike the
  *other* platform-isolation backlog item (native-Linux game builds), whose save format is an
  unversioned per-developer choice with no cross-platform contract. Emulator saves need no
  OS-isolation field and are the first source in this codebase that can sync Deck↔Windows directly.
  <br>The real footgun is **cross-core/cross-version, not cross-OS**: a save *state* is tied to the
  exact core/build that wrote it, unlike a save *file* (SRAM/memory card/title-ID data), which
  follows the stable hardware format. **Every phase in this task syncs save files only — save states
  are out of scope everywhere**, left as a separate future item once that risk gets its own design
  pass.
- **No server, `Entities.cs`, `GameDto`, or migration changes anywhere in this task.** "A game is
  defined once on the server" already covers one ROM = one `Game`, exactly like one Heroic title.

**EmuDeck's real folder layout, confirmed by reading its own documentation
(`emudeck.github.io`, `manual.emudeck.com`) directly rather than trusting a guess:**
- The `Emulation/{bios,hdpacks,roms,saves,storage,tools}/<system>/` layout is **confirmed identical
  between SteamOS and EmuDeck for Windows** — the docs state this explicitly. This is why EmuDeck
  (not RetroBat) was chosen as the Windows target: the `Emulation/roms/<system>/` and
  `Emulation/saves/<emulator>/` readers this task builds are shared, path-for-path, across both
  scanners — only OS-specific root-finding differs.
- **`Emulation/saves/<emulator>/` is a stable, documented per-emulator save root** —
  confirmed by name for RetroArch (`Emulation/saves/retroarch/saves`), PCSX2
  (`Emulation/saves/pcsx2/saves`), Dolphin (`Emulation/saves/dolphin/Wii` and `/GC`), DuckStation
  (`Emulation/saves/duckstation/saves`), Yuzu (`Emulation/saves/yuzu/`), and Cemu
  (`Emulation/saves/Cemu/saves/`, not in scope here but shows the convention generalizes). This means
  **no need to parse each emulator's own config file just to find the save root** when an EmuDeck
  install is present — reading `retroarch.cfg`/`PCSX2.ini`/etc. is only needed as the
  standalone-install fallback (no EmuDeck root found), or — for PCSX2/Dolphin/DuckStation — to read
  the per-game-memory-card *setting*, which is a mode, not a location.
- **NOT confirmed in the documentation read**: PrimeHack, RPCS3, Eden, and Xenia did not appear by
  name in the save-management page. Each phase below that touches one of these keeps its own
  "capture a real install before wiring the fast path" step for exactly this reason — do not assume
  the `Emulation/saves/<name>/` convention extends to them without checking.
- **`Emulation/saves/` entries are typically symlinks**, and EmuDeck's own docs warn that backing up
  the symlink itself (not its target) loses the data. `SuggestedSaveDir` must store the *resolved*
  real path, not the symlink path — the same "canonicalize, don't trust the stored string" instinct
  WA-02 already established for `SavePathGuard`.
- **EmuDeck's own "Level of integration" choice matters for scope**: **Low integration** makes
  EmulationStation DE the launcher, added to Steam as a *single* non-Steam entry (no per-ROM Steam
  shortcuts at all); **High integration** uses Steam ROM Manager to add *each ROM* as its own
  non-Steam entry. The folder-walk + native-library-file detection this task builds works under
  **both** levels (it never depends on Steam) — the deferred SRM-shortcut-identity item (Phase 7)
  only ever improves launch/exit lifecycle precision for High-integration installs specifically, it
  is never needed for detection coverage.

**The one correction already made against a wrong initial assumption** (verified, not guessed,
against `src/Shared/SaveArchive.cs:504-533`): scoping one `TrackedGame` to a single file inside a
directory shared by many ROMs (RetroArch's `savefile_directory`) **cannot** be done with
`ExcludeGlobs` negation (`["*", "!<romname>.srm"]`) — `ExcludeGlobs` only ever calls `AddExclude`
against a matcher that already includes everything; there is no negation semantics, and the proposed
globs would produce an *empty* archive, not a one-file one. The real fix is a small, genuine
addition (§ScanArchive changes, Phase 1).

---

## New `ScanCandidate` fields (built once, in Phase 1, reused by every later phase)

```csharp
// ScanSource gains:
Emulator

// ScanCandidate record gains, additive with defaults (same pattern as PrefixPath/Store before it):
string? EmulatorName = null,   // "RetroArch", "PCSX2", "Dolphin", "PrimeHack", "RPCS3", "Xenia",
                                // "Eden", … — free text, deliberately not a closed enum (Phase 6
                                // shows how fast this list actually changes)
string? EmulatorSystem = null, // "snes", "psx", "gamecube", "switch", "ps3", "xbox360", … —
                                // EmuDeck/ES-DE's own vocabulary, reused directly as the UI
                                // sub-filter key in Phase 7
string? EmulatorCore = null,   // libretro core name (RetroArch only); null for standalone emulators
```

Wire `CandidateDto` (`src/Agent.Core/AgentApiServer.cs`) gains `EmulatorName`/`EmulatorSystem` (skip
`EmulatorCore` — internal/Doctor-only, not UI-facing until there's a reason).

---

## Execution order (one phase per session)

### Phase 1 — `SaveArchive` include-glob support, then RetroArch (both OSes), save files only

The anchor phase: RetroArch has a real per-ROM library (its own `playlists/*.lpl` JSON files, one
per system, giving real titles — not filename-guessing) and native per-ROM save naming
(`<savefile_directory>/<romname>.srm`, or `.../<core>/<romname>.srm` with "Sort Saves By Core," on
by default under EmuDeck) — no per-game-folder configuration needed at all, unlike every emulator in
Phase 2. It's also the default frontend for most of what EmuDeck actually runs.

1. **`SaveArchive` addition (build and test this in isolation first — every later phase depends on
   it)**: add an opt-in `IncludeGlobs` parameter mirroring `ExcludeGlobs`'s shape, threaded through
   `EnumerateRelativeFiles`/`HashDirectory`/`CreateArchive`/`ListFiles` — when present, it replaces
   the default `AddInclude("**/*")` with the caller's own include patterns before excludes apply.
   This is what lets one `TrackedGame` be scoped to a single `<romname>.srm` inside a directory
   shared by every other ROM's saves.
2. **Config-root discovery**, EmuDeck fast path first: `Emulation/saves/retroarch/saves` directly
   (confirmed identical path on both OSes, no `retroarch.cfg` parsing needed). Standalone fallback
   (no EmuDeck root found): a new small parser for `retroarch.cfg` (flat `key = "value"` text,
   structurally simpler than `SteamTextVdf.cs`'s tokenizer but the closest existing precedent) for
   `savefile_directory` (used) and `savestate_directory` (read, unused — save states are out of
   scope). Config roots to try: Linux native (`~/.config/retroarch/`), Linux Flatpak
   (`~/.var/app/org.libretro.RetroArch/config/retroarch/`), Windows standalone
   (`%APPDATA%\RetroArch`). An empty `savefile_directory` value means "save beside the ROM," a real
   case, not an error.
3. **Symlink resolution**: `Emulation/saves/` entries are typically symlinks (confirmed via EmuDeck's
   own docs) — resolve to the real target path before storing as `SuggestedSaveDir`, never store the
   symlink path itself.
4. **Per-ROM enumeration**: read RetroArch's own `playlists/*.lpl` files for `path` (ROM file),
   `label` (real title — use this over any filename heuristic), `core_path`/`core_name`.
5. **Per-ROM save selection**: use the new `IncludeGlobs` (step 1) to scope each `TrackedGame` to
   `<romname>.srm` (and, if "Sort Saves By Core" is detected active, `<core>/<romname>.srm`) inside
   the shared save directory — the directory is real and shared, the include-glob is what makes one
   `TrackedGame` single-ROM.
6. Files: `src/Shared/SaveArchive.cs`, `src/Agent.Core/ScanCandidate.cs`, new
   `src/Agent.Core/RetroArchConfig.cs` + `RetroArchPlaylists.cs` (parallel to
   `HeroicRoots.cs`/`HeroicLibrary.cs`), wired into `src/Agent/GameScanner.cs` and
   `src/Agent.Linux/LinuxGameScanner.cs`, `src/Agent.Core/AgentApiServer.cs` (`CandidateDto` fields).

**Verify:**
- `SaveArchive`'s `IncludeGlobs` in isolation first: archive a directory with several files, confirm
  only the included pattern lands in the zip and the hash.
- Fixture-based test (a captured `retroarch.cfg` + sample `.lpl` files, parallel to `HeroicLibrary`'s
  own fixture tests) for the parser and dedupe logic.
- Real hardware pass against an actual EmuDeck (Deck) and EmuDeck-for-Windows install.

### Phase 2 — PCSX2 / Dolphin / DuckStation + the shared-memory-card `SaveDirSanity` warning

This is where the real data-safety risk in the whole task lives, so it comes right after the
anchor phase, before anything else builds on top of unproven scaffolding.

1. **EmuDeck fast path, confirmed**: `Emulation/saves/pcsx2/saves`, `Emulation/saves/dolphin/Wii`
   and `/GC`, `Emulation/saves/duckstation/saves`. Standalone fallback reads each emulator's own
   `.ini`/config file, same shape as Phase 1's RetroArch fallback.
2. **The granularity quirk, the reason this phase exists**: each of these emulators' *default*
   memory-card mode is one shared file per card slot covering the *entire* console library —
   restoring an old version would silently restore every other game that touched that card, not
   just the one being tracked. Each has a **per-game mode** (PCSX2: memory card type "Folder" +
   "Automatically manage saves based on running game"; Dolphin/DuckStation have their own per-game
   equivalents) that EmuDeck enables by default but a hand-configured standalone install typically
   does not. **Read the relevant config key to know which mode is active before deciding how to
   enumerate saves** — this one read gates everything else for these three emulators. When per-game
   mode is not confirmed active, leave `SuggestedSaveDir = null` rather than guess (WA-08's "admit
   ignorance" precedent) — the candidate feeds the diagnostic below instead.
3. **New `SaveDirSanity.Inspect` check**: judge the directory's actual *shape* (a flat card file
   directly in a memcard root vs. a per-game-keyed subfolder), not a stored config flag — same
   philosophy as the existing Wine-prefix check. This is heuristic-tier (overridable), not a hard
   refusal, per the existing two-tier model. It reaches every save-folder confirmation surface for
   free because `Inspect` is already wired into `/api/games/{id}/folder` (both hosts' folder
   pickers) and Linux `Doctor.cs`. Message style matches the existing ones, e.g.: *"'memcards' looks
   like a SHARED PS2 memory card, not a per-game save. Pulling an older version here would restore
   EVERY game that used this card. Enable per-game memory cards in PCSX2 (Storage → Folder +
   Automatically manage saves) and re-map to the per-game folder it creates."*
4. **Per-ROM enumeration fallback** (no library-manifest analog to RetroArch's playlists exists
   here): filtered walk of `Emulation/roms/<system>/` by known extension per system, with a
   filename-cleanup helper (strip `(USA)`/`(Rev 1)`-style tags) — **write this helper once here, it
   is reused again in Phase 4 (Xenia) and Phase 5 (Switch)**.
5. Files: `src/Agent.Core/SaveDirSanity.cs` (new check), new `Pcsx2Config.cs`/`DolphinConfig.cs`/
   `DuckStationConfig.cs`, wired into both scanners.

**Verify:** fixture tests for each config reader (shared vs. per-game mode detection) and for the
`SaveDirSanity` check against synthetic shared-file and per-game-folder layouts; real-hardware pass
on whichever emulator(s) are actually available.

### Phase 3 — `gamelist.xml` name enrichment (EmulationStation / EmuDeck's frontend)

Pure name-quality improvement on top of already-working Phase 1–2 detection — real titles instead of
cleaned-up filenames for Phase 2's standalone-emulator candidates specifically (RetroArch candidates
already have real names from playlists).

1. New small XML reader for `<game><path>`/`<name>` only — no other `gamelist.xml` fields needed.
2. Consumed by Phase 2's (and later Phase 4–5's) scanners as an optional name-override lookup keyed
   on ROM path.
3. Files: new `src/Agent.Core/GamelistXml.cs`.

**Verify:** fixture `gamelist.xml` samples; name-match test against Phase 2's filename-derived names.

### Phase 4 — PrimeHack (reuses Phase 2's Dolphin reader against a second config root)

The cheapest addition in this task. PrimeHack is a Dolphin fork (Metroid Prime Trilogy mouselook)
with its **own separate config/data root** (confirmed: Flatpak app id `io.github.shiiion.primehack`
on Linux, distinct from mainline Dolphin) but otherwise inherits Dolphin's exact save/memory-card
mechanics unmodified — same quirk, same `.ini` key, same Phase 2 `SaveDirSanity` warning.

1. `DolphinConfig.cs` (Phase 2) gains a second config-root candidate list — no new reader needed.
2. Tag candidates `EmulatorName = "PrimeHack"` (not `"Dolphin"`) so the two are never confused in
   Phase 7's UI sub-filter.
3. **Unconfirmed, check before wiring the EmuDeck fast path**: PrimeHack's own
   `Emulation/saves/<name>/` subfolder name did not appear in the EmuDeck documentation read for
   this task (only mainline Dolphin's `Emulation/saves/dolphin/` was confirmed) — capture the real
   name from an actual EmuDeck-managed PrimeHack install first. The standalone fallback needs no such
   confirmation since it reads PrimeHack's own `.ini` directly, same as Dolphin's.

**Verify:** point Phase 2's existing fixture tests at a captured PrimeHack config-root sample.

### Phase 5 — RPCS3 (PS3) and Xenia (Xbox 360): title-ID-keyed saves, already per-game by design

Both organize saves in a folder keyed by an opaque per-game ID — RPCS3:
`dev_hdd0/home/00000001/savedata/<game-id>/`; Xenia: `content/<xuid>/<title-id>/<content-type>/`
(older/simpler layouts exist too and should be tried as a fallback). **Genuinely lower risk than
Phase 2**: the ID-keyed folder already *is* the per-game boundary every time, no emulator setting has
to be right first, no `SaveDirSanity` warning is needed for either. The real new problem is naming —
`NPUB90001` or a 16-hex-digit title ID isn't a name a user recognizes, and neither emulator has a
manifest-shaped library file the way RetroArch has playlists.

1. **Unconfirmed, check first**: neither RPCS3 nor Xenia appeared in the EmuDeck save-management docs
   read for this task (which named RetroArch/PCSX2/Dolphin/DuckStation/Yuzu/Cemu specifically) —
   confirm whether EmuDeck manages either at all before assuming an `Emulation/saves/<name>/` fast
   path exists. If it doesn't manage them, standalone config-root discovery is this phase's *primary*
   path, not a fallback — a real structural difference from every earlier phase, worth settling
   before writing the readers, not discovering mid-implementation.
2. **RPCS3 naming**: read `PARAM.SFO` from the matching `dev_hdd0/game/<game-id>/` install folder —
   a small, well-documented binary format with a `TITLE` field, in the same tradition as this
   codebase's other hand-rolled binary parsers (`SteamVdf.cs`). New `ParamSfo.cs`.
3. **Xenia naming**: no equivalent sidecar exists. Fall back to Phase 2's filename-cleanup helper
   over `Emulation/roms/xbox360/`, matched to a save folder's title ID via the ISO/XEX filename's own
   embedded ID where the ROM set names it that way (common redump-style convention — small regex,
   same spirit as the tag-stripping heuristic). Where no match is found, show the raw title ID as the
   name (WA-08's "admit ignorance" precedent) — the user can rename post-enrollment as for any game.
4. Files: new `RpcsThreeConfig.cs` + `ParamSfo.cs`; new `XeniaConfig.cs` (reusing Phase 2's
   filename-cleanup helper).

**Verify:** fixture `PARAM.SFO` sample for the RPCS3 title-name test; fixture content-folder layout
for Xenia's ID-to-ROM matching.

### Phase 6 — Nintendo Switch (Eden primary; Ryujinx/Yuzu explicitly out of scope)

The most volatile part of this task — scope narrowly, revisit before build rather than over-design
now. **Yuzu shut down in March 2024 and Ryujinx followed that October, both taken down directly by
Nintendo; by early 2026 Nintendo's legal campaign had reportedly reached over a dozen more forks.**
The actively-maintained option worth targeting today is **Eden**, but which fork is "current" is
genuinely unstable ground — keep config-root discovery a small, easily-edited table (one entry per
known fork), not anything more structural, so swapping which name is current doesn't require
touching the scanner's shape.

1. **Confirmed technical split that matters**: Yuzu (and, by inheritance, Eden — forked from Yuzu's
   lineage) keys saves by TitleID (`saves/<titleid>/`) — the same lower-risk shape as Phase 5.
   **Presumed for Eden specifically, not yet verified against a real install.** Ryujinx does **not**:
   it uses an opaque sequential save-folder ID plus a binary key-value archive (`imkvdb.arc`)
   mapping TitleID → save folder — a materially harder new parser, for software with no path
   forward. **Do not build Ryujinx-specific detection** — an existing Ryujinx save can still be
   tracked today via ordinary manual folder browse; the binary-format investment isn't worth it for
   a dead project.
2. **Do not start implementation without first capturing a real Eden install's actual save-folder
   layout.** The TitleID-keying assumption is inherited from Yuzu's documented behavior, not
   confirmed against Eden's own code. Also unconfirmed: whether EmuDeck's current build manages Eden
   under `Emulation/saves/eden/`, still calls it `Emulation/saves/yuzu/` for compatibility (the only
   name the docs actually confirmed), or doesn't manage it at all — settle this from a real install,
   not by assumption, given how fast this ecosystem is moving.
3. **Naming**: no local manifest exists, so match TitleID-keyed save folders against
   `Emulation/roms/switch/` filenames (Switch ROM sets commonly embed the TitleID in brackets — same
   small regex-extraction spirit as the existing tag-stripping heuristic), falling back to the raw
   TitleID as the name when no match is found.
4. Files: new `SwitchEmulatorConfig.cs` (small config-root table, Eden entry only initially;
   save-folder walk keyed by TitleID; filename-based ID matching, reusing Phase 2's helper).

**Verify:** fixture tests only after a real Eden install's layout has been captured — this phase
should not proceed past that capture step on assumption alone.

### Phase 7 — UI: Emulator filter + Game Mode mirror

Last, after every detection phase, because the Backlog stub already frames this as the finishing
touch ("the row is built to take another entry") once real candidates exist to filter, and because
it's the natural point to surface `EmulatorSystem` as a Heroic-`STORES`-style sub-breakdown (by
console). No further data-model work needed — `EmulatorSystem` is already free-text (Phase 1), so
every value from Phases 1–6 flows through the same sub-filter mechanism Heroic's store breakdown
uses today.

1. `agent-ui/src/components/AddGamesView.tsx`: `FILTERS` gains `'emulator'` (`c.source ===
   'Emulator'`), a `STORES`-shaped sub-breakdown keyed on `emulatorSystem`.
2. `src/Agent.Linux/Ui/UiApp.cs`: `AddFilter` enum + `MatchesFilter`, mirroring the React side by
   the existing convention (nothing shares code between the two UIs today).
3. Regenerate `agent-ui/src/types.ts` from Phase 1's `CandidateDto` changes.

**Verify:** the Backlog already flags that nothing tests the Heroic store sub-chips — add real
coverage for these new chips rather than repeat that gap.

---

## Deferred, not built in this task

**Recovering ROM identity from a Steam ROM Manager shortcut's `LaunchOptions`** (structurally the
same problem `SteamShortcuts.MoonDeckAppId` already solves for MoonDeck) — needs a captured
`shortcuts.vdf` sample from a real SRM-configured, High-integration EmuDeck install first. This only
ever improves launch/exit lifecycle precision for that subset of installs; every integration level
and every standalone install is already fully covered for *detection* by the folder-tree scanning in
Phases 1–6, which never depends on Steam. Flag as a Backlog follow-on once a sample exists.

**Save states** — out of scope for every emulator in every phase (see Background, `Game.Platform`
discussion). A savestate is tied to the exact core/build that wrote it, a materially different and
unaddressed risk from a save file's stable hardware-format guarantee. Needs its own design pass.

**Ryujinx-specific detection** (Phase 6) — dead upstream project, materially harder binary format
(`imkvdb.arc`), not worth the investment. Manual folder-browse already covers an existing install.

---

## Done when

- Each phase above is built, verified per its own Verify section, and committed separately.
- No phase silently tracks a shared memory card/save file as if it were one game's save — Phase 2's
  `SaveDirSanity` warning is in place and tested against both shared and per-game layouts before
  Phase 4 (which depends on it) begins.
- Every `Emulation/saves/<name>/` "fast path" assumption for PrimeHack, RPCS3, Xenia, and Eden
  (Phases 4–6) is confirmed against a real captured install before being wired in, not shipped on
  the strength of the RetroArch/PCSX2/Dolphin/DuckStation/Yuzu precedent alone.
- `EmulatorName`/`EmulatorSystem` flow end-to-end from scan through to the Phase 7 UI filter row and
  its Game Mode mirror, with test coverage neither host's existing Heroic-store sub-chips have today.
