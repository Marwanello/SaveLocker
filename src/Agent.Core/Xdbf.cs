using System.Buffers.Binary;

namespace SaveLocker.Agent;

/// <summary>
/// An Xbox 360 XDBF file — the format of a profile's <c>.gpd</c> files (tasks/emulator-saves Phase 5c). Big-endian: a 24-byte
/// header (magic, version, entry-table length, entry count, free-table length, free count), 18-byte entries (namespace,
/// id, offset, length), 8-byte free entries, then the data, which offsets are relative to. Checked against the real
/// <c>FFFE07D1.gpd</c> and title GPDs Xenia Canary wrote on a PC and a Deck.
/// </summary>
public sealed class Xdbf
{
    public const ushort Achievement = 1, Image = 2, Setting = 3, Title = 4, String = 5;

    private const uint Magic = 0x58444246; // "XDBF"
    private const int HeaderSize = 24, EntrySize = 18, FreeSize = 8, TableStep = 512;

    private readonly SortedDictionary<(ushort Namespace, ulong Id), byte[]> _entries = new();
    private uint _version = 0x10000;
    private int _entryTable = TableStep, _freeTable = TableStep;

    public IEnumerable<(ushort Namespace, ulong Id, byte[] Data)> Entries => _entries.Select(e => (e.Key.Namespace, e.Key.Id, e.Value));

    public byte[]? Get(ushort ns, ulong id) => _entries.TryGetValue((ns, id), out var d) ? d : null;

    public void Set(ushort ns, ulong id, byte[] data) => _entries[(ns, id)] = data;

    public bool Remove(ushort ns, ulong id) => _entries.Remove((ns, id));

    public static Xdbf Parse(ReadOnlySpan<byte> b)
    {
        if (b.Length < HeaderSize || BinaryPrimitives.ReadUInt32BigEndian(b) != Magic)
            throw new InvalidDataException("Not an XDBF file.");
        var x = new Xdbf
        {
            _version = BinaryPrimitives.ReadUInt32BigEndian(b[4..]),
            _entryTable = checked((int)BinaryPrimitives.ReadUInt32BigEndian(b[8..])),
            _freeTable = checked((int)BinaryPrimitives.ReadUInt32BigEndian(b[16..])),
        };
        var count = BinaryPrimitives.ReadUInt32BigEndian(b[12..]);
        if (count > x._entryTable) throw new InvalidDataException("XDBF entry count exceeds its table.");
        var data = HeaderSize + (long)x._entryTable * EntrySize + (long)x._freeTable * FreeSize;
        if (data > b.Length) throw new InvalidDataException("XDBF tables run past the end of the file.");
        for (var i = 0; i < count; i++)
        {
            var e = b.Slice(HeaderSize + i * EntrySize, EntrySize);
            var ns = BinaryPrimitives.ReadUInt16BigEndian(e);
            var id = BinaryPrimitives.ReadUInt64BigEndian(e[2..]);
            var offset = data + BinaryPrimitives.ReadUInt32BigEndian(e[10..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(e[14..]);
            if (offset + length > b.Length) throw new InvalidDataException($"XDBF entry {ns}/{id:X} runs past the end of the file.");
            x._entries[(ns, id)] = b.Slice((int)offset, (int)length).ToArray();
        }
        return x;
    }

    /// <summary>The file with every entry's data packed in entry order and one free entry marking the end, as Xenia
    /// writes it. The tables keep their size unless the entries outgrow them.</summary>
    public byte[] Serialize()
    {
        while (_entries.Count > _entryTable) _entryTable += TableStep;
        var dataStart = HeaderSize + _entryTable * EntrySize + _freeTable * FreeSize;
        var dataLength = _entries.Values.Sum(d => (long)d.Length);
        var b = new byte[dataStart + dataLength];
        BinaryPrimitives.WriteUInt32BigEndian(b, Magic);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), _version);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), (uint)_entryTable);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), (uint)_entries.Count);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(16), (uint)_freeTable);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(20), 1);
        uint offset = 0;
        var i = 0;
        foreach (var ((ns, id), d) in _entries)
        {
            var e = b.AsSpan(HeaderSize + i++ * EntrySize, EntrySize);
            BinaryPrimitives.WriteUInt16BigEndian(e, ns);
            BinaryPrimitives.WriteUInt64BigEndian(e[2..], id);
            BinaryPrimitives.WriteUInt32BigEndian(e[10..], offset);
            BinaryPrimitives.WriteUInt32BigEndian(e[14..], (uint)d.Length);
            d.CopyTo(b, dataStart + offset);
            offset += (uint)d.Length;
        }
        var free = b.AsSpan(HeaderSize + _entryTable * EntrySize, FreeSize);
        BinaryPrimitives.WriteUInt32BigEndian(free, offset);
        BinaryPrimitives.WriteUInt32BigEndian(free[4..], ~offset);
        return b;
    }
}
