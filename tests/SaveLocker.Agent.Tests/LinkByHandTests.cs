using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Linking by hand (tasks/emulator-saves Phase 16): which server game an emulator save joins when the user
/// changes it on Add games, against a real server — and the options the row offers.
/// </summary>
public sealed class LinkByHandTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-link-" + Guid.NewGuid().ToString("N"));
    private readonly string _rom = "Daytona " + Guid.NewGuid().ToString("N")[..6];

    public LinkByHandTests(ServerProcess server)
    {
        _server = server;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private async Task<AgentConfig> Machine(string name)
    {
        var config = AgentConfig.Load(Path.Combine(_dir, name, "state", "config.json"));
        config.ServerUrl = _server.Url;
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync(name + Guid.NewGuid().ToString("N")[..6]);
        config.ApiKey = reg.ApiKey;
        config.MachineId = reg.MachineId;
        config.MachineName = name;
        config.Save();
        return config;
    }

    private ScanCandidate Scan(string machine, string rom)
    {
        var emulation = Path.Combine(_dir, machine, "Emulation");
        var saves = Path.Combine(emulation, "saves", "retroarch", "saves");
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(saves, rom + ".srm"), machine + "-save");
        return RetroArchSaves.Scan(RetroArchConfig.Folders(new[] { emulation }, Array.Empty<string>()), new[] { emulation })
            .Single(c => c.EmulatorRom == rom);
    }

    private static Dictionary<int, LinkChoice> Pick(string choice, Guid? game = null) => new() { [0] = new LinkChoice(choice, game) };

    [Fact]
    public async Task Kept_as_its_own_game_never_joins_the_one_with_the_same_files()
    {
        var deck = await Machine("deck");
        Assert.Equal((1, 0), await Enroller.EnrollAsync(deck, new[] { Scan("deck", _rom) }, new[] { 0 }));
        var deckGame = Assert.Single(AgentConfig.Load(deck.ConfigPath).Games);

        var pc = await Machine("pc");
        var save = Scan("pc", _rom);
        var options = EnrollLinks.For(await ApiClient.For(pc).ListGamesAsync(), save);
        Assert.Equal((EnrollLinks.Auto, LinkKind.Join, deckGame.GameId), (options[0].Choice, options[0].Kind, options[0].GameId));
        var own = Assert.Single(options, o => o.Choice == EnrollLinks.Separate);
        Assert.Equal($"{_rom} (RetroArch)", own.Name);

        Assert.Equal((1, 0), await Enroller.EnrollAsync(pc, new[] { save }, new[] { 0 }, links: Pick(EnrollLinks.Separate)));
        var pcGame = Enroller.TrackedFor(AgentConfig.Load(pc.ConfigPath), save)!;
        Assert.NotEqual(deckGame.GameId, pcGame.GameId);
        Assert.Equal(own.Name, pcGame.Name);
        Assert.True(pcGame.IsEnrolledHere);
    }

    [Fact]
    public async Task The_game_set_up_here_wins_over_one_only_known_from_the_server()
    {
        var deck = await Machine("deck");
        Assert.Equal((1, 0), await Enroller.EnrollAsync(deck, new[] { Scan("deck", _rom) }, new[] { 0 }));
        var fleetGame = Assert.Single(AgentConfig.Load(deck.ConfigPath).Games);
        var pc = await Machine("pc");
        var save = Scan("pc", _rom);
        Assert.Equal((1, 0), await Enroller.EnrollAsync(pc, new[] { save }, new[] { 0 }, links: Pick(EnrollLinks.Separate)));
        var config = AgentConfig.Load(pc.ConfigPath);
        // The fleet's game of the same files, adopted here with no folder — listed first by name.
        config.Games.Insert(0, new TrackedGame
        {
            GameId = fleetGame.GameId, Name = fleetGame.Name, SaveDirectory = "",
            IncludeGlobs = save.IncludeGlobs!.ToList(),
            ExtraPaths = save.ExtraSaveDirs!.Select(e => new TrackedSavePath { Key = e.Key, IncludeGlobs = e.IncludeGlobs!.ToList() }).ToList(),
        });
        Assert.Equal($"{_rom} (RetroArch)", Enroller.TrackedFor(config, save)!.Name);
    }

    [Fact]
    public async Task A_picked_game_is_joined_and_one_with_other_files_is_refused()
    {
        var deck = await Machine("deck");
        Assert.Equal((1, 0), await Enroller.EnrollAsync(deck, new[] { Scan("deck", _rom) }, new[] { 0 }));
        var deckGame = Assert.Single(AgentConfig.Load(deck.ConfigPath).Games);
        Assert.Equal((1, 0), await Enroller.EnrollAsync(deck, new[] { Scan("deck", _rom + " (Rev 1)") }, new[] { 0 }));
        var rev1 = AgentConfig.Load(deck.ConfigPath).Games.Single(g => g.GameId != deckGame.GameId);

        var pc = await Machine("pc");
        var save = Scan("pc", _rom);
        // Another dump of the same title keeps other files: listed, never pickable.
        var blocked = Assert.Single(EnrollLinks.For(await ApiClient.For(pc).ListGamesAsync(), save), o => o.GameId == rev1.GameId);
        Assert.Equal((LinkKind.Blocked, "Different file names"), (blocked.Kind, blocked.Badge));
        Assert.Equal((0, 1), await Enroller.EnrollAsync(pc, new[] { save }, new[] { 0 }, links: Pick(EnrollLinks.Game, rev1.GameId)));
        Assert.Contains("keeps different save files", EnrollLinks.Resolve(await ApiClient.For(pc).ListGamesAsync(), save,
            save.ExtraSaveDirs!, new LinkChoice(EnrollLinks.Game, rev1.GameId)).Refusal);
        Assert.Empty(AgentConfig.Load(pc.ConfigPath).Games.Where(g => g.IsEnrolledHere));

        Assert.Equal((1, 0), await Enroller.EnrollAsync(pc, new[] { save }, new[] { 0 }, links: Pick(EnrollLinks.Game, deckGame.GameId)));
        Assert.Equal(deckGame.GameId, Enroller.TrackedFor(AgentConfig.Load(pc.ConfigPath), save)!.GameId);
    }

    [Fact]
    public async Task A_game_gone_from_the_server_since_the_list_was_read_is_refused()
    {
        var pc = await Machine("pc");
        var save = Scan("pc", _rom);
        var gone = Guid.NewGuid();
        Assert.Equal((0, 1), await Enroller.EnrollAsync(pc, new[] { save }, new[] { 0 }, links: Pick(EnrollLinks.Game, gone)));
        Assert.Contains("no longer on the server", EnrollLinks.Resolve(await ApiClient.For(pc).ListGamesAsync(), save,
            save.ExtraSaveDirs!, new LinkChoice(EnrollLinks.Game, gone)).Refusal);
    }

    [Fact]
    public void Options_a_new_game_offers_and_what_another_title_keeps()
    {
        var save = Scan("x", _rom);
        GameDto Game(string name, string[]? scope) => new(Guid.NewGuid(), name, null, null, true, IncludeGlobs: scope);

        // Nothing like it on the server: one option, a new game, and no "keep it as its own game".
        var alone = EnrollLinks.For(new[] { Game("Something else", new[] { "x.srm" }) }, save);
        Assert.Equal((EnrollLinks.Auto, LinkKind.New, _rom), (Assert.Single(alone).Choice, alone[0].Kind, alone[0].Name));

        // A PC game of the same title: the save is a new game beside it, and the PC game is shown, not pickable.
        var pcGame = Game(_rom, null);
        var beside = EnrollLinks.For(new[] { pcGame }, save);
        Assert.Equal((LinkKind.New, $"{_rom} (RetroArch)"), (beside[0].Kind, beside[0].Name));
        Assert.Equal((LinkKind.Blocked, "Whole folder", pcGame.Id), (beside[1].Kind, beside[1].Badge, beside[1].GameId));

        // A PC game is never an emulator candidate's to offer.
        Assert.Empty(EnrollLinks.For(new[] { pcGame }, new ScanCandidate(_rom, "/x", ScanSource.SteamInstalled, false)));
    }
}
