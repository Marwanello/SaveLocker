using System.Threading.Channels;

namespace SaveLocker.Server.Services;

/// <summary>
/// Fills in artwork for games that have none, out of band. Triggered when a SteamGridDB key is saved:
/// every game enrolled before there was a key has no art, and the key is the moment that can change.
/// <para>
/// It runs in the background rather than inside the key-save request because that request would then
/// take (games × several SteamGridDB calls and downloads) — long enough to be cut off by whatever proxy
/// fronts the console, and the console would report the key as failed when it had worked. Games are
/// done one at a time, with a pause between, because being polite to SteamGridDB is the stated design
/// (see <see cref="ArtService"/>).
/// </para>
/// <para>
/// Requests coalesce: a trigger while one is already waiting is dropped, since the waiting run will
/// see the same missing games. A trigger during a run queues exactly one more.
/// </para>
/// </summary>
public sealed class ArtBackfillService : BackgroundService
{
    private static readonly TimeSpan PauseBetweenGames = TimeSpan.FromMilliseconds(300);

    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ArtBackfillService> _log;

    public ArtBackfillService(IServiceScopeFactory scopes, ILogger<ArtBackfillService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    /// <summary>Ask for a pass over the games that lack art. Returns immediately.</summary>
    public void Request() => _requests.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _requests.Reader.ReadAllAsync(ct))
                await RunAsync(ct);
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            List<Guid> ids;
            await using (var scope = _scopes.CreateAsyncScope())
                ids = await scope.ServiceProvider.GetRequiredService<ArtService>().GameIdsMissingArtAsync(ct);
            if (ids.Count == 0) return;

            _log.LogInformation("Art backfill: {Count} game(s) are missing artwork.", ids.Count);
            var filled = 0;
            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // A scope per game: each gets its own DbContext, so one game's failure cannot poison the next.
                    await using var scope = _scopes.CreateAsyncScope();
                    var art = scope.ServiceProvider.GetRequiredService<ArtService>();
                    var (ok, message) = await art.RefreshArtAsync(id, ct, onlyMissing: true);
                    if (ok) filled++;
                    // A key that was cleared while this ran makes every remaining game fail the same way.
                    else if (message.Contains("API key not configured", StringComparison.Ordinal)) break;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "Art backfill: game {GameId} failed; continuing.", id);
                }
                await Task.Delay(PauseBetweenGames, ct);
            }
            _log.LogInformation("Art backfill: filled artwork for {Filled} of {Count} game(s).", filled, ids.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Art backfill failed.");
        }
    }
}
