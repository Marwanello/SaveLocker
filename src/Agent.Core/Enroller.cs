using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// Turns scan candidates into tracked games: creates each on the server and records it locally.
/// Shared by the Windows tray and the Linux daemon — both enroll from the same agent UI.
/// </summary>
public static class Enroller
{
    /// <summary>Where an enrollment batch is, for a UI that would otherwise stare at one long request.</summary>
    /// <param name="Notes">Why a game was refused rather than skipped as already set up here, one line each,
    /// for the UI to say once the batch is done — a bare "skipped" count reads as "already tracked".</param>
    public sealed record EnrollProgress(
        bool Active, int Index, int Total, string? Game, string Step, int Enrolled, int Skipped,
        IReadOnlyList<string>? Notes = null);

    private static readonly object ProgressLock = new();
    private static EnrollProgress _progress = new(false, 0, 0, null, "", 0, 0);

    public static EnrollProgress Progress { get { lock (ProgressLock) return _progress; } }

    private static void Report(EnrollProgress p) { lock (ProgressLock) _progress = p; }

    /// <summary>
    /// Enroll the candidates at <paramref name="ids"/>. Skips ones already tracked or with no
    /// resolved save directory. Saves the config if anything was added.
    /// </summary>
    /// <param name="alsoSync">Per candidate id, which of its "Also found" folders
    /// (<see cref="ScanCandidate.AlternateSaveDirs"/>) the user ticked. Only those are added; a candidate
    /// missing from it adds none.</param>
    public static async Task<(int enrolled, int skipped)> EnrollAsync(
        AgentConfig config,
        IReadOnlyList<ScanCandidate> candidates,
        int[] ids,
        CancellationToken ct = default,
        IReadOnlyDictionary<int, string[]>? alsoSync = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
            throw new InvalidOperationException("Not registered yet. Open Settings and click Register first.");

        var api = ApiClient.For(config);
        var enrolled = 0;
        var skipped = 0;
        var total = ids.Count(i => i >= 0 && i < candidates.Count);
        var index = 0;
        var notes = new List<string>();
        // The fleet's games, read once for the batch and kept current as it creates more: an emulator
        // game's name is decided against it (ServerNameFor).
        List<GameDto>? serverGames = null;
        void Step(string? game, string step) =>
            Report(new EnrollProgress(true, index, total, game, step, enrolled, skipped, notes.ToArray()));
        void Refuse(string game, string why)
        {
            AgentLogger.Log($"Enrollment skipped '{game}': {why}");
            notes.Add($"{game}: {why}");
            skipped++;
        }

        try
        {
            // Before the first candidate: a poll that lands now must see THIS batch starting, not the
            // finished state the last one left behind.
            Step(null, "Getting ready");
            foreach (var id in ids)
            {
                if (id < 0 || id >= candidates.Count) continue;
                var c = candidates[id];
                index++;
                Step(c.Name, "Checking the save folder");
                // A game already set up here is skipped. One that is tracked but has no folder on this
                // machine (adopted from the server, its template unresolvable here — an emulator game
                // enrolled on the Deck, seen from Windows) is mapped by enrolling it: the server hands
                // back the same game and the entry below replaces the empty one.
                if (TrackedFor(config, c) is { IsEnrolledHere: true }) { skipped++; continue; }
                if (string.IsNullOrEmpty(c.SuggestedSaveDir)) { skipped++; continue; }

                // A scanner's save-root heuristic can land on something far too broad — an install tree,
                // a profile folder. Enrolling it would create the game on the server AND map it, so the
                // refusal has to happen before the game exists. WA-02.
                var check = SavePathGuard.Check(c.SuggestedSaveDir, config.StateDir);
                if (!check.Ok)
                {
                    AgentLogger.Log($"Enrollment skipped '{c.Name}': {check.Reason} " +
                                    $"(scanner suggested '{c.SuggestedSaveDir}')");
                    skipped++;
                    continue;
                }

                // The MANIFEST's spelling, not the shortcut's, is the server-side identity. A shortcut
                // carries whatever its owner typed, and the server matches names case-insensitively but
                // not past punctuation — so a Deck saying "DRAGON QUEST III HD 2D Remake" and a PC saying
                // "DRAGON QUEST III HD-2D Remake" created two games that could never sync to each other.
                // Falls back to the scanned name for anything the manifest does not know.
                var serverName = c.ManifestKey ?? c.Name;

                // Folders the scanner declared alongside the primary one. Every refusal happens here,
                // before the game exists on the server, for the same reason as the check above.
                if (DeclaredFolders(config, c, check.Canonical!) is not { } extras)
                {
                    skipped++;
                    continue;
                }

                if (c.Source == ScanSource.Emulator)
                {
                    serverGames ??= await api.ListGamesAsync();
                    if (ServerNameFor(serverGames, c, extras) is not { } named)
                    {
                        Refuse(c.Name, "every name it could take on the server is already another game's.");
                        continue;
                    }
                    serverName = named;
                }

                Step(c.Name, "Creating it on the server");
                GameDto game;
                try
                {
                    game = await api.CreateGameAsync(new CreateGameRequest(serverName, c.ManifestKey, null,
                        IncludeGlobs: Scope(c.IncludeGlobs),
                        ExtraPaths: extras.Count == 0
                            ? null
                            : extras.Select(e => new SavePathDto(e.Key, null, e.Template, Scope(e.IncludeGlobs))).ToArray()));
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // Every game would fail the same way, and a bare "401 (Unauthorized)" says nothing
                    // about the fix: the server no longer knows this machine's key. ASCII only, and no "Settings"
                    // without saying whose: Game Mode's screen shows this too, its font has no arrows, and its
                    // own Settings cannot register.
                    throw new InvalidOperationException(
                        "the server doesn't recognise this machine's key (401). It may have been reset, or " +
                        "this machine removed from it. Register again with Register / Re-register in the agent " +
                        "UI's Settings (on a Deck, from Desktop Mode).", ex);
                }
                // The folders and scopes are applied only when this request CREATED the game. One that
                // already existed keeps its own, and a game whose saves are defined differently than
                // this scanner knows them would sync the wrong files — or another game's, in a shared
                // emulator folder. Left for a human; the game stays on the server as it was.
                if (serverGames is not null && serverGames.All(g => g.Id != game.Id)) serverGames.Add(game);
                if ((c.IncludeGlobs is { Count: > 0 } || extras.Count > 0) && !SameFolders(game, c, extras))
                {
                    Refuse(c.Name, $"the server already has '{game.Name}' with different save folders or " +
                                   "include patterns than this scan found.");
                    continue;
                }
                // The other direction: a candidate with no scope of its own joining a game that has one. The
                // game's patterns would apply here too, and when none of this folder's files match them —
                // a PC release joining the same-named emulator game — nothing of this machine's would ever
                // be backed up, with every screen reporting it in sync. A folder with no files yet has
                // nothing to lose, and one some of whose files match is the same game (patterns set in the
                // console), so both join as before.
                if (c.IncludeGlobs is not { Count: > 0 } && game.IncludeGlobs is { Length: > 0 } scope &&
                    NothingInScope(check.Canonical!, game.ExcludeGlobs, scope))
                {
                    Refuse(c.Name, $"the server's '{game.Name}' only keeps {string.Join(", ", scope)}, and " +
                                   "nothing in this game's save folder matches that.");
                    continue;
                }

                // The "Also found" folders the user ticked. Unlike the declared ones they never decide
                // whether the game is joined: one the fleet's game lacks is added to it, one it has
                // (FleetHasFolder) is the poller's to map, like any other folder of a game this machine joins.
                var chosen = alsoSync is not null && alsoSync.TryGetValue(id, out var picked) ? picked : null;
                if (chosen is { Length: > 0 })
                {
                    Step(c.Name, "Adding its other save folders");
                    extras = [.. extras, .. await AddAlsoFoundAsync(api, config, c, game, check.Canonical!, extras, chosen)];
                }

                // Persisted per candidate, not once at the end: a later candidate that fails — or a UI
                // window closed mid-batch — must not lose the games already created on the server, along
                // with their Steam AppIDs. SetTracked also clears any per-machine opt-out, so re-adding
                // a game removed here earlier actually re-adds it.
                Step(c.Name, "Saving it on this machine");
                try
                {
                    config.SetTracked(game.Id, tracked: true, entry: new TrackedGame
                    {
                        GameId = game.Id,
                        Name = game.Name,
                        ManifestKey = c.ManifestKey,
                        SaveDirectory = check.Canonical!,
                        SteamAppId = c.SteamAppId,
                        HasSteamCloud = c.HasSteamCloud,
                        InstallDir = c.InstallDir,
                        Source = GameSources.From(c),
                        // Without this the Windows ProcessWatcher excludes the game outright, so lease,
                        // exit-push and the running-game pull refusal never run for anything enrolled
                        // through the UI. Only the CLI's --proc used to populate it. WA-08.
                        ProcessNames = c.SuggestedProcessName is { } proc ? new List<string> { proc } : new(),
                        IncludeGlobs = (game.IncludeGlobs ?? Array.Empty<string>()).ToList(),
                        // A fresh entry has no shadow to carry over, so the folders map directly.
                        ExtraPaths = extras.Select(e => new TrackedSavePath
                        {
                            Key = e.Key, Template = e.Template, Directory = e.Dir,
                            IncludeGlobs = (e.IncludeGlobs ?? Array.Empty<string>()).ToList(),
                        }).ToList(),
                    });
                }
                catch (AgentStateLockException ex)
                {
                    // The game already exists server-side, just not tracked locally yet — leaving it
                    // that way (rather than aborting the rest of this batch) is safe: CommandPoller's
                    // own reconcile adopts any server game not yet tracked locally on its next tick.
                    AgentLogger.LogException($"Enroller.SetTracked '{c.Name}'", ex);
                    skipped++;
                    continue;
                }

                // Report the chosen path to the server now, so it is authoritative from the start. The
                // Windows tray gets away without this because its in-process CommandPoller reports the
                // path on its next tick — but the Deck's `savelocker ui` is a separate process with no
                // poller, so without this the console shows "not set" and a daemon reconcile (which
                // resolves paths from the server) blanks the local path it was never told about, leaving
                // the game unmapped and a Sync reporting "no matching mapped game on this machine".
                Step(c.Name, "Telling the server where the save is");
                try
                {
                    await api.SetMachinePathAsync(game.Id, check.Canonical!);
                    foreach (var e in extras) await api.SetMachinePathAsync(game.Id, e.Dir, e.Key);
                }
                catch (Exception ex) { AgentLogger.LogException("Enroller.SetMachinePath", ex); }
                try { await api.SetGameSourceAsync(game.Id, GameSources.From(c)); }
                catch (Exception ex) { AgentLogger.LogException("Enroller.SetGameSource", ex); }
                enrolled++;
            }
        }
        finally
        {
            Report(new EnrollProgress(false, index, total, null, "Done", enrolled, skipped, notes.ToArray()));
        }

        return (enrolled, skipped);
    }

    /// <summary>
    /// Define the ticked "Also found" folders on the server game and return them as folders this
    /// machine maps directly — each is new to the fleet, so this machine's files are its content. A
    /// folder that breaks the rules between the game's folders (the primary folder was changed to one
    /// beside it) or that the server refuses is left out and logged; the game is still enrolled.
    /// </summary>
    private static async Task<List<DeclaredSavePath>> AddAlsoFoundAsync(ApiClient api, AgentConfig config,
        ScanCandidate c, GameDto game, string primary, IReadOnlyList<DeclaredSavePath> extras, string[] chosen)
    {
        var offered = (c.AlternateSaveDirs ?? Array.Empty<DeclaredSavePath>())
            .Where(a => chosen.Any(d => SavePathGuard.Canonicalize(d) == SavePathGuard.Canonicalize(a.Dir)))
            .Select(a => new ResolvedSaveLocation(a.Key, a.Dir));
        var roots = new List<SaveRoot> { SaveRoot.Primary(primary, c.IncludeGlobs) };
        roots.AddRange(extras.Select(e => new SaveRoot(e.Key, e.Dir, e.IncludeGlobs)));
        var server = game.ExtraPaths ?? Array.Empty<SavePathDto>();
        var serverKeys = server.Select(p => p.Key).ToList();

        var added = new List<DeclaredSavePath>();
        foreach (var also in FolderSuggestions.Alternates(offered, roots, [], config.StateDir))
        {
            var template = TemplateFor(c, also.Dir);
            // Another machine found the same folder first: the fleet's game has it, and the poller maps
            // this machine's copy onto it like any other folder of a game it joins.
            if (FleetHasFolder(server, also.Key, template)) continue;
            var tried = new HashSet<string>(serverKeys.Concat(extras.Select(e => e.Key)), StringComparer.Ordinal);
            var key = FolderSuggestions.KeyFor(also.Key, tried);
            for (var attempt = 0; attempt < 8; attempt++)
            {
                SavePathAddResult result;
                try { result = await api.AddSavePathAsync(game.Id, new AddSavePathRequest(key, null, template, null)); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    AgentLogger.Log($"Enrollment of '{c.Name}': could not add the folder {also.Dir} ({ex.Message}).");
                    break;
                }
                if (result.Path is { } path)
                {
                    added.Add(new DeclaredSavePath(path.Key, also.Dir, null, path.Template));
                    break;
                }
                // Added by another machine since this game was read: it is the poller's to map, as above.
                if (result.Refusal?.Code == SavePathRefusalCodes.TemplateTaken) break;
                // Taken by a folder another machine added, or retired before: the next free key.
                if (result.Refusal?.Code is not (SavePathRefusalCodes.KeyTaken or SavePathRefusalCodes.KeyRetired))
                {
                    AgentLogger.Log($"Enrollment of '{c.Name}': the server refused the folder {also.Dir}: {result.Error}");
                    break;
                }
                tried.Add(key);
                key = FolderSuggestions.KeyFor(also.Key, tried);
            }
        }
        return added;
    }

    /// <summary>
    /// Whether the fleet's game already has the "Also found" folder <paramref name="key"/>/<paramref name="template"/>,
    /// so this machine joins it rather than adding it: the same template is the same folder. A key match is too
    /// only when a template is missing on either side and there is nothing to tell them apart by — the key comes
    /// from the same manifest template on every machine. Two different templates under one key are two folders.
    /// </summary>
    internal static bool FleetHasFolder(IReadOnlyList<SavePathDto> server, string key, string? template) =>
        server.Any(p => template is not null && string.Equals(p.Template, template, StringComparison.OrdinalIgnoreCase))
        || server.Any(p => p.Key == key && (p.Template is null || template is null));

    /// <summary>
    /// The candidate's declared extra folders, canonical and each described as a template where one
    /// fits — or null, logged, when any of them may not be synced: a bad key or include pattern, a
    /// folder <see cref="SavePathGuard"/> refuses, or folders that break the rules between one game's
    /// folders (nested, or an unscoped shared directory). One bad folder skips the whole candidate:
    /// enrolling the rest would define the game on the server without it, for every machine.
    /// </summary>
    internal static List<DeclaredSavePath>? DeclaredFolders(AgentConfig config, ScanCandidate c, string primary)
    {
        var result = new List<DeclaredSavePath>();
        List<DeclaredSavePath>? Skip(string why)
        {
            AgentLogger.Log($"Enrollment skipped '{c.Name}': {why}");
            return null;
        }

        foreach (var glob in (c.IncludeGlobs ?? Array.Empty<string>()).Concat(
                     (c.ExtraSaveDirs ?? Array.Empty<DeclaredSavePath>()).SelectMany(e => e.IncludeGlobs ?? Array.Empty<string>())))
            if (SaveArchive.ValidateIncludeGlob(glob) is { } badGlob) return Skip(badGlob);

        foreach (var e in c.ExtraSaveDirs ?? Array.Empty<DeclaredSavePath>())
        {
            if (SaveRoot.ValidateExtraKey(e.Key) is { } badKey) return Skip(badKey);
            var check = SavePathGuard.Check(e.Dir, config.StateDir);
            if (!check.Ok) return Skip($"save folder '{e.Key}': {check.Reason} (scanner suggested '{e.Dir}')");
            var template = PathResolver.IsTemplate(e.Template) ? e.Template : TemplateFor(c, check.Canonical!);
            result.Add(e with { Dir = check.Canonical!, Template = template });
        }

        var roots = new List<SaveRoot> { SaveRoot.Primary(primary, c.IncludeGlobs) };
        roots.AddRange(result.Select(e => new SaveRoot(e.Key, e.Dir, e.IncludeGlobs)));
        if (SaveArchive.FolderRulesError(roots) is { } rules) return Skip(rules);
        return result;
    }

    /// <summary>The folder as a template other machines can expand — against the game's own prefix
    /// when it has one — or null when no token describes it.</summary>
    private static string? TemplateFor(ScanCandidate c, string dir)
    {
        PathResolver? resolver = null;
        if (!string.IsNullOrWhiteSpace(c.PrefixPath))
        {
            var root = SteamLayout.RootFromCompatData(c.PrefixPath);
            resolver = WinePrefix.ResolverFor(c.PrefixPath, c.InstallDir, root)
                       ?? PathResolver.Proton(c.PrefixPath, c.InstallDir, root);
        }
        else if (OperatingSystem.IsWindows())
            resolver = PathResolver.Windows(c.InstallDir);
        return resolver?.Tokenize(dir) is { } t && PathResolver.IsTemplate(t) ? t : null;
    }

    private static string[]? Scope(IReadOnlyList<string>? globs) =>
        globs is { Count: > 0 } ? globs.ToArray() : null;

    /// <summary>
    /// The names a candidate may hold on the server, preferred first. A PC game has one: its manifest's
    /// spelling. An emulator save is named by its cleaned title ("Chrono Trigger"), then that title with
    /// its emulator ("Chrono Trigger (RetroArch)"), then the save file's own name with it ("Chrono Trigger
    /// (Japan) (RetroArch)"). Every one of them comes from the save file alone, so every machine walks the
    /// same list — which of them it takes is then decided by the server (<see cref="ServerNameFor"/>), the
    /// one thing every machine sees alike. Deciding it from what else a machine has installed split one ROM
    /// into two games across a Deck and a PC (a Steam ROM Manager shortcut on one, nothing on the other).
    /// </summary>
    public static IReadOnlyList<string> NamesFor(ScanCandidate c)
    {
        if (c.Source != ScanSource.Emulator) return [c.Name];
        var by = c.EmulatorName ?? "Emulator";
        var names = new List<string> { c.Name, $"{c.Name} ({by})" };
        if (c.EmulatorRom is { Length: > 0 } rom && !string.Equals(rom, c.Name, StringComparison.OrdinalIgnoreCase))
            names.Add($"{rom} ({by})");
        return names;
    }

    /// <summary>
    /// The game this machine tracks for <paramref name="c"/>, if any: one under any of its names
    /// (<see cref="NamesFor"/>) that keeps the same files. A same-named game with other include patterns is
    /// a different game — "Chrono Trigger" from Steam is not the SNES save of the same name. A candidate that
    /// names its files (an emulator save) is also found by them under any name (D1, <see cref="ServerNameFor"/>):
    /// a game adopted from the server may be called something this machine's scan would never say.
    /// </summary>
    public static TrackedGame? TrackedFor(AgentConfig config, ScanCandidate c)
    {
        foreach (var name in NamesFor(c))
            if (config.FindGame(name) is { } g && SameScope(g.IncludeGlobs, c.IncludeGlobs)) return g;
        if (c.IncludeGlobs is not { Count: > 0 }) return null;
        var declared = c.ExtraSaveDirs ?? Array.Empty<DeclaredSavePath>();
        return config.Games
            .Where(g => SameScope(g.IncludeGlobs, c.IncludeGlobs) && g.ExtraPaths.Count == declared.Count &&
                        declared.All(e => g.ExtraPaths.FirstOrDefault(p => p.Key == e.Key) is { } p &&
                                          SameScope(p.IncludeGlobs, e.IncludeGlobs)))
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Which name an emulator save takes on the server. First (D1) the name of the server game that already
    /// keeps exactly these files — the same folder keys and include patterns, whatever it is called: a scope
    /// names the save file, so no two games share one, and a title taken from something only one machine has
    /// (an ES-DE <c>gamelist.xml</c>, Supermodel's <c>Games.xml</c>, a ScummVM description) can never split one
    /// game in two. Else the first of <see cref="NamesFor"/> no game has. Null when every name belongs to a
    /// different game. Order-independent across machines: whoever enrolls a ROM first, the next machine finds
    /// it by its files, not by being first to a name.
    /// </summary>
    internal static string? ServerNameFor(IReadOnlyList<GameDto> server, ScanCandidate c, IReadOnlyList<DeclaredSavePath> extras)
    {
        var names = NamesFor(c);
        bool Is(GameDto g, string n) => string.Equals(g.Name, n, StringComparison.OrdinalIgnoreCase);
        // Only a scoped candidate: an unscoped one (a PC game's whole folder) names no files of its own.
        if (c.IncludeGlobs is { Count: > 0 })
        {
            var same = server.Where(g => SameFolders(g, c, extras)).ToList();
            // Two such games exist only on a server that predates this rule; prefer one under our own names.
            if (names.FirstOrDefault(n => same.Any(g => Is(g, n))) is { } own) return own;
            if (same.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault() is { } other) return other.Name;
        }
        return names.FirstOrDefault(n => !server.Any(g => Is(g, n)));
    }

    /// <summary>The folder has files, and none of them is in <paramref name="scope"/>.</summary>
    internal static bool NothingInScope(string dir, IEnumerable<string>? excludeGlobs, IReadOnlyList<string> scope)
    {
        try
        {
            return SaveArchive.ListFiles(dir, excludeGlobs).Count > 0 &&
                   SaveArchive.ListFiles(dir, excludeGlobs, scope).Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool SameScope(IEnumerable<string>? a, IEnumerable<string>? b) =>
        (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>(), StringComparer.Ordinal);

    /// <summary>Does the server's game hold exactly the scopes and folder keys this candidate declared?</summary>
    internal static bool SameFolders(GameDto game, ScanCandidate c, IReadOnlyList<DeclaredSavePath> extras)
    {
        if (!SameScope(game.IncludeGlobs, c.IncludeGlobs)) return false;
        var server = game.ExtraPaths ?? Array.Empty<SavePathDto>();
        if (server.Length != extras.Count) return false;
        return extras.All(e => server.FirstOrDefault(s => s.Key == e.Key) is { } s && SameScope(s.IncludeGlobs, e.IncludeGlobs));
    }
}
