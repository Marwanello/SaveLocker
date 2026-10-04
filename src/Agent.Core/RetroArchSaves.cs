namespace SaveLocker.Agent;

/// <summary>
/// One Add-games candidate per RetroArch save file. Discovery starts from the SAVE, not from a
/// library: under EmuDeck the ROMs are launched by ES-DE or Steam ROM Manager shortcuts, which hand
/// RetroArch a path directly, so its own playlists are usually empty — while a game with nothing
/// saved has nothing to sync anyway.
/// <para>
/// Every ROM's save shares one folder (<c>&lt;rom&gt;.srm</c> side by side, or one folder per core
/// with "Sort saves by core"). The candidate's folder is the one the file is actually in, and its
/// <see cref="ScanCandidate.IncludeGlobs"/> name just that ROM's files, so the game's archive holds
/// <c>&lt;rom&gt;.srm</c> at its root whichever layout each machine uses.
/// </para>
/// </summary>
public static class RetroArchSaves
{
    public const string EmulatorName = "RetroArch";

    /// <summary>
    /// Suffix on every RetroArch game's name. It is part of the server-side identity on purpose: an
    /// SNES save and a PC release of the same title are different games with incompatible saves,
    /// and both would otherwise clean to the same name and merge on the server.
    /// </summary>
    public const string NameSuffix = " (RetroArch)";

    /// <summary>The candidates on this machine. Never throws for a missing or unreadable folder.</summary>
    public static IReadOnlyList<ScanCandidate> Scan() =>
        Scan(RetroArchConfig.SaveDirectories(), EmuDeckRoots.Find());

    /// <summary>The same, from explicit folders — what the tests drive.</summary>
    public static IReadOnlyList<ScanCandidate> Scan(IEnumerable<string> saveDirectories, IEnumerable<string> emuDeckRoots)
    {
        var systems = RomSystems(emuDeckRoots);
        var found = new List<(ScanCandidate Candidate, DateTime WrittenUtc)>();

        foreach (var saveDir in saveDirectories)
        {
            // The folder itself (sorting off), then one level of per-core folders (sorting on).
            found.AddRange(SavesIn(saveDir, core: null, systems));
            foreach (var sub in SafeSubdirs(saveDir))
                found.AddRange(SavesIn(sub, core: Path.GetFileName(sub), systems));
        }

        // Two saves can clean to one name — the same ROM under two cores, or two regions of one game.
        // One name is one server game, so only the most recently written survives.
        return found
            .GroupBy(f => f.Candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(f => f.WrittenUtc).First().Candidate)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The include patterns that make one ROM's files a game: its SRAM save and, for cartridges with
    /// a clock (Pokémon Gold/Silver/Crystal, Boktai), the <c>.rtc</c> beside it. Anchored at the save
    /// folder's root, so a same-named file in some other core's folder is never picked up.
    /// </summary>
    public static IReadOnlyList<string> IncludeGlobsFor(string romBaseName) =>
        new[] { romBaseName + ".srm", romBaseName + ".rtc" };

    private static IEnumerable<(ScanCandidate, DateTime)> SavesIn(
        string dir, string? core, IReadOnlyDictionary<string, string> systems)
    {
        FileInfo[] files;
        try { files = new DirectoryInfo(dir).GetFiles("*.srm"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }

        var realDir = EmuDeckRoots.RealPath(dir);
        foreach (var file in files)
        {
            // A link is never archived (SaveArchive skips them), so a linked save could never sync.
            if (file.LinkTarget is not null || file.Length == 0) continue;
            var romBase = Path.GetFileNameWithoutExtension(file.Name);
            // Both are legal in a Linux file name: '*' is the glob wildcard, so the name cannot be scoped,
            // and the server refuses ':' in an include (it reads as a Windows drive) — which would fail
            // the whole enrollment batch, not just this game.
            if (romBase.Length == 0 || romBase.Contains('*') || romBase.Contains(':')) continue;

            yield return (new ScanCandidate(
                Name: RomNames.CleanTitle(romBase) + NameSuffix,
                SuggestedSaveDir: realDir,
                Source: ScanSource.Emulator,
                HasSteamCloud: false,
                EmulatorName: EmulatorName,
                EmulatorSystem: systems.GetValueOrDefault(romBase),
                EmulatorCore: core,
                IncludeGlobs: IncludeGlobsFor(romBase)), file.LastWriteTimeUtc);
        }
    }

    /// <summary>
    /// ROM file name (no extension) → the <c>Emulation/roms/&lt;system&gt;</c> folder it is in. RetroArch
    /// names a save after its ROM, so this is how a save learns which console it belongs to.
    /// </summary>
    private static IReadOnlyDictionary<string, string> RomSystems(IEnumerable<string> emuDeckRoots)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in emuDeckRoots)
        {
            foreach (var systemDir in SafeSubdirs(Path.Combine(root, "roms")))
            {
                string[] roms;
                try { roms = Directory.GetFiles(systemDir); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                var system = Path.GetFileName(systemDir);
                foreach (var rom in roms)
                    map.TryAdd(Path.GetFileNameWithoutExtension(rom), system);
            }
        }
        return map;
    }

    private static string[] SafeSubdirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
