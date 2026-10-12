namespace SaveLocker.Agent;

/// <summary>
/// Xenia (Xbox 360), tasks/emulator-saves Phase 5b. Xenia keeps every content package in its <c>content</c> folder, one per
/// title ID, by type: <c>00000001</c> saved games, <c>00000002</c> DLC, <c>000B0000</c> title updates, and a
/// <c>Headers/&lt;type&gt;/&lt;package&gt;.header</c> beside each. A game's saves are its <c>00000001</c> packages and their
/// headers, so its scope is <c>&lt;title ID&gt;/00000001/**</c> and <c>&lt;title ID&gt;/Headers/00000001/**</c> (D4 shape 2) — never
/// the DLC and updates beside them. Xenia has no states to sync (D3).
/// <para>
/// Two layouts, both seen on the Deck 2026-10-12 (and the newer one on the maintainer's PC, with a real Forza Horizon save):
/// Xenia Canary keeps each profile's saves in <c>content/&lt;profile XUID&gt;/&lt;title ID&gt;/</c>, so the game's folder is the
/// profile's; older builds wrote <c>content/&lt;title ID&gt;/</c>, and the folder is <c>content</c> itself. The scope is the same
/// in both, so a game saved on either joins one server game (D1), whatever the XUID. A profile's save wins over an old-layout
/// one of the same game. <c>content/0000000000000000/</c> holds content no profile owns (DLC, a game's device data) and
/// <c>FFFE07D1</c> is the dashboard's profile data: neither is a game's save.
/// </para>
/// <para>
/// Name: a save's header names the save ("MaroLFC Car Setup Files"), not the game, so the name comes from the title ID
/// (<see cref="ConsoleTitles"/>: Redump's disc serials — <c>MS-2505</c> is title ID <c>4D5309C9</c>, Forza Horizon — and
/// No-Intro's Arcade titles), else <c>Xbox 360 &lt;title ID&gt;</c>. An Xbox 360 title ID is every region's, so there is no region.
/// </para>
/// <para>
/// Where: EmuDeck links <c>Emulation/saves/xenia/saves</c> with Xenia's <c>content</c> — <c>Emulation/roms/xbox360/content</c>
/// on SteamOS (Xenia Canary under Proton, portable), the portable <c>%APPDATA%\EmuDeck\Emulators\xenia\content</c> on Windows,
/// where the emulator's folder is the link. A standalone Xenia: its storage root's <c>content</c>, or the config's
/// <c>content_root</c> — the exe's folder with <c>portable.txt</c>, else <c>Documents\Xenia</c>, or <c>~/.local/share/Xenia</c>
/// for a native Linux build (<c>xenia_main.cc</c>).
/// </para>
/// </summary>
public static class XeniaSaves
{
    public const string EmulatorName = "Xenia";
    public const string System = "xbox360";
    /// <summary>The content type of a saved game.</summary>
    public const string SavedGame = "00000001";

    public static IReadOnlyList<ScanCandidate> Scan() =>
        Scan(Folders(EmuDeckRoots.Find(), EmulatorPaths.Standalone ? StorageRoots() : Array.Empty<string>()));

    public static IReadOnlyList<ScanCandidate> Scan(IEnumerable<XeniaFolders> folders)
    {
        var profileGames = new List<(ScanCandidate, DateTime)>();
        var oldGames = new List<(ScanCandidate, DateTime)>();
        foreach (var setup in folders)
        {
            foreach (var profile in RomSaves.SafeSubdirs(setup.Content).Order(StringComparer.Ordinal))
                if (Xuid(Path.GetFileName(profile)) is not null)
                    profileGames.AddRange(Games(setup, profile));
            oldGames.AddRange(Games(setup, setup.Content));
        }
        // A game this machine saved under a profile is that game; an old-layout copy of it is an earlier one.
        var profiled = profileGames.Select(g => g.Item1.EmulatorRom!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return TitleFolders.Newest(profileGames.Concat(oldGames.Where(g => !profiled.Contains(g.Item1.EmulatorRom!))));
    }

    private static IEnumerable<(ScanCandidate, DateTime)> Games(XeniaFolders setup, string dir)
    {
        foreach (var game in TitleFolders.Find(dir, TitleId))
        {
            if (LastSave(game.Dirs[0]) is not { } written) continue;
            yield return (new ScanCandidate(
                Name: ConsoleTitles.For(System, game.Id)?.Title ?? $"Xbox 360 {game.Id}",
                SuggestedSaveDir: EmuDeckRoots.RealPath(dir),
                Source: ScanSource.Emulator,
                HasSteamCloud: false,
                EmulatorName: EmulatorName,
                EmulatorSystem: System,
                EmulatorRom: game.Id,
                ViaEmuDeck: setup.EmuDeck,
                IncludeGlobs: ScopeFor(game.Id),
                Release: ConsoleTitles.Release(System, [game.Id])), written);
        }
    }

    /// <summary>One game's saves and their headers, nothing else of its content.</summary>
    public static IReadOnlyList<string> ScopeFor(string titleId) =>
        [$"{titleId}/{SavedGame}/**", $"{titleId}/Headers/{SavedGame}/**"];

    /// <summary>A folder's name as an Xbox 360 title ID (<c>4D5309C9</c>), else null — the dashboard's <c>FFFE07D1</c> and
    /// other <c>FFFE</c> system titles are no game's.</summary>
    public static string? TitleId(string name) =>
        name.Length == 8 && name.All(char.IsAsciiHexDigit) && !name.StartsWith("FFFE", StringComparison.OrdinalIgnoreCase)
            ? name.ToUpperInvariant() : null;

    /// <summary>A folder's name as a profile's XUID (16 hex digits, not all zeros: that one is content no profile owns), else null.</summary>
    public static string? Xuid(string name) =>
        name.Length == 16 && name.All(char.IsAsciiHexDigit) && name.Any(c => c != '0') ? name.ToUpperInvariant() : null;

    // When the game's newest save file was written, or null when it has no saved game with any bytes in it.
    private static DateTime? LastSave(DirectoryInfo title)
    {
        try
        {
            var saves = new DirectoryInfo(Path.Combine(title.FullName, SavedGame));
            if (!saves.Exists || saves.LinkTarget is not null) return null;
            var files = saves.EnumerateFiles("*", SearchOption.AllDirectories).Where(f => f.Length > 0).ToList();
            return files.Count == 0 ? null : files.Max(f => f.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Every Xenia <c>content</c> folder that exists, EmuDeck's first. <paramref name="storageRoots"/>: standalone
    /// installs' storage roots, whose <c>xenia-canary.config.toml</c> or <c>xenia.config.toml</c> may move <c>content</c>.</summary>
    public static IReadOnlyList<XeniaFolders> Folders(IEnumerable<string> emuDeckRoots, IEnumerable<string> storageRoots)
    {
        var candidates = new List<XeniaFolders>();
        foreach (var root in emuDeckRoots)
            candidates.Add(new XeniaFolders(Path.Combine(root, "saves", "xenia", "saves"), EmuDeck: true));
        foreach (var root in storageRoots)
        {
            var configured = new[] { "xenia-canary.config.toml", "xenia.config.toml" }
                .Select(f => ContentRoot(Path.Combine(root, f)))
                .FirstOrDefault(v => v is not null);
            candidates.Add(new XeniaFolders(EmulatorPaths.IniFolder(configured, root) ?? Path.Combine(root, "content")));
        }

        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var found = new List<XeniaFolders>();
        foreach (var c in candidates)
        {
            bool exists;
            try { exists = Directory.Exists(c.Content); } catch { exists = false; }
            var real = EmuDeckRoots.RealPath(c.Content);
            if (exists && seen.Add(real)) found.Add(c with { Content = real });
        }
        return found;
    }

    /// <summary>A Xenia config's <c>content_root = "…"</c>, or null when it is empty or missing.</summary>
    public static string? ContentRoot(string configToml)
    {
        try
        {
            if (!File.Exists(configToml)) return null;
            foreach (var raw in File.ReadLines(configToml))
            {
                var line = raw.Trim();
                if (!line.StartsWith("content_root", StringComparison.Ordinal)) continue;
                var eq = line.IndexOf('=');
                if (eq < 0 || line[..eq].Trim() != "content_root") continue;
                var value = line[(eq + 1)..].Trim();
                var quote = value.Length > 0 ? value[0] : '\0';
                if (quote is not ('"' or '\'')) return null;
                var end = value.IndexOf(quote, 1);
                var path = end > 0 ? value[1..end] : null;
                return string.IsNullOrWhiteSpace(path) ? null : quote == '"' ? path.Replace(@"\\", @"\") : path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Where a standalone Xenia keeps its storage root, per platform; EmuDeck for Windows' copy is portable.</summary>
    public static IReadOnlyList<string> StorageRoots() => OperatingSystem.IsWindows()
        ?
        [
            Path.Combine(EmulatorPaths.AppData, "EmuDeck", "Emulators", "xenia"),
            Path.Combine(EmulatorPaths.Documents, "Xenia"),
        ]
        : [Path.Combine(EmulatorPaths.XdgData, "Xenia")];
}

/// <summary>One Xenia setup's <c>content</c> folder.</summary>
public sealed record XeniaFolders(string Content, bool EmuDeck = false);
