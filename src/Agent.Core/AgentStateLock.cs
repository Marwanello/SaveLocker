namespace SaveLocker.Agent;

/// <summary>
/// Raised when a cross-process lock could not be taken. The operation did <b>not</b> run, and
/// nothing was read or written — callers report this rather than proceeding.
/// </summary>
public sealed class AgentStateLockException(string lockName, string message) : Exception(message)
{
    public string LockName { get; } = lockName;
}

/// <summary>
/// A cross-process mutex, held for as long as the returned handle is alive.
///
/// The agent is not one process. Autorun keeps the daemon running while Steam starts a *second*
/// process — <c>savelocker run -- %command%</c> — and both of them sync the same games, write the
/// same <c>config.json</c>, and drain the same queue and health files. The in-process locks that
/// were here before (a <c>SemaphoreSlim</c>, a <c>lock</c> statement) do nothing across that
/// boundary.
///
/// Implemented with a lock file opened <see cref="FileShare.None"/>, which .NET maps to a real
/// advisory <c>flock</c> on Unix and to share-mode denial on Windows.
///
/// <para><b>Take the in-process lock as well, not instead.</b> A <c>flock</c> is owned by the
/// process, so two threads in the SAME process both "acquire" it and neither blocks. This type
/// therefore guards process-vs-process only; SyncEngine still holds its per-game semaphore for
/// thread-vs-thread.</para>
///
/// <para><b>It fails closed.</b> This used to time out after 30 seconds and hand back an unheld
/// handle so the caller proceeded anyway — which is the precise state the lock exists to prevent:
/// two processes archiving, restoring and rewriting sync state for the same game at once. The
/// justification given was that a lock file left by a crashed process must not block syncing
/// forever. That justification was wrong: the lock is a <i>handle</i>, not the file's existence,
/// and every OS releases handles when a process dies. A crashed agent leaves a stale lock *file*,
/// which the next caller opens without difficulty. There is no stale-lock scenario to fail open
/// for, so the timeout no longer trades correctness for a problem that cannot happen. WA-07.</para>
/// </summary>
public sealed class AgentStateLock : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _name;

    private AgentStateLock(FileStream stream, string name)
    {
        _stream = stream;
        _name = name;
    }

    /// <summary>
    /// Short state writes — config, queue, health, lease warnings. These are file writes measured in
    /// milliseconds, so waiting this long already means another process is wedged.
    /// </summary>
    public static readonly TimeSpan StateWriteTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Block until the named lock is ours. Throws <see cref="AgentStateLockException"/> if it cannot
    /// be taken within <paramref name="timeout"/>, or if <paramref name="ct"/> is cancelled.
    /// </summary>
    public static AgentStateLock Acquire(
        string name, string stateDir, TimeSpan? timeout = null, CancellationToken ct = default) =>
        TryAcquire(name, stateDir, timeout, ct)
        ?? throw new AgentStateLockException(name,
            $"Another SaveLocker process is still using '{name}' after " +
            $"{(timeout ?? StateWriteTimeout).TotalSeconds:0}s. Nothing was changed. " +
            "If no other agent is running, check the agent log for a stuck operation.");

    /// <summary>
    /// The same, but returns null instead of throwing — for a caller that has its own way to report
    /// contention. Never returns an unheld handle.
    /// </summary>
    public static AgentStateLock? TryAcquire(
        string name, string stateDir, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var limit = timeout ?? StateWriteTimeout;
        var dir = Path.Combine(stateDir, "locks");
        var path = Path.Combine(dir, $"{name}.lock");

        // A locks directory that cannot be created is not a reason to run unlocked — it means the
        // state directory is unusable, and every write that follows would be just as broken.
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex)
        {
            throw new AgentStateLockException(name,
                $"Cannot create the lock directory {dir} ({ex.Message}). Nothing was changed.");
        }

        var deadline = DateTime.UtcNow + limit;
        var delay = 15;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new AgentStateLock(
                    new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None),
                    name);
            }
            catch (IOException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    AgentLogger.Log(
                        $"lock '{name}': still held after {limit.TotalSeconds:0}s — refusing to proceed. " +
                        "Another agent process may be stuck.");
                    return null;
                }
                // Cancellable wait: a retired engine or a cancelled command must not sit here.
                if (ct.WaitHandle.WaitOne(delay)) ct.ThrowIfCancellationRequested();
                delay = Math.Min(delay * 2, 250);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Since WA-03 the state directory is ACL-locked to the enrolling account, so this
                // means someone else's agent owns these files — exactly when NOT to write.
                throw new AgentStateLockException(name,
                    $"No permission to take the '{name}' lock ({ex.Message}). Nothing was changed.");
            }
        }
    }

    /// <summary>
    /// The per-game sync lock. Push and pull for one game are mutually exclusive fleet-wide on this
    /// box. <paramref name="timeout"/> should be sized by the caller for the whole operation it is
    /// protecting — see <see cref="SyncEngine"/>, which adds the settle gate and the upload window.
    /// </summary>
    public static AgentStateLock ForGame(
        Guid gameId, string stateDir, TimeSpan? timeout = null, CancellationToken ct = default) =>
        Acquire($"game-{gameId:N}", stateDir, timeout, ct);

    public void Dispose() => _stream.Dispose();
}
