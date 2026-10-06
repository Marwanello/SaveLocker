using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The server's own build output, run as a real process on a free port with its own state, for tests
/// whose subject is the agent talking to a real server. The test project references the server
/// project for build order only, so its output is always there.
/// </summary>
public sealed class ServerProcess : IDisposable
{
    private readonly Process _process;
    public string State { get; } = Path.Combine(Path.GetTempPath(), "savelocker-server-" + Guid.NewGuid().ToString("N"));
    public string Url { get; }

    public ServerProcess()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        Url = $"http://127.0.0.1:{port}";

        Directory.CreateDirectory(State);
        var psi = new ProcessStartInfo("dotnet", $"\"{ServerDll()}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["ASPNETCORE_URLS"] = Url;
        psi.Environment["Storage__DbPath"] = Path.Combine(State, "savelocker.db");
        psi.Environment["Storage__ArchiveRoot"] = Path.Combine(State, "archives");
        psi.Environment["Backup__Enabled"] = "false";
        _process = Process.Start(psi)!;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(60))
        {
            try
            {
                if (http.GetAsync(Url + "/api/admin/status").Result.IsSuccessStatusCode) return;
            }
            catch (AggregateException) { }
            if (_process.HasExited) break;
            Thread.Sleep(250);
        }
        Dispose();
        throw new InvalidOperationException($"The server did not start on {Url}.");
    }

    /// <summary>src/Server/bin/&lt;same configuration as this test build&gt;/net10.0/SaveLocker.Server.dll.</summary>
    private static string ServerDll()
    {
        var config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
            ? "Release" : "Debug";
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SaveLocker.sln"))) dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not find the repository root.");
        return Path.Combine(dir.FullName, "src", "Server", "bin", config, "net10.0", "SaveLocker.Server.dll");
    }

    public void Dispose()
    {
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        _process.Dispose();
        try { Directory.Delete(State, recursive: true); } catch (IOException) { }
    }
}

/// <summary>
/// Phase 5 of multiple save paths: a scanner that KNOWS a game keeps several folders (an emulator's
/// saves and save states) declares them, and enrollment adopts them without asking — on the machine
/// that creates the game and on the next one — while a game the server already holds with a
/// different definition is left alone.
/// </summary>
public sealed class EnrollDeclaredFoldersTests : IClassFixture<ServerProcess>, IDisposable
{
    private readonly ServerProcess _server;
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "savelocker-declared-" + Guid.NewGuid().ToString("N"));

    public EnrollDeclaredFoldersTests(ServerProcess server)
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
        var config = AgentConfig.Load(Path.Combine(_dir, name, "config.json"));
        config.ServerUrl = _server.Url;
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync(name);
        config.ApiKey = reg.ApiKey;
        config.MachineId = reg.MachineId;
        config.MachineName = name;
        config.Save();
        return config;
    }

    private string Dir(string machine, string name)
    {
        var d = Path.Combine(_dir, machine, name);
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "file.srm"), name);
        return d;
    }

    /// <summary>A RetroArch-shaped candidate: one ROM's save and save states in folders other ROMs share.</summary>
    private ScanCandidate Candidate(string game, string machine, string statesScope = "Rom.state*") => new(
        game, Dir(machine, "saves"), ScanSource.SaveRoot, false,
        IncludeGlobs: new[] { "Rom.srm" },
        ExtraSaveDirs: new[] { new DeclaredSavePath("states", Dir(machine, "states"), new[] { statesScope }) });

    [Fact]
    public async Task Declared_folders_are_adopted_on_the_first_machine_and_the_next()
    {
        var game = "Declared-" + Guid.NewGuid().ToString("N")[..8];
        var a = await Machine("a-" + game);
        var b = await Machine("b-" + game);

        var first = await Enroller.EnrollAsync(a, new[] { Candidate(game, "a") }, new[] { 0 });
        var second = await Enroller.EnrollAsync(b, new[] { Candidate(game, "b") }, new[] { 0 });

        Assert.Equal((1, 0), first);
        Assert.Equal((1, 0), second);
        foreach (var (config, machine) in new[] { (a, "a"), (b, "b") })
        {
            var tracked = AgentConfig.Load(config.ConfigPath).FindGame(game)!;
            Assert.Equal(new[] { "Rom.srm" }, tracked.IncludeGlobs);
            var states = Assert.Single(tracked.ExtraPaths);
            Assert.Equal("states", states.Key);
            Assert.Equal(Path.Combine(_dir, machine, "states"), states.Directory);
            Assert.Equal(new[] { "Rom.state*" }, states.IncludeGlobs);
        }

        // The server holds one game with both scopes, and each machine's own folder for it.
        var seenByB = (await ApiClient.For(b).ListGamesAsync()).Single(g => g.Name == game);
        Assert.Equal(new[] { "Rom.srm" }, seenByB.IncludeGlobs);
        var serverStates = Assert.Single(seenByB.ExtraPaths!);
        Assert.Equal(new[] { "Rom.state*" }, serverStates.IncludeGlobs);
        Assert.Equal(Path.Combine(_dir, "b", "states"), serverStates.MachinePath);
    }

    [Fact]
    public async Task A_game_the_server_defines_differently_is_skipped()
    {
        var game = "Differs-" + Guid.NewGuid().ToString("N")[..8];
        var a = await Machine("a-" + game);
        var c = await Machine("c-" + game);
        await Enroller.EnrollAsync(a, new[] { Candidate(game, "a") }, new[] { 0 });

        var result = await Enroller.EnrollAsync(c, new[] { Candidate(game, "c", statesScope: "*.state") }, new[] { 0 });

        Assert.Equal((0, 1), result);
        Assert.Null(AgentConfig.Load(c.ConfigPath).FindGame(game));
    }

    [Fact]
    public async Task A_declared_folder_inside_the_primary_one_is_refused_before_the_game_exists()
    {
        var game = "Nested-" + Guid.NewGuid().ToString("N")[..8];
        var a = await Machine("a-" + game);
        var saves = Dir("a", "nested-saves");
        var candidate = new ScanCandidate(game, saves, ScanSource.SaveRoot, false,
            ExtraSaveDirs: new[] { new DeclaredSavePath("states", Dir("a", Path.Combine("nested-saves", "states"))) });

        var result = await Enroller.EnrollAsync(a, new[] { candidate }, new[] { 0 });

        Assert.Equal((0, 1), result);
        Assert.DoesNotContain(await ApiClient.For(a).ListGamesAsync(), g => g.Name == game);
    }
}
