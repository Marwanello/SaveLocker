using System.Net;
using System.Net.Sockets;

namespace SaveLocker.Server.Services;

/// <summary>
/// Slows password guessing. Every place the server checks the ADMIN password — the console's
/// sign-in, the legacy <c>X-Admin-Password</c> header, and re-registering an existing machine — asks
/// this first and reports back, because each of those was an unmetered oracle: the only cost to a
/// guess was the PBKDF2 it made the server run, which an attacker gets to spend on the server's CPU.
/// <para>
/// Two layers, both in memory (a restart forgives everyone, which is the right failure mode for a
/// lockout that must never strand the owner behind a stale database row):
/// </para>
/// <list type="bullet">
/// <item><b>Per client.</b> <c>MaxFailedAttempts</c> wrong passwords inside <c>FailureWindowSeconds</c>
/// locks that client out for <c>LockoutSeconds</c>; each further lockout inside 24 h doubles it (to a
/// 4 h ceiling). A correct password clears the client's record.</item>
/// <item><b>Global backstop.</b> <c>GlobalMaxFailures</c> wrong passwords from ANYONE inside the
/// window refuses every password attempt for <c>GlobalLockoutSeconds</c>. This is what bounds a
/// distributed guess, and what covers the case where the client address is not meaningful — behind a
/// reverse proxy that has not been declared trusted, every request shares one address, so the
/// per-client layer degrades to a global one rather than to nothing. The cost is that an attacker can
/// hold new sign-ins off for that period; an existing session token is unaffected, and the period is
/// deliberately short.</item>
/// </list>
/// Wrong SESSION tokens are not counted: they are not guesses (256 random bits), and a stale
/// browser tab polling with an expired one would otherwise lock its own owner out of signing back in.
/// </summary>
public sealed class AuthThrottle
{
    private const int MaxTrackedClients = 5000;
    private static readonly TimeSpan StrikeMemory = TimeSpan.FromHours(24);
    private static readonly TimeSpan MaxLockout = TimeSpan.FromHours(4);

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
        public DateTime LastLockout = DateTime.MinValue;
        public DateTime LastActivity;
        public int Strikes;
    }

    public AuthThrottle(IConfiguration cfg)
    {
        _maxFailures = Math.Max(1, cfg.GetValue<int?>("Security:MaxFailedAttempts") ?? 5);
        _window = TimeSpan.FromSeconds(Math.Max(1, cfg.GetValue<int?>("Security:FailureWindowSeconds") ?? 900));
        _lockout = TimeSpan.FromSeconds(Math.Max(1, cfg.GetValue<int?>("Security:LockoutSeconds") ?? 900));
        _globalMaxFailures = Math.Max(1, cfg.GetValue<int?>("Security:GlobalMaxFailures") ?? 100);
        _globalLockout = TimeSpan.FromSeconds(Math.Max(1, cfg.GetValue<int?>("Security:GlobalLockoutSeconds") ?? 300));
    }

    /// <summary>
    /// What identifies "one client" for throttling: the connection's address (which, only when
    /// <c>Security:TrustedProxies</c> is configured, has already been replaced by the forwarded
    /// client address — see Program.cs). IPv4-mapped addresses collapse to IPv4, and an IPv6 client
    /// is its whole /64: a single host routinely owns 2^64 addresses, so keying on the full address
    /// would let it rotate past any limit.
    /// </summary>
    public static string ClientKey(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    /// <summary>Null when this client may attempt a password now, else how long until it may.</summary>
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

    /// <summary>Records a wrong password. True when this failure is the one that started a lockout.</summary>
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
                if (now - c.LastLockout > StrikeMemory) c.Strikes = 0;
                c.Strikes++;
                var multiplier = 1L << Math.Min(c.Strikes - 1, 4);
                c.LockedUntil = now + TimeSpan.FromTicks(Math.Min(_lockout.Ticks * multiplier, MaxLockout.Ticks));
                c.LastLockout = now;
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

    /// <summary>A correct password: forgive this client's earlier wrong ones.</summary>
    public void RecordSuccess(string client)
    {
        lock (_gate)
        {
            if (!_clients.TryGetValue(client, out var c)) return;
            c.Failures.Clear();
            c.Strikes = 0;
        }
    }

    private Client GetOrAdd(string client, DateTime now)
    {
        if (_clients.TryGetValue(client, out var existing)) return existing;

        if (_clients.Count >= MaxTrackedClients)
        {
            // Forget everyone who is neither locked out nor recently active; if that was not
            // enough (an address-rotating flood), drop the least recently active tenth. Either way
            // memory stays bounded no matter how many distinct addresses show up.
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
