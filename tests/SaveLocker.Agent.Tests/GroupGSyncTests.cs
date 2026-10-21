using System.Net.Http.Json;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Group G end to end against a real server (tasks/emulator-saves Phase 17): two dumps of one ROM — the PC's
/// <c>… (USA).srm</c>, the Deck's <c>… (USA) (Rev 1).srm</c> — linked as one game with the Deck keeping its own names; the
/// first sync asking which save wins; new state slots, a restore of an old version and the hash all under each machine's
/// own names; the refusals; and merging two games on the server.
/// </summary>
public sealed class GroupGSyncTests : IClassFixture<ServerProcess>, IDisposable
{
    private const string AdminPassword = "group-g-admin-password";
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-groupg-" + Guid.NewGuid().ToString("N"));
    private readonly string _game = "Chrono " + Guid.NewGuid().ToString("N")[..6];

    public GroupGSyncTests(ServerProcess server)
    {
        _server = server;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Usa => $"{_game} (USA)";
    private string Rev1 => $"{_game} (USA) (Rev 1)";

    private HttpClient Admin()
    {
        var http = new HttpClient { BaseAddress = new Uri(_server.Url) };
        http.DefaultRequestHeaders.Add("X-Admin-Password", AdminPassword);
        return http;
    }

    private async Task<AgentConfig> Machine(string name)
    {
        using (var http = Admin())
            await http.PostAsJsonAsync("/api/admin/password", new SetAdminPasswordRequest(AdminPassword));
        var config = AgentConfig.Load(Path.Combine(_dir, name, "state", "config.json"));
        config.ServerUrl = _server.Url;
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync(name + Guid.NewGuid().ToString("N")[..6], AdminPassword);
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

    private IReadOnlyList<ScanCandidate> Scan(string machine) =>
        RetroArchSaves.Scan(RetroArchConfig.Folders([P(machine, "Emulation")], []), [P(machine, "Emulation")]);

    private const string Saves = "Emulation/saves/retroarch/saves";
    private const string States = "Emulation/saves/retroarch/states";

    /// <summary>The PC has the USA dump's save and a state, pushed; the Deck has the Rev 1 dump's own save and state.</summary>
    private async Task<(AgentConfig Pc, AgentConfig Deck, TrackedGame PcGame, GameDto Server)> TwoDumps()
    {
        Write("pc", $"{Saves}/{Usa}.srm", "pc-save");
        Write("pc", $"{States}/{Usa}.state1", "pc-slot1");
        Write("deck", $"{Saves}/{Rev1}.srm", "deck-save");
        Write("deck", $"{States}/{Rev1}.state2", "deck-slot2");

        var pc = await Machine("pc");
        var deck = await Machine("deck");
        var pcFound = Scan("pc");
        Assert.Equal((1, 0), await Enroller.EnrollAsync(pc, pcFound, [0]));
        var pcGame = Enroller.TrackedFor(AgentConfig.Load(pc.ConfigPath), pcFound[0])!;
        await using (var engine = new SyncEngine(pc, ApiClient.For(pc)))
            Assert.NotNull(await engine.PushAsync(pcGame, force: true));
        var server = (await ApiClient.For(deck).ListGamesAsync()).Single(g => g.Id == pcGame.GameId);
        return (pc, deck, pcGame, server);
    }

    [Fact]
    public async Task A_second_dump_links_under_its_own_names_and_the_first_sync_asks_which_save_wins()
    {
        var (pc, deck, pcGame, server) = await TwoDumps();

        var found = Scan("deck");
        // Another revision of the same release joins by itself (tasks/emulator-pull-safety Phase 8, B): no Change needed.
        var option = EnrollLinks.For([server], found[0])[0];
        Assert.Equal((EnrollLinks.Auto, LinkKind.Join, server.Id, "Another revision"), (option.Choice, option.Kind, option.GameId, option.Badge));
        Assert.Equal((1, 0), await Enroller.EnrollAsync(deck, found, [0]));

        deck = AgentConfig.Load(deck.ConfigPath);
        var deckGame = Assert.Single(deck.Games, g => g.IsEnrolledHere);
        Assert.Equal(pcGame.GameId, deckGame.GameId);
        // One pair per file of the save's scope: the save and its clock file.
        Assert.Equal([new FileNameDto(SaveRoot.PrimaryKey, $"{Usa}.srm", $"{Rev1}.srm"), new FileNameDto(SaveRoot.PrimaryKey, $"{Usa}.rtc", $"{Rev1}.rtc")],
            deckGame.OwnFileNames);
        // Add games now shows the Deck's save as added: it is that game, under the Deck's names.
        Assert.Equal(deckGame.GameId, Enroller.TrackedFor(deck, found[0])?.GameId);

        // The two saves differ: a pull does not overwrite the Deck's, and its push is a conflict, not an overwrite.
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.Equal(PullOutcome.Refused, await engineDeck.PullAsync(deckGame));
        Assert.Equal("deck-save", Read("deck", $"{Saves}/{Rev1}.srm"));
        Assert.Equal(UploadStatus.Conflict, (await engineDeck.PushAsync(deckGame))?.Status);

        // The user keeps the cloud's: it lands under the Deck's names, and the two machines agree they are in step.
        await engineDeck.PullAsync(deckGame, force: true);
        Assert.Equal("pc-save", Read("deck", $"{Saves}/{Rev1}.srm"));
        Assert.Equal("pc-slot1", Read("deck", $"{States}/{Rev1}.state1"));
        Assert.False(File.Exists(P("deck", $"{Saves}/{Usa}.srm")));
        Assert.Equal(pcGame.LocalHash(pc.StateDir), deckGame.LocalHash(deck.StateDir));

        // A new slot on the Deck reaches the PC under the PC's name, and joins the Deck's list.
        Write("deck", $"{States}/{Rev1}.state3", "deck-slot3");
        Assert.Equal(UploadStatus.Created, (await engineDeck.PushAsync(deckGame))?.Status);
        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await enginePc.PullAsync(pcGame, force: true);
        Assert.Equal("deck-slot3", Read("pc", $"{States}/{Usa}.state3"));
        Assert.Contains(AgentConfig.Load(deck.ConfigPath).Games.Single(g => g.GameId == deckGame.GameId).OwnFileNames,
            n => n.Local == $"{Rev1}.state3" && n.Server == $"{Usa}.state3");

        // The console sees the Deck's names on its row.
        using var admin = Admin();
        var paths = await admin.GetFromJsonAsync<MachineSavePathDto[]>($"/api/games/{server.Id}/paths");
        Assert.Contains(paths!, p => p.MachineId == deck.MachineId && p.FileNames?.Any(n => n.Local == $"{Rev1}.srm") == true);
    }

    [Fact]
    public async Task An_old_version_restored_on_the_renamed_side_lands_under_its_names()
    {
        var (pc, deck, pcGame, server) = await TwoDumps();
        var found = Scan("deck");
        await Enroller.EnrollAsync(deck, found, [0], links: new Dictionary<int, LinkChoice> { [0] = new(EnrollLinks.Rename, server.Id) });
        deck = AgentConfig.Load(deck.ConfigPath);
        var deckGame = deck.Games.Single(g => g.IsEnrolledHere);
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        await engineDeck.PullAsync(deckGame, force: true);
        var first = server.Id;

        // The PC saves again; the Deck follows; then the console puts the first version back.
        Write("pc", $"{Saves}/{Usa}.srm", "pc-save-2");
        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        Assert.Equal(UploadStatus.Created, (await enginePc.PushAsync(pcGame))?.Status);
        await engineDeck.PullAsync(deckGame);
        Assert.Equal("pc-save-2", Read("deck", $"{Saves}/{Rev1}.srm"));

        using var admin = Admin();
        var versions = await admin.GetFromJsonAsync<SaveVersionDto[]>($"/api/games/{first}/versions");
        var oldest = versions!.OrderBy(v => v.CreatedAt).First();
        (await admin.PostAsync($"/api/games/{first}/set-latest?version={oldest.Id}", null)).EnsureSuccessStatusCode();
        await engineDeck.PullAsync(deckGame);
        Assert.Equal("pc-save", Read("deck", $"{Saves}/{Rev1}.srm"));
        Assert.False(File.Exists(P("deck", $"{Saves}/{Usa}.srm")));
    }

    [Fact]
    public async Task Another_emulators_game_a_clash_and_a_second_link_are_refused()
    {
        var (_, deck, _, server) = await TwoDumps();

        // A melonDS save of the same title: same emulator only.
        var melon = new ScanCandidate(_game, P("deck", "melon"), ScanSource.Emulator, false, EmulatorName: MelonDsSaves.EmulatorName,
            EmulatorRom: Rev1, IncludeGlobs: [$"{Rev1}.sav"]);
        Assert.Null(EnrollLinks.RenamePairs(server, melon, out _));

        // The Deck also has the USA dump's save: two saves would land on one file.
        Write("deck", $"{Saves}/{Usa}.srm", "deck-usa");
        var found = Scan("deck").Single(c => c.EmulatorRom == Rev1);
        Assert.Null(EnrollLinks.RenamePairs(server, found, out var why));
        Assert.Contains("also has", why);
        File.Delete(P("deck", $"{Saves}/{Usa}.srm"));

        // So does a state of the USA dump played from states alone: the swap would give the Deck's own state its name.
        Write("deck", $"{States}/{Usa}.state1", "deck-usa-slot");
        found = Scan("deck").Single(c => c.EmulatorRom == Rev1);
        Assert.Null(EnrollLinks.RenamePairs(server, found, out why));
        Assert.Contains($"{Usa}.state1", why);
        File.Delete(P("deck", $"{States}/{Usa}.state1"));

        // Linked once; a second dump here cannot link to the same game.
        found = Scan("deck").Single(c => c.EmulatorRom == Rev1);
        Assert.Equal((1, 0), await Enroller.EnrollAsync(deck, [found], [0],
            links: new Dictionary<int, LinkChoice> { [0] = new(EnrollLinks.Rename, server.Id) }));
        Write("deck", $"{Saves}/{_game} (USA) (Rev 2).srm", "rev2");
        var rev2 = Scan("deck").Single(c => c.EmulatorRom == $"{_game} (USA) (Rev 2)");
        Assert.Equal((0, 1), await Enroller.EnrollAsync(AgentConfig.Load(deck.ConfigPath), [rev2], [0],
            links: new Dictionary<int, LinkChoice> { [0] = new(EnrollLinks.Rename, server.Id) }));
    }

    [Fact]
    public async Task A_merged_games_versions_become_history_and_its_machines_move()
    {
        // Two games of the same files: the PC's, and the Deck's kept apart by hand ("Keep it as its own game").
        Write("pc", $"{Saves}/{Usa}.srm", "pc-save");
        Write("deck", $"{Saves}/{Usa}.srm", "deck-save");
        var pc = await Machine("pc");
        var deck = await Machine("deck");
        var pcFound = Scan("pc");
        await Enroller.EnrollAsync(pc, pcFound, [0]);
        var pcGame = Enroller.TrackedFor(AgentConfig.Load(pc.ConfigPath), pcFound[0])!;
        await using (var e = new SyncEngine(pc, ApiClient.For(pc))) await e.PushAsync(pcGame, force: true);
        var deckFound = Scan("deck");
        await Enroller.EnrollAsync(deck, deckFound, [0], links: new Dictionary<int, LinkChoice> { [0] = new(EnrollLinks.Separate) });
        deck = AgentConfig.Load(deck.ConfigPath);
        var copy = deck.Games.Single(g => g.IsEnrolledHere);
        Assert.NotEqual(pcGame.GameId, copy.GameId);
        await using (var e = new SyncEngine(deck, ApiClient.For(deck))) await e.PushAsync(copy, force: true);

        using var admin = Admin();
        var preview = await admin.GetFromJsonAsync<MergePreviewDto>($"/api/games/{copy.GameId}/merge-preview?into={pcGame.GameId}");
        Assert.Null(preview!.Refusal);
        Assert.Equal(1, preview.Versions);
        Assert.Equal(MergeOutcomes.Moved, Assert.Single(preview.Machines).Outcome);

        // The name must be typed.
        Assert.False((await admin.PostAsJsonAsync($"/api/games/{copy.GameId}/merge", new MergeGamesRequest(pcGame.GameId, "wrong"))).IsSuccessStatusCode);
        (await admin.PostAsJsonAsync($"/api/games/{copy.GameId}/merge", new MergeGamesRequest(pcGame.GameId, copy.Name))).EnsureSuccessStatusCode();

        var versions = await admin.GetFromJsonAsync<SaveVersionDto[]>($"/api/games/{pcGame.GameId}/versions");
        var merged = Assert.Single(versions!, v => v.MergedFrom == copy.Name);
        Assert.True(merged.Protected);
        Assert.False((await admin.PostAsync($"/api/games/{pcGame.GameId}/set-latest?version={merged.Id}", null)).IsSuccessStatusCode);

        // The Deck's poller moves its copy to the game it was merged into, with its folder.
        await Reconcile(deck);
        var moved = Assert.Single(AgentConfig.Load(deck.ConfigPath).Games, g => g.IsEnrolledHere);
        Assert.Equal(pcGame.GameId, moved.GameId);
        Assert.Equal(copy.SaveDirectory, moved.SaveDirectory);

        // Its save differs from the kept game's Latest: the next pull refuses rather than taking the PC's save over it, and
        // its push is a conflict that asks which to keep.
        deck = AgentConfig.Load(deck.ConfigPath);
        moved = deck.Games.Single(g => g.IsEnrolledHere);
        await using (var e = new SyncEngine(deck, ApiClient.For(deck)))
        {
            Assert.Equal(PullOutcome.Refused, await e.PullAsync(moved));
            Assert.Equal("deck-save", Read("deck", $"{Saves}/{Usa}.srm"));
            Assert.Equal(UploadStatus.Conflict, (await e.PushAsync(moved))?.Status);
        }
        Assert.Contains(await admin.GetFromJsonAsync<ConflictDto[]>("/api/conflicts") ?? [], c => c.GameId == pcGame.GameId);
    }

    [Fact]
    public async Task A_merge_is_refused_while_the_kept_game_has_an_open_conflict()
    {
        var (pc, deck, pcGame, server) = await TwoDumps();
        // A second PC game of other files, and a conflict on the first one.
        Write("pc", $"{Saves}/Other {_game}.srm", "other");
        var other = Scan("pc").Single(c => c.EmulatorRom == $"Other {_game}");
        Assert.Equal((1, 0), await Enroller.EnrollAsync(AgentConfig.Load(pc.ConfigPath), [other], [0]));
        var otherGame = Enroller.TrackedFor(AgentConfig.Load(pc.ConfigPath), other)!;

        var found = Scan("deck").Single(c => c.EmulatorRom == Rev1);
        Assert.Equal((1, 0), await Enroller.EnrollAsync(deck, [found], [0],
            links: new Dictionary<int, LinkChoice> { [0] = new(EnrollLinks.Rename, server.Id) }));
        deck = AgentConfig.Load(deck.ConfigPath);
        await using (var e = new SyncEngine(deck, ApiClient.For(deck)))
            Assert.Equal(UploadStatus.Conflict, (await e.PushAsync(deck.Games.Single(g => g.GameId == server.Id)))?.Status);

        using var admin = Admin();
        var preview = await admin.GetFromJsonAsync<MergePreviewDto>($"/api/games/{otherGame.GameId}/merge-preview?into={pcGame.GameId}");
        Assert.Contains("open conflict", preview!.Refusal);
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
}
