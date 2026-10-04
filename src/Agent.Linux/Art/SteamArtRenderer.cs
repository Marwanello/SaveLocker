using System.IO.Compression;
using StbImageSharp;

namespace SaveLocker.Agent.Linux.Art;

/// <summary>
/// Paints the four Steam library pieces (capsule, wide capsule, hero, logo) for whichever accent and mark
/// are in effect. The shapes are not drawn here: <c>web/scripts/export-art.mjs</c> rasterises the text and each
/// mark once, in placeholder colours, into layer PNGs; this only lays a background under them and tints them.
/// <para>
/// That keeps the pictures the same as the fixed ones the installer bundles (Ember, Pixel lock) without an SVG
/// renderer in the agent, and it is why a new accent needs no new file. The layer encoding is written down at
/// <c>layerSvgs</c> in the script: white text as alpha, accent text as alpha, marks as red (accent) and blue
/// (ink) whose blue share is how much ink a pixel holds.
/// </para>
/// </summary>
public static class SteamArtRenderer
{
    public static readonly string[] Pieces = ["capsule", "capsule-wide", "hero", "logo"];
    public static readonly string[] Marks = ["pixel", "cartridge", "memcard"];

    /// <summary>The logo is laid over the hero by Steam, so it stays transparent; the rest carry the brand wash.</summary>
    private static bool HasWash(string piece) => piece != "logo";

    private static readonly int[] WashBase = [0x10, 0x10, 0x14];
    private static readonly int[] WashEdge = [0x0b, 0x0b, 0x0e];

    // The brand kit's wash: the accent at 42% into #101014 at the top-left, easing to #0b0b0e by 68% of the radius.
    private const double AccentShare = 0.42;
    private const double WashStop = 0.68;
    private const double LineAngle = 25 * Math.PI / 180;
    private const double LinePeriod = 9, LineWidth = 2, LineAlpha = 0.05;

    /// <summary>One piece as PNG bytes. <paramref name="layers"/> maps a layer file name to its bytes.</summary>
    public static byte[] RenderPng(string piece, int accent, int onAccent, string mark, Func<string, byte[]> layers)
    {
        var (rgba, w, h) = Render(piece, accent, onAccent, mark, layers);
        return Png.Encode(rgba, w, h);
    }

    public static (byte[] Rgba, int Width, int Height) Render(
        string piece, int accent, int onAccent, string mark, Func<string, byte[]> layers)
    {
        if (!Pieces.Contains(piece)) throw new ArgumentException($"Unknown art piece '{piece}'.", nameof(piece));
        if (!Marks.Contains(mark)) mark = "pixel";

        var white = Decode(layers($"{piece}.white.png"));
        var accentText = Decode(layers($"{piece}.accent.png"));
        var markLayer = Decode(layers($"{piece}.mark-{mark}.png"));
        int w = white.Width, h = white.Height;
        if (accentText.Width != w || markLayer.Width != w || accentText.Height != h || markLayer.Height != h)
            throw new InvalidDataException($"The layers of '{piece}' are not the same size.");

        var acc = Channels(accent);
        var ink = Channels(onAccent);
        // Premultiplied accumulator: p* hold colour * alpha, pa the alpha.
        var pr = new float[w * h]; var pg = new float[w * h]; var pb = new float[w * h]; var pa = new float[w * h];

        if (HasWash(piece)) Wash(pr, pg, pb, pa, w, h, acc);

        void Over(int i, float r, float g, float b, float sa)
        {
            pr[i] = r * sa + pr[i] * (1 - sa);
            pg[i] = g * sa + pg[i] * (1 - sa);
            pb[i] = b * sa + pb[i] * (1 - sa);
            pa[i] = sa + pa[i] * (1 - sa);
        }

        for (int i = 0; i < w * h; i++)
        {
            int o = i * 4;
            // The mark: red is accent, blue is ink, so the pixel's blue share picks the colour.
            float ma = markLayer.Data[o + 3] / 255f;
            if (ma > 0)
            {
                float r = markLayer.Data[o], b = markLayer.Data[o + 2];
                float p = r + b > 0 ? b / (r + b) : 0;
                Over(i, Lerp(acc[0], ink[0], p), Lerp(acc[1], ink[1], p), Lerp(acc[2], ink[2], p), ma);
            }
            float wa = white.Data[o + 3] / 255f;
            if (wa > 0) Over(i, 255, 255, 255, wa);
            float aa = accentText.Data[o + 3] / 255f;
            if (aa > 0) Over(i, acc[0], acc[1], acc[2], aa);
        }

        var outBytes = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            float a = pa[i];
            int o = i * 4;
            if (a <= 0) continue;
            outBytes[o] = ToByte(pr[i] / a);
            outBytes[o + 1] = ToByte(pg[i] / a);
            outBytes[o + 2] = ToByte(pb[i] / a);
            outBytes[o + 3] = ToByte(a * 255f);
        }
        return (outBytes, w, h);
    }

    private static void Wash(float[] pr, float[] pg, float[] pb, float[] pa, int w, int h, float[] accent)
    {
        var c0 = new float[3];
        for (int k = 0; k < 3; k++) c0[k] = MathF.Round((float)(accent[k] * AccentShare + WashBase[k] * (1 - AccentShare)));
        double cx = w * 0.22, cy = h * 0.08, radius = Math.Max(w, h) * 1.2;
        double cos = Math.Cos(LineAngle), sin = Math.Sin(LineAngle);
        const int Sub = 3;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double dx = x + 0.5 - cx, dy = y + 0.5 - cy;
                double s = Math.Sqrt(dx * dx + dy * dy) / radius;
                float t = s >= WashStop ? 1f : (float)(s / WashStop);
                float r = Lerp(c0[0], WashEdge[0], t), g = Lerp(c0[1], WashEdge[1], t), b = Lerp(c0[2], WashEdge[2], t);

                // Diagonal hairlines: the share of the pixel that falls inside a stripe, from a few samples.
                int inside = 0;
                for (int j = 0; j < Sub; j++)
                    for (int i = 0; i < Sub; i++)
                    {
                        double u = (x + (i + 0.5) / Sub) * cos + (y + (j + 0.5) / Sub) * sin;
                        double f = u - LinePeriod * Math.Floor(u / LinePeriod);
                        if (f < LineWidth) inside++;
                    }
                float la = (float)(LineAlpha * inside / (Sub * Sub));
                int n = y * w + x;
                pr[n] = 255 * la + r * (1 - la);
                pg[n] = 255 * la + g * (1 - la);
                pb[n] = 255 * la + b * (1 - la);
                pa[n] = 1f;
            }
        }
    }

    private static ImageResult Decode(byte[] png) => ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
    private static float[] Channels(int rgb) => [(rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff];
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);

    /// <summary>A minimal 8-bit RGBA PNG writer: adaptive per-row filters and one deflate stream.</summary>
    internal static class Png
    {
        public static byte[] Encode(byte[] rgba, int w, int h)
        {
            var stride = w * 4;
            var raw = new byte[(stride + 1) * h];
            var cand = new byte[4][];
            for (int k = 0; k < 4; k++) cand[k] = new byte[stride];
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < stride; x++)
                {
                    int cur = rgba[row + x];
                    int a = x >= 4 ? rgba[row + x - 4] : 0;
                    int b = y > 0 ? rgba[row - stride + x] : 0;
                    int c = x >= 4 && y > 0 ? rgba[row - stride + x - 4] : 0;
                    cand[0][x] = (byte)cur;
                    cand[1][x] = (byte)(cur - a);
                    cand[2][x] = (byte)(cur - b);
                    cand[3][x] = (byte)(cur - Paeth(a, b, c));
                }
                int best = 0; long bestScore = long.MaxValue;
                for (int k = 0; k < 4; k++)
                {
                    long score = 0;
                    foreach (var v in cand[k]) score += v < 128 ? v : 256 - v;
                    if (score < bestScore) { bestScore = score; best = k; }
                }
                raw[y * (stride + 1)] = (byte)(best == 3 ? 4 : best);   // PNG filter 4 is Paeth; 3 is Average
                Buffer.BlockCopy(cand[best], 0, raw, y * (stride + 1) + 1, stride);
            }

            using var z = new MemoryStream();
            using (var zs = new ZLibStream(z, CompressionLevel.SmallestSize, leaveOpen: true)) zs.Write(raw);

            using var ms = new MemoryStream();
            ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var ihdr = new byte[13];
            WriteBE(ihdr, 0, (uint)w); WriteBE(ihdr, 4, (uint)h);
            ihdr[8] = 8; ihdr[9] = 6;
            Chunk(ms, "IHDR", ihdr);
            Chunk(ms, "IDAT", z.ToArray());
            Chunk(ms, "IEND", []);
            return ms.ToArray();
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static void WriteBE(byte[] buf, int at, uint v)
        {
            buf[at] = (byte)(v >> 24); buf[at + 1] = (byte)(v >> 16); buf[at + 2] = (byte)(v >> 8); buf[at + 3] = (byte)v;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var head = new byte[8];
            WriteBE(head, 0, (uint)data.Length);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head);
            s.Write(data);
            var crc = 0xFFFFFFFFu;
            for (int i = 4; i < 8; i++) crc = CrcTable[(crc ^ head[i]) & 0xff] ^ (crc >> 8);
            foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
            var tail = new byte[4];
            WriteBE(tail, 0, crc ^ 0xFFFFFFFFu);
            s.Write(tail);
        }
    }
}
