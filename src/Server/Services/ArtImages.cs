using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SaveLocker.Server.Services;

/// <summary>
/// Pixel-level helpers for cached artwork. Everything here treats the bytes as untrusted: a source
/// that will not decode, or is absurdly large for its file size, is reported as "no answer" rather
/// than thrown.
/// </summary>
internal static class ArtImages
{
    // A PNG can be a few KB and still declare a billion pixels; decoding it is the cost, not reading it.
    private const long MaxDecodePixels = 40_000_000;

    // ImageSharp's two most common failures on untrusted bytes — UnknownImageFormatException and
    // InvalidImageContentException — derive from ImageFormatException, NOT from ImageProcessingException,
    // so a filter on the latter alone lets both through (measured on 3.1.12).
    private static bool IsDecodeFailure(Exception ex) =>
        ex is ImageFormatException or ImageProcessingException or NotSupportedException;

    /// <summary>
    /// True when ImageSharp can read the image's header: it is a format it knows and the header is intact.
    /// Says nothing about the pixels. An .ico is a real image ImageSharp does not know, so callers that
    /// accept those ask about the extension first.
    /// </summary>
    public static bool IsIdentifiable(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        try { Image.Identify(bytes); return true; }
        catch (Exception ex) when (IsDecodeFailure(ex)) { return false; }
    }

    /// <summary>
    /// True when the header declares more pixels than we will decode. An image that cannot be identified
    /// at all (an .ico, say) is not "oversized" — that is a different question, and callers that pass
    /// small unidentifiable images through must not be able to mistake this for it.
    /// </summary>
    public static bool IsOversized(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        try { return Exceeds(Image.Identify(bytes)); }
        catch (Exception ex) when (IsDecodeFailure(ex)) { return false; }
    }

    private static bool Exceeds(ImageInfo info) => (long)info.Width * info.Height > MaxDecodePixels;

    /// <summary>
    /// True when the image has no pixel that is even partly transparent. Only the pixels can say so:
    /// the header's own alpha flag proved wrong in both directions (an RGBA PNG with one clear pixel
    /// was reported as having none, found by the suite's stub — the first version of this trusted it and
    /// kept a transparent icon). JPEG cannot carry alpha, so it skips the read; everything else is
    /// decoded. Anything that cannot be decoded is "not opaque" (never preferred).
    /// </summary>
    public static bool IsFullyOpaque(byte[] bytes)
    {
        try
        {
            if (Exceeds(Image.Identify(bytes))) return false;
            if (Image.DetectFormat(bytes).Name == "JPEG") return true;

            using var image = Image.Load<Rgba32>(bytes);
            return HasNoTransparency(image);
        }
        catch (Exception ex) when (IsDecodeFailure(ex)) { return false; }
    }

    private static bool HasNoTransparency(Image<Rgba32> image)
    {
        var opaque = true;
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height && opaque; y++)
                foreach (var px in rows.GetRowSpan(y))
                    if (px.A != 255) { opaque = false; break; }
        });
        return opaque;
    }

    /// <summary>
    /// The image at <paramref name="maxWidth"/> wide (aspect kept), resampled with Lanczos — or null when
    /// there is nothing to gain: it is already that narrow, it will not decode, or it is too big to
    /// decode safely. Never upscales. JPEG stays JPEG; everything else becomes PNG so transparency
    /// survives — except with <paramref name="jpegWhenOpaque"/>, which lets an image with no
    /// transparent pixel become a JPEG too. Covers are opaque, and as PNG a downscaled cover is several
    /// times the size for no visible gain; icons are not sent this way, because flat art with hard
    /// edges is what JPEG does worst.
    /// <para>
    /// Why this exists: the console shows 600×900 box art in a 38 px tile. A browser shrinking a source
    /// that much samples only a few of its pixels, which is what produced the jagged, shimmering
    /// edges — measured, not assumed. Resampling once on the server, from the full-size pixels, with a
    /// proper filter and in linear light (<c>Compand</c>, so a bright thin line is not darkened away),
    /// leaves the browser a small image it can draw as-is.
    /// </para>
    /// </summary>
    public static (byte[] Bytes, string Mime)? Downscale(Stream source, int maxWidth, bool jpegWhenOpaque = false)
    {
        try
        {
            var info = Image.Identify(source);
            if (info.Width <= maxWidth || Exceeds(info)) return null;
            source.Position = 0;

            var isJpeg = Image.DetectFormat(source).Name == "JPEG";
            source.Position = 0;

            using var image = Image.Load<Rgba32>(source);
            var height = Math.Max(1, (int)Math.Round(image.Height * (double)maxWidth / image.Width));
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(maxWidth, height),
                Sampler = KnownResamplers.Lanczos3,
                Compand = true,
            }));

            // Checked on the SMALL image: cheap, and what matters is what is being encoded.
            var asJpeg = isJpeg || (jpegWhenOpaque && HasNoTransparency(image));
            using var output = new MemoryStream();
            if (asJpeg) image.Save(output, new JpegEncoder { Quality = 88 });
            else image.Save(output, new PngEncoder());
            return (output.ToArray(), asJpeg ? "image/jpeg" : "image/png");
        }
        catch (Exception ex) when (IsDecodeFailure(ex)) { return null; }
    }
}
