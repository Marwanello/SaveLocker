using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// Save-location detection: manages the cached Ludusavi manifest and resolves
/// a game's concrete save directories on this machine.
///
/// The resolver is injectable because it is not a per-machine constant everywhere: under Proton
/// the manifest's Windows tokens resolve *inside a game's Wine prefix*, so the map is per-game.
/// Pass <see cref="PathResolver.Proton"/> for a Proton game; the default is the host's own.
/// </summary>
public sealed class Detection
{
    private readonly AgentConfig _config;
    private ManifestLoader? _manifest;

    public Detection(AgentConfig config) => _config = config;

    /// <summary>Load the manifest from cache, downloading it if missing or if forced.</summary>
    public async Task<ManifestLoader> GetManifestAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (_manifest is not null && !forceRefresh) return _manifest;

        if (!forceRefresh && File.Exists(_config.ManifestCachePath))
            _manifest = ManifestLoader.LoadFromFile(_config.ManifestCachePath);
        else
            _manifest = await ManifestLoader.DownloadAsync(_config.ManifestCachePath, ct: ct);

        return _manifest;
    }

    /// <summary>
    /// Find candidate save directories for a manifest game name. Empty if the game isn't in the
    /// manifest or none of its paths exist. On Linux with no <paramref name="resolver"/> this is
    /// always empty — the host's own paths are meaningless for a Windows game, and guessing would
    /// be worse than admitting we cannot resolve it.
    /// </summary>
    public async Task<IReadOnlyList<string>> ResolveSaveDirectoriesAsync(
        string gameName, PathResolver? resolver = null, CancellationToken ct = default)
    {
        resolver ??= HostResolver();
        if (resolver is null) return Array.Empty<string>();

        var manifest = await GetManifestAsync(ct: ct);
        return manifest.ResolveSaveDirectories(gameName, resolver);
    }

    /// <summary>
    /// Resolve a Proton game's save directories, supplying the three per-game placeholders.
    /// <para>
    /// <c>&lt;root&gt;</c> and the Steam account ids are DERIVED from the compatdata path rather
    /// than plumbed in: the prefix is always <c>{root}/steamapps/compatdata/{appid}</c>, so the
    /// library root is three levels up and the accounts are the <c>userdata/*</c> folders under it.
    /// That keeps every caller — scanner, poller, launch wrapper, CLI — passing only what it
    /// genuinely knows.
    /// </para>
    /// </summary>
    /// <param name="installDir">
    /// The game's install directory on the host — <c>&lt;base&gt;</c>. For a non-Steam shortcut
    /// that is its <c>StartDir</c>.
    /// </param>
    public async Task<IReadOnlyList<string>> ResolveProtonAsync(
        string gameName, string compatDataPath, string? installDir = null,
        CancellationToken ct = default)
    {
        var storeRoot = SteamLayout.RootFromCompatData(compatDataPath);
        var accountIds = SteamLayout.AccountIds(storeRoot);

        // One account is the norm; a shared machine has several and only one of them owns the save.
        // Trying each and taking the first that resolves beats picking arbitrarily, because a wrong
        // account id yields a path that does not exist rather than a plausible-looking wrong one.
        foreach (var resolver in Resolvers(accountIds, id =>
                     PathResolver.Proton(compatDataPath, installDir, storeRoot, id)))
        {
            var dirs = await ResolveSaveDirectoriesAsync(gameName, resolver, ct);
            if (dirs.Count > 0) return dirs;
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Resolve a natively-installed Windows game's save directories, with the per-game placeholders.
    /// Empty on any other platform — see <see cref="HostResolver"/>.
    /// </summary>
    public async Task<IReadOnlyList<string>> ResolveWindowsAsync(
        string gameName, string? installDir = null, string? storeRoot = null,
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<string>();

        foreach (var resolver in Resolvers(SteamLayout.AccountIds(storeRoot), id =>
                     PathResolver.Windows(installDir, storeRoot, id)))
        {
            var dirs = await ResolveSaveDirectoriesAsync(gameName, resolver, ct);
            if (dirs.Count > 0) return dirs;
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// One resolver per Steam account, plus a final id-less one. The id-less attempt matters: the
    /// great majority of games never mention &lt;storeUserId&gt;, and without it a machine whose
    /// userdata cannot be read would resolve nothing at all rather than merely losing the handful
    /// of games that do shard saves per account.
    /// </summary>
    private static IEnumerable<PathResolver> Resolvers(
        IReadOnlyList<string> accountIds, Func<string?, PathResolver> build)
    {
        foreach (var id in accountIds) yield return build(id);
        yield return build(null);
    }

    /// <summary>
    /// The resolver for games running natively on this host — Windows only. There is no Linux
    /// equivalent: a Proton game's paths live in its own prefix (per-game, so the caller must
    /// supply it), and native-Linux builds are out of scope (Decisions.md §1).
    /// </summary>
    public static PathResolver? HostResolver() =>
        OperatingSystem.IsWindows() ? PathResolver.Windows() : null;

    /// <summary>Suggest manifest game names that contain the given substring.</summary>
    public async Task<IReadOnlyList<string>> SearchAsync(string term, int max = 25, CancellationToken ct = default)
    {
        var manifest = await GetManifestAsync(ct: ct);
        return manifest.GameNames
            .Where(n => n.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();
    }
}
