using LiteDB;
using SaveLocker.Agent;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Fixture-based coverage for <see cref="PlayniteLibrary"/> (tasks/playnite-plugin/plan.md, Phase 18).
/// Builds a tiny <c>games.db</c> shaped like Playnite's own library collection — the one advantage
/// this source has over <c>HeroicLibrary</c>'s manual-only verification, since a LiteDB fixture is
/// cheap to construct in-process, unlike Heroic's real Electron JSON files. Does NOT prove the real,
/// Playnite-bundled LiteDB build accepts a concurrent <c>Connection=shared;ReadOnly=true</c> open
/// while Playnite itself holds the file — <see cref="PlayniteLibrary"/>'s own remarks name that as
/// the one risk only real hardware can settle.
/// </summary>
public sealed class PlayniteLibraryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"savelocker-playnite-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private void Seed(params BsonDocument[] games)
    {
        using var db = new LiteDatabase($"Filename={_dbPath}");
        var col = db.GetCollection("Game");
        foreach (var g in games) col.Insert(g);
    }

    private static BsonDocument Game(
        string name, bool isInstalled = true, string? installDir = "C:\\Games\\Fixture",
        string? gameId = null, string? pluginId = null) => new()
    {
        ["_id"] = Guid.NewGuid(),
        ["Name"] = name,
        ["IsInstalled"] = isInstalled,
        ["InstallDirectory"] = installDir!,
        ["GameId"] = gameId!,
        ["PluginId"] = pluginId!,
    };

    [Fact]
    public void Read_ReturnsInstalledGameWithAllFields()
    {
        Seed(Game(
            "Celeste", installDir: @"C:\Games\Celeste",
            gameId: "504230", pluginId: "cb91dfc9-b977-43bf-8e70-55f46e410fab"));

        var games = PlayniteLibrary.Read(_dbPath);

        var game = Assert.Single(games);
        Assert.Equal("Celeste", game.Name);
        Assert.Equal(@"C:\Games\Celeste", game.InstallDirectory);
        Assert.Equal("504230", game.GameId);
        Assert.Equal(Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab"), game.PluginId);
    }

    [Fact]
    public void Read_SkipsUninstalledGames()
    {
        Seed(
            Game("Installed", isInstalled: true),
            Game("Not installed", isInstalled: false));

        var games = PlayniteLibrary.Read(_dbPath);

        Assert.Equal(["Installed"], games.Select(g => g.Name));
    }

    [Fact]
    public void Read_SkipsGamesWithNoInstallDirectory()
    {
        Seed(
            Game("Has a path", installDir: @"C:\Games\HasAPath"),
            Game("No path at all", installDir: null),
            Game("Blank path", installDir: ""));

        var games = PlayniteLibrary.Read(_dbPath);

        Assert.Equal(["Has a path"], games.Select(g => g.Name));
    }

    [Fact]
    public void Read_TreatsMissingOrUnparsablePluginIdAsEmptyGuid()
    {
        Seed(
            Game("No plugin id", pluginId: null),
            Game("Garbage plugin id", pluginId: "not-a-guid"));

        var games = PlayniteLibrary.Read(_dbPath);

        Assert.All(games, g => Assert.Equal(Guid.Empty, g.PluginId));
    }

    [Theory]
    [InlineData("cb91dfc9-b977-43bf-8e70-55f46e410fab", GameStore.Steam)]
    [InlineData("aebe8b7c-6dc3-4a66-af31-e7375c6b5e9e", GameStore.Gog)]
    [InlineData("00000002-dbd1-46c6-b5d0-b1ba559d10e4", GameStore.Epic)]
    [InlineData("402674cd-4af6-4886-b6ec-0e695bfa0688", GameStore.Amazon)]
    [InlineData("11111111-1111-1111-1111-111111111111", GameStore.Unknown)]
    public void StoreFor_MapsKnownPluginIds(string pluginId, GameStore expected)
    {
        Assert.Equal(expected, PlayniteLibrary.StoreFor(Guid.Parse(pluginId)));
    }

    [Fact]
    public void IsSteam_OnlyTrueForTheSteamPluginId()
    {
        Assert.True(PlayniteLibrary.IsSteam(Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab")));
        Assert.False(PlayniteLibrary.IsSteam(Guid.Parse("aebe8b7c-6dc3-4a66-af31-e7375c6b5e9e")));
        Assert.False(PlayniteLibrary.IsSteam(Guid.Empty));
    }

    [Fact]
    public void Read_OpensReadOnlyOverAnAlreadyClosedWriter()
    {
        // Not proof of concurrent access against a REAL, still-running Playnite (see this class's own
        // remarks) — but confirms the shared/read-only connection string this reads with is itself
        // valid and does not require exclusive access to a file another connection has touched.
        Seed(Game("Anything"));

        var exception = Record.Exception(() => PlayniteLibrary.Read(_dbPath));

        Assert.Null(exception);
    }
}
