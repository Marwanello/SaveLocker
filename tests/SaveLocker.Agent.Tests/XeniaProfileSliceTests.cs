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
    public void A_row_pulled_while_Xenia_runs_is_never_replaced_by_the_older_row_here()
    {
        // The pull lands the newer row; Xenia is open, so it waits. The push that follows must not export this machine's
        // older row over it — that push would carry the older row to every machine.
        var state = Path.Combine(_root, "state");
        var game = Game();
        var older = XeniaFormatsTests.Row(Forza, 12, 180, "Forza Horizon");
        var pulled = XeniaFormatsTests.Row(Forza, 18, 260, "Forza Horizon");
        File.WriteAllBytes(Dashboard, XeniaFormatsTests.Dashboard(older));
        File.WriteAllBytes(RowFile, pulled);

        XeniaProfileSlice.AfterPull(game, _log.Add, () => true, state);
        XeniaProfileSlice.Export(game, _log.Add, () => true, state);
        Assert.Equal(pulled, File.ReadAllBytes(RowFile));

        // Xenia closed: the next push merges first, then exports the merged row.
        XeniaProfileSlice.Export(game, _log.Add, () => false, state);
        Assert.Equal(pulled, File.ReadAllBytes(RowFile));
        Assert.Equal(pulled, XeniaProfile.TitleRow(File.ReadAllBytes(Dashboard), Forza));

        // Nothing waits any more: progress made here is exported as usual.
        var played = XeniaFormatsTests.Row(Forza, 20, 300, "Forza Horizon");
        File.WriteAllBytes(Dashboard, XeniaFormatsTests.Dashboard(played));
        XeniaProfileSlice.Export(game, _log.Add, () => false, state);
        Assert.Equal(played, File.ReadAllBytes(RowFile));
    }

    [Theory]
    [InlineData("xenia_canary", true)]
    [InlineData("xenia_canary.exe", true)]
    [InlineData("xenia_canary.ex", true)] // Proton on Linux: 15 characters of xenia_canary.exe
    [InlineData("Xenia_Edge.exe", true)]
    [InlineData("xenia", true)]
    [InlineData("xenia_manager", false)]
    [InlineData("xenia_canary_x", false)]
    [InlineData("steam", false)]
    public void Xenia_is_recognised_by_its_process_name_even_cut_short(string name, bool xenia) =>
        Assert.Equal(xenia, XeniaLaunchProfile.IsXeniaProcessName(name));

    private string CoreFile => Path.Combine(_profile, "SaveLocker-4D5309C9.core");
    private string Account => Path.Combine(_profile, "Account");

    private static byte[] WithSetting(byte[] dashboard, ulong id, int value)
    {
        var x = Xdbf.Parse(dashboard);
        var d = new byte[24];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(d, (uint)id);
        d[8] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(d.AsSpan(16), value);
        x.Set(Xdbf.Setting, id, d);
        return x.Serialize();
    }

    [Fact]
    public void The_core_holds_Account_and_the_profiles_settings_but_no_game_row_or_total()
    {
        const ulong motto = 0x402C0011; // a profile setting that is not a total
        File.WriteAllBytes(Dashboard, WithSetting(XeniaFormatsTests.Dashboard(XeniaFormatsTests.Row(Forza, 18, 260, "Forza Horizon")), motto, 7));
        File.WriteAllBytes(Account, XeniaFormatsTests.AccountFor("Driver"));

        XeniaProfileSlice.Export(Game(), _log.Add);

        var core = Xdbf.Parse(File.ReadAllBytes(CoreFile));
        Assert.DoesNotContain(core.Entries, e => e.Namespace == Xdbf.Title);
        Assert.Null(XeniaProfile.GetInt(core, XeniaProfile.GamerscoreSetting));
        Assert.Equal(7, XeniaProfile.GetInt(core, motto));
        Assert.Equal(File.ReadAllBytes(Account), core.Get(0xFFFF, 0));
    }

    [Fact]
    public void A_machine_without_the_profile_gets_it_unpacked_with_this_games_row()
    {
        const ulong motto = 0x402C0011;
        var source = WithSetting(XeniaFormatsTests.Dashboard(XeniaFormatsTests.Row(Forza, 18, 260, "Forza Horizon"),
            XeniaFormatsTests.Row(Halo, 11, 190, "Halo 3")), motto, 7);
        var account = XeniaFormatsTests.AccountFor("Driver");
        File.WriteAllBytes(CoreFile, XeniaProfileSlice.Core(source, account));
        File.WriteAllBytes(RowFile, XeniaFormatsTests.Row(Forza, 18, 260, "Forza Horizon"));

        XeniaProfileSlice.AfterPull(Game(), _log.Add, () => false);

        Assert.Equal(account, File.ReadAllBytes(Account));
        Assert.Equal("Driver", XeniaProfile.Gamertag(_content, Xuid));
        var x = Xdbf.Parse(File.ReadAllBytes(Dashboard));
        Assert.Equal([Forza], x.Entries.Where(e => e.Namespace == Xdbf.Title).Select(e => (uint)e.Id)); // never Halo's row
        Assert.Equal(260, XeniaProfile.GetInt(x, XeniaProfile.GamerscoreSetting));
        Assert.Equal(7, XeniaProfile.GetInt(x, motto));
    }

    [Fact]
    public void A_profile_already_here_is_never_replaced_by_the_core()
    {
        var mine = XeniaFormatsTests.Dashboard(XeniaFormatsTests.Row(Halo, 11, 190, "Halo 3"));
        var myAccount = XeniaFormatsTests.AccountFor("Driver");
        File.WriteAllBytes(Dashboard, mine);
        File.WriteAllBytes(Account, myAccount);
        File.WriteAllBytes(CoreFile, XeniaProfileSlice.Core(XeniaFormatsTests.Dashboard(), XeniaFormatsTests.AccountFor("Someone Else")));

        Assert.False(XeniaProfileSlice.Seed(Game(), _log.Add, () => false));

        Assert.Equal(myAccount, File.ReadAllBytes(Account));
        Assert.Equal(mine, File.ReadAllBytes(Dashboard));
    }

    [Fact]
    public void A_game_found_under_another_profile_is_put_under_the_games_and_says_so()
    {
        const string player1 = "E03000004A1C77B2";
        var mine = Path.Combine(_content, player1);
        Directory.CreateDirectory(XeniaProfile.Folder(_content, player1));
        File.WriteAllBytes(Path.Combine(XeniaProfile.Folder(_content, player1), "Account"), XeniaFormatsTests.AccountFor("Player1"));
        File.WriteAllBytes(Account, XeniaFormatsTests.AccountFor("Driver"));
        var c = new ScanCandidate("Forza Horizon", mine, ScanSource.Emulator, false, EmulatorProfile: "xenia:" + player1);
        IReadOnlyList<DeclaredSavePath> extras =
        [
            new(XeniaSaves.DeviceKey, Path.Combine(_content, XeniaSaves.NoProfile), XeniaSaves.ScopeFor("4D5309C9")),
            new(XeniaProfileSlice.Key, mine, XeniaProfileSlice.ScopeFor("4D5309C9")),
        ];

        var aligned = XeniaSaves.AlignToGameProfile(c, "xenia:" + Xuid, mine, extras)!.Value;

        Assert.Equal(Path.Combine(_content, Xuid), aligned.Dir);
        Assert.Equal(Path.Combine(_content, Xuid), aligned.Extras.Single(e => e.Key == XeniaProfileSlice.Key).Dir);
        Assert.Equal(Path.Combine(_content, XeniaSaves.NoProfile), aligned.Extras.Single(e => e.Key == XeniaSaves.DeviceKey).Dir);
        Assert.Contains("added under Driver", aligned.Note);
        Assert.Contains("Player1's own save here was left as it is", aligned.Note);

        Assert.Null(XeniaSaves.AlignToGameProfile(c, "xenia:" + player1, mine, extras)); // already the game's
        Assert.Null(XeniaSaves.AlignToGameProfile(c, null, mine, extras)); // the game has none yet
        var legacy = c with { EmulatorProfile = null }; // content/<title ID>: an older Xenia reads it where it is
        Assert.Null(XeniaSaves.AlignToGameProfile(legacy, "xenia:" + Xuid, _content, extras));
    }

    [Fact]
    public void Only_a_xenia_profile_value_is_kept()
    {
        Assert.Equal("xenia:E030000033894516", SaveLocker.Shared.EmulatorProfiles.Clean(" xenia:e030000033894516 "));
        Assert.Null(SaveLocker.Shared.EmulatorProfiles.Clean("xenia:0000000000000000"));
        Assert.Null(SaveLocker.Shared.EmulatorProfiles.Clean("xenia:E03"));
        Assert.Null(SaveLocker.Shared.EmulatorProfiles.Clean("cemu:80000001"));
        Assert.Equal("E030000033894516", SaveLocker.Shared.EmulatorProfiles.XeniaXuid("xenia:E030000033894516"));
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
        Assert.Equal(["FFFE07D1/00010000/*/4D5309C9.gpd", "FFFE07D1/00010000/*/SaveLocker-4D5309C9.row",
            "FFFE07D1/00010000/*/SaveLocker-4D5309C9.core"], slice.IncludeGlobs);
        Assert.DoesNotContain(found.Single(c => c.EmulatorRom == "4D530AA4").ExtraSaveDirs!, e => e.Key == XeniaProfileSlice.Key);
    }
}
