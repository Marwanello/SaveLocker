using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Group C of multiple save paths: the steps behind the agent UI's "Add save folder", the "Also found"
/// prompt and `add-path`/`remove-path`, against a real server — one implementation for all of them.
/// </summary>
public sealed class SavePathEditorTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-editor-" + Guid.NewGuid().ToString("N"));

    public SavePathEditorTests(ServerProcess server)
    {
        _server = server;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Folder(string name, string? file = "f.dat")
    {
        var d = Path.Combine(_dir, name);
        Directory.CreateDirectory(d);
        if (file is not null) File.WriteAllText(Path.Combine(d, file), name);
        return d;
    }

    private async Task<(AgentConfig Config, TrackedGame Game, SyncEngine Engine)> Enrolled(string game)
    {
        var config = AgentConfig.Load(Path.Combine(_dir, "agent-" + game, "config.json"));
        config.ServerUrl = _server.Url;
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync("m-" + game);
        (config.ApiKey, config.MachineId, config.MachineName) = (reg.ApiKey, reg.MachineId, "m-" + game);
        config.Save();
        await Enroller.EnrollAsync(config, new[] { new ScanCandidate(game, Folder(game + "-main"), ScanSource.SaveRoot, false) }, new[] { 0 });
        return (config, config.FindGame(game)!, new SyncEngine(config, ApiClient.For(config)));
    }

    private static Func<TrackedGame, string, string, KeepSide?, Task<MapFolderResult>> Map(SyncEngine e) =>
        (g, k, d, keep) => e.MapSavePathAsync(g, k, d, keep);

    [Fact]
    public async Task A_folder_added_here_is_defined_for_the_fleet_and_mapped_here()
    {
        var name = "Edit-" + Guid.NewGuid().ToString("N")[..8];
        var (config, game, engine) = await Enrolled(name);
        var states = Folder(name + "-states");

        var result = await SavePathEditor.AddAsync(config, ApiClient.For(config), Map(engine), game, "states", states,
            include: new[] { "*.state" });

        Assert.True(result.Ok, result.Error);
        Assert.True(result.Added);
        Assert.True(result.Reported);
        var tracked = AgentConfig.Load(config.ConfigPath).FindGame(name)!;
        Assert.Equal(states, Assert.Single(tracked.ExtraPaths).Directory);
        var server = Assert.Single((await ApiClient.For(config).ListGamesAsync()).Single(g => g.Name == name).ExtraPaths!);
        Assert.Equal("states", server.Key);
        Assert.Equal(new[] { "*.state" }, server.IncludeGlobs);
        Assert.Equal(states, server.MachinePath);
    }

    [Fact]
    public async Task A_suggested_key_the_game_had_before_moves_to_the_next_free_one()
    {
        var name = "Retired-" + Guid.NewGuid().ToString("N")[..8];
        var (config, game, engine) = await Enrolled(name);
        var api = ApiClient.For(config);
        Assert.True((await SavePathEditor.AddAsync(config, api, Map(engine), game, "config", Folder(name + "-a"))).Ok);
        Assert.True((await SavePathEditor.RemoveAsync(config, api, (g, k) => engine.ForgetSavePathAsync(g, k), game, "config")).Ok);

        var named = await SavePathEditor.AddAsync(config, api, Map(engine), game, "config", Folder(name + "-b"));
        var suggested = await SavePathEditor.AddAsync(config, api, Map(engine), game, "config", Folder(name + "-b"), pickFreeKey: true);

        Assert.False(named.Ok);
        Assert.Contains("before", named.Error);
        Assert.True(suggested.Ok, suggested.Error);
        Assert.Equal("config-2", suggested.Key);
    }

    [Fact]
    public async Task Refusals_leave_nothing_behind_on_the_server()
    {
        var name = "Refuse-" + Guid.NewGuid().ToString("N")[..8];
        var (config, game, engine) = await Enrolled(name);
        var api = ApiClient.For(config);

        var nested = await SavePathEditor.AddAsync(config, api, Map(engine), game, "inner",
            Folder(Path.Combine(name + "-main", "inner")));
        var badKey = await SavePathEditor.AddAsync(config, api, Map(engine), game, "Not A Key", Folder(name + "-x"));

        Assert.False(nested.Ok);
        Assert.False(nested.Added);
        Assert.False(badKey.Ok);
        Assert.Empty((await api.ListGamesAsync()).Single(g => g.Name == name).ExtraPaths ?? []);
    }

    [Fact]
    public async Task Removing_a_folder_drops_it_everywhere_and_leaves_its_files()
    {
        var name = "Remove-" + Guid.NewGuid().ToString("N")[..8];
        var (config, game, engine) = await Enrolled(name);
        var api = ApiClient.For(config);
        var dir = Folder(name + "-extra");
        await SavePathEditor.AddAsync(config, api, Map(engine), game, "extra", dir);

        var removed = await SavePathEditor.RemoveAsync(config, api, (g, k) => engine.ForgetSavePathAsync(g, k), game, "extra");

        Assert.True(removed.Ok, removed.Error);
        Assert.False(removed.Deferred);
        Assert.Empty(AgentConfig.Load(config.ConfigPath).FindGame(name)!.ExtraPaths);
        Assert.Empty((await api.ListGamesAsync()).Single(g => g.Name == name).ExtraPaths ?? []);
        Assert.True(File.Exists(Path.Combine(dir, "f.dat")));
    }
}
