namespace SaveLocker.Server.Services;

/// <summary>
/// Slows join-code guessing. A join code is 8 Crockford characters (~40 bits) instead of the old
/// 256-bit file token, so the one thing that keeps an online guess hopeless is refusing to answer
/// fast or forever: every public route that looks a code up — <c>/join/{code}</c> and its policy,
/// script and setup children — and the redeem itself (<c>POST /api/enroll</c>) records a failure on
/// an unknown code and refuses the client past the limit.
/// <para>
/// The shape deliberately mirrors <see cref="AuthThrottle"/>: in memory (a restart forgives, the
/// right failure mode for a lockout that must never strand the owner), per client plus a global
/// backstop for the address-rotating flood, and a reasonably short lockout. Differences that are on
/// purpose: only <i>unknown</i> codes count — a used or expired code proves the guesser already had
/// it — and a successful redeem clears the client's record, so one admin enrolling several machines
/// in a row is never limited.
/// </para>
/// </summary>
public sealed class JoinThrottle
{
    private const int MaxTrackedClients = 5000;

    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly TimeSpan _lockout;
    private readonly int _globalMaxFailures;
    private readonly TimeSpan _globalLockout;

    private readonly object _gate = new();
    private readonly Dictionary<string, Client> _clients = new();
    private readonly Queue<DateTime> _globalFailures = new();
    private DateTime _globalLockedUntil = DateTime.MinValue;

    private sealed class Client
    {
        public readonly Queue<DateTime> Failures = new();
        public DateTime LockedUntil = DateTime.MinValue;
        public DateTime LastActivity;
    }

    public JoinThrottle(IConfiguration cfg)
    {
        _maxFailures = Math.Max(1, cfg.GetValue<int?>("Security:JoinMaxFailures") ?? 15);
        _window = TimeSpan.FromSeconds(Math.Max(1, cfg.GetValue<int?>("Security:JoinFailureWindowSeconds") ?? 900));
        _lockout = TimeSpan.FromSeconds(Math.Max(1, cfg.GetValue<int?>("Security:JoinLockoutSeconds") ?? 900));
        _globalMaxFailures = Math.Max(1, cfg.GetValue<int?>("Security:JoinGlobalMaxFailures") ?? 100);
        _globalLockout = TimeSpan.FromSeconds(Math.Max(1, cfg.GetValue<int?>("Security:JoinGlobalLockoutSeconds") ?? 300));
    }

    /// <summary>Null when this client may try a code now, else how long until it may.</summary>
    public TimeSpan? RetryAfter(string client)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            var wait = _globalLockedUntil > now ? _globalLockedUntil - now : (TimeSpan?)null;
            if (_clients.TryGetValue(client, out var c) && c.LockedUntil > now)
            {
                var own = c.LockedUntil - now;
                if (wait is null || own > wait) wait = own;
            }
            return wait;
        }
    }

    /// <summary>
    /// Records a failed code lookup (an unknown code only — see the class comment). Returns true
    /// when this failure is the one that started a lockout, for logging.
    /// </summary>
    public bool RecordFailure(string client)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            var c = GetOrAdd(client, now);
            Trim(c.Failures, now - _window);
            c.Failures.Enqueue(now);
            c.LastActivity = now;

            var startedLockout = false;
            if (c.Failures.Count >= _maxFailures)
            {
                c.LockedUntil = now + _lockout;
                c.Failures.Clear();
                startedLockout = true;
            }

            Trim(_globalFailures, now - _window);
            _globalFailures.Enqueue(now);
            if (_globalFailures.Count >= _globalMaxFailures)
            {
                _globalLockedUntil = now + _globalLockout;
                _globalFailures.Clear();
                startedLockout = true;
            }
            return startedLockout;
        }
    }

    /// <summary>A code that redeemed: forgive this client's earlier wrong guesses.</summary>
    public void RecordSuccess(string client)
    {
        lock (_gate)
        {
            if (_clients.TryGetValue(client, out var c)) c.Failures.Clear();
        }
    }

    private Client GetOrAdd(string client, DateTime now)
    {
        if (_clients.TryGetValue(client, out var existing)) return existing;

        if (_clients.Count >= MaxTrackedClients)
        {
            // Same bounded-memory rule as AuthThrottle: forget the idle first, then the least
            // recently active tenth, so an address-rotating flood cannot grow this table.
            foreach (var stale in _clients.Where(kv => kv.Value.LockedUntil <= now && now - kv.Value.LastActivity > _window)
                         .Select(kv => kv.Key).ToList())
                _clients.Remove(stale);
            if (_clients.Count >= MaxTrackedClients)
                foreach (var oldest in _clients.OrderBy(kv => kv.Value.LastActivity)
                             .Take(MaxTrackedClients / 10).Select(kv => kv.Key).ToList())
                    _clients.Remove(oldest);
        }

        var created = new Client { LastActivity = now };
        _clients[client] = created;
        return created;
    }

    private static void Trim(Queue<DateTime> q, DateTime cutoff)
    {
        while (q.Count > 0 && q.Peek() < cutoff) q.Dequeue();
    }
}
