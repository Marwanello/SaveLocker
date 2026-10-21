using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// The agent half of the command channel ([[UX Roadmap]] Workstream 5). On a
/// timer the agent polls the (passive) server to:
/// <list type="number">
///   <item><b>Reconcile games</b> — adopt games defined on the server that aren't
///   tracked here yet (auto-mapping the save dir from the Ludusavi manifest when
///   possible), and drop local games that were deleted on the server.</item>
///   <item><b>Run queued commands</b> — execute dashboard-issued pull/push/sync/scan
///   for this machine and report the outcome.</item>
/// </list>
/// Polling (vs server push) keeps the server passive and works through tunnels /
/// firewalls, since the agent only makes outbound requests.
/// </summary>
public sealed class CommandPoller : IDisposable
{
    private readonly AgentConfig _config;
    private readonly Func<ApiClient> _api;
    private readonly Func<SyncEngine> _engine;
    private readonly Detection _detection;
    private readonly IGameScanner _scanner;
    private readonly Action<string> _notify;
    private readonly Action _onGamesChanged;
    private readonly HealthReporter? _health;
    private readonly OfflineQueue? _offlineQueue;
    /// <summary>compatdata path for a Steam AppID. Host-supplied — Core cannot see Steam's layout.</summary>
    private readonly Func<string, string?>? _prefixForAppId;
    /// <summary>
    /// What turns this tick's two observations — did the server answer, and which conflicts are
    /// open — into notifications. Null for a host with nothing to show, which then pays nothing
    /// extra: the conflicts fetch below is skipped outright rather than run and ignored.
    /// </summary>
    private readonly NotificationCenter? _notices;
    private readonly System.Timers.Timer _timer;
    /// <summary>DLC hand-overs and held pulls (tasks/emulator-saves Phase 5c), on their own task: an upload can take minutes.</summary>
    private readonly DlcHandoverRunner _handover;
    private int _busy; // 0/1 guard so slow ticks don't overlap
    private Task _lastTick = Task.CompletedTask;

    /// <summary>How often an unmapped game is worth re-scanning for. See UpdatePathCandidatesAsync.</summary>
    private static readonly TimeSpan CandidateScanInterval = TimeSpan.FromMinutes(15);
    private DateTime _lastCandidateScan = DateTime.MinValue;
    /// <summary>Whether this start has looked for "Also found" folders yet. See AnnounceFolderSuggestionsAsync.</summary>
    private bool _foldersAnnounced;

    public CommandPoller(
        AgentConfig config,
        Func<ApiClient> api,
        Func<SyncEngine> engine,
        Detection detection,
        IGameScanner scanner,
        Action<string> notify,
        Action onGamesChanged,
        double pollMs = 20000,
        HealthReporter? health = null,
        OfflineQueue? offlineQueue = null,
        Func<string, string?>? prefixForAppId = null,
        NotificationCenter? notices = null)
    {
        _prefixForAppId = prefixForAppId;
        _config = config;
        _api = api;
        _engine = engine;
        _detection = detection;
        _scanner = scanner;
        _notify = notify;
        _onGamesChanged = onGamesChanged;
        _health = health;
        _offlineQueue = offlineQueue;
        _notices = notices;
        _handover = new DlcHandoverRunner(config, api, engine, notices, notify);
        _timer = new System.Timers.Timer(pollMs) { AutoReset = true };
        _timer.Elapsed += (_, _) => _lastTick = TickAsync();
    }

    public void Start() => _timer.Start();

    private async Task TickAsync()
    {
        // Skip if unregistered or a previous tick is still running.
        if (string.IsNullOrEmpty(_config.ApiKey) || _config.MachineId is null) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            await KeepSwitchCopiesAsync();
            await ShareSwitchUserAsync();
            await ReconcileGamesAsync();
            // The first call of the tick is what tells us the server is there. A push that fails
            // reports nothing of the kind on its own — it is queued and retried — so this is the one
            // place "unreachable for five minutes" can be measured (NotificationCenter.ObserveServer).
            _notices?.ObserveServer(true);
            await AnnounceFolderSuggestionsAsync();
            await UpdatePathCandidatesAsync();
            await SetUpEmulatorGamesAsync();
            await RepairDuckStationCardsAsync();
            // Independent of each other — RunCommandsAsync executes dashboard commands,
            // CheckConflictsAsync only reads _config.Games and hits its own endpoint — so run them
            // concurrently rather than paying their two round-trips back to back.
            if (_notices is not null)
                await Task.WhenAll(RunCommandsAsync(), CheckConflictsAsync());
            else
                await RunCommandsAsync();
            _handover.Kick();
            // After the commands: a first pass over a large library is up to MaxFetchesPerWarm downloads,
            // and a dashboard command must not wait behind cover art.
            await WarmArtAsync();
        }
        catch (Exception ex)
        {
            if (ServerReachability.IsUnreachable(ex)) _notices?.ObserveServer(false);
            AgentLogger.LogException("CommandPoller.TickAsync", ex);
        }
        finally
        {
            // Outside the try, and last: the heartbeat must go out even when reconcile or a command
            // threw — a tick that failed is precisely the tick the console needs to hear about.
            // HealthReporter.SendAsync never throws. Its response carries the console's manifest
            // window and "Refresh now"; this host is where the refresh can actually run. Handled is
            // stamped through onStarted so a press the attempt gap refuses stays pending, instead
            // of being recorded as acted on with nothing attempted.
            if (_health is not null)
                await _health.SendAsync(_api(), _config, _offlineQueue, _notify,
                    onEscalation: _notices is null ? null : e => _notices.RaiseEscalation(e),
                    onManifestRefresh: at => _ = _detection.RefreshManifestIfDueAsync(
                        force: true, onStarted: () => _config.MarkManifestRefreshHandled(at)));

            // The manifest is refreshed on its own cadence, never inside a scan: this starts nothing
            // more than an mtime comparison until the window has actually passed, and a download
            // that does start (17 MB) runs here, off any user-facing scan. Fire-and-forget on
            // purpose — a slow download must not stretch the tick.
            _ = _detection.RefreshManifestIfDueAsync();

            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// A Switch game's copies kept in step on this machine (<see cref="SwitchCopies"/>): one played in another emulator is
    /// taken into the synced folder, whose watcher then pushes it. Before the server is asked, so it works offline too.
    /// Under the game's sync lock, and skipped for a game a sync holds: the next tick takes it. An emulator the user waits
    /// for (<see cref="EmulatorCopy.TellWhenReady"/>) that can now sync the game is announced, once.
    /// </summary>
    private async Task KeepSwitchCopiesAsync()
    {
        foreach (var game in _config.Games.Where(g => g.IsEnrolledHere && g.EmulatorProfile == EmulatorProfiles.SwitchDevice).ToList())
        {
            try
            {
                var (ran, why) = await _engine().WithGameLockAsync(game,
                    () => SwitchCopies.MoveToDeviceSaveAsync(_config, game, _api(), AgentLogger.Log), TimeSpan.Zero);
                if (ran && why is not null && _deviceMoveSaid.Add($"{game.GameId:N}|{why}"))
                    AgentLogger.Log($"[{game.Name}] its save here is under an emulator user, not the device save the other devices sync, and could not move yet: {why}");
            }
            catch (Exception ex) { AgentLogger.LogException($"CommandPoller.MoveToDeviceSave '{game.Name}'", ex); }
        }
        IReadOnlyList<SwitchEmulators.Setup>? setups = null;
        foreach (var game in _config.Games.Where(g => g.IsEnrolledHere && g.EmulatorCopies.Any(c => c.KeepInSync || c.TellWhenReady)).ToList())
        {
            try
            {
                setups ??= SwitchEmulators.Find();
                var found = setups;
                // Both write the game's copies, so both run under its lock.
                var (ran, result) = await _engine().WithGameLockAsync(game,
                    () => Task.FromResult((Lines: SwitchCopies.Reconcile(_config, game, AgentLogger.Log, found),
                        Ready: SwitchCopies.NowReady(_config, game, found))), TimeSpan.Zero);
                if (!ran) continue;
                foreach (var line in result.Lines) _notify(line);
                foreach (var emulator in result.Ready)
                {
                    _notify($"[{game.Name}] {emulator} can sync it now.");
                    _notices?.Raise(NoticeCatalog.EmulatorReady(game.GameId, game.Name, emulator));
                }
            }
            catch (Exception ex) { AgentLogger.LogException($"CommandPoller.KeepSwitchCopies '{game.Name}'", ex); }
        }
    }

    private readonly HashSet<string> _deviceMoveSaid = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _deviceSaveSaid = new();

    // The shared user last asked about here, so a choice the user has to make is said once, not every tick.
    private string? _switchUserAsked;

    /// <summary>
    /// One Switch user on every device (<see cref="SwitchUserSharing"/>): once a user is chosen on any device (it rides the
    /// heartbeat), every Yuzu-based install here adopts it — after a Switch emulator closes, and unless this device was put
    /// back from it. Titles saved under both users wait for the user to pick, which is said once.
    /// </summary>
    private async Task ShareSwitchUserAsync()
    {
        if (_config.SwitchSharedUser is not { } shared ||
            string.Equals(_config.SwitchSharedUserDeclined, shared.Id, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var setups = SwitchEmulators.Find();
            var todo = SwitchUserSharing.States(_config, shared, setups).Where(s => s.UserId is not null && !s.Matches).ToList();
            if (todo.Count == 0 || SwitchCopies.RunningEmulator() is not null) return;
            if (todo.FirstOrDefault(s => s.Note is not null || s.Collisions.Length > 0) is { } waits)
            {
                if (_switchUserAsked == shared.Id) return;
                _switchUserAsked = shared.Id;
                _notify(waits.Note ?? $"{waits.Emulator} here and {shared.Name} both have saves of " +
                        $"{string.Join(", ", waits.Collisions.Select(c => c.GameName ?? c.TitleId))}. Choose which to keep in Settings, under Switch user.");
                return;
            }
            var outcome = await SwitchUserSharing.ApplyAsync(_config, shared, setups, _api(), SwitchUserSharing.LocksOf(_engine()), null, AgentLogger.Log);
            if (outcome.Refusal is { } why)
            {
                // The page's button is changing it right now: nothing to say, the next tick sees what it did.
                if (why == SwitchUserSharing.Busy || _switchUserAsked == shared.Id) return;
                _switchUserAsked = shared.Id;
                _notify($"Could not switch to the shared Switch user {shared.Name}: {why}");
                return;
            }
            foreach (var line in outcome.Done) _notify(line);
            if (outcome.Done.Count > 0) _onGamesChanged?.Invoke();
        }
        catch (Exception ex) { AgentLogger.LogException("CommandPoller.ShareSwitchUser", ex); }
    }

    // ----- game-list reconciliation (server → agent propagation) -----

    /// <summary>The server's game list from this tick's reconcile — what the art cache is warmed from.</summary>
    private List<GameDto>? _lastServerGames;

    /// <summary>
    /// Keep the covers and icons of the games tracked here on disk (<see cref="ArtCache"/>), so every
    /// surface has them offline and none has to be opened first for them to be fetched. Costs nothing once
    /// the cache is current: a file is fetched only when the server names a new image.
    /// </summary>
    private async Task WarmArtAsync()
    {
        if (_lastServerGames is not { } games) return;
        var tracked = _config.Games.Select(g => g.GameId).ToHashSet();
        try { await ArtCache.For(_config.StateDir).WarmAsync(games.Where(g => tracked.Contains(g.Id)).ToList(), _api()); }
        catch (Exception ex) { AgentLogger.LogException("CommandPoller.WarmArtAsync", ex); }
    }

    internal async Task ReconcileGamesAsync()
    {
        var serverGames = await _api().ListGamesAsync();
        _lastServerGames = serverGames;
        var serverById = serverGames.ToDictionary(g => g.Id);
        _serverGames = serverById;
        var changed = false;

        // Drop local games that were deleted on the server. Copy-on-write for the same reason as
        // enrollment: this runs on a timer thread while a UI thread enumerates the same list.
        // A game merged into another on the server (tasks/emulator-saves Phase 17) is gone from the list; when the server moved
        // this machine's folder to the game it was merged into, this device's copy becomes that game here, keeping its folders
        // and own file names. Its last synced version is that game's history now and its last synced hash is cleared, so when
        // its save differs from that game's Latest the next sync is a conflict, never a silent pull.
        var mergedInto = serverGames
            .SelectMany(g => (g.MergedGameIds ?? []).Select(id => (Id: id, Into: g)))
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Into);
        var mergeNotes = new List<string>();
        var movedInto = new List<Guid>();
        if (mergedInto.Count > 0)
            _config.MutateGames(list =>
            {
                foreach (var local in list.Where(g => !serverById.ContainsKey(g.GameId) && g.IsEnrolledHere).ToList())
                {
                    if (!mergedInto.TryGetValue(local.GameId, out var into)) continue;
                    var existing = list.FirstOrDefault(g => g.GameId == into.Id);
                    if (existing is { IsEnrolledHere: true })
                        mergeNotes.Add($"'{local.Name}' was merged into '{into.Name}', which this device already syncs.");
                    else if (!string.IsNullOrWhiteSpace(into.MachineSavePath) &&
                             string.Equals(into.MachineSavePath, local.SaveDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        if (existing is not null) list.Remove(existing);
                        mergeNotes.Add($"'{local.Name}' was merged into '{into.Name}' on the server; this device syncs it as that game now.");
                        local.GameId = into.Id;
                        local.Name = into.Name;
                        // Its last synced hash was the merged game's: kept, a pull would take the other game's Latest over
                        // this save without asking. Cleared, the pull refuses and the push meets that Latest as a conflict.
                        local.LastSyncedHash = null;
                        local.ConsecutiveConflicts = 0;
                        movedInto.Add(into.Id);
                        changed = true;
                    }
                    else
                        mergeNotes.Add($"'{local.Name}' was merged into '{into.Name}' on the server. Add '{into.Name}' from Add games to keep syncing it here.");
                }
            });
        // A copy kept apart by hand ("Keep it as its own game") opted this machine out of the game it is now part of.
        foreach (var id in movedInto) _config.SetTracked(id, tracked: true);
        foreach (var note in mergeNotes) _notify(note);

        var removed = 0;
        _config.MutateGames(list => removed = list.RemoveAll(g => !serverById.ContainsKey(g.GameId)));
        if (removed > 0)
        {
            changed = true;
            _notify($"Removed {removed} game(s) deleted on the server.");
        }

        var localById = _config.Games.ToDictionary(g => g.GameId);
        foreach (var sg in serverGames)
        {
            if (localById.TryGetValue(sg.Id, out var local))
            {
                // Keep exclude globs in sync with the server's effective set (silent).
                var serverGlobs = sg.ExcludeGlobs ?? Array.Empty<string>();
                if (!serverGlobs.SequenceEqual(local.ExcludeGlobs))
                {
                    local.ExcludeGlobs = serverGlobs.ToList();
                    changed = true;
                }

                // Which release it is — region, game IDs — as the server keeps it, for the Games pages.
                if (!GameReleaseDto.Same(local.Release, sg.Release))
                {
                    local.Release = sg.Release;
                    changed = true;
                }
                if (sg.EmulatorKey is { } key && local.EmulatorKey != key)
                {
                    local.EmulatorKey = key;
                    changed = true;
                }
                var emulators = (sg.Emulators ?? []).ToList();
                if (!emulators.SequenceEqual(local.Emulators, StringComparer.OrdinalIgnoreCase))
                {
                    local.Emulators = emulators;
                    changed = true;
                }
                if (local.EmulatorProfile != sg.EmulatorProfile)
                {
                    local.EmulatorProfile = sg.EmulatorProfile;
                    changed = true;
                }
                if (local.Title != TitleInfo.From(sg))
                {
                    local.Title = TitleInfo.From(sg);
                    changed = true;
                }
                // This machine syncs a device save the server doesn't know is one (a game enrolled before it was recorded):
                // it is told, so every other machine keeps the game in its device save too.
                if (SwitchCopies.ShouldSayDeviceSave(local, sg) && _deviceSaveSaid.TryAdd(sg.Id, 0))
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _api().CreateGameAsync(new CreateGameRequest(sg.Name, null, null,
                                EmulatorProfile: EmulatorProfiles.SwitchDevice, EmulatorKey: sg.EmulatorKey));
                        }
                        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { _deviceSaveSaid.TryRemove(sg.Id, out _); }
                    });
                // The server renames a Switch game first enrolled as "Switch <title ID>" once a machine knows its name. Only
                // that name follows the server's: no other game's is renamed under it.
                if (!string.IsNullOrWhiteSpace(sg.Name) && local.Name != sg.Name &&
                    SwitchTitles.TitleIdOf(sg.EmulatorKey) is { } titleId &&
                    string.Equals(local.Name, SwitchTitles.Placeholder(titleId), StringComparison.OrdinalIgnoreCase))
                {
                    local.Name = sg.Name;
                    changed = true;
                }

                // Server now has a stored path for this machine → apply it (highest authority).
                // "Highest authority" is not "unconditionally trusted": this is the one path source
                // with no local confirmation step at all, so a mistaken or hostile console value
                // would silently repoint a game at the user's profile. WA-02.
                if (!string.IsNullOrWhiteSpace(sg.MachineSavePath) &&
                    sg.MachineSavePath != local.SaveDirectory)
                {
                    var check = SavePathGuard.Check(sg.MachineSavePath, _config.StateDir);
                    if (!check.Ok)
                    {
                        // Not applied and not silent — the console sent it, so the console is where
                        // whoever set it is looking.
                        _health?.Report(AgentEventCodes.UnsafeSavePath, AgentEventSeverity.Error,
                            $"Refused the server's save folder for '{sg.Name}': {check.Reason} " +
                            $"(server sent '{sg.MachineSavePath}'). The previous mapping is unchanged.",
                            sg.Id);
                        AgentLogger.Log($"Refused server save path for '{sg.Name}': {check.Reason}");
                    }
                    else if (check.Canonical != local.SaveDirectory && Claimed(sg, check.Canonical!, sg.IncludeGlobs) is null)
                    {
                        local.SaveDirectory = check.Canonical!;
                        changed = true;
                        _notify($"Updated '{sg.Name}' save folder from server: {check.Canonical}");
                    }
                }
                // Already tracked but unmapped: detect locally and report back to server.
                else if (string.IsNullOrEmpty(local.SaveDirectory) &&
                         await ResolveSaveDirAsync(sg) is { } fill)
                {
                    local.SaveDirectory = fill;
                    changed = true;
                    _notify($"Mapped '{sg.Name}' to {fill}.");
                    ReportPathAsync(sg.Id, fill);
                }
                // Mapped here, but the server has no path for this machine — say what we are using.
                // Without this the console shows "not set" for a machine that is syncing happily,
                // which reads as a broken agent and invites someone to "fix" it by setting a path.
                // It also never self-corrects: clearing the path in the console does not unmap the
                // agent, so the disagreement is permanent until the agent happens to re-resolve.
                else if (string.IsNullOrWhiteSpace(sg.MachineSavePath) &&
                         !string.IsNullOrWhiteSpace(local.SaveDirectory))
                {
                    ReportPathAsync(sg.Id, local.SaveDirectory);
                }

                // A machine that has a working path can describe it generically for the others.
                if (!string.IsNullOrWhiteSpace(local.SaveDirectory))
                    ReportTemplateAsync(sg, local.SaveDirectory);

                // How this machine found the game: re-sent whenever the server's copy differs (a game
                // enrolled offline, or by a process that could not reach the server).
                // Once per source: a console that predates the route never echoes it back, and asking it
                // again every 20 s for every game would change nothing.
                if (local is { IsEnrolledHere: true, Source: { } source } && !source.SameAs(sg.MachineSource) &&
                    !(_sourceSettled.TryGetValue(sg.Id, out var settled) && source.SameAs(settled)))
                {
                    _ = Task.Run(async () =>
                    {
                        if (await GameSources.ReportAsync(_api(), local)) _sourceSettled[sg.Id] = source;
                    });
                }

                changed |= await ReconcileFoldersAsync(local, sg);
                continue;
            }

            // Adopt a server game not tracked here yet — unless this machine opted out of it. The
            // game is still on the server for the rest of the fleet; this box just said no.
            if (_config.IsUntracked(sg.Id)) continue;

            var dir = await ResolveSaveDirAsync(sg);
            // If we resolved via detection (not from the server path), report it back.
            if (dir is not null && string.IsNullOrWhiteSpace(sg.MachineSavePath))
                ReportPathAsync(sg.Id, dir);

            // No HasSteamCloud here on purpose: it stays null ("nobody established this"). Only
            // enrollment has a ScanCandidate to decide it from, and GameDto carries no such signal —
            // re-deriving it from the name against the manifest is the exact mistake TrackedGame
            // .HasSteamCloud documents. Null lets the Decky plugin fall back to its own heuristic;
            // a false here would tell it, wrongly, that a Steam Cloud game definitely is not one.
            var adopted = new TrackedGame
            {
                GameId = sg.Id,
                Name = sg.Name,
                ManifestKey = sg.ManifestKey,
                Release = sg.Release,
                EmulatorKey = sg.EmulatorKey,
                EmulatorProfile = sg.EmulatorProfile,
                Emulators = (sg.Emulators ?? []).ToList(),
                Title = TitleInfo.From(sg),
                SaveDirectory = dir ?? "",
                ExcludeGlobs = (sg.ExcludeGlobs ?? Array.Empty<string>()).ToList()
            };
            _config.MutateGames(list => list.Add(adopted));
            await ReconcileFoldersAsync(adopted, sg);
            changed = true;
            _notify(dir is null
                ? $"'{sg.Name}' was added on the server — set its save folder in Settings…"
                : $"Added '{sg.Name}' (save folder {dir}).");
        }

        if (changed)
        {
            _config.Save();
            _onGamesChanged();
        }
    }

    /// <summary>
    /// The include scopes and the extra save folders (tasks/multiple-save-paths plan §7): the same
    /// steps the primary folder gets above, once per key — apply the server's stored folder for this
    /// machine, else expand the folder's template here, report what is in use, and describe it
    /// generically for the fleet. Manifest detection is never a fallback here: which manifest
    /// locations belong together is exactly what the manifest cannot say (plan, "The ambiguity").
    /// <para>
    /// A folder nothing maps stays a shadow, and mapping one goes through
    /// <see cref="SyncEngine.MapSavePathAsync"/>, which carries the shadow's files over — never a
    /// bare assignment, or this machine's next push would drop the fleet's copy of that folder.
    /// </para>
    /// </summary>
    private async Task<bool> ReconcileFoldersAsync(TrackedGame local, GameDto sg)
    {
        var changed = false;
        var include = (sg.IncludeGlobs ?? Array.Empty<string>()).ToList();
        if (!include.SequenceEqual(local.IncludeGlobs))
        {
            local.IncludeGlobs = include;
            _cardChecked.Remove(local.GameId);
            changed = true;
        }

        // Another machine took in a DuckStation card the scope had missed, and pushes it. This machine's own copy of that
        // card was never synced, so it is not an edit to push over it: the pull brings the fleet's — once the fleet has one.
        // Until then it is asked again each poll; the answer is final once it is no repair or the head holds the card.
        if (!_cardPushes.Contains(local.GameId) && !_cardChecked.Contains(local.GameId))
        {
            if (DuckStationSaves.SyncedHashWithCard(local, _config.StateDir) is not { } synced)
                _cardChecked.Add(local.GameId);
            else if (DuckStationSaves.FleetHasCard(local, await HeadFilesOrNullAsync(local.GameId)))
            {
                _cardChecked.Add(local.GameId);
                local.LastSyncedHash = synced;
                _config.SaveGameSyncState(local);
                AgentLogger.Log($"[{local.Name}] its scope now takes DuckStation's region-named card; the fleet's copy will be pulled.");
            }
        }

        var serverPaths = sg.ExtraPaths ?? Array.Empty<SavePathDto>();
        foreach (var gone in local.ExtraPaths.Select(p => p.Key).Where(k => serverPaths.All(s => s.Key != k)).ToList())
        {
            if (!await _engine().ForgetSavePathAsync(local, gone)) continue;   // lock busy: next poll
            changed = true;
            _notify($"'{sg.Name}' no longer syncs its '{gone}' save folder — it was removed on the server.");
        }

        foreach (var sp in serverPaths)
        {
            if (SaveRoot.ValidateExtraKey(sp.Key) is not null) continue;   // a server must not name a path segment
            var lp = local.ExtraPaths.FirstOrDefault(p => p.Key == sp.Key);
            if (lp is null)
            {
                lp = new TrackedSavePath { Key = sp.Key };
                // New lists, never in-place edits: a push or the local API may be enumerating these.
                local.ExtraPaths = [.. local.ExtraPaths, lp];
                local.RemovedPathKeys = local.RemovedPathKeys.Where(k => k != sp.Key).ToList();
                changed = true;
            }

            var scope = (sp.IncludeGlobs ?? Array.Empty<string>()).ToList();
            if (lp.Label != sp.Label || lp.Template != sp.Template || !scope.SequenceEqual(lp.IncludeGlobs))
            {
                lp.Label = sp.Label;
                lp.Template = sp.Template;
                lp.IncludeGlobs = scope;
                changed = true;
            }

            // A folder this machine mapped that the server has not heard of yet: tell it first. Until
            // then (and for the rest of this pass, whose snapshot predates the report) the server's
            // stored folder is the old one, and applying it would move the folder straight back.
            var localWins = lp.IsMapped && lp.PathUnreported;
            if (localWins && await ReportPathNowAsync(sg, lp))
            {
                lp.PathUnreported = false;
                changed = true;
            }

            // The server's stored folder for this machine first, as for the primary folder; else the
            // template, expanded here, when it names a folder that exists.
            var fromServer = !localWins && !string.IsNullOrWhiteSpace(sp.MachinePath);
            var want = fromServer
                ? (SavePathGuard.Canonicalize(sp.MachinePath) == SavePathGuard.Canonicalize(lp.Directory) ? null : sp.MachinePath)
                : lp.IsMapped ? null : ResolveExtraDir(sg, sp);
            if (want is not null && Claimed(sg, want, sp.IncludeGlobs) is not null) want = null;
            if (want is not null && await TryMapFolderAsync(local, sg, lp.Key, want, fromServer))
                changed = true;

            if (!lp.IsMapped) continue;
            if (!fromServer && !localWins) ReportPathAsync(sg.Id, lp.Directory!, lp.Key);
            if (string.IsNullOrWhiteSpace(sp.Template)) ReportTemplateAsync(sg, lp.Directory!, lp.Key);
        }
        return changed;
    }

    /// <summary>Where an extra folder's template lands on this machine, when that folder exists here.</summary>
    private string? ResolveExtraDir(GameDto sg, SavePathDto sp)
    {
        if (string.IsNullOrWhiteSpace(sp.Template)) return null;
        if (!PathResolver.IsTemplate(sp.Template))
            return Directory.Exists(sp.Template) ? sp.Template : null;
        return ResolverForGame(sg)?.ResolveToDirectory(sp.Template) is { } expanded && Directory.Exists(expanded)
            ? expanded
            : null;
    }

    private async Task<bool> ReportPathNowAsync(GameDto sg, TrackedSavePath lp)
    {
        try
        {
            await _api().SetMachinePathAsync(sg.Id, lp.Directory!, lp.Key);
            return true;
        }
        catch (Exception ex)
        {
            AgentLogger.LogException($"CommandPoller.ReportPath {sg.Name}/{lp.Key}", ex);
            return false;
        }
    }

    /// <summary>Each game's source the server has already answered for (stored or refused), so a poll that
    /// still does not see it echoed back does not send it again. Written from the report task.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, GameSourceDto> _sourceSettled = new();

    /// <summary>Folders already reported as not mappable, so a 20 s poll says so once, not forever.</summary>
    private readonly HashSet<string> _unmappedReported = new(StringComparer.Ordinal);

    /// <summary>
    /// Mappings that ended in <see cref="MapFolderResult.NeedsChoice"/>, with a stamp of both copies
    /// when they did. Answering that hashes both folders under the game's lock, so it is asked again
    /// only once something it depends on changed — not every 20 s for as long as nobody answers.
    /// </summary>
    private readonly Dictionary<string, string> _awaitingChoice = new(StringComparer.Ordinal);

    private async Task<bool> TryMapFolderAsync(TrackedGame local, GameDto sg, string key, string dir, bool fromServer)
    {
        var id = $"{sg.Id:N}/{key}/{dir}";
        var stamp = ChoiceStamp(local, key, dir);
        if (_awaitingChoice.TryGetValue(id, out var asked) && asked == stamp) return false;

        MapFolderResult result;
        try { result = await _engine().MapSavePathAsync(local, key, dir); }
        catch (Exception ex)
        {
            AgentLogger.LogException($"CommandPoller.MapFolder {sg.Name}/{key}", ex);
            return false;
        }

        if (result.NeedsChoice) _awaitingChoice[id] = stamp;
        else _awaitingChoice.Remove(id);

        if (result.Ok)
        {
            _notify($"Mapped '{sg.Name}' save folder '{key}' to {result.Directory}.");
            return true;
        }

        if (!_unmappedReported.Add(id)) return false;
        if (result.NeedsChoice)
            _health?.Report(AgentEventCodes.SaveFolderNeedsChoice, AgentEventSeverity.Warning,
                $"'{sg.Name}' save folder '{key}': {result.Error} " +
                $"Run: savelocker add-path \"{sg.Name}\" --key {key} --dir \"{dir}\" --keep local|cloud", sg.Id);
        else if (fromServer)
            _health?.Report(AgentEventCodes.UnsafeSavePath, AgentEventSeverity.Error,
                $"Refused the server's folder for '{sg.Name}' save folder '{key}': {result.Error} " +
                $"(server sent '{dir}'). It keeps syncing through its shadow copy.", sg.Id);
        AgentLogger.Log($"Did not map '{sg.Name}' save folder '{key}' to '{dir}': {result.Error}");
        return false;
    }

    /// <summary>
    /// What a <see cref="SyncEngine.MapSavePathAsync"/> answer depends on, read from file metadata only:
    /// the folder's scope, and each file's name, size and time in both the target and the copy this
    /// machine syncs now (whether that copy exists at all included).
    /// </summary>
    private string ChoiceStamp(TrackedGame game, string key, string dir)
    {
        var path = game.ExtraPaths.FirstOrDefault(p => p.Key == key);
        if (path is null) return "";
        var source = path.Directory ?? TrackedGame.ShadowDir(_config.StateDir, game.GameId, key);
        var sb = new System.Text.StringBuilder(string.Join(';', path.IncludeGlobs)).Append('|');
        foreach (var folder in new[] { source, dir })
        {
            sb.Append(Directory.Exists(folder) ? '+' : '-').Append(folder).Append('|');
            try
            {
                foreach (var f in SaveArchive.ListSaveFiles(new[] { SaveRoot.Primary(folder, path.IncludeGlobs) }))
                {
                    var info = new FileInfo(f.FullPath);
                    sb.Append(f.ArchiveName).Append('|').Append(info.Exists ? info.Length : -1)
                      .Append('|').Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0).Append(';');
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                sb.Append('?').Append(ex.GetType().Name);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Best save folder for a server game on THIS machine:
    /// 1. Server-stored path for this machine (highest authority — set by dashboard or prior run).
    /// 2. SuggestedSaveDir, which may be a <b>template</b> (<c>&lt;winPublic&gt;/Documents/…</c>)
    ///    expanded against this machine, or a concrete path used as-is if it exists here.
    /// 3. Ludusavi manifest detection.
    /// Returns null if nothing resolves.
    /// </summary>
    private async Task<string?> ResolveSaveDirAsync(GameDto sg)
    {
        // Every return below goes through Safe(): adoption of a brand-new server game reaches the
        // config without passing the reconcile check above, and detection/template expansion can
        // land somewhere just as broad as a hostile server value. WA-02.
        if (!string.IsNullOrWhiteSpace(sg.MachineSavePath))
            return Safe(sg, sg.MachineSavePath);

        // A template beats a literal path, because it is the only form that means the same LOGICAL
        // folder on every machine. A literal from another machine is what lets two agents disagree
        // about the same game's save root by a segment — and that difference is what makes a restore
        // nest a folder under itself and delete the correctly-placed copy.
        if (PathResolver.IsTemplate(sg.SuggestedSaveDir) &&
            ResolverForGame(sg)?.ResolveToDirectory(sg.SuggestedSaveDir!) is { } expanded &&
            Directory.Exists(expanded))
            return Safe(sg, expanded);

        if (!string.IsNullOrWhiteSpace(sg.SuggestedSaveDir) &&
            !PathResolver.IsTemplate(sg.SuggestedSaveDir) &&
            Directory.Exists(sg.SuggestedSaveDir))
            return Safe(sg, sg.SuggestedSaveDir);
        try
        {
            var dirs = await _detection.ResolveSaveDirectoriesAsync(sg.ManifestKey ?? sg.Name);
            return dirs.FirstOrDefault() is { } d ? Safe(sg, d) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// The canonical path if it is one we may ever sync, else null — leaving the game unmapped,
    /// which is the safe state. Reported, because an unmapped game otherwise looks like detection
    /// simply failing and invites someone to "fix" it by setting the very path we refused.
    /// </summary>
    private string? Safe(GameDto sg, string path)
    {
        var check = SavePathGuard.Check(path, _config.StateDir);
        if (check.Ok) return Claimed(sg, check.Canonical!, sg.IncludeGlobs) is null ? check.Canonical : null;

        _health?.Report(AgentEventCodes.UnsafeSavePath, AgentEventSeverity.Error,
            $"Refused a save folder for '{sg.Name}': {check.Reason} (was '{path}'). " +
            "The game is left unmapped; set its folder in Settings.", sg.Id);
        AgentLogger.Log($"Refused save path for '{sg.Name}': {check.Reason} (was '{path}')");
        return null;
    }

    /// <summary>
    /// The other game here that already syncs these files in <paramref name="dir"/> (<see cref="SaveFolderClaims"/>),
    /// reported once per game and folder; null when the folder is free to map. The game stays unmapped: the
    /// usual way here is a save kept as its own game by hand, and mapping the fleet's game too would make two
    /// games push and pull one save.
    /// </summary>
    private TrackedGame? Claimed(GameDto sg, string dir, IReadOnlyList<string>? scope)
    {
        if (SaveFolderClaims.ClaimedBy(_config, sg.Id, dir, scope) is not { } other) return null;
        if (_claimReported.Add($"{sg.Id:N}|{dir}"))
        {
            var why = $"Left '{sg.Name}' unmapped: '{other.Name}' already syncs the same files in {dir} on this machine.";
            _health?.Report(AgentEventCodes.UnsafeSavePath, AgentEventSeverity.Warning, why, sg.Id);
            AgentLogger.Log(why);
        }
        return other;
    }

    private readonly HashSet<string> _claimReported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The server's games as the last reconcile read them, for what the scan may suggest.</summary>
    private IReadOnlyDictionary<Guid, GameDto> _serverGames = new Dictionary<Guid, GameDto>();

    /// <summary>
    /// The scan's guess at an unmapped game's folder: a same-named row that is a save of its own (not a seed, not a
    /// shared memory card) and, for an emulator save, the same save — the same files, written by an emulator the
    /// game was found through (<see cref="Enroller.SameFiles"/>). PrimeHack's and Dolphin's Metroid Prime Trilogy
    /// share a name and a scope and are still two games, and another ROM of the title is not the game at all.
    /// </summary>
    internal static ScanCandidate? PathCandidateFor(TrackedGame game, GameDto? serverGame, IEnumerable<ScanCandidate> found) =>
        found.FirstOrDefault(c =>
            !c.UntouchedSeed && c.NotSyncable is null && string.Equals(c.Name, game.Name, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(c.SuggestedSaveDir) &&
            (c.IncludeGlobs is not { Count: > 0 } ||
             (serverGame is not null ? Enroller.SameFiles(serverGame, c) : Enroller.SameScope(game.IncludeGlobs, c.IncludeGlobs))));

    /// <summary>
    /// Tell the console what this machine's scan <b>guesses</b> the save folder is, for every game
    /// still unmapped after reconcile. Reconcile has already tried manifest detection and reported
    /// anything it resolved; what is left is the case the scan can still answer — a Steam shortcut
    /// or a Proton prefix the manifest does not describe.
    /// <para>
    /// Only unmapped games are reported. This is not a privacy nicety: uploading the whole scan
    /// would put the user's entire game library on the server for no purpose the console has.
    /// </para>
    /// </summary>
    private async Task UpdatePathCandidatesAsync()
    {
        if (_health is null) return;

        var unmapped = _config.Games
            .Where(g => string.IsNullOrWhiteSpace(g.SaveDirectory))
            .ToList();

        // Clear immediately when nothing is unmapped — a machine that fixed itself should stop
        // offering guesses at once, without waiting out the scan interval below.
        if (unmapped.Count == 0)
        {
            _health.SetPathCandidates(Array.Empty<ScanPathCandidate>());
            return;
        }

        // A scan walks the disk; the poll is every 20 s. Rescanning on every tick would make an
        // unmapped game a permanent background I/O load on a device with a slow SD card.
        if (DateTime.UtcNow - _lastCandidateScan < CandidateScanInterval) return;
        _lastCandidateScan = DateTime.UtcNow;

        IReadOnlyList<ScanCandidate> found;
        try { found = await _scanner.ScanAsync(); }
        catch (Exception ex)
        {
            AgentLogger.LogException("CommandPoller.UpdatePathCandidates", ex);
            return;
        }
        _lastScan = found;

        var reports = new List<ScanPathCandidate>();
        foreach (var game in unmapped)
        {
            var match = PathCandidateFor(game, _serverGames.GetValueOrDefault(game.GameId), found);

            // Offering a path that is not there wastes the one click this exists to save.
            if (match?.SuggestedSaveDir is { } dir && Directory.Exists(dir))
                reports.Add(new ScanPathCandidate(game.GameId, Path.GetFullPath(dir)));
        }

        _health.SetPathCandidates(reports);
    }

    /// <summary>The last scan <see cref="UpdatePathCandidatesAsync"/> made, for <see cref="SetUpEmulatorGamesAsync"/>.</summary>
    private IReadOnlyList<ScanCandidate>? _lastScan;
    private DateTime _lastEmulatorSetUp = DateTime.MinValue;
    private static readonly TimeSpan EmulatorSetUpInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Sets up the emulator games this machine tracks without a folder (tasks/emulator-pull-safety Phase 5): ones taken
    /// from the server before their first save here, and ones never played here at all. Each is enrolled from the row
    /// <see cref="SetUpRowFor"/> finds, the way Add games would, so it joins the server game by its files. At most once a
    /// minute: a ROM copied in is picked up on the next pass.
    /// </summary>
    private async Task SetUpEmulatorGamesAsync()
    {
        var waiting = _config.Games.Where(g => !g.IsEnrolledHere && g.Emulators.Count > 0).ToList();
        if (waiting.Count == 0 || DateTime.UtcNow - _lastEmulatorSetUp < EmulatorSetUpInterval) return;
        _lastEmulatorSetUp = DateTime.UtcNow;

        var pairs = new List<(Guid GameId, ScanCandidate Row)>();
        foreach (var game in waiting)
            if (_serverGames.GetValueOrDefault(game.GameId) is { } sg && SetUpRowFor(sg, _lastScan, g => EmulatorPlaces.CandidateFor(g)) is { } row)
                pairs.Add((game.GameId, row));
        if (pairs.Count == 0) return;
        var rows = pairs.Select(p => p.Row).ToList();
        try
        {
            var (enrolled, _) = await Enroller.EnrollAsync(_config, rows, Enumerable.Range(0, rows.Count).ToArray(), showProgress: false);
            if (enrolled == 0) return;
            foreach (var (_, row) in pairs.Where(p => _config.Games.Any(g => g.GameId == p.GameId && g.IsEnrolledHere)))
                _notify(row.NoSaveYet
                    ? $"Set up '{row.Name}' here: its ROM is on this machine, so its save will be pulled into {row.SuggestedSaveDir}."
                    : $"Mapped '{row.Name}' to {row.SuggestedSaveDir}, where this machine saved it.");
            _onGamesChanged();
        }
        catch (Exception ex)
        {
            // Never thrown on to the tick: it would skip the dashboard's commands that follow, once a minute for good.
            AgentLogger.LogException("CommandPoller.SetUpEmulatorGames", ex);
        }
    }

    /// <summary>
    /// A DuckStation game enrolled states-only before tasks/emulator-pull-safety Phase 6 names its card without the
    /// region DuckStation puts in it, so the card never synced. The first machine that finds that card adds the wider
    /// patterns on the server and pushes its card; the others take it from there (<see cref="ReconcileFoldersAsync"/>).
    /// </summary>
    private readonly HashSet<Guid> _cardPushes = [];
    private readonly HashSet<Guid> _cardChecked = [];

    private readonly HashSet<Guid> _cardRefused = [];

    private async Task<HeadFilesDto?> HeadFilesOrNullAsync(Guid gameId)
    {
        try { return await _api().GetHeadFilesAsync(gameId); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
    }

    private async Task RepairDuckStationCardsAsync()
    {
        foreach (var game in _config.Games.Where(g => g.IsEnrolledHere && !_cardRefused.Contains(g.GameId) &&
                     EmulatorProcesses.EmulatorsOf(g).Contains(DuckStationSaves.EmulatorName, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            if (DuckStationSaves.MissingCardGlobs(game.IncludeGlobs, game.SaveDirectory) is not { } missing) continue;
            try
            {
                var added = await _api().AddIncludeGlobsAsync(game.GameId, missing);
                if (added.Length == 0) continue;
                game.IncludeGlobs = [.. game.IncludeGlobs, .. added];
                _cardPushes.Add(game.GameId);
                _config.Save();
                AgentLogger.Log($"[{game.Name}] its scope now takes DuckStation's region-named card ({string.Join(", ", added)}); pushing it.");
                await _engine().PushAsync(game);
            }
            catch (Exception ex)
            {
                // A scope the server refuses stays refused: asking again every poll only fills the log.
                if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.BadRequest }) _cardRefused.Add(game.GameId);
                // Never thrown on to the tick: it would skip the dashboard's commands that follow.
                AgentLogger.LogException($"CommandPoller.RepairDuckStationCards '{game.Name}'", ex);
            }
        }
    }

    /// <summary>
    /// The row that sets up <paramref name="sg"/> here: first a scan row of a save this machine has — the same files
    /// from the same emulator (<see cref="Enroller.SameFiles"/>, D1), whatever it is named — else the emulator's place for a
    /// game whose ROM is here (<paramref name="place"/>, <see cref="EmulatorPlaces.CandidateFor"/>). Null leaves it unmapped.
    /// </summary>
    internal static ScanCandidate? SetUpRowFor(GameDto sg, IEnumerable<ScanCandidate>? found, Func<GameDto, ScanCandidate?> place) =>
        (found ?? []).FirstOrDefault(c => c.Source == ScanSource.Emulator && !c.UntouchedSeed && c.NotSyncable is null &&
                                         !string.IsNullOrWhiteSpace(c.SuggestedSaveDir) && Enroller.SameFiles(sg, c) &&
                                         sg.Emulators is { Length: > 0 })
        ?? place(sg);

    /// <summary>
    /// Once per start, after the first reconcile: if any tracked game has folders the manifest knows
    /// and nobody has answered for (not added, not skipped, not refused), say so. That is what puts the
    /// question to someone who tracked games before this agent could sync more than one folder — the
    /// answer itself is the agent UI's (the notice opens its prompt).
    /// </summary>
    private async Task AnnounceFolderSuggestionsAsync()
    {
        if (_foldersAnnounced || _notices is null) return;
        _foldersAnnounced = true;
        try
        {
            var pending = (await new FolderSuggestions(_config, _detection, _prefixForAppId).AllAsync())
                .Where(s => !s.Deferred)
                .Select(s => s.GameId)
                .Distinct()
                .Count();
            if (pending == 0) return;
            AgentLogger.Log($"{pending} tracked game(s) keep saves in folders not synced yet (\"Also found\").");
            _notices.Raise(NoticeCatalog.FoldersFound(pending));
        }
        catch (Exception ex) { AgentLogger.LogException("CommandPoller.AnnounceFolderSuggestions", ex); }
    }

    /// <summary>
    /// The resolver that expands Windows tokens the way THIS machine sees them.
    /// <para>
    /// On Windows that is the host's own known folders. Under Proton the same tokens mean folders
    /// <b>inside the game's prefix</b>, so the resolver is per-game and needs the game's AppID —
    /// which is why the host supplies the lookup (Core cannot see Steam's layout). A game with no
    /// AppID, or a prefix Steam has not created yet, resolves to nothing rather than to the host's
    /// own folders: pointing a Proton game at <c>~/Documents</c> would sync the wrong directory.
    /// </para>
    /// </summary>
    private PathResolver? ResolverForGame(GameDto sg)
    {
        if (OperatingSystem.IsWindows())
        {
            var tracked = _config.Games.FirstOrDefault(g => g.GameId == sg.Id);
            return PathResolver.Windows(tracked?.InstallDir);
        }

        var appId = _config.Games.FirstOrDefault(g => g.GameId == sg.Id)?.SteamAppId;
        if (string.IsNullOrWhiteSpace(appId)) return null;

        var compatData = _prefixForAppId?.Invoke(appId);
        if (string.IsNullOrWhiteSpace(compatData)) return null;

        // <root> comes off the prefix path itself; <base> is what the scanner recorded at
        // enrollment. Without these the poller expands a game's paths differently from the scanner
        // that first resolved them, and the two disagree about where the save lives.
        var storeRoot = SteamLayout.RootFromCompatData(compatData);
        var installDir = _config.Games.FirstOrDefault(g => g.GameId == sg.Id)?.InstallDir;
        return PathResolver.Proton(compatData, installDir, storeRoot);
    }

    /// <summary>
    /// If the server has no save location for this game, offer one described GENERICALLY.
    /// <para>
    /// This is the exact counterpart of the console's "Use as template" button, and a better source:
    /// the console matches on path layout because it cannot see another machine's environment, while
    /// this reverses the machine's own real token values. Under Proton it tokenizes against the
    /// game's own prefix, so a Deck describes <c>&lt;winDocuments&gt;/My Games/…</c> rather than a
    /// compatdata path no other machine could ever have.
    /// </para>
    /// <para>
    /// The server refuses to overwrite an existing value, so the first correctly-configured machine
    /// teaches the fleet and later ones are declined harmlessly.
    /// </para>
    /// </summary>
    private void ReportTemplateAsync(GameDto sg, string localPath, string key = SaveRoot.PrimaryKey)
    {
        if (key == SaveRoot.PrimaryKey && !string.IsNullOrWhiteSpace(sg.SuggestedSaveDir)) return;
        if (ResolverForGame(sg)?.Tokenize(localPath) is not { } template) return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (await _api().TrySetSaveTemplateAsync(sg.Id, template, key))
                    AgentLogger.Log($"Described '{sg.Name}' save location ({key}) for the fleet: {template}");
            }
            catch (Exception ex) { AgentLogger.LogException("CommandPoller.ReportTemplate", ex); }
        });
    }

    /// <summary>Best-effort: tell the server what save path this machine resolved for one of a game's folders.</summary>
    private void ReportPathAsync(Guid gameId, string path, string key = SaveRoot.PrimaryKey) =>
        _ = Task.Run(async () =>
        {
            try { await _api().SetMachinePathAsync(gameId, path, key); }
            catch (Exception ex) { AgentLogger.LogException("CommandPoller.ReportPath", ex); }
        });

    // ----- conflict notification (Phase 9) -----

    /// <summary>
    /// Only conflicts this machine is actually a party to — the same "no bystander case" filter
    /// <c>doctor</c> and <c>savelocker conflicts</c> already apply (Doctor.cs, AgentCli.cs), so all
    /// three agree on what counts. Wrapped in its own try/catch, separate from the outer tick's:
    /// a failure here must never stop the other steps a tick already ran, and never surface as
    /// anything worse than a logged line — this whole path is best-effort by design.
    /// </summary>
    private async Task CheckConflictsAsync()
    {
        try
        {
            var conflicts = (await _api().GetOpenConflictsAsync())
                .Where(c => c.MachineId == _config.MachineId)
                .ToList();
            _notices!.ObserveConflicts(
                conflicts,
                gameId => _config.Games.FirstOrDefault(g => g.GameId == gameId)?.Name ?? "A tracked game");
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("CommandPoller.CheckConflicts", ex);
        }
    }

    // ----- command execution -----

    private async Task RunCommandsAsync()
    {
        var commands = await _api().GetAgentCommandsAsync();
        foreach (var cmd in commands)
        {
            string result;
            try
            {
                result = await ExecuteAsync(cmd);
            }
            catch (Exception ex)
            {
                await SafeReportFailure(cmd.Id, cmd.ClaimToken, ex.Message);
                _notify($"{cmd.Type} (from dashboard) failed: {ex.Message}");
                continue;
            }

            // Separate from the execution try/catch on purpose: the work is already done, so a
            // report that cannot be delivered must not be turned into "the command failed". The
            // server reclaims it when the lease expires and it runs again harmlessly.
            try { await _api().ReportCommandAsync(cmd.Id, CommandStatus.Done, result, cmd.ClaimToken); }
            catch (Exception ex) { AgentLogger.LogException("CommandPoller.ReportSuccess", ex); }
            _notify(result);
        }
    }

    private async Task<string> ExecuteAsync(AgentCommandDto cmd)
    {
        if (cmd.Type == AgentCommandType.Scan)
        {
            var candidates = await _scanner.ScanAsync();
            return $"found {candidates.Count} candidate(s): " +
                   string.Join(", ", candidates.Take(8).Select(c => c.Name)) +
                   (candidates.Count > 8 ? "…" : "");
        }

        // pull / push / sync target one game or all of this machine's games.
        var targets = TargetGames(cmd.GameId).ToList();
        if (targets.Count == 0)
            return "no matching mapped game on this machine.";

        var engine = _engine();
        var running = new List<string>();
        var unreachable = new List<string>();
        var conflicted = new List<string>();
        foreach (var g in targets)
        {
            // A dashboard force-pull is the most dangerous surface there is: the person clicking it
            // is not sitting at the machine and cannot see that the game is open. Refusing here and
            // saying so in the command result is what puts the reason in front of them — the engine's
            // own refusal only reaches the agent's event stream. WA-01.
            var active = GameActivity.IsActive(g, out var proc);
            if (active) running.Add(proc is null ? g.Name : $"{g.Name} ({proc}.exe)");

            switch (cmd.Type)
            {
                case AgentCommandType.Pull:
                    if (!active && await engine.PullAsync(g, cmd.Force) is PullOutcome.Unreachable)
                        unreachable.Add(g.Name);
                    break;
                case AgentCommandType.Push:
                    if (await engine.PushAsync(g, cmd.Force) is { Status: UploadStatus.Conflict })
                        conflicted.Add(g.Name);
                    break;
                case AgentCommandType.Sync:
                    if (!active && await engine.PullAsync(g, cmd.Force) is PullOutcome.Unreachable)
                        unreachable.Add(g.Name);
                    // A push answered with a conflict uploaded nothing; without this the command
                    // would report Done with a "pushed — latest save …" line, which is the opposite
                    // of what happened to the person who clicked it.
                    if (await engine.PushAsync(g, cmd.Force) is { Status: UploadStatus.Conflict })
                        conflicted.Add(g.Name);
                    break;
            }
        }

        // Thrown, so the command is reported Failed with this as its reason. The pull no longer throws
        // by itself, and answering "pulled" for a download that never arrived (a gateway timeout in
        // front of the server is the realistic case — the command itself just came from that server)
        // would tell the person at the dashboard the opposite of what happened.
        if (unreachable.Count > 0)
            throw new HttpRequestException(
                $"the server did not answer — nothing was pulled for {string.Join(", ", unreachable)}.");

        // A conflict is a refusal, not a push. Reported as the reason so the dashboard's command row
        // says Failed with the one thing the person needs to know, instead of "pushed" over a save
        // that never left the machine.
        if (conflicted.Count > 0)
            throw new InvalidOperationException(
                $"the save diverged from the server — nothing was uploaded for {string.Join(", ", conflicted)}. " +
                "Resolve the conflict, then push again.");

        if (running.Count > 0 && cmd.Type == AgentCommandType.Pull)
            return $"REFUSED — still running: {string.Join(", ", running)}. " +
                   "Restoring saves under a live game loses them; close it and re-issue the pull.";

        // One concise summary (the per-step engine progress goes to the log, not toasts).
        // For a single game, include the save's timestamp so the user can confirm it's current.
        var verb = cmd.Type.ToString().ToLowerInvariant() + "ed";
        var suffix = running.Count > 0
            ? $" (not pulled, still running: {string.Join(", ", running)})"
            : "";
        if (targets.Count == 1)
        {
            var save = LatestSaveTimestamp(targets[0].SaveDirectory);
            return save is { } d
                ? $"{targets[0].Name} {verb} — latest save {d:MMM d, h:mm tt}{suffix}"
                : $"{targets[0].Name} {verb}.{suffix}";
        }
        return $"{verb} {targets.Count} games.{suffix}";
    }

    /// <summary>Newest last-write time among a game's save files, or null if none/unreadable.</summary>
    private static DateTime? LatestSaveTimestamp(string dir)
    {
        try
        {
            DateTime? newest = null;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var t = File.GetLastWriteTime(f);
                if (newest is null || t > newest) newest = t;
            }
            return newest;
        }
        catch { return null; }
    }

    /// <summary>Mapped games matching the command's target (skips unmapped ones).</summary>
    private IEnumerable<TrackedGame> TargetGames(Guid? gameId) =>
        _config.Games.Where(g =>
            !string.IsNullOrEmpty(g.SaveDirectory) &&
            (gameId is null || g.GameId == gameId));

    /// <summary>
    /// Report a failure without letting the report itself become the failure. If this cannot reach
    /// the server the command keeps its claim until the server's visibility lease expires, and is
    /// then handed out again — the server treats delivery as at-least-once, so an unacknowledged
    /// command comes back rather than being lost. Re-running any command type is safe (see
    /// <c>SyncService.DequeueCommandsAsync</c>).
    /// </summary>
    private async Task SafeReportFailure(Guid commandId, Guid? claimToken, string message)
    {
        try { await _api().ReportCommandAsync(commandId, CommandStatus.Failed, message, claimToken); }
        catch (Exception ex) { AgentLogger.LogException("CommandPoller.SafeReportFailure", ex); }
    }

    public void Dispose() => _timer.Dispose();

    /// <summary>
    /// Stops the timer and waits out a tick already in flight, so a caller that disposes objects
    /// this poller's tick uses (e.g. the host's notification presenter) never races that tick still using
    /// them after this returns.
    /// </summary>
    public async Task StopAsync()
    {
        _timer.Stop();
        await _lastTick;
        await _handover.StopAsync();
    }
}
