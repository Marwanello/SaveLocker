using System.Text.Json;

namespace SaveLocker.Agent;

/// <summary>
/// Durable record of "you launched a game someone else has checked out".
///
/// This used to live only in <see cref="AgentApiServer"/>'s memory, which had two consequences.
/// First, a warning died with the process — a daemon restart silently dropped a conflict notice the
/// user had never seen. Second, and worse on a Deck, the Linux launch wrapper is a *separate,
/// short-lived process* from the daemon, so a warning it raised could never reach any UI at all:
/// <c>ProtonRun</c> discarded the result of <c>OnGameLaunchAsync</c> and the only trace was a line
/// in a console log that Game Mode never shows.
///
/// Persisting to disk beside <c>config.json</c> fixes both, and keeps the Game Mode UI a *view*
/// over shared state rather than a second API client (Decisions.md §2).
///
/// Every operation reads, mutates and writes under <see cref="AgentStateLock"/>. There is no
/// in-memory cache to merge: writes happen at most twice per game launch, so the simple form is
/// both correct and cheap. Contrast <see cref="OfflineQueue"/>, which is written often enough to
/// need a session-level merge.
/// </summary>
public sealed class LeaseWarningStore
{
    public sealed class Entry
    {
        public string GameName { get; set; } = "";
        public string HolderMachine { get; set; } = "";
        public DateTimeOffset RaisedAt { get; set; }
    }

    /// <summary>
    /// A warning is only meaningful until roughly the next play session. Without an expiry, a
    /// warning raised once and never dismissed — because the user quit Game Mode instead — would
    /// nag forever, and users learn to ignore a banner that is always there.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _path;
    private readonly string _stateDir;

    /// <summary>
    /// The store that belongs to this config. State lives beside the config file it belongs to, not
    /// in the machine default: with <c>--config</c> the two diverge, and every process that
    /// disagreed about the path would keep its own private store while believing it shared one.
    /// </summary>
    public static LeaseWarningStore For(AgentConfig config) =>
        new(Path.Combine(config.StateDir, "lease-warnings.json"));

    public LeaseWarningStore(string? path = null)
    {
        _path = path ?? Path.Combine(AgentConfig.DefaultDir, "lease-warnings.json");
        _stateDir = Path.GetDirectoryName(Path.GetFullPath(_path))!;
    }

    /// <summary>Record that <paramref name="gameName"/> is checked out by someone else.</summary>
    public void Add(string gameName, string holderMachine)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return;
        Mutate(list =>
        {
            list.RemoveAll(e => Same(e.GameName, gameName));
            list.Add(new Entry
            {
                GameName = gameName,
                HolderMachine = holderMachine,
                RaisedAt = DateTimeOffset.UtcNow,
            });
        });
    }

    /// <summary>
    /// Drop the warning for a game. Called on game exit and on dismissal from any UI — dismissing
    /// on the Deck must write through, or the same warning reappears in the browser UI afterwards.
    /// </summary>
    public void Clear(string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName)) return;
        Mutate(list => list.RemoveAll(e => Same(e.GameName, gameName)));
    }

    /// <summary>Current warnings, newest first, with stale ones filtered out.</summary>
    public IReadOnlyList<Entry> Read()
    {
        var cutoff = DateTimeOffset.UtcNow - MaxAge;
        return ReadDisk()
            .Where(e => e.RaisedAt > cutoff)
            .OrderByDescending(e => e.RaisedAt)
            .ToList();
    }

    private void Mutate(Action<List<Entry>> change)
    {
        try
        {
            using var guard = AgentStateLock.Acquire("lease-warnings", _stateDir);

            var list = ReadDisk();
            // Prune on write as well as read, so the file cannot grow without bound on a machine
            // whose UI is never opened.
            var cutoff = DateTimeOffset.UtcNow - MaxAge;
            list.RemoveAll(e => e.RaisedAt <= cutoff);

            change(list);

            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(list, JsonOpts),
                restrictPermissions: true);
        }
        catch { /* best-effort: a missed warning must never break a game launch */ }
    }

    private List<Entry> ReadDisk()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path), JsonOpts) ?? [];
        }
        catch { return []; }
    }

    private static bool Same(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
