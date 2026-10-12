using Xunit;
using F = SaveLocker.Agent.Tests.ConsoleSaveFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Group E: the title-ID emulators — Cemu (Wii U), Azahar (3DS) and Xenia (Xbox 360). Each keeps a
/// folder per title ID in a folder every game shares (D4 shape 2), and a game's scope takes only its own folder.
/// </summary>
public sealed class TitleIdSavesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-titleid-" + Guid.NewGuid().ToString("N"));

    public TitleIdSavesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string P(string rel) => Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));

    private void Text(string rel, string text = "data")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(rel))!);
        File.WriteAllText(P(rel), text);
    }

    // ---- Cemu ----

    private const string CemuCache = """
        <?xml version="1.0" encoding="UTF-8"?>
        <title_list>
         <title titleId="0005000010145d00" version="64" sdk_version="21204" group_id="00000145" app_type="80000000">
          <region>2</region>
          <name>Mario Kart 8</name>
          <format>1</format>
          <path>/roms/wiiu/Mario Kart 8.wua</path>
         </title>
         <title titleId="0005000e10145d00" version="64">
          <region>2</region>
          <name>Mario Kart 8 Update</name>
         </title>
        </title_list>
        """;

    [Fact]
    public void A_cemu_game_is_its_title_folder_named_by_cemus_title_cache()
    {
        const string save = "Emulation/saves/Cemu/saves";
        Text($"{save}/00050000/10145d00/user/80000001/save.dat", "mk8");
        Text($"{save}/00050000/10145d00/user/common/db.dat", "mk8-common");
        Text($"{save}/00050000/101c9500/user/80000001/game_data.sav", "botw");
        // Not games: a system app's save, Cemu's account data, an update's folder, and an empty game folder.
        Text($"{save}/00050010/1004a100/user/common/db/data", "system");
        Text($"{save}/system/act/80000001/account.dat", "account");
        Text($"{save}/0005000e/10145d00/user/common/x", "update");
        Directory.CreateDirectory(P($"{save}/00050000/10176900/user"));
        Text("cemu-data/title_list_cache.xml", CemuCache);

        var found = CemuSaves.Scan([new CemuFolders(P(save), null, [P("cemu-data/title_list_cache.xml")], EmuDeck: true)]);

        Assert.Equal(["10145d00", "101c9500"], found.Select(c => c.EmulatorRom));
        var mk8 = found.Single(c => c.EmulatorRom == "10145d00");
        Assert.Equal("Mario Kart 8", mk8.Name);
        Assert.Equal("USA", mk8.Release!.Region);
        Assert.Equal("Wii U", mk8.Release.Console);
        Assert.Equal(["0005000010145d00"], mk8.Release.GameIds!);
        Assert.Equal(["10145d00/**"], mk8.IncludeGlobs);
        Assert.Equal(P($"{save}/00050000"), mk8.SuggestedSaveDir);
        Assert.Equal(CemuSaves.System, mk8.EmulatorSystem);
        Assert.True(mk8.ViaEmuDeck);
        // Not in the cache, not installed: named by its title ID, no region.
        var botw = found.Single(c => c.EmulatorRom == "101c9500");
        Assert.Equal("Wii U 101C9500", botw.Name);
        Assert.Null(botw.Release!.Region);
    }

    [Fact]
    public void An_installed_wii_u_game_is_named_by_its_meta_xml_on_one_line()
    {
        Text("mlc01/usr/save/00050000/101c9500/user/80000001/game_data.sav", "botw");
        Text("mlc01/usr/title/00050000/101c9500/meta/meta.xml", """
            <?xml version="1.0" encoding="utf-8"?>
            <menu type="complex" access="777">
              <region type="hexBinary" length="4">00000004</region>
              <longname_en type="string" length="512">The Legend of Zelda
            Breath of the Wild</longname_en>
            </menu>
            """);
        Text("cemu/settings.xml", $"<?xml version=\"1.0\"?><content><mlc_path>{P("mlc01")}</mlc_path></content>");

        var folders = CemuSaves.Folders([], [new CemuSetup(P("cemu/settings.xml"), P("cemu"))]);
        var game = Assert.Single(CemuSaves.Scan(folders));

        Assert.Equal(P("mlc01"), Assert.Single(folders).Mlc);
        Assert.Equal("The Legend of Zelda Breath of the Wild", game.Name);
        Assert.Equal("Europe", game.Release!.Region);
    }

    [Fact]
    public void Cemus_mlc_is_beside_settings_when_it_names_none_and_emudecks_is_up_from_its_link()
    {
        Text("portable/mlc01/usr/save/00050000/10145d00/user/80000001/save.dat");
        Text("portable/settings.xml", "<?xml version=\"1.0\"?><content><mlc_path></mlc_path></content>");
        Text("Emulation/roms/wiiu/mlc01/usr/save/00050000/101c9500/user/80000001/game_data.sav");
        Directory.CreateDirectory(P("Emulation/saves/Cemu"));
        var link = P("Emulation/saves/Cemu/saves");
        try { Directory.CreateSymbolicLink(link, P("Emulation/roms/wiiu/mlc01/usr/save")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } // no symlink right on this Windows box

        var folders = CemuSaves.Folders([P("Emulation")], [new CemuSetup(P("portable/settings.xml"), P("portable"))]);

        Assert.Equal(2, folders.Count);
        Assert.True(folders[0].EmuDeck);
        Assert.Equal(P("Emulation/roms/wiiu/mlc01"), folders[0].Mlc);
        Assert.Equal(P("portable/mlc01"), folders[1].Mlc);
        Assert.Equal(P("portable/title_list_cache.xml"), folders[0].TitleCaches.Single());
    }

    [Theory]
    [InlineData(2u, "USA")]
    [InlineData(4u, "Europe")]
    [InlineData(1u, "Japan")]
    [InlineData(6u, "USA, Europe")]
    [InlineData(7u, null)]
    [InlineData(0u, null)]
    public void Cemus_region_bits_read_as_redump_regions(uint bits, string? region) =>
        Assert.Equal(region, CemuSaves.Region(bits));
    // ---- Azahar ----

    private const string Sd = "Emulation/storage/azahar/sdmc";
    private const string Titles = Sd + "/Nintendo 3DS/00000000000000000000000000000000/00000000000000000000000000000000/title";

    [Fact]
    public void A_3ds_rom_header_gives_its_program_id_and_product_code()
    {
        F.Write(P("roms/smb.cci"), F.Ncsd(0x0004000000054000, "CTR-P-AREE"));
        F.Write(P("roms/game.cxi"), F.Ncch(0x00040000001BB200, "CTR-P-A9AE"));
        Text("roms/not-a-rom.3ds", new string('x', 0x300));

        Assert.Equal(new Ncch.Header("0004000000054000", "CTR-P-AREE"), Ncch.Read(P("roms/smb.cci")));
        Assert.Equal(new Ncch.Header("00040000001BB200", "CTR-P-A9AE"), Ncch.Read(P("roms/game.cxi")));
        Assert.Null(Ncch.Read(P("roms/not-a-rom.3ds")));
        Assert.Null(Ncch.Read(P("roms/missing.3ds")));
    }

    [Fact]
    public void A_3ds_game_is_its_data_folder_named_by_its_roms_product_code()
    {
        // The Deck's three saves (2026-10-12): Super Mario 3D Land, Metroid: Samus Returns, and one with no ROM left.
        Text($"{Titles}/00040000/00054000/data/00000001/GameData.bin", "smb");
        Text($"{Titles}/00040000/00054000/data/00000001.metadata", "meta");
        Text($"{Titles}/00040000/001bb200/data/00000001/profile0/samus.bmssv", "samus");
        Text($"{Titles}/00040000/0016e300/data/00000001/profile1.sav", "other");
        // A CIA-installed game with no save yet, an update and a DLC: no game.
        Text($"{Titles}/00040000/00055d00/content/00000000.app", "game");
        Text($"{Titles}/0004000e/00055d00/content/00000000.app", "update");
        Text($"{Titles}/0004008c/000a0500/content/00000000.app", "dlc");
        F.Write(P("Emulation/roms/n3ds/Super Mario 3D Land (CTR-P-AREE) (v0.3.0) (U).legit-decrypted.cci"), F.Ncsd(0x0004000000054000, "CTR-P-AREE"));
        // A product code the table has no game of: named by its file name, cleaned.
        F.Write(P("Emulation/roms/n3ds/Samus Homebrew (USA).cci"), F.Ncsd(0x00040000001BB200, "CTR-P-ZZZZ"));

        var found = AzaharSaves.Scan([new AzaharFolders(P(Sd), P("states"), [P("Emulation/roms/n3ds")], EmuDeck: true)]);

        Assert.Equal(["0004000000054000", "000400000016E300", "00040000001BB200"], found.Select(c => c.EmulatorRom).Order(StringComparer.Ordinal));
        var smb = found.Single(c => c.EmulatorRom == "0004000000054000");
        Assert.Equal("Super Mario 3D Land", smb.Name);
        Assert.Equal("USA", smb.Release!.Region);
        Assert.Equal("Nintendo 3DS", smb.Release.Console);
        Assert.Equal("No-Intro", smb.Release.NamedBy);
        Assert.Equal(["CTR-P-AREE", "0004000000054000"], smb.Release.GameIds!);
        // Its data folder only: the installed game's content/ beside it is gigabytes and never synced.
        Assert.Equal(["00054000/data/**"], smb.IncludeGlobs);
        Assert.Equal(P($"{Titles}/00040000"), smb.SuggestedSaveDir);
        var states = Assert.Single(smb.ExtraSaveDirs!);
        Assert.Equal(RomSaves.StatesKey, states.Key);
        Assert.Equal(["0004000000054000.*"], states.IncludeGlobs);
        Assert.Equal("Samus Homebrew", found.Single(c => c.EmulatorRom == "00040000001BB200").Name);
        var noRom = found.Single(c => c.EmulatorRom == "000400000016E300");
        Assert.Equal("3DS 0016E300", noRom.Name);
        Assert.Null(noRom.Release!.Region);
        Assert.All(found, c => Assert.Equal(AzaharSaves.System, c.EmulatorSystem));
    }

    [Fact]
    public void An_installed_3ds_game_is_named_from_its_own_content()
    {
        Text($"{Titles}/00040000/00055d00/data/00000001/main", "pokemon");
        F.Write(P($"{Titles}/00040000/00055d00/content/00000000.app"), F.Ncch(0x0004000000055D00, "CTR-P-EKJA"));

        var game = Assert.Single(AzaharSaves.Scan([new AzaharFolders(P(Sd), null, [])]));

        Assert.Equal("Pokemon X", game.Name);
        Assert.Null(game.ExtraSaveDirs);
    }

    [Fact]
    public void A_standalone_azahars_custom_sd_card_states_and_game_folders_come_from_its_qt_config()
    {
        Text($"{Titles}/00040000/00054000/data/00000001/GameData.bin", "smb");
        F.Write(P("my-roms/smb.3ds"), F.Ncsd(0x0004000000054000, "CTR-P-AREE"));
        Text("config/azahar-emu/qt-config.ini", $"""
            [Data%20Storage]
            use_custom_storage\default=false
            use_custom_storage=true
            sdmc_directory\default=false
            sdmc_directory={P(Sd)}/

            [UI]
            Paths\gamedirs\1\path=INSTALLED
            Paths\gamedirs\2\path=SYSTEM
            Paths\gamedirs\3\path={P("my-roms")}
            """);
        Directory.CreateDirectory(P("data/azahar-emu/states"));
        // Citra's old folder, which has no SD card here: not a setup.
        Directory.CreateDirectory(P("config/citra-emu"));

        var folders = AzaharSaves.Folders([], [new AzaharSetup(P("config/azahar-emu"), P("data/azahar-emu")), new AzaharSetup(P("config/citra-emu"), P("data/citra-emu"))]);
        var setup = Assert.Single(folders);
        var game = Assert.Single(AzaharSaves.Scan(folders));

        Assert.Equal(P(Sd), setup.Sdmc.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Equal(P("data/azahar-emu/states"), setup.States);
        Assert.Contains(P("my-roms"), setup.RomDirs);
        Assert.Equal("Super Mario 3D Land", game.Name);
    }
}
