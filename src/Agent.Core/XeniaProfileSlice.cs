using System.Text.RegularExpressions;

namespace SaveLocker.Agent;

/// <summary>
/// A Xenia game's own part of its profile (tasks/emulator-saves Phase 5c, step 5). The game's folder <c>profile</c> is its
/// main folder, <c>content/&lt;XUID&gt;</c> — a folder may not sit inside another of the same game, but two may share a
/// directory when both are scoped — scoped to the profile's copy of the game's achievements file,
/// <c>FFFE07D1/00010000/*/&lt;title ID&gt;.gpd</c>, and to <c>SaveLocker-&lt;title ID&gt;.row</c> beside it: the game's row of
/// the dashboard <c>FFFE07D1.gpd</c>. <c>*</c> rather than the XUID keeps the scope the same on every machine.
/// The dashboard holds every game's row, so it is never synced whole: before a push the game's row is copied out to its
/// file, and after a pull it is merged back in, leaving the other games' rows as they are. Xenia loads a game's GPD only
/// when the dashboard lists the game, which is why the row travels with it. No two games share a file, so
/// <see cref="SaveFolderClaims"/> holds.
/// </summary>
public static partial class XeniaProfileSlice
{
    public const string Key = "profile";
    private const string ProfileGlob = XeniaProfile.Dashboard + "/00010000/*/";

    public static string RowFile(string titleId) => $"SaveLocker-{titleId.ToUpperInvariant()}.row";

    /// <summary>The profile's core as this game last saw it (step 6): the account and the dashboard's own settings,
    /// without any game's row or the totals over them. Only ever unpacked where the profile is missing.</summary>
    public static string CoreFile(string titleId) => $"SaveLocker-{titleId.ToUpperInvariant()}.core";

    // Where the core keeps Account: a namespace no Xbox file uses, in a file Xenia never opens.
    private const ushort AccountNamespace = 0xFFFF;

    public static IReadOnlyList<string> ScopeFor(string titleId) =>
        [$"{ProfileGlob}{titleId.ToUpperInvariant()}.gpd", ProfileGlob + RowFile(titleId), ProfileGlob + CoreFile(titleId)];

    [GeneratedRegex(@"^FFFE07D1/00010000/\*/([0-9A-F]{8})\.gpd$", RegexOptions.IgnoreCase)]
    private static partial Regex GpdScope();

    /// <summary>This machine's profile folder (<c>content/&lt;XUID&gt;/FFFE07D1/00010000/&lt;XUID&gt;</c>) and the game's title
    /// ID, or null when the game has no profile folder mapped here.</summary>
    public static (string Dir, string TitleId)? Of(TrackedGame game)
    {
        var folder = game.ExtraPaths.FirstOrDefault(p => p.Key == Key && p.IsMapped);
        if (folder?.Directory is not { } home) return null;
        home = Path.TrimEndingDirectorySeparator(home);
        if (XeniaSaves.Xuid(Path.GetFileName(home)) is not { } xuid) return null;
        var tid = folder.IncludeGlobs.Select(g => GpdScope().Match(g.Replace('\\', '/'))).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        return tid is null ? null : (XeniaProfile.Folder(Path.GetDirectoryName(home)!, xuid), tid.ToUpperInvariant());
    }

    /// <summary>Copies the game's dashboard row and the profile's core out to their files, before a push hashes the
    /// folders. Each is written only when it changed, so an unchanged profile never looks like a change. A pulled row still
    /// waiting to be merged (Xenia was running) is merged first, and while it still waits the row is not exported: this
    /// machine's older row would replace the pulled one, and the push would carry it to every machine.</summary>
    public static void Export(TrackedGame game, Action<string> log, Func<bool>? xeniaRunning = null, string? stateDir = null)
    {
        if (Of(game) is not { } slice) return;
        var dashboard = Path.Combine(slice.Dir, XeniaProfile.Dashboard + ".gpd");
        var rowWaits = stateDir is not null && File.Exists(PendingFile(stateDir, game.GameId))
                       && !Merge(game, log, xeniaRunning, stateDir) && File.Exists(PendingFile(stateDir, game.GameId));
        try
        {
            if (!File.Exists(dashboard)) return;
            var bytes = File.ReadAllBytes(dashboard);
            if (rowWaits)
                log($"[{game.Name}] its pulled row of the Xenia profile is not merged yet, so this push keeps it as pulled.");
            else if (XeniaProfile.TitleRow(bytes, Convert.ToUInt32(slice.TitleId, 16)) is { } row)
                WriteIfChanged(Path.Combine(slice.Dir, RowFile(slice.TitleId)), row);
            var account = Path.Combine(slice.Dir, "Account");
            if (File.Exists(account))
                WriteIfChanged(Path.Combine(slice.Dir, CoreFile(slice.TitleId)), Core(bytes, File.ReadAllBytes(account)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log($"[{game.Name}] could not read its row of the Xenia profile: {ex.Message}");
        }
    }

    /// <summary>The marker of a pulled row that waits to be merged, beside the agent's other state: the pull and the next
    /// push may run in different processes (the Linux launch wrapper and the daemon).</summary>
    private static string PendingFile(string stateDir, Guid gameId) =>
        Path.Combine(stateDir, "xenia", "merge-pending", gameId.ToString("N"));

    private static void SetPending(string? stateDir, Guid gameId, bool pending)
    {
        if (stateDir is null) return;
        var file = PendingFile(stateDir, gameId);
        try
        {
            if (pending)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (!File.Exists(file)) AtomicFile.WriteAllText(file, DateTime.UtcNow.ToString("O"));
            }
            else if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void WriteIfChanged(string path, byte[] bytes)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;
        AtomicFile.WriteAllBytes(path, bytes);
    }

    private static bool IsTotal(ushort ns, ulong id) => ns == Xdbf.Setting &&
        id is XeniaProfile.GamerscoreSetting or XeniaProfile.TitlesPlayedSetting or XeniaProfile.AchievementsSetting;

    /// <summary>The core: every dashboard entry but the game rows and their totals, plus Account.</summary>
    public static byte[] Core(byte[] dashboard, byte[] account)
    {
        var x = new Xdbf();
        foreach (var (ns, id, data) in Xdbf.Parse(dashboard).Entries)
            if (ns != Xdbf.Title && !IsTotal(ns, id)) x.Set(ns, id, data);
        x.Set(AccountNamespace, 0, account);
        return x.Serialize();
    }

    /// <summary>What a pull runs after it restored the game's folders: the profile unpacked where it is missing, then the
    /// game's row merged in.</summary>
    public static void AfterPull(TrackedGame game, Action<string> log, Func<bool>? xeniaRunning = null, string? stateDir = null)
    {
        Seed(game, log, xeniaRunning);
        Merge(game, log, xeniaRunning, stateDir);
    }

    /// <summary>
    /// Unpacks the pulled core where this machine has no copy of the profile (no Account): Account, and a dashboard of the
    /// core's settings and this game's row. A profile already here is never touched — its settings and its other games'
    /// rows are its own. Returns whether it unpacked one.
    /// </summary>
    public static bool Seed(TrackedGame game, Action<string> log, Func<bool>? xeniaRunning = null)
    {
        if (Of(game) is not { } slice) return false;
        var account = Path.Combine(slice.Dir, "Account");
        var core = Path.Combine(slice.Dir, CoreFile(slice.TitleId));
        try
        {
            if (File.Exists(account) || !File.Exists(core)) return false;
            if ((xeniaRunning ?? XeniaLaunchProfile.XeniaRunning)()) return false;
            var x = Xdbf.Parse(File.ReadAllBytes(core));
            if (x.Get(AccountNamespace, 0) is not { } accountBytes) return false;
            x.Remove(AccountNamespace, 0);
            var dashboard = Path.Combine(slice.Dir, XeniaProfile.Dashboard + ".gpd");
            if (!File.Exists(dashboard))
            {
                var row = Path.Combine(slice.Dir, RowFile(slice.TitleId));
                if (File.Exists(row)) x.Set(Xdbf.Title, Convert.ToUInt32(slice.TitleId, 16), File.ReadAllBytes(row));
                XeniaProfile.Recount(x);
                AtomicFile.WriteAllBytes(dashboard, x.Serialize());
            }
            AtomicFile.WriteAllBytes(account, accountBytes);
            log($"[{game.Name}] copied its Xenia profile ({XeniaProfile.Gamertag(accountBytes) ?? "unnamed"}) to this machine.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log($"[{game.Name}] could not copy its Xenia profile here: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Merges the pulled row into this machine's dashboard: that game's row replaced or added, the totals recounted, every
    /// other row kept. Nothing happens without a dashboard here (the profile itself is not on this machine yet), or while
    /// Xenia runs, which keeps the dashboard open and writes it back on exit; then, given <paramref name="stateDir"/>, the
    /// row is marked as waiting, and the next push merges it before anything else (<see cref="Export"/>).
    /// </summary>
    public static bool Merge(TrackedGame game, Action<string> log, Func<bool>? xeniaRunning = null, string? stateDir = null)
    {
        if (Of(game) is not { } slice) return false;
        var file = Path.Combine(slice.Dir, RowFile(slice.TitleId));
        var dashboard = Path.Combine(slice.Dir, XeniaProfile.Dashboard + ".gpd");
        try
        {
            if (!File.Exists(file) || !File.Exists(dashboard))
            {
                if (File.Exists(file)) log($"[{game.Name}] its achievements arrived, but this machine has no copy of the profile yet.");
                SetPending(stateDir, game.GameId, false);
                return false;
            }
            if ((xeniaRunning ?? XeniaLaunchProfile.XeniaRunning)())
            {
                log($"[{game.Name}] Xenia is running, so its row of the profile is merged once it has closed.");
                SetPending(stateDir, game.GameId, true);
                return false;
            }
            var before = File.ReadAllBytes(dashboard);
            var row = File.ReadAllBytes(file);
            var tid = Convert.ToUInt32(slice.TitleId, 16);
            if (XeniaProfile.TitleRow(before, tid) is { } current && current.AsSpan().SequenceEqual(row))
            {
                SetPending(stateDir, game.GameId, false);
                return false;
            }
            AtomicFile.WriteAllBytes(dashboard, XeniaProfile.MergeTitleRow(before, tid, row));
            SetPending(stateDir, game.GameId, false);
            log($"[{game.Name}] merged its row into the Xenia profile; other games' rows kept.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log($"[{game.Name}] could not merge its row into the Xenia profile: {ex.Message}");
            return false;
        }
    }
}
