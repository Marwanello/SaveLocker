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
