# Registry-based saves — summary

Seeded 2026-09-08 from `docs/Backlog.md` during the vault reorg (`chore/vault-docs-reorg`). No plan file exists yet — this is the backlog entry verbatim, kept as the starting point for a future `plan.md`.

---

- **Registry-based saves.** Scoped 2026-08-20 (full research write-up: `../logs/2026-08-20_wine-case-insensitive-and-scoping.md`), not built. The Ludusavi manifest's `registry:` section is invisible end to end today: `ManifestLoader`'s YAML DTO (`src/Shared/ManifestLoader.cs:245-257`) has no `Registry` property at all — not even an unused one — so `.IgnoreUnmatchedProperties()` silently drops a game's `registry:` block during parse. Even the detection test harness's deliberately-richer DTO (`tests/detection/SaveLocker.DetectionHarness/ManifestModel.cs`) has no registry field either, so there is no partial groundwork anywhere to build on — adding the property (parsed and visible, independent of whether anything acts on it yet) is step zero.
  <br>**Two genuinely different problems hide under one line, because SaveLocker runs on two hosts.** Native Windows (`src/Agent/`) is straightforward: `Microsoft.Win32.Registry` talks to the real registry directly, and the existing pattern (`GameScanner.cs:82-109`'s `ReadRegistryString` — try/catch narrowed to `UnauthorizedAccessException`/`IOException`/`SecurityException`, null-safe `OpenSubKey`/`GetValue`, already used for finding Steam's install path) is directly reusable for a manifest-declared `registry:` key. Linux/Proton (`src/Agent.Linux/`) is hard: the agent is a native linux-x64 process, never running under Wine itself, and typically needs to read a game's Proton prefix registry *without* launching Wine or that prefix's process, often across many different prefixes. Wine transparently backs `Microsoft.Win32.Registry` calls with a prefix's own `user.reg`/`system.reg` — but only from *inside* that specific prefix's own Wine process, which is not the position this agent is in. The realistic path is hand-parsing Wine's plain-text `user.reg`/`system.reg` format directly — `[Registry Key Path] timestamp` section headers, `"Name"="value"`/`dword:`/`hex:` lines, `@=` for the default value — which **does not exist in this codebase in any form today**. The closest existing pattern for "hand-write a parser for a semi-documented text config format" is `SteamTextVdf.cs`'s tokenizer for Steam's KeyValues format — a reasonable structural template, but the Wine `.reg` grammar itself is unrelated and would be new code either way.
  <br>**No archive story either.** `SaveArchive.cs` is zip-of-real-files throughout — `CreateArchive`, `HashDirectory`, and `RestoreArchive` all walk real files under a real directory, with no extensibility hook for a non-file member. Supporting registry data would need a reserved-path convention (e.g. an exported `.reg`-shaped blob at a fixed relative path inside the same zip) with explicit special-casing in all three of those methods to skip that path in the existing file-diff/symlink-guard logic and route it through a registry writer on restore instead of `File.Copy` — new logic in three places, not a drop-in extension.
  <br>**Restore is the dangerous direction here too**, mirroring the native-Linux item's own push-is-safe/pull-is-not framing: writing an untrusted registry blob back is a new kind of hostile-input surface the existing zip-slip/symlink hardening in `Decisions.md` doesn't cover at all — worth its own threat-modeling pass before restore is attempted, not an assumed extension of the existing archive guards.

---

## Competitor research (2026-10-18) — simpler archive story, Windows first

**Priority: High.** From [[logs/2026-10-18_competitor-gap-analysis]], after reading the code of OpenSave, Hoard and
Ludusavi.

- **The archive needs no special-casing.** OpenSave (MIT, `internal/winreg/materialize.go`,
  `internal/snapshot/registry_capture.go`) writes a game's registry keys to a plain `registry.json` file in a
  folder it owns, and registers that folder as one of the game's extra save locations. From then on hashing,
  the manifest, conflict detection and transfer treat it like any other file. Their notes give the reasons:
  JSON rather than `.reg` (a `.reg` runs on double-click and its escaping differs per value type); keys sorted and
  spellings folded (`HKCU`/`HKEY_CURRENT_USER`, case) so two machines capture identical bytes; a requested key that
  does not exist is recorded as `missing`, not dropped; the file is written whole then renamed; values only — no
  ACLs or timestamps on restore. SaveLocker already has extra save folders (`SaveRoot`, the `.savelocker/` slices in
  `SaveArchive`), so the "three methods of special-casing" in the 2026-08-20 note above is no longer the plan.
- **Their counts**: 430 manifest games declare a save-tagged registry key, and for 303 of them it is the only
  save. Re-count against our manifest in phase 0.
- **Nobody does Windows registry ↔ Proton prefix.** OpenSave's capture is Windows-only and warns on Linux;
  **a machine that cannot capture writes nothing**, so a Deck never overwrites a real capture with an empty one.
  Ludusavi on Linux copies a Proton prefix's whole `*.reg` files for registry-save games, which cannot be
  restored on Windows. Hoard parses the manifest's `registry:` block only to help detection; it does not back
  registry data up.

### Revised shape

0. Parse the manifest's `registry:` block (`ManifestLoader` DTO) and count what it covers.
1. **Windows**: capture each declared key under `HKCU` into `<state>/registry/<gameId>/registry.json`
   right before a push; declare that folder as the game's extra save folder `registry`; restore the values after
   the files on pull. Only `HKCU` (no `HKLM`, which needs elevation and is machine-wide).
2. **Restore safety**: the capture is hostile input like any archive. Write only under the keys the manifest
   declares for that game, refuse anything outside them, and refuse to restore while the game runs.
3. **Linux (later, optional)**: read and write the same keys in a Proton prefix's `user.reg` text file
   (`[Software\…]` sections), so a Windows capture can restore into a prefix and back. Until then the Linux
   agent skips the `registry` folder and never pushes an empty one.
4. UI: a registry chip on the game, and `doctor` lists the keys.
