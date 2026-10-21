using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-pull-safety Phase 5: a server emulator game is set up on a machine that has never saved it — only
/// where its emulator is set up and its ROM is here (Q4) — and a game taken from the server before its first save here
/// is mapped by its files once that save exists.
/// </summary>
public sealed class EmulatorSetUpTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-setup-" + Guid.NewGuid().ToString("N"));

    public EmulatorSetUpTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Dir(string rel)
    {
        var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(path);
        return path;
    }

    private void File(string rel, string text = "x")
    {
        var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, text);
    }

    private IReadOnlyList<string> Roots => [_root];

    private static GameDto Game(string name, string emulator, string[] scope, string[]? states = null) =>
        new(Guid.NewGuid(), name, null, null, true, IncludeGlobs: scope,
            ExtraPaths: states is null ? null : [new SavePathDto(RomSaves.StatesKey, null, null, states)],
            Emulators: [emulator]);

    private static readonly GameDto TombRaider = Game("Tomb Raider - The Last Revelation", "DuckStation",
        ["Tomb Raider - The Last Revelation_1.mcd", "Tomb Raider - The Last Revelation_2.mcd"], ["SLES-02024*"]);

    private static readonly GameDto GodOfWar = Game("God of War - Chains of Olympus", "PPSSPP",
        ["ULES00853*/**"], ["ULES00853_*"]);

    [Fact]
    public void A_duckstation_game_never_saved_here_is_set_up_where_duckstation_saves_when_its_rom_is_here()
    {
        var saves = Dir("saves/duckstation/saves");
        var states = Dir("saves/duckstation/states");
        Assert.Null(EmulatorPlaces.CandidateFor(TombRaider, Roots, standalone: false));

        File("roms/psx/Tomb Raider - The Last Revelation.chd");
        var row = EmulatorPlaces.CandidateFor(TombRaider, Roots, standalone: false)!;

        Assert.Equal(EmuDeckRoots.RealPath(saves), row.SuggestedSaveDir);
        Assert.Equal(TombRaider.IncludeGlobs, row.IncludeGlobs);
        var statesFolder = Assert.Single(row.ExtraSaveDirs!);
        Assert.Equal((RomSaves.StatesKey, EmuDeckRoots.RealPath(states)), (statesFolder.Key, statesFolder.Dir));
        Assert.Equal(["SLES-02024*"], statesFolder.IncludeGlobs);
        Assert.True(row.NoSaveYet);
        Assert.Equal("DuckStation", row.EmulatorName);
        // It joins the server game by its files.
        Assert.True(Enroller.SameFiles(TombRaider, row));
    }

    [Fact]
    public void A_rom_matches_by_title_ignoring_case_punctuation_and_one_folder_down()
    {
        Dir("saves/ppsspp/saves");
        Dir("saves/ppsspp/states");
        File("roms/psp/PSP games/God Of War - Chains Of Olympus.iso");

        Assert.NotNull(EmulatorPlaces.CandidateFor(GodOfWar, Roots, standalone: false));
        Assert.NotNull(EmulatorPlaces.CandidateFor(GodOfWar with { Name = "God of War: Chains of Olympus" }, Roots, standalone: false));
        Assert.Null(EmulatorPlaces.CandidateFor(GodOfWar with { Name = "God of War - Ghost of Sparta" }, Roots, standalone: false));
    }

    [Fact]
    public void No_emulator_here_or_two_emulators_named_leaves_it_unmapped()
    {
        File("roms/psx/Tomb Raider - The Last Revelation.chd");
        Assert.Null(EmulatorPlaces.CandidateFor(TombRaider, Roots, standalone: false));

        Dir("saves/duckstation/saves");
        Assert.Null(EmulatorPlaces.CandidateFor(TombRaider with { Emulators = ["DuckStation", "RetroArch"] }, Roots, standalone: false));
        Assert.Null(EmulatorPlaces.CandidateFor(TombRaider with { Emulators = ["Dolphin"] }, Roots, standalone: false));
    }

    [Fact]
    public void A_pcsx2_game_goes_on_the_first_folder_card_and_never_on_a_file_card()
    {
        var game = Game("Prince of Persia - Warrior Within", "PCSX2", ["BESLES-52822*/**"]);
        File("roms/ps2/Prince of Persia - Warrior Within.iso");
        Dir("saves/pcsx2/saves");
        System.IO.File.WriteAllBytes(Path.Combine(_root, "saves", "pcsx2", "saves", "Mcd001.ps2"), Ps2Superblock.Formatted8Mb());
        Assert.Null(EmulatorPlaces.CandidateFor(game, Roots, standalone: false));

        File("saves/pcsx2/saves/mc1.ps2/_pcsx2_superblock", "");
        var row = EmulatorPlaces.CandidateFor(game, Roots, standalone: false)!;
        Assert.EndsWith("mc1.ps2", row.SuggestedSaveDir);
    }

    [Fact]
    public void A_rom_named_save_needs_the_very_rom_its_scope_was_made_from()
    {
        var hunters = Game("Metroid Prime - Hunters", "melonDS", ["Metroid Prime - Hunters (USA) (Rev 1).sav"]);
        Dir("saves/melonds/saves");
        File("roms/nds/Metroid Prime - Hunters (USA).nds");
        Assert.Null(EmulatorPlaces.CandidateFor(hunters, Roots, standalone: false));

        File("roms/nds/Metroid Prime - Hunters (USA) (Rev 1).nds");
        Assert.NotNull(EmulatorPlaces.CandidateFor(hunters, Roots, standalone: false));
    }

    [Fact]
    public void A_game_adopted_before_its_first_save_is_mapped_by_its_files_whatever_it_is_called()
    {
        var darkSouls = new GameDto(Guid.NewGuid(), "DARK SOULS: REMASTERED Savedata (EU)", null, null, true,
            IncludeGlobs: ["CUSA08495/**"], Emulators: ["shadPS4"]);
        var row = new ScanCandidate("DARK SOULS™: REMASTERED", "/home/deck/.local/share/shadPS4/home/1000/savedata",
            ScanSource.Emulator, false, EmulatorName: "shadPS4", IncludeGlobs: ["CUSA08495/**"]);
        var other = row with { IncludeGlobs = ["CUSA00900/**"] };
        var otherEmulator = row with { EmulatorName = "RPCS3" };

        Assert.Same(row, CommandPoller.SetUpRowFor(darkSouls, [other, otherEmulator, row], _ => null));
        Assert.Null(CommandPoller.SetUpRowFor(darkSouls, [other, otherEmulator], _ => null));
        Assert.Null(CommandPoller.SetUpRowFor(darkSouls, [row with { UntouchedSeed = true }], _ => null));
    }
}
