# Art picker: which SteamGridDB game; Linux agent secrets

2026-10-12 · branch `art-picker-and-linux-agent-secrets` · worktree
`.claude/worktrees/art-picker-and-linux-agent-secrets` · PR pending to the fork.

Two backlog items in one branch, asked for directly:

1. **Art picker: choose which SteamGridDB game to pull from** (`tasks/_done/art-picker-game-choice/`).
2. **Linux agent secret permissions** (`tasks/linux-agent-secrets/`), with the state-layout half
   deliberately deferred into a plan (`plan-split.md`) after research — the maintainer picked
   "permissions only now; plan the split".

## 1. Art picker

**The defect:** `ArtService.FindGameIdAsync` took `search/autocomplete`'s first result as the game's
artist. A SaveLocker game whose name matches the wrong SteamGridDB entry (ports, remasters,
same-titled releases) offered that entry's covers/icons with no way out short of renaming the game.
The options listing, *Refresh art* and the missing-art backfill all shared the guess.

**Built:**

- `Game.SteamGridDbId` (int?) + `Game.SteamGridDbName` (text) — migration
  `AddGameSteamGridDbSource`. Null id = automatic first-name-match, so every existing game is
  unchanged.
- `ArtService.ResolveSgdbIdAsync(Game)` — the pinned id wins; otherwise the name lookup (still
  cached 30 min). Used by the options listing, `RefreshArtAsync` and therefore the backfill.
- `GET /api/games/{id}/art/source` → `ArtSourceDto { sgdbId, name, automatic }`. Answers without a
  key when a game is pinned; an automatic source with no name means no key or no match.
- `PUT /api/games/{id}/art/source { sgdbId }` — pins (or clears with null). A non-null id is
  validated against `games/id/{id}` **before** anything is stored, and what is stored is
  SteamGridDB's own name — never the caller's text. Unknown id → 400 and nothing changes.
- `GET /api/games/{id}/art/search?q=` → `SgdbGameDto[] { id, name, year? }` — the picker's search
  box. Empty query answers without contacting SteamGridDB; query capped at 100 chars, results at 20;
  `release_date` reduced to a year so same-named releases can be told apart.
- `web/src/components/ArtPicker.tsx`: a source row above the two strips —
  "Automatic match: DOOM" / "Chosen: DOOM" — with **Change** (search, pick) and **Use automatic
  match** (only while pinned). Both strips remount from page 0 when the source changes, so the
  chosen entry's art is what is listed.
- One source per game, both kinds (cover + icon) follow it — the backlog's own "remember the chosen
  id per game" wording.

**Verification:** `run-console-security-tests.ps1` **376/376** (was 364; +12 checks). The suite's
PowerShell stub gained `games/id/{id}` and per-id art for the chosen entry, and its autocomplete
branch now serves three candidates — plus two test-authoring bugs found by running it: the stub
branch keyed off the game's name instead of the search query, and PowerShell unrolls a
single-element array assigned from an `if` statement, so the stub's `data` field serialized as an
object and the listing came back empty (both recorded in [[Gotchas]] → Testing). Manual
reproduction against a scratch server + the same stub confirmed the whole path; `web`
(`tsc -b && vite build`) and `oxlint` clean; `openapi.json`/`api-types.ts` regenerated,
additions only. `tests/sgdb-stub.py` (the by-hand stub) now returns three candidates per query too,
so the source row can be exercised through `testenv` without a test suite.

**Not verified:** a real SteamGridDB key (none available — the same standing caveat as the rest of
the artwork work); the WebView2 tray window (the console is a browser surface); a real Deck.

## 2. Linux agent secret permissions

**The defect:** on Linux the state directory's mode came from whichever shell launched the agent —
under the usual umask 022 a 0755 directory with 0644 files, one of which (`config.json`) holds the
server API key and another (`api-token`) grants the local management API. `api-token` got a one-off
`chmod` when the daemon/UI started (`LocalAuth`), but a CLI-only run (`register`, `enroll`,
`add-game`) never started that path, and `agent.log` used `File.AppendAllText` (umask mode) and was
never tightened. The JSON state files were already 0600 (`AtomicFile`'s `UnixCreateMode`) but the
directory was not restricted at all.

**Built:**

- `StateDirSecurity.Protect` now does Unix work too: 0700 on the state directory, 0600 on the known
  private files (`config.json`, `api-token`, `agent.log`/`.old`, `offline-queue.json`,
  `health-events.json`, `lease-warnings.json`, `sync-activity.json`, `manifest.yaml`,
  `desktop-entry-offered`, `test-shortcut-art.json`). An allowlist, deliberately: everything else in
  the directory is app content (the binary tree — it IS the install prefix) or staging, covered by
  the directory mode, and a blanket chmod could break executables.
- The once-per-process gate moved from `AtomicFile` into `StateDirSecurity` so the logger, atomic
  writes and `LocalAuth` share it, and `AtomicFile` now calls `Protect` on **both** OSes for every
  restricted write. That is what makes CLI-only runs covered: `config.Save()` is the one choke point
  they all go through.
- `AgentLogger` creates the log 0600 from creation (`FileStreamOptions.UnixCreateMode`) and its first
  write calls `Protect`, so an older world-readable log is tightened in passing — including on
  processes that never write config.
- `LocalAuth`'s directory handling delegates to `Protect` (its token handling was already 0600).
- Windows is untouched: the ACL path still runs, and `Protect`'s Windows branch is what it was.
- Linux **not** done on purpose, researched and written up in `plan-split.md`: moving config/state
  out of the install prefix to XDG dirs. The research found the real costs — rollback across the
  split boots an old binary that resolves the old paths; the Decky plugin (separate repo) hard-codes
  `~/.local/share/SaveLocker/api-token`; both rigs isolate with exactly one variable
  (`XDG_DATA_HOME`), so a split must also set `XDG_CONFIG_HOME`/`XDG_STATE_HOME` in every rig or
  tests write into the real `~/.config`; ~30 source files plus tests/CI/docs.

**Verification:** 9 new checks in `tests/linux/run-linux-tests.sh` (daemon dir/token/config, the
CLI-only register path, retroactive tightening of a 0644 dir + log, the default XDG root and log).
Full suite in WSL as root: **280 pass / 2 fail**, the two being the documented WSLg-injected-session
pair ([[Gotchas]] → Testing). **Mutation-checked:** with the four Agent.Core files reverted to
`main`, four of the nine checks fail exactly (dir not 0700 after a CLI write, pre-existing 0644
`agent.log` not tightened, default root not 0700, its log not 0600); the other five pass on old code
because their surfaces (token, config 0600) already existed. `dotnet test` 450/450; Windows agent
builds clean.

**Gotchas hit while verifying (both recorded):** the suite hardcodes 5179/5186/5187 and a foreign
daemon from the shared testenv WSL clone held :5187 — the root run aborted in Kestrel's bind and
cascaded; ran the suite inside `unshare -n` with `NUGET_PACKAGES` pinned to the pre-warmed cache
(the suite re-points `HOME` at its fixture, which would otherwise move NuGet's cache and force a
network restore that cannot happen in a namespace).

**Not verified:** a real Deck; macOS (the Unix path is shared, but only Linux is tested);
multi-user boxes beyond the mode bits (no second account was used to read the files).

## Review fixes (2026-10-12, same branch, after a code review of PR #70)

Five findings, all fixed on the branch; the review re-ran the verification too (below).

- **`StateDirSecurity.Protect` now caches the outcome, not just the attempt.** The once-per-process
  set made a *failed* first attempt answer `true` ("already handled") to every later caller. It is a
  `Dictionary<string, bool>` now, seeded optimistically while the attempt runs (so a concurrent
  caller keeps the old "being handled" answer) and replaced with the real result at the end. No
  caller reads the value today, so this was latent — it is correct now regardless.
- **The picker's source lookup is cached and the new routes parse defensively.** `GetSourceAsync`
  asked SteamGridDB for the automatic name match on every open; the lookup is now
  `FindGameCachedAsync` — the same 30-minute, non-null-only cache the options listing, Refresh art
  and the backfill already shared through `ResolveSgdbIdAsync`. `FindGameAsync` uses `TryGetProperty`
  and takes the first entry that actually has an id, and the three new routes' catch treats
  `JsonException` as "no answer": a malformed 200 body is a 400 with a message, not a 500.
- **The picker's empty-result message names the search it describes.** It read the live input, so
  editing the box after a zero-result search mislabelled the message; the searched text is captured.
- **`AgentLogger`'s comment records the Windows consequence** of its first-write protection: a
  read-only command's first log line can claim a never-protected state directory for the account
  running it — the same rule any restricted `AtomicFile` write already applies, and a no-op once the
  directory is protected.
- **The missing-art backfill is now tested with a pinned source**, not just options and Refresh art.
  `run-console-security-tests` gained two checks: a game is created with no key (so it stays
  art-less), the key is added with the startup pass still off and a source pinned, and the restart's
  backfill must fill it from that entry — its automatic name match is a different id in the stub, so
  the request count for the pinned id is the discriminator.

**Re-verified after the fixes:** `run-console-security-tests` **378/378** (+2), `dotnet test`
**450/450**, `run-agent-tests` **47/47** (this review session ran it; the original session had not),
server and Windows-agent builds at their warning baselines, `web` build + lint clean, and the Linux
permission behavior re-run by hand in WSL (fresh CLI run → 700/600; retroactive 755/644 → 700/600).
