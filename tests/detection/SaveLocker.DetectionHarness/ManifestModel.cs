using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SaveLocker.DetectionHarness;

/// <summary>
/// A deliberately RICHER view of the Ludusavi manifest than
/// <see cref="SaveLocker.Shared.ManifestLoader"/> parses.
///
/// <para>
/// The harness has to know things production currently throws away — <c>tags</c> (save vs config),
/// <c>when</c> (os/store applicability), <c>installDir</c> and the Steam id — because those are what
/// decide whether a resolved directory is the RIGHT one. If the harness reused the production
/// schema it could only ever measure "did we return something", never "did we return the save
/// folder rather than the settings folder", which is the failure DRAGON QUEST III exhibits.
/// </para>
/// </summary>
public static class ManifestModel
{
    public static Dictionary<string, Game> Parse(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var raw = deserializer.Deserialize<Dictionary<string, Game>>(yaml)
                  ?? new Dictionary<string, Game>();

        // Same reasoning as ManifestLoader: the community manifest holds keys differing only in
        // case, and the ctor overload would throw on the second one.
        var games = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, game) in raw)
            if (game is not null) games.TryAdd(name, game);
        return games;
    }

    public sealed class Game
    {
        [YamlMember(Alias = "files")]
        public Dictionary<string, FileEntry?>? Files { get; set; }

        [YamlMember(Alias = "installDir")]
        public Dictionary<string, object?>? InstallDir { get; set; }

        [YamlMember(Alias = "steam")]
        public SteamEntry? Steam { get; set; }

        /// <summary>The install folder name, which is what <c>&lt;base&gt;</c> expands through.</summary>
        public string? InstallDirName => InstallDir?.Keys.FirstOrDefault();

        /// <summary>
        /// Templates that describe an actual SAVE on Windows/Proton. Everything else is noise for
        /// our purposes: a config-only path is not what the user asked us to sync, and a mac or
        /// Linux-native path is out of scope entirely (Decisions.md — never sync a native-Linux
        /// save into a Windows install).
        /// </summary>
        public IEnumerable<string> WindowsSaveTemplates =>
            (Files ?? new()).Where(kv => IsWindowsSave(kv.Value)).Select(kv => kv.Key);

        /// <summary>Templates tagged config-only — resolving to one of these is a WRONG answer.</summary>
        public IEnumerable<string> WindowsConfigOnlyTemplates =>
            (Files ?? new()).Where(kv => IsWindowsScoped(kv.Value) && !HasTag(kv.Value, "save"))
                            .Select(kv => kv.Key);

        private static bool IsWindowsSave(FileEntry? f) => IsWindowsScoped(f) && HasTag(f, "save");

        // A missing tag list means untagged, which Ludusavi treats as save-ish; we do the same
        // rather than dropping the entry, because dropping it would understate real coverage.
        private static bool HasTag(FileEntry? f, string tag) =>
            f?.Tags is null || f.Tags.Count == 0 || f.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

        // A missing `when` means "applies everywhere", so absence must NOT be read as exclusion.
        private static bool IsWindowsScoped(FileEntry? f) =>
            f?.When is null || f.When.Count == 0 ||
            f.When.Any(w => w?.Os is null || w.Os.Equals("windows", StringComparison.OrdinalIgnoreCase));
    }

    public sealed class FileEntry
    {
        [YamlMember(Alias = "tags")] public List<string>? Tags { get; set; }
        [YamlMember(Alias = "when")] public List<WhenEntry?>? When { get; set; }
    }

    public sealed class WhenEntry
    {
        [YamlMember(Alias = "os")] public string? Os { get; set; }
        [YamlMember(Alias = "store")] public string? Store { get; set; }
    }

    public sealed class SteamEntry
    {
        [YamlMember(Alias = "id")] public long? Id { get; set; }
    }
}
