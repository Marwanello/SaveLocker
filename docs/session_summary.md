# Session summary — 2026-10-14 — Enrollment join links replace the file (option B, PR #76)

Standalone: the facts below do not need the rest of the vault.

**PR:** [#76 on `Marwanello/SaveLocker`](https://github.com/Marwanello/SaveLocker/pull/76), *Enrollment: join links replace the enrollment file (option B)*. Head branch `enrollment-join-link` (worktree `.claude/worktrees/enrollment-mockups`), base `main`. Commits: `9ab48b7` (the mockups, on `enrollment-mockups`), `3239b1c` (the feature), `728538f` (CONTEXT handoff). Open at the time of writing.

## What was asked

1. "I don't like the enrollment file process" — suggest three ways to enroll a device, focused on ease of use.
2. Create three clickable walkthrough mockups using real console and agent-UI elements.
3. Start implementing option B (picked by the maintainer, who also answered five clarifying questions).
4. A step-by-step `testenv` verification plan for the things that need manual checking, plus a detailed technical and plain-terms explanation.
5. Open a PR.
6. Append this summary to `progress.md` and save a standalone copy here.

## Decisions taken with the maintainer

- **Replace the file entirely** — no coexistence; `savelocker enroll --file` and the JSON download are removed.
- **Short typable codes** (8 Crockford base32 characters) rather than the old 256-bit token, because the point is ease — which makes a server-side throttle mandatory, not optional.
- **Windows: paste once.** The pairing page's download button copies the link; the installer's Enroll page accepts a pasted link; `/SILENT /ENROLL-URL=<link>` for scripts; the agent CLI gets `enroll --url`.
- **QR rendered client-side** with `uqr` (zero transitive dependencies).
- Mockups committed first on `enrollment-mockups`; implementation on a new `enrollment-join-link` branch.

## What was built

Console → Configuration → Enroll a machine → **Create join link**: the code (shown once), the link, a QR, copy buttons, and the pre-existing Valid/Used/Expired lifecycle table. Opening the link on the new machine gives the pairing page:

| Tab | What it does |
|---|---|
| Windows | Downloads the hosted installer *and* copies the link; the wizard's Enroll page is Ctrl+V |
| Linux / Steam Deck | `curl -fsSL <link>/install.sh \| sh` — downloads, installs, enrolls |
| Already installed | `127.0.0.1:5178/#enroll:<link>` hand-off; the agent's Settings Join card arrives pre-filled |

- **Server:** `Shared/JoinCodes` (mint/normalise/format); `EnrollmentService` mints codes, `LookupAsync` (Valid/Used/Expired/Unknown), and `BuildPolicyAsync` — the policy is now built **at delivery**, so its game list describes the server at the moment the link is opened; new public routes `GET /join/{code}` (self-contained pairing page, inline CSS, HTML-encoded), `/policy`, `/install.sh`, `/setup.exe` (302 to the hosted installer); `JoinThrottle` (15 failures / 15 min / client + a global backstop, only *unknown* codes counted, a successful redeem resets) answering 429 + `Retry-After`; admin mint now returns `{ id, code, joinUrl, expiresAt }`.
- **Agent:** `AgentEnroller` (fetch policy → apply: redeem, TOFU pin, game pre-seeding) is the one implementation shared by the CLI (`enroll --url`, or `--code` + `--server`) and the daemon (`POST /api/join`, token-guarded, identity rolled back on failure, 400 with the server's own sentence); `agent-ui` gained the `#enroll:<link>` route and the `JoinCard`.
- **Console:** the enroll card rewritten; a clipboard helper that works on a plain-http LAN (`navigator.clipboard` is undefined there — the old call silently copied nothing).
- **Installer:** the Enroll page takes a pasted join link (validated, server + code preview, trust note); `/SILENT /ENROLL-URL`.
- **Docs/tests:** README, `installing-the-agent`, `cli-reference`, `troubleshooting`, `docs/API Reference`, a `Decisions.md` amendment; `run-enrollment-tests.ps1` rewritten (25 checks), TLS test updated, local-api +3 checks, bug-bounty CS-06/CS-10 updated, console-security CFG-01 updated; `openapi.json` and both `api-types.ts` regenerated from live scratch instances.

## Verification

- `run-enrollment-tests` **25/25** (pairing-page states, both enroll forms, single-use / revoked / forged, throttle against its own low-limit server)
- `run-local-api-tests` **128/128** (run on `SL_LOCALAPI_PORT=5197` — another worktree's tray held :5188; +3 `/api/join` checks)
- `run-server-bugbounty-tests` **216/216**; `run-agent-tests` 47/47; `dotnet test` **552/552**; `run-appearance-consistency-tests` 50/50
- `web` and `agent-ui` build + lint clean; installer compiles (`SaveLocker-Agent-Setup-0.7.1.exe`); the pairing page inspected in headless Edge (valid + invalid states)

## Not verified (manual pass owed)

- The Windows wizard paste round-trip and `/SILENT /ENROLL-URL` on a real install.
- A Steam Deck running the pairing page's one-liner (upload the new linux-x64 package first — the released agent predates `enroll --url`).
- The `#enroll:` hand-off clicked inside a real WebView2 window.
- `run-enrollment-tls-tests` — blocked on this box: the dev cert exists but is untrusted.
- Scanning the QR with an actual phone.

## Process notes worth keeping

- For a while the headless tooling's file edits landed in the **main checkout** instead of the worktree (the default cwd). Recovered with a stash-move; the standing rule: drive a worktree's builds and tests with that worktree's absolute paths.
- C# raw string literals (`"""..."""`) do not process `\u` escapes — the pairing page briefly shipped `Configuration \u2192 …` as literal text until a screenshot caught it. The same class of bug already existed once in a plain-HTML mockup file; check both.
- The QR's colours tripped `run-appearance-consistency-tests` (hex literals are banned in views); fixed with `currentColor` + keyword `black` on a forced-white tile.
- Another worktree's test tray held `:5188`; `SL_LOCALAPI_PORT` moves the suite. The `testenv` rig is shared — don't `clean`/`up` while another session uses it.

## Files

`src/Shared/{Contracts,JoinCodes}.cs`, `src/Server/{Program,AdminPasswordFilter}.cs`, `src/Server/Services/{EnrollmentService,JoinThrottle,JoinPages,Tokens}.cs`, `src/Agent.Core/{AgentEnroller,AgentCli,ApiClient,AgentApiServer}.cs`, `src/Agent.Linux/{Program,Doctor,Ui/UiApp}.cs`, `agent-ui/src/{route.ts,App.tsx,api.ts,components/{JoinCard,SettingsView}.tsx}`, `web/src/{api.ts,components/ConfigView.tsx,help/*.md}`, `installer/SaveLocker.iss`, `packaging/linux/install.sh`, `README.md`, `docs/{API Reference,Decisions,CONTEXT}.md`, `tests/{run-enrollment-tests,run-enrollment-tls-tests,run-local-api-tests,run-server-bugbounty-tests,run-console-security-tests}.ps1`, `docs/tasks/enrollment-options/` (the mockups), this entry.

---

# Session summary — 2026-10-14 — Duplicate-shortcut warnings shipped (PR #75); Group 9 checklist closed; setup-guide mockups

Standalone: the facts below do not need the rest of the vault.

**PR:** [#75 on `Marwanello/SaveLocker`](https://github.com/Marwanello/SaveLocker/pull/75) — *Duplicate shortcut warnings: which AppID will be tracked, details popover in Add Games, Game Mode caption*. Head branch `duplicate-shortcut-warnings` (worktree `.claude/worktrees/backlog-plans-testenv-mockup`, rebased onto `main` `3150f05`), base `main`. 13 commits; open and MERGEABLE at the time of writing.

## What was asked

1. Work the backlog: plan the next items and mock them up before building (interactive setup guide; duplicate-shortcut warnings), and finish the pending Group 9 (`checkpoint-ui-group-9-check`) testenv checklist.
2. Build the approved duplicate-shortcut warning — a chip whose popover names each same-named Steam shortcut's own name/AppID and which one the scan follows — plus a Game Mode caption.
3. Open a PR on the `Marwanello` fork.
4. Explain/fix the `testenv sync` "unable to unlink … Permission denied" warnings.
5. Drop the `decky-qam-scrolling` task ("not needed").
6. Append this summary to `progress.md`, save it here, push.

## What was done

**Group 9 checklist closed.** `checkpoint-ui-group-9-check` was run end-to-end through `testenv` (the maintainer ran the human-in-the-loop steps 4.6 and 1.8). Eight findings (F1–F8), each a maintainer decision, none a blocker: keep-count can't return to 7 once changed, revoking an enrollment removes its row, `-test` builds classify as releases so the Dev-build/Running chips don't show, and *Fetch from GitHub* defaults to the upstream repo. They are recorded in `tasks/_done/checkpoint-ui-group-9-check/run-2026-10-14.md`; task moved to `tasks/_done/`, Backlog updated, rig cleaned.

**Duplicate-shortcut warnings (the PR).** One detector — `SteamShortcuts.DuplicateNameGroups`/`DuplicatesFor` — now backs the doctor note and both scanners; `ScanCandidate.DuplicateShortcuts` rides the agent's wire (`CandidateDto`, additive; `agent-ui/src/api-types.ts` regenerated); agent-ui gained `DuplicateChip` (a ReleaseChip-style popover naming each same-named shortcut's own name/AppID and which one the scan follows) + a row note, and Game Mode a warning tile + ASCII caption. `Tracked` marks the AppID the scan actually follows, and no entry is marked when it follows none of them (a Windows save-root candidate that won the merge, or a MoonDeck shortcut reclassified as a genuinely installed game) — the copy says so rather than naming a loser. Same-day review, every finding fixed on the branch (`14c856c`, `edd2190`): chip and popover share a new `usePopover` (hover/pin/outside/Esc, 150 ms leave grace for the 8 px gap; the popover swallows clicks — an Add-games row is a `<label>`, so a click used to tick the game's checkbox) and `.sl-card:has(.sl-release__pop)` stops the card clipping the popover; `DuplicateShortcutTests` added (+7).

**Setup-guide mockups, not built.** `tasks/interactive-setup-guide/mockup.html` (option 1: a full-page checklist) and `mockup-options.html` — three walkthroughs on one shared state machine (console wizard / console↔agent handoff / guided tour over both surfaces), built from the real console and agent-ui markup. The options file is Playwright-verified 21/21 with zero page errors. No option picked yet.

**Decky QAM task dropped** (`c9a767b`). The plan's own premise check had already shown `doctor` left the QAM (moved to the plugin's full-screen page); the remaining "steppy" block is the sync result, which lives in the plugin repo — the maintainer judged it not needed. Task folder and Backlog entry removed.

## `testenv sync` "Permission denied" — not a script bug

`sync`'s fetch+checkout warned `unable to unlink '<path>': Permission denied` for two files under `docs/tasks/_done/checkpoint-ui-group-9-check/`: the folder was **root-owned**, left by an earlier session running git in the WSL clone as root, while `sync` runs as the normal user (`Invoke-Wsl` passes no `-u root`; testenv.ps1:911). Git treats it as a warning and carries on, leaving the files as untracked strays. Fixed with `chown -R maro:maro ~/SaveLocker` (which also caught a root-owned `.git/index`) and removing the strays. Prevention: run git in the clone as the user, not root; `chown` after any root session.

## Verification (final tree)

- `dotnet test` **559/559** (+7 `DuplicateShortcutTests`; the tracked marker mutation-checked).
- `run-linux-tests` **282/2** as root in `unshare -n` — the 2 are the documented WSLg-session pair; the duplicate-shortcut checks pass, asserting exact AppIDs/names/tracked flags. (The 71-failure cascade earlier in the session was this box's stray `~/SaveLocker` daemon holding :5187; the netns run is the workaround.)
- `run-appearance-consistency-tests` **50/50**; Agent.Core / Windows Agent / Agent.Linux build clean; `agent-ui` build + lint clean (two pre-existing warnings).
- Both UI fixes proven in headless Chrome against the real CSS/DOM (the card clip and the label-tick each reproduced with their fix removed, including a real trusted click).
- **Not verified:** a live Deck/WebView2 pass of the chip.

## Not done / caveats

- The duplicate-shortcut task moves to `tasks/_done/` on merge.
- The setup-guide mockups wait on the maintainer's pick before any implementation.
- The box's router DNS (192.168.1.1) failed intermittently all session: `git push` needed `-c http.curloptResolve=github.com:443:<ip>` at times, and the PR body refresh (dropping the removed QAM plan, updating the stale test numbers) had to be retried until the network returned — it went through at session end.

## Files

`src/Agent.Core/{SteamShortcuts,ScanCandidate,AgentApiServer}.cs`, `src/Agent/GameScanner.cs`, `src/Agent.Linux/{Doctor,LinuxGameScanner}.cs`, `src/Agent.Linux/Ui/UiApp.cs`, `agent-ui/src/components/{DuplicateChip,AddGamesView,ReleaseChip}.tsx`, `agent-ui/src/{usePopover.ts,ui.css,api-types.ts}`, `tests/SaveLocker.Agent.Tests/DuplicateShortcutTests.cs`, `tests/linux/{run-linux-tests.sh,make-fixtures.py}`, `docs/tasks/duplicate-shortcut-warnings/`, `docs/tasks/interactive-setup-guide/`, `docs/tasks/_done/checkpoint-ui-group-9-check/`, `docs/tasks/decky-qam-scrolling/` (removed), `docs/{Backlog,CONTEXT}.md`, this entry, `progress.md`.

---

# Session summary — 2026-10-14 — Manifest path constraints + scheduled refresh, console-controlled (PR #74)

Standalone: the facts below do not need the rest of the vault.

**PR:** #74 on `Marwanello/SaveLocker`, *Constrain manifest paths + scheduled manifest refresh,
console-controlled*. Branch `manifest-path-constraints` (worktree
`.claude/worktrees/manifest-path-constraints`), base `main`, six commits. Open at the time of writing.

## What was asked

1. Explain the one-line Backlog item *Constrain manifest paths*.
2. Create a full implementation plan — the first cut **pinned** the manifest to an approved revision.
3. After pushback: revise so the manifest stays a **live feed** (no pin), what an entry may resolve
   to is **constrained**, the feed actually **refreshes on a schedule** (which it never did), and the
   window is **editable in the console** with a "Refresh now" button.
4. Implement the revised plan in a worktree, open a PR, append the summary to `progress.md`, save
   this standalone summary.

## Decisions taken with the maintainer

- **No pin.** The manifest is a reliable, well-maintained live feed (updated several times a day); a
  pin makes every game released after it undetectable until an app update, and its commits are
  unsigned, so a fresh copy has nothing to verify against. Safety comes from constraining resolved
  paths instead.
- **Refresh window default 3 days** (`0` = never), editable in Configuration → Defaults &
  maintenance, fleet-wide, pushed on the heartbeat (the appearance pattern).
- **"Refresh now" is a heartbeat timestamp**, not a command: each agent acts once when it is newer
  than the request it last handled (persisted, so a restart does not re-run it). No per-machine
  feedback; the console shows "Last requested … ago".
- **Strict `..` rejection** (even a traversal that would stay inside a root), plus canonicalised
  containment and per-segment link resolution under link-resolved roots.

## What shipped

**Containment** (`src/Shared/PathResolver.cs`, new `src/Shared/RealPath.cs`). A template must be an
absolute path with no `..`; the expanded path is canonicalised before the token-root prefix check (the
old check compared the RAW string, so `<winAppData>/../../../x` passed); the result must survive
`WithinRealRoots` — every symlink/junction component resolved (OneDrive-style non-link reparse points
stay ordinary), contained under a link-resolved resolver root. Applied at `ResolveConcrete`, the one
choke point every caller funnels through. Refusal = "no existing save directory" (the user picks).
User-mapped folders never expand templates and are untouched; `SavePathGuard` stays the trusted-user
floor.

**Refresh engine** (`ManifestLoader`, `Detection`, `AgentConfig`). `DownloadAsync`: 64 MB cap, parse +
≥ 5,000 games (`MinPlausibleGames`), temp-file atomic cache swap — a failed or implausible download
never replaces a good cache. `Detection.RefreshManifestIfDueAsync`: refreshes past the window, keeps
the old copy on failure, 1 h/30 s attempt gaps, never throws; driven from `CommandPoller` on both
hosts. Before this the agent never refreshed at all — the cache was served forever until someone ran
`refresh-manifest` by hand.

**Console control** (server + dashboard). Audited admin routes `POST /api/settings/manifest`
(0–365 days, 400 otherwise) and `POST /api/settings/manifest/refresh`;
`AgentHeartbeatResponse` + `ServerSettingsDto` carry the window and requested-at (append-only,
version-skew-safe like appearance); Configuration → Defaults & maintenance gained the **Manifest
refresh** row: window select (daily / 3 days / weekly / 14 days / monthly / never) + **Refresh now**.

## Verified

- xunit **571/571** (+19: `ManifestPathConstraintTests` 11 — traversal, hostile YAML entry, leaf and
  chained link escapes, `<storeUserId>` child link, controls; `ManifestRefreshTests` 8 — floor, size
  cap, stale/fresh/zero/forced refresh, failure keeps the cache, request bookkeeping).
- `run-hardening-tests` **37/37** on Windows (+4; section 5: a real traversal entry and a real junction
  escape inside a fake prefix, clean-sibling control, escape target asserted untouched).
- `run-agent-tests` **49/49** against a fresh scratch server (+2).
- **Mutation-checked:** removing the guards flips 4 + 3 unit checks; a live mutation pass through the
  real agent makes both new hardening checks FAIL against the pre-fix resolver. That pass caught a
  vacuous fixture (the traversal target sat one directory above where the `..`s actually land) —
  fixed and re-proven.
- Live wire check: `POST` window → `GET /api/settings` → heartbeat response carries
  `manifestRefreshDays`/`manifestRefreshRequestedAt`; `9999` refused with 400.
- Builds clean (Server, Windows agent, `Agent.Linux`); `web` build + lint clean; `openapi.json` +
  `web/src/api-types.ts` regenerated, additions only. All fixture configs set `ManifestRefreshDays: 0`.

## Not verified

- `run-linux-tests` (WSL) and `run-winagent-tests` (needs an interactive desktop) were not run.
- The console card was not eyeballed in a browser (it type-checks/builds; the wire was checked live).
- Console button → live-daemon download is covered by unit tests + poller wiring, not a live daemon.

## Files

`src/Shared/{RealPath.cs (new),PathResolver.cs,ManifestLoader.cs,Contracts.cs}`,
`src/Agent.Core/{Detection.cs,AgentConfig.cs,HealthReporter.cs,CommandPoller.cs}`,
`src/Server/{Program.cs,Services/SettingsService.cs,Services/HealthService.cs,openapi.json}`,
`web/src/{api.ts,api-types.ts,types.ts,components/ConfigView.tsx,help/cli-reference.md}`,
`tests/{manifest.yaml,run-agent-tests.ps1,run-hardening-tests.ps1,cross-os/crossos.ps1}`,
`tests/linux/{run-linux-tests.sh,run-linux-regression-tests.sh}`,
`tests/SaveLocker.Agent.Tests/{ManifestPathConstraintTests.cs (new),ManifestRefreshTests.cs (new),LinkByHandTests.cs,FolderSuggestionsTests.cs}`,
`docs/{Decisions,Gotchas,REPO_MAP,API Reference,Build and Run,CONTEXT,Backlog}.md`,
`docs/logs/2026-10-14_manifest-path-constraints.md`, `docs/tasks/_done/manifest-path-constraints/`.

---

# Session summary — 2026-10-12 — Emulator saves Group E and the Xenia profile/DLC design (PR #68)

Standalone: the facts below do not need the rest of the vault.

**PR:** #68 on `Marwanello/SaveLocker`, *Emulator saves Group E: Cemu, Azahar, Xenia, Xbox 360 and 3DS names, title-ID
folders*. Branch `emulator-saves-group-e`, base `main`. Open at the time of writing.

## What was asked

1. Implement the next emulator-saves group (Group D had merged as #65, so Group E: Cemu, Azahar, Xenia), with testenv
   verification steps and a technical and plain explanation, on a PR to the fork.
2. Research whether any emulator ties a save to a user.
3. Explain the IDs shown as names (`3DS 00054000`, `Wii U 10145D00`, `3DS 0016E300`).
4. Build the Xenia fixes; for the profile one, plan first: copy the whole profile (no other saves) and add a DLC option.

## Decisions taken with the maintainer

- Group E, captured from the Deck (read-only SSH) and the PC, plus EmuDeck's scripts and the emulators' source.
- A Citra save and an Azahar save of one game are **one game**.
- A **bundled title table where licensed** (libretro-database, CC BY-SA 4.0): Xbox 360 by title ID, 3DS by ROM product code.
  No Wii U source exists; the only 3DS title-ID list (3dsdb) has no licence.
- Xenia: build device saves and the named-profile layout; design the whole-profile copy and DLC before building.

## What was built

| Emulator | A game is | Scope | Name |
|---|---|---|---|
| Cemu (Wii U) | `usr/save/00050000/<low>` | `<low>/**` | Cemu's title cache, `meta.xml`, else `Wii U <ID>` |
| Azahar (3DS) | `sdmc/Nintendo 3DS/…/title/00040000/<low>/data` | `<low>/data/**` + states `<program ID>.*` | ROM product code via No-Intro, file name, else `3DS <ID>` |
| Xenia (Xbox 360) | saved games in `content/<XUID>/<TID>`, `content/<name>/<TID>` or `content/<TID>`; device saves in `content/0000000000000000` (key `device`) | `<TID>/00000001/**` + headers | title ID via Redump/No-Intro, else `Xbox 360 <ID>` |

Plus `Ncch` (3DS ROM header), the title table rows, Add games chips, help, testenv fixtures. Unit **474**; Azahar's and Xenia's
scopes mutation-checked in two-machine tests (a wider scope deletes the installed game / DLC on pull).

## What was found

- No emulator checks a save's owner on load. Cemu (`user/<account>`, first account `80000001`), RPCS3, shadPS4, Vita3K and
  Xenia (`content/<XUID>`) bind by folder only. Xenia XUIDs are random per profile; some games may store the XUID inside the
  save, so one profile copied to every machine is the safe setup.
- Captures: the Deck's three real 3DS saves and 11 ROM headers (program IDs match the folders), Cemu/Azahar AppImages with
  data on the SD card, Xenia's two layouts; the PC's real Xenia *Forza Horizon* saves. `00054000` is Super Mario 3D Land,
  `10145D00` Mario Kart 8 (fixture), `0016E300` an unidentified Deck save with no ROM.
- The Xenia profile is `content/<XUID>/FFFE07D1/00010000/<XUID>/` (encrypted `Account`, per-game `.gpd`) + header, ~210 KB.
  DLC is always under XUID 0; Forza Horizon's is 1.0 GB, over the 500 MB upload cap.

## Phase 5c — designed, waiting on the maintainer

1. **Profile first:** each Xenia profile is its own Add games entry, scoped to its XUID (`FFFE07D1/00010000/<XUID>/**` + header).
   A machine without it sees it *On the server* and pulls it; sign in once, and every game's saves then land in the same folder.
2. **DLC per game, opt-in:** its own entry (`<TID>/00000002/**` under XUID 0), added from Add games or the game page later;
   needs a larger server cap for DLC.

Open questions: profile as its own entry or a folder of each game; DLC as its own entry and whether title updates come with it;
the DLC cap and its default; the gamertag from the decrypted `Account` or the XUID only.

## Not verified

The Group E testenv pass, and the real-game checks (Group H: Cemu, Azahar, Xenia).

**Phase 5c, maintainer's calls (same day):** the profile and DLC as folders of each game, a separate 2 GB cap for DLC, the
gamertag decrypted. Two consequences recorded in the plan and raised before building: one game's pull restores the profile as
that game last pushed it (other games' newer achievements/settings lost on that machine, and `SaveFolderClaims` needs an
exception); DLC in every version makes Forza Horizon ~1 GB per version (~7 GB kept, 1 GB per pull).

---

# Session summary — 2026-10-11 — Appearance follow-up (b): "N machines following"

Standalone: the facts below do not need the rest of the vault.

**PR:** [#67 on `Marwanello/SaveLocker`](https://github.com/Marwanello/SaveLocker/pull/67), *Appearance: "N machines following" on the console (last Group 5 follow-up)*. Head branch `appearance-follow-ups`, base `main`. Commits `6959a72` (code), `f5edb6e` and the closing docs commit.

## What was asked

Implement the last Appearance follow-up left by Group 5, item (b) "N machines following", and explain how to verify it with testenv. Then push it: PR #65 was named, but on origin that is *Emulator saves Group D*, so at the maintainer's choice it got its own PR, and the task was marked done.

## What was done (plain terms)

The console's Configuration → Appearance card now says which machines use the console's look and which keep their own:
- *N machines following* with their names; offline machines are marked *(offline)*.
- *own look: …* for machines that opted out.
- *No machine following* when none do.
- *Not shared · each machine keeps its own* when pushing is off.

A machine on an older agent can't report this, so it's in neither list. The prototype's "applied 2m ago" wasn't built, because the server can't know when an agent applied a look.

## Technical changes

- `AgentHeartbeat.FollowsConsoleAppearance` (optional `bool?`), sent by `HealthReporter` from `config.FollowConsoleAppearance`.
- `AgentHealth.FollowsConsoleAppearance` column (migration `AddAgentFollowsConsoleAppearance`). The server stores what the agent sends, including null; it's returned on `AgentHealthDto`.
- `AppearanceCard.tsx` gets `health` from `ConfigView`. The chip moved from beside the switch to a line below it.
- `openapi.json` and `web/src/api-types.ts`: additions only.

## Verification

- `run-health-tests` **35/35**, including 2 new checks. Both fail when the server doesn't store the value.
- `run-appearance-consistency-tests` 46/46.
- Solution builds; `web` typecheck, lint and build are clean.
- The migration applies on a fresh database.
- **Not run:** the by-hand testenv check. The task was closed as done at the maintainer's call; the steps are in `docs/tasks/_done/appearance-follow-ups/summary.md`:
  1. `sync`, then `build` and `up` for console, Windows and Linux.
  2. The card shows *2 machines following*.
  3. Turn off *Follow the console* in the Windows agent's Settings → the card shows *1 machine following* and *own look: …*.
  4. Turn off *Push appearance to agents* → *Not shared*.
  5. `down -Only linux` → that machine shows *(offline)* after about 5 minutes.

## Process notes

- A PowerShell 5.1 `Get-Content`/`Set-Content` round trip, used during the mutation check, mangled `HealthService.cs`'s UTF-8. It was restored from git and the edits reapplied.

## Files

`src/Shared/Contracts.cs`, `src/Server/Data/Entities.cs`, `src/Server/Services/HealthService.cs`, `src/Server/Migrations/*AddAgentFollowsConsoleAppearance*`, `src/Server/openapi.json`, `src/Agent.Core/HealthReporter.cs`, `web/src/components/{AppearanceCard,ConfigView}.tsx`, `web/src/api-types.ts`, `tests/run-health-tests.ps1`, `docs/{Backlog,CONTEXT,API Reference,Build and Run}.md`, `docs/logs/shipped-2026-10.md`, `docs/tasks/_done/appearance-follow-ups/` (moved from `docs/tasks/`), `docs/tasks/checkpoint-ui/{plan,implementation,implementation-grouping}.md` (links), this entry, `session_summary.md`.

---

# Session summary — 2026-10-11/12 — Notification follow-ups: toast buttons that act (PR #66)

Standalone: the facts below do not need the rest of the vault.

**PR:** #66 on `Marwanello/SaveLocker`, *Notification follow-ups: acting toast buttons (COM activator), rig
toast identity, installer wiring, clean guard*. Head branch `notification-follow-ups` (worktree
`.claude/worktrees/notification-follow-ups`), base `main`. Commits `97041bc` (the feature), `4ddb4d2` (the
same-day `testenv` fix), plus a clean merge of `origin/main`. Open at the time of writing.

## What was asked

1. Work the Backlog item left by Group 7 (`tasks/notification-follow-ups/summary.md`): (a) toast buttons
   that **act**, (b) a real-install check, (d) the `CsWinRTIncludes` size question, (e) a `clean` that
   stops lying about state removal.
2. "Tell me how to verify using testenv."
3. Open a PR on the `Marwanello` fork.
4. Mark the task done, append the summary to `progress.md`, save a standalone summary here.

## What was done

**Buttons that act (a).** Toast actions with no `activationType` carry `retry:<gameId>` / `install:<version>`
and are delivered through a per-user **COM activator** (`src/Agent/ToastActivation.cs`): `HKCU` CLSID with
`LocalServer32 -ToastActivated`, plus `CustomActivator` under the identity key — the Community Toolkit's
shape, measured; the shortcut's `ToastActivatorCLSID` is *not* what makes a click travel. While the tray
runs the click lands in it; with the tray closed, DCOM starts the host (`ToastActivationHost.cs`), which
sets its state root from `--config` before any logging, leaves the click in `toast-action.json` and starts
the tray. A rig gets a deterministic per-port CLSID, its own `SaveLocker Test <port>` Start-menu shortcut
(written by `up`, removed by `clean`) and registry keys; `installer/SaveLocker.iss` stamps the same
activator entries (`uninsdelete*`) so a fresh install's first toast works.

**`clean` refuses to lie (e).** It verifies the state root is actually gone and throws naming the leftovers
instead of printing "removed" over them; `Get-TestTray` gained the apphost shape the toast-activation host
starts, and later (see below) any worktree's tray.

**(d) measured, not changed.** `CsWinRTIncludes` shrinks nothing: 173.1 MB publish,
`Microsoft.Windows.SDK.NET.dll` 23.73 MB, with and without it — the property is for projection *authoring*,
and `Microsoft.Windows.UI.Xaml.dll` is not in this toolchain's output at all.

**Two bugs only running found:** a rig's `LocalServer32` first omitted `--config`, so a tray-closed click
loaded the **installed** config and briefly started the installed agent (its one log line lives in
`%PROGRAMDATA%\SaveLocker\agent.log`; the `LastUpdateCheck` it refreshed was reset); and the host logged
into the installed agent's log for the same reason. Both fixed and re-verified.

**Docs (this session's ask).** `docs/tasks/_done/notification-follow-ups/{summary,verification}.md` (the
folder moved out of `Backlog.md`, which no longer lists it), a row in `logs/shipped-2026-10.md`, the
`CONTEXT.md` entry, this file. `AGENTS.md`'s handoff step now says `docs/tasks/_done/`, matching what the
Backlog states and what the last two sessions did.

## Verification (final tree, after merging `main` through #65)

- `dotnet test` **450/450** — all green; main's #63 fixed the one machine-specific
  `ArcadeSavesTests.Supermodel_nvram…` leak this branch had carried, and #65's Group D tests are in
- `run-appearance-consistency-tests` **50/50** (incl. the new installer/agent CLSID + AUMID drift checks
  and the notice-route↔`route.ts` contract)
- Installer compiles under ISCC; full agent build at its 1-warning baseline; `origin/main` merged twice —
  #62–#64 cleanly, #65 conflicting only in the two tail-of-file docs (`progress.md`,
  `session_summary.md`), both resolved by keeping both sessions' entries
- **Live through `testenv`** (a real mouse click on a real toast): **Retry now** retried a 413-rejected
  push (`pushed new version.`); **Install now** downloaded, SHA-verified and ran a stand-in package with
  `/SILENT /FORCECLOSEAPPLICATIONS /NORESTART`, the tray exiting as designed; with the tray closed, DCOM's
  host started it and the retry succeeded

## The debugging session worth remembering

The first live walks showed **"no toast appears"** twice, then an endless **401 flood** surviving `clean`
and restarts. Three findings:

- The rig only toasts when something fails — `up -Only windows` alone never pops anything. The first toast
  is §1's >1 MB write hitting the capped console (413 → `notification shown: 'Retry Game: push failed'`).
  A brand-new AUMID gains its `HKCU\…\Notifications\Settings\SaveLocker.Test.<port>` record only once
  Windows has accepted a toast — a useful "did it ever render" probe.
- The 401s came from a tray started by **another worktree** (`fix-backlog-quick-wins`) that survived this
  worktree's `clean`/`up`: `Get-TestTray` was scoped to the calling worktree's own root, so `clean`
  deleted the shared state *under* the foreign tray, the next `up`'s tray could not take :5188, and the
  stale one kept serving with its old machine key. Fixed in `4ddb4d2`: any tree build matches by its
  `\src\Agent\bin\` path (the installed agent never lives there). Recorded in `Gotchas`.
- The verification doc gained a "no toast appears" troubleshooting block covering all of it.

## Not done / caveats

- **The real installer run is still the one manual check** — `docs/tasks/_done/notification-follow-ups/verification.md`
  §5 (with §0–§4 being the testenv walkthrough).
- Two sessions driving the shared rig at once still stomp each other's state — the tray-stopping fix
  removes the stranded-tray half, not the coordination half.
- Release notes for the next tag must cover: toast buttons that act (Retry now / Install now).
