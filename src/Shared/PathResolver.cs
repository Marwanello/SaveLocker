namespace SaveLocker.Shared;

/// <summary>
/// Expands Ludusavi manifest path placeholders (e.g. &lt;winAppData&gt;) into
/// concrete filesystem paths for the current machine, then trims the result at
/// the first wildcard segment so callers get a real directory to watch/archive.
/// </summary>
public sealed class PathResolver
{
    private readonly Dictionary<string, string> _tokens;

    /// <summary>
    /// Tokens holding an identifier rather than a path. <see cref="Tokenize"/> must skip them: they
    /// would match anywhere the name or id happens to appear in a path and rewrite that fragment
    /// into a placeholder, producing a template that expands to nonsense on the next machine.
    /// </summary>
    private static readonly HashSet<string> NonPathTokens =
        new(StringComparer.OrdinalIgnoreCase) { "<osUserName>", "<storeUserId>" };

    public PathResolver(Dictionary<string, string> tokens)
    {
        // Separators are normalised ONCE, here, so every comparison downstream is separator-safe.
        //
        // ResolveToDirectory rewrites the manifest's forward slashes to the platform separator, but
        // token VALUES arrive however the caller built them — and a caller that passes
        // "C:/games/steam" makes a token value that no expanded path will ever prefix-match. That
        // silently defeats both Tokenize and the known-root guard. Replacing '/' is safe on Linux
        // too: DirectorySeparatorChar is '/' there, so it is a no-op rather than a corruption.
        _tokens = tokens.ToDictionary(
            t => t.Key,
            t => NonPathTokens.Contains(t.Key)
                ? t.Value
                : t.Value?.Replace('/', Path.DirectorySeparatorChar) ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Build a resolver populated with this Windows user's known folders.
    /// </summary>
    /// <param name="installDir">
    /// The game's own install directory — what <c>&lt;base&gt;</c> expands to. Per-GAME, not
    /// per-machine, which is why it is a parameter: <c>&lt;base&gt;</c> is the most common token in
    /// the manifest by a wide margin, and without it roughly a third of resolvable games are lost.
    /// </param>
    /// <param name="storeRoot">The store library root the game is installed under — <c>&lt;root&gt;</c>.</param>
    /// <param name="storeUserId">
    /// Steam's 32-bit account id, i.e. the <c>userdata/&lt;id&gt;</c> folder name — NOT the 64-bit
    /// SteamID64. Games that shard saves per account nest them under it.
    /// </param>
    public static PathResolver Windows(
        string? installDir = null, string? storeRoot = null, string? storeUserId = null)
    {
        string Env(string var) => Environment.GetEnvironmentVariable(var) ?? string.Empty;
        string Special(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

        var appData = Special(Environment.SpecialFolder.ApplicationData);            // Roaming
        var localAppData = Special(Environment.SpecialFolder.LocalApplicationData);  // Local
        var localLow = Path.Combine(Env("USERPROFILE"), "AppData", "LocalLow");
        var documents = Special(Environment.SpecialFolder.MyDocuments);
        var home = Env("USERPROFILE");

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["<winAppData>"] = appData,
            ["<winLocalAppData>"] = localAppData,
            ["<winLocalAppDataLow>"] = localLow,
            ["<winDocuments>"] = documents,
            // %PUBLIC% — C:\Users\Public, the profile ROOT, not its Documents folder.
            //
            // This was SpecialFolder.CommonDocuments (C:\Users\Public\Documents), one level too
            // deep. Real manifest entries look like "<winPublic>/Documents/City Interactive/…", so
            // that produced "C:\Users\Public\Documents\Documents\…" — a path that does not exist,
            // silently costing detection for all 44 <winPublic> games on Windows.
            //
            // The Proton map below already had it right (drive_c/users/Public), so Windows and a
            // Deck disagreed by exactly one segment about where the same game's saves live. That is
            // the cross-machine root divergence that makes a restore nest a folder under itself and
            // delete the correctly-placed copy — arrived at with no user error at all.
            ["<winPublic>"] = Env("PUBLIC") is { Length: > 0 } pub
                ? pub
                : Path.GetDirectoryName(Special(Environment.SpecialFolder.CommonDocuments)) ?? "",
            ["<winProgramData>"] = Special(Environment.SpecialFolder.CommonApplicationData),
            ["<winDir>"] = Env("WINDIR"),
            ["<home>"] = home,
            ["<osUserName>"] = Env("USERNAME"),
            // Windows "Saved Games" has no SpecialFolder enum value.
            ["<winSavedGames>"] = Path.Combine(home, "Saved Games"),
        };

        AddGameTokens(tokens, installDir, storeRoot, storeUserId);
        return new PathResolver(tokens);
    }

    /// <summary>
    /// Add the three per-game placeholders shared by both platforms.
    /// <para>
    /// A null or empty value is <b>omitted entirely</b> rather than mapped to "". That distinction
    /// matters: an unknown token left in place trips the leftover-placeholder guard in
    /// <see cref="ResolveToDirectory"/> and yields null — "we cannot resolve this" — whereas an
    /// empty mapping would silently produce a truncated path pointing somewhere real and wrong.
    /// </para>
    /// </summary>
    private static void AddGameTokens(
        Dictionary<string, string> tokens, string? installDir, string? storeRoot, string? storeUserId)
    {
        if (!string.IsNullOrWhiteSpace(installDir)) tokens["<base>"] = installDir;
        if (!string.IsNullOrWhiteSpace(storeRoot)) tokens["<root>"] = storeRoot;
        if (!string.IsNullOrWhiteSpace(storeUserId)) tokens["<storeUserId>"] = storeUserId;
    }

    /// <summary>
    /// Build a resolver for a game running under Proton. The same Windows tokens resolve, but
    /// <b>inside the Wine prefix</b> rather than against this machine's folders — so unlike
    /// <see cref="Windows"/> this map is per-game, not per-machine.
    /// </summary>
    /// <param name="compatDataPath">
    /// The game's <c>compatdata/&lt;appid&gt;</c> directory — exactly what Steam passes as
    /// <c>STEAM_COMPAT_DATA_PATH</c>. The prefix is the <c>pfx</c> subdirectory of it.
    /// </param>
    /// <param name="installDir">
    /// The game's install directory on the HOST filesystem — <c>&lt;base&gt;</c>. Under Proton this
    /// is a native Linux path, not a path inside the prefix: a non-Steam shortcut's is its
    /// <c>StartDir</c>, and a Steam game's is <c>steamapps/common/&lt;installdir&gt;</c>.
    /// </param>
    /// <param name="storeRoot">The Steam library root — <c>&lt;root&gt;</c>. Also a host path.</param>
    /// <param name="storeUserId">
    /// Steam's 32-bit account id, i.e. the <c>userdata/&lt;id&gt;</c> folder name — NOT SteamID64.
    /// </param>
    public static PathResolver Proton(
        string compatDataPath,
        string? installDir = null,
        string? storeRoot = null,
        string? storeUserId = null)
    {
        // Proton runs every game as the fixed Wine user "steamuser".
        const string user = "steamuser";
        var driveC = Path.Combine(compatDataPath, "pfx", "drive_c");
        var userHome = Path.Combine(driveC, "users", user);
        var appData = Path.Combine(userHome, "AppData");

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["<winAppData>"] = Path.Combine(appData, "Roaming"),
            ["<winLocalAppData>"] = Path.Combine(appData, "Local"),
            ["<winLocalAppDataLow>"] = Path.Combine(appData, "LocalLow"),
            ["<winDocuments>"] = Path.Combine(userHome, "Documents"),
            ["<winPublic>"] = Path.Combine(driveC, "users", "Public"),
            ["<winProgramData>"] = Path.Combine(driveC, "ProgramData"),
            ["<winDir>"] = Path.Combine(driveC, "windows"),
            ["<home>"] = userHome,
            ["<osUserName>"] = user,
            ["<winSavedGames>"] = Path.Combine(userHome, "Saved Games"),
        };

        AddGameTokens(tokens, installDir, storeRoot, storeUserId);
        return new PathResolver(tokens);
    }

    /// <summary>
    /// The reverse of <see cref="ResolveToDirectory"/>: rewrite a concrete path from THIS machine
    /// back into a template, or null when nothing matches.
    /// <para>
    /// This is what lets one correctly-configured machine describe a save location in terms every
    /// other machine can expand for itself. Storing the literal path instead is what allows two
    /// machines to disagree about the same game's save root — and a root that differs by even one
    /// segment makes a restore nest a folder under itself and delete the correctly-placed copy.
    /// </para>
    /// <para>
    /// Longest value wins, so <c>C:\Users\me\Documents</c> becomes <c>&lt;winDocuments&gt;</c> rather
    /// than <c>&lt;home&gt;/Documents</c>. <c>&lt;osUserName&gt;</c> is excluded: it holds a name, not
    /// a path, and would match anywhere the username happens to appear.
    /// </para>
    /// </summary>
    public string? Tokenize(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return null;

        var path = absolutePath.Replace('/', Path.DirectorySeparatorChar)
                               .TrimEnd(Path.DirectorySeparatorChar);
        var cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var best = _tokens
            .Where(t => !NonPathTokens.Contains(t.Key) && !string.IsNullOrEmpty(t.Value))
            .Select(t => (t.Key, Value: t.Value.TrimEnd(Path.DirectorySeparatorChar)))
            .Where(t => path.Equals(t.Value, cmp) ||
                        path.StartsWith(t.Value + Path.DirectorySeparatorChar, cmp))
            .OrderByDescending(t => t.Value.Length)
            .FirstOrDefault();

        if (best.Key is null) return null;

        var rest = path[best.Value.Length..].TrimStart(Path.DirectorySeparatorChar);
        // Manifest templates use forward slashes, and ResolveToDirectory converts back on expansion.
        return rest.Length == 0 ? best.Key : $"{best.Key}/{rest.Replace('\\', '/')}";
    }

    /// <summary>
    /// True when an expanded path actually sits under one of this resolver's own roots. Anything
    /// else came from a template we did not fully expand, and is not ours to return.
    /// </summary>
    private bool StartsWithKnownRoot(string path)
    {
        var cmp = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return _tokens
            .Where(t => !NonPathTokens.Contains(t.Key) && !string.IsNullOrEmpty(t.Value))
            .Select(t => t.Value.TrimEnd(Path.DirectorySeparatorChar))
            .Any(root => path.Equals(root, cmp) ||
                         path.StartsWith(root + Path.DirectorySeparatorChar, cmp));
    }

    /// <summary>True when this value is a template rather than a concrete path.</summary>
    public static bool IsTemplate(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains('<') && value.Contains('>');

    /// <summary>
    /// Expand placeholders in a template and return the deepest directory prefix
    /// that precedes any wildcard. Returns null if a required token is unknown.
    /// </summary>
    public string? ResolveToDirectory(string template)
    {
        var expanded = template;
        foreach (var (token, value) in _tokens)
        {
            if (expanded.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(value)) return null; // unresolved on this machine
                expanded = expanded.Replace(token, value, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Any leftover <...> placeholder means we can't resolve it here.
        if (expanded.Contains('<') && expanded.Contains('>'))
            return null;

        expanded = expanded.Replace('/', Path.DirectorySeparatorChar);

        // Trim at the first wildcard segment so we return a concrete directory.
        var segments = expanded.Split(Path.DirectorySeparatorChar);
        var kept = new List<string>();
        foreach (var seg in segments)
        {
            if (seg.Contains('*') || seg.Contains('?'))
                break;
            kept.Add(seg);
        }

        if (kept.Count == 0) return null;
        var path = string.Join(Path.DirectorySeparatorChar, kept);

        // A template that expands to something not rooted in one of OUR tokens is refused.
        //
        // The community manifest contains malformed entries with no placeholder at all — e.g.
        // "/AppData/LocalLow/Strange Scaffold/.../Savegames" for Space Warlord Organ Trading
        // Simulator, which is missing its <home>. Left alone, Path.GetFullPath anchors that to the
        // CURRENT DRIVE, so the agent can hand back "E:\AppData\..." — a real path outside every
        // prefix and profile we know about, presented to the user as a confident answer. Found by
        // tests/detection.
        if (!StartsWithKnownRoot(path)) return null;

        // If the template pointed at a specific file (last kept segment has an
        // extension and there were no wildcards after it), use its directory.
        if (kept.Count == segments.Length && Path.HasExtension(path) && !Directory.Exists(path))
            path = Path.GetDirectoryName(path) ?? path;

        return path;
    }
}
