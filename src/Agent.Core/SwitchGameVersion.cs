using System.Text.RegularExpressions;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// Which version of a Switch game an emulator runs: the base game's, or the title update's installed over it. A Switch game
/// writes its save in its own version's format, and an older version can't read it — Animal Crossing says the save data is
/// damaged (seen 2026-10-16: the Deck's Yuzu ran 1.6.0, the PC's Eden the base 1.0.0, and the island's header said 1.6.0).
/// Nothing readable without the console's keys says what is installed, so this asks the emulator:
/// <list type="bullet">
/// <item>a Yuzu fork's log names the version each time it boots a game —
/// <c>Booting game: 01006F8002326000 | Animal Crossing: New Horizons (64-bit) | 1.6.0</c> — in <c>log/&lt;fork&gt;_log.txt</c>
/// and the one before it, <c>…_log.txt.old.txt</c>;</item>
/// <item>Yuzu's game list cache, <c>game_list/&lt;TID&gt;.pv.txt</c> (<c>Update (1.6.0)</c>, <c>[D] </c> when turned off),
/// written when the game list is read, so it can know an update the game hasn't started with yet (Eden writes none);</item>
/// <item>Ryujinx's logs, <c>Logs/Ryujinx_*.log</c>: <c>Application Loaded: … v1.6.0 [01006f8002326000]</c>.</item>
/// </list>
/// The newer of the cache and the log wins. Null when neither says: the emulator never ran the game, or keeps no log.
/// </summary>
public static partial class SwitchGameVersion
{
    public const string BaseVersion = "1.0.0";

    // The boot line can be anywhere in a log, so a whole log is read, up to this much.
    private const long MaxLogBytes = 64L * 1024 * 1024;
    private const int RyujinxLogsRead = 10;

    [GeneratedRegex(@"Booting game: ([0-9A-Fa-f]{16}) \| .* \| (\S+)\s*$")]
    private static partial Regex YuzuBoot();

    [GeneratedRegex(@"Application Loaded: .* v(\S+) \[([0-9A-Fa-f]{16})\]")]
    private static partial Regex RyujinxBoot();

    [GeneratedRegex(@"^(\[D\] )?Update \(([^)]+)\)\s*$")]
    private static partial Regex CachedUpdate();

    /// <summary>The version of <paramref name="titleId"/> that <paramref name="setup"/> runs, or null when it never said.</summary>
    public static string? Of(SwitchEmulators.Setup setup, string titleId)
    {
        if (setup.Data is not { } data) return null;
        var logged = setup.IsRyujinx ? FromRyujinxLogs(data, titleId) : FromYuzuLogs(data, titleId);
        var cached = setup.IsRyujinx ? null : FromGameListCache(data, titleId);
        if (logged is null) return cached?.Version;
        if (cached is null) return logged.Value.Version;
        return cached.Value.At >= logged.Value.At ? cached.Value.Version : logged.Value.Version;
    }

    /// <summary>
    /// What a Switch game's save is made with, for the DLC check (tasks/emulator-saves Phase 5c): the title update when the
    /// emulator holding the synced save runs one, empty for the base game, null when the version isn't known — which turns
    /// the check off rather than holding a pull for nothing.
    /// </summary>
    public static ContentPackageDto[]? Inventory(TrackedGame game, IReadOnlyList<SwitchEmulators.Setup>? setups = null)
    {
        if (SwitchCopies.TitleId(game) is not { } title) return null;
        if (SwitchCopies.HoldingSetup(game, setups ?? SwitchEmulators.Find()) is not { } setup) return null;
        return Package(title, Of(setup, title));
    }

    /// <summary>The package that says <paramref name="version"/> of <paramref name="titleId"/>: none for the base game.</summary>
    public static ContentPackageDto[]? Package(string titleId, string? version)
    {
        if (ContentPackageDto.ParseVersion(version) is not { } v) return null;
        if (v <= ContentPackageDto.ParseVersion(BaseVersion)) return [];
        return [new ContentPackageDto(ContentPackageDto.Update, UpdateId(titleId), $"Update {version}", 0, version)];
    }

    /// <summary>A title update's own ID: the game's, with <c>800</c> for its last three digits.</summary>
    public static string UpdateId(string titleId) => titleId.Length == 16 ? titleId[..13].ToUpperInvariant() + "800" : titleId;

    /// <summary>The version a package list says (<see cref="Package"/>'s inverse): the update's, else the base game's.</summary>
    public static string? VersionOf(IReadOnlyList<ContentPackageDto>? content) =>
        content is null ? null : content.FirstOrDefault(p => p.Kind == ContentPackageDto.Update)?.Version ?? BaseVersion;

    /// <summary>Is <paramref name="save"/> from a newer version than <paramref name="runs"/>? False when either is unknown.</summary>
    public static bool IsNewer(string? save, string? runs) =>
        ContentPackageDto.ParseVersion(save) is { } s && ContentPackageDto.ParseVersion(runs) is { } r && s > r;

    private static (string Version, DateTime At)? FromYuzuLogs(string data, string titleId)
    {
        var dir = Path.Combine(data, "log");
        if (!Directory.Exists(dir)) return null;
        IEnumerable<FileInfo> logs;
        try { logs = new DirectoryInfo(dir).GetFiles("*_log.txt*").OrderByDescending(f => f.LastWriteTimeUtc).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        foreach (var log in logs)
            if (LastMatch(log, YuzuBoot(), m => Is(m.Groups[1].Value, titleId) ? m.Groups[2].Value : null) is { } v)
                return (v, log.LastWriteTimeUtc);
        return null;
    }

    private static (string Version, DateTime At)? FromRyujinxLogs(string data, string titleId)
    {
        var dir = Path.Combine(data, "Logs");
        if (!Directory.Exists(dir)) return null;
        IEnumerable<FileInfo> logs;
        try { logs = new DirectoryInfo(dir).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(RyujinxLogsRead).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        foreach (var log in logs)
            if (LastMatch(log, RyujinxBoot(), m => Is(m.Groups[2].Value, titleId) ? m.Groups[1].Value : null) is { } v)
                return (v, log.LastWriteTimeUtc);
        return null;
    }

    private static (string Version, DateTime At)? FromGameListCache(string data, string titleId)
    {
        foreach (var dir in CacheDirs(data))
        {
            var file = new FileInfo(Path.Combine(dir, "game_list", titleId.ToUpperInvariant() + ".pv.txt"));
            if (!file.Exists) continue;
            string[] lines;
            try { lines = File.ReadAllLines(file.FullName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            var update = lines.Select(l => CachedUpdate().Match(l)).FirstOrDefault(m => m.Success && !m.Groups[1].Success);
            return (update?.Groups[2].Value ?? BaseVersion, file.LastWriteTimeUtc);
        }
        return null;
    }

    // Windows keeps a fork's cache in its own folder; Linux in $XDG_CACHE_HOME/<fork>, a Flatpak in its app's cache.
    private static IEnumerable<string> CacheDirs(string data)
    {
        yield return Path.Combine(data, "cache");
        if (OperatingSystem.IsWindows()) yield break;
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(data));
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(data));
        if (parent is not null && Path.GetFileName(parent) == "data" && Path.GetDirectoryName(parent) is { } app)
            yield return Path.Combine(app, "cache", folder);
        else
            yield return Path.Combine(EmulatorPaths.XdgCache, folder);
    }

    // The emulator holds its log open while it runs, so it is read shared.
    private static string? LastMatch(FileInfo log, Regex pattern, Func<Match, string?> pick)
    {
        try
        {
            if (log.Length > MaxLogBytes) return null;
            using var fs = new FileStream(log.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            string? found = null;
            while (reader.ReadLine() is { } line)
            {
                if (!line.Contains("Booting game", StringComparison.Ordinal) && !line.Contains("Application Loaded", StringComparison.Ordinal)) continue;
                var m = pattern.Match(line);
                if (m.Success && pick(m) is { } v) found = v;
            }
            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
