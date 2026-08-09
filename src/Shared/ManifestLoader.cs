using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SaveLocker.Shared;

/// <summary>
/// Loads and queries the community Ludusavi manifest
/// (https://github.com/mtkennerly/ludusavi-manifest), which maps thousands of
/// games to their save-data locations. We use it as the data source for save
/// detection rather than maintaining our own database.
/// </summary>
public sealed class ManifestLoader
{
    public const string DefaultManifestUrl =
        "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";

    private readonly Dictionary<string, ManifestGame> _games;

    private ManifestLoader(Dictionary<string, ManifestGame> games) => _games = games;

    public int GameCount => _games.Count;

    public IEnumerable<string> GameNames => _games.Keys;

    /// <summary>Parse a manifest from YAML text.</summary>
    public static ManifestLoader Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var raw = deserializer.Deserialize<Dictionary<string, ManifestGame>>(yaml)
                  ?? new Dictionary<string, ManifestGame>();

        // Case-insensitive lookup by game name. The community manifest contains
        // entries that differ only in case (e.g. "Afterlife" vs "afterlife"), so
        // add one-by-one and keep the first — the ctor overload would throw.
        var games = new Dictionary<string, ManifestGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, game) in raw)
            games.TryAdd(name, game);
        return new ManifestLoader(games);
    }

    /// <summary>Load from a local cached manifest file.</summary>
    public static ManifestLoader LoadFromFile(string path) => Parse(File.ReadAllText(path));

    /// <summary>Download the manifest and cache it to <paramref name="cachePath"/>.</summary>
    public static async Task<ManifestLoader> DownloadAsync(
        string cachePath,
        string url = DefaultManifestUrl,
        HttpClient? http = null,
        CancellationToken ct = default)
    {
        var client = http ?? new HttpClient();
        var yaml = await client.GetStringAsync(url, ct);
        var dir = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(cachePath, yaml, ct);
        return Parse(yaml);
    }

    public bool TryGetGame(string name, out ManifestGame game) =>
        _games.TryGetValue(name, out game!);

    /// <summary>
    /// Resolve the concrete, existing save directories for a game on this machine.
    /// Path templates with placeholders (e.g. &lt;winAppData&gt;/Celeste) are expanded
    /// and trimmed at the first wildcard so we return a directory to watch/archive.
    /// </summary>
    public IReadOnlyList<string> ResolveSaveDirectories(string gameName, PathResolver? resolver = null)
    {
        if (!TryGetGame(gameName, out var game) || game.Files is null)
            return Array.Empty<string>();

        resolver ??= PathResolver.Windows();

        // Only SAVE paths, and only ones that apply to Windows.
        //
        // Every entry used to be considered, which had two consequences the caller could not see.
        // Callers take the first result, and the order was a HashSet's — so a game could be mapped
        // to its settings folder instead of its saves. DRAGON QUEST III does exactly that: its save
        // path needs <storeUserId>, its sibling "Steam/Config/WindowsNoEditor" does not, so the
        // config folder was the only one that resolved and it won. Roughly 4% of sampled games hit
        // this (tests/detection). Returning nothing is strictly better: the user is asked to pick,
        // instead of being handed a wrong path presented as certain.
        //
        // Ordering is the template order in the manifest, de-duplicated, so the first result is
        // stable across runs rather than depending on hash iteration.
        var results = new List<string>();
        var seen = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var (template, entry) in game.Files)
        {
            if (!IsWindowsSave(entry)) continue;

            var dir = resolver.ResolveToDirectory(template);
            if (dir is null || !Directory.Exists(dir)) continue;

            var full = Path.GetFullPath(dir);
            if (seen.Add(full)) results.Add(full);
        }

        return results;
    }

    /// <summary>
    /// A file entry that describes a save on Windows. Absent <c>tags</c> or <c>when</c> mean
    /// "unspecified", which the manifest treats as applying — so absence must never be read as
    /// exclusion, or we would drop thousands of perfectly good entries.
    /// </summary>
    private static bool IsWindowsSave(ManifestFileEntry? entry)
    {
        var tagsOk = entry?.Tags is not { Count: > 0 } tags ||
                     tags.Contains("save", StringComparer.OrdinalIgnoreCase);

        var osOk = entry?.When is not { Count: > 0 } when ||
                   when.Any(w => string.IsNullOrEmpty(w?.Os) ||
                                 w.Os.Equals("windows", StringComparison.OrdinalIgnoreCase));

        return tagsOk && osOk;
    }

    // ----- Manifest schema (only the parts we use) -----

    public sealed class ManifestGame
    {
        // The value is nullable: "some/path:" with no tags block deserialises to null, not an
        // empty entry, and dereferencing it would take down the whole scan.
        [YamlMember(Alias = "files")]
        public Dictionary<string, ManifestFileEntry?>? Files { get; set; }

        [YamlMember(Alias = "installDir")]
        public Dictionary<string, object>? InstallDir { get; set; }
    }

    public sealed class ManifestFileEntry
    {
        [YamlMember(Alias = "tags")]
        public List<string>? Tags { get; set; }

        /// <summary>Applicability conditions. Only <c>os</c> is used; <c>store</c> is not, because
        /// the agent does not always know which store a given install came from.</summary>
        [YamlMember(Alias = "when")]
        public List<ManifestWhen?>? When { get; set; }
    }

    public sealed class ManifestWhen
    {
        [YamlMember(Alias = "os")]
        public string? Os { get; set; }
    }
}
