using System.Net;
using System.Net.Sockets;
using System.Text;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The conflict upload latch (<see cref="TrackedGame.ConsecutiveConflicts"/>) must not outlive the
/// conflict. Nothing pushes "someone resolved it while you were down" to an agent, and a plain pull
/// cannot clear it either — this machine still holds un-pushed changes, so its own guard refuses
/// the pull. Without a server check at the latch, the save is locked out of uploading until a
/// force-push. These tests drive a real <see cref="SyncEngine"/> against a stub server.
/// </summary>
public sealed class ConflictLatchTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "savelocker-latch-" + Guid.NewGuid().ToString("N"));

    private readonly HttpListener _listener = new();
    private readonly List<string> _requests = new();
    private readonly Task _loop;
    private volatile bool _openConflict;

    public ConflictLatchTests()
    {
        Directory.CreateDirectory(_dir);
        var port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }   // stopped
            try { await HandleAsync(ctx); }
            catch { try { ctx.Response.Abort(); } catch { } }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var method = ctx.Request.HttpMethod;
        var path = ctx.Request.Url!.AbsolutePath;
        lock (_requests) _requests.Add($"{method} {path}");

        if (method == "GET" && path.EndsWith("/state", StringComparison.Ordinal))
        {
            var body = Encoding.UTF8.GetBytes(
                "{\"game\":{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Latch\",\"manifestKey\":null," +
                "\"customPathsJson\":null,\"enabled\":true},\"head\":null,\"lease\":null," +
                $"\"hasOpenConflict\":{(_openConflict ? "true" : "false")},\"totalStorageBytes\":0}}");
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            ctx.Response.OutputStream.Write(body);
            ctx.Response.Close();
            return;
        }

        // Anything else (the upload route this test watches for) is recorded and refused with a
        // non-gateway status, which the engine reports and gives up on rather than retrying.
        using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
        await reader.ReadToEndAsync();
        ctx.Response.StatusCode = 500;
        ctx.Response.Close();
    }

    private (SyncEngine Engine, TrackedGame Game, AgentConfig Config) Rig(string serverUrl)
    {
        var save = Path.Combine(_dir, "Latch-save");
        Directory.CreateDirectory(save);
        File.WriteAllText(Path.Combine(save, "slot1.sav"), "progress");
        var game = new TrackedGame
        {
            GameId = Guid.NewGuid(),
            Name = "Latch",
            SaveDirectory = save,
            // Three rejected uploads, i.e. the latch is tripped, and a local hash that does not
            // match, so the push has something to send once the latch lets it through.
            ConsecutiveConflicts = 3,
            LastSyncedHash = "a-hash-from-before",
        };

        var cfgPath = Path.Combine(_dir, "config.json");
        var config = AgentConfig.Load(cfgPath);
        config.ServerUrl = serverUrl;
        config.ApiKey = "latch-test-key";
        config.MachineId = Guid.NewGuid();
        config.Games.Add(game);
        config.Save();
        return (new SyncEngine(config, ApiClient.For(config)), game, config);
    }

    private bool SawStateCheck() { lock (_requests) return _requests.Any(r => r.EndsWith("/state")); }
    private bool SawUpload() { lock (_requests) return _requests.Any(r => r.Contains("/upload")); }

    [Fact]
    public async Task ResolvedWhileDown_TheStaleLatchIsCleared_AndThePushRuns()
    {
        var (engine, game, _) = Rig($"http://127.0.0.1:{new Uri(_listener.Prefixes.First()).Port}");
        _openConflict = false;

        await engine.PushAsync(game, force: false, settle: false);

        Assert.True(SawStateCheck());
        Assert.True(SawUpload());                     // the latch did not refuse it locally
        Assert.Equal(0, game.ConsecutiveConflicts);   // and the stale count is gone
    }

    [Fact]
    public async Task StillOpen_TheLatchHolds_AndNothingIsUploaded()
    {
        var (engine, game, _) = Rig($"http://127.0.0.1:{new Uri(_listener.Prefixes.First()).Port}");
        _openConflict = true;

        var result = await engine.PushAsync(game, force: false, settle: false);

        Assert.NotNull(result);
        Assert.Equal(UploadStatus.Conflict, result!.Status);
        Assert.False(SawUpload());
        Assert.Equal(3, game.ConsecutiveConflicts);
    }

    [Fact]
    public async Task ServerUnreachable_TheLatchHolds_AndNothingIsUploaded()
    {
        // A port nothing listens on: the check itself cannot run, so the pause must stand rather
        // than gamble on an upload the server has already rejected three times.
        var dead = FreePort();
        var (engine, game, _) = Rig($"http://127.0.0.1:{dead}");

        var result = await engine.PushAsync(game, force: false, settle: false);

        Assert.NotNull(result);
        Assert.Equal(UploadStatus.Conflict, result!.Status);
        Assert.Equal(3, game.ConsecutiveConflicts);
    }
}
