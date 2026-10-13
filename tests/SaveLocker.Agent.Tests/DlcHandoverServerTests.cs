using System.Net;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Phase 5c step 11, the DLC hand-over on a real server: asked by the machine missing packs, approved
/// by the machine that has them, uploaded in pieces that resume, fetched only by the asking machine, and deleted the
/// moment it confirms. Its own server: these tests read the archive root's hand-over folder.
/// </summary>
public sealed class DlcHandoverServerTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-handover-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Random.Shared.Next(0x100000, 0xFFFFFF).ToString("X6");

    public DlcHandoverServerTests(ServerProcess server)
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

    private static readonly ContentPackageDto Rally = new(ContentPackageDto.Dlc, "BBBB", "Rally Expansion Pack", 900);

    private async Task<(ApiClient Pc, ApiClient Deck, Guid PcId, Guid GameId)> SetUp()
    {
        var pc = await Machine("pc");
        var deck = await Machine("deck");
        var pcApi = ApiClient.For(pc);
        var game = await pcApi.CreateGameAsync(new CreateGameRequest("Handover Racer " + _id, null, null));
        await pcApi.ReportContentAsync(game.Id, [Rally], null);
        return (pcApi, ApiClient.For(deck), pc.MachineId!.Value, game.Id);
    }

    [Fact]
    public async Task A_pack_goes_from_the_machine_that_has_it_to_the_one_that_asked_and_is_deleted_on_delivery()
    {
        var (pc, deck, pcId, gameId) = await SetUp();
        var zip = new byte[100_000];
        Random.Shared.NextBytes(zip);

        var asked = await deck.CreateTransferAsync(new CreateTransferRequest(gameId, pcId, [Rally]));
        Assert.Equal(TransferStatus.Requested, asked.Status);
        Assert.Equal(asked.Id, Assert.Single(await pc.ListTransfersAsync()).Id); // the PC sees it on its next poll
        Assert.Equal(TransferStatus.Approved, (await pc.AnswerTransferAsync(asked.Id, approve: true)).Status);

        // Two pieces; the second one first is refused, so an interrupted sender resumes where the server says.
        await Assert.ThrowsAsync<HttpRequestException>(() => pc.UploadTransferPartAsync(asked.Id, "BBBB", 60_000, zip.Length, new MemoryStream(zip, 60_000, 40_000)));
        var half = await pc.UploadTransferPartAsync(asked.Id, "BBBB", 0, zip.Length, new MemoryStream(zip, 0, 60_000));
        Assert.Equal(60_000, half.Parts.Single().Received);
        var ready = await pc.UploadTransferPartAsync(asked.Id, "BBBB", 60_000, zip.Length, new MemoryStream(zip, 60_000, 40_000));
        Assert.Equal(TransferStatus.Ready, ready.Status);

        // Only the asking machine may fetch it, and a fetch resumes.
        await Assert.ThrowsAsync<HttpRequestException>(() => pc.DownloadTransferPartAsync(asked.Id, "BBBB", new MemoryStream(), 0));
        var got = new MemoryStream();
        got.Write(zip, 0, 30_000);
        await deck.DownloadTransferPartAsync(asked.Id, "BBBB", got, 30_000);
        Assert.Equal(zip, got.ToArray());

        Assert.Equal(TransferStatus.Delivered, (await deck.TransferDeliveredAsync(asked.Id)).Status);
        Assert.False(Directory.Exists(Path.Combine(_server.State, "archives", ContentTransferServiceDir, asked.Id.ToString("N"))));
        Assert.Empty(await deck.ListTransfersAsync());
    }

    // The folder name the server keeps hand-overs in, under its archive root.
    private const string ContentTransferServiceDir = "_handover";

    [Fact]
    public async Task A_request_is_refused_for_packs_the_other_machine_never_reported_or_over_the_limit()
    {
        var (_, deck, pcId, gameId) = await SetUp();

        var unknown = await Assert.ThrowsAsync<HttpRequestException>(() => deck.CreateTransferAsync(new CreateTransferRequest(gameId, pcId,
            [new ContentPackageDto(ContentPackageDto.Dlc, "ZZZZ", "Not There", 1)])));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var huge = await Assert.ThrowsAsync<HttpRequestException>(() => deck.CreateTransferAsync(new CreateTransferRequest(gameId, pcId,
            [Rally with { Size = 9L * 1024 * 1024 * 1024 }])));
        Assert.Contains("hand-over limit", huge.Message);
    }

    [Fact]
    public async Task Declined_or_cancelled_nothing_can_be_uploaded_or_fetched()
    {
        var (pc, deck, pcId, gameId) = await SetUp();

        var asked = await deck.CreateTransferAsync(new CreateTransferRequest(gameId, pcId, [Rally]));
        Assert.Equal(TransferStatus.Declined, (await pc.AnswerTransferAsync(asked.Id, approve: false)).Status);
        await Assert.ThrowsAsync<HttpRequestException>(() => pc.UploadTransferPartAsync(asked.Id, "BBBB", 0, 10, new MemoryStream(new byte[10])));
        Assert.Empty(await pc.ListTransfersAsync());
        Assert.Equal(TransferStatus.Declined, Assert.Single(await deck.ListTransfersAsync()).Status); // so the Deck can say so

        var again = await deck.CreateTransferAsync(new CreateTransferRequest(gameId, pcId, [Rally]));
        Assert.NotEqual(asked.Id, again.Id);
        await deck.CancelTransferAsync(again.Id);
        await Assert.ThrowsAsync<HttpRequestException>(() => pc.AnswerTransferAsync(again.Id, approve: true));
    }
}
