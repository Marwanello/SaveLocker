using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// Where a server emulator game's saves go on a machine that has never saved it (tasks/emulator-pull-safety Phase 5).
/// The scan lists a game only once this machine has its save, and an EmuDeck path has no template, so without this a
/// game played on one machine never reaches the others. Q4 (maintainer, 2026-10-20): set it up only where the
/// emulator is installed and the game's ROM is here.
/// <para>
/// The result is the row the scan would have made had the game saved here once: the emulator's folder, the server
/// game's own scopes, <see cref="ScanCandidate.NoSaveYet"/>. Enrolling it joins the server game by its files (D1), and
/// the first pull writes the fleet's save into the emulator's folder.
/// </para>
/// <para>
/// Built for the emulators whose game is a scope in one shared folder: PCSX2 (the setup's first folder card),
/// DuckStation, PPSSPP, melonDS and Supermodel. The others (title-ID folders, Dolphin's cards, Xenia's profiles, the
/// Switch's users, RetroArch's per-core folders) stay unmapped as before, with the same notice.
/// </para>
/// </summary>
public static class EmulatorPlaces
{
    /// <summary>A no-save-yet row for <paramref name="game"/> here, or null: it names not exactly one emulator, that
    /// emulator isn't set up here, its ROM isn't here, or it has no place for the save (PCSX2 with only a file card).</summary>
    public static ScanCandidate? CandidateFor(GameDto game, IReadOnlyList<string>? emuDeckRoots = null, bool standalone = true)
    {
        if (game.Emulators is not [var emulator] || game.IncludeGlobs is not { Length: > 0 } scope) return null;
        var roots = emuDeckRoots ?? EmuDeckRoots.Find();
        if (Place(emulator, roots, standalone) is not { } place) return null;
        if (!RomHere(place.RomNamed, place.System, game, roots)) return null;

        var states = (game.ExtraPaths ?? [])
            .Where(p => p.Key == RomSaves.StatesKey && place.States is not null && Directory.Exists(place.States))
            .Select(p => new DeclaredSavePath(RomSaves.StatesKey, EmuDeckRoots.RealPath(place.States!), p.IncludeGlobs ?? []))
            .ToArray();
        return new ScanCandidate(
            Name: game.Name,
            SuggestedSaveDir: EmuDeckRoots.RealPath(place.Saves),
            Source: ScanSource.Emulator,
            HasSteamCloud: false,
            EmulatorName: emulator,
            EmulatorSystem: place.System,
            ViaEmuDeck: place.EmuDeck,
            IncludeGlobs: scope,
            ExtraSaveDirs: states.Length > 0 ? states : null,
            Release: game.Release,
            NoSaveYet: true);
    }

    private sealed record Setup(string Saves, string? States, string System, bool EmuDeck, bool RomNamed);

    /// <summary>The emulator's save folder here, EmuDeck's first; null when it isn't set up.</summary>
    private static Setup? Place(string emulator, IReadOnlyList<string> roots, bool standalone)
    {
        static Setup? First(IEnumerable<RomSaveFolders> folders, string system, bool romNamed) =>
            folders.Where(f => Directory.Exists(f.Saves)).Select(f => new Setup(f.Saves, f.States, system, f.EmuDeck, romNamed)).FirstOrDefault();

        if (emulator.Equals(Pcsx2Saves.EmulatorName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var setup in Pcsx2Saves.Folders(roots, standalone ? Pcsx2Saves.DataRoots() : []))
                if (Pcsx2Saves.FirstFolderCard(setup.Saves) is { } card)
                    return new Setup(card.FullName, setup.States, Pcsx2Saves.System, setup.EmuDeck, RomNamed: false);
            return null;
        }
        if (emulator.Equals(DuckStationSaves.EmulatorName, StringComparison.OrdinalIgnoreCase))
            return First(DuckStationSaves.Folders(roots, standalone ? DuckStationSaves.DataRoots() : []), DuckStationSaves.System, false);
        if (emulator.Equals(PpssppSaves.EmulatorName, StringComparison.OrdinalIgnoreCase))
            return First(PpssppSaves.Folders(roots, standalone ? PpssppSaves.MemoryStickDirs() : []), PpssppSaves.System, false);
        if (emulator.Equals(MelonDsSaves.EmulatorName, StringComparison.OrdinalIgnoreCase))
            return First(MelonDsSaves.Folders(roots, standalone ? MelonDsSaves.ConfigRoots() : []), "nds", true);
        if (emulator.Equals(SupermodelSaves.EmulatorName, StringComparison.OrdinalIgnoreCase))
            return First((standalone ? SupermodelSaves.DataRoots(roots.Count > 0) : [])
                .Select(r => new RomSaveFolders(Path.Combine(r.Item1, "NVRAM"), Path.Combine(r.Item1, "Saves"), r.Item2)), "model3", true);
        return null;
    }

    /// <summary>
    /// Is the game's ROM in EmuDeck's <c>roms/&lt;system&gt;</c> (and one folder down)? An emulator that names the save
    /// after the ROM (melonDS, Supermodel) needs the very ROM the scope was made from, or the save it writes would not be
    /// the game's file; one that saves under the disc's ID needs a ROM with the game's title, ignoring case and
    /// punctuation (<c>God Of War - Chains Of Olympus.iso</c> is <i>God of War: Chains of Olympus</i>).
    /// </summary>
    internal static bool RomHere(bool romNamed, string system, GameDto game, IReadOnlyList<string> roots)
    {
        var scopeRom = Path.GetFileNameWithoutExtension(game.IncludeGlobs![0]);
        var title = Comparable(game.Name);
        foreach (var root in roots)
            foreach (var entry in Entries(Path.Combine(root, "roms", system), depth: 2))
            {
                var name = entry is DirectoryInfo ? entry.Name : Path.GetFileNameWithoutExtension(entry.Name);
                if (romNamed ? name.Equals(scopeRom, StringComparison.OrdinalIgnoreCase)
                             : Comparable(RomNames.CleanTitle(name)) == title)
                    return true;
            }
        return false;
    }

    private static string Comparable(string name) =>
        new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static IEnumerable<FileSystemInfo> Entries(string dir, int depth)
    {
        FileSystemInfo[] here;
        try { here = Directory.Exists(dir) ? new DirectoryInfo(dir).GetFileSystemInfos() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var e in here)
        {
            yield return e;
            if (depth > 1 && e is DirectoryInfo d)
                foreach (var inner in Entries(d.FullName, depth - 1)) yield return inner;
        }
    }
}
