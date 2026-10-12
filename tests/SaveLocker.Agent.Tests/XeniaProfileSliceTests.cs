using SaveLocker.Agent;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Phase 5c step 5: a Xenia game's own part of its profile — its achievements file and its row of the
/// dashboard, copied out before a push and merged back after a pull, never the dashboard whole.
/// </summary>
public sealed class XeniaProfileSliceTests : IDisposable
{
    private const string Xuid = "E030000033894516";
    private const uint Forza = 0x4D5309C9, Halo = 0x4D5307E6;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-xslice-" + Guid.NewGuid().ToString("N"));
    private readonly string _content, _profile;
    private readonly List<string> _log = [];

    public XeniaProfileSliceTests()
    {
        _content = Path.Combine(_root, "content");
        _profile = XeniaProfile.Folder(_content, Xuid);
        Directory.CreateDirectory(_profile);
        Directory.CreateDirectory(Path.Combine(_content, Xuid, "4D5309C9", "00000001"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private TrackedGame Game() => new()
    {
        GameId = Guid.NewGuid(), Name = "Forza Horizon", SaveDirectory = Path.Combine(_content, Xuid),
        IncludeGlobs = [.. XeniaSaves.ScopeFor("4D5309C9")],
        ExtraPaths = [new TrackedSavePath { Key = XeniaProfileSlice.Key, Directory = Path.Combine(_content, Xuid), IncludeGlobs = [.. XeniaProfileSlice.ScopeFor("4D5309C9")] }],
    };

    private string Dashboard => Path.Combine(_profile, "FFFE07D1.gpd");
    private string RowFile => Path.Combine(_profile, "SaveLocker-4D5309C9.row");

    [Fact]
    public void Export_writes_the_games_row_and_only_when_it_changed()
    {
        var row = XeniaFormatsTests.Row(Forza, 18, 260, "Forza Horizon");
        File.WriteAllBytes(Dashboard, XeniaFormatsTests.Dashboard(row, XeniaFormatsTests.Row(Halo, 11, 190, "Halo 3")));

        XeniaProfileSlice.Export(Game(), _log.Add);
        Assert.Equal(row, File.ReadAllBytes(RowFile));

        var written = File.GetLastWriteTimeUtc(RowFile);
        File.SetLastWriteTimeUtc(RowFile, written.AddHours(-1));
        XeniaProfileSlice.Export(Game(), _log.Add);
        Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(RowFile));
    }

    [Fact]
    public void Merge_puts_the_pulled_row_in_and_keeps_every_other_row()
    {
        var halo = XeniaFormatsTests.Row(Halo, 11, 190, "Halo 3");
        File.WriteAllBytes(Dashboard, XeniaFormatsTests.Dashboard(XeniaFormatsTests.Row(Forza, 12, 180, "Forza Horizon"), halo));
        File.WriteAllBytes(RowFile, XeniaFormatsTests.Row(Forza, 18, 260, "Forza Horizon"));

        Assert.True(XeniaProfileSlice.Merge(Game(), _log.Add, () => false));

        var x = Xdbf.Parse(File.ReadAllBytes(Dashboard));
        Assert.Equal(halo, x.Get(Xdbf.Title, Halo));
        Assert.Equal(XeniaFormatsTests.Row(Forza, 18, 260, "Forza Horizon"), x.Get(Xdbf.Title, Forza));
        Assert.Equal(450, XeniaProfile.GetInt(x, XeniaProfile.GamerscoreSetting));
        Assert.False(XeniaProfileSlice.Merge(Game(), _log.Add, () => false)); // already in: nothing written
    }

    [Fact]
    public void Merge_waits_while_Xenia_runs_and_without_the_profile_here()
    {
        var before = XeniaFormatsTests.Dashboard(XeniaFormatsTests.Row(Forza, 12, 180, "Forza Horizon"));
        File.WriteAllBytes(Dashboard, before);
        File.WriteAllBytes(RowFile, XeniaFormatsTests.Row(Forza, 18, 260, "Forza Horizon"));

        Assert.False(XeniaProfileSlice.Merge(Game(), _log.Add, () => true));
        Assert.Equal(before, File.ReadAllBytes(Dashboard));

        File.Delete(Dashboard);
        Assert.False(XeniaProfileSlice.Merge(Game(), _log.Add, () => false));
        Assert.False(File.Exists(Dashboard));
    }

    [Fact]
    public void A_game_under_a_profile_declares_its_part_of_it_and_older_layouts_do_not()
    {
        Directory.CreateDirectory(Path.Combine(_content, Xuid, "4D5309C9", "00000001", "Profile"));
        File.WriteAllText(Path.Combine(_content, Xuid, "4D5309C9", "00000001", "Profile", "save.dat"), "s");
        Directory.CreateDirectory(Path.Combine(_content, "4D530AA4", "00000001", "Profile"));
        File.WriteAllText(Path.Combine(_content, "4D530AA4", "00000001", "Profile", "save.dat"), "s");

        var found = XeniaSaves.Scan([new XeniaFolders(_content)]);

        var forza = found.Single(c => c.EmulatorRom == "4D5309C9").ExtraSaveDirs!;
        var slice = forza.Single(e => e.Key == XeniaProfileSlice.Key);
        Assert.Equal(Path.Combine(_content, Xuid), slice.Dir); // the main folder, scoped: never one inside the other
        Assert.Equal(["FFFE07D1/00010000/*/4D5309C9.gpd", "FFFE07D1/00010000/*/SaveLocker-4D5309C9.row"], slice.IncludeGlobs);
        Assert.DoesNotContain(found.Single(c => c.EmulatorRom == "4D530AA4").ExtraSaveDirs!, e => e.Key == XeniaProfileSlice.Key);
    }
}
