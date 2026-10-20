using System.Text.RegularExpressions;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// DuckStation (PS1), tasks/emulator-saves Phase 2. Its default — upstream's and EmuDeck's, and the maintainer's Deck
/// (<c>Card1Type = PerGameTitle</c>) — is a card per game, <c>&lt;title&gt;_&lt;slot&gt;.mcd</c>, named after the game
/// database's title (<c>System::GetGameMemoryCardPath</c>), or <c>&lt;serial&gt;_&lt;slot&gt;.mcd</c> per serial. Each is
/// a game of its own, a file named after the game in a folder every game shares (D4 shape 1), and called by the
/// serial its saves carry (<see cref="ConsoleTitles"/>), its region beside the name (<see cref="ScanCandidate.Release"/>),
/// else by the card's own name. The shared card
/// <c>shared_card_&lt;slot&gt;.mcd</c>, or any card holding more than one game's saves, is listed as shared, with how to
/// switch (<see cref="MemoryCards.DuckStationFix"/>).
/// <para>
/// States are named by serial, <c>&lt;serial&gt;_&lt;slot&gt;.sav</c> and <c>&lt;serial&gt;_resume.sav</c>
/// (<c>System::GetGameSaveStatePath</c>), not by title, so a card's states are those of the product codes its saves
/// carry (<see cref="MemoryCards.Ps1Directory"/>): the game's own, the same on every machine that saved it. A card no
/// save of which names a code (homebrew) has no states folder.
/// </para>
/// <para>
/// Where: EmuDeck sets <c>[MemoryCards] Directory</c> and <c>[Folders] SaveStates</c> to
/// <c>Emulation/saves/duckstation/{saves,states}</c> (<c>DuckStation_setupConfig</c>), and links the Windows build's
/// own <c>memcards</c>/<c>savestates</c> there. Otherwise DuckStation's <c>settings.ini</c> in its data folder:
/// <c>~/.local/share/duckstation</c> (the AppImage EmuDeck installs), the Flatpak's <c>…/data/duckstation</c>,
/// <c>Documents\DuckStation</c>, <c>%LOCALAPPDATA%\DuckStation</c> or EmuDeck for Windows' portable
/// <c>%APPDATA%\EmuDeck\Emulators\duckstation</c>; default folders <c>memcards</c> and <c>savestates</c> in it.
/// </para>
/// </summary>
public static partial class DuckStationSaves
{
    public const string EmulatorName = "DuckStation";
    public const string System = "psx";

    public static IReadOnlyList<ScanCandidate> Scan() =>
        Scan(Folders(EmuDeckRoots.Find(), EmulatorPaths.Standalone ? DataRoots() : Array.Empty<string>()));

    public static IReadOnlyList<ScanCandidate> Scan(IEnumerable<RomSaveFolders> folders)
    {
        var found = new List<(ScanCandidate Candidate, DateTime WrittenUtc)>();
        var shared = new List<ScanCandidate>();
        foreach (var setup in folders)
        {
            FileInfo[] cards;
            try { cards = new DirectoryInfo(setup.Saves).GetFiles("*.mcd"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            var realDir = EmuDeckRoots.RealPath(setup.Saves);
            foreach (var file in cards)
            {
                if (file.LinkTarget is not null || !string.Equals(file.Extension, ".mcd", StringComparison.OrdinalIgnoreCase)) continue;
                if (MemoryCards.Shared(file, EmulatorName) is { } card)
                {
                    shared.Add(MemoryCards.Candidate(card, setup.Saves, System, setup.EmuDeck));
                    continue;
                }
                if (PerGameCard().Match(file.Name) is not { Success: true } m) continue;
                var title = m.Groups["title"].Value;
                if (title.Contains('*') || title.Contains(':')) continue;
                // A card DuckStation formatted when the game started but nothing was saved to: nothing to sync.
                if (MemoryCards.Ps1Directory(file.FullName) is not { Saves: > 0 } directory) continue;

                // A card per serial (Card1Type = PerGame) is named by the very code its states are. A title card's
                // are the codes of the game it is named after — every disc's, not a save copied in from another
                // game (that game's states are its own) — or, when no disc title says, every code on it.
                List<string> codes = BareSerial().IsMatch(title)
                    ? [title.ToUpperInvariant()]
                    : (MemoryCards.OwnCodes(title, directory.Codes) is { Count: > 0 } own ? own : directory.Codes)
                        .Order(StringComparer.Ordinal).ToList();
                DeclaredSavePath[]? states = setup.States is { } s && codes.Count > 0
                    ? [new DeclaredSavePath(RomSaves.StatesKey, EmuDeckRoots.RealPath(s), StateGlobsFor(codes))]
                    : null;
                found.Add((new ScanCandidate(
                    Name: codes.Select(code => ConsoleTitles.For(System, code)?.Title).FirstOrDefault(t => t is not null) ??
                          RomNames.CleanTitle(title),
                    SuggestedSaveDir: realDir,
                    Source: ScanSource.Emulator,
                    HasSteamCloud: false,
                    EmulatorName: EmulatorName,
                    EmulatorSystem: System,
                    EmulatorRom: title,
                    ViaEmuDeck: setup.EmuDeck,
                    IncludeGlobs: SaveGlobsFor(title),
                    ExtraSaveDirs: states,
                    Release: ConsoleTitles.Release(System, codes)), file.LastWriteTimeUtc));
            }
        }

        // A game played from states alone so far (Phase 19). A save anywhere on this machine — any card carrying one of
        // its product codes — wins.
        var saved = found
            .SelectMany(f => f.Candidate.ExtraSaveDirs?.SelectMany(d => d.IncludeGlobs ?? []) ?? [])
            .Select(glob => glob.EndsWith("_*.sav", StringComparison.Ordinal) ? glob[..^6] : glob)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var setup in folders)
            foreach (var (serial, written) in StateOnlyGames(setup.States))
                if (!saved.Contains(serial))
                    found.Add((StatesOnlyCandidate(setup, serial), written));

        return found
            .GroupBy(f => f.Candidate.EmulatorRom, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(f => f.Candidate.StatesOnly).ThenByDescending(f => f.WrittenUtc).First().Candidate)
            .Concat(shared)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.EmulatorRom, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// A game known only by its states' product code. DuckStation names a per-game card after the title its own game
    /// database gives the disc (<c>System::GetMemoryCardPathForSlot</c>, <c>Path::SanitizeFileName</c>), which nothing here
    /// can read; the bundled table's title is the same name for most discs, so the card is declared under it (else the
    /// code itself, DuckStation's <c>PerGame</c> naming). When DuckStation picks another name, the first in-game save is a
    /// row of its own and **Change** links it.
    /// </summary>
    private static ScanCandidate StatesOnlyCandidate(RomSaveFolders setup, string serial)
    {
        var known = ConsoleTitles.For(System, serial)?.Title;
        var card = known is null ? serial : SanitizeFileName(known);
        return new ScanCandidate(
            Name: known ?? serial,
            SuggestedSaveDir: EmuDeckRoots.RealPath(setup.Saves),
            Source: ScanSource.Emulator,
            HasSteamCloud: false,
            EmulatorName: EmulatorName,
            EmulatorSystem: System,
            EmulatorRom: card,
            ViaEmuDeck: setup.EmuDeck,
            IncludeGlobs: known is null ? SaveGlobsFor(card) : StatesOnlyCardGlobsFor(card),
            ExtraSaveDirs: [new DeclaredSavePath(RomSaves.StatesKey, EmuDeckRoots.RealPath(setup.States!), StateGlobsFor([serial]))],
            Release: ConsoleTitles.Release(System, [serial]),
            StatesOnly: true);
    }

    /// <summary>DuckStation's <c>Path::SanitizeFileName</c>: a character no file name may hold becomes <c>_</c>.</summary>
    public static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => c < ' ' || "<>:\"/\\|?*".Contains(c) ? '_' : c));

    /// <summary>The product codes with a non-empty state in a states folder (<c>SLUS-00594_1.sav</c>,
    /// <c>SLUS-00594_resume.sav</c>), and when the newest was written.</summary>
    public static IEnumerable<(string Serial, DateTime WrittenUtc)> StateOnlyGames(string? statesDir)
    {
        FileInfo[] files;
        try { files = statesDir is not null && Directory.Exists(statesDir) ? new DirectoryInfo(statesDir).GetFiles("*.sav") : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        return files
            .Where(f => f.Length > 0 && f.LinkTarget is null)
            .Select(f => (File: f, Match: StateName().Match(f.Name)))
            .Where(x => x.Match.Success)
            .GroupBy(x => x.Match.Groups["serial"].Value.ToUpperInvariant())
            .Select(g => (g.Key, g.Max(x => x.File.LastWriteTimeUtc)))
            .ToList();
    }

    [GeneratedRegex(@"^(?<serial>[A-Z]{4}-\d{5})_(?:\d+|resume)\.sav$", RegexOptions.IgnoreCase)]
    private static partial Regex StateName();

    /// <summary>A game's cards in both slots, each named exactly: <c>Crash_*.mcd</c> would take
    /// <c>Crash_Team_1.mcd</c>.</summary>
    public static IReadOnlyList<string> SaveGlobsFor(string title) => [$"{title}_1.mcd", $"{title}_2.mcd"];

    /// <summary>
    /// A states-only game's card under the bundled table's title, and under that title with what DuckStation's own game
    /// database adds in brackets — its region, mostly: <c>Tomb Raider - The Last Revelation (Europe, Australia)_1.mcd</c>
    /// (tasks/emulator-pull-safety Phase 6). The bracket keeps the next game of a series out: <c>Tomb Raider II_1.mcd</c>
    /// is not <c>Tomb Raider (…)_1.mcd</c>.
    /// </summary>
    public static IReadOnlyList<string> StatesOnlyCardGlobsFor(string title) =>
        [.. SaveGlobsFor(title), $"{title} (*)_1.mcd", $"{title} (*)_2.mcd"];

    /// <summary>
    /// The card patterns a DuckStation game enrolled states-only before Phase 6 still lacks: its scope names the title's
    /// two cards exactly, neither is in <paramref name="saveDir"/>, and a card of that title with a bracket is. Null when
    /// nothing is missing — a game whose card is named exactly, or one that was never states-only.
    /// </summary>
    public static IReadOnlyList<string>? MissingCardGlobs(IReadOnlyList<string> includeGlobs, string? saveDir)
    {
        if (OldCardTitle(includeGlobs) is not { } title || string.IsNullOrWhiteSpace(saveDir)) return null;
        try
        {
            if (!Directory.Exists(saveDir) || includeGlobs.Any(g => File.Exists(Path.Combine(saveDir, g)))) return null;
            var wider = StatesOnlyCardGlobsFor(title).Skip(2).ToList();
            return wider.Any(g => Directory.EnumerateFiles(saveDir, g).Any()) ? wider : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// The hash to count as synced when another machine took in the region-named card (<see cref="MissingCardGlobs"/>):
    /// everything but that card is as it was last synced, so this machine's own copy of the card — never synced — is no
    /// edit to push over the fleet's, and the pull brings the fleet's. Null when the scope is not that repair, nothing
    /// changed, or anything else changed too.
    /// </summary>
    public static string? SyncedHashWithCard(TrackedGame game, string stateDir)
    {
        if (game.LastSyncedHash is null || game.IncludeGlobs.Count != 4 || OldCardTitle(game.IncludeGlobs.Take(2).ToList()) is not { } title ||
            !game.IncludeGlobs.SequenceEqual(StatesOnlyCardGlobsFor(title), StringComparer.OrdinalIgnoreCase)) return null;
        var roots = game.Roots(stateDir);
        var full = SaveArchive.HashDirectory(roots, game.ExcludeGlobs);
        if (full == game.LastSyncedHash) return null;
        var withoutCard = roots.Select(r => r.IsPrimary ? r with { IncludeGlobs = SaveGlobsFor(title) } : r).ToList();
        return SaveArchive.HashDirectory(withoutCard, game.ExcludeGlobs) == game.LastSyncedHash ? full : null;
    }

    private static string? OldCardTitle(IReadOnlyList<string> globs) =>
        globs.Count == 2 && globs[0].EndsWith("_1.mcd", StringComparison.OrdinalIgnoreCase) && globs[0][..^6] is { Length: > 0 } title &&
        !title.Contains('*') && !title.Contains('(') && globs.SequenceEqual(SaveGlobsFor(title), StringComparer.OrdinalIgnoreCase)
            ? title : null;

    /// <summary>Every slot and the resume state of each of the game's product codes.</summary>
    public static IReadOnlyList<string> StateGlobsFor(IEnumerable<string> codes) => codes.Select(c => $"{c}_*.sav").ToList();

    /// <summary>Every DuckStation setup's (memory cards, states) folders that exist, EmuDeck's first.</summary>
    public static IReadOnlyList<RomSaveFolders> Folders(IEnumerable<string> emuDeckRoots, IEnumerable<string> dataRoots)
    {
        var candidates = new List<RomSaveFolders>();
        foreach (var root in emuDeckRoots)
            candidates.Add(new RomSaveFolders(Path.Combine(root, "saves", "duckstation", "saves"),
                Path.Combine(root, "saves", "duckstation", "states"), EmuDeck: true));
        foreach (var data in dataRoots)
        {
            var ini = EmulatorPaths.ReadIni(Path.Combine(data, "settings.ini"));
            if (ini.Count == 0) continue;
            candidates.Add(new RomSaveFolders(
                EmulatorPaths.IniFolder(ini.GetValueOrDefault("MemoryCards")?.GetValueOrDefault("Directory"), data)
                    ?? Path.Combine(data, "memcards"),
                EmulatorPaths.IniFolder(ini.GetValueOrDefault("Folders")?.GetValueOrDefault("SaveStates"), data)
                    ?? Path.Combine(data, "savestates")));
        }
        return RomSaves.Existing(candidates);
    }

    /// <summary>The folders a standalone DuckStation keeps <c>settings.ini</c> in, per platform.</summary>
    public static IReadOnlyList<string> DataRoots() => OperatingSystem.IsWindows()
        ?
        [
            Path.Combine(EmulatorPaths.AppData, "EmuDeck", "Emulators", "duckstation"),
            Path.Combine(EmulatorPaths.Documents, "DuckStation"),
            Path.Combine(EmulatorPaths.LocalAppData, "DuckStation"),
        ]
        : [Path.Combine(EmulatorPaths.XdgData, "duckstation"), Path.Combine(EmulatorPaths.Flatpak("org.duckstation.DuckStation"), "data", "duckstation")];

    // Two card slots, as the scope (SaveGlobsFor) names: a card it would not take is no game of its own.
    [GeneratedRegex(@"^(?<title>.+)_(?<slot>[12])\.mcd$", RegexOptions.IgnoreCase)]
    private static partial Regex PerGameCard();

    [GeneratedRegex(@"^[A-Z]{4}-\d{5}$", RegexOptions.IgnoreCase)]
    private static partial Regex BareSerial();
}
