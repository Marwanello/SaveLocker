using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-pull-safety Phase 8: another revision of a ROM joins the game of the same emulator by itself (B), and
/// a dump kept apart is named by its edition, not by its whole file name (A).
/// </summary>
public sealed class RevisionLinkTests
{
    private const string Rev1 = "Metroid Prime - Hunters (USA) (Rev 1)";
    private const string Usa = "Metroid Prime - Hunters (USA)";

    private static GameDto Server(string rom, string emulator = "RetroArch", string? region = null) =>
        new(Guid.NewGuid(), "Metroid Prime - Hunters (RetroArch)", null, null, true,
            IncludeGlobs: [$"{rom}.srm", $"{rom}.rtc"],
            ExtraPaths: [new SavePathDto(RomSaves.StatesKey, null, null, [$"{rom}.state*"])],
            Emulators: [emulator],
            Release: region is null ? null : new GameReleaseDto("Nintendo DS", region, [], []));

    private static ScanCandidate Save(string rom, string emulator = "RetroArch", bool states = true) =>
        new("Metroid Prime - Hunters", "/saves", ScanSource.Emulator, false,
            EmulatorName: emulator, EmulatorSystem: "nds", EmulatorRom: rom,
            IncludeGlobs: [$"{rom}.srm", $"{rom}.rtc"],
            ExtraSaveDirs: states ? [new DeclaredSavePath(RomSaves.StatesKey, "/states", [$"{rom}.state*"])] : null);

    [Theory]
    [InlineData(Usa, Rev1, true)]
    [InlineData("Chrono Trigger (USA) [!]", "Chrono Trigger (USA) (v1.1)", true)]
    [InlineData(Usa, "Metroid Prime - Hunters (Europe)", false)]
    [InlineData(Usa, "Metroid Prime - Hunters (USA) (Beta)", false)]
    [InlineData(Usa, "Metroid Prime - Hunters - First Hunt (USA)", false)]
    [InlineData(Usa, "Metroid Prime - Hunters (USA) (Revenge)", false)]
    public void A_revision_is_the_same_release_and_a_region_or_beta_is_not(string a, string b, bool same) =>
        Assert.Equal(same, RomNames.SameRelease(a, b));

    [Fact]
    public void Edition_tags_are_the_region_revision_and_pre_release_only() =>
        Assert.Equal(["Europe, Australia", "Rev 2", "Proto 1"],
            RomNames.EditionTags("Game (Europe, Australia) (En,Fr,De) (Rev 2) (Proto 1) [b]"));

    [Fact]
    public void Another_revision_joins_the_game_of_the_same_emulator_by_itself()
    {
        var server = Server(Rev1);
        Assert.Same(server, EnrollLinks.RevisionOf([server], Save(Usa)));
        var auto = EnrollLinks.For([server], Save(Usa))[0];
        Assert.Equal((EnrollLinks.Auto, LinkKind.Join, "Another revision"), (auto.Choice, auto.Kind, auto.Badge));
        // It can still be kept as a game of its own.
        Assert.Contains(EnrollLinks.For([server], Save(Usa)), o => o.Choice == EnrollLinks.Separate);
    }

    [Fact]
    public void Another_region_emulator_or_unpairable_save_stays_a_game_of_its_own()
    {
        Assert.Null(EnrollLinks.RevisionOf([Server("Metroid Prime - Hunters (Europe)")], Save(Usa)));
        Assert.Null(EnrollLinks.RevisionOf([Server(Rev1, emulator: "melonDS")], Save(Usa)));
        Assert.Null(EnrollLinks.RevisionOf([Server(Rev1) with { ExtraPaths = null }], Save(Usa)));
        Assert.Equal(EnrollLinks.Auto, EnrollLinks.For([Server("Metroid Prime - Hunters (Europe)")], Save(Usa))[0].Choice);
        Assert.Equal(LinkKind.New, EnrollLinks.For([Server("Metroid Prime - Hunters (Europe)")], Save(Usa))[0].Kind);
    }

    [Fact]
    public void The_same_files_win_over_a_revision()
    {
        var same = Server(Usa);
        var revision = Server(Rev1) with { Name = "Metroid Prime - Hunters (RetroArch, Rev 1)" };
        Assert.Null(EnrollLinks.RevisionOf([revision, same], Save(Usa)));
        Assert.Equal(same.Id, EnrollLinks.For([revision, same], Save(Usa))[0].GameId);
    }
}
