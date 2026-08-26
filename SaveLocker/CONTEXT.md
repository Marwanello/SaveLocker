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
enrollment-URL check, the missing LA-04/05/06/07 regression tests, the `WA-01`/113-of-114 baseline
drift, and the rest of *One game, several real sources* (steps 2–4) — is prioritised in [[Backlog]].
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
