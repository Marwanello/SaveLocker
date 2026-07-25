using System.Buffers.Binary;
using System.IO.Compression;
using Silk.NET.OpenGL;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// Capture the GL framebuffer to a PNG. This exists so the UI's appearance can be reviewed without
/// a person standing in front of the screen — on a dev box under WSLg, and equally on a real Deck,
/// where the alternative is photographing the panel.
///
/// The encoder is hand-rolled against System.IO.Compression rather than pulling in an imaging
/// package: PNG's baseline (8-bit RGB, no interlace, filter 0) is a few dozen lines, and adding a
/// dependency to a self-contained binary for a diagnostic feature is a poor trade.
/// </summary>
static class Screenshot
{
    /// <summary>Read the current framebuffer and write it to <paramref name="path"/> as a PNG.</summary>
    public static unsafe void Capture(GL gl, int width, int height, string path)
    {
        var pixels = new byte[width * height * 3];
        fixed (byte* p = pixels)
        {
            // Rows are tightly packed; the default 4-byte alignment would skew any width not a
            // multiple of 4 into a diagonal smear.
            gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
            gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgb, PixelType.UnsignedByte, p);
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var file = File.Create(path);
        WritePng(file, pixels, width, height);
    }

    private static void WritePng(Stream output, byte[] rgb, int width, int height)
    {
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // colour type: truecolour RGB
        ihdr[10] = 0;  // deflate
        ihdr[11] = 0;  // adaptive filtering
        ihdr[12] = 0;  // no interlace
        WriteChunk(output, "IHDR", ihdr);

        // GL's origin is bottom-left, PNG's is top-left, so scanlines go out in reverse. Each row
        // carries a leading filter byte; 0 (None) keeps the encoder trivial and still compresses
        // well on flat UI colour.
        var stride = width * 3;
        var raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            var src = (height - 1 - y) * stride;
            var dst = y * (stride + 1);
            raw[dst] = 0;
            Array.Copy(rgb, src, raw, dst + 1, stride);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);
        WriteChunk(output, "IDAT", compressed.ToArray());

        WriteChunk(output, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        output.Write(typeBytes);
        output.Write(data);

        // The CRC covers the type and the data, but not the length.
        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
