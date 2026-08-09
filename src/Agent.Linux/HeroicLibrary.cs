using System.Text.Json;

namespace SaveLocker.Agent.Linux;

/// <summary>A Windows game installed through Heroic, as its library files describe it.</summary>
/// <param name="AppName">Heroic's id for the game — also the <c>GamesConfig/&lt;name&gt;.json</c> key.</param>
/// <param name="Title">Display name. Falls back to the install folder's name when the runner's
/// installed-games file does not carry one (gogdl and nile both keep titles elsewhere).</param>
/// <param name="InstallPath">The game's directory on the host — this is <c>&lt;base&gt;</c>.</param>
/// <param name="Runner">Which store manager owns it: legendary / gog / nile / sideload.</param>
public sealed record HeroicGame(string AppName, string Title, string InstallPath, string Runner);

/// <summary>
/// Reads Heroic's installed-games files. There is one per store manager and they do not share a
/// shape, so there is one parser each.
///
/// <para>
/// <b>Every parser is individually fault-isolated.</b> Heroic's on-disk format is an implementation
/// detail of a fast-moving Electron app, not an interface anyone promised us. A shape change in one
/// runner must degrade to "no games found for that runner" — never to an exception that costs the
/// user the other three runners and the Steam shortcuts alongside them.
/// </para>
///
/// <para>
/// Native-Linux and macOS entries are skipped: they have no Wine prefix, and native-Linux builds are
/// out of scope (<c>Decisions.md §1</c>).
/// </para>
/// </summary>
public static class HeroicLibrary
{
    public static IReadOnlyList<HeroicGame> Read(string configRoot)
    {
        var games = new List<HeroicGame>();

        games.AddRange(Legendary(
            Path.Combine(configRoot, "legendaryConfig", "legendary", "installed.json")));
        games.AddRange(Legendary(
            Path.Combine(configRoot, "legendaryConfig", "legendary", "third-party-installed.json")));
        games.AddRange(Gog(configRoot));
        games.AddRange(Nile(Path.Combine(configRoot, "nile_config", "nile", "installed.json")));
        games.AddRange(Sideload(Path.Combine(configRoot, "sideload_apps", "library.json")));

        // One row per game. A title can legitimately repeat across runners (the same game owned on
        // both Epic and GOG); the app_name cannot.
        return games
            .Where(g => !string.IsNullOrWhiteSpace(g.InstallPath))
            .GroupBy(g => g.AppName, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>Epic, via legendary: an object keyed by app_name.</summary>
    private static List<HeroicGame> Legendary(string file) =>
        Parse(file, root =>
        {
            var games = new List<HeroicGame>();
            if (root.ValueKind != JsonValueKind.Object) return games;

            foreach (var entry in root.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                if (!IsWindows(Str(entry.Value, "platform"))) continue;
                if (Str(entry.Value, "install_path") is not { } path) continue;

                games.Add(new HeroicGame(
                    Str(entry.Value, "app_name") ?? entry.Name,
                    Str(entry.Value, "title") ?? FolderName(path),
                    path,
                    "legendary"));
            }
            return games;
        }, []);

    /// <summary>
    /// GOG, via gogdl. The installed-games file is an electron-store — <c>{ "installed": [ … ] }</c>
    /// — and its rows carry no title at all; those live in the separate library cache, so the two
    /// are joined here rather than falling back to a folder name for every GOG game.
    /// </summary>
    private static List<HeroicGame> Gog(string configRoot)
    {
        var titles = GogTitles(Path.Combine(configRoot, "gog_library.json"));

        return Parse(Path.Combine(configRoot, "gog_store", "installed.json"), root =>
        {
            var games = new List<HeroicGame>();
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("installed", out var installed) ||
                installed.ValueKind != JsonValueKind.Array)
                return games;

            foreach (var entry in installed.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (!IsWindows(Str(entry, "platform"))) continue;
                if (Str(entry, "install_path") is not { } path) continue;
                if (Str(entry, "appName") is not { } appName) continue;

                games.Add(new HeroicGame(
                    appName,
                    titles.GetValueOrDefault(appName) ?? FolderName(path),
                    path,
                    "gog"));
            }
            return games;
        }, []);
    }

    /// <summary>Amazon, via nile: a flat array of <c>{ id, path }</c>. Titles live in its library cache.</summary>
    private static List<HeroicGame> Nile(string file) =>
        Parse(file, root =>
        {
            var games = new List<HeroicGame>();
            if (root.ValueKind != JsonValueKind.Array) return games;

            foreach (var entry in root.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                // Nile only ever installs Windows builds, so there is no platform field to filter on.
                if (Str(entry, "path") is not { } path) continue;
                if (Str(entry, "id") is not { } id) continue;

                games.Add(new HeroicGame(id, Str(entry, "title") ?? FolderName(path), path, "nile"));
            }
            return games;
        }, []);

    /// <summary>
    /// Sideloaded apps — anything the user pointed Heroic at themselves, including a game installed
    /// from a GOG offline installer. These record an <b>executable</b>, not an install directory,
    /// so <c>&lt;base&gt;</c> is its containing folder.
    /// </summary>
    private static List<HeroicGame> Sideload(string file) =>
        Parse(file, root =>
        {
            var games = new List<HeroicGame>();
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("games", out var list) ||
                list.ValueKind != JsonValueKind.Array)
                return games;

            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (Str(entry, "app_name") is not { } appName) continue;

                string? platform = null, executable = null;
                if (entry.TryGetProperty("install", out var install) &&
                    install.ValueKind == JsonValueKind.Object)
                {
                    platform = Str(install, "platform");
                    executable = Str(install, "executable");
                }
                if (!IsWindows(platform)) continue;

                // folder_name is Heroic's own dirname(executable) and is the more reliable of the
                // two when a sideloaded entry was edited by hand.
                var path = Str(entry, "folder_name")
                           ?? (executable is null ? null : Path.GetDirectoryName(executable));
                if (string.IsNullOrWhiteSpace(path)) continue;

                games.Add(new HeroicGame(
                    appName, Str(entry, "title") ?? FolderName(path), path, "sideload"));
            }
            return games;
        }, []);

    /// <summary>app_name → title from the GOG library cache, empty on any problem.</summary>
    private static Dictionary<string, string> GogTitles(string file) =>
        Parse(file, root =>
        {
            var titles = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("games", out var list) ||
                list.ValueKind != JsonValueKind.Array)
                return titles;

            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (Str(entry, "app_name") is { } name && Str(entry, "title") is { } title)
                    titles[name] = title;
            }
            return titles;
        }, new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// A Heroic platform string we can resolve saves for. Windows-only: a native-Linux or macOS
    /// build has no prefix for the manifest's Windows tokens to resolve inside.
    /// </summary>
    /// <remarks>
    /// A missing platform is treated as Windows. Older Heroic releases omitted the field entirely,
    /// and at that time Windows was the only thing it installed — so absence means Windows, not
    /// "unknown".
    /// </remarks>
    private static bool IsWindows(string? platform) =>
        string.IsNullOrWhiteSpace(platform) ||
        platform.StartsWith("win", StringComparison.OrdinalIgnoreCase);

    private static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
        v.GetString() is { Length: > 0 } s
            ? s
            : null;

    private static string FolderName(string path) =>
        Path.GetFileName(path.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : path;

    /// <summary>Parse one library file, yielding <paramref name="empty"/> on any failure.</summary>
    private static T Parse<T>(string file, Func<JsonElement, T> read, T empty)
    {
        try
        {
            if (!File.Exists(file)) return empty;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            return read(doc.RootElement);
        }
        catch
        {
            return empty;
        }
    }
}
