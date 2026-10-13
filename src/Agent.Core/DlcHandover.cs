using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>What this machine does when another asks for DLC it has (tasks/emulator-saves Phase 5c). Null in the config means Ask.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DlcSendPolicy>))]
public enum DlcSendPolicy { Ask, Auto, Never }

/// <summary>
/// One Xenia package on disk for a hand-over: its folder and its header, zipped for sending, and installed from such a zip
/// on the machine that asked. Only the package's own folder and header are ever read or written.
/// </summary>
public static class DlcPackageFiles
{
    private static string TypeOf(string kind) => kind == ContentPackageDto.Update ? XeniaContent.TitleUpdateType : XeniaContent.DlcType;

    private static string HeaderPath(string home, string type, string id) => Path.Combine(home, "Headers", type, id + ".header");

    /// <summary>The title folder that holds the package, the current layout (<c>content/0000000000000000/&lt;TID&gt;</c>) first.</summary>
    public static string? FindHome(string content, string titleId, ContentPackageDto p)
    {
        var type = TypeOf(p.Kind);
        return new[] { Path.Combine(content, XeniaSaves.NoProfile, titleId), Path.Combine(content, titleId) }
            .FirstOrDefault(h => Directory.Exists(Path.Combine(h, type, p.Id)) && File.Exists(HeaderPath(h, type, p.Id)));
    }

    /// <summary>Zips the package as <c>&lt;type&gt;/&lt;id&gt;/…</c> plus <c>Headers/&lt;type&gt;/&lt;id&gt;.header</c>, stored, not compressed.</summary>
    public static void Zip(string content, string titleId, ContentPackageDto p, string zipPath)
    {
        var home = FindHome(content, titleId, p) ?? throw new FileNotFoundException($"{p.Name} is no longer installed here.");
        var type = TypeOf(p.Kind);
        var dir = Path.Combine(home, type, p.Id);
        var tmp = zipPath + ".tmp";
        File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                zip.CreateEntryFromFile(f, $"{type}/{p.Id}/" + Path.GetRelativePath(dir, f).Replace('\\', '/'), CompressionLevel.NoCompression);
            zip.CreateEntryFromFile(HeaderPath(home, type, p.Id), $"Headers/{type}/{p.Id}.header", CompressionLevel.NoCompression);
        }
        File.Move(tmp, zipPath, overwrite: true);
    }

    /// <summary>
    /// Installs a received package where this machine's Xenia reads it: <c>content/&lt;TID&gt;</c> for a game in the layout from
    /// before profiles, else <c>content/0000000000000000/&lt;TID&gt;</c>. Unpacked beside it first and checked — nothing outside the
    /// package's own folder and header, a header Xenia can read, the size the sender listed — then moved into place. A package
    /// already installed is left alone. Throws <see cref="InvalidDataException"/> when the zip fails a check.
    /// </summary>
    public static void Install(string zipPath, string content, string titleId, bool legacy, ContentPackageDto p)
    {
        if (FindHome(content, titleId, p) is not null) return;
        var type = TypeOf(p.Kind);
        var staging = Path.Combine(content, ".savelocker-incoming-" + Guid.NewGuid().ToString("N"));
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var prefix = $"{type}/{p.Id}/";
                var header = $"Headers/{type}/{p.Id}.header";
                foreach (var e in zip.Entries)
                {
                    var name = e.FullName.Replace('\\', '/');
                    if (name.EndsWith('/')) continue;
                    if (name != header && !name.StartsWith(prefix, StringComparison.Ordinal))
                        throw new InvalidDataException($"{p.Name}: the zip holds {name}, which is not part of the package.");
                    var dest = Path.GetFullPath(Path.Combine(staging, name));
                    if (!dest.StartsWith(root, StringComparison.Ordinal))
                        throw new InvalidDataException($"{p.Name}: the zip holds {name}, outside the package.");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    e.ExtractToFile(dest);
                }
            }
            var stagedHeader = HeaderPath(staging, type, p.Id);
            if (XeniaContent.ReadHeader(stagedHeader) is null) throw new InvalidDataException($"{p.Name}: no package header Xenia can read.");
            var size = FolderSize.Of(Path.Combine(staging, type, p.Id));
            if (size != p.Size) throw new InvalidDataException($"{p.Name}: {size:N0} bytes arrived, {p.Size:N0} were listed.");

            var target = legacy ? Path.Combine(content, titleId) : Path.Combine(content, XeniaSaves.NoProfile, titleId);
            Directory.CreateDirectory(Path.Combine(target, type));
            Directory.CreateDirectory(Path.Combine(target, "Headers", type));
            Directory.Move(Path.Combine(staging, type, p.Id), Path.Combine(target, type, p.Id));
            File.Move(stagedHeader, HeaderPath(target, type, p.Id), overwrite: true);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>
/// The agents' half of the DLC hand-over (tasks/emulator-saves Phase 5c, steps 12–13), run from the poller on its own task
/// so a gigabyte never holds up dashboard commands. As the machine asked: approve per <see cref="AgentConfig.DlcSend"/>
/// (or wait for the person), zip and upload each pack in pieces that resume. As the machine that asked: download (resuming),
/// install while Xenia is closed, confirm — which deletes it on the server — and say so. Then every held pull whose packs
/// are all here now goes ahead, however they arrived.
/// </summary>
public sealed class DlcHandoverRunner
{
    private const int ChunkBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan HoldCheckInterval = TimeSpan.FromMinutes(1);

    private readonly AgentConfig _config;
    private readonly Func<ApiClient> _api;
    private readonly Func<SyncEngine> _engine;
    private readonly NotificationCenter? _notices;
    private readonly Action<string> _log;
    private readonly Func<bool> _xeniaRunning;
    private readonly CancellationTokenSource _stop = new();
    private int _busy;
    private Task _last = Task.CompletedTask;
    private DateTime _lastHoldCheck = DateTime.MinValue;

    public DlcHandoverRunner(AgentConfig config, Func<ApiClient> api, Func<SyncEngine> engine, NotificationCenter? notices,
        Action<string> log, Func<bool>? xeniaRunning = null)
    {
        _config = config;
        _api = api;
        _engine = engine;
        _notices = notices;
        _log = log;
        _xeniaRunning = xeniaRunning ?? XeniaLaunchProfile.XeniaRunning;
    }

    private string Dir => Path.Combine(_config.StateDir, "handover");

    /// <summary>Starts a pass unless one is still running (a long upload); the poller calls this every tick.</summary>
    public void Kick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _last = Task.Run(async () =>
        {
            try { await RunAsync(_stop.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AgentLogger.LogException("DlcHandoverRunner", ex); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }

    public async Task StopAsync()
    {
        _stop.Cancel();
        await _last;
    }

    /// <summary>One pass; public so the two-machine tests can drive it without the timer.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        if (_config.MachineId is not { } me || string.IsNullOrEmpty(_config.ApiKey)) return;
        var api = _api();
        var list = await api.ListTransfersAsync(ct);
        foreach (var t in list)
        {
            try
            {
                if (t.FromMachineId == me) await AsSenderAsync(api, t, ct);
                else if (t.ToMachineId == me) await AsReceiverAsync(api, t, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log($"[{t.GameName}] DLC hand-over: {ex.Message}");
            }
        }
        CleanUp(list);
        if (DateTime.UtcNow - _lastHoldCheck >= HoldCheckInterval)
        {
            _lastHoldCheck = DateTime.UtcNow;
            await RecheckHoldsAsync(ct);
        }
    }

    /// <summary>Lets the next <see cref="RunAsync"/> re-check holds at once (after an install, or from a test).</summary>
    public void RecheckHoldsSoon() => _lastHoldCheck = DateTime.MinValue;

    private TrackedGame? Game(Guid id) => _config.Games.FirstOrDefault(g => g.GameId == id && g.IsEnrolledHere);

    private async Task AsSenderAsync(ApiClient api, ContentTransferDto t, CancellationToken ct)
    {
        if (t.Status == TransferStatus.Requested)
        {
            switch (_config.DlcSend ?? DlcSendPolicy.Auto)
            {
                case DlcSendPolicy.Never:
                    await api.AnswerTransferAsync(t.Id, approve: false, ct);
                    _log($"[{t.GameName}] declined {t.ToMachineName}'s request for DLC (this machine never sends it).");
                    return;
                case DlcSendPolicy.Auto:
                    t = await api.AnswerTransferAsync(t.Id, approve: true, ct);
                    _log($"[{t.GameName}] sending {t.Parts.Length} DLC pack(s) to {t.ToMachineName}.");
                    break;
                default:
                    _notices?.Raise(NoticeCatalog.HandoverAsked(t));
                    return;
            }
        }
        if (t.Status != TransferStatus.Approved) return;
        if (Game(t.GameId) is not { } game || ContentInventory.Home(game) is not { } home)
        {
            _log($"[{t.GameName}] cannot send its DLC: the game is not set up on this machine.");
            return;
        }

        var dir = Path.Combine(Dir, "out", t.Id.ToString("N"));
        Directory.CreateDirectory(dir);
        var buffer = new byte[ChunkBytes];
        foreach (var part in t.Parts.Where(p => !p.Complete))
        {
            var pkg = new ContentPackageDto(part.Kind, part.PackageId, part.Name, part.Size);
            var zip = Path.Combine(dir, part.PackageId + ".zip");
            if (!File.Exists(zip)) DlcPackageFiles.Zip(home.Content, home.TitleId, pkg, zip);
            var total = new FileInfo(zip).Length;
            var offset = part.Received;
            await using (var fs = File.OpenRead(zip))
            {
                while (offset < total)
                {
                    var n = (int)Math.Min(ChunkBytes, total - offset);
                    fs.Seek(offset, SeekOrigin.Begin);
                    await fs.ReadExactlyAsync(buffer.AsMemory(0, n), ct);
                    var after = await api.UploadTransferPartAsync(t.Id, part.PackageId, offset, total, new MemoryStream(buffer, 0, n), ct);
                    offset = after.Parts.First(p => p.PackageId == part.PackageId).Received;
                }
            }
            File.Delete(zip);
        }
        _log($"[{t.GameName}] sent {t.Parts.Length} DLC pack(s) to {t.ToMachineName} through the server.");
    }

    private async Task AsReceiverAsync(ApiClient api, ContentTransferDto t, CancellationToken ct)
    {
        if (t.Status is TransferStatus.Declined or TransferStatus.Expired or TransferStatus.Cancelled)
        {
            if (MarkSeen(t.Id)) _notices?.Raise(NoticeCatalog.HandoverEnded(t));
            return;
        }
        if (t.Status != TransferStatus.Ready) return;
        if (Game(t.GameId) is not { } game || ContentInventory.Home(game) is not { } home) return;

        var dir = Path.Combine(Dir, "in", t.Id.ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var part in t.Parts)
        {
            var zip = Path.Combine(dir, part.PackageId + ".zip");
            var have = File.Exists(zip) ? new FileInfo(zip).Length : 0;
            if (have > part.ZipSize) { File.Delete(zip); have = 0; }
            if (have < part.ZipSize)
            {
                await using var fs = new FileStream(zip, FileMode.Append, FileAccess.Write);
                await api.DownloadTransferPartAsync(t.Id, part.PackageId, fs, have, ct);
            }
            if (new FileInfo(zip).Length != part.ZipSize)
                throw new IOException($"{part.Name} did not arrive whole; the next pass resumes it.");
        }

        // Never into a running game: the packs wait in the agent's folder until Xenia closes.
        if (_xeniaRunning())
        {
            _log($"[{t.GameName}] the DLC from {t.FromMachineName} is downloaded and installs once Xenia closes.");
            return;
        }
        try
        {
            foreach (var part in t.Parts)
                DlcPackageFiles.Install(Path.Combine(dir, part.PackageId + ".zip"), home.Content, home.TitleId, home.Legacy,
                    new ContentPackageDto(part.Kind, part.PackageId, part.Name, part.Size));
        }
        catch (InvalidDataException ex)
        {
            _log($"[{t.GameName}] the DLC from {t.FromMachineName} failed its check and was not installed: {ex.Message}");
            await api.CancelTransferAsync(t.Id, ct);
            TryDelete(dir);
            return;
        }
        await api.TransferDeliveredAsync(t.Id, ct);
        TryDelete(dir);
        _log($"[{t.GameName}] installed {t.Parts.Length} DLC pack(s) from {t.FromMachineName}.");
        _notices?.Raise(NoticeCatalog.HandoverReceived(t));
        if (ContentInventory.For(game) is { } now)
        {
            try { await api.ReportContentAsync(game.GameId, now, null, ct); }
            catch (HttpRequestException) { }
        }
        RecheckHoldsSoon();
    }

    /// <summary>
    /// Step 13: a held pull goes ahead by itself once every pack it waited for is installed — handed over, copied by hand,
    /// or downloaded in Xenia. Checked once a minute; the pull's own guards (Xenia running, unpushed changes) still apply.
    /// </summary>
    private async Task RecheckHoldsAsync(CancellationToken ct)
    {
        foreach (var hold in DlcHoldStore.For(_config).Read())
        {
            if (Game(hold.GameId) is not { } game || ContentInventory.For(game) is not { } here) continue;
            if (DlcCheck.Missing(hold.Missing, here).Count > 0) continue;
            var outcome = await _engine().PullAsync(game, ct: ct);
            _log($"[{game.Name}] the DLC its pull waited for is installed. {SyncEngine.DescribePull(game, outcome)}");
            if (outcome == PullOutcome.Restored) _notices?.Raise(NoticeCatalog.HeldPullDone(game.GameId, game.Name));
        }
    }

    private bool MarkSeen(Guid id)
    {
        var path = Path.Combine(Dir, "seen.json");
        try
        {
            var seen = File.Exists(path) ? JsonSerializer.Deserialize<List<Guid>>(File.ReadAllText(path)) ?? [] : [];
            if (seen.Contains(id)) return false;
            seen.Add(id);
            Directory.CreateDirectory(Dir);
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(seen.TakeLast(200)));
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }

    // Pieces of hand-overs that ended (or that this machine no longer takes part in) go.
    private void CleanUp(ContentTransferDto[] open)
    {
        var keep = open.Where(t => t.Status is TransferStatus.Approved or TransferStatus.Ready).Select(t => t.Id.ToString("N")).ToHashSet();
        foreach (var side in new[] { "out", "in" })
        {
            var dir = Path.Combine(Dir, side);
            if (!Directory.Exists(dir)) continue;
            foreach (var d in Directory.EnumerateDirectories(dir))
                if (!keep.Contains(Path.GetFileName(d))) TryDelete(d);
        }
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
