using SaveLocker.Agent;
using Xunit;
using F = SaveLocker.Agent.Tests.ConsoleSaveFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Group I end to end against a real server (tasks/emulator-saves Phase 19), one test per save shape: a game played from
/// states alone on one machine joins the same game on another, its states travel, its first in-game save follows — and a
/// machine that pushed only states never empties another machine's real save of the game.
/// </summary>
public sealed class GroupISyncTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-groupi-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = F.UnknownSerial("ps2", "SLUS-");

    public GroupISyncTests(ServerProcess server)
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
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync(name + _id);
        config.ApiKey = reg.ApiKey;
        config.MachineId = reg.MachineId;
        config.MachineName = name;
        config.Save();
        return config;
    }

    private string P(string machine, string rel) => Path.Combine(_dir, machine, rel.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string machine, string rel, string content)
    {
        var path = P(machine, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string machine, string rel) => File.ReadAllText(P(machine, rel));

    private static async Task<TrackedGame> Enroll(AgentConfig config, IReadOnlyList<ScanCandidate> found, string rom)
    {
        var id = found.ToList().FindIndex(c => c.EmulatorRom == rom);
        Assert.True(id >= 0, $"no candidate for {rom}");
        Assert.Equal((1, 0), await Enroller.EnrollAsync(config, found, new[] { id }));
        return Enroller.TrackedFor(AgentConfig.Load(config.ConfigPath), found[id])!;
    }

    private IReadOnlyList<ScanCandidate> RetroArch(string machine) =>
        RetroArchSaves.Scan(RetroArchConfig.Folders([P(machine, "Emulation")], []), [P(machine, "Emulation")]);

    [Fact]
    public async Task A_retroarch_game_with_only_states_joins_and_its_first_save_follows()
    {
        var rom = $"Game {_id} (USA)";
        Directory.CreateDirectory(P("deck", "Emulation/saves/retroarch/saves"));
        Write("deck", $"Emulation/saves/retroarch/states/{rom}.state1", "deck-state");
        // Windows has an older save of the game, and a neighbour's.
        Write("win", $"Emulation/saves/retroarch/saves/Neighbour {_id}.srm", "win-neighbour");
        Directory.CreateDirectory(P("win", "Emulation/saves/retroarch/states"));

        var deck = await Machine("deck");
        var win = await Machine("win");
        var deckFound = RetroArch("deck");
        Assert.True(Assert.Single(deckFound).StatesOnly);
        var deckGame = await Enroll(deck, deckFound, rom);
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));

        // Windows has states of it too now, so it lists the game by them and joins the Deck's.
        Write("win", $"Emulation/saves/retroarch/states/{rom}.state", "win-state");
        var winGame = await Enroll(win, RetroArch("win"), rom);
        Assert.Equal(deckGame.GameId, winGame.GameId);
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        await engineWin.PullAsync(winGame, force: true);
        Assert.Equal("deck-state", Read("win", $"Emulation/saves/retroarch/states/{rom}.state1"));

        Write("deck", $"Emulation/saves/retroarch/saves/{rom}.srm", "first-save");
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));
        await engineWin.PullAsync(winGame, force: true);
        Assert.Equal("first-save", Read("win", $"Emulation/saves/retroarch/saves/{rom}.srm"));
        Assert.Equal("win-neighbour", Read("win", $"Emulation/saves/retroarch/saves/Neighbour {_id}.srm"));
    }

    [Fact]
    public async Task A_retroarch_states_only_push_never_empties_another_machines_real_save()
    {
        var rom = $"Game {_id} (USA)";
        Directory.CreateDirectory(P("deck", "Emulation/saves/retroarch/saves"));
        Write("deck", $"Emulation/saves/retroarch/states/{rom}.state1", "deck-state");
        Write("win", $"Emulation/saves/retroarch/saves/{rom}.srm", "win-real-save");
        Directory.CreateDirectory(P("win", "Emulation/saves/retroarch/states"));

        var deck = await Machine("deck");
        var win = await Machine("win");
        var deckGame = await Enroll(deck, RetroArch("deck"), rom);
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));

        var winGame = await Enroll(win, RetroArch("win"), rom);
        Assert.Equal(deckGame.GameId, winGame.GameId);
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        Assert.Equal(PullOutcome.Refused, await engineWin.PullAsync(winGame));
        Assert.Equal("win-real-save", Read("win", $"Emulation/saves/retroarch/saves/{rom}.srm"));
    }

    [Fact]
    public async Task A_ps2_game_with_only_states_joins_the_game_on_another_machines_card()
    {
        var serial = $"SLUS-{_id}";
        var save = $"BA{serial}";
        Write("deck", "Emulation/saves/pcsx2/saves/Mcd001.ps2/_pcsx2_superblock", "Sony PS2 Memory Card Format (deck)");
        Write("deck", $"Emulation/saves/pcsx2/states/{serial} (ABCD1234).01.p2s", "deck-state");
        Write("win", "pcsx2/memcards/MC1.ps2/_pcsx2_superblock", "Sony PS2 Memory Card Format (win)");
        Write("win", $"pcsx2/memcards/MC1.ps2/{save}SYS/data", "win-save");
        Directory.CreateDirectory(P("win", "pcsx2/sstates"));

        var win = await Machine("win");
        var deck = await Machine("deck");
        var winGame = await Enroll(win, Pcsx2Saves.Scan([new RomSaveFolders(P("win", "pcsx2/memcards"), P("win", "pcsx2/sstates"))]), save);
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        Assert.NotNull(await engineWin.PushAsync(winGame, force: true));

        var deckFound = Pcsx2Saves.Scan(Pcsx2Saves.Folders([P("deck", "Emulation")], []));
        Assert.True(Assert.Single(deckFound).StatesOnly);
        var deckGame = await Enroll(deck, deckFound, save);
        Assert.Equal(winGame.GameId, deckGame.GameId);
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        await engineDeck.PullAsync(deckGame, force: true);

        Assert.Equal("win-save", Read("deck", $"Emulation/saves/pcsx2/saves/Mcd001.ps2/{save}SYS/data"));
        Assert.Equal("Sony PS2 Memory Card Format (deck)", Read("deck", "Emulation/saves/pcsx2/saves/Mcd001.ps2/_pcsx2_superblock"));
    }

    [Fact]
    public async Task A_wii_game_with_only_states_joins_and_its_first_save_follows()
    {
        var code = "R" + _id[..3];
        var hex = Convert.ToHexStringLower(System.Text.Encoding.ASCII.GetBytes(code));
        Directory.CreateDirectory(P("deck", "dolphin/Wii/title"));
        Write("deck", $"dolphin/StateSaves/{code}01.s01", "deck-state");
        Directory.CreateDirectory(P("win", "dolphin/Wii/title"));
        Write("win", $"dolphin/StateSaves/{code}01.s03", "win-state");

        IReadOnlyList<ScanCandidate> Scan(string m) =>
            DolphinSaves.Scan([new DolphinFolders(DolphinSaves.EmulatorName, null, P(m, "dolphin/Wii"), P(m, "dolphin/StateSaves"))]);

        var deck = await Machine("deck");
        var win = await Machine("win");
        var deckGame = await Enroll(deck, Scan("deck"), code);
        var winGame = await Enroll(win, Scan("win"), code);
        Assert.Equal(deckGame.GameId, winGame.GameId);

        F.Write(P("deck", $"dolphin/Wii/title/00010000/{hex}/data/banner.bin"), F.Banner("Some Game"));
        Write("deck", $"dolphin/Wii/title/00010000/{hex}/data/save.dat", "first-save");
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));
        await engineWin.PullAsync(winGame, force: true);

        Assert.Equal("first-save", Read("win", $"dolphin/Wii/title/00010000/{hex}/data/save.dat"));
        Assert.Equal("deck-state", Read("win", $"dolphin/StateSaves/{code}01.s01"));
    }

    [Fact]
    public async Task A_3ds_game_with_only_states_joins_and_its_first_save_follows()
    {
        const string zeros = "00000000000000000000000000000000";
        var low = "0" + _id[..5] + "00";
        var programId = ("00040000" + low).ToUpperInvariant();
        var title = $"azahar/sdmc/Nintendo 3DS/{zeros}/{zeros}/title/00040000";
        foreach (var m in new[] { "deck", "win" })
            Directory.CreateDirectory(P(m, title));
        Write("deck", $"azahar/states/{programId}.01.cst", "deck-state");

        IReadOnlyList<ScanCandidate> Scan(string m) => AzaharSaves.Scan([new AzaharFolders(P(m, "azahar/sdmc"), P(m, "azahar/states"), [])]);

        var deck = await Machine("deck");
        var win = await Machine("win");
        var deckGame = await Enroll(deck, Scan("deck"), programId);
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));

        Write("win", $"azahar/states/{programId}.02.cst", "win-state");
        var winGame = await Enroll(win, Scan("win"), programId);
        Assert.Equal(deckGame.GameId, winGame.GameId);

        Write("deck", $"{title}/{low}/data/00000001/system", "first-save");
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        await engineWin.PullAsync(winGame, force: true);
        Assert.Equal("first-save", Read("win", $"{title}/{low}/data/00000001/system"));
        Assert.Equal("deck-state", Read("win", $"azahar/states/{programId}.01.cst"));
    }
}
