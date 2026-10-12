using System.Buffers.Binary;
using System.Text;
using SaveLocker.Agent;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Phase 5c, step 1: the Xenia files the profile-at-launch and the DLC check read and write — a
/// profile's XDBF GPDs and its encrypted Account, DLC and title-update headers, and the config line that picks the
/// profile at boot. Layouts were taken from files Xenia Canary wrote on a PC and a Deck; set
/// <c>SAVELOCKER_XENIA_CAPTURE</c> to a captured profile folder to run the real-file check as well.
/// </summary>
public sealed class XeniaFormatsTests : IDisposable
{
    private const string Xuid = "E030000033894516";
    private const uint Forza = 0x4D5309C9, Halo = 0x4D5307E6;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-xenia-" + Guid.NewGuid().ToString("N"));

    public XeniaFormatsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A title row as Xenia writes it: the five counters, 12 bytes of flags, last played, the name.</summary>
    internal static byte[] Row(uint titleId, int unlocked, int gamerscore, string name, int total = 50)
    {
        var n = Encoding.BigEndianUnicode.GetBytes(name + "\0");
        var b = new byte[40 + n.Length];
        BinaryPrimitives.WriteUInt32BigEndian(b, titleId);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), total);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), unlocked);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(12), 1000);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), gamerscore);
        BinaryPrimitives.WriteInt64BigEndian(b.AsSpan(32), 0x01DD2973422F9DF1);
        n.CopyTo(b, 40);
        return b;
    }

    internal static byte[] Dashboard(params byte[][] rows)
    {
        var x = new Xdbf();
        foreach (var r in rows) x.Set(Xdbf.Title, BinaryPrimitives.ReadUInt32BigEndian(r), r);
        XeniaProfile.Recount(x);
        return x.Serialize();
    }

    internal static byte[] AccountFor(string gamertag)
    {
        var data = new byte[388];
        new Random(7).NextBytes(data.AsSpan(0, 8)); // the confounder
        Encoding.BigEndianUnicode.GetBytes(gamertag).CopyTo(data, 16);
        return XeniaProfile.EncryptAccount(data);
    }

    // ---- XDBF ----

    [Fact]
    public void Xdbf_is_written_the_way_Xenia_lays_it_out_and_reads_back_the_same()
    {
        var bytes = Dashboard(Row(Forza, 1, 10, "Forza Horizon"), Row(0x4D530AA4, 0, 0, "Forza Horizon 2"));

        Assert.Equal("XDBF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(512u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)));
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12))); // 3 settings + 2 rows, as on the PC
        var dataStart = 24 + 512 * 18 + 512 * 8;
        var end = (uint)(bytes.Length - dataStart);
        Assert.Equal(end, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24 + 512 * 18)));
        Assert.Equal(~end, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(24 + 512 * 18 + 4)));

        var again = Xdbf.Parse(bytes);
        Assert.Equal(10, XeniaProfile.GetInt(again, XeniaProfile.GamerscoreSetting));
        Assert.Equal(2, XeniaProfile.GetInt(again, XeniaProfile.TitlesPlayedSetting));
        Assert.Equal(1, XeniaProfile.GetInt(again, XeniaProfile.AchievementsSetting));
        Assert.Equal(bytes, again.Serialize());
    }

    [Fact]
    public void Merging_one_games_row_leaves_every_other_row_alone_and_recounts_the_totals()
    {
        var halo = Row(Halo, 11, 190, "Halo 3");
        var deck = Dashboard(Row(Forza, 20, 295, "Forza Horizon"), halo);

        var merged = XeniaProfile.MergeTitleRow(deck, Forza, Row(Forza, 23, 340, "Forza Horizon"));

        var x = Xdbf.Parse(merged);
        Assert.Equal(halo, x.Get(Xdbf.Title, Halo));
        Assert.Equal(23, BinaryPrimitives.ReadInt32BigEndian(x.Get(Xdbf.Title, Forza).AsSpan(8)));
        Assert.Equal(530, XeniaProfile.GetInt(x, XeniaProfile.GamerscoreSetting));
        Assert.Equal(34, XeniaProfile.GetInt(x, XeniaProfile.AchievementsSetting));
        Assert.Equal(2, XeniaProfile.GetInt(x, XeniaProfile.TitlesPlayedSetting));
    }

    [Fact]
    public void A_row_for_a_game_the_dashboard_lacks_is_added_so_Xenia_loads_its_gpd()
    {
        var deck = Dashboard(Row(Halo, 11, 190, "Halo 3"));

        var merged = XeniaProfile.MergeTitleRow(deck, Forza, Row(Forza, 18, 260, "Forza Horizon"));

        Assert.NotNull(XeniaProfile.TitleRow(merged, Forza));
        Assert.Equal(2, XeniaProfile.GetInt(Xdbf.Parse(merged), XeniaProfile.TitlesPlayedSetting));
    }

    [Fact]
    public void A_row_for_another_title_is_refused()
    {
        var deck = Dashboard(Row(Halo, 11, 190, "Halo 3"));
        Assert.Throws<InvalidDataException>(() => XeniaProfile.MergeTitleRow(deck, Forza, Row(Halo, 1, 1, "Halo 3")));
    }

    [Fact]
    public void Not_an_xdbf_file_is_refused() =>
        Assert.Throws<InvalidDataException>(() => Xdbf.Parse(Encoding.ASCII.GetBytes("not a gpd at all, really")));

    // ---- Account ----

    [Fact]
    public void The_gamertag_decrypts_from_Account_with_the_retail_key()
    {
        Assert.Equal("Player One", XeniaProfile.Gamertag(AccountFor("Player One")));

        var tampered = AccountFor("Player One");
        tampered[^1] ^= 0xFF;
        Assert.Null(XeniaProfile.Gamertag(tampered));
    }

    [Fact]
    public void A_devkit_Account_decrypts_too()
    {
        var data = new byte[388];
        Encoding.BigEndianUnicode.GetBytes("Dev").CopyTo(data, 16);
        Assert.Equal("Dev", XeniaProfile.Gamertag(XeniaProfile.EncryptAccount(data, devkit: true)));
    }

    // ---- DLC and title-update headers ----

    [Fact]
    public void Headers_of_both_sizes_give_the_name_type_and_title()
    {
        var header = XeniaContent.BuildHeader(2, "Pre-Order Car Pack", "13E2064584A4BA7759BA105051C2199ABC5FA5554D", Forza);

        var current = XeniaContent.ParseHeader(header)!.Value;
        var older = XeniaContent.ParseHeader(header.AsSpan(0, 328))!.Value; // a Deck's older Xenia wrote 328 bytes

        foreach (var h in new[] { current, older })
        {
            Assert.Equal(2u, h.ContentType);
            Assert.Equal("Pre-Order Car Pack", h.Name);
            Assert.Equal("13E2064584A4BA7759BA105051C2199ABC5FA5554D", h.FileName);
            Assert.Equal(Forza, h.TitleId);
        }
        Assert.Null(XeniaContent.ParseHeader(new byte[100]));
    }

    private void Package(string home, string type, string id, string name, int bytes, uint contentType)
    {
        var dir = Path.Combine(home, type, id);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "data"), new byte[bytes]);
        var headers = Path.Combine(home, "Headers", type);
        Directory.CreateDirectory(headers);
        File.WriteAllBytes(Path.Combine(headers, id + ".header"), XeniaContent.BuildHeader(contentType, name, id, Forza));
    }

    [Fact]
    public void Inventory_reads_both_layouts_once_each_and_skips_folders_without_a_header()
    {
        var content = Path.Combine(_root, "content");
        var current = Path.Combine(content, XeniaSaves.NoProfile, "4D5309C9");
        var legacy = Path.Combine(content, "4D5309C9");
        Package(current, "00000002", "AAAA", "Pre-Order Car Pack", 300, 2);
        Package(legacy, "00000002", "AAAA", "Pre-Order Car Pack", 300, 2);
        Package(legacy, "00000002", "BBBB", "Rally Expansion Pack", 900, 2);
        Package(legacy, "000B0000", "tu00000001_00000000", "Forza Horizon Title Update", 150, 0xB0000);
        Directory.CreateDirectory(Path.Combine(legacy, "000B0000", "New folder"));

        var packages = XeniaContent.Inventory(content, "4D5309C9");

        Assert.Equal(
            ["dlc/AAAA/Pre-Order Car Pack/300", "dlc/BBBB/Rally Expansion Pack/900", "update/tu00000001_00000000/Forza Horizon Title Update/150"],
            packages.Select(p => $"{p.Kind}/{p.Id}/{p.Name}/{p.Size}"));
        Assert.Empty(XeniaContent.Inventory(content, "4D530AA4"));
    }

    // ---- Config ----

    private const string Config = "[Memory]\r\nprotect_zero = true\r\n\r\n[Profiles]\r\n" +
        "logged_profile_slot_0_xuid = \"E03000004A1C77B2\"   \t# XUID of the profile to load on boot in slot 0\r\n" +
        "logged_profile_slot_1_xuid = \"\"                   \t# XUID of the profile to load on boot in slot 1\r\n\r\n[Storage]\r\ncontent_root = \"\"\r\n";

    private string Install(string name, string text)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, "content"));
        File.WriteAllText(Path.Combine(dir, "xenia-canary.config.toml"), text);
        File.WriteAllText(Path.Combine(dir, "xenia_canary.exe"), "");
        return dir;
    }

    [Fact]
    public void Config_write_changes_only_the_quoted_value()
    {
        var dir = Install("portable", Config);
        var path = Path.Combine(dir, "xenia-canary.config.toml");
        Assert.Equal("E03000004A1C77B2", XeniaConfig.Read(path));

        XeniaConfig.Write(path, Xuid);

        Assert.Equal(Config.Replace("E03000004A1C77B2", Xuid), File.ReadAllText(path));
        Assert.Equal(Xuid, XeniaConfig.Read(path));
        Assert.Throws<ArgumentException>(() => XeniaConfig.Write(path, "not-a-xuid"));
    }

    [Fact]
    public void Config_without_the_line_gets_it_under_Profiles_or_a_new_section()
    {
        var withSection = Path.Combine(Install("a", "[Profiles]\nother = 1\n"), "xenia-canary.config.toml");
        var without = Path.Combine(Install("b", "[Storage]\ncontent_root = \"\"\n"), "xenia-canary.config.toml");
        Assert.Null(XeniaConfig.Read(withSection));

        XeniaConfig.Write(withSection, Xuid);
        XeniaConfig.Write(without, Xuid);

        Assert.Equal($"[Profiles]\nlogged_profile_slot_0_xuid = \"{Xuid}\"\nother = 1\n", File.ReadAllText(withSection));
        Assert.Equal($"[Storage]\ncontent_root = \"\"\n\n[Profiles]\nlogged_profile_slot_0_xuid = \"{Xuid}\"\n", File.ReadAllText(without));
    }

    [Fact]
    public void A_games_configs_are_the_root_one_and_Xenia_Managers_own_for_that_game()
    {
        var dir = Install("xenia-manager", Config);
        File.WriteAllText(Path.Combine(dir, "xenia.config.toml"), "[Storage]\n"); // an older build's, never read here
        Directory.CreateDirectory(Path.Combine(dir, "config"));
        File.WriteAllText(Path.Combine(dir, "config", "Forza Horizon.config.toml"), Config);
        File.WriteAllText(Path.Combine(dir, "config", "Halo 3.config.toml"), Config);

        var configs = XeniaConfig.For(Path.Combine(dir, "content"), "Forza Horizon");

        Assert.Equal(
            [Path.Combine(dir, "xenia-canary.config.toml"), Path.Combine(dir, "config", "Forza Horizon.config.toml")],
            configs);
    }

    [Fact]
    public void With_no_profile_line_anywhere_the_installed_builds_config_is_the_one()
    {
        var dir = Install("emudeck", "[Storage]\n");
        File.WriteAllText(Path.Combine(dir, "xenia.config.toml"), "[Storage]\n");

        Assert.Equal([Path.Combine(dir, "xenia-canary.config.toml")], XeniaConfig.For(Path.Combine(dir, "content"), null));
    }

    // ---- The real files, when a capture is at hand ----

    [Fact]
    public void Captured_profile_files_round_trip_and_their_totals_match_the_rows()
    {
        var capture = Environment.GetEnvironmentVariable("SAVELOCKER_XENIA_CAPTURE");
        if (string.IsNullOrEmpty(capture) || !Directory.Exists(capture)) return;

        foreach (var gpd in Directory.EnumerateFiles(capture, "*.gpd", SearchOption.AllDirectories))
        {
            var x = Xdbf.Parse(File.ReadAllBytes(gpd));
            var again = Xdbf.Parse(x.Serialize());
            Assert.Equal(x.Entries.Select(e => (e.Namespace, e.Id, Convert.ToHexString(e.Data))),
                         again.Entries.Select(e => (e.Namespace, e.Id, Convert.ToHexString(e.Data))));
            if (Path.GetFileName(gpd) != "FFFE07D1.gpd") continue;
            var recounted = Xdbf.Parse(x.Serialize());
            XeniaProfile.Recount(recounted);
            Assert.Equal(XeniaProfile.GetInt(x, XeniaProfile.GamerscoreSetting), XeniaProfile.GetInt(recounted, XeniaProfile.GamerscoreSetting));
            Assert.Equal(XeniaProfile.GetInt(x, XeniaProfile.TitlesPlayedSetting), XeniaProfile.GetInt(recounted, XeniaProfile.TitlesPlayedSetting));
            Assert.Equal(XeniaProfile.GetInt(x, XeniaProfile.AchievementsSetting), XeniaProfile.GetInt(recounted, XeniaProfile.AchievementsSetting));
        }
        foreach (var account in Directory.EnumerateFiles(capture, "Account", SearchOption.AllDirectories))
            Assert.NotNull(XeniaProfile.Gamertag(File.ReadAllBytes(account)));
    }
}
