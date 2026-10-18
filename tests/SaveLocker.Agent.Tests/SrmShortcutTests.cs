using System.Text;
using SaveLocker.Shared;
using Xunit;
using F = SaveLocker.Agent.Tests.ConsoleSaveFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Phase 20: a Steam ROM Manager shortcut is the emulator's game, not one of its own. The shortcut
/// commands below are copied from the maintainer's Deck (<c>shortcuts.vdf</c>, captured read-only 2026-10-18), one per shape
/// SRM wrote there.
/// </summary>
public sealed class SrmShortcutTests : IDisposable
{
    private const string Launchers = "/run/media/deck/SDCard/Emulation/tools/launchers";
    private const string Roms = "/run/media/deck/SDCard/Emulation/roms";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-srm-" + Guid.NewGuid().ToString("N"));

    public SrmShortcutTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static SteamShortcut Shortcut(string name, string appId, string rawExe, string? options = null) =>
        new(name, rawExe.Trim('"'), null, appId, RawExe: rawExe, LaunchOptions: options, Tags: []);

    // The Deck's shortcuts, verbatim.
    private static readonly SteamShortcut Mario64 = Shortcut("Super Mario 64", "2342167992",
        $"\"{Launchers}/retroarch.sh\" -L /mupen64plus_next_libretro.so \"{Roms}/n64/Super Mario 64.z64\"");
    private static readonly SteamShortcut MetalGearFlatpak = Shortcut("Metal Gear", "2473684172",
        $"\"/usr/bin/flatpak\" run org.libretro.RetroArch -L /bluemsx_libretro.so \"{Roms}/msx/Metal Gear (E).rom\"");
    private static readonly SteamShortcut Dread = Shortcut("Metroid Dread", "2442134062", $"\"{Launchers}/yuzu.sh\"",
        $"vblank_mode=0 %command% -f -g \"'{Roms}/switch/yuzu/Metroid Dread [010093801237C000][v0].nsp'\"");
    private static readonly SteamShortcut AnimalCrossing = Shortcut("Animal Crossing: New Horizons", "4130579547", $"\"{Launchers}/eden.sh\"",
        $"vblank_mode=0 %command% -f -g \"{Roms}/switch/yuzu/Animal Crossing New Horizons [01006F8002326000][US][v0].nsp\"");
    private static readonly SteamShortcut Forza = Shortcut("Forza Horizon", "2777482215",
        $"\"{Launchers}/xenia.sh\" \"Z:{Roms}/xbox360/roms/Forza Horizon (USA) (En,Ja,Fr,De,Es,It,Pt,Zh,Pl,Ru).iso\"");
    private static readonly SteamShortcut SamusReturns = Shortcut("Metroid: Samus Returns", "4042464773",
        $"\"{Launchers}/azahar.sh\"  \"{Roms}/n3ds/Metroid - Samus Returns (USA) (En,Fr,Es)-decrypted.cci\"");
    private static readonly SteamShortcut OtherM = Shortcut("Metroid: Other M", "2208192691", $"\"{Launchers}/dolphin-emu.sh\"",
        $"vblank_mode=0 %command% -b -e \"{Roms}/wii/Metroid - Other M [R3OE01].wbfs\"");
    private static readonly SteamShortcut Trilogy = Shortcut("Metroid Prime Trilogy", "2242827398", $"\"{Launchers}/primehack.sh\"",
        $"vblank_mode=0 %command% run io.github.shiiion.primehack -b -e \"{Roms}/primehacks/Metroid Prime Trilogy (USA).rvz\"");
    private static readonly SteamShortcut Hunters = Shortcut("Metroid Prime Hunters", "3422230082",
        $"\"{Launchers}/retroarch.sh\" -L /melondsds_libretro.so \"{Roms}/nds/Metroid Prime - Hunters (USA).nds\"");
    private static readonly SteamShortcut HuntersAgain = Shortcut("Metroid Prime Hunters: First Hunt", "3486406069",
        $"\"{Launchers}/retroarch.sh\" -L /melondsds_libretro.so \"{Roms}/nds/Metroid Prime - Hunters (USA).nds\"");
    private static readonly SteamShortcut RetroArchAlone = Shortcut("RetroArch", "3789783018", $"\"{Launchers}/retroarch.sh\"");
    private static readonly SteamShortcut Hellblade = Shortcut("Hellblade: Senua's Sacrifice", "3204429995",
        "\"/home/deck/Games/Hellblade - Senua's Sacrifice/HellbladeGame.exe\"", "/home/deck/.local/share/SaveLocker/savelocker run -- %command%");
    private static readonly SteamShortcut Heroic = Shortcut("Fez", "2285103759", "\"flatpak\"",
        "run com.heroicgameslauncher.hgl --no-gui --no-sandbox \"heroic://launch?appName=Bluebird&runner=legendary\"");

    [Theory]
    [MemberData(nameof(Recognized))]
    public void Each_shape_on_the_deck_is_its_emulators_game(SteamShortcut shortcut, string emulator, string rom)
    {
        var found = SrmShortcuts.Recognize(shortcut);
        Assert.NotNull(found);
        Assert.Equal(emulator, found.Emulator);
        Assert.Equal(rom, found.Rom);
    }

    public static TheoryData<SteamShortcut, string, string> Recognized() => new()
    {
        { Mario64, "RetroArch", $"{Roms}/n64/Super Mario 64.z64" },
        { MetalGearFlatpak, "RetroArch", $"{Roms}/msx/Metal Gear (E).rom" },
        { Dread, "Yuzu", $"{Roms}/switch/yuzu/Metroid Dread [010093801237C000][v0].nsp" },
        { AnimalCrossing, "Eden", $"{Roms}/switch/yuzu/Animal Crossing New Horizons [01006F8002326000][US][v0].nsp" },
        { Forza, "Xenia", $"{Roms}/xbox360/roms/Forza Horizon (USA) (En,Ja,Fr,De,Es,It,Pt,Zh,Pl,Ru).iso" },
        { SamusReturns, "Azahar", $"{Roms}/n3ds/Metroid - Samus Returns (USA) (En,Fr,Es)-decrypted.cci" },
        { OtherM, "Dolphin", $"{Roms}/wii/Metroid - Other M [R3OE01].wbfs" },
        { Trilogy, "PrimeHack", $"{Roms}/primehacks/Metroid Prime Trilogy (USA).rvz" },
    };

    [Fact]
    public void An_emulator_alone_a_pc_game_and_a_launcher_are_not_emulator_games()
    {
        Assert.Null(SrmShortcuts.Recognize(RetroArchAlone));
        Assert.Null(SrmShortcuts.Recognize(Hellblade));
        Assert.Null(SrmShortcuts.Recognize(Heroic));
    }

    [Fact]
    public void A_rom_named_like_an_emulator_does_not_make_a_pc_game_one()
    {
        var odd = Shortcut("Dolphin Adventure", "1", "\"/home/deck/Games/Dolphin Adventure/game.sh\"", $"\"{Roms}/dolphin adventure.iso\"");
        Assert.Null(SrmShortcuts.Recognize(odd));
    }

    [Fact]
    public void Srms_record_stands_in_for_a_launcher_it_does_not_know()
    {
        var custom = Shortcut("Chrono Trigger", "123", "\"/home/deck/my-launcher.sh\"", $"\"{Roms}/snes/Chrono Trigger (USA).sfc\"");
        Assert.Null(SrmShortcuts.Recognize(custom));
        Assert.Equal("RetroArch", SrmShortcuts.Recognize(custom, new HashSet<string> { "123" })!.Emulator);
    }

    [Fact]
    public void Srms_added_items_key_carries_the_app_id_in_its_high_half()
    {
        // The Deck's addedItemsV2.json: a Ryujinx shortcut's 64-bit ID.
        const string json = """{ "version": 2, "addedApps": { "11210316322829238272": { "parserId": "1", "artworkOnly": false } } }""";
        Assert.Equal(["2610105165"], SrmShortcuts.ParseAddedItems(json));
        Assert.Empty(SrmShortcuts.ParseAddedItems("not json"));
    }

    [Fact]
    public void The_shortcuts_vdf_keeps_the_raw_command_launch_options_and_tags()
    {
        var bytes = Vdf(("Forza Horizon", 2777482215u, Forza.RawExe!, null, ["Xbox 360", "Installed"]),
            ("Metroid Dread", 2442134062u, Dread.RawExe!, Dread.LaunchOptions, []));
        var parsed = SteamShortcuts.Parse(bytes);
        Assert.Equal(Forza.RawExe, parsed[0].RawExe);
        Assert.Equal(["Xbox 360", "Installed"], parsed[0].Tags);
        Assert.Equal(Dread.LaunchOptions, parsed[1].LaunchOptions);
        Assert.Equal("2777482215", parsed[0].AppId);
        Assert.NotNull(SrmShortcuts.Recognize(parsed[0]));
    }

    private static ScanCandidate Emulator(string name, string emulator, string rom, string? key = null) =>
        new(name, "/saves", ScanSource.Emulator, false, EmulatorName: emulator, EmulatorRom: rom, EmulatorKey: key);

    private static ScanCandidate ShortcutRow(SteamShortcut s) => new(s.AppName, null, ScanSource.SteamShortcut, false, SteamAppId: s.AppId);

    private static EmulatorShortcut E(SteamShortcut s) => SrmShortcuts.Recognize(s)!;

    [Fact]
    public void The_shortcut_row_goes_and_the_emulator_row_takes_its_app_id()
    {
        var candidates = new List<ScanCandidate>
        {
            ShortcutRow(Forza), ShortcutRow(Hunters), ShortcutRow(HuntersAgain), ShortcutRow(Hellblade), ShortcutRow(AnimalCrossing),
            Emulator("Forza Horizon", "Xenia", "4D5309C9"),
            Emulator("Metroid Prime - Hunters", "RetroArch", "Metroid Prime - Hunters (USA)"),
            Emulator("Animal Crossing: New Horizons", "Yuzu", "01006F8002326000", EmulatorKeys.Switch("01006F8002326000")),
        };

        var result = SrmShortcuts.Apply(candidates, [E(Forza), E(Hunters), E(HuntersAgain), E(AnimalCrossing)], _ => null);

        Assert.Equal(["Hellblade: Senua's Sacrifice"], result.Where(c => c.Source == ScanSource.SteamShortcut).Select(c => c.Name));
        var forza = Assert.Single(result, c => c.EmulatorName == "Xenia");
        Assert.Equal("2777482215", forza.SteamAppId);
        Assert.Equal("Forza Horizon", forza.ShortcutName);
        // SRM added Metroid Prime Hunters twice: both shortcuts start the one game.
        var hunters = Assert.Single(result, c => c.EmulatorName == "RetroArch");
        Assert.Equal(["3422230082", "3486406069"], hunters.ShortcutAppIds);
        Assert.Equal("3422230082", hunters.SteamAppId);
        // Eden's shortcut, Yuzu's save: one game per Switch title across every Switch emulator.
        Assert.Equal("4130579547", Assert.Single(result, c => c.EmulatorName == "Yuzu").SteamAppId);
    }

    [Fact]
    public void A_rom_with_no_save_is_a_row_where_its_save_will_be_or_is_hidden()
    {
        var made = Emulator("Super Mario 64", "RetroArch", "Super Mario 64") with { NoSaveYet = true };
        var result = SrmShortcuts.Apply([ShortcutRow(Mario64), ShortcutRow(Forza)], [E(Mario64), E(Forza)],
            s => s.Emulator == "RetroArch" ? made : null);

        var row = Assert.Single(result);
        Assert.True(row.NoSaveYet);
        Assert.Equal("2342167992", row.SteamAppId);
    }

    [Fact]
    public void A_shortcut_never_joins_another_emulators_row_or_another_rom()
    {
        var candidates = new List<ScanCandidate>
        {
            Emulator("Metroid Prime Trilogy", "Dolphin", "R3ME"),
            Emulator("Super Mario 64 DS", "RetroArch", "Super Mario 64 DS (USA)"),
        };
        var result = SrmShortcuts.Apply(candidates, [E(Trilogy), E(Mario64)], _ => null);
        Assert.All(result, c => Assert.Null(c.ShortcutAppIds));
    }

    [Fact]
    public void A_wii_disc_is_matched_by_its_game_id_in_the_name_or_the_header()
    {
        Assert.True(SrmShortcuts.Match(Emulator("Metroid: Other M", "Dolphin", "R3OE"), E(OtherM)));

        var rvz = Path.Combine(_root, "Metroid Prime Trilogy (USA).rvz");
        var header = new byte[0x80];
        Encoding.ASCII.GetBytes("RVZ\u0001").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("R3ME01").CopyTo(header, 0x58);
        F.Write(rvz, header);
        Assert.Equal("R3ME01", DolphinSaves.DiscGameId(rvz));
        var shortcut = new EmulatorShortcut(Trilogy, "PrimeHack", rvz);
        Assert.True(SrmShortcuts.Match(Emulator("Some Other Name", "PrimeHack", "R3ME"), shortcut));
    }

    [Fact]
    public void A_retroarch_rom_with_no_save_gets_its_save_scope_in_the_saves_folder()
    {
        var saves = Path.Combine(_root, "saves");
        var states = Path.Combine(_root, "states");
        Directory.CreateDirectory(saves);
        var row = SrmShortcuts.RomNamed(E(Mario64), [new RomSaveFolders(saves, states)], RetroArchSaves.Rules, [], perCore: true);

        Assert.NotNull(row);
        Assert.True(row.NoSaveYet);
        Assert.Equal("n64", row.EmulatorSystem);
        Assert.Equal(RetroArchSaves.IncludeGlobsFor("Super Mario 64"), row.IncludeGlobs);
        Assert.Equal(["Super Mario 64.state*"], row.ExtraSaveDirs![0].IncludeGlobs);

        // Saves sorted by core: the folder its first save goes to is not known yet.
        Directory.CreateDirectory(Path.Combine(saves, "Mupen64Plus-Next"));
        Assert.Null(SrmShortcuts.RomNamed(E(Mario64), [new RomSaveFolders(saves, states)], RetroArchSaves.Rules, [], perCore: true));
    }

    [Fact]
    public void The_launch_wrapper_finds_the_game_by_any_of_its_shortcuts()
    {
        var game = new TrackedGame { SteamAppId = "3422230082", ShortcutAppIds = ["3422230082", "3486406069"] };
        Assert.True(game.LaunchesUnder("3486406069"));
        Assert.True(game.LaunchesUnder("3422230082"));
        Assert.False(game.LaunchesUnder("1"));
        Assert.False(game.LaunchesUnder(null));
    }

    [Fact]
    public void A_tracked_shortcut_copy_names_the_emulator_game_it_starts()
    {
        var emulatorGame = new TrackedGame
        {
            GameId = Guid.NewGuid(), Name = "Forza Horizon", ShortcutAppIds = ["2777482215"],
            Source = new SaveLocker.Shared.GameSourceDto(SaveLocker.Shared.GameSourceKinds.Emulator, "Xenia"),
        };
        var copy = new TrackedGame { GameId = Guid.NewGuid(), Name = "Forza Horizon (2)", SteamAppId = "2777482215" };
        var same = SameGameAsDto.For(copy, [emulatorGame, copy]);
        Assert.Equal(new SameGameAsDto(emulatorGame.GameId, "Forza Horizon", "Xenia"), same);
        Assert.Null(SameGameAsDto.For(emulatorGame, [emulatorGame, copy]));
    }

    /// <summary>
    /// The Deck's whole <c>shortcuts.vdf</c>, when <c>SAVELOCKER_SRM_CAPTURE</c> names a copy (it lists the maintainer's library,
    /// so it is not in the repo): every ROM shortcut is an emulator's, nothing else is.
    /// </summary>
    [Fact]
    public void The_captured_deck_shortcuts_split_into_emulator_games_and_the_rest()
    {
        if (Environment.GetEnvironmentVariable("SAVELOCKER_SRM_CAPTURE") is not { Length: > 0 } path || !File.Exists(path)) return;
        var all = SteamShortcuts.Parse(File.ReadAllBytes(path));
        var emulator = all.Select(s => SrmShortcuts.Recognize(s)).OfType<EmulatorShortcut>().ToList();
        foreach (var e in emulator) Console.WriteLine($"{e.Shortcut.AppName} -> {e.Emulator}: {e.Rom}");
        Assert.Contains(emulator, e => e.Shortcut.AppName == "Forza Horizon" && e.Emulator == "Xenia");
        Assert.Contains(emulator, e => e.Shortcut.AppName == "Metroid: Other M" && e.Emulator == "Dolphin");
        Assert.Contains(emulator, e => e.Shortcut.AppName == "Pokemon Legends: Arceus" && e.Emulator == "Yuzu");
        Assert.All(emulator, e => Assert.Contains("/Emulation/roms/", e.Rom));
        Assert.Equal(all.Count(s => (s.RawExe + " " + s.LaunchOptions).Contains("/Emulation/roms/")), emulator.Count);
    }

    /// <summary>A binary <c>shortcuts.vdf</c> as Steam writes it: a map per shortcut keyed by its index.</summary>
    private static byte[] Vdf(params (string Name, uint AppId, string Exe, string? Options, string[] Tags)[] entries)
    {
        using var ms = new MemoryStream();
        void Str(string s) { var b = Encoding.UTF8.GetBytes(s); ms.Write(b); ms.WriteByte(0); }
        void Key(byte type, string key) { ms.WriteByte(type); Str(key); }
        Key(0, "shortcuts");
        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            Key(0, i.ToString());
            Key(2, "appid"); ms.Write(BitConverter.GetBytes(unchecked((int)e.AppId)));
            Key(1, "AppName"); Str(e.Name);
            Key(1, "Exe"); Str(e.Exe);
            Key(1, "StartDir"); Str("");
            Key(1, "LaunchOptions"); Str(e.Options ?? "");
            Key(0, "tags");
            for (var t = 0; t < e.Tags.Length; t++) { Key(1, t.ToString()); Str(e.Tags[t]); }
            ms.WriteByte(8);
            ms.WriteByte(8);
        }
        ms.WriteByte(8);
        ms.WriteByte(8);
        return ms.ToArray();
    }
}
