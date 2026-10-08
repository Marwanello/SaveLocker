# Task — Save file trees: what's inside a save, in the console and on this PC

**Created:** 2026-10-07

**Target:** `src/Shared/SaveArchive.cs` (`ListArchiveFolders`), `src/Shared/Contracts.cs` (`VersionFileDto`),
`src/Server/Program.cs` (version routes), `web/src/components/game/VersionsCard.tsx`,
`src/Agent.Core/AgentApiServer.cs` (a new game files route), the agent UI's game page
(`agent-ui/src/components/`), `web/src/api-types.ts`, `agent-ui/src/api-types.ts`, `src/Server/openapi.json`.

**Goal:** show the files a save is made of. In the **console**, every version in a game's history starts
collapsed and opens on **what changed** since the version before, with the full tree one click away
(mockup variant **B**, *Changes first*). In the **agent**, the game page shows **the save files on this PC right
now** as a nested folder tree, each file marked in sync, changed here, or only on the server, with other games'
files in a shared emulator folder folded into one line (mockup variant **A**, *Nested tree*).

Mockup (three variants, console and agent each): <https://claude.ai/artifact/2Nq9PbykqDDici67dWCGP2>. The
maintainer picked **B for the console and A for the agent** on 2026-10-07. Today the console's Save folders card
lists only the latest version's files, and the Versions card shows only id, time, machine, size and state; the agent
says "in sync" or not for the whole game, never which file.

One PR, one commit per phase: PR #62 (`save-file-trees`), 2026-10-07/08. Open questions settled with the maintainer
(answers at the end of this file).

## Status

| Phase | Status |
|-------|--------|
| 1 — Server: a fingerprint per file, and what changed per version | ✅ Shipped 2026-10-07 — changes cached in memory (no table), CRC-32 + size; head hashes read from the archive |
| 2 — Console: Versions rows open on their changes (variant B) | ✅ Shipped 2026-10-07 — change chips sit beside the state chip (no extra column) |
| 3 — Agent: route for this PC's save files compared with the server | ✅ Shipped 2026-10-07 — a differing file is `server` only when the head moved and nothing changed here |
| 4 — Agent UI: *Save files on this PC* tree on the game page (variant A) | ✅ Shipped 2026-10-08 — full-width card under Versions; "only on the server" is the new `--color-info` blue |
| testenv pass (below) | ⏳ Not run — the maintainer's by-hand check; every state was seen in a scratch tray against a two-machine seeded server |

## What already exists (don't re-derive)

- A version is a **full zip archive**. `SaveArchive.ListArchiveFolders(zip, maxFilesPerFolder = 500)` lists its
  files by save folder (primary first, then each extra folder's key) with size and modified time, reading only
  the zip's central directory. The console reads it through the admin route
  `GET /games/{id}/versions/{versionId}/folders` (`Program.cs`) as `VersionFolderDto(Key, FileCount, TotalBytes,
  VersionFileDto[] Files)`, with `VersionFileDto(Path, Size, ModifiedUtc)` and the path relative to its folder.
- Versions form a chain: `parentVersionId` (the Versions card walks it to split History from Backups).
- Pushes can carry a per-file manifest (`FileManifestEntry(Path, Sha256, Size)`) for delta uploads, but it isn't
  stored per version for reading back.
- The agent's `GET /api/games/{id}/sync-status` compares one whole-save hash (`TrackedGame.LocalHash`) with the
  head's `ContentHash`. `SaveArchive.ListSaveFiles(roots, excludes)` lists exactly the files a push would archive,
  and `TrackedGame.RealRoots` gives every folder of the game on this machine, scopes included.

## Phase 1 — Server: a fingerprint per file, and what changed per version

1. **Fingerprint:** add `Crc32` (the zip entry's own CRC-32, `ZipArchiveEntry.Crc32`) to `ListArchiveFolders`'
   tuple and to `VersionFileDto` (additive, so older consoles ignore it). It's free (it's in the central
   directory) and enough to tell "changed" from "same" between two versions of one game.
2. **Changes per version:** a new admin route `GET /games/{id}/versions/changes` returning, for every version, its
   counts against its parent: `added`, `changed`, `removed`, and `files: [{ key, path, change }]` for the changed
   ones. Computed by diffing each version's central directory with its parent's (path + CRC-32 + size). A
   version's archive never changes, so cache the result by version id (in memory, or a small table; see open
   questions). The first version (no parent) is all `added`.
3. **For the agent (Phase 3):** an **agent-key** route `GET /api/agent/games/{id}/head/files` returning the head
   version's files per folder with **SHA-256**, so the agent can compare its own files exactly. Hash on demand
   from the zip and cache by version id.

Verify: unit tests on `ListArchiveFolders` (CRC present, unchanged for an untouched file across two archives) and on
the diff (added, changed, removed, a file moved between folders counts as removed + added, the first version);
an API test for both routes, including another machine's key refused on the admin route. Regenerate
`openapi.json` and `web/src/api-types.ts` (additions only).

## Phase 2 — Console: Versions rows open on their changes (variant B)

- Each row of the Versions card (History and Backups) gets a chevron and starts **collapsed**. The collapsed row
  already shows the change chips from Phase 1: "1 changed", "2 added", "1 removed". One request for the whole list
  (`versions/changes`), not one per row.
- Opening a row shows **only the changed files**, as a tree per save folder (folder name + its template, then
  nested paths), each with an Added / Changed / Removed badge and its size. Removed files are struck through.
- **Show all files (N unchanged)** switches the open row to the full tree, from the existing `versions/{id}/folders`
  route, fetched only when asked. **Show changes only** switches back.
- **Collapse all** in the card header. Opening a row doesn't move focus; the chevron button has
  `aria-expanded`/`aria-controls`.
- The 500-files-per-folder cap stays; when hit, the row says "Showing the first 500 of N files in <folder>".
- Rows keep their existing actions (Set as Latest, Download, Protect, Delete); clicking those never toggles the row.

Verify: `web` lint, typecheck and build; in the browser against a seeded dev server, a game with three pushes
(one changing the save, one adding a state, one removing a state) shows the right chips and trees; a PC game with
nested folders (`Steam/<id>/user1.dat`) nests correctly; light and dark; phone width without sideways scroll.

## Phase 3 — Agent: route for this PC's save files compared with the server

New `GET /api/games/{id}/files` (local API, token-guarded like the rest), computed off the request thread (it reads
and hashes every file, like `sync-status`):

```
{ folders: [ { key, label, path,                        // this machine's real folder
               files:  [ { path, size, modifiedUtc, state } ],   // state: same | here | server
               other:  [ { path, size, why } ] } ],     // why: otherGame | excluded
  head: { versionId, when, machine } | null, reachable }
```

- `files`: every file a push would archive (`ListSaveFiles` over the game's real roots), compared by SHA-256 with
  Phase 1's head list. `same` matches; `here` differs or is missing from the head (a push would send it);
  `server` is in the head but not on this PC (a pull would write it).
- `other`: files in the same folder that this game doesn't sync. Out of scope (another ROM's save in a shared
  emulator folder) is `otherGame`; matched by an exclude pattern is `excluded`. Capped (say 200) with a count.
- Server unreachable: every file listed with no state, `reachable: false`.

Verify: unit tests against a real server (`ServerProcess`) with an emulator game in a shared folder: a changed
save shows `here`, a state only on the server shows `server`, another ROM's save shows under `other`, an excluded
`*.log` shows `excluded`; mutation-check the scope split (a pattern change must move a file between `files` and
`other`). Regenerate `agent-ui/src/api-types.ts` from a scratch tray (Gotchas: never `:5178`).

## Phase 4 — Agent UI: *Save files on this PC* tree on the game page (variant A)

- A card on the agent's game page: one block per save folder (label + this PC's real path), then a nested tree
  (folders foldable, open by default) with each file's size and a dot: green **in sync**, amber **changed here ·
  will push**, blue **only on the server · will pull**. A legend in the card header.
- Other files in the folder fold into one line: "6 other files in this folder belong to other games" (or "aren't
  synced" for excluded ones), which opens to show them, dimmed.
- Loaded when the card is opened or the page loads, never on a timer (it hashes the folder); a Refresh button,
  and it refreshes after a Sync from that page.
- Works in the Windows tray window, the Linux agent UI, and at the Deck's Desktop Mode width. Game Mode is out of
  scope.

Verify: `agent-ui` lint + build; the testenv pass below; screenshots light and dark.

## testenv pass (after Phase 4)

```powershell
.\tests\testenv.ps1 sync; .\tests\testenv.ps1 build; .\tests\testenv.ps1 emu-fixture; .\tests\testenv.ps1 up
```
1. Add Chrono Trigger on WSL (`http://localhost:5187`) and on Windows (`http://localhost:5188`), then Sync all on both.
2. Console → Chrono Trigger → Versions: every row is collapsed with change chips; open the latest: only changed files;
   *Show all files* gives the full tree with `saves` and `states` folders.
3. Windows agent → Chrono Trigger → *Save files on this PC*: everything green; the Zelda save and state are folded
   into "other files". Change the fixture's `.srm`: it turns amber after Refresh. Push a new state from WSL: it
   shows blue on Windows until a pull.

## Open questions

- **Where the change cache lives:** in memory (rebuilt after a restart, cheap since it reads only zip directories)
  or a small table filled at push time? In memory is simpler; a table makes the History list instant for games
  with hundreds of versions.
- **CRC-32 or SHA-256 in the console diff:** CRC-32 is free from the zip; a collision between two versions of one
  file is very unlikely but possible. SHA-256 means reading every byte of two archives per row. Recommend CRC-32 +
  size for the console, SHA-256 for the agent comparison.
- **Backups rows:** diff a backup against its own parent (what it changed), or against the current head (what you'd
  get back by restoring it)? Recommend its parent, with the restore difference left to the existing conflict view.
- **Agent page placement:** a card of its own, or inside the existing save folders card?
- **Game Mode on the Deck:** a read-only list there too, later?

**Answers (2026-10-07):** the change cache lives **in memory** (no table — no migration, no push-path change);
**CRC-32 + size** in the console, SHA-256 for the agent; Backups rows diff against **their own parent**; the agent card
is **its own full-width card at the bottom** of the game page. Game Mode stays out of scope. Found while building:
the palette had no blue, so a fifteenth token `--color-info` was added (Decisions.md) at the maintainer's request.
