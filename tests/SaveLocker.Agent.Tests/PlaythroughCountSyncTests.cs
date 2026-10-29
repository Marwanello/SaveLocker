using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// A playthrough made on another device (or in the console) reaches this one through an ordinary push or pull, against a real
/// server. Before, only reading the playthrough list or switching updated a device's count, so a device that had done neither
/// stayed on 1 for good — and the Deck shows its switch only above 1, so it could never show it.
/// </summary>
public sealed class PlaythroughCountSyncTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-ptcount-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Random.Shared.Next(0x100000, 0xFFFFFF).ToString("X6");

    public PlaythroughCountSyncTests(ServerProcess server)
    {
        _server = server;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<AgentConfig> Machine(string name, Guid? gameId = null, string? save = null)
    {
        var config = AgentConfig.Load(Path.Combine(_dir, name, "state", "config.json"));
        config.ServerUrl = _server.Url;
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync(name + _id);
        (config.ApiKey, config.MachineId, config.MachineName) = (reg.ApiKey, reg.MachineId, name);
        if (gameId is { } id)
        {
            var dir = Path.Combine(_dir, name, "save");
            Directory.CreateDirectory(dir);
            if (save is not null) File.WriteAllText(Path.Combine(dir, "slot1.sav"), save);
            config.Games.Add(new TrackedGame { GameId = id, Name = "Count Quest " + _id, SaveDirectory = dir });
        }
        config.Save();
        return config;
    }

    /// <summary>The PC pushes the game's first save; then the Deck makes a second playthrough of it.</summary>
    private async Task<(AgentConfig Pc, Guid GameId)> SecondPlaythroughMadeElsewhere()
    {
        var game = await ApiClient.For(await Machine("enroller")).CreateGameAsync(new CreateGameRequest("Count Quest " + _id, null, null));
        var pc = await Machine("pc", game.Id, "pc-progress");
        await using (var engine = new SyncEngine(pc, ApiClient.For(pc)))
            Assert.Equal(UploadStatus.Created, (await engine.PushAsync(pc.Games.Single()))!.Status);
        Assert.Equal(1, pc.Games.Single().PlaythroughCount);

        var deck = await Machine("deck", game.Id);
        var (made, error) = await ApiClient.For(deck).CreatePlaythroughAsync(game.Id, new CreatePlaythroughRequest("Hard mode"));
        Assert.True(made is not null, error);
        return (pc, game.Id);
    }

    [Fact]
    public async Task A_push_learns_of_a_playthrough_made_on_another_device()
    {
        var (pc, _) = await SecondPlaythroughMadeElsewhere();
        var game = pc.Games.Single();
        Assert.Null(game.PlaythroughId);   // never switched, never read the list
        File.WriteAllText(Path.Combine(game.SaveDirectory, "slot1.sav"), "pc-progress-2");

        await using (var engine = new SyncEngine(pc, ApiClient.For(pc)))
            Assert.Equal(UploadStatus.Created, (await engine.PushAsync(game))!.Status);

        Assert.Equal(2, game.PlaythroughCount);
        Assert.Equal(2, AgentConfig.Load(pc.ConfigPath).Games.Single().PlaythroughCount);   // saved, not just in memory
    }

    [Fact]
    public async Task A_pull_learns_of_a_playthrough_made_on_another_device()
    {
        var (_, gameId) = await SecondPlaythroughMadeElsewhere();
        var laptop = await Machine("laptop", gameId);
        var game = laptop.Games.Single();
        Assert.Equal(1, game.PlaythroughCount);

        await using (var engine = new SyncEngine(laptop, ApiClient.For(laptop)))
            Assert.Equal(PullOutcome.Restored, await engine.PullAsync(game));

        Assert.Equal(2, game.PlaythroughCount);
        Assert.Equal(2, AgentConfig.Load(laptop.ConfigPath).Games.Single().PlaythroughCount);
    }
}
