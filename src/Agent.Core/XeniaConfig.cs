namespace SaveLocker.Agent;

/// <summary>
/// Which profile Xenia signs in with at boot: <c>logged_profile_slot_0_xuid</c> under <c>[Profiles]</c> in its config
/// (Xenia Canary's <c>profile_manager.cc</c>), tasks/emulator-saves Phase 5c. The config sits beside <c>content</c> in a
/// portable install — EmuDeck's <c>roms/xbox360</c> on a Deck, Xenia Manager's emulator folder on a PC. Xenia Manager
/// also keeps one config per game in <c>config/&lt;game&gt;.config.toml</c> carrying the same line, so a game's
/// configs are the root one and its own.
/// </summary>
public static class XeniaConfig
{
    public const string Key = "logged_profile_slot_0_xuid";
    private const string Section = "[Profiles]";

    private static readonly (string Exe, string Config)[] Builds =
    [
        ("xenia_canary.exe", "xenia-canary.config.toml"),
        ("xenia_edge.exe", "xenia-edge.config.toml"),
        ("xenia.exe", "xenia.config.toml"),
    ];

    /// <summary>The configs that decide the profile when <paramref name="gameName"/> starts from this content folder.</summary>
    public static IReadOnlyList<string> For(string content, string? gameName, IEnumerable<string>? installs = null)
    {
        var root = Install(content, installs ?? XeniaSaves.StorageRoots());
        if (root is null) return [];
        var existing = Builds.Select(b => Path.Combine(root, b.Config)).Where(File.Exists).ToList();
        var configs = existing.Where(c => Read(c) is not null).ToList();
        if (configs.Count == 0)
        {
            // None names a profile yet: the one the installed build reads, else the first that exists.
            var build = Builds.FirstOrDefault(b => File.Exists(Path.Combine(root, b.Exe)));
            var chosen = build.Config is not null && File.Exists(Path.Combine(root, build.Config))
                ? Path.Combine(root, build.Config)
                : existing.FirstOrDefault();
            if (chosen is not null) configs.Add(chosen);
        }
        if (GameConfig(root, gameName) is { } own) configs.Add(own);
        return configs;
    }

    /// <summary>
    /// Xenia Manager's config for one game, <c>config/&lt;its title&gt;.config.toml</c>. Its title is its own, so the file is
    /// matched on letters and digits only, region and tags left out (<c>Halo 3 - ODST</c> is <c>Halo 3: ODST</c>'s), and only
    /// among the files in that folder: the game's name comes from the server and is never made into a path.
    /// </summary>
    private static string? GameConfig(string root, string? gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return null;
        const string suffix = ".config.toml";
        var dir = Path.Combine(root, "config");
        List<string> files;
        try { files = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*" + suffix).Order(StringComparer.Ordinal).ToList() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        string Stem(string f) { var n = Path.GetFileName(f); return n[..^suffix.Length]; }
        return files.FirstOrDefault(f => string.Equals(Stem(f), gameName, StringComparison.OrdinalIgnoreCase))
               ?? files.FirstOrDefault(f => Comparable(Stem(f)) is { Length: > 0 } c && c == Comparable(gameName));
    }

    private static string Comparable(string title) =>
        new([.. RomNames.CleanTitle(title).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    /// <summary>
    /// The Xenia folder whose configs go with <paramref name="content"/>. Usually the folder holding it — but a game's saves
    /// are recorded by their real path, and on EmuDeck for Windows the emulator's <c>content</c> is the link, into
    /// <c>Emulation/saves/xenia/saves</c>, so the configs are beside the install whose <c>content</c> leads there.
    /// </summary>
    private static string? Install(string content, IEnumerable<string> installs)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(content));
        if (parent is not null && Builds.Any(b => File.Exists(Path.Combine(parent, b.Config)))) return parent;
        var real = EmuDeckRoots.RealPath(content);
        foreach (var install in installs)
        {
            var linked = Path.Combine(install, "content");
            if (!Directory.Exists(linked)) continue;
            if (string.Equals(EmuDeckRoots.RealPath(linked), real, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return install;
        }
        return parent;
    }

    /// <summary>The XUID the config signs in with: "" when the line is empty, null when there is no such line.</summary>
    public static string? Read(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
                if (Value(line) is { } v) return v;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Sets the profile Xenia signs in with. Only the quoted value changes; spacing, the comment, every other
    /// line and the file's line endings stay. A config without the line gets it under <c>[Profiles]</c>.</summary>
    public static void Write(string path, string xuid)
    {
        if (xuid.Length != 0 && XeniaSaves.Xuid(xuid) is null) throw new ArgumentException($"Not an XUID: {xuid}", nameof(xuid));
        var text = File.ReadAllText(path);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Split(newline).ToList();

        var at = lines.FindIndex(l => Value(l) is not null);
        if (at >= 0)
        {
            var line = lines[at];
            var open = line.IndexOf('"');
            var close = line.IndexOf('"', open + 1);
            lines[at] = line[..(open + 1)] + xuid + line[close..];
        }
        else
        {
            var section = lines.FindIndex(l => l.Trim() == Section);
            var entry = $"{Key} = \"{xuid}\"";
            if (section >= 0) lines.Insert(section + 1, entry);
            else
            {
                if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                lines.AddRange(["", Section, entry, ""]);
            }
        }
        AtomicFile.WriteAllText(path, string.Join(newline, lines));
    }

    private static string? Value(string raw)
    {
        var line = raw.TrimStart();
        if (!line.StartsWith(Key, StringComparison.Ordinal)) return null;
        var eq = line.IndexOf('=');
        if (eq < 0 || line[..eq].Trim() != Key) return null;
        var rest = line[(eq + 1)..].TrimStart();
        if (rest.Length == 0 || rest[0] != '"') return null;
        var end = rest.IndexOf('"', 1);
        return end < 0 ? null : rest[1..end].Trim().ToUpperInvariant();
    }
}
