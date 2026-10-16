using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// ASP.NET Core host that drives the SaveLocker Agent UI. It serves the bundled React app and a
/// typed local API whose OpenAPI document is the source for the UI's generated TypeScript types.
/// </summary>
public sealed class AgentApiServer : IDisposable
{
    private readonly AgentConfig _config;
    private readonly Func<Task<IReadOnlyList<ScanCandidate>>> _doScan;
    private readonly Func<IReadOnlyList<ScanCandidate>, int[], IReadOnlyDictionary<int, string[]>?, IReadOnlyDictionary<int, LinkChoice>?, Task<(int enrolled, int skipped)>> _enroll;
    private readonly IAutoStart _autoStart;
    private readonly Func<Task<string?>> _pickFolder;
    private readonly Func<LaunchCommandDto> _launchInfo;
    private readonly Func<DeckyStatusDto> _deckyStatus;
    // The Windows analogue of _deckyStatus (tasks/playnite-plugin/plan.md, Phase 14) — but async and
    // network-touching rather than a local read, because "is a newer plugin package sitting on the
    // server, unable to auto-apply while Playnite is running" genuinely needs the same server round
    // trip the agent's own recurring update check makes (Agent.PlaynitePlugin.CheckAsync). Injected
    // because PlaynitePlugin lives in the Windows-only Agent project: Linux supplies nothing.
    private readonly Func<Task<PlaynitePluginStatusDto>> _playnitePluginStatus;
    // The agent-ui card's own two calls (tasks/playnite-plugin/plan.md, Phase 19) — a status read
    // for the suggest/install card, and the explicit, user-clicked install action. Kept as two
    // separate funcs rather than folded into _playnitePluginStatus above: that one is the PLUGIN
    // process's own self-check (a different caller, a different question), and install is a write,
    // which no existing status func here performs.
    private readonly Func<Task<PlaynitePluginCardStatusDto>> _playnitePluginCardStatus;
    private readonly Func<Task<PlaynitePluginStatusDto>> _playnitePluginInstall;
    private readonly PathBrowser _browser;
    private readonly Action? _onConnectionChanged;
    // Invoked after the tracked-game list or a save folder changed AND was durably written, so the
    // host can rebuild its folder watchers. Ordering matters: watchers must be rebuilt from the
    // config that is on disk, never from one a concurrent write is about to supersede.
    private readonly Action? _onGamesChanged;
    private readonly Func<UpdateResult?> _getUpdateResult;
    // "Downloaded, verified and waiting", which is a different state from _getUpdateResult's "the
    // server is offering something newer" — and the only one anything may offer to install now.
    // Injected because Updater lives in Agent.Linux: Windows stages nothing and supplies nothing.
    private readonly Func<StagedUpdateInfo?> _stagedUpdate;
    // Owned by the host so it outlives any single engine rebuild (a settings change replaces the
    // engine; the activity feed a user is watching should not reset because of that).
    private readonly SyncActivityTracker _activity;
    // "Sync all" for the Overview page's button — same operation the tray menu offers, resolved
    // against whichever engine and game list are current at the moment it is clicked.
    private readonly Func<Task<string>> _syncAll;
    // The Decky/Playnite launch gate (Phase 11) — SyncEngine.PrepareLaunchAsync, resolved against
    // whichever engine is current at the moment it's called, same reasoning as _syncAll above.
    private readonly Func<TrackedGame, CancellationToken, Task<LaunchGateResult>> _prepareLaunch;
    // POST /api/games/{id}/launch-answer (tasks/emulator-saves Phase 5c) — SyncEngine.AnswerLaunchQuestion.
    private readonly Func<TrackedGame, LaunchAnswerRequest, bool> _answerLaunch;
    // POST /api/games/{id}/dlc/pull-anyway (tasks/emulator-saves Phase 5c) — SyncEngine.PullAsync past the DLC check.
    private readonly Func<TrackedGame, CancellationToken, Task<string>> _pullAnyway;
    // POST /api/games/{id}/post-exit-sync (tasks/playnite-plugin/plan.md, Phase 5) — SyncEngine
    // .OnGameExitAsync, resolved the same way _prepareLaunch is: against whichever engine a settings
    // change may have rebuilt since the host wired this up.
    private readonly Func<TrackedGame, CancellationToken, Task> _postExitSync;
    // SyncEngine.ReleaseLaunchLeaseAsync — for a deferred exit (post-exit-sync while _syncGate is
    // held) whose game was untracked before the gate freed: no push, but the checkout still ends.
    private readonly Func<TrackedGame, CancellationToken, Task> _releaseLease;
    // POST /api/games/{id}/sync — SyncEngine.SyncGameAsync, resolved the same way as the two above.
    private readonly Func<TrackedGame, GameSyncMode, CancellationToken, Task<string>> _syncGame;

    // Raises the host's own window at a route: what a Windows toast button reaches through /open.
    // Null on a host with no window of its own, where /open falls through to the browser UI.
    private readonly Action<string>? _openView;
    // Hands a file to the desktop (Explorer's "select it" on Windows, xdg-open on Linux). Returns
    // false when there is nowhere to show it — a headless box — so /api/open-log can say so.
    private readonly Func<string, bool>? _openFile;
    /// <summary>Maps one of a game's extra save folders through the engine, which carries its shadow over.</summary>
    private readonly Func<TrackedGame, string, string, KeepSide?, Task<MapFolderResult>> _mapFolder;
    // SyncEngine.ForgetSavePathAsync: dropping an extra folder deletes its shadow, so it runs under the
    // game's lock in whichever engine is current.
    private readonly Func<TrackedGame, string, Task<bool>> _forgetFolder;
    // SyncEngine.WithGameLockAsync in whichever engine is current: what changes a Switch game's copies runs under the game's
    // sync lock. A host with no engine runs it as it is.
    private readonly Func<TrackedGame, Func<Task<string?>>, Task<(bool Ran, string? Result)>> _withGameLock;
    // POST /api/conflicts/{id}/resolve — SyncEngine.ResolveOwnConflictAsync in whichever engine is current.
    // Null on a host with no engine, where the route only tells the server.
    private readonly Func<ConflictDto, Guid, bool, CancellationToken, Task<(bool Ok, string Message)>>? _resolveConflict;
    private readonly Func<TrackedGame, bool, CancellationToken, Task<(bool Ok, string Message)>> _takeSide;
    // "Also found" for tracked games (tasks/multiple-save-paths plan §8). Reads the manifest and a few
    // folders per game, so it is computed on request, never on a timer.
    private readonly FolderSuggestions _suggestions;
    // Per-game save-dir resolution for /api/candidates/lookup and /api/manifest/search (Phase 5).
    // Host-agnostic (its own Windows-only checks no-op cleanly on Linux), so it is taken directly
    // rather than through a delegate — unlike _doScan/_enroll, nothing here differs per host.
    private readonly Detection _detection;
    // One sync at a time, however many surfaces are offering the button — see /api/sync.
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    // One manual sync per GAME (POST /api/games/{id}/sync). Deliberately not _syncGate — see that route.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> _gameSyncGates = new();
    // Games whose post-exit-sync arrived while _syncGate was held, queued to run once it frees.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _deferredExits = new();
    // A queued game's folder size, measured at most once a minute — see /api/offline-queue.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (long Bytes, long MeasuredAt)> _queuedSizes = new();
    // The most one art request may hold a browser connection: the UI asks for a grid of them at once,
    // and a browser gives one origin only a handful of connections.
    private static readonly TimeSpan ArtBudget = TimeSpan.FromSeconds(20);
    private readonly string _uiRoot;
    private readonly LocalAuth _auth;
    // Lease warnings are persisted, not held in memory: the Linux launch wrapper is a separate
    // short-lived process from the daemon, so a warning it raises can only reach a UI through
    // shared state on disk. See LeaseWarningStore.
    private readonly LeaseWarningStore _leaseWarnings;

    private IReadOnlyList<ScanCandidate>? _candidateCache;
    private WebApplication? _app;

    public int Port { get; }

    /// <summary>What a link to <c>/open</c> must carry to raise the host's window (see MapUi).</summary>
    public string OpenLinkKey => _auth.OpenLinkKey;

    public AgentApiServer(
        int port,
        AgentConfig config,
        Func<Task<IReadOnlyList<ScanCandidate>>> doScan,
        Func<IReadOnlyList<ScanCandidate>, int[], IReadOnlyDictionary<int, string[]>?, IReadOnlyDictionary<int, LinkChoice>?, Task<(int enrolled, int skipped)>> enroll,
        IAutoStart autoStart,
        Detection detection,
        Func<Task<string?>>? pickFolder = null,
        Action? onConnectionChanged = null,
        Func<UpdateResult?>? getUpdateResult = null,
        IEnumerable<string>? browseRoots = null,
        Func<LaunchCommandDto>? launchInfo = null,
        Action? onGamesChanged = null,
        Func<DeckyStatusDto>? deckyStatus = null,
        Func<StagedUpdateInfo?>? stagedUpdate = null,
        SyncActivityTracker? activity = null,
        Func<Task<string>>? syncAll = null,
        Func<TrackedGame, CancellationToken, Task<LaunchGateResult>>? prepareLaunch = null,
        Func<TrackedGame, LaunchAnswerRequest, bool>? answerLaunch = null,
        Func<TrackedGame, CancellationToken, Task<string>>? pullAnyway = null,
        Func<TrackedGame, CancellationToken, Task>? postExitSync = null,
        Func<TrackedGame, CancellationToken, Task>? releaseLease = null,
        Func<Task<PlaynitePluginStatusDto>>? playnitePluginStatus = null,
        Func<Task<PlaynitePluginCardStatusDto>>? playnitePluginCardStatus = null,
        Func<Task<PlaynitePluginStatusDto>>? playnitePluginInstall = null,
        Func<TrackedGame, GameSyncMode, CancellationToken, Task<string>>? syncGame = null,
        Action<string>? openView = null,
        Func<string, bool>? openFile = null,
        Func<TrackedGame, string, string, KeepSide?, Task<MapFolderResult>>? mapFolder = null,
        Func<TrackedGame, string, Task<bool>>? forgetFolder = null,
        Func<string, string?>? prefixForAppId = null,
        Func<ConflictDto, Guid, bool, CancellationToken, Task<(bool Ok, string Message)>>? resolveConflict = null,
        Func<TrackedGame, Func<Task<string?>>, Task<(bool Ran, string? Result)>>? withGameLock = null,
        Func<TrackedGame, bool, CancellationToken, Task<(bool Ok, string Message)>>? takeSide = null)
    {
        _resolveConflict = resolveConflict;
        _takeSide = takeSide ?? ((_, _, _) => Task.FromResult((false, "Not available.")));
        _withGameLock = withGameLock ?? (async (_, work) => (true, await work()));
        _forgetFolder = forgetFolder ?? ((_, _) => Task.FromResult(false));
        _suggestions = new FolderSuggestions(config, detection, prefixForAppId);
        _openFile = openFile;
        _mapFolder = mapFolder ?? ((_, _, _, _) => Task.FromResult(new MapFolderResult(false, "Not available.")));
        _browser = new PathBrowser(browseRoots);
        Port = port;
        _config = config;
        _doScan = doScan;
        _enroll = enroll;
        _autoStart = autoStart;
        _pickFolder = pickFolder ?? (() => Task.FromResult<string?>(null));
        _launchInfo = launchInfo ?? (() => new LaunchCommandDto(null, null));
        // Default is "not applicable", which is exactly right for the Windows tray: Decky cannot
        // exist there, so the host injects nothing and the UI hides the card.
        _deckyStatus = deckyStatus ?? (() => new DeckyStatusDto(false, false, false, null, ""));
        // Default is "not applicable" for the same reason as _deckyStatus above, mirrored: Linux
        // cannot run Playnite, so Daemon.cs injects nothing and this route answers a neutral status.
        _playnitePluginStatus = playnitePluginStatus ??
            (() => Task.FromResult(new PlaynitePluginStatusDto("NotApplicable", "", null, null)));
        _playnitePluginCardStatus = playnitePluginCardStatus ??
            (() => Task.FromResult(new PlaynitePluginCardStatusDto(false, false, null, null, "")));
        _playnitePluginInstall = playnitePluginInstall ??
            (() => Task.FromResult(new PlaynitePluginStatusDto("NotApplicable", "", null, null)));
        _onConnectionChanged = onConnectionChanged;
        _onGamesChanged = onGamesChanged;
        _getUpdateResult = getUpdateResult ?? (() => null);
        _stagedUpdate = stagedUpdate ?? (() => null);
        _activity = activity ?? new SyncActivityTracker();
        _syncAll = syncAll ?? (() => Task.FromResult("Not available."));
        _prepareLaunch = prepareLaunch ?? ((_, _) => Task.FromResult(new LaunchGateResult(LaunchDecision.Proceed)));
        _answerLaunch = answerLaunch ?? ((_, _) => false);
        _pullAnyway = pullAnyway ?? ((g, _) => Task.FromResult($"Nothing was pulled for {g.Name}."));
        _postExitSync = postExitSync ?? ((_, _) => Task.CompletedTask);
        _releaseLease = releaseLease ?? ((_, _) => Task.CompletedTask);
        _syncGame = syncGame ?? ((_, _, _) => Task.FromResult("Not available."));
        _openView = openView;
        _detection = detection;
        _uiRoot = Path.Combine(AppContext.BaseDirectory, "agent-ui");
        _auth = LocalAuth.LoadOrCreate(config.ConfigPath);
        _leaseWarnings = LeaseWarningStore.For(config);
    }

    public void AddLeaseWarning(string gameName, string holderMachine) =>
        _leaseWarnings.Add(gameName, holderMachine);

    public void ClearLeaseWarning(string gameName) => _leaseWarnings.Clear(gameName);

    public void Start()
    {
        if (_app is not null) return;

        var options = new WebApplicationOptions
        {
            ApplicationName = typeof(AgentApiServer).Assembly.FullName,
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Directory.Exists(_uiRoot) ? _uiRoot : null,
        };
        var builder = WebApplication.CreateSlimBuilder(options);

        // Loopback only, always. The management API hands out control of this machine, and binding
        // it to a LAN interface would expose that to the whole network — see Decisions.md.
        builder.WebHost.ConfigureKestrel(server => server.ListenLocalhost(Port));

        // No CORS policy on purpose: the bundled UI is same-origin, so nothing legitimate needs
        // one, and the previous AllowAnyOrigin let any web page read this API's responses.
        builder.Services.AddOpenApi(o =>
        {
            // .NET 10 emits OpenAPI 3.1, which hedges `long` as ["integer","string"] for a
            // serializer that might fall back to a string — System.Text.Json never does. Left in,
            // every byte-count field (ActivitySnapshotDto's progress) generates as `number | string`
            // in agent-ui's types for a case that cannot occur. Same fix as the server's own
            // AddOpenApi (Program.cs) — see its comment for the full reasoning.
            o.AddSchemaTransformer((schema, _, _) =>
            {
                if (schema.Type is { } t
                    && t.HasFlag(JsonSchemaType.String)
                    && (t.HasFlag(JsonSchemaType.Integer) || t.HasFlag(JsonSchemaType.Number)))
                {
                    schema.Type = t & ~JsonSchemaType.String;
                    schema.Pattern = null;
                }
                return Task.CompletedTask;
            });
        });

        _app = builder.Build();
        _app.Use(GuardAsync);
        // Rewrites the served document's components.schemas into alphabetical order — see
        // OpenApiSchemaSorter's own doc comment for why this has to happen on the raw JSON text
        // rather than via AddDocumentTransformer (fixes the recurring Windows-vs-Linux
        // api-types.ts ordering diff for good, instead of requiring a Linux regeneration). Shared
        // with Server/Program.cs, which serves its own OpenAPI document and needs the identical fix.
        _app.Use(OpenApiSchemaSorterMiddleware.SortOpenApiSchemasAsync);
        _app.MapOpenApi();
        MapApi(_app);
        MapUi(_app);
        // Task.Run for the same reason as Dispose: this runs on the WinForms UI thread, and awaiting
        // there directly risks the continuation needing the very thread that is blocking. Exceptions
        // still propagate — a server that did not start must not be reported as started.
        Task.Run(() => _app.StartAsync()).GetAwaiter().GetResult();

        AgentLogger.Log($"AgentApiServer listening on http://localhost:{Port}/ — UI root: {_uiRoot} (exists: {Directory.Exists(_uiRoot)})");
    }

    /// <summary>
    /// Every request must come from this machine (loopback Host, no foreign Origin), and every
    /// request except the UI itself must carry the local token. The UI is exempt because it is how
    /// the token is delivered — it has nothing to present yet.
    /// </summary>
    private async Task GuardAsync(HttpContext context, RequestDelegate next)
    {
        if (!LocalAuth.IsLoopbackHost(context.Request.Host.Host) ||
            !LocalAuth.IsAllowedOrigin(context.Request.Headers.Origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        // /openapi is deliberately not token-gated: it is a static description of the API, holds no
        // machine state and no secrets, and the UI's type generator (openapi-typescript) has no way
        // to send a header. Everything that reads or changes state does need the token.
        if (context.Request.Path.StartsWithSegments("/api") &&
            !_auth.IsValid(context.Request.Headers[LocalAuth.HeaderName]))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Static-file middleware would otherwise hand out index.html verbatim, placeholder and all.
        if (context.Request.Path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
            context.Request.Path = "/";

        await next(context);
    }

    private void MapApi(WebApplication app)
    {
        app.MapGet("/api/state", () =>
        {
            var warnings = _leaseWarnings.Read()
                .Select(e => new LeaseWarningDto(e.GameName, e.HolderMachine))
                .ToArray();

            var lastSyncAgo = _config.LastSyncTime.HasValue
                ? FormatAgo(DateTime.UtcNow - _config.LastSyncTime.Value)
                : "—";

            return new AgentStateDto(
                !string.IsNullOrEmpty(_config.ApiKey),
                UpdateChecker.CurrentVersionText,
                UpdateChecker.BuildLabel,
                _config.MachineName,
                _config.ServerUrl,
                _autoStart.IsEnabled(),
                _config.Games.Count(g => g.IsEnrolledHere),
                _config.TotalSavesPushed,
                lastSyncAgo,
                warnings,
                _config.SettleQuietSeconds,
                OperatingSystem.IsWindows() ? "Windows" : "Linux",
                _config.MachineId,
                _config.SentTodayBytesNow(),
                OfflineQueue.For(_config).GetAll().Count,
                !_config.ServerUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "plain-http"
                    : string.IsNullOrEmpty(_config.ServerPin) ? "unpinned" : "pinned",
                _config.AutoUpdate);
        }).Produces<AgentStateDto>();

        app.MapPost("/api/lease-warnings/dismiss", (DismissWarningRequest body) =>
        {
            if (!string.IsNullOrWhiteSpace(body.GameName)) ClearLeaseWarning(body.GameName);
            return new OkResponse();
        }).Produces<OkResponse>();

        app.MapGet("/api/candidates", async () => ToCandidateDtos(
            _candidateCache ?? await RescanAsync())).Produces<CandidateDto[]>();

        // How many games the LAST scan suggested, without scanning: the sidebar count must never make
        // navigating (or a poll) walk the disk. Null before any scan has run this session.
        app.MapGet("/api/candidates/cached", () => new CandidateCountDto(
            _candidateCache?.Count(c => !c.HasSteamCloud))).Produces<CandidateCountDto>();

        // "Can this machine reach its server, and how fast" — one anonymous GET of /health through the
        // same TLS policy every other call uses (so a pin mismatch shows up here too). Never throws:
        // the answer IS the failure.
        app.MapPost("/api/test-connection", async () =>
        {
            if (string.IsNullOrWhiteSpace(_config.ServerUrl))
                return new TestConnectionDto(false, null, 0, "No server URL is set.");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var http = ServerHttp.Create(_config, withApiKey: false);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var resp = await http.GetAsync("/health", cts.Token);
                return new TestConnectionDto(resp.IsSuccessStatusCode, (int)resp.StatusCode, sw.ElapsedMilliseconds,
                    resp.IsSuccessStatusCode ? null : $"The server answered {(int)resp.StatusCode}.");
            }
            catch (OperationCanceledException)
            {
                return new TestConnectionDto(false, null, sw.ElapsedMilliseconds, "The server did not answer within 10 seconds.");
            }
            catch (Exception ex)
            {
                return new TestConnectionDto(false, null, sw.ElapsedMilliseconds, ex.InnerException?.Message ?? ex.Message);
            }
        }).Produces<TestConnectionDto>();

        // How big the save folder is right now, by a plain directory walk — no hashing, so unlike
        // sync-status it is cheap enough for the game page to ask on open. Never on a list or a timer.
        app.MapGet("/api/games/{id:guid}/local-size", Results<Ok<LocalSizeDto>, NotFound> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            return game is null
                ? TypedResults.NotFound()
                // Every folder of the game on this machine; an unmapped one's shadow is SaveLocker's, not "here".
                : TypedResults.Ok(new LocalSizeDto(game.RealRoots(_config.StateDir).Sum(r => FolderSize.Of(r.Directory))));
        }).Produces<LocalSizeDto>();

        // Show a game's save folder in the desktop's file manager. Same seam as /api/open-log: false
        // means there was no desktop to show it on, and the caller shows the path instead.
        app.MapPost("/api/games/{id:guid}/open-folder",
            Results<Ok<OpenLogResponse>, Conflict<OpenLogResponse>, NotFound> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();
            var opened = false;
            try { opened = Directory.Exists(game.SaveDirectory) && _openFile?.Invoke(game.SaveDirectory) == true; }
            catch (Exception ex) { AgentLogger.LogException("AgentApiServer.open-folder", ex); }
            var body = new OpenLogResponse(opened, game.SaveDirectory);
            return opened ? TypedResults.Ok(body) : TypedResults.Conflict(body);
        }).Produces<OpenLogResponse>().Produces<OpenLogResponse>(StatusCodes.Status409Conflict);

        app.MapPost("/api/candidates/rescan", async () =>
            ToCandidateDtos(await RescanAsync())).Produces<CandidateDto[]>();

        app.MapPost("/api/enroll", async Task<Results<Ok<EnrollResponse>, BadRequest<ErrorResponse>>>
            (EnrollRequest body) =>
        {
            if (body.Ids is null)
                return TypedResults.BadRequest(new ErrorResponse("ids is required"));
            var candidates = _candidateCache ?? Array.Empty<ScanCandidate>();
            var alsoSync = body.AlsoSync?
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.SelectMany(c => c.Paths ?? Array.Empty<string>()).ToArray());
            var links = body.Links?
                .Where(l => l.Choice is EnrollLinks.Separate or EnrollLinks.Game)
                .GroupBy(l => l.Id)
                .ToDictionary(g => g.Key, g => new LinkChoice(g.Last().Choice, g.Last().GameId));
            var (enrolled, skipped) = await _enroll(candidates, body.Ids, alsoSync, links);
            return TypedResults.Ok(new EnrollResponse(enrolled, skipped,
                Enroller.Progress.Notes is { Count: > 0 } notes ? notes.ToArray() : null,
                Enroller.Progress.Info is { Count: > 0 } info ? info.ToArray() : null));
        });

        // Which server game each emulator save on Add games would join, and what else it could (EnrollLinks).
        // One read of the server's games per call, so the page asks once per list, not per row. Never fails
        // the page: an unreachable server answers with no links, and every row is linked automatically.
        app.MapGet("/api/candidates/links", async () =>
        {
            var candidates = _candidateCache ?? await RescanAsync();
            if (!candidates.Any(c => c.Source == ScanSource.Emulator) || string.IsNullOrEmpty(_config.ApiKey))
                return new CandidateLinksResponse(true, []);
            List<GameDto> server;
            try { server = await ApiClient.For(_config).ListGamesAsync(); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return new CandidateLinksResponse(false, []);
            }
            return new CandidateLinksResponse(true, candidates
                .Select((c, id) => (c, id))
                .Where(x => x.c.Source == ScanSource.Emulator && Enroller.TrackedFor(_config, x.c) is not { IsEnrolledHere: true })
                .Select(x => new CandidateLinksDto(x.id, EnrollLinks.For(server, x.c).ToArray()))
                .Where(l => l.Options.Length > 0)
                .ToArray());
        }).Produces<CandidateLinksResponse>();

        // Read by the Add games page while its POST /api/enroll is still open: enrolling creates each
        // game on the server one round trip at a time, which from the browser looked like a hang.
        app.MapGet("/api/enroll/progress", () =>
        {
            var p = Enroller.Progress;
            return new EnrollProgressDto(p.Active, p.Index, p.Total, p.Game, p.Step, p.Enrolled, p.Skipped);
        }).Produces<EnrollProgressDto>();

        app.MapGet("/api/config", () => new AgentConfigDto(
            _config.ServerUrl,
            _config.MachineName,
            _autoStart.IsEnabled(),
            _config.SettleQuietSeconds)).Produces<AgentConfigDto>();

        app.MapPost("/api/config", Results<Ok<ConfigChangeResponse>, BadRequest<ErrorResponse>>
            (ConfigRequest body) =>
        {
            // A host caches an ApiClient (base URL + key + pin) inside its SyncEngine. Changing any
            // of that here without telling the host leaves the daemon split-brained: the poller
            // rebuilds its client every tick and talks to the NEW server while watcher pushes and
            // queue drains keep hitting the OLD one. Track the change, then hand it back below.
            // Applied FIRST, and checked. It used to run last and its result was discarded, so a
            // registry write refused by group policy still answered { ok: true } and the UI drew a
            // ticked box for a machine that would not start the agent at login. Doing it here also
            // means a refusal costs nothing: no setting has been mutated yet. WA-10.
            if (body.StartWithWindows.HasValue)
            {
                var auto = _autoStart.SetEnabled(body.StartWithWindows.Value);
                if (!auto.Ok)
                    return TypedResults.BadRequest(new ErrorResponse(
                        auto.Error ?? "Could not change the startup setting."));
            }

            // Whether this agent may stage a newer version by itself (Linux). A single-field write
            // through UpdateSettings, so it cannot undo what another process changed in between.
            if (body.AutoUpdate.HasValue)
            {
                try { _config.UpdateSettings(c => c.AutoUpdate = body.AutoUpdate.Value); }
                catch (AgentStateLockException ex) { return TypedResults.BadRequest(new ErrorResponse(ex.Message)); }
            }

            // Only the server URL means "connection changed" — the engine's lease/API client are
            // keyed off it, not off the display name, so renaming this machine must not retire the
            // current engine and give up whatever lease it holds.
            var before = _config.ServerUrl;
            // Everything needed to undo a failed transition. Captured BEFORE any mutation, because
            // the point is that a rejected request leaves a config the agent can still start from.
            var previousIdentity = _config.CaptureIdentity();
            var previousName = _config.MachineName;
            var previousSettle = _config.SettleQuietSeconds;

            var identityCleared = false;
            if (!string.IsNullOrWhiteSpace(body.ServerUrl))
            {
                // Validated BEFORE anything is written. The old code assigned the raw string, saved
                // it, and only then built a client from it — so 'htp://typo' persisted, the client
                // constructor threw, the caller got a 500, and every subsequent start of the agent
                // crashed on the same unusable value. WA-04.
                if (!_config.TrySetServerUrl(body.ServerUrl, out identityCleared))
                    return TypedResults.BadRequest(new ErrorResponse(ServerOrigin.InvalidUrlMessage));
            }
            if (!string.IsNullOrWhiteSpace(body.MachineName))
                _config.MachineName = body.MachineName.Trim();
            if (body.SettleQuietSeconds.HasValue)
                _config.SettleQuietSeconds = Math.Clamp(body.SettleQuietSeconds.Value, 0, 300);

            try
            {
                _config.Save();

                // Rebuild before the response returns, so no request that starts after the caller
                // sees 200 can still be addressed to the previous server.
                if (before != _config.ServerUrl)
                    _onConnectionChanged?.Invoke();
            }
            catch (Exception ex)
            {
                // Roll back every field together. A half-applied transition is the one outcome worse
                // than a rejected one: the agent would hold a new URL with the old credentials.
                _config.RestoreIdentity(previousIdentity);
                _config.MachineName = previousName;
                _config.SettleQuietSeconds = previousSettle;
                try { _config.Save(); } catch { /* the in-memory rollback is what matters */ }
                AgentLogger.LogException("AgentApiServer.Config", ex);
                return TypedResults.BadRequest(new ErrorResponse(
                    "Could not apply the change; the previous settings were kept. " + ex.Message));
            }

            // The UI has to know the machine was un-enrolled, or the user is left looking at a
            // "not connected" agent with no idea why their key stopped working. The effective
            // auto-start state is re-read rather than echoed back, so the toggle renders what the
            // machine will actually do — including a change that was reverted underneath us.
            return TypedResults.Ok(new ConfigChangeResponse(identityCleared, _autoStart.IsEnabled()));
        }).Produces<ConfigChangeResponse>();

        // The look this window draws itself in (theme, accent, app mark) — pushed by the console on
        // each heartbeat unless this machine has opted out. Its own route pair rather than fields on
        // AgentConfigDto: that record is read by the Decky plugin, and this one is polled by every
        // agent-ui page. Both are local reads/writes of config.json — no disk walk, no network.
        app.MapGet("/api/appearance", () => ToAppearanceDto(_config)).Produces<AgentAppearanceDto>();

        app.MapPost("/api/appearance", Results<Ok<AgentAppearanceDto>, BadRequest<ErrorResponse>>
            (AgentAppearanceRequest body) =>
        {
            if (body.Look is { } look && !Appearances.IsValid(look.Theme, look.Accent, look.Mark))
                return TypedResults.BadRequest(new ErrorResponse(
                    $"Theme must be one of {string.Join(", ", Appearances.Themes)}; accent one of " +
                    $"{string.Join(", ", Appearances.Accents)}; mark one of {string.Join(", ", Appearances.Marks)}."));

            _config.SetAppearance(body.Follow, body.Look);
            return TypedResults.Ok(ToAppearanceDto(_config));
        }).Produces<AgentAppearanceDto>();

        app.MapPost("/api/register", async Task<Results<Ok<RegisterResponse>, InternalServerError<ErrorResponse>>>
            (RegisterRequest body) =>
        {
            var previousIdentity = _config.CaptureIdentity();
            try
            {
                var api = ApiClient.For(_config, useConfigKey: false);
                var reg = await api.RegisterAsync(_config.MachineName, body.AdminPassword);

                // All three together, in one Save. The pin was previously ignored here entirely, so
                // registering against an https server through the UI established an identity with no
                // TLS pin at all — every later connection had nothing to compare against, and the
                // TOFU guarantee enrollment provides simply did not exist on this path. WA-04.
                _config.ApiKey = reg.ApiKey;
                _config.MachineId = reg.MachineId;
                if (api.ObservedPin is { } pin) _config.ServerPin = pin;
                _config.Save();
                _onConnectionChanged?.Invoke();
                // The key itself is never returned — it is written to config and used from there.
                // Nothing in the UI needs its value, and echoing it only creates a way to exfiltrate it.
                return TypedResults.Ok(new RegisterResponse(_config.MachineName));
            }
            catch (Exception ex)
            {
                // A failed registration must not leave a half-built identity — in particular not a
                // pin or key from a partially-completed attempt against the new origin.
                _config.RestoreIdentity(previousIdentity);
                return TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
        });

        // Join this machine to a server from its join link (tasks/enrollment-options, option B) —
        // what the Settings tab and a "#enroll:<link>" deep link call. The same AgentEnroller the
        // CLI's `enroll --url` uses, so the two cannot drift; a bare code is accepted too, with the
        // server addressed by this machine's saved one (or an explicit override).
        app.MapPost("/api/join", async Task<Results<Ok<JoinResponse>, BadRequest<ErrorResponse>, InternalServerError<ErrorResponse>>>
            (JoinRequest body) =>
        {
            string joinUrl;
            if (!string.IsNullOrWhiteSpace(body.Url))
            {
                joinUrl = body.Url.Trim();
            }
            else
            {
                var normalized = JoinCodes.NormalizeJoinCode(body.Code);
                if (normalized is null)
                    return TypedResults.BadRequest(new ErrorResponse(
                        "That does not look like a join code (8 letters and digits). Paste the whole join link, or the code with the server address."));
                var serverUrl = !string.IsNullOrWhiteSpace(body.ServerUrl) ? body.ServerUrl.Trim()
                    : _config.ServerUrl;
                if (string.IsNullOrWhiteSpace(serverUrl))
                    return TypedResults.BadRequest(new ErrorResponse(
                        "Give the server address too, or paste the whole join link."));
                joinUrl = serverUrl.TrimEnd('/') + "/join/" + normalized;
            }

            var previousIdentity = _config.CaptureIdentity();
            var previousName = _config.MachineName;
            try
            {
                var policy = await AgentEnroller.FetchPolicyAsync(joinUrl);
                var outcome = await AgentEnroller.ApplyAsync(_config, policy, _detection, body.MachineName, AgentLogger.Log);
                // The engine caches an ApiClient (base URL + key + pin); a new identity must reach it
                // before any request that starts after this response — same rule as /api/register.
                _onConnectionChanged?.Invoke();
                return TypedResults.Ok(new JoinResponse(outcome.MachineName, outcome.MachineId, outcome.Mapped, outcome.Unmapped));
            }
            catch (Exception ex)
            {
                // A failed join must not leave a half-built identity — the key of a server that was
                // never confirmed, a name from neither side. In-memory rollback, like /api/register.
                _config.RestoreIdentity(previousIdentity);
                _config.MachineName = previousName;
                // A link the other end refused, an unreachable server, a timeout: all things the user
                // can act on, and all carrying the server's own sentence — 400, so the UI shows it
                // as a fixable input problem rather than a fault in this agent.
                return ex is HttpRequestException or InvalidOperationException or TaskCanceledException or UriFormatException
                    ? TypedResults.BadRequest(new ErrorResponse(ex.Message))
                    : TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
        }).Produces<JoinResponse>().Produces<ErrorResponse>();

        // HasSteamCloud is decided once, at enrollment (Enroller.EnrollAsync, from the candidate's
        // own ScanCandidate.HasSteamCloud) - not re-derived here from a name-only manifest lookup.
        // That lookup answers "does a Steam release of this TITLE have Steam Cloud", which is true
        // for plenty of titles this machine tracks a Heroic (Epic/GOG) install of, not "is THIS
        // install a Steam one" - see TrackedGame.HasSteamCloud's own doc comment.
        app.MapGet("/api/games", () => _config.Games
            .Select(g => new TrackedGameDto(
                g.GameId, g.Name, g.SaveDirectory, g.ProcessNames.ToArray(), g.Alias,
                SteamShortcuts.UnsignedAppId(g.ResolveSteamAppId()), g.PullBeforeLaunchEnabled,
                g.HasSteamCloud, g.PushAfterExitEnabled, g.InstallDir, g.LastPushBytes, g.LastPushAt,
                SaveFolderDto.Of(g), g.IsEnrolledHere ? g.Source : null, g.Release, g.IncludeGlobs.FirstOrDefault()))
            .ToArray()).Produces<TrackedGameDto[]>();

        // Editing the process names is the other half of WA-08: discovery can only know them for a
        // non-Steam shortcut, so for everything else the user needs a way to supply them — and the
        // UI needs to be able to show, honestly, that lifecycle sync is unconfigured until they do.
        app.MapPost("/api/games/{id:guid}/processes",
            Results<Ok<OkResponse>, BadRequest<ErrorResponse>> (Guid id, ProcessNamesRequest body) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.Ok(new OkResponse());

            // Accepts "foo.exe", "foo", or a comma-separated list, and normalises: the watcher
            // matches on Process.ProcessName, which never carries the extension.
            game.ProcessNames = (body.ProcessNames ?? Array.Empty<string>())
                .SelectMany(p => p.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(p => GameActivity.ProcessNameFromExe(p))
                .Where(p => p is not null)
                .Select(p => p!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            try { _config.Save(); }
            catch (AgentStateLockException ex) { return TypedResults.BadRequest(new ErrorResponse(ex.Message)); }
            _onGamesChanged?.Invoke();
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>();

        // The Decky plugin's per-game alias editor: a manual override for name-based matching, for
        // when this agent has not resolved a Steam AppID for the game (see TrackedGame.Alias). Goes
        // through SaveGameAlias rather than a plain mutate + Save() like /processes above — unlike
        // that route, this one can be hit by the Decky plugin's HTTP request on one thread at the
        // same moment a folder watcher on another thread calls its own Save(), and only the re-read-
        // under-lock, single-field write actually survives that race. Silently OK on an unknown id,
        // same as /processes — SaveGameAlias itself no-ops when the id isn't found on disk.
        app.MapPost("/api/games/{id:guid}/alias", (Guid id, AliasRequest body) =>
        {
            _config.SaveGameAlias(id, body.Alias);
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>();

        // The Decky plugin's per-game Gaming Mode pull toggle. Server-side for the same reason the
        // alias route above is: it is the one place every machine syncing this game (and a Decky
        // plugin reinstall on this one) can agree on the setting, rather than it living only in that
        // plugin's own local settings file. `enabled: null` clears the override.
        app.MapPost("/api/games/{id:guid}/pull-before-launch", (Guid id, PullBeforeLaunchRequest body) =>
        {
            _config.SaveGamePullBeforeLaunch(id, body.Enabled);
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>();

        // The Decky/Playnite per-game post-exit push toggle (tasks/playnite-plugin/plan.md, Phase 4).
        // Server-side for the same reason pull-before-launch is above. `enabled: null` clears the
        // override, which SyncEngine.OnGameExitAsync then reads back as "push" (today's unconditional
        // behaviour).
        app.MapPost("/api/games/{id:guid}/push-after-exit", (Guid id, PushAfterExitRequest body) =>
        {
            _config.SaveGamePushAfterExit(id, body.Enabled);
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>();

        // Untracks the game on THIS machine only. It stays on the server for the rest of the fleet;
        // the opt-out is what stops the poller adopting it straight back.
        app.MapPost("/api/games/{id:guid}/remove",
            Results<Ok<OkResponse>, BadRequest<ErrorResponse>> (Guid id) =>
        {
            try { _config.SetTracked(id, tracked: false); }
            catch (AgentStateLockException ex) { return TypedResults.BadRequest(new ErrorResponse(ex.Message)); }
            _onGamesChanged?.Invoke();
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>();

        app.MapPost("/api/games/{id:guid}/folder", async Task<Results<Ok<OkResponse>, BadRequest<ErrorResponse>>>
            (Guid id, FolderRequest body) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is not null && body.Path is not null &&
                body.Key is { Length: > 0 } key && key != SaveRoot.PrimaryKey)
            {
                // One of the game's extra folders (tasks/multiple-save-paths). The same two tiers of
                // checks as the primary folder below; the engine adds the folder rules and carries the
                // shadow's files over, so this is never a bare assignment.
                if (game.ExtraPaths.All(p => p.Key != key))
                    return TypedResults.BadRequest(new ErrorResponse($"'{game.Name}' has no save folder called '{key}'."));
                KeepSide? keep;
                switch (body.Keep?.Trim().ToLowerInvariant())
                {
                    case null or "": keep = null; break;
                    case "local": keep = KeepSide.Local; break;
                    case "cloud": keep = KeepSide.Cloud; break;
                    default:
                        return TypedResults.BadRequest(new ErrorResponse("keep must be 'local' or 'cloud'."));
                }
                var extraCheck = SavePathGuard.CheckFolder(game, key, body.Path, _config.StateDir);
                if (!extraCheck.Ok)
                    return TypedResults.BadRequest(new ErrorResponse($"Can't use that folder: {extraCheck.Reason}"));
                if (!body.Confirm)
                {
                    var problems = SaveDirSanity.Inspect(extraCheck.Canonical, game.ExcludeGlobs,
                        game.ExtraPaths.First(p => p.Key == key).IncludeGlobs);
                    if (problems.Count > 0)
                        return TypedResults.BadRequest(new ErrorResponse(
                            "That folder looks wrong: " + string.Join(" ", problems) +
                            ErrorResponse.ConfirmHint, NeedsConfirm: true));
                }

                var mapped = await _mapFolder(game, key, extraCheck.Canonical!, keep);
                if (!mapped.Ok)
                    return TypedResults.BadRequest(new ErrorResponse(mapped.Error ?? "Could not map the folder.",
                        NeedsChoice: mapped.NeedsChoice));
                _onGamesChanged?.Invoke();
                if (!string.IsNullOrEmpty(_config.ApiKey))
                {
                    try
                    {
                        await ApiClient.For(_config).SetMachinePathAsync(id, mapped.Directory!, key);
                        game.ExtraPaths.First(p => p.Key == key).PathUnreported = false;
                        _config.SaveGameFolders(game);
                    }
                    catch (Exception ex) { AgentLogger.LogException("AgentApiServer.SetMachinePath", ex); }
                }
                return TypedResults.Ok(new OkResponse());
            }
            if (game is not null && body.Path is not null)
            {
                // Typed paths and picked paths arrive here alike, and neither was validated before.
                // The hard check has no override — nothing makes C:\Users a save folder. WA-02.
                // CheckFolder adds the rules between the game's folders: nothing nested in another.
                var check = SavePathGuard.CheckFolder(game, SaveRoot.PrimaryKey, body.Path, _config.StateDir);
                if (!check.Ok)
                    return TypedResults.BadRequest(new ErrorResponse($"Can't use that folder: {check.Reason}"));

                // The heuristics DO have false positives (a game whose save folder is genuinely
                // large, or genuinely named 'drive_c'), so they are refused once and accepted on a
                // second, explicit confirmation rather than being silently ignored.
                if (!body.Confirm)
                {
                    var problems = SaveDirSanity.Inspect(check.Canonical, game.ExcludeGlobs, game.IncludeGlobs);
                    if (problems.Count > 0)
                        return TypedResults.BadRequest(new ErrorResponse(
                            "That folder looks wrong: " + string.Join(" ", problems) +
                            ErrorResponse.ConfirmHint, NeedsConfirm: true));
                }

                // The canonical form is stored, not the typed text: a relative path or a path with
                // a trailing separator must not reach the server as a different string than the one
                // that was validated.
                // A game set up here by picking its folder, not by a scan: it was added by hand. Moving the
                // folder of one already set up says nothing about how it was found, and stamping it here would
                // keep a scan from ever recording the real answer for a game enrolled before sources were.
                if (!game.IsEnrolledHere) game.Source ??= GameSources.Manual(GameSources.FolderPickedInAgent);
                game.SaveDirectory = check.Canonical!;
                // Save first: watchers must be built from the config that is on disk, never from
                // one a concurrent write is about to supersede.
                try { _config.Save(); }
                catch (AgentStateLockException ex) { return TypedResults.BadRequest(new ErrorResponse(ex.Message)); }
                _onGamesChanged?.Invoke();

                // Tell the server now rather than letting the next poll notice. The server's stored
                // path is the highest authority in reconcile, so until it hears about this one it
                // keeps handing back the old value — and on a machine with no in-process poller
                // (the Deck's `savelocker ui`) it never hears about it at all.
                if (!string.IsNullOrEmpty(_config.ApiKey))
                {
                    try { await ApiClient.For(_config).SetMachinePathAsync(id, check.Canonical!); }
                    catch (Exception ex) { AgentLogger.LogException("AgentApiServer.SetMachinePath", ex); }
                    await GameSources.ReportAsync(ApiClient.For(_config), game);
                }
            }
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>();

        // "Also found" (tasks/multiple-save-paths plan §8): the manifest's other locations of every
        // tracked game that exist here — or of one, for its page. Nothing is added until the user says so.
        app.MapGet("/api/folder-suggestions", async (Guid? gameId, CancellationToken ct) =>
        {
            IReadOnlyList<FolderSuggestion> found = gameId is { } id
                ? _config.Games.FirstOrDefault(g => g.GameId == id) is { } game
                    ? await _suggestions.ForGameAsync(game, ct)
                    : Array.Empty<FolderSuggestion>()
                : await _suggestions.AllAsync(ct);
            return found.Select(s => new FolderSuggestionDto(s.GameId, s.GameName, s.Key, s.Directory, s.Deferred)).ToArray();
        }).Produces<FolderSuggestionDto[]>();

        app.MapPost("/api/folder-suggestions/answer", Results<Ok<OkResponse>, NotFound, BadRequest<ErrorResponse>>
            (FolderSuggestionAnswerRequest body) =>
        {
            if (_config.Games.All(g => g.GameId != body.GameId)) return TypedResults.NotFound();
            static string[] Clean(string[]? dirs) =>
                (dirs ?? Array.Empty<string>()).Select(SavePathGuard.Canonicalize).OfType<string>().ToArray();
            try { _config.SaveGameFolderChoices(body.GameId, Clean(body.Ignore), Clean(body.Defer)); }
            catch (AgentStateLockException ex) { return TypedResults.BadRequest(new ErrorResponse(ex.Message)); }
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>();

        // Another save folder for a tracked game, for every machine — what `add-path` does, except that a
        // key the game already has is refused rather than moved here (mustBeNew): this route adds, and
        // moving a folder is /folder's. A refusal a heuristic made is confirmable (NeedsConfirm); different
        // files on both sides ask for a side (NeedsChoice, the folder's Key with it) — the folder exists for
        // the fleet by then, so the answer goes to /folder with that key.
        app.MapPost("/api/games/{id:guid}/paths",
            async Task<Results<Ok<AddFolderResponse>, NotFound, BadRequest<ErrorResponse>>> (Guid id, AddFolderRequest body) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();
            if (string.IsNullOrWhiteSpace(body.Path))
                return TypedResults.BadRequest(new ErrorResponse("path is required."));
            if (string.IsNullOrEmpty(_config.ApiKey))
                return TypedResults.BadRequest(new ErrorResponse("Register this machine first: a new save folder is defined on the server."));
            if (!game.IsEnrolledHere)
                return TypedResults.BadRequest(new ErrorResponse($"Choose the main save folder of '{game.Name}' first."));
            KeepSide? keep;
            switch (body.Keep?.Trim().ToLowerInvariant())
            {
                case null or "": keep = null; break;
                case "local": keep = KeepSide.Local; break;
                case "cloud": keep = KeepSide.Cloud; break;
                default: return TypedResults.BadRequest(new ErrorResponse("keep must be 'local' or 'cloud'."));
            }
            var named = !string.IsNullOrWhiteSpace(body.Key);
            var taken = new HashSet<string>(game.RemovedPathKeys, StringComparer.Ordinal);
            if (body.FreeKey) taken.UnionWith(game.ExtraPaths.Select(p => p.Key));
            var key = named
                ? body.FreeKey ? FolderSuggestions.KeyFor(body.Key!.Trim(), taken) : body.Key!.Trim()
                : FolderSuggestions.KeyFor(Path.GetFileName(body.Path.TrimEnd('/', '\\')),
                    new HashSet<string>(game.ExtraPaths.Select(p => p.Key).Concat(game.RemovedPathKeys), StringComparer.Ordinal));

            SavePathChange result;
            try
            {
                result = await SavePathEditor.AddAsync(_config, ApiClient.For(_config), _mapFolder, game, key, body.Path,
                    body.IncludeGlobs, body.Label, keep, body.Confirm, pickFreeKey: !named || body.FreeKey, mustBeNew: true);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return TypedResults.BadRequest(new ErrorResponse($"Could not reach the server: {ex.Message}"));
            }
            catch (AgentStateLockException ex) { return TypedResults.BadRequest(new ErrorResponse(ex.Message)); }
            if (result.Added || result.Ok) _onGamesChanged?.Invoke();
            if (!result.Ok)
                return TypedResults.BadRequest(new ErrorResponse(
                    result.NeedsConfirm ? result.Error + ErrorResponse.ConfirmHint : result.Error ?? "Could not add the folder.",
                    NeedsConfirm: result.NeedsConfirm, NeedsChoice: result.NeedsChoice,
                    Key: result.NeedsChoice ? result.Key : null));
            return TypedResults.Ok(new AddFolderResponse(result.Key!, result.Directory!, result.Template, result.Joined));
        }).Produces<AddFolderResponse>();

        // Stop syncing one of a game's extra folders, on every machine — what `remove-path` does. The
        // folder's files stay where they are.
        app.MapDelete("/api/games/{id:guid}/paths/{key}",
            async Task<Results<Ok<OkResponse>, NotFound, BadRequest<ErrorResponse>>> (Guid id, string key) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();
            if (string.IsNullOrEmpty(_config.ApiKey))
                return TypedResults.BadRequest(new ErrorResponse("Register this machine first: save folders are defined on the server."));
            SavePathChange result;
            try { result = await SavePathEditor.RemoveAsync(_config, ApiClient.For(_config), _forgetFolder, game, key); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return TypedResults.BadRequest(new ErrorResponse($"Could not reach the server: {ex.Message}"));
            }
            catch (AgentStateLockException ex) { return TypedResults.BadRequest(new ErrorResponse(ex.Message)); }
            if (!result.Ok) return TypedResults.BadRequest(new ErrorResponse(result.Error ?? "Could not remove the folder."));
            _onGamesChanged?.Invoke();
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>();

        app.MapPost("/api/folder-pick", async () =>
            new FolderResponse(await _pickFolder())).Produces<FolderResponse>();

        app.MapPost("/api/candidates/{id:int}/folder-pick", async Task<Results<Ok<FolderResponse>, BadRequest<ErrorResponse>>>
            (int id) =>
        {
            if (_candidateCache is null || id < 0 || id >= _candidateCache.Count)
                return TypedResults.BadRequest(new ErrorResponse("Invalid candidate id"));

            var path = await _pickFolder();
            if (path is not null)
            {
                var list = _candidateCache.ToList();
                list[id] = list[id] with { SuggestedSaveDir = path };
                _candidateCache = list;
            }
            return TypedResults.Ok(new FolderResponse(path));
        });

        // Sets a browsed path onto a cached candidate. Mirrors POST /api/games/{id}/folder, but the
        // candidate cache — not the tracked-games list — is what Add Games reads before enrollment.
        app.MapPost("/api/candidates/{id:int}/folder", Results<Ok<OkResponse>, BadRequest<ErrorResponse>>
            (int id, FolderRequest body) =>
        {
            if (_candidateCache is null || id < 0 || id >= _candidateCache.Count)
                return TypedResults.BadRequest(new ErrorResponse("Invalid candidate id"));
            if (body.Path is not null)
            {
                // Refused here as well as in Enroller, so the user is told while they are still
                // choosing rather than by a game silently skipping enrollment later. WA-02.
                var check = SavePathGuard.Check(body.Path, _config.StateDir);
                if (!check.Ok)
                    return TypedResults.BadRequest(new ErrorResponse($"Can't use that folder: {check.Reason}"));

                var list = _candidateCache.ToList();
                list[id] = list[id] with { SuggestedSaveDir = check.Canonical };
                _candidateCache = list;
            }
            return TypedResults.Ok(new OkResponse());
        });

        // The Deck's replacement for a folder dialog. Rooted at $HOME + the host's Steam roots;
        // a path outside them is refused rather than described (see PathBrowser).
        app.MapGet("/api/browse", Results<Ok<BrowseListing>, BadRequest<ErrorResponse>>
            (string? path) =>
        {
            var listing = _browser.List(path);
            return listing is null
                ? TypedResults.BadRequest(new ErrorResponse("That folder is not readable, or is outside the browsable roots."))
                : TypedResults.Ok(listing);
        });

        // Where the browser should open for an unmapped game: the scan's guess, if it has one.
        // Scanning is cached, so clicking "Set save path" does not re-walk the disk every time.
        app.MapGet("/api/games/{id:guid}/suggested-path", async (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return new SuggestedPathDto(null);

            var candidates = _candidateCache ?? await RescanAsync();
            var match = candidates.FirstOrDefault(c =>
                string.Equals(c.Name, game.Name, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.SuggestedSaveDir));

            // Only offer a path that is actually there — a stale guess sends the browser nowhere.
            var suggested = match?.SuggestedSaveDir;
            return new SuggestedPathDto(
                suggested is not null && Directory.Exists(suggested) ? suggested : null);
        }).Produces<SuggestedPathDto>();

        // The Steam launch-options command, so a Deck user never has to go back to install.sh's
        // one-time banner to find it. Linux resolves the real installed binary path; Windows returns
        // nulls (the tray sets up sync through the installer) and the UI hides the card.
        app.MapGet("/api/launch-command", () => _launchInfo()).Produces<LaunchCommandDto>();

        // The optional Decky plugin's state on this machine, so the agent UI can say "installed,
        // v0.2.0" instead of showing install instructions to someone who already followed them.
        // Local file reads only — no network, so polling it costs nothing.
        app.MapGet("/api/decky", () => _deckyStatus()).Produces<DeckyStatusDto>();

        // tasks/playnite-plugin/plan.md, Phase 14: lets the plugin itself (a separate process from
        // this one, running inside Playnite) ask whether a newer version of ITSELF is waiting on the
        // server. Always calls PlaynitePlugin.CheckAsync with apply:false — this route only reports,
        // it never writes; the agent's own recurring timer (TrayApp.CheckPlaynitePluginUpdateAsync)
        // owns actually installing one. Called from inside a running Playnite process, so
        // CheckAsync's own IsPlayniteRunning guard always takes its "close Playnite first" branch when
        // a newer version exists — which is exactly the restart notice Phase 14 wants to surface, with
        // no separate branching needed here.
        app.MapGet("/api/playnite-plugin", async () => await _playnitePluginStatus())
            .Produces<PlaynitePluginStatusDto>();

        // tasks/playnite-plugin/plan.md, Phase 19: the agent-ui suggest/install card's own data
        // source — a different consumer from the route above (that one is the plugin process asking
        // about itself; this one is agent-ui polling on behalf of a user who may not have the plugin
        // at all yet), shaped like DeckyStatusDto on purpose. Default answers "not applicable" for the
        // same reason _deckyStatus/_playnitePluginStatus do: Linux cannot run Playnite, so Daemon.cs
        // injects nothing.
        app.MapGet("/api/playnite-plugin/status", async () => await _playnitePluginCardStatus())
            .Produces<PlaynitePluginCardStatusDto>();

        // The install half of Phase 19 — an explicit, user-clicked first install, never triggered by
        // any poll or timer. See PlaynitePlugin.InstallFirstTimeAsync's own remarks for why a first
        // install can safely do what an ordinary background update deliberately never has.
        app.MapPost("/api/playnite-plugin/install", async () => await _playnitePluginInstall())
            .Produces<PlaynitePluginStatusDto>();

        // ---- Launch options (tasks/DeckyPlugin.md) ----
        //
        // The agent cannot write Steam's launch options: Steam holds localconfig.vdf/shortcuts.vdf in
        // memory and rewrites them on exit, so an agent-side edit is discarded. These three routes
        // are how something that CAN write them (a Decky plugin, whose frontend runs inside Steam's
        // own JS context) does it without holding any SaveLocker knowledge of its own — the rule
        // stays here, in LaunchOptions, testable without Steam or hardware.

        // What each tracked game should carry if it carries nothing yet, plus whatever anyone has
        // reported back about it. Empty on Windows: there is no wrapper there, so _launchInfo()
        // returns a null command and there is nothing any caller could usefully do.
        app.MapGet("/api/launch-options", () => LaunchOptionRows()).Produces<LaunchOptionRowDto[]>();

        // The merge. The caller knows each game's CURRENT options (only Steam does) and the agent
        // knows the rule, so neither can do this alone — which is exactly why this is a round trip
        // and not a string the caller assembles. Batched: a plugin sweeps the whole library at once.
        app.MapPost("/api/launch-options/resolve", (ResolveLaunchOptionsRequest body) =>
        {
            var wrapper = WrapperPath();
            if (wrapper is null) return Array.Empty<ResolvedLaunchOptionDto>();

            return (body.Games ?? []).Select(g =>
            {
                var desired = LaunchOptions.Apply(g.Current, wrapper);
                // Compared against the trimmed original: a caller that only ever writes on `changed`
                // must not be told to rewrite a game whose options differ by whitespace alone.
                return new ResolvedLaunchOptionDto(
                    g.SteamAppId, desired, !string.Equals(desired, g.Current?.Trim(), StringComparison.Ordinal));
            }).ToArray();
        }).Produces<ResolvedLaunchOptionDto[]>();

        // Reported back so `doctor` can say "this game's launch options were never set" instead of
        // the user discovering it as a save that silently never synced.
        app.MapPost("/api/launch-options/applied", (LaunchOptionsAppliedRequest body) =>
        {
            var game = _config.Games.FirstOrDefault(
                g => SteamShortcuts.UnsignedAppId(g.ResolveSteamAppId()) == body.SteamAppId);
            if (game is null) return TypedResults.Ok(new OkResponse());

            game.LaunchOptionsAppliedAt = body.Applied ? DateTime.UtcNow : null;
            game.LaunchOptionsError = string.IsNullOrWhiteSpace(body.Error) ? null : body.Error.Trim();
            _config.Save();
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>();

        // What the Overview page's activity card polls: what is syncing right now (with byte
        // progress for a push) and a short rolling history of what just happened. Cheap — an
        // in-memory read, no I/O — so the UI can poll it far more often than the 10 s /api/state tick.
        app.MapGet("/api/activity", () =>
        {
            var current = _activity.Current();
            var recent = _activity.Recent()
                .Select(e => new ActivityLogEntryDto(e.TimestampUtc, e.Message))
                .ToArray();
            var run = _activity.LastRun();
            return new ActivityDto(
                new ActivitySnapshotDto(
                    current.GameName, current.Phase.ToString(), current.BytesDone, current.BytesTotal,
                    current.StartedAtUtc, current.Index, current.Total, current.CancelRequested),
                recent,
                run is null ? null : new SyncRunDto(
                    run.FinishedAtUtc, run.Games, run.Uploaded, run.AlreadyCurrent, run.Conflicts,
                    run.Failed, run.BytesSent, run.Cancelled, run.Pulled, run.Queued, run.Unreachable, run.HeldForDlc));
        }).Produces<ActivityDto>();

        // Stops a running Sync all after the game it is on. Cooperative on purpose — see
        // SyncActivityTracker.RequestCancel: the game in progress always finishes, so a cancel never
        // lands between a restore's "delete files" and "write files". A per-game sync is not a run and
        // has nothing to cancel.
        app.MapPost("/api/sync/cancel", () => new CancelSyncResponse(_activity.RequestCancel()))
            .Produces<CancelSyncResponse>();

        // What is waiting for the server to come back. The list is read fresh from disk every time
        // (the launch wrapper queues from another process) because the Activity page polls it to watch
        // the queue drain. The size beside each entry is a directory walk, so it is NOT re-measured on
        // every poll: once a minute per queued game is plenty for "how big is what is waiting".
        app.MapGet("/api/offline-queue", () =>
        {
            var queued = OfflineQueue.For(_config).GetAll();
            foreach (var stale in _queuedSizes.Keys.Where(id => queued.All(e => e.GameId != id)).ToList())
                _queuedSizes.TryRemove(stale, out _);

            var now = Environment.TickCount64;
            long SizeOf(Guid gameId)
            {
                if (_queuedSizes.TryGetValue(gameId, out var known) && now - known.MeasuredAt < 60_000)
                    return known.Bytes;
                var bytes = FolderSize.Of(_config.Games.FirstOrDefault(g => g.GameId == gameId)?.SaveDirectory);
                _queuedSizes[gameId] = (bytes, now);
                return bytes;
            }

            return queued
                .OrderBy(e => e.QueuedAt)
                .Select(e => new OfflineQueueEntryDto(
                    e.GameId, e.GameName, e.QueuedAt.UtcDateTime, e.RetryCount,
                    e.LastAttemptAt?.UtcDateTime, e.Force, SizeOf(e.GameId)))
                .ToArray();
        }).Produces<OfflineQueueEntryDto[]>();

        // Show agent.log to a person at this machine. Windows selects it in Explorer; Linux xdg-opens
        // it with the borrowed desktop-session environment. With no desktop to show it on (a headless
        // box) it answers 409 CARRYING THE PATH — the page shows it with a Copy button rather than
        // pretending something opened.
        app.MapPost("/api/open-log",
            Results<Ok<OpenLogResponse>, Conflict<OpenLogResponse>> () =>
        {
            var path = AgentLogger.LogPath;
            var opened = false;
            try { opened = File.Exists(path) && _openFile?.Invoke(path) == true; }
            catch (Exception ex) { AgentLogger.LogException("AgentApiServer.open-log", ex); }
            var body = new OpenLogResponse(opened, path);
            return opened ? TypedResults.Ok(body) : TypedResults.Conflict(body);
        }).Produces<OpenLogResponse>().Produces<OpenLogResponse>(StatusCodes.Status409Conflict);

        // The Overview page's "Sync now" button: pull then push every tracked game, same as the tray
        // menu's "Sync All". Fire-and-poll from the UI's side — the response is a summary line, and
        // progress for whichever game is mid-sync shows up on the next /api/activity poll regardless.
        //
        // Single-flight: this route holds its request open for the whole sync, and there are now
        // three buttons wired to it (the browser card, Game Mode, and whatever else asks). Two
        // presses used to start two overlapping SyncAllAsync runs racing each other's leases; the
        // second caller is now told what is already happening instead.
        app.MapPost("/api/sync", async () =>
        {
            if (!await _syncGate.WaitAsync(0))
                return new SyncNowResponse("A sync is already running — watch the activity feed.");
            try { return new SyncNowResponse(await _syncAll()); }
            finally { _syncGate.Release(); }
        }).Produces<SyncNowResponse>();

        // The game page's Sync / Push now / Pull latest. Gated per GAME, not by _syncGate, and that is
        // load-bearing: pre-launch-sync and post-exit-sync answer 409 whenever _syncGate is held, on the
        // premise that "the running sync still converges" — true of Sync all, false of one game's sync.
        // Holding it here dropped an UNRELATED game's exit push, and with it OnGameExitAsync's lease
        // release (the game's lease renewer stops nowhere else, so its lease would be renewed on).
        // The engine already serialises one game's push and pull (per-game lock), so the gate here only
        // tells a second press on the same game what is happening: a 409 rather than /api/sync's 200
        // "watch the activity feed", because the caller named a game and that would read as success.
        // Never forced (see SyncEngine.SyncGameAsync).
        //
        // Not tied to the request's lifetime, like /api/sync: a page that is reloaded or closed must
        // not abort a push halfway through its upload. The result is on /api/activity either way.
        app.MapPost("/api/games/{id:guid}/sync",
            async Task<Results<Ok<SyncNowResponse>, Conflict<ErrorResponse>, BadRequest<ErrorResponse>, NotFound, InternalServerError<ErrorResponse>>>
                (Guid id, GameSyncRequest body) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();
            if (!game.IsEnrolledHere)
                return TypedResults.BadRequest(new ErrorResponse(
                    "This game has no save folder on this machine yet. Choose a folder first."));
            if (!Enum.TryParse<GameSyncMode>(body.Mode, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
                return TypedResults.BadRequest(new ErrorResponse("mode must be sync, push or pull."));

            var gate = _gameSyncGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0))
                return TypedResults.Conflict(new ErrorResponse("A sync of this game is already running. Watch the activity feed."));
            try { return TypedResults.Ok(new SyncNowResponse(await _syncGame(game, mode, CancellationToken.None))); }
            // With no request token, a cancellation is the engine's own (retired because the server
            // connection changed) or an HTTP timeout (a TaskCanceledException). Neither is a bug worth
            // a stack trace, and the message must not claim to know which.
            catch (OperationCanceledException)
            {
                return TypedResults.InternalServerError(new ErrorResponse(
                    "The sync was cancelled: the server connection changed, or the server stopped answering. Try again."));
            }
            catch (Exception ex)
            {
                AgentLogger.LogException("AgentApiServer.game-sync", ex);
                return TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
            finally { gate.Release(); }
        }).Produces<SyncNowResponse>().Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        // The DLC check (tasks/emulator-saves Phase 5c): what the latest save was made with against what is installed
        // here, and whether a pull of it is on hold. One small request; reading the installed packages lists folders
        // and their sizes, never package data.
        app.MapGet("/api/games/{id:guid}/dlc",
            async Task<Results<Ok<DlcCheckDto>, NotFound, InternalServerError<ErrorResponse>>> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();
            try
            {
                var api = ApiClient.For(_config);
                var head = (await api.GetStateAsync(id))?.Head;
                var check = DlcCheck.Describe(head, ContentInventory.For(game), DlcHoldStore.For(_config).For(id) is not null);
                ContentTransferDto[] transfers = [];
                try { transfers = [.. (await api.ListTransfersAsync()).Where(t => t.GameId == id)]; }
                catch (HttpRequestException) { } // an older server without hand-overs
                return TypedResults.Ok(check with
                {
                    HeadMachineId = head?.MachineId,
                    Handover = transfers.LastOrDefault(t => t.ToMachineId == _config.MachineId),
                    Asked = [.. transfers.Where(t => t.FromMachineId == _config.MachineId)],
                    CanHandOver = ContentInventory.CanHandOver(game),
                });
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        }).Produces<DlcCheckDto>();

        // "Pull anyway": the person chose to restore the latest save without the DLC it was made with. Gated with the
        // game page's own sync, so it never races a Pull latest of the same game.
        app.MapPost("/api/games/{id:guid}/dlc/pull-anyway",
            async Task<Results<Ok<SyncNowResponse>, Conflict<ErrorResponse>, NotFound, InternalServerError<ErrorResponse>>> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || !game.IsEnrolledHere) return TypedResults.NotFound();
            var gate = _gameSyncGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0))
                return TypedResults.Conflict(new ErrorResponse("A sync of this game is already running. Watch the activity feed."));
            try { return TypedResults.Ok(new SyncNowResponse(await _pullAnyway(game, CancellationToken.None))); }
            catch (Exception ex)
            {
                AgentLogger.LogException("AgentApiServer.dlc-pull-anyway", ex);
                return TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
            finally { gate.Release(); }
        }).Produces<SyncNowResponse>().Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        // Ask the machine the latest save came from to hand over the packs missing here (tasks/emulator-saves Phase 5c,
        // step 12). The poller fetches and installs them once they are ready, and the held pull then goes ahead.
        app.MapPost("/api/games/{id:guid}/dlc/handover",
            async Task<Results<Ok<ContentTransferDto>, BadRequest<ErrorResponse>, NotFound>> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || !game.IsEnrolledHere) return TypedResults.NotFound();
            if (!ContentInventory.CanHandOver(game))
                return TypedResults.BadRequest(new ErrorResponse("This game's update can't be handed over. Install it in the emulator yourself."));
            try
            {
                var api = ApiClient.For(_config);
                var head = (await api.GetStateAsync(id))?.Head;
                var missing = DlcCheck.Missing(head?.Content, ContentInventory.For(game));
                if (head?.MachineId is not { } from || missing.Count == 0)
                    return TypedResults.BadRequest(new ErrorResponse("Nothing to ask for: this machine has every pack the latest save needs."));
                if (from == _config.MachineId)
                    return TypedResults.BadRequest(new ErrorResponse("The latest save was made on this machine."));
                return TypedResults.Ok(await api.CreateTransferAsync(new CreateTransferRequest(id, from, [.. missing])));
            }
            catch (HttpRequestException ex) { return TypedResults.BadRequest(new ErrorResponse(ServerErrors.Of(ex))); }
        }).Produces<ContentTransferDto>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // This machine's answer to another's request, and cancelling a request either side made.
        app.MapPost("/api/dlc/handovers/{id:guid}/answer",
            async Task<Results<Ok<ContentTransferDto>, BadRequest<ErrorResponse>>> (Guid id, TransferAnswerRequest body) =>
        {
            try { return TypedResults.Ok(await ApiClient.For(_config).AnswerTransferAsync(id, body.Approve)); }
            catch (HttpRequestException ex) { return TypedResults.BadRequest(new ErrorResponse(ServerErrors.Of(ex))); }
        }).Produces<ContentTransferDto>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        app.MapPost("/api/dlc/handovers/{id:guid}/cancel",
            async Task<Results<Ok<SyncNowResponse>, BadRequest<ErrorResponse>>> (Guid id) =>
        {
            try { await ApiClient.For(_config).CancelTransferAsync(id); return TypedResults.Ok(new SyncNowResponse("Hand-over cancelled.")); }
            catch (HttpRequestException ex) { return TypedResults.BadRequest(new ErrorResponse(ServerErrors.Of(ex))); }
        }).Produces<SyncNowResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // Requests from other machines waiting on this one's answer, for the Overview banner.
        app.MapGet("/api/dlc/handovers", async () =>
        {
            try
            {
                return TypedResults.Ok((await ApiClient.For(_config).ListTransfersAsync())
                    .Where(t => t.FromMachineId == _config.MachineId && t.Status == TransferStatus.Requested).ToArray());
            }
            catch (HttpRequestException) { return TypedResults.Ok(Array.Empty<ContentTransferDto>()); }
        }).Produces<ContentTransferDto[]>();

        // Xbox 360 settings of this machine: the profile at launch, and what to do when another machine asks for DLC.
        app.MapGet("/api/settings/xbox360", () => TypedResults.Ok(
            new Xbox360SettingsDto(_config.XeniaProfileAtLaunch ?? ProfileAtLaunch.Ask, _config.DlcSend ?? DlcSendPolicy.Auto)))
            .Produces<Xbox360SettingsDto>();

        // A Xenia game's profile on this machine (tasks/emulator-saves Phase 5c): the profile its saves are under, the one
        // Xenia is set to start as right now (read from its config files), and what the launch check does. Reads two small
        // files; nothing is changed.
        app.MapGet("/api/games/{id:guid}/xenia-profile", Results<Ok<XeniaProfileDto>, NotFound> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || ContentInventory.Home(game) is not { } home) return TypedResults.NotFound();
            if (XeniaLaunchProfile.GameProfile(game) is not { } profile)
                return TypedResults.Ok(new XeniaProfileDto(null, null, null, null, [], XeniaLaunchProfile.PolicyFor(_config, game),
                    game.XeniaProfileAtLaunch, Legacy: true));
            var configs = XeniaConfig.For(profile.Content, game.Name);
            var configured = configs.Select(XeniaConfig.Read).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            return TypedResults.Ok(new XeniaProfileDto(profile.Xuid, XeniaProfile.Gamertag(profile.Content, profile.Xuid),
                configured, configured is null ? null : XeniaProfile.Gamertag(profile.Content, configured), [.. configs],
                XeniaLaunchProfile.PolicyFor(_config, game), game.XeniaProfileAtLaunch, Legacy: false));
        }).Produces<XeniaProfileDto>();

        // This game's own answer to the profile-at-launch setting; null goes back to the machine's.
        app.MapPost("/api/games/{id:guid}/xenia-profile", Results<Ok, NotFound> (Guid id, GameProfileAtLaunchRequest body) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || ContentInventory.Home(game) is null) return TypedResults.NotFound();
            _config.SaveGameProfileAtLaunch(id, body.ProfileAtLaunch);
            game.XeniaProfileAtLaunch = body.ProfileAtLaunch;
            return TypedResults.Ok();
        });

        app.MapPost("/api/settings/xbox360", (Xbox360SettingsDto body) =>
        {
            _config.UpdateSettings(c =>
            {
                c.XeniaProfileAtLaunch = body.ProfileAtLaunch;
                c.DlcSend = body.DlcSend;
            });
            return TypedResults.Ok(new Xbox360SettingsDto(_config.XeniaProfileAtLaunch ?? ProfileAtLaunch.Ask, _config.DlcSend ?? DlcSendPolicy.Auto));
        }).Produces<Xbox360SettingsDto>();

        // One Switch user on every device (SwitchUserSharing): every device's Yuzu-based emulator users, the one chosen, and
        // each install here against it. Reads the emulators' users files and asks the server; nothing is changed.
        app.MapGet("/api/switch/users", async () => TypedResults.Ok(await SwitchUserStateAsync())).Produces<SwitchUserSharingDto>();

        // Choose the user every device uses, and adopt it here at once; the other devices adopt it on their next heartbeat.
        app.MapPost("/api/switch/shared-user",
            async Task<Results<Ok<SwitchUserActionResponse>, BadRequest<ErrorResponse>, Conflict<ErrorResponse>>> (ShareSwitchUserRequest body) =>
        {
            if (!SwitchUserReport.IsUserId(body.Id)) return TypedResults.BadRequest(new ErrorResponse("That isn't a Switch user ID."));
            if (!await _switchUserGate.WaitAsync(0)) return TypedResults.Conflict(new ErrorResponse("The Switch user is already being changed."));
            try
            {
                SwitchUserDto shared;
                try { shared = await ApiClient.For(_config).SetSwitchSharedUserAsync(new SwitchUserDto(body.Id, body.Name)); }
                catch (HttpRequestException ex) { return TypedResults.BadRequest(new ErrorResponse(ServerErrors.Of(ex))); }
                _config.UpdateSettings(c => { c.SwitchSharedUser = shared; c.SwitchSharedUserDeclined = null; });
                var outcome = await ApplySwitchUserHereAsync(shared, null);
                var message = outcome.Refusal is null
                    ? $"Every device uses {shared.Name} now. {(outcome.Done.Count == 0 ? "This one already did." : string.Join(" ", outcome.Done))} The others follow on their next check."
                    : $"Every device is to use {shared.Name}; the others follow on their next check. Here: {outcome.Refusal}";
                return TypedResults.Ok(new SwitchUserActionResponse(await SwitchUserStateAsync(), message, outcome.Refusal is null));
            }
            finally { _switchUserGate.Release(); }
        }).Produces<SwitchUserActionResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // Adopt the chosen user here now, with the answers for titles saved under both users; it also undoes "not on this device".
        app.MapPost("/api/switch/shared-user/apply",
            async Task<Results<Ok<SwitchUserActionResponse>, BadRequest<ErrorResponse>, Conflict<ErrorResponse>>> (ApplySwitchUserRequest body) =>
        {
            if (_config.SwitchSharedUser is not { } shared) return TypedResults.BadRequest(new ErrorResponse("No Switch user is shared yet."));
            if (!await _switchUserGate.WaitAsync(0)) return TypedResults.Conflict(new ErrorResponse("The Switch user is already being changed."));
            try
            {
                _config.UpdateSettings(c => c.SwitchSharedUserDeclined = null);
                var outcome = await ApplySwitchUserHereAsync(shared, body.Keep);
                return TypedResults.Ok(new SwitchUserActionResponse(await SwitchUserStateAsync(),
                    outcome.Refusal ?? (outcome.Done.Count == 0 ? $"This device already uses {shared.Name}." : string.Join(" ", outcome.Done)),
                    outcome.Refusal is null));
            }
            finally { _switchUserGate.Release(); }
        }).Produces<SwitchUserActionResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // "Put back the original user" here: undone from the backup, and the shared user isn't adopted here again.
        app.MapPost("/api/switch/shared-user/undo",
            async Task<Results<Ok<SwitchUserActionResponse>, Conflict<ErrorResponse>>> () =>
        {
            if (!await _switchUserGate.WaitAsync(0)) return TypedResults.Conflict(new ErrorResponse("The Switch user is already being changed."));
            try
            {
                var outcome = await SwitchUserSharing.UndoAsync(_config, SwitchEmulators.Find(), ApiClient.For(_config),
                    (g, w) => _withGameLock(g, w), AgentLogger.Log);
                if (outcome.Refusal is null) _onGamesChanged?.Invoke();
                return TypedResults.Ok(new SwitchUserActionResponse(await SwitchUserStateAsync(),
                    outcome.Refusal ?? string.Join(" ", outcome.Done), outcome.Refusal is null));
            }
            finally { _switchUserGate.Release(); }
        }).Produces<SwitchUserActionResponse>();

        // Stop sharing a user: no device adopts it any more. Nothing is put back; each device keeps the user it has.
        app.MapDelete("/api/switch/shared-user",
            async Task<Results<Ok<SwitchUserActionResponse>, BadRequest<ErrorResponse>>> () =>
        {
            try { await ApiClient.For(_config).ClearSwitchSharedUserAsync(); }
            catch (HttpRequestException ex) { return TypedResults.BadRequest(new ErrorResponse(ServerErrors.Of(ex))); }
            _config.UpdateSettings(c => c.SwitchSharedUser = null);
            return TypedResults.Ok(new SwitchUserActionResponse(await SwitchUserStateAsync(),
                "No user is shared any more. Each device keeps the user it has.", true));
        }).Produces<SwitchUserActionResponse>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // A Switch game's copies on this machine (tasks/emulator-saves Phase 6): every Switch emulator here, the one that syncs
        // first, and what each other one holds next to it. Reads the emulators' folders; nothing is changed.
        app.MapGet("/api/games/{id:guid}/switch-copies", Results<Ok<SwitchCopyDto[]>, NotFound> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || SwitchCopies.TitleId(game) is null) return TypedResults.NotFound();
            return TypedResults.Ok(SwitchCopyRows(game));
        }).Produces<SwitchCopyDto[]>();

        // The save the emulator that syncs holds here next to the cloud's, for the Emulator settings page's status line, and
        // which Switch emulators the game's machines play it in (to show one not installed here). Hashes the save like
        // sync-status; asked for when the page opens and after each action, never polled.
        app.MapGet("/api/games/{id:guid}/vs-cloud",
            async Task<Results<Ok<SaveVsCloudDto>, NotFound>> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();
            GameStateDto? state;
            try { state = await ApiClient.For(_config).GetStateAsync(id); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                return TypedResults.Ok(new SaveVsCloudDto(SaveVsCloud.Unreachable, null, null, []));
            }
            var emulators = (state?.Sources ?? [])
                .Where(s => s.Kind == GameSourceKinds.Emulator && SwitchEmulators.IsSwitch(s.Detail))
                .Select(s => SwitchEmulators.Names.First(n => string.Equals(n, s.Detail, StringComparison.OrdinalIgnoreCase)))
                .Distinct().ToArray();
            if (state?.Head is not { } head) return TypedResults.Ok(new SaveVsCloudDto(SaveVsCloud.NoCloud, null, null, emulators));
            var headVersion = SwitchCopies.TitleId(game) is null ? null : SwitchGameVersion.VersionOf(head.Content);
            _config.RefreshGameSyncState(game);
            var local = game.IsEnrolledHere && Directory.Exists(game.SaveDirectory)
                ? await Task.Run(() => game.LocalHash(_config.StateDir)) : null;
            return TypedResults.Ok(new SaveVsCloudDto(SaveVsCloud.Of(local, head.ContentHash, game.LastSyncedHash),
                head.MachineName, head.CreatedAt, emulators, headVersion));
        }).Produces<SaveVsCloudDto>();

        // One action on one emulator's copy: copy the synced save there, keep it in step (on/off), make it the one that
        // syncs, settle a copy where both sides changed, or be told once it can sync. Under the game's sync lock
        // (_withGameLock), so it never lands in the middle of a push or a pull, whichever process or surface started it.
        // Putting the cloud save in it, or uploading it, is for the copy that syncs only, and refused when the page named
        // another: the page says which emulator the save goes into, and that must be where it goes.
        app.MapPost("/api/games/{id:guid}/switch-copies/{emulator}",
            async Task<Results<Ok<SwitchCopyDto[]>, BadRequest<ErrorResponse>, Conflict<ErrorResponse>, NotFound>> (Guid id, string emulator, SwitchCopyActionRequest body) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || !game.IsEnrolledHere || SwitchCopies.TitleId(game) is null) return TypedResults.NotFound();
            var gate = _gameSyncGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0))
                return TypedResults.Conflict(new ErrorResponse("A sync of this game is running. Try again when it is done."));
            if (body.Action is SwitchCopyActions.TakeCloud or SwitchCopyActions.Upload)
            {
                try
                {
                    var synced = SwitchCopies.SyncedEmulator(game);
                    if (!SwitchCopies.IsSynced(game, emulator, body.Saves))
                        return TypedResults.BadRequest(new ErrorResponse(synced is null
                            ? $"No Switch emulator here holds the folder {game.Name} syncs, so nothing was changed."
                            : $"{game.Name} syncs through {synced} here, not {emulator}, so nothing was changed."));
                    var (ok, message) = await _takeSide(game, body.Action == SwitchCopyActions.TakeCloud, CancellationToken.None);
                    return ok ? TypedResults.Ok(SwitchCopyRows(game)) : TypedResults.BadRequest(new ErrorResponse(message));
                }
                finally { gate.Release(); }
            }
            Func<Task<string?>>? work = body.Action switch
            {
                SwitchCopyActions.CopyHere => () => Task.FromResult(SwitchCopies.CopyHere(_config, game, emulator, AgentLogger.Log, saves: body.Saves)),
                SwitchCopyActions.KeepInSync => () => { SwitchCopies.SetKeepInSync(_config, game, emulator, true, saves: body.Saves); return Task.FromResult<string?>(null); },
                SwitchCopyActions.StopKeeping => () => { SwitchCopies.SetKeepInSync(_config, game, emulator, false, saves: body.Saves); return Task.FromResult<string?>(null); },
                SwitchCopyActions.MakeSynced => () => SwitchCopies.MakeSyncedAsync(_config, game, emulator, ApiClient.For(_config), AgentLogger.Log, saves: body.Saves),
                SwitchCopyActions.KeepCopy => () => Task.FromResult(SwitchCopies.Resolve(_config, game, emulator, keepCopy: true, AgentLogger.Log, saves: body.Saves)),
                SwitchCopyActions.KeepSynced => () => Task.FromResult(SwitchCopies.Resolve(_config, game, emulator, keepCopy: false, AgentLogger.Log, saves: body.Saves)),
                SwitchCopyActions.TellWhenReady => () => Task.FromResult(SwitchCopies.SetTellWhenReady(_config, game, emulator, true)),
                SwitchCopyActions.StopTelling => () => Task.FromResult(SwitchCopies.SetTellWhenReady(_config, game, emulator, false)),
                _ => null,
            };
            if (work is null) return TypedResults.BadRequest(new ErrorResponse($"Unknown action '{body.Action}'."));
            try
            {
                var (ran, refusal) = await _withGameLock(game, work);
                if (!ran) return TypedResults.Conflict(new ErrorResponse("A sync of this game is running. Try again when it is done."));
                return refusal is null ? TypedResults.Ok(SwitchCopyRows(game)) : TypedResults.BadRequest(new ErrorResponse(refusal));
            }
            finally { gate.Release(); }
        }).Produces<SwitchCopyDto[]>().Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // Every pull the DLC check holds on this machine, for the Overview banner.
        app.MapGet("/api/dlc/holds", () => TypedResults.Ok(DlcHoldStore.For(_config).Read()
            .Select(h => new DlcHoldDto(h.GameId, _config.Games.FirstOrDefault(g => g.GameId == h.GameId)?.Name ?? "",
                h.Missing.Count, h.Missing.Sum(p => p.Size), DlcCheck.Summary(h.Missing), h.HeldAt))
            .Where(h => h.GameName.Length > 0).ToArray())).Produces<DlcHoldDto[]>();

        // What the server holds for one game: head version (machine, time, size), open conflict, lease.
        // One small request and no disk work, so the game page can load it on open — unlike
        // sync-status below, which hashes the save folder.
        app.MapGet("/api/games/{id:guid}/state",
            async Task<Results<Ok<GameStateDto>, NotFound, InternalServerError<ErrorResponse>>> (Guid id) =>
        {
            if (_config.Games.All(g => g.GameId != id)) return TypedResults.NotFound();
            try
            {
                var state = await ApiClient.For(_config).GetStateAsync(id);
                return state is null ? TypedResults.NotFound() : TypedResults.Ok(state);
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        }).Produces<GameStateDto>();

        // Cover/icon art, relayed from the server through the on-disk ArtCache, so it is there with the
        // server out of reach and changes when the server's does. The agent UI cannot rely on reaching
        // the server itself (it may be on another network from the browser), and an <img src> could not
        // carry the local token anyway — the UI fetches this with it and shows a blob. `w` is one of the
        // server's thumbnail widths; anything else is dropped there, so the full-size original comes back.
        app.MapGet("/api/games/{id:guid}/art",
            async Task<IResult> (Guid id, string? kind, int? w, CancellationToken ct) =>
        {
            if (_config.Games.All(g => g.GameId != id)) return TypedResults.NotFound();
            if (kind is not ("grid" or "icon")) return TypedResults.BadRequest(new ErrorResponse("kind must be grid or icon."));
            // One budget for the lookup and the fetch, so a server that has gone quiet frees the
            // connection instead of holding it for the HTTP client's own (long) timeout.
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(ArtBudget);
            try
            {
                var api = ApiClient.For(_config);
                var art = await ArtCache.For(_config.StateDir).GetAsync(id, kind, w, api,
                    async c => (await api.GetStateAsync(id, c))?.Game, budget.Token);
                return art is null ? TypedResults.NotFound() : TypedResults.Bytes(art.Bytes, art.ContentType);
            }
            // The page went away (a navigation, an aborted fetch): nobody is left to answer.
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return TypedResults.Empty; }
            catch (OperationCanceledException)
            {
                return TypedResults.InternalServerError(new ErrorResponse("The server did not answer in time."));
            }
            catch (Exception ex)
            {
                return TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
        }).Produces<byte[]>(StatusCodes.Status200OK, "image/png", "image/jpeg", "image/webp", "image/gif");

        app.MapGet("/api/agent-version", () =>
        {
            var latest = _getUpdateResult() is UpdateResult.Available available
                ? available.Version
                : null;
            var staged = _stagedUpdate();
            return new AgentVersionDto(
                UpdateChecker.CurrentVersionText,
                latest,
                latest is not null,
                staged?.Version,
                staged?.BlockedReason);
        }).Produces<AgentVersionDto>();

        // ---- Conflicts (local API) ----
        //
        // Every frontend — the CLI, the Decky plugin, a future Playnite plugin — reaches conflict
        // resolution through this loopback API, never the server directly, so the resolution logic
        // has one home (Agent.Core) and nobody reimplements it. These proxy the machine-key
        // agent-group routes on the server with this machine's own credentials, wrapped in try/catch
        // like /api/config and /api/register: the upstream server can be as unreachable here as on
        // any other route in this file, and that must not surface as a bare, bodiless 500.
        app.MapGet("/api/conflicts",
            async Task<Results<Ok<ConflictDto[]>, InternalServerError<ErrorResponse>>> () =>
        {
            try
            {
                // The server route this proxies is deliberately unscoped (a machine key sees every
                // open conflict; "its own frontend decides what to show" — see Program.cs). This is
                // that decision: only conflicts this machine is actually a party to, the same "no
                // bystander case" filter the CLI's `conflicts` command applies.
                var mine = (await ApiClient.For(_config).GetOpenConflictsAsync())
                    .Where(c => c.MachineId == _config.MachineId)
                    .ToArray();
                return TypedResults.Ok(mine);
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        });

        app.MapGet("/api/conflicts/{id:guid}",
            async Task<Results<Ok<ConflictDto>, NotFound, InternalServerError<ErrorResponse>>> (Guid id) =>
        {
            try
            {
                return await ApiClient.For(_config).GetConflictAsync(id) is { } c
                    ? TypedResults.Ok(c) : TypedResults.NotFound();
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        });

        // The caller names the winning version — it knows which side is its own local save (the
        // conflict's VersionB, this machine's push) and which is the cloud's (VersionA, the head it
        // diverged from). keepBoth also protects the loser as a downloadable backup. The save here
        // follows the choice before this answers (SyncEngine.ResolveOwnConflictAsync), and the answer
        // says what happened to it.
        app.MapPost("/api/conflicts/{id:guid}/resolve",
            async Task<Results<Ok<ResolveConflictResponse>, BadRequest<ErrorResponse>>> (Guid id, LocalResolveRequest body) =>
        {
            try
            {
                var api = ApiClient.For(_config);
                if (_resolveConflict is null)
                {
                    var (done, why) = await api.ResolveConflictAsync(id, body.WinningVersionId, body.KeepBoth);
                    return done
                        ? TypedResults.Ok(new ResolveConflictResponse("Resolved."))
                        : TypedResults.BadRequest(new ErrorResponse(why ?? "Could not resolve the conflict."));
                }
                if (await api.GetConflictAsync(id) is not { } conflict)
                    return TypedResults.BadRequest(new ErrorResponse("The server no longer has this conflict."));
                var (ok, message) = await _resolveConflict(conflict, body.WinningVersionId, body.KeepBoth, CancellationToken.None);
                return ok
                    ? TypedResults.Ok(new ResolveConflictResponse(message))
                    : TypedResults.BadRequest(new ErrorResponse(message));
            }
            catch (Exception ex) { return TypedResults.BadRequest(new ErrorResponse(ex.Message)); }
        });

        app.MapGet("/api/games/{id:guid}/conflict-policy",
            async Task<Results<Ok<ConflictPolicyDto>, NotFound, InternalServerError<ErrorResponse>>> (Guid id) =>
        {
            try
            {
                return await ApiClient.For(_config).GetConflictPolicyAsync(id) is { } p
                    ? TypedResults.Ok(p) : TypedResults.NotFound();
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        });

        app.MapPost("/api/games/{id:guid}/conflict-policy",
            async Task<Results<Ok<OkResponse>, InternalServerError<ErrorResponse>>>
                (Guid id, SetConflictPolicyRequest body) =>
        {
            try
            {
                await ApiClient.For(_config).SetConflictPolicyAsync(id, body.Policy, body.PreferredMachineId);
                return TypedResults.Ok(new OkResponse());
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        });

        // Every version the server keeps of one game, for the game page's "Versions on the server".
        // One small server round trip, no disk work here.
        app.MapGet("/api/games/{id:guid}/versions",
            async Task<Results<Ok<SaveVersionDto[]>, NotFound, InternalServerError<ErrorResponse>>> (Guid id) =>
        {
            if (_config.Games.All(g => g.GameId != id)) return TypedResults.NotFound();
            try { return TypedResults.Ok((await ApiClient.For(_config).GetGameVersionsAsync(id)).ToArray()); }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        });

        // Conflicts this machine was a party to that have since been resolved, newest first — the
        // Conflicts page's "Recently resolved" table. `days` bounds the look-back (default a week).
        app.MapGet("/api/conflicts/resolved",
            async Task<Results<Ok<ConflictDto[]>, InternalServerError<ErrorResponse>>> (int? days) =>
        {
            try
            {
                var since = DateTime.UtcNow.AddDays(-Math.Clamp(days ?? 7, 1, 90));
                // The server filters to this machine before it caps the list; the Where stays for a
                // server too old to know the parameter, which answers with every machine's.
                var mine = (await ApiClient.For(_config).GetResolvedConflictsAsync(since, _config.MachineId))
                    .Where(c => c.MachineId == _config.MachineId)
                    .ToArray();
                return TypedResults.Ok(mine);
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        });

        // A conflict only carries version ids; these fill in machine name/timestamp/size and the
        // file-count/newest-change comparison for each side, the same two calls the dashboard's own
        // conflict card already makes, mirrored here for agent-ui's Conflicts page (Phase 6).
        app.MapGet("/api/versions/{id:guid}",
            async Task<Results<Ok<SaveVersionDto>, NotFound, InternalServerError<ErrorResponse>>> (Guid id) =>
        {
            try
            {
                return await ApiClient.For(_config).GetVersionAsync(id) is { } v
                    ? TypedResults.Ok(v) : TypedResults.NotFound();
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        });

        app.MapGet("/api/versions/{id:guid}/stats",
            async Task<Results<Ok<VersionStatsDto>, NotFound, InternalServerError<ErrorResponse>>> (Guid id) =>
        {
            try
            {
                return await ApiClient.For(_config).GetVersionStatsAsync(id) is { } s
                    ? TypedResults.Ok(s) : TypedResults.NotFound();
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        });

        // "Is my local save the same as the cloud's, without downloading it" — cheap on the network
        // (no bytes cross the wire), NOT cheap on disk: the local hash still walks and reads every
        // file in the save folder, the same cost a push's own hash pays, so it runs off the request
        // thread via Task.Run instead of blocking it. The head hash comes from the state the server
        // already serves; the local hash is computed here, because only the agent can see the save
        // folder. Decision 6 of the conflict-resolution plan.
        app.MapGet("/api/games/{id:guid}/sync-status",
            async Task<Results<Ok<SyncStatusDto>, NotFound, InternalServerError<ErrorResponse>>> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();

            try
            {
                var api = ApiClient.For(_config);
                var state = await api.GetStateAsync(id);
                var headHash = state?.Head?.ContentHash;
                var localHash = !string.IsNullOrWhiteSpace(game.SaveDirectory) && Directory.Exists(game.SaveDirectory)
                    ? await Task.Run(() => game.LocalHash(_config.StateDir))
                    : null;
                var inSync = headHash is not null && localHash is not null &&
                             string.Equals(headHash, localHash, StringComparison.OrdinalIgnoreCase);

                var hasConflict = state?.HasOpenConflict == true;
                Guid? conflictId = hasConflict
                    ? (await api.GetOpenConflictsAsync())
                        .FirstOrDefault(c => c.GameId == id && c.MachineId == _config.MachineId)?.Id
                    : null;
                return TypedResults.Ok(new SyncStatusDto(inSync, hasConflict, conflictId));
            }
            catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
        });

        // The game page's "Save files on this PC": every file of every folder here, each compared with
        // the server's head by SHA-256 (tasks/save-file-trees Phase 3). Hashes the whole save like
        // sync-status, off the request thread; asked for when the page opens or on Refresh, never polled.
        // An unreachable server is an answer (no states), not an error.
        app.MapGet("/api/games/{id:guid}/files",
            async Task<Results<Ok<GameFilesDto>, NotFound, InternalServerError<ErrorResponse>>> (Guid id, CancellationToken ct) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();
            try { return TypedResults.Ok(await SaveFileTree.BuildAsync(_config, game, ApiClient.For(_config), ct)); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                return TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
        }).Produces<GameFilesDto>();

        // The Decky/Playnite launch gate (tasks/conflict-resolution-ui/plan.md, Phase 11). Neither
        // can call SyncEngine.PrepareLaunchAsync in-process the way the Linux wrapper (Phase 4,
        // ProtonRun.cs) does, so this wraps it for a caller that can only reach the agent over HTTP.
        // Performs the pull (or the commit-before-choose push, on a diverged local save) as a side
        // effect exactly like the wrapper's own call — the caller only acts on the returned decision,
        // never syncs anything itself. LaunchGateResult/LaunchDecision are returned verbatim; nothing
        // here needs to reshape them.
        //
        // Decky/Playnite client contract (comment only — no behavior below enforces it): allow a long
        // timeout (the underlying push/pull is bounded by ApiClient's own 10-minute HttpClient
        // timeout, and a big save on a slow link can take minutes) and fail OPEN on anything that is
        // not an explicit Blocked — transport errors, client timeouts, 5xx, and the 409-busy below
        // all mean "launch anyway". Only LaunchDecision.Blocked is fail-closed (cancel the launch,
        // show the resolve UI). A 409 specifically means a /api/sync run already holds the gate; the
        // running sync still converges, so launching without pulling (ProceedSyncPaused) is safe.
        app.MapPost("/api/games/{id:guid}/pre-launch-sync",
            async Task<Results<Ok<LaunchGateResult>, Conflict<LaunchGateResult>, NotFound, InternalServerError<ErrorResponse>>>
                (Guid id, CancellationToken ct) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();

            // Single-flight with /api/sync above: two overlapping SyncAllAsync/PrepareLaunchAsync
            // runs would race each other's leases. The second caller is told what is already
            // happening instead — as a fail-open ProceedSyncPaused, so Decky launches without
            // pulling. /api/sync itself is unchanged (still 200 + SyncNowResponse when busy).
            if (!await _syncGate.WaitAsync(0))
                return TypedResults.Conflict(new LaunchGateResult(
                    LaunchDecision.ProceedSyncPaused,
                    "A sync is already running — launching without pulling."));

            try
            {
                var gate = await _prepareLaunch(game, ct);
                // The game list can change while the gate was in flight (untracked mid-sync); a
                // result for a game that no longer exists must not read as a green light for it.
                if (_config.Games.All(g => g.GameId != id)) return TypedResults.NotFound();
                // Same warning ProtonRun.cs persists after its own PrepareLaunchAsync call — without
                // it a Decky/Playnite launch through this route loses the "another machine has this
                // checked out" banner that a Linux-wrapper launch of the same game would show.
                if (gate.Decision == LaunchDecision.ProceedSyncPaused && gate.HolderMachineName is not null)
                    _leaseWarnings.Add(game.Name, gate.HolderMachineName);
                return TypedResults.Ok(gate);
            }
            // Fail OPEN, per this route's own documented client contract above: an exception here is
            // a transport/transient failure, not a confirmed conflict, so it must read the same as
            // "launch anyway" rather than as a hard error a naive client could block a launch on.
            catch (Exception ex)
            {
                AgentLogger.LogException("AgentApiServer.pre-launch-sync", ex);
                return TypedResults.Ok(new LaunchGateResult(LaunchDecision.Proceed));
            }
            finally { _syncGate.Release(); }
        }).Produces<LaunchGateResult>().Produces<LaunchGateResult>(StatusCodes.Status409Conflict);

        // The other half of a LaunchDecision.Ask (tasks/emulator-saves Phase 5c): the gate already applied the default, so
        // a client posts here only to choose something else, then launches. 404 when the question is unknown, expired,
        // already answered or another game's — the client launches anyway, on the default already in place.
        app.MapPost("/api/games/{id:guid}/launch-answer",
            Results<Ok<LaunchGateResult>, NotFound> (Guid id, LaunchAnswerRequest answer) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || !_answerLaunch(game, answer)) return TypedResults.NotFound();
            return TypedResults.Ok(new LaunchGateResult(LaunchDecision.Proceed));
        }).Produces<LaunchGateResult>();

        // Mirrors pre-launch-sync above almost exactly (tasks/playnite-plugin/plan.md, Phase 5) —
        // same single-flight gate, same "game removed mid-flight" re-check, same fail-open-on-
        // transport-error contract. The one real difference: there is no Blocked decision to fail OUT
        // of here — OnGameExitAsync's own push either succeeds, is skipped by PushAfterExitEnabled, or
        // fails softly and gets logged (SyncEngine already handles all three; nothing here needs to
        // distinguish them for the caller), so this always answers OkResponse rather than a decision
        // the caller must act on.
        app.MapPost("/api/games/{id:guid}/post-exit-sync",
            async Task<Results<Ok<OkResponse>, Conflict<OkResponse>, NotFound>> (Guid id, CancellationToken ct) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();

            if (!await _syncGate.WaitAsync(0))
            {
                // Whatever holds the gate is not guaranteed to push THIS game: a pre-launch or another
                // game's exit holds it too, and a Sync all may already be past it. Releasing the lease
                // without the push would let another machine launch on the older save; keeping it would
                // lock the fleet out of a game nobody is playing. So the whole exit — push, then release
                // — is queued behind the gate. Still 409, so the caller knows it has not run yet.
                QueueDeferredExit(game);
                return TypedResults.Conflict(new OkResponse());
            }

            try
            {
                await _postExitSync(game, ct);
                // Same re-check pre-launch-sync does above: the game list can change while the
                // sync was in flight, and a 200 for a game no longer tracked here is misleading.
                if (_config.Games.All(g => g.GameId != id)) return TypedResults.NotFound();
            }
            catch (Exception ex)
            {
                AgentLogger.LogException("AgentApiServer.post-exit-sync", ex);
            }
            finally { _syncGate.Release(); }
            return TypedResults.Ok(new OkResponse());
        }).Produces<OkResponse>().Produces<OkResponse>(StatusCodes.Status409Conflict);

        // A single, targeted resolve for a game a Playnite plugin already knows about — the per-game
        // resolution ScanInstalledSteamGamesAsync already makes (save-dir, Steam Cloud, canonical
        // manifest name), just reachable for one caller-supplied game instead of a whole sweep.
        // Deliberately not a full /api/candidates/rescan (tasks/playnite-plugin/plan.md, Phase 5):
        // that re-enumerates everything on the machine, which is slow and returns hundreds of
        // unrelated candidates for what should be a single "is this one game known?" lookup. Appends
        // to (or replaces a same-name entry in) the SAME _candidateCache /api/enroll already reads, so
        // the id this returns can be enrolled through that unmodified route.
        app.MapPost("/api/candidates/lookup",
            async Task<Results<Ok<CandidateLookupResponse>, BadRequest<ErrorResponse>>>
                (CandidateLookupRequest body, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return TypedResults.BadRequest(new ErrorResponse("name is required"));

            var name = body.Name.Trim();
            var installDir = string.IsNullOrWhiteSpace(body.InstallDir) ? null : body.InstallDir.Trim();
            var store = ParseStore(body.Store);

            // installDir is <base> only, not <root> (the store LIBRARY root) — a Playnite lookup has
            // no library-root signal to supply, so <root> is left unresolved rather than wrongly
            // pinned to the game's own install folder (PathResolver.Windows's own distinction).
            var dirs = await _detection.ResolveWindowsAsync(name, installDir, ct: ct);
            var suggestedSaveDir = dirs.FirstOrDefault();
            // Same convention ScanInstalledSteamGamesAsync uses: for a Steam-sourced candidate, no
            // manifest data defaults to "assume Steam Cloud" (a wrong "no" would let the pre-launch
            // gate race Steam's own Cloud sync); for anything else, no data defaults to "assume not."
            var hasSteamCloud = store == GameStore.Steam
                ? await _detection.HasSteamCloudAsync(name, ct) ?? true
                : await _detection.HasSteamCloudAsync(name, ct) ?? false;
            var manifestKey = suggestedSaveDir is null ? null : await _detection.CanonicalNameAsync(name, ct) ?? name;

            var candidate = new ScanCandidate(
                name, suggestedSaveDir, ScanSource.Playnite, hasSteamCloud,
                ManifestKey: manifestKey, InstallDir: installDir,
                SteamAppId: string.IsNullOrWhiteSpace(body.SteamAppId) ? null : body.SteamAppId.Trim(),
                Store: store);

            // A bare lookup must never be the first thing to populate the cache — GET /api/candidates
            // and POST /api/enroll both fall back to a real RescanAsync() when it's null, and a
            // one-entry cache from a single Playnite lookup would silently stand in for that scan on
            // every later call. Same fallback those routes already use, so an existing cache is still
            // reused as-is and only a genuinely empty one triggers a scan here.
            var list = (_candidateCache ?? await RescanAsync()).ToList();
            var normalized = ManifestLoader.NormalizeName(name);
            var existing = list.FindIndex(c => ManifestLoader.NormalizeName(c.Name) == normalized);
            int id;
            if (existing >= 0)
            {
                // A real scan can already carry fields a Playnite-only lookup has no way to supply —
                // SuggestedProcessName in particular, which WA-08's whole process lifecycle (lease,
                // exit-push, running-game pull refusal) depends on. Keep them instead of letting this
                // narrower lookup silently downgrade what enrollment will end up using.
                var prior = list[existing];
                candidate = candidate with
                {
                    SuggestedSaveDir = prior.SuggestedSaveDir ?? candidate.SuggestedSaveDir,
                    SuggestedProcessName = prior.SuggestedProcessName ?? candidate.SuggestedProcessName,
                    PrefixPath = prior.PrefixPath ?? candidate.PrefixPath,
                    MoonDeckAppId = prior.MoonDeckAppId ?? candidate.MoonDeckAppId,
                    ManifestKey = prior.ManifestKey ?? candidate.ManifestKey,
                };
                list[existing] = candidate;
                id = existing;
            }
            else { list.Add(candidate); id = list.Count - 1; }
            _candidateCache = list;

            return TypedResults.Ok(new CandidateLookupResponse(
                id, suggestedSaveDir is not null, ToCandidateDtos(list)[id]));
        });

        // In-memory substring search over the manifest's ~53,000 names (Detection.SearchAsync,
        // already existed) — the missing middle tier between automatic matching and a fully manual
        // path: a human recognises "Civilization" instantly where the automatic lookup's exact,
        // normalized-token match cannot bridge "Civ VII" to "Sid Meier's Civilization VII"
        // (tasks/playnite-plugin/plan.md, Phase 5).
        app.MapGet("/api/manifest/search", async (string? q, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(q)
                ? Array.Empty<string>()
                : (await _detection.SearchAsync(q.Trim(), ct: ct)).ToArray()
        ).Produces<string[]>();
    }

    /// <summary>Caller-supplied store name to <see cref="GameStore"/>, defaulting anything unrecognised
    /// (including absent) to <see cref="GameStore.Unknown"/> — a store names how a candidate is
    /// filtered/labeled, never anything that must be trusted, so silently defaulting is fine here
    /// unlike a platform string that names a directory.</summary>
    private static GameStore ParseStore(string? value) =>
        // Enum.TryParse alone accepts any numeric string (e.g. "99") and returns it as-is even when
        // no member has that value — Enum.IsDefined is what actually enforces the "unrecognised ->
        // Unknown" contract the doc comment above promises.
        Enum.TryParse<GameStore>(value, ignoreCase: true, out var store) && Enum.IsDefined(store)
            ? store
            : GameStore.Unknown;

    /// <summary>
    /// The installed wrapper binary's path, recovered from the invocation <see cref="_launchInfo"/>
    /// already builds rather than resolved a second time — two resolvers would eventually disagree,
    /// and the one the user is shown must be the one that gets written. Null where there is no
    /// wrapper at all (Windows).
    /// </summary>
    private string? WrapperPath()
    {
        var command = _launchInfo().Command;
        if (string.IsNullOrEmpty(command)) return null;
        var run = command.IndexOf(" run -- ", StringComparison.Ordinal);
        return run <= 0 ? null : command[..run].Trim('"');
    }

    /// <summary>
    /// One row per tracked game that launches under a Steam AppID. Games without one are skipped
    /// rather than reported: nothing can set launch options for a game Steam does not launch.
    /// </summary>
    private LaunchOptionRowDto[] LaunchOptionRows()
    {
        var wrapper = WrapperPath();
        if (wrapper is null) return [];

        var desired = LaunchOptions.Invocation(wrapper);
        return _config.Games
            .Select(g => (game: g, appId: SteamShortcuts.UnsignedAppId(g.ResolveSteamAppId())))
            .Where(x => x.appId is not null)
            .Select(x => new LaunchOptionRowDto(
                x.appId!.Value, x.game.GameId, x.game.Name, desired,
                x.game.LaunchOptionsAppliedAt, x.game.LaunchOptionsError))
            .ToArray();
    }

    private void MapUi(WebApplication app)
    {
        // Not under /api, so no token: a toast button is a plain link the default browser opens, which
        // cannot send one. Raising the window takes the link key instead (LocalAuth.OpenLinkKey, which
        // only the notification that built the link knows), so a web page — which can navigate or
        // frame this route, and whose framed copy would pass the Origin check on its own POST — can at
        // most show the agent UI's screen in a tab, never pull the window forward over whatever the
        // user is doing. A missing or stale key (a toast from before the token changed) still gets its
        // screen, the way the link worked before the window could be raised. The route is whitelisted
        // before it reaches the window's URL or the page.
        static bool SafeView(string? v) =>
            v is { Length: > 0 and <= 80 } && v.All(c => char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_');

        app.MapGet("/open", (string? view, string? key, HttpContext context) =>
        {
            if (!SafeView(view)) return Results.BadRequest();
            if (_openView is null || !_auth.IsValidOpenLinkKey(key)) return Results.Redirect("/#" + view);
            context.Response.Headers.CacheControl = "no-store";
            // Carries the key in its script, so nothing may frame it or keep it.
            context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            // The page asks for the raise itself, once it has loaded, rather than /open raising the
            // window directly: the browser takes the foreground when it commits this page, so a raise
            // made while answering the request lands first and is immediately covered by the tab.
            return Results.Content(
                "<!doctype html><meta charset=utf-8><title>SaveLocker</title>" +
                "<body style=\"font:16px system-ui;margin:3rem\">Opened in the SaveLocker window. You can close this tab.</body>" +
                $"<script>addEventListener('load',()=>fetch('/open/raise?view={view}&key={_auth.OpenLinkKey}',{{method:'POST'}}).finally(()=>window.close()))</script>",
                "text/html; charset=utf-8");
        }).ExcludeFromDescription();
        app.MapPost("/open/raise", (string? view, string? key) =>
        {
            if (!SafeView(view) || _openView is null) return Results.BadRequest();
            if (!_auth.IsValidOpenLinkKey(key)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            _openView(view!);
            return Results.NoContent();
        }).ExcludeFromDescription();

        if (!Directory.Exists(_uiRoot)) return;

        // No UseDefaultFiles: "/" must go through SendIndexAsync so the token is injected. Serving
        // index.html as a plain static file would hand out a UI that cannot call its own API.
        var provider = new PhysicalFileProvider(_uiRoot);
        app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });

        app.MapGet("/", SendIndexAsync);
        app.MapFallback(async (HttpContext context) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") ||
                context.Request.Path.StartsWithSegments("/openapi"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            await SendIndexAsync(context);
        });
    }

    /// <summary>
    /// Serve the SPA shell with the local token baked in. This is the one place the token crosses
    /// into the browser, and it is safe because the same-origin policy stops any other page from
    /// reading the response — which is exactly why the Guard rejects a non-loopback Host first.
    /// </summary>
    private async Task SendIndexAsync(HttpContext context)
    {
        var index = Path.Combine(_uiRoot, "index.html");
        if (!File.Exists(index))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var html = await File.ReadAllTextAsync(index);
        html = html.Replace(LocalAuth.TokenPlaceholder, _auth.Token);

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync(html);
    }

    private async Task<IReadOnlyList<ScanCandidate>> RescanAsync()
    {
        var result = await _doScan();
        _candidateCache = result;
        try
        {
            var filled = GameSources.Backfill(_config, result);
            if (filled.Count > 0 && !string.IsNullOrEmpty(_config.ApiKey))
                foreach (var game in filled) await GameSources.ReportAsync(ApiClient.For(_config), game);
        }
        catch (AgentStateLockException ex) { AgentLogger.LogException("AgentApiServer.BackfillSources", ex); }
        return result;
    }

    private static AgentAppearanceDto ToAppearanceDto(AgentConfig config)
    {
        var effective = config.EffectiveAppearance;
        return new AgentAppearanceDto(
            config.FollowConsoleAppearance,
            effective,
            config.ConsoleAppearance is { } pushed ? Appearances.Normalize(pushed) : null,
            config.ConsoleAppearanceAt,
            Appearances.Normalize(config.LocalAppearance ?? effective));
    }

    private CandidateDto[] ToCandidateDtos(IReadOnlyList<ScanCandidate> candidates) =>
        candidates.Select((candidate, id) => new CandidateDto(
            id,
            candidate.Name,
            candidate.Source.ToString(),
            candidate.HasSteamCloud,
            candidate.SuggestedSaveDir ?? "",
            SaveLocker.Shared.WinePrefix.BrowseStart(candidate.PrefixPath),
            candidate.SuggestedProcessName,
            candidate.Store.ToString(),
            candidate.ExtraSaveDirs is { Count: > 0 } extras
                ? extras.Select(e => new SaveFolderDto(e.Key, null, e.Dir, true,
                    (e.IncludeGlobs ?? Array.Empty<string>()).ToArray())).ToArray()
                : null,
            candidate.AlternateSaveDirs is { Count: > 0 } also
                ? also.Select(a => new SaveFolderDto(a.Key, null, a.Dir, false, Array.Empty<string>())).ToArray()
                : null,
            Enroller.TrackedFor(_config, candidate) is { IsEnrolledHere: true },
            candidate.EmulatorName,
            candidate.EmulatorSystem,
            candidate.EmulatorRom,
            candidate.UntouchedSeed,
            candidate.NotSyncable,
            GameSources.SystemLabel(candidate.EmulatorSystem),
            candidate.Release?.Region,
            candidate.StatesOnly,
            candidate.NoSaveYet,
            candidate.DuplicateShortcuts is { Count: > 0 } duplicates ? duplicates.ToArray() : null)).ToArray();

    private readonly SemaphoreSlim _switchUserGate = new(1, 1);

    private async Task<SwitchUserSharing.Outcome> ApplySwitchUserHereAsync(SwitchUserDto shared, IReadOnlyDictionary<string, string>? keep)
    {
        var outcome = await SwitchUserSharing.ApplyAsync(_config, shared, SwitchEmulators.Find(), ApiClient.For(_config),
            (g, w) => _withGameLock(g, w), keep, AgentLogger.Log);
        if (outcome.Refusal is null && outcome.Done.Count > 0) _onGamesChanged?.Invoke();
        return outcome;
    }

    /// <summary>The shared Switch user, every device's users (the server's, when it answers), and each install here.</summary>
    private async Task<SwitchUserSharingDto> SwitchUserStateAsync()
    {
        SwitchUsersDto? fleet = null;
        try { fleet = await ApiClient.For(_config).GetSwitchUsersAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) { }
        if (fleet is not null) _config.ApplySwitchSharedUser(fleet.Shared);
        var shared = fleet is null ? _config.SwitchSharedUser : fleet.Shared;
        var here = SwitchUserSharing.States(_config, shared, SwitchEmulators.Find())
            .Select(s => new SwitchInstallUserDto(s.Emulator, s.Install, s.Saves, s.UserId, s.UserName, s.Matches,
                [.. s.Collisions.Select(c => new SwitchCollisionDto(c.TitleId, c.GameName, c.HereWritten, c.SharedWritten))], s.CanUndo, s.Note))
            .ToArray();
        return new SwitchUserSharingDto(shared,
            shared is not null && string.Equals(_config.SwitchSharedUserDeclined, shared.Id, StringComparison.OrdinalIgnoreCase),
            here, fleet?.Machines ?? [], fleet is not null, _config.MachineId);
    }

    private SwitchCopyDto[] SwitchCopyRows(TrackedGame game) =>
        SwitchCopies.List(_config, game).Select(r => new SwitchCopyDto(r.Emulator, r.User, r.Folder,
            r.Synced, r.State, r.WrittenUtc, r.KeepInSync, r.CanCopyHere, r.CanMakeSynced, r.Note, r.TellWhenReady, r.Saves, r.Install,
            r.GameVersion)).ToArray();

    private static string FormatAgo(TimeSpan ago)
    {
        if (ago.TotalSeconds < 60) return "just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }

    /// <summary>
    /// Shut the host down without deadlocking the caller's thread.
    /// <para>
    /// <b>This must never await on the calling thread.</b> The Windows tray disposes this from the
    /// WinForms UI thread (Exit → <c>Application.ThreadContext.DisposeThreadWindows</c>), where a
    /// <c>SynchronizationContext</c> is installed: blocking there with <c>GetAwaiter().GetResult()</c>
    /// froze the whole agent. Kestrel stopped listening, so the port closed and it looked half-dead,
    /// but the process never exited, the tray menu stuck on screen, and only Task Manager could end
    /// it. A captured stack showed the UI thread parked in <c>TaskAwaiter</c> inside this method
    /// while another thread waited on <c>Control.Invoke</c> for that same UI thread.
    /// </para>
    /// <para>
    /// <c>Task.Run</c> moves the continuations onto the thread pool, which has no
    /// <c>SynchronizationContext</c> to post back to, and the bounded wait means a host that refuses
    /// to stop delays exit instead of preventing it — we are tearing down either way.
    /// </para>
    /// </summary>
    /// <summary>
    /// Run a game's exit (<see cref="_postExitSync"/>: push, then release its lease) once
    /// <see cref="_syncGate"/> frees. One queued exit per game: a repeated 409 for the same exit adds
    /// nothing. A game untracked while it waited only has its lease released — there is nothing to push.
    /// </summary>
    private void QueueDeferredExit(TrackedGame game)
    {
        if (!_deferredExits.TryAdd(game.GameId, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _syncGate.WaitAsync();
                try
                {
                    _deferredExits.TryRemove(game.GameId, out _);
                    // CancellationToken.None: the request that queued this is long gone.
                    if (_config.Games.Any(g => g.GameId == game.GameId))
                        await _postExitSync(game, CancellationToken.None);
                    else
                        await _releaseLease(game, CancellationToken.None);
                }
                finally { _syncGate.Release(); }
            }
            catch (Exception ex)
            {
                _deferredExits.TryRemove(game.GameId, out _);
                AgentLogger.LogException("AgentApiServer.post-exit-sync (deferred)", ex);
            }
        });
    }

    public void Dispose()
    {
        var app = Interlocked.Exchange(ref _app, null);
        if (app is null) return;

        try
        {
            Task.Run(async () =>
            {
                try { await app.StopAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                catch { /* stopping is best-effort; we still have to dispose */ }
                await app.DisposeAsync().ConfigureAwait(false);
            }).Wait(TimeSpan.FromSeconds(5));
        }
        catch { /* faulted or timed out — the process is going away regardless */ }
    }
}

public sealed record LeaseWarningDto(string GameName, string HolderMachine);
public sealed record AgentStateDto(
    bool Connected,
    string CurrentVersion,
    /// <summary>Display-only; <see cref="UpdateChecker.BuildLabel"/>. Never compare it.</summary>
    string BuildLabel,
    string MachineName,
    string ServerUrl,
    bool StartWithWindows,
    int GamesTracked,
    int SavesBacked,
    string LastSyncAgo,
    LeaseWarningDto[] LeaseWarnings,
    int SettleQuietSeconds,
    string Platform,
    /// <summary>This device's own machine id, once registered — null before then. Lets a local
    /// frontend (Decky, agent-ui) offer "prefer THIS device" for <see cref="ConflictPolicy.PreferMachine"/>
    /// without needing the fleet-wide machine list only the dashboard's admin API exposes.</summary>
    Guid? MachineId = null,
    /// <summary>Bytes this machine uploaded today (its local day) — the Overview's "Sent today".</summary>
    long SentTodayBytes = 0,
    /// <summary>Pushes waiting for the server to come back; the Activity nav count.</summary>
    int OfflineQueueCount = 0,
    /// <summary><c>pinned</c> (the server's TLS key was recorded on first connect), <c>unpinned</c>
    /// (https, nothing recorded yet) or <c>plain-http</c> (no identity to pin).</summary>
    string ServerTrust = "plain-http",
    /// <summary>Whether this agent may stage a newer version by itself (only the Linux agent stages).</summary>
    bool AutoUpdate = true);
/// <param name="ProcessName">
/// The process discovery is confident means this game is running, or null when it cannot know —
/// which is every source but a non-Steam shortcut. Null tells the UI that enrolling this candidate
/// leaves launch/exit sync unconfigured, so it can say so instead of implying otherwise. WA-08.
/// </param>
/// <param name="Store">
/// The storefront the game came from — <c>Steam</c>, <c>Epic</c>, <c>Gog</c>, <c>Amazon</c>,
/// <c>Sideload</c>, or <c>Unknown</c> when discovery cannot tell (a non-Steam shortcut). Orthogonal
/// to <paramref name="Source"/>: one Heroic source covers four storefronts, and a large install
/// base is only navigable if the UI can narrow to one of them.
/// </param>
public sealed record CandidateDto(
    int Id, string Name, string Source, bool HasSteamCloud, string Path, string? PrefixPath,
    string? ProcessName, string Store,
    /// <summary>The other save folders discovery found this game keeps (an emulator's save states),
    /// adopted with it on enrollment. Null when there are none.</summary>
    SaveFolderDto[]? ExtraFolders = null,
    /// <summary>The manifest's other locations for this game that exist here ("Also found"): added only
    /// when the enroll request names them in <see cref="EnrollRequest.AlsoSync"/>. Null when there are none.</summary>
    SaveFolderDto[]? AlsoFound = null,
    /// <summary>Already tracked here with a folder, so enrolling it again is skipped (<see cref="Enroller"/>).
    /// A game adopted from the server with no folder on this machine is not enrolled: adding it maps it.</summary>
    bool Enrolled = false,
    // Which emulator and console, for an emulator save (null otherwise) — the Add games filter's keys.
    string? EmulatorName = null, string? EmulatorSystem = null,
    // The save file's own name ("Chrono Trigger (Japan)"): two ROMs can share a title.
    string? EmulatorRom = null,
    /// <summary>EmuDeck's preinstalled save, never played here (<see cref="ScanCandidate.UntouchedSeed"/>): list it
    /// only when <c>GET /api/candidates/links</c> says it joins a server game.</summary>
    bool UntouchedSeed = false,
    /// <summary>Why this row can never be added and how to fix that (<see cref="ScanCandidate.NotSyncable"/>): a memory
    /// card every game of a console shares. Shown greyed out with this text; null for every addable row.</summary>
    string? NotSyncable = null,
    /// <summary>The console as people say it ("PS2", "GameCube"), from <see cref="EmulatorSystem"/> by
    /// <see cref="GameSources.SystemLabel"/> — the Add games Console row's chip label. Null when there is no system.</summary>
    string? EmulatorConsole = null,
    /// <summary>Which region's release a console game's save is ("USA", "Europe, Australia"), kept out of
    /// <see cref="Name"/>; null when it is no disc console's save or Redump does not know its game ID.</summary>
    string? Region = null,
    /// <summary>Found by its save states alone, before the game's first in-game save here
    /// (<see cref="ScanCandidate.StatesOnly"/>).</summary>
    bool StatesOnly = false,
    /// <summary>A Switch ROM whose title has no save on this machine yet (<see cref="ScanCandidate.NoSaveYet"/>): adding it
    /// puts the fleet's save where its emulator will look for it.</summary>
    bool NoSaveYet = false,
    /// <summary>Same-named Steam shortcuts under different AppIDs (<see cref="ScanCandidate.DuplicateShortcuts"/>):
    /// one entry per AppID, <see cref="DuplicateShortcut.Tracked"/> on the one the scan uses. Null when not the shape.</summary>
    DuplicateShortcut[]? DuplicateShortcuts = null);
/// <param name="ProcessNames">
/// Process names (no extension) that mean this game is running. <b>Empty means the Windows agent
/// cannot detect it</b> — no lease, no exit push, and no refusal to pull under a live game — so the
/// UI must say so rather than imply automatic sync is working. WA-08.
/// </param>
/// <param name="Alias">
/// The manual name-match override, or null when none is set — see <see cref="TrackedGame.Alias"/>.
/// </param>
/// <param name="SteamAppId">
/// This game's resolved compatdata-prefix AppID, or null when it has none — the same resolution
/// <see cref="TrackedGame.ResolveSteamAppId"/> and the launch-options rows use. <b>Not</b> a "is this
/// a Steam Store game" signal: a non-Steam shortcut run under Proton gets its own compatdata prefix
/// too, so it resolves a non-null value here despite never having Steam Cloud. Use
/// <paramref name="HasSteamCloud"/> for that question instead.
/// </param>
/// <param name="PullBeforeLaunchEnabled">
/// The Gaming Mode pull-before-launch override, or null when none is set — see
/// <see cref="TrackedGame.PullBeforeLaunchEnabled"/>.
/// </param>
/// <param name="HasSteamCloud">
/// Whether this game was enrolled as a genuinely Steam-sourced install, or <b>null when nothing has
/// established that</b> — see <see cref="TrackedGame.HasSteamCloud"/>, decided once at enrollment,
/// not a live lookup. A Decky plugin needs this, not <paramref name="SteamAppId"/>, to decide
/// whether its own pre-launch pull would race Steam's own Cloud sync — an AppID alone can't tell a
/// real Steam install apart from a non-Steam shortcut run under Proton, which gets a compatdata
/// prefix too. Null means "unknown, use your own heuristic", not "no Steam Cloud".
/// </param>
/// <param name="PushAfterExitEnabled">
/// Whether the post-exit push should run, or null when none is set — see
/// <see cref="TrackedGame.PushAfterExitEnabled"/>. Added in tasks/playnite-plugin/plan.md Phase 4,
/// after every field below <see cref="HasSteamCloud"/> — additive, same reasoning as the remark below.
/// </param>
/// <remarks>
/// <see cref="Id"/> and <see cref="Path"/> keep those names, rather than the GameId/SaveDirectory
/// the config-side properties use, because this record IS the <c>/api/games</c> wire contract and
/// the Decky plugin reading it ships from its own repo on its own update channel. Renaming them
/// would break every already-installed plugin the moment an agent updated ahead of it, for nothing
/// — the four fields after them are additive, so an older reader keeps working untouched.
/// </remarks>
public sealed record TrackedGameDto(
    Guid Id, string Name, string Path, string[] ProcessNames, string? Alias,
    uint? SteamAppId, bool? PullBeforeLaunchEnabled, bool? HasSteamCloud,
    bool? PushAfterExitEnabled = null,
    /// <summary>
    /// Mirrors <see cref="AgentConfig.TrackedGame.InstallDir"/> — the second tier of the
    /// Playnite plugin's automatic matching chain (tasks/playnite-plugin/plan.md, "Automatic game
    /// matching"), for anything Steam AppID doesn't resolve (Epic, GOG, a manual install). Null for
    /// a game discovery never recorded one for (a save-root match, most non-Steam shortcuts before
    /// this field existed).
    /// </summary>
    string? InstallDir = null,
    /// <summary>What this machine's last accepted push actually put on the wire (a delta is far
    /// smaller than the save); null until it has pushed since the field existed.</summary>
    long? LastPushBytes = null,
    DateTime? LastPushAt = null,
    /// <summary>Every save folder of the game, primary (<c>main</c>) first (tasks/multiple-save-paths).
    /// <see cref="Path"/> stays the primary folder, for readers that predate this.</summary>
    SaveFolderDto[]? Paths = null,
    /// <summary>How this machine found the game; null when it was enrolled before sources were recorded,
    /// or is not set up here.</summary>
    GameSourceDto? Source = null,
    /// <summary>Which release of a console game it is (region, game IDs); null for any other game.</summary>
    GameReleaseDto? Release = null,
    /// <summary>The first of the primary folder's include patterns — the save's own files, in the details the
    /// release chip opens; null for a game that keeps its whole folder.</summary>
    string? SaveFiles = null);

/// <summary>One save folder of a tracked game on this machine.</summary>
/// <param name="Path">This machine's folder, or <c>""</c> when it is not mapped here — an unmapped
/// extra folder keeps syncing through a shadow copy in the agent's own state.</param>
public sealed record SaveFolderDto(string Key, string? Label, string Path, bool Mapped, string[] IncludeGlobs)
{
    public static SaveFolderDto[] Of(TrackedGame g) =>
        new[] { new SaveFolderDto(SaveRoot.PrimaryKey, null, g.SaveDirectory, g.IsEnrolledHere, g.IncludeGlobs.ToArray()) }
            .Concat(g.ExtraPaths.Select(p =>
                new SaveFolderDto(p.Key, p.Label, p.Directory ?? "", p.IsMapped, p.IncludeGlobs.ToArray())))
            .ToArray();
}

/// <param name="Mode"><c>sync</c> (pull then push), <c>push</c> or <c>pull</c>; case-insensitive.</param>
public sealed record GameSyncRequest(string? Mode);

public sealed record ProcessNamesRequest(string[]? ProcessNames);
public sealed record AliasRequest(string? Alias);
public sealed record PullBeforeLaunchRequest(bool? Enabled);
public sealed record PushAfterExitRequest(bool? Enabled);

/// <summary>
/// Request for <c>POST /api/candidates/lookup</c> (tasks/playnite-plugin/plan.md, Phase 5) — a
/// single-candidate resolve for a game a Playnite plugin already knows about (name, install dir, and
/// whichever Steam/store hints it can supply), deliberately not a full rescan. <paramref name="Store"/>
/// is the caller's own string for <see cref="GameStore"/> (case-insensitive; unrecognised or absent
/// means <see cref="GameStore.Unknown"/>).
/// </summary>
public sealed record CandidateLookupRequest(
    string Name, string? InstallDir = null, string? SteamAppId = null, string? Store = null);

/// <param name="Resolved">Whether a save directory was found — the same signal a caller would read off
/// <see cref="CandidateDto.Path"/> being non-empty, surfaced explicitly so it need not parse that.</param>
public sealed record CandidateLookupResponse(int Id, bool Resolved, CandidateDto Candidate);
public sealed record AgentConfigDto(
    string ServerUrl,
    string MachineName,
    bool StartWithWindows,
    int SettleQuietSeconds);
/// <param name="Follow">This machine takes the console's look. Off is the per-machine override.</param>
/// <param name="Effective">What to draw right now — the one field a page needs to style itself. Always
/// a known-good value, so a page never has to defend against an unknown id.</param>
/// <param name="Console">The look the console last pushed, or null if none ever arrived (the console
/// is not sharing, or is an older build).</param>
/// <param name="ConsoleAppliedAt">When <paramref name="Console"/> last changed, UTC.</param>
/// <param name="Local">This machine's own look — its previous choice, or the current one if it never
/// made one — offered as the starting point when "Follow the console" is turned off.</param>
public sealed record AgentAppearanceDto(
    bool Follow,
    AppearanceDto Effective,
    AppearanceDto? Console,
    DateTime? ConsoleAppliedAt,
    AppearanceDto Local);
/// <param name="Look">Required to change this machine's own look; omitted to only flip
/// <paramref name="Follow"/>.</param>
public sealed record AgentAppearanceRequest(bool Follow, AppearanceDto? Look = null);
/// <param name="UpdateAvailable">
/// The server is offering something newer. That is all it means: nothing is downloaded, and acting
/// on it needs network, a download, a digest check and a smoke test — any of which can fail, and all
/// of which take a while.
/// </param>
/// <param name="StagedVersion">
/// A version already downloaded, verified against the published SHA-256, unpacked and smoke-tested,
/// waiting for the next start. Applying it is a file copy and a restart: it works offline and cannot
/// fail for any of the reasons a download can, which is why this — and never
/// <paramref name="UpdateAvailable"/> — is what an "install now" control may offer.
/// Null when nothing is staged, and always null on Windows, which stages nothing (its updater runs
/// the installer).
/// </param>
/// <param name="StagedBlockedReason">
/// Why restarting right now would install nothing, as a sentence to show verbatim, or null when it
/// would work. Today the only reason is a game running under the launch wrapper: the apply is
/// deferred while one is alive, so a restart in that state is <b>safe but does nothing visible</b> —
/// the worst possible outcome for a button. Phrased here rather than by each caller so the three
/// surfaces that can say "update" cannot word it differently.
/// </param>
public sealed record AgentVersionDto(
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string? StagedVersion,
    string? StagedBlockedReason);

/// <summary>
/// What the host knows about an update it has already staged. Supplied by the Linux daemon, which
/// owns <c>Updater</c>; the Windows tray injects nothing, because it stages nothing.
/// </summary>
/// <param name="BlockedReason"><inheritdoc cref="AgentVersionDto" path="/param[@name='StagedBlockedReason']"/></param>
public sealed record StagedUpdateInfo(string Version, string? BlockedReason);
public sealed record LaunchCommandDto(string? Command, string? Note);

/// <summary>
/// What this machine knows about the optional Decky plugin, from <b>local files only</b> — no
/// network call, so the agent UI can poll it as freely as anything else.
/// </summary>
/// <param name="Applicable">
/// False wherever Decky cannot exist (Windows). The UI hides the whole card on false rather than
/// rendering "not installed", which would be advice nobody on that platform can act on.
/// </param>
/// <param name="DeckyPresent">Is Decky Loader itself installed? Judged by its plugins directory.</param>
/// <param name="PluginVersion">
/// The installed plugin's version, read from the same <c>package.json</c> Decky reports in its own
/// UI, so the two cannot disagree. Null when the plugin is not installed — or when that file cannot
/// be read, which is itself the signal to reinstall.
/// </param>
/// <param name="InstallUrl">
/// Served rather than hard-coded in the UI so the one-paste URL has a single source. Empty when not
/// <paramref name="Applicable"/>.
/// </param>
public sealed record DeckyStatusDto(
    bool Applicable,
    bool DeckyPresent,
    bool PluginInstalled,
    string? PluginVersion,
    string InstallUrl);

/// <summary>
/// GET /api/playnite-plugin (tasks/playnite-plugin/plan.md, Phase 14) — a Windows-only mirror of
/// <see cref="DeckyStatusDto"/> for the Playnite plugin's own self-update notice. <paramref
/// name="State"/> is <c>Agent.PlaynitePluginState</c>'s name as a plain string (<c>NoPlaynite</c>,
/// <c>NotInstalled</c>, <c>UpToDate</c>, <c>Available</c>, <c>Failed</c>, or <c>NotApplicable</c> on a
/// host — Linux — that never wires this up at all) rather than the enum type itself, so Agent.Core
/// never needs to reference the Windows-only Agent project that actually defines it.
/// </summary>
public sealed record PlaynitePluginStatusDto(
    string State, string Message, string? InstalledVersion, string? LatestVersion);

/// <summary>
/// GET /api/playnite-plugin/status (tasks/playnite-plugin/plan.md, Phase 19) — the agent-ui
/// suggest/install card's data source, deliberately shaped like <see cref="DeckyStatusDto"/>: the
/// same three-state card pattern (not applicable, applicable but not installed, installed), one field
/// narrower since there is no need for a separate "is the launcher present" flag here — this card
/// renders nothing at all when <paramref name="Applicable"/> is false, so the two states Decky's own
/// <c>DeckyPresent</c> distinguishes (present-without-plugin vs. absent) never need to render
/// differently for Playnite. <paramref name="LatestVersion"/> has no Decky equivalent: a card that
/// offers to install the plugin in the first place needs to know what it would install, which
/// <see cref="PlaynitePluginStatusDto"/> above never has to answer before something is installed.
/// </summary>
public sealed record PlaynitePluginCardStatusDto(
    bool Applicable, bool PluginInstalled, string? PluginVersion, string? LatestVersion, string InstallUrl);

/// <param name="SteamAppId">
/// The <b>unsigned</b> 32-bit AppID, normalised here so no caller has to know the trap: Steam stores
/// a non-Steam shortcut's id signed but exposes it unsigned, and comparing the two representations
/// silently matches nothing — which would be every game this feature exists for.
/// </param>
/// <param name="Desired">What this game should carry if it carries nothing yet. Identical for every
/// game on a device; a game that already has options needs the resolve route instead.</param>
/// <param name="AppliedAt">Null with a null <paramref name="Error"/> means <b>unknown</b>, not
/// broken — most machines have nothing that could report.</param>
public sealed record LaunchOptionRowDto(
    uint SteamAppId, Guid GameId, string Name, string Desired, DateTime? AppliedAt, string? Error);

public sealed record ResolveLaunchOptionsRequest(LaunchOptionCurrentDto[]? Games);
/// <param name="Current">The game's launch options as Steam holds them right now, empty or null if unset.</param>
public sealed record LaunchOptionCurrentDto(uint SteamAppId, string? Current);
/// <param name="Changed">False when the game already carries exactly this — the caller should not write.</param>
public sealed record ResolvedLaunchOptionDto(uint SteamAppId, string Desired, bool Changed);

public sealed record LaunchOptionsAppliedRequest(uint SteamAppId, bool Applied, string? Error);

/// <param name="AlsoSync">Which "Also found" folders (<see cref="CandidateDto.AlsoFound"/>) to add with each
/// candidate. A candidate missing here adds none.</param>
/// <param name="Links">Emulator saves the user linked by hand (Add games' Change). Any other is linked automatically.</param>
public sealed record EnrollRequest(int[]? Ids, EnrollFolderChoice[]? AlsoSync = null, EnrollLinkChoice[]? Links = null);
/// <param name="Choice"><c>auto</c>, <c>separate</c> (keep it as its own game) or <c>game</c> (join <paramref name="GameId"/>).</param>
public sealed record EnrollLinkChoice(int Id, string Choice, Guid? GameId = null);
/// <param name="Reachable">False when the server could not be asked: the page says the link is decided when the game is added.</param>
public sealed record CandidateLinksResponse(bool Reachable, CandidateLinksDto[] Links);
/// <param name="Options">The automatic choice first, then the others (<see cref="EnrollLinks.For"/>).</param>
public sealed record CandidateLinksDto(int Id, LinkOption[] Options);
public sealed record EnrollFolderChoice(int Id, string[]? Paths);

/// <summary>A folder a tracked game could also sync ("Also found"), from the manifest.</summary>
/// <param name="Deferred">"Skip for now" was chosen: listed on the game's page, not asked about at start-up.</param>
public sealed record FolderSuggestionDto(Guid GameId, string GameName, string Key, string Path, bool Deferred);
/// <param name="Ignore">Folders this game does not sync: never suggested again.</param>
/// <param name="Defer">Folders put off ("Skip for now"): no longer asked about at start-up.</param>
public sealed record FolderSuggestionAnswerRequest(Guid GameId, string[]? Ignore = null, string[]? Defer = null);
/// <param name="Key">The folder's key. Null: one is made from the folder's name.</param>
/// <param name="Keep"><c>local</c> or <c>cloud</c>, when both this folder and the fleet's copy hold files.</param>
/// <param name="FreeKey"><paramref name="Key"/> is only a preference (a suggestion's): if the game has or
/// had it, take the next free one instead of refusing.</param>
public sealed record AddFolderRequest(string? Path, string? Key = null, string? Label = null,
    string[]? IncludeGlobs = null, string? Keep = null, bool Confirm = false, bool FreeKey = false);
/// <param name="Joined">The fleet already had this folder (another device added it first, as
/// <paramref name="Key"/>): this device joined it rather than adding it twice.</param>
public sealed record AddFolderResponse(string Key, string Path, string? Template, bool Joined = false);
/// <param name="IdentityCleared">
/// True when the server URL moved to a different origin, so this machine's key, id and TLS pin were
/// dropped and it must register or enroll again. See <see cref="ServerOrigin"/>.
/// </param>
public sealed record ConfigChangeResponse(bool IdentityCleared, bool StartWithWindows);

public sealed record ConfigRequest(
    string? ServerUrl,
    string? MachineName,
    bool? StartWithWindows,
    int? SettleQuietSeconds,
    bool? AutoUpdate = null);
public sealed record RegisterRequest(string? AdminPassword = null);
/// <param name="Confirm">
/// Accept a path the sanity heuristics flagged. It never overrides <see cref="SavePathGuard"/> —
/// those refusals are absolute.
/// </param>
/// <param name="Key">Which of the game's save folders: absent or <c>main</c> for the primary one.</param>
/// <param name="Keep"><c>local</c> or <c>cloud</c>, when a previous attempt answered NeedsChoice.</param>
public sealed record FolderRequest(string? Path, bool Confirm = false, string? Key = null, string? Keep = null);
public sealed record DismissWarningRequest(string? GameName);
/// <param name="WinningVersionId">
/// The version to keep — the conflict's VersionB to keep this machine's local save, VersionA to keep
/// the cloud's. The frontend reads both ids off the conflict.
/// </param>
/// <param name="KeepBoth">Also protect the losing version as a downloadable backup.</param>
public sealed record LocalResolveRequest(Guid WinningVersionId, bool KeepBoth = false);
public sealed record OkResponse(bool Ok = true);
/// <param name="Message">What became of the save on this machine, as one sentence to show.</param>
public sealed record ResolveConflictResponse(string Message);
/// <param name="NeedsConfirm">The request was refused by a heuristic that has false positives, and
/// re-sending it with <c>confirm</c> will be accepted. A client decides "may this be clicked past?" from
/// this field alone, never from the wording of <paramref name="Error"/> — a hard refusal never sets it.</param>
/// <param name="NeedsChoice">Mapping a save folder met different files on both sides; re-send with
/// <c>keep</c> (<c>local</c> or <c>cloud</c>) to say which wins. Nothing changed.</param>
/// <param name="Key">With <paramref name="NeedsChoice"/> from adding a folder: the key the folder has now, which
/// the answer (keep local or cloud) is sent for.</param>
public sealed record ErrorResponse(string Error, bool NeedsConfirm = false, bool NeedsChoice = false, string? Key = null)
{
    /// <summary>The sentence a confirmable refusal ends with. Kept in the message for clients that
    /// predate <see cref="NeedsConfirm"/>; newer ones drop it and ask the question in their own words.</summary>
    public const string ConfirmHint = " Re-send with confirm to use it anyway.";
}
internal static class ServerErrors
{
    /// <summary>The server's own sentence from an <see cref="ApiClient"/> error (“409: "The request is declined."”).</summary>
    public static string Of(HttpRequestException ex)
    {
        var m = System.Text.RegularExpressions.Regex.Match(ex.Message, @"^\d{3}: (.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim().Trim('"') : ex.Message;
    }
}

/// <summary>A Xenia game's profile here. <see cref="Legacy"/>: the layout from before profiles, where no profile is involved.</summary>
public sealed record XeniaProfileDto(
    string? GameXuid, string? GameTag, string? ConfiguredXuid, string? ConfiguredTag, string[] Configs,
    ProfileAtLaunch Policy, ProfileAtLaunch? GamePolicy, bool Legacy);

public sealed record GameProfileAtLaunchRequest(ProfileAtLaunch? ProfileAtLaunch);

public sealed record Xbox360SettingsDto(ProfileAtLaunch ProfileAtLaunch, DlcSendPolicy DlcSend);

/// <summary>A pull the DLC check holds (tasks/emulator-saves Phase 5c).</summary>
public sealed record DlcHoldDto(Guid GameId, string GameName, int MissingCount, long MissingSize, string Summary, DateTimeOffset HeldAt);

/// <param name="Notes">Why each refused game was not added ("Chrono Trigger: the server's …"), one line each;
/// null when every skip was a game already set up here.</param>
/// <param name="Info">What was added differently from how it was found ("Forza Horizon: added under …"), one line each.</param>
public sealed record EnrollResponse(int Enrolled, int Skipped, string[]? Notes = null, string[]? Info = null);
public sealed record EnrollProgressDto(bool Active, int Index, int Total, string? Game, string Step, int Enrolled, int Skipped);
public sealed record RegisterResponse(string MachineName);
public sealed record FolderResponse(string? Path);
public sealed record SuggestedPathDto(string? Path);

/// <param name="GameName">Null when nothing is syncing right now.</param>
/// <param name="Phase">One of "Idle", "Pulling", "Settling", "Pushing" — a plain string rather than
/// a typed enum so the local API's slim JSON pipeline needs no enum-converter configuration for it,
/// matching every other DTO in this file.</param>
/// <param name="BytesDone">Meaningful only during "Pushing" — see the chunk loop in
/// <see cref="ApiClient.UploadAsync"/>, the only place that knows progress mid-transfer. Zero for
/// every other phase.</param>
/// <param name="Index">During a Sync all: which game (1-based) of <paramref name="Total"/> is running;
/// both 0 outside one, so a single-game sync never claims to be "1 of 1".</param>
/// <param name="CancelRequested">A cancel was asked for and the run will stop after the current game.</param>
public sealed record ActivitySnapshotDto(
    string? GameName, string Phase, long BytesDone, long BytesTotal, DateTime? StartedAtUtc,
    int Index = 0, int Total = 0, bool CancelRequested = false);
public sealed record ActivityLogEntryDto(DateTime TimestampUtc, string Message);
/// <param name="LastRun">What the most recent Sync all did, until the next one begins; null before any.</param>
public sealed record ActivityDto(
    ActivitySnapshotDto Current, ActivityLogEntryDto[] Recent, SyncRunDto? LastRun = null);
/// <param name="Failed">Pushes that were refused or could not run. A save waiting in the offline queue
/// is <paramref name="Queued"/>, and a game the server could not be asked about is <paramref name="Unreachable"/>.</param>
/// <param name="HeldForDlc">Games whose newer save was not pulled because it needs DLC this machine lacks.</param>
public sealed record SyncRunDto(
    DateTime FinishedAtUtc, int Games, int Uploaded, int AlreadyCurrent, int Conflicts, int Failed,
    long BytesSent, bool Cancelled, int Pulled = 0, int Queued = 0, int Unreachable = 0, int HeldForDlc = 0);
public sealed record SyncNowResponse(string Message);
/// <param name="Requested">False when no Sync all was running, so there was nothing to cancel.</param>
public sealed record CancelSyncResponse(bool Requested);
/// <param name="Size">Bytes in the save folder now (a plain directory walk, no hashing); 0 if it is gone.</param>
public sealed record OfflineQueueEntryDto(
    Guid GameId, string GameName, DateTime QueuedAt, int Attempts, DateTime? LastAttemptAt, bool Force, long Size);
/// <param name="Opened">The desktop was handed the file. False on a headless box, where the caller shows <paramref name="Path"/>.</param>
public sealed record OpenLogResponse(bool Opened, string Path);
/// <param name="Suggested">Candidates the last scan proposed (those Steam Cloud does not already back up); null before a scan.</param>
public sealed record CandidateCountDto(int? Suggested);
public sealed record LocalSizeDto(long Bytes);
public sealed record TestConnectionDto(bool Ok, int? Status, long LatencyMs, string? Error);

/// <summary>One Switch emulator's copy of a game on this machine (<see cref="SwitchCopies.Row"/>). <see cref="State"/> is one of
/// <see cref="SwitchCopies.States"/>.</summary>
public sealed record SwitchCopyDto(
    string Emulator, string? User, string? Folder, bool Synced, string State, DateTime? WrittenUtc,
    bool KeepInSync, bool CanCopyHere, bool CanMakeSynced, string? Note, bool TellWhenReady, string? Saves, string? Install,
    string? GameVersion);

/// <summary>One Switch user on every device, as this machine's settings show it (<see cref="SwitchUserSharing"/>):
/// <see cref="Shared"/> the chosen user, <see cref="Declined"/> whether it was put back here, <see cref="Here"/> each
/// Yuzu-based install on this machine, <see cref="Devices"/> every device's users (empty when the server didn't answer,
/// <see cref="Reachable"/>).</summary>
public sealed record SwitchUserSharingDto(SwitchUserDto? Shared, bool Declined, SwitchInstallUserDto[] Here,
    MachineSwitchUsersDto[] Devices, bool Reachable, Guid? ThisMachineId);

/// <summary>One Yuzu-based install here against the shared user (<see cref="SwitchUserSharing.InstallState"/>).</summary>
public sealed record SwitchInstallUserDto(string Emulator, string Install, string Saves, string? UserId, string? UserName,
    bool Matches, SwitchCollisionDto[] Collisions, bool CanUndo, string? Note);

/// <summary>A title saved under both this install's user and the shared one: the user picks which to keep.</summary>
public sealed record SwitchCollisionDto(string TitleId, string? GameName, DateTime? HereWritten, DateTime? SharedWritten);

public sealed record SwitchUserActionResponse(SwitchUserSharingDto State, string Message, bool Ok);
public sealed record ShareSwitchUserRequest(string Id, string Name);
/// <summary><see cref="Keep"/>: title ID → <c>here</c> or <c>shared</c>, for the titles saved under both users.</summary>
public sealed record ApplySwitchUserRequest(Dictionary<string, string>? Keep = null);

/// <summary>A game's save here next to the cloud's head: <see cref="State"/> is one of <see cref="SaveVsCloud"/>.
/// <see cref="Emulators"/>: the Switch emulators the game's machines say they play it in, this one's too.
/// <see cref="HeadGameVersion"/>: the version of the game the cloud's save was made with, when its machine said.</summary>
public sealed record SaveVsCloudDto(string State, string? HeadMachineName, DateTime? HeadCreatedAt, string[] Emulators,
    string? HeadGameVersion = null);

public static class SaveVsCloud
{
    public const string Same = "same";
    /// <summary>The cloud moved on since the last sync here, and the save here didn't.</summary>
    public const string CloudNewer = "cloud-newer";
    /// <summary>The save here changed since the last sync, and the cloud didn't.</summary>
    public const string HereNewer = "here-newer";
    /// <summary>Both changed since they were last the same.</summary>
    public const string Different = "different";
    public const string NoCloud = "no-cloud";
    public const string Unreachable = "unreachable";

    public static string Of(string? local, string cloud, string? lastSynced)
    {
        if (string.Equals(local, cloud, StringComparison.OrdinalIgnoreCase)) return Same;
        var cloudMoved = !string.Equals(cloud, lastSynced, StringComparison.OrdinalIgnoreCase);
        var hereMoved = local is not null && !string.Equals(local, lastSynced, StringComparison.OrdinalIgnoreCase);
        return cloudMoved && hereMoved ? Different : hereMoved ? HereNewer : CloudNewer;
    }
}

/// <summary>What to do with one emulator's copy: one of <see cref="SwitchCopyActions"/>. <see cref="Saves"/> picks one
/// install when the emulator has two here (its row's <see cref="SwitchCopyDto.Saves"/>).</summary>
public sealed record SwitchCopyActionRequest(string Action, string? Saves = null);

public static class SwitchCopyActions
{
    public const string CopyHere = "copy-here";
    public const string KeepInSync = "keep-in-sync";
    public const string StopKeeping = "stop-keeping";
    public const string MakeSynced = "make-synced";
    public const string KeepCopy = "keep-copy";
    public const string KeepSynced = "keep-synced";
    public const string TellWhenReady = "tell-when-ready";
    public const string StopTelling = "stop-telling";
    /// <summary>A forced pull into the copy that syncs; what it holds is zipped first.</summary>
    public const string TakeCloud = "take-cloud";
    /// <summary>A forced push of the copy that syncs; the cloud's head stays a version.</summary>
    public const string Upload = "upload";
}
