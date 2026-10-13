using System.Buffers.Binary;

namespace SaveLocker.Agent;

/// <summary>
/// Ryujinx's save index, <c>bis/system/save/8000000000000000/0/imkvdb.arc</c> (tasks/emulator-saves Phase 6): Ryujinx keeps
/// each save in a numbered folder (<c>bis/user/save/&lt;save ID&gt;/0</c>) handed out in order per install, and this file is
/// what says which title and user a number belongs to. Read from the real files on the PC and the Deck (2026-10-13): an
/// <c>IMKV</c> header (4 reserved bytes, a u32 entry count), then per entry <c>IMEN</c>, a u32 key size and a u32 value size
/// (both 0x40), the key — the save's attribute: program ID u64, user ID as two u64 (high, low), system save ID u64, type u8 —
/// and the value: the save ID u64 first. Ryujinx writes the user as <c>{high:x16}{low:x16}</c> (<c>Profiles.json</c>).
/// SaveLocker only ever reads it.
/// </summary>
public static class RyujinxSaveIndex
{
    /// <summary>The save's kind, as the Switch's file system numbers them.</summary>
    public enum SaveType : byte { System = 0, Account = 1, Bcat = 2, Device = 3, Temporary = 4, Cache = 5, SystemBcat = 6 }

    /// <summary>One save the index knows: whose (title, user, kind) and its folder number.</summary>
    public sealed record Entry(string TitleId, string UserId, SaveType Type, ulong SaveId)
    {
        /// <summary>The save's folder name under <c>bis/user/save</c>.</summary>
        public string Folder => SaveId.ToString("x16");
    }

    /// <summary>The entries of an <c>imkvdb.arc</c>, or none when it is missing or not one.</summary>
    public static IReadOnlyList<Entry> Read(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    public static IReadOnlyList<Entry> Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("IMKV"u8)) return [];
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        var entries = new List<Entry>();
        var at = 12;
        for (var i = 0; i < count && at + 12 <= data.Length; i++)
        {
            if (!data.Slice(at, 4).SequenceEqual("IMEN"u8)) break;
            var keySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 4)..]);
            var valueSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(at + 8)..]);
            at += 12;
            if (keySize < 0x21 || valueSize < 8 || at + keySize + valueSize > data.Length) break;
            var key = data.Slice(at, keySize);
            var programId = BinaryPrimitives.ReadUInt64LittleEndian(key);
            var userHigh = BinaryPrimitives.ReadUInt64LittleEndian(key[8..]);
            var userLow = BinaryPrimitives.ReadUInt64LittleEndian(key[16..]);
            var type = (SaveType)key[0x20];
            var saveId = BinaryPrimitives.ReadUInt64LittleEndian(data[(at + keySize)..]);
            if (programId != 0)
                entries.Add(new Entry(programId.ToString("X16"), $"{userHigh:x16}{userLow:x16}", type, saveId));
            at += keySize + valueSize;
        }
        return entries;
    }

    /// <summary>The <c>imkvdb.arc</c> under a <c>bis/system/save</c> folder: the committed copy, else the working one.</summary>
    public static string? IndexFile(string systemSaves)
    {
        foreach (var copy in new[] { "0", "1" })
        {
            var path = Path.Combine(systemSaves, "8000000000000000", copy, "imkvdb.arc");
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
