using SaveLocker.Shared;
using Xunit;
using S = SaveLocker.Agent.Tests.SwitchFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Group F end to end against a real server (tasks/emulator-saves Phase 6): one Switch title is one game across emulators
/// (D2), so Eden on the Deck and Ryujinx on Windows — different names, different folders — share it; and on one machine a
/// second emulator's copy is kept in step with the one that syncs.
/// </summary>
public sealed class GroupFSyncTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-groupf-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Random.Shared.Next(0x100000, 0xFFFFFF).ToString("X6");
    private const string RyuUser = "00000000000000010000000000000000";
    private const string EdenUser = "C4D7AB8B14D1F6F277CD40D6A0F00ECA";

    public GroupFSyncTests(ServerProcess server)
    {
        _server = server;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Title => $"0100{_id}000000";
    private string P(string rel) => Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));

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

    private static async Task<TrackedGame> Enroll(AgentConfig config, IReadOnlyList<ScanCandidate> found, string title)
    {
        var id = found.ToList().FindIndex(c => c.EmulatorRom == title);
        Assert.True(id >= 0, $"no candidate for {title}");
        Assert.Equal((1, 0), await Enroller.EnrollAsync(config, found, [id]));
        return Enroller.TrackedFor(AgentConfig.Load(config.ConfigPath), found[id])!;
    }

    [Fact]
    public async Task One_switch_title_moves_between_eden_on_the_deck_and_ryujinx_on_windows()
    {
        // The Deck: Eden, named from its ROM. Windows: Ryujinx, the save in a numbered folder, named from its own metadata.
        var eden = S.Eden(P("deck/Emulation"), EdenUser);
        S.Write(P($"deck/Emulation/roms/switch/Group F Quest {_id} [{Title}][v0].nsp"), "rom");
        S.Write(Path.Combine(S.EdenSave(P("deck/Emulation"), EdenUser, Title), "save.bin"), "deck-save");
        S.Write(Path.Combine(S.EdenSave(P("deck/Emulation"), EdenUser, Title), "slot", "1.dat"), "deck-slot");
        var ryu = S.Ryujinx(P("win/ryu"), RyuUser, (Title, RyuUser, RyujinxSaveIndex.SaveType.Account, 7));
        S.Write(Path.Combine(S.RyujinxSave(P("win/ryu"), 7), "save.bin"), "win-old");
        S.Write(Path.Combine(S.RyujinxSave(P("win/ryu"), 7), "stale.bin"), "only-on-windows");
        S.Write(P($"win/ryu/games/{Title.ToLowerInvariant()}/gui/metadata.json"), $$"""{"title":"Ryujinx's name {{_id}}"}""");

        var deck = await Machine("deck");
        var win = await Machine("win");
        var deckFound = SwitchEmulators.Scan([eden], [P("deck/Emulation")]);
        var winFound = SwitchEmulators.Scan([ryu], []);
        var deckGame = await Enroll(deck, deckFound, Title);
        var winGame = await Enroll(win, winFound, Title);

        Assert.Equal($"Group F Quest {_id}", deckGame.Name);
        Assert.Equal(deckGame.GameId, winGame.GameId);
        Assert.Equal(deckGame.Name, winGame.Name);
        Assert.Equal($"switch:{Title}", winGame.EmulatorKey);
        var server = (await ApiClient.For(win).ListGamesAsync()).Single(g => g.Id == winGame.GameId);
        Assert.Equal($"switch:{Title}", server.EmulatorKey);
        Assert.Equal(["Eden", "Ryujinx"], server.Emulators!.Order());

        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        await using var engineWin = new SyncEngine(win, ApiClient.For(win));
        Assert.NotNull(await engineDeck.PushAsync(deckGame, force: true));
        await engineWin.PullAsync(winGame, force: true);

        // Ryujinx's committed copy holds the Deck's save exactly, and its working copy is made the same.
        foreach (var copy in new[] { "0", "1" })
        {
            Assert.Equal("deck-save", File.ReadAllText(Path.Combine(S.RyujinxSave(P("win/ryu"), 7, copy), "save.bin")));
            Assert.Equal("deck-slot", File.ReadAllText(Path.Combine(S.RyujinxSave(P("win/ryu"), 7, copy), "slot", "1.dat")));
            Assert.False(File.Exists(Path.Combine(S.RyujinxSave(P("win/ryu"), 7, copy), "stale.bin")));
        }
        Assert.True(File.Exists(Path.Combine(P("win/ryu"), "bis", "user", "save", "0000000000000007", "ExtraData0")));

        // And back.
        S.Write(Path.Combine(S.RyujinxSave(P("win/ryu"), 7), "save.bin"), "win-newer");
        Assert.NotNull(await engineWin.PushAsync(winGame, force: true));
        await engineDeck.PullAsync(deckGame, force: true);
        Assert.Equal("win-newer", File.ReadAllText(Path.Combine(S.EdenSave(P("deck/Emulation"), EdenUser, Title), "save.bin")));
    }

    [Fact]
    public async Task A_game_of_the_same_name_that_is_another_title_is_never_joined()
    {
        var other = $"0101{_id}000000";
        var eden = S.Eden(P("a/Emulation"), EdenUser);
        S.Write(P($"a/Emulation/roms/switch/Same Name {_id} [{Title}].nsp"), "rom");
        S.Write(P($"a/Emulation/roms/switch/Same Name {_id} [{other}].nsp"), "rom");
        S.Write(Path.Combine(S.EdenSave(P("a/Emulation"), EdenUser, Title), "save.bin"), "one");
        S.Write(Path.Combine(S.EdenSave(P("a/Emulation"), EdenUser, other), "save.bin"), "two");
        var a = await Machine("a");
        var found = SwitchEmulators.Scan([eden], [P("a/Emulation")]);

        var first = await Enroll(a, found, Title);
        var second = await Enroll(a, found, other);

        Assert.NotEqual(first.GameId, second.GameId);
        Assert.Equal($"Same Name {_id}", first.Name);
        Assert.Equal($"Same Name {_id} (Switch)", second.Name);
    }

    [Fact]
    public async Task A_copy_in_another_emulator_is_kept_in_step_and_asks_when_both_changed()
    {
        var eden = S.Eden(P("pc/Emulation"), EdenUser);
        var ryu = S.Ryujinx(P("pc/ryu"), RyuUser, (Title, RyuUser, RyujinxSaveIndex.SaveType.Account, 2));
        var edenSave = S.EdenSave(P("pc/Emulation"), EdenUser, Title);
        S.Write(Path.Combine(edenSave, "save.bin"), "eden-1");
        var pc = await Machine("pc");
        IReadOnlyList<SwitchEmulators.Setup> setups = [eden, ryu];
        var game = await Enroll(pc, SwitchEmulators.Scan(setups, []), Title);
        void Log(string _) { }
        string Ryu(string copy = "0") => File.ReadAllText(Path.Combine(S.RyujinxSave(P("pc/ryu"), 2, copy), "save.bin"));

        // Ryujinx made a slot, empty: the synced save can go there.
        var rows = SwitchCopies.List(pc, game, setups);
        Assert.Equal([("Eden", "synced"), ("Ryujinx", "empty")], rows.Select(r => (r.Emulator, r.State)));
        Assert.Null(SwitchCopies.CopyHere(pc, game, "Ryujinx", Log, setups));
        Assert.Equal(("eden-1", "eden-1"), (Ryu(), Ryu("1")));
        Assert.Equal("same", SwitchCopies.List(pc, game, setups)[1].State);

        // Kept in step: played in Ryujinx last, it comes into Eden's copy; played in Eden, it goes back.
        SwitchCopies.SetKeepInSync(pc, game, "Ryujinx", true);
        S.Write(Path.Combine(S.RyujinxSave(P("pc/ryu"), 2), "save.bin"), "ryu-2");
        Assert.Single(SwitchCopies.Reconcile(pc, game, Log, setups));
        Assert.Equal("ryu-2", File.ReadAllText(Path.Combine(edenSave, "save.bin")));
        S.Write(Path.Combine(edenSave, "save.bin"), "eden-3");
        Assert.Single(SwitchCopies.Reconcile(pc, game, Log, setups));
        Assert.Equal(("eden-3", "eden-3"), (Ryu(), Ryu("1")));
        Assert.Empty(SwitchCopies.Reconcile(pc, game, Log, setups));

        // Both changed: nothing is copied, the user is asked, and their pick wins.
        S.Write(Path.Combine(edenSave, "save.bin"), "eden-4");
        S.Write(Path.Combine(S.RyujinxSave(P("pc/ryu"), 2), "save.bin"), "ryu-4");
        Assert.Single(SwitchCopies.Reconcile(pc, game, Log, setups));
        Assert.Equal(("eden-4", "ryu-4"), (File.ReadAllText(Path.Combine(edenSave, "save.bin")), Ryu()));
        Assert.Equal("conflict", SwitchCopies.List(pc, game, setups)[1].State);
        Assert.True(AgentConfig.Load(pc.ConfigPath).Games.Single(g => g.GameId == game.GameId).EmulatorCopies.Single().Conflict);
        Assert.Null(SwitchCopies.Resolve(pc, game, "Ryujinx", keepCopy: true, Log, setups));
        Assert.Equal("ryu-4", File.ReadAllText(Path.Combine(edenSave, "save.bin")));
        Assert.Equal("same", SwitchCopies.List(pc, game, setups)[1].State);

        // Every overwrite was backed up first.
        Assert.InRange(Directory.GetFiles(Path.Combine(pc.StateDir, "emulator-copies", game.GameId.ToString("N")), "*.zip").Length, 1, SwitchCopies.BackupsKept);

        // Ryujinx's copy becomes the one that syncs; Eden's is now a copy, still kept in step.
        Assert.Null(await SwitchCopies.MakeSyncedAsync(pc, game, "Ryujinx", ApiClient.For(pc), Log, setups));
        var moved = AgentConfig.Load(pc.ConfigPath).Games.Single(g => g.GameId == game.GameId);
        Assert.Equal(S.RyujinxSave(P("pc/ryu"), 2), moved.SaveDirectory);
        Assert.Equal("Ryujinx", moved.Source!.Detail);
        Assert.True(moved.EmulatorCopies.Single(c => c.Emulator == "Eden").KeepInSync);
        Assert.Equal([("Ryujinx", "synced"), ("Eden", "same")], SwitchCopies.List(pc, moved, setups).Select(r => (r.Emulator, r.State)));
    }
}
