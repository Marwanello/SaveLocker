using SaveLocker.Shared;
using Xunit;
using F = SaveLocker.Agent.Tests.ConsoleSaveFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Group D: <c>PARAM.SFO</c>, the folder-per-game-ID shape (D4 shape 2) and the Sony emulators
/// that read it — PPSSPP, RPCS3, Vita3K and shadPS4. A game owns exactly the folders named from its ID; the folders of
/// every other game in the same <c>SAVEDATA</c> are never in its scope.
/// </summary>
public sealed class SonySavesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-sony-" + Guid.NewGuid().ToString("N"));

    public SonySavesTests() => Directory.CreateDirectory(_root);

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

    private void Sfo(string rel, params (string, object)[] entries) => F.Write(P(rel), F.ParamSfo(entries));

    // ---- PARAM.SFO ----

    [Fact]
    public void A_param_sfo_reads_its_text_and_number_entries()
    {
        var sfo = ParamSfo.Parse(F.ParamSfo(("CATEGORY", "SD"), ("PARENTAL_LEVEL", 3), ("TITLE", "Heavenly\nSword™"), ("SUB_TITLE", "Autosave")));

        Assert.NotNull(sfo);
        Assert.Equal("SD", sfo!["CATEGORY"]);
        Assert.Equal(3, sfo["PARENTAL_LEVEL"]);
        // A PS3 title often breaks its line; the name never does.
        Assert.Equal("Heavenly Sword", ParamSfo.Title(sfo));
    }

    [Fact]
    public void A_ps4_save_is_named_by_its_maintitle_and_no_param_sfo_names_nothing()
    {
        Assert.Equal("Bloodborne", ParamSfo.Title(ParamSfo.Parse(F.ParamSfo(("MAINTITLE", "Bloodborne"), ("SUBTITLE", "Hunter")))!));
        Assert.Null(ParamSfo.Parse("not a param.sfo at all"u8));
        // Truncated inside its own index: refused, not read past the end.
        Assert.Null(ParamSfo.Parse(F.ParamSfo(("TITLE", "Cut short")).AsSpan(0, 30)));
        Assert.Null(ParamSfo.Title(P("missing/PARAM.SFO")));
    }

    [Theory]
    [InlineData("psp", "ULUS10041", "USA")]
    [InlineData("psp", "ULES00151", "Europe")]
    [InlineData("psp", "NPJH50001", "Japan")]
    [InlineData("ps3", "NPUB30010", "USA")]
    [InlineData("ps3", "BLKS20001", "Korea")]
    [InlineData("psvita", "PCSE00120", "USA")]
    [InlineData("psvita", "PCSB00245", "Europe")]
    [InlineData("psvita", "PCSG00001", "Japan")]
    [InlineData("ps4", "CUSA00552", null)]
    [InlineData("psvita", "ABCD12345", null)]
    public void A_sony_id_prefix_names_its_region(string system, string id, string? region) =>
        Assert.Equal(region, ConsoleTitles.PrefixRegion(system, id));

    // ---- PPSSPP ----

    /// <summary>EmuDeck's PPSSPP folders: a Redump disc with two save folders, a PSN game Redump has no disc of, a
    /// folder no game's ID starts, and an empty save folder; states for the disc and for another game.</summary>
    private void PpssppEmuDeck(string root)
    {
        Sfo($"{root}/saves/ppsspp/saves/ULUS10041DATA00/PARAM.SFO", ("TITLE", "GTA LCS"), ("SAVEDATA_TITLE", "Slot 1"));
        Text($"{root}/saves/ppsspp/saves/ULUS10041DATA00/DATA.BIN", "lcs-1");
        Sfo($"{root}/saves/ppsspp/saves/ULUS10041SYSTEM/PARAM.SFO", ("TITLE", "GTA LCS"));
        Text($"{root}/saves/ppsspp/saves/ULUS10041SYSTEM/SYSTEM.BIN", "lcs-system");
        Sfo($"{root}/saves/ppsspp/saves/NPUH99999SAVE/PARAM.SFO", ("TITLE", "Homebrew Hero"));
        Text($"{root}/saves/ppsspp/saves/NPUH99999SAVE/SAVE.DAT", "hb");
        Text($"{root}/saves/ppsspp/saves/PPSSPP_MISC/notes.txt", "not a game");
        Directory.CreateDirectory(P($"{root}/saves/ppsspp/saves/ULES00151DATA"));
        Text($"{root}/saves/ppsspp/states/ULUS10041_1.00_0.ppst", "state");
        Text($"{root}/saves/ppsspp/states/ULUS10041_1.00_0.jpg", "thumb");
        Text($"{root}/saves/ppsspp/states/ULUS10042_1.00_0.ppst", "ssx-state");
    }

    [Fact]
    public void Ppsspp_saves_are_one_game_per_id_named_by_redump_then_param_sfo()
    {
        PpssppEmuDeck("Emulation");

        var found = PpssppSaves.Scan(PpssppSaves.Folders([P("Emulation")], []));

        Assert.Equal(new[] { "Grand Theft Auto - Liberty City Stories", "Homebrew Hero" }, found.Select(c => c.Name));
        var lcs = found[0];
        Assert.Equal(("PPSSPP", "psp", "ULUS10041", true), (lcs.EmulatorName, lcs.EmulatorSystem, lcs.EmulatorRom, lcs.ViaEmuDeck));
        Assert.Equal(("PlayStation Portable", "USA", "Redump"), (lcs.Release!.Console, lcs.Release.Region, lcs.Release.NamedBy));
        Assert.Equal(new[] { "ULUS10041*/**" }, lcs.IncludeGlobs);
        Assert.Equal(new[] { "ULUS10041DATA00/DATA.BIN", "ULUS10041DATA00/PARAM.SFO", "ULUS10041SYSTEM/PARAM.SFO", "ULUS10041SYSTEM/SYSTEM.BIN" },
            SaveArchive.ListFiles(lcs.SuggestedSaveDir!, null, lcs.IncludeGlobs).Order(StringComparer.Ordinal));

        var states = Assert.Single(lcs.ExtraSaveDirs!);
        Assert.Equal(RomSaves.StatesKey, states.Key);
        Assert.Equal(new[] { "ULUS10041_1.00_0.jpg", "ULUS10041_1.00_0.ppst" },
            SaveArchive.ListFiles(states.Dir, null, states.IncludeGlobs).Order(StringComparer.Ordinal));

        // Redump has no disc of a PSN game: the TITLE inside its save, the region from its ID.
        var psn = found[1];
        Assert.Equal(("USA", null), (psn.Release!.Region, psn.Release.NamedBy));
        Assert.Equal(new[] { "NPUH99999SAVE/PARAM.SFO", "NPUH99999SAVE/SAVE.DAT" },
            SaveArchive.ListFiles(psn.SuggestedSaveDir!, null, psn.IncludeGlobs).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_standalone_ppsspp_memory_stick_is_read_and_the_newest_copy_of_a_game_wins()
    {
        Sfo("old/PSP/SAVEDATA/ULUS10041DATA00/PARAM.SFO", ("TITLE", "GTA LCS"));
        Text("old/PSP/SAVEDATA/ULUS10041DATA00/DATA.BIN", "old");
        Sfo("new/PSP/SAVEDATA/ULUS10041DATA00/PARAM.SFO", ("TITLE", "GTA LCS"));
        Text("new/PSP/SAVEDATA/ULUS10041DATA00/DATA.BIN", "new");
        File.SetLastWriteTimeUtc(P("old/PSP/SAVEDATA/ULUS10041DATA00/DATA.BIN"), DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(P("old/PSP/SAVEDATA/ULUS10041DATA00/PARAM.SFO"), DateTime.UtcNow.AddDays(-3));

        var folders = PpssppSaves.Folders([], [P("old/PSP"), P("new/PSP"), P("missing/PSP")]);
        Assert.Equal(2, folders.Count);
        // A states folder PPSSPP has not made yet is still declared.
        Assert.Equal(P("new/PSP/PPSSPP_STATE"), folders[1].States);

        var game = Assert.Single(PpssppSaves.Scan(folders));
        Assert.Equal(P("new/PSP/SAVEDATA"), game.SuggestedSaveDir);
        Assert.False(game.ViaEmuDeck);
    }

    // ---- RPCS3 ----

    [Fact]
    public void Rpcs3_saves_are_named_by_redump_then_param_sfo_and_keep_the_trophies_out()
    {
        // The Deck's save (2026-10-11): Redump knows the disc, so the name is not the save's "Heavenly Sword™".
        Sfo("Emulation/saves/rpcs3/saves/BCUS98132-GAMEAUTOSAVE/PARAM.SFO", ("TITLE", "Heavenly Sword™"), ("SUB_TITLE", "Save Game"));
        Text("Emulation/saves/rpcs3/saves/BCUS98132-GAMEAUTOSAVE/USERDATA.DAT", "hs");
        Text("Emulation/saves/rpcs3/saves/BCUS98132-GAMEAUTOSAVE/ICON0.PNG", "icon");
        Sfo("Emulation/saves/rpcs3/saves/NPUB99999SLOT1/PARAM.SFO", ("TITLE", "A PSN\nGame"));
        Text("Emulation/saves/rpcs3/saves/NPUB99999SLOT1/SAVE.DAT", "psn");
        Text("Emulation/saves/rpcs3/trophy/NPWR00001_00/TROPCONF.SFM", "trophy");

        var found = Rpcs3Saves.Scan(Rpcs3Saves.Folders([P("Emulation")], []));

        Assert.Equal(new[] { "A PSN Game", "Heavenly Sword" }, found.Select(c => c.Name));
        var hs = found[1];
        Assert.Equal(("RPCS3", "ps3", "BCUS98132", true), (hs.EmulatorName, hs.EmulatorSystem, hs.EmulatorRom, hs.ViaEmuDeck));
        Assert.Equal(("PlayStation 3", "USA", "Redump"), (hs.Release!.Console, hs.Release.Region, hs.Release.NamedBy));
        Assert.Equal(new[] { "BCUS98132-GAMEAUTOSAVE/ICON0.PNG", "BCUS98132-GAMEAUTOSAVE/PARAM.SFO", "BCUS98132-GAMEAUTOSAVE/USERDATA.DAT" },
            SaveArchive.ListFiles(hs.SuggestedSaveDir!, null, hs.IncludeGlobs).Order(StringComparer.Ordinal));
        Assert.Null(hs.ExtraSaveDirs);
        Assert.Equal(("USA", null), (found[0].Release!.Region, found[0].Release!.NamedBy));
    }

    [Fact]
    public void Rpcs3_vfs_yml_says_where_dev_hdd0_is_on_the_deck_and_in_emudeck_for_windows()
    {
        // The Deck's ~/.config/rpcs3/vfs.yml, as read 2026-10-11: dev_hdd0 moved to the SD card.
        Text("deck/.config/rpcs3/vfs.yml", $"$(EmulatorDir): \"\"\n/dev_hdd0/: {P("sd/storage/rpcs3/dev_hdd0").Replace('\\', '/')}/\n/dev_hdd1/: $(EmulatorDir)dev_hdd1/\n/dev_bdvd/: \"\"\n");
        Sfo("sd/storage/rpcs3/dev_hdd0/home/00000001/savedata/BLUS30443-AUTO/PARAM.SFO", ("TITLE", "Demon's Souls"));
        Sfo("sd/storage/rpcs3/dev_hdd0/home/00000002/savedata/BLES00932-AUTO/PARAM.SFO", ("TITLE", "Demon's Souls"));
        // EmuDeck for Windows' RPCS3: config\vfs.yml beside rpcs3.exe, dev_hdd0 left at its default.
        Text("win/RPCS3/config/vfs.yml", "$(EmulatorDir): \"\"\n/dev_hdd1/: $(EmulatorDir)dev_hdd1/\n");
        Sfo("win/RPCS3/dev_hdd0/home/00000001/savedata/NPUB30010-SAVE/PARAM.SFO", ("TITLE", "Flower"));

        Assert.Equal(Path.GetFullPath(P("sd/storage/rpcs3/dev_hdd0")).TrimEnd(Path.DirectorySeparatorChar),
            Rpcs3Saves.DevHdd0(P("deck/.config/rpcs3"))!.TrimEnd(Path.DirectorySeparatorChar));
        Assert.Null(Rpcs3Saves.DevHdd0(P("never-ran")));

        var folders = Rpcs3Saves.Folders([], [P("deck/.config/rpcs3"), P("win/RPCS3")]);
        Assert.Equal(3, folders.Count);
        Assert.Equal(new[] { "BLES00932", "BLUS30443", "NPUB30010" }, Rpcs3Saves.Scan(folders).Select(c => c.EmulatorRom).Order(StringComparer.Ordinal));
    }
}
