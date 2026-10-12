using Xunit;

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
}
