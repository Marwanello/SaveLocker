using System.Buffers.Binary;
using System.Text;

namespace SaveLocker.Agent;

/// <summary>One installed package of a game: DLC (<c>00000002</c>) or a title update (<c>000B0000</c>).</summary>
public sealed record ContentPackage(string Kind, string Id, string Name, long Size, string TitleId);

/// <summary>
/// The DLC and title updates Xenia has for a game (tasks/emulator-saves Phase 5c, the DLC check). Current builds keep them
/// under <c>content/0000000000000000/&lt;title ID&gt;/</c>; builds from before profiles kept them under
/// <c>content/&lt;title ID&gt;/</c> (a Deck still has Forza Horizon's there). Each package is a folder with a header in
/// <c>Headers/&lt;type&gt;/&lt;package&gt;.header</c>; only headers and sizes are read, never package data.
/// </summary>
public static class XeniaContent
{
    public const string Dlc = "dlc", TitleUpdate = "update";
    public const string DlcType = "00000002", TitleUpdateType = "000B0000";

    /// <summary>Every DLC and title update installed for a title, in both layouts; a package in both counts once.</summary>
    public static IReadOnlyList<ContentPackage> Inventory(string content, string titleId)
    {
        var found = new Dictionary<string, ContentPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var home in new[] { Path.Combine(content, XeniaSaves.NoProfile, titleId), Path.Combine(content, titleId) })
        {
            foreach (var (type, kind) in new[] { (DlcType, Dlc), (TitleUpdateType, TitleUpdate) })
            {
                foreach (var dir in RomSaves.SafeSubdirs(Path.Combine(home, type)))
                {
                    var id = Path.GetFileName(dir);
                    var header = ReadHeader(Path.Combine(home, "Headers", type, id + ".header"));
                    if (header is null) continue; // a folder Xenia never installed, e.g. "New folder"
                    found.TryAdd(kind + "/" + id, new ContentPackage(kind, id, header.Value.Name, FolderSize.Of(dir), titleId));
                }
            }
        }
        return [.. found.Values.OrderBy(p => p.Kind, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// A package header as Xenia writes it: device (u32), content type (u32), display name (128 UTF-16BE chars), file name
    /// (ASCII), title ID (u32 at 0x140). Older builds wrote 328 bytes, newer 332; both carry the same fields.
    /// </summary>
    public static (uint ContentType, string Name, string FileName, uint TitleId)? ReadHeader(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var b = File.ReadAllBytes(path);
            return ParseHeader(b);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public static (uint ContentType, string Name, string FileName, uint TitleId)? ParseHeader(ReadOnlySpan<byte> b)
    {
        if (b.Length < 0x144) return null;
        var name = Encoding.BigEndianUnicode.GetString(b.Slice(8, 0x100));
        var nul = name.IndexOf('\0');
        var file = Encoding.ASCII.GetString(b.Slice(0x108, 0x38));
        var fileNul = file.IndexOf('\0');
        return (BinaryPrimitives.ReadUInt32BigEndian(b[4..]),
                (nul >= 0 ? name[..nul] : name).Trim(),
                fileNul >= 0 ? file[..fileNul] : file,
                BinaryPrimitives.ReadUInt32BigEndian(b[0x140..]));
    }

    /// <summary>Writes a header the way current Xenia does — for tests and testenv fixtures.</summary>
    public static byte[] BuildHeader(uint contentType, string name, string fileName, uint titleId)
    {
        var b = new byte[332];
        BinaryPrimitives.WriteUInt32BigEndian(b, 1);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), contentType);
        var n = Encoding.BigEndianUnicode.GetBytes(name);
        n.AsSpan(0, Math.Min(n.Length, 0xFE)).CopyTo(b.AsSpan(8));
        var f = Encoding.ASCII.GetBytes(fileName);
        f.AsSpan(0, Math.Min(f.Length, 0x37)).CopyTo(b.AsSpan(0x108));
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0x140), titleId);
        return b;
    }
}
