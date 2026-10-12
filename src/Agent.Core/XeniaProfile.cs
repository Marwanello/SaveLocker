using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SaveLocker.Agent;

/// <summary>
/// A Xenia profile on disk (tasks/emulator-saves Phase 5c): <c>content/&lt;XUID&gt;/FFFE07D1/00010000/&lt;XUID&gt;/</c> holds
/// <c>Account</c>, the dashboard's <c>FFFE07D1.gpd</c> and one <c>&lt;title ID&gt;.gpd</c> per game played. A game owns its
/// own GPD and its own title row in <c>FFFE07D1.gpd</c>; everything else is the profile's.
/// </summary>
public static class XeniaProfile
{
    public const string Dashboard = "FFFE07D1";

    // Settings in FFFE07D1.gpd that are totals over the title rows (XPROFILE_GAMERCARD_*).
    public const ulong GamerscoreSetting = 0x10040006, TitlesPlayedSetting = 0x10040012, AchievementsSetting = 0x10040013;

    // The retail and devkit XEKEY 0x19 Xenia uses for Account (xenia/kernel/util/crypto_utils.cc).
    private static readonly byte[] RetailKey = Convert.FromHexString("E1BC159C73B1EAE9AB3170F3AD47EBF3");
    private static readonly byte[] DevkitKey = Convert.FromHexString("DAB69AD98E28764F977EE2487E4F3F68");

    /// <summary>The profile's own folder under a content root.</summary>
    public static string Folder(string content, string xuid) =>
        Path.Combine(content, xuid, Dashboard, "00010000", xuid);

    public static bool Exists(string content, string xuid) =>
        File.Exists(Path.Combine(Folder(content, xuid), "Account"));

    /// <summary>The gamertag in <c>Account</c>, or null when it is missing or does not decrypt with either key.</summary>
    public static string? Gamertag(string content, string xuid)
    {
        try { return Gamertag(File.ReadAllBytes(Path.Combine(Folder(content, xuid), "Account"))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static string? Gamertag(byte[] account)
    {
        foreach (var key in new[] { RetailKey, DevkitKey })
        {
            var info = DecryptAccount(account, key);
            if (info is null) continue;
            // X_XAMACCOUNTINFO after the 8-byte confounder: two flag words, then the gamertag as 16 UTF-16BE chars.
            var tag = Encoding.BigEndianUnicode.GetString(info, 16, 32);
            var end = tag.IndexOf('\0');
            tag = (end >= 0 ? tag[..end] : tag).Trim();
            return tag.Length > 0 ? tag : null;
        }
        return null;
    }

    /// <summary>The decrypted data of an Account file, or null when its hash does not check out with this key.</summary>
    public static byte[]? DecryptAccount(byte[] account, byte[] key)
    {
        if (account.Length < 16 + 48) return null;
        var rc4Key = HMACSHA1.HashData(key, account.AsSpan(0, 16))[..16];
        var data = Rc4(rc4Key, account.AsSpan(16));
        return HMACSHA1.HashData(key, data).AsSpan(0, 16).SequenceEqual(account.AsSpan(0, 16)) ? data : null;
    }

    /// <summary>Encrypts decrypted account data the way Xenia does — for tests and fixtures.</summary>
    public static byte[] EncryptAccount(byte[] data, bool devkit = false)
    {
        var key = devkit ? DevkitKey : RetailKey;
        var hash = HMACSHA1.HashData(key, data)[..16];
        var rc4Key = HMACSHA1.HashData(key, hash)[..16];
        return [.. hash, .. Rc4(rc4Key, data)];
    }

    internal static byte[] Rc4(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input)
    {
        Span<byte> s = stackalloc byte[256];
        for (var i = 0; i < 256; i++) s[i] = (byte)i;
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }
        var output = new byte[input.Length];
        for (int n = 0, i = 0, j = 0; n < input.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(input[n] ^ s[(s[i] + s[j]) & 0xFF]);
        }
        return output;
    }

    /// <summary>A game's title row in a dashboard GPD, or null when the dashboard does not list it.</summary>
    public static byte[]? TitleRow(byte[] dashboard, uint titleId) => Xdbf.Parse(dashboard).Get(Xdbf.Title, titleId);

    /// <summary>
    /// The dashboard with one game's row put in (added or replaced) and the three totals recounted from all the rows —
    /// every other game's row and every other setting are kept as they are. Xenia loads a game's own GPD only when the
    /// dashboard lists the title (UserProfile::LoadProfileGpds), so this row must arrive with the game's GPD.
    /// </summary>
    public static byte[] MergeTitleRow(byte[] dashboard, uint titleId, byte[] row)
    {
        if (row.Length < 20 || BinaryPrimitives.ReadUInt32BigEndian(row) != titleId)
            throw new InvalidDataException($"Not a title row for {titleId:X8}.");
        var x = Xdbf.Parse(dashboard);
        x.Set(Xdbf.Title, titleId, row);
        Recount(x);
        return x.Serialize();
    }

    /// <summary>Sets gamerscore, games played and achievements earned to the sums over the title rows.</summary>
    public static void Recount(Xdbf x)
    {
        var rows = x.Entries.Where(e => e.Namespace == Xdbf.Title && e.Data.Length >= 20).Select(e => e.Data).ToList();
        SetInt(x, GamerscoreSetting, rows.Sum(r => (int)BinaryPrimitives.ReadUInt32BigEndian(r.AsSpan(16))));
        SetInt(x, TitlesPlayedSetting, rows.Count);
        SetInt(x, AchievementsSetting, rows.Sum(r => (int)BinaryPrimitives.ReadUInt32BigEndian(r.AsSpan(8))));
    }

    public static int? GetInt(Xdbf x, ulong setting) =>
        x.Get(Xdbf.Setting, setting) is { Length: >= 20 } d ? BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(16)) : null;

    // A setting is 24 bytes: id, 4 zero, the type (1 = Int32) and 3 pad, 4 zero, the value, 4 zero. Xenia writes a total
    // only once it is above zero (a Deck's dashboard with nothing unlocked has no gamerscore setting), and so does this.
    private static void SetInt(Xdbf x, ulong setting, int value)
    {
        var existing = x.Get(Xdbf.Setting, setting);
        if (existing is null && value == 0) return;
        var d = existing is { Length: >= 24 } old ? (byte[])old.Clone() : new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(d, (uint)setting);
        d[8] = 1;
        BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(16), value);
        x.Set(Xdbf.Setting, setting, d);
    }
}
