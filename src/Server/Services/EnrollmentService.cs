using Microsoft.EntityFrameworkCore;
using SaveLocker.Server.Data;
using SaveLocker.Shared;

namespace SaveLocker.Server.Services;

/// <summary>
/// Enrollment: the console mints a single-use, short-lived <b>join code</b> (tasks/enrollment-options,
/// option B), the link <c>/join/{code}</c> opens a pairing page on the new machine, and the agent
/// trades the code for its real machine API key (Decisions.md §4). A leaked link therefore expires on
/// its own and is revocable — a long-lived API key sitting in a browser history is neither.
/// </summary>
public sealed class EnrollmentService
{
    /// <summary>Long enough to walk to the other machine, short enough that a stray link is dead on arrival.</summary>
    public const int DefaultTtlMinutes = 15;
    private const int MaxTtlMinutes = 24 * 60;

    /// <summary>
    /// How long a code lingers in the console's enrollment list after its window closes, before it
    /// is pruned. The audit log keeps the permanent record (create / redeem / revoke / expire); this
    /// only controls how long dead links clutter the list.
    /// </summary>
    public static readonly TimeSpan ListRetention = TimeSpan.FromHours(24);

    private readonly AppDbContext _db;
    private readonly SyncService _sync;
    private readonly IConfiguration _config;
    private readonly SettingsService _settings;

    public EnrollmentService(AppDbContext db, SyncService sync, IConfiguration config, SettingsService settings)
    {
        _db = db;
        _sync = sync;
        _config = config;
        _settings = settings;
    }

    /// <summary>
    /// Mint a join code. The raw code is returned here and nowhere else — only its hash is stored —
    /// so it is shown once on the console and the pairing page, and never again.
    /// </summary>
    public async Task<CreateEnrollmentResponse> CreateAsync(CreateEnrollmentRequest req, string requestServerUrl)
    {
        var ttl = Math.Clamp(req.TtlMinutes ?? DefaultTtlMinutes, 1, MaxTtlMinutes);
        var now = DateTime.UtcNow;
        var rawCode = JoinCodes.New();

        var token = new EnrollmentToken
        {
            Id = Guid.NewGuid(),
            TokenHash = Tokens.Hash(rawCode),
            MachineName = string.IsNullOrWhiteSpace(req.MachineName) ? null : req.MachineName.Trim(),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(ttl)
        };
        _db.EnrollmentTokens.Add(token);

        var serverUrl = (string.IsNullOrWhiteSpace(req.ServerUrl) ? requestServerUrl : req.ServerUrl).TrimEnd('/');
        _db.AuditLogs.Add(NewAudit("enrollment.create", MintDetail(token)));
        await _db.SaveChangesAsync();

        return new CreateEnrollmentResponse(token.Id, rawCode, $"{serverUrl}/join/{rawCode}", token.ExpiresAt);
    }

    /// <summary>
    /// What an unknown code is told, everywhere. The route layer compares against it to decide
    /// whether a failed redeem counts as a guess (unknown) or as a code the caller already had
    /// (used/expired) — see <see cref="JoinThrottle"/>.
    /// </summary>
    public const string UnknownCodeMessage = "Unknown enrollment code. Create a new join link from the console.";

    /// <summary>The state a join code is in right now, for the pairing page and the policy route.</summary>
    public enum JoinCodeState { Valid, Used, Expired, Unknown }

    /// <summary>Find a join code by its (normalized) raw value and say what state it is in.</summary>
    public async Task<(JoinCodeState State, EnrollmentToken? Token)> LookupAsync(string? code)
    {
        var normalized = JoinCodes.NormalizeJoinCode(code);
        if (normalized is null) return (JoinCodeState.Unknown, null);

        var hash = Tokens.Hash(normalized);
        var token = await _db.EnrollmentTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null) return (JoinCodeState.Unknown, null);
        if (token.RedeemedAt is not null) return (JoinCodeState.Used, token);
        if (token.ExpiresAt <= DateTime.UtcNow) return (JoinCodeState.Expired, token);
        return (JoinCodeState.Valid, token);
    }

    /// <summary>
    /// Build the policy an agent fetches from <c>/join/{code}/policy</c>. Built at delivery rather
    /// than at mint, so the games list and the default excludes are whatever the server holds at the
    /// moment the link is opened — the same answer the agent's first reconcile would reach.
    /// </summary>
    public async Task<EnrollmentPolicy> BuildPolicyAsync(EnrollmentToken token, string rawCode, string serverUrl)
    {
        var games = await _db.Games
            .Where(g => g.Enabled)
            .OrderBy(g => g.Name)
            .ToListAsync();

        var (defaultExcludes, _) = await _settings.GetDefaultExcludesAsync();

        return new EnrollmentPolicy(
            Version: EnrollmentPolicy.CurrentVersion,
            ServerUrl: serverUrl.TrimEnd('/'),
            Token: rawCode,
            ExpiresAt: token.ExpiresAt,
            MachineName: token.MachineName,
            SettleQuietSeconds: null,
            SettleMaxWaitSeconds: null,
            Games: games.Select(g => new EnrollmentGame(
                g.Id,
                g.Name,
                g.ManifestKey,
                g.SuggestedSaveDir,
                GlobConfig.Effective(defaultExcludes, g.ExcludeGlobs))).ToArray());
    }

    /// <summary>
    /// Every still-relevant code, newest first, with the machine that spent it (for the console's
    /// list). Codes whose window closed more than <see cref="ListRetention"/> ago are dropped —
    /// their history lives in the audit log, not this list. The sweeper deletes them for real
    /// shortly after; filtering here makes the 24-hour cutoff exact regardless of sweep timing.
    /// </summary>
    public async Task<List<EnrollmentDto>> ListAsync()
    {
        var cutoff = DateTime.UtcNow - ListRetention;
        var rows = await _db.EnrollmentTokens
            .Where(t => t.ExpiresAt > cutoff)
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .ToListAsync();

        var machineNames = await _db.Machines.ToDictionaryAsync(m => m.Id, m => m.Name);

        return rows.Select(t => new EnrollmentDto(
            t.Id,
            t.MachineName,
            t.CreatedAt,
            t.ExpiresAt,
            t.RedeemedAt,
            t.RedeemedByMachineId is { } id ? machineNames.GetValueOrDefault(id) : null)).ToList();
    }

    /// <summary>Revoke a code. Deleting a redeemed one only drops the record; it does not
    /// un-issue the API key (delete the machine for that).</summary>
    public async Task<bool> RevokeAsync(Guid id)
    {
        var token = await _db.EnrollmentTokens.FindAsync(id);
        if (token is null) return false;
        _db.EnrollmentTokens.Remove(token);
        _db.AuditLogs.Add(NewAudit("enrollment.revoke", Who(token)));
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Delete codes whose window closed more than <see cref="ListRetention"/> ago, so the console's
    /// enrollment list does not accumulate dead links forever. The audit log is the durable record:
    /// a code that was never redeemed is logged as <c>enrollment.expire</c> on the way out (a
    /// redeemed one already logged <c>enrollment.redeem</c>, a revoked one is deleted at revoke time).
    /// Called on the hourly maintenance sweep. Returns how many rows were removed.
    /// </summary>
    public async Task<int> PruneExpiredAsync()
    {
        var cutoff = DateTime.UtcNow - ListRetention;
        var stale = await _db.EnrollmentTokens
            .Where(t => t.ExpiresAt < cutoff)
            .ToListAsync();
        if (stale.Count == 0) return 0;

        foreach (var t in stale)
        {
            if (t.RedeemedAt is null)
                _db.AuditLogs.Add(NewAudit("enrollment.expire", ExpiredDetail(t)));
            _db.EnrollmentTokens.Remove(t);
        }
        await _db.SaveChangesAsync();
        return stale.Count;
    }

    /// <summary>
    /// Spend a join code for a machine API key. Returns null with a reason when the code is unknown,
    /// expired or already spent — the caller must not distinguish those to the client beyond the
    /// message, since all three mean the same thing: this link is no good.
    /// </summary>
    public async Task<(RedeemEnrollmentResponse? Result, string? Error)> RedeemAsync(RedeemEnrollmentRequest req)
    {
        // Normalized first, so the same code works typed with a dash, in lowercase, or with the
        // confusable letters Crockford leaves out.
        var normalized = JoinCodes.NormalizeJoinCode(req.Token);
        if (normalized is null)
            return (null, UnknownCodeMessage);

        var hash = Tokens.Hash(normalized);
        var token = await _db.EnrollmentTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (token is null)
            return (null, UnknownCodeMessage);
        if (token.RedeemedAt is not null)
            return (null, "This join link has already been used. Create a new one from the console.");
        if (token.ExpiresAt <= DateTime.UtcNow)
            return (null, "This join link has expired. Create a new one from the console.");

        // The name on the token is binding when set: a code minted for the Deck cannot be spent to
        // claim the desktop's identity. Only an unbound code lets the agent name itself.
        var name = token.MachineName
                   ?? (string.IsNullOrWhiteSpace(req.MachineName) ? null : req.MachineName.Trim());
        if (string.IsNullOrWhiteSpace(name))
            return (null, "No machine name: this link was not minted for a machine, so the agent must supply one.");

        // Spending the code and issuing the key are ONE transaction. The burn used to stand on its
        // own, so a failure in registration or in the writes below left the link spent and the agent
        // keyless — a single-use credential consumed for nothing, and the admin's only route back
        // was to mint another. Anything that throws before the commit rolls the burn back and the
        // link stays usable.
        await using var tx = await _db.Database.BeginTransactionAsync();

        // Conditional on the token still being unspent, so two agents racing the same link cannot
        // both come away with a key: the second UPDATE re-evaluates the predicate against the row
        // the first one committed and matches nothing.
        var burnt = await _db.EnrollmentTokens
            .Where(t => t.Id == token.Id && t.RedeemedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RedeemedAt, DateTime.UtcNow));
        if (burnt == 0)
        {
            await tx.RollbackAsync();
            return (null, "This join link has already been used. Create a new one from the console.");
        }

        // Registering an existing name ROTATES its key. That is the intended re-enrollment path (a
        // wiped Deck gets a fresh link and comes back as itself) and it is authorised by the code,
        // exactly as the admin password authorises re-registration on /api/machines/register.
        var reg = await _sync.RegisterMachineAsync(name);

        await _db.EnrollmentTokens
            .Where(t => t.Id == token.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RedeemedByMachineId, reg.MachineId));

        _db.AuditLogs.Add(NewAudit("enrollment.redeem", name, reg.MachineId));
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return (new RedeemEnrollmentResponse(reg.MachineId, reg.ApiKey, name), null);
    }

    private static AuditLog NewAudit(string action, string? detail, Guid? machineId = null) => new()
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        MachineId = machineId,
        Action = action,
        Detail = detail
    };

    private static string Who(EnrollmentToken t) => t.MachineName ?? "(any machine)";

    // Record the expiry on the audit trail so troubleshooting can confirm when a link was valid,
    // even after the token row itself is pruned from the list. The raw code is not stored, so the
    // audit detail says who it was for, never the code itself.
    private static string MintDetail(EnrollmentToken t) =>
        $"{Who(t)} (expires {t.ExpiresAt:yyyy-MM-dd HH:mm} UTC)";

    private static string ExpiredDetail(EnrollmentToken t) =>
        $"{Who(t)} (expired {t.ExpiresAt:yyyy-MM-dd HH:mm} UTC, never used)";
}
