using System.Net;
using System.Net.Sockets;
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

    /// <summary>One reconcile of <paramref name="config"/>'s game list against the server, as the poller's tick does.</summary>
    private static async Task Reconcile(AgentConfig config)
    {
        config.ManifestCachePath = Path.Combine(Path.GetDirectoryName(config.ConfigPath)!, "manifest.yaml");
        if (!File.Exists(config.ManifestCachePath)) File.WriteAllText(config.ManifestCachePath, "{}\n");
        await using var engine = new SyncEngine(config, ApiClient.For(config));
        await new CommandPoller(config, () => ApiClient.For(config), () => engine, new Detection(config), new NoScan(),
            _ => { }, () => { }).ReconcileGamesAsync();
    }

    private sealed class NoScan : IGameScanner
    {
        public Task<IReadOnlyList<ScanCandidate>> ScanAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ScanCandidate>>(Array.Empty<ScanCandidate>());
    }

    [Fact]
    public async Task A_game_first_enrolled_under_its_title_id_takes_the_name_the_next_machine_knows()
    {
        // WSL first: Ryujinx with no record of the game's name, no ROM, and a title the eShop table has no game of.
        var title = SwitchNamesAndLinksTests.UnknownTitle();
        var ryu = S.Ryujinx(P("wsl/ryu"), RyuUser, (title, RyuUser, RyujinxSaveIndex.SaveType.Account, 5));
        S.Write(Path.Combine(S.RyujinxSave(P("wsl/ryu"), 5), "save.bin"), "wsl");
        var eden = S.Eden(P("pc/Emulation"), EdenUser);
        S.Write(P($"pc/Emulation/roms/switch/Named Later {_id} [{title}][v0].nsp"), "rom");
        S.Write(Path.Combine(S.EdenSave(P("pc/Emulation"), EdenUser, title), "save.bin"), "pc");
        var wsl = await Machine("wsl");
        var pc = await Machine("pc");

        var first = await Enroll(wsl, SwitchEmulators.Scan([ryu], []), title);
        Assert.Equal($"Switch {title}", first.Name);
        var second = await Enroll(pc, SwitchEmulators.Scan([eden], [P("pc/Emulation")]), title);

        Assert.Equal(first.GameId, second.GameId);
        Assert.Equal($"Named Later {_id}", second.Name);
        Assert.Equal($"Named Later {_id}", (await ApiClient.For(pc).ListGamesAsync()).Single(g => g.Id == first.GameId).Name);
        // And the first machine follows on its next tick.
        await Reconcile(AgentConfig.Load(wsl.ConfigPath));
        Assert.Equal($"Named Later {_id}", AgentConfig.Load(wsl.ConfigPath).Games.Single(g => g.GameId == first.GameId).Name);

        // A real name is never replaced: a third machine with only the title ID leaves it alone.
        var deck = await Machine("deck");
        var ryuDeck = S.Ryujinx(P("deck/ryu"), RyuUser, (title, RyuUser, RyujinxSaveIndex.SaveType.Account, 5));
        S.Write(Path.Combine(S.RyujinxSave(P("deck/ryu"), 5), "save.bin"), "deck");
        Assert.Equal($"Named Later {_id}", (await Enroll(deck, SwitchEmulators.Scan([ryuDeck], []), title)).Name);
    }

    [Fact]
    public async Task A_same_named_game_with_no_title_can_be_picked_and_takes_the_title()
    {
        var eden = S.Eden(P("a/Emulation"), EdenUser);
        S.Write(P($"a/Emulation/roms/switch/Picked {_id} [{Title}][v0].nsp"), "rom");
        S.Write(Path.Combine(S.EdenSave(P("a/Emulation"), EdenUser, Title), "save.bin"), "save");
        var a = await Machine("a");
        var byHand = await ApiClient.For(a).CreateGameAsync(new CreateGameRequest($"Picked {_id}", null, null));
        var found = SwitchEmulators.Scan([eden], [P("a/Emulation")]);
        var id = found.ToList().FindIndex(c => c.EmulatorRom == Title);

        var options = EnrollLinks.For(await ApiClient.For(a).ListGamesAsync(), found[id]);
        Assert.Equal((LinkKind.New, $"Picked {_id} (Switch)"), (options[0].Kind, options[0].Name));
        Assert.Contains(options, o => o.GameId == byHand.Id && o.Kind == LinkKind.Join);

        Assert.Equal((1, 0), await Enroller.EnrollAsync(a, found, [id],
            links: new Dictionary<int, LinkChoice> { [id] = new(EnrollLinks.Game, byHand.Id) }));

        var tracked = Enroller.TrackedFor(AgentConfig.Load(a.ConfigPath), found[id])!;
        Assert.Equal(byHand.Id, tracked.GameId);
        Assert.Equal($"switch:{Title}", (await ApiClient.For(a).ListGamesAsync()).Single(g => g.Id == byHand.Id).EmulatorKey);
    }

    [Fact]
    public async Task A_switch_game_takes_its_eshop_icon_with_no_steamgriddb_key()
    {
        using var images = new ImageHost();
        var hash = SwitchTitles.For("0100F2C0115B6000")!.IconHash!;
        images.Images[$"/i/{hash}.jpg"] = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var server = new ServerProcess(new Dictionary<string, string>
        {
            ["Art__AllowedImageHosts"] = "127.0.0.1",
            ["Art__AllowInsecureImageUrls"] = "true",
            ["Art__EshopImageBase"] = images.Url + "/i/",
            ["Storage__ArtRoot"] = P("art"),
        });
        var eden = S.Eden(P("e/Emulation"), EdenUser);
        S.Write(Path.Combine(S.EdenSave(P("e/Emulation"), EdenUser, "0100F2C0115B6000"), "save.bin"), "totk");
        var config = AgentConfig.Load(P("e/state/config.json"));
        config.ServerUrl = server.Url;
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync("eshop" + _id);
        (config.ApiKey, config.MachineId, config.MachineName) = (reg.ApiKey, reg.MachineId, "eshop");
        config.Save();

        var game = await Enroll(config, SwitchEmulators.Scan([eden], [P("e/Emulation")]), "0100F2C0115B6000");

        Assert.Equal("The Legend of Zelda: Tears of the Kingdom", game.Name);
        var icon = (await ApiClient.For(config).ListGamesAsync()).Single(g => g.Id == game.GameId).IconUrl;
        Assert.StartsWith($"/art/{game.GameId:N}/icon.png", icon);
        lock (images.Requested) Assert.Contains($"/i/{hash}.jpg", images.Requested);
    }

    /// <summary>Serves <see cref="Images"/> by path and records what was asked for.</summary>
    private sealed class ImageHost : IDisposable
    {
        private readonly HttpListener _listener = new();
        public Dictionary<string, byte[]> Images { get; } = new();
        public List<string> Requested { get; } = new();
        public string Url { get; }

        public ImageHost()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch (Exception) { return; }
                    lock (Requested) Requested.Add(ctx.Request.Url!.AbsolutePath);
                    if (Images.TryGetValue(ctx.Request.Url.AbsolutePath, out var bytes))
                        await ctx.Response.OutputStream.WriteAsync(bytes);
                    else ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                }
            });
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch (ObjectDisposedException) { }
        }
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
    [Fact]
    public async Task A_device_save_only_one_emulator_has_a_place_for_is_not_copied_again_every_tick()
    {
        var eden = S.Eden(P("pc/Emulation"), EdenUser);
        var ryu = S.Ryujinx(P("pc/ryu"), RyuUser, (Title, RyuUser, RyujinxSaveIndex.SaveType.Account, 2));
        S.Write(Path.Combine(S.EdenSave(P("pc/Emulation"), EdenUser, Title), "save.bin"), "eden");
        S.Write(Path.Combine(S.EdenSave(P("pc/Emulation"), SwitchEmulators.DeviceProfile, Title), "island.bin"), "island");
        Directory.CreateDirectory(S.RyujinxSave(P("pc/ryu"), 2));
        IReadOnlyList<SwitchEmulators.Setup> setups = [eden, ryu];
        var pc = await Machine("pc");
        var game = await Enroll(pc, SwitchEmulators.Scan(setups, []), Title);
        Assert.Contains(game.ExtraPaths, p => p.Key == SwitchEmulators.DeviceFolderKey);

        // Ryujinx has made no device save, so only the main folder goes there — and then the two are the same.
        Assert.Null(SwitchCopies.CopyHere(pc, game, "Ryujinx", _ => { }, setups));
        SwitchCopies.SetKeepInSync(pc, game, "Ryujinx", true, setups);
        Assert.Empty(SwitchCopies.Reconcile(pc, game, _ => { }, setups));
        Assert.Empty(SwitchCopies.Reconcile(pc, game, _ => { }, setups));
        Assert.Equal("same", SwitchCopies.List(pc, game, setups)[1].State);
    }

    [Fact]
    public async Task Turning_keep_in_sync_on_takes_the_save_played_last_without_asking()
    {
        var eden = S.Eden(P("pc/Emulation"), EdenUser);
        var ryu = S.Ryujinx(P("pc/ryu"), RyuUser, (Title, RyuUser, RyujinxSaveIndex.SaveType.Account, 2));
        var edenSave = Path.Combine(S.EdenSave(P("pc/Emulation"), EdenUser, Title), "save.bin");
        var ryuSave = Path.Combine(S.RyujinxSave(P("pc/ryu"), 2), "save.bin");
        S.Write(edenSave, "eden");
        S.Write(ryuSave, "ryu-older");
        File.SetLastWriteTimeUtc(ryuSave, DateTime.UtcNow.AddDays(-2));
        IReadOnlyList<SwitchEmulators.Setup> setups = [eden, ryu];
        var pc = await Machine("pc");
        var game = await Enroll(pc, SwitchEmulators.Scan(setups, []), Title);
        Assert.Equal("older", SwitchCopies.List(pc, game, setups)[1].State);

        // Eden's was played last: it goes to Ryujinx.
        SwitchCopies.SetKeepInSync(pc, game, "Ryujinx", true, setups);
        Assert.Single(SwitchCopies.Reconcile(pc, game, _ => { }, setups));
        Assert.Equal("eden", File.ReadAllText(ryuSave));

        // Off and on again after Ryujinx was played: Ryujinx's comes into Eden's.
        SwitchCopies.SetKeepInSync(pc, game, "Ryujinx", false, setups);
        S.Write(ryuSave, "ryu-newer");
        File.SetLastWriteTimeUtc(ryuSave, DateTime.UtcNow.AddMinutes(5));
        SwitchCopies.SetKeepInSync(pc, game, "Ryujinx", true, setups);
        Assert.Single(SwitchCopies.Reconcile(pc, game, _ => { }, setups));
        Assert.Equal("ryu-newer", File.ReadAllText(edenSave));
        Assert.False(game.EmulatorCopies.Single().Conflict);
    }

    [Fact]
    public async Task Making_a_copy_the_synced_one_changes_nothing_when_it_cannot_be_kept()
    {
        var eden = S.Eden(P("pc/Emulation"), EdenUser);
        var ryu = S.Ryujinx(P("pc/ryu"), RyuUser, (Title, RyuUser, RyujinxSaveIndex.SaveType.Account, 2));
        var edenDir = S.EdenSave(P("pc/Emulation"), EdenUser, Title);
        S.Write(Path.Combine(edenDir, "save.bin"), "eden");
        S.Write(Path.Combine(S.RyujinxSave(P("pc/ryu"), 2), "save.bin"), "ryu");
        // Older, so the scan lists Eden's and Eden's copy is the one that syncs.
        File.SetLastWriteTimeUtc(Path.Combine(S.RyujinxSave(P("pc/ryu"), 2), "save.bin"), DateTime.UtcNow.AddDays(-2));
        IReadOnlyList<SwitchEmulators.Setup> setups = [eden, ryu];
        var pc = await Machine("pc");
        var game = await Enroll(pc, SwitchEmulators.Scan(setups, []), Title);

        // No server to tell: the poller would put the server's folder back, so it isn't moved here either.
        Assert.NotNull(await SwitchCopies.MakeSyncedAsync(pc, game, "Ryujinx", null, _ => { }, setups));
        Assert.Equal(edenDir, AgentConfig.Load(pc.ConfigPath).Games.Single(g => g.GameId == game.GameId).SaveDirectory);

        // A game that keeps a device save, where Ryujinx has made none: refused rather than leaving the device folder behind.
        var other = $"0101{_id}000000";
        var eden2 = S.Eden(P("pc2/Emulation"), EdenUser);
        var ryu2 = S.Ryujinx(P("pc2/ryu"), RyuUser, (other, RyuUser, RyujinxSaveIndex.SaveType.Account, 2));
        var eden2Dir = S.EdenSave(P("pc2/Emulation"), EdenUser, other);
        S.Write(Path.Combine(eden2Dir, "save.bin"), "eden");
        S.Write(Path.Combine(S.EdenSave(P("pc2/Emulation"), SwitchEmulators.DeviceProfile, other), "island.bin"), "island");
        S.Write(Path.Combine(S.RyujinxSave(P("pc2/ryu"), 2), "save.bin"), "ryu");
        // Older, so the scan lists Eden's and Eden's copy is the one that syncs.
        File.SetLastWriteTimeUtc(Path.Combine(S.RyujinxSave(P("pc2/ryu"), 2), "save.bin"), DateTime.UtcNow.AddDays(-2));
        IReadOnlyList<SwitchEmulators.Setup> setups2 = [eden2, ryu2];
        var pc2 = await Machine("pc2");
        var withDevice = await Enroll(pc2, SwitchEmulators.Scan(setups2, []), other);
        Assert.Contains(withDevice.ExtraPaths, p => p.Key == SwitchEmulators.DeviceFolderKey);
        Assert.Contains("device save", await SwitchCopies.MakeSyncedAsync(pc2, withDevice, "Ryujinx", ApiClient.For(pc2), _ => { }, setups2));
        Assert.Equal(eden2Dir, AgentConfig.Load(pc2.ConfigPath).Games.Single(g => g.GameId == withDevice.GameId).SaveDirectory);
    }
}
