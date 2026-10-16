using System.Text.Json;
using System.Text.RegularExpressions;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// The Switch emulators (tasks/emulator-saves Phase 6): Yuzu and its forks — Citron, Eden, Suyu, Sudachi — and Ryujinx. A
/// Switch save is the title's own folder, so every emulator's copy of one title holds the same files, and the game is one
/// server game across all of them, keyed by the title (D2, <see cref="EmulatorKeys"/>; the maintainer's call, 2026-10-13).
/// <para>
/// <b>Yuzu forks</b> save under <c>nand/user/save/0000000000000000/&lt;profile&gt;/&lt;title ID&gt;</c>; a device save (Animal
/// Crossing's island) under the all-zero profile. The profile is <c>profiles.dat</c>'s user ID with its 16 bytes reversed
/// (read on both machines: <c>ca0ef0a0…c4</c> → <c>C4D7AB8B…0ECA</c>). Yuzu also leaves a mangled folder holding only
/// <c>.yuzu_save_size</c> beside the real one (seen on both), which is no save.
/// <b>Ryujinx</b> saves under <c>bis/user/save/&lt;save ID&gt;/0</c> (the committed copy; <c>1</c> is the working one), the ID
/// from <see cref="RyujinxSaveIndex"/>.
/// </para>
/// <para>
/// Where (EmuDeck's scripts, both OSes, and captures): EmuDeck links <c>Emulation/saves/&lt;fork&gt;/saves</c> with the NAND's
/// <c>user/save</c> and <c>…/profiles</c> with the avatars folder that holds <c>profiles.dat</c>; Ryujinx's
/// <c>saves/ryujinx/{saves,system_saves,system}</c> with <c>bis/user/save</c>, <c>bis/system/save</c> and <c>system</c>, its
/// <c>games</c> moved to <c>storage/ryujinx/games</c>. Standalone: <c>qt-config.ini</c>'s <c>nand_directory</c>, else
/// <c>&lt;data&gt;/nand</c> — Linux <c>~/.config/&lt;fork&gt;</c> + <c>~/.local/share/&lt;fork&gt;</c>, Windows
/// <c>%APPDATA%\&lt;fork&gt;</c>, EmuDeck for Windows' portable <c>user</c> folders; Ryujinx <c>~/.config/Ryujinx</c>, the
/// Flatpak's, <c>%APPDATA%\Ryujinx</c>, EmuDeck for Windows' <c>Ryujinx\portable</c>.
/// </para>
/// </summary>
public static partial class SwitchEmulators
{
    public const string System = "switch";
    public const string Ryujinx = "Ryujinx";
    /// <summary>The all-zero profile a device save is filed under.</summary>
    public const string DeviceProfile = "00000000000000000000000000000000";
    public const string DeviceFolderKey = "device";

    /// <summary>One Yuzu fork: its name, its folder names, and where EmuDeck for Windows unpacks it.</summary>
    public sealed record Fork(string Name, string Folder, string? FlatpakId, string? EmuDeckWindows);

    /// <summary>The forks, one row each, so a new one is one line. EmuDeck has a script for every one but Sudachi.</summary>
    public static readonly IReadOnlyList<Fork> Forks =
    [
        new("Eden", "eden", null, Path.Combine("eden-windows-msvc", "user")),
        new("Citron", "citron", null, Path.Combine("citron", "user")),
        new("Yuzu", "yuzu", "org.yuzu_emu.yuzu", Path.Combine("yuzu", "yuzu-windows-msvc", "user")),
        new("Suyu", "suyu", "org.suyu_emu.suyu", null),
        new("Sudachi", "sudachi", null, null),
    ];

    /// <summary>Every Switch emulator's name, as a save's <see cref="ScanCandidate.EmulatorName"/> says it.</summary>
    public static readonly IReadOnlyList<string> Names = [.. Forks.Select(f => f.Name), Ryujinx];

    /// <summary>Is this one of the Switch emulators?</summary>
    public static bool IsSwitch(string? emulator) =>
        emulator is not null && Names.Contains(emulator, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// One installed Switch emulator. <see cref="Saves"/> is the NAND's <c>user/save</c> (a fork) or <c>bis/user/save</c>
    /// (Ryujinx), by its real path. A fork's <see cref="Profiles"/> is its <c>profiles.dat</c> and <see cref="Config"/> its
    /// <c>qt-config.ini</c>; Ryujinx's are <c>Profiles.json</c> and the <c>imkvdb.arc</c> in <see cref="SystemSaves"/>.
    /// <see cref="Data"/> is the emulator's own folder, which holds its logs (<see cref="SwitchGameVersion"/>).
    /// </summary>
    public sealed record Setup(string Emulator, string Saves, string? Profiles = null, string? Config = null,
        string? SystemSaves = null, string? Games = null, bool EmuDeck = false, string? Data = null)
    {
        public bool IsRyujinx => string.Equals(Emulator, Ryujinx, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One user of an emulator: the folder or ID a save is filed under, and the name to show.</summary>
    public sealed record User(string Id, string Name);

    /// <summary>
    /// Where one title's save is, or would go, in one setup. <see cref="Dir"/> is the account save (the device save when the
    /// title has only that); <see cref="DeviceDir"/> the device save beside an account one. <see cref="Exists"/>: it has files.
    /// </summary>
    public sealed record TitleSave(Setup Setup, string TitleId, string Dir, string? DeviceDir, string? UserName, bool Exists, DateTime WrittenUtc,
        string? AccountDir = null);

    // ---------------------------------------------------------------- finding the emulators

    /// <summary>Every Switch emulator on this machine, EmuDeck's links first; one per real saves folder.</summary>
    public static IReadOnlyList<Setup> Find() =>
        Dedupe(FromEmuDeck(EmuDeckRoots.Find()).Concat(EmulatorPaths.Standalone ? Standalone() : []));

    /// <summary>The setups EmuDeck's <c>Emulation/saves</c> links lead to.</summary>
    public static IEnumerable<Setup> FromEmuDeck(IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            var saves = Path.Combine(root, "saves");
            foreach (var fork in Forks)
                yield return new Setup(fork.Name, Path.Combine(saves, fork.Folder, "saves"),
                    Profiles: Path.Combine(saves, fork.Folder, "profiles", "profiles.dat"), EmuDeck: true);
            var ryu = Path.Combine(saves, "ryujinx");
            yield return new Setup(Ryujinx, Path.Combine(ryu, "saves"), Profiles: Path.Combine(ryu, "system", "Profiles.json"),
                SystemSaves: Path.Combine(ryu, "system_saves"), Games: Path.Combine(root, "storage", "ryujinx", "games"), EmuDeck: true);
        }
    }

    /// <summary>The standalone and portable installs, per platform.</summary>
    public static IEnumerable<Setup> Standalone()
    {
        foreach (var fork in Forks)
            foreach (var (config, data) in ForkFolders(fork))
                if (YuzuSetup(fork.Name, config, data) is { } s) yield return s;
        foreach (var dir in RyujinxFolders())
            yield return new Setup(Ryujinx, Path.Combine(dir, "bis", "user", "save"), Profiles: Path.Combine(dir, "system", "Profiles.json"),
                SystemSaves: Path.Combine(dir, "bis", "system", "save"), Games: Path.Combine(dir, "games"), Data: dir);
    }

    private static IEnumerable<(string Config, string Data)> ForkFolders(Fork fork)
    {
        if (OperatingSystem.IsWindows())
        {
            if (fork.EmuDeckWindows is { } portable)
            {
                var user = Path.Combine(EmulatorPaths.AppData, "EmuDeck", "Emulators", portable);
                yield return (Path.Combine(user, "config", "qt-config.ini"), user);
            }
            var roaming = Path.Combine(EmulatorPaths.AppData, fork.Folder);
            yield return (Path.Combine(roaming, "config", "qt-config.ini"), roaming);
            yield break;
        }
        yield return (Path.Combine(EmulatorPaths.XdgConfig, fork.Folder, "qt-config.ini"), Path.Combine(EmulatorPaths.XdgData, fork.Folder));
        if (fork.FlatpakId is { } id)
        {
            var app = EmulatorPaths.Flatpak(id);
            yield return (Path.Combine(app, "config", fork.Folder, "qt-config.ini"), Path.Combine(app, "data", fork.Folder));
        }
    }

    private static IEnumerable<string> RyujinxFolders()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(EmulatorPaths.AppData, "EmuDeck", "Emulators", "Ryujinx", "portable");
            yield return Path.Combine(EmulatorPaths.AppData, "Ryujinx");
            yield break;
        }
        yield return Path.Combine(EmulatorPaths.XdgConfig, "Ryujinx");
        yield return Path.Combine(EmulatorPaths.Flatpak("org.ryujinx.Ryujinx"), "config", "Ryujinx");
    }

    /// <summary>A Yuzu fork's setup from its <c>qt-config.ini</c> and data folder: the NAND it names, else the default.</summary>
    public static Setup YuzuSetup(string emulator, string config, string data)
    {
        var nand = IniValue(config, "nand_directory") is { } configured
            ? EmulatorPaths.IniFolder(configured, Path.GetDirectoryName(config)!) ?? Path.Combine(data, "nand")
            : Path.Combine(data, "nand");
        return new Setup(emulator, Path.Combine(nand, "user", "save"),
            Profiles: Path.Combine(nand, "system", "save", "8000000000000010", "su", "avators", "profiles.dat"), Config: config, Data: data);
    }

    /// <summary>One per real saves folder that exists. A later row of the same folder fills in what the first lacked — an
    /// EmuDeck link knows it is EmuDeck's, the standalone row knows the emulator's config.</summary>
    public static IReadOnlyList<Setup> Dedupe(IEnumerable<Setup> setups)
    {
        var found = new List<Setup>();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var s in setups)
        {
            bool exists;
            try { exists = Directory.Exists(s.Saves); } catch { exists = false; }
            if (!exists) continue;
            var real = EmuDeckRoots.RealPath(s.Saves);
            var at = found.FindIndex(f => comparer.Equals(f.Saves, real) && string.Equals(f.Emulator, s.Emulator, StringComparison.OrdinalIgnoreCase));
            if (at < 0) { found.Add(s with { Saves = real }); continue; }
            var f = found[at];
            found[at] = f with
            {
                Config = f.Config ?? s.Config,
                Profiles = ExistingFile(f.Profiles) ?? s.Profiles,
                SystemSaves = ExistingDir(f.SystemSaves) ?? s.SystemSaves,
                Games = ExistingDir(f.Games) ?? s.Games,
                EmuDeck = f.EmuDeck || s.EmuDeck,
                Data = f.Data ?? s.Data,
            };
        }
        return found;
    }

    private static string? ExistingFile(string? p) => p is not null && File.Exists(p) ? p : null;
    private static string? ExistingDir(string? p) => p is not null && Directory.Exists(p) ? p : null;

    // ---------------------------------------------------------------- users

    /// <summary>The users of a setup, the current one first: a fork's <c>profiles.dat</c> (<c>current_user</c> in its config
    /// picks one), Ryujinx's <c>Profiles.json</c> (<c>last_opened</c>).</summary>
    public static IReadOnlyList<User> Users(Setup s)
    {
        var users = s.IsRyujinx ? RyujinxUsers(s.Profiles) : YuzuUsers(s.Profiles);
        var current = s.IsRyujinx ? RyujinxCurrent(s.Profiles)
            : int.TryParse(IniValue(s.Config, "current_user"), out var i) && i >= 0 && i < users.Count ? users[i].Id : null;
        return current is null ? users : [.. users.Where(u => u.Id == current), .. users.Where(u => u.Id != current)];
    }

    /// <summary><c>profiles.dat</c>: 0x10 bytes of header, then up to eight 0xC8-byte users — a 16-byte UUID, a copy, a u64
    /// timestamp, a 0x20-byte name. The save folder is the UUID's bytes reversed, as upper-case hex.</summary>
    public static IReadOnlyList<User> YuzuUsers(string? profilesDat)
    {
        byte[] data;
        try
        {
            if (profilesDat is null || !File.Exists(profilesDat)) return [];
            data = File.ReadAllBytes(profilesDat);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        var users = new List<User>();
        for (var at = 0x10; at + 0xC8 <= data.Length && users.Count < 8; at += 0xC8)
        {
            var uuid = data.AsSpan(at, 16);
            if (!uuid.ContainsAnyExcept((byte)0)) continue;
            var reversed = uuid.ToArray();
            Array.Reverse(reversed);
            var name = global::System.Text.Encoding.UTF8.GetString(data, at + 0x28, 0x20).TrimEnd('\0');
            users.Add(new User(Convert.ToHexString(reversed), name.Length == 0 ? "User" : name));
        }
        return users;
    }

    private static IReadOnlyList<User> RyujinxUsers(string? profilesJson) =>
        ReadJson(profilesJson, root =>
            root.TryGetProperty("profiles", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray()
                    .Where(p => p.TryGetProperty("user_id", out var id) && id.ValueKind == JsonValueKind.String)
                    .Select(p => new User(p.GetProperty("user_id").GetString()!.ToLowerInvariant(),
                        p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "User"))
                    .ToList()
                : []) ?? [];

    private static string? RyujinxCurrent(string? profilesJson) =>
        ReadJson(profilesJson, root => root.TryGetProperty("last_opened", out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.ToLowerInvariant() : null);

    // ---------------------------------------------------------------- one title's save

    /// <summary>Every title with a save in this setup, each where the emulator would load it from.</summary>
    public static IReadOnlyList<TitleSave> Saves(Setup s)
    {
        if (s.IsRyujinx)
        {
            var index = s.SystemSaves is null ? null : RyujinxSaveIndex.IndexFile(s.SystemSaves);
            if (index is null) return [];
            return RyujinxSaveIndex.Read(index).Select(e => e.TitleId).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(t => Slot(s, t)).Where(t => t is { Exists: true }).Select(t => t!).ToList();
        }
        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in ProfileFolders(s))
            foreach (var dir in SafeDirs(profile.FullName))
                if (IsTitleId(dir.Name)) titles.Add(dir.Name.ToUpperInvariant());
        return titles.Select(t => Slot(s, t)).Where(t => t is { Exists: true }).Select(t => t!).OrderBy(t => t.TitleId).ToList();
    }

    /// <summary>
    /// Where <paramref name="titleId"/>'s save is in this setup — the current user's when it has one, else the user whose copy
    /// was written last — or where it would go for the current user when there is none yet. Null when it cannot go anywhere:
    /// a fork with no user, or Ryujinx with no save slot for the title (it makes one the first time the game runs; SaveLocker
    /// never writes its index).
    /// </summary>
    public static TitleSave? Slot(Setup s, string titleId)
    {
        var users = Users(s);
        var names = users.ToDictionary(u => u.Id, u => u.Name, StringComparer.OrdinalIgnoreCase);
        titleId = titleId.ToUpperInvariant();
        if (s.IsRyujinx)
        {
            var index = s.SystemSaves is null ? null : RyujinxSaveIndex.IndexFile(s.SystemSaves);
            var entries = index is null ? [] : RyujinxSaveIndex.Read(index).Where(e => e.TitleId == titleId).ToList();
            string Dir(RyujinxSaveIndex.Entry e) => Path.Combine(s.Saves, e.Folder, "0");
            var accounts = entries.Where(e => e.Type == RyujinxSaveIndex.SaveType.Account).ToList();
            var current = users.FirstOrDefault()?.Id;
            var account = accounts.Where(e => HasFiles(Dir(e))).OrderByDescending(e => e.UserId == current).ThenByDescending(e => Written(Dir(e))).FirstOrDefault()
                          ?? accounts.FirstOrDefault(e => e.UserId == current) ?? accounts.FirstOrDefault();
            var device = entries.FirstOrDefault(e => e.Type == RyujinxSaveIndex.SaveType.Device);
            var main = account ?? device;
            if (main is null) return null;
            var deviceDir = account is not null && device is not null ? Dir(device) : null;
            return Make(s, titleId, Dir(main), deviceDir, main == device ? "Device" : names.GetValueOrDefault(main.UserId));
        }

        var profiles = ProfileFolders(s).ToList();
        string At(string profile) => Path.Combine(s.Saves, "0000000000000000", profile, titleId);
        var saved = profiles.Where(p => !string.Equals(p.Name, DeviceProfile, StringComparison.OrdinalIgnoreCase))
            .Select(p => At(p.Name)).Where(HasFiles).ToList();
        var deviceSave = At(DeviceProfile);
        var currentUser = users.FirstOrDefault()?.Id;
        var accountDir = saved.OrderByDescending(d => currentUser is not null && d.Contains(currentUser, StringComparison.OrdinalIgnoreCase))
                             .ThenByDescending(Written).FirstOrDefault();
        if (accountDir is null && HasFiles(deviceSave))
            return Make(s, titleId, deviceSave, null, "Device", currentUser is null ? null : At(currentUser));
        accountDir ??= currentUser is null ? null : At(currentUser);
        if (accountDir is null) return null;
        var user = Path.GetFileName(Path.GetDirectoryName(accountDir));
        return Make(s, titleId, accountDir, HasFiles(deviceSave) ? deviceSave : null, names.GetValueOrDefault(user!));
    }

    /// <summary>
    /// A Switch game's folders here, laid out as the server's game has them, so each folder key holds the same save on every
    /// machine. A title with only a device save (Animal Crossing's island) is found as one main folder; one with an account
    /// save beside it as main plus <c>device</c>. A machine with a stray account copy beside the device save (the maintainer's
    /// PC, 2026-10-16: an old island under Eden's user) then filed the real save under <c>device</c> while the Deck synced its
    /// island as <c>main</c> — the Deck's save landed in the PC's stray copy, and Eden kept loading the old one.
    /// <list type="bullet">
    /// <item>The game has no device folder and this machine found one: the device save is the main folder here; the account
    /// copy is left alone.</item>
    /// <item>The game has a device folder and this machine found only the device save: it is the device folder here, and the
    /// main folder is the current user's (<see cref="ScanCandidate.SwitchAccountDir"/>, empty until a pull fills it). Yuzu
    /// forks only: Ryujinx has no account folder for a title until the game makes one.</item>
    /// </list>
    /// Null when nothing changes.
    /// </summary>
    public static (string Dir, IReadOnlyList<DeclaredSavePath> Extras, string Note)? AlignToGameFolders(
        ScanCandidate c, GameDto game, string dir, IReadOnlyList<DeclaredSavePath> extras)
    {
        if (SwitchTitles.TitleIdOf(c.EmulatorKey) is not { } title) return null;
        var gameHasDevice = (game.ExtraPaths ?? []).Any(p => p.Key == DeviceFolderKey);
        var device = extras.FirstOrDefault(e => e.Key == DeviceFolderKey);
        if (!gameHasDevice && device is not null)
            return (device.Dir, extras.Where(e => e != device).ToList(),
                $"{c.Name}: your other devices keep this game's save in the device folder, so it syncs from {device.Dir} here. " +
                $"The copy under the emulator's user ({dir}) was left as it is.");
        if (!gameHasDevice || device is not null || c.SwitchAccountDir is not { } account) return null;
        return (account, [.. extras, new DeclaredSavePath(DeviceFolderKey, dir)],
            $"{c.Name}: your other devices keep this game in two folders, so its device save here syncs as the device folder, " +
            $"and the emulator user's folder ({account}) takes the other one.");
    }

    private static TitleSave Make(Setup s, string titleId, string dir, string? deviceDir, string? user, string? accountDir = null)
    {
        var exists = HasFiles(dir);
        var written = new[] { dir, deviceDir }.Where(d => d is not null).Select(d => Written(d!)).DefaultIfEmpty(DateTime.MinValue).Max();
        return new TitleSave(s, titleId, dir, deviceDir, user, exists, written, accountDir);
    }

    /// <summary>A fork's profile folders under <c>save/0000000000000000</c>: the users <c>profiles.dat</c> names and the device
    /// profile — never Yuzu's mangled leftovers — or every 32-digit folder when <c>profiles.dat</c> can't be read.</summary>
    private static IEnumerable<DirectoryInfo> ProfileFolders(Setup s)
    {
        var known = YuzuUsers(s.Profiles).Select(u => u.Id).Append(DeviceProfile).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var any = known.Count == 1;
        return SafeDirs(Path.Combine(s.Saves, "0000000000000000"))
            .Where(d => d.Name.Length == 32 && d.Name.All(char.IsAsciiHexDigit) && (any || known.Contains(d.Name)));
    }

    // ---------------------------------------------------------------- the scan

    /// <summary>Every Switch game on this machine.</summary>
    public static IReadOnlyList<ScanCandidate> Scan() => Scan(Find(), EmuDeckRoots.Find());

    /// <summary>
    /// One row per title: the copy written last, whichever emulator has it — the others are this game's copies on this
    /// machine (<see cref="SwitchCopies"/>). A title with a ROM in <c>Emulation/roms/switch</c> and no save anywhere here is
    /// listed too, under a Yuzu fork's current user, so the first pull puts the fleet's save where that fork will load it.
    /// </summary>
    public static IReadOnlyList<ScanCandidate> Scan(IReadOnlyList<Setup> setups, IEnumerable<string> emuDeckRoots)
    {
        var roms = Roms(emuDeckRoots);
        var titles = Titles(setups.Where(s => s.IsRyujinx).Select(s => s.Games), roms);
        var saves = setups.SelectMany(Saves).ToList();
        var found = new List<ScanCandidate>();
        foreach (var group in saves.GroupBy(t => t.TitleId))
            found.Add(Candidate(group.OrderByDescending(t => t.WrittenUtc).First(), titles, noSaveYet: false));
        foreach (var (titleId, folder) in roms.Where(r => saves.All(s => s.TitleId != r.Key)).Select(r => (r.Key, r.Value.Folder)))
        {
            var forks = setups.Where(s => !s.IsRyujinx)
                .OrderByDescending(s => folder is not null && string.Equals(s.Emulator, folder, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(s => s.EmuDeck);
            if (forks.Select(s => Slot(s, titleId)).FirstOrDefault(t => t is not null) is { } slot)
                found.Add(Candidate(slot, titles, noSaveYet: true));
        }
        return found.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ScanCandidate Candidate(TitleSave t, IReadOnlyDictionary<string, string> titles, bool noSaveYet) =>
        new(Name: NameFor(t.TitleId, titles),
            SuggestedSaveDir: t.Dir,
            Source: ScanSource.Emulator,
            HasSteamCloud: false,
            EmulatorName: t.Setup.Emulator,
            EmulatorSystem: System,
            EmulatorRom: t.TitleId,
            ViaEmuDeck: t.Setup.EmuDeck,
            ExtraSaveDirs: t.DeviceDir is { } device ? [new DeclaredSavePath(DeviceFolderKey, device)] : null,
            Release: new GameReleaseDto("Nintendo Switch", Region: null, GameIds: [t.TitleId]),
            EmulatorKey: EmulatorKeys.Switch(t.TitleId),
            NoSaveYet: noSaveYet,
            SwitchAccountDir: t.AccountDir);

    // ---------------------------------------------------------------- names

    /// <summary>A title's name: this machine's own (<see cref="Titles"/> — Ryujinx's record, then a ROM's file name), else the
    /// eShop's from the bundled table (<see cref="SwitchTitles"/>), else <c>Switch &lt;title ID&gt;</c>, which the server
    /// replaces as soon as any machine knows a name (D2).</summary>
    public static string NameFor(string titleId, IReadOnlyDictionary<string, string> titles) =>
        titles.GetValueOrDefault(titleId)
        ?? (SwitchTitles.For(titleId) is { } known ? CleanTitle(known.Name) : SwitchTitles.Placeholder(titleId));

    /// <summary>A Switch ROM in <c>Emulation/roms/switch</c> (or a folder in it, as EmuDeck's <c>switch/yuzu</c>) whose name
    /// carries its title ID in brackets: title ID → (its name, the folder it is in).</summary>
    public static IReadOnlyDictionary<string, (string Name, string? Folder)> Roms(IEnumerable<string> emuDeckRoots)
    {
        var roms = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in emuDeckRoots)
        {
            var dir = Path.Combine(root, "roms", System);
            IEnumerable<FileInfo> files;
            try
            {
                if (!Directory.Exists(dir)) continue;
                files = new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1, IgnoreInaccessible = true }).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var f in files)
            {
                if (!RomExtensions.Contains(f.Extension)) continue;
                var name = Path.GetFileNameWithoutExtension(f.Name);
                var id = TitleIdInName().Matches(name).Select(m => m.Groups[1].Value.ToUpperInvariant()).FirstOrDefault(IsBaseTitle);
                if (id is null) continue;
                var folder = string.Equals(f.Directory?.FullName.TrimEnd('/', '\\'), dir.TrimEnd('/', '\\'),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? null : f.Directory?.Name;
                roms.TryAdd(id, (CleanTitle(name), folder));
            }
        }
        return roms;
    }

    /// <summary>Names by title ID: Ryujinx's <c>games/&lt;title ID&gt;/gui/metadata.json</c> <c>title</c> first (the game's own),
    /// else the ROM's file name. Both are on one machine only; the title key joins the others whatever they call it (D2).</summary>
    public static IReadOnlyDictionary<string, string> Titles(IEnumerable<string?> ryujinxGames, IReadOnlyDictionary<string, (string Name, string? Folder)> roms)
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var games in ryujinxGames)
            foreach (var dir in games is null ? [] : SafeDirs(games))
            {
                if (!IsTitleId(dir.Name)) continue;
                var title = ReadJson(Path.Combine(dir.FullName, "gui", "metadata.json"),
                    root => root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null);
                if (!string.IsNullOrWhiteSpace(title)) titles.TryAdd(dir.Name.ToUpperInvariant(), CleanTitle(title));
            }
        foreach (var (id, rom) in roms) titles.TryAdd(id, rom.Name);
        return titles;
    }

    /// <summary>"Metroid Prime™ 4꞉ Beyond [010019A01E2F2000][v0][Base][XCI]" → "Metroid Prime 4: Beyond": the brackets
    /// dropped, the trademark signs too, and the look-alike colon some ROM tools write in place of one Windows forbids.</summary>
    public static string CleanTitle(string name) =>
        RomNames.CleanTitle(name.Replace('꞉', ':').Replace("™", "").Replace("®", "").Replace("©", ""));

    private static readonly HashSet<string> RomExtensions = new(StringComparer.OrdinalIgnoreCase) { ".nsp", ".xci", ".nsz", ".xcz", ".nca", ".nro" };

    [GeneratedRegex(@"\[([0-9A-Fa-f]{16})\]")]
    private static partial Regex TitleIdInName();

    /// <summary>A Switch application's title ID: 16 hex digits starting <c>01</c>; a base game's ends in <c>000</c> (an
    /// update's in <c>800</c>, DLC's elsewhere).</summary>
    public static bool IsTitleId(string name) =>
        name.Length == 16 && name.StartsWith("01", StringComparison.Ordinal) && name.All(char.IsAsciiHexDigit);

    private static bool IsBaseTitle(string id) => IsTitleId(id) && id.EndsWith("000", StringComparison.Ordinal);

    // ---------------------------------------------------------------- helpers

    /// <summary>A setting's value from a Qt ini, whatever section it is in; null when unset or unreadable.</summary>
    private static string? IniValue(string? config, string key)
    {
        if (config is null) return null;
        foreach (var section in EmulatorPaths.ReadIni(config).Values)
            if (section.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }

    private static T? ReadJson<T>(string? path, Func<JsonElement, T?> read)
    {
        try
        {
            if (path is null || !File.Exists(path)) return default;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return read(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return default; }
    }

    private static IEnumerable<DirectoryInfo> SafeDirs(string dir)
    {
        try { return Directory.Exists(dir) ? new DirectoryInfo(dir).GetDirectories().Where(d => d.LinkTarget is null).ToList() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>A save has a file with bytes in it other than Yuzu's <c>.yuzu_save_size</c>.</summary>
    public static bool HasFiles(string dir)
    {
        try
        {
            return Directory.Exists(dir) && new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
                .Any(f => f.Length > 0 && !string.Equals(f.Name, ".yuzu_save_size", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static DateTime Written(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Select(f => f.LastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max()
                : DateTime.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return DateTime.MinValue; }
    }
}
