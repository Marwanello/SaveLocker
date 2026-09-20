using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SaveLocker.Server.Data;

namespace SaveLocker.Server.Services;

public enum PasswordOutcome { Ok, Invalid, Throttled }

/// <param name="RetryAfter">Set when <see cref="PasswordOutcome.Throttled"/>: how long until an attempt is considered.</param>
public readonly record struct PasswordCheck(PasswordOutcome Outcome, TimeSpan? RetryAfter = null);

/// <summary>
/// Remembers, for a few minutes, that a given plaintext already verified against a given stored
/// hash. <c>AdminPasswordFilter</c> used to run PBKDF2 on EVERY admin request (the dashboard makes
/// six per refresh), which was tolerable at 100k iterations and is not at 600k; scripts that still
/// send <c>X-Admin-Password</c> would otherwise pay it on every call. Only SUCCESSES are cached, keyed
/// by an HMAC under a per-process random key — the plaintext is never held, nothing is persisted, and
/// an entry is bound to the stored hash it verified against, so changing the password invalidates it
/// on the spot.
/// </summary>
public sealed class VerifiedPasswordCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private const int MaxEntries = 16;

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly object _gate = new();
    private readonly Dictionary<string, (string StoredHash, DateTime Expires)> _entries = new();

    private string Fingerprint(string password) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(password)));

    public bool IsVerified(string password, string storedHash)
    {
        var fp = Fingerprint(password);
        lock (_gate)
            return _entries.TryGetValue(fp, out var e) && e.StoredHash == storedHash && e.Expires > DateTime.UtcNow;
    }

    public void Remember(string password, string storedHash)
    {
        var fp = Fingerprint(password);
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_entries.Count >= MaxEntries)
            {
                foreach (var dead in _entries.Where(kv => kv.Value.Expires <= now).Select(kv => kv.Key).ToList())
                    _entries.Remove(dead);
                if (_entries.Count >= MaxEntries) _entries.Clear();
            }
            _entries[fp] = (storedHash, now + Ttl);
        }
    }
}

/// <summary>
/// The console's admin credential: checking the password (throttled — see <see cref="AuthThrottle"/>)
/// and the revocable sessions that replace keeping that password in the browser.
/// </summary>
public sealed class AdminAuth
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly AuthThrottle _throttle;
    private readonly VerifiedPasswordCache _cache;
    private readonly IConfiguration _cfg;

    public AdminAuth(AppDbContext db, SettingsService settings, AuthThrottle throttle,
        VerifiedPasswordCache cache, IConfiguration cfg)
    {
        _db = db;
        _settings = settings;
        _throttle = throttle;
        _cache = cache;
        _cfg = cfg;
    }

    // Idle expiry slides forward as the session is used; the absolute cap does not.
    private TimeSpan IdleLifetime => TimeSpan.FromDays(Math.Max(1, _cfg.GetValue<int?>("Security:SessionIdleDays") ?? 7));
    private TimeSpan MaxLifetime => TimeSpan.FromDays(Math.Max(1, _cfg.GetValue<int?>("Security:SessionMaxDays") ?? 30));
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(1);

    /// <summary>
    /// Check a candidate admin password against <paramref name="storedHash"/>. The order matters: a
    /// locked-out client is refused BEFORE any PBKDF2 runs (a refusal must be cheaper than the guess it
    /// refuses), then a recent success is answered from the cache, and only then is the hash computed.
    /// A hash written by an older version is re-written at today's strength on the way through — this
    /// is the only moment the plaintext is in hand to do it.
    /// </summary>
    /// <param name="via">Which door this came through ("login", "header", "register") — audit context only.</param>
    public async Task<PasswordCheck> CheckPasswordAsync(string provided, string storedHash, HttpContext http, string via)
    {
        var client = AuthThrottle.ClientKey(http);
        if (_throttle.RetryAfter(client) is { } wait)
            return new PasswordCheck(PasswordOutcome.Throttled, wait);

        if (_cache.IsVerified(provided, storedHash))
        {
            _throttle.RecordSuccess(client);
            return new PasswordCheck(PasswordOutcome.Ok);
        }

        if (Tokens.VerifyPassword(provided, storedHash))
        {
            _throttle.RecordSuccess(client);
            var effective = storedHash;
            if (Tokens.NeedsRehash(storedHash))
                effective = await _settings.UpgradeAdminPasswordHashAsync(provided, storedHash) ?? storedHash;
            _cache.Remember(provided, effective);
            return new PasswordCheck(PasswordOutcome.Ok);
        }

        if (_throttle.RecordFailure(client))
            await AuditAsync("admin.lockout", $"{client}: too many wrong admin passwords (via {via}); further attempts are refused for a while");
        return new PasswordCheck(PasswordOutcome.Invalid);
    }

    // ----- Sessions -----

    /// <summary>Mint a session. The token is returned here and nowhere else — only its hash is kept.</summary>
    public async Task<(string Token, DateTime ExpiresAt)> CreateSessionAsync(HttpContext http)
    {
        var now = DateTime.UtcNow;
        // Housekeeping rides along with sign-ins, the only thing that adds rows: no sweeper needed.
        await _db.AdminSessions.Where(s => s.ExpiresAt < now).ExecuteDeleteAsync();

        var token = Tokens.NewApiKey();
        var expires = now + IdleLifetime;
        var client = AuthThrottle.ClientKey(http);
        _db.AdminSessions.Add(new AdminSession
        {
            Id = Guid.NewGuid(),
            TokenHash = Tokens.Hash(token),
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = expires,
            ClientAddress = client
        });
        await AuditAsync("admin.login", $"signed in from {client}");
        return (token, expires);
    }

    /// <summary>True for a live session. Also slides its idle expiry forward — at most once an hour,
    /// so six dashboard requests a poll do not become six database writes.</summary>
    public async Task<bool> ValidateSessionAsync(string token)
    {
        var hash = Tokens.Hash(token);
        var session = await _db.AdminSessions.AsNoTracking().FirstOrDefaultAsync(s => s.TokenHash == hash);
        if (session is null) return false;

        var now = DateTime.UtcNow;
        if (session.ExpiresAt <= now) return false;

        if (now - session.LastUsedAt > RefreshEvery)
        {
            var extended = Min(now + IdleLifetime, session.CreatedAt + MaxLifetime);
            var stale = now - RefreshEvery;
            // Conditional on LastUsedAt so the parallel requests of one poll race to a single write.
            await _db.AdminSessions
                .Where(s => s.Id == session.Id && s.LastUsedAt < stale)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.LastUsedAt, now)
                    .SetProperty(s => s.ExpiresAt, extended));
        }
        return true;
    }

    public async Task RevokeSessionAsync(string token)
    {
        var hash = Tokens.Hash(token);
        if (await _db.AdminSessions.Where(s => s.TokenHash == hash).ExecuteDeleteAsync() > 0)
            await AuditAsync("admin.logout", "signed out");
    }

    /// <summary>Sign every browser out. Returns how many sessions were ended.</summary>
    public async Task<int> RevokeAllSessionsAsync()
    {
        var n = await _db.AdminSessions.ExecuteDeleteAsync();
        await AuditAsync("admin.logout_all", $"{n} session(s) ended");
        return n;
    }

    private async Task AuditAsync(string action, string detail)
    {
        _db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Action = action, Detail = detail });
        await _db.SaveChangesAsync();
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
