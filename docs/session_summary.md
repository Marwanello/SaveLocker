# Session summary — 2026-10-18 — Competitor gap analysis (PR #77)

Standalone: the facts below do not need the rest of the vault.

**PR:** [#77 on `Marwanello/SaveLocker`](https://github.com/Marwanello/SaveLocker/pull/77), *Docs: backlog items from the competitor gap analysis*. Branch `docs-competitor-gaps`, base `main`. Vault only, no code. Evidence: `logs/2026-10-18_competitor-gap-analysis.md`.

Read the code of OpenSave, Hoard, Ludusavi, BlockSlot, EmuSync, Game-Save-Genie and Worldline (shallow clones, read only, deleted afterwards) and checked every gap against SaveLocker's source and plans. 15 Backlog items came out of it, each with its task file.

### High priority

1. **Emptied save folder wipes every machine (possible bug, not reproduced).** `SyncEngine.PushCoreAsync` refuses only a *missing* folder; an existing but empty one is pushed as a normal version, and every other machine's pull deletes its copy. Recoverable with *Set as Latest*, but it reaches the fleet first. OpenSave holds the game and asks *delete everywhere* / *put them back*. Plan: failing test, then refuse an empty push when the last version had files, answered like a conflict; optional server backstop. → `tasks/empty-save-hold/`
2. **A pull deletes excluded files (possible bug, not reproduced).** Excluded files are never archived, and the restore's delete pass removes in-scope files the archive lacks. Default excludes include `*.bak` (many games' own backup saves); per-machine excludes like `settings.ini` get deleted too. OpenSave sets them aside and puts them back. Plan: pass the excludes to `RestoreArchive` and never delete a match; test in `run-hardening-tests.ps1`. → `tasks/keep-excluded-files-on-pull/`
3. **Registry-based saves (amended).** OpenSave writes the keys to a `registry.json` in an extra save folder, so no archive special-casing is needed (the old plan assumed three places). ~430 manifest games with registry saves, 303 registry-only (OpenSave's count; re-count). Windows `HKCU` first; restore only declared keys; a Deck never pushes an empty capture; Proton `user.reg` later and optional (no competitor does it).
4. **Switch, Group F (amended).** Neither OpenSave nor Hoard splits Ryujinx per title; both sync the whole folder, so our `imkvdb.arc` plan goes further and has a fallback (yuzu forks first, Ryujinx manual-only). Added OpenSave's profile-rehoming rule, the emulators' name-cache locations, Ryubing and Flatpak IDs. Open: cross-emulator identity contradicts the "different emulators are never one game" decision — maintainer's call.
5. **File-level saves (amended).** Reuse include scopes: `<base>` + scope `Save.dat`, no archive change. `PathResolver.IsInstallRoot` allows an install folder only with a scope; the settle gate and watcher watch only scoped files. Up to 702 manifest entries. Depends on item 8.
6. **Native Linux saves (amended).** BlockSlot ships the same design (history per OS family, Proton counts as Windows), confirming the plan. The launch wrapper can tell Proton from native.
7. **Setup guide + Unraid template (amended, Medium → High).** Setup scores lowest (2/5). Add an Unraid Community Apps template (Hoard: admin user/password as form fields); first run asks for the admin password (BlockSlot); then join link → machine → first game → first push, each self-checking. The old text still said enrollment *file* (replaced by join links in #76).

### Medium priority

8. **Epic, GOG, Microsoft Store, Lutris and Bottles installs (new).** `<base>` needs the install folder; today Steam + Playnite on Windows, Steam + Heroic on Linux. Hoard reads Epic `.item` manifests, GOG Galaxy's SQLite db, the Xbox app registry; Hoard/Ludusavi/OpenSave scan Lutris and Bottles. Game Pass saves are mostly unsyncable containers.
9. **Travel mode / offline backoff (moved onto `main` from the parked branch).** ~5 failed requests a minute forever, the drainer re-zipping each time. Hoard adds separate ladders per failure kind (uploads 10 min; restores 1→5→15→60 min; unresolvable conflict 10→80 min then ask a person). Reuse `ServerReachability` and the 5-minute unreachable clock. Plus a help article on reaching the server from outside (Tailscale / Cloudflare with trusted proxies).
10. **Household accounts (new).** One server = one person; two people playing the same game collide as conflicts. Hoard has per-user accounts, tokens and storage. Decide: game per user vs shared game with per-user lineage; admin vs member. Large.
11. **Version history upgrades (new).** Labels on versions, single-file restore, trash with N days to recover, age-based thinning, prune preview. Branches noted, not proposed.

### Low priority

12. **Storage: off-site copies, then dedup (new).** Full zip per version; backups on the same disk. Cheap step: copy server backups to S3-compatible storage or another disk. Dedup stays deferred per Decisions.
13. **Playtime and "last played" (new).** The agent already knows start/stop; Hoard and OpenSave track sessions.
14. **More standalone emulators (new).** Mostly one `RomSaves` row each: mGBA, Lime3DS, Project64/Mupen64Plus/simple64, DeSmuME, SameBoy, Snes9x, bsnes, FCEUX, Mesen, Flycast, BlastEm; MAME, ares, BigPEmu to research. xemu probably unsyncable per game. EmuDeck first; add each emulator's chip.
15. **macOS agent (new).** Every main competitor has one. Third small host + Steam-on-Mac discovery + notarized package; after native-Linux's per-OS lineage.

### Side findings

- **Scores (out of 100):** OpenSave 85.0, Hoard 77.6, **SaveLocker 74.4** (tied with BlockSlot), Ludusavi 57.6, EmuSync 53.6. SaveLocker's 74.4 includes a half-point safety deduction for bugs 1–2. High + Medium items → about 86.
- **SaveLocker leads on:** conflict safety (leases, launch gate, same conflict choice everywhere), fleet management, self-updating agents/plugins, Playnite plugin, Game Mode controller UI, emulator identity depth, security hardening.
- **Corrections to the README-only first pass:** code signing is not a gap (nobody Authenticode-signs); Hoard does not back up registry data; EmuSync is last-modified-wins with no conflict handling.
- **Licence rule:** Hoard (AGPL) and EmuSync (GPL) ideas only; MIT projects may be ported with attribution.
- **Not adopted:** OpenSave's save paths for pirated-game Steam emulators (Goldberg, CODEX).
- **Stale docs:** emulator-saves status tables fixed (Groups C, E and Phase 5c shipped in #63/#68). "Duplicate-shortcut warnings outside doctor" is still in the Backlog although #75 shipped it — left as is.
