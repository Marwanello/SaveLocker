using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>RetroArch discovery against a fake EmuDeck tree (tasks/emulator-saves Phase 1).</summary>
public sealed class RetroArchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-ra-" + Guid.NewGuid().ToString("N"));

    public RetroArchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Touch(string rel, string content = "sram", DateTime? writtenUtc = null)
    {
        var path = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (writtenUtc is { } at) File.SetLastWriteTimeUtc(path, at);
        return path;
    }

    private string Emulation => Path.Combine(_root, "Emulation");
    private string Saves => Path.Combine(Emulation, "saves", "retroarch", "saves");

    [Theory]
    [InlineData("Chrono Trigger (USA) (Rev 1) [!]", "Chrono Trigger")]
    [InlineData("Legend of Zelda, The - A Link to the Past (USA)", "Legend of Zelda, The - A Link to the Past")]
    [InlineData("Pokemon - Crystal Version (USA, Europe) (Rev 1)", "Pokemon - Crystal Version")]
    [InlineData("Tetris", "Tetris")]
    [InlineData("(Prototype)", "(Prototype)")]
    public void Clean_title_drops_dump_tags(string raw, string expected) =>
        Assert.Equal(expected, RomNames.CleanTitle(raw));

    [Fact]
    public void One_candidate_per_save_named_scoped_and_placed_where_the_file_is()
    {
        Touch("Emulation/saves/retroarch/saves/Chrono Trigger (USA).srm");
        Touch("Emulation/saves/retroarch/saves/Snes9x/Super Metroid (USA).srm");
        Touch("Emulation/saves/retroarch/saves/empty.srm", "");
        Touch("Emulation/saves/retroarch/saves/Chrono Trigger (USA).state");
        Touch("Emulation/roms/snes/Chrono Trigger (USA).sfc");

        var found = RetroArchSaves.Scan(RetroArchConfig.SaveDirectories(new[] { Emulation }, Array.Empty<string>()),
            new[] { Emulation });

        Assert.Equal(new[] { "Chrono Trigger (RetroArch)", "Super Metroid (RetroArch)" }, found.Select(c => c.Name));

        var chrono = found[0];
        Assert.Equal(ScanSource.Emulator, chrono.Source);
        Assert.Equal("RetroArch", chrono.EmulatorName);
        Assert.Equal("snes", chrono.EmulatorSystem);
        Assert.Null(chrono.EmulatorCore);
        Assert.False(chrono.HasSteamCloud);
        Assert.Equal(Path.GetFullPath(Saves), chrono.SuggestedSaveDir);
        Assert.Equal(new[] { "Chrono Trigger (USA).srm", "Chrono Trigger (USA).rtc" }, chrono.IncludeGlobs);

        var metroid = found[1];
        Assert.Equal(Path.Combine(Path.GetFullPath(Saves), "Snes9x"), metroid.SuggestedSaveDir);
        Assert.Equal("Snes9x", metroid.EmulatorCore);
        Assert.Null(metroid.EmulatorSystem);
    }

    [Fact]
    public void Two_saves_with_one_clean_name_keep_the_newest()
    {
        Touch("Emulation/saves/retroarch/saves/Snes9x/Chrono Trigger (USA).srm", writtenUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Touch("Emulation/saves/retroarch/saves/bsnes/Chrono Trigger (Japan).srm", writtenUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var found = RetroArchSaves.Scan(new[] { Saves }, Array.Empty<string>());

        var only = Assert.Single(found);
        Assert.Equal("bsnes", only.EmulatorCore);
    }

    [Fact]
    public void A_missing_saves_folder_yields_nothing()
    {
        Assert.Empty(RetroArchSaves.Scan(new[] { Path.Combine(_root, "nope") }, new[] { Path.Combine(_root, "nope2") }));
        Assert.Empty(RetroArchConfig.SaveDirectories(new[] { Path.Combine(_root, "nope") }, Array.Empty<string>()));
    }

    [Fact]
    public void Standalone_config_savefile_directory_is_honoured()
    {
        var config = Path.Combine(_root, "retroarch");
        Touch("retroarch/retroarch.cfg", "# comment\nsavefile_directory = \":/mysaves\"\nsavestate_directory = \"~/states\"\n");
        Touch("retroarch/mysaves/Tetris.srm");

        var dirs = RetroArchConfig.SaveDirectories(Array.Empty<string>(), new[] { config });

        Assert.Equal(new[] { Path.Combine(config, "mysaves") }, dirs);
    }

    [Fact]
    public void A_config_root_without_a_cfg_falls_back_to_its_saves_folder()
    {
        // EmuDeck for Windows: the RetroArch folder itself, reached when Emulation\saves is a .lnk.
        var config = Path.Combine(_root, "RetroArch");
        Touch("RetroArch/saves/Tetris.srm");

        Assert.Equal(new[] { Path.Combine(config, "saves") },
            RetroArchConfig.SaveDirectories(Array.Empty<string>(), new[] { config }));
    }

    [Fact]
    public void Config_parse_and_path_expansion()
    {
        var cfg = RetroArchConfig.Parse("a = \"1\"\r\nbad line\n# x = 2\nsavefile_directory = \"default\"\na = \"3\"\n");
        Assert.Equal("3", cfg["a"]);
        Assert.False(cfg.ContainsKey("# x"));
        Assert.Null(RetroArchConfig.ExpandPath(cfg["savefile_directory"], _root));
        Assert.Null(RetroArchConfig.ExpandPath("", _root));
        Assert.Equal(Path.Combine(_root, "saves"), RetroArchConfig.ExpandPath(":\\saves", _root));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "x"),
            RetroArchConfig.ExpandPath("~/x", _root));
    }

    [Fact]
    public void EmuDeck_setting_is_read_from_either_script_dialect()
    {
        var sh = Touch("settings.sh", "#!/bin/bash\nemulationPath=/run/media/mmcblk0p1/Emulation\nromsPath=x\n");
        var ps = Touch("settings.ps1", "$emulationPath = \"D:\\Emulation\"\n");
        Assert.Equal("/run/media/mmcblk0p1/Emulation", EmuDeckRoots.ReadSetting(sh, "emulationPath"));
        Assert.Equal("D:\\Emulation", EmuDeckRoots.ReadSetting(ps, "$emulationPath"));
        Assert.Null(EmuDeckRoots.ReadSetting(sh, "missing"));
    }

    [Fact]
    public void A_linked_saves_folder_is_recorded_by_its_real_path()
    {
        var real = Path.Combine(_root, "real-saves");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "Tetris.srm"), "sram");
        Directory.CreateDirectory(Path.Combine(Emulation, "saves", "retroarch"));
        try { Directory.CreateSymbolicLink(Saves, real); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Windows without Developer Mode cannot create links; Linux CI covers it.
        }

        var found = RetroArchSaves.Scan(RetroArchConfig.SaveDirectories(new[] { Emulation }, Array.Empty<string>()),
            Array.Empty<string>());

        Assert.Equal(Path.GetFullPath(real), Assert.Single(found).SuggestedSaveDir);
    }
}
