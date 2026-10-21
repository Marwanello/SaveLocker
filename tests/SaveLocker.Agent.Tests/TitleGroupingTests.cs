using System.Net.Http.Json;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>The name a game is grouped by (tasks/multi-emulator-games): its emulator suffix and region tag off.</summary>
public sealed class TitleNamesTests
{
    [Theory]
    [InlineData("Metroid Prime - Hunters (RetroArch)", "RetroArch", "", "Metroid Prime - Hunters", null)]
    [InlineData("Metroid Prime - Hunters (RetroArch, Rev 0)", "RetroArch", "USA", "Metroid Prime - Hunters", "Rev 0")]
    [InlineData("Metroid Prime - Hunters (USA) (RetroArch)", "RetroArch", "", "Metroid Prime - Hunters", null)]
    [InlineData("Metroid Prime - Hunters (USA) (Rev 1) (RetroArch)", "RetroArch", "USA", "Metroid Prime - Hunters", "Rev 1")]
    [InlineData("Chrono Trigger (RetroArch, Japan)", "RetroArch", "Japan", "Chrono Trigger", null)]
    [InlineData("Metroid Prime - Hunters", "melonDS", "USA", "Metroid Prime - Hunters", null)]
    [InlineData("Metroid Prime Trilogy (PrimeHack)", "PrimeHack", "USA", "Metroid Prime Trilogy", null)]
    // Not the emulator's suffix and not a region: part of the name.
    [InlineData("Daytona USA (Model 2)", "RetroArch", "", "Daytona USA (Model 2)", null)]
    // Another region than the game's is not dropped.
    [InlineData("Metroid Prime - Hunters (Europe) (RetroArch)", "RetroArch", "USA", "Metroid Prime - Hunters (Europe)", null)]
    public void Base_name_and_edition(string name, string emulator, string region, string baseName, string? edition)
    {
        var parsed = TitleNames.Parse(name, [emulator], region);
        Assert.Equal(baseName, parsed.BaseName);
        Assert.Equal(edition, parsed.Edition);
    }

    [Fact]
    public void Platform_is_the_first_console_tag_then_the_release()
    {
        Assert.Equal("DS", TitleNames.Platform([["Flatpak", "DS"]], "Nintendo DS"));
        Assert.Equal("Wii", TitleNames.Platform([null, ["Wii", "EmuDeck"]], null));
        Assert.Equal("Nintendo DS", TitleNames.Platform([["EmuDeck"]], "Nintendo DS"));
        Assert.Null(TitleNames.Platform([], null));
        Assert.True(TitleNames.SamePlatform(null, "DS"));
        Assert.False(TitleNames.SamePlatform("GBA", "DS"));
        Assert.True(TitleNames.SameRegion("", "USA"));
        Assert.False(TitleNames.SameRegion("Europe", "USA"));
    }
}

/// <summary>Titles on a real server: who groups, who never does, split and group by hand, and the backfill.</summary>
public sealed class TitleGroupingServerTests : IDisposable
{
    private const string AdminPassword = "titles-admin-pw";
    private readonly string _db = Path.Combine(Path.GetTempPath(), "savelocker-titles-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Guid.NewGuid().ToString("N")[..6];
    private ServerProcess _server;

    public TitleGroupingServerTests()
    {
        Directory.CreateDirectory(_db);
        _server = Start();
    }

    private ServerProcess Start() => new(new Dictionary<string, string>
        { ["Storage__DbPath"] = Path.Combine(_db, "savelocker.db"), ["Storage__ArchiveRoot"] = Path.Combine(_db, "archives") });

    public void Dispose()
    {
        _server.Dispose();
        try { Directory.Delete(_db, recursive: true); } catch (IOException) { }
    }

    private HttpClient Admin()
    {
        var http = new HttpClient { BaseAddress = new Uri(_server.Url) };
        http.DefaultRequestHeaders.Add("X-Admin-Password", AdminPassword);
        return http;
    }

    private async Task<ApiClient> Machine(string name)
    {
        using (var http = Admin())
            await http.PostAsJsonAsync("/api/admin/password", new SetAdminPasswordRequest(AdminPassword));
        var reg = await new ApiClient(_server.Url, null).RegisterAsync(name + _id, AdminPassword);
        return new ApiClient(_server.Url, reg.ApiKey);
    }

    private static async Task<GameDto> Add(ApiClient api, string name, string emulator, string? console = "DS",
        GameReleaseDto? release = null, string kind = GameSourceKinds.Emulator, string? key = null)
    {
        var game = await api.CreateGameAsync(new CreateGameRequest(name, null, null,
            IncludeGlobs: [name + ".sav"], Release: release, EmulatorKey: key));
        await api.SetGameSourceAsync(game.Id, new GameSourceDto(kind, emulator, console is null ? null : [console]));
        return game;
    }

    private async Task<Dictionary<Guid, GameStateDto>> Overview()
    {
        using var http = Admin();
        return (await http.GetFromJsonAsync<List<GameStateDto>>("/api/overview"))!.ToDictionary(s => s.Game.Id);
    }

    [Fact]
    public async Task One_title_per_game_across_emulators_and_none_for_what_must_stay_apart()
    {
        var pc = await Machine("pc");
        var deck = await Machine("deck");
        var hunters = "Metroid Prime - Hunters " + _id;
        var melon = await Add(pc, hunters, "melonDS");
        var retro = await Add(pc, hunters + " (RetroArch)", "RetroArch");
        var older = await Add(deck, hunters + " (USA) (RetroArch)", "RetroArch");
        var trilogy = "Metroid Prime Trilogy " + _id;
        var dolphin = await Add(pc, trilogy, "Dolphin", "Wii");
        var prime = await Add(pc, trilogy + " (PrimeHack)", "PrimeHack", "Wii");
        var steam = await Add(pc, hunters + " (Steam)", "Installed game", null, kind: GameSourceKinds.Steam);
        var gba = await Add(pc, hunters + " (mGBA)", "mGBA", "GBA");
        var europe = await Add(deck, "Advance Wars " + _id, "melonDS", release: new GameReleaseDto("DS", "Europe"));
        var usa = await Add(pc, "Advance Wars " + _id + " (RetroArch)", "RetroArch", release: new GameReleaseDto("DS", "USA"));
        var zelda = "Tears of the Kingdom " + _id;
        var eden = await Add(pc, zelda, "Eden", "Switch", key: "switch:0100F2C0115B" + _id[..4].ToUpperInvariant());
        var ryu = await Add(deck, zelda + " (Ryujinx)", "Ryujinx", "Switch");

        var o = await Overview();
        var title = o[melon.Id].Game.TitleId;
        Assert.NotNull(title);
        Assert.Equal(title, o[retro.Id].Game.TitleId);
        Assert.Equal(title, o[older.Id].Game.TitleId);
        Assert.Equal(hunters, o[retro.Id].Game.TitleName);
        Assert.True(o[melon.Id].Game.TitleLead);
        Assert.Equal("RetroArch", o[retro.Id].Emulator);
        Assert.NotNull(o[dolphin.Id].Game.TitleId);
        Assert.Equal(o[dolphin.Id].Game.TitleId, o[prime.Id].Game.TitleId);
        Assert.NotEqual(title, o[dolphin.Id].Game.TitleId);
        foreach (var alone in new[] { steam, gba, europe, usa, eden, ryu })
            Assert.Null(o[alone.Id].Game.TitleId);

        // The agent sees the title too, on its own game list.
        var listed = (await pc.ListGamesAsync()).Single(g => g.Id == retro.Id);
        Assert.Equal((title, hunters), (listed.TitleId, listed.TitleName));
    }

    [Fact]
    public async Task Split_out_stays_out_and_group_with_brings_it_back()
    {
        var pc = await Machine("pc");
        var trilogy = "Metroid Prime Trilogy " + _id;
        var dolphin = await Add(pc, trilogy, "Dolphin", "Wii");
        var prime = await Add(pc, trilogy + " (PrimeHack)", "PrimeHack", "Wii");
        using var http = Admin();

        (await http.PostAsync($"/api/games/{prime.Id}/title/split", null)).EnsureSuccessStatusCode();
        var o = await Overview();
        // A title of one is no title: both stand alone again.
        Assert.Null(o[prime.Id].Game.TitleId);
        Assert.Null(o[dolphin.Id].Game.TitleId);

        // A new source report does not take it back.
        await pc.SetGameSourceAsync(prime.Id, new GameSourceDto(GameSourceKinds.Emulator, "PrimeHack", ["Wii", "Flatpak"]));
        Assert.Null((await Overview())[prime.Id].Game.TitleId);

        var candidates = await http.GetFromJsonAsync<List<TitleCandidateDto>>($"/api/games/{dolphin.Id}/title/candidates");
        Assert.Contains(candidates!, c => c.GameId == prime.Id && c.SameName && c.Emulator == "PrimeHack");
        (await http.PostAsync($"/api/games/{prime.Id}/title/join?into={dolphin.Id}", null)).EnsureSuccessStatusCode();
        o = await Overview();
        Assert.NotNull(o[prime.Id].Game.TitleId);
        Assert.Equal(o[dolphin.Id].Game.TitleId, o[prime.Id].Game.TitleId);

        var refused = await http.PostAsync($"/api/games/{prime.Id}/title/join?into={prime.Id}", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_game_alone_grouped_with_a_titled_one_joins_its_title_and_leaves_it_whole()
    {
        var pc = await Machine("pc");
        var deck = await Machine("deck");
        var hunters = "Metroid Prime - Hunters " + _id;
        var melon = await Add(pc, hunters, "melonDS");
        var retro = await Add(pc, hunters + " (RetroArch)", "RetroArch");
        var older = await Add(deck, hunters + " (USA) (RetroArch)", "RetroArch");
        using var http = Admin();
        (await http.PostAsync($"/api/games/{older.Id}/title/split", null)).EnsureSuccessStatusCode();
        var title = (await Overview())[melon.Id].Game.TitleId;
        Assert.NotNull(title);

        // "Group with…" on the lone game, picking a game of the title: the console asks for the pick to join this game.
        var candidates = await http.GetFromJsonAsync<List<TitleCandidateDto>>($"/api/games/{older.Id}/title/candidates");
        Assert.Contains(candidates!, c => c.GameId == melon.Id);
        (await http.PostAsync($"/api/games/{melon.Id}/title/join?into={older.Id}", null)).EnsureSuccessStatusCode();

        var o = await Overview();
        Assert.Equal(title, o[older.Id].Game.TitleId);
        Assert.Equal(title, o[melon.Id].Game.TitleId);
        Assert.Equal(title, o[retro.Id].Game.TitleId);
    }

    [Fact]
    public async Task The_backfill_on_a_restart_changes_nothing()
    {
        var pc = await Machine("pc");
        var hunters = "Metroid Prime - Hunters " + _id;
        var melon = await Add(pc, hunters, "melonDS");
        var retro = await Add(pc, hunters + " (RetroArch)", "RetroArch");
        var before = (await Overview())[retro.Id].Game.TitleId;

        // Same database, a fresh process: the startup backfill runs again.
        _server.Dispose();
        _server = Start();
        var o = await Overview();
        Assert.Equal(before, o[retro.Id].Game.TitleId);
        Assert.Equal(before, o[melon.Id].Game.TitleId);
    }
}
