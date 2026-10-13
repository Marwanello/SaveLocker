using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SaveLocker.Server.Data;
using SaveLocker.Shared;

namespace SaveLocker.Server.Services;

/// <summary>A hand-over refused for a reason the caller can show.</summary>
public sealed class TransferRefusedException(string message) : Exception(message);

/// <summary>
/// The DLC hand-over (tasks/emulator-saves Phase 5c, step 11): a machine missing packs a save was made with asks the
/// machine that made it; that machine uploads each pack as a zip; only the asking machine may download them; they are
/// deleted as soon as it confirms, or when the request expires. Packs pass through the server and are never kept:
/// Requested → Approved → Ready → Delivered (files deleted), or Declined / Cancelled / Expired (files deleted).
/// </summary>
public sealed partial class ContentTransferService
{
    public const string DirName = "_handover";
    public const int DefaultMaxMb = 8192;
    public const int DefaultExpiryDays = 7;
    public const long MaxChunkBytes = 16L * 1024 * 1024;

    private static readonly TransferStatus[] Open = [TransferStatus.Requested, TransferStatus.Approved, TransferStatus.Ready];
    private static readonly TransferStatus[] Ended = [TransferStatus.Declined, TransferStatus.Expired, TransferStatus.Cancelled];

    private readonly AppDbContext _db;
    private readonly string _root;
    private readonly long _maxBytes;
    private readonly int _expiryDays;

    public ContentTransferService(AppDbContext db, ArchiveStore store, IConfiguration config)
    {
        _db = db;
        _root = Path.Combine(store.Root, DirName);
        _maxBytes = MaxBytes(config);
        _expiryDays = Math.Max(1, config.GetValue<int?>("Storage:HandoverExpiryDays") ?? DefaultExpiryDays);
    }

    /// <summary>The largest hand-over, <c>Storage:MaxHandoverMb</c> (8 GB by default), separate from the save cap.</summary>
    public static long MaxBytes(IConfiguration config)
    {
        var mb = config.GetValue<int?>("Storage:MaxHandoverMb") ?? DefaultMaxMb;
        if (mb <= 0) throw new InvalidOperationException($"Storage:MaxHandoverMb must be positive (got {mb}).");
        return (long)mb * 1024 * 1024;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex SafeId();

    private sealed class Part
    {
        public string Kind { get; set; } = "";
        public string PackageId { get; set; } = "";
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public long? ZipSize { get; set; }
        public long Received { get; set; }
        public bool Complete { get; set; }
    }

    private static List<Part> PartsOf(ContentTransfer t) =>
        JsonSerializer.Deserialize<List<Part>>(t.PartsJson, JsonSerializerOptions.Web) ?? [];

    private static void Save(ContentTransfer t, List<Part> parts) =>
        t.PartsJson = JsonSerializer.Serialize(parts, JsonSerializerOptions.Web);

    private string Dir(Guid id) => Path.Combine(_root, id.ToString("N"));

    private void DeleteFiles(Guid id)
    {
        try { if (Directory.Exists(Dir(id))) Directory.Delete(Dir(id), recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the sweep tries again */ }
    }

    private void Audit(Guid? machineId, Guid gameId, string action, string detail) =>
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, MachineId = machineId, GameId = gameId, Action = action, Detail = detail,
        });

    /// <summary>The asking machine's request. The other machine must have reported every pack, and the packs must fit the
    /// cap. An open request for the same game and machine is answered with that one, never a second.</summary>
    public async Task<ContentTransferDto> CreateAsync(Guid toMachineId, CreateTransferRequest req)
    {
        if (req.Packages is not { Length: > 0 }) throw new TransferRefusedException("Name at least one package.");
        if (ContentPackageDto.Validate(req.Packages) is { } why) throw new TransferRefusedException(why);
        if (req.Packages.Any(p => !SafeId().IsMatch(p.Id))) throw new TransferRefusedException("A package ID is letters, digits, - and _ only.");
        if (req.FromMachineId == toMachineId) throw new TransferRefusedException("A machine cannot hand packs to itself.");
        var game = await _db.Games.FindAsync(req.GameId) ?? throw new TransferRefusedException("No such game.");
        if (!await _db.Machines.AnyAsync(m => m.Id == req.FromMachineId)) throw new TransferRefusedException("No such machine.");
        var total = req.Packages.Sum(p => p.Size);
        if (total > _maxBytes)
            throw new TransferRefusedException($"{total / (1024 * 1024)} MB is more than the hand-over limit of {_maxBytes / (1024 * 1024)} MB.");

        var reported = await _db.MachineGameContents.FindAsync(req.FromMachineId, req.GameId);
        var has = new HashSet<string>((Mapping.ReadContent(reported?.ContentJson) ?? []).Select(p => $"{p.Kind}/{p.Id}"), StringComparer.OrdinalIgnoreCase);
        if (req.Packages.FirstOrDefault(p => !has.Contains($"{p.Kind}/{p.Id}")) is { } absent)
            throw new TransferRefusedException($"That machine has not reported '{absent.Name}'.");

        var existing = await _db.ContentTransfers
            .Where(t => t.GameId == req.GameId && t.ToMachineId == toMachineId && Open.Contains(t.Status)).FirstOrDefaultAsync();
        if (existing is not null) return await ToDtoAsync(existing);

        var now = DateTime.UtcNow;
        var transfer = new ContentTransfer
        {
            Id = Guid.NewGuid(), GameId = game.Id, FromMachineId = req.FromMachineId, ToMachineId = toMachineId,
            Status = TransferStatus.Requested, CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddDays(_expiryDays),
        };
        Save(transfer, [.. req.Packages.Select(p => new Part { Kind = p.Kind, PackageId = p.Id, Name = p.Name, Size = p.Size })]);
        _db.ContentTransfers.Add(transfer);
        Audit(toMachineId, game.Id, "dlc.handover.request", $"{req.Packages.Length} pack(s), {total / (1024 * 1024)} MB");
        await _db.SaveChangesAsync();
        return await ToDtoAsync(transfer);
    }

    /// <summary>
    /// The open hand-overs this machine sends or receives, and for the machine that asked, the ones that ended without
    /// arriving in the last day, so it can say the request was declined or expired.
    /// </summary>
    public async Task<ContentTransferDto[]> ForMachineAsync(Guid machineId)
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var list = await _db.ContentTransfers
            .Where(t => (Open.Contains(t.Status) && (t.FromMachineId == machineId || t.ToMachineId == machineId))
                     || (Ended.Contains(t.Status) && t.ToMachineId == machineId && t.UpdatedAt > since))
            .OrderBy(t => t.CreatedAt).ToListAsync();
        var result = new List<ContentTransferDto>();
        foreach (var t in list) result.Add(await ToDtoAsync(t));
        return [.. result];
    }

    /// <summary>Every hand-over of a game, newest first, for the console.</summary>
    public async Task<ContentTransferDto[]> ForGameAsync(Guid gameId)
    {
        var list = await _db.ContentTransfers.Where(t => t.GameId == gameId).OrderByDescending(t => t.CreatedAt).Take(20).ToListAsync();
        var result = new List<ContentTransferDto>();
        foreach (var t in list) result.Add(await ToDtoAsync(t));
        return [.. result];
    }

    private async Task<ContentTransfer> MineAsync(Guid id, Guid machineId, bool sender)
    {
        var t = await _db.ContentTransfers.FindAsync(id);
        if (t is null || (sender ? t.FromMachineId : t.ToMachineId) != machineId) throw new KeyNotFoundException();
        return t;
    }

    public async Task<ContentTransferDto> AnswerAsync(Guid machineId, Guid id, bool approve)
    {
        var t = await MineAsync(id, machineId, sender: true);
        if (t.Status != TransferStatus.Requested) throw new TransferRefusedException($"The request is {t.Status.ToString().ToLowerInvariant()}.");
        t.Status = approve ? TransferStatus.Approved : TransferStatus.Declined;
        t.UpdatedAt = DateTime.UtcNow;
        Audit(machineId, t.GameId, approve ? "dlc.handover.approve" : "dlc.handover.decline", "");
        await _db.SaveChangesAsync();
        return await ToDtoAsync(t);
    }

    /// <summary>
    /// One piece of a pack's zip from the sending machine, at <paramref name="offset"/> of <paramref name="total"/> bytes
    /// (declared with the first piece). Pieces arrive in order, so a sender that was interrupted asks where to resume
    /// (<see cref="TransferPartDto.Received"/>) and carries on from there.
    /// </summary>
    public async Task<ContentTransferDto> AppendAsync(Guid machineId, Guid id, string packageId, long offset, long total, Stream body, CancellationToken ct)
    {
        var t = await MineAsync(id, machineId, sender: true);
        if (t.Status != TransferStatus.Approved) throw new TransferRefusedException($"The request is {t.Status.ToString().ToLowerInvariant()}.");
        var parts = PartsOf(t);
        var part = parts.FirstOrDefault(p => p.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException();
        if (part.Complete) return await ToDtoAsync(t);
        if (total <= 0) throw new TransferRefusedException("total must be positive.");
        part.ZipSize ??= total;
        if (part.ZipSize != total) throw new TransferRefusedException("total changed between pieces.");
        if (parts.Sum(p => p.ZipSize ?? p.Size) > _maxBytes + _maxBytes / 10)
            throw new TransferRefusedException("The packs grew past the hand-over limit.");
        if (offset != part.Received) throw new TransferRefusedException($"Expected offset {part.Received}.");

        Directory.CreateDirectory(Dir(id));
        var path = Path.Combine(Dir(id), part.PackageId + ".zip");
        long written;
        await using (var file = new FileStream(path, offset == 0 ? FileMode.Create : FileMode.Open, FileAccess.Write, FileShare.None))
        {
            file.Seek(offset, SeekOrigin.Begin);
            var before = file.Position;
            var buffer = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(buffer, ct)) > 0)
            {
                if (file.Position + read > total) throw new TransferRefusedException("More bytes than declared.");
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            written = file.Position - before;
        }
        part.Received += written;
        part.Complete = part.Received == total;
        if (parts.All(p => p.Complete))
        {
            t.Status = TransferStatus.Ready;
            Audit(machineId, t.GameId, "dlc.handover.ready", $"{parts.Count} pack(s) up");
        }
        Save(t, parts);
        t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return await ToDtoAsync(t);
    }

    /// <summary>A finished pack's zip, for the asking machine only.</summary>
    public async Task<string> PartPathAsync(Guid machineId, Guid id, string packageId)
    {
        var t = await MineAsync(id, machineId, sender: false);
        if (t.Status is not (TransferStatus.Approved or TransferStatus.Ready)) throw new KeyNotFoundException();
        var part = PartsOf(t).FirstOrDefault(p => p.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        if (part is not { Complete: true }) throw new KeyNotFoundException();
        var path = Path.Combine(Dir(id), part.PackageId + ".zip");
        return File.Exists(path) ? path : throw new KeyNotFoundException();
    }

    /// <summary>The asking machine has every pack: the request is done and its files deleted at once.</summary>
    public async Task<ContentTransferDto> DeliveredAsync(Guid machineId, Guid id)
    {
        var t = await MineAsync(id, machineId, sender: false);
        if (t.Status != TransferStatus.Ready) throw new TransferRefusedException($"The request is {t.Status.ToString().ToLowerInvariant()}.");
        t.Status = TransferStatus.Delivered;
        t.UpdatedAt = DateTime.UtcNow;
        Audit(machineId, t.GameId, "dlc.handover.delivered", "files deleted");
        await _db.SaveChangesAsync();
        DeleteFiles(id);
        return await ToDtoAsync(t);
    }

    /// <summary>Either machine, or the console (<paramref name="machineId"/> null), ends an open request.</summary>
    public async Task<bool> CancelAsync(Guid? machineId, Guid id)
    {
        var t = await _db.ContentTransfers.FindAsync(id);
        if (t is null || (machineId is { } m && t.FromMachineId != m && t.ToMachineId != m)) return false;
        if (!Open.Contains(t.Status)) return true;
        t.Status = TransferStatus.Cancelled;
        t.UpdatedAt = DateTime.UtcNow;
        Audit(machineId, t.GameId, "dlc.handover.cancel", machineId is null ? "from the console" : "");
        await _db.SaveChangesAsync();
        DeleteFiles(id);
        return true;
    }

    /// <summary>Open requests past their expiry end, and their files go. Run by the hourly sweeper.</summary>
    public async Task<int> SweepAsync()
    {
        var now = DateTime.UtcNow;
        var expired = await _db.ContentTransfers.Where(t => Open.Contains(t.Status) && t.ExpiresAt < now).ToListAsync();
        foreach (var t in expired)
        {
            t.Status = TransferStatus.Expired;
            t.UpdatedAt = now;
            Audit(null, t.GameId, "dlc.handover.expire", "files deleted");
        }
        await _db.SaveChangesAsync();
        foreach (var t in expired) DeleteFiles(t.Id);
        // Files of a request that already ended (a delete that failed earlier) go too.
        if (Directory.Exists(_root))
        {
            var open = (await _db.ContentTransfers.Where(t => Open.Contains(t.Status)).Select(t => t.Id).ToListAsync())
                .Select(i => i.ToString("N")).ToHashSet();
            foreach (var dir in Directory.EnumerateDirectories(_root))
                if (!open.Contains(Path.GetFileName(dir))) DeleteFiles(Guid.TryParse(Path.GetFileName(dir), out var g) ? g : Guid.Empty);
        }
        return expired.Count;
    }

    /// <summary>The console's Configuration card: the limits, what the server holds now, and every open hand-over.</summary>
    public async Task<HandoverOverviewDto> OverviewAsync()
    {
        var list = await _db.ContentTransfers.Where(t => Open.Contains(t.Status)).OrderBy(t => t.CreatedAt).ToListAsync();
        var result = new List<ContentTransferDto>();
        foreach (var t in list) result.Add(await ToDtoAsync(t));
        return new HandoverOverviewDto(_maxBytes, _expiryDays, BytesInTransit(), [.. result]);
    }

    /// <summary>Bytes held for hand-overs right now, for the console.</summary>
    public long BytesInTransit() =>
        Directory.Exists(_root) ? new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    private async Task<ContentTransferDto> ToDtoAsync(ContentTransfer t)
    {
        var names = await _db.Machines.Where(m => m.Id == t.FromMachineId || m.Id == t.ToMachineId).ToDictionaryAsync(m => m.Id, m => m.Name);
        var game = await _db.Games.Where(g => g.Id == t.GameId).Select(g => g.Name).FirstOrDefaultAsync() ?? "";
        return new ContentTransferDto(t.Id, t.GameId, game, t.FromMachineId, names.GetValueOrDefault(t.FromMachineId, ""),
            t.ToMachineId, names.GetValueOrDefault(t.ToMachineId, ""), t.Status, t.CreatedAt, t.ExpiresAt,
            [.. PartsOf(t).Select(p => new TransferPartDto(p.Kind, p.PackageId, p.Name, p.Size, p.ZipSize, p.Received, p.Complete))]);
    }
}
