using System.Xml;

namespace SaveLocker.Agent;

/// <summary>
/// Supermodel (Sega Model 3 arcade), a row of <see cref="RomSaves"/> (tasks/emulator-saves Phase 9). NVRAM is
/// <c>NVRAM/&lt;set&gt;.nv</c>, written on every exit; states <c>Saves/&lt;set&gt;.st&lt;slot&gt;</c> (<c>Src/OSD/SDL/Main.cpp</c>).
/// <para>
/// Where (<c>Src/OSD/{Unix,Windows}/FileSystemPath.cpp</c>): on Linux <c>~/.supermodel/{NVRAM,Saves}</c> when
/// <c>~/.supermodel</c> exists — EmuDeck's choice, it installs the Flatpak <c>com.supermodel3.Supermodel</c> but keeps
/// everything there and links nothing (<c>Supermodel_setupSaves</c> is "NYI") — else <c>$XDG_DATA_HOME/supermodel</c>,
/// by default <c>~/.local/share/supermodel</c> (inside the Flatpak, its own data folder). On Windows the folders are
/// relative to the emulator's own: EmuDeck for Windows keeps it in <c>%APPDATA%\EmuDeck\Emulators\Supermodel</c> and
/// links its states folder to <c>Emulation\saves\supermodel\saves</c>; NVRAM is only in the emulator's folder. A
/// standalone Windows copy can be anywhere, so it is not looked for.
/// </para>
/// <para>
/// Name: Supermodel's own <c>Config/Games.xml</c> (<c>&lt;game name="scud"&gt;&lt;identity&gt;&lt;title&gt;</c>), which it
/// needs to load any ROM and EmuDeck downloads from upstream — then ES-DE's gamelist, then the set name.
/// </para>
/// </summary>
public static class SupermodelSaves
{
    public const string EmulatorName = "Supermodel";

    public static IReadOnlyList<ScanCandidate> Scan()
    {
        var roots = EmuDeckRoots.Find();
        return Scan(EmulatorPaths.Standalone ? DataRoots(roots.Count > 0) : Array.Empty<(string, bool)>(), roots);
    }

    /// <param name="dataRoots">Supermodel folders holding <c>NVRAM</c>, <c>Saves</c> and <c>Config</c>, with whether
    /// EmuDeck set each up.</param>
    public static IReadOnlyList<ScanCandidate> Scan(IEnumerable<(string Dir, bool EmuDeck)> dataRoots, IEnumerable<string> emuDeckRoots)
    {
        var roots = dataRoots.ToList();
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dir, _) in roots)
            foreach (var config in ConfigDirs(dir))
                foreach (var (set, title) in LoadGamesXml(Path.Combine(config, "Games.xml")))
                    titles.TryAdd(set, title);

        var rules = new RomSaveRules(EmulatorName, ".nv", (set, ext) => new[] { set + ext }, set => new[] { set + ".st*" },
            System: "model3", KnownTitle: set => titles.GetValueOrDefault(set));
        var folders = roots.Select(r => new RomSaveFolders(Path.Combine(r.Dir, "NVRAM"), Path.Combine(r.Dir, "Saves"), r.EmuDeck));
        var emu = emuDeckRoots.ToList();
        return RomSaves.Scan(rules, RomSaves.Existing(folders), emu, GamelistXml.Find(emu));
    }

    /// <summary>Supermodel's data folders on this machine. <paramref name="emuDeck"/>: an EmuDeck install exists,
    /// so <c>~/.supermodel</c> is EmuDeck's.</summary>
    public static IReadOnlyList<(string, bool)> DataRoots(bool emuDeck)
    {
        var home = EmulatorPaths.Home;
        if (OperatingSystem.IsWindows())
            return new[] { (Path.Combine(EmulatorPaths.AppData, "EmuDeck", "Emulators", "Supermodel"), true) };
        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x && EmulatorPaths.HomeOverride is null
            ? x : Path.Combine(home, ".local", "share");
        return new[]
        {
            (Path.Combine(home, ".supermodel"), emuDeck),
            (Path.Combine(xdgData, "supermodel"), false),
            (Path.Combine(home, ".var", "app", "com.supermodel3.Supermodel", "data", "supermodel"), false),
        };
    }

    /// <summary>Where a data folder's <c>Games.xml</c> may be: its own <c>Config</c>, or — for the XDG layout, which
    /// keeps config apart — <c>~/.config/supermodel/Config</c>.</summary>
    private static IEnumerable<string> ConfigDirs(string dataDir)
    {
        yield return Path.Combine(dataDir, "Config");
        yield return Path.Combine(EmulatorPaths.Home, ".config", "supermodel", "Config");
    }

    /// <summary>Set name → <c>&lt;identity&gt;&lt;title&gt;</c> from one <c>Games.xml</c>; empty when it is missing or
    /// broken (whatever parsed before the fault is kept). No DTD, no external entities.</summary>
    public static IReadOnlyDictionary<string, string> LoadGamesXml(string file)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(file)) return result;
            using var reader = XmlReader.Create(file, new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true });
            string? set = null;
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "game")
                    set = reader.GetAttribute("name");
                else if (set is not null && reader.NodeType == XmlNodeType.Element && reader.Name == "title")
                {
                    var title = reader.ReadElementContentAsString().Trim();
                    if (set.Length > 0 && title.Length > 0) result.TryAdd(set, title);
                    set = null;
                    continue;
                }
                reader.Read();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException) { }
        return result;
    }
}
