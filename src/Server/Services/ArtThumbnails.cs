using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;

namespace SaveLocker.Server.Services;

/// <summary>
/// <c>GET /art/{gameId}/grid.png?w=96</c> — a cached, right-sized copy of a stored cover or icon.
/// Anything else under <c>/art</c> (including the same URL without <c>w</c>) falls through to the
/// static-file handler, so existing URLs keep serving the original.
/// <para>
/// It works on art that is already cached, with no migration and no re-fetch: the thumbnail is made the
/// first time it is asked for and kept in <c>{gameId}/thumbs/</c>. See <see cref="ArtImages.Downscale"/>
/// for why the console wants these at all.
/// </para>
/// <para>
/// Nothing in the request names a path: the game id, file name and extension are matched against a
/// fixed shape, and the width must be one of <see cref="Widths"/>. That is what stops a caller filling
/// the disk with one thumbnail per integer, and what keeps the file lookup away from traversal.
/// </para>
/// </summary>
internal static class ArtThumbnails
{
    /// <summary>The widths a thumbnail may be requested at. Sized for the console's 38 px rows, its grid
    /// tiles and the 94 px detail cover, each at 1× and 2× pixel density.</summary>
    public static readonly int[] Widths = { 48, 64, 96, 128, 192, 256, 384 };

    private static readonly Regex Shape = new(
        @"^/art/(?<game>[0-9a-f]{32})/(?<kind>grid|icon)\.(?<ext>png|jpg|gif|webp)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task HandleAsync(HttpContext ctx, RequestDelegate next, string artRoot)
    {
        var req = ctx.Request;
        if (!(HttpMethods.IsGet(req.Method) || HttpMethods.IsHead(req.Method)) ||
            !int.TryParse(req.Query["w"], out var width) || Array.IndexOf(Widths, width) < 0 ||
            Shape.Match(req.Path.Value ?? "") is not { Success: true } m)
        {
            await next(ctx);
            return;
        }

        var dir = Path.Combine(artRoot, m.Groups["game"].Value);
        var kind = m.Groups["kind"].Value;
        var source = Path.Combine(dir, $"{kind}.{m.Groups["ext"].Value}");

        var thumb = File.Exists(source) ? await EnsureAsync(dir, source, kind, width) : null;
        if (thumb is null)
        {
            await next(ctx); // missing, undecodable, or already smaller than asked: serve the original
            return;
        }

        var written = File.GetLastWriteTimeUtc(thumb);
        var result = Results.File(
            thumb,
            contentType: thumb.EndsWith(".jpg", StringComparison.Ordinal) ? "image/jpeg" : "image/png",
            lastModified: new DateTimeOffset(written, TimeSpan.Zero),
            entityTag: new EntityTagHeaderValue($"\"{written.Ticks:x}\""));
        // The stored URL carries ?v=<write time>, so a re-fetched image is a new URL; a day is safe.
        ctx.Response.Headers.CacheControl = "public, max-age=86400";
        await result.ExecuteAsync(ctx);
    }

    /// <summary>The thumbnail's path, made if it does not exist or the source has changed since.</summary>
    private static async Task<string?> EnsureAsync(string dir, string source, string kind, int width)
    {
        var thumbDir = Path.Combine(dir, "thumbs");
        var sourceWritten = File.GetLastWriteTimeUtc(source);

        // Which format a thumbnail ended up in depends on what was in it (see Downscale), so look for both.
        // A refreshed image is rewritten in place, so "newer than the source" is the whole freshness rule.
        foreach (var ext in new[] { ".jpg", ".png" })
        {
            var existing = Path.Combine(thumbDir, $"{kind}-{width}{ext}");
            if (File.Exists(existing) && File.GetLastWriteTimeUtc(existing) >= sourceWritten) return existing;
        }

        (byte[] Bytes, string Mime)? made;
        try
        {
            // Delete is shared so ArtService can replace the cover by rename while this is reading it.
            await using var stream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Covers are opaque and go out as JPEG; icons keep PNG, where flat art and transparency matter.
            made = ArtImages.Downscale(stream, width, jpegWhenOpaque: kind == "grid");
        }
        catch (IOException) { return null; }
        if (made is null) return null;

        var thumb = Path.Combine(thumbDir, $"{kind}-{width}{(made.Value.Mime == "image/jpeg" ? ".jpg" : ".png")}");
        var temp = Path.Combine(thumbDir, $".{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(thumbDir);
            // Not tied to the request: a client hanging up mid-write would only leave the temp file behind.
            await File.WriteAllBytesAsync(temp, made.Value.Bytes);
            File.Move(temp, thumb, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (IOException) { }
            // Another request may have finished the same thumbnail first, and theirs is as good as ours —
            // but only if it is not the stale one this was regenerating. A read-only volume ends up here too.
            if (!File.Exists(thumb) || File.GetLastWriteTimeUtc(thumb) < sourceWritten) return null;
        }
        return thumb;
    }
}
