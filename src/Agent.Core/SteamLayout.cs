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

    /// <summary>
    /// The AppID out of any path that runs through <c>compatdata/&lt;appid&gt;/</c> — a prefix, or a
    /// save folder inside one. Null when the path does not go through a prefix at all (a portable
    /// game beside its .exe, or a native save location).
    ///
    /// <para>
    /// A prefix directory is <b>named</b> for the AppID that launches into it, so a save folder
    /// mapped inside one already identifies its game. That matters because the AppID is what the
    /// launch wrapper matches on, and a tracked game that never recorded one is invisible to it: the
    /// wrapper logs "no tracked game matches this launch" and the session syncs nothing, while every
    /// other sign — the game is tracked, the folder is mapped, the launch options are set — says it
    /// should be working. Seen on real hardware 2026-08-15 on two of four tracked games.
    /// </para>
    /// </summary>
    public static string? CompatDataIdIn(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var i = Array.FindIndex(parts, p => p.Equals("compatdata", StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= parts.Length) return null;
        var id = parts[i + 1];
        // Only a numeric segment: `compatdata/whatever/` is not an AppID, and handing a non-numeric
        // one to the wrapper's matcher would be worse than having none.
        return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : null;
    }
}
