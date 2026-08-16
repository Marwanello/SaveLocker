using SaveLocker.Shared;

namespace SaveLocker.Server.Services;

/// <summary>
/// Periodically checks the configured GitHub repository for a newer agent
/// installer and refreshes the server-hosted copy when enabled.
/// </summary>
public sealed class AgentInstallerPollerService : BackgroundService
{
    private static readonly TimeSpan ConfigurationCheckInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentInstallerPollerService> _log;

    public AgentInstallerPollerService(
        IServiceScopeFactory scopeFactory,
        ILogger<AgentInstallerPollerService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            double? configuredHours = null;
            DateTime? nextPollAt = null;

            while (!ct.IsCancellationRequested)
            {
                var hours = await GetConfiguredHoursAsync(ct);
                if (configuredHours != hours)
                {
                    configuredHours = hours;
                    nextPollAt = null;
                    if (hours > 0)
                        _log.LogInformation(
                            "GitHub installer auto-poll enabled; checking every {Hours:0.##} hour(s).", hours);
                    else
                        _log.LogInformation(
                            "GitHub installer auto-poll disabled (AgentUpdate:AutoFetchHours is not positive).");
                }

                if (hours > 0 && (nextPollAt is null || DateTime.UtcNow >= nextPollAt.Value))
                {
                    // A newly enabled or reconfigured schedule checks immediately.
                    await PollAsync(ct);
                    nextPollAt = DateTime.UtcNow.AddHours(hours);
                }

                var untilNextPoll = nextPollAt is null
                    ? ConfigurationCheckInterval
                    : nextPollAt.Value - DateTime.UtcNow;
                await Task.Delay(
                    untilNextPoll < ConfigurationCheckInterval ? untilNextPoll : ConfigurationCheckInterval,
                    ct);
            }
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    private async Task<double> GetConfiguredHoursAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        return await settings.GetAutoFetchHoursAsync(ct);
    }

    /// <summary>
    /// Checks every platform, each inside its own boundary. One platform's failure must not stop the
    /// others being refreshed — and the commonest failure by far is benign: a release that predates
    /// the Linux tarball simply has no asset for that slot, which is a warning, not an error.
    /// </summary>
    private async Task PollAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var installer = scope.ServiceProvider.GetRequiredService<AgentInstallerService>();
        var sync = scope.ServiceProvider.GetRequiredService<SyncService>();
        var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();

        foreach (var platform in AgentPlatform.All)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var before = installer.GetInfo(platform);
                var after = await installer.FetchLatestFromGitHubAsync(
                    http, ct, onlyIfNewer: true, platform: platform);

                if (before?.Version == after.Version && before.UploadedAt == after.UploadedAt)
                    _log.LogDebug("GitHub installer auto-poll: hosted {Platform} package v{Version} is current.",
                        platform, after.Version);
                else
                {
                    _log.LogInformation("GitHub installer auto-poll: hosted {Platform} package updated to v{Version}.",
                        platform, after.Version);
                    await sync.LogAuditAsync("agent_installer.auto_fetch",
                        $"{platform}: {(before is null ? "—" : "v" + before.Version)} → v{after.Version}");
                }
            }
            catch (InstallerRejectedException ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning("GitHub installer auto-poll: nothing to fetch for {Platform}. {Reason}",
                    platform, ex.Message);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogError(ex, "GitHub installer auto-poll failed for {Platform}.", platform);
            }
        }
    }
}
