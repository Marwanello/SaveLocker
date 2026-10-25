using System.Net;
using System.Net.Sockets;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// A save folder is whatever the server's head says, so no path that reaches a game may be a folder whose files run
/// at login: one stolen machine key could otherwise publish a game every agent adopts, aimed at its Startup folder.
/// </summary>
public sealed class AutoRunFolderTests
{
    [Fact]
    public void The_guard_refuses_the_Startup_folder_what_holds_it_and_what_is_inside_it()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(SavePathGuard.Check(Path.Combine(home, ".ssh")).Ok);

        if (OperatingSystem.IsWindows())
        {
            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            Assert.False(SavePathGuard.Check(startup).Ok);
            Assert.False(SavePathGuard.Check(Path.Combine(startup, "Game")).Ok);
            Assert.False(SavePathGuard.Check(Path.GetDirectoryName(startMenu)).Ok);
            Assert.False(SavePathGuard.Check(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)).Ok);
            Assert.True(SavePathGuard.Check(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hollow Knight Silksong")).Ok);
        }
        else
        {
            Assert.False(SavePathGuard.Check(Path.Combine(home, ".config", "autostart")).Ok);
            Assert.False(SavePathGuard.Check(Path.Combine(home, ".config", "systemd", "user")).Ok);
            Assert.False(SavePathGuard.Check(Path.Combine(home, ".config")).Ok);
            Assert.True(SavePathGuard.Check(Path.Combine(home, ".config", "Hollow Knight Silksong")).Ok);
        }
    }

    [Theory]
    [InlineData("<winAppData>/Microsoft/Windows/Start Menu/Programs/Startup", true)]
    [InlineData("<home>/.config/autostart", true)]
    [InlineData("<home>\\.ssh", true)]
    [InlineData("<home>/.local/share/systemd/user", true)]
    [InlineData("<winAppData>/Team Cherry/Hollow Knight Silksong", false)]
    [InlineData("<winDocuments>/My Games/Startup Company", false)]
    public void A_template_naming_an_autorun_folder_is_recognised(string template, bool autoRun) =>
        Assert.Equal(autoRun, PathResolver.NamesAutoRunFolder(template));
}

public sealed class AutoRunTemplateServerTests(ServerProcess server) : IClassFixture<ServerProcess>
{
    private async Task<ApiClient> Machine()
    {
        var reg = await new ApiClient(server.Url, null).RegisterAsync("pc-" + Guid.NewGuid().ToString("N")[..6]);
        return new ApiClient(server.Url, reg.ApiKey);
    }

    [Fact]
    public async Task An_agent_cannot_give_the_fleet_a_Startup_or_literal_save_folder()
    {
        var api = await Machine();
        var name = "Silksong " + Guid.NewGuid().ToString("N")[..6];

        await Assert.ThrowsAsync<HttpRequestException>(() => api.CreateGameAsync(new CreateGameRequest(name, null, null,
            SuggestedSaveDir: "<winAppData>/Microsoft/Windows/Start Menu/Programs/Startup")));
        await Assert.ThrowsAsync<HttpRequestException>(() => api.CreateGameAsync(new CreateGameRequest(name, null, null,
            SuggestedSaveDir: @"C:\Users\victim\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup")));
        await Assert.ThrowsAsync<HttpRequestException>(() => api.CreateGameAsync(new CreateGameRequest(name, null, null,
            ExtraPaths: [new SavePathDto("states", null, "<home>/.config/autostart", null)])));

        var game = await api.CreateGameAsync(new CreateGameRequest(name, null, null));
        Assert.False(await api.TrySetSaveTemplateAsync(game.Id, "<winAppData>/Microsoft/Windows/Start Menu/Programs/Startup"));
        Assert.Null((await api.ListGamesAsync()).Single(g => g.Id == game.Id).SuggestedSaveDir);

        Assert.True(await api.TrySetSaveTemplateAsync(game.Id, "<winAppData>/Team Cherry/Hollow Knight Silksong"));
    }
}

public sealed class LoopbackPeerTests
{
    private const string Table =
        "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\n" +
        "   0: 0100007F:1436 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 1 1 0\n" +
        "   1: 0100007F:1436 0100007F:D431 01 00000000:00000000 00:00000000 00000000  1000        0 2 1 0\n" +
        "   2: 0100007F:D431 0100007F:1436 01 00000000:00000000 00:00000000 00000000  1001        0 3 1 0\n";

    [Fact]
    public void The_client_end_of_the_connection_names_its_owner()
    {
        Assert.Equal(1001, LoopbackPeer.FindSocketUid(Table, clientPort: 0xD431, serverPort: 0x1436));
        Assert.Null(LoopbackPeer.FindSocketUid(Table, clientPort: 0xD432, serverPort: 0x1436));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task A_connection_from_this_account_is_recognised(string loopback)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) return;
        var address = IPAddress.Parse(loopback);
        using var listener = new TcpListener(address, 0);
        listener.Start();
        var serverPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new TcpClient(address.AddressFamily);
        await client.ConnectAsync(address, serverPort);
        using var accepted = await listener.AcceptTcpClientAsync();
        var remote = (IPEndPoint)accepted.Client.RemoteEndPoint!;

        Assert.True(LoopbackPeer.IsSameUser(remote.Address, remote.Port, serverPort));
        Assert.False(LoopbackPeer.IsSameUser(IPAddress.Parse("192.168.1.5"), remote.Port, serverPort));
    }
}
