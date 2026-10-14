using System.Net;
using System.Net.Sockets;
using System.Text;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The manifest is a live community feed, refreshed rather than pinned: a stale cache is replaced in
/// the background, a failed or implausible download never replaces a good cache, and the console's
/// window and "Refresh now" reach the agent through persisted config fields. These tests drive the
/// real loader and <see cref="Detection"/> against a stub HTTP server.
/// </summary>
public sealed class ManifestRefreshTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "savelocker-manifest-" + Guid.NewGuid().ToString("N"));

    private readonly Stub _stub;

    private static readonly string Plausible = BuildManifest(5_100);

    public ManifestRefreshTests()
    {
        Directory.CreateDirectory(_dir);
        _stub = new Stub();
        _stub.Body = Plausible;
    }

    public void Dispose()
    {
        _stub.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string BuildManifest(int games)
    {
        var sb = new StringBuilder(games * 60);
        for (var i = 0; i < games; i++)
            sb.Append("Game ").Append(i).Append(":\n  files:\n    <winAppData>/Game")
              .Append(i).Append(":\n      tags:\n        - save\n");
        return sb.ToString();
    }

    private string Cache() => Path.Combine(_dir, "manifest.yaml");

    private AgentConfig Config()
    {
        var config = AgentConfig.Load(Path.Combine(_dir, Guid.NewGuid().ToString("N"), "config.json"));
        config.ManifestCachePath = Cache();
        config.Save();
        return config;
    }

    private Detection Detection() => new(Config()) { ManifestUrlOverride = _stub.Url };

    [Fact]
    public async Task A_plausible_download_is_cached_and_parsed()
    {
        var loader = await ManifestLoader.DownloadAsync(Cache(), _stub.Url);

        Assert.True(loader.GameCount >= 5_100, $"parsed {loader.GameCount} games");
        Assert.Equal(Plausible, File.ReadAllText(Cache()));
    }

    [Fact]
    public async Task An_implausible_download_is_refused_and_the_cache_is_kept()
    {
        var cache = Cache();
        File.WriteAllText(cache, "Cached Game:\n  files:\n    <winAppData>/Cached:\n      tags:\n        - save\n");
        _stub.Body = "{}"; // an error page or a truncated body parses to nothing

        await Assert.ThrowsAsync<InvalidDataException>(() => ManifestLoader.DownloadAsync(cache, _stub.Url));

        Assert.Contains("Cached Game", File.ReadAllText(cache));
    }

    [Fact]
    public async Task An_oversized_download_is_refused_before_anything_is_written()
    {
        _stub.DeclaredLength = ManifestLoader.MaxManifestBytes + 1;

        await Assert.ThrowsAsync<InvalidDataException>(() => ManifestLoader.DownloadAsync(Cache(), _stub.Url));

        Assert.False(File.Exists(Cache()));
    }

    [Fact]
    public async Task A_stale_cache_is_refreshed_on_the_next_check()
    {
        var cache = Cache();
        File.WriteAllText(cache, "Old Game:\n  files:\n    <winAppData>/Old:\n      tags:\n        - save\n");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-10));

        var detection = Detection();
        Assert.True(await detection.RefreshManifestIfDueAsync());

        Assert.Equal(1, _stub.Requests);
        Assert.Contains("Game 0", File.ReadAllText(cache));
        // And the refreshed copy is what the resolver answers from from now on.
        Assert.True((await detection.GetManifestAsync()).GameCount >= 5_100);
    }

    [Fact]
    public async Task A_fresh_cache_is_not_re_downloaded()
    {
        var cache = Cache();
        File.WriteAllText(cache, "Old Game:\n  files: {}\n");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow);

        var detection = Detection();
        Assert.False(await detection.RefreshManifestIfDueAsync());

        Assert.Equal(0, _stub.Requests);
    }

    [Fact]
    public async Task A_zero_window_means_never_and_the_manual_commands_still_work()
    {
        var cache = Cache();
        File.WriteAllText(cache, "Old Game:\n  files: {}\n");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-30));

        var config = Config();
        config.ManifestRefreshDays = 0;
        config.Save();
        var detection = new Detection(config) { ManifestUrlOverride = _stub.Url };

        Assert.False(await detection.RefreshManifestIfDueAsync());
        Assert.Equal(0, _stub.Requests);

        // The console's "Refresh now" bypasses the window entirely.
        Assert.True(await detection.RefreshManifestIfDueAsync(force: true));
        Assert.Equal(1, _stub.Requests);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_cache_and_is_not_retried_every_tick()
    {
        var cache = Cache();
        File.WriteAllText(cache, "Old Game:\n  files: {}\n");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-10));
        _stub.Status = 500;

        var detection = Detection();
        Assert.False(await detection.RefreshManifestIfDueAsync());
        Assert.Equal("Old Game", File.ReadAllText(cache).Split(':')[0]);

        // A second tick right after finds the attempt gap and does not ask again.
        Assert.False(await detection.RefreshManifestIfDueAsync());
        Assert.Equal(1, _stub.Requests);
    }

    [Fact]
    public void The_console_window_overrides_the_local_one_and_the_request_is_acted_on_once()
    {
        var config = Config();
        config.ManifestRefreshDays = 3;
        config.Save();

        config.ApplyManifestSettings(7);
        Assert.Equal(7, config.EffectiveManifestRefreshDays);
        Assert.Equal(7, AgentConfig.Load(config.ConfigPath).ConsoleManifestRefreshDays);

        config.ApplyManifestSettings(null); // no value pushed (an unregistered machine / old server)
        Assert.Equal(3, config.EffectiveManifestRefreshDays);

        var request = DateTime.UtcNow;
        Assert.True(config.IsManifestRefreshDue(request));
        config.MarkManifestRefreshHandled(request);

        Assert.False(config.IsManifestRefreshDue(request));                    // the one acted on
        Assert.False(config.IsManifestRefreshDue(request.AddSeconds(-1)));     // older beats stay old
        Assert.True(config.IsManifestRefreshDue(request.AddSeconds(1)));       // a NEW press is due again
        Assert.Equal(request, AgentConfig.Load(config.ConfigPath).ManifestRefreshHandledAt);
    }

    /// <summary>The smallest HTTP server that can answer one GET: a body, a status, and a request count.</summary>
    private sealed class Stub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public string Body { get; set; } = "";
        public int Status { get; set; } = 200;
        /// <summary>When set, answers with only this Content-Length header — for the cap check.</summary>
        public long? DeclaredLength { get; set; }
        public int Requests { get; private set; }
        public string Url { get; }

        public Stub()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Url = $"http://127.0.0.1:{port}/manifest.yaml";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _loop = Task.Run(LoopAsync);
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
            try { _listener.Close(); } catch { }
        }

        private async Task LoopAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }
                try
                {
                    Requests++;
                    if (DeclaredLength is { } length)
                    {
                        // One byte flushes the headers (the client refuses on the Content-Length
                        // alone and never reads the body); Close then drops the connection, which
                        // the client has already stopped listening to.
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentLength64 = length;
                        ctx.Response.OutputStream.WriteByte(0);
                        ctx.Response.Close();
                        continue;
                    }

                    var bytes = Encoding.UTF8.GetBytes(Body);
                    ctx.Response.StatusCode = Status;
                    ctx.Response.ContentType = "text/yaml";
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes);
                    ctx.Response.Close();
                }
                catch { try { ctx.Response.Abort(); } catch { } }
            }
        }
    }
}
