# Backlog

Not-yet-done work only. Shipped items are indexed in `logs/shipped-2026-07.md` and
`logs/shipped-2026-08.md` (full detail in `logs/sessions.md`).

---

## High priority

- **Decky plugin: proper conflict resolution, not just refusals — plan expanded to 15 phases
  (0–14).** Scoped 2026-08-28, expanded 2026-08-30 (full design:
  `tasks/conflict-resolution-ui/plan.md`, moved there 2026-08-30 from `logs/` since Phases 4–14 are
  still open work — see that folder's `README.md`). **Phase 0/1 shipped 2026-08-29** (server +
  Agent.Core, this repo only — see below). **Phase 2 (the Force push/pull bookkeeping fix) and Phase 3
  (the dashboard Backups tab) both shipped 2026-08-30, and Phase 5 (Linux environment-capability
  detection) shipped 2026-08-30 in the same session the plan was expanded, and Phase 4 (Linux wrapper
  launch gate) + Phase 6 (shared `agent-ui` conflicts page) shipped 2026-08-31 together as
  `implementation-grouping.md`'s "Group 2", and Phase 9 (D-Bus desktop notification) shipped
  2026-09-01 as that document's "Group 3"** — see below. **Phases 7, 8, 10, 11, 13–14 remain not
  built** (Phase 12's endpoint exists but has no consumer yet — see below). Before Phase 2, the
  plugin's only way past
  a stuck sync was Force push/pull, which bypassed the server's own conflict bookkeeping — an orphaned
  `ConflictFlag`, an unprotected losing version, a stranded other device. The plan reframes conflicts
  as local-vs-cloud (never device-vs-device), moves the actual resolution *decision* into the shared
  agent engine (the server becomes a passive store that also files the losing save away as a separate,
  downloadable backup rather than mixing it into the normal version history), and adds a real
  conflict-aware chip and resolve popup to the Deck's library page and QAM. A deliberate behavior
  change from today's "always launch" philosophy: a *certain* conflict now cancels the launch, shows
  the popup, syncs the choice, and relaunches automatically — no second Play press. Playnite gets the
  equivalent treatment via the SDK's `IPlayniteAPI.StartGame`.
  <br>**Expanded 2026-08-30, asked directly**: whether any phase built a Decky-equivalent resolve
  popup on plain Windows or plain Linux — no Decky, no Playnite. It didn't; the answer had existed
  since the earlier fork-blind 8-document pass (`tasks/conflict-resolution-ui/reference/
  03-platform-ux-flows.md`'s Linux
  escalation ladder + Windows in-app chooser) but had been left as reference material, not folded into
  execution. Five new phases now close that gap: **Phase 5** (Linux environment-capability detection —
  Wayland/X11 session, D-Bus, notification daemon, TTY), **Phase 6** (a shared `agent-ui` conflicts
  page, served by both the Windows tray and the Linux daemon at `:5178` — also closes a confirmed gap
  where `doctor` never actually gained the conflict line the original plan called for), **Phase 7**
  (Windows tray automatic chooser + bulk "apply to all remaining" queue — the first real popup-at-
  launch-time experience for a plain Windows user), **Phase 8** (a native Linux **Game Mode conflict
  screen** in `savelocker ui`, drawn with the existing Dear ImGui stack — the direct, literal
  Decky-equivalent popup with zero Decky dependency), and **Phase 9** (an optional D-Bus desktop
  notification — tooling decision made 2026-08-30: `gdbus`, shelled out, not `Tmds.DBus` or a
  hand-rolled client; see `tasks/conflict-resolution-ui/plan.md` Phase 9).
  The old Phase 5–8 are renumbered **10–13** (content unchanged); a new **Phase 14** folds in the
  optional webhook/ntfy notify and a per-game "block launch" opt-in that were previously a separate
  open question. Full dependency graph, verification plan, and file-level detail for every new phase:
  the design doc's *Implementation phases* section and its *Scope note*. Phased; several phases are
  independently mergeable or can proceed in parallel — see the doc.
  <br>**Phase 5 shipped 2026-08-30** (same session as the expansion above, `tasks/conflict-resolution-ui/
  implementation-grouping.md`'s "Group 1"): new `DesktopEnvironment.Detect()` in `src/Agent.Linux/`
  answers whether a graphical session, a genuinely connectable D-Bus session bus, a notification
  daemon (via `gdbus`'s `NameHasOwner`), an interactive terminal, and the `systemd --user` unit itself
  are each present — surfaced as a new, purely informational "Session" section in `doctor`. The
  `gdbus` subprocess call is bounded to a 2s timeout (a stale/half-started bus can accept a connection
  and never complete the handshake; this must never hang `doctor` or, later, the launch wrapper).
  `tests/linux/run-linux-tests.sh` gained 9 checks covering all five flags, including a real UNIX
  socket with nothing implementing the D-Bus protocol behind it, to prove `HasSessionBus` and
  `NotificationDaemonPresent` genuinely disagree in that case rather than one silently implying the
  other. 246 total (239 passed + 9 new, replacing the prior 237/237 baseline's 0 failures — see the
  pre-existing-failure note just below, unrelated to this change).
  <br>**Phase 9's tooling decision made, not yet implemented** — see the plan doc's Phase 9 for the
  full reasoning (`gdbus` over `Tmds.DBus` or a hand-rolled client).
  <br>**Phase 12 corrected, pulled back out of "Group 1," and left open**: it was about to be wired to
  a passive `agent-ui` poll before actually reading `AgentApiServer.cs:696`'s handler — it hashes the
  entire local save folder on every call ("NOT cheap on disk," its own comment says) and calls the
  same `GetStateAsync` `AgentCli.cs`'s existing `status` command already uses, so it is *more*
  expensive than what a per-game status loop does today, not a lighter replacement. `plan.md` and
  `implementation-grouping.md` are both corrected; a consumer still needs a genuine on-demand trigger
  (a button, a CLI flag, a specific decision point) rather than a timer, decided when Phase 6, 8, or
  10 actually builds one.
  <br>**Group 2 shipped 2026-08-31** (`tasks/conflict-resolution-ui/implementation-grouping.md`'s
  "Group 2" — Phase 4 + Phase 6 together, the grouping document's own "biggest value-per-session"
  pick, chosen because it makes conflict resolution end-to-end usable on a plain Linux box with zero
  hardware dependency): **Phase 4**, `SyncEngine.PrepareLaunchAsync` (new `LaunchDecision`/
  `LaunchGateResult`) replaces `ProtonRun.ExecuteAsync`'s bare `OnGameLaunchAsync` call for the Steam
  launch wrapper specifically (Windows' `TrayApp.cs` keeps calling `OnGameLaunchAsync` unchanged —
  Phase 7, still deferred, is what would touch that). Blocks the launch outright for exactly one
  reason — a genuinely confirmed, open `ConflictFlag` — never for the game running elsewhere, lock
  contention or a network hiccup (all still `ProceedSyncPaused`/`Proceed`, unchanged). Two checks: an
  already-open conflict short-circuits before the lease is even taken, and "commit-before-choose" —
  an ordinary push attempted first, cheap in the overwhelmingly common case (a hash-only bail, no
  network call, when nothing changed since the last push) — turns unsynced local changes into a real,
  hash-verified conflict instead of a bare pull refusal with nothing recoverable to show for it. New
  `AgentEventCodes.LaunchBlocked` event. **Phase 6**: new `agent-ui/src/components/ConflictsView.tsx`
  (genuinely new code against the agent's own local API, not a port of the dashboard's card, though
  the visual shape is deliberately copied from it), a fourth `Sidebar.tsx` nav entry badge-numbered by
  open-conflict count, and two new server routes (agent-group `GET /versions/{id}`, mirroring the
  existing `/versions/{id}/stats`) + two new local-API proxies (`GET /api/versions/{id}`,
  `GET /api/versions/{id}/stats`) so a conflict card can show machine/timestamp/size and file-count/
  newest-change for both sides — a conflict itself only ever carried version ids. Also closes the
  confirmed `doctor` gap: a new "Conflicts" section, one line per open conflict plus the
  `:5178/#conflicts` URL, informational (not a `Problem`) the same way an open conflict already reads
  in the console.
  <br>**Verified live, not just built**: a real two-machine conflict seeded against a throwaway dev
  server (register, add-game, diverging pushes, genuine `ConflictFlag`); `doctor` confirmed printing
  the new Conflicts section correctly; a Playwright screenshot of the real `agent-ui` page at
  `:5178/#conflicts` showing both sides with the right labels (including "This device (Machine2)"),
  clicking "Use as Latest" resolved it through the real server (head moved, badge cleared, empty
  state rendered); `savelocker run` confirmed to refuse a launch outright on a genuine open conflict
  (child process never started, exit code 1) and to launch normally once resolved, and confirmed
  lease contention alone (no conflict) still only warns and launches. `run-agent-tests.ps1` 45/45 and
  `run-health-tests.ps1` 22/22 unchanged (this Linux container cannot build/run the Windows-only
  `run-winagent-tests.ps1`/`run-server-bugbounty-tests.ps1`, which hard-depend on `src/Agent`).
  `openapi.json` and both `api-types.ts` (web, agent-ui) regenerated against live scratch instances
  and diffed — only the intended new route/types appear. Full solution's Linux-buildable projects
  (Shared, Server, Agent.Core, Agent.Linux) build clean; `agent-ui` `tsc -b && vite build` and
  `oxlint` clean (two pre-existing unrelated warnings in `AddGamesView.tsx`/`SettingsView.tsx`).
  <br>**A pre-existing, unrelated regression found while verifying Phase 5, not caused by it**: the
  last recorded `run-linux-tests.sh` baseline was 237/237 (`logs/sessions.md`, 2026-08-27); it is now
  237/230-passing — 7 failures, all in the "Decky plugin updates" section (a top-level-file package
  refusal path), confirmed via `git stash` to already fail on the code as it stood *before* this
  session's Phase 5 changes. Not investigated further here — out of this phase's scope — but worth a
  session that owns test-suite maintenance, similar to the CS-03 `FileShare.None` flake noted
  2026-08-29. **Root-caused and fixed 2026-09-01 — see the Group 3 entry below.**
  <br>**Phase 0/1 detail:** `SyncService.IngestAsync` no longer evaluates `ConflictPolicy` itself —
  every divergence unconditionally records/updates a `ConflictFlag`; `SyncEngine
  .TryPolicyResolveAsync` is where the decision moved (fetches the game's policy after a push comes
  back `Conflict`, and — unless it's `Manual` — calls the same mechanical resolve endpoint a human's
  choice would use). `ResolveConflictAsync` gained a `resolverMachineId` parameter so an agent
  resolving its own push doesn't get a redundant pull queued for itself, while the admin/console path
  (passing null) still tells every machine, exactly as before. New agent-group routes
  (`GET/POST /api/agent/conflicts[...]`, `GET/POST /api/agent/games/{id}/conflict-policy`) and
  matching local-API proxies on `:5178` (`/api/conflicts[...]`, `/api/games/{id}/conflict-policy`,
  `/api/games/{id}/sync-status` — decision 6's cheap status check, built alongside since it touches
  the same files). New CLI commands `conflicts` and `resolve-conflict --keep local|cloud
  [--keep-both]`, added so the doc's "usable end-to-end from the CLI alone" claim for this phase is
  actually true (it wasn't, when first drafted — the doc had described the value without the commands
  existing). Verified: `run-agent-tests` 47/47, `run-server-bugbounty-tests` 194/194 (including the
  `NewestWins` auto-accept, the "winning uploader gets no redundant pull" assertion specific to this
  change, and the admin Set-as-Latest fan-out unchanged), `run-health-tests` 22/22; `openapi.json` and
  both `api-types.ts` (web and agent-ui) regenerated and diff-checked; full solution + both frontends
  build clean. Not yet run against real hardware or a live fleet — this phase has no UI of its own,
  so nothing changes for a user until a later phase's GUI work lands.
  <br>**Phase 2 detail (server-side only, no new routes or DTOs):** `PrepareUploadAsync` still skips
  divergence detection outright when `force:true`, so a forced push could land while the game already
  had an open `ConflictFlag` from an earlier, unforced divergence — moving the head to a brand-new
  version that is neither side of that conflict. Left alone, the flag was orphaned:
  `ResolveConflictAsync`'s own rewind guard refuses to promote either of its two versions once
  something newer already won the race, so nothing could ever close it through the ordinary path
  again, neither version was protected from ordinary retention even though it was now the only record
  of the losing side, and the other machine involved was never told the head had moved.
  `IngestAsync`'s fast-forward branch now calls a new `CloseOrphanedConflictsOnForceAsync` whenever
  `force` is true: it closes every open conflict on that game (`Resolved`, tagged `"force-push by
  {machine}"`), protects **both** sides of each as recoverable backups (the same guarantee a human's
  explicit "keep both" gives — nobody was actually asked here, so nothing is silently lost), and
  queues the fleet the same unforced pull fan-out a real resolve gets. A no-op when there's nothing
  open, so an ordinary forced push looks exactly as it did before. Verified with a new `CS-13` section
  in `run-server-bugbounty-tests.ps1` (open a real conflict, force past it, confirm the flag closes,
  both sides protect, the resolution is audited, the stranded machine gets an unforced pull and the
  forcing machine doesn't, the stranded machine's next push is clean, and a control case proving a
  force push with nothing open adds no bookkeeping) — 207/207, up from 194; `run-agent-tests` 47/47
  unchanged. No API/DTO changes, so `openapi.json`/`api-types.ts` needed no regeneration. Not yet run
  against real hardware — like Phase 0/1, invisible until a later phase's GUI work lands.
  <br>**Phase 3 detail (client-side only, no new server endpoint):** a version is "in the main tree"
  if it's an ancestor of the current head — walk `parentVersionId` back from it; anything else the
  game still has is a backup, almost always the losing side of a past conflict. `GameDetail.tsx`
  already fetched the full version list and the head id, so this is a purely computed split of data
  already on hand — no new column, no new route. The old single "Versions" table is now a
  `Versions (N)` / `Backups (N)` tab toggle over the same table and the same per-row actions
  (Download, Set as Latest, Protect/Unprotect, Delete) — promoting a backup back to head via Set as
  Latest also resolves any open conflict it was part of, for free, via the existing `SetHeadAsync`
  superseded-conflict logic. Verified live: seeded the exact Phase 2 scenario (force-pushed past an
  open conflict) against a throwaway dev server, confirmed in the browser that Versions correctly
  shows the head's 3-version ancestor chain (one of them still `Protected` from Phase 2) and Backups
  correctly shows the one truly orphaned version with all four actions present; no console errors.
  `web` build and lint clean.
  <br>**Group 3 shipped 2026-09-01** (`implementation-grouping.md`'s "Group 3" — Phase 9's actual
  D-Bus notification implementation, the only phase that group names, since its real dependencies
  are Phase 5 and Phase 6, both already done): `CommandPoller` (Agent.Core) gained an optional
  `onConflictsPolled` hook, fired once per 20s tick with this machine's open conflicts (filtered to
  `MachineId`, the same "no bystander case" filter `doctor`/`savelocker conflicts` already apply) —
  `null` by default so the Windows agent's tick is unchanged (Phase 7 is what would wire the tray to
  this). New `src/Agent.Linux/ConflictNotifier.cs` is rung 3 itself: a `HashSet<Guid>` notifies once
  per conflict id the moment it opens and forgets ids that close (not a timer), gated on Phase 5's
  `NotificationDaemonPresent` so the common no-desktop case costs nothing beyond a set comparison,
  and fires `org.freedesktop.Notifications.Notify` via `gdbus` with a "View conflict" action button.
  A lazily-started `gdbus monitor` (one for the process's life, started on the first notification
  actually sent) listens for the matching `ActionInvoked` signal and opens Phase 6's `agent-ui`
  conflicts page via `xdg-open` — deliberately does not bring Phase 8's (unbuilt) Game Mode screen to
  the foreground, the same way Phase 4 left Windows' tray untouched for Phase 7.
  <br>**A real bug caught before shipping**: the first draft's GVariant call arguments were bare or
  naively single-quoted, which breaks the instant a game name contains an apostrophe (Baldur's Gate 3,
  Assassin's Creed) — extremely common, not an edge case. Fixed with a `GVariantString` helper
  (double-quoted, `\`/`"`-escaped) plus explicit type annotations gdbus can't reliably infer without
  introspection (`uint32 0` for `replaces_id`, `@a{sv} {}` for empty hints).
  <br>**Verified**: all Linux-buildable projects build clean. `run-linux-tests.sh` extended the
  existing `deck_cfg`/`other_cfg` two-machine fixture into a genuine conflict, started a daemon on the
  stuck machine, and confirmed the log shows the conflict noticed on the next tick — correctly gated
  to the "no notification daemon reachable" branch, since this sandbox has no real D-Bus session bus,
  same as CI. 244 passed. The 7 failures alongside it were the **same pre-existing Decky-plugin-update
  flake already recorded above** (Phase 5's entry) — reconfirmed via `git stash` against this session's
  own pre-change commit: identical 7 failures, identical names, 240 passed without this change, 244
  with it (240 + 4 new checks) — not a regression this work introduced.
  <br>**Then root-caused and fixed, same session, asked directly ("can we fix the 7 failures while
  we're at it").** All 7 were one cause, not seven: the "package needing a new top-level file is
  REFUSED" case (`DeckyPlugin.cs`'s `CanReplace`, the check that a root-owned-755 plugin directory
  refuses a genuinely new file while still allowing an existing one to be overwritten) simulated that
  constraint with `chmod 555`, and this suite's own dev/CI container runs as **root** — root's
  `CAP_DAC_OVERRIDE` bypasses ordinary permission bits entirely, so the simulated-refusal write
  silently succeeded instead of being blocked, corrupting the plugin's on-disk version for every
  assertion downstream that read it (the other 6 failures were that one bad write cascading, not
  independent bugs — confirmed by their names: two read the version directly, the rest already passed
  once the first assertion in the chain did). Fixed by swapping the simulation to the ext4 immutable
  attribute (`chattr +i`/`chattr -i`), a filesystem-level restriction `CanReplace`'s write-probe cannot
  bypass even as root — confirmed directly before changing the test: creating a NEW file under an
  `+i` directory fails for root exactly as it would for an unprivileged user, while overwriting an
  EXISTING file's content still succeeds, since that never touches the directory's own inode. This
  reproduces the real root-owned-755 constraint regardless of which user runs the suite, so it's
  correct for both this sandbox and a normal non-root CI runner. `run-linux-tests.sh` 244/251 (7
  failing) → **251/251**.
  <br>**Not yet run against real hardware** — same open question `plan.md`'s 2026-08-31 correction
  already named: whether a real `Notify` call renders a visible popup in Desktop Mode and Game Mode
  alike, and whether the action button's click actually opens the browser to the right page. Needs a
  live Deck Desktop Mode session or a plain desktop Linux box — the plan's own stated lower priority
  for scripted D-Bus coverage, unchanged by this session.
  <br>**Run on the real Deck 2026-09-02 — the shipped version did not work, and is fixed.** Desktop
  Mode is now a *verified* surface rather than an assumed one, but only after the transport was
  replaced. The `gdbus call` version (plan.md's own Phase 9 tooling pick) reported exit code 0, a real
  notification id and `conflict notification: sent for 'X'` in the log, while the popup vanished within
  a second: **a notification carrying `actions` is owned by the bus connection that sent it, and the
  server withdraws it when that connection drops** — and `gdbus call` is one-shot, so it can never
  hold one open. Bisected on the hardware, one argument at a time: timeout `0` alone persists,
  app-name/icon alone render, the **action button** is what makes it flash. Rewritten around
  `notify-send --wait --action` (confirmed present on the Deck first): `--wait` keeps the connection
  alive for the life of the notification and prints the clicked action key on stdout, so the separate
  `gdbus monitor` connection is **deleted** rather than fixed — one mechanism instead of two, no
  GVariant hand-escaping, and killing the child now withdraws a stale popup when the conflict is
  resolved elsewhere. `gdbus` remains Phase 5's `NameHasOwner` probe, untouched.
  <br>**New regression coverage, proven to bite**: a fake bus socket + fake `gdbus` + fake
  `notify-send` that records its argv, asserting `--wait`, the action key and the game name. Verified
  to FAIL against the pre-fix code by removing `--wait` and re-running, not assumed. `run-linux-tests`
  251 → **256/256**. The transferable lesson: *an exit code from a fire-and-forget IPC call is not
  evidence the other end did anything* — this passed a clean build, a full suite and a careful read
  because nothing checked what was actually sent.
  <br>**Still open on this phase:** Game Mode rendering (unchanged, unknown), and confirming
  `xdg-open` lands on the conflicts page on the Deck specifically.

**All three bug bounties shipped in v0.5.0 (2026-07-29).** Code is on `main`; what remains is the
verification that did not happen before the tag. Write-ups:
`logs/2026-07-29_winagent-bugbounty.md`, `logs/2026-07-29_linuxagent-bugbounty.md`,
`logs/2026-07-27_console-bugbounty.md`.

- **v0.5.0 post-release verification.** Ordered by what carries the most risk of the release notes
  being wrong:
  - **Deck verification** — the five scenarios in `logs/2026-07-29_linuxagent-bugbounty.md` →
    Verification. Hardware available since 2026-07-19. Fold in the two v0.5.1 Deck fixes while
    there (one ring on open with A working; a resting cursor must not paint a second selector) and
    a real save-path detection check now that `<base>` resolves from `StartDir`.
  - **Second-Windows-account ACL test (WA-03).** The one with a user-visible consequence: the
    credentials are ACL-locked to the enrolling account and asserted against the well-known SIDs,
    but no second account has ever tried to read them, and it is unconfirmed that the enrolled user
    can still sync *and take a silent update* after a reboot. **v0.5.0's notes describe the change
    rather than promising the guarantee, and Known Issues says so — reword
    `web/src/releases/0.5.0.md` once this passes.** That file is both the console page and the
    GitHub Release body, so one edit fixes both.
  - Remaining Windows gates: fresh-VM install, a real game, a real non-Steam Steam shortcut, and the
    first-run Settings deep link on a cold WebView2 profile (the automated test drives the same code
    path through a refused launch, because the prompt is a modal dialog no test can answer).
  - **LAN enrollment-URL check** on the real deployment (`logs/2026-07-27_console-bugbounty.md` →
    Verification).

- **v0.5.4 surfaces that shipped without hardware coverage.** Neither can lose save data — worst
  case is a list that filters oddly — which is why they shipped, but both are unverified: the Heroic
  **store** sub-chips (the test Deck has no Heroic games, so the chip correctly never rendered) and
  the Game Mode filter row's gamepad navigation. Nothing drives the agent-ui React chips in any
  suite either.

- **Emulator saves.** Not implemented at all: RetroArch, Dolphin, PCSX2, DuckStation and friends
  keep saves and save-states in their own per-emulator trees, and the Ludusavi manifest does not
  describe them, so nothing in discovery can find them today. On a Deck this is a large share of
  what people actually play. Once it lands, add an **Emulator** filter to the Add Games chip row
  (`agent-ui/src/components/AddGamesView.tsx` → `FILTERS`, and the Game Mode mirror in
  `Ui/UiApp.cs` → `AddFilter`) — the row is built to take another entry. Note the save-variant
  problem from Decisions.md §1 applies here too: an emulator save is platform-neutral, so it is
  the first candidate that could sync between a Deck and a Windows PC without Proton involved.

- **One game, several real sources — the dedupe rule needs a second dimension, and the choice needs
  to be visible.** Scoped 2026-08-19; **step 1 (the tie-break bug) shipped the same day**, steps 2–4
  remain. Asked for directly, using Cyberpunk as the example:
  MoonDeck's streaming shortcut and a genuinely-installed copy (Steam, or Epic/GOG through Heroic) can
  both legitimately exist for the same game on one device, and the question was three-fold — what to
  do about it, whether the dashboard should even know, and whether multiple local sources should
  enroll under one title.
  <br>**A concrete bug first, exposed by today's own MoonDeck fix.** `LinuxGameScanner.ScanAsync`'s
  final dedupe (`GroupBy(NormalizeName).OrderByDescending(has-a-save-dir).ThenBy(HasSteamCloud)
  .First()`) ties on `HasSteamCloud`, and a `SteamShortcut` candidate is *always* `HasSteamCloud:
  false` — "the same game can be both an installed Steam title and a shortcut to a DRM-free build;
  enrolling the one Cloud doesn't cover is the more useful pick," which is sound when the shortcut
  really is an alternate, independent build. Before today, a MoonDeck shortcut never resolved a save
  dir at all, so it never reached that tie — the first sort key (has-a-save-dir) already picked the
  genuine install correctly, for the wrong reason. Now that MoonDeck shortcuts resolve (this session's
  fix), if the SAME game is ALSO genuinely installed with real Steam Cloud, both candidates resolve, the
  MoonDeck one's hardcoded `false` wins the tie, and the candidate that survives is a streaming
  *pointer*, not the local install — recording the pointer's own synthetic AppID as `SteamAppId`, which
  the launch wrapper will only ever see during a MoonDeck session, never a genuine local one. The DRM-free
  reasoning the rule was built for doesn't apply to a pointer at all; it needs its own signal (e.g.
  `SteamShortcut.MoonDeckAppId is not null`, already known from this session's fix) rather than reusing
  `HasSteamCloud`. Small, precise, worth fixing on its own rather than folding into the larger piece
  below.
  <br>**The larger piece: discovery currently decides and hides, rather than showing its work.** The
  GroupBy dedupe silently picks ONE candidate per normalized name and the user never sees that there
  were two or three — not in `scan`, not in the Add Games UI, not in `doctor` (today's duplicate-AppID
  note only compares raw `shortcuts.vdf` entries against each other, never a shortcut against a
  SteamInstalled or Heroic candidate of the same name — the exact Cyberpunk shape asked about here
  would currently say nothing). That was a reasonable default when the "duplicate" was almost always
  genuinely redundant (Steam Cloud already covers the installed copy). It stops being reasonable once a
  "duplicate" can be a mere pointer to the SAME data, or genuinely divergent — two real, independently-
  played installs (Steam Cyberpunk at hour 20, a fresh Epic install never launched) that a silent pick
  would make one of them invisible.
  <br>**Should the dashboard handle it?** Only the part it already does, and no schema change: the
  server already converges "the same game from different machines" correctly via
  `ManifestLoader.CanonicalName` — one `Game`, many `MachineSavePath` rows, exactly the shape it needs.
  The new question — several local sources for the same game *on one machine* — is a discovery-time,
  agent-side concern, not a server one: it never needs to know a choice was made, only which single path
  this machine currently maps, precisely what it already stores. Pushing "which of several local copies"
  into the server model would be solving a local problem non-locally for no benefit.
  <br>**Should multiple local sources enroll to one title?** Yes to one title, no to each syncing
  independently — and the two cases genuinely differ in risk, worth telling apart from the native-Linux
  item above rather than solving the same way. Same-platform duplicates (Steam vs Epic vs GOG, all
  Windows/Proton, or a MoonDeck pointer at one of them) are the SAME underlying build's save format
  regardless of storefront — safely interchangeable, so "one title, one active local path, switchable"
  is enough; the mechanism already exists (`add-game --name X --dir <other-path>` re-maps today, it's
  just not discoverable). Cross-platform duplicates (Windows/Proton vs native-Linux) are NOT
  interchangeable — that is the whole reason the native-Linux item above needs `Game.Platform`
  isolation. Conflating the two would either over-restrict the safe case or under-protect the unsafe
  one.
  <br>**Proposed shape:**
  1. ~~Fix the tie-break: a MoonDeck-resolved candidate loses to a genuine SteamInstalled/Heroic
     candidate of the same name when both resolve, regardless of `HasSteamCloud`.~~ **DONE, same
     day.** `ScanAsync` now carries a local `ViaMoonDeck` flag per shortcut candidate (true only when
     `SuggestSaveDirAsync` resolved through the MoonDeck fallback prefix, not the shortcut's own) and
     sorts it ahead of the `HasSteamCloud` tie-break. Pinned by a new fixture (`Fake Dual Source
     Game`: genuinely installed with real Steam Cloud, also pointed at by a MoonDeck shortcut) —
     `run-linux-tests.sh` 227 → 230. Confirmed no regression on the real Deck across all four games
     from the sessions above.
  2. Extend the existing duplicate-AppID `doctor` note to compare ACROSS `ScanSource`s, not only within
     raw shortcuts — the Cyberpunk shape (shortcut vs. genuinely installed) is invisible to it today.
  3. Record what discovery is doing, not just its result: when the dedupe collapses more than one real
     candidate, keep the runner-up(s) — visible in `doctor`, and in the Add Games UI as a small
     "N other copies found" affordance on the row, so switching later is a menu pick instead of a
     remembered CLI flag.
  4. No new field is required to make a manual switch possible — `add-game --dir` already re-maps an
     existing tracked game. Step 3 is entirely about discoverability, not a missing mechanism.
  <br>**Also closes a stale pointer:** `CONTEXT.md`'s Next action list has referenced "Check the live
  server for duplicate games ([[Backlog]])" without a matching entry existing here — the server-side
  half of that (merging two Games that already diverged under different spellings) is a real, separate
  gap from the agent-side discovery question above and still has no plan; noted here so the pointer
  resolves to something.

- **Native Linux save support** — sync a native-Linux-built game's save alongside its Windows/Proton
  counterpart, without letting the two formats corrupt each other. Asked for directly 2026-08-19,
  same session as the fixes above: "I want the freedom to just store it in the dashboard and try — if
  it works ok, if it doesn't I have my OS save already there." Scoped, not built.
  <br>**The data already exists for this.** The SAME manifest SaveLocker downloads carries `os: linux`
  save-path entries — seen directly while investigating A Short Hike and Roadwarden today:
  `<xdgConfig>/unity3d/adamgryu/A Short Hike/GameSaveNew.mountain`, `<home>/.renpy/roadwarden`.
  `Decisions.md` §1 keeps native Linux out of scope for a SYNC-safety reason, not a detection one: "a
  Proton save is byte-identical to a Windows PC's, so the existing content-hash lineage works with
  zero schema change" — a native Linux build's save format carries no such guarantee, so restoring a
  Linux-format save over a Windows-format one (or the reverse) risks a save the target game can't read
  or silently misinterprets.
  <br>**The user's own framing narrows the real risk usefully.** PUSHING a save is a pure read —
  archiving whatever a native Linux build wrote can never damage anything, whatever the format turns
  out to be. The danger is entirely on PULL, which overwrites live files. Discovery and backup can be
  as exploratory as the user wants; only restore needs to be made safe.
  <br>**The footgun to design around, found by reasoning through it rather than by hitting it:**
  enrollment already makes "the same canonical manifest name" auto-join one Game
  (`ManifestLoader.CanonicalName` — "makes both machines converge" is the whole point of it). Add
  native-Linux discovery with no other change, and a Windows PC and a Linux-native Deck both playing
  "A Short Hike" would silently join the SAME Game the first time either enrolled it — exactly the
  cross-format mixing `Decisions.md` warns against, by DEFAULT, not as an edge case.
  <br>**Proposed shape**, mirroring `tasks/LinuxAutoUpdate.md`'s multi-session structure:
  1. *Detection* — `PathResolver` gains a native-Linux host resolver (parallel to `.Windows()`) with
     the XDG tokens the manifest actually uses (`<xdgData>`/`<xdgConfig>`, same `$XDG_*`-or-fallback
     pattern `HeroicRoots.Find()` already has, just not centralized). `ManifestLoader` gains an
     `IsLinuxSave` filter parallel to `IsWindowsSave`, and `ResolveSaveDirectories` takes which one to
     apply instead of hardcoding Windows. Telling a native install from a Proton one apart isn't
     reliable from Steam's ACF alone (Steam can silently offer either build for the same game), so
     `LinuxGameScanner` should try BOTH resolvers and surface whichever actually resolves — the same
     "report the disagreement, don't guess" instinct behind Heroic's split-prefix note — rather than
     trying to classify the install up front.
  2. *Data model — keep them from ever merging.* A `Game.Platform` field decided at creation; the
     uniqueness key auto-join relies on becomes (canonical name, platform), not canonical name alone.
     This has to land BEFORE step 1 reaches anyone with the same game on two different platforms, or
     the footgun above is live from day one. Two Games can share a display name; they must never share
     a version lineage.
  3. *Pull-time guard, belt-and-braces.* Even with #2 preventing the dangerous case structurally, check
     platform again immediately before restore — mirrors `SavePathGuard`'s own "validated at five entry
     points, re-checked again right before use" pattern, and this codebase's standing preference for
     defense in depth over trusting one earlier gate.
  4. *UI* — a badge, not a name suffix: two cards both titled "A Short Hike" with a distinguishing tag
     reads better than inventing a second string identity for one real game. The Add Games filter row
     already has the right slot (`FILTERS` in `AddGamesView.tsx`, `AddFilter` in `Ui/UiApp.cs`).
  <br>**Related — read together with Emulator saves, above.** An emulator's own save-state format is
  genuinely platform-neutral (RetroArch's format doesn't care what OS RetroArch runs on), which is why
  that item was already flagged as the first thing that could sync Deck↔Windows with no Proton
  involved. A native-Linux GAME build carries no such guarantee — its save format is whatever the
  developer's Linux port happened to use, unrelated to the Windows build's — which is exactly why this
  item needs the platform-isolation step (#2) that emulator saves may not.
  <br>**Deliberately not attempted in phase 1:** cross-format conversion or merging — nothing about the
  manifest or the archive format gives any basis for that, and guessing at compatibility between two
  save formats is precisely the mistake `logs/2026-08-19_moondeck-save-detection.md` argues against
  throughout.

- **Multiple save paths per game.** Scoped 2026-08-20 (full research write-up:
  `logs/2026-08-20_wine-case-insensitive-and-scoping.md`), not built — this is a maintainer scope
  decision (build the schema work, or keep today's single-path limitation), not something to just do.
  Some manifest games list more than one `files:` template — `ManifestLoader.ResolveSaveDirectories`
  (`src/Shared/ManifestLoader.cs:181`) already loops over every one of them and returns a full
  `IReadOnlyList<string>` — but essentially every real caller (`LinuxGameScanner.cs:279`,
  `AgentCli.cs:195` and `:672`, `CommandPoller.cs:237`, `GameScanner.cs:301`) immediately reduces that
  to `.FirstOrDefault()`, because the storage model the rest of the system is built on is single-path
  end to end, all the way down to the database's primary key.
  <br>**The design risk that gates everything else.** `ResolveSaveDirectories`'s own doc comment
  already carries a cautionary tale directly on point: DRAGON QUEST III has two manifest templates for
  the same game — one resolves to the real save folder, the other to a sibling `Config` folder — and
  this codebase was already burned once treating "multiple templates" as "resolve all of them," which
  silently picked the wrong one via `HashSet` ordering. **The manifest format does not disambiguate
  between "alternate locations, pick the one that resolves" and "complementary locations, sync all of
  them that resolve."** Stopping the `.FirstOrDefault()` calls is not enough by itself; the real
  question is a policy for that ambiguity, most plausibly by never auto-adopting more than one
  manifest-resolved candidate without explicit user confirmation of which ones are genuinely
  complementary.
  <br>**Why this is the most invasive schema change in the project's history, not a resolver tweak.**
  `MachineSavePath` (`src/Server/Data/Entities.cs:200-205`) is a plain `SavePath` string on a
  composite-key `(MachineId, GameId)` row — "one path per machine per game" is baked into the primary
  key itself, not just the column type, and the table has no FK constraints today (cleanup is
  hand-written `RemoveRange` calls in `SyncService.cs`), a pre-existing gap worth fixing in the same
  migration rather than separately. Every path-carrying wire DTO in `Contracts.cs`
  (`MachineSavePathDto.SavePath`, `GameDto.SuggestedSaveDir`) is a single string too — mechanically
  easy to widen (there's already a `string[]` precedent via `ExcludeGlobs` on the same DTOs), but
  ripples into `openapi.json` and the generated `web/src/api-types.ts`. Agent-side,
  `TrackedGame.SaveDirectory` (`src/Agent.Core/AgentConfig.cs:454`) is one non-nullable string, and
  `SyncEngine`'s push/pull is hard-coded to one root at the type level —
  `SaveArchive.CreateArchive`/`HashDirectory`/`RestoreArchive` all take one `sourceDir`/`targetDir`
  string, not a list. `CommandPoller.ReconcileGamesAsync`'s reconciliation logic (~10 branches of
  single-path comparison/reporting) would need real rework, not just a type change.
  <br>**`SaveArchive` is the single hardest piece.** Turning "zip one directory" into "zip N
  directories into one archive" needs a namespacing scheme so two roots' relative paths can't collide
  (`Documents/save.dat` from root A vs `AppData/save.dat` from root B), and the restore-side safety
  logic — the stale-file delete pass, the symlink guard, the nested-restore-depth guard — is all
  written in terms of one root and would need re-deriving per-root rather than per-archive.
  <br>**Proposed phased shape**, once the ambiguity policy above is settled: (1) data model — a child
  table replacing `MachineSavePath`'s composite-key single row (or a JSON-encoded ordered list if a
  new table is unwanted), `TrackedGame.SaveDirectory` → list, wire DTOs reusing the `ExcludeGlobs`
  `string[]` precedent; (2) `SaveArchive` namespacing-and-restore-safety — its own design pass, not a
  bullet, given the safety mechanisms involved; (3) scanners — stop reducing to `.FirstOrDefault()`,
  surface every resolved candidate, require explicit confirmation per the ambiguity policy; (4) UI —
  `GameDetail.tsx`'s per-machine path table (one text input per row) becomes a nested list editor.

- **Registry-based saves.** Scoped 2026-08-20 (full research write-up:
  `logs/2026-08-20_wine-case-insensitive-and-scoping.md`), not built. The Ludusavi manifest's
  `registry:` section is invisible end to end today: `ManifestLoader`'s YAML DTO
  (`src/Shared/ManifestLoader.cs:245-257`) has no `Registry` property at all — not even an unused one
  — so `.IgnoreUnmatchedProperties()` silently drops a game's `registry:` block during parse. Even the
  detection test harness's deliberately-richer DTO
  (`tests/detection/SaveLocker.DetectionHarness/ManifestModel.cs`) has no registry field either, so
  there is no partial groundwork anywhere to build on — adding the property (parsed and visible,
  independent of whether anything acts on it yet) is step zero.
  <br>**Two genuinely different problems hide under one line, because SaveLocker runs on two hosts.**
  Native Windows (`src/Agent/`) is straightforward: `Microsoft.Win32.Registry` talks to the real
  registry directly, and the existing pattern (`GameScanner.cs:82-109`'s `ReadRegistryString` —
  try/catch narrowed to `UnauthorizedAccessException`/`IOException`/`SecurityException`, null-safe
  `OpenSubKey`/`GetValue`, already used for finding Steam's install path) is directly reusable for a
  manifest-declared `registry:` key. Linux/Proton (`src/Agent.Linux/`) is hard: the agent is a native
  linux-x64 process, never running under Wine itself, and typically needs to read a game's Proton
  prefix registry *without* launching Wine or that prefix's process, often across many different
  prefixes. Wine transparently backs `Microsoft.Win32.Registry` calls with a prefix's own
  `user.reg`/`system.reg` — but only from *inside* that specific prefix's own Wine process, which is
  not the position this agent is in. The realistic path is hand-parsing Wine's plain-text
  `user.reg`/`system.reg` format directly — `[Registry Key Path] timestamp` section headers,
  `"Name"="value"`/`dword:`/`hex:` lines, `@=` for the default value — which **does not exist in this
  codebase in any form today**. The closest existing pattern for "hand-write a parser for a
  semi-documented text config format" is `SteamTextVdf.cs`'s tokenizer for Steam's KeyValues format —
  a reasonable structural template, but the Wine `.reg` grammar itself is unrelated and would be new
  code either way.
  <br>**No archive story either.** `SaveArchive.cs` is zip-of-real-files throughout — `CreateArchive`,
  `HashDirectory`, and `RestoreArchive` all walk real files under a real directory, with no
  extensibility hook for a non-file member. Supporting registry data would need a reserved-path
  convention (e.g. an exported `.reg`-shaped blob at a fixed relative path inside the same zip) with
  explicit special-casing in all three of those methods to skip that path in the existing
  file-diff/symlink-guard logic and route it through a registry writer on restore instead of
  `File.Copy` — new logic in three places, not a drop-in extension.
  <br>**Restore is the dangerous direction here too**, mirroring the native-Linux item's own
  push-is-safe/pull-is-not framing: writing an untrusted registry blob back is a new kind of hostile-
  input surface the existing zip-slip/symlink hardening in `Decisions.md` doesn't cover at all — worth
  its own threat-modeling pass before restore is attempted, not an assumed extension of the existing
  archive guards.

- **File-level saves — the 24 games the install-root guard now refuses.** A save location is a
  DIRECTORY throughout, so a manifest entry like `<base>/Save.dat` can only resolve to the whole
  install folder. Refusing that was right (it would archive the game, and restore's delete pass
  would prune another machine's installation), but it costs **8% of the manifest**: sweep went
  99.0% → 90.9%, every new miss a `MISS(<base>)`. Recovering them properly means archiving the
  matching FILES rather than their containing directory — a change to the archive model, touching
  `SaveArchive`, the settle gate and restore. Measure before building: some of the 24 have another
  path that now wins instead (Cave Story+ did), so the true loss is smaller than 24.
  <br>**Manifest-wide sizing (2026-08-14):** of 21,061 entries with save paths, **702** have a save
  set that trims to `<base>` and nothing else — those are refused outright — and **169** more have
  `<base>` as one of several, so they lose a path but keep an answer. That 702 is the ceiling on
  what this item can recover. Counted from `data/manifest.yaml` by trimming each save template at
  its first wildcard, the same rule `PathResolver` applies.

- **Self-host the console fonts.** The console loads Inter and JetBrains Mono from Google Fonts at
  runtime, so on a LAN box with no internet it renders in fallback fonts. CS-13 fixed the import
  being *discarded*, not the dependency. Needs woff2 subsets for five Inter weights; the Deck UI
  already vendors TTF Regular/SemiBold in `src/Agent.Linux/Ui/Fonts/` (SIL OFL).

- **Device-verify fresh Windows installer enrollment.** The wizard shipped in v0.1.7; the upgrade path is well verified. The **fresh install** (clean box, no `%PROGRAMDATA%\SaveLocker`) has never been exercised. Scenarios archived in `logs/2026-07-14_installer-enrollment.md`:
  - Happy path: run installer, choose enrollment file → page shows server + machine name → install → machine appears online in Machines.
  - ACL trap: `icacls "%PROGRAMDATA%\SaveLocker"` — interactive user needs Modify.
  - Expired-token, skip, and `/SILENT /ENROLL="C:\path\policy.json"`.

## Medium priority

- **Interactive setup guide in the console.** Raised 2026-08-14 alongside the savelocker.com plan. A
  first-run walkthrough that takes a new user from an empty server to a syncing game, in the console
  itself rather than in prose: mint an enrollment file → wait for the machine to appear → add a first
  game → confirm a push landed, each step self-checking against live state (Machines, GameState) so
  it advances on its own and cannot claim a step is done when it is not. Two reasons this is not
  documentation: the marketing site's Get Started has to end *somewhere*, and the natural place is
  "open the console, it takes it from here" — and the Deck's launch-option step (`savelocker run --
  %command%`) is the single most-missed action in the whole flow, with nothing today that notices it
  was skipped. Re-runnable from Help, and resumable — a user enrolls the Deck days after the PC.
  Reuses the Help KB shell (`web/src/help/`) for the article surface; the checks are `/api/overview`
  + machines. Deck Game Mode already has a "Next step" card (`Ui/UiApp.cs`) — same idea, wider scope.

- **Decky plugin Phase 5 — prove it on real hardware.** The code is done (Phases 1–4 done and
  verified on hardware, shipped in v0.5.6 and plugin v0.2.0; Phase 5 itself shipped in v0.5.6/plugin
  v0.2.1, `run-linux-tests` 161 → 197/197) — the plugin lives at
  <https://github.com/SkorcherX/SaveLocker-Decky>. What's left is entirely a hardware pass: **upload
  SaveLocker-Decky v0.2.1's `SaveLocker.zip` in Config → Agent updates → Decky plugin** (the Deck is
  still on plugin v0.2.0, so it's genuinely behind) and watch the Deck pick it up on its own. The
  harness covers the agent's half with a fake plugin directory; what it cannot cover is the part that
  makes the feature work at all — Decky noticing the files change and hot-reloading, via the `debug`
  flag in `plugin.json`. That mechanism *was* observed by hand during Phase 4 (manual `scp` +
  `touch`), so this is not a guess, but the agent doing it unattended has never happened on hardware,
  and neither has the server hosting a real plugin zip. Requires the v0.5.8+ agent rollout first —
  the plugin reads `stagedVersion`, which only a v0.5.8-or-later agent publishes (**that rollout is
  now done**, so this is unblocked). A manual reinstall through Decky always works as a fallback, and
  is what the refusal path tells the user to do if this pass fails. Plan and corrections:
  `logs/2026-08-15_decky-plugin.md` → Phase 5.

- **Decky plugin: left-stick scrolling in the QAM is steppy.** The D-pad is fine. Steam scrolls by
  moving focus, so the stick steps between focus targets rather than scrolling freely, and the panel
  has many — `doctor` output is one target per line, which is what makes it reachable at all. The
  lever is *fewer stops*: collapse the output behind a "show output" expander so those rows only
  exist when wanted. Cosmetic, and only noticeable after running doctor.

- **Surface duplicate-shortcut-name warnings outside `doctor`.** `logs/2026-08-19_moondeck-save-detection.md`
  added a `doctor` note for a game name backed by two different Steam AppIDs (found on the
  maintainer's own Deck: HITMAN 3, Minit, Moving Out, Animal Crossing, Metal Gear, Waydroid) — Steam
  launches only one, and scan's dedupe can silently pick the dead one. `doctor` is CLI/headless-only;
  neither the agent-ui Add Games view nor the Game Mode UI reads its output at all, so a user who
  never opens a terminal never sees the warning. Worth a chip on the affected candidate once there is
  evidence this recurs for more than one Deck.

- **Linux agent secret permissions and state layout.** `config.json` contains a long-lived machine key; file privacy depends on the launching shell's umask. Enforce `0700` on private state directories and `0600` on config, queue, health, and log files in code, including CLI enrollment paths. Consider separating immutable app files from mutable XDG config/state so upgrades cannot overlap the executable tree.

- **Constrain external manifest paths.** The Ludusavi manifest is downloaded from mutable `master`; expanded templates are not proven to stay inside the intended Proton prefix. Pin or integrity-verify an approved manifest revision, canonicalize resolved paths, reject `..`/symlink escapes outside allowed roots, test a hostile manifest entry. Preserve explicit manually mapped portable-save paths as a separate trusted-user path.

- **Deferred: one state owner for the Linux agent** — wrapper→daemon IPC over a Unix socket, standalone fallback when no daemon is up. The locking in `Decisions.md` §8 makes the current two-owner model *correct*; IPC would make it *simple*. Worth doing before the state files grow further.

## Planned / future

- **Game Mode UI reflects a stale game list.** `savelocker ui` only *reads* local `config.json`; it never reconciles with the server (only the daemon does, every 20s — `CommandPoller.ReconcileGamesAsync`). So a game deleted in the console still shows in Game Mode until the daemon runs, and there is no in-UI way to untrack. Deferred 2026-07-24 (maintainer chose to keep Phase 3 lean). Fix when revisited: reconcile-on-launch (+ periodic) in `savelocker ui`, optionally a per-game "Stop tracking" that also deletes server-side so the daemon does not re-adopt it (`CommandPoller.cs:157`).

- **The other stores' cloud flags.** `ManifestLoader.ManifestCloud` parses only `steam`, because
  that is the only flag a surface acts on. The manifest also marks `gog` (3,232), `epic` (739),
  `origin` (239) and `uplay` (106). These matter for **Heroic** candidates, which are exactly the
  GOG/Epic/Amazon games currently flagged `HasSteamCloud: false` — correct as far as it goes, but it
  means a GOG game that GOG Galaxy already syncs is offered as if nothing covers it. Needs a
  per-store flag on `ScanCandidate` rather than a second bool, and a decision about whether the
  default view should hide those too (Galaxy sync is opt-in per game, unlike Steam Cloud — so
  probably not, which is why this is not high priority).

_Dropped items (won't-do) are recorded in `logs/shipped-2026-07.md`._
