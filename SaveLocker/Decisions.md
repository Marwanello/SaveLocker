# Decisions

Settled and shipped. Don't re-litigate — the "why" is kept to one line so a future
session can judge an edge case, not to reopen the choice.

- **Detection: reuse Ludusavi's manifest** (community save-location DB), don't re-map save
  locations ourselves. Build our own agent/server/dashboard for orchestration, leasing, conflicts.
- **Conflict prevention: proactive lock/lease.** Server tracks a per-game checkout; the agent pulls
  before launch *where it has a real launch boundary* (see the next bullet); other machines are
  warned if leased elsewhere. Content-hash + parent-version lineage is the fallback detector.
- **Only Linux has a pre-launch pull; Windows refuses to restore under a running game** (WA-01,
  2026-07-27). `savelocker run -- %command%` runs *instead of* the game and starts it itself, so it
  restores with certainty that nothing has the save open. Windows has only `ProcessWatcher`, which
  polls every 4 s and therefore observes a game *after* it started and opened its saves — calling
  that "pre-launch" meant restoring underneath a live process, which the game then overwrote at
  exit, losing the pulled save silently. The Windows launch path now takes the lease and skips the
  pull. `GameActivity.IsActive` is the central refusal, enforced inside `SyncEngine.PullAsync`
  (twice — before download and again immediately before restore, since a game can launch during a
  long download) so no tray action, dashboard command, CLI call, or lifecycle callback can route
  around it. Each surface checks it too, only to word the reason. A game with no configured
  `ProcessNames` cannot be detected as running — that is what makes WA-08 a data-safety fix, not a
  convenience one. Adding a Windows launch wrapper would let the pull come back; nothing here
  precludes it.
- **Two tiers of save-path validation: absolute refusals and overridable warnings** (WA-02,
  2026-07-27). `SavePathGuard` is the hard floor — a drive root, a user profile (or the folder
  holding all of them), a Windows/system directory, Program Files, or the agent's own state or
  install directory can never be a save folder, and there is no override, because a force-pull
  *replaces* whatever it is pointed at. `SaveDirSanity` stays the heuristic tier (Wine prefix,
  repeated tail, over the upload cap): those have real false positives, so they are refused once and
  accepted on an explicit second confirmation (`confirm` on the local API, `--force-path` on the
  CLI). Paths are validated *and canonicalized* at all five entry points — folder picker, typed
  local API, enrollment, CLI, and server `MachineSavePath` reconciliation — and validated **again**
  inside `SyncEngine` immediately before archiving or restoring. The re-check is the load-bearing
  one: config.json is hand-editable, the server can push a path with no local confirmation, and a
  stored path can become a junction after it was accepted, so canonicalization resolves reparse
  points rather than trusting the stored string. The state directory is refused as
  same-or-ancestor only, not as a whole tree — `config.json` and `api-token` sit directly in it, and
  the test suites legitimately put save folders below it.
- **Windows agent state belongs to one account: whoever enrolled** (WA-03, 2026-07-27). Not a local
  group. `%PROGRAMDATA%` grants every authenticated user read access and it inherits, so
  `config.json` (this machine's **server API key**) and `api-token` (which grants the local
  management API) were readable by any local account. `StateDirSecurity` severs inheritance on the
  state directory and grants only the enrolling user, `SYSTEM`, and Administrators — the last two
  because an administrator can take ownership anyway, so removing them costs backup and repair
  while adding nothing. This matches the shape the installer already chose: it deliberately does
  **not** create `%PROGRAMDATA%\SaveLocker` while elevated, so the de-elevated tray user owns it
  (`installer/SaveLocker.iss`), and no installer change was needed. The ACL is applied to the
  *directory* with inheritable rules and enforced in `AtomicFile` — the one choke point every state
  writer already goes through — so a state file added later cannot quietly miss it. Failure to
  apply is logged loudly and is never fatal: an agent that refuses to start protects nothing.
- **Stack: single-language .NET.** Agent = C#/WinForms (Windows), C#/headless (Linux); Server =
  ASP.NET Core in Docker on unRAID.
- **Runtime: .NET 10 (LTS)**, locked 2026-07-13. .NET 9 is STS, EOL 2026-11-10; .NET 10 is LTS to
  2028-11-14. `global.json` pins the SDK — bump it and the Dockerfile's `sdk:`/`aspnet:` tags
  together.
- **unRAID as hub, not peer-to-peer.** Async decoupling (offline machines sync later), single
  source of truth for conflict resolution, versioned history in one place. Rejected raw Syncthing
  (continuous sync risks mid-write copies; conflict files unusable for binary saves).
- **Dashboard auth:** `AdminPasswordFilter` + PBKDF2-SHA256. Cloudflare Access/Google SSO deferred
  — blocked by the Tunnel's 100 MB file limit conflicting with large save archives.
- **Enrollment model:** a game is defined once on the server; each agent maps its own local save
  dir. The server game is the single definition; scanners only suggest candidates.
- **"Latest" = `Game.HeadVersionId`.** UI label "Latest"; admin action "Set as Latest".
- **Artwork:** SteamGridDB images are downloaded/cached server-side, not stored as bare URLs
  (offline-safe, survives upstream changes).
- **Product name: SaveLocker.** Rename complete 2026-07-10 — config dir, mutex, registry key,
  DB path, namespaces, solution/project files. Existing Docker deployments may still have
  `/data/localgamesync.db`; rename or set `Storage__DbPath`.
- **Installer: Inno Setup 6**, not WiX/MSIX. MSIX's virtualization would interfere with reading
  the Steam registry + arbitrary save folders. Machine-wide install, UAC up front. Uninstall
  prompts before deleting `%PROGRAMDATA%\SaveLocker` (API key + config).
- **Linux agent scope: non-Steam Windows games run under Proton only** (v1). Steam-bought games
  already have Steam Cloud; native Linux game builds are explicitly out of scope (would need a
  save-variant model — different formats/paths/line-endings per platform). A Proton save is a
  Windows save, byte-identical to a Windows PC's — existing content-hash lineage works with zero
  server schema change. **Never sync a native-Linux save into a Windows install.**
- **Linux discovery:** `shortcuts.vdf` (non-Steam shortcuts), not `libraryfolders.vdf`/`*.acf`
  (installed Steam games — irrelevant to this niche). Steam's shortcut AppID is signed in the
  VDF but the `compatdata/<id>/` folder name is unsigned — `SteamShortcuts.CompatDataId()` is the
  one place that converts.
- **Linux UI: headless daemon serving the existing React UI** on `:5178` (Desktop Mode = KDE +
  browser). **Game Mode has no browser**, so `savelocker ui` (SDL + Dear ImGui, in-process against
  `Agent.Core`, no second API client) covers Status/Add game/Set folder/Launch setup as a gamepad
  view — not a second frontend. Themed to the console's exact palette (`Ui/Theme.cs` mirrors
  `web/src/index.css`). 60 fps cap (was 30; this is a menu, not a running game — VSync bounds it).
  Rejected: Flatpak+WebKitGTK (665 MB+), Godot (needs ≤net9), Avalonia (still hand-built nav),
  `steam://openurl` (Game Mode won't open a browser on request). Detail:
  `logs/2026-07-24_linux-agent-streamline.md`.
- **Launch trigger: the Steam wrapper (`savelocker run %command%`), not process polling.**
  Gives exact prefix path + precise hooks via `STEAM_COMPAT_DATA_PATH`/`SteamAppId`. Process-name
  polling is the fallback for non-Steam launchers only (Lutris/Heroic/Bottles) — `/proc/pid/comm`
  truncation and Proton wrapper processes make it unpleasant.
- **Enrollment token is short-lived and unsigned**, not a long-lived API key. Redeemed once for
  the real machine key; expires ~15 min. Deliberately unsigned — the threat is a forged file
  pointing at a malicious server, which signing can't fix (no trust anchor exists before the
  user's own download). Mitigated by HTTPS + hardened restore path + TOFU pin after enrollment.
- **Linux install: `~/.local/share/SaveLocker` + `systemd --user`**, never `/usr` — SteamOS's
  rootfs is immutable and wiped on update. Self-contained publish (SteamOS ships no .NET runtime).
- **Linux dev: WSL2 (Ubuntu 24.04 LTS) on ext4**, not a VM or Arch. Reproduces everything that
  matters (FileShare/inotify/proc/systemd/case-sensitivity) and gives CI parity (`ubuntu-latest`
  is Ubuntu 24.04). Never build from `/mnt/*`. Release job builds on `ubuntu-latest` for the
  oldest-glibc guarantee (package floor is glibc 2.27 either way — .NET's native libs are
  prebuilt by Microsoft, not compiled against the build host).
- **Agent local API: loopback-only, token-authenticated, never serves the machine key.**
  `AgentApiServer` can re-point a machine at another server — reaching it is equivalent to owning
  the box. `--lan` is withdrawn and exits non-zero. Remote access is an SSH tunnel. Full rationale
  in git history if ever revisited.
- **Config/queue/health files are cross-process shared state**, not single-owner. Daemon +
  launch-wrapper are separate processes sharing `config.json` etc. — per-game `flock` +
  in-process semaphore both required, every write atomic (temp+rename), reads happen under the
  lock immediately before use (not just before writing).
- **Untracking a game is a per-machine opt-out, not a local delete.** `AgentConfig.UntrackedGameIds`
  records that this machine does not sync a server game; the game stays on the server for the rest
  of the fleet. A plain `Games.RemoveAll` does not stick — the game is still on the server, so the
  daemon's next reconcile adopts it back, and any stale in-memory writer rewrites the entry. So the
  opt-out is enforced in the primitive, not by convention: `Save()` drops anything on the list,
  `SaveGameSyncState` never re-adds an entry absent from disk, and `CommandPoller` skips adoption.
  `add-game` / enrollment clear the opt-out — explicitly adding a game back is the only "track this
  here again". Untrack from the CLI with `remove-game --name`.
- **Connection-affecting config changes must rebuild the host's `SyncEngine`.** `SyncEngine` caches
  an `ApiClient` (base URL + key + pin). `CommandPoller` builds a fresh client per tick, so a server
  URL change moved control traffic immediately while watcher pushes and queue drains kept hitting
  the old host — split-brain with no error anywhere. `AgentApiServer` fires `onConnectionChanged`
  before the settings response returns; both hosts rebuild there.
- **A pulled archive is hostile input.** No destination write may traverse a symlink below the
  save root (the root itself is followed — it's user-chosen; paths *inside* the archive are not).
  Size caps (100k entries / 2 GB), checked against both declared and actual bytes. A refused
  restore is reported to the console as an event.
- **LAN-only over plain HTTP. No tunnel, no TLS termination, no reverse proxy** (2026-07-27). The
  payload is save files, not credentials or PII, and the earlier Cloudflare Tunnel plan is dropped.
  So `X-Forwarded-*` is never read and no trusted-proxy configuration exists — there is nothing in
  front to trust. The one URL the server hands to *other machines* (enrollment policy, hosted
  installer link) comes from `PublicUrl.For`: `Server:PublicBaseUrl` if configured, else the request
  origin. Minting is **refused when an inferred URL is loopback**, because a console opened at
  `http://localhost:5080` on the server box otherwise mints a file telling the new machine to sync
  with itself. A loopback URL that was *stated* — typed into the override, or put in
  `Server:PublicBaseUrl` — is honoured: agent and server on one box is a real setup, and it is what
  both enrollment suites do. `Server:PublicBaseUrl` is also the single knob to set if a proxy is
  ever added.
- **Lease writes are single statements; losing a race is an answer, not an error.** Acquisition is a
  conditional `UPDATE` (take over a row that is mine or expired) falling back to an `INSERT` whose
  unique `GameId` index arbitrates — the losing caller catches the constraint violation and returns
  `Granted=false` with the holder's lease. Read-then-insert gave one of two simultaneous launches a
  500 at exactly the moment leasing is the thing being relied on. Release, renew, force-release and
  the expiry sweep are conditional statements for the same reason, and the sweep can no longer
  delete a lease renewed since it was selected. `ActiveLeaseAsync` is a pure read: it used to delete
  expired rows, which made `GET /api/overview` a write that two parallel dashboard requests could
  collide on.
- **Every server-authoritative head change fans out; an ordinary push does not.** One code path
  (`SetHeadAndPropagateAsync`) moves `HeadVersionId`. Where the *server* decided — automatic conflict
  policy, manual resolution, rollback, Set as Latest — it queues a deduplicated **unforced** Pull for
  every live machine that syncs the game (mapped save path or uploaded version), skipping the
  uploader that already learned the head from its own response. An agent's parent advances only on
  its own push or pull, so without this the fleet stays on the old parent and re-conflicts on its
  next save — the console said resolved while the machines disagreed. An ordinary push deliberately
  does **not** fan out: the other machines are not displaced by a decision, and a command arriving
  mid-session is the failure the lease and settle gate exist to prevent. Unforced is the safety
  property: unsynced local work reports *blocked* rather than being overwritten (only the console's
  own Pull button forces).
- **Set as Latest / rollback supersedes the conflicts it decides, and only those.** A conflict
  offering the chosen version is resolved in its favour — leaving it open makes the console insist
  the version it was just told to trust is unresolved. A conflict between two *other* versions stays
  open: the admin has said nothing about it, its machine is still stuck, and closing it would also
  disarm the rule that resolution may not rewind a newer Latest. Each supersession is audited as
  `conflict.resolve_superseded`.
- **A credential is only consumed once the thing it buys has been issued.** The SteamGridDB key is
  verified *before* it replaces the stored one, and a rejection is a 4xx — it used to store first,
  ask second, and answer 200 with `{ ok: false }`, so a typo overwrote a working key while the
  console reported success. Enrollment burns its single-use token and issues the machine key inside
  **one transaction**; anything that throws rolls the burn back, so a file is never spent for
  nothing. The single-winner guarantee is unchanged: the burn is still a conditional `UPDATE`, and
  a second redeemer matches zero rows.
- **The SteamGridDB probe must hit an authenticated endpoint.** `search/autocomplete` is served
  *without* a key — it returned 200 with real Celeste data for 25 characters of nonsense, so
  "API key verified" only ever meant the site was reachable. `grids/game/{id}` answers 401 without
  a valid key, which is the question being asked.
- **The hosted installer is staged, published, then the old one is removed — under one gate.**
  `AgentInstallerService` is a singleton holding a `SemaphoreSlim` that manual upload, manual GitHub
  fetch, the background poller and delete all pass through; the fetch holds it across its
  "is this newer?" read too, since that decision is read-then-write. Validation (`.exe`, a usable
  filename, a parseable version) happens **before** anything on disk is touched: the old code
  accepted a 1 KB `notes.txt` with version `not-a-version` and served it to the fleet as the
  installer. The request body is capped at `AgentUpdate:MaxInstallerMb` (200) rather than having
  Kestrel's limit removed outright — `ReadFormAsync` buffers to memory and temp storage, and the
  route is open until an admin password is set.
- **An archive is staged, then published; rows are deleted before files.** Uploads land in
  `{ArchiveRoot}/.incoming/*.part` (same volume, so the publish is one rename) and are size-capped
  while copying, not on declared length — a disconnect used to leave a truncated archive sitting at
  the exact path a `SaveVersion` row would name. Deletion runs the other way round: commit the DB,
  then delete the file, because an orphaned file is wasted space while a live row whose archive is
  gone is a save the console offers and cannot produce. Failed deletes are audited as
  `archive.orphaned` rather than ignored; staging is swept at startup (1 h age floor).
- **Command delivery is at-least-once, under a visibility lease.** A claimed `AgentCommand` is
  Dispatched with a `LeaseExpiresAt` + `ClaimToken`, written by one atomic UPDATE so two pollers
  sharing a machine identity cannot both receive it; when the lease expires unacknowledged the
  command is claimable again (`ClaimCount` records the redelivery, and a reclaim is audited).
  At-most-once was the old behaviour and it silently lost any command whose agent died between the
  poll and the result. Retrying is safe for every type — Pull re-reads the head, Push re-hashes and
  the server answers `NoChange` on identical content, Sync is both, Scan only reads — so a lost
  request is the worse failure. Terminal states stick: a late duplicate result is a no-op.
- **Conflict pressure caps at 3 rejected payloads**, then further pushes report the condition
  without re-uploading. 6-hour overdue threshold surfaces stale conflicts to a connected agent.
  "Keep both" doesn't create two heads — the chosen snapshot becomes Latest, both conflicting
  snapshots get `Protected` (exempt from retention) until explicitly unprotected. Resolution
  refuses to promote something older than the current head.

## Environment facts (user-provided)
- Games are standalone builds, not bought on Steam/Epic → manifest-based detection + manual
  `--dir` fallback is the primary path, not a fallback, on Linux.
- Sync trigger: hybrid (automatic background + manual override).
