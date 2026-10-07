using SaveLocker.Agent;
using Xunit;
using F = SaveLocker.Agent.Tests.ConsoleSaveFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The Group C emulators end to end against a real server (tasks/emulator-saves Phase 2): one game's saves on a
/// PCSX2 folder card, a DuckStation per-game card and a Dolphin Wii title folder travel between two machines with
/// their states — and the other games sharing the card or folder, and the card's own superblock, never move.
/// </summary>
public sealed class GroupCSyncTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-groupc-" + Guid.NewGuid().ToString("N"));
    // The class shares one server, so each test's game is its own.
    private readonly string _id = Random.Shared.Next(10000, 99999).ToString();

    public GroupCSyncTests(ServerProcess server)
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

    [Fact]
    public async Task A_ps2_game_on_a_folder_card_moves_alone_with_its_states()
    {
        var serial = $"SLUS-{_id}";
        var save = $"BA{serial}";
        // Windows: the game's two save folders, another game, and a state with its backup copy.
        Write("win", "pcsx2/memcards/Mcd001.ps2/_pcsx2_superblock", "win-superblock");
        F.Write(P("win", $"pcsx2/memcards/Mcd001.ps2/{save}SYS/icon.sys"), F.IconSys($"Game {_id}"));
        Write("win", $"pcsx2/memcards/Mcd001.ps2/{save}SYS/_pcsx2_index", "win-index");
        Write("win", $"pcsx2/memcards/Mcd001.ps2/{save}S01/data", "win-slot1");
        Write("win", "pcsx2/memcards/Mcd001.ps2/BESLES-50330GTA3/save", "win-gta");
        Write("win", $"pcsx2/sstates/{serial} (ABCD1234).01.p2s", "win-state");
        Write("win", $"pcsx2/sstates/{serial} (ABCD1234).01.p2s.backup", "win-backup");
        // Deck: an older save of the same game, its own card superblock and another game.
        Write("deck", "Emulation/saves/pcsx2/saves/Mcd001.ps2/_pcsx2_superblock", "deck-superblock");
        F.Write(P("deck", $"Emulation/saves/pcsx2/saves/Mcd001.ps2/{save}SYS/icon.sys"), F.IconSys($"Game {_id}"));
        Write("deck", $"Emulation/saves/pcsx2/saves/Mcd001.ps2/{save}SYS/_pcsx2_index", "deck-old");
        Write("deck", "Emulation/saves/pcsx2/saves/Mcd001.ps2/BASLUS-20000OTHER/save", "deck-other");
        Directory.CreateDirectory(P("deck", "Emulation/saves/pcsx2/states"));

        var win = await Machine("win");
        var deck = await Machine("deck");
        var winGame = await Enroll(win, Pcsx2Saves.Scan(new[] { new RomSaveFolders(P("win", "pcsx2/memcards"), P("win", "pcsx2/sstates")) }), save);
        var deckGame = await Enroll(deck, Pcsx2Saves.Scan(Pcsx2Saves.Folders(new[] { P("deck", "Emulation") }, [])), save);
        Assert.Equal(winGame.GameId, deckGame.GameId);
        Assert.Equal($"Game {_id}", deckGame.Name);

        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await engineWin.PushAsync(winGame, force: true));
        await engineDeck.PullAsync(deckGame, force: true);

        Assert.Equal("win-index", Read("deck", $"Emulation/saves/pcsx2/saves/Mcd001.ps2/{save}SYS/_pcsx2_index"));
        Assert.Equal("win-slot1", Read("deck", $"Emulation/saves/pcsx2/saves/Mcd001.ps2/{save}S01/data"));
        Assert.Equal("win-state", Read("deck", $"Emulation/saves/pcsx2/states/{serial} (ABCD1234).01.p2s"));
        Assert.False(File.Exists(P("deck", $"Emulation/saves/pcsx2/states/{serial} (ABCD1234).01.p2s.backup")));
        // The rest of the Deck's card is its own.
        Assert.Equal("deck-superblock", Read("deck", "Emulation/saves/pcsx2/saves/Mcd001.ps2/_pcsx2_superblock"));
        Assert.Equal("deck-other", Read("deck", "Emulation/saves/pcsx2/saves/Mcd001.ps2/BASLUS-20000OTHER/save"));
        Assert.False(Directory.Exists(P("deck", "Emulation/saves/pcsx2/saves/Mcd001.ps2/BESLES-50330GTA3")));
    }

    [Fact]
    public async Task A_ps1_game_card_and_its_states_travel_and_the_neighbour_cards_stay()
    {
        var title = $"Crash {_id}";
        var code = $"SCUS-{_id}";
        F.Write(P("a", $"ds/memcards/{title}_1.mcd"), F.Ps1Card($"BA{code}CRASH"));
        Write("a", $"ds/savestates/{code}_1.sav", "a-state1");
        Write("a", $"ds/savestates/{code}_resume.sav", "a-resume");
        F.Write(P("b", $"ds/memcards/{title}_1.mcd"), F.Ps1Card($"BA{code}OLD"));
        F.Write(P("b", $"ds/memcards/{title} Team_1.mcd"), F.Ps1Card("BASCUS-94426CTR"));
        Write("b", "ds/savestates/SCUS-94426_1.sav", "b-ctr-state");

        var a = await Machine("a");
        var b = await Machine("b");
        var gameA = await Enroll(a, DuckStationSaves.Scan(new[] { new RomSaveFolders(P("a", "ds/memcards"), P("a", "ds/savestates")) }), title);
        var gameB = await Enroll(b, DuckStationSaves.Scan(new[] { new RomSaveFolders(P("b", "ds/memcards"), P("b", "ds/savestates")) }), title);
        Assert.Equal(gameA.GameId, gameB.GameId);

        await using var engineA = new SyncEngine(a, ApiClient.For(a));
        await using var engineB = new SyncEngine(b, ApiClient.For(b));
        Assert.NotNull(await engineA.PushAsync(gameA, force: true));
        await engineB.PullAsync(gameB, force: true);

        Assert.Equal(File.ReadAllBytes(P("a", $"ds/memcards/{title}_1.mcd")), File.ReadAllBytes(P("b", $"ds/memcards/{title}_1.mcd")));
        Assert.Equal("a-state1", Read("b", $"ds/savestates/{code}_1.sav"));
        Assert.Equal("a-resume", Read("b", $"ds/savestates/{code}_resume.sav"));
        Assert.True(File.Exists(P("b", $"ds/memcards/{title} Team_1.mcd")));
        Assert.Equal("b-ctr-state", Read("b", "ds/savestates/SCUS-94426_1.sav"));
    }

    [Fact]
    public async Task A_wii_save_travels_between_the_deck_and_windows_layouts()
    {
        // A made-up title ID per test, R + the test's digits: four ASCII characters, eight hex digits.
        var id = "R" + _id[..3];
        var hex = Convert.ToHexStringLower(System.Text.Encoding.ASCII.GetBytes(id));
        var deckWii = $".var/app/org.DolphinEmu.dolphin-emu/data/dolphin-emu/Wii/title/00010000/{hex}/data";
        F.Write(P("deck", $"{deckWii}/banner.bin"), F.Banner($"Wii Game {_id}"));
        Write("deck", $"{deckWii}/save.bin", "deck-save");
        Write("deck", ".var/app/org.DolphinEmu.dolphin-emu/data/dolphin-emu/StateSaves/" + id + "01.s01", "deck-state");
        // Windows: EmuDeck's portable User folder, with another game's save beside it.
        F.Write(P("win", $"Dolphin-x64/User/Wii/title/00010000/{hex}/data/banner.bin"), F.Banner($"Wii Game {_id}"));
        Write("win", $"Dolphin-x64/User/Wii/title/00010000/{hex}/data/save.bin", "win-old");
        F.Write(P("win", "Dolphin-x64/User/Wii/title/00010000/52334f45/data/banner.bin"), F.Banner("Metroid: Other M"));
        Directory.CreateDirectory(P("win", "Dolphin-x64/User/StateSaves"));

        var deck = await Machine("deck");
        var win = await Machine("win");
        var deckGame = await Enroll(deck, DolphinSaves.Scan(DolphinSaves.Folders([],
            [(DolphinSaves.EmulatorName, P("deck", ".var/app/org.DolphinEmu.dolphin-emu/data/dolphin-emu"))])), id);
        var winGame = await Enroll(win, DolphinSaves.Scan(DolphinSaves.Folders([],
            [(DolphinSaves.EmulatorName, P("win", "Dolphin-x64/User"))])), id);
        Assert.Equal(deckGame.GameId, winGame.GameId);

        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));
        await engineWin.PullAsync(winGame, force: true);

        Assert.Equal("deck-save", Read("win", $"Dolphin-x64/User/Wii/title/00010000/{hex}/data/save.bin"));
        Assert.Equal("deck-state", Read("win", $"Dolphin-x64/User/StateSaves/{id}01.s01"));
        Assert.True(File.Exists(P("win", "Dolphin-x64/User/Wii/title/00010000/52334f45/data/banner.bin")));
    }
}
