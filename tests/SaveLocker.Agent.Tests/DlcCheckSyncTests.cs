using System.Net;
using System.Net.Http.Json;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Phase 5c, the DLC check, against a real server: a push records which DLC and title updates the
/// save was made with, every sync reports what each machine has, and the console reads both. Its own server, because a
/// test here locks the console.
/// </summary>
public sealed class DlcCheckSyncTests : IClassFixture<ServerProcess>, IDisposable
{
    private const string AdminPassword = "dlc-check-admin-pw";
    private const string Xuid = "E030000033894516";
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-dlc-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Random.Shared.Next(0x100000, 0xFFFFFF).ToString("X6");

    public DlcCheckSyncTests(ServerProcess server)
    {
        _server = server;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

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
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync(name + _id, AdminPassword);
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

    /// <summary>A package as Xenia installs it: its folder and its header.</summary>
    private void Package(string machine, string content, string title, string type, string id, string name, int bytes)
    {
        var dir = P(machine, $"{content}/{XeniaSaves.NoProfile}/{title}/{type}/{id}");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "data.bin"), new byte[bytes]);
        var header = P(machine, $"{content}/{XeniaSaves.NoProfile}/{title}/Headers/{type}/{id}.header");
        Directory.CreateDirectory(Path.GetDirectoryName(header)!);
        File.WriteAllBytes(header, XeniaContent.BuildHeader(type == "00000002" ? 2u : 0xB0000u, name, id, Convert.ToUInt32(title, 16)));
    }

    private static async Task<TrackedGame> Enroll(AgentConfig config, IReadOnlyList<ScanCandidate> found, string rom)
    {
        var id = found.ToList().FindIndex(c => c.EmulatorRom == rom);
        Assert.True(id >= 0, $"no candidate for {rom}");
        Assert.Equal((1, 0), await Enroller.EnrollAsync(config, found, new[] { id }));
        return Enroller.TrackedFor(AgentConfig.Load(config.ConfigPath), found[id])!;
    }

    [Fact]
    public async Task A_push_records_the_saves_dlc_and_each_machine_reports_its_own()
    {
        var title = "8D" + _id;
        const string pcContent = "Xenia Canary/content";
        const string deckContent = "SDCard/Emulation/roms/xbox360/content";
        Write("pc", $"{pcContent}/{Xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Package("pc", pcContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);
        Package("pc", pcContent, title, "00000002", "BBBB", "Rally Expansion Pack", 900);
        Package("pc", pcContent, title, "000B0000", "tu00000002_00000000", "Title Update 2", 50);
        Write("deck", $"{deckContent}/{Xuid}/{title}/00000001/Profile/save.dat", "deck-old");
        Package("deck", deckContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);

        var pc = await Machine("pc");
        var deck = await Machine("deck");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        var deckGame = await Enroll(deck, XeniaSaves.Scan([new XeniaFolders(P("deck", deckContent), EmuDeck: true)]), title);

        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await enginePc.PushAsync(pcGame, force: true));
        Assert.Equal(PullOutcome.Restored, await engineDeck.PullAsync(deckGame, force: true));

        // The head says what the save was made with — what the Deck's agent reads before a pull.
        var head = (await ApiClient.For(deck).GetStateAsync(deckGame.GameId))!.Head!;
        Assert.Equal(["dlc/AAAA/Pre-Order Car Pack/300", "dlc/BBBB/Rally Expansion Pack/900", "update/tu00000002_00000000/Title Update 2/50"],
            head.Content!.Select(p => $"{p.Kind}/{p.Id}/{p.Name}/{p.Size}"));

        // The console sees each machine's own.
        using var admin = Admin();
        var content = (await admin.GetFromJsonAsync<GameContentDto>($"/api/games/{pcGame.GameId}/content"))!;
        Assert.StartsWith("pc", content.HeadMachineName);
        Assert.Equal(3, content.Machines.Single(m => m.MachineName.StartsWith("pc")).Packages.Length);
        Assert.Equal(["AAAA"], content.Machines.Single(m => m.MachineName.StartsWith("deck")).Packages.Select(p => p.Id));
    }

    [Fact]
    public async Task A_pull_of_a_save_made_with_dlc_missing_here_is_held_until_pulled_anyway()
    {
        // Phase 5c step 9: the PC's save was made with two packs; the Deck has one. An ordinary pull is held — the
        // Deck's save stays — and says so; "pull anyway" restores it and lifts the hold.
        var title = "9D" + _id;
        const string pcContent = "Xenia Canary/content";
        const string deckContent = "SDCard/Emulation/roms/xbox360/content";
        Write("pc", $"{pcContent}/{Xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Package("pc", pcContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);
        Package("pc", pcContent, title, "00000002", "BBBB", "Rally Expansion Pack", 900);
        Write("deck", $"{deckContent}/{Xuid}/{title}/00000001/Profile/save.dat", "deck-old");
        Package("deck", deckContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);

        var pc = await Machine("pc3");
        var deck = await Machine("deck3");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        var deckGame = await Enroll(deck, XeniaSaves.Scan([new XeniaFolders(P("deck", deckContent), EmuDeck: true)]), title);
        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await enginePc.PushAsync(pcGame, force: true));
        deckGame.LastSyncedHash = deckGame.LocalHash(deck.StateDir); // the Deck's save is in step, so a pull may replace it
        deck.SaveGameSyncState(deckGame);

        Assert.Equal(PullOutcome.HeldForDlc, await engineDeck.PullAsync(deckGame));
        Assert.Equal("deck-old", File.ReadAllText(P("deck", $"{deckContent}/{Xuid}/{title}/00000001/Profile/save.dat")));
        var hold = DlcHoldStore.For(deck).For(deckGame.GameId)!;
        Assert.Equal(["BBBB"], hold.Missing.Select(p => p.Id));

        Assert.Equal(PullOutcome.Restored, await engineDeck.PullAsync(deckGame, skipDlcCheck: true));
        Assert.Equal("pc-save", File.ReadAllText(P("deck", $"{deckContent}/{Xuid}/{title}/00000001/Profile/save.dat")));
        Assert.Null(DlcHoldStore.For(deck).For(deckGame.GameId));
    }

    [Fact]
    public async Task Packs_handed_over_from_the_pc_install_on_the_deck_and_the_held_pull_goes_ahead()
    {
        // Phase 5c steps 12–13, end to end: the Deck's pull is held for a pack only the PC has; the Deck asks; the PC
        // (set to send automatically) zips and uploads it; the Deck downloads, checks and installs it where its Xenia
        // reads it, confirms — which deletes it on the server — and the held pull then restores the PC's save.
        var title = "9E" + _id;
        const string pcContent = "Xenia Canary/content";
        const string deckContent = "SDCard/Emulation/roms/xbox360/content";
        Write("pc", $"{pcContent}/{Xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Package("pc", pcContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);
        Package("pc", pcContent, title, "00000002", "BBBB", "Rally Expansion Pack", 9_000_000);
        Write("deck", $"{deckContent}/{Xuid}/{title}/00000001/Profile/save.dat", "deck-old");
        Package("deck", deckContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);

        var pc = await Machine("pc4");
        var deck = await Machine("deck4");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        var deckGame = await Enroll(deck, XeniaSaves.Scan([new XeniaFolders(P("deck", deckContent), EmuDeck: true)]), title);
        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await enginePc.PushAsync(pcGame, force: true));
        deckGame.LastSyncedHash = deckGame.LocalHash(deck.StateDir);
        deck.SaveGameSyncState(deckGame);
        Assert.Equal(PullOutcome.HeldForDlc, await engineDeck.PullAsync(deckGame));

        var head = (await ApiClient.For(deck).GetStateAsync(deckGame.GameId))!.Head!;
        var missing = DlcCheck.Missing(head.Content, ContentInventory.For(deckGame));
        var asked = await ApiClient.For(deck).CreateTransferAsync(new CreateTransferRequest(deckGame.GameId, head.MachineId!.Value, [.. missing]));

        Assert.Null(pc.DlcSend); // the default sends without asking
        var logs = new List<string>();
        var pcRunner = new DlcHandoverRunner(pc, () => ApiClient.For(pc), () => enginePc, null, logs.Add, () => false);
        var deckRunner = new DlcHandoverRunner(deck, () => ApiClient.For(deck), () => engineDeck, null, logs.Add, () => false);
        await pcRunner.RunAsync();
        Assert.Equal(TransferStatus.Ready, (await ApiClient.For(deck).ListTransfersAsync()).Single(t => t.Id == asked.Id).Status);

        await deckRunner.RunAsync();
        var installed = P("deck", $"{deckContent}/{XeniaSaves.NoProfile}/{title}/00000002/BBBB/data.bin");
        Assert.Equal(9_000_000, new FileInfo(installed).Length);
        Assert.True(File.Exists(P("deck", $"{deckContent}/{XeniaSaves.NoProfile}/{title}/Headers/00000002/BBBB.header")));
        Assert.DoesNotContain(await ApiClient.For(deck).ListTransfersAsync(), t => t.Id == asked.Id); // delivered, deleted

        await deckRunner.RunAsync(); // the hold check: every pack is here now
        Assert.Equal("pc-save", File.ReadAllText(P("deck", $"{deckContent}/{Xuid}/{title}/00000001/Profile/save.dat")));
        Assert.Null(DlcHoldStore.For(deck).For(deckGame.GameId));
        Assert.False(Directory.Exists(Path.Combine(deck.StateDir, "handover", "in", asked.Id.ToString("N"))));
    }

    [Fact]
    public async Task The_console_asks_for_a_machine_and_its_agent_installs_what_it_lacked()
    {
        // The console's Emulator settings: "Send DeckTest's missing DLC from WinTest". The server works out what the Deck
        // lacks from what it reported, asks the machine that made the latest save, and the Deck takes it as its own request.
        var title = "9D" + _id;
        const string pcContent = "Xenia Canary/content";
        const string deckContent = "SDCard/Emulation/roms/xbox360/content";
        Write("pc", $"{pcContent}/{Xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Package("pc", pcContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);
        Package("pc", pcContent, title, "00000002", "BBBB", "Rally Expansion Pack", 5000);
        Write("deck", $"{deckContent}/{Xuid}/{title}/00000001/Profile/save.dat", "deck-old");
        Package("deck", deckContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);

        var pc = await Machine("pc6");
        var deck = await Machine("deck6");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        var deckGame = await Enroll(deck, XeniaSaves.Scan([new XeniaFolders(P("deck", deckContent), EmuDeck: true)]), title);
        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await enginePc.PushAsync(pcGame, force: true));
        await ApiClient.For(deck).ReportContentAsync(deckGame.GameId, [.. ContentInventory.For(deckGame)], null);

        using var admin = Admin();
        var resp = await admin.PostAsJsonAsync($"/api/games/{deckGame.GameId}/transfers", new ConsoleTransferRequest(deck.MachineId!.Value));
        resp.EnsureSuccessStatusCode();
        var asked = (await resp.Content.ReadFromJsonAsync<ContentTransferDto>())!;
        Assert.Equal(pc.MachineId, asked.FromMachineId);
        Assert.Equal(["BBBB"], asked.Parts.Select(p => p.PackageId));
        var again = await admin.PostAsJsonAsync($"/api/games/{deckGame.GameId}/transfers", new ConsoleTransferRequest(deck.MachineId!.Value));
        Assert.Equal(asked.Id, (await again.Content.ReadFromJsonAsync<ContentTransferDto>())!.Id);
        var self = await admin.PostAsJsonAsync($"/api/games/{deckGame.GameId}/transfers", new ConsoleTransferRequest(pc.MachineId!.Value));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);

        await new DlcHandoverRunner(pc, () => ApiClient.For(pc), () => enginePc, null, _ => { }, () => false).RunAsync();
        await new DlcHandoverRunner(deck, () => ApiClient.For(deck), () => engineDeck, null, _ => { }, () => false).RunAsync();
        Assert.Equal(5000, new FileInfo(P("deck", $"{deckContent}/{XeniaSaves.NoProfile}/{title}/00000002/BBBB/data.bin")).Length);
    }

    [Fact]
    public async Task A_machine_set_to_never_send_declines_and_the_asker_hears_once()
    {
        var title = "9F" + _id;
        const string pcContent = "Xenia Canary/content";
        Write("pc", $"{pcContent}/{Xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Package("pc", pcContent, title, "00000002", "BBBB", "Rally Expansion Pack", 900);
        var pc = await Machine("pc5");
        var deck = await Machine("deck5");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await enginePc.PushAsync(pcGame, force: true);
        var asked = await ApiClient.For(deck).CreateTransferAsync(new CreateTransferRequest(pcGame.GameId, pc.MachineId!.Value,
            [new ContentPackageDto(ContentPackageDto.Dlc, "BBBB", "Rally Expansion Pack", 900)]));

        pc.DlcSend = DlcSendPolicy.Never;
        await new DlcHandoverRunner(pc, () => ApiClient.For(pc), () => enginePc, null, _ => { }, () => false).RunAsync();
        Assert.Equal(TransferStatus.Declined, (await ApiClient.For(deck).ListTransfersAsync()).Single(t => t.Id == asked.Id).Status);
    }

    [Fact]
    public void A_received_zip_installs_only_the_package_it_names_and_only_at_the_listed_size()
    {
        var content = P("x", "content");
        Package("src", "content", "4D5309C9", "00000002", "CCCC", "Car Pack", 1000);
        var pack = new ContentPackageDto(ContentPackageDto.Dlc, "CCCC", "Car Pack", 1000);
        var zip = Path.Combine(_dir, "pack.zip");
        DlcPackageFiles.Zip(P("src", "content"), "4D5309C9", pack, zip);

        // Listed at another size: refused, nothing left behind.
        Assert.Throws<InvalidDataException>(() => DlcPackageFiles.Install(zip, CreateDir(content), "4D5309C9", legacy: true, pack with { Size = 999 }));
        Assert.Empty(Directory.EnumerateFileSystemEntries(content));

        // The layout from before profiles reads packages from content/<TID>.
        DlcPackageFiles.Install(zip, content, "4D5309C9", legacy: true, pack);
        Assert.True(File.Exists(Path.Combine(content, "4D5309C9", "00000002", "CCCC", "data.bin")));
        Assert.Equal("Car Pack", XeniaContent.ReadHeader(Path.Combine(content, "4D5309C9", "Headers", "00000002", "CCCC.header"))!.Value.Name);

        // A zip that reaches outside the package is refused.
        var evil = Path.Combine(_dir, "evil.zip");
        using (var z = System.IO.Compression.ZipFile.Open(evil, System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("00000002/DDDD/../../../escape.txt").Open())) w.Write("x");
        Assert.Throws<InvalidDataException>(() => DlcPackageFiles.Install(evil, content, "4D5309C9", legacy: false,
            new ContentPackageDto(ContentPackageDto.Dlc, "DDDD", "Evil", 1)));
        Assert.False(File.Exists(Path.Combine(_dir, "x", "escape.txt")));
    }

    private static string CreateDir(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void The_check_matches_dlc_by_kind_and_id_and_any_title_update_counts()
    {
        ContentPackageDto[] head =
        [
            new(ContentPackageDto.Dlc, "AAAA", "Pre-Order Car Pack", 52 * 1024 * 1024),
            new(ContentPackageDto.Dlc, "BBBB", "Rally Expansion Pack", 1028L * 1024 * 1024),
            new(ContentPackageDto.Update, "tu00000002_00000000", "Title Update 2", 15 * 1024 * 1024),
        ];
        ContentPackageDto[] noUpdate = [new(ContentPackageDto.Dlc, "aaaa", "Pre-Order Car Pack", 52 * 1024 * 1024)];
        // A title update named BBBB is no DLC BBBB; but any title update stands for the one the save was made with, since
        // each replaces the last and nothing says which is newer.
        ContentPackageDto[] updated = [.. noUpdate, new(ContentPackageDto.Update, "BBBB", "Title Update 3", 1)];

        var missing = DlcCheck.Missing(head, noUpdate);
        Assert.Equal(["BBBB", "tu00000002_00000000"], missing.Select(p => p.Id));
        Assert.Equal("2 DLC packs (Rally Expansion Pack, Title Update 2, 1043 MB)", DlcCheck.Summary(missing));
        Assert.Equal(["BBBB"], DlcCheck.Missing(head, updated).Select(p => p.Id));
        Assert.Empty(DlcCheck.Missing(null, noUpdate));
        var described = DlcCheck.Describe(new SaveVersionDto(Guid.NewGuid(), Guid.NewGuid(), null, "PC", DateTime.UtcNow, "h", 1, null, Content: head), updated, held: true);
        Assert.Equal(1, described.MissingCount);
        Assert.Equal([true, false, true], described.Packages!.Select(p => p.Here));
        Assert.Null(DlcCheck.Describe(new SaveVersionDto(Guid.NewGuid(), Guid.NewGuid(), null, "PC", DateTime.UtcNow, "h", 1, null), noUpdate, false).Packages);
    }

    [Fact]
    public async Task A_save_this_machine_made_or_already_has_is_never_held()
    {
        // The PC made the save with a pack it later removed: its own sync must not hold its own save, nor say this
        // machine lacks something for it.
        var title = "9A" + _id;
        const string pcContent = "Xenia Canary/content";
        Write("pc", $"{pcContent}/{Xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Package("pc", pcContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);
        var pc = await Machine("pc7");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        Assert.NotNull(await enginePc.PushAsync(pcGame, force: true));
        Directory.Delete(P("pc", $"{pcContent}/{XeniaSaves.NoProfile}/{title}/00000002/AAAA"), recursive: true);

        Assert.Equal(PullOutcome.UpToDate, await enginePc.PullAsync(pcGame));
        Assert.Null(DlcHoldStore.For(pc).For(pcGame.GameId));
    }

    [Fact]
    public async Task A_launch_whose_pull_is_held_says_it_starts_with_this_machines_save()
    {
        var title = "9B" + _id;
        const string pcContent = "Xenia Canary/content";
        const string deckContent = "SDCard/Emulation/roms/xbox360/content";
        Write("pc", $"{pcContent}/{Xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Package("pc", pcContent, title, "00000002", "BBBB", "Rally Expansion Pack", 900);
        Write("deck", $"{deckContent}/{Xuid}/{title}/00000001/Profile/save.dat", "deck-old");
        var pc = await Machine("pc8");
        var deck = await Machine("deck8");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        var deckGame = await Enroll(deck, XeniaSaves.Scan([new XeniaFolders(P("deck", deckContent), EmuDeck: true)]), title);
        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await using var engineDeck = new SyncEngine(deck, ApiClient.For(deck));
        Assert.NotNull(await enginePc.PushAsync(pcGame, force: true));
        deckGame.LastSyncedHash = deckGame.LocalHash(deck.StateDir);
        deck.SaveGameSyncState(deckGame);

        var gate = await engineDeck.PrepareLaunchAsync(deckGame, canAsk: true);
        await engineDeck.ReleaseLaunchLeaseAsync(deckGame);

        Assert.Equal(LaunchDecision.ProceedSyncPaused, gate.Decision);
        Assert.Null(gate.HolderMachineName);
        Assert.Contains("DLC", gate.Reason);
        Assert.Equal("deck-old", File.ReadAllText(P("deck", $"{deckContent}/{Xuid}/{title}/00000001/Profile/save.dat")));
    }

    [Fact]
    public async Task A_request_for_a_pack_not_installed_here_is_cancelled_and_nothing_else_is_read()
    {
        // The sender reads only packs it lists itself, whatever the request names.
        var title = "9C" + _id;
        const string pcContent = "Xenia Canary/content";
        Write("pc", $"{pcContent}/{Xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Package("pc", pcContent, title, "00000002", "BBBB", "Rally Expansion Pack", 900);
        var pc = await Machine("pc9");
        var deck = await Machine("deck9");
        var pcGame = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        await using var enginePc = new SyncEngine(pc, ApiClient.For(pc));
        await enginePc.PushAsync(pcGame, force: true);
        var asked = await ApiClient.For(deck).CreateTransferAsync(new CreateTransferRequest(pcGame.GameId, pc.MachineId!.Value,
            [new ContentPackageDto(ContentPackageDto.Dlc, "BBBB", "Rally Expansion Pack", 900)]));
        Directory.Delete(P("pc", $"{pcContent}/{XeniaSaves.NoProfile}/{title}/00000002/BBBB"), recursive: true);

        await new DlcHandoverRunner(pc, () => ApiClient.For(pc), () => enginePc, null, _ => { }, () => false).RunAsync();

        Assert.Equal(TransferStatus.Cancelled, (await ApiClient.For(deck).ListTransfersAsync()).Single(t => t.Id == asked.Id).Status);
    }

    [Fact]
    public void A_received_zip_with_a_name_twice_or_more_bytes_than_listed_is_refused_before_anything_is_written()
    {
        var content = CreateDir(P("y", "content"));
        var pack = new ContentPackageDto(ContentPackageDto.Dlc, "EEEE", "Car Pack", 10);
        var header = XeniaContent.BuildHeader(2, "Car Pack", "EEEE", 0x4D5309C9);

        var twice = Path.Combine(_dir, "twice.zip");
        using (var z = System.IO.Compression.ZipFile.Open(twice, System.IO.Compression.ZipArchiveMode.Create))
        {
            for (var i = 0; i < 2; i++)
                using (var w = z.CreateEntry("00000002/EEEE/data.bin").Open()) w.Write(new byte[5]);
            using (var h = z.CreateEntry("Headers/00000002/EEEE.header").Open()) h.Write(header);
        }
        Assert.Throws<InvalidDataException>(() => DlcPackageFiles.Install(twice, content, "4D5309C9", legacy: false, pack));

        var big = Path.Combine(_dir, "big.zip");
        using (var z = System.IO.Compression.ZipFile.Open(big, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var w = z.CreateEntry("00000002/EEEE/data.bin").Open()) w.Write(new byte[1024 * 1024]);
            using (var h = z.CreateEntry("Headers/00000002/EEEE.header").Open()) h.Write(header);
        }
        Assert.Throws<InvalidDataException>(() => DlcPackageFiles.Install(big, content, "4D5309C9", legacy: false, pack));
        Assert.Empty(Directory.EnumerateFileSystemEntries(content));
    }

    [Fact]
    public async Task A_report_never_rewrites_what_a_save_was_made_with()
    {
        var title = "8E" + _id;
        const string pcContent = "Xenia Canary/content";
        Write("pc", $"{pcContent}/{Xuid}/{title}/00000001/Profile/save.dat", "pc-save");
        Package("pc", pcContent, title, "00000002", "AAAA", "Pre-Order Car Pack", 300);
        var pc = await Machine("pc2");
        var game = await Enroll(pc, XeniaSaves.Scan([new XeniaFolders(P("pc", pcContent))]), title);
        await using var engine = new SyncEngine(pc, ApiClient.For(pc));
        var pushed = (await engine.PushAsync(game, force: true))!.Version!;

        // Reported again for the same version with another list: the version keeps the list it was made with.
        await ApiClient.For(pc).ReportContentAsync(game.GameId, [new ContentPackageDto(ContentPackageDto.Dlc, "ZZZZ", "Other", 1)], pushed.Id);

        var head = (await ApiClient.For(pc).GetStateAsync(game.GameId))!.Head!;
        Assert.Equal(["AAAA"], head.Content!.Select(p => p.Id));
    }
}
