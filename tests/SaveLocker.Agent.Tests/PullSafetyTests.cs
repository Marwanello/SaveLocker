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

    // ---- Phase 2 ----

    [Theory]
    [InlineData(0)]     // a card PCSX2 just made: an empty superblock
    [InlineData(-1)]    // none at all
    [InlineData(0xFF)]  // erased
    public void A_pulled_save_on_an_unformatted_card_gets_the_card_formatted(int superblock)
    {
        var card = Card(superblock switch { 0 => [], -1 => null, _ => Enumerable.Repeat((byte)0xFF, 8192).ToArray() });

        Assert.True(Pcsx2Saves.FormatCardOf(Game("PCSX2", card), _log.Add, () => false));

        Assert.Equal(Ps2Superblock.Formatted8Mb(), Superblock(card));
        Assert.True(MemoryCards.FolderCardFormatted(card));
        Assert.DoesNotContain(Directory.GetFiles(card), f => f.EndsWith(".savelocker-tmp", StringComparison.Ordinal));
        Assert.Contains(_log, l => l.Contains("formatted PCSX2 card \"mc1.ps2\""));
    }

    [Fact]
    public void A_formatted_card_is_never_formatted_again()
    {
        var own = Ps2Superblock.Formatted8Mb();
        own[0x170] = 0x12; // another card's own bytes
        var card = Card(own);

        Assert.False(Pcsx2Saves.FormatCardOf(Game("PCSX2", card), _log.Add, () => false));

        Assert.Equal(own, Superblock(card));
    }

    [Fact]
    public void Only_a_pcsx2_folder_card_is_touched()
    {
        var card = Card([]);
        Assert.False(Pcsx2Saves.FormatCardOf(Game("DuckStation", card), _log.Add, () => false));
        Assert.Empty(Superblock(card));

        // A game on a file card: its folder is the memory cards folder, not a card.
        var memcards = Path.Combine(_root, "memcards");
        Directory.CreateDirectory(memcards);
        File.WriteAllBytes(Path.Combine(memcards, "Mcd001.ps2"), Ps2Superblock.Formatted8Mb());
        Assert.False(Pcsx2Saves.FormatCardOf(Game("PCSX2", memcards), _log.Add, () => false));
        Assert.Equal(Ps2Superblock.Formatted8Mb(), File.ReadAllBytes(Path.Combine(memcards, "Mcd001.ps2")));
        Assert.Single(Directory.GetFileSystemEntries(memcards));
    }

    [Fact]
    public void A_card_is_left_alone_while_pcsx2_is_open_and_said_so()
    {
        var card = Card([]);

        Assert.False(Pcsx2Saves.FormatCardOf(Game("PCSX2", card), _log.Add, () => true));

        Assert.Empty(Superblock(card));
        Assert.Contains(_log, l => l.Contains("can't see the saves") && l.Contains("once PCSX2 is closed"));
    }

    [Fact]
    public void A_scan_formats_an_unformatted_card_holding_saves_and_nothing_else()
    {
        var saves = Path.Combine(_root, "saves");
        var card = Card([]);
        var empty = Path.Combine(saves, "mc2.ps2");
        Directory.CreateDirectory(empty);
        File.WriteAllBytes(Path.Combine(empty, MemoryCards.FolderCardSuperblock), []);
        var folders = new[] { new RomSaveFolders(saves, null) };

        Pcsx2Saves.FormatCardsWithSaves(folders, () => true, _log.Add);
        Assert.Empty(Superblock(card));

        Pcsx2Saves.FormatCardsWithSaves(folders, () => false, _log.Add);
        Assert.Equal(Ps2Superblock.Formatted8Mb(), Superblock(card));
        Assert.Empty(File.ReadAllBytes(Path.Combine(empty, MemoryCards.FolderCardSuperblock)));

        // Its games are still found after it is formatted.
        Assert.Contains(Pcsx2Saves.Scan(folders), c => c.EmulatorRom == "BESLES-52822");
    }

    // ---- Phase 3 ----

    [Theory]
    [InlineData("pcsx2-qtx64", "PCSX2", true)]
    [InlineData("pcsx2-qtx64-avx2", "PCSX2", true)]
    [InlineData("PCSX2-qt.exe", "PCSX2", true)]
    [InlineData("pcsx2helper", "PCSX2", false)]
    [InlineData("duckstation-qt-x64-ReleaseLTCG", "DuckStation", true)]
    [InlineData("duckstation-qt-", "DuckStation", true)]   // Linux keeps 15 characters
    [InlineData("PPSSPPWindows64", "PPSSPP", true)]
    [InlineData("PPSSPPSDL", "PPSSPP", true)]
    [InlineData("dolphin-emu", "Dolphin", true)]
    [InlineData("Dolphin.exe", "PrimeHack", true)]
    [InlineData("retroarch", "RetroArch", true)]
    [InlineData("retroarch", "PCSX2", false)]
    [InlineData("emulator_multicpu.exe", "Model 2", true)]
    [InlineData("emulator_multic", "Model 2", true)]       // Proton: 15 characters of emulator_multicpu.exe
    [InlineData("emulationstatio", "Model 2", false)]      // ES-DE is not Model 2
    [InlineData("xenia_canary.ex", "Xenia", true)]
    [InlineData("xenia_manager", "Xenia", false)]
    [InlineData("eden.AppImage", "Eden", true)]
    [InlineData("EdenSync", "Eden", false)]
    public void An_emulator_is_known_by_its_process_on_either_os(string process, string emulator, bool its) =>
        Assert.Equal(its, EmulatorProcesses.IsProcessOf(process, emulator));

    [Fact]
    public void A_pull_waits_while_the_games_emulator_runs_and_only_that_emulator()
    {
        string[] running = ["explorer", "pcsx2-qtx64", "steam"];

        Assert.True(GameActivity.IsActive(Game("PCSX2"), running, out var process));
        Assert.Equal("pcsx2-qtx64", process);
        Assert.False(GameActivity.IsActive(Game("RetroArch"), running, out _));

        // A game taken from the server knows its emulator from the server, not from how this machine found it.
        var adopted = new TrackedGame { GameId = Guid.NewGuid(), Name = "Tomb Raider", Emulators = ["PCSX2"] };
        Assert.True(GameActivity.IsActive(adopted, running, out _));

        // A PC game with no process names still cannot be told.
        Assert.False(GameActivity.IsActive(new TrackedGame { GameId = Guid.NewGuid(), Name = "Hollow Knight" }, running, out _));
    }

    [Fact]
    public void The_refusal_names_the_emulator()
    {
        var game = Game("PCSX2");
        var message = GameActivity.RefusalMessage(game, "pcsx2-qtx64");
        Assert.Contains("PCSX2 is open", message);
        Assert.Contains("Close PCSX2 and pull again", message);

        var pc = new TrackedGame { Name = "Hollow Knight", ProcessNames = ["hollow_knight"] };
        Assert.Contains("the game is running (hollow_knight.exe)", GameActivity.RefusalMessage(pc, "hollow_knight"));

        Assert.Equal("PCSX2", GameActivity.WhatIsOpen(game, "pcsx2-qtx64"));
        Assert.Equal("the game", GameActivity.WhatIsOpen(pc, "hollow_knight"));
    }

    [Fact]
    public void The_emulator_is_never_a_process_of_the_game()
    {
        var game = Game("PCSX2");
        Assert.Empty(game.ProcessNames);
        Assert.Equal(["PCSX2"], EmulatorProcesses.EmulatorsOf(game));
    }

    [Fact]
    public void Closing_an_emulator_is_raised_once_for_its_games_and_starting_one_is_not()
    {
        var ps2 = Game("PCSX2", "/saves/mc1.ps2");
        var ps1 = Game("DuckStation", "/saves/duckstation");
        string[] processes = ["explorer"];
        var watcher = new EmulatorWatcher(() => [ps2, ps1], () => processes);
        var closed = new List<string>();
        watcher.EmulatorClosed += closed.Add;

        processes = ["explorer", "pcsx2-qtx64"];
        watcher.Poll();                       // the first look only records what runs
        processes = ["explorer", "pcsx2-qtx64", "duckstation-qt-x64-ReleaseLTCG"];
        watcher.Poll();                       // DuckStation started: nothing to push yet
        Assert.Empty(closed);
        processes = ["explorer", "duckstation-qt-x64-ReleaseLTCG"];
        watcher.Poll();
        watcher.Poll();
        Assert.Equal(["PCSX2"], closed);

        Assert.Same(ps2, Assert.Single(EmulatorWatcher.GamesOf([ps2, ps1, Game("PCSX2")], "PCSX2")));   // not set up here: left out
    }
}
