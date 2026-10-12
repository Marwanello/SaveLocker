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
    public static IReadOnlyList<string> For(string content, string? gameName)
    {
        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(content));
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
        if (!string.IsNullOrWhiteSpace(gameName))
        {
            var own = Path.Combine(root, "config", gameName + ".config.toml");
            if (File.Exists(own)) configs.Add(own);
        }
        return configs;
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
