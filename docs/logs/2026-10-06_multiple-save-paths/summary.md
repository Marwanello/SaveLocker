# Multiple save paths per game — summary

> **Now blocking emulator saves** (maintainer, 2026-10-04): build this next, in its own session and
> branch off `main`; the `emulator-saves` branch (Phase 1, RetroArch) waits and is rebased onto it.
> See *What emulator saves needs from this* at the end.

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). **The plan
now exists: [`plan.md`](plan.md) (2026-10-04).** This file is kept as the research it was built from;
where the two disagree, `plan.md` wins. For example, the "proposed phased shape" below is superseded.

---

- **Multiple save paths per game.** Scoped 2026-08-20 (full research write-up: `../logs/2026-08-20_wine-case-insensitive-and-scoping.md`), not built — this is a maintainer scope decision (build the schema work, or keep today's single-path limitation), not something to just do. Some manifest games list more than one `files:` template — `ManifestLoader.ResolveSaveDirectories` (`src/Shared/ManifestLoader.cs:181`) already loops over every one of them and returns a full `IReadOnlyList<string>` — but essentially every real caller (`LinuxGameScanner.cs:279`, `AgentCli.cs:195` and `:672`, `CommandPoller.cs:237`, `GameScanner.cs:301`) immediately reduces that to `.FirstOrDefault()`, because the storage model the rest of the system is built on is single-path end to end, all the way down to the database's primary key.
  <br>**The design risk that gates everything else.** `ResolveSaveDirectories`'s own doc comment already carries a cautionary tale directly on point: DRAGON QUEST III has two manifest templates for the same game — one resolves to the real save folder, the other to a sibling `Config` folder — and this codebase was already burned once treating "multiple templates" as "resolve all of them," which silently picked the wrong one via `HashSet` ordering. **The manifest format does not disambiguate between "alternate locations, pick the one that resolves" and "complementary locations, sync all of them that resolve."** Stopping the `.FirstOrDefault()` calls is not enough by itself; the real question is a policy for that ambiguity, most plausibly by never auto-adopting more than one manifest-resolved candidate without explicit user confirmation of which ones are genuinely complementary.
  <br>**Why this is the most invasive schema change in the project's history, not a resolver tweak.** `MachineSavePath` (`src/Server/Data/Entities.cs:200-205`) is a plain `SavePath` string on a composite-key `(MachineId, GameId)` row — "one path per machine per game" is baked into the primary key itself, not just the column type, and the table has no FK constraints today (cleanup is hand-written `RemoveRange` calls in `SyncService.cs`), a pre-existing gap worth fixing in the same migration rather than separately. Every path-carrying wire DTO in `Contracts.cs` (`MachineSavePathDto.SavePath`, `GameDto.SuggestedSaveDir`) is a single string too — mechanically easy to widen (there's already a `string[]` precedent via `ExcludeGlobs` on the same DTOs), but ripples into `openapi.json` and the generated `web/src/api-types.ts`. Agent-side, `TrackedGame.SaveDirectory` (`src/Agent.Core/AgentConfig.cs:454`) is one non-nullable string, and `SyncEngine`'s push/pull is hard-coded to one root at the type level — `SaveArchive.CreateArchive`/`HashDirectory`/`RestoreArchive` all take one `sourceDir`/`targetDir` string, not a list. `CommandPoller.ReconcileGamesAsync`'s reconciliation logic (~10 branches of single-path comparison/reporting) would need real rework, not just a type change.
  <br>**`SaveArchive` is the single hardest piece.** Turning "zip one directory" into "zip N directories into one archive" needs a namespacing scheme so two roots' relative paths can't collide (`Documents/save.dat` from root A vs `AppData/save.dat` from root B), and the restore-side safety logic — the stale-file delete pass, the symlink guard, the nested-restore-depth guard — is all written in terms of one root and would need re-deriving per-root rather than per-archive.
  <br>**Proposed phased shape**, once the ambiguity policy above is settled: (1) data model — a child table replacing `MachineSavePath`'s composite-key single row (or a JSON-encoded ordered list if a new table is unwanted), `TrackedGame.SaveDirectory` → list, wire DTOs reusing the `ExcludeGlobs` `string[]` precedent; (2) `SaveArchive` namespacing-and-restore-safety — its own design pass, not a bullet, given the safety mechanisms involved; (3) scanners — stop reducing to `.FirstOrDefault()`, surface every resolved candidate, require explicit confirmation per the ambiguity policy; (4) UI — `GameDetail.tsx`'s per-machine path table (one text input per row) becomes a nested list editor.

---

## What emulator saves needs from this (added 2026-10-04)

`emulator-saves` Phase 1 (branch `emulator-saves`, paused) added a **per-game include scope**:
`Game.IncludeGlobs` on the server (migration `AddGameIncludeGlobs`), `TrackedGame.IncludeGlobs` on the
agent, and include-scoped hash/archive/**restore** in `SaveArchive` — one RetroArch game is the shared
saves folder narrowed to `<rom>.srm`/`<rom>.rtc`. Its next step, save states (Phase 1b), is a second
folder of the same game. For that, this feature must provide:

1. **The include scope per path, not per game.** The saves path is scoped to `<rom>.srm` + `<rom>.rtc`, the
   states path to `<rom>.state*`, both inside folders every other ROM shares. Move `Game.IncludeGlobs` onto
   the per-path definition (server-side, identical on every machine) rather than adding a second mechanism.
2. **A stable per-path key shared by every machine** ("saves", "states") that names the path's namespace in
   the archive. Each machine's absolute paths differ (Deck Flatpak vs Windows), so restore must match paths
   by key, never by position or by a literal path.
3. **The scoped restore kept per root.** The delete pass removes local files absent from the archive; Phase 1
   scopes it to the game's include patterns (`IncludeGlobTests.Scoped_restore_*`, mutation-checked). With N
   roots that guarantee must hold per root and per that root's scope, alongside the symlink and
   nested-depth guards.
4. **A path that is missing on one machine** (no states folder yet, or a standalone RetroArch whose states
   live somewhere not found) must neither fail the push nor be recorded as "everything in it was deleted".
   A pull creates it.
5. **Scanner-declared paths may be auto-adopted.** The manifest ambiguity policy above (alternate vs
   complementary templates) is about Ludusavi templates. An emulator reader knows its paths are
   complementary, so its candidates can carry several paths without a confirmation step.
6. **Single-path games keep their archive layout**, or every stored version needs migrating. Phase 1's
   RetroArch games are unreleased, so they can change layout freely as long as the rebase happens before a
   release.

Later emulator phases need the same thing: PCSX2 (memcards + `sstates`), Dolphin (`GC` + `Wii`),
DuckStation (memcards + savestates), RPCS3 and Eden.
