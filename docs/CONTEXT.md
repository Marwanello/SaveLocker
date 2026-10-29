# SaveLocker — Session Context

Read this and [[REPO_MAP]] at the start of every session. This file is a **short orientation** —
where the project is right now, and where to go next. Detail belongs in the files it links to; if a
section here starts growing, move it and leave a link.

**What:** Self-hosted Windows/Linux game save sync. Hub-and-spoke: a tray/headless agent on each PC
or Steam Deck syncs saves through an ASP.NET Core server (Docker on unRAID). React admin dashboard +
embedded React agent UI + a gamepad-native Deck Game Mode UI. See [[Architecture]].

**Repo:** https://github.com/SkorcherX/SaveLocker | **Branch:** `main`

**Released:** **v0.5.10** (tagged 2026-08-19) — notes in `web/src/releases/0.5.10.md`. **Rolled out
2026-08-20** (maintainer-confirmed): console redeployed, both agent installer packages uploaded.
The Cyberpunk chunked-upload fix ships as *part of* v0.5.10 (the fix commit landed after v0.5.9 was
tagged), so "roll out v0.5.8" and "deploy the Cyberpunk fix" — this file's old Next action items 0
and 1 — both resolve to the same rollout: the console and fleet are now current through v0.5.10,
which also carries v0.5.9's console UI/audit cleanup and the MoonDeck/case-insensitive save-detection
fixes. See `logs/shipped-2026-08.md` for the full index of what that covers.

**Playthroughs Group 3, Phase 7 — the console's Saves card — built (2026-10-27, branch `playthroughs-group-3`,
stacked on `main` with Groups 1 and 2 merged, PR open, not merged).** The console's game page gets a **Saves** card
in place of Versions ([[tasks/save-playthroughs/plan]] Phase 7, chosen mockup variant E,
`web/mockup-features/features/playthroughs.tsx`): a tab per playthrough (just Main on every game until a second
exists), each tab's own head (devices, Switch a device to this…, Rename, Make default, Archive/Unarchive) and its
own versions table underneath, scoped with the existing `?playthrough=` query the server already answered
(`SyncService.ListVersionsAsync`) — nothing new on the server (until the review: Prune gained `?playthrough=`). **New playthrough** offers *Current save* (fork
another playthrough's or a specific version's latest — "New playthrough from this version" sits in the versions
table's row actions) or *Start fresh* (an empty playthrough; there is no local save folder on the console to offer
a file-keep list from, so that part of the agent/Playnite flow does not apply here — a deliberate scope decision,
not an oversight). **Switch a device to this…** only queues `AgentCommandType.SwitchPlaythrough`
(`api.switchDevicePlaythrough`, matching the existing Push/Pull/Sync "queue and toast" idiom); the device runs the
switch itself (Group 2) and the result shows up in the game page's existing Remote commands table — no new
progress UI was built for this, since one already exists. `VersionsCard.tsx` is gone, folded into the new
`SavesCard.tsx`; `GameDetail.tsx` updated, `web/src/api.ts`/`types.ts` gained the playthrough calls (admin routes
from Group 1, unchanged). **Verified live** against a throwaway dev server + the dashboard dev server: created a
game, a second and third playthrough (one from "Current save" — correctly refused by the server with "has no save
yet to start from" until a real push exists, one "Start fresh"), renamed, archived and unarchived, and confirmed
"Switch a device to this…" degrades correctly with zero machines enrolled (the button stays disabled, no request
fires). `tsc -b`, `vite build` and `oxlint` all clean. **Not verified:** an actual switch against a running agent,
and a multi-emulator game's playthrough tabs under its emulator tabs (Phase 6/7's interaction) — neither this repo
had a second emulator tree or a live agent handy. **Next action:** Decky (Phase 8) and Playnite (Phase 9) each need
their own repo/worktree/PR per `implementation-grouping.md`; Phase 10 (delete to trash) still waits on
version-history Group 1.
**Reviewed 2026-10-27 (PR #97), fixed:** CI's `build-web` failed on a same-string ternary (the appearance check's flattened-state rule); a fork's History read "No versions yet" under a header naming its latest (its head lives in the playthrough it was forked from — now listed with a *From Main* chip); **Prune** on one tab pruned every playthrough (the one server change: `POST /api/games/{id}/prune?playthrough=`, and the count now skips every playthrough's head); a new playthrough was created but its tab not opened; archiving the open tab left it showing with no tab; *New playthrough from this* after picking *Start fresh* made an empty playthrough; versions pushed while the page was open never appeared (the card now takes GameDetail's live list); Switch, Prune and *New playthrough from this* were offered where the server refuses them (archived playthrough, merged version); the New playthrough panel opened below the whole versions table. `run-playthrough-tests` 99 → 102.
**Second review 2026-10-28 (PR #97, commits `0a2924c`..`a80cc9a`), fixed:** forking a version of an archived playthrough from the console's popup quietly forked another line's latest (the archived source was not in the picker); Escape on a dropdown inside that popup closed the whole popup; the agent page's new 5 s playthrough poll dropped the tabs (and the one being looked at, or a rename being typed) on any one failed ask; the agent's Versions card now marks a fork's head *From Main* like the console. Not changed: X on a controller now plays another playthrough here in one press (deliberate, and the switch keeps both saves). `run-playthrough-tests` 102 → 103 (the agent's own list includes its fork point).
**Fix 2026-10-28, a device's playthrough count went stale:** only reading the playthrough list, a switch or the CLI's `playthrough` updated `TrackedGame.PlaythroughCount`, so a device that never did any of those stayed on 1 after a second playthrough was made elsewhere — and the Deck shows its switch only above 1. The count now rides the sync the device already does: `UploadResult.PlaythroughCount` (single-shot, chunked complete, and a begin's NoChange) and an `X-Playthrough-Count` header on the head download; the agent takes it on every push and pull (`SyncEngine.TakePlaythroughCount`). No extra round trip; an older server sends neither and the count is left alone. New `PlaythroughCountSyncTests` (2, against a real server; both fail with the agent's take disabled).

**Follow-up 2026-10-27, mockup-driven (`web/mockup-features/features/playthroughs.tsx`, `?focus=1` to view just this feature):** tabs now always show, even a single-playthrough game's lone "Main" (renameable there too, unlike before); **Default** and **Archived** moved from the head into a chip on the tab itself; switching tabs slides+fades from the side the new tab sits on (`.animate-pane-right`/`-left`, `src/index.css`). **New playthrough** is now a popup (`AddGameDialog`'s modal pattern) instead of an inline panel — the plan's "No modals" rule's one deliberate exception — with Name, then a shared **Playthrough + Version** picker (horizontal, labeled) both *Current save* and *Start fresh* sit under; *Start fresh* still always starts empty (the server can't see a device's files, so the picker there is informational, not functional — a real per-file keep list already exists on the agent, which does have the folder). Each version row's actions are icon-only now (Download, and **Fork** — `git-fork`/`download` added to `ui/Icon.tsx`, geometry from lucide-static 0.511.0, not hand-drawn); Fork opens the same New-playthrough popup pre-filled with that playthrough and version (normalizing to "Latest" when the forked version is already that playthrough's head), and the picker shows each version's real hash, not a placeholder. Agent-ui's equivalent trigger (`VersionsCard.tsx`'s `onFork`) renamed "New playthrough from this version" → **Fork…**, and it now passes the whole version so its label shows the hash too. Verified live on testenv (`console` + `WinTest` + `LinuxTest`): forked Chrono Trigger's Main (`062cf4ec`) into "Hard mode", confirmed the *From Main* tag and the real hash preselect, confirmed Default/Archived read correctly off a live fleet. `tsc -b`, `oxlint`, and `run-appearance-consistency-tests` (50/50) all clean.

**Follow-up 2 2026-10-27, bug reports against the shipped UI:** *Start fresh*'s Playthrough+Version picker is now hidden
rather than shown-but-inert (`SavesCard.tsx`'s `NewPlaythroughPanel`) — it did nothing there, and showing it with an
explanatory note was more confusing than just not showing it. Agent-ui's "Versions on the server" (`VersionsCard.tsx`)
now matches the console's `PlaythroughVersions`: a History/Backups `Seg` split (walked from `headId` by
`parentVersionId`, same rule as the console), matching columns (Version · When · Machine · Size · State), and an
icon-only Fork anchored to the row's end (`lucide-react`'s `GitFork`, agent-ui needs no hand-copied `Icon.tsx` since it
uses the real package). Root cause of "Hard mode has no version in the agent though the console shows one": the
agent-only route (`GET /api/agent/games/{id}/versions`) filtered strictly by `SaveVersion.PlaythroughId`, so a fresh
fork's head — which belongs to the *source* playthrough until its own first push — was never in the list; the console
never had this bug because it fetches every playthrough's versions unfiltered and injects the head client-side. Fixed
server-side in `Program.cs`'s agent route instead (inserts the playthrough's head via `GetVersionAsync` when missing),
since it's the one caller that narrows by playthrough. Verified live on testenv: Chrono Trigger's "Hard mode" (empty,
forked from Main) now shows History 1 in the agent, matching the console; Kingdom Hearts II's Main now reads History 1
/ Backups 1 in the agent (previously a flat "2 versions", which read as a mismatch against the console's own 1+1).
`tsc -b` and `oxlint` clean on both `web` and `agent-ui`; server `dotnet build` clean.

**Follow-up 3 2026-10-28, agent-ui page layout and freshness:** the game page's "Versions on the server" is now the
first card (full width, no longer paired with `GameManagement` in `sl-grid2`) — it was the thing being asked for most
and used to be buried below Sync buttons, stats and two other cards. `PlaythroughTabs.tsx`'s subtitle no longer says
"also on <device>" — that device list is one scroll away on the page itself, and the tab was the wrong place for it.
A new playthrough made on the console used to need a back-then-forward to show up here: there's no push channel from
the server for this (the agent's own `/api/activity` poll is local-only — a file change or a sync this agent ran, not
a hint the server's *metadata* moved), so `GameDetailView` now re-asks `/api/playthroughs` every 5s while the page is
open, plus once immediately on tab-focus regain. Separately, `GamesView` used to flash "No games are on this machine
or the server yet" / **Add games** for every cold open, including the common case of the first `/api/games` request
still in flight — `games` starts at `[]` in `App.tsx` with no "have we asked yet" of its own. Added `gamesLoaded`
(true once that first request settles, success or fail) and a loading state in `GamesView` so the empty state only
ever shows once there's a real answer. Verified live on testenv: created "Heartbeat test" on the console and watched
it appear in the open agent tab (`WinTest`) within the 5s poll, no navigation; confirmed the Versions card's new
position and the tab subtitles. `tsc -b`, `oxlint` clean; `run-appearance-consistency-tests` 50/50.

**Follow-up 4 2026-10-28, page order correction + an other-playthrough view + controller nav:** corrects Follow-up 3's
ordering — the game page is now **game info → Sync/Push/Pull bar → playthrough tabs → Versions**, not Versions first;
the bar was getting lost under a table every time. Looking at a playthrough this device isn't on no longer shows a
full "Sync this game" bar it can't use: `GameDetailView` now computes one `actionBar` (Sync/Push/Pull for your own
playthrough, or **Play this one here / Rename… / Archive…** for one you're not on) rendered in that same top slot, so
the layout stays identical across both — only its buttons change. The Card that used to repeat the other playthrough's
name below Versions (the tab already says that) is now titled **Info** and carries no buttons — they moved to the
action bar. Controller: LT/RT step between playthrough tabs when there's more than one (`gamepad/nav.ts`'s new
`stepPtTab`); with only one tab (most games: just "Main") they fall through to Push now/Pull latest exactly as before,
so nothing changes for the common case. X plays the open "other" playthrough here when that action bar is showing,
falling back to Sync this game otherwise. Push now/Pull latest drop their LT/RT `PadGlyph` hint whenever a second
playthrough exists (so they rejoin the normal focus ring instead of becoming unreachable), and the
Rename/Archive/Unarchive/Keep buttons that were missing `data-pad-label` now carry one. Verified live on testenv:
confirmed the new top-to-bottom order, and that opening "Hard mode" (not this device's own) swaps the bar to
Play-here/Rename/Archive and relabels the card to "Info" with its dl/paragraph but no buttons. `tsc -b`, `oxlint`
clean; `run-appearance-consistency-tests` 50/50.

**Help KB: where emulator saves are looked for — written (2026-10-29, branch `emulator-kb-troubleshooting`, not merged).** New article
`web/src/help/emulator-save-locations.md` (Troubleshooting, slug `emulator-save-locations`): how EmuDeck's `Emulation` folder is found, then
per emulator the EmuDeck folder, the standalone/Flatpak/portable folders in the order the readers try them, the setting that moves each and
what is *not* read, plus a *My emulator game isn't in Add games* checklist. Linked from `troubleshooting.md` and `emulator-backups.md`; the
latter's states-only sentence was stale (PSP only) and now names RetroArch, PCSX2, DuckStation, Dolphin, PrimeHack, PPSSPP and Azahar. Paths
come from the code, not the readers' doc comments — they claim a few places the code never reads (a portable Xenia's `portable.txt`, a
shadPS4 `user/` beside any exe), and the article follows the code. **Keep it in sync** when [[tasks/standalone-emulators/summary]] adds
readers. Verified: `npm run build`; rendered in the console mockup's Help view, dark and light. **Not verified:** the testenv console.
**Next action:** the maintainer's testenv look at the article, then merge.

**Playthroughs Group 2 — one device — built (2026-10-26, branch `playthroughs-group-2`, stacked on `playthroughs-group-1`, PR open, not merged).** Phases 4–6 of
[[tasks/save-playthroughs/plan]]: an agent now **follows one playthrough per game** and can **switch**. `SyncEngine.SwitchPlaythroughAsync` refuses while the game
runs, a session is open here or a conflict is open, pushes the outgoing save to **its own** playthrough, restores the target's latest (or empties the folder for
*Start fresh*), then tells the server; a failure leaves the device on the old one. `PlaythroughStore` keeps deduplicated local copies (*Only until uploaded* /
*Keep for offline switching* / *Off*), which is what makes an **offline switch** possible — the outgoing save and the selection go up when the server is back. New
surfaces: playthrough **tabs** under a game's page, **New playthrough** (Current save / Start fresh) and a switch **dialog** with the steps as they happen, Settings
split into six sections with a jump bar (LT/RT on a controller) and the copies row under *Sync*, `savelocker playthrough list|switch|new|flush`, and the console's
`SwitchPlaythrough` command now actually runs. Rationale in [[tasks/save-playthroughs/plan]] → *What Group 2 settled*. Verified by the new
`tests/run-playthrough-agent-tests.ps1` (60 checks: fork, switch, two lines at once, running refusal, conflict refusal, start fresh, offline from a copy, copies off,
tampered copy, only-until-uploaded) and `PlaythroughStoreTests` (13), plus the existing server and agent suites (see [[Build and Run]] → Suite baseline). **Not verified:**
a Deck, a controller, the Linux launch wrapper with a non-default playthrough, the console's switch command through a real fleet (testenv pass).
**Reviewed 2026-10-26 (PR #95), fixed:** a save played offline could be marked uploaded and never sent (away from a line restored from its
unsent copy, without playing); back on such a line, the flush and the device's next push conflicted with each other; one damaged copy
stopped every later copy and selection; the sweep could delete a copy whose index it failed to read; two switches of one game could
run at once; `status`/sync-status read the default playthrough. `run-playthrough-agent-tests` 60 → 68 (the 6 new behaviour checks fail on
the pre-review agent); `PlaythroughStoreTests` 13 → 15.
**Next action:** the maintainer's testenv pass of the steps in the PR (build with `-Version 0.9.0-test`), then merge; Group 3 (console Saves card, Decky, Playnite, delete) is next.

**Playthroughs Group 1 — the server — built (2026-10-26, branch `playthroughs-group-1`, PR open, not merged).** Phases 1–3 of
[[tasks/save-playthroughs/plan]]: a game now has one or more **playthroughs** (named lines of saves), each with its own head, versions, conflicts,
lease and retention, and each device follows one per game. Nothing is visible yet — no UI, and the agent does not know about them (Group 2) —
but every existing game became one playthrough, **Main** (migration `AddPlaythroughs`; `Game.HeadVersionId` stays as a mirror of the default's head), and a
client that names no playthrough lands on the one its device follows, so nobody's sync changes. New: `Playthrough` / `DeviceSelection` tables,
`SyncService.Playthroughs.cs`, the admin and agent playthrough routes ([[API Reference]] → *Playthroughs*), a per-playthrough lease, a
`SwitchPlaythrough` command the server only hands to an agent that reports ≥ `Playthroughs.MinAgentVersion` (placeholder `0.9.0`). Settled with the maintainer:
a fork **points at** a version (nothing copied), and an archived playthrough is **frozen** (409) — both in [[Decisions]]. Verified by the new
`tests/run-playthrough-tests.ps1` (96, including a previous-schema database seeded and migrated) plus the existing server suites (all unchanged,
see [[Build and Run]] → Suite baseline). **Not verified:** a migration of a *real* database (none on this box), the testenv fleet, anything Docker.
**Reviewed 2026-10-26 (PR #94), two findings fixed:** the agent state route (`savelocker status`, save-vs-cloud, the DLC check) described the
*default* playthrough, so a device left on Main after the default moved compared itself with another line's save — it now answers for the
calling machine's playthrough (or `?playthrough=`); and the merge refusal no longer tells the admin to delete playthroughs (not possible yet).
`run-playthrough-tests` 96 → 99.
**Next action:** the maintainer's testenv pass of the steps in the PR, then merge; Group 2 (agent switch, local copy store, agent UI + CLI) is next.

**Full-repo security review: two findings fixed (2026-10-25, branch `security-review-fixes`, PR open, not merged).**
(1) **One stolen machine key could plant code in every Windows agent's Startup folder:** an agent-created game's
`SuggestedSaveDir` was never validated, every agent adopts server games and expands the template, `SavePathGuard`
had no rule for autorun folders, and the restore writes any file name. Now the guard refuses the Start Menu
(both Startups), `~/.ssh` and the Linux autostart/systemd/KDE-login folders as the folder, inside it, or holding
it, and the server refuses an agent template naming one — and a literal `SuggestedSaveDir` from an agent at all.
(2) **Another local account could read the local API token** by fetching `/` over loopback; `index.html` now goes
only to a process of the same OS account (`LoopbackPeer`). Also: the audit CSV export neutralises formula cells.
Rationale in [[Decisions]] (WA-02 autorun note; local API entry). Tests: new `AutoRunFolderTests.cs` (guard,
template recogniser, a real server refusing the templates, the peer check over IPv4/IPv6) — both behaviour tests
fail against the pre-fix code; `dotnet test` 849/849; `run-local-api-tests` 128/0. **Pre-existing, not fixed:** that suite's section 10 (server
switch) hits a 401 reading the daemon's state and its checks never run — identical with this fix reverted. **Not verified:** the
refusal from a genuinely different account (needs a second login on Windows and on the Deck).
**Next action:** the maintainer tries the agent UI from the tray, `savelocker open` and a browser tab, then merge.

**One console entry per game, one save tree per emulator (2026-10-21, branch `multi-emulator-games`, PR #85 merged;
maintainer marked it done 2026-10-26, ships in v0.8.1).** All six phases of [[tasks/_done/multi-emulator-games/plan]] are built: a console-side `GameTitle` groups the
server games one title is saved as (melonDS + RetroArch's Hunters, Dolphin + PrimeHack's Trilogy); the console and
the agent UI list it once with a tab per emulator; Group with / Split out by hand; and **Copy this save to another
emulator** (server-side, melonDS ⇄ RetroArch and Dolphin ⇄ PrimeHack only — see [[Decisions]]). Sync is unchanged:
each tree is still its own game. Migration `AddGameTitles` (also `SaveVersion.CopiedFrom`); help article
*Games saved with more than one emulator*. **Done.** **Gotcha found:** `run-console-security-tests` aborts mid-run if its server (:5215) is killed — never stop
"every SaveLocker.Server.dll" while a suite runs; match on the port or the state dir.

**Per-file delta upload shipped (2026-08-22, this session, branch `worktree-per-file-delta-upload`,
`f7d2a58`) — on `main` but NOT yet in a tagged release.** The backlog item "upload only changed
files, not the whole save folder": a normal push now diffs a per-file manifest against the server's
stored per-file baseline for its current head and sends only what actually changed, instead of
re-zipping and re-uploading the whole folder on every save (the Cyberpunk/Fallout: New Vegas
multi-slot-save motivation from `tasks/PerFileDeltaUpload.md`, now moved to
`logs/2026-08-22_PerFileDeltaUpload.md`). Measured: an 8-slot 3.2 MB fixture with one file changed
sent ~400 KB instead of ~3.2 MB. New `SaveVersionFile` EF table (migration
`20260822103632_AddSaveVersionFiles`); `openapi.json`/`web/src/api-types.ts` regenerated (`agent-ui`'s
is unaffected — it reflects the agent's own local API, not this wire protocol). The same session
also investigated a second question — whether version-change detection was ever fooled by a
timestamp rather than real content — and found it was **already correct** before this work
(`SaveArchive.HashDirectory` hashes path+bytes only); recorded as a settled fact in `Decisions.md`
rather than a fix. New suite `tests/run-delta-upload-tests.ps1` (17/17); no regression in
`run-agent-tests` (47/47) or `run-hardening-tests` (33/33). `run-server-bugbounty-tests` reads
160/164 — the 4 failures are a pre-existing `wsl -d Ubuntu-24.04` distro-name mismatch on this dev
box's WSL install (registered as plain `Ubuntu`) in that suite's raw-schema helper, unconnected to
this change; not fixed here since it's a per-machine environment quirk, not a code issue.
<br>**Not yet run against a real fleet** — verified end-to-end against a throwaway dev server via
the CLI (register/add-game/push/pull across two machines, a genuine conflict, a forced push, a
below-floor small game, and a deliberately hostile delta payload), and the security check was
confirmed to actually catch what it exists for by disabling it and watching the assertion flip
(then immediately restoring it — the auto-mode classifier blocked a second, redundant run of the
suite with it disabled, which is exactly the caution you'd want there). Next real-world step: roll
out with the next release and watch it on the maintainer's own Cyberpunk 2077 / Fallout: New Vegas
saves — the `upload.delta` audit entry's "N of M files needed fresh bytes" is the thing to read.
<br>**Reviewed at depth on 2026-08-26 (branch `per-file-delta-upload`); 14 findings, all applied.**
The three that mattered: the agent zipped whatever paths the *server* named, with no check they were
files it had itself declared — a server reached by a forged enrollment file could have had any
readable file on the machine zipped and uploaded (the upload-side mirror of the hostile-archive rule
[[Decisions]] §4 already applies on pull); a failure while storing the per-file baseline ran inside
the `catch` that deletes the just-published archive, *after* the version row was committed and the
head advanced, leaving the head pointing at a file that no longer existed; and `entry.LastWriteTime`
was set before the source file was opened, so a save file that vanished mid-push raised
`ArgumentOutOfRangeException` (zip cannot represent the 1601 timestamp a missing file reports)
instead of a diagnosable `FileNotFoundException`. Also: the server now verifies that a reconstructed
archive actually hashes to the `ContentHash` the agent declared for it — per-entry hashes only ever
proved the pieces — bounds it by `Storage:MaxUploadMb` (successive deltas could otherwise grow one
version past the configured cap a few MB at a time), and validates the client-supplied manifest at
Begin rather than discovering a duplicate path as a primary-key violation after the commit.
<br>Structurally, the push decision tree moved out of `ApiClient` into `SyncEngine.SendPushAsync` —
which is what makes the containment check natural, since that is where the manifest the agent sent
still is. `SaveArchive.ComputeManifest` now returns the aggregate content hash alongside the
manifest from **one** pass, so `HashDirectory`'s separate read is gone and `CreateArchive` no longer
hashes at all; that made the per-file *count* floor deletable, and a game with three 400 MB slots
now deltas instead of re-uploading 1.2 GB every push. `run-delta-upload-tests` 17 → **29/29** (new:
the Complete-time `RetryFull` branch, malformed-manifest rejections, and an `HttpListener` stub
server that asks the agent for a path outside the save folder and asserts nothing is uploaded);
`run-agent-tests` 47/47 and `run-hardening-tests` 33/33 unchanged. **Note for that suite:**
`run-agent-tests` does not start its own server and is not idempotent against a dirty one — a reused
DB produces shifting, misleading failures. Give it a fresh `Storage__DbPath`/`ArchiveRoot`.
<br>**Untested:** the `Storage:MaxUploadMb` ceiling on a reconstructed archive. Exercising it needs
either a >500 MB fixture or a second server started with a tiny cap; neither was worth it, but it is
the one fix here resting on inspection rather than a test.
<br>**2026-09-28 — a version's "newest change" is its real UTC write time (branch `fix-archive-utc-timestamps`, merged into `group-8-ui-redesign` / PR #52).** It read 3 h late
from a UTC+3 uploader: zip's DOS timestamp is the writer's local wall clock and `GetArchiveStats` labelled it UTC. Each
archive entry now also carries `mtime-utc=<UTC>` in its comment (the DOS field stays local, so other zip tools are
unchanged); the server's delta rebuild copies that record forward. Old archives still read shifted — nothing in them
says which zone they came from. Why not UTC in the DOS field or the 0x5455 extra field: [[Decisions]]. Verified live via
`testenv conflict -Windows -Wsl`: both sides' newest change equals the file's own UTC mtime to the tick.

v0.5.7's rollout (2026-08-15) is complete: console redeployed, the Windows agent took it from the
tray's *Check for updates*, and **the Deck updated itself** — see below, it is the first time that
has happened. The **decky-plugin** row of Config → Agent updates is still **empty**, so no Deck is
offered a plugin update — the release to put in it exists (v0.2.1, below) and uploading it is Next
action 2. GHCR package `savelocker` is **public** ([[Gotchas]]).

**Decky plugin:** its own repo, <https://github.com/SkorcherX/SaveLocker-Decky>, at **v0.2.1**
(tagged 2026-08-15 — the first plugin release cut since the agent gained the ability to install one).
Not on the Decky store and cannot honestly be submitted — see `logs/2026-08-15_decky-plugin.md`.
Since Phase 5 (below) the **agent keeps it updated**, so the custom-store setting is no longer the
only route to an update.

**Console UI/audit cleanup (2026-08-16, this session, on `main` but NOT in v0.5.8 — that was already
tagged).** Four small tasks, one commit each, all with a real bug caught during manual browser
verification and fixed before shipping — write-ups in `logs/2026-08-16_*.md`:
- NavBar button sizing + Refresh icon-only, Info-severity agent events no longer read as
  "problems" (`75d2df7`, `ab9d9da` for the follow-up checkbox-color fix).
- Audit Log gained CSV export, and three new audited events: agent/plugin package updates,
  human-readable conflict-resolution detail (which machine's version won/lost), and exclude-glob
  diffs (`de6c7a4`, `6d594e3`).
- Agent Updates card is now read-only status + a single Edit modal (bulk "fetch all", per-package
  controls, GitHub `SHA256SUMS*.txt` hash verification) — `5672547`.
- The auto-fetch interval became a real schedule: disabled/hours/weekly/monthly, with the
  weekly/monthly modes computing their next occurrence server-side
  (`AutoFetchScheduler.ComputeNextRun`, tested by `tests/run-schedule-tests.ps1`) — `290ca2c`.
<br>**Not yet in a tagged release** — next release's notes need to cover all four. `AutoFetchSchedule`
replaces the old `SetAutoFetchHoursRequest` on the wire (`/api/settings/agent-update-auto-fetch`'s
body shape changed), and `AgentInstallerStatus` gained a `Source` field — both are additive/back-
compat server-side (old DB rows with no schedule read as `mode: "hours"`, exactly as before), but any
external script POSTing the old `{ hours }` shape to that endpoint needs updating.

**`testenv` gained a Steam Deck target and a real `clean`, and both are now proven on real
hardware (2026-08-17–19, across sessions).** Asked for directly: push a test build to the real Deck
beside the installed release agent, the way the rig already does for Windows and WSL, and be able to
delete every test build/state it leaves behind — not just stop the processes, which `down` already
did. `tests/testenv.ps1 build -Only deck` cross-publishes a self-contained linux-x64 tarball in WSL
(`packaging/linux/build-linux.sh`, same packer a real release uses); `up` `scp`s it to
`~/savelocker-test` on the Deck (state in the sibling `~/savelocker-test-state`, never
`~/.local/share/SaveLocker`) over SSH, gated by `$env:SAVELOCKER_DECK_HOST` /
`$env:SAVELOCKER_DECK_SERVER_URL` — unset skips the Deck silently. Editing those two settings got
its own command, `deck-config`, writing a gitignored `tests/testenv.local.ps1` that's loaded
automatically (a DHCP-leased Deck IP moves; retyping an env var every shell was the wrong friction).
`clean` stops everything first and then deletes it: Windows' `-StateRoot`, the WSL rig's state dir,
the Deck's deployed build+state, and the dashboard container **and its image and data volume** (a
plain `down` left all three of those behind). Design detail: *Throwaway test rig* in
[[Build and Run]] and *`tests/testenv.ps1`* in [[Gotchas]].
<br>**The first real `up` against hardware (2026-08-19) found three genuine bugs, not zero** — worth
internalising as a reminder that "the self-contained publish works" is not the same claim as "the
rig works": a fresh worktree's `agent-ui` had never had `npm install` run, which the Windows build
needs internally but never checked for; both bash scripts' `cmd_up` read the daemon's auth token
exactly once, racing the daemon still writing it; and the Deck's daemon — this is the one that would
have made the whole feature useless in practice — **did not survive its own SSH session closing**,
because `setsid`/`disown` guard against a different mechanism than the one that killed it
(`logind`'s `KillUserProcesses` tracks by cgroup, not POSIX session ID). Fixed with
`systemd-run --user --scope`, confirmed by starting a throwaway process, closing the SSH connection,
and finding it still running from a second one. All four commands (`build`/`up`/`down`/`clean`) now
verified working end-to-end against the real Deck, the real WSL clone, and Docker, with the real
installed Deck agent (pid confirmed unchanged throughout) and its config never touched. Full
write-up of all three: [[Gotchas]] → *`tests/testenv.ps1`*.

**Save-path detection reliability investigated and fixed on the real Deck — four root causes, two
rounds (2026-08-19, branch `claude/steam-deck-save-detection-68b6c5`, no task file).** Round 1, asked
directly: some games "definitely have save paths" weren't detected reliably, and Cyberpunk 2077
specifically wasn't picked up despite being installed. Deployed the testenv Deck target (above) to
investigate live. Root cause: Cyberpunk is launched through a **MoonDeck** (Decky streaming plugin)
shortcut, whose `Exe` runs MoonDeck's own client script, never the game — Steam makes no compatdata
prefix for it at all. The real save data (autosaves 9 days old) sat orphaned under
`compatdata/1091500`, Cyberpunk's actual Steam AppID, from a Steam-Store install since uninstalled.
`LinuxGameScanner` now also tries the AppID MoonDeck's `LaunchOptions` names
(`MOONDECK_STEAM_APP_ID=`) when the shortcut's own prefix fails to resolve, and `Doctor` explains the
finding either way. **Found in passing: six shortcuts on this same Deck (HITMAN 3, Minit, Moving Out,
Animal Crossing, Metal Gear, Waydroid) are each duplicated in `shortcuts.vdf` under two different
AppIDs** — Steam only ever launches one, and scan's dedupe could silently pick the dead one with no
way for the user to know; `doctor` now notes it (not a `Problem` — scan's dedupe still often lands
correctly).
<br>**Round 2, once Cyberpunk was fixed: four more named games (A Short Hike, Left 4 Dead 2,
Borderlands 2, Roadwarden) "and so much more."** Two further, independent bugs in
`ScanInstalledSteamGamesAsync` — installed Steam games specifically, not shortcuts. Left 4 Dead 2's
save is Steam Cloud's own sync folder (`<root>/userdata/<storeUserId>/550/remote`), which needs no
Wine prefix at all, but resolution was gated on one existing; Borderlands 2 was moved to the SD card
and Steam re-created a fresh, useless prefix there (`My games`, wrong case) while the ORIGINAL,
working prefix stayed in the main root — so the fallback now retries the main root whenever the
current library's own prefix resolves nothing, not only when it is missing outright. `<root>` is now
always built from the verified Steam root directly rather than derived from whichever prefix path is
tried, or Steam Cloud paths on a secondary library silently pointed at a `userdata` folder that isn't
there. A Short Hike and Roadwarden turned out to have no bug behind them — the former runs as a
native Linux build (out of scope, `Decisions.md` §1), the latter has never been Proton-launched on
this Deck at all — confirmed rather than assumed. Resolved candidates on the maintainer's own scan:
16 → 32, roughly double.
<br>`run-linux-tests` 216 → **227 (225 pass; 2 pre-existing, unrelated Decky-messaging failures
flagged separately, not caused by this change)**. Full write-up:
`logs/2026-08-19_moondeck-save-detection.md`; scope decisions recorded in [[Decisions]] → *Linux
discovery*.
<br>**Two follow-ups scoped, not built, same session:** reading Ludusavi's own resolver source
alongside ours (asked directly: "is there a distinct difference... what to learn from it") found it
glob-matches the filesystem and is case-insensitive inside a Wine prefix, which is *why* it never hit
the Borderlands 2 bug — worth generalizing beyond today's one-off fix. Separately, the maintainer
wants native-Linux saves trackable alongside Windows/Proton ones, accepting per-game restore
uncertainty in exchange for being able to try — the data for it already exists in the same manifest,
the real design cost is keeping a Windows save and a Linux save of one game from ever sharing a
version lineage. Both phased plans: [[Backlog]] → *Case-insensitive path matching inside a
Wine/Proton prefix* and *Native Linux save support*; the scope decision each touches is annotated in
[[Decisions]].
<br>**A third, same session: what happens when one game has several real local sources** (MoonDeck
shortcut + a genuine install, or Steam+Epic+GOG all installed at once) **— scoped, and its one
concrete bug fixed same day.** The dedupe's tie-break could pick a MoonDeck pointer over a genuine
local install (it could not before round 1's own fix, since the pointer never used to resolve
anything) — `ScanAsync` now tracks that case explicitly and sorts a genuine install first regardless
of Steam Cloud. `run-linux-tests` 227 → 230; confirmed no regression on the real Deck. The rest —
surfacing multiple sources in the UI, extending `doctor`'s duplicate note across scan sources, the
server-side "two Games already diverged under different spellings" gap — stays in [[Backlog]] → *One
game, several real sources*, by the maintainer's own choice to split the small fix from the rest.
Full answer to "should the dashboard handle this" (no — it already converges cross-machine correctly;
this is a same-machine, agent-side discovery question) is there too.
<br>**A fourth, same session: Cyberpunk was reported wrong again — turned out not to be a bug.** The
Deck has FOUR configured Steam libraries (main root, SDCard, WinSD, SD2) and only SDCard is currently
mounted; Steam's own `libraryfolders.vdf` records Cyberpunk's real 91 GB Steam install (appid
1091500) on `SD2`, which just isn't inserted right now — no scanner, ours or Steam's, can read
missing media, and `SteamRoots.LibraryPaths` already skips it correctly by design. What was missing
was saying so: `doctor` now names every library still configured but not currently reachable.
`run-linux-tests` 230 → 232.
<br>**A fifth, immediately after: classified correctly without needing the card at all.** The
maintainer's own follow-up ("doesn't Steam know it's installed even with the SD not mounted?") named
the lever round four had surfaced but not used — `libraryfolders.vdf`'s per-library `apps` block
persists across a card being absent, on the always-reachable main root. `SteamRoots
.AllKnownInstalledAppIds` reads it; a MoonDeck shortcut whose real AppID appears there is now built as
`SteamInstalled` throughout (real Cloud flag, real AppID, no misleading install dir), never just
`SteamShortcut`. A resolved compatdata prefix alone could never justify this — Steam does not clean up
compatdata on uninstall, so it cannot tell "genuinely installed, unreachable right now" apart from
"uninstalled, orphaned data left behind"; the `apps` block is the one signal that can. `run-linux-tests`
232 → 234. Confirmed on the real Deck with `SD2` still NOT inserted: `scan` now reads
`Cyberpunk 2077  <SteamInstalled> [Steam Cloud] appid=1091500` with the correct save path. One
side effect worth knowing, not a bug: the Cloud flag now hides it from the default Add Games view,
same as any other Cloud-covered installed game.
<br>**The Deck's IP has moved again since this file was last touched** — `tests/testenv.local.ps1`
(gitignored, authoritative) now has `deck@192.168.0.103` / `http://192.168.0.124:5080`, not the
`192.168.68.x` addresses below. Update this file's own IPs the next time they're touched by hand;
`deck-config` (no args) always prints the current, real values.

**One of the two follow-ups above shipped (2026-08-20, branch
`claude/wine-proton-case-insensitive-paths-815ba3`, no task file): case-insensitive path matching
inside a Wine/Proton prefix.** `PathResolver.Wine()`/`.Proton()` now fall back to a segment-by-segment
walk against the real directory listing when the naive exact-case path doesn't resolve, anchored at
the longest matching token root; `Windows()` never reaches it (NTFS handles this at the OS level).
Newest-mtime wins when a parent holds more than one case-insensitively-matching sibling neither of
which is exact. `run-linux-tests` 234 → 237 (235 pass; the 2 failures are the pre-existing, unrelated
Decky "no plugin installed" messaging noted in `logs/2026-08-19_moondeck-save-detection.md`, still not
investigated). Confirmed by hand against both new fixtures with `savelocker resolve --prefix`, the
same method used to root-cause the original bug. Full write-up, including a dead-code correction found
by tracing the algorithm before running it:
`logs/2026-08-20_wine-case-insensitive-and-scoping.md`.
<br>**The other two items asked about the same session — multiple save paths per game, and
registry-based saves — were investigated in depth but deliberately NOT built.** Both turned out to be
genuine schema/architecture changes once the whole codebase was actually traced (a database
primary-key shape and an archive-format extensibility gap, not resolver tweaks), so building either is
a maintainer scope decision, not something to just do. Phased plans, detailed enough to act on without
re-deriving this session's research, now in [[Backlog]] → *Multiple save paths per game* and
*Registry-based saves*.

**Cyberpunk 2077 uploads root-caused, fixed, and confirmed on the maintainer's own hardware
(2026-08-18, branch `cyberpunk-save-upload-fix`).** "Always fails to upload" was never an agent bug —
the maintainer's save is a genuine 100+ MB archive and `maro.savelocker.dpdns.org` is Cloudflare-
proxied, whose free edge kills any single request past a fixed ~100s, which this connection's upload
speed cannot beat. Fixed with a chunked upload protocol (`/upload/begin` → `/upload/{id}/chunk` →
`/upload/{id}/complete`, `ApiClient.UploadAsync`) that never asks one request to carry more than
4 MiB; the old single-shot route stays for agents/servers that haven't updated yet, with an automatic
fallback either way. Write-up: `logs/2026-08-18_cyberpunk-upload-timeout.md`. Gotcha recorded:
`Gotchas.md` → *Hosting / network*. Verified working after a manual local console image + agent
9.9.9 test-installer deploy (not yet a tagged release — see Next action).
<br>**Same session, same branch: the agent UI's Overview page gained a live activity card** — a
"Sync now" button, current phase/game with a byte progress bar for a push (fed straight from the
chunk loop above, `SyncActivityTracker` in `Agent.Core`), and a short rolling log of what just
happened, at the bottom of the page. `SyncEngine` also gained `SyncAllAsync`, deduplicating what the
tray's menu and the new button both do. Verified end to end through a real running tray + isolated
console (sync-now click → activity log → stat updates, all correct); a live mid-transfer progress
snapshot could not be screenshotted locally — loopback is fast enough that a 90 MB chunked push
completes in under a second — but the exact code path that reported it is what the successful push
itself exercised 20+ times without error.

**Markdown tables now render in the console (2026-08-23, branch `claude/markdown-tables-console-d4a57e`,
from [[Backlog]]).** `HelpView.tsx` and `WhatsNewView.tsx` were bare `<ReactMarkdown>`; GFM tables
rendered as raw pipe characters, worst on `cli-reference.md` (almost entirely tables). Added
`remark-gfm`, plus `rehype-raw` so the `<br>`-separated multi-line option lists in that file's table
cells render as real line breaks — react-markdown drops raw HTML by default, and without it the fix
would have swapped one visible bug (raw pipes) for another (literal `<br>` text). `.help-content
table` is now `display: block; overflow-x: auto` so a table wider than the content pane scrolls
sideways rather than being silently clipped by the console's fixed-height, `overflow: hidden` root
(the bounded-flex-column trap in [[Gotchas]]). Verified live in the dev server: all 5 `cli-reference.md`
tables render correctly, `<br>` produces real breaks, no horizontal page overflow. `npm run build`
clean. `agent-update.md` was deliberately written with only one table before this landed — left as-is;
converting more of its bulleted content into tables is an editorial call, not part of this fix.

**Conflict Tier 1's last piece shipped in code (2026-08-24, branch `conflict-version-stats`, from
[[Backlog]], PR #15).** The conflict card showed size and upload time per side but nothing
that says which machine has "more" progress. `SaveArchive.CreateArchive` has always stamped
`entry.LastWriteTime` per zip entry, so a new `SaveArchive.GetArchiveStats` reads file count and
newest mtime straight from a version's archive on demand — no migration, works retroactively on every
version ever uploaded. Two new endpoints share one `SyncService.GetVersionStatsAsync`:
`GET /api/versions/{id}/stats` (agent group, unscoped) and
`GET /api/games/{id}/versions/{versionId}/stats` (admin group, scoped) — the agent route has no UI
caller yet, built ahead of need because conflict resolution is expected to move to the agent later
(Steam-Cloud-style), and that will want this same comparison. Console fetches lazily, only for
versions an open conflict is actually showing, cached by version id (an archive's stats never change
once uploaded) so the 15s poll never re-fetches. A code review then found the archive's mtime was
being read back using the reading process's own local timezone instead of the writing agent's
(wrong by the offset difference between server and agent — fixed to be deterministic instead), a
failed stats fetch that permanently suppressed itself with no retry (fixed), and added a server-side
per-version cache plus test coverage for the previously-untested agent-scoped route. `dotnet build`
(Server + Windows Agent) and `web` build/lint clean; `openapi.json`/`api-types.ts` regenerated;
`run-health-tests.ps1` covers both stats routes (22/22 passing); verified live against a real dev
server + console with two machines pushed into a genuine conflict. Write-up:
`logs/2026-08-24_conflict-version-stats.md`.

**Review fixes for the per-game alias / HasSteamCloud PR (2026-08-25, branch
`steam-cloud-fallback`, no task file).** A code review of that PR (merged as
`01f1c56`) found five issues; all five are fixed on this branch, none of it yet released.
- **`TrackedGame.HasSteamCloud` is now `bool?`, and `null` means "nobody established this"** — not
  "no Steam Cloud". Only enrollment has a `ScanCandidate` to decide it from; the poller's adopt path
  and `add-game` have no such signal, and neither does any entry written before the field existed. As
  a plain `bool` all three read as **false**, which tells a Decky plugin to default its pre-launch
  pull ON and race Steam's own Cloud sync — on a Deck, where adoption is how games usually arrive,
  that was most of them. Re-deriving it from a name-only manifest lookup is *not* the fix and stays
  refused (it is what wrongly called Heroic's Fez a Steam Cloud game). The plugin side still needs
  updating to read `null` as "use your own heuristic".
- **`/api/games` keeps its `id` / `path` field names.** The PR had renamed them to
  `gameId` / `saveDirectory`; that record *is* the wire contract for a consumer shipping from its own
  repo on its own update channel, so an agent updating ahead of the plugin broke every installed one
  for no gain. The four new fields are additive — confirmed by diffing the regenerated
  `agent-ui/src/api-types.ts` against the pre-PR base: **zero deleted lines**.
- `SaveGameAlias` / `SaveGamePullBeforeLaunch` collapse onto one `MutateGameUnderLock` helper (the
  re-read-under-lock reasoning had been copy-pasted a third time). Note the `catch` fallback sets
  `onDisk = this`, so the in-memory mirror is guarded with `ReferenceEquals`, not applied twice.
- `testenv.ps1`'s Decky staging normalises `main.py` to **LF** before matching. SaveLocker-Decky is a
  separate repo, so a `*.py text eol=lf` there checks the file out LF and the old CRLF-only needles
  matched nothing — silently, until the `throw` killed the whole test-plugin build. Verified both
  ways: the old needles miss on LF input, the new code applies all four substitutions on LF *and*
  CRLF.
<br>Full solution builds, `agent-ui` `tsc -b && vite build` passes, `testenv.ps1` parses under
Windows PowerShell 5.1. **No test suite was run** — worth doing before this merges.
<br>Two process notes: `api-types.ts` was **regenerated, not hand-edited** (per [[REPO_MAP]]) by
running a dev daemon on port 5190 against a scratch config, never touching the installed agent on
:5178; and this worktree's `agent-ui` had never had `npm install` run — the fresh-worktree trap
already recorded above, which also blocks the Windows agent build since it shells out to
`npm run build`.

---
**A recall review of the ten merged fork PRs (#1-10), and its fifteen findings applied (2026-08-26,
branch `chunked-upload-integrity-and-review-fixes`).** The one that matters: **the chunked upload
protocol (PR #3) had quietly dropped the all-or-nothing guarantee the single-shot route it replaced
still has.** `ArchiveStore.SaveAsync` deletes its staging file on any failure, so a body that stopped
early was never published. The chunked path kept a faulted chunk's partial bytes *and counted them*,
so the client's retry of that same chunk arrived BEHIND the session, took the "harmless replay"
branch, and the rest of it was silently dropped. On the final chunk that published a **truncated zip
under the content hash of a complete one** - which every other machine then pulled believing it was
intact. Nothing verified the assembled bytes, and the whole protocol shipped with **zero tests**.
<br>Fixed: a faulted chunk rolls back to the offset it started at (so a retry is a real write, not a
mis-read replay) and the byte count only advances once the chunk is durable; `CompleteSession`
refuses to publish bytes that are not a readable archive (422). Note *why* it reads the zip's central
directory rather than re-hashing: the session's `ContentHash` is `SaveArchive.HashDirectory`'s hash
of the save **folder**, not of the zip carrying it, so the server cannot recompute it without
extracting - but a zip's index lives at its END, which makes "did every byte arrive" answerable at
that boundary and nowhere else. Crossing the size cap mid-chunk now drops the session instead of
leaving a stale one that answered the retry 200 and failed the NEXT chunk on an offset mismatch,
hiding why the upload died.
<br>The other twelve, briefly: the agent's chunk retry never caught a **stalled** request (`ServerHttp`
sets no `Timeout`, so HttpClient's own 100s default surfaces as a *cancellation*, not an
`HttpRequestException` - the exact transient this layer exists for fell straight through it, while a
409/413 was slept on three times for nothing); Game Mode's "Sync now" reported failure over a sync
that was still running and about to succeed, and `/api/sync` is now single-flight; `SyncActivityTracker`
snapshots under its lock and orders its writes, so a slow persist cannot strand the Deck's progress
bar on a finished push, and log lines are throttled instead of fsyncing the 50-entry feed per line;
`PathResolver` memoises directory listings per resolver; Add Games names the control actually hiding
rows; the in-app help still sent users to the **upstream** repo's releases; and `testenv`'s WSL and
Windows halves got the same refuse-to-operate guard the Deck half already had before its `rm -rf`,
plus `sync` no longer CR-strips binaries into the clone.
<br>**New: `CS-12` in `run-server-bugbounty-tests.ps1` - 30 checks over the protocol** (byte-identical
round trip, replay, gap-ahead, cross-machine ownership, truncation, mid-chunk fault recovery, the
cumulative cap). **30/30 against the fix; 8 fail against the pre-fix code**, including "the truncated
session published no version" - i.e. the old server really did publish a corrupt archive.
`run-linux-tests` **235/237**, the recorded baseline (the 2 are the pre-existing Decky messaging
failures). Full solution builds; `agent-ui` `tsc -b && vite build` passes.
<br>**Two things deliberately NOT done**, both recorded so they are not re-derived: `complete` still
has no retry - it is not idempotent server-side (a second call hits `TryRemove` and 404s), so
retrying a lost response would turn a success into a spurious hard failure; making it safe needs the
server to remember completed sessions. And `web/src/help/decky-plugin.md` + `DeckyPlugin.cs` still
name `SkorcherX/SaveLocker-Decky`: the fork's own `Marwanello/SaveLocker-Decky` **exists but has no
releases**, so repointing the documented `Install Plugin from URL` would break it today. Revisit once
a plugin release is cut there.

---

**The LA-04/05/06/07 regression suite landed and `WA-01` is fixed (2026-08-27, branch
`linux-regression-tests`).** The new `tests/linux/run-linux-regression-tests.sh` (four bugs fixed in
it: a field-name misdiagnosis correctly reverted, a missing `agent register` step, a `wait` deadlocked
on the wrong PID set, and bash vars silently not expanding inside a single-quoted heredoc) is now
wired into `testenv.ps1 test` and passes 15/15. Also fixed while triaging a full `testenv.ps1 test`
run: a real lost-update bug in `AgentCli.cs`'s `add-game` (an unconditional `config.Save()` reopened
the window `SetTracked`'s own re-read-under-lock had already closed), a hardcoded WSL distro name
(`Ubuntu-24.04` vs. this machine's `Ubuntu`) in two server-bugbounty scripts, and a `testenv.ps1 sync`
bug that skipped the git fetch+checkout step on a clean working tree, letting the persistent WSL
clone silently rot on an old commit. The two long-standing `run-linux-tests.sh` Decky "no plugin
installed" messaging failures are also fixed (an unrelated MoonDeck fixture directory was making a
plain directory-existence check read `true`) — 237/0, confirmed no MoonDeck regression. **`WA-01`
the dashboard is told the real reason** (backlog, found 2026-08-14, intermittent, never
investigated) is root-caused and fixed: the test's polling loop accepted `CommandStatus.Dispatched` —
documented as *"a lease, not a terminal state"*, set the instant the agent claims a dashboard command
— as good enough, so a 1-second poll could occasionally catch the tiny window before the agent
actually reported a result and grab `{result: null}`. Reproduced live (WA-01 needs no interactive
desktop, unlike the tray tests later in the same file) and fixed by waiting for the real terminal
states (`Done`/`Failed`) instead; confirmed 10/10 twice after the fix, having failed once before it.
Full write-up: `progress.md` and `session_summary.md`.

---

**Decky conflict-resolution Phase 0/1 shipped (2026-08-29, this branch, no task file — full design in
`tasks/_done/conflict-resolution-ui/plan.md`).** Server + Agent.Core only; no UI, no Decky/Playnite
work — those are later phases. The server no longer decides a conflict: `SyncService.IngestAsync`'s
`NewestWins`/`PreferMachine` auto-win branch is gone, so every divergence unconditionally records a
`ConflictFlag`. `SyncEngine.TryPolicyResolveAsync` is the new agent-side decision point — it fires
after a push comes back `Conflict`, fetches the game's policy, and calls the same mechanical resolve
endpoint a human would use when the policy isn't `Manual`. `ResolveConflictAsync` gained
`resolverMachineId` so an agent resolving its own push isn't queued a redundant pull; the admin path
(null) is unchanged. New routes: server agent-group `/api/agent/conflicts[...]` +
`/api/agent/games/{id}/conflict-policy`; matching local-API proxies on `:5178`
(`/api/conflicts[...]`, `/api/games/{id}/conflict-policy`, `/api/games/{id}/sync-status`); new CLI
`conflicts` / `resolve-conflict --keep local|cloud [--keep-both]` (added so this phase is genuinely
usable end-to-end from the CLI alone, as the doc claims — it wasn't when first drafted).
<br>**Verified:** `run-agent-tests` 47/47, `run-server-bugbounty-tests` 194/194 (CS-04's `NewestWins`
auto-accept and the new "winning uploader gets no redundant pull" assertion both pass; admin
Set-as-Latest fan-out unchanged), `run-health-tests` 22/22; `openapi.json` and both `api-types.ts`
(web, agent-ui) regenerated against live scratch instances and diffed (only the intended new
routes/types appear); full solution and both frontends build clean.
<br>**Gotcha hit again, same as 2026-08-25:** this worktree's `agent-ui` AND `web` both needed a
fresh `npm install` — neither had `node_modules`. Blocks the Windows agent build (shells out to
`agent-ui`'s `npm run build`) and `web`'s own build/typecheck alike; see [[Gotchas]].
<br>**A pre-existing, unrelated flake found and NOT fixed** (out of this phase's scope): `run-server-
bugbounty-tests.ps1`'s CS-03 section deterministically throws opening a just-written archive with
`FileShare.None` (`MethodInvocationException`, uncaught, kills the whole script silently after
printing a truncated PASS count with exit code 0) — reproduced 2x, most likely Windows Defender
real-time protection (confirmed active on this box) transiently holding the file. Verified CS-04
onward (the sections this phase's tests actually live in) by temporarily patching in a retry loop,
confirming 194/194, then reverting the patch — the test file is untouched in the final diff. Worth a
real fix in a session that owns test-suite maintenance, not folded into this one.
<br>**Not yet run against real hardware or a live fleet** — this phase has no UI of its own; nothing
is visibly different to a user until Phase 3 (dashboard Backups tab, zero dependencies, could ship
first) or Phase 5/6 (Decky UI) land. Phases 2 (Force-push bookkeeping fix), 4 (Linux wrapper gate), 7
(sync-status, already partly done here since the endpoint exists), and 8 (Playnite) remain open — see
[[Backlog]].

---

**Decky conflict-resolution Phase 2 shipped (2026-08-30, this branch, no task file — design already
settled in `tasks/_done/conflict-resolution-ui/plan.md`).** The Force push/pull bookkeeping fix,
server-side only. Before this, `PrepareUploadAsync` skipping divergence detection outright on
`force:true` meant a forced push could land while the game already had an open `ConflictFlag` from an
earlier, unforced divergence — moving the head to a brand-new version that was neither side of it, so
the flag was orphaned (its own rewind guard means nothing can ever close it normally again), neither
of its two versions was protected from ordinary retention, and the other machine it involved was never
told the head had moved. `SyncService.IngestAsync`'s fast-forward branch now calls a new
`CloseOrphanedConflictsOnForceAsync` whenever `force` is true: closes every open conflict on that game
(`Resolved`, tagged `"force-push by {machine}"`), protects **both** sides of each as recoverable
backups (nobody was actually asked which to keep, so — same as an explicit "keep both" — nothing is
silently lost), and queues the same unforced pull fan-out a real resolve gets. A no-op when nothing is
open, so an ordinary forced push looks exactly as it did before — no new routes, DTOs, or UI.
<br>**Verified:** new `CS-13` section in `run-server-bugbounty-tests.ps1` (open a real conflict, force
past it, confirm the flag closes, both sides protect, the resolution is audited, the stranded machine
gets an unforced pull and the forcing machine doesn't, its next push is clean, and a control case
proving a force push with nothing open adds no bookkeeping) — 207/207 (up from 194, no regressions);
`run-agent-tests` 47/47 unchanged. `openapi.json`/`api-types.ts` untouched on purpose — no API surface
changed. Full solution builds clean.
<br>**Gotcha hit again, same as 2026-08-25/29:** this worktree's `agent-ui` had never had `npm
install` run, blocking the Windows agent build; see [[Gotchas]].
<br>**Test bug found and fixed while verifying, not a server bug:** the first CS-13 draft had the
stranded laptop machine call a plain (non-forced) `pull` to catch up after the resolution. That machine's
OWN earlier push had been rejected as a conflict, so its `LastSyncedHash` was never advanced past what
it pulled before — an ordinary pull correctly refuses to overwrite what the agent still sees as its own
unsynced local content (WA-02's guard), exactly like `run-health-tests.ps1`'s existing "take the server
copy" `--force` pull. Fixed by adding `--force` to that one step, matching the established idiom rather
than treating it as a product bug.
<br>**Not yet run against real hardware or a live fleet** — same as Phase 0/1, invisible until a later
phase's GUI work lands. Phases 3 (dashboard Backups tab), 4 (Linux wrapper gate), 5/6 (Decky UI), 7
(sync-status), and 8 (Playnite) remain open — see [[Backlog]].

---

**Decky conflict-resolution Phase 3 shipped (2026-08-30, same session as Phase 2, this branch, no task
file — design already settled in `tasks/_done/conflict-resolution-ui/plan.md`).** The dashboard
"Backups" tab, client-side only, zero new server endpoints. A version is "in the main tree" if it's an
ancestor of the current head — walk `parentVersionId` back from it; anything else the game still has
is a backup, almost always the losing side of a past conflict. `GameDetail.tsx` already fetched the
full version list and the head id, so this is purely a computed split of data already on hand. The old
single "Versions" table (`web/src/components/GameDetail.tsx`) is now a `Versions (N)` / `Backups (N)`
tab toggle over the same table and the same per-row actions — Download, Set as Latest,
Protect/Unprotect, Delete are unchanged; promoting a backup back to head via Set as Latest also
resolves any open conflict it was part of, for free, through the existing `SetHeadAsync`
superseded-conflict logic.
<br>**Verified live**, not just built: seeded the exact Phase 2 scenario (two machines, a real
diverged conflict, force-pushed past it) against a throwaway dev server on `:5179` + the dashboard dev
server on `:5173`, and confirmed in the browser that Versions correctly shows the head's 3-version
ancestor chain — including the conflict's non-losing side, still `Protected` from Phase 2's fix, even
though it's on the main tree — and Backups correctly shows the one truly orphaned version (also
`Protected`) with all four actions present and no console errors. `web` build (`tsc -b && vite build`)
and lint (`oxlint`) both clean.
<br>**A minor design note surfaced by seeding a real scenario, not a bug:** both sides of an
overridden conflict get `Protected` by Phase 2's fix, but only the truly-superseded one necessarily
lands under Backups — a side that happens to still be an ancestor of the (later-advanced) head shows
under Versions instead, protected but otherwise ordinary. Expected given the purely head-ancestry-based
classification; not worth a special case.
<br>**Not yet run against real hardware or a live fleet** — same as Phases 0–2; this is the first
phase with a visible UI change, so it's also the first one a maintainer could actually eyeball on a
real deployment. Phases 4 (Linux wrapper gate), 5/6 (Decky UI), 7 (sync-status), and 8 (Playnite)
remain open — see [[Backlog]].

---

**Decky conflict-resolution "Group 2" shipped (2026-08-31, this branch, no task file — the practical
execution grouping in `tasks/_done/conflict-resolution-ui/implementation-grouping.md`, chosen there as "the
biggest value-per-session in the whole plan").** Phase 4 (Linux wrapper launch gate) + Phase 6 (shared
`agent-ui` conflicts page + the `doctor`/log gap), together — the pair `implementation-grouping.md`
picked because it makes conflict resolution end-to-end usable on a plain Linux box for the first time,
with zero Decky/Windows/Deck-hardware dependency.
<br>**Phase 4**: new `SyncEngine.PrepareLaunchAsync` (`LaunchDecision`/`LaunchGateResult`) replaces
`ProtonRun.ExecuteAsync`'s bare `OnGameLaunchAsync` call — Windows' `TrayApp.cs` is untouched on
purpose, since Phase 7 (Windows tray wiring, still open) is what would touch that, and Windows has no
pre-launch boundary this certain anyway (`OnGameLaunchAsync`'s own doc comment). Blocks the launch
outright for exactly one reason — a genuinely confirmed, open `ConflictFlag` — never for the game
running elsewhere, lock contention, or a network hiccup, which all still proceed exactly as before.
Two checks, in order: an already-open conflict for the game short-circuits before the lease is even
taken, and "commit-before-choose" (the plan's own term) attempts an ordinary push first so unsynced
local changes become a real, hash-verified conflict rather than a bare pull refusal — cheap in the
overwhelmingly common case, since `PushCoreAsync` already bails on a hash comparison alone with no
network call at all when nothing changed since the last push. New `AgentEventCodes.LaunchBlocked`.
<br>**Phase 6**: new `agent-ui/src/components/ConflictsView.tsx` — genuinely new code against the
agent's own local API (a distinct wire protocol from the dashboard's, per [[REPO_MAP]]), not a port of
`web/src/components/GameDetail.tsx`'s card, though the visual shape (machine, timestamp, size, file
count, newest-change, Use as Latest / Keep both) is deliberately copied from it — plus a fourth
`Sidebar.tsx` nav entry, badge-numbered by open-conflict count. A conflict only ever carried version
ids, so two new server routes were needed: agent-group `GET /versions/{id}` (mirroring the existing
`/versions/{id}/stats`, same flat/unscoped trust boundary) and matching local-API proxies
(`GET /api/versions/{id}`, `GET /api/versions/{id}/stats`) so the card can show machine/timestamp/size
and file-count/newest-change for both sides. Also closes the confirmed gap Phase 0/1's own write-up
never actually filled: `Doctor.cs` gained a "Conflicts" section, one line per open conflict plus the
`http://127.0.0.1:5178/#conflicts` URL — informational, not a `Problem`, the same way an open conflict
under `Manual` policy already reads elsewhere in this codebase.
<br>**Verified live, not just built**: seeded a real two-machine divergence (register, add-game,
diverging pushes) against a throwaway dev server, confirming a genuine `ConflictFlag` existed;
`doctor` confirmed printing the new Conflicts section with the right game name, "overdue" flag and
URL; a Playwright screenshot of the actual `agent-ui` page at `:5178/#conflicts` (well, the scratch
port used here) showed both sides correctly labeled — including "This device (Machine2)" — and
clicking "Use as Latest" resolved the conflict through the real server: the head moved to the clicked
version, the sidebar badge cleared, and the empty state rendered. Separately confirmed with
`savelocker run`: it refused to launch a game with a genuine open conflict (child process never
started, exit code 1, correct message), launched normally once the conflict was resolved, and — the
control case — that ordinary lease contention alone (no conflict) still only warns and launches, never
blocks. `run-agent-tests.ps1` 45/45 and `run-health-tests.ps1` 22/22 unchanged (this session's
container cannot build or run `run-winagent-tests.ps1`/`run-server-bugbounty-tests.ps1`, both of which
hard-depend on the Windows-only `src/Agent` build). `openapi.json` and both `api-types.ts` (web,
agent-ui) regenerated against live scratch server/daemon instances and diffed — only the two intended
new routes/types appear in each. Every Linux-buildable project (Shared, Server, Agent.Core,
Agent.Linux) builds clean; `agent-ui`'s `tsc -b && vite build` and `oxlint` are clean (two pre-existing,
unrelated warnings in `AddGamesView.tsx`/`SettingsView.tsx`).
<br>**Not yet run against real hardware or a live fleet** — this is the first phase in the expanded
plan with a real, usable end-to-end conflict-resolution surface on a *plain* Linux box (no Decky), so
it's the first one worth a maintainer actually trying on the real Deck's Desktop Mode or a WSL rig.
Phases 7 (Windows tray wiring), 8 (Game Mode screen), 9 (D-Bus notify impl), 10/11 (Decky), 13
(Playnite), and 14 (webhook + block-launch opt-in) remain open — see [[Backlog]].

**Conflict UI redesign — local vs. cloud, plus a sync-time pop-up (2026-08-31, same session, this
branch, asked directly rather than scoped up front — the maintainer reviewed Group 2's card and
wanted the framing changed before anything else built on it).** Two requests, both mocked up first
as a published Artifact and approved before any code changed:
1. **Reframe every conflict card as "This device" / "The cloud," never machine name vs. machine
   name** — the machine name that raced the cloud is now a small caption ("from \"Living Room PC\"")
   on whichever side isn't this device, never the panel's own label; the cloud side is *always*
   labelled "The cloud" regardless of whose push last updated it (plan.md decision 2, which this
   corrects the UI to actually match — Phase 6's first pass had drifted from it). Recency ("12m ago")
   is now the headline number in each panel, with a small "newer" tag on whichever side is more
   recent; the exact timestamp is still there as a hover tooltip. New two-step interaction: click a
   side (or its "Keep this" button) to select it, a separate "Resolve with {side}" button confirms —
   replacing the old one-click-does-it four-button layout (Use as Latest / Keep both, ×2 sides).
2. **A sync-time pop-up**: pressing "Sync now" on the Overview page now pauses on each conflict it
   surfaces, one at a time, in a `position: fixed` overlay covering the *entire* app shell (sidebar
   included, not just the content pane) — reusing the same card in a second interaction mode where a
   side's own button resolves immediately and the queue auto-advances, since the point there is
   momentum through a queue, not a considered review. A "Decide later" control always has somewhere
   to go: it dismisses the pop-up and switches straight to the Conflicts view (badge still lit),
   never just closes back to a spinner with no trace. Deliberately scoped to the agent-ui **Sync now**
   button only — the passive 15s conflicts poll never opens it, and the Windows tray's own "Sync
   All"/per-game menu items don't yet either, since making the tray raise this same window is Phase
   7 (unbuilt, Windows-only, correctly deferred rather than guessed at from this Linux container).
<br>**Refactored for reuse, not duplicated**: `ConflictCard.tsx` (the local/cloud card itself, taking
a `mode: 'confirm' | 'immediate'` prop) and `useConflictVersions.ts` (the version/stats fetch-and-cache
hook) are now shared by `ConflictsView.tsx` and the new `SyncConflictModal.tsx`, so the two surfaces
cannot drift out of visual sync with each other the way two independent copies eventually would.
<br>**One design bug caught and fixed during live verification, not left in**: the card's first draft
pre-selected "This device" by default via a `useState` initializer reading `versionB?.machineName`
before that version had actually loaded (an async fetch), so the "smart default" silently never fired
— confirmed on a real screenshot showing neither side selected when it should have been one. Fixed by
dropping the default entirely rather than chasing the race: no side is ever pre-selected, matching
this same design's own "never silently default to a side" rule for the Decky-equivalent popup.
<br>**Verified live against a real dev server + Linux daemon**, not just built: seeded a genuine
two-machine conflict, screenshotted the pop-up appearing correctly on "Sync now" (both sides
correctly labelled, "newer" tag on the right side, full-shell dim behind it), confirmed "Decide
later" lands on the Conflicts view with the same conflict rendered in confirm mode, confirmed the
confirm-mode Resolve button starts disabled and enables with the right label once a side is picked,
confirmed a real resolve moves the server head and clears the badge, and separately confirmed
immediate mode (clicking a side inside the pop-up) resolves right away and closes the pop-up once its
one-conflict queue empties. `agent-ui` `tsc -b && vite build` and `oxlint` both clean (same two
pre-existing, unrelated warnings as before). No server/`Agent.Core` changes this round — purely
`agent-ui`.

**`ConflictCard` restyled to actually match the approved mockup, plus a repeatable manual test
path (2026-08-31, same session, asked directly — "the artifact looks much better... it's ugly now
with the red border").** The redesign above had gotten the local-vs-cloud *framing* right but never
matched the mockup's actual look: the card had carried a dark reddish tint (`#241a1a` background,
`#4a2a2a`/danger-red borders) on every conflict, escalated or not — the "ugly red border" — instead
of the mockup's neutral `#1E252A` card on `#34424b`, panels on `#222d34`/`#3a4750`, an amber
"CONFLICT" chip, and a plain-language sentence ("This device and the cloud both changed since the
last sync..."). Red is now reserved for the genuine overdue-escalation line only, same as before.
`ConflictCard.tsx` was rewritten line-for-line against `tasks/_done/conflict-resolution-ui/`'s published
"Local vs Cloud" artifact (still live: ask `Artifact` → `list` for the URL) — panel padding/radius,
the mono "recency" headline with its "newer" tag, the per-side caption wording ("This is the machine
you're using right now." / `Last updated from "X"`), and the footer's dashed rule + resolve button
are all copied verbatim rather than approximated. No component structure changed — same
`ConflictCard`/`useConflictVersions` shared by `ConflictsView` and `SyncConflictModal`, so both
surfaces picked up the fix for free.
<br>**New: `tests/seed-test-conflict.sh`** — the manual "how do I see a conflict without my real
games" path asked for in the same message. Runs entirely against a throwaway scratch server and two
fake machine configs (`Living Room PC` / `This Device`, dummy `savefile.txt`s under
`~/savelocker-conflict-test`, isolated the same way `testenv.sh` isolates Linux state via
`XDG_DATA_HOME`) — never touches a real server, a real Steam save, or `~/.local/share/SaveLocker`.
`up` builds if needed, starts the scratch server + daemon, seeds a genuine two-machine divergence via
the CLI (register → add-game → push → diverge → push), and prints the Conflicts-page URL; `again`
reseeds a fresh one (see below); `down`/`clean` tear down (`clean` also deletes the scratch state).
<br>**One real bug found writing it, not a test artifact:** the first `again` draft tried to be a
"lighter" reset — resolve the open conflict, wipe just the two machine configs, reseed — and it
produced a conflict on the *wrong* side unpredictably. Root cause: a brand-new machine's first-ever
push is only unconditionally conflict-free when the game has **no head at all yet** (Phase 0/1's own
rule — even a never-synced machine is compared against the current cloud head, not exempted from it).
Reusing the same long-lived server DB across `again` calls meant whichever side happened to push
second inherited a stale head from a *previous* run and conflicted against content that was never
part of the story it was telling. Fixed by making `again` a full reset (server-data included), not a
lighter path — deterministic every time, same as a fresh `up`. Also caught before that: `set-server`/
`register` write to `$XDG_DATA_HOME/SaveLocker/config.json`, not straight under `$XDG_DATA_HOME` —
an early draft's cleanup path silently missed the config file it meant to delete.
<br>**Verified live**: ran `up`, screenshotted the Conflicts page (no red anywhere, chip/sentence/
caption/newer-tag all present, matches the artifact), clicked a side and confirmed the selected-state
styling (accent border/gradient/icon, "Resolve with This device" enabling), ran `again` and confirmed
a fresh conflict reseeds deterministically, and screenshotted the sync-time pop-up (`Sync now` on
Overview) showing the same restyled card in immediate mode over the full shell. `agent-ui`
`tsc -b && vite build` and `oxlint` clean (same two pre-existing warnings). No server/`Agent.Core`
changes.

**`seed-test-conflict.sh` now stages `agent-ui` itself, same session, follow-up to the above** — the
maintainer hit exactly the documented WSL gotcha (*"WSL usually has no native `node`"*, `Gotchas.md`)
running the manual copy step by hand: plain WSL has no native Node, so `npm` resolves through Windows
PATH interop to `/mnt/c/.../npm.exe`, which fails on this shell's UNC-style cwd (`CMD.EXE...UNC paths
are not supported`, then `'tsc' is not recognized`). New `stage_ui()` (mirrors `testenv.sh`'s own
function of the same name and purpose) tries, in order: a **native** Linux npm right there (checked by
resolving `command -v npm` and rejecting anything under `/mnt/*` — that path can only be Windows'
npm.exe reached through WSL interop); an already-built `agent-ui/dist` on disk; a Windows-side checkout
via `$SAVELOCKER_WIN_REPO` (same convention `testenv.sh` already established); and only then a refusal
naming both fixes, rather than starting a daemon whose UI is silently a blank page. Also fixed the copy
destination itself: `AgentApiServer` serves from `<bin>/agent-ui` directly, not `<bin>/agent-ui/dist` —
an early manual attempt nested it one level too deep and got a 404, not the old UI. `up`/`again` both
call it now; no more manual `npm run build` + copy step. Verified live in this session's native-Linux
container (the one environment here that has a real npm): fresh `clean` → `up` auto-built and staged
`agent-ui/dist`, the Conflicts page rendered correctly, and a second `again` rebuilt cleanly too. The
WSL-without-native-node and `SAVELOCKER_WIN_REPO` branches are reasoned through against the documented
gotcha but not exercised on a real WSL box this session — worth a maintainer confirmation next time
this is used from Windows.

**`seed-test-conflict.sh` also self-heals npm/cli#4828, hit for real on the maintainer's own WSL
worktree (`.claude/worktrees/...`, same session, immediate follow-up).** `npm run build` died with
`Cannot find native binding` — rolldown's (Vite 8's bundler) platform-specific `.node` binary either
never got installed or went stale in this worktree's `node_modules`, a known npm bug
(https://github.com/npm/cli/issues/4828) whose own fix is "delete `node_modules` and
`package-lock.json`, reinstall." New `build_ui_or_recover()` tries a normal build first, greps the
captured output for that exact message on failure, and only then does the clean
`node_modules`+`package-lock.json` wipe and reinstall + one retry — a build that fails for any other
reason still surfaces its real log and dies, so this never masks an actual code problem as "just
reinstall." Confirmed against a faithful repro, not the error text alone: deleted this repo's own
`@rolldown/binding-linux-x64-gnu/*.node` (the exact file the real error names) and ran `up` — it
detected the failure, reinstalled, and rebuilt successfully on the retry. A first attempt at
reproducing it by corrupting `rolldown`'s `package.json` main field instead threw a *different*
`ERR_MODULE_NOT_FOUND` earlier in module resolution — not a faithful repro of the reported bug, caught
by actually reading the diverging error rather than assuming the first repro attempt matching "some
error" was good enough. `agent-ui/node_modules` and `package-lock.json` restored to their committed
state afterward (the repro necessarily dirties both; regenerating `package-lock.json` also happens to
bump transitively-pinned `^`-range packages like `vite` 8.1.3→8.2.2, an expected but noteworthy side
effect of the real fix a maintainer hitting this for real would see too, worth knowing rather than
being surprised by).

**`seed-test-conflict.sh` made a genuinely separate parallel app, not just separate state (same
session, immediate follow-up — the maintainer worried it "messed up" their real remote-server setup
and asked for testenv-style isolation).** Every earlier version still built Server/Agent.Linux with a
plain `dotnet build` into their project's own default `bin/Debug/net10.0/`, and staged agent-ui into
`agent-ui/dist` before copying — the same paths a real dev daemon on the same machine, pointed at a
real remote server, would be built and running from. Rebuilding those out from under a live process
is exactly the "messed up my real one" risk, even though the *data* (server DB, machine configs,
conflicts) was always fully isolated under `$STATE` via `XDG_DATA_HOME`. Fixed by routing every build
artifact into `$STATE/build/` instead: `dotnet build -o` for Server and Agent.Linux (their `-o`
argument redirects the final output copy; `obj/` intermediates are still the project's own, but
those are never what a running process executes from), and `vite build --outDir ... --emptyOutDir`
for agent-ui, so nothing is ever written to `agent-ui/dist` either. This goes a step further than
`testenv.sh` itself, which still builds Agent.Linux into the shared default `bin/Debug` path — worth
knowing if that script is ever run alongside a real dev daemon too.
<br>**Verified, not assumed**, that `-o` actually avoids the shared path: built Agent.Linux once
into the default location, recorded `savelocker.dll`'s mtime, then built again with `-o /tmp/...` and
confirmed the default file's mtime was **unchanged** while the fresh binary appeared only under
`/tmp/...` — including checking whether the csproj's own `CopyAgentUiDist` MSBuild target (which
references `$(OutputPath)` directly, a separate property from the `-o` override, and could in
principle still land in the shared path even with `-o` given) fired against the default directory;
it didn't. Ran the full `up` → `again` → `clean` cycle afterward against the new isolated paths and
confirmed the Conflicts page still renders correctly and `clean` removes the entire isolated build
tree along with the rest of `$STATE`.

---

**Decky conflict-resolution "Group 3" shipped (2026-09-01, this branch, no task file —
`implementation-grouping.md`'s Group 3: Phase 9's actual D-Bus notification implementation, unblocked
now that Group 2 shipped Phase 6).** `CommandPoller` (Agent.Core) gained an optional
`onConflictsPolled` hook, fired once per 20s tick with this machine's open conflicts (filtered to
`MachineId`, the same "no bystander case" filter `doctor`/`savelocker conflicts` already apply) —
`null` by default so the Windows agent's tick is byte-for-byte unchanged (Phase 7, wiring the tray to
this, is still separate and open). New `src/Agent.Linux/ConflictNotifier.cs` is rung 3 of the
escalation ladder itself: a `HashSet<Guid>` notifies once per conflict id the moment it's seen open
and forgets ids the moment they close (not on a timer), gated on Phase 5's
`DesktopEnvironment.NotificationDaemonPresent` so the overwhelmingly common no-desktop case costs
nothing beyond a set comparison, and fires `org.freedesktop.Notifications.Notify` via `gdbus` (the
tooling Phase 5's own `NameHasOwner` probe already uses) with a "View conflict" action button. A
`gdbus monitor` is started lazily on the first notification actually sent — one for the whole life of
the process, not one per notification — and opens Phase 6's `agent-ui` conflicts page via `xdg-open`
when its `ActionInvoked` signal matches an outstanding notification id. Deliberately does NOT bring
Phase 8's Game Mode screen to the foreground on click — that screen doesn't exist yet (Group 4, next)
— the same way Phase 4 left Windows' tray untouched for Phase 7.
<br>**A real bug found and fixed before this shipped, not left in**: the first draft's GVariant
`Notify` arguments were bare or naively single-quoted, which breaks the instant a game name contains
an apostrophe — Baldur's Gate 3, Assassin's Creed, extremely common titles, not an edge case. A bare
`SaveLocker` token also isn't valid GVariant STRING syntax at all (strings must be quoted). Fixed with
a `GVariantString` helper (double-quoted, C-style `\`/`"` escaping) and explicit type annotations for
the two numeric arguments `gdbus` can't reliably infer without introspection (`uint32 0` for
`replaces_id`, `@a{sv} {}` for the empty hints dict).
<br>**Verified**: all Linux-buildable projects (Shared, Server, Agent.Core, Agent.Linux) build clean.
`run-linux-tests.sh` extended the harness's existing `deck_cfg`/`other_cfg` two-machine "Fake Prefix
Game" fixture into a genuine conflict (a divergent push from the machine that never re-pulled),
started a daemon on the stuck machine, and confirmed the log shows the conflict noticed on the next
20s tick — correctly gated to the "no notification daemon reachable" branch, since this sandbox (like
CI) has no real D-Bus session bus. 244 passed. **The 7 failures alongside it were the same pre-existing
Decky-plugin-update flake already recorded in [[Backlog]] from Phase 5 (2026-08-30) — reconfirmed by
`git stash`-ing this session's own changes and re-running**: identical 7 failures by name, 240 passed
on the pre-change commit, 244 with this change's 4 new checks added on top — proof this session
introduced no regression, not an assumption.
<br>**Then root-caused and fixed, same session, asked directly.** All 7 were one cause: `DeckyPlugin
.CanReplace`'s "new top-level file refused" case is simulated in the test with `chmod 555` on the
plugin directory, and this dev/CI container runs as **root** — root's `CAP_DAC_OVERRIDE` bypasses
ordinary permission bits, so the simulated write silently succeeded instead of being refused,
corrupting the plugin's on-disk version for every downstream assertion that read it (the other 6 were
that one bad write cascading, confirmed by which ones failed: the two that read the version directly,
plus every check after them in that block). Fixed by swapping the simulation to the ext4 immutable
attribute (`chattr +i`/`-i`) — a filesystem-level restriction the write-probe cannot bypass even as
root, confirmed directly before changing the test (a new file under `+i` fails for root exactly as for
an unprivileged user; overwriting an existing file's content still succeeds, since that never touches
the directory's own inode), so the same test now reproduces the real root-owned-755 constraint
regardless of which user runs it. `run-linux-tests.sh` 244/251 → **251/251**.
<br>**Not yet run against real hardware** — the one thing this sandbox genuinely cannot check, and
the same open question `plan.md`'s 2026-08-31 correction already named: whether a real `Notify` call
renders a visible popup in Desktop Mode (and Game Mode, still unconfirmed either way), and whether the
action button's click actually opens the browser to the right page. Needs a live Deck Desktop Mode
session or a plain desktop Linux box — the plan's own explicitly lower priority for scripted D-Bus
coverage, unchanged by this session. Group 4 (Phase 8, the Game Mode conflict screen) is next per
`implementation-grouping.md`; Phases 7 (Windows tray wiring), 10/11 (Decky), 13 (Playnite), and 14
(webhook + block-launch opt-in) remain open — see [[Backlog]].

---

**Phase 9 run on the real Deck — the shipped version was broken, root-caused and fixed (2026-09-02,
this branch, `save-conflicts-phase-9`).** The maintainer deployed the Group 3 build to their Deck via
`testenv.ps1`'s Deck target (isolated at `~/savelocker-test` / `~/savelocker-test-state` on :5177,
never the real install), seeded a genuine two-machine conflict, and got `conflict notification: sent
for 'Notify Test'.` in the log — **and nothing on screen.** In Desktop Mode, with a working
notification daemon.
<br>**Root cause: a notification carrying `actions` is owned by the bus connection that sent it, and
the server withdraws it when that connection drops.** `gdbus call` — the transport plan.md's own
Phase 9 tooling decision picked — is one-shot by construction: it sends, takes its reply and exits,
so it can never hold an actionable notification open. Every layer the agent can see reported success:
exit code 0, a real notification id, a log line saying "sent." Bisected on the hardware against the
same daemon on the same bus, one variable at a time: `expire_timeout=0` alone renders and persists;
`app_name`/`icon` alone render (`dialog-warning` correctly draws a caution icon, not a bug);
**adding the action button is what makes the popup flash and vanish inside a second.**
<br>**Fixed by rewriting `ConflictNotifier` around `notify-send --wait --action`**, confirmed present
and working on the Deck before the rewrite (libnotify; the maintainer verified the popup persists and
clicking the button prints `view`). `--wait` holds the connection open for as long as the
notification is displayed, which fixes the popup *and* the button, and it prints the invoked action
key on stdout — so the entire second `gdbus monitor` connection the first version used to catch the
click (which could never have owned the notification anyway) is **deleted**, not fixed. Net simpler:
one mechanism instead of two, and the GVariant hand-escaping is gone too, since `notify-send` takes
plain argv. Bonus the ownership rule buys for free: killing the child withdraws its popup, so a
conflict resolved from the dashboard, the CLI or another machine now takes its own stale notification
off the screen instead of leaving a button that resolves nothing. `gdbus` is untouched as Phase 5's
`NameHasOwner` probe — that part was never in question.
<br>**The regression test that should have existed**, added with the fix: a fake session bus (a real
listening socket), a fake `gdbus` answering the `NameHasOwner` probe, and a fake `notify-send`
recording its own argv, asserting the command carries `--wait`, the `view` action key, and the
conflicted game's name. **Confirmed to FAIL against the pre-fix code, not assumed to**: `--wait` was
removed, the daemon re-run, and the recorded argv checked — the assertion flips. `run-linux-tests.sh`
251 → **256/256**.
<br>**The lesson worth keeping, bigger than the fix: an exit code from a fire-and-forget IPC call is
not evidence the other end did anything.** This shipped green through a clean build, a full agent
suite and a careful read because every check stopped at "the call returned 0." Nothing looked at what
was actually sent, which is exactly what the new test now does. Also worth knowing for next time: the
whole diagnosis came from three one-line `gdbus` commands run by hand on the hardware, each changing
one argument — the bisect was faster and more certain than any amount of reading the code.
<br>**Confirmed on real hardware 2026-09-02:** the shipped `notify-send --wait` build renders on
a Deck in desktop mode via `testenv.ps1 up -Only deck` — user-confirmed on the actual fix, not
inferred from the three bisect variants that led to it.
<br>**Still unverified:** Game Mode (the open question `plan.md` has carried since 2026-08-31 — the
bus and a claimant are both there, whether anything renders is unknown), and the action button's
`xdg-open` actually landing on the conflicts page on the Deck specifically.

---

**PR #26 code review (2026-09-02, this branch): 14 findings, 12 fixed, 2 left as design calls —
one of those two then confirmed on real hardware, same session.** The review's angle-C finding
called out that `Withdraw()`'s "bonus the ownership rule buys for free" above (line 773) rests on
undocumented, daemon-specific behavior — killing the connection, not the documented
`org.freedesktop.Notifications.CloseNotification(id)` — and had zero test coverage. `notify-send`
now also passes `--print-id`; `ConflictNotifier` captures the id off the same stdout stream the
action key already comes from, and `Withdraw()` tries `CloseNotification(id)` first. **Confirmed
directly on the Deck, same session:** backgrounded `notify-send --wait --print-id`, captured the
printed id, `sleep 5` (popup stayed up the whole time, not a flash — the earlier attempt at this
test called `CloseNotification` too fast to tell), then `gdbus call ... CloseNotification <id>` —
the popup vanished exactly at the call and `notify-send` exited on its own (status 0), no `Kill()`
needed. Kept as belt-and-suspenders regardless: `Withdraw()` still always kills the process too, so
a daemon that doesn't honor `CloseNotification` falls back to the path already verified above. The
other left-as-is finding, unifying `ConflictNotifier._notified` with `HealthReporter`'s own
notify-once `HashSet`, was a design call, not a coverage gap — the two already have different clear
semantics (per-tick vs. process-lifetime) and forcing one shape onto both was judged to add
indirection for no real gain.

---

**Decky conflict-resolution "Group 4" shipped (2026-09-03, branch
`claude/save-conflict-next-group-ebc9b1`, no task file — `implementation-grouping.md`'s Group 4: Phase
8, the Linux Game Mode conflict screen, unblocked immediately since it depends only on Phase 0/1 and
runs fully in parallel with every other open phase).** New `Screen.Conflicts` in `src/Agent.Linux/
Ui/UiApp.cs` — "the direct, literal answer to 'a conflict popup like the Decky one, but in the native
Linux UI, with no Decky installed'" per the plan doc's own framing for this phase. A fourth rail entry
(Overview / Add game / **Conflicts** / Steam setup / Settings, matching `agent-ui`'s `Sidebar.tsx`
order), badge-numbered by open-conflict count, plus a conflict prompt on a tracked game's own row on
the Overview screen so a conflict is reachable without ever attempting a launch first — exactly what
the phase spec calls for. The screen polls `GET /api/agent/conflicts` on a 12s timer, filtered to
`MachineId == config.MachineId` (the same "no bystander case" rule `AgentCli.cs`'s `conflicts` command
and `Doctor.cs` already apply), and renders each open conflict as the same local-vs-cloud two-panel
comparison the dashboard/`agent-ui` cards already use: machine, relative time, file count/size, a
"newer" tag, and Keep Local/Keep Cloud buttons that resolve immediately on press — the sync-pop-up's
`'immediate'` mode, not the confirm-then-resolve two-step the list pages use, since this is a screen
to visit, not a queue to advance through — plus a Keep Both toggle. Resolving talks to `ApiClient`
directly, in-process, rather than round-tripping through the daemon's local `:5178` API: unlike "Sync
now," which genuinely needs the daemon's own live `SyncEngine`/lease state, resolving a conflict is
the exact same stateless, mechanical server call `AgentCli.cs`'s `conflicts`/`resolve-conflict`
commands already make. Two new hand-drawn icons in `Icons.cs` (`Cloud`, a closed straight-line
polygon tracing a scalloped-top/flat-bottom cloud silhouette — the same curve-to-polyline technique
`Folder`/`Shield` already use; `GitBranch`, an exact match of lucide's own two-node-plus-arc path) so
this screen speaks the same icon language as the React surfaces' lucide `Cloud`/`GitBranch`, and a
new `Theme.AccentRed` (`#E5534B`, matching the dashboard/agent-ui conflict card's escalated-border
red) reserved for the overdue line only.
<br>**The Cloud icon's first version was wrong, caught by the maintainer looking at the screenshots
above, not by this session's own review.** A `PathArcTo`-bump version shipped first and read as two
overlapping speech-bubble blobs at every size, not a cloud — confirmed by adding both new icons to
the component gallery's icon strip (`Gallery.cs`, already the project's own "every icon reviewed
together" surface) and screenshotting it at 14-40px. Rewritten as the straight-line polygon described
above and reverified the same way, plus back in the actual conflict card and empty-state screens.
`GitBranch` was checked at the same time and was already correct, so it's unchanged.
<br>**A real bug found and fixed while verifying, not left in:** the `--screenshot` capture-and-exit
path's "is anything still busy" gate (`UiApp.OnRender`'s `busy` check) never listed the new resolve
task, so a scripted `--nav` press on a Keep button had its in-flight `ResolveConflictAsync` call
killed by `Environment.Exit` before the request ever reached the server — reproduced directly: the
conflict was still open afterward per an independent `savelocker conflicts` check. This is the exact
class of bug `_syncNowTask` was already in that gate to prevent; the fix adds `_resolveTask` alongside
it. Deliberately did NOT add the passive `_conflictsTask`/version-fetch tasks to the same gate — those
are an ambient background poll, not a user-triggered write, and gating settle on them would make
every screenshot anywhere in the app wait on a network round trip for no reason.
<br>**Verified live against a real server, not just built — genuinely beyond this group's own
"code-only, verify later" ceiling.** `implementation-grouping.md` named "a Windows+WSLg box" as the
one thing this group needed for live verification that a prior cloud-container session couldn't
supply; this session ran directly on the maintainer's own Windows dev box, which turned out to
already be exactly that box (WSL's `Ubuntu` distro, confirmed present in earlier sessions' gotchas,
carries a working native `dotnet` and WSLg). Built the UI binary with `dotnet build ... -r linux-x64
--no-self-contained` (RID-specific, matching `tests/linux/run-ui-wslg.sh`'s own documented reasoning
— a RID-agnostic build never maps `libSDL2` into `deps.json` and fails with a misleading "not
applicable" platform error) and seeded a genuine two-machine conflict via `tests/
seed-test-conflict.sh again` (a full reset, not the lighter `up`, after `up` alone was found to reuse
a prior session's stale scratch state and mis-seed — not a bug in this session's own code, confirmed
by tracing `SyncService.IngestAsync`'s `MachineId`/`VersionAId`/`VersionBId` assignment directly
before concluding that). Screenshots confirmed: the populated conflict card (correct machine name,
relative time, size/file count, the "newer" tag on the actually-newer side, both captions), the
Overview row's conflict prompt with its chevron and the rail's badge count, and the empty state (a
green `Cloud` icon, correct copy). A scripted `--nav` sequence (D-pad into the content pane, then to
the "Keep This device" button, then A) drove a real resolve through the real `ApiClient` call —
confirmed not by the UI alone but by a separate `savelocker conflicts` CLI invocation afterward
showing the conflict genuinely gone. Full solution (`dotnet build SaveLocker.sln`, all five projects
including the Windows-only `Agent`) and the Linux-x64 UI binary both build clean.
<br>**Gotcha hit again, distinct from the usual "no `npm install`" one:** this worktree's `agent-ui`
DID have `node_modules`, but it was missing a working `tsc` binary shim (`'tsc' is not recognized`),
breaking the Windows `Agent` build (which shells out to `agent-ui`'s `npm run build`) even though
`node_modules` existed — `npm install` again repaired it. Worth knowing this is a distinct failure
mode from the already-documented "no `node_modules` at all" gotcha, not the same one recurring.
<br>**Not yet checked:** real gamepad input via Steam Input and gamescope compositing specifically —
a WSLg pass never validates either, on any screen in this codebase, per `run-ui-wslg.sh`'s own
documented scope. Also worth knowing for anyone adding a scripted nav regression test to this screen
later: the async version/stats fetch that gates a Keep button's enabled state needs a little more
settle time than `--nav`'s default 6-frame gap between steps before the button is genuinely there to
press — a real person's own reaction time already clears this by a wide margin, so it is a testing-
tool timing note, not a user-facing bug.
<br>Phases 7 (Windows tray wiring), 10/11 (Decky), 13 (Playnite), and 14 (webhook + block-launch
opt-in) remain open — see [[Backlog]].

---

**Decky conflict-resolution "Group 5" shipped — Phase 7 only (2026-09-03, branch
`claude/save-conflict-next-group-ebc9b1`, no task file — `implementation-grouping.md`'s Group 5: the
Windows tray automatic chooser + bulk "apply to all remaining" queue).** Originally deferred pending a
Windows-connected session; this one runs directly on the maintainer's Windows dev box, so it shipped.
Group 5 also bundles Phase 14 (webhook notify + a per-game block-launch setting), but that was asked
about directly and scoped **out** of this session on purpose: its block-launch half has a real,
unresolved design fork against already-shipped, hardware-verified Phase 4 behavior — Phase 4 blocks a
Linux launch unconditionally on any confirmed conflict, while the original open question
(`07-open-questions.md` §2) assumed the opposite default (block is opt-in). Building it without that
decision risked silently walking back verified behavior; deferred to a maintainer call rather than
guessed at.
<br>**Shipped**: `TrayApp.cs`'s three native trigger points that can leave this machine with an open
conflict — `SyncAll()` (the "Sync All" menu item), per-game Force Pull, per-game Force Push — now call
a new `CheckConflictsAndRaiseAsync()` afterward. A best-effort `ApiClient.GetOpenConflictsForMachineAsync`
check that, on a hit, raises `AgentWindow` straight to a new `#conflicts:queue` deep link — reusing the
exact `_ui.Post(() => OpenWindow(...))` pattern already proven twice elsewhere in the same file (the
first-run prompt, the lease-warning path) rather than inventing a new mechanism. `agent-ui`'s `App.tsx`
recognizes that one specific hash suffix (ordinary `#conflicts` is untouched) and auto-opens the same
`SyncConflictModal` queue pop-up the browser-side "Sync now" button already shows — one queue UI
regardless of which of the four trigger points found the conflict, matching the plan's own framing of
"the phase that gives a plain Windows tray user a real popup-at-the-moment-it-matters experience for
the first time."
<br>**The bulk-queue half (plan.md Phase 7 item 2) is genuinely new code, not already covered by the
existing pop-up.** `SyncConflictModal.tsx` now asks, once, right after the first resolve in a
multi-conflict queue, whether to apply that same choice — keep this device's save, or keep the
cloud's — to every remaining conflict in one step: "Apply to all remaining" resolves the rest via the
same per-conflict `resolveConflict` call Phase 0/1 already covers server-side (no new endpoint);
"Review each" falls back to the pre-existing one-at-a-time immediate-resolve flow with no further
prompting. The choice is transferable across every conflict in the queue because they all share one
shape — `versionA` is always "the cloud", `versionB` is always this machine's own diverged push — a
fact already established by how `/api/conflicts` filters to this machine's own conflicts before the UI
ever sees them.
<br>**Verified live, not just built**: seeded two genuine divergent conflicts (two separate games)
against a throwaway scratch server (`dotnet run` on :5199, no Docker) using two CLI-driven machine
identities — one of them then reused as the real built Windows tray's own registered identity — and
confirmed, via the tray's actually-served `agent-ui` bundle at exactly the URL
`CheckConflictsAndRaiseAsync` opens (`http://localhost:<port>/#conflicts:queue`), that the queue
pop-up auto-opens showing "1 of 2" with both conflicts correctly framed as local-vs-cloud; that
resolving the first conflict surfaces "Apply to the rest too?" with correct singular/plural wording for
one remaining conflict; and that "Apply to all remaining" resolves the second conflict too and returns
to the empty Conflicts state with the sidebar badge cleared. Confirmed the **correct** side won — not
merely that some resolution happened — by pulling on the other machine afterward and checking its
restored file content matched what the tray's own machine had pushed, for both games. Full solution
(`dotnet build SaveLocker.sln`, all five projects including the Windows-only `Agent`) and `agent-ui`
(`tsc -b && vite build`) both build clean.
<br>**Not literally exercised: a real click on the NotifyIcon's own context menu.** No tool available
in this environment drives native Win32 system-tray UI — the Browser pane only drives Chromium tabs,
and `AgentWindow`'s WebView2 content is exactly what got exercised above, just not by way of an actual
mouse click on the tray icon's menu. The two pieces that click assembles were each verified directly
instead: the conflict-fetch call via the equivalent live query proven above, and `OpenWindow`'s
downstream effect via the direct-navigation check above. Both call sites are otherwise low-risk reuse
of code patterns already proven correct in this exact file. Worth a five-minute manual click-through
on a real desktop (Sync All / Force Pull / Force Push from the actual tray icon) before treating this
as fully confirmed.
<br>Phases 10/11 (Decky) shipped and hardware-verified 2026-09-08 — see below. Phases 13 (Playnite) and
14 (webhook + block-launch opt-in, pending the design decision above) remain open — see [[Backlog]].

---

**Decky conflict-resolution Phase 10 shipped (2026-09-07) — the separate `SaveLocker-Decky` repo,
attached this session at `D:\Projects\SaveLocker\SaveLocker-Decky`, worked in its own worktree/branch
`decky-conflict-resolution-ui` (`implementation-grouping.md`'s "Group 6," the phase it names as
unblocked the moment that repo is available).** `main.py` gained seven one-line `_request(...)` proxy
methods — `conflicts`, `conflict`, `resolve_conflict`, `conflict_policy`/`set_conflict_policy`,
`save_version`, `version_stats` — mirroring the local API Phase 0/1 and Phase 6 already shipped
exactly; no new server or local-API route was needed anywhere. New `src/conflicts.tsx`: a 20s poller
held at module scope (survives Steam remounting `/library/app/:appid`, same reasoning as
`syncStatus.tsx`'s chip store), chip-merge logic that never clobbers an actively-`'syncing'` chip, and
the resolve popup itself — Big Picture-styled `Focusable` cards (D-pad left/right, A resolves
immediately, B backs out via `ModalRoot`'s cancel handling), local-vs-cloud framing and copy following
this plan's own mockup verbatim, never pre-selecting a side. Wired into three surfaces: the
library-page `SyncChip` gains a `'conflict'` kind (magenta-red `FaCodeBranch`, distinct from
`'blocked'`'s amber lock) that's clickable straight into the popup; a new QAM "Save conflicts" panel
(structurally identical to the existing `LeaseWarnings`) reaches a conflict without the library page
open; and a per-game "If a save conflict happens" dropdown on the full-screen settings page, framed as
"Prefer this device" rather than a full machine picker, since only the dashboard has a fleet-wide
machine list.
<br>**One small, genuinely necessary gap closed in the main repo alongside this**: "Prefer this
device" needs this device's own machine id to send as `PreferredMachineId`, and the local `/api/state`
route's `AgentStateDto` didn't carry one. Added as an additive, defaulted trailing field
(`MachineId`); `agent-ui/src/api-types.ts` regenerated against a scratch dev daemon on `:5190` and
diffed — only the new field (plus unrelated alphabetical churn on two untouched entries) appears
(at tip; Windows-ordering churn at 7306968 removed by 1bf3f8a Linux regen).
Committed separately from the Decky-side work.
> Superseded by hardware-verification note below — kept for chronology

<br>**Buildable and type-checked, not hardware-verified.** `npm run build` in the Decky worktree
(rollup + `@rollup/plugin-typescript`, which type-checks the whole module graph) is clean; `python -m
py_compile main.py` is clean; the main repo's full solution and `agent-ui` (`tsc -b && vite build`,
`oxlint`) both build clean with the `machineId` addition (same two pre-existing, unrelated
`AddGamesView.tsx`/`SettingsView.tsx` warnings as every prior session). This is a Decky plugin — nothing
here can render or take D-pad input outside a real Steam Big Picture/Deck session, so **this needs a
manual pass on real hardware before it's trusted as working, not just compiling** — see [[Backlog]] for
the exact five-point check (chip click-through, D-pad nav + immediate resolve + B-cancel inside the
popup, the "newer" tag and keep-both toggle, the policy dropdown round-tripping "prefer this device").
`ModalRoot`'s B-button/cancel behavior specifically is inferred from this same codebase's existing
`ConfirmModal` usage, not directly tested — the piece of this popup most likely to need a real-hardware
fix.
<br>Phase 11 (Decky launch-gate wiring) has since shipped and is hardware-verified (see note below); Phase 13 (Playnite) has no
dependency on it and can be picked up independently — see [[Backlog]].

---

**Phase 10/11 hardware-verified, two real bugs found and fixed (2026-09-07/08).** Real-Deck testing
of Phase 11 showed no pause and no "Play Anyway" popup — a regression from what Phase 10's own
hardware pass had confirmed. Root-caused via a live CDP console trace (Decky frontends run inside
Steam's `SharedJSContext` CEF process, which has no on-disk log; tailed over an SSH-tunneled
`ws://localhost:8080` using a throwaway Node script): `RegisterForGameActionStart`'s `appId` is Steam's
packed 64-bit `CGameID` for any non-Steam shortcut, not a plain AppID — the real AppID sits in the
upper 32 bits. `Number(appIdStr)` silently rounded it to a garbage float, so the fast-path appId match
failed for **every** game this feature targets (Heroic titles, emulators, this repo's own fake-game
rig), not just an edge case. Fixed with a BigInt-aware `parseGameActionAppId()` in `gamingSync.tsx`.
<br>A second bug surfaced once the first was fixed: every attempt then logged "cancel lost the race"
even when the cancel visibly worked. `SteamClient.Apps.GetActiveGameActions()` proved unreliable in
**both** directions on real hardware — an empty result doesn't prove a cancel succeeded (the action can
simply progress past cancellable state before being checked), and a non-empty result doesn't prove it
failed (Steam can be slow to prune a cancelled action from the list). Removed as a verification step
entirely from both call sites in `handleGameActionStart`; the pre-existing `pendingBlock`/`bRunning`
check in `handleLifetimeChange` is the only authority now. User confirmed on real Steam Deck hardware:
"it works perfectly now."

---

**Easy verification wins triaged, fixed where code was needed, and maintainer-verified live
(2026-09-08, branch `easy-wins`, three commits, unmerged).** Five backlog items closed
in one pass, two of them verification-only (no code change): self-hosted console fonts (`5ffd7d1`,
Fontsource Inter/JetBrains Mono, zero Google requests), the WA-03 ACL trap (installer never creates
`%PROGRAMDATA%\SaveLocker`; `StateDirSecurity.Protect` verified live via `icacls`), the LAN
enrollment URL (inferred-loopback refusal + `effective-url` preview verified against `:5196`),
the cross-source doctor note (`bc82708`, `[tracked]` winner + `add-game` hint, suite's dual-source
assertions green), and the Game Mode stale list (`d6d4814`, `RefreshGameList` polled every 10s
including first frame — add appears and delete disappears under WSLg with no restart, renames
deliberately out of scope as membership-only). Debugging the last one surfaced two real findings:
a malformed `GameId` crashes `Load` at startup yet is silently skipped by the poll's `ReadOnDisk`
(robustness gap, worth its own item), and `agent-ui/index.html` still loads Inter from Google Fonts
(the fonts fix covered only the console). Folders archived to `logs/2026-09-08_*/`, index in
`logs/shipped-2026-09.md`.

---

**Playnite plugin work (Groups 1-5, merged PRs #37-#41 on `main`) was never logged in this file —
noticed and flagged, not backfilled, while adding Group 6 below (2026-09-16).** The full design and
per-phase status lives entirely in `docs/tasks/_done/playnite-plugin/plan.md` and
`implementation-grouping.md`, which this file's own session-start instructions never point a fresh
session at unless it happens to be told to work on Playnite specifically. Worth a real backfill pass
in a session that owns this file's upkeep — not done here since it wasn't what was asked.

**Playnite plugin Group 6 — Phases 18/19 shipped, Phase 16 prep staged, Phase 17 re-confirmed
(2026-09-16, branch `claude/playnite-group6-phase17-82e72b`, plus a sibling commit on
`SaveLocker-Playnite`'s branch `playnite-plugin-group-6`).** Asked directly, alongside a set of
uncommitted plan-doc edits already sitting on `main`'s working tree (moved to this branch and
committed first, `c10ecb8`) that had added Phases 18-19 to the plan without yet building them.
**Phase 18**: `src/Agent/PlayniteLibrary.cs` reads Playnite's own `games.db` (LiteDB) as a fourth
`GameScanner` broad-sweep source — a Playnite-only game now surfaces in Add Games with zero
dependency on the Playnite plugin ever being installed, the agent-side structural twin of
`HeroicLibrary.cs` on Linux. **Verified against this machine's own real, installed Playnite, not a
synthetic fixture alone — which caught two real bugs the plan's own risk section anticipated but got
wrong in a first draft**: the real `games.db` is LiteDB **4**'s on-disk format (its header literally
reads `** This is a LiteDB file **`), not 5's — LiteDB 5.0.21 refused to open it outright — and the
collection is named `Game` (singular, capital), not `games`. Both fixed against the real file before
committing; new `tests/SaveLocker.Agent.Tests` (11/11) covers the LiteDB parsing with a fixture. A
real Epic-owned game on this same machine turned out to use a different, non-official community
PluginId and correctly falls to `GameStore.Unknown` — an accepted gap mirroring `HeroicLibrary`'s own
fallback, confirmed rather than assumed.
<br>**Phase 19**: `agent-ui/src/components/PlaynitePluginCard.tsx` on the Overview page, plus new
`GET /api/playnite-plugin/status` and `POST /api/playnite-plugin/install`
(`PlaynitePlugin.InstallFirstTimeAsync`, reusing the existing self-update machinery). Unlike the Decky
card, this one can genuinely auto-install on request — Playnite's Extensions folder carries none of
Decky's root-owned-directory constraint — gated behind one explicit button click, never a background
poll. **Verified live against a real dev daemon on this machine**, which genuinely has Playnite
installed without the plugin: the card rendered the real not-installed state end to end, and clicking
Install automatically correctly round-tripped through the new route and surfaced "This machine is not
registered, so there is no server to ask." inline. `openapi-typescript` regenerated against a scratch
daemon; diffed to confirm only the two new routes/types appear. Full solution and `agent-ui`
build/lint clean throughout.
<br>**Re-verified properly through `tests/testenv.ps1` the next day (2026-09-17), per a direct
standing instruction to always use it for manual tests — and it found a real, THIRD bug the scratch-
daemon testing above had no way to catch.** This machine already has a real, feature-rich portable
Playnite test instance at `D:\Projects\SaveLocker\Playnite-Test` (true portable mode — its own
`library`/`Extensions` folders, not `%AppData%`). Pointing `PlayniteLibrary`/`PlaynitePlugin` at it via
`testenv.ps1 -PlaynitePath` found nothing at all: both only ever checked `%AppData%\Playnite`, which a
portable install never uses. Fixed with a `SAVELOCKER_PLAYNITE_PATH` env var override (same name
`testenv.ps1 -PlaynitePath` already used internally) that both types now honor, wired through
`Use-TestEnvVars`/`Clear-TestEnvVars` the same way `SAVELOCKER_STATE_ROOT`/`SAVELOCKER_TRAY_PORT`
already are. Also corrected `PlaynitePlugin.PlayniteDataRoot`'s own doc comment, which had claimed
portable mode still used `%AppData%` — confirmed here that it doesn't.
<br>With that fixed: `testenv.ps1 build` + `up` built the plugin from the `SaveLocker-Playnite`
group-6 worktree and installed it into `Playnite-Test`; the test Windows agent's rescan found all 6
real Playnite-only games with no read errors; `GET /api/playnite-plugin/status` correctly read back
the installed `v0.1.0`; and restarting Playnite loaded the plugin cleanly
(`ExtensionFactory:Loaded plugin: SaveLocker, version 0.1.0` in Playnite's own log) — the first
confirmed real-Playnite load of this plugin from an actual tagged release build, not a local dev
build. Full manual click-through (conflict gate, menu items, Link popup) still not done — that
remains Phase 16's own real gate, tracked in `SaveLocker-Playnite`'s own `docs/CONTEXT.md`.
<br>**Phase 16 (add-on database submission) — prep staged on `SaveLocker-Playnite`, PR deliberately
NOT opened.** Fetched `JosefNemec/PlayniteAddonDatabase`'s own README directly and cross-checked
against a real merged entry rather than working from memory; two manifest files staged under that
repo's `docs/addon-submission/`. The one real blocker: no version tag has ever been pushed to
`SaveLocker-Playnite`, so the installer manifest's `PackageUrl` has nothing real to point at yet —
confirmed **Phase 17 needs no further work**, re-reading its actual `release.yml` shows it already
produces the `.pext` asset a stale paragraph in `plan.md` had claimed was still missing (fixed in the
same pass). `RequiredApiVersion: 6.17.0` in the staged manifest is not a guess — downloaded Playnite's
own current release (10.60) and read the real `Playnite.SDK.dll`'s `FileVersion`.
<br>Full write-up: `SaveLocker-Playnite`'s own `docs/logs/2026-09-16_group-6-phase-16-prep.md`.
**Not done, on purpose**: opening the add-on-database PR (needs the real tag first) and pushing that
tag itself (a visible, public GitHub Release — needs the user's own go-ahead, same standing blocker
Phase 17's own build session already recorded).

---

**Checkpoint UI redesign, Groups 1–2 shipped (2026-09-17 / 09-18) — PR #44, then reviewed and corrected
2026-09-20 (branch `console-review-fixes-and-security-hardening`).** Design system + console shell:
tokens, `ui/` primitives, sidebar rows and a games grid, the notifications menu, sign-in, console Sync
all, and the exclude-pattern chip editor with a server-side dry run (`POST /commands/bulk`,
`POST /games/{id}/excludes/preview`). Full handoff text of both sessions, including the gaps they found
and what was verified live: `logs/2026-09-18_checkpoint-ui-groups-1-2.md`. Design: `tasks/_done/checkpoint-ui/`.
<br>**The review (2026-09-20) found the shipped state materially wrong in ways the original verification
missed, and every finding is fixed.** The load-bearing ones: **(1) light was the default theme**, so
every visitor whose OS prefers light saw near-black text on the unmigrated views' hardcoded dark cards
(measured 1.04–1.15:1 — game titles, machine names, headings, most of the Audit Log; 0 unreadable
elements now, in both schemes) — dark is the base and light is `data-theme="light"` opt-in until the
migration ends; **(2) sign-in** said "Wrong password" to a first-time visitor, "Lock" was undone by the
next poll, and the password itself sat in `localStorage` — replaced by revocable sessions; **(3) Sync
all** queued commands for offline machines (commands never expire, so they fire unannounced days later)
and hung its progress rail forever; **(4) unlayered global CSS beat Tailwind utilities** (a disabled
button lit up on hover, focus rings computed to `none`) — one cause, three findings ([[Gotchas]] → *Web
console*); **(5) a `..` exclude pattern threw** inside the matcher — a 500 from the preview, and once
saved, an exception in every agent's hash of that game.
<br>**Security hardening done in the same pass** (all in [[Decisions]], asserted by the new
`tests/run-console-security-tests.ps1`, **105/105**, confirmed to fail with three of the defences
disabled): revocable console sessions; a sign-in throttle on every path that checks the admin password
(re-registration had been an unmetered oracle); PBKDF2 → `v2:` at 600,000 iterations with `v1:` upgraded
on sign-in (`verify-password-compat.ps1` passes); artwork URLs treated as untrusted (a `.html` path
used to put an attacker-shaped page on the console's origin); a strict CSP; and the container now runs
unprivileged — with an entrypoint that hands an existing root-created `/data` over once, because a bare
`USER app` would have failed every upgrade (verified on fresh, root-created, `SAVELOCKER_UID=99` and
forced-`--user` volumes). **`X-Forwarded-*` is still never read by default**; `Security:TrustedProxies`
is an opt-in for deployments behind a proxy — which the maintainer's own is, despite the LAN-only
decision ([[Decisions]] amended). **Left for a maintainer decision, not changed:** first-time machine
registration is still open by default (`Security:RequireAdminPasswordToRegister` exists, off) even
though a machine key can read/write every game. *(Since PR #64 it defaults to on — see the quick-wins entry.)*
<br>**Not verified here:** the Windows-agent suites, `run-server-bugbounty-tests` (216) and the CI-only
Linux/cross-OS chain — this session had no agent build; the server, the new suite, the compat guard,
`web` build/lint and a real container were all run. Release notes for the next release must cover:
sessions (a one-time re-sign-in is NOT needed — the old password is migrated on first load), the
throttle (5 wrong passwords → 15 min), the non-root container (and `SAVELOCKER_UID`), and the CSP.

**Checkpoint UI redesign, Group 3 shipped (2026-09-20, branch `claude/group-3-ui-redesign-c16953`,
`2705ce4`, local — no PR opened).** The agent half of the design system, `agent-ui` only — no C#, no wire
change. `tokens.css` (a hand-kept copy of `web`'s tokens; dark base, light opt-in) + `ui.css` + Archivo +
six primitives in `components/ui/`; **`StatusHeader` is now the strip on every agent page: a primary Sync
all and live push progress**, fed by one shared `useActivity` store so a progress tick re-renders only the
progress area (measured: 16 DOM mutations there, 0 in the page and at the button); the Overview is three
stats, one banner, Next up and Recent (which expands to the full log); the launch-setup and plugin cards
moved to Settings. **Phase 3 item 5 (per-game "Sync this game") moved to Group 4** — it needs a game page and
a per-game agent route, neither of which exists. One inherited bug fixed on the way: `handleSynced` used a
stale `view`, so the "don't pop the overlay over Conflicts" guard never worked mid-sync. Verified against a
real seeded conflict on the WSL test agent (real Sync all → busy → toast → pop-up), keyboard focus and both
themes; write-up, deliberate departures from the prototype and what was *not* verified (the WebView2 tray
window, a real Deck) are in `tasks/_done/checkpoint-ui/implementation-grouping.md` → Group 3.
<br>**One finding for the maintainer, not fixed:** the plan's `--color-faint` is 3.31:1 (dark) / 3.55:1
(light) — below WCAG AA for the small text it is used on, in the console too. Agent content text uses
`--color-dim` instead; changing `--faint` itself means `web`, `agent-ui` and `Ui/Theme.cs` together.
<br>**Two rig traps found, in [[Gotchas]]:** `testenv.ps1 sync` silently skipped a NEW directory unless its
files were staged (**fixed the same day**, branch `testenv-sync-untracked-files` — it also lost deletions
and renames), and `conflict -Wsl` alone seeds no conflict. Also: a Windows test
agent left over from an earlier session was mapped to eight of the maintainer's REAL save folders — `clean`
removed it, and Sync all was only ever pressed on the WSL agent.

**Artwork: backfill on key save, opaque icons in the list, thumbnails, and a cover/icon picker
(2026-09-20, branch `steamgriddb-art-picker`, no task file — asked directly).** Four things, one session:
saving a SteamGridDB key now fills in the games that have no art, in the background, and never touches a
cover chosen by hand; the games list shows the square **icon** (the default is the first fully *opaque*
PNG) instead of cropped box art; the aliasing in the list/grid was real — a browser shrinking 600×900 to
38 px — and is fixed by serving right-sized Lanczos copies (`?w=`, on demand, cached, no migration); and
a pen over the game card's cover opens an inline picker of five SteamGridDB covers and five icons with
pagers ([[Decisions]] → *Artwork*, [[API Reference]], [[Gotchas]] → *Web console*).
<br>**Verified:** `run-console-security-tests` **137/137** (was 105) and, through `testenv` against the real
Docker console, the whole loop in a browser (both themes, keyboard, thumbnails at the right sizes) — see
`progress.md`. To repeat it: [[Build and Run]] → *Testing artwork* (`testenv up -ConsoleEnv …` +
`tests/sgdb-stub.py`). **Not verified: against the real SteamGridDB** — no key was available; the
`dimensions`/`mimes`/`nsfw`/`page` parameters follow the official client but have only met the stub.
<br>**Release notes for the next release must cover:** adding a key now fetches art for existing games;
the list shows icons; existing art is re-served at proper sizes (no action needed); the pen/picker; and
that *Refresh art* replaces a hand-picked cover. `Release Notes Pending.md` is stale — draft in
`web/src/releases/` at tag time.

**Branch note (2026-09-20, end of session):** the Group 3 work, the artwork work and the testenv sync fix are
all on `ui-redesign-group-3` (merge `0f0868e`, local, no PR). **Next action:** Group 4 (agent Games tab) in a
fresh session on a branch stacked on this one — sizing and the reasons are in `progress.md` and
`session_summary.md`.

**PR #45 reviewed, every finding fixed and pushed onto it (2026-09-21, branch `ui-redesign-group-3`).**
A review of the PR (Group 3 + artwork + the `testenv sync` fix) found no blockers and about a dozen smaller
issues; all are fixed, plus two suggestions the maintainer asked for. **Server art:** an image declaring more
than 40M pixels is refused in the one fetch chokepoint (`TryFetchAsync`) — before, a 144-megapixel PNG of
~20 KB was inlined as a picker preview; picker pages are cut under the listing's lock; `page` is bounded;
art files are replaced by temp-file-and-move; the thumbnail handler no longer leaks a temp file on an aborted
request or serves a stale thumbnail. **A latent bug found while fixing, not by the review:** ImageSharp 3.x's
`UnknownImageFormatException`/`InvalidImageContentException` are not `ImageProcessingException`s, so the PR's
catch filters never caught them (a valid-magic PNG with junk would 500 a picker page); fixed in `ArtImages`,
and junk is now refused at store and preview ([[Gotchas]] → *Web console*). **Startup backfill:**
`ArtBackfillService` also runs once after startup (`Art:BackfillOnStartup`, `Art:BackfillStartupDelaySeconds`),
so a key from `SteamGridDb__ApiKey` backfills and a restart mid-run loses nothing. **CI:** a new
`console-security-tests` job runs the suite on `windows-latest`; its SQLite checks now prefer a native Python
over WSL. **agent-ui:** a finished Sync all no longer flashes "Pushing…" (a poll in flight when the sync ends
answered with the old state — measured 480 ms → 0 ms), a failure toast stays until dismissed, the "newer"
pill no longer names the removed Inter font, the Overview conflict banner no longer repeats the header.
<br>**Verified:** `run-console-security-tests` **149/149**, 0 skipped (137 before). Against the build without the
fixes the new oversized/page/startup checks failed (5 failures, confirmed); a build with only the oversize
guard failed 9 — which is how the filter bug surfaced. `agent-ui` `tsc -b && vite build` clean, `oxlint` shows
the same two pre-existing warnings; the timing fix was A/B-tested in the Browser pane against a mock agent,
with the old module confirmed live for the control ([[Gotchas]] → *Agent UI*). **Not verified:** the new CI job
on a real GitHub runner (it runs on the PR), the real SteamGridDB, the WebView2 tray window, a Deck.
<br>**Left alone on purpose:** `CONTEXT.md` itself is now ~1,350 lines and a single Read truncates at ~650, so the
session-start read takes several calls. Moving older per-session entries to `logs/` (as its own header says)
would fix that, but it is a large editorial change to the file every session depends on and belongs in its own
change, not in this PR.

**Checkpoint UI redesign, Group 4 shipped (2026-09-21, branch `ui-redesign-group-4`, PR #46).** The agent's Games tab: list and grid, search, a per-game page (Sync this game / Push now / Pull latest /
Check now, what the server holds, what this device watches), cover art, and a search box in Add games. Three new
local-API routes: `POST /api/games/{id}/sync` (`mode` sync|push|pull; one at a time **per game** — **409** on the same game, and never behind the global gate the launch
routes share — never forced), `GET /api/games/{id}/state` (server head/lease/conflict) and `GET /api/games/{id}/art` (the
art proxy). Art reaches the page as a **blob** fetched with the local token — an `<img src>` cannot carry it.
<br>**Caught by running it, not building it:** `.Produces<byte[]>(…, "image/*")` throws at daemon startup (no
wildcard content types); `dotnet build` and `tsc` were both clean. **Follow-up, same day:** `run-local-api-tests.ps1`
§11 now covers the routes (53/53; the hostile-art-URL guards and the 409 gate checked by mutation), and a per-game
sync on a real two-machine conflict was run live. **Not verified:** the WebView2 window, a Deck, light theme
(Group 5). Detail and honest gaps (no version list or last-push bytes on the page — no data behind them; now in
[[Backlog]]) in
`tasks/_done/checkpoint-ui/implementation-grouping.md` → Group 4.
<br>**Reviewed the same day (PR #46): eight findings, all fixed** — write-up in
`tasks/_done/checkpoint-ui/implementation-grouping.md` → Group 4 → *Review fixes*. The two that mattered: the
art proxy followed a `302` to another origin *carrying the machine key* (it also buffered chunked bodies of
any size and let `%2e%2e` past its `..` check), and the per-game sync route held the global sync gate that
`pre-launch-sync` / `post-exit-sync` answer 409 on — so syncing one game dropped an *unrelated* game's exit
push and lease release. Art now goes through its own no-redirect, no-key, bounded client; syncs are gated
per game.
<br>**Verified:** `run-local-api-tests.ps1` **66/66** (§11 is now 36 checks); `run-health-tests` 22/22 and
`run-delta-upload-tests` 29/29 unchanged (`ApiClient` sits under every sync path); the whole solution builds
and `agent-ui` `tsc -b && vite build` and `oxlint` are clean (same two pre-existing warnings). **Mutation-
checked:** with the PR-head `ApiClient` / `AgentApiServer` / `SyncEngine` swapped back in, 9 of the new
checks fail — redirect followed, SVG served, oversized body buffered, the `%2e%2e` path reaching the stub,
the other origin contacted, the key sent on art, and all three cross-game gate checks. `useArt`'s queue /
abort / expiry logic was driven in Node against the real source (22 checks; 8 fail against the PR-head
version) — a scratch harness, not committed: `agent-ui` has no JS test runner.
<br>**Not verified:** any of it in a real browser or the WebView2 window, and the unreachable-server
starvation the four-at-a-time limiter addresses (reasoned from the code — no rig for it). **Still open,
pre-existing, now in [[Backlog]]:** `post-exit-sync` answering 409 while *Sync all* holds the gate. **A flake,
seen once, not investigated:** `run-local-api-tests` §10's *NO traffic reached the old server A* failed on
the first run after a cold build and passed on every run since — most likely a request from before the
server switch sitting in the listener's queue while the daemon started slowly.
<br>**Superseded by Group 5 below — next action is now Group 6 (Deck) or 7 (notifications).**

**Checkpoint UI redesign, Group 5 shipped (2026-09-21, branch `ui-redesign-group-5-appearance`, local — no PR).**
Appearance and fleet sync, plus the theme default flip. The console's look is **three ids** (`Ui:Theme`/`Accent`/
`Mark`/`PushToAgents`, also settable from env) written by `POST /api/settings/appearance` (validated, admin-only,
audited) and **carried to every agent on the heartbeat** when pushing is on — absent means "keep what you have", not
"the default". Agents store it, can opt out per machine (**Follow the console**, default on; off changes nothing on
screen, pushes are still stored so turning it on shows the *current* look), and read it from `GET /api/appearance`.
The look repaints a console/agent page via `data-theme` + an inline accent + a redrawn favicon, and the brand position
in both top bars and the sign-in screen draws the chosen mark; the **Windows tray and window icons are drawn at runtime**
(`Agent/MarkIcon.cs`, GDI+, transparent punch-outs) — no `.ico` files. **The theme default now follows the OS**
(`data-theme` pins one, no attribute is System), which needed **303 + 166 hardcoded hex colours** in the console's
`GameDetail`/`ConfigView`/`AuditView`/`AgentUpdatesCard`/help views and the agent's Add games/Settings/Conflicts/plugin
cards migrated to tokens first — by role, not by shade (details in `tasks/_done/checkpoint-ui/implementation-grouping.md` →
Group 5). **Phase 4 item 4, the Deck's accent, moved to Group 6:** `Theme.cs`'s `AccentGreen` is both "accent" and
"healthy" at 68 sites; the plumbing (`EffectiveAppearance`, `AppearancePalette`) is done — the Deck's own refresh
(`savelocker ui` loads `config.json` once) is written with its caller in Group 6.
<br>**Verified live through `testenv`** (Windows tray agent + Docker console), by clicking: the card stored `dark/coolant/
cartridge`; the real tray agent adopted it by itself; the override, the stored-but-ignored push and re-following all
behaved; the contrast walk (all text nodes, composited backgrounds) is **0 failing in both OS schemes for all five
accents** on Config/Audit/Help/What's New/Games+detail, and only the documented 10 px uppercase eyebrows on the agent.
`run-console-security-tests` **172/172**, `run-local-api-tests` **85/85**, and the new source-only
`run-appearance-consistency-tests` **20/20** (mutation-checked). **Two real bugs the walk caught, not the builds:**
content text on `--color-faint` (3.1–3.6:1) and a count badge dimmed with `opacity: .65` (2.6:1 on light).
<br>**Not verified:** the WebView2 tray window, the tray icon on a real taskbar (the renderer was proven on a contact
sheet, at 16/24/32 px, on dark and light backdrops), a real OS-theme *flip event* (a hidden Browser pane delivers no
`change` events — the handler was invoked directly; [[Gotchas]] → *Web console*), the Deck, screenshots (none rendered).
<br>**Release notes for the next release must cover:** the Appearance card and *Push appearance to agents*; agents follow
the console by default and can opt out in their own Settings; the theme now follows the OS (a light-preferring browser
that saw the dark console before will see light now, and can pin it); the brand mark replaces the old logo.
<br>**Next action:** Group 6 (Deck — its token split is what also unlocks the Deck's accent) or Group 7 (notifications).
The Backlog → *Appearance follow-ups* carries the five small leftovers.

**Checkpoint UI redesign, Group 6 shipped (2026-09-22, branch `claude/group-6-ui-redesign-6d9b06`, no PR yet).**
Phase 6 items 1-3, the Deck (`savelocker ui`, Game Mode). `Ui/Theme.cs` now carries the literal Checkpoint
dark palette from `web/src/index.css`, and — the actual work — the old `AccentGreen`, which had been the
interaction accent *and* "healthy" at once, is split into a fixed `Safe`/`Watch` (never swapped by an accent
choice) and a dynamic `Accent` (from `AppearancePalette`, applied via a new `Theme.SetAccent`). Every one of
the ~40 call sites was checked against the shipped web/agent-ui component it mirrors rather than guessed —
`Chip.tsx`'s own `ok`/`warn`/`crit` doc comment turned out to already be the exact framework in use, and
`agent-ui/src/ui.css`'s `.sl-nav[aria-current]` comment ("is NOT the accent... that colour is reserved for
'a decision is waiting'") caught the same mistake in the Deck's rail that Group 3 had already corrected on
the web — the current-screen indicator is now a neutral tile, never the accent. A new
`AgentConfig.RefreshAppearance()` (mirrors `RefreshGameList()`; renamed `RefreshFromDisk()` and given a test in the PR #49 review, below) is what Group 5's write-up named as the
missing piece — `savelocker ui` is a separate, long-lived process from the daemon that applies a pushed
console look, so it polls the setting off disk every 5s and repaints on change. **Also shipped:** `ListRow`'s
two-line rows are now a fixed 62px (was computed from whichever font baked), the rail widened to 236px, and
the button legend gained "Y Sync now", "L1 / R1 Switch section" and "☰ Steam menu" (a new
`Widgets.GamepadHintWide` and `Icons.Menu` glyph — the literal ☰ character is outside the embedded font's
atlas, same trap as every other non-ASCII character in this file). The header gained a real, visible
**Sync all** primary button (new `Icons.Sync` glyph) plus a global Y/L1/R1 gamepad binding
(`HandleGlobalGamepadActions`) — Y fires the same `SyncNowAsync()` the header button and the Overview's own
"Sync now" already share; L1/R1 step through the rail's screens in order.
<br>**Verified live, not just by build** — `savelocker ui --screenshot`/`--nav`/`--gallery` turned out to
actually run on this Windows box (SDL/GL resolved without WSLg). A pixel-level scan of a real
`--gallery` screenshot confirmed the Primary button is exactly `E0533C`/`160F0E` (Accent/OnAccent), the
Danger button and warning badges are exactly `D9A63F` (Watch), and the stat tile is exactly `7FA96A`
(Safe) — not eyeballed. **A real focus-timing bug was found and fixed this way, not by inspection**:
`--nav r1,r1,r1` with `--nav-debug` showed the SCREEN switching correctly but the gamepad focus RING
staying behind on the old rail entry — `RequestFocus` was being called from inside `DrawRail`, one
step too early in the frame relative to `Widgets.AgeFocusRequest()`, so the request always aged to 0
before the next frame could serve it. Fixed by deferring it into `HandleGlobalGamepadActions` (which
runs after that frame's `AgeFocusRequest`); re-verified with the same script — the ring now lands
exactly on the target rail entry, 5×R1 wraps back to Overview, 2×L1 steps backward correctly.
`ParseNavScript` gained `y`/`l1`/`r1` tokens to make this reproducible.
<br>**Still not run** — a real Deck or gamescope's actual input path (this used a synthetic `--nav`
script, not a physical controller), and the accent actually repainting from a real pushed console look
(this environment's scratch runs had nothing pushing one). The gap that's left is genuinely smaller
than "needs a WSLg pass" — it's specifically "needs a physical controller and a live console."
<br>**Deliberately not built:** Phase 6 item 4 (the Wayland desktop window) — the plan's own "open decision,
blocking nothing yet," untouched here. Archivo was not swapped in for the Deck's fonts (still Inter +
JetBrains Mono) — no Archivo TTFs to embed in this environment, the same asset gap Group 1 hit for PNG/ICO
rasterization; flagged in `Theme.cs`'s own font-section comment rather than left unexplained.
<br>**Follow-up (2026-09-22, same day) — two more bugs caught by eye, not by inspection.** The header's
`Icons.Sync` glyph had its arrowhead hand-picked at the wrong angle — roughly a third of the way around
the circle from where the arc it caps actually ends — so the "Sync all" button showed a near-full ring
with a triangle stuck to its side instead of a refresh icon. Rewritten as a shared `ArcWithArrow` helper
that computes the tip and its tangent from the same end angle the arc is drawn to, so the two can't drift
apart again. Separately, the mark in the header's top-left corner was still `logo-96.png` — a fixed-colour
raster of the pre-Checkpoint brand, baked long before this redesign and never touched by Group 6, so it
neither matched the new palette nor moved when the accent changed. Replaced with a new `AppMark.cs` that
draws whichever mark `EffectiveAppearance.Mark` names (Pixel lock, Cartridge or Memory card) straight into
the draw list in `Theme.Accent`/`Theme.OnAccent` — the same 32-unit geometry as `web/src/appearance.ts`'s
`MARKS` and `src/Agent/MarkIcon.cs` (the Windows tray icon), and the same accent/no-tile rule
`agent-ui/src/App.tsx`'s own topbar draws `<Mark/>` with. `Art.cs` (which existed solely to load that one
PNG) and its embedded resource are gone. All three marks and several accents were pixel-checked via a
scratch `SAVELOCKER_STATE_ROOT` config with `FollowConsoleAppearance: false` — this also closes the "does
the accent actually repaint from a pushed look" half of the gap above, short of a real console push.
Archivo still was not swapped in — still no TTFs to embed in this environment; the user offered to supply
them, and was told exactly what's needed: `Archivo-Regular.ttf` and `Archivo-SemiBold.ttf` (the same two
weights `Ui/Fonts/Inter-*.ttf` already provides) dropped in `src/Agent.Linux/Ui/Fonts/`, at which point
`Theme.cs`'s `RegularResource`/`SemiBoldResource` constants and the `.csproj`'s embedded-resource block are
a small, mechanical swap.
<br>**Second follow-up (2026-09-22, still the same day) — the Sync icon fix above was still wrong, and a
real bug turned up on another surface entirely.** The user tested the first follow-up through the
`SAVELOCKER_STATE_ROOT` recipe (below) against a live, interactive window, not a screenshot, and the Sync
icon still looked broken — a muddled blob, not two arrows. The screenshot-based check that had "confirmed"
the earlier fix used `--screenshot`'s fixed size, which happened to render the icon larger than its real
~18px button size; at the real size, the two arcs' circles (offset by less than their own diameter, closer
to lucide's actual two-circle geometry) overlapped enough that their strokes crossed through the shared
middle and read as a blob rather than two arrows. Redrawn as a single circle with two 140° arcs on opposite
sides — one circle can't self-overlap — verified this time at the real 1280×800 default size, not an
inflated one. Separately, the user reported agent-ui's Games tab showing game names in the system font
instead of Archivo. `Row`/`.sl-row` and the grid `.sl-tile` both render as a real `<button>` when clickable
(every tracked-game row is), and browsers don't inherit `font-family` into form controls by default —
every *other* interactive element in `agent-ui/src/ui.css` (`.sl-btn`, `.sl-nav`, `.sl-search`, `.sl-seg
button`) already carries an explicit `font: inherit` to counter exactly that, but `.sl-row` and `.sl-tile`
were missed when they were built. The web console never had this bug: Tailwind's Preflight resets
`font-family: inherit` on form elements globally, and agent-ui has no such reset since it isn't
Tailwind-based. Fixed by adding `font: inherit` to both rules; verified live in the agent-ui dev server —
`getComputedStyle` on a game name now reports `Archivo, system-ui, sans-serif` in both List and Grid mode,
and `document.fonts.check('600 13px Archivo')` is `true`.
<br>**Third follow-up (2026-09-23) — stopped hand-guessing curved icons; ported them from lucide's own
path data instead.** The user asked why the Deck UI doesn't just reuse the same icon pack as the agent
and console, pointing out that every hand-drawn icon here "is created from scratch and looks bad" — a
fair read of the pattern: `Icons.Cloud` had failed twice and `Icons.Sync` twice more, each time because
the shape was eyeballed rather than copied from the real geometry. `Icons.cs`'s own doc comment already
explained *why* there's no image atlas (crisp at any size, no rasteriser, no asset in the tarball) — that
part was sound — but nothing was stopping the actual coordinates from coming from lucide's real path data
instead of memory. They do now: a new `SvgPath.cs` is a ~100-line M/L/H/V/A/Z tessellator (arcs
specialised to the rx==ry, no-rotation case, which is every arc lucide's icons use — the general
endpoint-to-centre formula is SVG 1.1 spec appendix F.6.5), and `Icons.Cloud`, `Icons.Sync` and half of
`Icons.GitBranch` now stroke the literal `d` string copied out of
`agent-ui/node_modules/lucide-react/dist/esm/icons/*.js` (v0.511.0) instead of a hand-picked polygon or
arc. `Icons.Sync`'s bespoke `ArcWithArrow` helper (the piece that was wrong both prior times) is gone
entirely — lucide's own refresh-cw draws its arrowhead as a plain two-segment corner, not a filled
triangle, and that shape now comes along for free as part of the same path string. Straight-edged icons
(rects, lines, plain circles — Monitor, Plus, Menu, Cpu, Server, the chevrons, etc.) were left as
hand-authored primitives; porting a shape that's already three lines would be pure overhead, and none of
those have ever been the buggy ones. Verified against a live `--screenshot` at the real 1280×800 default
size (Sync in the header pill, GitBranch in the Conflicts rail item) and against `--gallery` at
1280×1500 (Cloud, which needs an actual conflict on screen to reach any other way) — all three read as
clean, closed outlines with no dents or crossed strokes. Separately, the user's screenshot also showed the button-legend labels
sitting visibly low against their glyph badges (A/B/Y circles, the L1/R1 pill, the Steam-menu bars).
`Widgets.HintLabel` was calling `ImGui.AlignTextToFramePadding()` before the label, which offsets the
next text's baseline to match a *framed* widget's taller box — but the glyphs above are centred on a
plain `lineH`-tall box (`GamepadHint`'s `centre = pos + (r, lineH/2)`), not a framed one, so the call was
pushing every label down by `FramePadding.y` past its own glyph's centre. Removed; the label now centres
in the same box the glyph does.
<br>**Fourth follow-up, same day — the Deck's font gap closed.** The user supplied
`Archivo-Regular.ttf`/`Archivo-SemiBold.ttf` (dropped at the worktree root, not yet in
`src/Agent.Linux/Ui/Fonts/`). Moved into place alongside a fetched `Archivo-OFL.txt` (the real SIL OFL
1.1 text, from `google/fonts`'s own repo, matching the licence-file-per-font convention
`Inter-LICENSE.txt`/`JetBrainsMono-OFL.txt` already set), then the swap `Theme.cs`'s own comment called
"small and mechanical" actually was: `RegularResource`/`SemiBoldResource` now point at the Archivo
resources, the `.csproj`'s `EmbeddedResource` entries and size comment updated to match (~500 KB, down
from Inter's ~1.05 MB), and the now-fully-unused `Inter-Regular.ttf`/`Inter-SemiBold.ttf`/
`Inter-LICENSE.txt` deleted — nothing else in the repo referenced them. `Gallery.cs`'s own
"Inter + JetBrains Mono baked." status line would have quietly kept lying otherwise; now reads
"Archivo + JetBrains Mono baked." Verified live, not just by build: a `--gallery` screenshot shows that
exact line (confirming `Theme.FontsLoaded` is `true`, not silently falling back to ImGui's bitmap font),
and the Overview screen's body text visibly changed face. The Deck now matches the console and agent-ui
on every surface Checkpoint specifies a typeface for.
<br>**PR #49 reviewed, every finding fixed (2026-09-23, branch `group-6-ui-redesign`).** Thirteen findings; full
write-up in `tasks/_done/checkpoint-ui/implementation-grouping.md` -> Group 6 -> *Review fixes*. The ones that mattered:
(1) **Sync all was silent off the Overview** - the header button and Y start it from every screen but the result
was read only on the Overview, so a failure on Conflicts/Settings showed nothing; it is now collected once per
frame and shown in the header. (2) **`SvgPath` silently mis-drew what it claimed to reject** (curve commands
dropped, a second `M` discarding the first subpath, compact arc flags mis-read) - rewritten as a pure, cached,
unit-tested flattener that throws. (3) `RefreshAppearance()` had shipped without a test, against
`implementation.md`'s own rule; it is now `RefreshFromDisk()` with `AgentConfigRefreshTests`. (4)
`run-appearance-consistency-tests` now guards `Theme.cs`'s tokens and both C# mark ports too. Also: the header's
right-hand cluster is on the header's centre line (**8 px lower** than first shipped), content text moved from
`Faint` to `Dim`, doc drift fixed. **Verified** under WSLg against the test rig with real `--nav` presses and
a real Sync all round trip (success and failure); `dotnet test` 43/43 (11 before), consistency test 28/28.
**Not verified:** a real Deck with a physical controller. **Left for the maintainer:** whether the legend's "Steam
menu" should use the Steam button's logo rather than the three-bar Menu glyph, and whether 13 px `Caption` text
satisfies "16 px minimum body text".<br>**Next action:** a real Deck (or a box with a physical gamepad) to exercise the Y/L1/R1 bindings
through an actual controller, or Group 7 (OS notifications) if that hardware access isn't available
first. The Wayland decision (Phase 6 item 4) is still open and needs deciding before any code is written
for it.

**Checkpoint UI redesign, Group 7 shipped (2026-09-24, branch `group-7-ui-redesign`, PR #50 — reviewed
2026-09-27 and every finding fixed, see the next entry).**
Phase 7, OS notifications — both halves, on the same rules. **The rules are shared, in
`Agent.Core/Notifications.cs`:** `NoticeCatalog` decides what fires and what it says, `NotificationCenter`
decides how often, and each host supplies only a presenter — `src/Agent/ToastPresenter.cs` (a real Windows
toast: title, body, the brand mark, two buttons) and `src/Agent.Linux/DesktopNotifier.cs` (the old
`ConflictNotifier`, generalised from "conflict id" to "notice key", with its hardware-verified
`notify-send --wait --print-id` mechanics untouched). **What fires** is exactly the plan's list — conflict
opened, lease held elsewhere, push rejected, pull refused, update ready, server unreachable past five
minutes — and **never** a successful push or pull. A standing condition is announced **once** and taken down
when it ends (a clean sync of the game, the server resolving the conflict, the server answering again).
`SyncEngine` no longer takes a free-text `notify` sink; coded alerts go through the catalog, and everything
the catalog does not name stays in the log and the console's bell, where it always was.
<br>**Behaviour that changed on Windows, on purpose:** the routine balloons are gone — `sync.busy`,
`settle.timeout`, `savedir.*`, the poller's "Added/Removed/Mapped…" and dashboard-command results, and the
offline drainer's "attempting drain…" **every 30 s for as long as the server was down** (a per-poll
announcement of exactly the kind the plan's "once" rule exists to stop). Conflicts now toast on Windows for
the first time (the poll finds them ≤20 s after they open; before, only a tray-initiated sync raised the
window), and the server's "open too long" escalation now toasts on both platforms in the plan's voice instead
of the old `URGENT: …` string on Windows only. Tray-menu answers (Force Pull, Sync All, update-check results)
are still shown — they are replies to a click, not conditions.
<br>**A button opens a page; it is not a callback — and that was a decision forced by measurement, not taste.**
The plan wanted "Retry now" / "Install now" style buttons. Every way of getting a click back into the tray was
tried on Windows 11 25H2: an in-process `Activated` handler does not survive the Action Center; a **custom URL
scheme** was built end to end (registry handler → short-lived process → named pipe → tray) and the plain link
worked from `Start-Process` and from `Launcher.LaunchUriAsync`, but **the shell's toast host answered every
freshly registered scheme with "Get an app to open this link"** — while launching Discord's and Steam's, and
`ms-settings:`. A hyphen-free name, a signed handler, an exact copy of Discord's key layout, `DefaultIcon`,
capabilities/`RegisteredApplications`, `SHChangeNotify`, restarting `ShellExperienceHost` and setting the
process AUMID all changed nothing. So the button is now a plain `http://localhost:<port>/open?view=route` link the shell
hands to the default browser, whose page asks the tray to raise its own window at that screen (Linux: a
freedesktop action runs `xdg-open` on `/#route`, the browser being its UI) — verified end to end:
clicking **Choose a save** on a real toast opened the agent UI at the conflict chooser. That removed the scheme
registration, the pipe, the `Program.cs` handler and the door a web page could have knocked on. **Dropped as
a result:** "Retry now" (now "Open game", whose page has *Push now*), "Install now" (the toast names the tray
menu's *Update to vX…* item instead), and the old click-the-balloon-to-update. Recorded in `NoticeAction`'s
own doc comment with the measurements, so nobody rebuilds the scheme.
<br>**`agent-ui` learned to be deep-linked** (`agent-ui/src/route.ts`): the hash used to be read once at load,
so the tray window — kept and re-shown, never rebuilt — ignored every deep link after the first, including the
tray's own `conflicts:queue`. There is now a `hashchange` listener and a `#game:<id>` route; verified by
navigating an already-open window. `run-appearance-consistency-tests` ties the routes the catalog emits to what
`route.ts` understands, and the installer's AUMID to `ToastPresenter.ProductAumid` (both mutation-checked).
<br>**What names and decorates a Windows toast** (all measured, all in `ToastPresenter`'s doc comment): the
header's name and icon come from a **Start-menu shortcut carrying the toast's AUMID** — a scratch shortcut
proved it (header became the shortcut's name plus the exe's icon) — so `installer/SaveLocker.iss` now stamps
`AppUserModelID: "SaveLocker"` on the shortcut (compiles under ISCC; **not installed** — see below). An HKCU
`AppUserModelId` registration with a DisplayName/IconUri does **nothing** on this build, and a build with no
shortcut (the rig, a dev run) shows the bare AUMID string as its name, so it is chosen to be readable
(`SaveLocker.Test.<port>` on a rig). The brand mark rides in the toast's own logo slot, in the current accent —
but the file **must live in `%TEMP%`**: the toast is drawn in an AppContainer process that can read only where
*ALL APPLICATION PACKAGES* has access, and both the agent's ACL-locked state dir and a plain
`%LOCALAPPDATA%` folder silently gave a toast with no picture.
<br>**Cost worth knowing:** the Windows agent's TFM is now `net10.0-windows10.0.19041.0` (that is the only way to
get the WinRT projection — `TargetPlatformVersion` alone does not), which raises the minimum OS to Windows 10
2004 and adds `Microsoft.Windows.SDK.NET.dll` (23.7 MB, ~5.9 MB compressed) + `Microsoft.Windows.UI.Xaml.dll`
(6.9 MB, unused) to the self-contained exe — roughly **+8 MB on a 67.8 MB build**. The output folder is
**pinned to `bin/<Config>/net10.0-windows/`** so the ~20 scripts, the installer and CI that hardcode that path
keep working (`Gotchas` → Builds). `CsWinRTIncludes` could shrink it; not attempted.
<br>**Verified live, through `tests/testenv.ps1`** (real Windows tray + the Docker console + a WSL agent, a genuine
two-machine conflict): the real toast rendered with title, body, mark and both buttons (screenshotted); the
real click opened the chooser; **the server-unreachable rule** (`SAVELOCKER_UNREACHABLE_NOTICE_SECONDS=20`
instead of 5 min) stayed silent through the short outage, toasted once at the first poll past the threshold,
stayed at one entry across later polls, and **withdrew itself ~16 s after the console came back**. `dotnet
test` 84 (66 new: the rules with a fake clock and presenter — once-per-condition, withdraw on clear, the five
minutes, undelivered-then-retry, the route→URL contract — mutation-checked); `run-linux-tests` 192 with all the
notification checks passing, including the new "announced once across several polls" one; `run-health-tests`
22/22; `run-appearance-consistency-tests` 33/33; the installer compiles. Full solution and both frontends build;
the Windows build warning count is unchanged (the rig's own guard caught a second one I had introduced, `CS8602`,
and it is fixed).
<br>**Not verified:** the installed agent's shortcut-based header (only a scratch shortcut with the same AUMID —
the real installer has never been run with the change); ~~a notification on a real Linux desktop~~ — **since
verified on a real Deck in Desktop Mode (2026-09-27):** the popup appears and *Choose a save* opens the flatpak
default browser at the conflict queue, once the click borrowed the session's environment (Gotchas → Linux agent); the **Deck's Game Mode
Steam toast** (the Decky plugin, its own repo, deliberately out of scope in `plan.md`); the WebView2 tray window
beyond what navigation showed. **Two failures in `run-linux-tests` are this WSL's, not the code's:** it runs
under WSLg, which injects `DISPLAY`, `WAYLAND_DISPLAY` and a D-Bus session bus into every shell — even under
`env -i` — so "no session: graphical session / D-Bus session bus reported no" cannot pass here (the suite's
own premise is "this harness has no graphical session").
<br>**Found, not fixed:** `testenv.ps1 clean` printed `removed …\SaveLocker-test` and the Windows state — an old
API key included — was still there for the next `conflict` (deleting it by hand worked), which produced a 401 and a
one-sided seed; cause not found. And `web/node_modules` is absent in this worktree (irrelevant here; the
console was not touched).
<br>**Next action:** run the installer once on a real machine and look at a real toast's header (the one unverified
claim above); then Phase 6 item 4 (the Wayland window) still needs its decision, and Phase 8's Steam art and
PNG/ICO rasterisation are still open. Release notes for the next tag need a line covering notifications — and
the raised Windows minimum.

**Review fixes for PR #50 (2026-09-27, branch `group-7-review-fixes`, on top of the PR's `group-7-ui-redesign`).** A
thorough review of Group 7 found five real bugs and six smaller things; all are fixed.
- **Escalation toasts reached machines that were not party to the conflict**, then vanished ~20 s later. The
  heartbeat's escalation list is fleet-wide (`HealthService` does not filter it); the next poll's
  `ObserveConflicts` — filtered to this machine — took the key back down, and its button opened an empty chooser.
  `NotificationCenter.RaiseEscalation` now announces only conflicts in the last observed own set, and
  `HealthReporter` passes every beat's list (its old once-per-conflict guard spent an escalation before anything was
  shown, so one that could not be shown was never retried).
- **A repeat deep link did nothing in the tray window.** Nothing wrote the hash when the user moved on, so a second
  link to the same screen navigated to the identical URL — same-document, no `hashchange` (checked in Chromium).
  `agent-ui` now consumes the hash once read (`clearRouteHash`, in an effect because StrictMode runs the initializer twice).
- **`/open` could be driven by any web page**: framed, its own same-origin POST passed the Origin check, and each raise
  injected a synthetic Alt into whatever app had focus. Raising now needs `LocalAuth.OpenLinkKey` (a keyed hash of the
  local token — stable across restarts, never the token in a URL), which the toast link carries; without it `/open`
  just redirects to `/#route`. The page sends `frame-ancestors 'none'` + `X-Frame-Options: DENY`; both routes are
  excluded from the OpenAPI document, so `agent-ui/src/api-types.ts` is back to its pre-`/open` content.
- **"Can't reach the server" never fired behind a proxy**, and a proxied outage raised a sticky "push failed" per game
  (not queued). `ServerReachability` (new, `Agent.Core`) treats 502/503/504 and Cloudflare 520–530 as the server
  missing; the poller and `SyncEngine`'s push catch both use it — a gateway error now queues the push.
- **A user's "Check for updates" was silent** once the update toast had been announced; it now clears and re-raises.
- Smaller: the Deck's staged-update notice is raised on every check while one waits (it was tried once, right after
  staging); `DesktopNotifier` believes a "no notification daemon" probe for 30 s (the Decky pre-launch route re-ran
  gdbus on each launch); `DesktopEnvironment.ApplySessionEnv` removes display/identity keys the session does not set
  when it has a display (search paths kept; with no display in the manager, as under WSL, the daemon's own stand);
  `ToastPresenter`'s look cleanup matched every test rig's marks from the installed agent; doc drift, mojibake, and the
  installer script's UTF-8 BOM.
<br>**Verified:** `dotnet test` **105** (21 new; the escalation filter and the gateway codes mutation-checked);
`run-appearance-consistency-tests` 33/33; `agent-ui` build + lint clean (the two known warnings); both agents build
(Windows at its 1-warning baseline). **Live on the test tray** (`testenv build/up -Only windows`): `/open` 302 without
the key, 200 with `frame-ancestors 'none'`/`DENY` with it; `/open/raise` 403 without it and from a foreign Origin, 204
with it (the window opened at the screen); a repeat `#settings` / `#conflicts:queue` link after moving away navigated
in the same document; `api-types.ts` regenerated from that tray. Linux suite: see *Build and Run → Suite baseline*.
**Not run:** the escalation on a real two-machine fleet (needs a 6-hour-old conflict — the unit tests carry it), the
proxy path against a real Cloudflare outage, the Deck. The test console did not start this session only because its
image was not built (a previous `clean` removes it) — `testenv build -Only console` first.

**Checkpoint UI gap audit (2026-09-27, branch `checkpoint-ui-gap-audit`, docs only — no code changed).** The maintainer
pointed out that much of `tasks/_done/checkpoint-ui/prototype.html` never shipped; a screen-by-screen comparison with the code
confirmed it. Groups 1–7 built the *shell*; most *pages* behind it were migrated to the tokens by Group 5 and otherwise kept
their pre-Checkpoint layout — measured by the inline styles still in each view (`GameDetail.tsx` 116, `ConfigView.tsx` 112,
`AgentUpdatesCard.tsx` 61, agent `SettingsView.tsx` 37…). **The console has no Backups tab at all:** `BackupService` snapshots
nightly and `GET /admin/backups` / `POST /admin/backup` exist, but nothing in `web/src/api.ts` calls them. The Deck's rail,
header, stat tiles and rows fall short of the mockup (no Tracked games or Activity screen, no cover art), and the Wayland
window was never decided. Also found: the Steam library art `install.sh` ships (`packaging/linux/artwork/dist/`) is still
the pre-Checkpoint brand, at non-Steam sizes. Written up as **Phases 9–14** in `implementation.md` (*The 2026-09-27 audit*
also lists what differs **on purpose** and must not be rebuilt) and **Groups 8–10** in `implementation-grouping.md`, with
status rows in `plan.md` and both task files. The Wayland options are written out under Phase 6 item 4 with a recommendation
(a chrome-less browser app window + a `.desktop` launcher; WebKitGTK stays rejected) — **decided 2026-09-28: option 1**
(see `Decisions.md` → *Linux UI*); the launcher stays out of Steam, Game Mode keeps `savelocker ui`. **Regrouped 2026-09-28 at the maintainer's request** from nine groups into three coarse ones worked as
ordered parts: 8 console kit + top bar + Games page, 9 Backups + Configuration + the other console pages, 10 agent UI +
Deck + assets + Wayland.
<br>**Group 8 shipped 2026-09-28 (branch `group-8-ui-redesign`, three commits: 8a `e6514ce`, 8b `1566de8`,
8c `24074a0`) — in review as PR https://github.com/Marwanello/SaveLocker/pull/52, not yet in a release.** The console's page kit (`web/src/components/ui/`: `Page`, `PageHead`,
`DataTable`, `KV`, `PathField`, `Banner`, `EmptyState`, `SearchField`, `FilterChips`, `Meter`, `Dot`, `InlineConfirm`, `Icon`,
a global `Toaster`), the top bar (pill tabs, an always-present bell with per-item actions, the conflict pill, Sync all's
full-width rail + **Cancel**), and the Games page re-laid out to the prototype with **no `alert`/`confirm`/`prompt` left on it**.
One new route, `POST /api/commands/cancel`, and a new terminal `CommandStatus.Cancelled` (appended; agents never see it);
`openapi.json` + `web/src/api-types.ts` regenerated. `run-console-security-tests` 172 → **189** (API-03, mutation-checked),
`run-appearance-consistency-tests` 33 → **35**. Verified live through `testenv` with real conflicts made by editing each
side's save; that pass found and fixed one real bug (a conflict side pushed while the page was open was invisible to it, so
"Keep both" named the older save "the newer"). Decisions taken with the maintainer, the departures, and what was not verified:
`tasks/_done/checkpoint-ui/implementation-grouping.md` → Group 8; the step-by-step checklist is `tasks/_done/checkpoint-ui/group-8-verification.md`.
Found in passing and fixed in the same PR (the "real UTC write time" entry above): a version's "newest change" read hours off east of UTC (zip entries were
stamped in local time, read as UTC — `SaveArchive`).
<br>**Same branch, same day — OS logos and a real machine picker (maintainer's request).** Agents now say what they run:
`AgentOsInfo` rides the heartbeat (appended, optional), filled by `Agent.Core/OsIdentity.cs` from os-release on Linux
(`ID`, `ID_LIKE`, `VARIANT_ID`, `PRETTY_NAME`, plus "Steam Deck"/"Steam Deck OLED" from a Valve board and "WSL") and the
build number on Windows; the server stores it on `AgentHealth` (migration `AddAgentOsInfo`, five nullable columns) and
serves it on `GET /admin/health`. The console's conflict panel became the agent's conflict card with each side led by its
machine's OS logo (pick a side → the sentence says what happens → "Resolve with X", keep-the-other as a checkbox), and the
game page's machine dropdowns (Push/Pull target, Rules' preferred machine, and the policy beside it) are a listbox with
the logo, the OS and online state (`ui/Select.tsx`, `game/MachineSelect.tsx`). Logos: Simple Icons 16.33.0 (CC0) for
the distros, the Windows 11 squares, Bazzite's own press-kit mark; an unknown distro is Tux, an older agent falls back to
its platform. `run-console-security-tests` **195** (UI-02). Verified live: WSL reported "Ubuntu 26.04 LTS · WSL", a real
conflict resolved through the new panel. Not seen: a real Deck/Bazzite report, and a Windows agent at this build (the rig's
tray that day was an elevated one this session could not rebuild — `Gotchas` → Testing).
<br>**PR #52 reviewed the same day, every finding addressed (branch `group-8-review-fixes`, pushed onto
`group-8-ui-redesign`).** 14 findings; write-up in `tasks/_done/checkpoint-ui/implementation-grouping.md` → Group 8 →
*Review fixes*. The two that mattered: **the UTC "newest change" fix had nothing left showing it** — `b3caca9` dropped
it from the conflict tile, and the "verified live … to the tick" line in the UTC entry above was checked against
`main`'s old panel, not this branch; it is back on the tile. And **Discard in Exclude patterns left the abandoned draft's
dry-run count on screen.** Server: Cancel now also withdraws a claim that **lapsed** unanswered (the claim's own
predicate) and runs in one transaction, so two cancels at once audit once. `run-console-security-tests` 195 → **205**
(8 of the 10 new checks fail against the PR-head server), `web` build + lint clean, and the UI fixes checked live
through `testenv` with a real conflict.
PR #52 merged.
<br>**Group 9 started 2026-09-29 (branch `group-9-ui-redesign`) — 9a done: the console's Backups tab.** New admin routes
`GET /admin/backups/status`, `POST /admin/backups/settings` (DB-backed schedule, re-read every scheduler loop) and
`GET /admin/backups/{file}` (the download: admin-only, name matched against the listing, audited, `no-store`); a snapshot's
reason (Nightly / Manual / Before upgrade) rides in its file name; the server takes a **before-upgrade snapshot** before
migrating whenever it starts on a different build. `BackupsView.tsx` + KB `database-backups.md`. `openapi.json` +
`web/src/api-types.ts` regenerated (additive). `run-console-security-tests` +34 (BK-01, mutation-checked) — run here only
as that one phase through a pwsh-7 shim; CI runs the whole suite. Details and what was not verified:
`tasks/_done/checkpoint-ui/implementation-grouping.md` → Group 9.
<br>**Same day — 9b and 9c done too; Group 9 is complete in code.** 9b: Configuration rebuilt on the kit (Server with a
`DriveInfo` storage meter, Enroll, Defaults & maintenance with the auto-update and nightly-backup switches, Machines,
Agent updates edited in place, Admin password) and the **default exclude list editable** (`POST /api/settings/default-excludes`,
stored as a JSON array so `[]` means "none"); no `alert`/`confirm` left anywhere in `web/src` (guard widened). 9c: Audit log
search + machine chips, Help as the docs grid, What's new with the release-history table and Agent versions, the two-column
sign-in with **Remember this browser** (localStorage vs sessionStorage); server extends `AgentHeartbeat.StagedVersion`
(migration `AddAgentStagedVersion`) and `ServerBuildInfo.LatestRelease`. `run-console-security-tests` +17 (CFG-01,
mutation-checked). All verified in a browser against a scratch server, **not yet through `testenv`** — the step-by-step
is `tasks/_done/checkpoint-ui/group-9-verification.md`.
<br>**Same day, maintainer follow-ups:** backups are now zips of the database + every game's latest save on an
editable UTC daily/weekly schedule, restorable from the Backups page (safety backup first, undoable); the SteamGridDB key
is encrypted at rest (key ring `/data/keys`, never in a backup); the lock screen no longer shows the top bar's tabs,
Sync all or bell. Write-up: `implementation-grouping.md` → Group 9 → *Follow-up*; decisions in `Decisions.md`.
<br>**PR #53 opened and reviewed the same day; every finding fixed on the branch.** Restore no longer cancels after the
database swap and runs the start's own setup (`Data/DatabaseSetup.cs`); downloads go through a single-use ticket so a
multi-GB backup streams to disk. `run-console-security-tests` **308/308** on Windows (BK-01 +14, mutation-checked).
Detail: `implementation-grouping.md` → Group 9 → *Review fixes (PR #53)*.
<br>**Release notes for the next release must cover:** backups are now zips of the database + every game's latest save,
restorable from the Backups page; **the schedule changes for upgraders** — default **weekly** (Sunday) instead of nightly,
and the hour is now **UTC**, not the container's local time (an unRAID template's `TZ` no longer shifts it) — so anyone who
relied on nightly backups should set *daily* under Configuration; the SteamGridDB key is encrypted at rest, and a backup
restored onto a different server needs it re-entered; a machine enrolled after a restored backup must re-enroll.
<br>**Group 10 shipped 2026-09-29 (branch `claude/group-10-ui-redesign-2083d5`, four commits: 10a `7f80be6`, 10c `a88fbe1`, 10d `f2d01d2`, 10b `81321d7`; no PR yet).**
10a: agent UI Activity tab, offline queue, Sync-all "N of M" + cooperative Cancel (between games, `POST /api/sync/cancel`), last-run summary,
per-game stats/versions/open-folder, resolved conflicts, "Sent today"; new server agent routes `games/{id}/versions` and `conflicts?resolvedSince=`.
10b: Deck rail of six sections, Tracked games with cover art (StbImageSharp), Game and Activity screens, battery, Steam setup under Settings; `SvgPath` gained cubics.
10c: `npm run export:art` (resvg, Archivo only) regenerates Steam art, PNG/ICO favicons, installer icon. 10d: `savelocker open` (Chromium `--app=`, Flatpak and xdg-open fallbacks, pure `AppWindowPlanner`) + `.desktop` entry from `install.sh`.
Not verified: a real Deck, Windows tray/WebView2, the WA suites. **Gotcha:** `testenv clean` wipes the rig shared by all worktrees — it disrupted another worktree's process on :5188.
<br>**Group 10 follow-ups (2026-09-30, branch `group-10-ui-redesign`).** (1) **Sync with the server down no longer throws:** `PullAsync` let the
`HttpRequestException` out, so "Sync this game" answered 500 and the push that would have queued the save never ran (Sync all aborted the same way).
It now reports *server unreachable*, returns false, and the push queues; the game page says "queued and will upload when it is back".
(2) **Games not set up on this machine** (`TrackedGame.IsEnrolledHere` = has a save folder) are listed apart in the agent UI's Games tab
("On this machine" / "On the server"), have no Sync/Push/Pull/Check now (the route refuses with 400), are skipped by Sync all and the Windows tray menu,
and are not counted in "games tracked". (3) The games list no longer disappears with the server down (`App.tsx` fetched it together with the
server-side conflicts). (4) Add games shows a real progress bar: `GET /api/enroll/progress`, fed by `Enroller` step by step.
The Deck's Tracked games screen groups the same way ("On this Deck" / "On the server"), a game with no folder gets "Choose save folder" instead of Sync/Push/Pull, and the Games Tracked tile counts enrolled games only.
`run-agent-tests` is now repeatable against a used dev server: it starts from an empty `.verify/` and names its games per run (`$stamp` was never set, so "TemplateGuard-" was one shared game).
Tests: `OfflineSyncTests` (3, mutation-checked), `run-local-api-tests` 110/110, `run-agent-tests` 47/47, `run-health-tests` 29/29,
`run-appearance-consistency-tests` 47/47, xunit 146/146. Verified in a browser against a scratch daemon and the dev server, **not** through
`testenv` (Docker was not running).
<br>**Steam art follows the look (2026-09-30).** The agent repaints the four library pictures the installer bundles
(`~/.local/share/SaveLocker/artwork/`: capsule, capsule-wide, hero, logo — only if that folder exists) in the accent and mark in effect, at start and
on every change (`Agent.Linux/Art`: `SteamArt`, `SteamArtRenderer`, `SteamArtHost`). Steam and its shortcuts are **not** touched — and **Steam keeps its
own copy of a picture once it is set**, so the library shows a new look only after the person sets the files as custom artwork again (review, below).
No SVG renderer in the agent: `export-art.mjs` also writes text and mark **layers** (`src/Agent.Linux/Art/layers/`,
~220 KB, embedded) and the agent tints and composites them — for Ember/Pixel lock it reproduces the bundled PNGs (test-held). `savelocker steam-art
[--out dir]`; testenv: `.\tests\testenv.ps1 art` (XDG_DATA_HOME keeps it in the rig's state). Not verified on a real Deck. Deck rig: `testenv.ps1 up` also adds a "SaveLocker Test" Steam shortcut (`savelocker ui`) with the art in its grid folder (`dev-shortcut-add --kind ui`). It also writes a "SaveLocker Test" Desktop Mode menu entry (`~/.local/share/applications/savelocker-test.desktop`, `savelocker open --port 5177` on the test state; the agent only writes its own entry for the real install). `clean` removes the shortcut, the menu entry and "Conflict Game".
<br>**PR #54 reviewed 2026-09-30, twice; every finding fixed and pushed onto the PR's branch.** The one that
mattered: the follow-up's `PullAsync` returned `false` for an unreachable server, and the tray's Force Pull, a dashboard
pull command and the CLI all reported that as a pull that went through — it is a `PullOutcome` now. Also: two test checks
that could not fail, a green "Synced 6 games · All clear" after a run that reached nothing, a "no desktop" test that a
headless box passed, a notification click starting the browser inside the unit's cgroup (now through `systemd-run --user`,
`xdg-open` as the fallback — **unverified on a Deck**), and the Deck's game page showing the previous game's data.
`ErrorResponse.needsConfirm`, `SyncRunDto.pulled/queued/unreachable` and `?machineId=` on the resolved-conflicts route are
additive. Detail, counts and what was not verified: `tasks/_done/checkpoint-ui/implementation-grouping.md` → Group 10 → *Review fixes*.
**Not seen in a browser:** the `testenv` rig was in use (its Windows agent mapped to real save folders) and was left alone.
<br>**Second pass — the two Steam-art commits.** The premise of the second one was wrong: *Set Custom Artwork* hands Steam the
image, not its path, so "set them once and they stay current" cannot happen — the installer's text, the CLI reference and the
log line now say to set them again after a look change. **Open decision:** keep that (Steam untouched, one manual step per
look change) or go back to painting the shortcut's own `grid/` files as `071d1f8` did (automatic, but writes inside Steam's
folder). Also fixed: the artwork folder is this agent's own (`config.StateDir`), so a `--config` second agent cannot repaint
the installed one's pictures; `steam-art --accent/--mark` refuses an unknown id instead of painting Ember under the typed
name; an unchanged look no longer rewrites the marker at every start.
<br>**Third pass (2026-10-01) — the eight commits after that** (background-only hero, render-and-compare instead of the
marker, the rig's "SaveLocker Test" shortcut with art and its Desktop Mode entry, Decky skip-reinstall). Fixed: the two test
shortcuts share one backup, created-file marker and grid folder, and `Remove` still assumed one — removing "Conflict Game"
deleted "SaveLocker Test"'s art, removing "SaveLocker Test" left its art in Steam's `grid/` whenever the entry was already
gone or the file SaveLocker created was deleted, and removing an absent one beside the other was a refusal ("delete the backup
manually"). `DevSteamShortcut` is now unit-tested on a temp `shortcuts.vdf` (its CRC-32 moved out of `Ui.Screenshot` into
`Crc32.cs` so the tests can link it). Also: `steam-art` crashed instead of reporting a folder it cannot write; `--kind ui
--port abc` fell back to the real agent's :5178. Note: every repaint now renders all four pieces (~0.7 s here) to compare.
<br>**Fourth pass (2026-10-04) — the nine commits after that** (appearance follow-ups, `playnite-import`, the Deck's enroll
progress bar). Fixed: `has_live_key` in `testenv-deck.sh`/`testenv.sh` had a literal 0x01 byte for sed's `\1`, so it never
sent the stored key — check for that byte when a sed backreference is written by a tool; the Deck's progress line and the new
401 message used `—`/`…`/`→`, which Game Mode's Latin-1 font draws as boxes.
<br>**Release notes for the next release must cover (Group 10):** the agent UI's Activity tab and offline queue; Sync all's
N of M, Cancel and summary; games on the server with no folder here listed apart; `savelocker open` and the application-menu
entry (added once by an agent that updates itself); `savelocker pull` exits non-zero when the server cannot be reached.
<br>**Playnite plugin closed as done (2026-10-01).** Submitted as JosefNemec/PlayniteAddonDatabase#661; the maintainer
put new plugin submissions on hold until Playnite 11's new add-on database. The listing, the self-update proof and four
unchecked surfaces are now their own Backlog tasks (`tasks/playnite-*`).
<br>**Checkpoint UI closed as done (2026-10-04)**, merging with PR #54; the Group 9 `testenv` checklist is its own
Backlog task now (`tasks/checkpoint-ui-group-9-check/`). **That checklist was run through `testenv` on
2026-10-14 (branch `backlog-plans-testenv-mockup`) and the task is closed**: every step passed, including
the maintainer's 4.6/1.8; eight findings (F1–F8) — keep-count can't return to 7 once changed, revoking an
enrollment removes its row, `-test` builds classify as releases so the Dev-build/Running chips don't show,
and *Fetch from GitHub* defaults to the upstream repo — are recorded in
`tasks/_done/checkpoint-ui-group-9-check/run-2026-10-14.md`, each a maintainer decision, none a blocker.

**Duplicate-shortcut warnings outside doctor shipped (2026-10-14, this branch, PR #75) — reviewed the
same day, every finding fixed.** The one detector (`SteamShortcuts.DuplicateNameGroups`/
`DuplicatesFor`) now backs the `doctor` note and both scanners; `ScanCandidate.DuplicateShortcuts`
rides the agent's wire (`CandidateDto`, additive/nullable; `agent-ui/src/api-types.ts` regenerated);
agent-ui gained `DuplicateChip` (a ReleaseChip-style popover naming each same-named shortcut's own
name/AppID and which one the scan follows) + a row note, and Game Mode a warning tile + ASCII caption.
`Tracked` marks the AppID the scan actually follows, and no entry is marked when it follows none of
them (a Windows save-root candidate that won the merge, or a MoonDeck shortcut reclassified as a
genuinely installed game) — the copy says so rather than naming a loser. `DuplicateChip` and
`ReleaseChip` share `usePopover` (hover/pin/outside/Esc, with a 150 ms leave grace for the 8 px
chip→popover gap); the popover swallows clicks (an Add-games row is a `<label>`, so a click on it
used to tick the game's checkbox) and `.sl-card:has(.sl-release__pop)` stops the card clipping it.
<br>**Verified:** xunit **559/559** (+7 `DuplicateShortcutTests`; the tracked marker mutation-checked),
Agent.Core/Windows Agent/Agent.Linux build clean, `agent-ui` build + lint clean,
`run-appearance-consistency-tests` **50/50**, `run-linux-tests` **282/2** as root in `unshare -n`
(the 2 are the documented WSLg-session pair; both duplicate-shortcut checks pass, now asserting the
exact AppIDs/names/tracked flags rather than field presence), and both UI fixes proven in headless
Chrome against the real CSS/DOM (the card clip and the label-tick each reproduced with their fix
removed, including a real trusted click). **Not verified:** a live Deck/WebView2 pass of the chip.
The 71-failure cascade the PR body first carried was this box's stray `~/SaveLocker` daemon holding
:5187 — the netns run is the workaround; the task is ready to move to `tasks/_done/` on merge.
<br>**v0.6.0 prepared (2026-10-04, branch `release-0.6.0`) — not yet tagged.** PR #54 is merged. Release notes in
`web/src/releases/0.6.0.md` (covering everything since v0.5.12: the whole Checkpoint redesign, Groups 1–10, plus backup
restore and the encrypted SteamGridDB key; three migrations). A minor bump, not 0.5.13, because of the redesign's size and
the migrations. The README was rewritten around the redesign with fresh screenshots in `docs/screenshots/` (console,
agent UI and Deck Game Mode, all captured from the `testenv` rig seeded with a showcase fleet and placeholder covers), and
`docs/brand/` holds the README banner, `icon.svg` and a 1280×640 `social-preview.png` for the GitHub repo's Settings →
Social preview (an upload only the maintainer can make). The old tray and installer screenshots were dropped as
pre-redesign; there is no headless way to capture either.
**Next action:** merge `release-0.6.0`, tag `v0.6.0`, upload `docs/brand/social-preview.png` as the repo's social preview.
<br>**Multiple save paths planned (2026-10-04, docs only).** `logs/2026-10-06_multiple-save-paths/plan.md` covers any game, not
just emulators, in 7 phases and 3 groups (`implementation-grouping.md`):
- The primary path stays at the archive root. Extra paths go under `.savelocker/paths/<key>/`, each with a marker entry.
- A machine that can't map a path keeps a **shadow** copy, so every push carries every path. Without it, two machines
  push the path back and forth forever.
- `Games.SuggestedSaveDir` is kept; dropping it would rebuild the table every FK points at.
- **Group A shipped (2026-10-04, branch `multiple-save-paths-group-a`, not merged):** Phase 1 `SaveArchive` takes several
  `SaveRoot`s (layout, markers, one Ordinal hash order, per-slice restore checks); Phase 2 adds the migration
  `AddMultipleSavePaths`, key-aware path routes and `save-paths` add/remove. Nothing user-visible yet — no agent uses it.
  Suites: [[Build and Run]] → Suite baseline. Upgrade checked against a DB seeded by `main`'s server (orphans removed).
- **Group B shipped (2026-10-05, branch `multiple-save-paths-group-b`, PR to the fork, not merged):** Phase 3 the agent
  hashes/pushes/pulls/watches every folder (`TrackedGame.Roots()`, shadows in `<state>/shadow/<game>/<key>/`,
  `SyncEngine.MapSavePathAsync`); Phase 4 per-key reconcile, `add-path`/`remove-path`, local API `Paths` + keyed
  `/folder`, doctor, ProtonRun; Phase 5 `ScanCandidate.ExtraSaveDirs` adopted by `Enroller`. The plan's open question
  is settled in [[Decisions]] (adopt at once; `remove-path` fleet-wide; a folder with no template is still added).
  New `tests/run-multipath-tests.ps1` (39, two machines) — counts in [[Build and Run]] → Suite baseline.
  **Not yet run on the testenv rig or hardware.**
- **Group C shipped (2026-10-05, branch `multiple-save-paths-group-c`, PR to the fork, not merged):** Phase 6 "Also
  found" — the manifest's other existing locations are suggested (`FolderSuggestions`, `ScanCandidate.AlternateSaveDirs`),
  never adopted on their own; Phase 7 UI in all three surfaces: agent-ui folder list + Add/Remove + a start-up "More save
  folders found" prompt, Add games pre-ticked, console one section per folder with Latest's files (new
  `GET …/versions/{v}/folders`) and agents < 0.7.0 flagged, Deck per-folder browser. The maintainer's three calls are in
  [[Decisions]]. Counts in [[Build and Run]] → Suite baseline. **testenv pass done on Windows + WSL + console** (the PR has
  the steps and screenshots; it caught two bugs, fixed in `7bbca5c`). **The Deck target has not run.**
- **Done (2026-10-07):** merged as PRs #56–#58 and released in v0.7.0; moved to `logs/2026-10-06_multiple-save-paths/`
  and indexed in `logs/shipped-2026-10.md`. The Deck testenv target never ran.
<br>**Settle-gate test flake fixed (2026-10-05, on `multiple-save-paths-group-c` / PR #58).**
`MultiPathAgentTests.The_settle_gate_waits_on_every_real_folder` failed once on windows-latest (PR #58): the gate
never went quiet in 10 s. The Windows lock probe opens with `FileShare.Read`, so a reader that shares reads does
**not** count as a writer (now pinned by a test). One that denies reads, though, such as an AV scanner or indexer
opening exclusively, raises the same sharing violation as a writer and holds the gate. The probe was not narrowed
because a game writing with `FileShare.None` looks identical. The test now pins the probe quiet through an internal
`SaveSettler` overload and still proves the second folder restarts the quiet period. Reproduced off Windows with an
always-locked probe (same `false` at maxWait). Recorded in [[Gotchas]] → *Testing*.

**Emulator saves Phase 1 built (2026-10-04, branch `emulator-saves`) — RetroArch, both OSes; waiting on the
real-hardware pass.** One Add-games candidate per RetroArch `.srm` (EmuDeck's `Emulation/saves/retroarch/saves`,
else a standalone `retroarch.cfg`), named `<cleaned file name> (RetroArch)` and scoped to that ROM's files by a
new **server-side** `Game.IncludeGlobs` (migration `AddGameIncludeGlobs`; `openapi.json` and both `api-types.ts`
regenerated, additions only). `SaveArchive` hashes/archives/lists **and restores** within the scope — a restore
deletes local files absent from the archive, so an unscoped pull into a shared saves folder would delete every
other ROM's save (a test proves it, and the three scoped-restore tests fail with the scope disabled). Decisions
taken with the maintainer and every deviation from the plan: `tasks/_done/emulator-saves/plan.md` → *Status* /
*Phase 1 — as built*; [[Decisions]] → *A game can own part of a save folder*. **Verified:** xunit 202 (+21),
delta 33, hardening 33, health 33; a scratch two-machine round trip (Linux daemon + Windows CLI, each pull
leaving the other ROMs' saves intact); and through `testenv` — `emu-fixture` (new) → the Windows test tray's
Add games listed the fixture's saves, adding Chrono Trigger and Sync all uploaded a one-file archive. **Not
verified:** a real EmuDeck install on the Deck or on Windows (`Build and Run` → *Testing emulator saves*).
<br>**Same day, follow-up: save states decided, and the branch is PAUSED.** RetroArch states will sync, always
(not opt-in), as a second save path of the same game — so they wait for *Multiple save paths*, which the
maintainer chose to build first in its own session/branch off `main`; `emulator-saves` is then rebased and
continues with Phase 1b (states). What that feature must provide for emulators is appended to
`logs/2026-10-06_multiple-save-paths/summary.md`; the states research is in `tasks/_done/emulator-saves/plan.md` → *Save states
— decided*. Also fixed: EmuDeck for Windows' RetroArch lives at
`%USERPROFILE%\emudeck\EmulationStation-DE\Emulators\RetroArch` (Phase 1 had guessed an `%APPDATA%` path).
<br>**Resumed 2026-10-06: `main` (multiple save paths Groups A+B) merged in, Phase 1b (save states) built.** The
merge kept `main`'s version of every shared file — its per-folder include scopes replace this branch's migration,
DTO fields, server validation and `SaveArchive` code (`tasks/_done/emulator-saves/plan.md` → *Merged onto multiple save
paths*). A RetroArch game is now two scoped folders: `main` (`<rom>.srm`/`.rtc`) and `states` (`<rom>.state*`),
the states folder declared even before it exists. `testenv emu-fixture` now writes a second tree for the WSL
daemon, so the Windows ↔ WSL round trip runs on the rig. Unit **247**, multipath **39**, delta **33**.
<br>**2026-10-07: game sources and plain names.** Each machine records how it found a game ("Emulator ›
RetroArch" + SNES/Flatpak/EmuDeck tags, "Steam › Installed game", …) — new `MachineGameSources` table, agent
`PUT /api/agent/source/{id}`, admin `GET /api/games/{id}/sources` — shown on the game page of the console
(chip, "N sources" + per-machine popover), agent UI (chip + tags) and Game Mode (pill). RetroArch games drop the
"(RetroArch)" suffix. Agent UI Add games gained an
*Emulators* chip with an *Emulator* row (one chip per emulator — add each new one). `tasks/_done/emulator-saves/plan.md`
→ *Game sources and names*. Unit **280**.
<br>**2026-10-07: review of PR #60, every finding fixed on the PR.** The first naming rule (append the console
when *this machine's scan* had a same-named game) split one ROM into two server games across a Deck with Steam ROM
Manager shortcuts and a PC without them; and a Steam game with an emulator game's title joined it and inherited
its `.srm` scope, so none of the PC's saves were ever backed up (proven with a two-machine test before fixing).
Now: names come from the save file alone and **the server decides** which one an emulator save takes
(`Enroller.NamesFor`/`ServerNameFor`); a game never joins one whose patterns keep none of its files, and Add games
says why it refused (`EnrollResponse.notes`). Also: the poller sends a source once per value, not every 20 s
against a console without the route; "EmuDeck" tag from the scanner, not the (real) path; server bounds
sources. Decisions → *Emulator games are named…* and *A game never joins…*. Unit **291**; both main fixes
mutation-checked.
<br>**2026-10-07: the rest of the task planned.** The maintainer added Cemu, Azahar, Model 2, PPSSPP and the Switch
family (Yuzu, Citron, Eden, Ryujinx), plus bonus Supermodel, melonDS, ScummVM, shadPS4 and Vita3K. Every save
folder was researched from EmuDeck's own setup scripts (both OSes) and the emulators' source. The remaining phases
are in five groups by save shape, one PR each: B saves named after the ROM, C memory cards, D Sony (`PARAM.SFO`),
E title-ID folders, F Switch. Two design points need the maintainer's yes first: D1, an emulator game found by its
folders rather than its name (B), and D2, a server title key so Ryujinx's numbered folders sync with the Yuzu forks
(F). Details: `tasks/_done/emulator-saves/plan.md` → *The other emulators* and `implementation-grouping.md`.
<br>**Group A merged as PR #60.** Its testenv pass of the source chips and the EmuDeck hardware pass are still open.
<br>**2026-10-07: Group B built (branch `emulator-saves-group-b`, one commit per phase, no PR yet).** D1 confirmed by
the maintainer and built first: an emulator save joins the server game that keeps exactly its files, whatever it is
called (`Enroller.ServerNameFor`/`TrackedFor`, mutation-checked) — so titles from machine-local sources are safe.
`GamelistXml` (ES-DE titles, arcade systems only — maintainer's choice). `RetroArchSaves` became a row of the new
`RomSaves` reader (D4 shape 1), with melonDS, Supermodel and Model 2 as further rows; ScummVM has its own reader
(`scummvm.ini` targets). Both scanners loop `EmulatorSaves.Sources`. **Captured on the real Deck (read-only SSH)**:
paths all as researched, and one trap the plan missed — EmuDeck rsyncs 29 Supermodel NVRAM files into
`~/.supermodel/NVRAM` at install, exactly like Model 2's 39 `NVDATA` files; both are now hidden by SHA-256 until the
game rewrites them (maintainer's choice for Model 2). testenv `emu-fixture` gained all four plus a fixture home per
machine (`SAVELOCKER_EMULATOR_HOME`). Unit **319**. The maintainer also asked for a manual "link to this server game"
option: researched and mocked up (three variants), not built — `tasks/_done/emulator-saves/plan.md` → *Linking by hand*.
<br>**Next action for this item:** the testenv pass (`testenv clean` first — the rig still maps nine real save
folders from earlier sessions), then merge PR #61 (Group B, opened since; *Linking by hand* was picked and built). A played
Supermodel/Model 2 save on the Deck is the one thing no capture could show.
<br>**2026-10-07, later:** Model 2 save states added (`STATES/<set><0-9>.sta`, read from the Deck's `EMULATOR.EXE`);
unit **320**. The maintainer set the rule for every emulator reader: **EmuDeck (SteamOS/Linux and Windows) is the
design target, standalone installs a bonus** (Decisions.md).
<br>**2026-10-07, later still:** **Phase 16, linking by hand** built on the same branch (mockup version 2: each
emulator row says which server game it joins, with a **Change** button — automatic, keep it as its own game, or another
game with the same files; different file names shown greyed out). The different-file-names case is **Phase 17,
Group G**, not started, open questions in its plan section. Unit **325**. A save-tree mockup (console version history with
each save collapsed + the agent's current save tree, three variants) is at
<https://claude.ai/artifact/2Nq9PbykqDDici67dWCGP2>, picked: **B in the console, A in the agent**, now its own task
(`tasks/save-file-trees/plan.md`, a separate PR — built 2026-10-08, see below).
<br>**Group H, real-game checks** (no code, always the last group): melonDS, ScummVM, Supermodel and Model 2 are
waiting for real games (maintainer has none yet). Any later emulator shipped without a real-game test is added there
(`tasks/_done/emulator-saves/plan.md` → *Phase 18*).
<br>**2026-10-07: PR #61 (Group B) reviewed, every finding fixed on the PR.** The one that mattered: *Keep it as its own
game* could still end up syncing with the fleet's game. The machine that kept a save apart also tracked the fleet's
same-files game (adopted, no folder), and the poller mapped it the moment a folder resolved here: another Windows
machine's `<winAppData>` template (always one for Model 2 and Supermodel) or the console. After that, two games pushed
and pulled one save. Now that choice opts this machine out of the other game (`Enroller.KeepApart`), and the poller
never maps a second game onto files another game syncs here (`SaveFolderClaims`, reported as `savedir.unsafe`). D1
now looks at the **primary folder only**, ignoring case. An exact match on every folder made an older agent's game
(no `states` yet) or one changed by `add-path` look like a different game, and the next machine split it under
"… (RetroArch)". The missing folder is now added to the game instead. **EmuDeck's untouched NVRAM seeds** are no
longer dropped: they are listed (agent UI, Game Mode) only when a server game keeps the same file, they may only join,
and they are recorded as in step so the first pull replaces the seed. Before, a fresh Deck could not get the fleet's
Scud Race save without playing first and causing a conflict. Smaller: Add games says when the server could not be
asked; melonDS's "beside the ROM" mode only takes a save with a DS ROM beside it, and honours `XDG_CONFIG_HOME`;
ScummVM finds fixed-name engines from a `gameid` alone (`sword1mac`); testenv keeps a hand-set
`SAVELOCKER_EMULATOR_HOME`; and the row checkbox has its own accessible name. Unit **332** (+7, the four main
fixes mutation-checked); `agent-ui/src/api-types.ts` regenerated (one addition, `untouchedSeed`).
<br>**2026-10-07: Group C built (branch `emulator-saves-group-c`, one commit per phase, no PR yet).**
PCSX2, DuckStation, Dolphin (GameCube and Wii) and PrimeHack, plus the shared-memory-card warning and a Console row
on Add games. The maintainer decided four things at the start:
- a card every game shares is a greyed-out row with how to fix it, never a game;
- states sync, but not PCSX2's `.p2s.backup` copies;
- Wii is in this group;
- **saves from different emulators are never one game**, even with the same files. The server now sends
  `GameDto.Emulators`, an additive field.

That last rule contradicts Group F's planned Eden ↔ Ryujinx sync, so confirm it when F starts. A read-only Deck
capture found PCSX2 on **one 64 MB shared file card** (EmuDeck does not make it a folder card) and Metroid Prime
Trilogy saved in both Dolphin and PrimeHack. The maintainer's PC turned out to have a real EmuDeck for Windows (link
layout as researched), which broke a Group B test that read its ES-DE gamelists; that is fixed, see Gotchas. Unit
**361**. Details: `tasks/_done/emulator-saves/plan.md` → *Group C — as built*.
<br>**2026-10-08: names by serial.** The maintainer's Deck showed Warrior Within and The Two Thrones both as "Prince
of Persia" (`icon.sys` says only the series). PS1/PS2/GameCube/Wii saves are now named by their serial from a
bundled Redump table (`ConsoleTitles`, CC BY-SA 4.0 via libretro-database, PS3 included for later), region kept —
each region's disc reads only its own saves. Fallback: the title inside the save. Unit **363** (+2).
<br>**2026-10-09: PR #63 (Group C) reviewed, every finding fixed on the PR.** The one that mattered: a DuckStation card
per game holding more than one product code was called *shared* and could never be added — a multi-disc game whose
later discs save under their own code (DuckStation keeps one card per title), or Suikoden II with a Suikoden save copied
in to import. Now a card named after one of its games is that game's (`MemoryCards.OwnCodes`, Decisions amended), and its
states are only that game's codes, so an imported game's states are never claimed twice. Also: the poller's "found
at…" folder guess for an unmapped game took any same-named row — another emulator's save (PrimeHack's Trilogy for
Dolphin's), another ROM, a shared card, or a scoped emulator folder for a PC game; it now needs the same save
(`CommandPoller.PathCandidateFor`). DuckStation only takes slots 1–2, which its scope names; `ReadGci` reads the
header and comment, not the whole save; `SaveDirSanity` also warns about a whole folder of DuckStation per-game cards;
an emulator source with no name no longer makes a game no emulator's; and Add games' Console filter only applies
while its chip is on screen. Unit **366** (+3; the card and poller fixes mutation-checked).
<br>**2026-10-11: the region left the name** (maintainer's choice, from a clickable mockup). *Prince of Persia - Warrior
Within*, not *… (USA)*: the region, game IDs, console and languages are the game's release (`GameReleaseDto`), kept on
the server (`Game.Region` + `ReleaseJson`, migration `20261011085942_AddGameRelease`) — **games are now unique by
(name, region)**, so both Warrior Withins share a name. A save joins a same-named game only in its own region. The
console and agent game pages show a region chip that opens the details; lists show the region as text; the Deck's
Game Mode list shows it as the row's trailing text (and its rows are now keyed by game id, not name). Unit **387**
(+4 agent, +3 against a real server; the server's region match mutation-checked).
<br>**2026-10-11, same branch: one PS2 game across differently named cards.** The PC's European Warrior Within
(card `MC1.ps2`) became *… (PCSX2)* instead of joining the Deck's (card `PS2MC-1_converted.ps2`): the card's name was
in the scope. A PCSX2 game's folder is now the card itself and its scope only the product code (`BESLES-52822*/**`),
so the scope is the same on every machine — no server or API change. Unit **388** (+1; the cross-card case and the
two-machine sync test, now on two card names, fail with the old scope). **The rig's two Prince of Persia games on the
console (DeckTest) still have the old scope:** remove them and re-add from the Deck and the PC, after `sync` +
`build -Only deck` + `up -Only deck`. Also found on the Deck that day: its PCSX2 states folder is empty (the only
`.p2s` files are in `~/ludusavi-backup`, Sept 2025), and its `PCSX2.ini` names no slot-1 card, so which card PCSX2
writes to is unconfirmed.
<br>**2026-10-11: those three commits reviewed, both findings fixed on the PR.** The tray watched game processes in a
map keyed by **name**, so two tracked releases of one game, each with a process name, threw on every poll. The timer
swallows the exception, so no game's launch or exit was seen again. It is keyed by id now (`ProcessWatcher.ByGameId`).
And the "same files" rule (D1, `Enroller.SameFiles`/`TrackedFor`) ignored the region: DuckStation names a card per game
by its title, which both regions' discs share. So Add games offered the USA *Crash Bandicoot* as the one a European
card joins, while the server made a separate game. Now same files means the same region too, and the other region's
game is listed as *Another region*. The testenv change (`ssh -n`) and the PCSX2 card-as-folder change had no findings.
Unit **390** (+2, both mutation-checked).
<br>**2026-10-11, same branch: keeping the cloud's save in a conflict now takes effect.** Maintainer's report from the rig: the
PC's first sync of Warrior Within conflicted, *Cloud* closed the pop-up with no word, and the conflict came back. Resolving only
told the server; the guarded pull it queued is refused on the loser whenever its save was never synced (it differs from the last
synced hash), so the next sync pushed again — three rounds, then uploads paused behind a conflict the server no longer had (the
header still said "1 conflict"; no pop-up). Now `SyncEngine.ResolveOwnConflictAsync` (agent UI, CLI `resolve-conflict`, and the
Deck's Game Mode screen through the daemon) pulls straight after: forced when the save here is still the rejected version (its hash
read before resolving — an unprotected loser is pruned by the resolve) or with Keep both; a save changed since keeps the guard and
says so. Server-queued pulls stay guarded. A push paused behind a conflict the server no longer has open lifts the pause and asks
again. The local `POST /api/conflicts/{id}/resolve` answers `{ message }`, shown as a toast. Unit **394** with the review fixes merged (+4, against a real server;
the forced pull and the pause lift each mutation-checked). On the rig the pop-up came back after *Sync all*; the choice was left to
the maintainer (Cloud overwrites the real `D:\Emulation` card's Warrior Within save). **Not fixed:** the conflict card says "the
other is never deleted, just set aside", but without *Also keep the other one as a backup* the loser is ordinary history and was
pruned within seconds on the rig.
<br>**2026-10-11/12: Group D shipped — PR #65 (`emulator-saves-group-d`).** PPSSPP (PSP), RPCS3
(PS3), and the bonus Vita3K (PS Vita) and shadPS4 (PS4), on a new `PARAM.SFO` reader and D4's shape-2 helper
`TitleFolders` (a folder per game ID in a shared folder, which Group E reuses). The maintainer decided three things:
build from the research; name PSP/PS3 games by Redump, then `PARAM.SFO` (Redump's PSP set added to the bundled table at
the same pinned commit); ship both bonus emulators. A PSN or Vita game's region comes from its ID's prefix; a PS4 game
has none. Captured read-only: the Deck's real RPCS3 save (*Heavenly Sword*, whose SFO says "Heavenly Sword™"), its
`vfs.yml` and Vita3K `pref-path` on the SD card, PPSSPP as a Flatpak, shadPS4 on the old layout; on the PC, EmuDeck for
Windows' junctions point from the emulators' folders into `Emulation\saves`. No PSP, Vita or PS4 save on either, so all
four are in Group H. Unit **420** (+26; the per-game scope in the two-machine PPSSPP test mutation-checked). No API
change. 2026-10-12 follow-up: RPCS3, Vita3K and shadPS4 save per emulator user (folder only; no emulator checks a
save's owner); Vita3K now reads every `ux0/user/*/savedata`, not only `00`, and the help article says how to move a
game to another user's folder (**Change** on the game's folder). A PSP game played from states only is now listed
(*Save states only so far*); the other emulators' version is new Phase 19 / Group I. Merged `main` (PR #64) into the
branch. Unit **427**. Details: `tasks/_done/emulator-saves/plan.md` → *Group D — as built*.
<br>**2026-10-12: Group E built (branch `emulator-saves-group-e`, one commit per phase).** Cemu (Wii U), Azahar (3DS, Citra's
folders as its rows — maintainer: one game) and Xenia (Xbox 360), on Group D's `TitleFolders`. The maintainer asked for a
bundled title table where licensed: the table gained **Xbox 360 by title ID** (a Redump serial spells it: `MS-2505` →
`4D5309C9`, *Forza Horizon*; No-Intro's Arcade titles) and **3DS by product code** (No-Intro, read from a ROM's NCCH header,
new `Ncch`), same pinned commit, every old row unchanged. No Wii U table exists (Cemu's own cache or `meta.xml` names it). Scopes
keep only saves: Azahar `<low>/data/**` (never the installed `content/`), Xenia `<TID>/00000001/**` + its headers (never DLC),
both mutation-checked in two-machine tests. Captured read-only: the Deck's three 3DS saves and 11 ROM headers, its Cemu/Azahar
AppImages, Xenia's two layouts; the PC's real Xenia *Forza Horizon* saves (per profile). Unit **471**. All three are in Group H.
Details: `tasks/_done/emulator-saves/plan.md` → *Group E — as built*.
<br>**Same day, PR #68 (Group E) follow-ups:** a user-binding review of every emulator (none checks a save's owner; Cemu, RPCS3,
shadPS4, Vita3K and Xenia bind by folder only; Xenia XUIDs are random per profile). Built: Xenia device saves
(`content/0000000000000000`, key `device`) and the 2023 named-profile layout; unit **474**. Designed, not built: **Phase 5c**,
copying the whole Xenia profile to another machine and optional per-game DLC (Forza Horizon's is 1.0 GB, over the 500 MB cap) —
four open questions in `tasks/_done/emulator-saves/plan.md` → *Phase 5c*.
<br>**Phase 5c approved and started (same day):** the Xenia profile checked at launch with the profile split by game, and the
DLC check — final plan in `tasks/_done/emulator-saves/plan.md` → *Phase 5c — final plan*, 14 steps. Steps 1–4 built on #68: Xenia
file formats (XDBF, Account gamertag, DLC/update headers, the `logged_profile_slot_0_xuid` config incl. Xenia Manager's per-game
configs), `LaunchDecision.Ask` + `POST /api/games/{id}/launch-answer` (the default is applied before asking; answers kept on
disk 10 min so the Linux wrapper honours a Decky answer), the profile at launch. Plugin PRs: Marwanello/SaveLocker-Decky#5,
Marwanello/SaveLocker-Playnite#8. Unit **495**. Real files captured from the PC (Xenia Manager) and the Deck (EmuDeck, Proton,
legacy `content/<TID>` DLC and a title update) are in the session scratchpad only — they hold a real gamertag.
<br>**Later the same day:** Phase 5c steps 5–9 and 11 built (profile slice + core seed + alignment with `Game.EmulatorProfile`;
DLC lists per version/machine, held pulls, the agent DLC card; console DLC card; the server hand-over `ContentTransfer`).
Migrations `AddGameEmulatorProfile`, `AddDlcCheckContent`, `AddDlcHandover`. Unit **513**.
<br>**2026-10-13:** Phase 5c steps 10 (rest), 12, 13 and 14 built — `DlcHandoverRunner` (poller-kicked, its own task),
`DlcPackageFiles` (zip / checked install, old layout → `content/<TID>`), held pulls re-checked once a minute, notices,
agent Settings → Xbox 360, launch reports (`MachineGameContent.LaunchJson`, migration `AddLaunchProfileReport`), console
profile-at-launch + hand-over cards, help article, `dlc` CLI, testenv DLC fixtures; Decky#5 gained a "Waiting for DLC"
section. Unit **517**. Gotcha: the hold re-check is a 1-minute poll, not a folder watch.
<br>**2026-10-13, later:** a sender now **sends DLC automatically by default** (`DlcSend` null = Auto); hand-overs up to
**8 GB** by default; the agent reads a server refusal sent as a bare JSON string (a conflict resolve used to say only
"400 Bad Request"). **Emulator settings** built from the approved mockup: a page of its own off each game page, in the agent
(`EmulatorSettingsView`, sections Xbox 360 profile / DLC and title updates / Sharing DLC; `emulators.ts` says which emulator
has settings — only Xenia so far) and the console (`EmulatorSettings`, Xbox 360 profile / DLC and title updates /
Hand-overs). The DLC and profile cards left the game pages; banners there point in. New `POST /api/games/{id}/transfers`
lets the console send a machine its missing DLC. Unit **523**. Checked by hand against the rig's tray and a copy of its console
database. Gotcha: a stuck conflict whose two versions are both behind Latest can't be closed from the agent (both choices
would rewind) — the console's *Set as Latest* on one side closes it; making "keep the cloud's" mean "the current Latest" is
an open question for the maintainer.
<br>**2026-10-13: PR #68 and its plugin PRs reviewed, every finding fixed on the PRs.** The one that mattered: a pull while
Xenia ran left the profile row unmerged, and the next push exported this machine's older row over it — carrying it to every
machine. A skipped merge is now marked (`<state>/xenia/merge-pending/<game>`); the next push merges first and never exports
the row while it waits. Also: Xenia under Proton is `xenia_canary.ex` to Linux (15-character names), so it was never seen as
running on the Deck; a title update counts as present when any title update is installed (`ContentPackageDto.Missing`, shared
by agent, server and console), and a save this machine made or already has is never held; a launch whose pull is held answers
`ProceedSyncPaused` with the reason (Decky#5 and Playnite#8 show it); the sending agent only zips packs it lists itself and
skips links, and the receiver refuses duplicate names and oversized zips before writing; Xenia Manager's per-game config is
matched on its own spelling of the title, inside `config/` only; Sync all counts held games (`SyncRunDto.HeldForDlc`); an
unanswered hand-over for other packs is replaced; the console ignores machines that no longer keep the game. Decky: an `Ask`
no longer reuses the conflict freeze (a raced launch runs on; a different choice applies next launch); `.gitattributes` keeps
LF. Unit **539**.
<br>**2026-10-13: Group F built (branch `emulator-saves-group-f`, PR on the fork).** Switch: Eden, Citron, Yuzu, Suyu,
Sudachi (one table, `SwitchEmulators`) and Ryujinx (its `imkvdb.arc` read by `RyujinxSaveIndex`). The maintainer's calls:
**one game per Switch title across every Switch emulator** (the exception to "different emulators are never one game",
Decisions.md), D2 built as planned (`Games.EmulatorKey`, migration `AddGameEmulatorKey`), a ROM with no save listed as
*No save here yet*, captures from both machines (Citron researched), and a **same-machine copies** feature for playing
one game in two emulators: the agent's Emulator settings page copies the synced save to another emulator, keeps it in
sync (the one played last wins; asks when both changed; nothing copied while an emulator runs; backups under
`<state>/emulator-copies`), or makes it the synced copy. Captured read-only: the PC's Eden (*Metroid Prime 4*) and
Ryujinx index; the Deck's Yuzu and Ryujinx, with *Undertale* saved in both. Also marked Groups C and E (with 5c) shipped.
Unit **551**. Details: `tasks/_done/emulator-saves/plan.md` → *Group F — as built*.
<br>**2026-10-14, Group F follow-ups (same PR):** Switch games are named from a bundled eShop table
(`src/Shared/SwitchTitles`, blawar/titledb, MIT) when the machine has no Ryujinx record or ROM name; a game first
added as `Switch <title ID>` is renamed by the server when a machine knows its name; Switch rows have **Change**; the
server takes a Switch game's eShop icon (no SteamGridDB key needed). Checked live on the rig (WSL first, then Windows;
the real TotK icon fetched). Unit **558**. The emulator/console icon work is planned as Group J (Phase 21), not built.
**Rig gotcha:** another session's tray held :5188, so `testenv up` reported *its* build — this branch's Windows tray ran
with `-WinPort 5198`.
<br>**2026-10-14, PR review fixes (same PR):** keep-in-sync compared a copy against a different set of folders than it
wrote, so a device save only one emulator had a place for (Ryujinx before it makes one) was copied again every poller
tick, each with a backup and a toast — both sides now go through `SwitchCopies.Pair` (only the folders both have) and an
empty extra folder hashes as absent. Turning keep-in-sync on now seeds `LastHash` so the save played last wins instead of
an instant "Both changed". Copy actions and the poller's reconcile run under the game's sync lock
(`SyncEngine.WithGameLockAsync`, the API server's `withGameLock`). **Make this the synced copy** tells the server first
and changes nothing when it can't (the poller applies the server's folder back), and refuses a target with no device
save when the game keeps one. The server never joins or edits a same-named game keyed to another title; the poller
renames only a `Switch <title ID>` placeholder. Unit **566**.
<br>**2026-10-15, same PR: which emulator plays it (direction A).** The maintainer's "I chose the cloud save and nothing
changed" on the Deck was the save going into Yuzu, the emulator the game synced through there, with Eden (where they
play) not installed. The Switch Emulator settings page now leads with a status line naming that emulator (**Put the cloud
save in Yuzu** / **Keep Yuzu's and upload it**, forced, refused if it no longer syncs) and an **On this device, you play it
in** picker (switch keeping its save or taking the cloud's; not-installed and no-slot emulators explained, with **Tell me
when … is ready**). New local route `vs-cloud`, actions `take-cloud`/`upload`/`tell-when-ready`/`stop-telling`. Agent
tests **639**. Details: `tasks/_done/emulator-saves/plan.md` → Group F follow-ups, 2026-10-15.
<br>**2026-10-16, same PR:** a Switch game's folder keys now follow the server game's layout on join (a device save is
`main` where the fleet has no `device` folder), which is why the Deck's Animal Crossing island never reached the PC's
Eden — **remove the game and add it again** on machines enrolled before this. The picker moves the save to the emulator
picked, shows each install (EmuDeck/Standalone) separately and always lists Eden, Citron, Yuzu and Ryujinx; the copies
card is gone; the controller only drives a focused agent window. `api-types.ts` regenerated from a running agent (the
`package-linux` CI check). Playthroughs (named save forks) added to the backlog.
<br>**2026-10-16, later:** "the save data is damaged" in the PC's Eden was the **game version** (Deck's Yuzu runs ACNH
1.6.0, the PC's Eden the base 1.0.0; the save copied byte for byte). The DLC check now covers a Switch game's version,
read from the emulator's log or game list cache: a newer version's save holds the pull, and the picker shows each
emulator's version and warns. Animal Crossing also ties residents to the emulator user's ID (decrypted `main.dat`),
so the Deck and PC need one user ID or the game asks to relink on each switch.
<br>**2026-10-16, last:** built — **Settings → One Switch user on every device** (`SwitchUserSharing`): the user picks a user
ID; every Yuzu-based install on every device adopts it (backed up, saves moved, games re-pointed on the server too),
titles saved under both users wait for a pick, each device can put its original user back. Migration `AddSwitchUsers`.
Not yet run on the rig or the Deck — pick the Deck's Yuzu user `DBBE23FA…` (the island's).
<br>**2026-10-18: Group F done** (PR #73, ready to merge, CI green). Animal Crossing 3.0.3 loads on the PC (Eden) and the
Deck (Eden) through the rig. Found and fixed: one Eden listed twice (EmuDeck's link ends in `/`), and a device save copied
under a user, where the emulator never opens it — the server now records a device-save game (`EmulatorProfile =
"switch:device"`), joins and copies use the device save, and a machine that kept it under a user moves it there itself.
Agent tests **656**. Details: `session_summary.md`, `progress.md`.
<br>**Gotcha:** `testenv.ps1 build` builds the Deck/WSL agents from the WSL clone — run `testenv.ps1 sync` first after
committing, or the Deck keeps the previous build (check `testenv.ps1 status`'s build labels).
<br>**2026-10-18, second review of PR #73 (the commits since 2026-10-14):** a Switch copy is now recorded per install
(`EmulatorCopy.Saves`), so keep-in-sync for a standalone install of the synced emulator no longer stops at the EmuDeck one;
`SwitchUserSharing` adopts or undoes one pass at a time (the poller and the Settings button raced on one install), and a
failed adopt restores the saves it set aside for titles under both users from its backup; `NowReady` runs under the game's
lock with the keep-in-sync pass; `SwitchGameVersion` reads each log once per size and write time instead of on every page
load, push and pull; three misplaced doc comments fixed. Unit **659**.
<br>**Next action for this item:** merge PR #73; then Group G, H (the other emulators), I or J.
<br>**2026-10-18/19: Groups G, I and J built — [PR #78](https://github.com/Marwanello/SaveLocker/pull/78) (`emulator-saves-group-.g-i-j`),
one commit per phase.** Phase 19 (I): every reader with states lists a game played from states alone. Phase 20 (G): a Steam ROM
Manager shortcut is the emulator's game (`SrmShortcuts`; its app ID drives the launch wrapper), built against the Deck's real
`shortcuts.vdf`, captured read-only. Phase 17 (G): a save links to a game of the same emulator whose files are named
differently, with a per-machine file-by-file name list (`FileNames`, `OwnFileNames`), plus a **server merge** of two games
(console, preview + typed name; merged versions read-only history). Migration `AddFileNamesAndMerge`. Phase 21 (J): emulator and
console icons generated from Arcticons and RetroArch's Monochrome glyphs (`web/scripts/build-platform-icons.mjs`, `NOTICE-icons.md`)
in all three UIs; `SvgPath` draws every path command. Agent tests **721**. Details: `tasks/_done/emulator-saves/plan.md` → *Group G/I/J
— as built*.
<br>**2026-10-19: review fixes on the same PR.** A file that the name swap would give the server's name (another dump's
`… (USA).state1` beside this machine's `… (USA) (Rev 1).state1`) is now that other dump's, never archived or deleted as this game's,
and its presence refuses the link like a same-named `.srm` does. The archive's name map uses the platform's comparer again (it
had made `save.dat`/`SAVE.DAT` one file on Linux, for every game). A machine moved by a merge clears its last synced hash, so a
differing save meets the kept game's Latest as a conflict instead of being pulled over. `LaunchesUnder` compares unsigned app IDs;
launch-options status is per shortcut (`ShortcutLaunchOptions`); a shortcut gone from Steam leaves the game's list; only the
program word (or what `flatpak run` starts) names an emulator; merge also refuses on the kept game's lease/conflict. Agent tests
**728** (in Release: the Debug agent output was held by a running test tray).
<br>**2026-10-21: emulator saves done.** PR #78 (Groups G, I, J) merged 2026-10-19 and PR #81 (Group H's real-game checks plus
`tasks/_done/emulator-pull-safety`) merged 2026-10-21; both task folders moved to `tasks/_done/`. The maintainer verified every
testenv and hardware pass (Groups A–J), played Ryujinx and Xenia (launched from EmulationStation) against real games. Left by
choice: Model 2's real-game check (no game) and the items each plan lists as not built or *Deferred*. The testenv PCSX2 fixture
with a 0-byte superblock was added afterwards (WSL `Mcd000.ps2`). The next action lines below are history.
<br>**2026-10-22: v0.8.0 hid every scoped emulator save on a machine that also tracks PC games (branch
`emulator-saves-hidden-by-pc-games`, not released).** `Enroller.ByFiles` compared a scoped save with `ServerNames(g, …)`,
which is empty for a game with no own file names, so every PC game (no include patterns) "kept" it: on the real Deck all 101
non-Switch emulator saves read `enrolled: true` and Add games showed only the Switch ones (unscoped, never matched by files).
`SrmShortcuts.Backfill` rode the same match and put Super Smash Bros. 3DS's shortcut (`2599779049`) on *Alba* as its
shortcut and Steam app ID — removed by hand from the Deck's `config.json` (backup `config.json.bak-alba-20261022`); a v0.8.0
rescan (opening Add games) puts it back until this ships. Fix: an unscoped tracked game never matches by files.
<br>Same branch, from a review of the other callers: a Steam/Heroic/Playnite game never matches an emulator save by name
either (a Steam *Undertale* made Yuzu's read as tracked; no source or a hand-mapped folder still matches), and every rescan
now drops shortcut IDs from a game that is never an emulator's — a store game, or a sourceless one with no scope and no key
— with an app ID taken from one, so other machines heal themselves once updated. Only a shortcut that same scan gives to
another save is dropped (review fix): one the scan did not see (SD card out, unreadable Steam folder) is kept, since a
sourceless game may be a hand-mapped emulator folder whose launches match on it. Unit **798**.
**Next:** release it; nothing to clean by hand on the Windows PC — its rescan does it.
<br>**Next action for this item:** the testenv pass with the Windows agent (Phase 17 Earthbound link Windows ⇄ WSL — the Windows
test agent still maps a real `D:\Emulation` Animal Crossing save; unmap it first), the Deck pass (Forza Horizon listed once;
launching it from Steam syncs), then merge PR #78. Group H stays open.
<br>**Next action for this item:** the testenv pass for Group E and Phase 5c (`Build and Run` → *Testing emulator saves*,
steps 1–5 of the profile and 1–11 of the DLC hand-over), then Forza Horizon for real between PC and Deck (Group H).
<br>**Next action for this item:** merge PR #65; the testenv pass of Group D (`Build and Run` → *Testing emulator saves*) and its real-game checks (Group H: PPSSPP, RPCS3, Vita3K, shadPS4) are still to do.
<br>**Next action for this item:** the testenv pass of Group C (`Build and Run` → *Testing emulator saves*), then merge PR #63.
On 2026-10-07 the live rig still mapped nine real save folders on its Windows tray and its console was on another
commit, so this session did not touch it: run `testenv clean` first.
<br>**2026-10-08: Save file trees built — PR #62 (`save-file-trees`), not merged.** The console's Versions rows now start collapsed with change chips and open on the changed files (variant B); the agent's game page has *Save files on this PC* (variant A). New routes: `versions/changes` (admin), `agent/games/{id}/head/files` (machine key, SHA-256 from the stored archive) and the local `games/{id}/files`. The palette gained a fifteenth token, **`--color-info` (blue)** — three copies as always (Decisions.md). A file that differs from the server counts as the server's only when the head moved past this machine's last sync and nothing changed here. Detail: `logs/2026-10-08_save-file-trees/plan.md`.
<br>**Next action for this item:** the by-hand testenv pass in the plan (step 3 needs `testenv up`, and another worktree's tray was holding :5188 on 2026-10-08), then merge #62. Separately, `ArcadeSavesTests`' Supermodel naming test fails on a PC with a real EmuDeck install (it reads the real ES-DE gamelists) — offered as its own task.
<br>**2026-10-08, same PR: the agent keeps covers and icons on disk** (`ArtCache`, `<state>/art-cache/`). The maintainer reported art in the agent only appearing once the console had been opened. Not reproduced here — a fresh server with art already stored relays it with no console open — but the proxy held nothing on disk, so art existed only while the server answered and only after something asked. Now the poller warms every tracked game's art at the sizes the agent draws, the proxy serves the cache offline, and a re-picked cover (a new `?v=` URL) replaces the cached one within a poll. Checked end to end with the stub SteamGridDB: cached with no window open, a changed cover replaced, both covers shown with the server stopped. **If art still only appears after opening the console on the maintainer's server, the cause is server-side and still open** — ask which surface and how the server is reached.
<br>**2026-10-09, same PR: review fixes.** A zip that holds one name twice (or `a\b` beside `a/b`) no longer 500s the whole `versions/changes` list or the agent's file tree — later entry wins, as on extract; the server's head-file hashing stops at `SAVELOCKER_MAX_RESTORE_MB` instead of hashing an endlessly expanding archive. A 404 from `head/files` (a server older than the agent) now means "not compared", not "nothing on the server, every file is a push". Files the head lacks carry `NotInHead`, so the agent says *New here* / *Removed on the server* instead of "Changed". The console no longer says "Loading what changed…" forever when the request fails or a version's archive is unreadable, and *Show all files* shows a folder this version removed entirely. Art warming runs after the poll's dashboard commands, not before.
<br>**2026-10-11: "N machines following" built (Appearance follow-up (b), branch `claude/appearance-follow-ups-testenv-f17f77`).** The heartbeat carries `FollowsConsoleAppearance`, `AgentHealth` stores it (migration `AddAgentFollowsConsoleAppearance`), and Configuration → Appearance names the machines that follow and those keeping their own look. `run-health-tests` **35/35**. Closed as done (maintainer's call); the testenv check in `tasks/_done/appearance-follow-ups/summary.md` has not been run.

---

**Quick-wins batch: seven backlog items closed, one verification item retired (2026-10-09, branch
`fix/backlog-quick-wins`, no PR yet).** One pass, each with the verification the item named:
- **Registration default flipped** (**release notes must call this out**: a scripted `savelocker
  register` without `--admin-password` against a server with a password now gets 401) —
  `Security:RequireAdminPasswordToRegister` now defaults to `true`:
  once an admin password is set, a **new** machine name must present it. A machine key is
  fleet-scoped, so the open door meant the password guarded the dashboard but not the saves. No
  password set is unchanged (first-run stays open); `savelocker register --admin-password` and
  enrollment tokens are the two ways in; setting the switch to `false` deliberately remains for
  trusted networks. Docs updated ([[Build and Run]], [[API Reference]], [[Decisions]], compose, CLI
  reference, troubleshooting). `run-console-security-tests` proves both the default-ON half and the
  OFF switch.
- **The conflict upload latch no longer outlives the conflict** (`SyncEngine.PushCoreAsync`): at the
  pause threshold it asks the server whether the game still has an open conflict before holding the
  save back, so a resolve that landed while the agent was down clears the latch and the push runs.
  Server unreachable keeps the pause. New `ConflictLatchTests` (3, stub server; the stale-latch
  clear is mutation-checked). *(Superseded 2026-10-12, PR #64–67 review: #63's `ConflictClosedAsync` had
  already fixed this in parallel — the two checks are now one, `ConflictStillOpenAsync`, asking the
  machine-scoped conflict list; the test stub answers that route instead of 500ing it.)* **Related, same area:** a dashboard **Push** command answered with a
  conflict now reports Failed with the reason instead of "pushed — latest save …" (`CommandPoller`
  had ignored the engine's result).
- **`post-exit-sync` releases the lease even while Sync all holds the agent-wide gate**: the route
  still answers 409 (the running sync pushes the game) but now stops that game's renewer and releases
  its checkout through a new `SyncEngine.ReleaseLaunchLeaseAsync`, so the fleet is not locked out of
  a game nobody is playing. `run-local-api-tests` holds a real Sync all open and asserts the release.
  *(Changed 2026-10-12, PR #64–67 review: releasing without the push let another machine launch on an
  older save — the gate's holder need not push this game. The whole exit, push then release, is now
  queued behind the gate (`AgentApiServer.QueueDeferredExit`); the test asserts no release while the
  gate is held and the release once it frees.)*
- **Signed-in browsers are listable and revocable one by one** (Configuration → Admin password):
  `GET /api/admin/sessions` (current session marked), `DELETE /api/admin/sessions/{id}` (404 when not
  live, audited as `admin.session_revoked`); "Sign out everywhere" stays.
- **A deleted game's cached art is deleted with it** (`ArtService.DeleteGameArt`, called by the delete
  route — cover, icon and thumbnails).
- **Sign-in lockouts surface as a bell notice**: `GET /api/admin/lockouts` (the in-memory throttle's
  own view, so a restart forgives) is merged into the notifications menu as a Warning that clears
  itself when the window ends; it is not dismissible (there is no record to dismiss).
- **Console Sync all names every failing machine and its reason**, not the first plus "(+N more)".
- `unverified-ui-surfaces` (Heroic store sub-chips) retired as verified; all eight task folders moved
  to `docs/tasks/_done/`, and [[Backlog]] no longer lists any of them.
Verified on the final tree (PRs #62 and #63 merged in; `Backlog.md`'s only conflict resolved by
keeping this batch's removals plus #63's emulator-progress line):
`run-console-security-tests` **364/364** (was 308; the +56 are this batch's checks, including a stale
`run-local-api-tests` candidate-folder check whose expectation predated `SavePathGuard` — it now
asserts the user-profile refusal plus a real folder accepted); `run-local-api-tests` **127/127**
(`SL_LOCALAPI_PORT` moves it off :5188 when another worktree's rig holds that);
`run-agent-tests` 47/47; `run-delta-upload-tests` 33/33; `run-health-tests` 33/33; xunit
**397/397** — all green, including #62's `SaveFileTreeServerTests`, whose shared-server registration
now carries the admin password the flipped default requires, and the `ArcadeSavesTests.Supermodel_…`
casing failure #61 had carried (fixed by #63); `openapi.json` + `web/src/api-types.ts` regenerated
(additions only); `web` build + lint clean; full solution builds. **Not yet run on real hardware** —
the testenv walkthrough is in this session's report.

---

**Notification follow-ups shipped (2026-10-11, branch `notification-follow-ups`,
[PR #66](https://github.com/Marwanello/SaveLocker/pull/66) open — the last Group 7 leftovers,
`tasks/_done/notification-follow-ups/`).** Toast buttons that **act**: **Retry now** on a
failed push and **Install now** on an available update, delivered through a per-user **COM activator**
(`src/Agent/ToastActivation.cs`) — `HKCU` CLSID with `LocalServer32`, plus `CustomActivator` under the
identity key (the Community Toolkit's shape; the shortcut's `ToastActivatorCLSID` is *not* what activates,
measured). While the tray runs the click lands in it; when it is closed DCOM starts the exe with
`-ToastActivated`, which leaves the click in `toast-action.json` and starts the tray. A rig gets its own
per-port CLSID, its own Start-menu shortcut (`SaveLocker Test <port>`, written by `up`, removed by
`clean`) and registry keys; `clean` now **verifies** the state root is actually gone instead of printing
"removed" over the elevated-state leftovers, and `Get-TestTray` also sees an apphost-started tray.
**Verified live through `testenv`**, not just built: a real mouse click on a real toast's **Retry now**
retried a 413-rejected push (`pushed new version.`); **Install now** downloaded, SHA-verified and ran a
stand-in package with `/SILENT /FORCECLOSEAPPLICATIONS /NORESTART` and the tray exited as designed; with
the tray closed, DCOM's host started it and the retried push succeeded. Two bugs were found only by
running it (both fixed, both in [[Gotchas]] → Windows notifications): a rig's `LocalServer32` first
omitted `--config`, so a click with the tray closed loaded the **installed** config and started the
installed agent once — it was stopped immediately, the one log it wrote lives in
`%PROGRAMDATA%\SaveLocker\agent.log`, and the `LastUpdateCheck` it refreshed was reset; and that host
logged to the installed agent's log for the same reason. Unit tests: the notification ones pass
(new: action-argument parsing incl. hostile shapes, the toast XML's acting action, the pending-action
file, the identity/CLSID split); after merging `main` through #65, the **full suite is 450/450** —
including the `ArcadeSavesTests.Supermodel_nvram…` leak this entry previously carried as a
machine-specific failure, which #63 fixed. **The `CsWinRTIncludes`
size idea was measured and changed nothing** (173.1 MB publish, `Microsoft.Windows.SDK.NET.dll` 23.73 MB
with and without it; the property is for projection authoring, and `Microsoft.Windows.UI.Xaml.dll` is not
in this toolchain's output at all). **2026-10-12 — the rig fought back, and `testenv` got tougher.** The
first live walk of the checklist hit an endless 401 flood that survived `clean` and restarts: the tray
holding :5188 was another worktree's build (`fix-backlog-quick-wins`), and `Get-TestTray` was scoped to
the calling worktree's own root, so this worktree's `clean`/`up` deleted the shared state *under* the
foreign tray and left it serving with its old key while the new tray could not take the port. Fixed on
the branch (`4ddb4d2`): any tree build now matches by its `\src\Agent\bin\` path (the installed agent
never lives there); recorded in [[Gotchas]] → `tests/testenv.ps1`. The verification doc also gained its
"no toast appears" troubleshooting block — the toast exists only after §1's >1 MB write, `connection
refused` means the console is down, and `notification shown` with no banner means Windows put it in the
Action Center. Task closed; moved to `tasks/_done/`. **The one manual check is not testenv's:** run the
installer once and look at a real toast's header (steps:
`tasks/_done/notification-follow-ups/verification.md`). Release notes must cover: toast buttons that act
(Retry now / Install now).

---

**Art picker source choice + Linux agent secret permissions shipped (2026-10-12, branch
`art-picker-and-linux-agent-secrets`, worktree, PR to the fork pending — two backlog items, one
branch; full write-up `logs/2026-10-12_art-picker-and-agent-secrets.md`).**
<br>**Art picker: choose which SteamGridDB game a game pulls from** (`tasks/_done/art-picker-game-choice/`).
The first autocomplete result used to be the only option, so a game whose name matches the wrong
entry offered that entry's art with no way out short of renaming it. `Game.SteamGridDbId` +
`SteamGridDbName` (migration `AddGameSteamGridDbSource`; null = automatic) are honored by the options
listing, *Refresh art* and the missing-art backfill; new admin routes `GET/PUT /games/{id}/art/source`
and `GET /games/{id}/art/search`; the picker gained a source row with **Change** (search) and **Use
automatic match**. A pinned id is validated against `games/id/{id}` and the stored name is
SteamGridDB's own. `run-console-security-tests` 364 → **376/376** (+12; its stub now serves
`games/id` and per-id art, and three autocomplete candidates); `tests/sgdb-stub.py` too, for the
by-hand root. `openapi.json`/`api-types.ts` regenerated (additions only); `web` build + lint clean.
**Not verified against the real SteamGridDB** (no key — same standing caveat as the artwork work).
<br>**Linux agent secret permissions** (`tasks/linux-agent-secrets/`). The state dir's mode came from
the launching shell (0755/0644 under umask 022 — `config.json` holds the server key, `api-token`
grants the local API); CLI-only runs never reached `LocalAuth`'s one-off chmod, and `agent.log` was
never tightened. `StateDirSecurity.Protect` now runs on Unix too (0700 dir, 0600 on the known private
files, retroactively), its once-per-process gate moved out of `AtomicFile` so the logger, atomic
writes and `LocalAuth` share it, and `AgentLogger` creates its file 0600 and protects on first write.
Windows untouched. `run-linux-tests` gained 9 checks (4 fail against the pre-change Agent.Core —
mutation-checked); full WSL suite as root **280 pass / 2 fail** (the documented WSLg-session pair);
`dotnet test` **450/450**; Windows agent builds clean. Verified in `unshare -n` because a foreign
daemon from the shared testenv WSL clone held :5187 (both new Gotchas recorded). **The state-layout
split half is planned, not built** — `tasks/linux-agent-secrets/plan-split.md` carries the upgrade
migration, the rollback-across-the-split and Decky-plugin compatibility decisions, and the rig
changes; [[Backlog]] now lists it as its own item.
<br>**Next action:** merge the PR; the by-hand `testenv` pass (art picker against the stub; WSL
agent `stat` on the state dir) is the maintainer's manual check — steps were given in this session's
report. Release notes for the next tag must cover both: artwork can be pointed at the right
SteamGridDB entry, and the Linux agent's state files are owner-only now.
<br>**Reviewed 2026-10-12; five findings, all fixed on the branch.** `StateDirSecurity.Protect` caches
the *outcome* per directory, so a failed first attempt is no longer reported as "already handled" to
later callers; `ArtService`'s name lookup is remembered once for every reader (including the picker's
source row, which had been asking every time) and parses defensively (a malformed search body is "no
match", not a 500); the picker's empty-result message names the query that was actually searched;
`AgentLogger`'s comment records the Windows consequence of its first-write protection; and the
missing-art backfill gained a test proving it fills from a **pinned** source, not the automatic name
match. Re-verified after the fixes: `run-console-security-tests` **378/378** (+2), `dotnet test`
450/450, `run-agent-tests` 47/47, both agent builds at baseline, `web` build + lint clean, the WSL
permission checks re-run by hand.

---

**PR #72 (controller support in the agent UI) reviewed and its findings fixed (2026-10-13, branch
`gamepad-agent-ui`).** The CI failure was the repo's own `run-appearance-consistency-tests` (it scans
every `.tsx` under `agent-ui/src`): the `StartGlyph` fallback SVG in `gamepad/glyphs.tsx` carried
`#2c2c31`/`#fff`; now the tokens `--color-raise`/`--color-fg`, and the suite reads **50/50**.
Also fixed: A over the Controller card's size slider opened the on-screen keyboard (and `NaN` px
could persist for the session) — sliders now own left/right and step with A, the OSK only accepts
text-like fields (textarea included); `aria-disabled` buttons are skipped by nav as the plan
claimed (blocked server-game link options were still stops); Repeat state is cleared when a pad
disconnects mid-hold; the hint-bar mode persists like the glyph prefs; `dispatchEscape` targets the
overlay (a body-dispatched Escape never reached React's root listener); `hasResolveButton` ignores
disabled buttons; overlay detection is cached briefly and picks the highest z-index; the main
bindings now use `data-pad-action` attributes with the old text lookups as fallback; and the game
page's LT/RT hints no longer advertise Push/Pull while a conflict card owns those buttons.
`agent-ui` `tsc -b && vite build` and `oxlint` clean. **Not verified:** Gamepad API input and
gamescope on a real Deck — Phase 7's checklist still owns that.

---

**Enrollment join links — option B implemented (2026-10-14, branch `enrollment-join-link`, worktree
`.claude/worktrees/enrollment-mockups`; the three option mockups are committed on branch
`enrollment-mockups` as `9ab48b7`, under `docs/tasks/_done/enrollment-options/`).** Asked for directly: the
enrollment file is disliked, replace it. Option B picked from the mockups — the console mints a short
code (8 Crockford chars, `JoinCodes` in Shared) into `GET /join/{code}`, a self-contained pairing page:
Windows tab starts the installer download and copies the link, Linux tab is a `curl …/install.sh | sh`
one-liner, and an already-installed agent is reached by `127.0.0.1:5178/#enroll:<link>`. The agent
side is `enroll --url` (CLI; `--code` + `--server` accepted as sugar) over `GET /join/{code}/policy` —
the policy (games, effective excludes) is now built **at delivery**, not minted into a file — and
`POST /api/join` on the local API, which the agent-UI Join card and the `#enroll:` deep link use
(`AgentEnroller` is the one implementation both call). `enroll --file` and the JSON download are
**removed**; the installer's enroll page now takes a pasted join link (`/ENROLL-URL` for silent), and
`install.sh`/help/README/[[API Reference]]/[[Decisions]] were updated. Because 40 bits is short,
`JoinThrottle` guards every lookup (15 failures / 15 min / client + a global backstop; only unknown
codes count; a successful redeem resets) — that throttle is a security property, asserted end to end
including 429 + `Retry-After`. Console card rewritten (code + link + QR via `uqr`, a new web dep).
<br>**Verified:** `run-enrollment-tests` **25/25** (rewritten for links: pairing page, both enroll
forms, single-use / revoked / forged, throttle against its own low-limit server), `run-local-api-tests`
**128/128** (run with `SL_LOCALAPI_PORT=5197` — another worktree's tray held :5188; +3 `/api/join`
checks), `run-server-bugbounty-tests` **216/216** (CS-06/CS-10 updated to the link shape; one
transient CS-01 crash on the first attempt, not reproducible), `run-agent-tests` 47/47, `dotnet test`
**552/552**, `run-appearance-consistency-tests` 50/50 (the QR's colours needed `currentColor` +
keyword black — hex literals are banned in views), `web` and `agent-ui` build + lint clean, installer
compiles (`installer/dist/SaveLocker-Agent-Setup-0.7.1.exe`). `openapi.json` + both `api-types.ts`
regenerated against live scratch instances.
<br>**Not verified (manual pass owed):** the real Windows wizard paste-round-trip against a real
tray install, and a Steam Deck running the pairing page's one-liner — steps were given in the
session's report (testenv walkthrough). `run-enrollment-tls-tests` is blocked on this box: the dev
cert exists but is **untrusted** (`dotnet dev-certs https --trust` needs its dialog accepted).
<br>**Session note:** for a while the headless tooling wrote edits into the **main checkout** instead
of this worktree (opencode's default cwd); recovered with a stash move — builds/tests in a worktree
must always be driven with the worktree's absolute paths. The implementation is committed on
`enrollment-join-link` and in review as PR #76. A review pass then fixed the pairing page's script —
inline under the dashboard's `script-src 'self'` it was silently blocked, so the page lost its Copy
buttons and the Linux tab; it is now served same-origin as `/join.js` — plus `Cache-Control:
no-store` on every `/join/*` response, a pwsh-only `Retry-After` read in the suite (the one Linux CI
failure), a missed `--file` banner in `UiApp.cs`, two stale "file" strings, an empty `#enroll`
banner, and the pre-seed's game-list writes now going through `MutateGames`. Release notes for the
next tag must cover the enrollment-file removal (old agents/installers cannot enroll until updated —
they can still `savelocker register`).
<br>**Follow-up (2026-10-15, same branch) — Linux `.desktop` installer + `savelocker://`.** The pairing
page's Linux tab now leads with **Download installer**: `GET /join/{code}/install.desktop`, a launcher
(`JoinLinks.InstallLauncher` in Shared) whose `Exec=sh -c '…'` fetches and runs the same `install.sh`
in a terminal — the Decky/EmuDeck pattern, single quotes because GLib and KDE both parse them; a URL
needing any quoting gets no launcher (409). Fetching it does not spend the code. The installed menu
entry now registers `x-scheme-handler/savelocker` (`Exec=… open %u`; install.sh runs `xdg-mime
default`, and the daemon rewrites an entry *it* wrote that predates this — `DesktopEntry.OfferOnce`).
`savelocker open savelocker://enroll?link=<join url>` opens the Join card pre-filled (only a
well-formed join link is accepted, still one click to enroll), and `open` now starts the systemd
unit when the daemon is down. The same session also found the pairing page's script blocked by the dashboard CSP and fixed it
with a per-page hash; merging the review commit replaced that with its `/join.js` (the better fix — the
dashboard policy stays as it is), carrying over only the tab switch for the Linux hand-off. **Verified:** `run-enrollment-tests` **31/31** (+6: launcher served,
terminal + exec line, not spending the code, refused once spent, the Linux agent link, the script's tab switch),
`run-console-security-tests` 378/378, `run-server-bugbounty-tests` 216/216, `JoinLinksTests` +
`DesktopEntryTests` 25/25 (`dotnet test` **574/574** overall); the launcher parsed by GLib (`Gio.DesktopAppInfo`, WSL Ubuntu) to
`sh -c <one script>`, and that script run against a scratch server shows the refusal for a bad code
and runs `install.sh` for a good one; pairing page checked in the browser. **Not verified:** KDE
on a real Deck (trust prompt, Konsole), the `savelocker://` link from a Deck browser (Flatpak
Firefox/Chrome go through the portal), and the Windows agent has no `savelocker://` handler.
<br>**Second follow-up (2026-10-15) — a release `.desktop` installer + `testenv flow`.** Releases now
carry `SaveLocker-Installer.desktop` + `savelocker-install.sh` (rendered by build-linux.sh with the
repo, attached by release.yml): double-clicked, it finds the tarball through `SHA256SUMS-linux.txt`,
checks it, runs install.sh and opens the Join card — the EmuDeck pattern for installing without a join
link. README and the install help lead with it. **`testenv.ps1 flow`** tests the fresh-install flow in
throwaway HOMEs (`tests/testenv-flow.sh`, `tests/linux/run-desktop-launcher.py`): install.sh gained
test-only `SAVELOCKER_INSTALL_SANDBOX=<port>` (never touches the user's systemd manager — `$HOME` does
not separate it — and runs the daemon on that port), and both installers honour
`SAVELOCKER_RELEASE_BASE` ([[Gotchas]]). WSL headless, Deck staged for double-clicking (or
`-Headless`), Windows via a generated Windows Sandbox `.wsb`. `flow` refuses a Linux build without the
sandboxed install.sh. CI's package-linux job now runs the release installer end to end through GLib,
and its menu-entry check was updated for `open %u` (the previous commit would have failed it).
**Verified:** `flow -Only linux` **10/10** against a separate console container (:5081, this branch)
and a Linux build from a private WSL copy — the shared rig was in use by another worktree
(`duplicate-shortcut-warnings`) and was not rebuilt; WSL's real `~/.local/share/SaveLocker` unchanged
since 2026-09-22, no systemd unit touched. `flow -Only windows` built and hosted the installer and
wrote the `.wsb`; the pairing page answered on the LAN URL. **Not verified:** Windows Sandbox itself
(the feature is not enabled on this PC), the Deck target, and the CI workflow changes (not run).
Testing through `flow` from a sandboxed shell: its writes under `%LOCALAPPDATA%` are invisible to WSL,
so `-StateRoot` had to point into the session scratchpad.

---

**Manifest path constraints + scheduled refresh, console-controlled (2026-10-14, branch
`manifest-path-constraints`, worktree `.claude/worktrees/manifest-path-constraints`, three commits —
not merged).** The backlog item *Constrain manifest paths*, built after the maintainer revised the
approach: the first plan pinned the manifest to an approved revision; the maintainer rejected the pin
(a reliable live feed, and a pinned copy makes every game released after the pin undetectable) and the
plan was re-cut to **constrain what an entry may resolve to** and to actually **refresh the feed on a
schedule** — which the agent had never done (the cache was downloaded once and served forever unless
someone ran `refresh-manifest`). The maintainer also asked for the window to be editable in the
console with a "refresh now" button, which is where the server/dashboard/agent plumbing below
comes from. Full write-up: `logs/2026-10-14_manifest-path-constraints.md`; decisions in
[[Decisions]] (two new bullets: the live feed and the constrained paths).
<br>**What shipped:** (1) `PathResolver` refuses any template that is not absolute, canonicalises
before the token-root prefix check (the old check compared the RAW
string, so `<winAppData>/../../../x` passed), and requires the link-resolved path — every symlink/
junction component, via the new `Shared/RealPath.cs` — to stay under a link-resolved resolver root;
the user-mapped path classes (`SavePathGuard`'s floor) are untouched. (2) `ManifestLoader.DownloadAsync`
streams with a 64 MB cap and validates (parses, ≥ 5,000 games) before replacing the cache by a
temp-file move; `Detection.RefreshManifestIfDueAsync` refreshes a stale cache in the background
(attempt gap 1 h / 30 s forced, never throws, failure keeps the old copy), driven from `CommandPoller`
on both hosts; `AgentConfig` carries `ManifestRefreshDays` (default 3), the pushed window, and the
handled request timestamp. (3) Server: audited admin routes `POST /api/settings/manifest` (0–365,
400 otherwise) and `POST /api/settings/manifest/refresh`; `AgentHeartbeatResponse` and
`ServerSettingsDto` carry the window + requested-at (append-only, version-skew-safe — the appearance
pattern); Configuration → Defaults & maintenance has the **Manifest refresh** row (window select +
**Refresh now**, "Last requested … ago"). `openapi.json` + `web/src/api-types.ts` regenerated,
additions only.
<br>**Verified:** xunit **571** (`ManifestPathConstraintTests` 11, `ManifestRefreshTests` 8 — an
`HttpListener` stub; guards removed flip 4 + 3 of them); `run-hardening-tests` **37/37 Windows** (+4,
section 5: a real traversal entry and a real junction escape in a fake prefix, clean-sibling control —
**both new checks FAIL against the pre-fix resolver** via a live mutation pass); `run-agent-tests`
**49/49** against a fresh scratch server (+2; hostile entry refused with its target created); live wire
check (`POST` window → `GET /api/settings` → heartbeat response carries both fields; 9999 → 400);
Server/Agent/`Agent.Linux` builds clean, `web` build + lint clean. All fixture configs that point
`ManifestCachePath` at a file now set `ManifestRefreshDays: 0` (five suites + two xunit fixtures) so
no suite can download over its fixture.
<br>**A real trap the mutation check caught:** the hardening fixture's first traversal target sat one
directory above where six `..` segments actually land, so the check passed against BOTH fixed and
mutated code — a vacuous test, invisible without the "must FAIL pre-fix" pass. Fixed and re-proven.
<br>**Not verified:** `run-linux-tests` (WSL) and `run-winagent-tests` (interactive desktop) were not
run; the console card was not eyeballed in a browser (it type-checks/builds; the wire was checked
live); console-button → live-daemon download is covered by unit tests + wiring, not a live daemon.
**Next actions:** merge when reviewed; next release notes need a line about the scheduled refresh +
Configuration control; watch a real machine's log for "Manifest refreshed: N games." after the first
window or a console press. Deliberately not done: `SavePathGuard.Canonicalize` still resolves only the
leaf (an intermediate-link upgrade for user paths is a separate item), and the on-disk cache is not
re-hashed at load.
<br>**Reviewed at depth on 2026-10-14 (ten angles), every finding fixed on the branch.** The
load-bearing fixes: a non-YAML body (a proxy HTML page — the `YamlException` that used to escape the
fire-and-forget refresh task) is wrapped as `InvalidDataException`, so "never throws, keeps the
cache" is real and logged; the console's "Refresh now" is marked handled only once an attempt
actually starts (a press the 30 s gap refused used to be consumed with nothing downloaded), and the
due check is "differs from", not "newer than", so a server clock step-back cannot strand a press;
the download runs OUTSIDE the manifest lock (a first-ever reader used to wait out the whole transfer;
it now serves the cached file immediately); one shared `HttpClient` with a 30-minute timeout instead
of a fresh 100 s-default client per refresh (a ~17 MB manifest on a slow link could never finish
while the console reported success); PID+Guid temp names (the fixed `.tmp` was shared across
processes) plus a declared-vs-delivered length check; conditional GETs via an ETag stored beside the
cache (a 304 restarts the window without re-downloading); `Detection` reloads its in-memory manifest
when another process replaces the file (Game Mode and the daemon used to serve it forever); `..` is
canonicalised-and-contained rather than flat-banned — the ban contradicted real entries
(`<base>/cfg/../*.json`, the shape the detection harness itself collapses) — and `ManifestPathConstraintTests`
now pins both the escape and the contained-collapse cases; `SetManifestRefreshRequest.RefreshDays`
is nullable so `{}` is a 400 instead of silently meaning "never"; `ApplyManifestSettings(null)` keeps
the last pushed window (an old server no longer clears it) and `TrySetServerUrl` clears the
server-scoped window/handled stamps on an origin change; the effective window is clamped on read so
a hand-edited config cannot overflow `TimeSpan.FromDays`. Test/suite fixes: the Linux, regression,
concurrency and winagent suites pin the SERVER's window (`Manifest__RefreshDays=0`) — the fixtures'
agent-side `0` never won, because the heartbeat's pushed value overrides it, so a daemon on a >3-day-old
checkout would have replaced `tests/linux/manifest.yaml` with the live feed; the hardening "traversal
target was never touched" check (unfailable — the suite created the target) is now "never named",
with the section numbers and header counts corrected; run-agent-tests' traversal fixture escapes to
`C:\Users` (exists on every box, under no root) instead of staying inside `<home>`, which the new
containment deliberately allows. **Re-verified:** xunit **576/576** (`ManifestPathConstraintTests`
12, `ManifestRefreshTests` 12), `run-agent-tests` **49/49**, `run-hardening-tests` **37/37 Windows**,
`run-delta-upload-tests` 33/33, `run-local-api-tests` 128/128, `run-console-security-tests`
378/378, `openapi.json` + `web/src/api-types.ts` regenerated (only schema change: `refreshDays`
nullable; live wire: `{}` → 400, `5` → stored round-trip, `9999` → 400), `web` build + lint clean,
`bash -n` on both Linux scripts. **Still not run:** `run-linux-tests` (WSL) and `run-winagent-tests`
(interactive desktop), and the console row still has not been eyeballed in a browser.

---

## Where things stand

**Shipped in v0.5.8: "Install update now"** (`logs/2026-08-15_install-update-now.md`, all three
phases). `/api/agent-version` now distinguishes **staged** (downloaded,
SHA-256-verified, smoke-tested, applying it is a file copy that works offline) from merely
**available** (the server is offering something newer; taking it needs a download that can fail) —
they were one field and only the first may be offered as an instant install. It also publishes
`stagedBlockedReason`, a finished sentence naming the game whose being open defers the swap, because
restarting then **succeeds and changes nothing**, which is the worst thing a button can do.
Game Mode's dead-end *"Nothing to do."* is replaced by an instruction probed for this device: the
Desktop-mode switch only cycles the unit when lingering is **off**, and the KB recommends
`enable-linger` in three places. `run-linux-tests` 208 → **216/216**; the new checks were confirmed
to fail against deliberately broken builds.

**The plugin half shipped first, as SaveLocker-Decky v0.2.1** (2026-08-16, before the agent half was
tagged). Its Update section reads `stagedVersion`, so it shows nothing until a v0.5.8 agent is
actually installed — which is why cutting it first was safe, and it gives Phase 5's never-exercised
plugin update channel something harmless to carry. **Nothing in either half has run on hardware**, and
the one genuine unknown is whether Decky's backend gets a usable `systemctl --user`.

Everything else below is released and rolled out.

**v0.5.6 (2026-08-15)** carries a **save-losing fix** found on real hardware. A tracked game with no
recorded `SteamAppId` matched nothing in the launch wrapper, so it played with **no sync at all** and
said so only to `agent.log` — two of four games on the maintainer's Deck were in that state, and a
Khazan session on 2026-08-11 was never pushed. A Proton prefix is named for the AppID that launches
into it, so the save path already carried the answer; the agent now resolves it everywhere and the
daemon records it (`b31e160`). Also in the release: the launch-option rule and API (`logs/2026-08-15_decky-plugin.md`
Phases 1–2), and `doctor` reporting whether a game's launch options were ever set.

**Phase 5 (2026-08-15, this session)** closes the plugin's update story. Decky holds exactly one
custom-store URL and it *replaces* the official store while set, so nobody leaves it there and in
practice nobody was ever prompted about a plugin update. The agent does it instead, through the
channel it already uses for its own updates: the server hosts the plugin zip in a **third installer
slot** (`?platform=decky-plugin`, from the plugin's own repo — the slot carries its own GitHub repo)
and the Linux agent replaces the files under `~/homebrew/plugins/SaveLocker`, which Decky hot-reloads
within a second. It refuses rather than half-installs: every destination is proven writable before a
byte is written, because the plugin directory is root-owned 755 and `plugin.json` is root's outright.
`run-linux-tests` 161 → **197/197**. **It has not run on hardware** — see Next action.

**The Decky plugin is done and working on hardware** — Phases 1–4. It sets Steam launch options
(which the agent *cannot*: Steam rewrites its own config on exit), and its Quick Access panel shows
lease warnings, sync status, push/pull per game or all, and `doctor` on demand. Phases 1–2 are in the
agent and covered by `run-linux-tests`; 3–4 are the plugin and were verified on the Deck.

**Proven on hardware by the v0.5.7 rollout (2026-08-15) — two things that had never been seen.**
The Deck **staged and applied an update entirely by itself**: it noticed v0.5.7, downloaded and
verified it, and installed it at the next start with nobody typing anything. Everything the feature
needed had been in place since v0.5.5 and every piece was proven separately, but the unattended run
had never been watched, and it was the standing caveat in v0.5.7's release notes. The **Game Mode
"update is ready" notice** (`Ui/UiApp.cs`) was seen for the first time in the same pass. The trigger
was a plain **reboot** — chosen over Desktop mode precisely because it needs no terminal, which is
the argument `logs/2026-08-15_install-update-now.md` is built on.

Still unseen on hardware: the **agent UI's Updates panel**, everything about the plugin updating
itself (nothing has been uploaded to the decky-plugin row yet), and all of "Install update now" —
in particular **whether Decky's backend has a usable `systemctl --user`**, which is the one genuine
unknown in that feature and the thing to watch on the first press.

Everything shipped before this: `logs/sessions.md` (reverse-chronological) and
`logs/shipped-2026-07.md`.

## Next action

1. **Prove Decky Phase 5 on the Deck — unblocked now that the v0.5.8+ rollout is done.** The harness
   covers the agent's half with a fake plugin directory; what it cannot cover is the part that makes
   the feature work at all — Decky noticing the files change and reloading. That mechanism *was*
   observed by hand during Phase 4 (`touch` as the desktop user, and repeated `scp`s of real builds),
   so this is not a guess, but the agent doing it by itself has never happened on hardware, and
   neither has the server hosting a real plugin zip. **Upload SaveLocker-Decky v0.2.1's
   `SaveLocker.zip` in Config → Agent updates → Decky plugin** (type the version — Decky's artifact is
   always `SaveLocker.zip`) and watch the Deck pick it up. The Deck is on plugin v0.2.0, so it is
   genuinely behind. A manual reinstall through Decky always works, and is what the refusal path tells
   the user to do. Details and the two deviations from the plan: `logs/2026-08-15_decky-plugin.md`.
2. **Check the live server for duplicate games.** Canonical naming stops *new* splits; it does not
   merge what a server already holds under two spellings, and there is no merge tool — scoped
   alongside a related but distinct agent-side gap in [[Backlog]] → *One game, several real sources*.
3. **Give the six duplicate-shortcut names found during the MoonDeck investigation a look** — worth a
   heads-up to the maintainer directly (HITMAN 3, Minit, Moving Out, Animal Crossing, Metal Gear,
   Waydroid on the maintainer's own Deck), independent of any code change; the code fix for the
   related dedupe tie-break already shipped (`logs/shipped-2026-08.md`).

Everything else — the WA-03 second-account ACL test, the remaining Windows manual gates, the LAN
enrollment-URL check, and the rest of *One game, several real sources* (steps 2–4) — is prioritised
in [[Backlog]].
`claude/steam-deck-save-detection-68b6c5` and `claude/wine-proton-case-insensitive-paths-815ba3` are
both merged to `main` and shipped in v0.5.10 — see `logs/shipped-2026-08.md`.

## Handoff: working on the Decky plugin

Read `logs/2026-08-15_decky-plugin.md` and [[Gotchas]] → *Decky plugin* first. Both carry
things that were measured on hardware and cannot be re-derived by reading code.

**The Deck.** `deck@192.168.0.103` (moved since this was last written — a DHCP-leased IP drifts; run
`.\tests\testenv.ps1 deck-config` with no args for whatever is current), key auth is set up (see the
maintainer — the key is deliberately not named here). It is awake only when the maintainer wakes it.
Useful state on it:
- agent **v0.5.6 (the released build**, checksum-verified against the published `SHA256SUMS-linux.txt`),
  service `savelocker.service` active, 4 tracked games, all with launch options confirmed;
- plugin at `~/homebrew/plugins/SaveLocker`, and a config backup at
  `~/savelocker-decky-backup-20260815-112213` (`config.json`, `shortcuts.vdf`, `localconfig.vdf`);
- `~/savelocker-plugin-stage/` is the staging directory for anything needing `sudo`.

**How to iterate on the plugin without sudo** — this is also exactly the mechanism Phase 5 automates:
`scp` a new `dist/index.js` (and `main.py`) straight into `~/homebrew/plugins/SaveLocker/`, and Decky
hot-reloads within a second. Works because a non-`_root` plugin's files are chowned to the desktop
user and the plugin ships the `debug` flag. **`plugin.json` is root-owned and needs `sudo`.**

**Debugging the plugin frontend.** It logs nowhere on disk. Tunnel CEF —
`ssh -N -L 8080:127.0.0.1:8080 deck@…` — then `curl http://localhost:8080/json` for targets and drive
CDP over the websocket. **Discard the console buffer before measuring**: `Runtime.enable` replays
history, which already caused one wrong conclusion. Log state at *render* time, not in handlers.

**Plugin releases.** Tag the plugin repo; its workflow builds the zip, publishes it, and regenerates
`store/plugins` with the artifact's SHA-256 — Decky verifies that hash, so index and artifact must
come from the same job. Bump `package.json` too: Decky reads the installed version from it.

## Offline backoff — no longer only on a branch (2026-10-18)

The task parked on `offline-backoff-task` (`bee3116`) since 2026-07-29 now lives on `main` as
`tasks/offline-backoff/summary.md` ([[Backlog]] → Medium), with the 2026-10-18 competitor research
added; the branch can be deleted. That review (`logs/2026-10-18_competitor-gap-analysis.md`) also
added fourteen other items to [[Backlog]], two of them possible data-safety bugs at the top of High
(an emptied save folder pushed as a new version; a pull deleting excluded files) — both since
**confirmed by a failing test and fixed** (next section).

An agent that cannot reach the server retries forever at a fixed cadence: the offline drainer every
30 s, *re-archiving the save folder each time before it finds out*, and the command poller every 20 s
carrying the reconcile, the command fetch and the heartbeat — roughly five failed requests a minute,
indefinitely. On a Deck away from home that is battery and metered traffic spent on nothing.
**Backing off only the drainer does not achieve the goal**, which is why the task covers all three
loops.

## Save safety: an emptied folder is held, a pull keeps excluded files (2026-10-20)

Branch `save-safety-empty-hold-and-excludes` (worktree `.claude/worktrees/save-safety`, off `main` at
`4c9b636`), **not merged, no PR yet**; plus SaveLocker-Decky branch `empty-save-hold` (`ae4ab9f`, its
worktree `.claude/worktrees/empty-save-hold` there). Both bugs from the 2026-10-18 review were real:
- **Excluded files** (`a0b6472`): every pull deleted this machine's excluded files (`*.bak` is a
  default exclude). `RestoreArchive` now takes the excludes and neither deletes nor writes a match.
  `run-hardening-tests` §10: 5 fail on `main`, 45/45 fixed. [[Decisions]] has the rule.
- **Emptied folder** (`36c86d8`): an emptied folder was pushed and **wiped the other machine's saves**
  (`run-health-tests` §9: 10 fail on `main`, 51/51 fixed). The game is now held — no push, no pull —
  until Delete everywhere / Restore here (agent UI, `doctor`, notification, CLI `emptied-hold`, console
  bell's Restore there, Decky), and the server refuses an unforced empty archive over a head with files.
  Detail and the maintainer's choices: `logs/2026-10-20_empty-save-hold/summary.md`.

**Gotcha found:** running several PowerShell suites back to back *while other dotnet/npm work runs*
gave false failures (hardening §9, delta-upload 4 of 12 with a 404). Each passed alone. Run suites
on a quiet box before trusting a red one.

**Not done:** the testenv pass (the browser check used a hand-started scratch tray — against the
standing "manual tests through testenv" rule; redo it with testenv), the Decky plugin on a real
Deck, `run-linux-tests`, Game Mode (by choice). `openapi.json` unchanged on purpose: no server route
or DTO shape changed, only a 409 the routes did not declare. **Next:** testenv pass, then PRs for
both repos.

## Linux update crash safety (PR #87, branch `linux-update-crash-safety`, 2026-10-25)

A Deck that crashed ~30 s after the v0.7.0 self-update came back with every updated file at 0 bytes
and no rollback copy ([[Gotchas]] → Linux agent, "An update's files came back empty"). The updater
now fsyncs everything before `applied.json`; `installing.json` records an in-progress install
(including which files it adds) so a cut-off one resumes safely; `savelocker-preflight.sh` (plain
sh, the unit's `ExecStartPre`) puts `previous/` back when the installed agent cannot start, but only
while an update is pending. The daemon refreshes a stale installed unit. **Not on hardware yet**;
protection starts with the update *after* the one that installs it. `run-linux-tests` covers it.

## TLS pin: renewals behind Cloudflare stop warning (2026-10-22)

Found on the maintainer's Deck: "TLS identity has CHANGED" since 2026-10-13 with no certificate
touched — Cloudflare's Universal SSL renewal rotated the edge key the agent had pinned. The agent now
records how the pinned cert was vouched for (`ServerPinTrust`) and re-pins quietly when a new key is
CA-valid for the host *and* carries embedded CT SCTs; a private cert's new key, a public server
presenting an unlogged cert, or a failing chain still warns, with a reason line. Existing agents fix
themselves on the next connection. Rule and rejected options: [[Decisions]] (TLS bullet, amendment);
[[Gotchas]] → Hosting / network. Tests: `ServerTrustTests` (xUnit, 16) and
`run-enrollment-tls-tests` (now 9). **Next:** watch the Deck's agent log for the one-line "certificate
was renewed … pin updated" after it updates.

---

## Playthroughs, version history and storage: planned and designed (2026-10-25)

Three linked tasks, built in this order: [[tasks/save-playthroughs/plan|Playthroughs]],
[[tasks/version-history-upgrades/plan|Version history upgrades]] (its trash phase first),
[[tasks/storage-dedup-offsite/plan|Storage: off-site copies and deduplication]]. Every design question was
settled on mockups of the real agent and console (`agent-ui/mockup-features/`, `web/mockup-features/`), and each
plan's *Design (decided)* section lists the picks; all of them together: <https://claude.ai/artifact/R3BrsWpzSicoRt4xqqVDtJ>.
Nothing is built yet. **Next action:** Playthroughs Group 1 per its `implementation-grouping.md`, and version
history's Group 1 (trash) alongside it.

---

## eMudhra distrust: Node and ca-certificates pinned (2026-10-25, branch `emudhra-trust-pin`)

Ported from upstream (SkorcherX `8199c6b`): the console image builds on `node:22.23.4-alpine`, every CI/release
`setup-node` step pins `22.23.4`, and the runtime layer force-upgrades `ca-certificates` so a rebuild picks up
distrusted roots. **Next:** redeploy the console on unRAID once merged; a cached image keeps the old trust store.

## Where to look

| Topic | File |
|-------|------|
| Codebase map | [[REPO_MAP]] |
| System design | [[Architecture]] |
| Locked decisions (don't re-litigate) | [[Decisions]] |
| Recurring traps — **read before any build, test or path work** | [[Gotchas]] |
| Dev build/run, deploy, test suites + baselines | [[Build and Run]] |
| REST endpoints | [[API Reference]] |
| Agent CLI | `web/src/help/cli-reference.md` (KB article) |
| Prioritised open work | [[Backlog]] |
| Shipped release notes | `web/src/releases/*.md` (the console page *and* the GitHub Release body) |
| Session history | `logs/sessions.md` |
| Write-ups | `logs/2026-08-14_steam-cloud-from-manifest.md` · `logs/2026-08-09_heroic-detection.md` · `logs/2026-07-29_winagent-bugbounty.md` · `logs/2026-07-29_linuxagent-bugbounty.md` · `logs/2026-07-27_console-bugbounty.md` |
