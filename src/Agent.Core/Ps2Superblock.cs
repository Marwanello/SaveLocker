using System.Buffers.Binary;
using System.Text;

namespace SaveLocker.Agent;

/// <summary>
/// The superblock of a formatted 8 MB PS2 memory card, as PCSX2 keeps it in a folder card's
/// <see cref="MemoryCards.FolderCardSuperblock"/> (tasks/emulator-pull-safety Phase 1). PCSX2 makes a new folder card
/// with an EMPTY superblock and writes this only when a game formats the card on its first save
/// (<c>FileMcd_CreateNewCard</c>, <c>FolderMemoryCard::FlushSuperBlock</c>); until then it ignores every save folder on
/// the card (<c>LoadMemoryCardData</c> checks <c>raw[0x16] == 'o'</c>). A save pulled onto such a card is invisible, so
/// SaveLocker writes this in its place. That loses nothing: PCSX2 rebuilds the card's FAT and directories from the
/// folders every time it opens the card.
/// <para>
/// Byte for byte the superblock a game wrote on a real EmuDeck Deck (<c>PS2MC-1_converted.ps2</c>, captured
/// 2026-10-20; the test fixture). The fields are the PS2's own (<c>mcman</c>): 512-byte pages, 2 per 1 KB cluster, 16
/// per erase block, 8192 clusters, the allocatable area from cluster 41 to 8135, the root directory at its first
/// cluster, backup blocks 1023 and 1022, the indirect FAT at cluster 8, no bad blocks. What follows the card flags is
/// what the format wrote there and PCSX2 never reads; it is kept as it was written.
/// </para>
/// </summary>
public static class Ps2Superblock
{
    /// <summary>The size of a folder card's superblock file.</summary>
    public const int Size = 8192;

    /// <summary>Where the format string's <c>o</c> of "Format" is: PCSX2's whole "is it formatted" test.</summary>
    public const int FormattedMarkOffset = 0x16;

    public static byte[] Formatted8Mb()
    {
        var b = new byte[Size];
        Array.Fill(b, (byte)0xFF);
        b.AsSpan(0, 0x184).Clear();
        Encoding.ASCII.GetBytes("Sony PS2 Memory Card Format 1.2.0.0").CopyTo(b, 0);
        var s = b.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(s[0x28..], 512);   // page length
        BinaryPrimitives.WriteUInt16LittleEndian(s[0x2A..], 2);     // pages per cluster
        BinaryPrimitives.WriteUInt16LittleEndian(s[0x2C..], 16);    // pages per erase block
        BinaryPrimitives.WriteUInt16LittleEndian(s[0x2E..], 0xFF00);
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x30..], 8192);  // clusters per card
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x34..], 41);    // first allocatable cluster
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x38..], 8135);  // end of the allocatable area
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x3C..], 0);     // root directory cluster
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x40..], 1023);  // backup block 1
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x44..], 1022);  // backup block 2
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x50..], 8);     // indirect FAT cluster list: one, cluster 8
        s[0xD0..0x150].Fill(0xFF);                                  // bad erase block list: none
        s[0x150] = 2;                                               // card type: PS2
        s[0x151] = 0x2B;                                            // card flags
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x154..], 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x158..], 256);
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x15C..], 8);
        BinaryPrimitives.WriteInt32LittleEndian(s[0x160..], -1);
        BinaryPrimitives.WriteUInt32LittleEndian(s[0x170..], 0xFDE9);
        BinaryPrimitives.WriteInt32LittleEndian(s[0x17C..], -1);
        new byte[] { 0x57, 0xCC, 0x19, 0x76, 0xA6, 0xE9, 0x64, 0x49 }.CopyTo(b, 0x200);
        return b;
    }

    /// <summary>Does <paramref name="superblock"/> say its card is formatted, the way PCSX2 tells: the format string is
    /// there (which puts <c>o</c> at <see cref="FormattedMarkOffset"/>)?</summary>
    public static bool IsFormatted(ReadOnlySpan<byte> superblock) =>
        superblock.Length > FormattedMarkOffset && superblock.StartsWith(Encoding.ASCII.GetBytes(MemoryCards.Ps2CardMagic));
}
