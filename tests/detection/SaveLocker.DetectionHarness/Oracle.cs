namespace SaveLocker.DetectionHarness;

/// <summary>
/// The harness's ground truth: an expander that knows EVERY placeholder the manifest uses,
/// including the ones production cannot resolve.
///
/// <para>
/// This is the whole reason the harness can measure anything. It materialises dummy save
/// directories at the paths the manifest actually claims, then the production
/// <see cref="SaveLocker.Shared.PathResolver"/> is scored on how many of them it can find. If the
/// fixtures were built with the production resolver instead, every test would pass by construction
/// and the gap we are trying to close would be invisible.
/// </para>
///
/// <para>
/// The Windows-token half is a deliberate mirror of <c>PathResolver.Proton</c>. If the two ever
/// disagree the harness reports false misses, so any change there must be made in both places.
/// </para>
/// </summary>
public sealed class Oracle
{
    private readonly Dictionary<string, string> _tokens;

    /// <summary>The Steam library root — what <c>&lt;root&gt;</c> means for a Steam install.</summary>
    public string SteamRoot { get; }

    /// <summary>The game's <c>compatdata/&lt;appid&gt;</c>, i.e. what Steam passes as STEAM_COMPAT_DATA_PATH.</summary>
    public string CompatData { get; }

    /// <summary>The game's install directory — what <c>&lt;base&gt;</c> means.</summary>
    public string InstallBase { get; }

    /// <summary>Steam's 32-bit account id, the <c>userdata/&lt;id&gt;</c> folder name.</summary>
    public string StoreUserId { get; }

    /// <summary>Everything this game's fixtures may touch. Nothing is created outside it.</summary>
    public string FixtureRoot { get; }

    private Oracle(
        Dictionary<string, string> tokens, string steamRoot, string compatData,
        string installBase, string storeUserId, string fixtureRoot)
    {
        _tokens = tokens;
        SteamRoot = steamRoot;
        CompatData = compatData;
        InstallBase = installBase;
        StoreUserId = storeUserId;
        FixtureRoot = fixtureRoot;
    }

    /// <summary>
    /// Build ground truth for one fake Proton game under <paramref name="fixtureRoot"/>.
    /// </summary>
    /// <param name="storeUserId">
    /// Steam's 32-bit account id — the <c>userdata/&lt;id&gt;</c> folder name, NOT the 64-bit
    /// SteamID64. Games like DRAGON QUEST III nest their saves under it.
    /// </param>
    public static Oracle ForProtonFixture(
        string fixtureRoot, string appId, string? installDirName, string storeUserId)
    {
        var steamRoot = Path.Combine(fixtureRoot, "steam");
        var compatData = Path.Combine(steamRoot, "steamapps", "compatdata", appId);

        const string user = "steamuser";
        var driveC = Path.Combine(compatData, "pfx", "drive_c");
        var userHome = Path.Combine(driveC, "users", user);
        var appData = Path.Combine(userHome, "AppData");

        // <base> is "<root>/<game>" — for Steam, the install folder under steamapps/common.
        var installBase = Path.Combine(
            steamRoot, "steamapps", "common", installDirName ?? "UnknownInstallDir");

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // --- mirrored from PathResolver.Proton ---
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
            // --- the three production does not implement ---
            ["<base>"] = installBase,
            ["<root>"] = steamRoot,
            ["<storeUserId>"] = storeUserId,
        };

        return new Oracle(
            tokens, steamRoot, compatData, installBase, storeUserId,
            Path.GetFullPath(fixtureRoot));
    }

    /// <summary>Which unsupported/unknown token blocks this template, or null if fully expandable.</summary>
    public string? BlockingToken(string template)
    {
        var expanded = Expand(template);
        if (expanded is null)
        {
            var start = template.IndexOf('<');
            var end = template.IndexOf('>', start < 0 ? 0 : start);
            return start >= 0 && end > start ? template[start..(end + 1)] : "<unknown>";
        }
        return null;
    }

    /// <summary>Expand every placeholder, or null if one is unknown even to the oracle.</summary>
    public string? Expand(string template)
    {
        var expanded = template;
        foreach (var (token, value) in _tokens)
            expanded = expanded.Replace(token, value, StringComparison.OrdinalIgnoreCase);

        if (expanded.Contains('<') && expanded.Contains('>')) return null;
        return expanded.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// The concrete directory a template designates: expanded, then cut at the first wildcard
    /// segment, then stripped of a trailing filename. Mirrors what a caller ultimately archives.
    /// </summary>
    public string? DirectoryFor(string template)
    {
        if (Expand(template) is not { } expanded) return null;

        var segments = expanded.Split(Path.DirectorySeparatorChar);
        var kept = new List<string>();
        foreach (var seg in segments)
        {
            if (seg.Contains('*') || seg.Contains('?')) break;
            kept.Add(seg);
        }
        if (kept.Count == 0) return null;

        var path = string.Join(Path.DirectorySeparatorChar, kept);

        // A template naming a specific file designates its containing directory.
        if (kept.Count == segments.Length && Path.HasExtension(path))
            path = Path.GetDirectoryName(path) ?? path;

        // Must land INSIDE this game's fixture root. Being "rooted" is not enough: the manifest's
        // malformed entries (e.g. "/AppData/LocalLow/...", missing its <home>) are absolutely rooted
        // on Linux, and Materialise then tried to mkdir /AppData — permission denied, whole sweep
        // dead. On Windows the same string became E:\AppData and silently succeeded, which is why
        // this only surfaced on ext4. A fixture that writes outside its scratch directory is a bug
        // however the path got that way.
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return null;
        return IsInsideFixtureRoot(path) ? path : null;
    }

    private bool IsInsideFixtureRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = FixtureRoot.TrimEnd(Path.DirectorySeparatorChar);
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.Equals(root, cmp) || full.StartsWith(root + Path.DirectorySeparatorChar, cmp);
    }

    /// <summary>
    /// Create the directory a template designates, and drop a marker file in it so the tree is
    /// never empty — an empty directory is a weaker fixture, since some heuristics ignore them.
    /// Returns the directory, or null when the template cannot be expanded at all.
    /// </summary>
    public string? Materialise(string template, string markerName = "savelocker-fixture.dat")
    {
        if (DirectoryFor(template) is not { } dir) return null;
        Directory.CreateDirectory(dir);
        var marker = Path.Combine(dir, markerName);
        if (!File.Exists(marker)) File.WriteAllText(marker, "fixture\n");
        return dir;
    }
}
