using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// A store's PC game is never an emulator's game (v0.8.0 on the Deck, 2026-10-22): a same-named one does not make a Switch
/// save read as tracked, and the Steam ROM Manager shortcuts v0.8.0 recorded on PC games are dropped at the next rescan.
/// </summary>
public sealed class PcGameEmulatorMatchTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-pcmatch-" + Guid.NewGuid().ToString("N"));

    public PcGameEmulatorMatchTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private AgentConfig Config(params TrackedGame[] games)
    {
        var config = AgentConfig.Load(Path.Combine(_dir, "state", "config.json"));
        config.Games.AddRange(games);
        config.Save();
        return config;
    }

    private TrackedGame Game(string name, GameSourceDto? source, string? appId = null, string[]? shortcuts = null) => new()
    {
        GameId = Guid.NewGuid(), Name = name, SaveDirectory = Path.Combine(_dir, name), Source = source, SteamAppId = appId,
        ShortcutAppIds = (shortcuts ?? []).ToList(),
    };

    private ScanCandidate SwitchSave(string name) =>
        new ScanCandidate(name, Path.Combine(_dir, "yuzu", name), ScanSource.Emulator, false) with
        {
            EmulatorName = "Yuzu", EmulatorKey = "switch:010080B00AD66000", ShortcutAppIds = new[] { "3000000001" },
        };

    [Fact]
    public void A_store_pc_game_of_the_same_name_is_not_the_emulator_save()
    {
        var steam = Game("Undertale", new GameSourceDto(GameSourceKinds.Steam, "Installed game", new[] { "AppID 391540" }));
        Assert.Null(Enroller.TrackedFor(Config(steam), SwitchSave("Undertale")));
        Assert.Empty(SrmShortcuts.Backfill(AgentConfig.Load(Path.Combine(_dir, "state", "config.json")), new[] { SwitchSave("Undertale") }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(GameSourceKinds.Manual)]
    public void A_game_with_no_source_or_mapped_by_hand_still_is(string? kind)
    {
        var game = Game("Undertale", kind is null ? null : new GameSourceDto(kind, "Folder picked in the agent"));
        Assert.Equal(game.GameId, Enroller.TrackedFor(Config(game), SwitchSave("Undertale"))?.GameId);
    }

    [Fact]
    public void A_rescan_drops_the_shortcuts_v080_put_on_pc_games()
    {
        var alba = Game("Alba: A Wildlife Adventure", new GameSourceDto(GameSourceKinds.Heroic, "Epic Games"), "2599779049", ["2599779049"]);
        alba.ShortcutLaunchOptions["2599779049"] = new LaunchOptionsState { AppliedAt = DateTime.UtcNow };
        var sifu = Game("Sifu", new GameSourceDto(GameSourceKinds.Steam, "Installed game"), "2138710", ["2208192691"]);
        var noSource = Game("Overcooked!", null, null, ["2329661433"]);
        var emulator = Game("Super Metroid", new GameSourceDto(GameSourceKinds.Emulator, "RetroArch"), "2254219648", ["2254219648"]);
        var legacy = Game("Metal Gear", null, "2473684172", ["2473684172"]);
        legacy.IncludeGlobs = ["Metal Gear.srm"];
        var config = Config(alba, sifu, noSource, emulator, legacy);

        // The scan gives the stray shortcuts to the emulator saves they start, none of which is tracked here.
        var smash = SwitchSave("Super Smash Bros.") with { EmulatorKey = null, ShortcutAppIds = new[] { "2599779049", "2208192691" } };
        var kart = SwitchSave("Mario Kart 8") with { EmulatorKey = null, ShortcutAppIds = new[] { "2329661433" } };
        Assert.Equal(3, SrmShortcuts.Backfill(config, new[] { smash, kart }).Count);

        var saved = AgentConfig.Load(config.ConfigPath);
        TrackedGame Saved(TrackedGame g) => saved.Games.Single(x => x.GameId == g.GameId);
        Assert.Empty(Saved(alba).ShortcutAppIds);
        Assert.Null(Saved(alba).SteamAppId);
        Assert.Empty(Saved(alba).ShortcutLaunchOptions);
        Assert.False(Saved(alba).LaunchesUnder("2599779049"));
        Assert.Empty(Saved(sifu).ShortcutAppIds);
        Assert.Equal("2138710", Saved(sifu).SteamAppId);
        Assert.Empty(Saved(noSource).ShortcutAppIds);
        Assert.Equal(["2254219648"], Saved(emulator).ShortcutAppIds);
        Assert.Equal(["2473684172"], Saved(legacy).ShortcutAppIds);

        // Nothing left to drop: the next rescan writes nothing.
        Assert.Empty(SrmShortcuts.Backfill(saved, new[] { smash, kart }));
    }

    [Fact]
    public void A_shortcut_the_scan_did_not_see_is_kept()
    {
        // An SD card out, or a Steam folder it could not read: the scan says nothing about the shortcut, so a game with no source
        // (a folder mapped before sources were recorded, possibly an emulator's) keeps it and the app ID its launches match on.
        var mapped = Game("Pokemon X", null, "2329661433", ["2329661433"]);
        var config = Config(mapped);

        Assert.Empty(SrmShortcuts.Backfill(config, []));
        var saved = AgentConfig.Load(config.ConfigPath).Games.Single();
        Assert.Equal(["2329661433"], saved.ShortcutAppIds);
        Assert.Equal("2329661433", saved.SteamAppId);
    }
}
