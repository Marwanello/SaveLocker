using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// This machine's own file names for a game (tasks/emulator-saves Phase 17): the explicit list grows by itself as files the
/// listed names derive appear — a new state slot — and the server keeps a copy for the console to show.
/// </summary>
public static class OwnFileNames
{
    /// <summary>
    /// Add to <paramref name="game"/>'s list every file in its own folders that a listed name derives but the list does not
    /// have yet (<see cref="FileNames.Derive"/>), and persist it. True when the list changed.
    /// </summary>
    public static bool Grow(AgentConfig config, TrackedGame game)
    {
        if (game.OwnFileNames.Count == 0) return false;
        var added = new List<FileNameDto>();
        foreach (var root in game.RealRoots(config.StateDir))
        {
            if (!Directory.Exists(root.Directory)) continue;
            List<string> rels;
            try
            {
                var full = Path.GetFullPath(root.Directory);
                rels = Directory.EnumerateFiles(full).Select(f => Path.GetRelativePath(full, f).Replace('\\', '/')).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            var derived = FileNames.Derive([.. game.OwnFileNames, .. added], root.Key, rels);
            // Only names in the folder's scope: a derived swap for another game's file is no file of this game.
            var inScope = SaveArchive.FilterIncluded(derived.Select(d => d.Server), root.IncludeGlobs).ToHashSet(StringComparer.Ordinal);
            added.AddRange(derived.Where(d => inScope.Contains(d.Server)));
        }
        if (added.Count == 0) return false;
        var names = game.OwnFileNames.Concat(added).ToList();
        if (FileNames.Validate(names) is not null) return false;
        game.OwnFileNames = names;
        config.SaveGameFileNames(game.GameId, names);
        return true;
    }

    /// <summary>Tell the server this machine's names for the game. Best effort: the console's copy is for showing; the agent's
    /// is the one every sync uses.</summary>
    public static async Task ReportAsync(ApiClient api, TrackedGame game, Action<string>? log = null)
    {
        try { await api.SetFileNamesAsync(game.GameId, game.OwnFileNames.ToArray()); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log?.Invoke($"[{game.Name}] could not tell the server this device's file names: {ex.Message}");
        }
    }
}
