using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// A Steam shortcut that starts an emulator with a ROM — what Steam ROM Manager adds for every ROM, and what a hand-made
/// shortcut to an emulator is too (tasks/emulator-saves Phase 20). <see cref="Emulator"/> is the reader's name for it
/// (<see cref="ScanCandidate.EmulatorName"/>); <see cref="Rom"/> the ROM's full path.
/// </summary>
public sealed record EmulatorShortcut(SteamShortcut Shortcut, string Emulator, string Rom);

/// <summary>
/// Steam ROM Manager's shortcuts are the emulator's game, not a game of their own (tasks/emulator-saves Phase 20). A shortcut
/// is an emulator's when its command starts an emulator SaveLocker reads — an EmuDeck launcher (<c>Emulation/tools/launchers/
/// retroarch.sh</c>), the emulator itself, or its Flatpak — and names a ROM file (maintainer's call, 2026-10-18: launcher and
/// ROM path; SRM's own record, <c>userdata/&lt;id&gt;/config/addedItemsV2.json</c>, stands in for an unknown launcher). Such a
/// shortcut is never listed as a Steam shortcut game: it is attached to the emulator's row for its ROM — its app ID becomes
/// the game's, so the launch wrapper's pull before launch and push after exit work for it — or, when the ROM has saved
/// nothing yet, a row is made for it where the reader can say where its save will be (<see cref="NoSaveYet"/>); otherwise
/// it is hidden.
/// <para>
/// Read from the Deck on 2026-10-18 (127 shortcuts): SRM writes the whole command into <c>Exe</c> with empty launch options
/// (<c>"…/retroarch.sh" -L /mupen64plus_next_libretro.so "…/roms/n64/Super Mario 64.z64"</c>), or the launcher alone with the
/// ROM in the launch options (<c>vblank_mode=0 %command% -f -g "'…/Metroid Dread [010093801237C000][v0].nsp'"</c>, quotes
/// inside quotes); Xenia's ROM is a Wine path (<c>"Z:/run/media/…/Forza Horizon (USA).iso"</c>).
/// </para>
/// </summary>
public static class SrmShortcuts
{
    /// <summary>The emulator a launcher, executable or Flatpak ID starts, by the start of its file name, lower case — the
    /// longest key first, so <c>primehack</c> is never read as Dolphin's and <c>model-2-emulator</c> is found whole.</summary>
    private static readonly (string Key, string Emulator)[] Launchers =
    [
        ("model-2-emulator", Model2Saves.EmulatorName),
        ("emulator_multicpu", Model2Saves.EmulatorName),
        ("org.libretro.retroarch", RetroArchSaves.EmulatorName),
        ("retroarch", RetroArchSaves.EmulatorName),
        ("melonds", MelonDsSaves.EmulatorName),
        ("net.kuribo64.melonds", MelonDsSaves.EmulatorName),
        ("supermodel", SupermodelSaves.EmulatorName),
        ("scummvm", ScummVmSaves.EmulatorName),
        ("pcsx2", Pcsx2Saves.EmulatorName),
        ("net.pcsx2.pcsx2", Pcsx2Saves.EmulatorName),
        ("duckstation", DuckStationSaves.EmulatorName),
        ("org.duckstation.duckstation", DuckStationSaves.EmulatorName),
        ("io.github.shiiion.primehack", DolphinSaves.PrimeHackName),
        ("primehack", DolphinSaves.PrimeHackName),
        ("org.dolphinemu.dolphin-emu", DolphinSaves.EmulatorName),
        ("dolphin", DolphinSaves.EmulatorName),
        ("org.ppsspp.ppsspp", PpssppSaves.EmulatorName),
        ("ppsspp", PpssppSaves.EmulatorName),
        ("rpcs3", Rpcs3Saves.EmulatorName),
        ("net.rpcs3.rpcs3", Rpcs3Saves.EmulatorName),
        ("vita3k", Vita3KSaves.EmulatorName),
        ("shadps4", ShadPs4Saves.EmulatorName),
        ("info.cemu.cemu", CemuSaves.EmulatorName),
        ("cemu", CemuSaves.EmulatorName),
        ("azahar", AzaharSaves.EmulatorName),
        ("org.azahar_emu.azahar", AzaharSaves.EmulatorName),
        ("citra", AzaharSaves.EmulatorName),
        ("lime3ds", AzaharSaves.EmulatorName),
        ("xenia", XeniaSaves.EmulatorName),
        ("ryujinx", SwitchEmulators.Ryujinx),
        ("eden", "Eden"),
        ("citron", "Citron"),
        ("yuzu", "Yuzu"),
        ("suyu", "Suyu"),
        ("sudachi", "Sudachi"),
    ];

    /// <summary>Files on a command line that are the program or its plugins, never a ROM.</summary>
    private static readonly HashSet<string> NotRoms = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sh", ".so", ".dll", ".exe", ".ps1", ".bat", ".cmd", ".appimage", ".py", ".lnk", ".desktop", ".cfg", ".ini", ".toml", ".json",
    };

    /// <summary>ROM and disc image extensions, for a ROM kept outside a <c>roms</c> folder.</summary>
    private static readonly HashSet<string> RomExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".chd", ".iso", ".cue", ".bin", ".img", ".m3u", ".pbp", ".cso", ".rvz", ".wbfs", ".wia", ".gcm", ".ciso",
        ".nsp", ".xci", ".nsz", ".xcz", ".3ds", ".cci", ".cia", ".cxi", ".nds", ".gba", ".gbc", ".gb", ".sfc", ".smc", ".nes",
        ".z64", ".n64", ".v64", ".md", ".gen", ".sms", ".gg", ".pce", ".rom", ".wud", ".wux", ".rpx", ".wua", ".xex", ".god",
    };

    /// <summary>
    /// This shortcut as an emulator's game, or null: its command starts an emulator SaveLocker reads (or SRM says it added
    /// it, <paramref name="srmAppIds"/>) and names a ROM file. A shortcut to an emulator alone ("RetroArch", "Yuzu") is no game.
    /// </summary>
    public static EmulatorShortcut? Recognize(SteamShortcut shortcut, IReadOnlySet<string>? srmAppIds = null)
    {
        var tokens = Tokens(shortcut.RawExe).Concat(Tokens(shortcut.LaunchOptions)).ToList();
        // Only a word that is the program says which emulator runs: the executable, or what a `run` starts (Flatpak's
        // `flatpak run org.libretro.RetroArch`). A ROM's own file name ("Dolphin Adventure.iso") or any other argument
        // never does — a misread one would hide a PC game's shortcut from Add games.
        var programs = tokens.Take(1).Concat(tokens.Skip(1).Where((_, i) => tokens[i].Equals("run", StringComparison.OrdinalIgnoreCase)));
        var emulator = programs.Where(t => RomPath(t) is null).Select(EmulatorOf).FirstOrDefault(e => e is not null);
        if (emulator is null)
        {
            // An SRM shortcut whose launcher is not one of ours (a custom script): the ROM still names the game, but only a
            // reader that knows the ROM's folder can take it — RetroArch by EmuDeck's roms layout.
            if (srmAppIds is null || shortcut.AppId is null || !srmAppIds.Contains(shortcut.AppId)) return null;
            emulator = RetroArchSaves.EmulatorName;
        }
        var rom = tokens.Skip(1).Select(RomPath).LastOrDefault(r => r is not null);
        return rom is null ? null : new EmulatorShortcut(shortcut, emulator, rom);
    }

    /// <summary>The emulator one command-line word starts, by its file name (or Flatpak ID), or null.</summary>
    public static string? EmulatorOf(string token)
    {
        var name = FileName(token).ToLowerInvariant();
        foreach (var (key, emulator) in Launchers)
            if (name.StartsWith(key, StringComparison.Ordinal)) return emulator;
        return null;
    }

    /// <summary>
    /// The ROM a command-line word names, or null: an absolute path (a Wine <c>Z:</c> path made a Linux one) to a file that is
    /// not a program, in a <c>roms</c> folder or with a ROM's extension.
    /// </summary>
    public static string? RomPath(string token)
    {
        var path = token;
        if (path.Length > 3 && (path[0] is 'Z' or 'z') && path[1] == ':' && path[2] == '/') path = path[2..];
        var rooted = path.StartsWith('/') || (path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/');
        if (!rooted) return null;
        var ext = Path.GetExtension(path);
        if (ext.Length == 0 || NotRoms.Contains(ext)) return null;
        var inRoms = path.Split('/', '\\').Any(seg => seg.Equals("roms", StringComparison.OrdinalIgnoreCase));
        return inRoms || RomExtensions.Contains(ext) ? path : null;
    }

    /// <summary>
    /// A command line's words as a shell reads them: split on spaces outside quotes, either quote kind; a word quoted twice
    /// (<c>"'/path with spaces.nsp'"</c>, as SRM writes a Yuzu fork's ROM) loses both.
    /// </summary>
    public static IEnumerable<string> Tokens(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) yield break;
        var sb = new StringBuilder();
        char? quote = null;
        var any = false;
        foreach (var ch in command)
        {
            if (quote is null && char.IsWhiteSpace(ch))
            {
                if (any) yield return Unquote(sb.ToString());
                sb.Clear();
                any = false;
                continue;
            }
            any = true;
            if (quote is null && ch is '"' or '\'') { quote = ch; continue; }
            if (quote == ch) { quote = null; continue; }
            sb.Append(ch);
        }
        if (any) yield return Unquote(sb.ToString());
    }

    private static string Unquote(string word)
    {
        while (word.Length >= 2 && (word[0] is '"' or '\'') && word[^1] == word[0]) word = word[1..^1];
        return word;
    }

    private static string FileName(string token)
    {
        var name = token.TrimEnd('/', '\\');
        var slash = name.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    /// <summary>
    /// The app IDs (unsigned, as <see cref="SteamShortcut.AppId"/>) Steam ROM Manager says it added, from every Steam account's
    /// <c>config/addedItemsV2.json</c> under <paramref name="steamRoot"/>. Its keys are Steam's 64-bit shortcut IDs, the app ID in
    /// the high half. Empty when SRM never ran here.
    /// </summary>
    public static IReadOnlySet<string> SrmAppIds(string steamRoot)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var user in RomSaves.SafeSubdirs(Path.Combine(steamRoot, "userdata")))
        {
            var file = Path.Combine(user, "config", "addedItemsV2.json");
            try
            {
                if (!File.Exists(file)) continue;
                ids.UnionWith(ParseAddedItems(File.ReadAllText(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return ids;
    }

    /// <summary>The app IDs in one <c>addedItemsV2.json</c>; none for anything unreadable.</summary>
    public static IEnumerable<string> ParseAddedItems(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("addedApps", out var apps) || apps.ValueKind != JsonValueKind.Object) return [];
            return apps.EnumerateObject()
                .Select(p => ulong.TryParse(p.Name, out var id) ? (id >> 32).ToString() : null)
                .Where(id => id is not null && id != "0")
                .Select(id => id!)
                .ToList();
        }
        catch (JsonException) { return []; }
    }

    /// <summary>
    /// <paramref name="candidates"/> with every emulator shortcut in <paramref name="shortcuts"/> put where it belongs: its Steam
    /// shortcut row dropped, its app ID and name attached to the emulator row for its ROM (<see cref="Match"/>), or — no row
    /// for it — a row from <paramref name="noSaveYet"/> carrying them, when that can say where the ROM's save will be.
    /// Several shortcuts to one ROM (SRM re-adding a game, the Deck's two Metroid Prime Hunters) all attach to its row.
    /// </summary>
    public static List<ScanCandidate> Apply(
        IReadOnlyList<ScanCandidate> candidates, IReadOnlyList<EmulatorShortcut> shortcuts,
        Func<EmulatorShortcut, ScanCandidate?> noSaveYet)
    {
        var result = candidates.ToList();
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var attached = new Dictionary<int, List<EmulatorShortcut>>();
        var made = new List<(ScanCandidate Candidate, List<EmulatorShortcut> Shortcuts)>();

        foreach (var s in shortcuts)
        {
            if (s.Shortcut.AppId is { } appId) consumed.Add(appId);
            var at = result.FindIndex(c => Match(c, s));
            if (at >= 0)
            {
                (attached.TryGetValue(at, out var list) ? list : attached[at] = []).Add(s);
                continue;
            }
            var mine = made.FindIndex(m => Match(m.Candidate, s));
            if (mine >= 0) { made[mine].Shortcuts.Add(s); continue; }
            if (noSaveYet(s) is { } row) made.Add((row, [s]));
        }

        foreach (var (at, list) in attached) result[at] = WithShortcuts(result[at], list);
        result.AddRange(made.Select(m => WithShortcuts(m.Candidate, m.Shortcuts)));
        return result
            .Where(c => c.Source != ScanSource.SteamShortcut || c.SteamAppId is null || !consumed.Contains(c.SteamAppId))
            .ToList();
    }

    private static ScanCandidate WithShortcuts(ScanCandidate c, List<EmulatorShortcut> shortcuts)
    {
        var ids = shortcuts.Select(s => s.Shortcut.AppId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return c with
        {
            SteamAppId = c.SteamAppId ?? ids.FirstOrDefault(),
            ShortcutAppIds = ids,
            ShortcutName = shortcuts[0].Shortcut.AppName,
        };
    }

    /// <summary>What a shortcut's ROM file says of itself, read at most once per shortcut: <see cref="Match"/> runs for every
    /// candidate, and the ROM is often on an SD card.</summary>
    private sealed class RomIds(string rom)
    {
        public readonly Lazy<Ncch.Header?> Ncch = new(() => global::SaveLocker.Agent.Ncch.Read(rom));
        public readonly Lazy<string?> DiscGameId = new(() => DolphinSaves.DiscGameId(rom));
    }

    private static readonly ConditionalWeakTable<EmulatorShortcut, RomIds> RomIdsOf = new();

    /// <summary>
    /// Is <paramref name="c"/> the emulator row for this shortcut's ROM? Only an emulator row of the same emulator (any Switch
    /// emulator for a Switch ROM: one game per title, Group F), and only by what names the ROM's save on every machine: a Switch
    /// title ID, a 3DS program ID, a GameCube or Wii game ID, a save named after the ROM (RetroArch, melonDS, the arcade
    /// emulators) — else, for the emulators whose saves say nothing of the ROM (Xenia, PCSX2, …), the ROM's cleaned name or the
    /// shortcut's against the row's.
    /// </summary>
    public static bool Match(ScanCandidate c, EmulatorShortcut s)
    {
        if (c.Source != ScanSource.Emulator || c.EmulatorName is null) return false;
        var romName = Path.GetFileNameWithoutExtension(s.Rom);
        if (SwitchEmulators.IsSwitch(s.Emulator))
            return SwitchEmulators.IsSwitch(c.EmulatorName) &&
                   SwitchEmulators.TitleIdOfRom(s.Rom) is { } tid && c.EmulatorKey == EmulatorKeys.Switch(tid);
        if (!string.Equals(c.EmulatorName, s.Emulator, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(c.EmulatorRom, romName, StringComparison.OrdinalIgnoreCase)) return true;
        var ids = RomIdsOf.GetValue(s, x => new RomIds(x.Rom));
        if (s.Emulator == AzaharSaves.EmulatorName)
            return ids.Ncch.Value is { } h && string.Equals(c.EmulatorRom, h.ProgramId, StringComparison.OrdinalIgnoreCase);
        if (s.Emulator is DolphinSaves.EmulatorName or DolphinSaves.PrimeHackName && ids.DiscGameId.Value is { } id &&
            (string.Equals(c.EmulatorRom, id, StringComparison.OrdinalIgnoreCase) || string.Equals(c.EmulatorRom, id[..4], StringComparison.OrdinalIgnoreCase)))
            return true;
        var name = ManifestLoader.NormalizeName(c.Name);
        return name.Length > 0 &&
               (name == ManifestLoader.NormalizeName(RomNames.CleanTitle(romName)) || name == ManifestLoader.NormalizeName(s.Shortcut.AppName));
    }

    /// <summary>
    /// A row for a shortcut's ROM that has saved nothing here yet, where its reader can say exactly where the save will be:
    /// RetroArch and melonDS (named after the ROM; not when RetroArch sorts saves by core, as the core folder is not known
    /// before the first save), Azahar (the ROM's program ID), Dolphin and PrimeHack (the disc's game ID). Switch ROMs are rows
    /// already (<see cref="SwitchEmulators.Scan(IReadOnlyList{SwitchEmulators.Setup}, IEnumerable{string})"/>). Null for every
    /// other emulator: Xenia, PCSX2, DuckStation and the rest name a save by something only the disc holds.
    /// </summary>
    public static ScanCandidate? NoSaveYet(EmulatorShortcut s)
    {
        var roots = EmuDeckRoots.Find();
        return s.Emulator switch
        {
            RetroArchSaves.EmulatorName => RomNamed(s, RetroArchConfig.Folders().Select(f => new RomSaveFolders(f.Saves, f.States, f.EmuDeck)),
                RetroArchSaves.Rules, roots, perCore: true),
            MelonDsSaves.EmulatorName => RomNamed(s, MelonDsSaves.Folders(roots, EmulatorPaths.Standalone ? MelonDsSaves.ConfigRoots() : []),
                MelonDsSaves.Rules, roots, perCore: false),
            AzaharSaves.EmulatorName => Azahar(s, AzaharSaves.Folders(roots, EmulatorPaths.Standalone ? AzaharSaves.Setups() : [])),
            DolphinSaves.EmulatorName or DolphinSaves.PrimeHackName =>
                Dolphin(s, DolphinSaves.Folders(roots, EmulatorPaths.Standalone ? DolphinSaves.UserDirs() : [])),
            _ => null,
        };
    }

    /// <summary>A save-named-after-the-ROM emulator's row for a ROM with no save yet, in its first setup's folders.</summary>
    public static ScanCandidate? RomNamed(EmulatorShortcut s, IEnumerable<RomSaveFolders> folders, RomSaveRules rules,
        IEnumerable<string> emuDeckRoots, bool perCore)
    {
        if (folders.FirstOrDefault() is not { } setup) return null;
        if (perCore && RomSaves.SafeSubdirs(setup.Saves).Length > 0) return null;
        var rom = Path.GetFileNameWithoutExtension(s.Rom);
        if (rom.Length == 0 || rom.Contains('*') || rom.Contains(':')) return null;
        var system = rules.System ?? SystemOf(s.Rom);
        return new ScanCandidate(
            Name: RomNames.TitleFor(rom, system, GamelistXml.Find(emuDeckRoots), rules.KnownTitle?.Invoke(rom)),
            SuggestedSaveDir: EmuDeckRoots.RealPath(setup.Saves),
            Source: ScanSource.Emulator,
            HasSteamCloud: false,
            EmulatorName: rules.EmulatorName,
            EmulatorSystem: system,
            EmulatorRom: rom,
            ViaEmuDeck: setup.EmuDeck,
            IncludeGlobs: rules.SaveGlobs(rom, rules.SaveExtension),
            ExtraSaveDirs: rules.StateGlobs is { } states && setup.States is { } dir
                ? [new DeclaredSavePath(RomSaves.StatesKey, EmuDeckRoots.RealPath(dir), states(rom))]
                : null,
            NoSaveYet: true);
    }

    /// <summary>An Azahar row for a ROM with no save yet: its NCCH program ID is its save folder's, in the first setup's SD card.</summary>
    public static ScanCandidate? Azahar(EmulatorShortcut s, IEnumerable<AzaharFolders> folders)
    {
        if (Ncch.Read(s.Rom) is not { } h || !h.ProgramId.StartsWith(AzaharSaves.GameTitles, StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var setup in folders)
        {
            if (AzaharSaves.TitleDirs(setup.Sdmc).FirstOrDefault() is not { } titleDir) continue;
            var programId = h.ProgramId.ToUpperInvariant();
            var known = h.ProductCode is { } code ? ConsoleTitles.For(AzaharSaves.System, code) : null;
            return new ScanCandidate(
                Name: known?.Title ?? RomNames.CleanTitle(Path.GetFileNameWithoutExtension(s.Rom)),
                SuggestedSaveDir: EmuDeckRoots.RealPath(titleDir),
                Source: ScanSource.Emulator,
                HasSteamCloud: false,
                EmulatorName: AzaharSaves.EmulatorName,
                EmulatorSystem: AzaharSaves.System,
                EmulatorRom: programId,
                ViaEmuDeck: setup.EmuDeck,
                IncludeGlobs: [$"{programId[8..].ToLowerInvariant()}/data/**"],
                ExtraSaveDirs: setup.States is { } states
                    ? [new DeclaredSavePath(RomSaves.StatesKey, EmuDeckRoots.RealPath(states), AzaharSaves.StateGlobsFor(programId))]
                    : null,
                Release: ConsoleTitles.Release(AzaharSaves.System, [h.ProductCode, programId]),
                NoSaveYet: true);
        }
        return null;
    }

    /// <summary>A Dolphin or PrimeHack row for a disc with no save yet, by its game ID, in that emulator's first setup.</summary>
    public static ScanCandidate? Dolphin(EmulatorShortcut s, IEnumerable<DolphinFolders> folders)
    {
        if (DolphinSaves.DiscGameId(s.Rom) is not { } id) return null;
        return folders.Where(f => f.Emulator == s.Emulator).Select(f => DolphinSaves.ForGameId(f, id)).FirstOrDefault(c => c is not null)
            is { } row ? row with { NoSaveYet = true } : null;
    }

    /// <summary>The <c>roms/&lt;system&gt;</c> folder a ROM is in (EmuDeck's console vocabulary), or null.</summary>
    private static string? SystemOf(string rom)
    {
        var parts = rom.Split('/', '\\');
        var at = Array.FindLastIndex(parts, p => p.Equals("roms", StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 2 < parts.Length ? parts[at + 1] : null;
    }

    /// <summary>
    /// Record on each emulator game tracked here the shortcuts a scan found for it — a game added before shortcuts were read, or
    /// a ROM SRM added since — so the launch wrapper finds it by any of them, and one removed since is dropped. A game the scan
    /// found no shortcut for keeps the ones it had: an unreadable Steam folder says nothing about them. The games changed.
    /// </summary>
    public static IReadOnlyList<TrackedGame> Backfill(AgentConfig config, IEnumerable<ScanCandidate> candidates)
    {
        var changed = new List<TrackedGame>();
        // Which game this scan gives each shortcut to (null: none tracked here).
        var owner = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            if (c.ShortcutAppIds is not { Count: > 0 } ids) continue;
            var tracked = Enroller.TrackedFor(config, c);
            foreach (var id in ids) owner[id] = tracked?.GameId;
            if (tracked is not { IsEnrolledHere: true } game) continue;
            if (ids.ToHashSet(StringComparer.Ordinal).SetEquals(game.ShortcutAppIds) && !string.IsNullOrWhiteSpace(game.SteamAppId)) continue;
            config.SaveGameShortcuts(game.GameId, ids);
            changed.Add(game);
        }
        // v0.8.0 matched emulator saves to PC games and recorded their shortcuts there, so a launch of the emulator's shortcut
        // synced the PC game. Such a game is never an emulator's, so a shortcut this scan gives to another save goes (and an
        // app ID taken from it with it). Only those: one the scan did not see (an SD card out, an unreadable Steam folder) says
        // nothing, and dropping it would leave a launch through it syncing nothing until the next Add games.
        foreach (var game in config.Games.Where(g => g.ShortcutAppIds.Count > 0 && NeverAnEmulatorGame(g)).ToList())
        {
            var stray = game.ShortcutAppIds.Where(id => owner.TryGetValue(id, out var o) && o != game.GameId).ToHashSet(StringComparer.Ordinal);
            if (stray.Count == 0) continue;
            config.SaveGameShortcuts(game.GameId, game.ShortcutAppIds.Where(id => !stray.Contains(id)).ToList());
            if (!changed.Contains(game)) changed.Add(game);
        }
        return changed;
    }

    /// <summary>A store's PC game, or one with no source that names no save files and no emulator title: what the v0.8.0 match
    /// put shortcuts on. An emulator game added before sources were recorded has a scope or a key, so it keeps its own.</summary>
    private static bool NeverAnEmulatorGame(TrackedGame g) => g.Source switch
    {
        { Kind: GameSourceKinds.Emulator } => false,
        { } source => Enroller.IsStoreKind(source.Kind),
        null => g.IncludeGlobs.Count == 0 && g.EmulatorKey is null,
    };

    /// <summary>
    /// Every emulator shortcut in the Steam installs under <paramref name="steamRoots"/>, read from their <c>shortcuts.vdf</c>
    /// and SRM's records — what both scanners hand <see cref="Apply"/>.
    /// </summary>
    public static async Task<IReadOnlyList<EmulatorShortcut>> FindAsync(IEnumerable<string> steamRoots, CancellationToken ct = default)
    {
        var found = new List<EmulatorShortcut>();
        foreach (var root in steamRoots)
        {
            var srm = SrmAppIds(root);
            foreach (var s in await SteamShortcuts.ReadAllAsync(root, ct))
                if (Recognize(s, srm) is { } e) found.Add(e);
        }
        return found;
    }
}
