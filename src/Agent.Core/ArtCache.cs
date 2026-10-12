using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// The covers and icons of this machine's games, kept on disk in the agent's state folder so every
/// surface that draws one — the agent UI, the Deck's Game Mode, anything else behind the
/// <c>/api/games/{id}/art</c> proxy — has it with the server out of reach, and without having to ask
/// first. The poller warms it for every tracked game (<see cref="WarmAsync"/>), so art is here before
/// any window opens.
/// <para>
/// The server stamps every stored image's URL with <c>?v=&lt;write time&gt;</c>, so a re-picked or
/// refreshed cover is a new URL. A cached file is named after a hash of the URL it came from: while the
/// server's URL is unchanged the file is served as it is, and when it changes the file is fetched again
/// and the old one removed. A game whose art was cleared on the server loses it here too. With the
/// server unreachable, whatever is cached is served — a stale cover beats a blank tile.
/// </para>
/// </summary>
public sealed class ArtCache
{
    /// <summary>What the agent's own surfaces ask for: the agent UI's game page (grid 192), its grid of
    /// covers (grid 256) and list (icon 96), and Game Mode's list (grid 96). Warmed ahead of time.</summary>
    public static readonly (string Kind, int Width)[] Prefetch = [("grid", 96), ("grid", 192), ("grid", 256), ("icon", 96)];

    /// <summary>The server's thumbnail widths (its <c>ArtThumbnails.Widths</c>). Any other width gets the
    /// original from the server, so it is cached as the original — never one file per number a caller
    /// makes up.</summary>
    private static readonly int[] ServerWidths = [48, 64, 96, 128, 192, 256, 384];

    /// <summary>The most images one <see cref="WarmAsync"/> fetches: the first pass over a large library
    /// spreads over a few polls instead of holding one for minutes.</summary>
    public const int MaxFetchesPerWarm = 24;

    /// <summary>How long a URL the cache looked up itself stays trusted. The poller refreshes every
    /// tracked game's on each tick, so this only matters for a game it has not seen yet.</summary>
    private static readonly TimeSpan UrlTtl = TimeSpan.FromMinutes(5);

    /// <summary>The most a lookup of the game's art URLs may take before the cache answers instead: a
    /// server that has gone quiet (not refused — quiet) must not hold a grid of tiles blank.</summary>
    private static readonly TimeSpan LookupBudget = TimeSpan.FromSeconds(5);

    private static readonly Dictionary<string, string> ExtByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = "png", ["image/jpeg"] = "jpg", ["image/gif"] = "gif", ["image/webp"] = "webp",
        ["image/x-icon"] = "ico", ["image/vnd.microsoft.icon"] = "ico",
    };
    private static readonly Dictionary<string, string> TypeByExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png", ["jpg"] = "image/jpeg", ["gif"] = "image/gif", ["webp"] = "image/webp", ["ico"] = "image/x-icon",
    };

    private static readonly ConcurrentDictionary<string, ArtCache> Instances = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The one cache of a state folder: the API server and the poller share its URL knowledge
    /// and its per-file locks.</summary>
    public static ArtCache For(string stateDir) =>
        Instances.GetOrAdd(Path.GetFullPath(stateDir), d => new ArtCache(d));

    private readonly string _root;
    private readonly ConcurrentDictionary<Guid, (DateTime At, string? Grid, string? Icon)> _urls = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ArtCache(string stateDir) => _root = Path.Combine(stateDir, "art-cache");

    public sealed record Image(byte[] Bytes, string ContentType);

    /// <summary>What the server currently names as these games' art — the poller's game list.</summary>
    public void Observe(IEnumerable<GameDto> games)
    {
        var now = DateTime.UtcNow;
        foreach (var g in games) _urls[g.Id] = (now, g.GridUrl, g.IconUrl);
    }

    /// <summary>
    /// The game's cover or icon: from disk when the server still names the same image, else fetched and
    /// kept. <paramref name="lookup"/> asks the server for the game (its art URLs) when the cache has not
    /// heard of them recently; when it cannot answer, the cached image is served regardless of age —
    /// and failing that, the same kind at any other size. Null: no art, here or there.
    /// </summary>
    public async Task<Image?> GetAsync(Guid gameId, string kind, int? width, ApiClient api,
        Func<CancellationToken, Task<GameDto?>> lookup, CancellationToken ct = default)
    {
        var w = Normalize(width);
        string? url;
        using (var budget = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            budget.CancelAfter(LookupBudget);
            try { url = await CurrentUrlAsync(gameId, kind, lookup, budget.Token); }
            catch (Exception) when (!ct.IsCancellationRequested) { return ReadAny(gameId, kind, w); }
        }

        if (string.IsNullOrEmpty(url))
        {
            Purge(gameId, kind);
            return null;
        }
        return await FetchAsync(gameId, kind, w, url, api, ct) ?? ReadAny(gameId, kind, w);
    }

    /// <summary>
    /// Bring every tracked game's prefetch set up to the server's current art, and forget the art of
    /// games no longer tracked here. Best effort: an image that fails is tried again next time, and a
    /// server that stops answering ends the pass.
    /// </summary>
    public async Task WarmAsync(IReadOnlyCollection<GameDto> trackedServerGames, ApiClient api, CancellationToken ct = default)
    {
        Observe(trackedServerGames);
        PruneExcept(trackedServerGames.Select(g => g.Id));

        var fetched = 0;
        foreach (var g in trackedServerGames)
        foreach (var (kind, width) in Prefetch)
        {
            var url = kind == "grid" ? g.GridUrl : g.IconUrl;
            if (string.IsNullOrEmpty(url)) { Purge(g.Id, kind); continue; }
            if (File.Exists(PathFor(g.Id, kind, width, url, ext: null))) continue;
            if (fetched >= MaxFetchesPerWarm) return;
            fetched++;
            try { await FetchAsync(g.Id, kind, width, url, api, ct, throwIfUnreachable: true); }
            catch (Exception ex) when (ServerReachability.IsUnreachable(ex) || ex is TaskCanceledException && !ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                AgentLogger.LogException("ArtCache.warm", ex);
            }
        }
    }

    private async Task<string?> CurrentUrlAsync(Guid gameId, string kind, Func<CancellationToken, Task<GameDto?>> lookup,
        CancellationToken ct)
    {
        if (!_urls.TryGetValue(gameId, out var known) || known.At < DateTime.UtcNow - UrlTtl)
        {
            var game = await lookup(ct);
            known = (DateTime.UtcNow, game?.GridUrl, game?.IconUrl);
            _urls[gameId] = known;
        }
        return kind == "grid" ? known.Grid : known.Icon;
    }

    /// <summary>The image at <paramref name="url"/>: the cached copy when there is one, else downloaded and
    /// stored in place of any older copy. Null when the server has nothing there or could not be read.</summary>
    private async Task<Image?> FetchAsync(Guid gameId, string kind, int? width, string url, ApiClient api,
        CancellationToken ct, bool throwIfUnreachable = false)
    {
        var gate = _locks.GetOrAdd($"{gameId:N}/{Slot(kind, width)}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (Read(PathFor(gameId, kind, width, url, ext: null)) is { } cached) return cached;

            (byte[] Bytes, string ContentType)? got;
            try { got = await api.GetArtAsync(url, width, ct); }
            catch (Exception) when (!throwIfUnreachable && !ct.IsCancellationRequested) { return null; }
            if (got is not { } art || !ExtByType.TryGetValue(art.ContentType, out var ext)) return null;

            var path = PathFor(gameId, kind, width, url, ext);
            try
            {
                AtomicFile.WriteAllBytes(path, art.Bytes);
                foreach (var old in Entries(gameId, kind, width))
                    if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) TryDelete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AgentLogger.LogException("ArtCache.store", ex);
            }
            return new Image(art.Bytes, art.ContentType);
        }
        finally { gate.Release(); }
    }

    /// <summary>Served with the server out of reach: this size if it is here, else the largest other size
    /// of the same kind (a bigger image draws fine where a smaller one was asked for).</summary>
    private Image? ReadAny(Guid gameId, string kind, int? width)
    {
        if (Entries(gameId, kind, width).Select(Read).FirstOrDefault(i => i is not null) is { } exact) return exact;
        return Directory.Exists(GameDir(gameId))
            ? Directory.EnumerateFiles(GameDir(gameId), $"{kind}-*")
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .OrderByDescending(f => WidthOf(f))
                .Select(Read)
                .FirstOrDefault(i => i is not null)
            : null;
    }

    private void Purge(Guid gameId, string kind)
    {
        if (!Directory.Exists(GameDir(gameId))) return;
        foreach (var f in Directory.EnumerateFiles(GameDir(gameId), $"{kind}-*")) TryDelete(f);
    }

    private void PruneExcept(IEnumerable<Guid> keep)
    {
        if (!Directory.Exists(_root)) return;
        var wanted = keep.Select(id => id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            if (wanted.Contains(Path.GetFileName(dir))) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private string GameDir(Guid gameId) => Path.Combine(_root, gameId.ToString("N"));

    private static string Slot(string kind, int? width) => width is { } w ? $"{kind}-{w}" : $"{kind}-full";

    /// <summary><c>grid-192.&lt;hash of the URL&gt;.jpg</c>. With no <paramref name="ext"/>, the file of that
    /// URL in whatever format it was stored (or a path that does not exist).</summary>
    private string PathFor(Guid gameId, string kind, int? width, string url, string? ext)
    {
        var stem = $"{Slot(kind, width)}.{UrlHash(url)}";
        if (ext is not null) return Path.Combine(GameDir(gameId), $"{stem}.{ext}");
        return Directory.Exists(GameDir(gameId))
            ? Directory.EnumerateFiles(GameDir(gameId), stem + ".*").FirstOrDefault() ?? Path.Combine(GameDir(gameId), stem)
            : Path.Combine(GameDir(gameId), stem);
    }

    private IEnumerable<string> Entries(Guid gameId, string kind, int? width) =>
        Directory.Exists(GameDir(gameId))
            ? Directory.EnumerateFiles(GameDir(gameId), Slot(kind, width) + ".*").Where(f => !Path.GetFileName(f).StartsWith('.')).ToList()
            : [];

    private static Image? Read(string path)
    {
        if (!TypeByExt.TryGetValue(Path.GetExtension(path).TrimStart('.'), out var type)) return null;
        try { return File.Exists(path) ? new Image(File.ReadAllBytes(path), type) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static int WidthOf(string path)
    {
        var slot = Path.GetFileName(path).Split('.')[0];
        return int.TryParse(slot[(slot.IndexOf('-') + 1)..], out var w) ? w : int.MaxValue;
    }

    private static int? Normalize(int? width) => width is { } w && Array.IndexOf(ServerWidths, w) >= 0 ? w : null;

    private static string UrlHash(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
