using SaveLocker.Shared;
using Xunit;
using S = SaveLocker.Agent.Tests.SwitchFixtures;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Which version of a Switch game each emulator runs (<see cref="SwitchGameVersion"/>), from the lines the real emulators
/// write (2026-10-16: the PC's Eden v0.2.1, the Deck's Yuzu, Ryujinx 1.3.2), and the check it feeds: a save made by a
/// newer version than the one here is missing its update.
/// </summary>
public sealed class SwitchGameVersionTests : IDisposable
{
    private const string Acnh = "01006F8002326000";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-switchver-" + Guid.NewGuid().ToString("N"));

    public SwitchGameVersionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string rel) => Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));

    private static void At(string path, string text, int minutesAgo)
    {
        S.Write(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
    }

    private static string Boot(string titleId, string version) =>
        "[   1.183766] Core <Info> core/core.cpp:327:Load: Loading Animal Crossing: New Horizons (01006F8002326000) ...\n" +
        $"[   1.787343] Frontend <Info> yuzu/main_window.cpp:2177:BootGame: Booting game: {titleId} | Animal Crossing: New Horizons (64-bit) | {version}\n" +
        "[   8.679261] Service.FS <Info> core/hle/service/filesystem/fsp/fsp_srv.cpp:269:OpenSaveDataFileSystem: called.\n";

    [Fact]
    public void A_yuzu_fork_says_the_version_it_last_booted_and_its_game_list_cache_the_one_installed_since()
    {
        var data = P("eden-windows-msvc/user");
        var eden = S.Eden(P("Emulation"), "C4D7AB8B14D1F6F277CD40D6A0F00ECA") with { Data = data };
        Assert.Null(SwitchGameVersion.Of(eden, Acnh));

        // The log before last booted the base game; the last one booted another game, then the update.
        At(Path.Combine(data, "log", "eden_log.txt.old.txt"), Boot(Acnh, "1.0.0"), 60);
        At(Path.Combine(data, "log", "eden_log.txt"), Boot("0100000000010000", "1.0.0") + Boot(Acnh, "1.6.0"), 30);
        Assert.Equal("1.6.0", SwitchGameVersion.Of(eden, Acnh));
        Assert.Null(SwitchGameVersion.Of(eden, "0100ABCDEF000000"));

        // An update installed since: the game list, read after the last boot, knows it first.
        var cache = Path.Combine(data, "cache", "game_list", Acnh + ".pv.txt");
        At(cache, "Update (2.0.8)\n[60 FPS v2.0.6] (Cheats)\nDLC (300, 301)", 10);
        Assert.Equal("2.0.8", SwitchGameVersion.Of(eden, Acnh));
        // Turned off in the emulator, it runs the base game.
        At(cache, "[D] Update (2.0.8)\nDLC (300, 301)", 5);
        Assert.Equal("1.0.0", SwitchGameVersion.Of(eden, Acnh));
        // A boot after the cache was written is what it really ran.
        At(Path.Combine(data, "log", "eden_log.txt"), Boot(Acnh, "1.6.0"), 1);
        Assert.Equal("1.6.0", SwitchGameVersion.Of(eden, Acnh));
    }

    [Fact]
    public void Ryujinx_says_it_in_its_logs()
    {
        var dir = P("ryu");
        var ryu = S.Ryujinx(dir, "00000000000000010000000000000000") with { Data = dir };
        At(Path.Combine(dir, "Logs", "Ryujinx_1.3.2_2026-08-13_01-43-12.log"),
            "00:00:02.101 |I| Application LoadFromNca: Application Loaded: Animal Crossing: New Horizons v1.6.0 [01006f8002326000] [64-bit]\n", 20);
        At(Path.Combine(dir, "Logs", "Ryujinx_1.3.2_2026-08-13_01-46-22.log"),
            "00:00:01.733 |I| Application LoadFromNca: Application Loaded: Pokémon Legends: Arceus v1.0.0 [01001f5010dfa000] [64-bit]\n", 10);
        Assert.Equal("1.6.0", SwitchGameVersion.Of(ryu, Acnh));
    }

    [Fact]
    public void A_save_from_a_newer_version_than_the_one_here_is_missing_its_update()
    {
        var made = SwitchGameVersion.Package(Acnh, "1.6.0")!;
        var update = Assert.Single(made);
        Assert.Equal(("update", "01006F8002326800", "Update 1.6.0", "1.6.0"), (update.Kind, update.Id, update.Name, update.Version));
        Assert.Empty(SwitchGameVersion.Package(Acnh, "1.0.0")!);
        Assert.Null(SwitchGameVersion.Package(Acnh, null));
        Assert.Equal("1.6.0", SwitchGameVersion.VersionOf(made));
        Assert.Equal("1.0.0", SwitchGameVersion.VersionOf([]));
        Assert.Null(SwitchGameVersion.VersionOf(null));

        // The PC's base game can't open the Deck's 1.6.0 island; 1.1.0 can't either; the same or newer can.
        Assert.Equal(["Update 1.6.0"], DlcCheck.Missing(made, SwitchGameVersion.Package(Acnh, "1.0.0")).Select(p => p.Name));
        Assert.Single(DlcCheck.Missing(made, SwitchGameVersion.Package(Acnh, "1.1.0")));
        Assert.Empty(DlcCheck.Missing(made, SwitchGameVersion.Package(Acnh, "1.6.0")));
        Assert.Empty(DlcCheck.Missing(made, SwitchGameVersion.Package(Acnh, "2.0.8")));
        Assert.Empty(DlcCheck.Missing(SwitchGameVersion.Package(Acnh, "1.0.0"), []));
        // Without versions (Xenia), any title update still counts.
        Assert.Empty(DlcCheck.Missing(made, [new ContentPackageDto(ContentPackageDto.Update, "tu2", "Title Update 2", 50)]));

        Assert.Equal("1 title update (Update 1.6.0)", DlcCheck.Summary(DlcCheck.Missing(made, [])));
        Assert.True(SwitchGameVersion.IsNewer("1.6.0", "1.0.0"));
        Assert.False(SwitchGameVersion.IsNewer("1.6.0", "2.0.8"));
        Assert.False(SwitchGameVersion.IsNewer("1.6.0", null));
    }

    [Fact]
    public void The_version_is_read_only_from_the_install_that_holds_the_games_folder()
    {
        var data = P("eden-user");
        var eden = S.Eden(P("Emulation"), "C4D7AB8B14D1F6F277CD40D6A0F00ECA") with { Data = data };
        At(Path.Combine(data, "log", "eden_log.txt"), Boot(Acnh, "1.6.0"), 1);
        var game = new TrackedGame
        {
            GameId = Guid.NewGuid(), Name = "Animal Crossing: New Horizons", EmulatorKey = "switch:" + Acnh,
            Source = new GameSourceDto(GameSourceKinds.Emulator, "Eden"),
            SaveDirectory = S.EdenSave(P("Emulation"), SwitchEmulators.DeviceProfile, Acnh),
        };
        Assert.Equal("1.6.0", SwitchGameVersion.VersionOf(SwitchGameVersion.Inventory(game, [eden])));

        // An Eden elsewhere, by name alone, says nothing about this folder.
        game.SaveDirectory = P("elsewhere/" + Acnh);
        Assert.Null(SwitchGameVersion.Inventory(game, [eden]));
    }

    [Fact]
    public void A_log_written_since_it_was_read_is_read_again()
    {
        var data = P("eden-windows-msvc/user");
        var eden = S.Eden(P("Emulation"), "C4D7AB8B14D1F6F277CD40D6A0F00ECA") with { Data = data };
        var log = Path.Combine(data, "log", "eden_log.txt");
        At(log, Boot(Acnh, "1.0.0"), 5);
        Assert.Equal("1.0.0", SwitchGameVersion.Of(eden, Acnh));

        // The update installed and the game started with it: the same log, rewritten.
        At(log, Boot(Acnh, "1.0.0") + Boot(Acnh, "1.6.0"), 1);
        Assert.Equal("1.6.0", SwitchGameVersion.Of(eden, Acnh));
    }
}
