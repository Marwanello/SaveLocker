using System.Diagnostics;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;
using F = SaveLocker.Agent.Tests.ConsoleSaveFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Keeping the cloud's save in a conflict this machine is party to, against a real server. The maintainer's PC
/// (2026-10-11): its first sync of Warrior Within, which the Deck already had, conflicted; "Cloud" told the server,
/// the guarded pull the server queued was refused (the save here was never synced), the next sync pushed it again,
/// and after three rounds uploads paused behind a conflict the server no longer had.
/// </summary>
public sealed class ConflictResolveTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-resolve-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Random.Shared.Next(10000, 99999).ToString();

    public ConflictResolveTests(ServerProcess server)
    {
        _server = server;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Save => $"BASLUS-{_id}SYS/data";
    private string P(string machine, string rel) => Path.Combine(_dir, machine, rel.Replace('/', Path.DirectorySeparatorChar));

    private static void Text(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string machine) => File.ReadAllText(P(machine, $"mc/Card.ps2/{Save}"));

    private async Task<AgentConfig> Machine(string name, string content)
    {
        var config = AgentConfig.Load(Path.Combine(_dir, name, "state", "config.json"));
        config.ServerUrl = _server.Url;
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync(name + _id);
        (config.ApiKey, config.MachineId, config.MachineName) = (reg.ApiKey, reg.MachineId, name);
        config.Save();
        Text(P(name, "mc/Card.ps2/_pcsx2_superblock"), "superblock");
        F.Write(P(name, $"mc/Card.ps2/BASLUS-{_id}SYS/icon.sys"), F.IconSys($"Game {_id}"));
        Text(P(name, $"mc/Card.ps2/{Save}"), content);
        var found = Pcsx2Saves.Scan(new[] { new RomSaveFolders(P(name, "mc"), null) });
        Assert.Equal((1, 0), await Enroller.EnrollAsync(config, found, new[] { 0 }));
        return AgentConfig.Load(config.ConfigPath);
    }

    /// <summary>The Deck has pushed its save; the PC's first push of its own conflicts.</summary>
    private async Task<(SyncEngine Pc, TrackedGame Game, ConflictDto Conflict)> ConflictOnThePc(AgentConfig deck, AgentConfig pc)
    {
        await using var deckEngine = new SyncEngine(deck, ApiClient.For(deck));
        Assert.Equal(UploadStatus.Created, (await deckEngine.PushAsync(deck.Games.Single()))!.Status);
        var engine = new SyncEngine(pc, ApiClient.For(pc));
        var game = pc.Games.Single();
        Assert.Equal(UploadStatus.Conflict, (await engine.PushAsync(game))!.Status);
        var conflict = Assert.Single(await ApiClient.For(pc).GetOpenConflictsForMachineAsync(pc.MachineId),
            c => c.GameId == game.GameId);
        return (engine, game, conflict);
    }

    [Fact]
    public async Task Keeping_the_clouds_save_restores_it_here_and_the_next_sync_has_nothing_to_send()
    {
        var deck = await Machine("deck", "deck-progress");
        var pc = await Machine("pc", "pc-progress");
        var (engine, game, conflict) = await ConflictOnThePc(deck, pc);
        await using var _ = engine;

        var (ok, message) = await engine.ResolveOwnConflictAsync(conflict, conflict.VersionAId, keepBoth: false);

        Assert.True(ok, message);
        Assert.Contains("restored on this device", message);
        Assert.Equal("deck-progress", Read("pc"));
        Assert.Equal(0, game.ConsecutiveConflicts);
        Assert.Equal(UploadStatus.NoChange, (await engine.PushAsync(game))!.Status);
        Assert.Empty(await ApiClient.For(pc).GetOpenConflictsForMachineAsync(pc.MachineId));
    }

    [Fact]
    public async Task A_refused_resolve_says_why()
    {
        var deck = await Machine("deck", "deck-progress");
        var pc = await Machine("pc", "pc-progress");
        var (engine, _, conflict) = await ConflictOnThePc(deck, pc);
        await using var _ = engine;
        // The Deck moves Latest on past both sides (testenv, 2026-10-13): the server refuses with a plain-string body,
        // which the agent UI used to show as "Server returned 400 Bad Request."
        Text(P("deck", $"mc/Card.ps2/{Save}"), "deck-more-progress");
        await using (var deckEngine = new SyncEngine(deck, ApiClient.For(deck)))
            Assert.Equal(UploadStatus.Created, (await deckEngine.PushAsync(deck.Games.Single()))!.Status);

        var (ok, message) = await engine.ResolveOwnConflictAsync(conflict, conflict.VersionBId, keepBoth: false);

        Assert.False(ok);
        Assert.StartsWith("Latest changed after this conflict opened", message);
    }

    [Fact]
    public async Task Keeping_the_clouds_save_while_the_game_runs_changes_nothing_anywhere()
    {
        var deck = await Machine("deck", "deck-progress");
        var pc = await Machine("pc", "pc-progress");
        var (engine, game, conflict) = await ConflictOnThePc(deck, pc);
        await using var _ = engine;
        game.ProcessNames = [Process.GetCurrentProcess().ProcessName];

        var (ok, message) = await engine.ResolveOwnConflictAsync(conflict, conflict.VersionAId, keepBoth: false);

        Assert.False(ok);
        Assert.Contains("Close it, then choose again", message);
        Assert.Equal("pc-progress", Read("pc"));
        // Still open on the server, so the choice is made again once the game is closed — not recorded there alone.
        Assert.Contains(await ApiClient.For(pc).GetOpenConflictsForMachineAsync(pc.MachineId), c => c.Id == conflict.Id);
    }

    [Fact]
    public async Task A_save_changed_after_the_conflict_is_not_replaced()
    {
        var deck = await Machine("deck", "deck-progress");
        var pc = await Machine("pc", "pc-progress");
        var (engine, _, conflict) = await ConflictOnThePc(deck, pc);
        await using var _ = engine;
        File.WriteAllText(P("pc", $"mc/Card.ps2/{Save}"), "pc-played-on");

        var (ok, message) = await engine.ResolveOwnConflictAsync(conflict, conflict.VersionAId, keepBoth: false);

        Assert.True(ok, message);
        Assert.Contains("changed after the conflict", message);
        Assert.Equal("pc-played-on", Read("pc"));
    }

    [Fact]
    public async Task Keeping_this_devices_save_makes_its_next_push_no_conflict()
    {
        var deck = await Machine("deck", "deck-progress");
        var pc = await Machine("pc", "pc-progress");
        var (engine, game, conflict) = await ConflictOnThePc(deck, pc);
        await using var _ = engine;

        var (ok, message) = await engine.ResolveOwnConflictAsync(conflict, conflict.VersionBId, keepBoth: false);

        Assert.True(ok, message);
        Assert.Equal("pc-progress", Read("pc"));
        File.WriteAllText(P("pc", $"mc/Card.ps2/{Save}"), "pc-next");
        Assert.Equal(UploadStatus.Created, (await engine.PushAsync(game))!.Status);
    }

    [Fact]
    public async Task Uploads_paused_behind_a_conflict_resolved_elsewhere_resume()
    {
        var deck = await Machine("deck", "deck-progress");
        var pc = await Machine("pc", "pc-progress");
        var (engine, game, conflict) = await ConflictOnThePc(deck, pc);
        await using var _ = engine;
        while (game.ConsecutiveConflicts < 3) await engine.PushAsync(game);
        // Resolved by someone else (the console), and the guarded pull it queued refused here.
        Assert.True((await ApiClient.For(pc).ResolveConflictAsync(conflict.Id, conflict.VersionAId)).ok);
        Assert.Equal(PullOutcome.Refused, await engine.PullAsync(game));

        // The push is sent again, so the server asks again: a new conflict a person can see and settle.
        Assert.Equal(UploadStatus.Conflict, (await engine.PushAsync(game))!.Status);
        Assert.Equal(1, game.ConsecutiveConflicts);
        Assert.Single(await ApiClient.For(pc).GetOpenConflictsForMachineAsync(pc.MachineId), c => c.GameId == game.GameId);
    }
}
