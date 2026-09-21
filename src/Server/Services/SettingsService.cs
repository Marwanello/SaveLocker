using SaveLocker.Server.Data;
using SaveLocker.Shared;
using Microsoft.EntityFrameworkCore;

namespace SaveLocker.Server.Services;

/// <summary>
/// Server settings persisted as DB key/value pairs, with fallback to
/// <see cref="IConfiguration"/> (appsettings / env) for back-compat. Lets admins
/// manage things like the SteamGridDB API key from the dashboard instead of
/// editing config files. A DB value always wins over the config value.
/// </summary>
public sealed class SettingsService
{
    /// <summary>Settings key for the SteamGridDB API key (matches the config path).</summary>
    public const string SteamGridDbApiKey = "SteamGridDb:ApiKey";

    /// <summary>Settings key for the admin dashboard password hash.</summary>
    public const string AdminPasswordHash = "Admin:PasswordHash";

    /// <summary>Settings key for the GitHub installer auto-fetch interval. Still the only thing
    /// read when <see cref="AgentUpdateScheduleMode"/> is absent or "hours" — existing deployments
    /// that never touch the newer weekly/monthly modes see no behaviour change.</summary>
    public const string AgentUpdateAutoFetchHours = "AgentUpdate:AutoFetchHours";

    /// <summary>"disabled" | "hours" | "weekly" | "monthly". Absent means "hours", for back-compat
    /// with every deployment that predates weekly/monthly scheduling.</summary>
    public const string AgentUpdateScheduleMode = "AgentUpdate:Schedule:Mode";
    public const string AgentUpdateScheduleDayOfWeek = "AgentUpdate:Schedule:DayOfWeek";
    public const string AgentUpdateScheduleDayOfMonth = "AgentUpdate:Schedule:DayOfMonth";
    public const string AgentUpdateScheduleTimeOfDay = "AgentUpdate:Schedule:TimeOfDay";

    /// <summary>The console's look (docs/tasks/checkpoint-ui/plan.md). Ids, never colours — see
    /// <see cref="Appearances"/>. Absent means the default, so a deployment that never opens the
    /// Appearance card sees nothing change; they are also settable from config/env
    /// (<c>Ui__Accent=coolant</c>), which is how an unRAID template can pick one up front.</summary>
    public const string UiTheme = "Ui:Theme";
    public const string UiAccent = "Ui:Accent";
    public const string UiMark = "Ui:Mark";
    /// <summary>Whether enrolled agents are told the look above. Absent means <b>true</b> — the
    /// prototype's default, and the reason "set it once, every machine matches" works out of the box.</summary>
    public const string UiPushToAgents = "Ui:PushToAgents";

    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;

    public SettingsService(AppDbContext db, IConfiguration cfg)
    {
        _db = db;
        _cfg = cfg;
    }

    /// <summary>The DB value if set, else the configuration value, else null.</summary>
    public async Task<string?> GetEffectiveAsync(string key, CancellationToken ct = default)
    {
        var row = await _db.Settings.FindAsync(new object?[] { key }, ct);
        if (!string.IsNullOrWhiteSpace(row?.Value)) return row!.Value;
        var fromCfg = _cfg[key];
        return string.IsNullOrWhiteSpace(fromCfg) ? null : fromCfg;
    }

    /// <summary>Store (or clear, when null/blank) a setting in the DB.</summary>
    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        value = value?.Trim();
        var row = await _db.Settings.FindAsync(new object?[] { key }, ct);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (row is not null) { _db.Settings.Remove(row); await _db.SaveChangesAsync(ct); }
            return;
        }

        if (row is null) _db.Settings.Add(new AppSetting { Key = key, Value = value });
        else row.Value = value;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> HasAdminPasswordAsync(CancellationToken ct = default) =>
        !string.IsNullOrEmpty(await GetEffectiveAsync(AdminPasswordHash, ct));

    /// <summary>
    /// Set (or, when blank, clear) the admin password. Every signed-in browser is signed out: a
    /// password change is the one moment an owner is certain to want a stolen session dead, and it
    /// costs a legitimate user one sign-in.
    /// </summary>
    public async Task SetAdminPasswordAsync(string? password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(password))
            await SetAsync(AdminPasswordHash, null, ct);
        else
            await SetAsync(AdminPasswordHash, Tokens.HashPassword(password), ct);
        await _db.AdminSessions.ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Re-write the stored admin hash at today's strength, given the plaintext that was JUST verified
    /// against <paramref name="verifiedHash"/>. Returns the new hash, or null when there was nothing
    /// to do. Compare-and-set on <paramref name="verifiedHash"/>: a sign-in that verified the OLD
    /// password must not overwrite one an admin changed a moment later. A hash that lives in
    /// configuration (env / appsettings) rather than the database is left alone — there is nowhere
    /// to write it back to.
    /// </summary>
    public async Task<string?> UpgradeAdminPasswordHashAsync(string password, string verifiedHash, CancellationToken ct = default)
    {
        var row = await _db.Settings.FindAsync(new object?[] { AdminPasswordHash }, ct);
        if (row is null || row.Value != verifiedHash || !Tokens.NeedsRehash(row.Value)) return null;
        row.Value = Tokens.HashPassword(password);
        await _db.SaveChangesAsync(ct);
        return row.Value;
    }

    public async Task<double> GetAutoFetchHoursAsync(CancellationToken ct = default)
    {
        var value = await GetEffectiveAsync(AgentUpdateAutoFetchHours, ct);
        return double.TryParse(value, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var hours) &&
               double.IsFinite(hours) && hours >= 0 && hours <= TimeSpan.MaxValue.TotalHours
            ? hours
            : 0;
    }

    public async Task SetAutoFetchHoursAsync(double hours, CancellationToken ct = default)
    {
        if (!double.IsFinite(hours) || hours < 0 || hours > TimeSpan.MaxValue.TotalHours)
            throw new ArgumentOutOfRangeException(nameof(hours), "Hours must be a finite, non-negative value.");

        // Store zero explicitly so an admin can override a positive appsettings/env value to disable polling.
        await SetAsync(AgentUpdateAutoFetchHours,
            hours.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
    }

    /// <summary>The full schedule, defaulting Mode to "hours" when never set — the DB carries no
    /// row for it on every deployment that predates weekly/monthly scheduling, and defaulting to
    /// "hours" there means <see cref="GetAutoFetchHoursAsync"/> (and its own config/env fallback)
    /// keeps being the entire story for them, unchanged.</summary>
    public async Task<AutoFetchSchedule> GetAutoFetchScheduleAsync(CancellationToken ct = default)
    {
        var mode = await GetEffectiveAsync(AgentUpdateScheduleMode, ct) ?? "hours";
        var hours = await GetAutoFetchHoursAsync(ct);
        var dayOfWeek = int.TryParse(await GetEffectiveAsync(AgentUpdateScheduleDayOfWeek, ct), out var dw)
            ? Math.Clamp(dw, 0, 6) : 0; // Sunday
        var dayOfMonth = int.TryParse(await GetEffectiveAsync(AgentUpdateScheduleDayOfMonth, ct), out var dm)
            ? Math.Clamp(dm, 1, 31) : 1;
        var timeOfDay = await GetEffectiveAsync(AgentUpdateScheduleTimeOfDay, ct) ?? "03:00";
        return new AutoFetchSchedule(mode, hours, dayOfWeek, dayOfMonth, timeOfDay);
    }

    public async Task SetAutoFetchScheduleAsync(AutoFetchSchedule schedule, CancellationToken ct = default)
    {
        var mode = schedule.Mode?.Trim().ToLowerInvariant();
        if (mode is not ("disabled" or "hours" or "weekly" or "monthly"))
            throw new ArgumentOutOfRangeException(nameof(schedule), "Mode must be disabled, hours, weekly, or monthly.");
        if (schedule.DayOfWeek is < 0 or > 6)
            throw new ArgumentOutOfRangeException(nameof(schedule), "DayOfWeek must be 0-6 (0=Sunday).");
        if (schedule.DayOfMonth is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(schedule), "DayOfMonth must be 1-31.");
        if (!TimeOnly.TryParse(schedule.TimeOfDay, out _))
            throw new ArgumentOutOfRangeException(nameof(schedule), "TimeOfDay must be a time like 03:00.");

        await SetAsync(AgentUpdateScheduleMode, mode, ct);
        // Only touched in hours mode, through its own validated setter — switching to weekly and
        // back to hours later should not have silently zeroed out the interval the admin had.
        if (mode == "hours")
            await SetAutoFetchHoursAsync(schedule.Hours, ct);
        await SetAsync(AgentUpdateScheduleDayOfWeek, schedule.DayOfWeek.ToString(), ct);
        await SetAsync(AgentUpdateScheduleDayOfMonth, schedule.DayOfMonth.ToString(), ct);
        await SetAsync(AgentUpdateScheduleTimeOfDay, schedule.TimeOfDay, ct);
    }

    /// <summary>
    /// The console's look and whether it is shared. Always a known-good <see cref="AppearanceDto"/>:
    /// a value that is not a recognised id (a typo in an env var, a row written by a newer build) is
    /// replaced with that field's default here, at the one place every reader goes through.
    /// </summary>
    public async Task<AppearanceSettingsDto> GetAppearanceAsync(CancellationToken ct = default)
    {
        var look = Appearances.Normalize(new AppearanceDto(
            await GetEffectiveAsync(UiTheme, ct) ?? "",
            await GetEffectiveAsync(UiAccent, ct) ?? "",
            await GetEffectiveAsync(UiMark, ct) ?? ""));
        var push = !bool.TryParse(await GetEffectiveAsync(UiPushToAgents, ct), out var parsed) || parsed;
        return new AppearanceSettingsDto(look, push);
    }

    /// <summary>
    /// Store the console's look. All four keys are written explicitly, including a value that equals
    /// the default: a stored "ember" keeps meaning Ember if a later release changes what the default is.
    /// </summary>
    public async Task SetAppearanceAsync(SetAppearanceRequest req, CancellationToken ct = default)
    {
        if (!Appearances.IsValid(req.Theme, req.Accent, req.Mark))
            throw new ArgumentOutOfRangeException(nameof(req),
                $"Theme must be one of {string.Join(", ", Appearances.Themes)}; accent one of " +
                $"{string.Join(", ", Appearances.Accents)}; mark one of {string.Join(", ", Appearances.Marks)}.");

        var look = Appearances.Normalize(new AppearanceDto(req.Theme, req.Accent, req.Mark));
        await SetAsync(UiTheme, look.Theme, ct);
        await SetAsync(UiAccent, look.Accent, ct);
        await SetAsync(UiMark, look.Mark, ct);
        await SetAsync(UiPushToAgents, req.PushToAgents ? "true" : "false", ct);
    }

    /// <summary>The dashboard-facing settings snapshot (never includes the raw key).</summary>
    public async Task<ServerSettingsDto> GetServerSettingsDtoAsync(CancellationToken ct = default)
    {
        var inDb = await _db.Settings.AnyAsync(s => s.Key == SteamGridDbApiKey && s.Value != "", ct);
        var key = await GetEffectiveAsync(SteamGridDbApiKey, ct);
        var schedule = await GetAutoFetchScheduleAsync(ct);
        return new ServerSettingsDto(
            SteamGridDbConfigured: !string.IsNullOrWhiteSpace(key),
            SteamGridDbKeyMasked: Mask(key),
            SteamGridDbFromConfig: !inDb && !string.IsNullOrWhiteSpace(key),
            AdminPasswordSet: await HasAdminPasswordAsync(ct),
            DefaultExcludeGlobs: GlobConfig.GlobalDefaults(_cfg),
            AutoFetchHours: schedule.Hours,
            Schedule: schedule,
            NextAutoFetchRunAt: AutoFetchScheduler.ComputeNextRun(schedule, DateTime.UtcNow),
            Appearance: await GetAppearanceAsync(ct));
    }

    /// <summary>Show only the last 4 characters so the dashboard can confirm which key is set.</summary>
    private static string? Mask(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (s.Length <= 4) return new string('•', s.Length);
        return new string('•', Math.Min(8, s.Length - 4)) + s[^4..];
    }
}
