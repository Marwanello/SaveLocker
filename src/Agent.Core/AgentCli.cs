using System.Text.Json;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// The agent's one-shot command surface, shared by the Windows and Linux hosts. Everything here
/// is platform-neutral; the pieces that are not (game discovery) arrive as <see cref="IGameScanner"/>.
/// Host-specific commands (the Windows tray, the Linux daemon / run wrapper / doctor) stay in
/// their own Program and delegate the rest here, so the two never drift.
/// </summary>
public static class AgentCli
{
    /// <summary>Commands handled here. A host checks this before falling through to its own.</summary>
    public static bool Handles(string command) => command is
        "register" or "enroll" or "trust" or "set-server" or "whoami" or "search" or "scan" or
        "resolve" or "add-game" or "remove-game" or "list" or "status" or "push" or "pull" or
        "conflicts" or "resolve-conflict" or "emptied-hold" or "add-path" or "remove-path" or
        "refresh-manifest" or "log" or "hash" or "check-update" or "dlc" or "playthrough";

    /// <summary>Run one command. Returns the process exit code.</summary>
    public static async Task<int> RunAsync(
        string command,
        Dictionary<string, string> opts,
        List<string> positionals,
        AgentConfig config,
        IGameScanner scanner)
    {
        ApiClient Api() => ApiClient.For(config);
        void Log(string msg) { Console.WriteLine(msg); AgentLogger.Log(msg); }

        // A one-shot `push`/`pull` can hit a conflict or a blocked pull just as the daemon can, and
        // on a headless box the console is the only place anyone would ever see it. The command
        // flushes the reporter before it exits (see below) — there is no poller in this process.
        var health = HealthReporter.For(config);

        // Share the durable queue too. A one-shot push that fails because the server is down used to
        // drop the save on the floor: nothing retried it, and on a headless box nobody saw the error
        // scroll past. Queuing it means the daemon's drainer picks it up on the next pass — the same
        // treatment the launch wrapper already gets.
        var offlineQueue = OfflineQueue.For(config);
        SyncEngine Engine() => new(config, Api(), Log, offlineQueue: offlineQueue, health: health);
        var detection = new Detection(config);

        try
        {
            switch (command)
            {
                case "register":
                {
                    var name = opts.GetValueOrDefault("name") ?? config.MachineName;
                    var adminPassword = opts.GetValueOrDefault("admin-password");
                    var reg = await Api().RegisterAsync(name, adminPassword);
                    config.MachineName = name;
                    config.ApiKey = reg.ApiKey;
                    config.MachineId = reg.MachineId;
                    config.Save();
                    Console.WriteLine($"Registered '{name}' (id {reg.MachineId}).");
                    Console.WriteLine($"API key:  {reg.ApiKey}");
                    Console.WriteLine($"Saved to: {config.ConfigPath}");
                    Console.WriteLine("Paste the API key into the dashboard to view this machine's saves.");
                    Console.WriteLine("(Re-registering this name later rotates the key; retrieve it anytime with 'whoami'.)");
                    break;
                }

                case "enroll":
                {
                    // The join link is the whole credential now (tasks/enrollment-options, option B).
                    // A bare code plus the server address is accepted too — it is the same thing,
                    // assembled — so a person reading a code off the console never has to retype a URL.
                    var url = opts.GetValueOrDefault("url") ?? positionals.FirstOrDefault();
                    var code = opts.GetValueOrDefault("code");
                    var serverOpt = opts.GetValueOrDefault("server");
                    string joinUrl;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        joinUrl = url.Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(code))
                    {
                        var normalized = JoinCodes.NormalizeJoinCode(code)
                            ?? throw new ArgumentException($"'{code}' does not look like a join code (8 letters and digits).");
                        var serverUrl = serverOpt
                            ?? (string.IsNullOrWhiteSpace(config.ServerUrl) ? null : config.ServerUrl);
                        if (string.IsNullOrWhiteSpace(serverUrl))
                            throw new ArgumentException(
                                "Give the server too, e.g. enroll --code K7QM-4H2X --server http://192.168.1.10:5080 " +
                                "(or pass the whole join link with --url).");
                        joinUrl = serverUrl.TrimEnd('/') + "/join/" + normalized;
                    }
                    else
                    {
                        throw new ArgumentException(
                            "Pass the join link, e.g. enroll --url http://192.168.1.10:5080/join/K7QM-4H2X " +
                            "(or --code K7QM-4H2X --server http://192.168.1.10:5080).");
                    }

                    var policy = await AgentEnroller.FetchPolicyAsync(joinUrl);
                    var outcome = await AgentEnroller.ApplyAsync(
                        config, policy, detection, opts.GetValueOrDefault("name"), Log);

                    Console.WriteLine(outcome.Mapped + outcome.Unmapped > 0
                        ? $"Pre-seeded {outcome.Mapped + outcome.Unmapped} game(s): {outcome.Mapped} mapped to a local save folder, {outcome.Unmapped} still need one."
                        : "No games in the policy — the agent will adopt whatever the server has on its next poll.");
                    Console.WriteLine($"Config: {config.ConfigPath}");
                    Console.WriteLine("Next:   doctor    (then: daemon)");
                    return 0;
                }

                case "trust":
                {
                    // The pin is recorded at enrollment; this is how a user inspects it, and how they
                    // accept a new key a renewal does not explain — a private certificate they replaced.
                    // A publicly-logged renewal re-pins by itself (ServerTrust.Judge).
                    if (!ServerTrust.IsPinnable(config.ServerUrl))
                    {
                        Console.WriteLine($"Server {config.ServerUrl} is plain http — there is no TLS identity to pin.");
                        break;
                    }

                    if (!opts.ContainsKey("accept"))
                    {
                        Console.WriteLine($"Server:     {config.ServerUrl}");
                        Console.WriteLine($"Pinned key: {config.ServerPin ?? "(not pinned — enroll, or run: trust --accept)"}");
                        Console.WriteLine($"Trust:      {ServerTrust.Describe(config)}");
                        break;
                    }

                    // Contact the server to observe its current identity, then pin whatever answered.
                    var probe = new ApiClient(config.ServerUrl, config.ApiKey);
                    await probe.GetAdminStatusAsync();
                    if (probe.ObservedPin is not { } freshPin)
                    {
                        Console.Error.WriteLine("Could not read the server's TLS certificate; nothing pinned.");
                        return 1;
                    }
                    var was = config.ServerPin;
                    config.SetPin(probe.Observed);
                    config.Save();
                    Console.WriteLine(was is null
                        ? $"Pinned {config.ServerUrl}: {freshPin}"
                        : $"Re-pinned {config.ServerUrl}\n  was: {was}\n  now: {freshPin}");
                    Console.WriteLine($"Trust: {ServerTrust.Describe(config)}");
                    break;
                }

                case "set-server":
                {
                    var url = opts.GetValueOrDefault("url") ?? positionals.FirstOrDefault()
                              ?? throw new ArgumentException("Pass the server URL, e.g. set-server --url https://lgs.example.com");

                    // Validated before it can reach disk: an unusable value here used to persist and
                    // then crash every subsequent start when the client was constructed. WA-04.
                    if (!config.TrySetServerUrl(url, out var cleared))
                    {
                        Console.Error.WriteLine(ServerOrigin.InvalidUrlMessage);
                        return 1;
                    }
                    config.Save();
                    Console.WriteLine($"Server URL set to {config.ServerUrl}");
                    if (cleared)
                        Console.WriteLine(
                            "This is a different server, so the stored machine key, machine id and " +
                            "TLS pin were cleared — they were issued by the previous one. " +
                            "Register or enroll against the new server before syncing.");
                    break;
                }

                case "whoami":
                {
                    // Local-only: print the stored identity + key for this config.
                    Console.WriteLine($"Machine:   {config.MachineName}");
                    Console.WriteLine($"MachineId: {(config.MachineId?.ToString() ?? "(not registered)")}");
                    Console.WriteLine($"Server:    {config.ServerUrl}");
                    Console.WriteLine($"API key:   {(config.ApiKey ?? "(not registered)")}");
                    Console.WriteLine($"Config:    {config.ConfigPath}");
                    break;
                }

                case "search":
                {
                    var term = positionals.FirstOrDefault() ?? "";
                    foreach (var n in await detection.SearchAsync(term))
                        Console.WriteLine("  " + n);
                    break;
                }

                case "scan":
                {
                    // Local-only: discover enrollment candidates on this machine.
                    var hideCloud = opts.ContainsKey("no-cloud");
                    var found = await scanner.ScanAsync();
                    // EmuDeck's untouched preinstalled saves are not the user's games; only Add games offers
                    // one, and only to join a server game that keeps it.
                    var seeds = found.Count(c => c.UntouchedSeed);
                    var candidates = found
                        .Where(c => !c.UntouchedSeed && (!hideCloud || !c.HasSteamCloud))
                        .ToList();
                    if (seeds > 0)
                        Console.WriteLine($"({seeds} of EmuDeck's preinstalled saves, never played here, not listed.)");
                    if (candidates.Count == 0)
                    {
                        Console.WriteLine("No games discovered.");
                        break;
                    }
                    Console.WriteLine($"Discovered {candidates.Count} candidate(s):");
                    foreach (var c in candidates)
                    {
                        var save = c.SuggestedSaveDir ?? "(save dir unknown — pass --dir on add-game)";
                        var cloud = c.HasSteamCloud ? " [Steam Cloud]" : "";
                        var appid = c.SteamAppId is null ? "" : $" appid={c.SteamAppId}";
                        Console.WriteLine($"  {c.Name}  <{c.Source}>{cloud}{appid}");
                        Console.WriteLine($"      save: {save}");
                        if (c.NotSyncable is { } why) Console.WriteLine($"      cannot be added: {why}");
                        foreach (var also in c.AlternateSaveDirs ?? Array.Empty<DeclaredSavePath>())
                            Console.WriteLine($"      also found: {also.Dir}  (once enrolled: add-path \"{c.Name}\" --key {also.Key} --dir \"{also.Dir}\")");
                    }

                    await OfferUnmappedPathsAsync(candidates, config, opts);
                    break;
                }

                case "resolve":
                {
                    // Local-only: resolve a manifest game's save dirs on this machine.
                    var key = opts.GetValueOrDefault("manifest")
                              ?? positionals.FirstOrDefault()
                              ?? throw new ArgumentException("Pass a game name or --manifest.");
                    var dirs = await detection.ResolveSaveDirectoriesAsync(key, ResolverFor(opts));
                    if (dirs.Count == 0) Console.WriteLine("(no existing save directory found)");
                    foreach (var d in dirs) Console.WriteLine("Resolved: " + d);
                    break;
                }

                case "add-game":
                {
                    var name = opts.GetValueOrDefault("name")
                               ?? throw new ArgumentException("--name is required.");
                    var manifestKey = opts.GetValueOrDefault("manifest");
                    var dir = opts.GetValueOrDefault("dir");

                    if (string.IsNullOrEmpty(dir))
                    {
                        var resolved = await detection.ResolveSaveDirectoriesAsync(
                            manifestKey ?? name, ResolverFor(opts));
                        dir = resolved.FirstOrDefault()
                              ?? throw new InvalidOperationException(
                                  "Could not auto-detect a save directory; pass --dir.");
                        Console.WriteLine($"Auto-detected save directory: {dir}");
                    }
                    else if (!Directory.Exists(dir))
                    {
                        Console.WriteLine($"Warning: save directory does not exist — run the game first or verify the path.");
                    }

                    // Refused before the game is created on the server, so a bad --dir does not leave
                    // a half-made game behind. No --force-path here: these refusals are absolute. WA-02.
                    var dirCheck = SavePathGuard.Check(dir, config.StateDir);
                    if (!dirCheck.Ok)
                    {
                        Console.Error.WriteLine($"Refusing '{dir}': {dirCheck.Reason}");
                        return 1;
                    }

                    // The heuristics can be wrong, so they are overridable — but only by saying so.
                    var sanity = SaveDirSanity.Inspect(dirCheck.Canonical);
                    if (sanity.Count > 0 && !opts.ContainsKey("force-path"))
                    {
                        Console.Error.WriteLine($"'{dir}' does not look like a save folder:");
                        foreach (var p in sanity) Console.Error.WriteLine("  - " + p);
                        Console.Error.WriteLine("Pass --force-path to use it anyway.");
                        return 1;
                    }
                    dir = dirCheck.Canonical;

                    // Canonicalised for the same reason Enroller does it: the server-side game name
                    // is the identity every machine matches on, and the server compares names
                    // case-insensitively but not past punctuation. Without this, `add-game` on a
                    // Deck and the UI on a PC create two games for one title whenever the two
                    // spellings differ by so much as a hyphen. Unknown to the manifest → unchanged.
                    manifestKey = await detection.CanonicalNameAsync(manifestKey ?? name) ?? manifestKey;
                    var serverName = manifestKey ?? name;

                    var game = await Api().CreateGameAsync(new CreateGameRequest(serverName, manifestKey, null));
                    var existing = config.FindGame(name) ?? config.FindGame(serverName);
                    // HasSteamCloud is deliberately left alone: null on a fresh entry ("nobody
                    // established this"), and untouched on an existing one so re-adding a game does
                    // not erase what enrollment already decided. `--appid` cannot stand in for it —
                    // a non-Steam shortcut run under Proton has an AppID too. See TrackedGame.
                    var tracked = existing ?? new TrackedGame();
                    tracked.GameId = game.Id;
                    tracked.Name = game.Name;
                    tracked.ManifestKey = manifestKey;
                    tracked.SaveDirectory = dir!;
                    tracked.Source ??= GameSources.Manual(GameSources.AddedFromCli);
                    if (opts.TryGetValue("appid", out var appId) && !string.IsNullOrWhiteSpace(appId))
                        tracked.SteamAppId = appId.Trim();
                    if (opts.TryGetValue("proc", out var proc) && !string.IsNullOrWhiteSpace(proc))
                        // Normalised the same way the local API does: the watcher matches on
                        // Process.ProcessName, which is neither a path nor carries an extension, so
                        // "C:\Games\Foo\foo.exe" stored verbatim would simply never match. WA-08.
                        tracked.ProcessNames = proc
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(GameActivity.ProcessNameFromExe)
                            .Where(p => p is not null)
                            .Select(p => p!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    // Clears any per-machine opt-out as well as adding the entry — explicitly adding
                    // a game back is the one action that means "track this here again".
                    config.SetTracked(game.Id, tracked: true, entry: tracked);
                    // SetTracked only ADDS a not-yet-present entry — for a brand new game that already
                    // durably persisted it, re-reading and re-writing fresh under its own lock. Calling
                    // Save() unconditionally afterward re-opens a lost-update window: Save() writes
                    // THIS process's in-memory Games, which (for a freshly started CLI process) reflects
                    // only what was on disk when it loaded plus its own addition — not any sibling
                    // process's concurrent add that SetTracked's fresh read already captured on disk.
                    // A re-added EXISTING game is different: SetTracked's add-branch no-ops because the
                    // GameId is already present, so the field updates above (SaveDirectory, ManifestKey,
                    // ProcessNames) live only on `tracked` in memory until Save() writes them.
                    if (existing is not null) config.Save();
                    Console.WriteLine($"Tracking '{name}' -> {dir}");
                    break;
                }

                case "remove-game":
                {
                    var name = opts.GetValueOrDefault("name")
                               ?? throw new ArgumentException("--name is required.");
                    var target = config.FindGame(name)
                                 ?? throw new InvalidOperationException($"'{name}' is not tracked here.");

                    // Per-machine opt-out, not a plain delete: the game stays on the server for the
                    // rest of the fleet, and the opt-out is what stops a running daemon's reconcile
                    // adopting it straight back.
                    config.SetTracked(target.GameId, tracked: false);
                    Console.WriteLine($"No longer tracking '{target.Name}' on this machine. It is still on the server.");
                    break;
                }

                case "add-path":
                    return await AddPathAsync(opts, positionals, config, Api(), Engine());

                case "remove-path":
                {
                    var game = GameArg(opts, positionals, config, "remove-path SyncGame --key states");
                    var key = (opts.GetValueOrDefault("key") ?? "").Trim();
                    if (game.ExtraPaths.All(p => p.Key != key))
                        throw new InvalidOperationException($"'{game.Name}' has no save folder called '{key}' here.");

                    var removed = await SavePathEditor.RemoveAsync(config, Api(), (g, k) => Engine().ForgetSavePathAsync(g, k), game, key);
                    if (!removed.Ok)
                    {
                        Console.Error.WriteLine(removed.Error);
                        return 1;
                    }
                    Console.WriteLine(removed.Deferred
                        ? $"Removed on the server. '{game.Name}' is busy here, so this machine drops the '{key}' " +
                          "folder on the agent's next poll."
                        : $"'{game.Name}' no longer syncs its '{key}' save folder, on any machine." +
                          (removed.Directory is null ? "" : $" Its files stay where they are ({removed.Directory})."));
                    break;
                }

                case "list":
                    foreach (var g in config.Games)
                    {
                        Console.WriteLine($"  {g.Name}  [{g.SaveDirectory}]  procs={string.Join(",", g.ProcessNames)}");
                        foreach (var p in g.ExtraPaths)
                            Console.WriteLine($"      {p.Key}: {p.Directory ?? "(not mapped here)"}");
                    }
                    break;

                case "status":
                {
                    var api = Api();
                    var suggestions = new FolderSuggestions(config, detection);
                    foreach (var g in config.Games)
                    {
                        var s = await api.GetStateAsync(g.GameId);
                        var head = s?.Head is null
                            ? "(none)"
                            : $"{s.Head.Id:N}".Substring(0, 8) + $" from {s.Head.MachineName}";
                        var lease = s?.Lease?.HolderMachineName is { } h ? $"leased by {h}" : "free";
                        var conflict = s?.HasOpenConflict == true ? "  *** CONFLICT ***" : "";
                        Console.WriteLine($"  {g.Name}: head={head}, {lease}{conflict}");
                        if (g.ExtraPaths.Count > 0)
                        {
                            Console.WriteLine($"      main: {(g.IsEnrolledHere ? g.SaveDirectory : "(not mapped here)")}");
                            foreach (var line in FolderLines(g, config.StateDir)) Console.WriteLine("      " + line);
                        }
                        // The manifest's other locations that exist here, never synced until someone says so.
                        foreach (var also in await suggestions.ForGameAsync(g))
                            Console.WriteLine($"      also found: {also.Directory}  " +
                                              $"(add-path \"{g.Name}\" --key {also.Key} --dir \"{also.Directory}\")");
                    }
                    break;
                }

                case "conflicts":
                {
                    // Only conflicts this machine is actually a party to — a genuine "my local save
                    // vs. the cloud" divergence, not another machine's. There is no bystander case
                    // (tasks/conflict-resolution-ui/plan.md decision 2).
                    var open = (await Api().GetOpenConflictsAsync())
                        .Where(c => c.MachineId == config.MachineId)
                        .ToList();
                    if (open.Count == 0)
                    {
                        Console.WriteLine("No open conflicts on this machine.");
                        break;
                    }
                    foreach (var c in open)
                    {
                        var name = config.Games.FirstOrDefault(g => g.GameId == c.GameId)?.Name
                                   ?? c.GameId.ToString();
                        Console.WriteLine($"  {name}");
                        Console.WriteLine($"    local (this machine's save):   {c.VersionBId}");
                        Console.WriteLine($"    cloud (the server's save):     {c.VersionAId}");
                        Console.WriteLine($"    diverged {c.Count} time(s), last {c.LastSeen:u}" +
                                           (c.Escalated ? "  — escalated" : ""));
                        Console.WriteLine($"    resolve-conflict \"{name}\" --keep local|cloud [--keep-both]");
                    }
                    break;
                }

                case "emptied-hold":
                {
                    // A game held because every save file vanished here (tasks/empty-save-hold): list them, or answer one.
                    var holds = EmptyFolderHoldStore.For(config);
                    var name = opts.GetValueOrDefault("game") ?? positionals.FirstOrDefault();
                    var delete = opts.ContainsKey("delete-everywhere");
                    var restore = opts.ContainsKey("restore-here");
                    if (name is null)
                    {
                        var held = holds.Read();
                        if (held.Count == 0) Console.WriteLine("No game is held for an emptied save folder.");
                        foreach (var h in held)
                            Console.WriteLine($"  {config.Games.FirstOrDefault(g => g.GameId == h.GameId)?.Name ?? h.GameId.ToString()}" +
                                              $" — held since {h.HeldAt:u}");
                        if (held.Count > 0)
                            Console.WriteLine("  Answer one: emptied-hold <game> --delete-everywhere | --restore-here");
                        break;
                    }
                    var game = config.FindGame(name)
                               ?? throw new InvalidOperationException($"'{name}' is not tracked here.");
                    if (delete == restore)
                        throw new ArgumentException("Pass exactly one of --delete-everywhere or --restore-here.");
                    var (ok, message) = await Engine().AnswerEmptiedAsync(game, delete);
                    await health.SendAsync(Api(), config, null, Log);
                    if (!ok)
                    {
                        Console.Error.WriteLine(message);
                        return 1;
                    }
                    Console.WriteLine(message);
                    break;
                }

                case "resolve-conflict":
                {
                    var name = opts.GetValueOrDefault("game") ?? positionals.FirstOrDefault()
                               ?? throw new ArgumentException(
                                   "Pass a game name and --keep local|cloud, " +
                                   "e.g. resolve-conflict SyncGame --keep local");
                    var game = config.FindGame(name)
                               ?? throw new InvalidOperationException($"'{name}' is not tracked here.");

                    var keep = (opts.GetValueOrDefault("keep") ?? "").Trim().ToLowerInvariant();
                    if (keep is not ("local" or "cloud"))
                        throw new ArgumentException("Pass --keep local or --keep cloud.");
                    var keepBoth = opts.ContainsKey("keep-both");

                    var conflict = (await Api().GetOpenConflictsAsync())
                        .FirstOrDefault(c => c.GameId == game.GameId && c.MachineId == config.MachineId)
                        ?? throw new InvalidOperationException(
                            $"No open conflict for '{game.Name}' on this machine.");

                    var winningVersionId = keep == "local" ? conflict.VersionBId : conflict.VersionAId;
                    var (ok, message) = await Engine().ResolveOwnConflictAsync(conflict, winningVersionId, keepBoth);
                    if (!ok)
                    {
                        Console.Error.WriteLine($"Could not resolve: {message}");
                        return 1;
                    }
                    Console.WriteLine(message + (keepBoth ? " The other side is kept as a downloadable backup." : ""));
                    break;
                }

                case "hash":
                {
                    // The content hash is what conflict detection compares, so it must be identical
                    // for the same bytes on Windows and on Linux. Printing it is the only way to
                    // assert that from outside the process (see tests/cross-os).
                    if (opts.TryGetValue("dir", out var hashDir) && !string.IsNullOrWhiteSpace(hashDir))
                    {
                        var globs = opts.GetValueOrDefault("exclude")?
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        Console.WriteLine(SaveArchive.HashDirectory(hashDir.Trim(), globs));
                        break;
                    }

                    foreach (var g in GamesFor(positionals.FirstOrDefault(), config))
                        Console.WriteLine($"  {g.Name}: {g.LocalHash(config.StateDir)}");
                    break;
                }

                case "push":
                {
                    var engine = Engine();
                    var force = opts.ContainsKey("force");
                    foreach (var g in GamesFor(positionals.FirstOrDefault(), config))
                        await engine.PushAsync(g, force);
                    await health.SendAsync(Api(), config, null, Log);
                    break;
                }

                case "pull":
                {
                    var engine = Engine();
                    var force = opts.ContainsKey("force");
                    var unreachable = false;
                    foreach (var g in GamesFor(positionals.FirstOrDefault(), config))
                    {
                        if (string.IsNullOrWhiteSpace(g.SaveDirectory))
                        {
                            Console.Error.WriteLine(
                                $"'{g.Name}' has no save directory set. " +
                                $"Run: savelocker add-game --name \"{g.Name}\" --dir <path>");
                            continue;
                        }
                        if (GameActivity.IsActive(g, out var proc))
                        {
                            // Exit code stays 0: the other games in the loop may pull fine, and the
                            // engine refuses this one regardless. This is the readable reason.
                            Console.Error.WriteLine(
                                $"'{g.Name}' is running{(proc is null ? "" : $" ({proc})")} — pull refused. " +
                                "Restoring under a live game loses the restored save. Close it first.");
                            continue;
                        }
                        unreachable |= await engine.PullAsync(g, force) is PullOutcome.Unreachable;
                    }
                    await health.SendAsync(Api(), config, null, Log);
                    // A pull that never reached the server is a failed command, as it was when the
                    // engine threw: a script must not read exit 0 as "this machine has the latest save".
                    if (unreachable) return 1;
                    break;
                }

                case "dlc":
                    return await DlcAsync(positionals, config, Api());

                case "playthrough":
                    return await PlaythroughAsync(opts, positionals, config, Api(), Engine());

                case "check-update":
                {
                    // Ask the configured server what it is offering, and optionally fetch and VERIFY
                    // the artifact — never run it. Launching is the tray's job and needs a user's
                    // consent; this exists so the download-and-verify path is inspectable from
                    // outside the process, and so a headless box has any way at all to see what its
                    // server is publishing.
                    using var checker = new UpdateChecker(config);
                    var result = await checker.CheckAsync();

                    switch (result)
                    {
                        case UpdateResult.UpToDate:
                            Console.WriteLine($"Up to date (v{UpdateChecker.CurrentVersionText}).");
                            break;
                        case UpdateResult.Skipped:
                            Console.WriteLine($"v{config.SkipVersion} is available but was skipped.");
                            break;
                        case UpdateResult.Failed f:
                            Console.Error.WriteLine($"Update check failed: {f.Reason}");
                            return 1;
                        case UpdateResult.Available a:
                            Console.WriteLine($"v{a.Version} is available (current {UpdateChecker.CurrentVersionText}).");
                            Console.WriteLine($"  url:    {a.DownloadUrl}");
                            Console.WriteLine($"  sha256: {a.Sha256 ?? "(none published — an off-origin download will be refused)"}");

                            if (!opts.ContainsKey("download")) break;
                            try
                            {
                                var file = await checker.DownloadInstallerAsync(a.Version, a.DownloadUrl, a.Sha256);
                                Console.WriteLine($"VERIFIED: {file}");
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"REFUSED: {ex.Message}");
                                return 1;
                            }
                            break;
                    }
                    break;
                }

                case "refresh-manifest":
                {
                    var m = await detection.GetManifestAsync(forceRefresh: true);
                    Console.WriteLine($"Manifest refreshed: {m.GameCount} games.");
                    break;
                }

                case "log":
                {
                    var n = opts.TryGetValue("n", out var ns) && int.TryParse(ns, out var ni) ? ni : 50;
                    if (!File.Exists(AgentLogger.LogPath))
                    {
                        Console.WriteLine("(no log file yet)");
                        break;
                    }
                    var lines = File.ReadAllLines(AgentLogger.LogPath);
                    foreach (var l in lines.TakeLast(n))
                        Console.WriteLine(l);
                    break;
                }

                default:
                    Console.Error.WriteLine($"Unknown command '{command}'.");
                    return 2;
            }
            return 0;
        }
        catch (AgentStateLockException ex)
        {
            // Contention is an expected outcome, not a bug: the tray or the launch wrapper is
            // legitimately mid-sync. A stack trace here would read as a crash, when in fact nothing
            // was changed and retrying in a moment is the right answer. WA-07.
            Console.Error.WriteLine($"Busy: {ex.Message}");
            AgentLogger.Log($"CLI '{command}' skipped: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            var msg = $"Error: {ex}";
            Console.Error.WriteLine(msg);
            AgentLogger.Log(msg);
            return 1;
        }
    }

    /// <summary>
    /// A game's manifest paths resolve inside its Wine prefix when one is given
    /// (<c>--prefix &lt;compatdata/appid&gt;</c>, or any other Wine prefix — Heroic manages its own);
    /// otherwise against this host.
    /// </summary>
    /// <remarks>
    /// <c>--install-dir</c> supplies the manifest's <c>&lt;base&gt;</c>. It is optional but worth
    /// passing: more manifest save paths are written in terms of <c>&lt;base&gt;</c> than of every
    /// other placeholder combined, so omitting it is the difference between resolving a game and
    /// being told to pass <c>--dir</c> by hand. <c>&lt;root&gt;</c> is derived from the prefix path
    /// and <c>&lt;storeUserId&gt;</c> is discovered on disk, so neither needs an option.
    /// </remarks>
    private static PathResolver? ResolverFor(Dictionary<string, string> opts)
    {
        var installDir = opts.GetValueOrDefault("install-dir");

        if (opts.TryGetValue("prefix", out var prefix) && !string.IsNullOrWhiteSpace(prefix))
        {
            var prefixRoot = prefix.Trim();
            // <root> stays null for anything that is not compatdata-shaped — RootFromCompatData
            // verifies rather than assumes, so a Heroic prefix leaves <root> unresolved instead of
            // claiming its grandparent is a Steam library.
            var storeRoot = SteamLayout.RootFromCompatData(prefixRoot);

            // Reading the layout off disk covers Heroic, whose prefixes nest `pfx` only for a Proton
            // runner and whose Wine user is the Linux username otherwise. Proton() remains the
            // fallback for a prefix that does not exist yet — the pre-existing behaviour, and the
            // right one for a Steam game that has not been launched.
            return WinePrefix.ResolverFor(prefixRoot, installDir, storeRoot)
                   ?? PathResolver.Proton(prefixRoot, installDir, storeRoot);
        }

        if (!OperatingSystem.IsWindows()) return null;
        return PathResolver.Windows(installDir);
    }

    /// <summary>
    /// After a scan, offer to map any tracked game that has no save folder but whose name matches a
    /// candidate the scan found. This is the common case on a Deck, where the alternative is typing
    /// <c>add-game --name "…" --dir /home/deck/.local/share/Steam/steamapps/compatdata/…</c> into an
    /// on-screen keyboard.
    /// </summary>
    /// <remarks>
    /// Prompting is <b>skipped entirely</b> when stdin is not a terminal. `scan` runs from the test
    /// harness, from a dashboard-issued command and under systemd, and a prompt there would either
    /// read EOF and loop or block the unit forever. <c>--yes</c> applies without asking;
    /// <c>--no-prompt</c> only lists.
    /// </remarks>
    private static async Task OfferUnmappedPathsAsync(
        IReadOnlyList<ScanCandidate> candidates, AgentConfig config, Dictionary<string, string> opts)
    {
        if (opts.ContainsKey("no-prompt")) return;

        var assumeYes = opts.ContainsKey("yes");
        var matches = new List<(TrackedGame Game, string Dir)>();

        foreach (var game in config.Games.Where(g => string.IsNullOrWhiteSpace(g.SaveDirectory)))
        {
            var match = candidates.FirstOrDefault(c =>
                string.Equals(c.Name, game.Name, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.SuggestedSaveDir));
            if (match?.SuggestedSaveDir is { } dir && Directory.Exists(dir))
                matches.Add((game, Path.GetFullPath(dir)));
        }

        if (matches.Count == 0) return;

        // Non-interactive: say what was found so the path is at least copy-pasteable, then stop.
        if (!assumeYes && Console.IsInputRedirected)
        {
            Console.WriteLine();
            foreach (var (game, dir) in matches)
                Console.WriteLine($"Found a save path for '{game.Name}': {dir}");
            Console.WriteLine("Re-run with --yes to apply, or use the agent UI to confirm.");
            return;
        }

        var applied = 0;
        foreach (var (game, dir) in matches)
        {
            if (!assumeYes)
            {
                Console.WriteLine();
                Console.Write($"Found save path for '{game.Name}' — apply? [{dir}] (y/n) ");
                var answer = Console.ReadLine();
                // A closed stdin mid-loop reads null; treat it as "no" rather than spinning.
                if (answer is null) return;
                if (!answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)) continue;
            }

            game.SaveDirectory = dir;
            applied++;
            Console.WriteLine($"Tracking '{game.Name}' -> {dir}");
        }

        if (applied == 0) return;
        config.Save();

        // Best-effort, and after the local save: the mapping is useful on this machine even if the
        // server is unreachable, and reconcile will report it later anyway.
        foreach (var (game, dir) in matches.Where(m => m.Game.SaveDirectory == m.Dir))
        {
            try { await ApiClient.For(config).SetMachinePathAsync(game.GameId, dir); }
            catch (Exception ex) { AgentLogger.LogException("scan.SetMachinePath", ex); }
        }
    }

    /// <summary>
    /// <c>add-path &lt;game&gt; --key &lt;k&gt; --dir &lt;d&gt; [--include a,b] [--label L] [--keep local|cloud]</c>:
    /// give a game another save folder, for every machine (tasks/multiple-save-paths), or map one it
    /// already has to a folder on this machine. The folder is described to the fleet as a template
    /// when it can be; a folder no token describes is added without one, and other machines keep a
    /// shadow copy of it until someone sets their folder.
    /// </summary>
    private static async Task<int> AddPathAsync(Dictionary<string, string> opts, List<string> positionals,
        AgentConfig config, ApiClient api, SyncEngine engine)
    {
        var game = GameArg(opts, positionals, config,
            "add-path SyncGame --key states --dir <folder> [--include \"*.state\"] [--keep local|cloud]");
        var key = (opts.GetValueOrDefault("key") ?? "").Trim();
        if (SaveRoot.ValidateExtraKey(key) is { } badKey) throw new ArgumentException(badKey);
        var dir = opts.GetValueOrDefault("dir") ?? throw new ArgumentException("--dir is required.");
        KeepSide? keep = (opts.GetValueOrDefault("keep") ?? "").Trim().ToLowerInvariant() switch
        {
            "" => null,
            "local" => KeepSide.Local,
            "cloud" => KeepSide.Cloud,
            _ => throw new ArgumentException("Pass --keep local or --keep cloud."),
        };
        var include = (opts.GetValueOrDefault("include") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var glob in include)
            if (SaveArchive.ValidateIncludeGlob(glob) is { } badGlob) throw new ArgumentException(badGlob);

        var existing = game.ExtraPaths.FirstOrDefault(p => p.Key == key);
        if (existing is not null && include.Count > 0 && !include.SequenceEqual(existing.IncludeGlobs))
            Console.WriteLine($"Note: '{key}' already exists; its include patterns are set in the console, " +
                              "so --include is ignored.");

        var result = await SavePathEditor.AddAsync(config, api, (g, k, d, side) => engine.MapSavePathAsync(g, k, d, side), game, key, dir, include,
            opts.GetValueOrDefault("label"), keep, confirm: opts.ContainsKey("force-path"), resolver: ResolverFor(opts));
        if (result.NeedsConfirm)
        {
            Console.Error.WriteLine($"'{dir}' does not look like a save folder:");
            foreach (var p in result.Problems ?? Array.Empty<string>()) Console.Error.WriteLine("  - " + p);
            Console.Error.WriteLine("Pass --force-path to use it anyway.");
            return 1;
        }
        // A joined folder has the key another machine gave it, not the one asked for.
        key = result.Key ?? key;
        if (result.Joined)
            Console.WriteLine($"'{game.Name}' already syncs that folder as '{key}' (another machine added it); joined it.");
        if (result.Added)
            Console.WriteLine(result.Template is null
                ? $"Added save folder '{key}' to '{game.Name}'. No template describes {result.Directory}, so other " +
                  "machines keep a copy of it until their own folder is set."
                : $"Added save folder '{key}' to '{game.Name}' for every machine: {result.Template}");
        if (!result.Ok)
        {
            Console.Error.WriteLine(result.NeedsChoice
                ? result.Error + $"\nRe-run with --key {key} --keep local or --keep cloud."
                : result.Directory is null ? result.Error : $"Could not map '{key}': {result.Error}");
            return 1;
        }
        if (!result.Reported)
            Console.WriteLine("Note: could not tell the server yet; the agent does on its next poll.");
        Console.WriteLine($"'{game.Name}' save folder '{key}' -> {result.Directory}");
        return 0;
    }

    /// <summary>
    /// <c>playthrough list &lt;game&gt;</c>, <c>playthrough switch &lt;game&gt; &lt;name&gt;</c>, <c>playthrough new &lt;game&gt; &lt;name&gt;</c>
    /// (tasks/save-playthroughs Phase 6): the same actions as the game page's playthrough tabs. The exit code says whether the
    /// switch or the new playthrough happened.
    /// </summary>
    private static async Task<int> PlaythroughAsync(
        Dictionary<string, string> opts, List<string> positionals, AgentConfig config, ApiClient api, SyncEngine engine)
    {
        const string usage = "Usage: playthrough list <game> | playthrough switch <game> <name> | " +
                             "playthrough new <game> <name> [--from <playthrough>] [--from-version <id>] [--fresh [--keep <file,file>]] [--no-switch] | playthrough flush";
        var verb = positionals.FirstOrDefault() ?? throw new ArgumentException(usage);
        if (verb == "flush")
        {
            // What the daemon's drainer does on its tick, for a box with no daemon: saves kept at an offline switch go up, and
            // the server is told which playthrough this device is on.
            await engine.FlushPlaythroughWorkAsync();
            Console.WriteLine(engine.HasPlaythroughWork()
                ? "Some playthrough work is still waiting for the server."
                : "Nothing is waiting for the server.");
            return 0;
        }
        var gameName = positionals.Skip(1).FirstOrDefault() ?? throw new ArgumentException(usage);
        var game = config.FindGame(gameName) ?? throw new InvalidOperationException($"'{gameName}' is not tracked here.");
        List<PlaythroughDto>? list = null;
        var reachable = true;
        try { list = await api.ListPlaythroughsAsync(game.GameId); }
        catch (Exception ex) when (ServerReachability.IsUnreachable(ex)) { reachable = false; }
        if (list is not null) engine.RememberFollowed(game, list);

        PlaythroughDto Named(string name) =>
            list?.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"'{game.Name}' has no playthrough called '{name}'. See: playthrough list \"{game.Name}\"");

        switch (verb)
        {
            case "list":
            {
                if (list is null)
                {
                    Console.WriteLine(reachable
                        ? "This server does not have playthroughs yet."
                        : $"The server is not reachable. {game.Name} follows {game.PlaythroughName ?? "its default playthrough"}.");
                    foreach (var copy in PlaythroughStore.For(config).All(game.GameId))
                        Console.WriteLine($"    copy on this device: {copy.PlaythroughName ?? copy.PlaythroughId.ToString()}" +
                                          (copy.Uploaded ? "" : "  (not uploaded yet)"));
                    return reachable ? 1 : 0;
                }
                foreach (var p in list)
                {
                    var here = p.Id == game.PlaythroughId ? "*" : " ";
                    var latest = p.Head is { } h ? $"latest {h.CreatedAt:u} from {h.MachineName}" : "no save yet";
                    var others = p.Devices.Where(d => d.MachineId != config.MachineId).Select(d => d.MachineName).ToArray();
                    Console.WriteLine($"  {here} {p.Name}{(p.IsDefault ? " (default)" : "")}{(p.ArchivedAt is not null ? " [archived]" : "")}" +
                                      $" - {latest}, {p.VersionCount} version(s)" +
                                      (others.Length > 0 ? $", also on {string.Join(", ", others)}" : "") +
                                      (p.HasOpenConflict ? "  ** CONFLICT **" : ""));
                }
                Console.WriteLine("  (* = this device)");
                return 0;
            }
            case "switch":
            {
                var name = positionals.Skip(2).FirstOrDefault() ?? throw new ArgumentException(usage);
                var target = list is null
                    ? PlaythroughStore.For(config).All(game.GameId).FirstOrDefault(c => string.Equals(c.PlaythroughName, name, StringComparison.OrdinalIgnoreCase))?.PlaythroughId
                      ?? throw new InvalidOperationException("The server is not reachable and no copy of that playthrough is kept on this device.")
                    : Named(name).Id;
                var result = await engine.SwitchPlaythroughAsync(game, target);
                (result.Ok ? Console.Out : Console.Error).WriteLine(result.Message);
                return result.Ok ? 0 : 1;
            }
            case "new":
            {
                var name = positionals.Skip(2).FirstOrDefault() ?? throw new ArgumentException(usage);
                if (list is null) throw new InvalidOperationException("A new playthrough needs the server.");
                Guid? fromVersion = null;
                if (opts.TryGetValue("from-version", out var versionText))
                    fromVersion = Guid.TryParse(versionText, out var v) ? v : throw new ArgumentException("--from-version needs a version id.");
                Guid? from = opts.TryGetValue("from", out var fromName) ? Named(fromName).Id : null;
                var keep = opts.TryGetValue("keep", out var keepText)
                    ? keepText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : null;
                var result = await engine.NewPlaythroughAsync(game, name, from, opts.ContainsKey("fresh"), keep,
                    playHere: !opts.ContainsKey("no-switch"), fromVersionId: fromVersion);
                (result.Ok ? Console.Out : Console.Error).WriteLine(result.Message);
                return result.Ok ? 0 : 1;
            }
            default:
                throw new ArgumentException(usage);
        }
    }

    /// <summary>
    /// <c>dlc status [game]</c>, <c>dlc ask &lt;game&gt;</c>, <c>dlc send|decline &lt;request id&gt;</c> (tasks/emulator-saves Phase 5c):
    /// what the latest save needs against what is here, a hand-over request to the machine the save came from, and an
    /// answer to another machine's request. The daemon does the moving.
    /// </summary>
    private static async Task<int> DlcAsync(List<string> positionals, AgentConfig config, ApiClient api)
    {
        var verb = positionals.FirstOrDefault() ?? "status";
        var arg = positionals.Skip(1).FirstOrDefault();
        switch (verb)
        {
            case "status":
            {
                var games = arg is null ? config.Games.Where(g => g.IsEnrolledHere) : [config.FindGame(arg) ?? throw new InvalidOperationException($"'{arg}' is not tracked here.")];
                ContentTransferDto[] transfers = [];
                try { transfers = await api.ListTransfersAsync(); } catch (HttpRequestException) { }
                foreach (var g in games)
                {
                    if (ContentInventory.For(g) is not { } here) continue;
                    var head = await api.GetPlaythroughHeadAsync(g.GameId, g.PlaythroughId);
                    var missing = DlcCheck.Missing(head?.Content, here);
                    var held = DlcHoldStore.For(config).For(g.GameId) is not null;
                    Console.WriteLine($"  {g.Name}: {here.Length} package(s) here; latest save " +
                        (head?.Content is null ? "lists none" : missing.Count == 0 ? "has every one it needs" : $"needs {DlcCheck.Summary(missing)}") +
                        (held ? "  *** PULL HELD ***" : ""));
                    foreach (var t in transfers.Where(t => t.GameId == g.GameId))
                        Console.WriteLine($"      hand-over {t.Id:N}: {t.FromMachineName} -> {t.ToMachineName}, {t.Status.ToString().ToLowerInvariant()}");
                }
                return 0;
            }
            case "ask":
            {
                var g = config.FindGame(arg ?? throw new ArgumentException("Pass a game name, e.g. dlc ask \"Forza Horizon\""))
                        ?? throw new InvalidOperationException($"'{arg}' is not tracked here.");
                var head = await api.GetPlaythroughHeadAsync(g.GameId, g.PlaythroughId);
                var missing = DlcCheck.Missing(head?.Content, ContentInventory.For(g));
                if (head?.MachineId is not { } from || missing.Count == 0 || from == config.MachineId)
                {
                    Console.WriteLine($"Nothing to ask for: this machine has every pack the latest save of {g.Name} needs.");
                    return 0;
                }
                var t = await api.CreateTransferAsync(new CreateTransferRequest(g.GameId, from, [.. missing]));
                Console.WriteLine($"Asked {t.FromMachineName} for {DlcCheck.Summary(missing)} (request {t.Id:N}). " +
                                  "The agent installs them once sent, and the held pull goes ahead.");
                return 0;
            }
            case "send" or "decline":
            {
                if (!Guid.TryParse(arg, out var id)) throw new ArgumentException($"Pass the request id from dlc status, e.g. dlc {verb} <id>");
                var t = await api.AnswerTransferAsync(id, approve: verb == "send");
                Console.WriteLine(verb == "send" ? $"Sending to {t.ToMachineName}: the agent uploads it on its next check." : $"Declined {t.ToMachineName}'s request.");
                return 0;
            }
            default:
                throw new ArgumentException("Usage: dlc status [game] | dlc ask <game> | dlc send <id> | dlc decline <id>");
        }
    }

    private static TrackedGame GameArg(Dictionary<string, string> opts, List<string> positionals, AgentConfig config,
        string example)
    {
        var name = opts.GetValueOrDefault("game") ?? positionals.FirstOrDefault()
                   ?? throw new ArgumentException($"Pass a game name, e.g. {example}");
        return config.FindGame(name) ?? throw new InvalidOperationException($"'{name}' is not tracked here.");
    }

    /// <summary>One line per extra folder: where it is here, or its shadow and how big that is.</summary>
    internal static IEnumerable<string> FolderLines(TrackedGame game, string stateDir) =>
        game.ExtraPaths.Select(p => p.IsMapped
            ? $"{p.Key}: {p.Directory}{(Directory.Exists(p.Directory) ? "" : " (missing — the next pull recreates it)")}"
            : $"{p.Key}: not mapped here — synced through its shadow copy " +
              $"({FolderSize.Of(TrackedGame.ShadowDir(stateDir, game.GameId, p.Key)) / 1024.0 / 1024.0:0.#} MB)");

    private static IEnumerable<TrackedGame> GamesFor(string? name, AgentConfig config)
    {
        if (string.IsNullOrEmpty(name) || name.Equals("all", StringComparison.OrdinalIgnoreCase))
            return config.Games;
        var g = config.FindGame(name);
        return g is null ? Enumerable.Empty<TrackedGame>() : new[] { g };
    }
}
