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
either a >200 MB fixture or a second server started with a tiny cap; neither was worth it, but it is
the one fix here resting on inspection rather than a test.

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
`tasks/conflict-resolution-ui/plan.md`).** Server + Agent.Core only; no UI, no Decky/Playnite
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
settled in `tasks/conflict-resolution-ui/plan.md`).** The Force push/pull bookkeeping fix,
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
file — design already settled in `tasks/conflict-resolution-ui/plan.md`).** The dashboard
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
execution grouping in `tasks/conflict-resolution-ui/implementation-grouping.md`, chosen there as "the
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
`ConflictCard.tsx` was rewritten line-for-line against `tasks/conflict-resolution-ui/`'s published
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

## Parked on a branch — `offline-backoff-task` (`bee3116`, pushed, not merged)

`main` carries **no** task file now. Do not conclude there is no open work. `tasks/OfflineBackoff.md`
and its Backlog entry exist only on that branch, deliberately (2026-07-29) — the task was written and
the work deferred. `git checkout offline-backoff-task` to pick it up.

An agent that cannot reach the server retries forever at a fixed cadence: the offline drainer every
30 s, *re-archiving the save folder each time before it finds out*, and the command poller every 20 s
carrying the reconcile, the command fetch and the heartbeat — roughly five failed requests a minute,
indefinitely. On a Deck away from home that is battery and metered traffic spent on nothing.
**Backing off only the drainer does not achieve the goal**, which is why the task covers all three
loops.

---

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
