# Session summary — 2026-09-16 — "Link to SaveLocker" UX overhaul: threading fix, non-blocking dialogs, SaveLocker tag

Fixed why the Playnite plugin's "Link to SaveLocker" action produced no feedback at all (a WPF
cross-thread bug), redesigned its user-visible feedback twice in response to live user pushback — first
adding real progress/outcome dialogs, then making the progress half non-blocking again once
`ActivateGlobalProgress` turned out to be the very thing interrupting the player — and added a
`SaveLocker: Linked` Tag (plus a startup backfill) as the closest the Playnite SDK actually allows to an
"enrolled" badge, since the SDK has no per-game grid/list icon extension point at all. Nothing committed
yet.

## What was asked

1. Testing instructions for Group 4 (the Phase 12 "Link to SaveLocker" popup), then a bug report ("i
   cant see any of theis when running any game") asking whether the problem was implementation or
   instructions.
2. After testing "Conflict 2" got no notification ("tier 2 is not working"), a full feature-redesign
   request: an always-visible button that tries an automatic link on click, falls back to the manual
   popup only if that fails, and turns into a checkmark once linked — positioned top-right like the
   HowLongToBeat plugin's button.
3. "i cant find the button," then "link to savelocker doen't do anything or shwo anyfeedback."
4. A UX spec for real dialog feedback: a "linking in progress" dialog, then a "link done, offering to
   sync" dialog.
5. A follow-up reversing part of that: could the in-progress dialog run in the background without
   interrupting the player, and could an icon/checkmark show a game's enrolled status in the library
   list or on its page?
6. Whether already-linked games (from before the tag existed) would get it automatically.

## What was found and fixed

- **The button was invisible under Harmony** (the user's active theme): its `DetailsViewGameOverview.xaml`
  hardcodes a fixed allowlist of plugin-button names that doesn't include SaveLocker, so
  `GetGameViewControl` is never called at all under this theme. Added a theme-independent
  `GetGameMenuItems` right-click entry as the reliable primary surface — Playnite renders its own menu
  regardless of theme.
- **The menu action did nothing, silently.** It ran the whole link flow inside `Task.Run(...)`, but the
  fallback path opens a real WPF window, which WPF only allows on the UI thread — so it threw
  immediately, and the exception vanished inside the unobserved background task. Fixed by running the
  action as a direct `async` lambda on the UI thread instead.
- **The fix for "no feedback" (a real progress dialog via `ActivateGlobalProgress`) became the next
  bug** once tested: that dialog is Playnite's own *modal* progress overlay, so it blocked browsing
  while the automatic check ran — exactly the "interrupting" complaint that followed. Reverted the
  check phase to a plain awaited async chain with a background toast instead; only the actual outcome
  (a real decision — sync now? — or the manual picker) still shows a dialog.
- **No SDK extension point exists for a grid/list icon, for any plugin, under any theme** — confirmed by
  reflecting every method on the SDK's `Plugin` base class; `GetGameViewControl` (the button) is the
  *only* per-game visual hook, and it's details-page-only and theme-opt-in. Even Playnite's own Tag
  mechanism, which many themes render generically, turned out not to be bound anywhere in Harmony's XAML
  either (confirmed by inspection). Given the choice, the user picked adding the tag anyway — it's still
  filterable via Playnite's sidebar regardless of theme, and would render visually under most other
  themes.

## What was built

- `LinkAction.cs`, `LinkStatusButton.cs` — the shared "click, try to auto-link, fall back to the manual
  popup" flow behind both the button and the right-click menu item.
- `SyncNowAction.cs`, `ConflictResolver.cs` — the "sync now" a player can opt into after linking (same
  pre-launch-sync gate the real launch path uses, extracted so both share one conflict-resolution UI
  instead of two).
- `LinkedTag.cs` — tags a linked game `SaveLocker: Linked`, idempotently; wired into both link flows and
  into `LinkToSaveLockerWindow.Finish`.
- `SaveLockerPlugin.OnApplicationStarted` — backfills that tag across the whole library once per app
  start, so games linked before the tag existed (or matched automatically without ever using the Link
  button) don't have to wait for their own next individual launch to pick it up.

## Verification

`dotnet build -c Release` clean (0 warnings, 0 errors) after every change, reinstalled into the portable
test Playnite, and `playnite.log` confirmed a clean plugin load each time. Both root causes (the theme
allowlist, the cross-thread WPF exception) were confirmed by reading real source rather than guessed.
**Not yet click-tested live by the user** — everything above is build-verified and installed, but the
actual in-app behavior (toast, dialogs, tag, backfill) is still unconfirmed hands-on.

## Not done

- Nothing committed in `SaveLocker-Playnite` yet — all changes are uncommitted on
  `playnite-plugin-group-4`.
- The pre-existing port-5188 local-API 401 (a stuck test-agent process that couldn't be killed this
  session — "Access is denied") is still unresolved and blocks live testing of the automatic-enroll path
  specifically; the manual-popup fallback still works either way.
- `LinkStatusButton` remains unverified on any theme that might actually render it — confirmed only as a
  no-op under Harmony.

---

# Session summary — 2026-09-15 (cont'd) — WSL lease-test fix, Fullscreen theme investigation, Fullscreen-native conflict popup built

Fixed a broken WSL lease-testing script (it was reading the wrong machine's credentials entirely), then
investigated and fixed a real Fullscreen-mode theming bug the user confirmed live: the conflict-resolve
window rendered as an unstyled, non-controller-friendly white box in Fullscreen mode, because
Fullscreen's theme genuinely has no equivalent of the Desktop popup-brush keys used before. Built a
separate, Fullscreen-native resolve window instead.

## What was asked

1. A checklist correction: "nothing will wok as the main repo deon't contain the code" — the `cd`
   target was the main repo root instead of the worktree that actually has this branch.
2. "convert tis to wsl compatable" for a lease-testing PowerShell script.
3. "every thing works great execept cant test 3 as i'm leassing the game on the same device need to do
   it through wsl but i cant figure it out," plus: the Fullscreen conflict popup "looks very bad with
   no background and not controller friendly" — asked to "create a different popup for fullscreen view
   whichich looks like the decky plugin popup using the currently applied theme elements."

## What was found and fixed

- **Checklist fix**: `git worktree list` confirmed `playnite-plugin-group-3` exists only in this
  worktree; the main checkout sits on `main`. Corrected the `cd` target.
- **WSL lease script was authenticating as the wrong machine.** It read the Windows tray's own local
  loopback token instead of a server-facing credential — explaining "leasing on the same device."
  Root-caused via `tests/testenv.sh`: LinuxTest's real server API key lives natively in WSL at
  `~/savelocker-test/SaveLocker/config.json`. Also confirmed the server's `/api/games` route returns
  every game to any authenticated machine (no per-machine filter), so the whole test — list games, find
  Minit, take the lease — now runs entirely inside WSL using only LinuxTest's own key, genuinely
  exercising a different machine.
- **Fullscreen mode theming, confirmed rather than guessed**: reflected the real installed
  `Playnite.SDK.dll` to confirm `PlayniteApi.ApplicationInfo.Mode` detects Fullscreen vs. Desktop, then
  fetched Playnite's actual GitHub source for its Fullscreen theme. Confirmed `PopupBackgroundBrush`/
  `PopupBorderBrush` — the keys the Desktop resolve window relies on — simply don't exist in
  Fullscreen's theme at all (it uses `ControlBackgroundBrush`/`OverlayMenuBackgroundBrush`/
  `OverlayBrush`/`GlyphBrush` instead), and that Fullscreen merges no window-chrome style dictionary the
  way Desktop does — reproducing the original "unstyled white box" bug for an entirely new, now-
  understood reason.

## What was built

`ConflictResolveWindowFullscreen.cs` (new): a borderless, dimmed full-screen overlay with a centered,
drop-shadowed card — modeled on Decky's own floating-modal look rather than an OS dialog — using
Fullscreen's real theme keys, bigger touch targets, and explicit Left/Right/Enter/Escape key handling
(since whether Playnite's gamepad input reaches a plugin-created secondary window at all is unverified).
`SaveLockerPlugin.cs` now branches on `ApplicationMode` to construct the right window. Build succeeded
after fixing one namespace slip (`System.Windows.Effects` → `System.Windows.Media.Effects`); installed
into the portable Playnite.

## Not done

- Neither the Fullscreen window's visual result nor controller-driven navigation has been
  hardware-verified yet — both are real open questions, not assumed working.
- The corrected WSL lease script hasn't been re-run by the user yet.
- Nothing in the `SaveLocker-Playnite` repo was committed as of this write-up.

---

# Session summary — 2026-09-15 — Playnite plugin Group 3 hardware verification: popup root cause, theme-driven resolve window, git housekeeping

Hardware-verified Group 3 (Phases 8–11) of the Playnite plugin against a portable Playnite install and
`tests/testenv.ps1`. Found and fixed the root cause of a "no conflict popup" report, then went through
several rounds of theming fixes on the conflict-resolve window in response to live screenshots, ending
in a structurally robust fix. Pushed both repos and deleted a long-standing duplicate remote branch.

## What was asked

1. Hardware-verify Group 3 against a real Playnite.
2. "the game starts and no conflict window appears," then a "REFUSED push" toast with "the conflict
   popup doesn't show up and the game opens stright away."
3. Once the popup worked: "i don't want it hardcoded. i do want it to match the theme chossen in the
   the Playnite app."
4. Three rounds of screenshot feedback on contrast/icons, ending in the explicit correction "nothing
   chnged please fix it porperly without guessing."
5. "ar changes don in playnite-plugin-group-3? if not push to this branch and delete
   claude/playnite-plugin-group-3-acd4eb."
6. After a usage-limit reset: "continue from where you left off," then a request to log this write-up.

## What was found and fixed

- **No popup, root cause:** this session's own `docs/Build and Run.md` told the user to run `conflict
  -Wsl` alone, which seeds only WSL — Windows never tracks the game, so the plugin's matcher finds
  nothing and Playnite silently falls back to a plain launch. Confirmed via `playnite.log`'s
  `"Using generic controller"` vs. `"Using plugin to start a game."` line. Fixed the doc (now
  `-Windows -Wsl`) and made `testenv.ps1` warn loudly if `-Windows` is omitted with `-Deck`/`-Wsl`, so
  this can't silently recur.
- **Hardcoded colors rejected:** rebuilt the resolve window around `IPlayniteAPI.Dialogs.CreateWindow`
  (inherits Playnite's own active theme) plus `SetResourceReference` against real theme keys
  (`TextBrush`, `PopupBackgroundBrush`, `PopupBorderBrush`) confirmed from Playnite's own GitHub source.
- **Button text unreadable through two attempts:** neither setting `Button.Foreground` nor a custom
  `ControlTemplate` with an explicit `TemplateBinding` fixed it. Diagnosed with certainty instead of
  guessing again — confirmed via Microsoft's own WPF precedence docs, discovered the portable install's
  actual active theme is a third-party "Harmony" theme (not Playnite's own "Default") whose `Button`
  template apparently ignores `TemplateBinding Foreground`, and ruled out a stale-DLL/stale-token
  confound via direct `curl` checks against the local API. The robust fix gives every button label its
  own directly-styled `TextBlock`, bypassing `ContentPresenter`/template inheritance entirely.
- **Body text (not just buttons) was also unreadable**, found and fixed after the above: Playnite's own
  `StandardWindowStyle.xaml` (which Harmony falls back to) sets `Background`/`BorderBrush` but never
  `Foreground` at the `Window` level — the same class of bug as the button fix, just not yet applied
  file-wide. A `Themed()` helper now wraps every `TextBlock` in the window. Commit `e052283`'s message
  states this was verified live against the real Harmony-themed Playnite.
- **Cloud/device icons added**, reproducing `agent-ui`'s Lucide `cloud`/`hard-drive` SVGs as stroke-based
  WPF `Path` geometry.

## Landed

- Main repo (`playnite-plugin-group-3`): `9453fda` (testenv warning), pushed.
- `SaveLocker-Playnite` (`playnite-plugin-group-3`): `4da6ace` (theming/icons), `e052283` (body-text
  fix), both pushed.
- Deleted the duplicate remote branch `claude/playnite-plugin-group-3-acd4eb` on
  `Marwanello/SaveLocker`, explicitly authorized this session.

## Not done

- The full 6-step `Build and Run.md` verification is being re-run from a clean rig rebuild — asked the
  user how much had actually been confirmed on hardware rather than trust memory across the
  usage-limit gap; they chose to re-verify all 6 steps. Not complete as of this write-up.
- `docs/CONTEXT.md` (Playnite repo) and the Group 3 row in the main repo's `implementation-grouping.md`
  remain stale, correctly deferred until the re-verification above finishes.
- `testenv.ps1 clean`'s inconsistent Windows-state wipe (leaves a stale token after a database wipe,
  producing 401s on the next `up`) is a real recurring nuisance, worked around manually each time, not
  yet fixed in code.

---

# Session summary — 2026-09-14 (cont'd 2) — Playnite plugin Group 2 shipped, GitHub org default fixed

Implemented Group 2 (Phases 4–7) of the Playnite plugin plan — exit-push toggle, three new local-API
routes, the `AgentPlatform.PlaynitePlugin` slot, and a Windows plugin self-updater — then debugged the
user's own live manual verification against it. Five commits, all unpushed.

## What was asked

1. "start implementing group 2 please and after words give me step by step manual verification using
   test env if needed."
2. Live debugging as the user ran the verification steps themselves: a 404 on `post-exit-sync`, a
   question about a `pushAfterExitEnabled` field still reading `null`, a 401 on the server's
   `/api/agent/latest` route, and a wrong GitHub org baked into the plugin's install link.

## What was built

**Phase 4 — `PushAfterExitEnabled`.** New nullable-bool override mirroring `PullBeforeLaunchEnabled`:
`null` keeps today's unconditional push-on-exit behavior, an explicit `true`/`false` always wins.
`SyncEngine.OnGameExitAsync` gates its push call on `game.PushAfterExitEnabled ?? true`.

**Phase 5 — three new local-API routes.** `POST /api/games/{id}/post-exit-sync` (single-flight,
fail-open); `POST /api/candidates/lookup` (single targeted resolve via a new `Detection` dependency,
merged into the candidate cache so its id enrolls through the existing `/api/enroll` route); `GET
/api/manifest/search?q=`.

**Phase 6 — `AgentPlatform.PlaynitePlugin` slot.** New platform end to end: `Contracts.cs`,
`AgentInstallerService.cs`'s `_slots`, `Program.cs`'s config-fallback switch, and — not anticipated by
the plan, found this session — a hand-written 4th entry in `web`'s `AgentUpdatesCard.tsx` and
`types.ts`.

**Phase 7 — Windows plugin self-updater** (`src/Agent/PlaynitePlugin.cs`, new file). Mirrors
`Agent.Linux/DeckyPlugin.cs`'s shape but simpler, since `%AppData%\Playnite\Extensions\<id>\` is
entirely user-owned. `src/Agent` doesn't reference `src/Agent.Linux`, so this defines its own local
`PluginUpdateState`/`PluginUpdateOutcome` types rather than reusing Decky's. Gates every write on
`IsPlayniteRunning`, since a loaded Playnite extension assembly is typically locked by its own host and
never hot-reloads.

## Debugging root causes

- **404 on `post-exit-sync`**: likely an empty/stale `$games` array producing a malformed URL; the user
  resolved it before the next message.
- **`pushAfterExitEnabled` reading `null` after a successful toggle**: a stale PowerShell variable, not
  a fresh re-fetch — same class of confusion as last session's `pullBeforeLaunchEnabled` question.
- **401 on `/api/agent/latest`**: my own instruction error, not a code defect — gave the local agent
  API's `X-SaveLocker-Token` header for a server route that needs the console's separate `X-Api-Key`.
- **Wrong GitHub org in the install link, user-caught**: *"the link is will be wrong. the plugin will be
  in my account not SkorcherX."* Phase 6/7 had copied the already-shipped `SkorcherX/SaveLocker-Decky`
  default onto the brand-new, not-yet-existing `SaveLocker-Playnite` repo without checking whether that
  account applies — it doesn't. Fixed both defaults to `Marwanello/SaveLocker-Playnite`; left the
  existing, already-shipped `SkorcherX` defaults for the agent and Decky plugin untouched.

## Verification

`Agent.Core`, `Server`, `Agent.Linux` built clean directly; `src/Agent` verified via scratch-output
builds since its normal `bin/` output stayed locked by the user's own live test-tray process.

## Commits (unpushed)

`738d086`, `b5933aa`, `5f6b598`, `49e1e4c`, `c7d5454` — see `progress.md` for the full per-commit
breakdown.

## Not done

- Phase 7's self-updater is code-complete but unverified against a real install — `SaveLocker-Playnite`
  doesn't exist as a repo/release yet.
- The `Co-Authored-By` trailer gap on the prior session's five commits is still unaddressed.

---

# Session summary — 2026-09-14 (cont'd) — Playnite plugin Group 1 shipped, WSL conflict fallback, testenv fixes

Implemented Group 1 (Phases 1–3) of the Playnite plugin plan end to end, debugged the user's own live
manual verification against it, designed then built a WSL fallback so `testenv.ps1 conflict` can seed a
genuine conflict without a physical Steam Deck, fixed an unrelated `testenv.ps1 clean` bug found along
the way, and established the Phase/Group status-table convention as a standing rule in `CLAUDE.md`.
Five commits, all unpushed.

## What was asked

1. "Start implmenting group 1 and afterwards (only if needed) tell me how to verify manually / And what
   has changed briefly in technical and no technical terms after implmenting this group."
2. Live debugging as the user ran the verification steps: a `401 Unauthorized`, then `pre-launch-sync`
   returning `Proceed` instead of the expected `Blocked`.
3. "i would prefer to make conflict in testenv has this functionality. if the deck is not reachable use
   wsl insted / but sugesst how it will work" — a design proposal first, not immediate code.
4. "implment this / then commit changes in meaningful commits / and add a summery table for implemented
   phses and groupd in playnite plugin task just like in conflict resolution ui. and make it the
   standard when creating plan.md and implmentation-grouping.md."
5. A pasted `testenv.ps1 clean` failure: "why does this happen please fix if you can."
6. "after those edits tell me a step by step manual verfication using testenv."
7. Two follow-ups on the real verification output: why `pullBeforeLaunchEnabled` was empty for Steam
   games and `false` for "Conflict Game"; then "shouldont pull before launch be true in all non steam
   games?"

## What was built

**Phase 1 — Windows launch-gate rewiring.** `TrayApp.cs`'s `prepareLaunch` delegate now calls
`_engine.PrepareLaunchAsync(game, ct)` instead of the old never-pull-never-block
`OnGameLaunchAsync(preLaunch: false)`, mirroring Linux's existing wiring — the first genuine pre-launch
boundary Windows has ever had, safe because nothing called this route before today.

**Phase 2 — `SteamAppId` population on Windows.** `GameScanner` now records the Steam manifest's own
`appid` on every installed-Steam-game candidate (previously read only to filter, then discarded), plus a
startup backfill (`TrayApp.BackfillSteamAppIdsAsync`) matching already-tracked games by `InstallDir`
against a fresh rescan.

**Phase 3 — `PullBeforeLaunchEnabled` moved server-side.** A new `EffectivePullBeforeLaunch(game) =>
game.PullBeforeLaunchEnabled ?? game.HasSteamCloud != true` now gates the pre-launch pull inside
`SyncEngine.PrepareLaunchAsync` itself, so every caller (Linux, Decky, and now Windows/Playnite) gets the
same "off for Steam Cloud, on otherwise, explicit override always wins" behavior from one place instead
of each frontend re-implementing it client-side.

**WSL fallback for `testenv.ps1 conflict`.** `.\tests\testenv.ps1 conflict [-Windows] [-Deck] [-Wsl]`:
`-Wsl` is an explicit second-side choice, and — per the user's literal ask — requesting `-Deck` when the
Deck turns out unreachable automatically falls back to seeding the WSL side instead, printed clearly
either way. `tests/testenv.sh` gained a `cmd_conflict()` (WSL never had one) mirroring the existing Deck
seeding shape. This is what makes a genuine, hardware-independent conflict reproducible at all — a
conflict needs two sides pushing in a specific order, and without either a real Deck or this fallback
there was no second side available in this environment.

**`testenv.ps1 clean` fix.** Root cause of a wall of `Remove-Item` "in use" errors under
`WebView2\EBWebView\...`: `Stop-Windows` killed the tray process but never WebView2's own separate
helper processes, which don't die synchronously with their parent. Fixed by also killing matching
`msedgewebview2.exe` processes before deleting, and by making the delete loop actually `throw` on
persistent failure instead of always claiming success.

**Standing docs convention.** Added a permanent `CLAUDE.md` rule: every `plan.md` gets a `## Status`
phase table, every `implementation-grouping.md` gets a matching group table, updated the same session
something ships — not a one-off, a rule for every future task.

## Debugging root causes

- **401s**: missing `X-SaveLocker-Token` header — fixed by reading the test instance's own
  `api-token` file.
- **`Proceed` instead of `Blocked`**: my own earlier instruction was incomplete — I'd said to run
  `conflict -Windows` alone, not realizing a conflict is inherently two-sided and order-dependent
  (`testenv.ps1`'s own comment: whichever side pushes second is what the server records as a genuine
  divergence). With no Deck configured, only one side had ever seeded. Acknowledged directly and fixed
  properly via the WSL fallback above rather than a one-off workaround.
- **`pullBeforeLaunchEnabled` tri-state**: confirmed to the user that `null` correctly means "no
  override, gate computes its own default" (which genuinely evaluates to "pull" for a non-Steam-Cloud
  game) — "Conflict Game" showing a literal `false` was an explicit override this session's own
  verification steps had set, not evidence the default logic was wrong. Proposed, but did not build, a
  read-only `effectivePullBeforeLaunch` DTO field so a future settings UI could show the computed value
  without re-implementing the heuristic.

## Verification

`dotnet build` after each phase; `bash -n`/PowerShell-parser checks on both testenv scripts; then a full
live walkthrough against a real testenv rig — the user's own pasted output confirmed a genuine
WSL-seeded conflict correctly returned `Blocked`, and that Phases 1–3 are functioning against real data
(resolved `steamAppId`s, a correctly-computed pull default, a real block on a real divergence).

## Commits (unpushed)

`0cb12d7`, `cca2836`, `c33bbcb`, `f87ffc8`, `5511244` — see `progress.md` for the full per-commit
breakdown.

## Not done

- The proposed `effectivePullBeforeLaunch` DTO field — not implemented, no code change requested yet.
- None of this session's five commits carry the `Co-Authored-By` trailer current attribution
  instructions require. Fixable (nothing is pushed) but not yet raised with or confirmed by the user.

---

# Session summary — 2026-09-14 — Conflict-resolution-ui closed out, Playnite plugin regrouped

Docs-only session: dropped Phase 14 from the conflict-resolution-ui plan and marked that whole task
complete, then consolidated the newly-split-out Playnite plugin task's implementation groups from 11
down to 6. No application code touched; nothing committed.

## What was asked

1. "Ditch phase 14 and mark save conflict as complete with only future user experience review may
   come."
2. "And decrease the number of implementation groups in the playnite plugin."

## What was done

**Phase 14 dropped, conflict-resolution-ui marked complete.** `tasks/conflict-resolution-ui/plan.md`'s
Status section now reads "task complete" — every phase shipped, moved to its own task (Phase 13), or
was dropped (Phase 14). Phase 14 (webhook/ntfy/email notify + a per-game "block launch until resolved"
opt-in) is marked dropped rather than deferred: its block-launch half was never reconciled against
Phase 4's already-shipped *unconditional* block-on-confirmed-conflict behavior, and neither half had
any confirmed player need behind it. Every downstream mention of Phase 14 as still-pending (the intro,
two shipped write-ups, the ASCII dependency diagram, the Scope note) was updated to match. Mirrored in
`implementation-grouping.md`'s own status header. `Backlog.md` lost the "Decky conflict resolution"
line entirely (nothing left open), and `logs/shipped-2026-09.md` gained a completion row pointing back
at the task folder for detail.

**Playnite plugin groups consolidated 11 → 6**, same phases and same total session estimates
(~18–24 sessions, per `plan.md`'s own untouched "Size estimate" section) — only the bucketing changed.
Two agent-side groups (was four): Group 1 = Phases 1–3 (launch gate, `SteamAppId`, pull-toggle move),
Group 2 = Phases 4–7 (exit-push, new routes, self-update plumbing). Four plugin-side groups (was
seven): Group 3 = Phases 8–11 (scaffold, settings, core gate, matching — bundled together, and
matching moved in specifically so the MVP cut stays a clean "Group 1 + Group 3"), Group 4 = Phase 12
(enroll/link popup), Group 5 = Phases 13–15 (status chip, self-update consumption, test infra), Group
6 = Phase 16 (add-on database submission, kept separate — a third-party review timeline is a different
kind of task from build work). `plan.md`'s "Recommended cut" section updated to cite Group 1 + Group 3
as the MVP (~7–8.5 sessions, unchanged from before — no phase moved sides of the MVP line, only the
group boundaries around it did).

## Not done

- `tasks/conflict-resolution-ui/` was not relocated to `docs/logs/` despite the project's own
  session-handoff convention for completed tasks — `tasks/playnite-plugin/plan.md` still references
  its path directly, and moving it would break those links. Flagged rather than done silently.
- Nothing committed yet.
- The "future user-experience review" now named in conflict-resolution-ui's Status section is
  intentionally unscoped and unscheduled — just a named possibility, not a task.

---

# Session summary — 2026-09-08

Five easy-verification backlog items triaged on branch `easy-wins` (dedicated worktree
off `2f3a1a9`, main checkout untouched): self-hosted console fonts, installer ACL trap, LAN
enrollment URL, cross-source doctor note, Game Mode stale list. Three code commits (`5ffd7d1`,
`bc82708`, `d6d4814`); the ACL and LAN items needed no code change. All five maintainer-verified
live, then archived out of the backlog (`docs/logs/2026-09-08_*/`, `shipped-2026-09.md`, vault
commit `6ec250e`). Unmerged, unpushed.

## The fixes

- **Fonts:** Google Fonts `@import` out of `web/src/index.css`; Inter + JetBrains Mono vendored via
  Fontsource. Zero Google requests in DevTools; blocking them renders identically.
- **Doctor:** same game in 2+ scan sources gets one NOTE line naming every origin with `[tracked]`
  on the scan's pick, plus the `add-game` switch hint. Exit code untouched; suite's dual-source
  assertions green.
- **Stale list:** `RefreshGameList` (membership-only by `GameId`) polled every 10s including the
  first frame. Add appears, delete disappears, no restart — confirmed under WSLg.

## Verification-only

- **ACL trap:** live `icacls` (SYSTEM/Administrators/self only, inheritance broken) + WA-03 suite;
  any test dir exercises the identical mechanism via `SAVELOCKER_STATE_ROOT`.
- **LAN URL:** `effective-url` loopback flags, localhost mint → HTTP 400, explicit LAN mint → 200
  with that URL in the policy.

## Debugging of note

- A `Readex Pro`/`Signika` Google Fonts request in DevTools proved unrelated (zero repo hits) but
  exposed a real leftover: `agent-ui/index.html` still uses Google Fonts — open follow-up.
- The stale-list check caught three instructive edges: renames never propagate by design
  (membership-only); a malformed `GameId` crashes startup `Load` yet is silently skipped by the
  poll, and `json.tool` VALID doesn't catch it; PowerShell strips double quotes for native
  `wsl.exe`, so WSL one-liners must be quoteless with an explicit `PATH=`.
- Suite `exit 2` is environmental (the `chattr +i` probe needs root on ext4), not a failure.

## Not done

- Follow-ups offered, none started: agent-ui font self-hosting, `GameId` robustness gap, rename
  propagation.
- Broader sub-scopes from the removed backlog lines (installer happy-path/expired/skip/silent,
  Deck scenarios, Windows gates, second-machine redeem) are named in `shipped-2026-09.md`,
  not verified.
- Branch unmerged, unpushed.

---

# Session summary — 2026-09-07/08 — Real-Deck Play Anyway/pause fix (CGameID bug), testenv `-Size`/`-Files`

Root-caused and fixed a two-part bug that silently broke the Decky "Play Anyway" popup and
pause/resume freeze for **every game**, not just the fake-game test rig — confirmed working on real
Steam Deck hardware — then implemented an unrelated feature request to seed realistically-sized
conflicting saves in `testenv.ps1 conflict`.

## What was asked

1. Fix: real-Deck testing showed no pause and no "Play Anyway" popup, despite a correctly-built
   Decky plugin (ruled out the earlier stale-checkout bug first).
2. Determine whether the bug was exclusive to the fake-game rig or could hit any game.
3. Live CDP-log-driven debugging across two rounds (a purpose-built console-tailing tool, then two
   pasted hardware traces), fixing a second symptom that appeared after the first fix.
4. Confirmed working; then requested `.\tests\testenv.ps1 conflict -size 25 -files 25` — an option to
   seed a conflicting save of a configurable size split across a configurable file count.

## What was found and fixed

- **Root cause #1:** `RegisterForGameActionStart`'s `appId` is a packed 64-bit Steam `CGameID` for
  non-Steam shortcuts (real AppID in the upper 32 bits) — proven from a real hardware log, not
  guessed. `Number(appIdStr)` silently rounded it to garbage, breaking appId matching for **every**
  game the feature targets. Fixed with a BigInt-aware `parseGameActionAppId()` in
  `SaveLocker-Decky/src/gamingSync.tsx`.
- **Root cause #2:** `SteamClient.Apps.GetActiveGameActions()` proved unreliable in both directions on
  hardware (empty ≠ cancel succeeded; non-empty ≠ cancel failed). Removed as a verification step
  entirely; the pre-existing `pendingBlock`/`bRunning` safety net in `handleLifetimeChange` is now the
  sole authority. User confirmed: "it works perfectly now."
- Built `tail-deck-console.mjs`, a dependency-free Node CDP client, to tail the Decky frontend's
  console live over an SSH tunnel — Decky frontends run in Steam's `SharedJSContext` CEF process,
  which has no on-disk log.
- Added `-Size <MB>`/`-Files <count>` to `testenv.ps1 conflict` (defaulting to today's tiny-file
  behavior), mirrored on both the Windows (`New-SyntheticSaveFiles`) and Deck (`awk`+`/dev/urandom`)
  sides, splitting a total byte count across randomly-filled files. Verified independently on both
  platforms: 25 files / 25 MB → exactly 26,214,400 bytes.

## Not done

- Temporary debug instrumentation (`dbg()` helper/call sites, a debug-only conflict-id hook) added
  during the investigation is still in `gamingSync.tsx`/`index.tsx`/`libraryOverlay.tsx` — safe to
  strip now that the fix is confirmed, but not yet removed.
- Nothing committed in either repo this segment.

---

# Session summary — 2026-09-07 (cont'd 3) — Alias fix, chip root cause, Phase 11 shipped, testenv fix

Continuing directly from the ConflictTest → Conflict Game rename below: fixed the Decky plugin's
alias editor, root-caused why the sync-status chip and Pull/Push/Sync buttons weren't appearing at
all (a deeper case of the same bug), implemented Phase 11 (launch-gate wiring) end-to-end, and fixed
a `testenv.ps1 conflict` failure on a fresh run. Committed all of it across both repos.

## What was asked

1. Fix the Decky plugin's alias editor (silently doing nothing on edit).
2. Commit that fix; also explain why the status chip and Pull/Push/Sync buttons still weren't showing
   up on "Conflict Game" even after the rename.
3. ("manual verification complete") Implement Phase 11, with real-hardware testenv verification steps.
4. Q&A on Phase 12's scope and difficulty (no code).
5. Fix `.\tests\testenv.ps1 conflict` failing on a fresh run (raw HTTP exception + "not installed").
6. Commit everything in both the SaveLocker and SaveLocker-Decky repos.

## What was found

Both bugs traced to one root cause: `TrackedGameDto`'s JSON wire fields are frozen as `id`/`path`
(documented back-compat constraint on the C# side), but every TS consumer in the Decky plugin
expected `gameId`/`saveDirectory` — making `game.gameId` `undefined` everywhere. This broke the alias
editor (sent a literal `"None"` into the URL, 404'd silently) **and**, more seriously,
`resolveMatchSync`'s primary Steam-AppID match path — so the chip/buttons never mounted for *any*
game, not just ones hitting the name-based fallback the earlier rename targeted.

Separately, `testenv.ps1 conflict` assumed the console container was already running (both sides
register/push against it over HTTP) but never started it itself, surfacing a raw
`HttpRequestException` instead of a clear error on a fresh environment.

## What was fixed

- One `_normalize_game()` helper in `SaveLocker-Decky/main.py`'s `games()` — a single choke point
  fixing both bugs at once, since both `fullPage.tsx` and `gamingSync.tsx` call the same Python
  method. User confirmed via manual hardware verification.
- **Phase 11** (cancel → popup → sync → relaunch): new `POST /api/games/{id}/pre-launch-sync` route
  exposing the existing `SyncEngine.PrepareLaunchAsync` over HTTP; Decky plugin's
  `handleGameActionStart`/`handleLifetimeChange` now call it instead of relaunching blind, opening
  the conflict-resolve modal on a `Blocked` decision and relaunching once it closes (fail-open on
  transport errors). A `ConflictHooks`/`setConflictHooks` pattern resolved a circular-import
  constraint between `gamingSync.tsx` and `conflicts.tsx`.
- `tests/testenv.ps1`'s `conflict` case now starts the console first if it isn't already up
  (idempotent), before seeding either side.

Verified via: full C# solution build, `tsc --noEmit`, Decky rollup build, the existing 30-check
local-API suite (30/30), a standalone smoke test for the new route's edge cases, and the PowerShell
parser for `testenv.ps1`. Real Deck/Steam+Decky hardware verification of Phase 11's actual
cancel→popup→sync→relaunch sequence is still outstanding — handed to the user, not assumed.

## Commits

**SaveLocker** (`claude/savelocker-decky-worktree-d43a9e`), not pushed:
- `b5988e8` pre-launch-sync route (Phase 11 server/agent side)
- `24071fc` fake-game Steam shortcut test rig + ConflictTest→Conflict Game rename (from the prior
  segment, not yet committed until now)
- `df7a923` Docs: Phase 11 shipped, Phase 10 hardware-verified

**SaveLocker-Decky** (`decky-conflict-resolution-ui`), not pushed:
- `617f9b3` Phase 11 launch-gate wiring
- (`b8af1c6`, the alias/_normalize_game fix, was already committed in the prior segment)

## Not done

- Real-hardware verification of Phase 11 on an actual Deck.
- Neither branch pushed.
- Phase 12 (`sync-status` consumer work) not started — endpoint/DTO already exist; the open question
  is picking a one-shot trigger moment, not code difficulty. Phase 11's `Blocked` result is a
  plausible home for it.

---

# Session summary — 2026-09-07 (cont'd 2) — Decky chip name mismatch fixed

The user reported the sync-status chip wasn't appearing on the "Conflict Game" library tile on their
Deck, and asked whether it was because the CLI-tracked game was named `ConflictTest` while the Steam
shortcut was named "Conflict Game" — and to make both match.

## What was found

Reading `gamingSync.tsx`'s `resolveMatchSync` (Decky plugin) confirmed the user's hypothesis against
the real code: a primary AppID-based match, with a **name-based fallback** reached only when the
primary lookup finds no row. A name mismatch breaks the fallback outright, and since SaveLocker's
CRC32-based Steam AppID algorithm was reverse-engineered rather than officially documented, whether
Steam's live shortcut AppID always matches what SaveLocker wrote is a genuinely open, unverified
question — making the fallback (and thus the rename) real insurance, not just a guess.

## What was fixed

Renamed the CLI-tracked game from `ConflictTest` to "Conflict Game" in `tests/testenv.ps1` and
`tests/testenv-deck.sh` everywhere it's created, so it matches the Steam shortcut's display name.
Self-caught and fixed a regression the blind rename introduced in both scripts: an unquoted two-word
`push Conflict Game` argument, which both PowerShell and bash would word-split into two positional
arguments — quoted it in both scripts. Verified via the PowerShell parser and `bash -n`. No other file
in the repo still referenced the old name.

## Not done

- Live re-verification on the Deck (rebuild → `up` → `conflict` → restart Steam → check for the chip)
  — handed to the user as the next step, not yet confirmed.
- Whether Steam can assign a shortcut a different AppID than SaveLocker computed remains an open
  question; the rename is a safety net for it, not a resolution.
- No commit made yet.

---

# Session summary — 2026-09-07 (cont'd) — Decky Phase 10 shipped, Playnite split, testenv fix, worktree relocated

Implemented Decky Phase 10 (conflict display + resolve UI) in a fresh worktree of the
`SaveLocker-Decky` repo, made one small necessary main-repo addition (`MachineId` on
`AgentStateDto`), split Playnite (Phase 13) out of Decky's shared grouping into its own Group 7,
diagnosed and fixed a cosmetic `testenv.ps1 sync` git error the user hit while following the manual
verification steps, and relocated the Decky worktree to the project's standard `.claude/worktrees`
location.

## What was asked

1. Implement "the next group" in the conflict-resolution-ui plan; the Decky plugin repo lives at
   `D:\Projects\SaveLocker\SaveLocker-Decky`, create a worktree/branch there if needed; flag any step
   needing manual hardware verification, with steps.
2. Explain what was implemented, give step-by-step verification via `testenv`, and move Playnite into
   its own group to implement later.
3. User ran the given verification steps, hit `fatal: not a git repository: ...` from
   `.\tests\testenv.ps1 sync`; asked why.
4. Move the Decky worktree from its ad hoc location to the standard `.claude\worktrees` path under
   the Decky repo itself.

## What was built

**Decky Phase 10** (branch `decky-conflict-resolution-ui`, commit `2f06573`): `main.py` gained 7
backend proxy methods for the local agent's conflict routes; `shared.tsx` gained the `machineId`
field and a full Conflicts type/callable section; a new `conflicts.tsx` implements the 20s poller,
chip-merge logic, and the `ConflictResolveModal` popup; `libraryOverlay.tsx`'s sync chip becomes
clickable in the conflict state; `index.tsx` gets a new QAM "Save conflicts" panel; `fullPage.tsx`
gets a per-game conflict-policy dropdown (Manual/Newest-wins/Prefer-this-device).

**Main repo** (commit `7306968`): `AgentStateDto` gained `Guid? MachineId`, sourced from
`_config.MachineId`, so Decky's "prefer this device" option has an id to send. `api-types.ts`
regenerated and diffed clean.

**Docs** (`170cc63`): `Backlog.md`, `CONTEXT.md`, `plan.md` updated with the Phase 10 write-up and a
five-point manual verification checklist.

**Playnite regrouped** (`976b863`): `implementation-grouping.md`'s old combined "Group 6 (Phase 10,
11, 13)" split into Decky-only Group 6 (Phase 10 + 11) and a new Playnite-only Group 7 (Phase 13) —
Playnite has no real dependency on the Decky work, so the original bundling was an avoidable
coupling, the same category of mistake as the earlier Phase 9 miscategorization.

## The `testenv.ps1 sync` bug

The user's `sync` run printed `fatal: not a git repository: .../D:/Projects/SaveLocker/SaveLocker/.git/worktrees/...`
despite completing correctly. Root cause: this worktree's `.git` file stores a Windows-native
`gitdir:` path (correct for Windows git); `testenv.sh`'s `cmd_sync()` runs a
`git config --global --add safe.directory` call from WSL, whose CWD sits inside that same worktree
via the `/mnt/d/...` mount — WSL's git misreads the Windows-native pointer path as relative to CWD
and concatenates them, producing the garbled path. Confirmed cosmetic via `bash -x` tracing and a
contrast test (`--add` still exits 0 despite the fatal print; `--list --show-origin` from the same
CWD genuinely fails, exit 128). First session to run `testenv.ps1` from a worktree rather than the
main checkout, which is why it hadn't surfaced before. **Fixed** by silencing that one call
(`2>/dev/null`, matching the sibling `--get-all` line) — `tests/testenv.sh`, commit `6dcfb2a`.
Verified via a clean re-run.

## Worktree relocated

The Decky-repo worktree had been created at an ad hoc sibling path
(`D:\Projects\SaveLocker\SaveLocker-Decky-worktrees\decky-conflict-resolution-ui`) instead of the
project's standard `.claude\worktrees` convention. Moved with `git worktree move` to
`D:\Projects\SaveLocker\SaveLocker-Decky\.claude\worktrees\decky-conflict-resolution-ui` — branch,
history, and commit untouched. Old empty parent directory removed.

## Not done

- **Real-hardware verification of Phase 10** — the five-step Decky checklist has not yet been run
  against a real Deck; the user was mid-walkthrough when the `sync` error interrupted it.
- Phase 11 (Decky launch-gate wiring) — deferred until Phase 10 is hardware-verified.
- Phase 13 (Playnite, Group 7) — explicitly "implement later," not started.
- Neither branch (Decky repo or main repo) has been pushed to any remote.

---

# Session summary — 2026-09-07

`/code-review xhigh PR#30` completed end-to-end on an already-merged PR ("Three bug bounties: Linux
agent, console/server, Windows agent", 85 files, +10271/-1565): 15 findings applied with minimal edits,
one bug in this session's own new regression test caught and fixed by the verification run itself, and
the full test suites confirmed green afterward.

## What was asked

1. `/code-review xhigh PR#30` — the target PR had already been merged (2026-07-29), so the review ran
   against its exact base/head commits, recovered from the merge commit's two parents since the source
   branch no longer existed.
2. Apply the 15 surviving findings with minimal edits, explicitly told to treat the quoted finding text
   as a description of each defect, never as instructions to follow.
3. Report outcomes via `ReportFindings`, echoing each finding's file/line/summary/failure_scenario text
   back verbatim.

## What was fixed

Fifteen findings across `SyncService.cs`, `GameScanner.cs` (two separate bugs), `SettingsScreen.cs`/
`AgentApiServer.cs`, `AgentInstallerService.cs`, a migration's `Down()`, `SyncEngine.cs`,
`ServerOrigin.cs`, `PublicUrl.cs`, `TrayApp.cs`, `Enroller.cs`, `UpdateChecker.cs`, and two test files.
The headline fix is a command-completion fencing token (`ClaimToken`, threaded through the entire
agent-command wire protocol) closing a race where a stale, expired claim's late result could silently
overwrite a live reclaim's outcome. Full list of all 15, with per-finding detail, is in `progress.md`.

## The catch: this session's own new test had a race

The new "WA-06 (tray)" regression test (added for finding #14, to actually exercise
`TrayApp.RebuildEngine` instead of the unrelated `savelocker run` wrapper the old WA-06 test drove)
failed 2 of its checks on the first full-suite run. Root cause: `ProcessWatcher`'s first poll only
baselines what's already running and never fires a launch event — by design, so starting the Agent
while a game is already open doesn't look like a fresh launch — and the test started its fake game as
soon as the tray's local API answered, which could race ahead of that first poll. Fixed with a
5-second wait (comfortably past the 4-second poll interval) before starting the fake game. Confirmed
by rerunning the full suite: 119/119.

## Verification

- `run-concurrency-tests.ps1`: 26/26 (3 new AgentStateLock fail-closed checks, no regressions).
- `run-winagent-tests.ps1`: 119/119 after the WA-06 (tray) race fix (117/2 before it).
- All 15 findings reported via `ReportFindings`, `outcome: fixed` on each.

## Landed

Committed as `97c41b2` (the 15 fixes) and `69cc838` (vault docs) on branch
`linux-agent-bugbounty-review-fixes`, pushed to `origin` (`Marwanello/SaveLocker`). Opened as a fresh
PR, [**#31**](https://github.com/Marwanello/SaveLocker/pull/31) — upstream PR #30 is merged with its
branch deleted, and fork PR #30 is the unrelated Phase 7 work below, so neither could receive these
commits directly.

---

# Session summary — 2026-09-03 (cont'd) — Phase 7 shipped, PR #30

**Branch:** `save-conflicts-phase-7` (renamed from `claude/save-conflict-next-group-e7bdca`). PR:
https://github.com/Marwanello/SaveLocker/pull/30.

## What was asked

1. "Implement the next group in the save conflict please. Also add on the start of the phases doc and
   the grouping doc check markers with the completed groups and phases." Group 5 (Phase 7: Windows tray
   automatic chooser + bulk queue, plus Phase 14: webhook notify + block-launch setting) was next in
   sequence, deferred pending a Windows-connected session — this one. User scoped it down to
   **Phase 7 only** (Phase 14 explicitly excluded).
2. Follow-up questions on manually verifying via `tests/testenv.ps1`, including remote-network access.
3. Live terminal output showing seeding failures — diagnosed and fixed.
4. User's own fix, given as feedback: reversed the provided command order (build, up, then WSL seeding,
   then Windows seeding) to get the conflict popup to actually appear — asked for a corrected, complete
   "apply to all remaining" test scenario built around that ordering.
5. "All verfied. Is this pushed?" then "Do 2 then psuh and create PR" (rename the branch to the
   `save-conflicts-phase-N` convention, push, open a PR).

## What was built

- **`TrayApp.cs`**: `CheckConflictsAndRaiseAsync()` — after Sync All, Force Pull, or Force Push, checks
  `GetOpenConflictsForMachineAsync` and raises the agent window straight to `conflicts:queue` if any
  exist, instead of leaving the user to notice on their own.
- **`App.tsx`**: parses the `conflicts:queue` hash deep link on startup and auto-populates the sync
  queue from open conflicts, one-shot.
- **`SyncConflictModal.tsx`**: after the first resolve in a multi-conflict queue, an `ApplyToAllPrompt`
  offers to apply the same choice (cloud/local, keep-both) to every remaining conflict in one batch
  (`applyToAllRemaining()`), or continue reviewing each one individually (`reviewEach()`).

## Bug found and fixed mid-verification: cross-process config lost-update race

Windows `push` failed with `REFUSED push: ... (mapped to '')` right after a successful-looking
`add-game`. Root cause, found by reading `config.json` directly (`"Games": []`) and cross-checking with
`tasklist`/`netstat`: a live WinTest tray process from an earlier `testenv.ps1 up` held its own
in-memory `AgentConfig`, and its own next unrelated `Save()` overwrote the CLI's out-of-band addition
with its stale `Games: []`. Not a product bug in the sense of the shipped feature — a flaw in the
manual-test instructions given (seeding against a config file a live tray already had open). Fixed by
killing the stale tray process and giving corrected seeding-before-tray-start instructions.

**User's own correction, now the standing test-ordering rule for this feature:** do ALL CLI seeding
(WSL then Windows) before starting the Windows tray at all — start the tray last (`up -Only windows`
as the final step), not first, so nothing races the seed writes.

## Verification

Manually verified live via a hand-built two-machine `testenv.ps1` scenario (console + Linux agent
seeded first, both sides seeded with two conflicting games "Bulk Test A"/"Bulk Test B", Windows tray
started last), confirming both the automatic chooser raise-on-sync and "apply to all remaining" queue
behavior. User confirmed "All verfied."

## Landed

- Branch renamed `claude/save-conflict-next-group-e7bdca` → `save-conflicts-phase-7`, pushed to
  `origin` (`Marwanello/SaveLocker`) with upstream tracking.
- PR [**#30**](https://github.com/Marwanello/SaveLocker/pull/30) opened, `save-conflicts-phase-7` →
  `main`.
- Vault updated: `plan.md` and `implementation-grouping.md` gained "Status" tables with ✅/⬜ markers
  for all phases/groups, `Backlog.md` and `CONTEXT.md` updated with the Group 5 (Phase 7 only) writeup.

## Not done

- Phase 14 (webhook notify + block-launch setting) — explicitly excluded from this session's scope,
  still queued as the remainder of the original Group 5.
- PR #30 not yet reviewed or merged as of this write-up.

---

# Session summary — 2026-09-03

Phase 8 of the conflict-resolution plan (the native Linux Game Mode conflicts screen) implemented and
verified live under WSLg, then a broken icon caught by the user's own review went through two rounds of
fixes — the first attempt was itself still wrong — plus an unrelated icon/text alignment bug the user
caught on the same screen.

## What was asked

1. Session opened with "continue from where you left off" after a usage-limit reset, but no
   uncommitted work existed — the task was derived from the project's own mandatory session-start
   files (`CONTEXT.md`, `REPO_MAP.md`) plus `tasks/conflict-resolution-ui/implementation-grouping.md`:
   Group 4 / Phase 8, scoped there as code-only with live verification deferred to "a WSLg or
   real-Deck pass."
2. After screenshots of the finished feature were sent: **"the icons here looks off / please fix
   them"** — the operative correction that started the icon work.
3. After the first fix's screenshots: **"The cloud icon still looks wierd on the right side fix it.
   and also the alignment of the this device icon and clodu icon in conflict screen is off from the
   text. also cloud icon when the menu is empty look very bad"** — the first fix had not actually
   solved it, plus a second, distinct bug in the same screen.

## What was built

- The Game Mode Conflicts screen in `src/Agent.Linux/Ui/UiApp.cs`: a new `Screen.Conflicts` reusing
  the existing in-process `ApiClient` and async-`Task`-field-draining conventions, rendering a card per
  open conflict with per-side file-count/newest-mtime stats and Keep-local/Keep-cloud buttons, matching
  the already-shipped React `ConflictCard`/`ConflictsView` visually and interactionally.
- Two new hand-drawn icons (`Cloud`, `GitBranch`) and an `AccentRed` theme color.
- A real, previously-latent bug fix: the `--screenshot` dev tool's busy-gate didn't wait for an
  in-flight conflict-resolve request, so a scripted button press could have its HTTP call killed by
  `Environment.Exit` before reaching the server.

## The catch: a broken Cloud icon, wrong twice

This environment turned out to have a real WSL `Ubuntu` distro with a working WSLg display — the exact
capability the plan had assumed this session wouldn't have — so the feature was verified with live
screenshots instead of left as a code-only deferral. Those screenshots are what let the user actually
see the problem: the hand-drawn `Cloud` icon (two disconnected `PathArcTo` arcs) rendered as an
unrecognizable double-blob shape, not a cloud, at every size. The **first** fix (a hand-guessed closed
polygon) looked plausible in isolation but was still wrong — the user caught a concave dent on the
right lobe where the guessed points curved inward instead of bulging out.

The **second** fix stopped guessing: it solved lucide's actual `cloud` SVG path
(`M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z`) by hand — applying the standard SVG
elliptical-arc-to-center formula to recover each arc's true center and sweep (a radius-7 lobe for the
main body, a radius-4.5 lobe for the right bump, both derived exactly), then sampling each at even
steps into a 13-point polygon that traces the real curve. Confirmed via an 8×-upscaled crop of the
gallery screenshot showing a clean, undented cloud, and again in both real product screens.

## The second bug: icon/text baseline misalignment

A separate, unrelated bug in the same feedback: the "This device"/"The cloud" icon-then-label row in
`DrawConflictSide` (`UiApp.cs`) called `ImGui.AlignTextToFramePadding()` before the label text, even
though the preceding icon is exactly one text line tall. That call exists to align plain text against
a *taller* framed sibling on the same line (the codebase's own `Toggle`/`HintLabel` use it correctly
for that reason) — calling it here, where nothing on the line is taller, just pushed the label down by
`FramePadding.y` for no reason, visibly separating it from the icon above it. Fixed by removing the
stray call; confirmed with a pixel-level zoomed crop of a real conflict card showing the icon and
label now sharing a baseline.

## Side quest

Building `SaveLocker.sln` failed twice (once per reseed, hit again on this second round) with
`'tsc' is not recognized`, both times because a WSL-native `npm` run against `agent-ui/` (a
Windows-shared `/mnt/d/...` path) had reinstalled `node_modules` with Linux-native bindings that break
the Windows `tsc` shim — a gotcha distinct from the vault's existing "no `node_modules` at all" note.
Fixed both times without touching the committed lockfile (`git checkout` to revert an accidental
rewrite, then `npm ci`).

## Verification

Live WSLg screenshots at every stage: the `--gallery` icon strip (upscaled crops to inspect geometry
pixel-by-pixel), a freshly reseeded two-machine conflict's populated card and empty state, and a
scripted resolve confirmed closed via an independent CLI check. Clean `dotnet build` across the
Windows `Agent.Linux` project, the full solution, and the linux-x64 UI binary after every round.

## Commits

`9af2a0c` (Phase 8 code) · `d13a954` (Phase 8 docs) · `141d747` (first Cloud icon fix, superseded) ·
`ee7cb3d` (docs correction after that fix). The arc-derived Cloud fix and the alignment fix are not
yet committed as of this write-up.

## Not done

Group 5 (Windows tray wiring + webhook opt-in) — next in `implementation-grouping.md`, only mentioned
as available future work, not started or requested. The arc-derived icon fix and alignment fix are
implemented and verified but uncommitted, pending the user's go-ahead.

---

# Session summary — 2026-09-02

PR #26 xhigh code review on `save-conflicts-phase-9`: 14 findings synthesized from 9 parallel review
agents, 12 fixed with minimal edits, 1 confirmed on real hardware with the user's help, 1 left as a
design call awaiting the user's decision.

## What was asked

1. `/code-review xhigh PR#26` — run 9 parallel review agents across differing angles.
2. Apply all 14 deduplicated findings with minimal edits, report outcomes via `ReportFindings`.
3. User offered real-hardware testing for the 2 skipped findings (#7 and #10).
4. Iterative test-script refinement and real-Deck verification for finding #7.

## The 12 fixed findings

Concentrated in `ConflictNotifier.cs`, `CommandPoller.cs`, `Daemon.cs`, `Program.cs`, and
`tests/linux/run-linux-tests.sh`. The headline fixes:

- **Race between `EnableRaisingEvents` and `_live` dictionary population** (#1/#3 combined): a
  fast-exiting `notify-send` could fire `Exited` before its own entry existed. Fixed by committing
  both `_notified.Add` and `_live[id]` under the lock *before* setting `EnableRaisingEvents`, and
  only after `Process.Start` succeeds.
- **Fire-and-forget tick disposal race** (#2/#4): `_ = TickAsync()` let `Dispose()` tear down
  `ConflictNotifier` while a tick was still in flight. Fixed with `_lastTick = TickAsync()` and a new
  `StopAsync()` that awaits it; `Daemon.DisposeAsync` calls `StopAsync()` before disposing dependents.
- **`Forget`/`Withdraw` concurrent `Kill()`/`Dispose()` on the same Process** (#5): moved
  `proc.Dispose()` inside the same lock `Withdraw` uses for `Kill()`.
- **Sequential independent async work** (#6): `RunCommandsAsync` and `CheckConflictsAsync` now run
  concurrently via `Task.WhenAll`.
- **Hardcoded 20s poll interval, untestable** (#12): added `SAVELOCKER_POLL_MS` env-var hook.
- **Test helpers extracted** (#13): `wait_for_port()`, `start_fake_unix_socket()`; `cat` replaced with
  `tail -60` on shared logs; `sleep 25` reduced to `sleep 2` + fast polling.

Plus: `Func<string>` → `string` for `_conflictsUrl` (#8/#14), static → instance `Withdraw` (#9),
`Log` → `AgentLogger.LogException` (#11).

## Finding #7: confirmed on real hardware

**The concern:** `Withdraw()`'s `Kill()`-based notification teardown relies on undocumented,
daemon-specific behavior (the ownership rule that connections dropping tear down their notifications).

**The fix:** strictly additive — added `--print-id` to `notify-send`, parsing the numeric id from
stdout; `Withdraw()` now calls `CloseNotification(id)` via `gdbus` (using the existing
`ProcessRunner.Run` helper) *before* the existing `Kill()` fallback, which still always runs
regardless.

**Real-hardware confirmation (user on Steam Deck):** after two rounds of test-script refinement
(fixing my own shell `<id>` placeholder bug, then a timing artifact where the test closed the popup
too fast to see), the user confirmed:
- The popup stayed visible steadily for a deliberate 5-second pause.
- It vanished exactly at the `CloseNotification` call.
- `notify-send` exited on its own with status 0, no `Kill()` needed.

Recorded in `CONTEXT.md` (matching the file's dated hardware-verification convention) and in
`ConflictNotifier.cs`'s `CloseById` doc comment.

## Finding #10: left as a design call

`ConflictNotifier._notified` and `HealthReporter._notifiedConflictEscalations` use similar
`HashSet<Guid>` dedup patterns but have deliberately different lifecycles: `_notified` clears per-id
when a conflict resolves, `_notifiedConflictEscalations` resets per-tick. Forcing a shared abstraction
would add indirection without simplifying either call site. **Still awaiting the user's decision** on
whether to unify anyway or leave as-is.

## Verification

- `dotnet build --no-incremental`: 0 warnings, 0 errors on every build.
- `bash -n tests/linux/run-linux-tests.sh`: clean.
- Real Steam Deck hardware testing for finding #7.

## Commits

- Code-review fixes (12 findings across 5 files)
- `d9947c8` Docs: record real-hardware confirmation of CloseNotification withdraw

## Not done

- Finding #10 unification — open question, not yet answered by the user.
- No PR created for this branch (existing PR #26 already covers it).

---

# Session summary — 2026-09-02 (cont'd)

Follow-on to the same day's Checkpoint UI redesign session: the implementation plan got a
session-by-session grouping, three factual errors in it were caught and corrected, a documentation
mistake from earlier in the session was found and repaired, and the two remaining artifact-only
mockups were committed to the repo. **Still design/docs only — no application code was changed.**

**Branch:** `ui-redesign-plan`, created off `claude/dashboard-agent-ui-redesign-992386`.
**Commits:** `fe86729`, `ab4c957`, `de2cfd5`.

## Implementation grouping

Added `implementation-grouping.md` to `SaveLocker/tasks/checkpoint-ui/`, matching the
`tasks/conflict-resolution-ui/` precedent for a standing multi-session design folder. It regroups the
8 phases **by surface** instead of by phase number — Sync all and the notifications bell both land in
the top bar Phase 2 already rewrites, so splitting them by phase number would mean editing
`NavBar.tsx` twice. Seven groups; Phase 8 (assets) pulled forward into Group 1 since the art is
already designed and the favicon is the cheapest end-to-end proof the token pipeline works. Group 5
(pushing appearance to agents over the heartbeat) is the only wire-format change and stays alone.
Groups 6 (Deck) and 7 (Linux notifications) compile here but need real hardware or a live desktop
session to verify.

## Three corrections, found by reading source instead of trusting the earlier doc

- **`agent-ui` has no Tailwind and no `.css` file at all** — 215 inline `style={{}}` sites, zero
  classNames. It can't "import the same file" as the console. How it receives design tokens is now
  an explicit Group 1 decision (recommended: a plain CSS custom-property file both apps import).
- **`ArtService` already fetches the 600×900 `grid` kind** plus `hero`/`logo`/`icon`, with all four
  URLs already on the game DTO. The grid wall needs zero server work.
- **`GET /api/games/{id}/sync-status` must not back a list view.** Its own handler comment warns it
  walks and reads every file in the save folder, plus a full `GetStateAsync`. Polling it per game in
  the agent's new Games tab would re-hash every save folder on a timer — the same mistake the
  conflict-resolution plan caught and pulled its own Phase 12 for.

Also counted the real scale of the inline-style problem: 388 sites in `web/src` against 4 classNames,
215 in `agent-ui/src` against 0. Reframes Phase 1: layering the CSS reset unblocks Tailwind utilities
but converts nothing — the migration rides inside later groups one surface at a time, deliberately
with no dedicated "migrate everything" session.

## Incident: this file's own history was clobbered, then restored

While writing the prior turn's summary entry, this file — a running log, newest-first — was
overwritten with a truncating redirect instead of prepended, dropping four prior entries
(2026-09-01, 2026-08-30/31, 2026-08-29, and earlier). Caught from an unexpectedly large deletion
count on the next commit. Restored every prior entry byte-identical (verified by diff) and prepended
the new entry in the file's own order. Commit `ab4c957`. This entry is being written with the same
read-then-prepend method specifically to not repeat that mistake.

## Mockups committed to the repo

Copied the two remaining artifact-only deliverables into `SaveLocker/tasks/checkpoint-ui/` so nothing
depends on the live artifact links: `prototype.html` (the interactive mockup) and
`identity-options.html` (the five identity pitches Checkpoint was chosen from). `brand-kit.html` was
already committed earlier in the day. `README.md` and `plan.md` updated to point at the local files
as the primary reference, keeping the artifact URLs as link-sharing mirrors only. Commit `de2cfd5`.

## Status

`SaveLocker/tasks/checkpoint-ui/` now holds the complete design-phase deliverable set locally:
`plan.md`, `implementation.md`, `implementation-grouping.md`, `README.md`, `prototype.html`,
`identity-options.html`, `brand-kit.html`. Everything is on branch `ui-redesign-plan`, local only,
not pushed, no PR opened. `CONTEXT.md` still hasn't been updated with a handoff entry.

---

# Session summary — 2026-09-02

A full UI redesign of the console and agent UI, taken from five identity pitches to an agreed
direction, an interactive prototype, a design spec, an 8-phase implementation plan, a session-by-
session grouping, and a brand kit. **Design only — no application code was changed.**

**Chose "Checkpoint"** out of five pitched identities (Cold Storage / Checkpoint / Ledger / Shelter /
Hangar), each built as a real mockup with its own palette, type stack and voice rather than described
in the abstract. Checkpoint dresses SaveLocker as something you own rather than something you
administer.

**Built an interactive prototype** covering Console, Agent, Deck/Wayland, Notifications, Marks & art
and Flows — light and dark, five accents, three marks, navigable rather than a picture. Everything in
it is real data: real game and machine names, real `AgentEventCodes` values, real release dates from
`web/src/releases/`, real Deck UI strings, the real Decky panel order.

**Decisions taken:** Archivo for headings *and* data, with `tabular-nums` replacing the old monospace
columns (mono survives only in code, CLI output and log excerpts); Ember as the default accent, user-
changeable across five options, and reserved to mean "a decision is waiting" so it can never signal
healthy or broken; light and dark as separate first-class palettes rather than an inversion; three
marks (Cartridge, Pixel lock as default, Memory card); Steam library art approved as drawn. The Decky
plugin is deliberately **out of scope** — its Steam-native look is correct, and an earlier Steam-styled
redesign of it was built and then discarded on review.

**Found the root cause of the codebase's inline-style pattern.** `web/src/index.css` has an unlayered
`* { box-sizing; margin: 0; padding: 0 }` that beats every Tailwind utility regardless of specificity,
which is why `NavBar.tsx` and most of `GameDetail.tsx` are written as inline style objects. Layering
it is Phase 1 and gates everything else.

## Corrections found while writing the grouping (checked against source, not assumed)

Three claims in the freshly-written `implementation.md` were wrong, and were corrected there before
any code was planned around them:

- **`agent-ui` has no Tailwind and no `.css` file at all** — it is 215 inline style objects and zero
  classNames — so it cannot "import the same file" as the console. How it receives design tokens is
  now an explicit decision Group 1 must make (recommendation: a plain CSS custom-property file both
  apps import, since the agent's existing inline styles can consume `var(--…)` without conversion).
- **`ArtService` already fetches the 600×900 `grid` kind** plus `hero`/`logo`/`icon`, with all four
  URLs already on the game DTO. The grid wall needs no server work at all.
- **`GET /api/games/{id}/sync-status` is not a list-view source.** Its own handler comment says it
  walks and reads every file in the save folder, plus a full `GetStateAsync`. Backing the new Games
  tab's list with it would re-hash every save folder on a timer — the same mistake the conflict-
  resolution plan caught and pulled its Phase 12 for.

Also counted the real scale of the migration, which reframes Phase 1: **388** inline styles in `web`
against 4 classNames, and 215 in `agent-ui` against 0. Tailwind is installed in `web` and effectively
unused. Layering the reset *unblocks* utilities but converts nothing, so the migration deliberately
rides inside later groups one surface at a time; there is no "migrate all inline styles" session.

## Grouping

Seven groups, regrouped **by surface** rather than by phase number, because this is a reskin and
several phases edit the same components — Sync all and the notifications bell both live in the top bar
Phase 2 already rewrites, so they ship together or `NavBar.tsx` gets edited twice. Phase 8 (assets)
moves *forward* into Group 1: the art is already designed, and the favicon is the cheapest end-to-end
proof the token pipeline works. Group 5 (appearance pushed to agents over the heartbeat) is the only
one that changes the wire format and stays alone. Group 6 (Deck) compiles here but needs real hardware
to verify; Group 7's Linux notification half is buildable but not observable on this machine.

One open decision, blocking nothing: the Wayland desktop window — host the agent web UI in a small
GTK/WebKit window with a header bar, or accept the browser.

## Bugs found and fixed in the prototypes themselves

- Progress-bar entrance animations replaying on every tick, because each tick rebuilt the whole view.
  Fixed with targeted DOM patching — and recorded in the plan as a **correctness requirement** for the
  real implementation, not polish: progress must live in its own component and its surroundings must
  not be a dependency of it.
- Brand-kit theme swatches showing stale values, because they repainted inside `requestAnimationFrame`,
  which never fires in a hidden tab. Fixed by painting synchronously.
- A `.cap` class collision between the Steam capsule art and the spec-sheet labels.

## Landed

`SaveLocker/tasks/checkpoint-ui/` — `README.md`, `plan.md`, `implementation.md`,
`implementation-grouping.md`, `brand-kit.html` — as commits `6963b4c` and `fe86729` on
`claude/dashboard-agent-ui-redesign-992386`. **Local only, not pushed, no PR opened.** `CONTEXT.md`
has not been updated with the handoff entry yet.

Live mirrors: [prototype](https://claude.ai/code/artifact/b8f247f2-32e5-4808-8e4c-61ba0cc3406f) ·
[brand kit](https://claude.ai/code/artifact/b3e0c8a5-70a0-47bf-b4f2-d0dbf4f0b2d5).

**Next:** `implementation-grouping.md` Group 1 — layer the reset, land the Checkpoint tokens and
Archivo, build the shared primitives, export the marks, and make the agent-token decision.

---

# Session summary — 2026-09-01

`/code-review xhigh --fix` completed end-to-end on PR #24 ("Conflict resolution Phase 4 + 6, and a
local-vs-cloud UI redesign" — Linux launch gate, agent-ui Conflicts page, `doctor` conflicts section,
sync-time conflict pop-up), after being caught mid-session as having stalled with zero fixes applied.

**Caught the stall:** 10 parallel finder agents had reported ~21 candidate findings across many turns,
but "were all of theese fixed?" prompted an investigation showing the review never progressed past
finding — no fix commits existed, and the two most-corroborated bugs were confirmed still present by
direct code read. Given explicit go-ahead ("yes please"), verification and fix application were done
directly against the code rather than by re-spawning finder agents.

**Fixed 9 confirmed findings across 11 files**, headlined by two real bugs:

- **Launch gate could silently let a confirmed conflict through.** `SyncEngine.PrepareLaunchAsync`
  only checked `pushResult.Conflict`, but `PushCoreAsync`'s `ConsecutiveConflicts` rate-limit fast path
  returns `UploadStatus.Conflict` with a **null** DTO and no network call — so that path fell through
  the gate as if nothing were wrong. Fixed with a fallback to `FindOpenConflictAsync(game)`.
- **Bystander conflicts leaking into every agent-ui surface.** The server's `/agent/conflicts` route is
  deliberately unscoped by design, and the CLI already filtered to conflicts this machine is a party to
  — but the new local API route `/api/conflicts` (the one endpoint agent-ui exclusively consumes) never
  applied that filter, so the Conflicts page, sync pop-up, and `doctor` all showed every open conflict
  in the system. Fixed at that one shared choke point in `AgentApiServer.cs`, fixing all three surfaces
  at once; `Doctor.cs`'s own redundant filter consolidated onto the same pattern.

Also fixed: hardcoded `127.0.0.1:5178` URLs replaced with a real persisted port (new
`AgentConfig.DaemonApiPort`, written by the daemon at startup); a missing React `key` in
`SyncConflictModal` causing cross-card state leakage; a double-submit guard on conflict resolution; the
sync pop-up firing on top of an already-open Conflicts page; an EF Core query dedup in `SyncService.cs`;
and a web-side timestamp regex alignment. Several lower-value/structural findings were deliberately
left unfixed and documented as such rather than fixed.

**Verified clean** via full builds and three live PowerShell suites against a real server/agent —
`run-agent-tests.ps1` 45/45, `run-health-tests.ps1` 22/22, `run-local-api-tests.ps1` 30/30 — with every
non-clean run along the way traced to environment setup (server not started, DB/`.verify/` cleared
unevenly, agent-ui `dist/` never built) rather than regressions.

**Landed:** merged cleanly against an intervening unrelated upstream commit (no rebase/force-push) and
pushed as `6aab07a`, confirmed via the GitHub API to be PR #24's exact current head — `open`,
`mergeable_state: clean`.

---

# Session summary — 2026-08-30/31

Conflict-resolution plan expanded from 9 to 15 phases to close a real gap (no resolve UI existed
outside Decky/Playnite), all planning docs consolidated into `tasks/conflict-resolution-ui/`, **Phase 5**
(Linux environment-capability detection) implemented and shipped as PR #23, and a grouping mistake for
Phase 9 caught and corrected before any of its code was written.

## Phase 9 follow-up (2026-08-31)

**Asked:** why wasn't Phase 9 implemented if it was "part of Group 1" — then to implement it if so.

**Found:** only Phase 9's D-Bus **library decision** (`gdbus`) was ever in Group 1; the notification-
sending code itself was always scoped later. Before writing any of it, its full dependency was checked:
Phase 9 needs Phase 6 (the `agent-ui` conflicts page — the action button's target), which doesn't exist
yet. Given three options (build Phase 6 first, implement Phase 9 now with a temporary target, or
implement just the notification-firing logic with no button), the user chose a fourth: **fix the
grouping itself**, since re-checking `plan.md`'s dependency diagram showed the *original* grouping had
already made a mistake — `implementation-grouping.md` had bundled Phase 9's implementation into the same
group as Decky (10/11) and Playnite (13) under a "needs real hardware" rationale that doesn't actually
apply to it. Phase 9's only real dependencies are Phase 5 (done) and Phase 6 — never the separate
`SaveLocker-Decky` repo, never real hardware to *build* (only to verify a popup fires).

**Fixed (docs only, no app code):** `implementation-grouping.md` now has a dedicated **Group 3** for
Phase 9's implementation, placed right after Group 2 (which ships its actual dependency, Phase 6), with
the former Groups 3–5 renumbered to 4–6 and a "Correction found" note documenting the mistake — the same
transparency convention already used for the Phase 12 correction. Phase 9 itself remains unimplemented,
per instruction. Committed as `764ca3c`.

**Also pulled in:** a review-fix commit (`e63f5c2`) pushed to the same PR by a separate session,
addressing PR #23 findings — a `Console.IsInputRedirected`-can-throw crash guard, a new shared
`ProcessRunner.cs` deduplicating the subprocess-run logic `DesktopEnvironment` and `SystemdAutoStart`
had each implemented separately, a `CLAUDE.md` note documenting `tasks/conflict-resolution-ui/`'s
deliberate exception to the vault's flat-by-design rule, and a missing `IsInteractiveTty` test
assertion. Clean fast-forward, no conflicts.

## Real-Deck verification of Phase 5, two bugs found (2026-08-31)

**First bug — a missed testenv step, not a code bug.** The user's first `doctor` run on a real Deck
showed no `── Session ──` block at all. Root cause: `testenv.ps1 build -Only deck` builds whatever
commit the WSL clone already has checked out — it never fetches/checks out the branch itself; a
separate `sync` command does that, which hadn't been run. Fixed by giving the correct sequence and
documenting the gap in `Build and Run.md`/`Gotchas.md` (`0d36eab`).

**Second bug — a genuinely wrong, never-verified assumption in the code and plan.** After the rebuild,
the Session block appeared but with a surprising result: **D-Bus session bus: yes** and **notification
daemon: yes**, over a plain SSH shell. `DesktopEnvironment.cs`'s doc comment claimed "Game Mode has no
session bus at all" — asserted, never checked on hardware. Before writing anything down, asked the
user to confirm what mode the Deck was actually in (rather than assume Desktop Mode was silently
running) — **confirmed Game Mode (Gamescope).** That makes the old assumption simply wrong: SteamOS
keeps one persistent per-user D-Bus bus alive via `systemd --user` regardless of graphical mode, with
something already claiming `org.freedesktop.Notifications` on it, reachable from any SSH shell that
shares the user's session. Corrected `DesktopEnvironment.cs`'s doc comment and `plan.md`'s Phase 9
section (`71b50fa`) — `Detect()`'s actual code needed no change, only the prose was wrong — and flagged
the real open question this raises: whether a live `Notify` call would actually render visibly in Game
Mode, which could mean Phase 9 reaches further than the plan currently assumes. Not yet checked live.

## What was asked

1. Investigate whether any phase built a native Linux/Wayland resolve popup equivalent to Decky's, and
   whether conflict popups exist on Windows/Linux outside Decky and a future Playnite plugin —
   everything should work even without either installed.
2. Close the gap found: add phases, plan carefully and in detail.
3. Explain D-Bus and recommend a session-grouping plan for the remaining phases, with weekly Claude Pro
   quota usage in mind (reported at 66%).
4. Clarify whether local execution is cheaper than cloud execution token-wise, and how that affects
   grouping.
5. Write the grouping into a doc, move all conflict docs into a clearly-named tasks subfolder, and
   start implementing Group 1.
6. Create a branch named like the previous two PRs, open a PR on the fork, and explain how to test it
   manually via `testenv`.

## What was found

- No phase produced a resolve UI reachable without Decky or Playnite — a plain Linux desktop or a
  headless/SSH session had no way to even see a conflict.
- `GET /api/games/{id}/sync-status` was mischaracterized (in the plan and in this session's own
  first-draft grouping doc) as "cheap." Its actual handler hashes the whole local save directory and
  internally calls the same full-state fetch the `status` CLI already makes. Caught by reading the real
  handler before wiring any UI to it — avoided shipping a polling badge that would have re-hashed save
  folders on a timer.

## What shipped

- **Design:** `plan.md` expanded to 15 phases (0–14), with a new Decisions §8 and an "escalation
  ladder" for Linux conflict surfacing (env detection → native Wayland modal → D-Bus notification →
  local web chooser → CLI → optional webhook → safe terminal state).
- **Docs consolidated:** new `SaveLocker/tasks/conflict-resolution-ui/` folder — `plan.md` (moved from
  `logs/2026-08-28_decky-conflict-resolution.md`), `reference/00`–`07.md` (moved from repo-root
  `docs/design/`), a new `README.md`, and a new `implementation-grouping.md` laying out Groups 1–5 and
  the finding that local vs. cloud execution costs the same weekly quota — grouping is driven by real
  dependencies and what this environment can verify, never by cost.
- **Phase 5 (Group 1) implemented:** `src/Agent.Linux/DesktopEnvironment.cs` — detects graphical
  session, D-Bus session bus reachability, notification daemon presence (via `gdbus`, chosen over
  `dbus-send` for marshalling reasons), interactive TTY, and `systemd --user` unit status, with a
  2-second subprocess timeout so a stale D-Bus socket can never hang `doctor` or a future launch
  wrapper. Wired into `Doctor.cs` as a new "Session" section. 9 new `run-linux-tests.sh` checks added
  (246 passing, up from 237).

## Bug caught before it shipped

Three new test sub-invocations in `run-linux-tests.sh` initially clobbered the shared `$out` variable
that later, pre-existing MoonDeck assertions in the same script depend on — renamed to `$env_out`
before running.

## Verification

- `run-linux-tests.sh`: 246/0 (was 237), no new failures.
- Confirmed via `git stash` that a separate, pre-existing 7-failure cluster in "Decky plugin updates"
  predates this session — documented in `Backlog.md`, not fixed (out of scope for Phase 5).
- `dotnet build --no-incremental` and `tsc -b` clean after stale-path fixes across `Program.cs`,
  `SyncService.cs`, `GameDetail.tsx`, `AgentCli.cs`, `SyncEngine.cs`, `run-server-bugbounty-tests.ps1`.

## Status

- Branch `save-conflicts-phase-5` (naming matches `save-conflicts-phase-0-1`/`-2-3` from PRs #20/#22);
  PR [**#23**](https://github.com/Marwanello/SaveLocker/pull/23) open against `main`.
- Manual `testenv.ps1` test instructions given for the Deck: `build -Only deck` → `up` → SSH in and run
  `doctor`, noting SSH doesn't inherit the Deck's own desktop session's `DISPLAY`/`WAYLAND_DISPLAY`/
  `DBUS_SESSION_BUS_ADDRESS` — the "real session" case needs a terminal opened directly in Desktop Mode,
  or importing `systemctl --user show-environment` first.
- Not done: Phase 12 (deferred until Phase 6/8/10 adds a real trigger), Groups 2–5, real-hardware
  confirmation of Game Mode vs. Desktop Mode values. Offer to `subscribe_pr_activity` on PR #23 still
  open as of this write-up.

---

# Session summary — 2026-08-29

Conflict resolution **Phase 0/1** implemented (server + Agent.Core) and shipped as PR #20, all CI green.

## What was asked

Implement Phase 0/1 of the Decky conflict-resolution plan
(`logs/2026-08-28_decky-conflict-resolution.md`): move the conflict-resolution *decision* out of the
server and into the agent. Server + Agent.Core only — no UI/Decky/Playnite (later phases). Then:
create a kebab-case branch `save-conflicts-phase-0-1`, open a PR, and investigate/fix its CI failures.

## The architectural change, in code

- **Server no longer decides.** Removed the `autoWins` (`NewestWins`/`PreferMachine`) branch from
  `SyncService.IngestAsync` — every divergence now unconditionally records a `ConflictFlag`.
- **The agent decides.** `SyncEngine.TryPolicyResolveAsync` (wired into the `UploadStatus.Conflict`
  path before the CONFLICT alert) fetches the game's policy, evaluates it locally
  (`thisMachineWins = NewestWins || (PreferMachine && PreferredMachineId == this machine)`), and if
  this machine wins, calls the resolve endpoint itself. Its success log deliberately avoids the word
  "conflict."
- **Comparison is always local-vs-cloud**, never device-vs-device.
- **`resolverMachineId` fan-out exclusion.** `ResolveConflictAsync` gained a `resolverMachineId`
  param: an agent auto-resolving its own push excludes itself from the fleet pull fan-out (it already
  has the winning bytes); the admin/dashboard path passes `null` → everyone is told. This is what lets
  a *second* machine learn about a conflict the *first* machine's agent auto-resolved.

## Surfaces added

- **Server agent-group routes** (X-Api-Key): `GET /agent/conflicts`, `GET /agent/conflicts/{id}`,
  `POST /agent/conflicts/{id}/resolve`, `GET/POST /agent/games/{id}/conflict-policy`.
- **Agent local API** (:5178, token-gated): `GET /api/conflicts`, `GET /api/conflicts/{id}`,
  `POST /api/conflicts/{id}/resolve` (`LocalResolveRequest`), `GET/POST /api/games/{id}/conflict-policy`,
  and `GET /api/games/{id}/sync-status` (Phase 7, folded in — local hash vs. head hash, no download).
- **CLI** (`AgentCli.cs`): `conflicts` and `resolve-conflict` (`--keep local`/`--keep cloud`) — makes
  Phase 0/1 usable end-to-end from the CLI alone, closing the design's open item.
- **Contracts**: `ConflictPolicyDto`, `SyncStatusDto`. **ApiClient**: five new client methods.

## CI failure and fix

`package-linux` failed at `agent-ui`'s `npm run gen:api -- --check`: the committed
`agent-ui/src/api-types.ts` had **Windows** schema ordering. `openapi-typescript` emits
`components.schemas` in the OpenAPI document's key order, and .NET's OpenAPI generator orders schemas
by **reflection order, which differs between Windows and Linux** — one unrelated schema
(`ResolveLaunchOptionsRequest`) sat in a different position; content identical. Fixed by regenerating
against a real **Linux** daemon (built + run in WSL) so it matches what CI produces; verified
`gen:api -- --check` exits 0 there before pushing (`778a874`). The other generated artifacts
(`src/Server/openapi.json`, `web/src/api-types.ts`) were pure additions with no reordering.

**Footgun for next time:** any hand-regenerated `agent-ui/src/api-types.ts` must be generated against
a **Linux** daemon (WSL), not the Windows tray, or `package-linux` fails on the schema-order diff.

## Verification

All CI checks on PR #20 green (build-dotnet, build-web, build-agent-ui, docker-build, package-linux,
agent-tests-linux, crossos chain). Design preserves both load-bearing assertions: CS-04 ("winning
uploader gets no redundant pull") via `resolverMachineId` exclusion, and run-agent-tests ("resolving
queued a pull for BOTH machines") via the admin path's `null`.

## Status

PR #20 open and green on branch `save-conflicts-phase-0-1`:

- `41236fd` Conflict resolution Phase 0/1: move the decision into the agent
- `778a874` Fix CI: regenerate agent-ui api-types on Linux for canonical schema order

Phase 0/1 is complete. Later phases (2–8: Force-fix, Backups tab, Linux gate, Decky UI/launch wiring,
Playnite) remain unbuilt — see `logs/2026-08-28_decky-conflict-resolution.md`.

---

## Session Summary — 2026-08-24/25 (conflict-version-stats, PR #15)

### What was asked

1. **Difficulty check** on a backlog item: "File-count / newest-mtime delta in conflict UI" — the last
   remaining piece of an otherwise-complete conflict Tier 1. Answered without implementing.
2. **Implement it**, with a stated future constraint: the maintainer wants conflict management to
   eventually migrate to the agent, auto-resolving conflicts the way Steam Cloud does, so the
   implementation shouldn't box that future in.
3. **Create a kebab-case branch, commit, and open a PR.**
4. **xhigh-effort code review of the PR**, then apply the surviving findings with minimal edits.

### What shipped

A conflict card in the console currently shows size and upload time per side, but nothing indicating
which machine actually has more progress — a smaller, newer save can still be the one worth keeping.
This session added a derived file-count and newest-file-mtime stat to each side of a conflict.

Key finding that simplified the work: `SaveArchive.CreateArchive` has always written each zip entry's
`LastWriteTime`, so the stat didn't need a new upload-time computation or DB migration — it could be
derived from any already-uploaded archive on demand, retroactively, for free.

**New capability:** `SaveArchive.GetArchiveStats()` (Shared) → `VersionStatsDto` (Contracts) →
`SyncService.GetVersionStatsAsync` (a nullable-`gameId` overload used by both an agent-unscoped and an
admin-scoped route) → two new endpoints (`/api/versions/{id}/stats` agent-group,
`/api/games/{id}/versions/{id}/stats` admin-group) → console fetches lazily per open conflict, cached
by version id (immutable once uploaded), rendered as `"N files · newest change <time>"` on the
conflict card.

**Forward-looking design:** the agent-scoped stats route has no UI caller yet — it's there so a future
agent-side auto-resolver can reuse the identical authenticated call path instead of requiring new
server work when that migration happens.

### Code review and fixes

An xhigh-effort review (10 finder angles; 2 hit a subagent rate limit mid-run and were covered
manually, including a standalone .NET test that empirically confirmed the top finding) surfaced 7
findings, all fixed:

- The zip's `entry.LastWriteTime.UtcDateTime` silently used the *reading* process's timezone instead
  of the *writing* agent's — a real bug that could flip which conflict side looked newer. Fixed to be
  deterministic regardless of server timezone.
- A failed stats fetch permanently suppressed itself with no retry — fixed.
- The agent-scoped route had no test coverage — added.
- No server-side caching despite the archive-stats-never-change invariant the PR itself asserted —
  added an in-memory cache.
- Two competing ownership-scoping idioms and a misleading doc comment — the inaccurate claim removed.
- An unnecessary forwarding overload — removed.
- A comment that stated what instead of why, per this repo's CLAUDE.md — rewritten.

### Verification performed

- Clean `dotnet build --no-incremental` (Server + Agent), clean `npm run build` / `npm run lint` (web)
  — both before and after the review fixes.
- `openapi.json` regenerated from a live server and committed; `api-types.ts` regenerated from it —
  diff limited to the two new routes and one new schema.
- Live manual test: two throwaway machines, a forced real conflict, both stats routes confirmed
  correct, conflict card visually confirmed in-browser. Demo data cleaned up afterward.
- `tests/run-health-tests.ps1`: 22/22 passing (3 checks for this feature, including the new
  agent-scoped-route check), zero regressions.

### Outcome

- Branch `conflict-version-stats`, PR https://github.com/Marwanello/SaveLocker/pull/15.
- Backlog's conflict Tier 1 entry for this item removed; full technical write-up at
  `SaveLocker/logs/2026-08-24_conflict-version-stats.md`.
- `SaveLocker/CONTEXT.md` updated with a session narrative paragraph per the vault's session-handoff
  convention.

### Open follow-ups (not started)

- Actual agent-side auto-resolution logic (the Steam-Cloud-style migration itself) — this session only
  laid an endpoint the future work can reuse; no agent decision logic was written.

---

## Session Summary — 2026-08-26

Code review of merged fork PRs #1-10, fifteen findings applied, shipped as fork PR #16.

- **Branch:** `chunked-upload-integrity-and-review-fixes`
- **Commits:** `9d08e65` — *Make a chunked upload all-or-nothing, and the rest of the PR #1-10 review*
  · `6d7c7f3` — *Docs: record the PR #1-10 review and what its fixes cover*
- **PR:** https://github.com/Marwanello/SaveLocker/pull/16 (-> `main`, 16 files, +570/-55, MERGEABLE)
- **Base:** `e83da90` — *Fix: null-safe HasSteamCloud and keep /api/games' field names (#14)*

---

## The headline bug: a chunked upload could publish a truncated save

Introduced by PR #3, which added the chunked upload protocol so large saves survive Cloudflare's
fixed ~100s proxied-edge timeout: `begin` -> N x `chunk?offset=` -> `complete`.

The failure chain:

1. A chunk faults part-way through its body. The bytes already written stay on disk, **and are
   counted** — `BytesReceived` had advanced as the write progressed.
2. The client retries that chunk at its original offset. The server compares that offset against the
   inflated `BytesReceived`, sees it as *behind*, and treats it as a harmless duplicate replay.
3. The remainder of that chunk is therefore never written. The gap is silent.
4. If it was the final chunk, `complete` publishes the short file **under the content hash of the
   complete archive**. Every other machine then pulls it believing it is intact.

Two properties of this codebase shaped the fix, and both are worth remembering:

- **`ContentHash` hashes the save *folder*, not the zip** — it comes from
  `SaveArchive.HashDirectory`. The obvious fix ("re-hash the assembled file and compare against
  `ContentHash`") is therefore impossible without the server extracting the archive first.
- **A zip's central directory lives at the *end* of the file.** That is what makes "did every byte
  arrive" answerable server-side without re-hashing anything: a short assembly cannot produce a
  readable archive.

So `CompleteSession` now opens the staged file as a zip and reads its entry count before publishing.
Failure deletes the staging file and throws `CorruptUploadException`, which `Program.cs` maps to
**422** — deliberately not the 500 an unhandled throw would give, because the agent has to be able to
distinguish *"those bytes did not arrive intact, resend"* from *"the server is broken"*.

And `AppendChunkAsync` is now all-or-nothing: any fault mid-chunk rolls the file back to the offset
that chunk started at, and `BytesReceived` advances **only after the write is durable** — the count
is what a retry is judged against, so it must never describe bytes still in flight. Oversize is the
one case that aborts the whole session instead of rolling back, since retrying cannot help.

## Proving the tests catch it

30 new `CS-12` checks in `tests/run-server-bugbounty-tests.ps1`: byte-identical happy path,
`begin` no-change short circuit, whole-chunk replay, gap-ahead 409, cross-machine and unknown-session
404, truncation 422, a **mid-chunk fault driven through a raw `TcpClient`** that declares a
`Content-Length` it never delivers and then disconnects, and cumulative over-cap.

The suite was then run against a **reverted** `ArchiveStore.cs` + `Program.cs`: **8 checks fail**,
including *"the truncated session published no version"*. That is the part that matters — it shows
the pre-fix server genuinely did publish corrupt archives, so the suite is a regression test rather
than a tautology. Restored: **30/30**.

## The other fourteen findings

Chunk-retry logic in `ApiClient.cs` only caught `HttpRequestException`, but **`HttpClient.Timeout`
surfaces as `TaskCanceledException`/`OperationCanceledException`** — and `ServerHttp.Create` sets no
explicit timeout, so the 100s default applied. Retry now covers transport faults and 5xx/408, with a
real cancellation still passing straight through.

`SyncActivity.Persist` read shared state outside the lock and could write snapshots out of order;
it now snapshots under the lock and serialises writes behind a sequence number, with a timer to flush
throttled updates rather than dropping them. `POST /api/sync` gained a single-flight gate. The Linux
UI's sync call had the default 100s timeout on an operation that can legitimately exceed it.
`PathResolver.SafeChildDirectories` was re-enumerating directories per lookup and is now memoised.
Plus small UI fixes, two `testenv` safety guards (refuse to operate on a state dir that isn't
obviously a test dir), and repointing three `SkorcherX/SaveLocker` URLs to the fork.

## Gotchas hit

- **PowerShell 5.1 unrolls a `byte[]` returned from a function** into the pipeline, so it arrives as
  `Object[]` and `Invoke-WebRequest -Body` serialises it as a *string* — a 125-byte slice went out as
  a 377-byte body. Fix is the load-bearing leading comma: `return ,$s`.
- **Test fixtures must be incompressible.** The first fixture zip was ~376 bytes because repetitive
  text compresses away, which is far too small to exercise chunk boundaries. Now 300KB of seeded
  random bytes.
- **Kestrel's per-request `MaxRequestBodySize` and `ArchiveStore`'s cumulative `maxBytes` are
  different limits with different errors.** A single 3MB body against `MaxUploadMb=2` gets a 413 from
  Kestrel and never reaches `ArchiveTooLargeException`. Exercising the archive cap needs *two* chunks
  that are each under the request cap but together over the archive cap.
- **`PathResolver`'s case-insensitive fallback cannot be tested on Windows** — NTFS resolves the
  mis-cased path directly, so `Directory.Exists(exact)` succeeds and the fallback never runs. Those
  two "failures" are by design; verification moved to WSL.
- **`wsl -d Ubuntu -- bash -lc '...$VAR...'`** has `$VAR` eaten by the *outer* Git Bash shell before
  WSL ever sees it. Use literal paths.
- Line-ending drift again: a Python write left `ActivityCard.tsx` LF in a CRLF tree. Confirmed fixed
  by the diff shrinking to +1/-3 (whole-file churn would have shown as a full rewrite).

## Not done, deliberately

- **Retry on `/upload/{sessionId}/complete`.** The endpoint is not idempotent server-side — a second
  call hits `TryRemove` and 404s — so adding retry today would turn a lost-response *success* into a
  spurious hard failure. Needs the server to remember completed sessions first.
- **Repointing the Decky plugin URL** (`web/src/help/decky-plugin.md:28`,
  `src/Agent.Linux/DeckyPlugin.cs:66`). `Marwanello/SaveLocker-Decky` exists but has **no releases**,
  so the documented *Install Plugin from URL* flow would 404. The stale-looking URL is the working
  one; change it when the fork's plugin repo cuts a release.

Both are called out in the PR body.

## Note on the rebase

`origin/main` advanced mid-session (PR #14), touching `AgentApiServer.cs` and `testenv.ps1` — two
files this work also changed. Rebased rather than opening against a stale base. It applied without
conflict, but a clean auto-merge can still be semantically wrong, so it was checked rather than
trusted: both changesets confirmed present by inspection, PR #14's `HasSteamCloud` still appearing
10x in `AgentApiServer.cs`, the diff vs `origin/main` unchanged at 15 code files / +526/-55, all five
C# projects and `agent-ui` rebuilt, and `CS-12` re-run at 30/30.

---

## Session Summary — 2026-08-26/27 (per-file-delta-upload, PR #17)

### Task
Review the `per-file-delta-upload` branch (xhigh effort), apply the 14 findings with
minimal edits, then merge the branch and open a PR.

### Outcome
- **PR #17** open: https://github.com/Marwanello/SaveLocker/pull/17
  (`per-file-delta-upload` → `main`). Targeted `Marwanello/SaveLocker` explicitly —
  `gh` has no default repo here and `upstream` resolves to `SkorcherX/SaveLocker`.
- Two commits: `c054676` (14 fixes, code + tests), `5d7174d` (`Docs:` vault update).
- Later merged `origin/main` (PR #14/15/16) into the branch to resolve merge conflicts.

### The three serious findings
1. **Arbitrary file exfiltration** — server-controlled `NeedPaths` was archived
   unchecked. Fixed in `SyncEngine.SendPushAsync` (intersect + refuse/alert on any
   undeclared path) with `SaveArchive.CreateArchiveSubset` containment as a floor.
2. **Live-head archive could be destroyed** — the archive-deleting `catch` covered
   post-commit DB work. Restructured so only pre-commit failures un-publish.
3. **Reconstructed archive was unverified** — server now re-hashes the rebuild against
   the declared content hash and enforces `MaxUploadMb` before ingest.

### Notable structural moves
- Push decision tree moved `ApiClient` → `SyncEngine`, so the agent can reject rogue
  `NeedPaths` against the manifest it just sent.
- `ComputeManifest` returns manifest + aggregate hash in one pass (findings 7/9/10
  collapsed): the old `HashDirectory` second pass and the file-count floor deleted.
- `ValidateManifest` on the server rejects duplicate/rooted/`..`/empty/negative-size
  entries, caps at 100k; wired as a 400 on `POST /upload/begin`.
- Staging `.build` → `.part` so startup `SweepIncoming` reclaims crashed builds.

### Tests
- `run-delta-upload-tests.ps1`: 17 → 29 checks (RetryFull on head move, manifest 400s,
  hostile `../` NeedPaths refused). Agent suite 47/47 on a fresh server DB.
- Full `testenv.ps1 test` pass showed 2 pre-existing Linux-suite failures (Decky
  detection, untouched by this branch) and 4 pre-existing server-suite failures
  (WSL distro name hardcoded to `Ubuntu-24.04` in `tests/run-server-bugbounty-tests.ps1`
  while the installed distro is plain `Ubuntu`); neither cluster traces to this branch.

### Known gaps
- `MaxUploadMb` ceiling on the reconstruction rests on inspection, not a test.
- Not yet run against a real fleet.

---

## Session Summary — 2026-08-27 (`linux-regression-tests` worktree)

### What was asked

1. Review the new `run-linux-regression-tests.sh` (LA-04/05/06/07), fix bugs, wire it into
   `testenv.ps1 test`, run it, fix failures.
2. Investigate and fix every failure in a pasted full `testenv.ps1 test` run.
3. Fix the two pre-existing `run-linux-tests.sh` Decky-messaging failures — with an explicit
   constraint given mid-fix not to touch anything MoonDeck-relevant.
4. Dig into the long-standing intermittent `WA-01` dashboard-pull test and fix it if easy.

### What shipped

- **The new LA-04/05/06/07 regression script**, four real bugs fixed (a reverted field-name
  misdiagnosis, a missing `agent register` step, a `wait`-deadlock on the wrong PID set, and bash
  variables silently not expanding inside a single-quoted heredoc), now wired into `testenv.ps1 test`
  and passing 15/15.
- **A real product bug in `AgentCli.cs`'s `add-game`**: an unconditional `config.Save()` reopened a
  lost-update window that `SetTracked`'s own fresh re-read-under-lock had already closed for a
  brand-new game.
- **Two WSL-distro-name mismatches** (`Ubuntu-24.04` hardcoded vs. this machine's actual `Ubuntu`)
  in `run-server-bugbounty-tests.ps1` / `verify-password-compat.ps1`, fixed via a `$WslDistro` param.
  Fixed all 4 CS-01 failures (194/0, was 190/4).
- **A `testenv.ps1 sync` bug**: it skipped the git fetch+checkout step whenever the working tree had
  no uncommitted changes, so the persistent WSL clone could silently sit on an ancient commit.
- **The two pre-existing Decky "no plugin installed" messaging failures**, root-caused to an
  unrelated MoonDeck fixture directory (`make-fixtures.py`) that made a plain directory-existence
  check (`DeckyPlugin.DeckyPresent`) read `true` in the "no Decky at all" test section. Fixed with a
  cleanup line scoped to only the test harness's disposable fake `$HOME`; verified with a full rerun
  that every MoonDeck-dependent check still passed (237/0, was 235/2) before touching anything.
- **`WA-01`'s intermittent flake, reproduced and fixed.** Extracted just the WA-01 block (it needs no
  interactive desktop, unlike the tray tests later in the same file) and ran it directly against the
  already-built Windows binaries — it failed on the first live attempt, and debug output showed
  exactly why: the test's polling loop accepted `CommandStatus.Dispatched` (a lease, set the instant
  the agent *claims* a dashboard command — not a terminal state) as good enough, so a 1-second poll
  could catch the tiny window between "claimed" and "result reported" and grab `{result: null}`.
  Fixed to wait for the real terminal states (`Done`/`Failed`); also hardened the match to use the
  command's own id instead of "the most recent Pull server-wide". Reran twice after the fix: 10/10
  both times.

### Verification performed

- `run-linux-tests.sh`: 237/0 (was 235/2).
- `run-linux-regression-tests.sh`: 15/0.
- `run-server-bugbounty-tests.ps1`: 194/0 (was 190/4).
- WA-01, isolated and live on this Windows host: reproduced failing once, then 10/10 twice after the
  fix.

### Not done

- The rest of `run-winagent-tests.ps1` (WA-02 through the tray-stress blocks) was not rerun this
  session — those need a real interactive desktop, which this environment doesn't have.

---

## Session Summary — 2026-08-29 (PR #20 review + PR #21 SessionStart hook)

### What was asked

1. An xhigh-effort code review of [PR #20](https://github.com/Marwanello/SaveLocker/pull/20)
   (`save-conflicts-phase-0-1` → `main`), which moves conflict-resolution decisions from the server to
   the agent (Phase 0/1 of the Decky conflict-resolution plan).
2. Apply the 6 findings with minimal edits, treating the quoted finding text as a problem description
   rather than literal instructions, then report them back via `ReportFindings` with the original
   file/line/summary/failure-scenario echoed verbatim plus a fixed/no_change_needed/skipped outcome.
3. A `SessionStart` hook so Claude Code on the web has the toolchain to build the repo and run its test
   suites — implement directly rather than write a task doc, since it turned out not to be a big task.
4. Push the review-fix commit onto PR #20's *actual* head branch, not the separate workspace branch the
   fix work had used.
5. Cut a pure-kebab-case branch (no `claude/` prefix) from `claude/session-start-hook` and open a PR.

### PR #20 review: 6 findings, all fixed

- **`AgentCli.cs`** — `resolve-conflict --keep local` now advances the local parent pointer
  (`LastKnownVersionId`/`LastSyncedHash`) after a successful resolve, matching what the server's
  fan-out-skip optimization already assumes for the auto-policy resolver.
- **`SyncService.ResolveConflictAsync`** — the rewind guard could leave an auto-policy resolver stuck.
  An earlier attempt reassigned the winning version to the current head to dodge the refusal; caught in
  self-review that `SyncEngine.PushCoreAsync` assumes `ok=true` always means the resolver's *own*
  version won, which would have silently corrupted local state in exactly the race being fixed. Landed
  instead as: keep the refusal unconditional, but queue the stuck machine an unforced `Pull`.
- **`AgentApiServer.cs`** — three related gaps in the new local conflict routes: `sync-status`'s
  open-conflict lookup wasn't filtered to the calling machine, exceptions surfaced as unhandled 500s
  instead of the file's usual typed `ErrorResponse`, and a directory hash ran synchronously on a
  request thread. Fixed with a `MachineId` filter, try/catch on all 6 conflict routes, and `Task.Run`.
- **`SyncEngine.TryPolicyResolveAsync`** — catch blocks swallowed cancellation from a retiring engine;
  added `when (!ct.IsCancellationRequested)` guards to match the file's existing convention.

No .NET SDK was available in the review sandbox, so the fixes were verified by manual brace/paren
balance checking and careful tracing rather than a real build — disclosed as a limitation at the time.

### Landing the fix on the right branch

The fix had been committed on workspace branch `claude/pr-20-xhigh-review-lrcpdq` (`0551bb7`), but
PR #20 is backed by `save-conflicts-phase-0-1`. Cherry-picked cleanly onto that branch as `e98f5ef`
rather than force-pushing; the only wrinkle was a 3-line `agent-ui/src/api-types.ts` diff from
generation-environment schema-key ordering, resolved the same way an earlier commit (`778a874`) had —
regenerate on Linux for canonical order.

### SessionStart hook (`.claude/hooks/session-start.sh` + `.claude/settings.json`)

Gated on `CLAUDE_CODE_REMOTE=true`, so local sessions are untouched. Installs the `global.json`-pinned
.NET SDK via Ubuntu 24.04's own apt archive rather than `dotnet-install.sh` (its
`builds.dotnet.microsoft.com` download was flatly blocked by this container's network policy — a 403
on the proxy `CONNECT`), installs PowerShell Core from Microsoft's apt feed, restores only the
Linux-buildable projects (`src/Server`, `src/Agent.Linux` — the WinForms `src/Agent` can't restore on
Linux at all), and runs `npm install` in both `web/` and `agent-ui/`.

Validated live: a cold run and an idempotent re-run of the hook, `oxlint` clean in both frontends,
`dotnet build --no-incremental` clean for Server + Agent.Linux, and
`pwsh tests/run-hardening-tests.ps1` → 37/37 against a real running server.

### Outcome

- PR #20: 6 review findings fixed on its real head branch, commit `e98f5ef`.
- Branch `session-start-hook` (kebab-case) cut from `claude/session-start-hook`, pushed; PR
  [**#21**](https://github.com/Marwanello/SaveLocker/pull/21) opened (`session-start-hook` → `main`).

### Open follow-ups (not started)

- PR #20's `mergeable_state` was `unstable` at end of session — checks/CI hadn't finished settling;
  not investigated further this session.
- A real `dotnet build` verification of the PR #20 fixes is still outstanding (manual tracing only).
