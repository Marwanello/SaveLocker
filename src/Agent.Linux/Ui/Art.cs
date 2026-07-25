using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using Silk.NET.OpenGL;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// Embedded image assets, decoded and uploaded as GL textures for <c>ImGui.Image</c>.
///
/// The decoder is hand-rolled for the same reason <see cref="Screenshot"/>'s encoder is: baseline
/// PNG is a small, completely specified format, and taking an imaging dependency into a
/// self-contained binary to load one logo is a poor trade. It handles 8-bit RGB and RGBA,
/// non-interlaced — which is what the vendored assets are, and what a re-export must stay.
/// </summary>
static class Art
{
    public readonly record struct Texture(uint Handle, int Width, int Height)
    {
        public bool Ok => Handle != 0;
        public IntPtr Id => (IntPtr)Handle;

        /// <summary>Width when scaled to the given height, for laying out into a fixed-height bar.</summary>
        public float WidthAt(float height) => Height == 0 ? 0f : Width * (height / Height);
    }

    public static Texture Logo { get; private set; }

    /// <summary>
    /// Decode and upload every asset. Must run with a current GL context. Failure is non-fatal: a
    /// missing logo leaves an empty texture and the header falls back to text, rather than taking
    /// the whole Game Mode UI down over decoration.
    /// </summary>
    public static void Load(GL gl)
    {
        Logo = LoadEmbedded(gl, "SaveLocker.Agent.Linux.Ui.Art.logo-96.png");
    }

    private static Texture LoadEmbedded(GL gl, string resource)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream is null)
            {
                Console.Error.WriteLine($"Art: embedded resource not found: {resource}");
                return default;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var (rgba, w, h) = DecodePng(ms.ToArray());
            return Upload(gl, rgba, w, h);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Art: failed to load {resource}: {ex.Message}");
            return default;
        }
    }

    private static unsafe Texture Upload(GL gl, byte[] rgba, int width, int height)
    {
        var handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);

        fixed (byte* p = rgba)
            gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgba8,
                (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        gl.BindTexture(TextureTarget.Texture2D, 0);
        return new Texture(handle, width, height);
    }

    // ── PNG decode ───────────────────────────────────────────────────────────────────────────

    private static (byte[] Rgba, int Width, int Height) DecodePng(byte[] png)
    {
        if (png.Length < 8 || png[0] != 137 || png[1] != 'P' || png[2] != 'N' || png[3] != 'G')
            throw new InvalidDataException("Not a PNG.");

        int width = 0, height = 0, channels = 0;
        var idat = new MemoryStream();
        var pos = 8;

        while (pos + 8 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = pos + 8;

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data));
                    height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(data + 4));
                    var bitDepth = png[data + 8];
                    var colourType = png[data + 9];
                    var interlace = png[data + 12];

                    if (bitDepth != 8)
                        throw new NotSupportedException($"PNG bit depth {bitDepth}; only 8 is supported.");
                    if (interlace != 0)
                        throw new NotSupportedException("Interlaced PNG is not supported.");
                    channels = colourType switch
                    {
                        2 => 3,  // truecolour
                        6 => 4,  // truecolour + alpha
                        _ => throw new NotSupportedException(
                            $"PNG colour type {colourType}; only 2 (RGB) and 6 (RGBA) are supported. " +
                            "Re-export the asset as 32-bit RGBA."),
                    };
                    break;

                case "IDAT":
                    idat.Write(png, data, length);
                    break;

                case "IEND":
                    pos = png.Length;
                    continue;
            }

            pos = data + length + 4; // skip the trailing CRC
        }

        if (width <= 0 || height <= 0 || channels == 0)
            throw new InvalidDataException("PNG header missing or unreadable.");

        idat.Position = 0;
        using var inflate = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflate.CopyTo(raw);

        return (Unfilter(raw.ToArray(), width, height, channels), width, height);
    }

    /// <summary>
    /// Reverse PNG's per-scanline filters and widen to RGBA. Each row is prefixed with a filter byte
    /// and predicted from the pixel to the left (a), the one above (b) and the one above-left (c).
    /// </summary>
    private static byte[] Unfilter(byte[] data, int width, int height, int channels)
    {
        var stride = width * channels;
        var image = new byte[stride * height];
        var src = 0;

        for (int y = 0; y < height; y++)
        {
            if (src >= data.Length) throw new InvalidDataException("PNG data ended early.");
            var filter = data[src++];
            var row = y * stride;
            var prev = row - stride;

            for (int x = 0; x < stride; x++)
            {
                int a = x >= channels ? image[row + x - channels] : 0;
                int b = y > 0 ? image[prev + x] : 0;
                int c = (x >= channels && y > 0) ? image[prev + x - channels] : 0;

                int value = data[src + x] + filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
                };
                image[row + x] = (byte)value;
            }
            src += stride;
        }

        if (channels == 4) return image;

        var rgba = new byte[width * height * 4];
        for (int i = 0, j = 0; i < image.Length; i += 3, j += 4)
        {
            rgba[j] = image[i];
            rgba[j + 1] = image[i + 1];
            rgba[j + 2] = image[i + 2];
            rgba[j + 3] = 255;
        }
        return rgba;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }
}
