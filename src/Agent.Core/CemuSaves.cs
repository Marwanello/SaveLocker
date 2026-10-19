using System.Globalization;
using System.Xml.Linq;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// Cemu (Wii U), tasks/emulator-saves Phase 14. A Wii U game saves into the MLC's <c>usr/save/00050000/&lt;title ID
/// low&gt;/</c> — <c>user/&lt;account&gt;/</c> (<c>80000001</c> is the first account) and <c>common/</c>, plus Cemu's
/// <c>meta/</c> — so a game is that one folder, scope <c>&lt;low&gt;/**</c> (D4 shape 2, <see cref="TitleFolders"/>).
/// <c>00050010</c> (system apps, found on the Deck), <c>0005000e</c> (updates) and <c>0005000c</c> (DLC) are no game's
/// saves. Cemu has no states to sync (D3).
/// <para>
/// Nothing in the save names the game, and no title table we may bundle covers the Wii U, so the name is machine-local
/// (D1 joins the server's game by its folder whatever the name): Cemu's <c>title_list_cache.xml</c> (<c>&lt;title
/// titleId=…&gt;&lt;name&gt;</c>, <c>TitleList.cpp</c>), else an installed game's <c>usr/title/00050000/&lt;low&gt;/meta/meta.xml</c>
/// <c>longname_en</c>, else the <c>meta/meta.xml</c> of a game folder (the extracted <c>code/content/meta</c> layout) in
/// the ROM folders — EmuDeck's <c>roms/wiiu</c> and a standalone Cemu's <c>&lt;GamePaths&gt;</c> — matched by its
/// <c>title_id</c>, else <c>Wii U &lt;low&gt;</c>. The region comes from the same place (Cemu's <c>CafeConsoleRegion</c>
/// bits: 1 Japan, 2 USA, 4 Europe), else none.
/// </para>
/// <para>
/// Where: EmuDeck links <c>Emulation/saves/Cemu/saves</c> with the MLC's <c>usr/save</c> — on the Deck into
/// <c>Emulation/roms/wiiu/mlc01</c> (its <c>settings.xml</c> <c>mlc_path</c>, the AppImage), on Windows from the portable
/// <c>%APPDATA%\EmuDeck\Emulators\cemu\mlc01</c> (both seen 2026-10-12, no game save yet). A standalone Cemu: its
/// <c>settings.xml</c> <c>&lt;mlc_path&gt;</c>, else <c>mlc01</c> in its user-data folder — <c>~/.local/share/Cemu</c>
/// (config <c>~/.config/Cemu</c>), the Flatpak's, <c>%APPDATA%\Cemu</c>, or a portable folder beside the exe
/// (<c>CemuApp.cpp</c>). The title cache lives in the user-data folder.
/// </para>
/// </summary>
public static class CemuSaves
{
    public const string EmulatorName = "Cemu";
    public const string System = "wiiu";
    /// <summary>A Wii U game application's title ID high half: the folder every game's save is in.</summary>
    public const string GameTitles = "00050000";

    public static IReadOnlyList<ScanCandidate> Scan() =>
        Scan(Folders(EmuDeckRoots.Find(), EmulatorPaths.Standalone ? Setups() : Array.Empty<CemuSetup>()));

    public static IReadOnlyList<ScanCandidate> Scan(IEnumerable<CemuFolders> folders)
    {
        var found = new List<(ScanCandidate, DateTime)>();
        foreach (var setup in folders)
        {
            var titles = TitleCache(setup.TitleCaches);
            // Walked only once a game is left unnamed: a ROM folder can be large.
            var roms = new Lazy<IReadOnlyDictionary<string, WiiUTitle>>(() => RomTitles(setup.RomFolders ?? []));
            foreach (var game in TitleFolders.Find(Path.Combine(setup.Saves, GameTitles), TitleIdLow))
            {
                var known = titles.GetValueOrDefault(game.Id) ?? MetaXml(setup.Mlc, game.Id) ?? roms.Value.GetValueOrDefault(game.Id);
                found.Add((new ScanCandidate(
                    Name: known?.Name ?? $"Wii U {game.Id.ToUpperInvariant()}",
                    SuggestedSaveDir: EmuDeckRoots.RealPath(Path.Combine(setup.Saves, GameTitles)),
                    Source: ScanSource.Emulator,
                    HasSteamCloud: false,
                    EmulatorName: EmulatorName,
                    EmulatorSystem: System,
                    EmulatorRom: game.Id,
                    ViaEmuDeck: setup.EmuDeck,
                    IncludeGlobs: TitleFolders.Scope(game.Id, severalFolders: false),
                    Release: new GameReleaseDto("Wii U", Region: known?.Region, GameIds: [GameTitles + game.Id])), game.WrittenUtc));
            }
        }
        return TitleFolders.Newest(found);
    }

    /// <summary>A save folder's name as a title ID's low half (<c>10145d00</c>, as Cemu writes it), else null.</summary>
    public static string? TitleIdLow(string name) =>
        name.Length == 8 && name.All(char.IsAsciiHexDigit) ? name.ToLowerInvariant() : null;

    /// <summary>One game's name and region, as Cemu or the game's own metadata says.</summary>
    public sealed record WiiUTitle(string Name, string? Region);

    /// <summary>
    /// Game titles in Cemu's <c>title_list_cache.xml</c> files by title ID low half — <c>&lt;title_list&gt;&lt;title
    /// titleId="0005000010145d00"&gt;&lt;region&gt;2&lt;/region&gt;&lt;name&gt;Mario Kart 8&lt;/name&gt;</c>. Updates and DLC are
    /// listed under their own high halves and skipped.
    /// </summary>
    public static IReadOnlyDictionary<string, WiiUTitle> TitleCache(IEnumerable<string> cacheFiles)
    {
        var titles = new Dictionary<string, WiiUTitle>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in cacheFiles)
        {
            XDocument doc;
            try
            {
                if (!File.Exists(file)) continue;
                doc = XDocument.Load(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or global::System.Xml.XmlException) { continue; }
            foreach (var title in doc.Root?.Elements("title") ?? [])
            {
                var id = ((string?)title.Attribute("titleId"))?.Trim() ?? "";
                if (id.Length != 16 || !id.StartsWith(GameTitles, StringComparison.OrdinalIgnoreCase)) continue;
                var name = Clean((string?)title.Element("name"));
                if (name is null) continue;
                titles.TryAdd(id[8..].ToLowerInvariant(), new WiiUTitle(name, Region(ParseRegion((string?)title.Element("region"), hex: false))));
            }
        }
        return titles;
    }

    /// <summary>An installed game's <c>meta/meta.xml</c> under <paramref name="mlc"/>: <c>longname_en</c> (one line) and
    /// <c>region</c> (hex bits); null when the game is not installed there.</summary>
    public static WiiUTitle? MetaXml(string? mlc, string low) =>
        mlc is null ? null : ReadMeta(Path.Combine(mlc, "usr", "title", GameTitles, low, "meta", "meta.xml"))?.Title;

    /// <summary>
    /// Game titles in the ROM folders by title ID low half: each game folder's <c>meta/meta.xml</c> (<c>title_id</c>,
    /// <c>longname_en</c>, <c>region</c>), the folder itself or one level down (EmuDeck's <c>roms/wiiu/roms/&lt;game&gt;</c>).
    /// An MLC in the folder (EmuDeck keeps <c>mlc01</c> in <c>roms/wiiu</c>) is skipped, as are updates and DLC, which carry
    /// their own high halves. A packed game (<c>.wua</c>, <c>.wud</c>, <c>.wux</c>) has no file to read and names nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, WiiUTitle> RomTitles(IEnumerable<string> romFolders)
    {
        var titles = new Dictionary<string, WiiUTitle>(StringComparer.OrdinalIgnoreCase);
        void Add(string folder)
        {
            var meta = ReadMeta(Path.Combine(folder, "meta", "meta.xml"));
            if (meta is { TitleId: var id, Title: { } title } && id.Length == 16 && id.StartsWith(GameTitles, StringComparison.OrdinalIgnoreCase))
                titles.TryAdd(id[8..].ToLowerInvariant(), title);
        }
        foreach (var root in romFolders)
            foreach (var dir in SubFolders(root))
            {
                Add(dir);
                foreach (var inner in SubFolders(dir)) Add(inner);
            }
        return titles;
    }

    private static IEnumerable<string> SubFolders(string folder)
    {
        string[] dirs;
        try { dirs = Directory.Exists(folder) ? Directory.GetDirectories(folder) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { dirs = []; }
        return dirs.Where(d => !Path.GetFileName(d).Equals("mlc01", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(d).Equals("meta", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A <c>meta.xml</c>'s <c>title_id</c> (16 hex digits, as written) and the game's name and region; null when
    /// the file is missing or unreadable. <c>Title</c> is null when it names nothing.</summary>
    private static (string TitleId, WiiUTitle? Title)? ReadMeta(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var root = XDocument.Load(path).Root;
            var name = Clean((string?)root?.Element("longname_en")) ?? Clean((string?)root?.Element("shortname_en"));
            var id = ((string?)root?.Element("title_id"))?.Trim() ?? "";
            return (id, name is null ? null : new WiiUTitle(name, Region(ParseRegion((string?)root?.Element("region"), hex: true))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or global::System.Xml.XmlException) { return null; }
    }

    private static uint ParseRegion(string? text, bool hex) =>
        uint.TryParse(text?.Trim(), hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>Cemu's region bits in Redump's words ("USA", "USA, Europe"); null for none or every region.</summary>
    public static string? Region(uint bits)
    {
        var names = new List<string>();
        if ((bits & 2) != 0) names.Add("USA");
        if ((bits & 4) != 0) names.Add("Europe");
        if ((bits & 1) != 0) names.Add("Japan");
        return names.Count is 0 or 3 ? null : string.Join(", ", names);
    }

    // A Wii U long name often breaks its line ("The Legend of Zelda\nBreath of the Wild"); the name never does.
    private static string? Clean(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : string.Join(' ', name.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));

    /// <summary>
    /// Every Cemu setup's <c>usr/save</c> that exists, EmuDeck's first, with its MLC (for <c>meta.xml</c>), the title
    /// caches and the ROM folders to name its games from. EmuDeck's link is the MLC's <c>usr/save</c>, so its MLC is two
    /// folders up the real path. Caches and ROM folders are every setup's: one Cemu's game list names the others' saves too.
    /// </summary>
    public static IReadOnlyList<CemuFolders> Folders(IEnumerable<string> emuDeckRoots, IEnumerable<CemuSetup> setups)
    {
        var setupList = setups.ToList();
        var rootList = emuDeckRoots.ToList();
        var caches = setupList.Select(s => Path.Combine(s.UserData, "title_list_cache.xml")).ToList();
        var settings = setupList.Select(s => (Setup: s, Root: SettingsRoot(s.SettingsXml))).ToList();
        var romFolders = rootList.Select(r => Path.Combine(r, "roms", "wiiu"))
            .Concat(settings.SelectMany(x => GamePaths(x.Root, Path.GetDirectoryName(x.Setup.SettingsXml)!)))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToList();
        var candidates = new List<CemuFolders>();
        foreach (var root in rootList)
            candidates.Add(new CemuFolders(Path.Combine(root, "saves", "Cemu", "saves"), null, caches, EmuDeck: true, romFolders));
        foreach (var (s, xml) in settings)
        {
            var configured = (string?)xml?.Element("mlc_path");
            var mlc = EmulatorPaths.IniFolder(configured, Path.GetDirectoryName(s.SettingsXml)!) ?? Path.Combine(s.UserData, "mlc01");
            candidates.Add(new CemuFolders(Path.Combine(mlc, "usr", "save"), mlc, caches, RomFolders: romFolders));
        }

        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var found = new List<CemuFolders>();
        foreach (var c in candidates)
        {
            bool exists;
            try { exists = Directory.Exists(c.Saves); } catch { exists = false; }
            var real = EmuDeckRoots.RealPath(c.Saves);
            if (!exists || !seen.Add(real)) continue;
            found.Add(c with { Saves = real, Mlc = c.Mlc ?? Path.GetDirectoryName(Path.GetDirectoryName(real)) });
        }
        return found;
    }

    private static XElement? SettingsRoot(string settingsXml)
    {
        try { return File.Exists(settingsXml) ? XDocument.Load(settingsXml).Root : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or global::System.Xml.XmlException) { return null; }
    }

    /// <summary>The game folders a Cemu lists in <c>settings.xml</c>: <c>&lt;GamePaths&gt;&lt;Entry&gt;</c>, a relative one
    /// against the settings' own folder.</summary>
    public static IEnumerable<string> GamePaths(XElement? settings, string settingsDir) =>
        (settings?.Element("GamePaths")?.Elements("Entry") ?? [])
            .Select(e => EmulatorPaths.IniFolder((string?)e, settingsDir))
            .OfType<string>();

    /// <summary>Where a standalone Cemu keeps <c>settings.xml</c> and its user data (MLC default, title cache), per
    /// platform; EmuDeck for Windows' copy is the old portable layout, everything beside the exe.</summary>
    public static IReadOnlyList<CemuSetup> Setups()
    {
        if (OperatingSystem.IsWindows())
        {
            var emuDeck = Path.Combine(EmulatorPaths.AppData, "EmuDeck", "Emulators", "cemu");
            var roaming = Path.Combine(EmulatorPaths.AppData, "Cemu");
            return
            [
                new CemuSetup(Path.Combine(emuDeck, "settings.xml"), emuDeck),
                new CemuSetup(Path.Combine(emuDeck, "portable", "settings.xml"), Path.Combine(emuDeck, "portable")),
                new CemuSetup(Path.Combine(roaming, "settings.xml"), roaming),
            ];
        }
        var flatpak = EmulatorPaths.Flatpak("info.cemu.Cemu");
        return
        [
            new CemuSetup(Path.Combine(EmulatorPaths.XdgConfig, "Cemu", "settings.xml"), Path.Combine(EmulatorPaths.XdgData, "Cemu")),
            new CemuSetup(Path.Combine(flatpak, "config", "Cemu", "settings.xml"), Path.Combine(flatpak, "data", "Cemu")),
        ];
    }
}

/// <summary>One Cemu setup: its <c>usr/save</c> folder, its MLC, and the title caches and ROM folders its games are named
/// from.</summary>
public sealed record CemuFolders(string Saves, string? Mlc, IReadOnlyList<string> TitleCaches, bool EmuDeck = false,
    IReadOnlyList<string>? RomFolders = null);

/// <summary>A standalone Cemu's <c>settings.xml</c> and user-data folder.</summary>
public sealed record CemuSetup(string SettingsXml, string UserData);
