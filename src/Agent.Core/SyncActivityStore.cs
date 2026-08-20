using System.Text.Json;

namespace SaveLocker.Agent;

/// <summary>
/// Durable handoff of <see cref="SyncActivityTracker"/>'s state to a reader in another process.
///
/// The browser agent UI reads the tracker in-process, through the same daemon that owns it. The
/// Game Mode UI (<c>savelocker ui</c>) cannot: it is its own OS process, launched separately from
/// the daemon that actually does the syncing, so — exactly like <see cref="LeaseWarningStore"/> —
/// disk is the only channel between them.
/// <para>
/// One writer (the daemon, via <see cref="SyncActivityTracker"/>'s persist callback), any number of
/// readers. Every write replaces the whole file, so there is nothing to merge and no cross-process
/// lock to take: <see cref="AtomicFile"/> already guarantees a reader never observes a half-written
/// file, only the previous complete one or the new one.
/// </para>
/// </summary>
public sealed class SyncActivityStore
{
    private sealed class Payload
    {
        public SyncActivitySnapshot? Current { get; set; }
        public List<ActivityLogEntry> Recent { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;

    /// <summary>The store that belongs to this config — state lives beside the config file it
    /// belongs to, the same reasoning as <see cref="LeaseWarningStore.For"/>.</summary>
    public static SyncActivityStore For(AgentConfig config) =>
        new(Path.Combine(config.StateDir, "sync-activity.json"));

    public SyncActivityStore(string path) => _path = path;

    public void Write(SyncActivitySnapshot current, IReadOnlyList<ActivityLogEntry> recent)
    {
        try
        {
            var payload = new Payload { Current = current, Recent = recent.ToList() };
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(payload, JsonOpts),
                restrictPermissions: true);
        }
        catch { /* best-effort: a missed status write must never break a sync */ }
    }

    public (SyncActivitySnapshot? Current, IReadOnlyList<ActivityLogEntry> Recent) Read()
    {
        try
        {
            if (!File.Exists(_path)) return (null, Array.Empty<ActivityLogEntry>());
            var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(_path), JsonOpts);
            return (payload?.Current, payload?.Recent ?? new List<ActivityLogEntry>());
        }
        catch { return (null, Array.Empty<ActivityLogEntry>()); }
    }
}
