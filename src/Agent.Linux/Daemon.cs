namespace SaveLocker.Agent.Linux;

/// <summary>
/// The headless agent. There is no tray and no toast on a Deck (Game Mode has no desktop), so
/// the daemon is the whole Linux agent: it serves the same React UI the tray hosts, polls the
/// server for dashboard commands, drains the offline queue, and watches save folders.
///
/// The launch wrapper (<see cref="ProtonRun"/>), not a process watcher, is what drives
/// pull-on-launch and push-on-exit — see Decisions.md §3.
/// </summary>
public sealed class Daemon : IAsyncDisposable
{
    public const int DefaultApiPort = 5178;

    private readonly int _apiPort;
    private readonly AgentConfig _config;
    private readonly Detection _detection;
    private readonly LinuxGameScanner _scanner;
    private readonly OfflineQueue _offlineQueue;
    private readonly HealthReporter _health;
    private readonly List<FolderWatcher> _folderWatchers = new();

    private AgentApiServer? _apiServer;
    private CommandPoller? _commandPoller;
    private OfflineQueueDrainer? _drainer;
    // Written from an API request thread (server URL change, registration), read from watcher,
    // poller and drainer threads — the publish has to be visible to all of them.
    private volatile SyncEngine _engine;

    /// <summary>
    /// <paramref name="apiPort"/> is overridable so a test harness can run a daemon alongside the
    /// real agent. It is still loopback-only — the port moves, the exposure does not.
    /// </summary>
    public Daemon(AgentConfig config, int apiPort = DefaultApiPort)
    {
        _apiPort = apiPort;
        _config = config;
        _offlineQueue = OfflineQueue.For(config);
        _health = HealthReporter.For(config);
        _detection = new Detection(config);
        _scanner = new LinuxGameScanner(_detection);
        _engine = BuildEngine();
    }

    private SyncEngine BuildEngine() =>
        new(_config, ApiClient.For(_config),
            log: AgentLogger.Log, notify: Notify, offlineQueue: _offlineQueue, health: _health);

    /// <summary>
    /// There is nobody here to notify — a toast is impossible in Game Mode — so locally a
    /// "notification" is only a log line. What makes these visible is <see cref="HealthReporter"/>:
    /// the same alerts go to the server, and the console shows them. The console is the Deck's UI
    /// (Decisions.md §2).
    /// </summary>
    private static void Notify(string message) => AgentLogger.Log(message);

    public async Task RunAsync(CancellationToken ct)
    {
        AgentLogger.Log($"SaveLocker daemon starting — machine '{_config.MachineName}', server {_config.ServerUrl}");

        _apiServer = new AgentApiServer(
            port: _apiPort,
            config: _config,
            doScan: () => _scanner.ScanAsync(),
            enroll: async (candidates, ids) =>
            {
                var result = await Enroller.EnrollAsync(_config, candidates, ids, ct);
                if (result.enrolled > 0) StartFolderWatchers();
                return result;
            },
            autoStart: new SystemdAutoStart(),
            pickFolder: null, // headless: no native dialog — the UI browses via /api/browse instead
            // Retire the engine being replaced, off the request thread. Dropping it kept its lease
            // timers renewing against the old server for the life of the process. WA-06.
            onConnectionChanged: () =>
            {
                var replaced = _engine;
                _engine = BuildEngine();
                _ = Task.Run(async () =>
                {
                    try { await replaced.RetireAsync(); }
                    catch (Exception ex) { AgentLogger.LogException("RetireEngine", ex); }
                });
            },
            getUpdateResult: () => null, // self-update is Windows-only (installer-based)
            browseRoots: SteamRoots.BrowseRoots(),
            launchInfo: LinuxLaunchCommand,
            onGamesChanged: StartFolderWatchers);
        _apiServer.Start();

        _drainer = new OfflineQueueDrainer(_offlineQueue, _config, () => _engine, Notify);

        _commandPoller = new CommandPoller(
            _config,
            () => ApiClient.For(_config),
            () => _engine,
            _detection,
            _scanner,
            Notify,
            onGamesChanged: StartFolderWatchers,
            health: _health,
            offlineQueue: _offlineQueue,
            // Lets a templated save path expand inside the game's own Proton prefix. Core has no
            // way to find it: only the Linux host knows where Steam keeps compatdata.
            prefixForAppId: appId => SteamRoots.Find()
                .Select(root => SteamRoots.CompatDataPath(root, appId))
                .FirstOrDefault(p => p is not null));
        _commandPoller.Start();

        StartFolderWatchers();

        var where = $"http://localhost:{_apiPort}/";
        AgentLogger.Log($"daemon ready — agent UI on {where}");
        Console.WriteLine($"SaveLocker daemon running. Agent UI: {where}");
        Console.WriteLine($"To reach it from another device, tunnel it: ssh -L {_apiPort}:localhost:{_apiPort} <user>@<this-machine>");

        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { /* SIGTERM / Ctrl-C */ }

        AgentLogger.Log("SaveLocker daemon stopping.");
    }

    /// <summary>
    /// The Steam launch-options command for this device. We resolve the binary's real path from
    /// <c>/proc/self/exe</c> rather than assuming <c>$HOME/.local/bin</c>: that is where install.sh
    /// links <c>savelocker</c>, but the daemon may have been started from anywhere, and the full path
    /// is required because Game Mode does not put <c>~/.local/bin</c> on PATH.
    /// </summary>
    internal static LaunchCommandDto LinuxLaunchCommand()
    {
        string? exe = null;
        try { exe = new FileInfo("/proc/self/exe").LinkTarget; } catch { /* fall back below */ }
        if (string.IsNullOrEmpty(exe)) exe = Environment.ProcessPath;
        return string.IsNullOrEmpty(exe)
            ? new LaunchCommandDto(null, "Could not determine the installed path — see install.sh for the exact command.")
            : new LaunchCommandDto($"{exe} run -- %command%", null);
    }

    /// <summary>
    /// Watch each mapped save folder so a game that syncs *without* the launch wrapper (started
    /// outside Steam, or a save written while the game idles) still gets pushed. The settle gate
    /// keeps this from archiving a save mid-write.
    /// </summary>
    private void StartFolderWatchers()
    {
        foreach (var w in _folderWatchers) w.Dispose();
        _folderWatchers.Clear();

        foreach (var g in _config.Games.Where(g => Directory.Exists(g.SaveDirectory)))
        {
            var game = g;
            _folderWatchers.Add(new FolderWatcher(game.SaveDirectory, () =>
                _ = PushQuietlyAsync(game)));
        }
    }

    private async Task PushQuietlyAsync(TrackedGame game)
    {
        try { await _engine.PushAsync(game, settle: true); }
        catch (Exception ex) { AgentLogger.LogException($"watch-push {game.Name}", ex); }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var w in _folderWatchers) w.Dispose();
        _commandPoller?.Dispose();
        _drainer?.Dispose();
        _apiServer?.Dispose();
        // Shutdown, not a connection change: stop renewing and let any held lease expire rather
        // than blocking the exit on a release to a server that may be why we are stopping.
        _engine.Dispose();
    }
}
