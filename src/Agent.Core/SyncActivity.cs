namespace SaveLocker.Agent;

/// <summary>What a sync attempt is doing right now, for a UI to show rather than a log file.</summary>
public enum SyncPhase
{
    Idle,
    Pulling,
    /// <summary>Waiting for the game to finish writing before a push archives the folder.</summary>
    Settling,
    Pushing
}

/// <summary>A moment-in-time read of the current sync attempt, or all-default fields when nothing
/// is running.</summary>
public sealed record SyncActivitySnapshot(
    string? GameName, SyncPhase Phase, long BytesDone, long BytesTotal, DateTime? StartedAtUtc);

public sealed record ActivityLogEntry(DateTime TimestampUtc, string Message);

/// <summary>
/// What the agent UI's Overview page shows at the bottom: what is syncing right now (with byte
/// progress for a push, since that is the direction slow enough to want one — see
/// <see cref="ApiClient.UploadAsync"/>'s chunk loop, which is what actually reports it) and a short
/// rolling history of what just happened.
/// <para>
/// One instance is owned by the host (the tray, the daemon) and outlives any single
/// <see cref="SyncEngine"/> — settings changes rebuild the engine, but the activity feed a user is
/// watching should not reset just because the server URL changed underneath it.
/// </para>
/// <para>
/// Thread-safe: <see cref="SyncEngine"/> writes from watcher, poller and drainer threads, and the
/// agent API server reads from a Kestrel request thread, all concurrently.
/// </para>
/// </summary>
public sealed class SyncActivityTracker
{
    private const int MaxRecent = 50;

    // A push reports progress once per 4 MiB chunk (ApiClient.UploadAsync), which on a fast local
    // link can be many times a second — plenty to make a progress bar look smooth, far more than a
    // disk write needs to keep up with. Phase changes and log lines always persist immediately: they
    // are rare, human-relevant events, not a stream.
    private static readonly TimeSpan ProgressPersistThrottle = TimeSpan.FromMilliseconds(300);

    private readonly object _lock = new();
    // The Game Mode UI (`savelocker ui`) runs as its own OS process, separate from the daemon that
    // actually does the syncing, so — exactly like LeaseWarningStore — it can only see this feed
    // through disk. Windows leaves this null: the tray hosts its UI in-process and reads the
    // tracker directly, so persisting would only cost I/O nothing ever reads.
    private readonly Action<SyncActivitySnapshot, IReadOnlyList<ActivityLogEntry>>? _persist;
    private string? _gameName;
    private SyncPhase _phase = SyncPhase.Idle;
    private long _bytesDone;
    private long _bytesTotal;
    private DateTime? _startedAtUtc;
    private readonly LinkedList<ActivityLogEntry> _recent = new();
    private DateTime _lastPersistUtc = DateTime.MinValue;

    // Orders the disk writes themselves, which happen outside _lock — see Persist(). A snapshot is
    // stamped with _persistSeq while the state it describes is still held, and _persistedSeq is the
    // newest stamp that actually reached disk, so a slow writer can never undo a fast one.
    private readonly object _persistLock = new();
    private long _persistSeq;
    private long _persistedSeq;

    // Fires once after a throttled write was skipped, so the LAST event in a burst still lands.
    // Without it, throttling a log line would mean an entry could sit unwritten indefinitely if
    // nothing else ever happened; with it, the feed is at worst ProgressPersistThrottle stale.
    private readonly System.Threading.Timer? _flushTimer;

    public SyncActivityTracker(Action<SyncActivitySnapshot, IReadOnlyList<ActivityLogEntry>>? persist = null)
    {
        _persist = persist;
        if (persist is not null)
            _flushTimer = new System.Threading.Timer(_ => PersistNow(), null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Start (or restart) tracking one game's sync attempt.</summary>
    public void Begin(string gameName, SyncPhase phase)
    {
        lock (_lock)
        {
            _gameName = gameName;
            _phase = phase;
            _bytesDone = 0;
            _bytesTotal = 0;
            _startedAtUtc = DateTime.UtcNow;
        }
        PersistNow();
    }

    /// <summary>Move to a later phase of the same attempt (e.g. Settling -> Pushing) without
    /// resetting the started-at time or the game name.</summary>
    public void SetPhase(SyncPhase phase)
    {
        lock (_lock) { _phase = phase; }
        PersistNow();
    }

    public void Progress(long bytesDone, long bytesTotal)
    {
        bool complete;
        lock (_lock)
        {
            _bytesDone = bytesDone;
            _bytesTotal = bytesTotal;
            complete = bytesTotal > 0 && bytesDone >= bytesTotal;
        }
        // The last chunk always lands, throttling only the ones in between — otherwise a bar could
        // visibly stall short of 100% until the next unrelated event flushed it.
        if (complete) PersistNow(); else PersistThrottled();
    }

    /// <summary>Back to idle. Safe to call even when nothing was ever begun.</summary>
    public void End()
    {
        lock (_lock)
        {
            _gameName = null;
            _phase = SyncPhase.Idle;
            _bytesDone = 0;
            _bytesTotal = 0;
            _startedAtUtc = null;
        }
        PersistNow();
    }

    /// <summary>
    /// Append one line to the rolling history. Takes the message only — callers already have their
    /// own <c>Action&lt;string&gt; log</c> for the file log (<see cref="AgentLogger"/> or a CLI's
    /// console write), and this is deliberately not that: duplicating file-logging here would mean
    /// two places deciding what "logged" means. A host composes both into one delegate instead.
    /// </summary>
    public void Record(string message)
    {
        lock (_lock)
        {
            _recent.AddLast(new ActivityLogEntry(DateTime.UtcNow, message));
            while (_recent.Count > MaxRecent) _recent.RemoveFirst();
        }
        // Throttled like progress, not written immediately. On Linux this delegate carries EVERY
        // engine log line (Daemon.Log), and each persist serialises the whole 50-entry feed and
        // does a durable temp-write-and-rename — a sync of several games turned dozens of log lines
        // into dozens of fsyncs against a Deck's eMMC or SD card, for a feed only the Game Mode UI
        // reads. The trailing flush in PersistThrottled guarantees the last line still lands, and
        // Begin/SetPhase/End persist immediately regardless, so nothing waits on a quiet period.
        PersistThrottled();
    }

    public SyncActivitySnapshot Current()
    {
        lock (_lock) return new SyncActivitySnapshot(_gameName, _phase, _bytesDone, _bytesTotal, _startedAtUtc);
    }

    /// <summary>Newest first — what a UI wants to render top-to-bottom without reversing it itself.</summary>
    public IReadOnlyList<ActivityLogEntry> Recent()
    {
        lock (_lock) return _recent.Reverse().ToList();
    }

    private void PersistNow()
    {
        if (_persist is null) return;
        lock (_lock) _lastPersistUtc = DateTime.UtcNow;
        Persist();
    }

    private void PersistThrottled()
    {
        if (_persist is null) return;

        TimeSpan wait;
        lock (_lock)
        {
            var since = DateTime.UtcNow - _lastPersistUtc;
            wait = since < ProgressPersistThrottle ? ProgressPersistThrottle - since : TimeSpan.Zero;
            if (wait == TimeSpan.Zero) _lastPersistUtc = DateTime.UtcNow;
        }

        if (wait == TimeSpan.Zero) Persist();
        // Skipped, so make sure it still lands: re-arm the trailing flush rather than dropping the
        // event on the floor. Persist()'s sequence check makes a redundant firing harmless.
        else _flushTimer?.Change(wait, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Snapshot under the lock, write outside it, and never let an older snapshot overwrite a newer
    /// one.
    /// <para>
    /// Both halves were wrong before. Reading <see cref="Current"/> after the mutation lock had
    /// already been released meant the snapshot could describe a state nobody ever passed through;
    /// and two persists could then land in the opposite order to the events that produced them. Two
    /// games push concurrently (the push gate in SyncEngine is per-game), so game A's End() racing
    /// game B's Progress() could leave sync-activity.json — the ONLY thing the Game Mode UI reads,
    /// in another process entirely — showing a push that had already finished, with a progress bar
    /// frozen there until some unrelated event happened to flush it.
    /// </para>
    /// The write itself stays outside the lock: it is a disk write (SyncActivityStore), and a sync
    /// operation running on a network thread must never block on it — same reasoning as
    /// HealthReporter's own writes.
    /// </summary>
    private void Persist()
    {
        SyncActivitySnapshot snapshot;
        IReadOnlyList<ActivityLogEntry> recent;
        long seq;
        lock (_lock)
        {
            snapshot = Current();
            recent = Recent();
            seq = ++_persistSeq;
        }

        lock (_persistLock)
        {
            if (seq <= _persistedSeq) return; // a newer snapshot already reached disk
            _persistedSeq = seq;
            try { _persist!(snapshot, recent); }
            catch { /* best-effort: a missed status write must never break a sync */ }
        }
    }
}
