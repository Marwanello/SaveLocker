namespace SaveLocker.Agent.Linux;

/// <summary>
/// The standard CRC-32 (poly 0xEDB88320), table-accelerated: PNG chunks (<see cref="Ui.Screenshot"/>) and Valve's
/// non-Steam shortcut AppID (<see cref="DevSteamShortcut"/>) both use it. Pure, so the tests link it in.
/// </summary>
static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
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

    public static uint Compute(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    public static uint Compute(byte[] data) => Compute(data, []);
}
