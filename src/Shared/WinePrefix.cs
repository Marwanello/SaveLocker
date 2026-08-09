namespace SaveLocker.Shared;

/// <summary>
/// The two things a Wine prefix must tell us before any manifest token can be resolved inside it:
/// where <c>drive_c</c> is, and which Wine user the game runs as.
/// <para>
/// Steam answers both by convention — <c>compatdata/&lt;appid&gt;/pfx/drive_c</c>, user
/// <c>steamuser</c> — which is why <see cref="PathResolver.Proton"/> can hardcode them. Nothing
/// else does. Heroic nests <c>pfx</c> only when the runner is Proton, and a plain Wine runner runs
/// the game as the LINUX user, so a prefix created by one and read with the other's assumptions
/// resolves every path to a directory that does not exist.
/// </para>
/// </summary>
public sealed record WinePrefixLayout(string Root, string DriveC, string UserName)
{
    /// <summary>The Wine user's profile directory — <c>&lt;home&gt;</c>.</summary>
    public string UserHome => Path.Combine(DriveC, "users", UserName);

    /// <summary>True when this prefix has Proton's shape (a <c>pfx</c> level).</summary>
    public bool IsProtonShaped =>
        Path.GetFileName(Path.GetDirectoryName(DriveC)) == "pfx";
}

/// <summary>
/// Identifies the layout of an arbitrary Wine prefix by looking at it. This is the only part of
/// prefix handling that touches disk — <see cref="PathResolver"/> stays pure, so the detection
/// harness can score it against trees that were never created by a real Wine.
/// </summary>
public static class WinePrefix
{
    /// <summary>
    /// Inspect <paramref name="prefixRoot"/>, or null when it holds no prefix at all.
    /// <para>
    /// Null is a meaningful answer and must stay distinguishable from "a prefix with nothing in
    /// it": the first means we should not claim a save path for this game, the second means the
    /// game has simply not been run yet.
    /// </para>
    /// </summary>
    public static WinePrefixLayout? Locate(string? prefixRoot)
    {
        if (string.IsNullOrWhiteSpace(prefixRoot)) return null;

        // Proton first: a Proton prefix has BOTH a pfx/drive_c and, sometimes, stray top-level
        // directories. Checking the nested form first stops a half-created prefix from being read
        // as a plain Wine one.
        var driveC = FirstExisting(
            Path.Combine(prefixRoot, "pfx", "drive_c"),
            Path.Combine(prefixRoot, "drive_c"));

        return driveC is null ? null : new WinePrefixLayout(prefixRoot, driveC, UserIn(driveC));
    }

    /// <summary>
    /// Build a resolver for a game in an arbitrary Wine prefix, or null when there is no prefix
    /// there. The per-game placeholders are the caller's to supply, exactly as for Proton.
    /// </summary>
    public static PathResolver? ResolverFor(
        string? prefixRoot, string? installDir = null, string? storeRoot = null,
        string? storeUserId = null)
    {
        var layout = Locate(prefixRoot);
        return layout is null
            ? null
            : PathResolver.Wine(layout.DriveC, layout.UserName, installDir, storeRoot, storeUserId);
    }

    /// <summary>
    /// The Wine prefix that <paramref name="path"/> lives inside, or null if it is not in one.
    /// <para>
    /// A game installed by running an installer <i>inside</i> a prefix — which is what a GOG offline
    /// installer under Heroic is — has an install directory like
    /// <c>…/Prefixes/Foo/drive_c/GOG Games/Foo</c>. The prefix that contains it is a fact about
    /// where the game's files actually are, and it does not always agree with the prefix the
    /// launcher is configured to run the game in. When they disagree, something is worth saying out
    /// loud rather than silently picking one.
    /// </para>
    /// </summary>
    public static string? ContainingPrefix(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var i = Array.FindLastIndex(
            parts, p => p.Equals("drive_c", StringComparison.OrdinalIgnoreCase));
        if (i <= 0) return null;

        // drive_c's parent is the prefix — unless it is Proton's `pfx`, one level deeper.
        var depth = i > 1 && parts[i - 1].Equals("pfx", StringComparison.OrdinalIgnoreCase)
            ? i - 1
            : i;

        var root = string.Join(Path.DirectorySeparatorChar, parts[..depth]);
        return string.IsNullOrEmpty(root) ? null : root;
    }

    /// <summary>
    /// Where a path browser should open for a game whose save folder we could not guess: the Wine
    /// user's profile directory, falling back to <c>drive_c</c>, the prefix root, and finally null.
    /// <para>
    /// This walks the prefix's ACTUAL layout rather than a literal <c>pfx/drive_c/users/steamuser</c>.
    /// Against a Heroic Wine prefix the literal path matches nothing, so the browser used to drop
    /// the user at the prefix root and leave them to find <c>drive_c</c> themselves — on a Deck, with
    /// a gamepad.
    /// </para>
    /// </summary>
    public static string? BrowseStart(string? prefixRoot)
    {
        if (string.IsNullOrWhiteSpace(prefixRoot)) return null;

        var layout = Locate(prefixRoot);
        if (layout is null) return Directory.Exists(prefixRoot) ? prefixRoot : null;

        return Directory.Exists(layout.UserHome) ? layout.UserHome : layout.DriveC;
    }

    /// <summary>
    /// The Wine user a game in this prefix runs as. <c>steamuser</c> when it is there, otherwise
    /// the sole real profile under <c>users/</c>.
    /// <para>
    /// Only a SOLE profile counts. Wine leaves <c>Public</c> behind and often a symlinked profile
    /// named after the Linux user; picking arbitrarily from several would silently resolve half a
    /// game's paths into the wrong profile. Falling back to <c>steamuser</c> makes the failure a
    /// path that does not exist — which the resolver already reports as "unresolved" — rather than
    /// a path that exists and is wrong.
    /// </para>
    /// </summary>
    private static string UserIn(string driveC)
    {
        const string steamUser = "steamuser";
        var users = Path.Combine(driveC, "users");
        if (Directory.Exists(Path.Combine(users, steamUser))) return steamUser;

        try
        {
            var profiles = Directory.EnumerateDirectories(users)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(n => !n.Equals("Public", StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            return profiles.Count == 1 ? profiles[0] : steamUser;
        }
        catch
        {
            return steamUser;
        }
    }

    private static string? FirstExisting(params string[] paths) =>
        paths.FirstOrDefault(Directory.Exists);
}
