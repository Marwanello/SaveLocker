# Manifest path constraints + scheduled refresh (2026-10-14, branch `manifest-path-constraints`)

The backlog item *Constrain manifest paths* — "the Ludusavi manifest is downloaded from mutable
`master`; expanded templates are not proven to stay inside the intended Proton prefix." Built on the
maintainer's own revision after an exchange about its shape: the first plan pinned the feed to an
approved revision; the maintainer pushed back (the manifest is a reliable live feed, and a pin makes
every game released after it undetectable), and the revision below **drops the pin** and replaces it
with (a) constraining what an entry may resolve to, and (b) actually refreshing the feed on a
schedule — which the agent had never done: before this, the cache was downloaded once and served
forever unless someone ran `refresh-manifest` by hand. The maintainer also asked for the refresh
window to be editable in the console, with a "refresh now" button.

## What shipped

1. **Manifest-expanded paths are constrained** (`src/Shared/PathResolver.cs`, new
   `src/Shared/RealPath.cs`). A template must be an absolute path with no `..` segment (strict, even
   when it would stay inside a root), the expanded path is canonicalised before the token-root prefix
   check (the old check compared the RAW string, so `<winAppData>/../../../x` began with a root's value
   and passed), and the final path must survive `WithinRealRoots`: every symlink/junction component is
   resolved (`RealPath.Resolve`, segment by segment, hop-limited, OneDrive-style non-link reparse
   points deliberately treated as ordinary directories) and the result must sit under the
   link-resolved form of one of the resolver's own roots. Applied at `ResolveConcrete`, the single
   choke point every caller already funnels through (ManifestLoader, CommandPoller, FolderSuggestions,
   ProtonRun, CLI `resolve`), including the case-insensitive Wine correction and the `<storeUserId>`
   fan-out branches. Refusal means "no existing save directory" — the user picks, as for every other
   unresolvable entry. User-mapped folders never expand templates and are untouched; `SavePathGuard`
   stays the trusted-user floor.
2. **The manifest refresh engine** (`src/Shared/ManifestLoader.cs`, `src/Agent.Core/Detection.cs`,
   `AgentConfig`). `DownloadAsync` now streams the body with a 64 MB cap, parses it, refuses fewer
   than `MinPlausibleGames` (5,000 — the real one carries ~53k; a proxy error page parses to
   nothing), and only then replaces the cache by a temp-file move. `Detection.RefreshManifestIfDueAsync`
   refreshes when the cache is older than the effective window, keeps the old copy on any failure,
   throttles attempts (1 h automatic, 30 s forced), and never throws; `CommandPoller` calls it
   fire-and-forget on every tick (both hosts) and passes `onManifestRefresh` to the heartbeat so the
   console's button reaches `Detection` too. `AgentConfig` gained `ManifestRefreshDays` (local default
   3), `ConsoleManifestRefreshDays` (pushed), `ManifestRefreshHandledAt` (the server timestamp last
   acted on — persisted so a restart does not re-run a request), and `EffectiveManifestRefreshDays`.
3. **Console control** (server + dashboard). New audited admin routes `POST /api/settings/manifest`
   (window, 0–365, 400 otherwise) and `POST /api/settings/manifest/refresh`; the `AgentHeartbeatResponse`
   carries `manifestRefreshDays` + `manifestRefreshRequestedAt` (both appended/optional — an old agent
   ignores them, a new agent on an old server sees null and keeps its local value), and
   `ServerSettingsDto` carries the same for the console. Configuration → Defaults & maintenance got a
   **Manifest refresh** row: a window select (daily / 3 days / weekly / 14 days / monthly / never) and
   a **Refresh now** button, with "Last requested … ago".
4. **Tests.** New `ManifestPathConstraintTests` (11: traversal beyond and inside roots, non-absolute
   forms, a hostile YAML entry through `ManifestLoader` with a clean control, leaf and chained link
   escapes, a `<storeUserId>` child link, and ordinary templates still resolving) and
   `ManifestRefreshTests` (8: the plausibility floor, size cap, stale refresh, fresh no-op, 0 = never
   + forced refresh, failure keeps the cache and is not retried every tick, console window/request
   bookkeeping — against an `HttpListener` stub). `run-hardening-tests.ps1` gained section 5 (37/37
   Windows): a fixture manifest + a fake prefix, a real traversal entry and a real junction escaping
   it, a clean-sibling control, and the escape target asserted untouched. `run-agent-tests.ps1` gained
   the hostile traversal + clean sibling checks (49/49 against a fresh scratch server). All fixture
   configs that point `ManifestCachePath` at a file now also set `ManifestRefreshDays: 0`
   (`run-agent-tests`, `run-winagent-tests` WA-11, `run-linux-tests`, `run-linux-regression-tests`,
   `crossos.ps1`, and two xunit fixtures).
5. `openapi.json` + `web/src/api-types.ts` regenerated (additions only); Server, Windows agent and
   `Agent.Linux` builds clean; `web` build + lint clean; a live wire check confirmed
   `POST /api/settings/manifest` → `GET /api/settings` → the heartbeat response carrying both fields,
   and 9999 refused with 400.

## Mutation checks (the point of a security change)

- Guards removed from `PathResolver` → 4 xunit checks flip; guards removed from the loader/detection →
  3 refresh checks flip; false in `PathResolver` mutation live through the Windows agent → both new
  hardening-suite checks FAIL against the pre-fix resolver, then pass once restored.
- **Writing the hardening fixture caught a real trap**: the first traversal fixture put its escape
  target one directory above where six `..` segments actually land, so the check passed against BOTH
  the fixed and the mutated code — a vacuous test. The mutation check is what exposed it; fixed and
  re-proven. Exactly why the suite's own convention ("run it against pre-fix code: it must FAIL")
  exists.

## Not done / not verified, honestly

- `run-linux-tests` (WSL) and `run-winagent-tests` (interactive desktop) were not run this session —
  the new hardening section runs on both OSes by design, and the xunit coverage is platform-neutral,
  but the Linux suite has not executed this change.
- The console card was not eyeballed in a browser (no browser available here); it type-checks, builds,
  and the wire behind it was verified live.
- End-to-end agent-side "console pressed refresh → daemon downloads and marks it handled" is exercised
  by unit tests + wiring, not by a live daemon this session.
- Deliberately NOT changed: `SavePathGuard.Canonicalize` still resolves only the leaf component (an
  intermediate-link upgrade for *user* paths is a separate hardening item); the on-disk cache is not
  re-hashed at load (owner-only state dir; path constraints protect regardless; fixture caches must
  keep working).

## Next actions

1. Merge when reviewed; the next release notes need a line: save-detection data now refreshes on its
   own (default every 3 days) and can be set or forced from Configuration → Manifest refresh.
2. Worth a real-fleet look: watch a machine's `agent.log` for "Manifest refreshed: N games." after a
   window passes or the console button is pressed.
3. Follow-up candidates: a `doctor` line showing the cache's age; running `run-linux-tests` in CI
   after this merges.
