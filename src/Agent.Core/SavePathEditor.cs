using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>What adding or removing a save folder came to.</summary>
/// <param name="NeedsChoice">Both this folder and the fleet's copy hold different files: re-send with a
/// keep side. The folder was added for the fleet already if <paramref name="Added"/>.</param>
/// <param name="NeedsConfirm">A heuristic with false positives refused the folder
/// (<paramref name="Problems"/>); re-send confirmed to use it anyway.</param>
/// <param name="Deferred">Removed on the server, but the game is busy here, so this machine drops the
/// folder on the agent's next poll.</param>
public sealed record SavePathChange(
    bool Ok,
    string? Error = null,
    bool NeedsChoice = false,
    bool NeedsConfirm = false,
    IReadOnlyList<string>? Problems = null,
    string? Key = null,
    string? Directory = null,
    string? Template = null,
    bool Added = false,
    bool Reported = false,
    bool Deferred = false);

/// <summary>
/// Adding a game's extra save folder and removing one (tasks/multiple-save-paths): the steps
/// <c>add-path</c>/<c>remove-path</c>, the agent UI and the "Also found" prompt all take, in one place,
/// so a folder added from any of them is checked, defined and mapped the same way.
/// </summary>
public static class SavePathEditor
{
    /// <summary>
    /// Give <paramref name="game"/> another save folder for every machine, or map one it already has
    /// (<paramref name="key"/> exists) to <paramref name="dir"/> here. Every check that needs no server
    /// runs first, so a refused folder never leaves a new key behind on the server. The folder is
    /// described to the fleet as a template when it can be; one no token describes is added without,
    /// and other machines keep a shadow copy of it until their own folder is set.
    /// </summary>
    /// <param name="pickFreeKey">The key is a suggestion (from a manifest template): if the server
    /// already has it, or had it once, take the next free one instead of failing.</param>
    public static async Task<SavePathChange> AddAsync(AgentConfig config, ApiClient api,
        Func<TrackedGame, string, string, KeepSide?, Task<MapFolderResult>> map, TrackedGame game,
        string key, string dir, IReadOnlyList<string>? include = null, string? label = null, KeepSide? keep = null,
        bool confirm = false, PathResolver? resolver = null, bool pickFreeKey = false)
    {
        key = key.Trim();
        if (SaveRoot.ValidateExtraKey(key) is { } badKey) return new SavePathChange(false, badKey);
        var scope = (include ?? Array.Empty<string>()).Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList();
        foreach (var glob in scope)
            if (SaveArchive.ValidateIncludeGlob(glob) is { } badGlob) return new SavePathChange(false, badGlob);

        var existing = game.ExtraPaths.FirstOrDefault(p => p.Key == key);
        var check = existing is not null
            ? SavePathGuard.CheckFolder(game, key, dir, config.StateDir)
            : CheckNewFolder(game, key, dir, scope, config.StateDir);
        if (!check.Ok) return new SavePathChange(false, $"Can't use that folder: {check.Reason}");
        var canonical = check.Canonical!;

        var problems = SaveDirSanity.Inspect(canonical, game.ExcludeGlobs);
        if (problems.Count > 0 && !confirm)
            return new SavePathChange(false, "That folder looks wrong: " + string.Join(" ", problems),
                NeedsConfirm: true, Problems: problems);

        string? template = existing?.Template;
        if (existing is null)
        {
            template = TemplateFor(game, canonical, resolver);
            SavePathDto? added = null;
            string? refused = null;
            var stem = key;
            var tried = new HashSet<string>(game.ExtraPaths.Select(p => p.Key).Concat(game.RemovedPathKeys), StringComparer.Ordinal);
            for (var attempt = 0; attempt < 8; attempt++)
            {
                (added, refused) = await api.AddSavePathAsync(game.GameId,
                    new AddSavePathRequest(key, label, template, scope.Count > 0 ? scope.ToArray() : null));
                // A key this machine never heard of can still be taken: another machine added it, or the
                // server retired it before this machine tracked the game.
                if (added is not null || !pickFreeKey || refused is null || !refused.Contains($"'{key}'")) break;
                tried.Add(key);
                key = FolderSuggestions.KeyFor(stem, tried);
            }
            if (added is null) return new SavePathChange(false, $"Could not add the save folder: {refused}");

            key = added.Key;
            // New lists, never in-place edits: a push or the poller may be enumerating these right now.
            game.ExtraPaths = [.. game.ExtraPaths, new TrackedSavePath
            {
                Key = added.Key, Label = added.Label, Template = added.Template,
                IncludeGlobs = (added.IncludeGlobs ?? Array.Empty<string>()).ToList(),
            }];
            game.RemovedPathKeys = game.RemovedPathKeys.Where(k => k != key).ToList();
            config.SaveGameFolders(game);
        }

        // A folder this call just created on the server has no copy anywhere but here: its files are the
        // folder's content by definition, so there is nothing to choose between.
        var result = await map(game, key, canonical, existing is null ? keep ?? KeepSide.Local : keep);
        if (!result.Ok)
            return new SavePathChange(false, result.Error ?? "Could not map the folder.", NeedsChoice: result.NeedsChoice,
                Key: key, Directory: canonical, Template: template, Added: existing is null);

        var reported = false;
        try
        {
            await api.SetMachinePathAsync(game.GameId, canonical, key);
            if (game.ExtraPaths.FirstOrDefault(p => p.Key == key) is { } mapped) mapped.PathUnreported = false;
            config.SaveGameFolders(game);
            reported = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AgentLogger.Log($"Could not report '{game.Name}' folder '{key}' yet ({ex.Message}); the next poll does.");
        }
        return new SavePathChange(true, Key: key, Directory: canonical, Template: template,
            Added: existing is null, Reported: reported);
    }

    /// <summary>Stop syncing an extra folder on every machine: the server retires its key, and this machine
    /// forgets it (its shadow included). The folder's files stay where they are.</summary>
    public static async Task<SavePathChange> RemoveAsync(AgentConfig config, ApiClient api,
        Func<TrackedGame, string, Task<bool>> forget, TrackedGame game, string key)
    {
        var path = game.ExtraPaths.FirstOrDefault(p => p.Key == key);
        if (path is null) return new SavePathChange(false, $"'{game.Name}' has no save folder called '{key}' here.");
        if (await api.RemoveSavePathAsync(game.GameId, key) is { } refused)
            return new SavePathChange(false, $"Could not remove: {refused}");
        if (!await forget(game, key))
            return new SavePathChange(true, Key: key, Directory: path.Directory, Deferred: true);
        config.SaveGameFolders(game);
        return new SavePathChange(true, Key: key, Directory: path.Directory);
    }

    /// <summary><see cref="SavePathGuard.CheckFolder"/> for a key the game does not have yet: the folder
    /// against every folder the game has, as if it were already one of them.</summary>
    private static SavePathGuard.Result CheckNewFolder(TrackedGame game, string key, string dir,
        IReadOnlyList<string> scope, string stateDir)
    {
        var check = SavePathGuard.Check(dir, stateDir);
        if (!check.Ok) return check;
        var roots = game.Roots(stateDir).Where(r => !string.IsNullOrWhiteSpace(r.Directory)).ToList();
        if (roots.All(r => !r.IsPrimary)) return check;
        roots.Add(new SaveRoot(key, check.Canonical!, scope));
        return SaveArchive.FolderRulesError(roots) is { } why ? SavePathGuard.Result.Refuse(why) : check;
    }

    /// <summary>
    /// The folder as a template every machine can expand, or null when no token describes it. Under
    /// Proton the game's own prefix is what <c>&lt;winDocuments&gt;</c> and the rest mean, so a folder
    /// inside one is tokenized against that prefix, never against this Linux host.
    /// </summary>
    public static string? TemplateFor(TrackedGame game, string dir, PathResolver? resolver = null)
    {
        if (resolver is null && PrefixRootOf(dir) is { } prefix)
            resolver = WinePrefix.ResolverFor(prefix, game.InstallDir, SteamLayout.RootFromCompatData(prefix))
                       ?? PathResolver.Proton(prefix, game.InstallDir, SteamLayout.RootFromCompatData(prefix));
        if (resolver is null && OperatingSystem.IsWindows()) resolver = PathResolver.Windows(game.InstallDir);
        return resolver?.Tokenize(dir) is { } t && PathResolver.IsTemplate(t) ? t : null;
    }

    /// <summary><c>…/compatdata/&lt;appid&gt;</c> when <paramref name="dir"/> is inside a Steam prefix.</summary>
    private static string? PrefixRootOf(string dir)
    {
        var parts = dir.Replace('\\', '/').Split('/');
        var i = Array.FindLastIndex(parts, p => p == "compatdata");
        return i >= 0 && i + 1 < parts.Length ? string.Join('/', parts[..(i + 2)]) : null;
    }
}
