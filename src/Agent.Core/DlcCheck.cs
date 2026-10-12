using System.Text.Json;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>One package of the latest save, and whether this machine has it.</summary>
public sealed record DlcPackageDto(string Kind, string Id, string Name, long Size, bool Here);

/// <summary>
/// The game page's DLC check (tasks/emulator-saves Phase 5c): what the latest save was made with, what is missing here,
/// and whether a pull of it is on hold. Null <see cref="Packages"/> means the latest save did not say (an older agent
/// made it, or the game's emulator has no DLC this knows), and nothing is checked.
/// </summary>
public sealed record DlcCheckDto(
    string? HeadMachineName, DateTime? HeadAt, DlcPackageDto[]? Packages, int MissingCount, long MissingSize, bool Held);

/// <summary>
/// The DLC check before a pull (tasks/emulator-saves Phase 5c, step 9): the latest save lists the DLC and title updates it
/// was made with; when this machine lacks some, an ordinary pull is held instead of restoring a save the game may refuse
/// to load, and says which. “Pull anyway” and a forced pull skip it.
/// </summary>
public static class DlcCheck
{
    /// <summary>The packages of <paramref name="head"/> this machine does not have, matched by kind and package ID.</summary>
    public static IReadOnlyList<ContentPackageDto> Missing(IReadOnlyList<ContentPackageDto>? head, IReadOnlyList<ContentPackageDto>? here)
    {
        if (head is null || head.Count == 0) return [];
        var have = new HashSet<string>((here ?? []).Select(Key), StringComparer.OrdinalIgnoreCase);
        return [.. head.Where(p => !have.Contains(Key(p)))];
    }

    private static string Key(ContentPackageDto p) => p.Kind + "/" + p.Id;

    public static DlcCheckDto Describe(SaveVersionDto? head, IReadOnlyList<ContentPackageDto>? here, bool held)
    {
        if (head?.Content is not { } content) return new DlcCheckDto(head?.MachineName, head?.CreatedAt, null, 0, 0, held);
        var missing = Missing(content, here);
        var missingKeys = new HashSet<string>(missing.Select(Key), StringComparer.OrdinalIgnoreCase);
        return new DlcCheckDto(head.MachineName, head.CreatedAt,
            [.. content.Select(p => new DlcPackageDto(p.Kind, p.Id, p.Name, p.Size, !missingKeys.Contains(Key(p))))],
            missing.Count, missing.Sum(p => p.Size), held);
    }

    /// <summary>One line for a person: which packs, how big.</summary>
    public static string Summary(IReadOnlyList<ContentPackageDto> missing)
    {
        var names = missing.Select(p => string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name).ToList();
        var shown = names.Count <= 3 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + $" and {names.Count - 3} more";
        var mb = Math.Max(1, (missing.Sum(p => p.Size) + 512 * 1024) / (1024 * 1024));
        var what = missing.All(p => p.Kind == ContentPackageDto.Update) ? "title update" : "DLC pack";
        return $"{names.Count} {what}{(names.Count == 1 ? "" : "s")} ({shown}, {mb} MB)";
    }
}

/// <summary>
/// The pulls held by the DLC check, on disk beside the config: the daemon, the tray and the Linux launch wrapper all
/// pull, and each must see the others' holds. A hold names the version it was for, so a newer save is checked afresh.
/// </summary>
public sealed class DlcHoldStore
{
    public sealed class Entry
    {
        public Guid GameId { get; set; }
        public Guid VersionId { get; set; }
        public List<ContentPackageDto> Missing { get; set; } = new();
        public DateTimeOffset HeldAt { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;
    private readonly string _stateDir;

    public static DlcHoldStore For(AgentConfig config) => new(Path.Combine(config.StateDir, "dlc-holds.json"));

    public DlcHoldStore(string path)
    {
        _path = path;
        _stateDir = Path.GetDirectoryName(Path.GetFullPath(path))!;
    }

    public IReadOnlyList<Entry> Read() => ReadDisk();

    public Entry? For(Guid gameId) => ReadDisk().FirstOrDefault(e => e.GameId == gameId);

    public void Hold(Guid gameId, Guid versionId, IReadOnlyList<ContentPackageDto> missing) => Mutate(list =>
    {
        list.RemoveAll(e => e.GameId == gameId);
        list.Add(new Entry { GameId = gameId, VersionId = versionId, Missing = [.. missing], HeldAt = DateTimeOffset.UtcNow });
    });

    public void Clear(Guid gameId) => Mutate(list => list.RemoveAll(e => e.GameId == gameId));

    private void Mutate(Action<List<Entry>> change)
    {
        try
        {
            using var guard = AgentStateLock.Acquire("dlc-holds", _stateDir);
            var list = ReadDisk();
            change(list);
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(list, JsonOpts), restrictPermissions: true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AgentLogger.Log($"Could not record a DLC hold: {ex.Message}");
        }
    }

    private List<Entry> ReadDisk()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path), JsonOpts) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }
}
