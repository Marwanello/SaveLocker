namespace SaveLocker.Agent;

/// <summary>
/// The parts of Steam's on-disk layout that are the same on every platform, so they can live in
/// Core. Finding Steam in the first place is NOT here — that is a registry read on Windows and a
/// set of known directories on Linux, which is why <c>IGameScanner</c> hosts own it.
/// </summary>
public static class SteamLayout
{
    /// <summary>
    /// The Steam library root a compatdata prefix belongs to, i.e. three levels above
    /// <c>{root}/steamapps/compatdata/{appid}</c>. Null if the path is not that shape.
    /// </summary>
    public static string? RootFromCompatData(string? compatDataPath)
    {
        if (string.IsNullOrWhiteSpace(compatDataPath)) return null;

        var compatDir = Path.GetDirectoryName(compatDataPath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (compatDir is null) return null;

        var steamapps = Path.GetDirectoryName(compatDir);
        var root = steamapps is null ? null : Path.GetDirectoryName(steamapps);

        // Verified rather than assumed: a caller may hand us a prefix from somewhere else entirely,
        // and silently treating its grandparent as a Steam root would put <root> and <base> on a
        // tree that has nothing to do with Steam.
        return root is not null &&
               string.Equals(Path.GetFileName(compatDir), "compatdata", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(Path.GetFileName(steamapps!), "steamapps", StringComparison.OrdinalIgnoreCase)
            ? root
            : null;
    }
}
