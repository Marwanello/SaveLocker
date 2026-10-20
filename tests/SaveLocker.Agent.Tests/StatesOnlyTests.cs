using SaveLocker.Shared;
using Xunit;
using F = SaveLocker.Agent.Tests.ConsoleSaveFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Phase 19: a game played from save states alone, before its first in-game save, is listed for every
/// emulator with states (PPSSPP's shipped with Group D) — named and scoped as if it had a save, so that save joins it; a save
/// anywhere on the machine wins; an empty state, and a state no game's name starts, is no game.
/// </summary>
public sealed class StatesOnlyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-states-" + Guid.NewGuid().ToString("N"));

    public StatesOnlyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string P(string rel) => Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));

    private void Touch(string rel, string content = "state")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(rel))!);
        File.WriteAllText(P(rel), content);
    }

    private IReadOnlyList<ScanCandidate> RetroArch() =>
        RetroArchSaves.Scan(RetroArchConfig.Folders([P("Emulation")], []), [P("Emulation")]);

    [Fact]
    public void RetroArch_lists_a_game_from_its_states_with_the_save_scope_declared()
    {
        Directory.CreateDirectory(P("Emulation/saves/retroarch/saves"));
        Touch("Emulation/saves/retroarch/states/Chrono Trigger (USA).state1");
        Touch("Emulation/saves/retroarch/states/Chrono Trigger (USA).state1.png");
        Touch("Emulation/saves/retroarch/states/Chrono Trigger (USA).state.auto");
        Touch("Emulation/saves/retroarch/states/Empty Game.state", "");
        Touch("Emulation/saves/retroarch/states/notes.txt");
        Touch("Emulation/roms/snes/Chrono Trigger (USA).sfc", "rom");

        var game = Assert.Single(RetroArch());

        Assert.True(game.StatesOnly);
        Assert.Equal("Chrono Trigger", game.Name);
        Assert.Equal("Chrono Trigger (USA)", game.EmulatorRom);
        Assert.Equal("snes", game.EmulatorSystem);
        Assert.Equal(Path.GetFullPath(P("Emulation/saves/retroarch/saves")), game.SuggestedSaveDir);
        Assert.Equal(RetroArchSaves.IncludeGlobsFor("Chrono Trigger (USA)"), game.IncludeGlobs);
        var states = Assert.Single(game.ExtraSaveDirs!);
        Assert.Equal(RomSaves.StatesKey, states.Key);
        Assert.Equal(["Chrono Trigger (USA).state*"], states.IncludeGlobs);
    }

    [Fact]
    public void RetroArch_a_save_wins_over_states_alone()
    {
        Touch("Emulation/saves/retroarch/saves/Snes9x/Chrono Trigger (USA).srm", "sram");
        Touch("Emulation/saves/retroarch/states/Chrono Trigger (USA).state1");

        var game = Assert.Single(RetroArch());
        Assert.False(game.StatesOnly);
        Assert.Equal("Snes9x", game.EmulatorCore);
    }

    [Fact]
    public void RetroArch_states_sorted_by_core_put_the_save_in_that_core_folder()
    {
        Directory.CreateDirectory(P("Emulation/saves/retroarch/saves/mGBA"));
        Touch("Emulation/saves/retroarch/states/mGBA/Metroid Fusion (USA).state");

        var game = Assert.Single(RetroArch());
        Assert.True(game.StatesOnly);
        Assert.Equal("mGBA", game.EmulatorCore);
        Assert.Equal(Path.GetFullPath(P("Emulation/saves/retroarch/saves/mGBA")), game.SuggestedSaveDir);
        Assert.Equal(Path.GetFullPath(P("Emulation/saves/retroarch/states/mGBA")), game.ExtraSaveDirs![0].Dir);
    }

    [Theory]
    [InlineData("Game.state", "Game")]
    [InlineData("Game (USA).state12", "Game (USA)")]
    [InlineData("Game.state.auto", "Game")]
    [InlineData("Game.state1.png", null)]
    [InlineData("Game.srm", null)]
    public void RetroArch_state_names(string file, string? rom) => Assert.Equal(rom, RetroArchSaves.RomOfState(file));

    [Fact]
    public void MelonDs_lists_a_game_from_its_states()
    {
        Directory.CreateDirectory(P("Emulation/saves/melonds/saves"));
        Touch("Emulation/saves/melonds/states/Pokemon Platinum (USA).ml3");
        Touch("Emulation/saves/melonds/states/Other.ml9");

        var game = Assert.Single(MelonDsSaves.Scan([P("Emulation")], []));
        Assert.True(game.StatesOnly);
        Assert.Equal(["Pokemon Platinum (USA).sav"], game.IncludeGlobs);
        Assert.Equal("nds", game.EmulatorSystem);
    }

    [Fact]
    public void Pcsx2_lists_a_game_from_its_states_on_the_first_folder_card()
    {
        var serial = $"SLUS-{F.UnknownSerial("ps2", "SLUS-")}";
        Touch("pcsx2/memcards/Mcd001.ps2/_pcsx2_superblock", "sb");
        Touch("pcsx2/memcards/Mcd002.ps2/_pcsx2_superblock", "sb");
        Touch($"pcsx2/sstates/{serial} (ABCD1234).01.p2s");
        Touch($"pcsx2/sstates/{serial} (ABCD1234).01.p2s.backup");
        Touch("pcsx2/sstates/SLES-50330 (00000000).02.p2s", "");

        var game = Assert.Single(Pcsx2Saves.Scan([new RomSaveFolders(P("pcsx2/memcards"), P("pcsx2/sstates"))]));
        Assert.True(game.StatesOnly);
        Assert.Equal($"BA{serial}", game.EmulatorRom);
        Assert.Equal([$"BA{serial}*/**"], game.IncludeGlobs);
        Assert.Equal(Path.Combine(Path.GetFullPath(P("pcsx2/memcards")), "Mcd001.ps2"), game.SuggestedSaveDir);
        Assert.Equal(Pcsx2Saves.StateGlobsFor(serial), game.ExtraSaveDirs![0].IncludeGlobs);
    }

    [Theory]
    [InlineData("SLUS-21050", "BASLUS-21050")]
    [InlineData("SCES-50330", "BESCES-50330")]
    [InlineData("SLPM-65000", "BISLPM-65000")]
    public void Pcsx2_save_prefix_from_a_serial(string serial, string prefix) => Assert.Equal(prefix, Pcsx2Saves.SavePrefix(serial));

    [Fact]
    public void Pcsx2_states_with_only_a_shared_file_card_are_not_a_game()
    {
        F.Write(P("pcsx2/memcards/Mcd001.ps2"), F.Ps2FileCard());
        Touch("pcsx2/sstates/SLUS-21050 (ABCD1234).01.p2s");

        Assert.DoesNotContain(Pcsx2Saves.Scan([new RomSaveFolders(P("pcsx2/memcards"), P("pcsx2/sstates"))]), c => c.StatesOnly);
    }

    [Fact]
    public void Pcsx2_a_save_on_a_card_wins_over_states_alone()
    {
        Touch("pcsx2/memcards/Mcd001.ps2/_pcsx2_superblock", "sb");
        Touch("pcsx2/memcards/Mcd001.ps2/BASLUS-21050SYS/data", "save");
        Touch("pcsx2/sstates/SLUS-21050 (ABCD1234).01.p2s");

        var game = Assert.Single(Pcsx2Saves.Scan([new RomSaveFolders(P("pcsx2/memcards"), P("pcsx2/sstates"))]));
        Assert.False(game.StatesOnly);
    }

    [Fact]
    public void DuckStation_lists_a_game_from_its_states_under_the_card_name_it_would_get()
    {
        var code = $"SLUS-{F.UnknownSerial("psx", "SLUS-")}";
        Directory.CreateDirectory(P("ds/memcards"));
        Touch($"ds/savestates/{code}_1.sav");
        Touch($"ds/savestates/{code}_resume.sav");

        var game = Assert.Single(DuckStationSaves.Scan([new RomSaveFolders(P("ds/memcards"), P("ds/savestates"))]));
        Assert.True(game.StatesOnly);
        // Not in the bundled table: DuckStation's per-serial card name.
        Assert.Equal(code, game.EmulatorRom);
        Assert.Equal([$"{code}_1.mcd", $"{code}_2.mcd"], game.IncludeGlobs);
        Assert.Equal([$"{code}_*.sav"], game.ExtraSaveDirs![0].IncludeGlobs);
    }

    [Fact]
    public void DuckStation_a_known_title_also_takes_the_card_duckstation_names_with_its_region()
    {
        Directory.CreateDirectory(P("ds/memcards"));
        Touch("ds/savestates/SCUS-94163_1.sav");

        var game = Assert.Single(DuckStationSaves.Scan([new RomSaveFolders(P("ds/memcards"), P("ds/savestates"))]));

        Assert.Equal(["Final Fantasy VII_1.mcd", "Final Fantasy VII_2.mcd", "Final Fantasy VII (*)_1.mcd", "Final Fantasy VII (*)_2.mcd"],
            game.IncludeGlobs);
        Touch("ds/memcards/Final Fantasy VII (USA)_1.mcd");
        Touch("ds/memcards/Final Fantasy VIII_1.mcd");
        Assert.Equal(["Final Fantasy VII (USA)_1.mcd"], SaveArchive.ListFiles(P("ds/memcards"), includeGlobs: game.IncludeGlobs!.ToArray()));
    }

    [Fact]
    public void DuckStation_a_card_carrying_the_code_wins_over_its_states()
    {
        var code = $"SCUS-{F.UnknownSerial("psx", "SCUS-")}";
        F.Write(P("ds/memcards/Crash_1.mcd"), F.Ps1Card($"BA{code}CRASH"));
        Touch($"ds/savestates/{code}_1.sav");

        var game = Assert.Single(DuckStationSaves.Scan([new RomSaveFolders(P("ds/memcards"), P("ds/savestates"))]));
        Assert.False(game.StatesOnly);
    }

    [Fact]
    public void DuckStation_sanitises_a_title_as_a_file_name() =>
        Assert.Equal("Final Fantasy_ VII", DuckStationSaves.SanitizeFileName("Final Fantasy: VII"));

    [Fact]
    public void Dolphin_lists_gamecube_and_wii_games_from_their_states()
    {
        Directory.CreateDirectory(P("dolphin/GC/USA/Card A"));
        Directory.CreateDirectory(P("dolphin/Wii/title"));
        Touch("dolphin/StateSaves/GZZE01.s01");
        Touch("dolphin/StateSaves/RZZE01.s02");
        Touch("dolphin/StateSaves/lastState.sav");
        Touch("dolphin/StateSaves/HAAA01.s01");

        var found = DolphinSaves.Scan([new DolphinFolders(DolphinSaves.EmulatorName, P("dolphin/GC"), P("dolphin/Wii"), P("dolphin/StateSaves"))]);

        Assert.All(found, c => Assert.True(c.StatesOnly));
        var gc = Assert.Single(found, c => c.EmulatorSystem == "gc");
        Assert.Equal("GZZE01", gc.EmulatorRom);
        Assert.Equal(["01-GZZE-*.gci"], gc.IncludeGlobs);
        Assert.Equal(Path.GetFullPath(P("dolphin/GC/USA/Card A")), gc.SuggestedSaveDir);
        Assert.Equal(["GZZE01.s*"], gc.ExtraSaveDirs![0].IncludeGlobs);
        var wii = Assert.Single(found, c => c.EmulatorSystem == "wii");
        Assert.Equal("RZZE", wii.EmulatorRom);
        Assert.Equal(["00010000/525a5a45/data/**"], wii.IncludeGlobs);
        Assert.Equal(["RZZE*.s*"], wii.ExtraSaveDirs![0].IncludeGlobs);
    }

    [Fact]
    public void Dolphin_a_wii_save_wins_over_its_states()
    {
        F.Write(P("dolphin/Wii/title/00010000/525a5a45/data/banner.bin"), F.Banner("Some Game"));
        Touch("dolphin/StateSaves/RZZE01.s01");

        var game = Assert.Single(DolphinSaves.Scan([new DolphinFolders(DolphinSaves.EmulatorName, null, P("dolphin/Wii"), P("dolphin/StateSaves"))]));
        Assert.False(game.StatesOnly);
    }

    [Fact]
    public void Azahar_lists_a_game_from_its_states_in_the_title_folder()
    {
        const string zeros = "00000000000000000000000000000000";
        Directory.CreateDirectory(P($"azahar/sdmc/Nintendo 3DS/{zeros}/{zeros}/title/00040000"));
        Touch("azahar/states/00040000000AB000.01.cst");
        Touch("azahar/states/00040000000AB000.movie0000000000000001.02.cst");
        Touch("azahar/states/0004000E000AB000.01.cst");

        var game = Assert.Single(AzaharSaves.Scan([new AzaharFolders(P("azahar/sdmc"), P("azahar/states"), [])]));
        Assert.True(game.StatesOnly);
        Assert.Equal("00040000000AB000", game.EmulatorRom);
        Assert.Equal(["000ab000/data/**"], game.IncludeGlobs);
        Assert.Equal(AzaharSaves.StateGlobsFor("00040000000AB000"), game.ExtraSaveDirs![0].IncludeGlobs);
    }

    [Fact]
    public void A_game_enrolled_before_its_region_card_was_taken_asks_for_it()
    {
        const string t = "Tomb Raider - The Last Revelation";
        var old = DuckStationSaves.SaveGlobsFor(t);
        Touch($"cards/{t} (Europe, Australia)_1.mcd");
        Assert.Equal([$"{t} (*)_1.mcd", $"{t} (*)_2.mcd"], DuckStationSaves.MissingCardGlobs(old, P("cards")));
        Assert.True(DuckStationSaves.IsCardRepair(old, DuckStationSaves.StatesOnlyCardGlobsFor(t)));

        // Nothing is missing once the scope has them, or when the card is named exactly.
        Assert.Null(DuckStationSaves.MissingCardGlobs(DuckStationSaves.StatesOnlyCardGlobsFor(t), P("cards")));
        Touch($"cards/{t}_1.mcd");
        Assert.Null(DuckStationSaves.MissingCardGlobs(old, P("cards")));
    }

    [Fact]
    public void Only_a_title_scope_with_a_bracketed_card_beside_it_is_repaired()
    {
        Touch("cards/Tomb Raider II (USA)_1.mcd");
        Assert.Null(DuckStationSaves.MissingCardGlobs(DuckStationSaves.SaveGlobsFor("Tomb Raider"), P("cards")));
        Assert.Null(DuckStationSaves.MissingCardGlobs(["*.mcd"], P("cards")));
        Assert.Null(DuckStationSaves.MissingCardGlobs(DuckStationSaves.SaveGlobsFor("Tomb Raider II (USA)"), P("cards")));
        Assert.Null(DuckStationSaves.MissingCardGlobs(DuckStationSaves.SaveGlobsFor("Tomb Raider II"), P("missing")));
        Assert.False(DuckStationSaves.IsCardRepair(DuckStationSaves.SaveGlobsFor("Tomb Raider"), ["*.mcd"]));
    }
}
