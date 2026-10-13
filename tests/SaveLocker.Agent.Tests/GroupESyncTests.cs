using SaveLocker.Agent;
using Xunit;
using F = SaveLocker.Agent.Tests.ConsoleSaveFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The Group E emulators end to end against a real server (tasks/emulator-saves Group E): one game's title-ID folder in a
/// folder every game shares travels between two machines, with its states, and nothing else in that folder moves.
/// </summary>
public sealed class GroupESyncTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-groupe-" + Guid.NewGuid().ToString("N"));
    // The class shares one server, so each test's game is its own.
    private readonly string _id = Random.Shared.Next(0x100000, 0xFFFFFF).ToString("x6");

    public GroupESyncTests(ServerProcess server)
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

    private const string Ids = "Nintendo 3DS/00000000000000000000000000000000/00000000000000000000000000000000/title/00040000";

    [Fact]
    public async Task A_3ds_game_moves_between_the_decks_sd_card_and_windows_portable_azahar_with_its_states()
    {
        var low = "00" + _id;
        var programId = "00040000" + low.ToUpperInvariant();
        const string deckSd = "SDCard/Emulation/storage/azahar/sdmc";
        const string deckStates = ".local/share/azahar-emu/states";
        const string winUser = "AppData/Roaming/EmuDeck/Emulators/azahar/user";
        // The Deck has the ROM (a product code no table knows, so its file name names it); Windows has none.
        F.Write(P("deck", $"SDCard/Emulation/roms/n3ds/Group E Quest {_id} (USA).cci"), F.Ncsd(Convert.ToUInt64(programId, 16), "CTR-P-ZZZZ"));
        Write("deck", $"{deckSd}/{Ids}/{low}/data/00000001/save.bin", "deck-save");
        Write("deck", $"{deckSd}/{Ids}/{low}/data/00000001.metadata", "deck-meta");
        Write("deck", $"{deckStates}/{programId}.01.cst", "deck-state");
        // Windows: an older save, the same game installed from a CIA, and another game with its own state.
        Write("win", $"{winUser}/sdmc/{Ids}/{low}/data/00000001/save.bin", "win-old");
        Write("win", $"{winUser}/sdmc/{Ids}/{low}/content/00000000.app", "win-installed-game");
        Write("win", $"{winUser}/sdmc/{Ids}/00054000/data/00000001/GameData.bin", "win-smb");
        Write("win", $"{winUser}/states/0004000000054000.01.cst", "win-smb-state");

        var deck = await Machine("deck");
        var win = await Machine("win");
        var deckFound = AzaharSaves.Scan([new AzaharFolders(P("deck", deckSd), P("deck", deckStates), [P("deck", "SDCard/Emulation/roms/n3ds")], EmuDeck: true)]);
        var winFound = AzaharSaves.Scan([new AzaharFolders(P("win", $"{winUser}/sdmc"), P("win", $"{winUser}/states"), [])]);
        var deckGame = await Enroll(deck, deckFound, programId);
        var winGame = await Enroll(win, winFound, programId);
        Assert.Equal($"Group E Quest {_id}", deckGame.Name);
        // Windows called it "3DS <ID>"; it joins the Deck's game by its folder all the same (D1).
        Assert.Equal($"3DS {low.ToUpperInvariant()}", winFound.Single(c => c.EmulatorRom == programId).Name);
        Assert.Equal(deckGame.GameId, winGame.GameId);

        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));
        await engineWin.PullAsync(winGame, force: true);

        Assert.Equal("deck-save", Read("win", $"{winUser}/sdmc/{Ids}/{low}/data/00000001/save.bin"));
        Assert.Equal("deck-meta", Read("win", $"{winUser}/sdmc/{Ids}/{low}/data/00000001.metadata"));
        Assert.Equal("deck-state", Read("win", $"{winUser}/states/{programId}.01.cst"));
        // The installed game beside the save, and the other game and its state, are Windows' own.
        Assert.Equal("win-installed-game", Read("win", $"{winUser}/sdmc/{Ids}/{low}/content/00000000.app"));
        Assert.Equal("win-smb", Read("win", $"{winUser}/sdmc/{Ids}/00054000/data/00000001/GameData.bin"));
        Assert.Equal("win-smb-state", Read("win", $"{winUser}/states/0004000000054000.01.cst"));

        // And back: a newer save made on Windows reaches the Deck.
        Write("win", $"{winUser}/sdmc/{Ids}/{low}/data/00000001/save.bin", "win-newer");
        Assert.NotNull(await engineWin.PushAsync(winGame, force: true));
        await engineDeck.PullAsync(deckGame, force: true);
        Assert.Equal("win-newer", Read("deck", $"{deckSd}/{Ids}/{low}/data/00000001/save.bin"));
        Assert.False(File.Exists(P("deck", $"{deckSd}/{Ids}/{low}/content/00000000.app")));
    }
    [Fact]
    public async Task An_xbox_360_save_moves_between_two_profiles_and_layouts_and_leaves_the_dlc()
    {
        // A title ID no table knows; the Deck saved under its profile, Windows under another profile.
        var title = "5A" + _id.ToUpperInvariant();
        const string deckContent = "SDCard/Emulation/roms/xbox360/content";
        const string winContent = "AppData/Roaming/EmuDeck/Emulators/xenia/content";
        Write("deck", $"{deckContent}/E030000033894516/{title}/00000001/Profile/save.dat", "deck-save");
        Write("deck", $"{deckContent}/E030000033894516/{title}/Headers/00000001/Profile.header", "deck-header");
        Write("win", $"{winContent}/E0300000AAAAAAAA/{title}/00000001/Profile/save.dat", "win-old");
        Write("win", $"{winContent}/E0300000AAAAAAAA/{title}/00000002/DLCPACK/dlc.bin", "win-dlc");
        Write("win", $"{winContent}/E0300000AAAAAAAA/4D5309C9/00000001/ForzaProfile/ForzaProfile", "win-forza");

        var deck = await Machine("deck");
        var win = await Machine("win");
        var deckGame = await Enroll(deck, XeniaSaves.Scan([new XeniaFolders(P("deck", deckContent), EmuDeck: true)]), title);
        var winGame = await Enroll(win, XeniaSaves.Scan([new XeniaFolders(P("win", winContent))]), title);
        Assert.Equal(deckGame.GameId, winGame.GameId);

        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));
        await engineWin.PullAsync(winGame, force: true);

        // Phase 5c step 7: into the profile the game is under (the Deck's, the first to say), which Windows' Xenia is
        // set to when the game starts. Windows' own profile and its old save, DLC and other game stay as they were.
        Assert.Equal("xenia:E030000033894516", (await ApiClient.For(win).ListGamesAsync()).Single(g => g.Id == winGame.GameId).EmulatorProfile);
        Assert.Equal(P("win", $"{winContent}/E030000033894516"), AgentConfig.Load(win.ConfigPath).Games.Single(g => g.GameId == winGame.GameId).SaveDirectory);
        Assert.Equal("deck-save", Read("win", $"{winContent}/E030000033894516/{title}/00000001/Profile/save.dat"));
        Assert.Equal("deck-header", Read("win", $"{winContent}/E030000033894516/{title}/Headers/00000001/Profile.header"));
        Assert.Equal("win-old", Read("win", $"{winContent}/E0300000AAAAAAAA/{title}/00000001/Profile/save.dat"));
        Assert.Equal("win-dlc", Read("win", $"{winContent}/E0300000AAAAAAAA/{title}/00000002/DLCPACK/dlc.bin"));
        Assert.Equal("win-forza", Read("win", $"{winContent}/E0300000AAAAAAAA/4D5309C9/00000001/ForzaProfile/ForzaProfile"));
    }
    [Fact]
    public async Task An_xbox_360_device_save_moves_with_its_game_and_the_dlc_beside_it_stays()
    {
        var title = "5B" + _id.ToUpperInvariant();
        const string deckContent = "SDCard/Emulation/roms/xbox360/content";
        const string winContent = "AppData/Roaming/EmuDeck/Emulators/xenia/content";
        Write("deck", $"{deckContent}/E030000033894516/{title}/00000001/Profile/save.dat", "deck-save");
        Write("deck", $"{deckContent}/0000000000000000/{title}/00000001/globaloptions/options", "deck-calibration");
        Write("win", $"{winContent}/E0300000AAAAAAAA/{title}/00000001/Profile/save.dat", "win-old");
        Write("win", $"{winContent}/0000000000000000/{title}/00000002/SONGPACK/song.bin", "win-dlc");

        var deck = await Machine("deck");
        var win = await Machine("win");
        var deckGame = await Enroll(deck, XeniaSaves.Scan([new XeniaFolders(P("deck", deckContent), EmuDeck: true)]), title);
        var winGame = await Enroll(win, XeniaSaves.Scan([new XeniaFolders(P("win", winContent))]), title);
        Assert.Equal(deckGame.GameId, winGame.GameId);

        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));
        await engineWin.PullAsync(winGame, force: true);

        Assert.Equal("deck-save", Read("win", $"{winContent}/E030000033894516/{title}/00000001/Profile/save.dat"));
        Assert.Equal("deck-calibration", Read("win", $"{winContent}/0000000000000000/{title}/00000001/globaloptions/options"));
        Assert.Equal("win-dlc", Read("win", $"{winContent}/0000000000000000/{title}/00000002/SONGPACK/song.bin"));
    }

    [Fact]
    public async Task A_xbox_360_games_part_of_the_profile_moves_with_it_and_other_games_rows_stay()
    {
        // Phase 5c step 5: both machines are on one profile. The PC played Forza Horizon (18 unlocked); the Deck played
        // Halo 3 since its last sync (11 unlocked) and still has an older Forza Horizon row. Pulling Forza Horizon onto
        // the Deck must bring its achievements file and row, and leave Halo 3's file and row alone.
        var title = "5C" + _id.ToUpperInvariant();
        var tid = Convert.ToUInt32(title, 16);
        const uint halo = 0x4D5307E6;
        const string xuid = "E030000033894516";
        const string deckContent = "SDCard/Emulation/roms/xbox360/content";
        const string pcContent = "Xenia Manager/Emulators/Xenia Canary/content";
        var profile = $"{xuid}/FFFE07D1/00010000/{xuid}";
        Write("pc", $"{pcContent}/{xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Write("pc", $"{pcContent}/{profile}/{title}.gpd", "pc-achievements");
        F.Write(P("pc", $"{pcContent}/{profile}/FFFE07D1.gpd"), XeniaFormatsTests.Dashboard(XeniaFormatsTests.Row(tid, 18, 260, "Group E Racer")));
        Write("deck", $"{deckContent}/{xuid}/{title}/00000001/Profile/save.dat", "deck-old-save");
        Write("deck", $"{deckContent}/{profile}/{title}.gpd", "deck-old-achievements");
        Write("deck", $"{deckContent}/{profile}/4D5307E6.gpd", "deck-halo-achievements");
        var haloRow = XeniaFormatsTests.Row(halo, 11, 190, "Halo 3");
        F.Write(P("deck", $"{deckContent}/{profile}/FFFE07D1.gpd"),
            XeniaFormatsTests.Dashboard(XeniaFormatsTests.Row(tid, 12, 180, "Group E Racer"), haloRow));

        var pc = await Machine("pc");
        var deck = await Machine("deck");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        var deckGame = await Enroll(deck, XeniaSaves.Scan([new XeniaFolders(P("deck", deckContent), EmuDeck: true)]), title);
        Assert.Equal(pcGame.GameId, deckGame.GameId);
        Assert.Contains(pcGame.ExtraPaths, p => p.Key == XeniaProfileSlice.Key);

        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await enginePc.PushAsync(pcGame, force: true));
        Assert.Equal(PullOutcome.Restored, await engineDeck.PullAsync(deckGame, force: true));

        Assert.Equal("pc-save", Read("deck", $"{deckContent}/{xuid}/{title}/00000001/Profile/save.dat"));
        Assert.Equal("pc-achievements", Read("deck", $"{deckContent}/{profile}/{title}.gpd"));
        Assert.Equal("deck-halo-achievements", Read("deck", $"{deckContent}/{profile}/4D5307E6.gpd"));
        var dashboard = Xdbf.Parse(File.ReadAllBytes(P("deck", $"{deckContent}/{profile}/FFFE07D1.gpd")));
        Assert.Equal(haloRow, dashboard.Get(Xdbf.Title, halo));
        Assert.Equal(18, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(dashboard.Get(Xdbf.Title, tid).AsSpan(8)));
        Assert.Equal(450, XeniaProfile.GetInt(dashboard, XeniaProfile.GamerscoreSetting));
        Assert.Equal(29, XeniaProfile.GetInt(dashboard, XeniaProfile.AchievementsSetting));
    }

    [Fact]
    public async Task A_machine_without_the_xbox_360_profile_gets_it_with_the_first_game_it_pulls()
    {
        // Phase 5c step 6: the PC has the profile; the Deck has only the game's save folder. The pull unpacks the profile -
        // Account and a dashboard with this game's row - so Xenia there can sign in to it and load the game's GPD.
        var title = "6C" + _id.ToUpperInvariant();
        var tid = Convert.ToUInt32(title, 16);
        const string xuid = "E030000033894516";
        const string deckContent = "SDCard/Emulation/roms/xbox360/content";
        const string pcContent = "Xenia Manager/Emulators/Xenia Canary/content";
        var profile = $"{xuid}/FFFE07D1/00010000/{xuid}";
        Write("pc", $"{pcContent}/{xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Write("pc", $"{pcContent}/{profile}/{title}.gpd", "pc-achievements");
        F.Write(P("pc", $"{pcContent}/{profile}/Account"), XeniaFormatsTests.AccountFor("Driver"));
        F.Write(P("pc", $"{pcContent}/{profile}/FFFE07D1.gpd"), XeniaFormatsTests.Dashboard(
            XeniaFormatsTests.Row(tid, 18, 260, "Group E Racer"), XeniaFormatsTests.Row(0x4D5307E6, 11, 190, "Halo 3")));
        Write("deck", $"{deckContent}/{xuid}/{title}/00000001/Profile/save.dat", "deck-old-save");

        var pc = await Machine("pc");
        var deck = await Machine("deck");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        var deckGame = await Enroll(deck, XeniaSaves.Scan([new XeniaFolders(P("deck", deckContent), EmuDeck: true)]), title);

        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await enginePc.PushAsync(pcGame, force: true));
        Assert.Equal(PullOutcome.Restored, await engineDeck.PullAsync(deckGame, force: true));

        Assert.Equal("Driver", XeniaProfile.Gamertag(P("deck", deckContent), xuid));
        Assert.Equal("pc-achievements", Read("deck", $"{deckContent}/{profile}/{title}.gpd"));
        var dashboard = Xdbf.Parse(File.ReadAllBytes(P("deck", $"{deckContent}/{profile}/FFFE07D1.gpd")));
        Assert.Equal([tid], dashboard.Entries.Where(e => e.Namespace == Xdbf.Title).Select(e => (uint)e.Id));
        Assert.Equal(260, XeniaProfile.GetInt(dashboard, XeniaProfile.GamerscoreSetting));
    }
}
