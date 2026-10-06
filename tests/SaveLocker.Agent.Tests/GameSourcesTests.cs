using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

public sealed class GameSourcesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-sources-" + Guid.NewGuid().ToString("N"));

    public GameSourcesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static ScanCandidate Emulated(string name, string? system = "snes", string dir = "/home/deck/Emulation/saves/retroarch/saves") =>
        new(name, dir, ScanSource.Emulator, false, EmulatorName: RetroArchSaves.EmulatorName, EmulatorSystem: system);

    [Fact]
    public void An_emulated_game_names_its_emulator_then_its_console()
    {
        var s = GameSources.From(Emulated("Chrono Trigger", dir: "/home/deck/.var/app/org.libretro.RetroArch/config/retroarch/saves"));
        Assert.Equal(GameSourceKinds.Emulator, s.Kind);
        Assert.Equal("RetroArch", s.Detail);
        Assert.Equal(new[] { "SNES", "Flatpak" }, s.Tags);
        Assert.Equal("Emulator › RetroArch", GameSources.Describe(s));
        Assert.Equal(new[] { "SNES", "EmuDeck" }, GameSources.From(Emulated("Chrono Trigger")).Tags);
    }

    [Fact]
    public void Each_scanner_source_has_its_own_two_levels()
    {
        Assert.Equal(new GameSourceDto("steam", "Installed game", ["AppID 1245620", "Proton"]),
            GameSources.From(new ScanCandidate("Elden Ring", "/x", ScanSource.SteamInstalled, true, SteamAppId: "1245620", PrefixPath: "/p")),
            new SourceComparer());
        Assert.Equal("Epic Games", GameSources.From(new ScanCandidate("Hades II", "/x", ScanSource.Heroic, false, Store: GameStore.Epic)).Detail);
        Assert.Equal("Playnite library", GameSources.From(new ScanCandidate("Hades II", "/x", ScanSource.Playnite, false)).Detail);
        Assert.Equal(new GameSourceDto("save-folder", "Saved Games"),
            GameSources.From(new ScanCandidate("Hades II", Path.Combine("C:", "Users", "me", "Saved Games", "Hades II"), ScanSource.SaveRoot, false)),
            new SourceComparer());
    }

    [Fact]
    public void An_emulated_game_that_shares_a_pc_games_name_gets_its_console_appended()
    {
        var kept = GameSources.AvoidNameClashes(
            [Emulated("Chrono Trigger"), Emulated("Super Metroid"), Emulated("Tetris", system: null)],
            ["CHRONO TRIGGER", "Tetris"]);
        Assert.Equal(new[] { "Chrono Trigger (SNES)", "Super Metroid", "Tetris (RetroArch)" }, kept.Select(c => c.Name));
    }

    [Fact]
    public void A_game_enrolled_before_sources_gets_one_from_a_scan_that_found_it_at_the_same_folder()
    {
        var saves = Path.Combine(_dir, "saves");
        Directory.CreateDirectory(saves);
        var config = AgentConfig.Load(Path.Combine(_dir, "state", "config.json"));
        var same = new TrackedGame { GameId = Guid.NewGuid(), Name = "Chrono Trigger", SaveDirectory = saves };
        var elsewhere = new TrackedGame { GameId = Guid.NewGuid(), Name = "Super Metroid", SaveDirectory = Path.Combine(_dir, "other") };
        config.MutateGames(l => { l.Add(same); l.Add(elsewhere); });
        config.Save();

        var filled = GameSources.Backfill(config, [Emulated("Chrono Trigger", dir: saves), Emulated("Super Metroid", dir: saves)]);

        Assert.Equal(new[] { same.GameId }, filled.Select(g => g.GameId));
        var reloaded = AgentConfig.Load(config.ConfigPath);
        Assert.Equal("RetroArch", reloaded.FindGame("Chrono Trigger")!.Source?.Detail);
        Assert.Null(reloaded.FindGame("Super Metroid")!.Source);
    }

    [Fact]
    public void A_source_another_process_saved_survives_a_stale_hosts_save()
    {
        var config = AgentConfig.Load(Path.Combine(_dir, "state", "config.json"));
        var game = new TrackedGame { GameId = Guid.NewGuid(), Name = "Chrono Trigger", SaveDirectory = _dir };
        config.MutateGames(l => l.Add(game));
        config.Save();

        AgentConfig.Load(config.ConfigPath).SaveGameSource(game.GameId, GameSources.Manual(GameSources.FolderPickedInAgent));
        config.Save();

        Assert.Equal(GameSourceKinds.Manual, AgentConfig.Load(config.ConfigPath).FindGame("Chrono Trigger")!.Source?.Kind);
    }

    private sealed class SourceComparer : IEqualityComparer<GameSourceDto>
    {
        public bool Equals(GameSourceDto? x, GameSourceDto? y) => x is not null && x.SameAs(y);
        public int GetHashCode(GameSourceDto obj) => obj.Kind.GetHashCode();
    }
}
