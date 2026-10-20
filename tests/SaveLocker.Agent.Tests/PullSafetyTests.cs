using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-pull-safety Phases 1–3: a PCSX2 folder card's formatted superblock, formatting a card a save is pulled
/// onto (or was, before this), and no pull while the game's emulator is open.
/// </summary>
public sealed class PullSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-pullsafety-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    public PullSafetyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static byte[] Captured()
    {
        using var stream = typeof(PullSafetyTests).Assembly.GetManifestResourceStream("pcsx2-superblock-8mb.bin")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static TrackedGame Game(string emulator, string dir = "") => new()
    {
        GameId = Guid.NewGuid(),
        Name = "Prince of Persia - Warrior Within",
        SaveDirectory = dir,
        Source = new GameSourceDto(GameSourceKinds.Emulator, emulator),
    };

    /// <summary>A folder card <c>mc1.ps2</c> holding Warrior Within's save, with the superblock given (null: none).</summary>
    private string Card(byte[]? superblock, bool withSave = true)
    {
        var card = Path.Combine(_root, "saves", "mc1.ps2");
        Directory.CreateDirectory(card);
        if (superblock is not null) File.WriteAllBytes(Path.Combine(card, MemoryCards.FolderCardSuperblock), superblock);
        if (withSave)
        {
            Directory.CreateDirectory(Path.Combine(card, "BESLES-52822WW"));
            File.WriteAllText(Path.Combine(card, "BESLES-52822WW", "BESLES-52822WW"), "save");
        }
        return card;
    }

    private static byte[] Superblock(string card) => File.ReadAllBytes(Path.Combine(card, MemoryCards.FolderCardSuperblock));

    // ---- Phase 1 ----

    [Fact]
    public void The_formatted_superblock_is_the_one_a_game_wrote_on_a_real_card()
    {
        Assert.Equal(Captured(), Ps2Superblock.Formatted8Mb());
        Assert.True(Ps2Superblock.IsFormatted(Captured()));
    }

    [Fact]
    public void A_card_is_formatted_only_with_pcsx2s_mark_at_0x16()
    {
        Assert.False(Ps2Superblock.IsFormatted([]));
        Assert.False(Ps2Superblock.IsFormatted(Enumerable.Repeat((byte)0xFF, Ps2Superblock.Size).ToArray()));
        Assert.False(Ps2Superblock.IsFormatted(new byte[Ps2Superblock.Size]));
        var marked = Ps2Superblock.Formatted8Mb();
        Assert.Equal((byte)'o', marked[Ps2Superblock.FormattedMarkOffset]);
        marked[Ps2Superblock.FormattedMarkOffset] = (byte)'x';
        Assert.False(Ps2Superblock.IsFormatted(marked));
    }
}
