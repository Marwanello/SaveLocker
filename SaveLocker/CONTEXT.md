# SaveLocker — Session Context

**What:** Self-hosted Windows/Linux game save sync. Hub-and-spoke: a tray/headless agent on each
PC or Steam Deck syncs saves through an ASP.NET Core server (Docker on unRAID). React admin
dashboard + embedded React agent UI + a gamepad-native Deck Game Mode UI. See [[Architecture]].

**Repo:** https://github.com/SkorcherX/SaveLocker | **Branch:** main

**Current released version:** **v0.5.2** (released 2026-08-08). GHCR package `savelocker` is
**public** — see [[Gotchas]].

---

## Status

**In flight 2026-08-09: branch `heroic-detection` (`4f919d1`, not pushed).** Games staged in
**Heroic Games Launcher** now get detected with their save paths. See `tasks/HeroicDetection.md`.

1. **They were always discovered and never resolvable.** Heroic's "Add to Steam" writes a real
   `shortcuts.vdf` entry, so the game appeared — but the prefix was looked up as
   `steamapps/compatdata/<appid>`, and Heroic does not launch through Steam, so it does not exist.
2. **Two Steam conventions were hardcoded and are only Steam's**: that a prefix is
   `<compatdata>/pfx/drive_c`, and that games run as `steamuser`. Heroic nests `pfx` only for a
   Proton runner, and a plain Wine runner runs the game as the **Linux** user. `PathResolver.Wine`
   takes both as parameters; `Proton()` delegates to it, behaviourally unchanged.
3. **Verified on the Deck:** Absolute Drift (GOG via Heroic), found from Game Mode with the correct
   path. `run-linux-tests.sh` **40 → 57**. Hardware has covered **gogdl and the sideload parser
   only** — `legendary` and `nile` are fixtures-only. See [[Backlog]].
4. **A sideloaded game's `.exe` can live in a different prefix than the one it runs in** — a
   leftover prefix survives uninstall and its stale `.exe` gets picked. Wine reaches it through
   `Z:`, so nothing complains. Saves follow the prefix it RUNS in, so this works and `doctor` keeps
   exit 0 — but `<base>` points at the other copy. `doctor` says so.
5. **`Prefixes/Title` is Heroic's, verbatim** — its sideload dialog seeds the prefix name from a
   title field initialised to the literal word `Title`. Not a placeholder of ours. **Do not rename
   the folder to fix it**: that does not update `GamesConfig`, and Heroic just makes a new empty
   prefix at the old path.

**Merged 2026-08-08: PR #36 `save-path-autodetection` (→ `637d11f`).** Save-path autodetection went
from resolving **57.5%** of sampled manifest games to **99.5%**, and from ~4% confidently-wrong
answers to **zero**. Not yet released — see Deploying below.

1. **`PathResolver` implemented 10 of the manifest's 13 placeholders.** The three missing ones appear
   in more save paths than everything else combined (`<base>` alone outnumbers every other token put
   together). `<storeUserId>` is **discovered from disk, not derived** — Steam exposes both a 32-bit
   account id and a SteamID64 and games use either, so an id read from `userdata/` resolved to paths
   that do not exist.
2. **`tags` were ignored**, so a `config`-tagged path could beat the real save path on hash order.
   DRAGON QUEST III mapped to `Steam/Config/WindowsNoEditor`. Returning nothing now beats returning
   a wrong path presented as certain.
3. **Name matching now ignores punctuation as well as case** — a non-Steam shortcut carries whatever
   its owner typed. Normalisation deliberately keeps word boundaries: deleting non-alphanumerics
   collapses "Dragon Quest I & II" onto "III", and the manifest holds both.
4. **Enrollment creates games under the manifest's canonical title.** Two machines spelling one
   shortcut differently used to create two server games that could never sync, with no error
   anywhere — no user error required.
5. **Windows installed-Steam games never consulted the manifest at all**, so they always arrived with
   no save folder and the user typed one by hand.
6. **Add Games hides Steam Cloud titles by default**, naming what it hid.

**New suite: `tests/detection`** — materialises dummy save trees at the paths the real manifest
claims and scores the production resolver against them. No Steam, Proton, GPU or Deck needed.
`sweep` measures, `pinned` gates (15 cases). See `tests/detection/README.md`.

**`run-linux-tests.sh` passes for the first time — 40/40.** It had never worked:
`Storage__AgentInstallerRoot` was unset, so the server died at startup *after* migrations and all 16
server-dependent checks failed as though the agent were broken. One variable. This clears what was
item 1 of the v0.5.0 post-release verification.

**Two verification traps worth remembering, both recorded in [[Gotchas]]:**
- **The Windows suites drive `src/Agent/bin/DEBUG`.** A branch built only in Release runs them
  against a stale binary and reports green while testing nothing.
- **A harness can be structurally incapable of catching the bug it was written for.** The detection
  oracle used the same fake `<storeUserId>` on both sides, so it passed a fix that did not work on
  hardware; it also dropped `tags` when re-serialising, silently disabling the filter under test.

Shipped in v0.5.1 (2026-08-05) and earlier: see `logs/shipped-2026-07.md` + `logs/sessions.md`.

## Next action

1. **Check the live server for duplicate games** before deploying. Canonical naming stops NEW splits;
   it does not merge games a server already holds under two spellings. There is no merge tool.
2. **Deck verification** of the Linux bounty — the five scenarios in
   `logs/2026-07-29_linuxagent-bugbounty.md` → Verification. Hardware available (2026-07-19). Fold in
   the two v0.5.1 Deck fixes (one ring on open with A working; a resting cursor must not paint a
   second selector) and a real save-path detection check now that `<base>` resolves from `StartDir`.
3. **The second-Windows-account ACL test (WA-03).** Still the only outstanding item with a
   user-visible promise attached — reword `web/src/releases/0.5.0.md` once it passes.
4. The rest of the Windows manual gates: fresh-VM install, a real game, a real non-Steam shortcut,
   the first-run Settings deep link on a cold WebView2 profile.
5. **The LAN enrollment-URL check** on the real deployment
   (`logs/2026-07-27_console-bugbounty.md` → Verification).

Missing regression tests: LA-04/05/06/07 have code fixes and no tests.

## Deploying v0.5.1

**No migration** — unlike v0.5.0, downgrading the container is a clean rollback again.

The console must be redeployed **and every agent updated**: the version-display fix is split across
both halves, so doing one and not the other leaves Config still showing a version split. Windows
agents self-update once the installer is uploaded to the console (**the GitHub Release asset is not
automatically what the fleet is offered** — upload it in Config → Agent Updates). Deck users re-run
`install.sh` from the newer tarball.

The fleet was on v0.5.0 as of 2026-08-05, so the `0.5.0` / `0.5.0.0` split is what Config shows until
this rollout completes.

### Deploying v0.5.0 (kept — it is the last one that needed care)

**Back up `/data` first.** Two schema migrations run on first start, and this was the first release
where downgrading the container was not a clean rollback. Anyone upgrading from before v0.5.0 still
crosses that boundary.

## Open work

See [[Backlog]] for the prioritized list. `tasks/HeroicDetection.md` is live on branch
`heroic-detection` (see Status) — and see the parked task below.

### Parked on a branch — `offline-backoff-task` (`bee3116`, pushed, not merged)

**`tasks/OfflineBackoff.md` and its Backlog entry exist only on that branch**, by choice
(2026-07-29): the task was written but the work was deferred, and the maintainer did not want it
landing on `main` yet. `git checkout offline-backoff-task` to pick it up.

The task: an agent that cannot reach the server retries forever at a fixed cadence — the offline
drainer every 30 s, *re-archiving the save folder each time before it finds out*, and the command
poller every 20 s carrying the reconcile, the command fetch and the heartbeat. Roughly five failed
requests a minute, indefinitely. On a Deck away from home that is battery and metered traffic spent
on nothing. Note the scope: **backing off only the drainer does not achieve the goal**, which is why
the task covers all three loops.

### Suite baseline (all green at 2026-08-08)

Windows, local: **win agent bug bounty 114** · server bug bounty 145 · agent **47** · hardening 33 ·
local-api 30 · concurrency 23 · health 19 · enrollment 18 · enrollment-TLS 6.
Linux, local (WSL ext4): **run-linux-tests 57** on `heroic-detection`, 40 on `main`. Detection: sweep 394/396, 15 pinned.
Linux, in CI: agent 43 · hardening 37 · local-api 30 · concurrency 23 · health 19 · enrollment 16.
The two platforms differ by design — each suite skips the other's cases.

Server build 0/0. Console lint and build clean; the Windows agent build has **one** pre-existing
warning (MSB3277, a WindowsBase 4.0/5.0 conflict from WebView2) — a second one means something.

`run-winagent-tests.ps1` owns :5189 (+ :5190–:5198) and `.verify-winagent`. It is slow by design —
~5 minutes, most of it real waits on lease renewal, lock contention and WebView2 startup. **It drives
two real tray processes**, so it needs an interactive desktop session and skips those blocks without
one.

**Before running any suite, read [[Gotchas]] → Testing and Test harness.** Two traps cost hours this
session and both are documented there: clear `.verify/` and `src/Server/localstate/` *together*, and
start the dev server with `ASPNETCORE_ENVIRONMENT=Development` *and* explicit `Storage__*` or it
opens `/data/savelocker.db` (i.e. `E:\data\`) and hangs on a stale migrations lock.

---

## Where to look

| Topic | File |
|-------|------|
| Codebase map | [[REPO_MAP]] |
| System design | [[Architecture]] |
| Locked decisions (don't re-litigate) | [[Decisions]] |
| Recurring traps | [[Gotchas]] |
| REST endpoints | [[API Reference]] |
| Dev build & run commands, test suites | [[Build and Run]] |
| Agent CLI | `web/src/help/cli-reference.md` (KB article) |
| Active backlog | [[Backlog]] |
| Shipped release notes | `web/src/releases/*.md` (the console page *and* the GitHub Release body) |
| Windows bounty write-up | `logs/2026-07-29_winagent-bugbounty.md` |
| Linux bounty write-up | `logs/2026-07-29_linuxagent-bugbounty.md` |
| Console bounty write-up | `logs/2026-07-27_console-bugbounty.md` |
| Session history | `logs/sessions.md` |
