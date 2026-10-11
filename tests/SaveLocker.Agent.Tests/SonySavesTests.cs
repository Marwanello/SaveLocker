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
}
