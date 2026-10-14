using SaveLocker.Shared;
using Xunit;
using S = SaveLocker.Agent.Tests.SwitchFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// A Switch game's name (Ryujinx's record, then the ROM's, then the bundled eShop table, then <c>Switch &lt;title ID&gt;</c>)
/// and its Change options on Add games: the game keyed to its title, or a new one, or by hand a same-titled game no title
/// was recorded for.
/// </summary>
public sealed class SwitchNamesAndLinksTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-switchnames-" + Guid.NewGuid().ToString("N"));
    private const string User = "C4D7AB8B14D1F6F277CD40D6A0F00ECA";
    private const string Totk = "0100F2C0115B6000";

    public SwitchNamesAndLinksTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string rel) => Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>A title ID the bundled table has no game of.</summary>
    internal static string UnknownTitle()
    {
        while (true)
        {
            var id = $"0100{Random.Shared.Next(0x100000, 0xFFFFFF):X6}000000";
            if (SwitchTitles.For(id) is null) return id;
        }
    }

    private ScanCandidate Eden(string titleId, string? rom = null)
    {
        var eden = S.Eden(P("Emulation"), User);
        S.Write(Path.Combine(S.EdenSave(P("Emulation"), User, titleId), "save.bin"), "save");
        if (rom is not null) S.Write(P($"Emulation/roms/switch/{rom} [{titleId}][v0].nsp"), "rom");
        return SwitchEmulators.Scan([eden], [P("Emulation")]).Single(c => c.EmulatorRom == titleId);
    }

    private static GameDto Game(string name, string? key = null, string[]? include = null) =>
        new(Guid.NewGuid(), name, null, null, true, IncludeGlobs: include, EmulatorKey: key);

    [Fact]
    public void The_bundled_table_names_a_title_and_carries_its_eshop_icon()
    {
        var totk = SwitchTitles.For(Totk.ToLowerInvariant());
        Assert.NotNull(totk);
        Assert.Contains("Tears of the Kingdom", totk.Name);
        Assert.Matches("^[0-9a-f]{64}$", totk.IconHash!);
        Assert.Null(SwitchTitles.For(UnknownTitle()));
        Assert.Equal(Totk, SwitchTitles.TitleIdOf($"switch:{Totk.ToLowerInvariant()}"));
        Assert.Null(SwitchTitles.TitleIdOf("xenia:E0000123"));
    }

    [Fact]
    public void A_name_comes_from_ryujinx_then_the_rom_then_the_eshop_then_the_title_id()
    {
        Assert.Equal("Ryujinx's own", SwitchEmulators.NameFor(Totk, new Dictionary<string, string> { [Totk] = "Ryujinx's own" }));
        // The eShop's, cleaned of its trademark signs, when the machine has none.
        Assert.Equal("The Legend of Zelda: Tears of the Kingdom", SwitchEmulators.NameFor(Totk, new Dictionary<string, string>()));
        var unknown = UnknownTitle();
        Assert.Equal($"Switch {unknown}", SwitchEmulators.NameFor(unknown, new Dictionary<string, string>()));

        // Through a scan: a ROM's name beats the table's, and a title with neither takes the table's.
        Assert.Equal("My Own Dump Name", Eden(Totk, rom: "My Own Dump Name").Name);
        Assert.Equal("Metroid Dread", Eden("010093801237C000").Name);
    }

    [Fact]
    public void A_title_the_server_has_a_game_for_always_joins_it_and_nothing_else_is_offered()
    {
        var save = Eden(Totk);
        var keyed = Game("Switch " + Totk, $"switch:{Totk}");

        var option = Assert.Single(EnrollLinks.For([keyed, Game(save.Name)], save));

        Assert.Equal((EnrollLinks.Auto, LinkKind.Join, keyed.Id), (option.Choice, option.Kind, option.GameId));
    }

    [Fact]
    public void A_new_title_is_its_own_game_and_may_join_a_same_named_game_with_no_title_by_hand()
    {
        var save = Eden(Totk);
        var byHand = Game(save.Name);                                        // added by hand: a whole folder, no title
        var other = Game(save.Name + " (Japan)", "switch:0100AAAAAAAA0000");   // another title of that name
        var scoped = Game(save.Name + " (Eden)", include: ["*.srm"]);

        var options = EnrollLinks.For([byHand, other, scoped], save);

        // Automatic: a new game, under the first of its names nobody has.
        Assert.Equal((EnrollLinks.Auto, LinkKind.New), (options[0].Choice, options[0].Kind));
        Assert.Equal(save.Name + " (Switch)", options[0].Name);
        Assert.Equal(LinkKind.Join, options.Single(o => o.GameId == byHand.Id).Kind);
        Assert.Equal(LinkKind.Blocked, options.Single(o => o.GameId == other.Id).Kind);
        Assert.Equal(LinkKind.Blocked, options.Single(o => o.GameId == scoped.Id).Kind);

        var extras = save.ExtraSaveDirs ?? [];
        Assert.Equal((byHand.Name, null), EnrollLinks.Resolve([byHand, other], save, extras, new LinkChoice(EnrollLinks.Game, byHand.Id)));
        Assert.NotNull(EnrollLinks.Resolve([byHand, other], save, extras, new LinkChoice(EnrollLinks.Game, other.Id)).Refusal);
        Assert.NotNull(EnrollLinks.Resolve([scoped], save, extras, new LinkChoice(EnrollLinks.Game, scoped.Id)).Refusal);
    }
}
