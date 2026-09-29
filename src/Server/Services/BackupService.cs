using SaveLocker.Shared;
using Microsoft.Data.Sqlite;

namespace SaveLocker.Server.Services;

/// <summary>Configuration for <see cref="BackupService"/>, built once at startup. <c>Enabled</c>,
/// <c>RetentionCount</c> and <c>HourOfDay</c> are only the fallbacks: the admin's own choice lives in the
/// DB (<see cref="BackupService.GetSettingsAsync"/>) and is re-read on every scheduler loop.</summary>
public sealed class BackupOptions
{
    /// <summary>Absolute (or working-dir-relative) path to the live SQLite database file.</summary>
    public required string DbPath { get; init; }

    /// <summary>Directory the nightly snapshots are written to (created on demand).</summary>
    public required string BackupRoot { get; init; }

    /// <summary>How many of the most recent snapshots to keep; older ones are pruned.</summary>
    public int RetentionCount { get; init; } = 7;

    /// <summary>Local hour of day (0–23) the nightly snapshot fires at.</summary>
    public int HourOfDay { get; init; } = 3;

    /// <summary>When false, the scheduler is a no-op (manual <see cref="BackupService.BackupAsync"/> still works).</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>The scheduled-backup settings in force: the DB's value for each, else config's.</summary>
public sealed record BackupSettings(bool Enabled, int RetentionCount, int HourOfDay);

/// <summary>
/// Takes point-in-time snapshots of the live SQLite database. The DB <em>is</em> the version
/// graph (archives on disk are meaningless without it), so a corrupt or lost file loses
/// history for every machine — hence a self-contained on-box backup.
///
/// Snapshots are produced with <c>VACUUM INTO</c>, which reads a consistent view under a
/// read transaction and folds any pending WAL content into a fresh, defragmented single-file
/// copy. It is safe to run while the server is serving writes (unlike copying the .db file,
/// which can capture a torn WAL). The copy is written to a <c>.tmp</c> name and renamed on
/// success so retention never sees a half-written file.
///
/// The reason rides in the name after the timestamp (<c>savelocker-20260927-030000-manual.db</c>), so
/// the ordinal newest-first sort and the <c>savelocker-*.db</c> prune pattern work unchanged.
/// </summary>
public sealed class BackupService
{
    private const string Prefix = "savelocker-";
    private const string Extension = ".db";
    private const string Pattern = Prefix + "*" + Extension;
    private const int StampLength = 15; // yyyyMMdd-HHmmss

    public const string EnabledKey = "Backup:Enabled";
    public const string RetentionCountKey = "Backup:RetentionCount";
    public const string HourOfDayKey = "Backup:HourOfDay";
    public const int MaxRetention = 365;

    private readonly BackupOptions _options;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<BackupService> _log;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly object _gate = new();
    private string? _lastError;
    private DateTime? _lastErrorAt;
    private DateTime? _nextRunAt;

    public BackupService(BackupOptions options, IServiceScopeFactory scopes, ILogger<BackupService> log)
    {
        _options = options;
        _scopes = scopes;
        _log = log;
    }

    public BackupOptions Options => _options;

    /// <summary>When the scheduler will next fire; null while scheduled backups are off.</summary>
    public DateTime? NextRunAt { get { lock (_gate) return _nextRunAt; } internal set { lock (_gate) _nextRunAt = value; } }

    /// <summary>The most recent failure, kept for the page until a snapshot succeeds.</summary>
    public (string? Error, DateTime? At) LastError { get { lock (_gate) return (_lastError, _lastErrorAt); } }

    public async Task<BackupSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var enabled = bool.TryParse(await settings.GetEffectiveAsync(EnabledKey, ct), out var e) ? e : _options.Enabled;
        var keep = int.TryParse(await settings.GetEffectiveAsync(RetentionCountKey, ct), out var k) ? k : _options.RetentionCount;
        var hour = int.TryParse(await settings.GetEffectiveAsync(HourOfDayKey, ct), out var h) ? h : _options.HourOfDay;
        return new BackupSettings(enabled, Math.Clamp(keep, 1, MaxRetention), Math.Clamp(hour, 0, 23));
    }

    public async Task SetSettingsAsync(SetBackupSettingsRequest req, CancellationToken ct = default)
    {
        using (var scope = _scopes.CreateScope())
        {
            var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
            await settings.SetAsync(EnabledKey, req.Enabled ? "true" : "false", ct);
            await settings.SetAsync(RetentionCountKey, req.RetentionCount.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
            await settings.SetAsync(HourOfDayKey, req.HourOfDay.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        }
        // The scheduler is sleeping until the OLD hour; wake it so the new ones take effect now.
        _wake.Release();
    }

    /// <summary>Sleeps for <paramref name="delay"/>; true if woken early by a settings change.</summary>
    internal Task<bool> WaitForWakeAsync(TimeSpan delay, CancellationToken ct) => _wake.WaitAsync(delay, ct);

    /// <summary>Snapshot the DB, then prune old snapshots down to the retention count.</summary>
    /// <param name="retention">The count to prune to; null skips pruning (the before-upgrade snapshot runs
    /// before the DB it would read the setting from has been migrated).</param>
    public async Task<BackupResult> BackupAsync(BackupReason reason, int? retention, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(_options.BackupRoot);

            var fileName = $"{Prefix}{DateTime.Now:yyyyMMdd-HHmmss}{Suffix(reason)}{Extension}";
            var finalPath = Path.Combine(_options.BackupRoot, fileName);
            var tempPath = finalPath + ".tmp";
            if (File.Exists(tempPath)) File.Delete(tempPath);

            // A separate connection to the same file; coexists with the app's connection.
            // VACUUM INTO never writes the source, so this is a pure read of a consistent snapshot.
            await using (var conn = new SqliteConnection($"Data Source={_options.DbPath}"))
            {
                await conn.OpenAsync(ct);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM main INTO $target";
                cmd.Parameters.AddWithValue("$target", tempPath);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            File.Move(tempPath, finalPath, overwrite: true);

            var info = new BackupInfo(fileName, new FileInfo(finalPath).Length, File.GetLastWriteTimeUtc(finalPath), reason);
            var retained = retention is { } keep ? Prune(keep) : ListBackups().Count;
            lock (_gate) { _lastError = null; _lastErrorAt = null; }
            _log.LogInformation(
                "SQLite backup written: {File} ({Size:N0} bytes); {Count} snapshot(s) retained.",
                fileName, info.SizeBytes, retained);
            return new BackupResult(true, null, info, retained);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "SQLite backup failed.");
            lock (_gate) { _lastError = ex.Message; _lastErrorAt = DateTime.UtcNow; }
            return new BackupResult(false, ex.Message, null, ListBackups().Count);
        }
    }

    /// <summary>Existing snapshots, newest first.</summary>
    public IReadOnlyList<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(_options.BackupRoot)) return Array.Empty<BackupInfo>();
        return EnumerateNewestFirst()
            .Select(f => new BackupInfo(f.Name, f.Length, f.LastWriteTimeUtc, ReasonOf(f.Name)))
            .ToList();
    }

    /// <summary>
    /// The full path of the snapshot named <paramref name="fileName"/>, or null. The name is matched
    /// against the listing and never joined into a path, so <c>..</c>, an absolute path or a
    /// percent-encoded separator can only ever miss.
    /// </summary>
    public string? Find(string fileName)
    {
        if (!Directory.Exists(_options.BackupRoot)) return null;
        return EnumerateNewestFirst()
            .FirstOrDefault(f => string.Equals(f.Name, fileName, StringComparison.Ordinal))
            ?.FullName;
    }

    private static string Suffix(BackupReason reason) => reason switch
    {
        BackupReason.Manual => "-manual",
        BackupReason.BeforeUpgrade => "-before-upgrade",
        _ => "",
    };

    internal static BackupReason ReasonOf(string fileName)
    {
        var stem = fileName[Prefix.Length..^Extension.Length];
        return stem.Length > StampLength ? stem[StampLength..] switch
        {
            "-manual" => BackupReason.Manual,
            "-before-upgrade" => BackupReason.BeforeUpgrade,
            _ => BackupReason.Nightly,
        } : BackupReason.Nightly;
    }

    // Names start with fixed-width, zero-padded timestamps, so ordinal string order == chronological order.
    private IEnumerable<FileInfo> EnumerateNewestFirst() =>
        new DirectoryInfo(_options.BackupRoot)
            .EnumerateFiles(Pattern)
            .OrderByDescending(f => f.Name, StringComparer.Ordinal);

    private int Prune(int retention)
    {
        var files = EnumerateNewestFirst().ToList();
        foreach (var stale in files.Skip(Math.Max(0, retention)))
        {
            try { stale.Delete(); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to prune old backup {File}.", stale.Name); }
        }
        return Math.Min(files.Count, Math.Max(0, retention));
    }
}

/// <summary>
/// Fires <see cref="BackupService.BackupAsync"/> nightly at the configured hour, re-reading the settings
/// every loop (and immediately when an admin changes them), so the console's toggle takes effect without
/// a restart. When scheduled backups are on at startup it also takes a catch-up snapshot if the newest
/// existing one is missing or older than a day (e.g. the box was down over its window), while the age
/// guard keeps frequent redeploys from spamming snapshots.
/// </summary>
public sealed class BackupScheduler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan OffRecheck = TimeSpan.FromHours(1);

    private readonly BackupService _backup;
    private readonly ILogger<BackupScheduler> _log;

    public BackupScheduler(BackupService backup, ILogger<BackupScheduler> log)
    {
        _backup = backup;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            var first = await _backup.GetSettingsAsync(ct);
            if (first.Enabled && (MostRecentAge() is not { } age || age > Interval))
                await _backup.BackupAsync(BackupReason.Nightly, first.RetentionCount, ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var s = await _backup.GetSettingsAsync(ct);
                    if (!s.Enabled)
                    {
                        _backup.NextRunAt = null;
                        _log.LogInformation("Scheduled SQLite backups are off.");
                        await _backup.WaitForWakeAsync(OffRecheck, ct);
                        continue;
                    }

                    var delay = DelayUntilNextRun(s.HourOfDay);
                    _backup.NextRunAt = DateTime.UtcNow + delay;
                    _log.LogInformation("Next SQLite backup in {Hours:0.0} h.", delay.TotalHours);
                    if (await _backup.WaitForWakeAsync(delay, ct)) continue;

                    // Read again: the retention may have changed while this slept.
                    var now = await _backup.GetSettingsAsync(ct);
                    if (now.Enabled) await _backup.BackupAsync(BackupReason.Nightly, now.RetentionCount, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogError(ex, "Backup scheduler loop failed; retrying in a minute.");
                    await Task.Delay(TimeSpan.FromMinutes(1), ct);
                }
            }
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    private TimeSpan? MostRecentAge()
    {
        var newest = _backup.ListBackups().FirstOrDefault();
        return newest is null ? null : DateTime.UtcNow - newest.CreatedAt;
    }

    private static TimeSpan DelayUntilNextRun(int hourOfDay)
    {
        var now = DateTime.Now;
        var next = now.Date.AddHours(Math.Clamp(hourOfDay, 0, 23));
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
