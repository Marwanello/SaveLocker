using SaveLocker.Agent;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// A sync while the server is down must end the way a push always has — the save queued for retry —
/// not as an exception out of the pull that runs first. The "server" here is a port nothing listens on,
/// so the failure is the real one an unplugged network produces, not a stub of it.
/// </summary>
public sealed class OfflineSyncTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "savelocker-offline-" + Guid.NewGuid().ToString("N"));

    public OfflineSyncTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private (SyncEngine Engine, OfflineQueue Queue, AgentConfig Config) Rig(params TrackedGame[] games)
    {
        var cfgPath = Path.Combine(_dir, "config.json");
        var config = AgentConfig.Load(cfgPath);
        config.ServerUrl = "http://127.0.0.1:1";
        config.ApiKey = "offline-test-key";
        config.Games.AddRange(games);
        config.Save();
        var queue = OfflineQueue.For(config);
        return (new SyncEngine(config, ApiClient.For(config), offlineQueue: queue), queue, config);
    }

    private TrackedGame GameWithSave(string name)
    {
        var save = Path.Combine(_dir, name + "-save");
        Directory.CreateDirectory(save);
        File.WriteAllText(Path.Combine(save, "slot1.sav"), "progress " + Guid.NewGuid());
        return new TrackedGame { GameId = Guid.NewGuid(), Name = name, SaveDirectory = save };
    }

    [Fact]
    public async Task Sync_WithTheServerDown_QueuesTheSave_InsteadOfThrowing()
    {
        var game = GameWithSave("Hades");
        var (engine, queue, _) = Rig(game);

        var message = await engine.SyncGameAsync(game, GameSyncMode.Sync);

        Assert.True(queue.Contains(game.GameId));
        Assert.Contains("queued", message);
    }

    [Fact]
    public async Task Pull_WithTheServerDown_SaysSo_InsteadOfThrowing()
    {
        var game = GameWithSave("Celeste");
        var (engine, queue, _) = Rig(game);

        var message = await engine.SyncGameAsync(game, GameSyncMode.Pull);

        Assert.Contains("unreachable", message);
        Assert.False(queue.Contains(game.GameId));
    }

    [Fact]
    public async Task SyncAll_WithTheServerDown_QueuesEveryEnrolledGame_AndSkipsTheOnesWithNoFolder()
    {
        var a = GameWithSave("Hades");
        var b = GameWithSave("Celeste");
        var onServerOnly = new TrackedGame { GameId = Guid.NewGuid(), Name = "Elsewhere", SaveDirectory = "" };
        var (engine, queue, config) = Rig(a, b, onServerOnly);

        var message = await engine.SyncAllAsync(config.Games);

        Assert.StartsWith("Sync all complete", message);
        Assert.True(queue.Contains(a.GameId));
        Assert.True(queue.Contains(b.GameId));
        Assert.False(queue.Contains(onServerOnly.GameId));
    }
}
