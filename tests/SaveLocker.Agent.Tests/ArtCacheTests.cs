using System.Net;
using System.Net.Sockets;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The agent's on-disk art cache: the same image is served from disk while the server names the same
/// URL, a changed URL is fetched again, the cache answers with the server gone, and the poller's warm
/// pass fetches only what changed and forgets games no longer tracked. The "server" is a stub that
/// serves <c>/art/</c> and counts what it was asked.
/// </summary>
public sealed class ArtCacheTests : IDisposable
{
    private readonly string _state = Path.Combine(Path.GetTempPath(), "sl-artcache-" + Guid.NewGuid().ToString("N"));
    private readonly ArtStub _stub = new();

    public ArtCacheTests() => Directory.CreateDirectory(_state);

    public void Dispose()
    {
        _stub.Dispose();
        try { Directory.Delete(_state, recursive: true); } catch (IOException) { }
    }

    private static GameDto Game(Guid id, string? grid, string? icon = null) =>
        new(id, "Game", null, null, true, GridUrl: grid, IconUrl: icon);

    private ApiClient Api => new(_stub.Url, null);

    private static Func<CancellationToken, Task<GameDto?>> Lookup(GameDto? game) => _ => Task.FromResult(game);

    private static Func<CancellationToken, Task<GameDto?>> Unreachable =>
        _ => throw new HttpRequestException("connection refused");

    [Fact]
    public async Task The_same_url_is_served_from_disk_and_a_new_one_is_fetched_again()
    {
        var id = Guid.NewGuid();
        var cache = new ArtCache(_state);
        _stub.Images[$"/art/{id:N}/grid.png"] = [1, 2, 3];
        var v1 = Game(id, $"/art/{id:N}/grid.png?v=1");

        var first = await cache.GetAsync(id, "grid", 192, Api, Lookup(v1));
        var second = await cache.GetAsync(id, "grid", 192, Api, Lookup(v1));
        Assert.Equal(new byte[] { 1, 2, 3 }, first!.Bytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, second!.Bytes);
        Assert.Equal(1, _stub.Hits);

        // Re-picked on the server: a new ?v= is a new URL, fetched once, and the old file is gone.
        _stub.Images[$"/art/{id:N}/grid.png"] = [4, 5];
        cache.Observe([Game(id, $"/art/{id:N}/grid.png?v=2")]);
        Assert.Equal(new byte[] { 4, 5 }, (await cache.GetAsync(id, "grid", 192, Api, Lookup(null)))!.Bytes);
        Assert.Equal(2, _stub.Hits);
        Assert.Single(Directory.GetFiles(Path.Combine(_state, "art-cache", id.ToString("N")), "grid-192.*"));
    }

    [Fact]
    public async Task With_the_server_gone_the_cache_answers_at_that_size_or_another()
    {
        var id = Guid.NewGuid();
        _stub.Images[$"/art/{id:N}/grid.png"] = [7, 7];
        var game = Game(id, $"/art/{id:N}/grid.png?v=1");
        Assert.NotNull(await new ArtCache(_state).GetAsync(id, "grid", 192, Api, Lookup(game)));

        _stub.Dispose();
        var offline = new ArtCache(_state);   // a restarted agent: nothing known but the disk
        Assert.Equal(new byte[] { 7, 7 }, (await offline.GetAsync(id, "grid", 192, Api, Unreachable))!.Bytes);
        Assert.Equal(new byte[] { 7, 7 }, (await offline.GetAsync(id, "grid", 96, Api, Unreachable))!.Bytes);
        Assert.Null(await offline.GetAsync(id, "icon", 96, Api, Unreachable));
    }

    [Fact]
    public async Task Art_cleared_on_the_server_is_cleared_here()
    {
        var id = Guid.NewGuid();
        var cache = new ArtCache(_state);
        _stub.Images[$"/art/{id:N}/icon.png"] = [9];
        Assert.NotNull(await cache.GetAsync(id, "icon", 96, Api, Lookup(Game(id, null, $"/art/{id:N}/icon.png?v=1"))));

        cache.Observe([Game(id, null, null)]);
        Assert.Null(await cache.GetAsync(id, "icon", 96, Api, Lookup(null)));
        Assert.Empty(Directory.GetFiles(Path.Combine(_state, "art-cache", id.ToString("N")), "icon-*"));
    }

    [Fact]
    public async Task Warming_fetches_the_prefetch_set_once_and_forgets_untracked_games()
    {
        var a = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var cache = new ArtCache(_state);
        _stub.Images[$"/art/{a:N}/grid.png"] = [1];
        _stub.Images[$"/art/{a:N}/icon.png"] = [2];
        _stub.Images[$"/art/{gone:N}/grid.png"] = [3];
        var games = new[] { Game(a, $"/art/{a:N}/grid.png?v=1", $"/art/{a:N}/icon.png?v=1"), Game(gone, $"/art/{gone:N}/grid.png?v=1") };

        await cache.WarmAsync(games, Api);
        Assert.Equal(ArtCache.Prefetch.Length + ArtCache.Prefetch.Count(p => p.Kind == "grid"), _stub.Hits);

        await cache.WarmAsync(games, Api);
        Assert.Equal(ArtCache.Prefetch.Length + ArtCache.Prefetch.Count(p => p.Kind == "grid"), _stub.Hits);  // nothing changed

        await cache.WarmAsync([games[0]], Api);
        Assert.False(Directory.Exists(Path.Combine(_state, "art-cache", gone.ToString("N"))));

        // Then every size the agent's surfaces ask for is answered with no server at all.
        _stub.Dispose();
        foreach (var (kind, width) in ArtCache.Prefetch)
            Assert.NotNull(await new ArtCache(_state).GetAsync(a, kind, width, Api, Unreachable));
    }

    [Fact]
    public async Task A_width_the_server_does_not_make_is_cached_as_the_original()
    {
        var id = Guid.NewGuid();
        var cache = new ArtCache(_state);
        _stub.Images[$"/art/{id:N}/grid.png"] = [5];
        var game = Game(id, $"/art/{id:N}/grid.png?v=1");

        await cache.GetAsync(id, "grid", 123, Api, Lookup(game));
        await cache.GetAsync(id, "grid", 77, Api, Lookup(game));

        Assert.Equal(1, _stub.Hits);
        Assert.Equal("grid-full", Path.GetFileName(Assert.Single(Directory.GetFiles(Path.Combine(_state, "art-cache", id.ToString("N"))))).Split('.')[0]);
        Assert.DoesNotContain("w=", _stub.LastQuery);
    }

    /// <summary>Serves <see cref="Images"/> by path as image/png and counts the requests.</summary>
    private sealed class ArtStub : IDisposable
    {
        private readonly HttpListener _listener = new();
        private bool _stopped;
        public Dictionary<string, byte[]> Images { get; } = new();
        public int Hits;
        public string LastQuery = "";
        public string Url { get; }

        public ArtStub()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (!_stopped)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                Interlocked.Increment(ref Hits);
                LastQuery = ctx.Request.Url!.Query;
                if (Images.TryGetValue(ctx.Request.Url.AbsolutePath, out var bytes))
                {
                    ctx.Response.ContentType = "image/png";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
                else ctx.Response.StatusCode = 404;
                ctx.Response.Close();
            }
        }

        public void Dispose()
        {
            if (_stopped) return;
            _stopped = true;
            try { _listener.Stop(); _listener.Close(); } catch (ObjectDisposedException) { }
        }
    }
}
