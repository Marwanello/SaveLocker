namespace SaveLocker.Agent;

/// <summary>
/// Where RetroArch keeps save files on this machine: EmuDeck's documented
/// <c>Emulation/saves/retroarch/saves</c> first, then any standalone RetroArch's own
/// <c>savefile_directory</c> from its <c>retroarch.cfg</c>.
/// <para>
/// Save STATES are deliberately not looked for: a state is tied to the exact core build that wrote
/// it, unlike an SRAM save, which follows the emulated hardware's format (tasks/emulator-saves).
/// </para>
/// </summary>
public static class RetroArchConfig
{
    /// <summary>Every existing folder RetroArch writes <c>.srm</c> saves into, real paths, no duplicates.</summary>
    public static IReadOnlyList<string> SaveDirectories() =>
        SaveDirectories(EmuDeckRoots.Find(), EmuDeckRoots.Override is null ? ConfigRoots() : Array.Empty<string>());

    /// <summary>The same, from explicit inputs — what the tests drive.</summary>
    public static IReadOnlyList<string> SaveDirectories(IEnumerable<string> emuDeckRoots, IEnumerable<string> configRoots)
    {
        var candidates = new List<string>();
        foreach (var root in emuDeckRoots)
            candidates.Add(Path.Combine(root, "saves", "retroarch", "saves"));

        foreach (var configRoot in configRoots)
        {
            var cfg = Path.Combine(configRoot, "retroarch.cfg");
            var settings = File.Exists(cfg) ? Parse(SafeRead(cfg)) : new Dictionary<string, string>();
            if (settings.TryGetValue("savefile_directory", out var dir) && ExpandPath(dir, configRoot) is { } expanded)
                candidates.Add(expanded);
            // RetroArch's own default when the key is unset or "default".
            candidates.Add(Path.Combine(configRoot, "saves"));
        }

        var seen = new HashSet<string>(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var found = new List<string>();
        foreach (var c in candidates)
        {
            bool exists;
            try { exists = Directory.Exists(c); } catch { exists = false; }
            if (!exists) continue;
            var real = EmuDeckRoots.RealPath(c);
            if (seen.Add(real)) found.Add(real);
        }
        return found;
    }

    /// <summary>Folders a standalone RetroArch keeps <c>retroarch.cfg</c> in, per platform.</summary>
    public static IReadOnlyList<string> ConfigRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new[]
            {
                Path.Combine(appData, "RetroArch"),
                // EmuDeck for Windows' own RetroArch copy. Not confirmed against a real install yet;
                // its saves are found through Emulation/saves/retroarch/saves regardless.
                Path.Combine(appData, "emudeck", "Emulators", "RetroArch"),
            };
        }
        return new[]
        {
            Path.Combine(home, ".config", "retroarch"),
            Path.Combine(home, ".var", "app", "org.libretro.RetroArch", "config", "retroarch"),
        };
    }

    /// <summary>
    /// <c>retroarch.cfg</c>'s flat <c>key = "value"</c> lines. Comments and malformed lines are
    /// skipped; the last occurrence of a key wins, as it does in RetroArch.
    /// </summary>
    public static Dictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            result[key] = value;
        }
        return result;
    }

    /// <summary>
    /// A path as RetroArch writes it, made absolute: <c>~</c> is the home folder and a leading
    /// <c>:</c> is RetroArch's own folder (a portable Windows install writes <c>:\saves</c>). Empty and
    /// <c>"default"</c> mean "not set" — null, and the caller falls back to RetroArch's default.
    /// </summary>
    public static string? ExpandPath(string value, string configRoot)
    {
        var v = value.Trim();
        if (v.Length == 0 || v.Equals("default", StringComparison.OrdinalIgnoreCase)) return null;
        if (v == "~" || v.StartsWith("~/") || v.StartsWith("~\\"))
            v = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), v.Length > 2 ? v[2..] : "");
        else if (v.StartsWith(':'))
            v = Path.Combine(configRoot, v.TrimStart(':').TrimStart('/', '\\'));
        try { return Path.IsPathRooted(v) ? Path.GetFullPath(v) : null; }
        catch { return null; }
    }

    private static string SafeRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }
}
