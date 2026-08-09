using System.Text.Json;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Locates Heroic Games Launcher and answers the one question that matters per game: which Wine
/// prefix does it run in?
///
/// <para>
/// Heroic's default install root is <c>~/Games/Heroic</c>, but that is a fallback and not the
/// mechanism — the user can override it globally and per game, and Heroic has shipped releases
/// where it ignored its own global override. The library files hold the real <c>install_path</c>,
/// so those are what we read. See <see cref="HeroicLibrary"/>.
/// </para>
/// </summary>
public static class HeroicRoots
{
    /// <summary>The Flatpak app id — how a Deck almost always has Heroic installed.</summary>
    private const string FlatpakId = "com.heroicgameslauncher.hgl";

    /// <summary>
    /// Every Heroic config root on this machine. <b>An empty list is the answer to "is Heroic
    /// installed?"</b> — there is no process probe and no <c>which</c>, because a launcher that is
    /// merely not running right now is still a launcher whose games we must find.
    /// </summary>
    /// <remarks>
    /// A directory counts only if it holds <c>config.json</c>. Uninstalling Heroic can leave the
    /// directory behind, and an empty shell would otherwise be reported as an installation whose
    /// games had all vanished.
    /// </remarks>
    public static IReadOnlyList<string> Find()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(xdg)) candidates.Add(Path.Combine(xdg, "heroic"));
        candidates.Add(Path.Combine(home, ".config", "heroic"));
        candidates.Add(Path.Combine(home, ".var", "app", FlatpakId, "config", "heroic"));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var roots = new List<string>();
        foreach (var c in candidates)
        {
            if (!File.Exists(Path.Combine(c, "config.json"))) continue;
            var real = RealPath(c);
            if (seen.Add(real)) roots.Add(real);
        }
        return roots;
    }

    /// <summary>Heroic's default install root — <c>~/Games/Heroic</c>. Used only for browsing.</summary>
    public static string DefaultInstallRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games", "Heroic");

    /// <summary>
    /// Extra roots the path browser may descend into. Heroic installs under <c>$HOME</c> by
    /// default so this is usually redundant, but a Deck owner who redirected Heroic to the SD card
    /// otherwise cannot browse to a game they can plainly see.
    /// </summary>
    public static IEnumerable<string> BrowseRoots()
    {
        foreach (var root in Find())
        {
            yield return root;
            if (InstallRoot(root) is { } install && Directory.Exists(install)) yield return install;
        }

        var fallback = DefaultInstallRoot();
        if (Directory.Exists(fallback)) yield return fallback;
    }

    /// <summary>
    /// The Wine prefix a specific game runs in, or null when Heroic has never recorded one.
    /// <para>
    /// <c>GamesConfig/&lt;app_name&gt;.json</c> is authoritative and per game; the global default is
    /// only what a game that has never been configured inherits. Both are consulted before the
    /// documented default, and a prefix that does not exist on disk is reported as null — the game
    /// has not been launched yet, and pointing the resolver at an absent tree would produce
    /// confident nonsense.
    /// </para>
    /// </summary>
    public static string? PrefixFor(string configRoot, string appName)
    {
        var perGame = Path.Combine(configRoot, "GamesConfig", $"{appName}.json");
        if (ReadStringMember(perGame, appName, "winePrefix") is { } prefix &&
            Directory.Exists(prefix))
            return prefix;

        foreach (var fallback in DefaultPrefixes(configRoot))
            if (Directory.Exists(fallback)) return fallback;

        return null;
    }

    /// <summary>The prefixes a game with no per-game setting would inherit, best first.</summary>
    private static IEnumerable<string> DefaultPrefixes(string configRoot)
    {
        var config = Path.Combine(configRoot, "config.json");
        foreach (var key in new[] { "winePrefix", "defaultWinePrefix" })
            if (ReadStringMember(config, "defaultSettings", key) is { } p)
                yield return p;

        var prefixes = Path.Combine(DefaultInstallRoot(), "Prefixes");
        yield return Path.Combine(prefixes, "default");
        yield return Path.Combine(prefixes, "shared");
    }

    /// <summary>
    /// Heroic's install root as configured, falling back to the documented default. Only used to
    /// give the path browser somewhere sensible to start.
    /// </summary>
    private static string? InstallRoot(string configRoot) =>
        ReadStringMember(Path.Combine(configRoot, "config.json"),
                         "defaultSettings", "defaultInstallPath")
        ?? DefaultInstallRoot();

    /// <summary>
    /// Read <c>{ "&lt;section&gt;": { "&lt;member&gt;": "…" } }</c> from a JSON file, or null.
    /// <para>
    /// Every failure — missing file, unreadable file, malformed JSON, a member that turned into an
    /// object in a later Heroic release — collapses to null on purpose. Heroic's on-disk format is
    /// not an API we control, and a scan that throws finds no games at all, whereas a scan that
    /// returns null for one game still finds the rest.
    /// </para>
    /// </summary>
    private static string? ReadStringMember(string file, string section, string member)
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            if (!doc.RootElement.TryGetProperty(section, out var s) ||
                s.ValueKind != JsonValueKind.Object ||
                !s.TryGetProperty(member, out var v) ||
                v.ValueKind != JsonValueKind.String)
                return null;

            var value = v.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static string RealPath(string path)
    {
        try
        {
            var resolved = Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName;
            return Path.GetFullPath(resolved ?? path);
        }
        catch
        {
            return Path.GetFullPath(path);
        }
    }
}
