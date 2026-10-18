using System.IO.Compression;
using SaveLocker.Shared;
using Xunit;
using S = SaveLocker.Agent.Tests.SwitchFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// One Switch user on every device (<see cref="SwitchUserSharing"/>), against a real server: the maintainer's Deck (Yuzu,
/// user <c>DBBE23FA…</c>, which Animal Crossing's island links its resident to) and PC (Eden, user <c>C4D7AB8B…</c>).
/// </summary>
public sealed class SwitchUserSharingTests : IClassFixture<ServerProcess>, IDisposable
{
    private const string DeckUser = "DBBE23FA78D7168D745B090673B123AC";
    private const string PcUser = "C4D7AB8B14D1F6F277CD40D6A0F00ECA";
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-switchuser-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Random.Shared.Next(0x100000, 0xFFFFFF).ToString("X6");

    public SwitchUserSharingTests(ServerProcess server)
    {
        _server = server;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Title(char c) => $"0100{_id}{c}00000";
    private string P(string rel) => Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));
    private static readonly Func<string?> NothingRunning = () => null;

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

    private static SwitchUserSharing.GameLock Unlocked => async (_, work) => (true, await work());

    [Fact]
    public async Task Every_install_takes_the_chosen_user_its_saves_and_games_follow_and_it_can_be_put_back()
    {
        var eden = S.Eden(P("pc/Emulation"), PcUser, "maro");
        var played = S.EdenSave(P("pc/Emulation"), PcUser, Title('A'));
        S.Write(Path.Combine(played, "save.bin"), "pc-played");
        S.Write(Path.Combine(S.EdenSave(P("pc/Emulation"), PcUser, Title('B')), "other.bin"), "untracked");
        var island = S.EdenSave(P("pc/Emulation"), SwitchEmulators.DeviceProfile, Title('C'));
        S.Write(Path.Combine(island, "main.dat"), "island");
        var pc = await Machine("pc");
        var game = await Enroll(pc, SwitchEmulators.Scan([eden], []), Title('A'));
        Assert.Equal(Path.GetFullPath(played), Path.GetFullPath(game.SaveDirectory!));
        Assert.Equal([new SwitchUserReport("Eden", "EmuDeck", PcUser, "maro", true)], SwitchUserSharing.Reports([eden]));

        var shared = new SwitchUserDto(DeckUser, "maro");
        var outcome = await SwitchUserSharing.ApplyAsync(pc, shared, [eden], ApiClient.For(pc), Unlocked, null, _ => { }, NothingRunning);
        Assert.Null(outcome.Refusal);
        Assert.Single(outcome.Done);

        // The user's ID is the shared one, its name kept; every one of its saves moved; the device save untouched.
        Assert.Equal(DeckUser, Assert.Single(SwitchEmulators.YuzuUsers(eden.Profiles)).Id);
        Assert.Equal("maro", SwitchEmulators.YuzuUsers(eden.Profiles)[0].Name);
        Assert.False(Directory.Exists(SwitchUserSharing.UserDir(eden, PcUser)));
        var moved = S.EdenSave(P("pc/Emulation"), DeckUser, Title('A'));
        Assert.Equal("pc-played", File.ReadAllText(Path.Combine(moved, "save.bin")));
        Assert.Equal("untracked", File.ReadAllText(Path.Combine(S.EdenSave(P("pc/Emulation"), DeckUser, Title('B')), "other.bin")));
        Assert.Equal("island", File.ReadAllText(Path.Combine(island, "main.dat")));
        Assert.True(Assert.Single(SwitchUserSharing.States(pc, shared, [eden])).Matches);

        // The game follows, here and on the server, so the poller doesn't move it back.
        Assert.Equal(Path.GetFullPath(moved), Path.GetFullPath(AgentConfig.Load(pc.ConfigPath).Games.Single(g => g.GameId == game.GameId).SaveDirectory!));
        var onServer = (await ApiClient.For(pc).ListGamesAsync()).Single(g => g.Id == game.GameId);
        Assert.Equal(Path.GetFullPath(moved), Path.GetFullPath(onServer.MachineSavePath!));

        // Everything that moved is in the backup.
        var backup = Assert.Single(Directory.GetFiles(Path.Combine(pc.StateDir, "switch-users"), "*.zip"));
        using (var zip = ZipFile.OpenRead(backup))
        {
            Assert.NotNull(zip.GetEntry("profiles.dat"));
            Assert.NotNull(zip.GetEntry($"old/{Title('A')}/save.bin"));
            Assert.NotNull(zip.GetEntry($"old/{Title('B')}/other.bin"));
        }

        // Put back: the old user, its saves where they were, the game too, and the shared user not adopted here again.
        var undone = await SwitchUserSharing.UndoAsync(pc, [eden], ApiClient.For(pc), Unlocked, _ => { }, NothingRunning);
        Assert.Null(undone.Refusal);
        Assert.Equal(PcUser, SwitchEmulators.YuzuUsers(eden.Profiles)[0].Id);
        Assert.Equal("pc-played", File.ReadAllText(Path.Combine(played, "save.bin")));
        Assert.False(Directory.Exists(SwitchUserSharing.UserDir(eden, DeckUser)));
        Assert.Equal(Path.GetFullPath(played), Path.GetFullPath(AgentConfig.Load(pc.ConfigPath).Games.Single(g => g.GameId == game.GameId).SaveDirectory!));
        onServer = (await ApiClient.For(pc).ListGamesAsync()).Single(g => g.Id == game.GameId);
        Assert.Equal(Path.GetFullPath(played), Path.GetFullPath(onServer.MachineSavePath!));
        Assert.Equal(DeckUser, AgentConfig.Load(pc.ConfigPath).SwitchSharedUserDeclined);
    }

    [Fact]
    public async Task A_title_saved_under_both_users_waits_for_a_choice_and_the_one_put_aside_comes_back_on_undo()
    {
        // The PC already has a folder under the Deck's ID (a save copied over by hand once), with the same title in it.
        var eden = S.Eden(P("pc/Emulation"), PcUser, "maro");
        S.Write(Path.Combine(S.EdenSave(P("pc/Emulation"), PcUser, Title('A')), "save.bin"), "pc-island");
        S.Write(Path.Combine(S.EdenSave(P("pc/Emulation"), DeckUser, Title('A')), "save.bin"), "old-copy");
        S.Write(Path.Combine(S.EdenSave(P("pc/Emulation"), DeckUser, Title('D')), "save.bin"), "only-there");
        var pc = await Machine("pc");
        var shared = new SwitchUserDto(DeckUser, "maro");

        var collision = Assert.Single(Assert.Single(SwitchUserSharing.States(pc, shared, [eden])).Collisions);
        Assert.Equal(Title('A'), collision.TitleId);
        var refused = await SwitchUserSharing.ApplyAsync(pc, shared, [eden], null, Unlocked, null, _ => { }, NothingRunning);
        Assert.Contains("Choose which to keep", refused.Refusal);
        Assert.Equal(PcUser, SwitchEmulators.YuzuUsers(eden.Profiles)[0].Id);

        var kept = await SwitchUserSharing.ApplyAsync(pc, shared, [eden], null, Unlocked,
            new Dictionary<string, string> { [Title('A')] = SwitchUserSharing.Here }, _ => { }, NothingRunning);
        Assert.Null(kept.Refusal);
        Assert.Equal("pc-island", File.ReadAllText(Path.Combine(S.EdenSave(P("pc/Emulation"), DeckUser, Title('A')), "save.bin")));
        Assert.Equal("only-there", File.ReadAllText(Path.Combine(S.EdenSave(P("pc/Emulation"), DeckUser, Title('D')), "save.bin")));

        Assert.Null((await SwitchUserSharing.UndoAsync(pc, [eden], null, Unlocked, _ => { }, NothingRunning)).Refusal);
        Assert.Equal("pc-island", File.ReadAllText(Path.Combine(S.EdenSave(P("pc/Emulation"), PcUser, Title('A')), "save.bin")));
        Assert.Equal("old-copy", File.ReadAllText(Path.Combine(S.EdenSave(P("pc/Emulation"), DeckUser, Title('A')), "save.bin")));
        Assert.Equal("only-there", File.ReadAllText(Path.Combine(S.EdenSave(P("pc/Emulation"), DeckUser, Title('D')), "save.bin")));
    }

    [Fact]
    public async Task An_install_that_already_has_the_user_as_another_one_is_told_to_pick_it_instead()
    {
        var eden = S.Eden(P("pc/Emulation"), PcUser, "maro");
        S.Write(eden.Profiles!, S.Profiles((PcUser, "maro"), (DeckUser, "deck")));
        var pc = await Machine("pc");
        var state = Assert.Single(SwitchUserSharing.States(pc, new SwitchUserDto(DeckUser, "maro"), [eden]));
        Assert.Contains("already has a user with that ID", state.Note);
        var outcome = await SwitchUserSharing.ApplyAsync(pc, new SwitchUserDto(DeckUser, "maro"), [eden], null, Unlocked, null, _ => { }, NothingRunning);
        Assert.Equal(state.Note, outcome.Refusal);
    }

    [Fact]
    public async Task The_server_keeps_each_machines_users_and_hands_the_chosen_one_out_on_the_heartbeat()
    {
        var deck = await Machine("deck");
        var pc = await Machine("pc");
        var deckApi = ApiClient.For(deck);
        await deckApi.ReportHealthAsync(new AgentHeartbeat("9.9.9", "Linux",
            SwitchUsers: [new SwitchUserReport("Yuzu", "EmuDeck", DeckUser.ToLowerInvariant(), "maro", true),
                          new SwitchUserReport("Yuzu", "EmuDeck", SwitchEmulators.DeviceProfile, "device", false)]));
        var beat = await ApiClient.For(pc).ReportHealthAsync(new AgentHeartbeat("9.9.9", "Windows",
            SwitchUsers: [new SwitchUserReport("Eden", "EmuDeck", PcUser, "maro", true)]));
        Assert.Null(beat.SwitchSharedUser);

        var users = await ApiClient.For(pc).GetSwitchUsersAsync();
        Assert.Equal([new SwitchUserReport("Yuzu", "EmuDeck", DeckUser, "maro", true)],
            users.Machines.Single(m => m.MachineId == deck.MachineId).Users);
        Assert.Null(users.Shared);

        await Assert.ThrowsAsync<HttpRequestException>(() => ApiClient.For(pc).SetSwitchSharedUserAsync(new SwitchUserDto(SwitchEmulators.DeviceProfile, "x")));
        Assert.Equal(new SwitchUserDto(DeckUser, "maro"), await ApiClient.For(pc).SetSwitchSharedUserAsync(new SwitchUserDto(DeckUser.ToLowerInvariant(), "maro")));
        beat = await deckApi.ReportHealthAsync(new AgentHeartbeat("9.9.9", "Linux"));
        Assert.Equal(new SwitchUserDto(DeckUser, "maro"), beat.SwitchSharedUser);
        // A heartbeat without users (an older agent) keeps the last report.
        Assert.Single((await deckApi.GetSwitchUsersAsync()).Machines.Single(m => m.MachineId == deck.MachineId).Users);

        await ApiClient.For(pc).ClearSwitchSharedUserAsync();
        Assert.Null((await deckApi.ReportHealthAsync(new AgentHeartbeat("9.9.9", "Linux"))).SwitchSharedUser);
    }

    [Fact]
    public async Task One_change_of_user_at_a_time_the_poller_and_the_page_never_both_move_an_install()
    {
        var eden = S.Eden(P("pc/Emulation"), PcUser, "maro");
        S.Write(Path.Combine(S.EdenSave(P("pc/Emulation"), PcUser, Title('A')), "save.bin"), "pc");
        var pc = await Machine("pc");
        var shared = new SwitchUserDto(DeckUser, "maro");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        // The first pass is held inside, where it checks for a running emulator.
        var first = Task.Run(() => SwitchUserSharing.ApplyAsync(pc, shared, [eden], ApiClient.For(pc), Unlocked, null, _ => { },
            () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(30)); return "Eden"; }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
        Assert.Equal(SwitchUserSharing.Busy, (await SwitchUserSharing.ApplyAsync(pc, shared, [eden], ApiClient.For(pc), Unlocked, null, _ => { }, NothingRunning)).Refusal);
        Assert.Equal(SwitchUserSharing.Busy, (await SwitchUserSharing.UndoAsync(pc, [eden], ApiClient.For(pc), Unlocked, _ => { }, NothingRunning)).Refusal);
        release.Set();
        Assert.Equal("Eden is running. Close it first.", (await first).Refusal);

        // Free again once it is done.
        Assert.Null((await SwitchUserSharing.ApplyAsync(pc, shared, [eden], ApiClient.For(pc), Unlocked, null, _ => { }, NothingRunning)).Refusal);
    }
}
