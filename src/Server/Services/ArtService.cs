using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SaveLocker.Server.Data;
using SaveLocker.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace SaveLocker.Server.Services;

/// <summary>
/// Fetches cover/hero/logo/icon artwork for a game from SteamGridDB
/// (https://www.steamgriddb.com/api/v2) and caches the images locally under
/// <c>wwwroot/art/{gameId}/</c>, storing the served relative URLs on the
/// <see cref="Game"/>. Per the design we cache server-side and are polite
/// (fetch on enroll or an explicit refresh).
///
/// Requires a free API key, now managed from the dashboard (<see cref="SettingsService"/>,
/// DB value overriding config <c>SteamGridDb:ApiKey</c> / env <c>SteamGridDb__ApiKey</c>).
/// The key is resolved per call and attached per request, so a dashboard change takes
/// effect immediately without a restart. With no key, refresh is a no-op with an
/// explanatory message.
/// </summary>
public sealed class ArtService
{
    private readonly AppDbContext _db;
    private readonly SettingsService _settings;
    private readonly HttpClient _http;       // api.steamgriddb.com: base address (Bearer added per request)
    private readonly HttpClient _download;   // plain client for CDN image GETs (NO auth header)
    private readonly string _artRoot;        // wwwroot/art
    private string? _apiKey;                 // resolved at the start of each operation
    private readonly string[] _allowedImageHosts;
    private readonly bool _allowInsecureImageUrls;

    /// <summary>Hosts an artwork URL may point at (each also matches its subdomains): SteamGridDB's own
    /// domain, whose CDN (cdn2.steamgriddb.com) serves the images. <c>Art:AllowedImageHosts</c> replaces it.</summary>
    private static readonly string[] DefaultImageHosts = { "steamgriddb.com" };

    // Hero art at full resolution is ~9.5 MB (see HeroMaxWidth); nothing legitimate is near this.
    private const long MaxImageBytes = 25L * 1024 * 1024;
    private const int MaxRedirects = 3;

    // Asset kind -> SteamGridDB endpoint (relative to the api/v2 base).
    // The icon is the exception to "take the first result": see PickIconAsync, which reads a short
    // list. PNG only, because the console draws it in a square tile and PNG is the one icon format we
    // can inspect for transparency.
    private static readonly (string kind, string path)[] Assets =
    {
        ("grid", "grids/game/{0}?dimensions=600x900&types=static&limit=1"),
        ("hero", "heroes/game/{0}?limit=1"),
        ("logo", "logos/game/{0}?limit=1"),
        ("icon", "icons/game/{0}?mimes=image/png"),
    };

    /// <summary>How many icon candidates to download looking for one with no transparent pixels.</summary>
    private const int IconCandidates = 6;

    /// <summary>Options shown per page in the console's picker.</summary>
    public const int OptionsPageSize = 5;

    // Each API page is up to 50 results; ten pages is far more than anyone scrolls, and bounds the work.
    private const int MaxOptionApiPages = 10;

    // A preview is shrunk to fit its tile (see GetOptionsAsync); one that cannot be shrunk is passed
    // through only if it is already small, since it travels inline in the JSON.
    private const int MaxPassthroughPreviewBytes = 300 * 1024;

    private readonly IMemoryCache _cache;

    public ArtService(AppDbContext db, SettingsService settings, IHttpClientFactory factory,
        IWebHostEnvironment env, IConfiguration config, IMemoryCache cache)
    {
        _db = db;
        _settings = settings;
        _cache = cache;
        _http = factory.CreateClient("steamgriddb");
        // Asset images live on a separate CDN host that rejects the API bearer token,
        // so download them with a clean client carrying no Authorization header. It is the
        // no-redirect client: every hop is validated by FetchImageAsync itself.
        _download = factory.CreateClient("steamgriddb-cdn");
        _allowedImageHosts = ConfiguredImageHosts(config);
        _allowInsecureImageUrls = config.GetValue<bool?>("Art:AllowInsecureImageUrls") ?? false;
        _artRoot = ResolveRoot(config, env);
    }

    /// <summary>
    /// Where art lives on disk. In production <c>Storage:ArtRoot</c> points to /data/art (the persistent
    /// volume mount); in dev it falls back to wwwroot/art so local runs work without configuration.
    /// Shared with the thumbnail endpoint, which must read from the same place.
    /// </summary>
    public static string ResolveRoot(IConfiguration config, IWebHostEnvironment env) =>
        config["Storage:ArtRoot"]
            ?? Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "art");

    private Task<string?> ResolveKeyAsync(CancellationToken ct) =>
        _settings.GetEffectiveAsync(SettingsService.SteamGridDbApiKey, ct);

    /// <summary>Check the configured key is accepted by SteamGridDB (used after a dashboard save).</summary>
    public async Task<(bool ok, string message)> VerifyKeyAsync(CancellationToken ct = default)
    {
        var key = await ResolveKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(key))
            return (false, "No SteamGridDB API key is configured.");
        return await VerifyCandidateKeyAsync(key, ct);
    }

    /// <summary>
    /// Celeste. A stable, always-present SteamGridDB game id, used only as something to ask about.
    /// </summary>
    private const int VerifyProbeGameId = 13136;

    /// <summary>
    /// Check a key that has <b>not been stored</b>. The dashboard save used to write first and ask
    /// afterwards, so a typo replaced a working key and the console still reported success — the
    /// only way back was to find the old key again.
    /// <para>
    /// The probe deliberately hits an <b>authenticated</b> endpoint. An older one asked
    /// <c>search/autocomplete</c>, which SteamGridDB serves without a key at all: it returned 200
    /// with real data for a 25-character string of nonsense, so "API key verified" only ever meant
    /// "steamgriddb.com is reachable".
    /// </para>
    /// <para>
    /// <b>Choosing an authenticated endpoint is not enough on its own — the request must also miss
    /// Cloudflare's cache.</b> SteamGridDB sits behind Cloudflare, which caches these responses by
    /// URL and answers from the edge <i>without the origin ever seeing the Authorization header</i>.
    /// Measured 2026-08-15: the fixed probe URL came back <c>200</c> with
    /// <c>{"success":true,…}</c> and <c>cf-cache-status: HIT, age: 37422</c> — a ten-hour-old copy
    /// of somebody's valid-key response, handed to a request carrying deliberate nonsense. So the
    /// bug came back exactly as CS-09 described it, and reading the body's <c>success</c> flag would
    /// <b>not</b> have caught it: the cached body says <c>true</c>. A <c>Cache-Control: no-cache</c>
    /// request header does not help either — the edge ignores it.
    /// </para>
    /// <para>
    /// Hence the unique query parameter: it makes each verification a URL nothing has cached, the
    /// edge answers <c>BYPASS</c>, and the origin actually evaluates the key. It then distinguishes
    /// the cases usefully — <c>Invalid key format</c>, <c>Invalid API key</c>,
    /// <c>Authentication Required</c> — so its own words are worth passing on to whoever is pasting
    /// the key. The <c>success</c> flag is checked too, as a second line rather than the first.
    /// </para>
    /// <para>
    /// This applies only to verification. Art fetches deliberately keep the cacheable URLs: a cached
    /// image is exactly what we want there, and being polite to SteamGridDB is the stated design.
    /// </para>
    /// </summary>
    public async Task<(bool ok, string message)> VerifyCandidateKeyAsync(
        string candidate, CancellationToken ct = default)
    {
        var key = candidate?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return (false, "No SteamGridDB API key is configured.");

        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get, $"grids/game/{VerifyProbeGameId}?limit=1&_={Guid.NewGuid():N}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var resp = await _http.SendAsync(req, ct);

            var body = await resp.Content.ReadAsStringAsync(ct);
            var reason = ErrorFrom(body);

            // Status codes, not just "did I get a document": a rejected key and an unreachable
            // service are different answers, and the admin needs to be told which one happened.
            if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return (false, reason is null
                    ? "SteamGridDB rejected the key (check that it was pasted correctly)."
                    : $"SteamGridDB rejected the key: {reason}.");

            if (!resp.IsSuccessStatusCode)
                return (false, $"Could not verify the key: SteamGridDB answered {(int)resp.StatusCode}.");

            // A 2xx that says success:false is not a verified key. Should not happen now the probe
            // reaches the origin, and it costs one field to not depend on that.
            if (!SucceededBody(body))
                return (false, reason is null
                    ? "SteamGridDB did not accept the key."
                    : $"SteamGridDB did not accept the key: {reason}.");

            return (true, "API key verified with SteamGridDB.");
        }
        catch (Exception ex)
        {
            return (false, "Could not reach SteamGridDB: " + ex.Message);
        }
    }

    /// <summary>The API's own explanation of a refusal, or null if it gave none we can read.</summary>
    private static string? ErrorFrom(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("errors", out var errors) ||
                errors.ValueKind != JsonValueKind.Array)
                return null;

            var text = string.Join("; ", errors.EnumerateArray()
                .Select(e => e.GetString())
                .Where(e => !string.IsNullOrWhiteSpace(e)));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch { return null; }
    }

    /// <summary>
    /// Did the body itself claim success? Unreadable or absent counts as yes — the status code has
    /// already said so, and this is a corroborating check, not a stricter one.
    /// </summary>
    private static bool SucceededBody(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return !doc.RootElement.TryGetProperty("success", out var ok) ||
                   ok.ValueKind != JsonValueKind.False;
        }
        catch { return true; }
    }

    /// <summary>
    /// (Re)fetch and cache artwork for a game by name. Returns a status message.
    /// <para>
    /// With <paramref name="onlyMissing"/> it fills the gaps and leaves whatever is already there alone.
    /// The backfill after a key is added needs that: a cover somebody picked by hand is not "missing",
    /// and re-fetching it would put SteamGridDB's default back over their choice. The explicit
    /// "Refresh art" button does not pass it — asking for a refresh means asking for the default again.
    /// </para>
    /// </summary>
    public async Task<(bool ok, string message)> RefreshArtAsync(
        Guid gameId, CancellationToken ct = default, bool onlyMissing = false)
    {
        _apiKey = await ResolveKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(_apiKey))
            return (false, "SteamGridDB API key not configured — set it in the dashboard (Server settings).");

        var game = await _db.Games.FindAsync(new object?[] { gameId }, ct);
        if (game is null) return (false, "Unknown game.");

        var wanted = Assets.Where(a => !onlyMissing || string.IsNullOrEmpty(UrlFor(game, a.kind))).ToArray();
        if (wanted.Length == 0) return (true, "No art was missing.");

        var sgdbId = await FindGameIdAsync(game.Name, ct);
        if (sgdbId is null) return (false, $"No SteamGridDB match for \"{game.Name}\".");

        var found = new List<string>();
        foreach (var (kind, pathTemplate) in wanted)
        {
            var path = string.Format(pathTemplate, sgdbId);
            string? cached;
            if (kind == "icon")
                cached = await PickIconAsync(gameId, path, ct);
            else
                cached = await FirstAssetUrlAsync(path, ct) is { } url ? await DownloadAsync(gameId, kind, url, ct) : null;
            if (cached is null) continue;

            SetUrl(game, kind, cached);
            found.Add(kind);
        }

        await _db.SaveChangesAsync(ct);
        return found.Count == 0
            ? (false, $"Matched SteamGridDB id {sgdbId} but found no downloadable assets.")
            : (true, $"Updated art: {string.Join(", ", found)}.");
    }

    /// <summary>Best-effort fetch used on enroll; swallows errors so enroll never fails on art.</summary>
    public async Task TryRefreshOnEnrollAsync(Guid gameId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(await ResolveKeyAsync(ct))) return;
        try { await RefreshArtAsync(gameId, ct); } catch { /* art is non-critical */ }
    }

    /// <summary>
    /// Games with no cover or no icon — the two the console draws. Hero and logo are not counted: they
    /// are fetched alongside, but a game that only lacks those is not one anybody can see is incomplete.
    /// </summary>
    public Task<List<Guid>> GameIdsMissingArtAsync(CancellationToken ct = default) =>
        _db.Games
            .Where(g => g.GridUrl == null || g.GridUrl == "" || g.IconUrl == null || g.IconUrl == "")
            .Select(g => g.Id)
            .ToListAsync(ct);

    private static string? UrlFor(Game g, string kind) => kind switch
    {
        "grid" => g.GridUrl, "hero" => g.HeroUrl, "logo" => g.LogoUrl, "icon" => g.IconUrl, _ => null,
    };

    private static void SetUrl(Game g, string kind, string url)
    {
        switch (kind)
        {
            case "grid": g.GridUrl = url; break;
            case "hero": g.HeroUrl = url; break;
            case "logo": g.LogoUrl = url; break;
            case "icon": g.IconUrl = url; break;
        }
    }

    /// <summary>
    /// Store the first candidate icon with no transparent pixel, else the first that downloads at all.
    /// <para>
    /// The console draws the icon in a rounded square in place of the box art. Most icons on SteamGridDB
    /// are logos cut out on a transparent background; on the tile those show the panel colour through
    /// and read as a floating glyph rather than the game's icon. There is no API filter for it, so
    /// the candidates are read and their pixels checked — up to <see cref="IconCandidates"/>, which is
    /// a few small PNGs. Falling back to a transparent one beats no icon: the list would drop to box art.
    /// </para>
    /// </summary>
    private async Task<string?> PickIconAsync(Guid gameId, string path, CancellationToken ct)
    {
        byte[]? firstUsable = null;
        foreach (var url in await AssetUrlsAsync(path, IconCandidates, ct))
        {
            var bytes = await TryFetchAsync(url, ct);
            if (bytes is null || ImageSniffer.DetectExtension(bytes) is null) continue;
            if (ArtImages.IsFullyOpaque(bytes)) return await StoreAsync(gameId, "icon", bytes, ct);
            firstUsable ??= bytes;
        }
        return firstUsable is null ? null : await StoreAsync(gameId, "icon", firstUsable, ct);
    }

    // ----- Choosing art by hand -----

    /// <summary>Make a SteamGridDB image (one of the URLs <see cref="GetOptionsAsync"/> offered) the game's cover or icon.</summary>
    public async Task<(bool ok, string message, Game? game)> SetArtAsync(
        Guid gameId, string kind, string url, CancellationToken ct = default)
    {
        if (kind is not ("grid" or "icon")) return (false, "Art kind must be 'grid' or 'icon'.", null);
        var game = await _db.Games.FindAsync(new object?[] { gameId }, ct);
        if (game is null) return (false, "Unknown game.", null);

        // The URL came from the caller. DownloadAsync is what makes that safe: https, an allowlisted host
        // on every redirect hop, a size cap, and a type decided by the bytes.
        var stored = await DownloadAsync(gameId, kind, url, ct);
        if (stored is null)
            return (false, "That image could not be downloaded — it is not on SteamGridDB, or not a usable image.", null);

        SetUrl(game, kind, stored);
        await _db.SaveChangesAsync(ct);
        return (true, kind == "grid" ? "Cover updated." : "Icon updated.", game);
    }

    /// <summary>
    /// One page (<see cref="OptionsPageSize"/> options) of the game's SteamGridDB covers or icons, each with
    /// a small inline preview. Pages are numbered from 0.
    /// <para>
    /// SteamGridDB pages are much bigger than five and their size is not ours to assume, so the listing
    /// is kept (ten minutes, in memory) and sliced here: page N is items [5N, 5N+5) of one running list
    /// that grows by fetching API pages only as far as needed — plus one more item, so <c>HasMore</c>
    /// is a fact rather than a guess that opens an empty page.
    /// </para>
    /// <para>
    /// Previews are fetched by the server and inlined as <c>data:</c> URIs. The console's CSP allows
    /// images from itself and <c>data:</c> only, and a browser fetching from SteamGridDB's CDN directly
    /// would also tell it who is browsing. Each is shrunk to its tile first, so five of them cost tens
    /// of kilobytes, not five full covers.
    /// </para>
    /// </summary>
    public async Task<(ArtOptionsPageDto? page, string? error)> GetOptionsAsync(
        Guid gameId, string kind, int page, CancellationToken ct = default)
    {
        if (kind is not ("grid" or "icon")) return (null, "Art kind must be 'grid' or 'icon'.");
        if (page < 0) return (null, "Page must be 0 or more.");

        _apiKey = await ResolveKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(_apiKey))
            return (null, "SteamGridDB API key not configured — set it in the dashboard (Server settings).");

        var game = await _db.Games.FindAsync(new object?[] { gameId }, ct);
        if (game is null) return (null, "Unknown game.");

        try
        {
            var sgdbId = await ResolveSgdbIdAsync(game.Name, ct);
            if (sgdbId is null) return (null, $"No SteamGridDB match for \"{game.Name}\".");

            var source = _cache.GetOrCreate($"sgdb-options:{sgdbId}:{kind}", e =>
            {
                e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return new OptionSource();
            })!;
            await FillAsync(source, sgdbId.Value, kind, (page + 1) * OptionsPageSize + 1, ct);

            var slice = source.Items.Skip(page * OptionsPageSize).Take(OptionsPageSize).ToList();
            var hasMore = source.Items.Count > (page + 1) * OptionsPageSize;

            var previews = await Task.WhenAll(slice.Select(o => PreviewAsync(o, kind, ct)));
            var options = slice
                .Select((o, i) => new ArtOptionDto(o.Url, previews[i], o.Width, o.Height, o.Author))
                .ToList();
            return (new ArtOptionsPageDto(kind, page, hasMore, options), null);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            return (null, "Could not reach SteamGridDB: " + ex.Message);
        }
    }

    private sealed record OptionMeta(string Url, string? Thumb, int? Width, int? Height, string? Author);

    /// <summary>The running listing for one (SteamGridDB game, kind). Mutated only under <see cref="Gate"/>.</summary>
    private sealed class OptionSource
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly List<OptionMeta> Items = new();
        public readonly HashSet<string> Seen = new();
        public int NextApiPage;
        public int Raw;
        public bool Exhausted;
    }

    private async Task FillAsync(OptionSource src, int sgdbId, string kind, int need, CancellationToken ct)
    {
        await src.Gate.WaitAsync(ct);
        try
        {
            while (src.Items.Count < need && !src.Exhausted && src.NextApiPage < MaxOptionApiPages)
            {
                var (items, total) = await FetchOptionsPageAsync(sgdbId, kind, src.NextApiPage, ct);
                src.NextApiPage++;
                src.Raw += items.Count;
                foreach (var item in items)
                    if (src.Seen.Add(item.Url)) src.Items.Add(item);   // a repeated page must not repeat options
                if (items.Count == 0 || (total is { } t && src.Raw >= t)) src.Exhausted = true;
            }
        }
        finally { src.Gate.Release(); }
    }

    private async Task<(List<OptionMeta> items, int? total)> FetchOptionsPageAsync(
        int sgdbId, string kind, int apiPage, CancellationToken ct)
    {
        // Every portrait size SteamGridDB has for covers — they all crop to the console's 2:3 tile, and
        // the default fetch's 600x900 alone leaves too little to choose from. nsfw=false because these
        // are drawn in the console unprompted.
        var path = kind == "grid"
            ? $"grids/game/{sgdbId}?dimensions=600x900,342x482,660x930&types=static&nsfw=false&page={apiPage}"
            : $"icons/game/{sgdbId}?nsfw=false&page={apiPage}";

        using var doc = await GetJsonAsync(path, ct);
        if (doc is null)
        {
            // Refused on the FIRST page means the request itself is bad (a rejected key); refused later is
            // just the end of the list, which some endpoints answer with an error rather than an empty page.
            if (apiPage == 0) throw new HttpRequestException("SteamGridDB did not accept the request — check the API key.");
            return (new List<OptionMeta>(), null);
        }

        var items = new List<OptionMeta>();
        var root = doc.RootElement;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in data.EnumerateArray())
            {
                if (Str(e, "url") is not { Length: > 0 } url) continue;
                string? author = null;
                if (e.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object) author = Str(a, "name");
                items.Add(new OptionMeta(url, Str(e, "thumb"), Int(e, "width"), Int(e, "height"), author));
            }
        }
        return (items, Int(root, "total"));

        static string? Str(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static int? Int(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
    }

    private async Task<string?> PreviewAsync(OptionMeta option, string kind, CancellationToken ct)
    {
        var bytes = await TryFetchAsync(option.Thumb ?? option.Url, ct);
        if (bytes is null && option.Thumb is not null) bytes = await TryFetchAsync(option.Url, ct);
        if (bytes is null) return null;

        var ext = ImageSniffer.DetectExtension(bytes);
        if (ext is null) return null;

        // Sized for the picker's tiles at 2× density: ~84 px wide covers, ~56 px icons.
        using var stream = new MemoryStream(bytes);
        if (ArtImages.Downscale(stream, kind == "grid" ? 200 : 128, jpegWhenOpaque: kind == "grid") is { } small)
            return $"data:{small.Mime};base64,{Convert.ToBase64String(small.Bytes)}";

        // Already narrower than the tile (or undecodable, like .ico): send it as it is, if it is small.
        return bytes.Length <= MaxPassthroughPreviewBytes
            ? $"data:{MimeFor(ext)};base64,{Convert.ToBase64String(bytes)}"
            : null;
    }

    private static string MimeFor(string ext) => ext switch
    {
        ".png" => "image/png", ".jpg" => "image/jpeg", ".gif" => "image/gif",
        ".webp" => "image/webp", _ => "image/x-icon",
    };

    private async Task<int?> ResolveSgdbIdAsync(string name, CancellationToken ct)
    {
        var key = "sgdb-id:" + name.ToLowerInvariant();
        if (_cache.TryGetValue(key, out int cached)) return cached;
        var id = await FindGameIdAsync(name, ct);
        if (id is { } found) _cache.Set(key, found, TimeSpan.FromMinutes(30));
        return id;
    }

    // ----- SteamGridDB calls -----

    private async Task<int?> FindGameIdAsync(string name, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"search/autocomplete/{Uri.EscapeDataString(name)}", ct);
        if (doc is null) return null;
        var data = doc.RootElement.GetProperty("data");
        return data.GetArrayLength() > 0 ? data[0].GetProperty("id").GetInt32() : null;
    }

    private async Task<string?> FirstAssetUrlAsync(string path, CancellationToken ct) =>
        (await AssetUrlsAsync(path, 1, ct)).FirstOrDefault();

    /// <summary>The first <paramref name="max"/> asset URLs SteamGridDB lists at <paramref name="path"/>, best-scored first.</summary>
    private async Task<List<string>> AssetUrlsAsync(string path, int max, CancellationToken ct)
    {
        var urls = new List<string>();
        using var doc = await GetJsonAsync(path, ct);
        if (doc is null || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return urls;
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String && u.GetString() is { Length: > 0 } s)
                urls.Add(s);
            if (urls.Count >= max) break;
        }
        return urls;
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken ct)
    {
        // Attach the current key per request (it can change at runtime via the dashboard).
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        // SteamGridDB wraps every response in { success, data }.
        if (doc.RootElement.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            doc.Dispose();
            return null;
        }
        return doc;
    }

    // Hero images are wide banners; SteamGridDB serves them at full resolution (~9.5 MB
    // at 1920×620). Cap them at this width to keep file sizes reasonable.
    private const int HeroMaxWidth = 920;

    /// <summary>
    /// Download an asset into wwwroot/art/{gameId}/{kind}{ext}; return its served URL — or null when the
    /// URL is refused or the bytes are not an image we recognise (the caller just skips that asset).
    /// <para>
    /// The URL is <b>data from SteamGridDB's response, not something we chose</b>, and what we fetch
    /// from it is written under <c>/art</c>, which is served from the same origin as the admin console.
    /// So it is checked as untrusted input: https only, host on the allowlist (every redirect hop too),
    /// a size cap, and the stored file's type is decided by its own leading bytes — never by the URL's
    /// extension, which used to let a path ending in <c>.html</c> put an attacker-shaped page on the
    /// console's origin. An unreachable or unresponsive host is also just "skip this asset".
    /// </para>
    /// </summary>
    private async Task<string?> DownloadAsync(Guid gameId, string kind, string url, CancellationToken ct)
    {
        var bytes = await TryFetchAsync(url, ct);
        return bytes is null ? null : await StoreAsync(gameId, kind, bytes, ct);
    }

    /// <summary><see cref="FetchImageAsync"/>, with "the host is unreachable or unresponsive" folded into null.</summary>
    private async Task<byte[]?> TryFetchAsync(string url, CancellationToken ct)
    {
        try { return await FetchImageAsync(url, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { return null; }
    }

    /// <summary>Write downloaded bytes as the game's <paramref name="kind"/> image and return its served URL, or null if they are not a recognised image.</summary>
    private async Task<string?> StoreAsync(Guid gameId, string kind, byte[] bytes, CancellationToken ct)
    {
        var ext = ImageSniffer.DetectExtension(bytes);
        if (ext is null) return null;

        var dir = Path.Combine(_artRoot, gameId.ToString("N"));
        Directory.CreateDirectory(dir);

        string file;
        if (kind == "hero")
        {
            // Downscale to HeroMaxWidth, preserving aspect ratio, and store as JPEG.
            file = Path.Combine(dir, "hero.jpg");
            try { await ResizeHeroAsync(bytes, file, ct); }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException) { return null; }
        }
        else
        {
            file = Path.Combine(dir, kind + ext);
            await File.WriteAllBytesAsync(file, bytes, ct);
        }

        // A replacement in another format (icon.ico over icon.png) would otherwise leave the old file
        // behind, unreferenced, for good.
        foreach (var stale in Directory.EnumerateFiles(dir, kind + ".*"))
            if (!string.Equals(stale, file, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(stale); } catch (IOException) { }

        // Cache-bust with the write time so the dashboard <img> refreshes after a re-fetch.
        var filename = Path.GetFileName(file);
        return $"/art/{gameId:N}/{filename}?v={DateTime.UtcNow.Ticks}";
    }

    /// <summary>The image bytes at <paramref name="url"/>, or null if any hop is not an allowed URL,
    /// the response is an error, or it exceeds <see cref="MaxImageBytes"/>. Redirects are followed by
    /// hand, at most <see cref="MaxRedirects"/> of them, each one re-checked.</summary>
    private async Task<byte[]?> FetchImageAsync(string url, CancellationToken ct)
    {
        var current = Uri.TryCreate(url, UriKind.Absolute, out var first) ? first : null;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (current is null || !IsAllowedImageUrl(current, _allowedImageHosts, _allowInsecureImageUrls))
                return null;

            using var resp = await _download.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is >= 300 and < 400)
            {
                current = resp.Headers.Location is { } loc ? new Uri(current, loc) : null;
                continue;
            }
            if (!resp.IsSuccessStatusCode) return null;
            if (resp.Content.Headers.ContentLength > MaxImageBytes) return null;

            await using var body = await resp.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, ct)) > 0)
            {
                // Counted as it arrives: Content-Length is only a claim, and absent on chunked replies.
                if (buffer.Length + read > MaxImageBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        return null; // too many redirects
    }

    /// <summary>
    /// True for an https URL (http only when <c>Art:AllowInsecureImageUrls</c> is set — the test suite's
    /// stand-in server has no certificate) whose host is one of <paramref name="allowedHosts"/> or a
    /// subdomain of one, and that carries no credentials.
    /// </summary>
    internal static bool IsAllowedImageUrl(Uri uri, IReadOnlyList<string> allowedHosts, bool allowInsecure)
    {
        var secure = uri.Scheme == Uri.UriSchemeHttps;
        if (!secure && !(allowInsecure && uri.Scheme == Uri.UriSchemeHttp)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;

        var host = uri.IdnHost.ToLowerInvariant();
        return allowedHosts.Any(a => host == a || host.EndsWith("." + a, StringComparison.Ordinal));
    }

    private static string[] ConfiguredImageHosts(IConfiguration config)
    {
        var configured = (config["Art:AllowedImageHosts"]?
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                ?? Array.Empty<string>())
            .Concat(config.GetSection("Art:AllowedImageHosts").GetChildren().Select(c => c.Value ?? ""))
            .Select(h => h.Trim().TrimStart('.').ToLowerInvariant())
            .Where(h => h.Length > 0)
            .Distinct()
            .ToArray();
        return configured.Length > 0 ? configured : DefaultImageHosts;
    }

    private static async Task ResizeHeroAsync(byte[] bytes, string destPath, CancellationToken ct)
    {
        using var image = Image.Load(bytes);
        if (image.Width > HeroMaxWidth)
            image.Mutate(x => x.Resize(HeroMaxWidth, 0)); // height=0 preserves aspect ratio

        var encoder = new JpegEncoder { Quality = 85 };
        await using var fs = File.Create(destPath);
        await image.SaveAsync(fs, encoder, ct);
    }
}

/// <summary>
/// Decides what a downloaded artwork file IS from its own leading bytes. Only raster image formats
/// are recognised — deliberately no SVG (it can carry script) and nothing text-based — so a body that
/// is HTML, JSON or a script is refused however its URL or Content-Type dressed it.
/// </summary>
internal static class ImageSniffer
{
    /// <summary>The file extension for a recognised image (".png", ".jpg", ".gif", ".webp", ".ico"), else null.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 &&
            b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return ".png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 6 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'8' &&
            (b[4] == (byte)'7' || b[4] == (byte)'9') && b[5] == (byte)'a') return ".gif";
        if (b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' &&
            b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return ".webp";
        if (b.Length >= 4 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0x01 && b[3] == 0x00) return ".ico";
        return null;
    }
}
