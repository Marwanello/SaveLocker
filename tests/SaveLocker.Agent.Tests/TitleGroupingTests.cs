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

/// <summary>Copy this save to another emulator: which pairs, and what each file is called on the other side.</summary>
public sealed class EmulatorSaveCopyTests
{
    [Fact]
    public void Only_checked_pairs_are_offered()
    {
        Assert.NotNull(EmulatorSaveCopy.For("melonDS", "RetroArch"));
        Assert.NotNull(EmulatorSaveCopy.For("retroarch", "MELONDS"));
        Assert.NotNull(EmulatorSaveCopy.For("Dolphin", "PrimeHack"));
        Assert.Null(EmulatorSaveCopy.For("Dolphin", "RetroArch"));
        Assert.Null(EmulatorSaveCopy.For("DuckStation", "RetroArch"));
        Assert.Null(EmulatorSaveCopy.For(null, "RetroArch"));
    }

    [Fact]
    public void A_renamed_save_takes_the_targets_own_name_when_it_has_one()
    {
        var rule = EmulatorSaveCopy.For("melonDS", "RetroArch")!;
        var (files, refusal) = EmulatorSaveCopy.Map(rule, ["Hunters (USA).sav"], ["Hunters (USA) (Rev 1).srm"], ["Hunters (USA) (Rev 1).srm"]);
        Assert.Null(refusal);
        Assert.Equal(("Hunters (USA).sav", "Hunters (USA) (Rev 1).srm"), Assert.Single(files));

        // No target save yet: the extension swap, if the scope takes it.
        (files, refusal) = EmulatorSaveCopy.Map(rule, ["Hunters (USA).sav"], null, ["Hunters (USA).srm"]);
        Assert.Equal(("Hunters (USA).sav", "Hunters (USA).srm"), Assert.Single(files));

        (_, refusal) = EmulatorSaveCopy.Map(rule, ["a.sav", "b.sav"], null, null);
        Assert.Contains("2 .sav files", refusal);
        // The scope names the target's save outright, whatever its ROM is called: that is the name.
        (files, _) = EmulatorSaveCopy.Map(rule, ["Hunters (USA).sav"], null, ["Hunters (Europe).srm"]);
        Assert.Equal("Hunters (Europe).srm", Assert.Single(files).To);
        (_, refusal) = EmulatorSaveCopy.Map(rule, ["Hunters (USA).sav"], null, ["saves/*.dsv"]);
        Assert.NotNull(refusal);
    }

    [Fact]
    public void A_folder_save_copies_under_its_own_paths_within_the_targets_scope()
    {
        var rule = EmulatorSaveCopy.For("Dolphin", "PrimeHack")!;
        var (files, refusal) = EmulatorSaveCopy.Map(rule, ["banner.bin", "data/save1.dat"], null, null);
        Assert.Null(refusal);
        Assert.Equal(new[] { ("banner.bin", "banner.bin"), ("data/save1.dat", "data/save1.dat") }, files);
        (_, refusal) = EmulatorSaveCopy.Map(rule, ["banner.bin"], null, ["data/**"]);
        Assert.NotNull(refusal);
    }
}

/// <summary>Titles on a real server: who groups, who never does, split and group by hand, and the backfill.</summary>
public sealed class TitleGroupingServerTests : IDisposable
{
    private const string AdminPassword = "titles-admin-pw";
    private readonly string _db = Path.Combine(Path.GetTempPath(), "savelocker-titles-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Guid.NewGuid().ToString("N")[..6];
    private ServerProcess _server;
    private readonly Dictionary<ApiClient, string> _keys = new();

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
        var api = new ApiClient(_server.Url, reg.ApiKey);
        _keys[api] = reg.ApiKey;
        return api;
    }

    private static async Task<GameDto> Add(ApiClient api, string name, string emulator, string? console = "DS",
        GameReleaseDto? release = null, string kind = GameSourceKinds.Emulator, string? key = null, bool wholeFolder = false)
    {
        var game = await api.CreateGameAsync(new CreateGameRequest(name, null, null,
            IncludeGlobs: wholeFolder ? null : [name + ".sav"], Release: release, EmulatorKey: key));
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

    private async Task<Guid> Push(ApiClient api, Guid gameId, string file, string content, string[]? include, Guid? parent = null,
        UploadStatus expect = UploadStatus.Created)
    {
        var dir = Path.Combine(_db, "saves", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, file))!);
        File.WriteAllText(Path.Combine(dir, file), content);
        var (_, hash) = SaveArchive.ComputeManifest(dir, null, include);
        var zip = Path.Combine(_db, Guid.NewGuid().ToString("N") + ".zip");
        SaveArchive.CreateArchive(dir, zip, null, include);
        var result = await api.UploadAsync(gameId, hash, parent, false, zip);
        Assert.Equal(expect, result.Status);
        return result.Version!.Id;
    }

    [Fact]
    public async Task A_save_copies_to_another_emulator_as_its_new_latest_and_the_old_one_stays()
    {
        var pc = await Machine("pc");
        var hunters = "Metroid Prime - Hunters " + _id;
        var melon = await Add(pc, hunters, "melonDS");
        var retro = await Add(pc, hunters + " (RetroArch)", "RetroArch");
        var sav = await Push(pc, melon.Id, hunters + ".sav", "melonDS progress", [hunters + ".sav"]);
        await Push(pc, retro.Id, hunters + " (RetroArch).sav", "unused", [hunters + " (RetroArch).sav"]);
        using var http = Admin();

        var targets = await http.GetFromJsonAsync<List<CopyTargetDto>>($"/api/games/{melon.Id}/versions/{sav}/copy-targets");
        var target = Assert.Single(targets!);
        // This RetroArch game was made with a .sav scope, which names no .srm: refused, not guessed.
        Assert.Contains(".srm", target.Refusal);
        Assert.Empty(target.Files);

        // A real RetroArch tree: its scope names the .srm.
        var srm = hunters + " (USA).srm";
        var ra = await pc.CreateGameAsync(new CreateGameRequest(hunters + " (USA) (RetroArch)", null, null, IncludeGlobs: [srm]));
        await pc.SetGameSourceAsync(ra.Id, new GameSourceDto(GameSourceKinds.Emulator, "RetroArch", ["DS"]));
        var raHead = await Push(pc, ra.Id, srm, "old RetroArch save", [srm]);
        targets = await http.GetFromJsonAsync<List<CopyTargetDto>>($"/api/games/{melon.Id}/versions/{sav}/copy-targets");
        var t = targets!.Single(x => x.GameId == ra.Id);
        Assert.Null(t.Refusal);
        Assert.Equal(new CopyFileDto(hunters + ".sav", srm, "melonDS progress".Length), Assert.Single(t.Files));
        Assert.Equal(raHead, t.TargetHead?.Id);

        var copied = await (await http.PostAsync($"/api/games/{melon.Id}/versions/{sav}/copy-to?target={ra.Id}", null))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SaveVersionDto>();
        Assert.Equal(("RetroArch", "melonDS", sav), ("RetroArch", copied!.CopiedFrom, copied.CopiedFromVersionId));
        var o = await Overview();
        Assert.Equal(copied.Id, o[ra.Id].Head?.Id);
        Assert.Equal(sav, o[melon.Id].Head?.Id);
        var versions = await pc.GetGameVersionsAsync(ra.Id);
        Assert.Contains(versions, v => v.Id == raHead);
        Assert.Equal(raHead, copied.ParentVersionId);

        // What a machine pulls: the .srm with the .sav's bytes, and the hash it computes over the restored folder.
        using var agent = new HttpClient { BaseAddress = new Uri(_server.Url) };
        agent.DefaultRequestHeaders.Add("X-Api-Key", _keys[pc]);
        var zip = Path.Combine(_db, "pulled.zip");
        await File.WriteAllBytesAsync(zip, await agent.GetByteArrayAsync($"/api/versions/{copied.Id}/download"));
        var restored = Path.Combine(_db, "restored");
        SaveArchive.RestoreArchive(zip, restored);
        Assert.Equal("melonDS progress", File.ReadAllText(Path.Combine(restored, srm)));
        Assert.Equal(copied.ContentHash, SaveArchive.ComputeManifest(restored, null, [srm]).ContentHash);
    }

    [Fact]
    public async Task A_copy_is_refused_between_formats_that_differ_and_into_an_open_conflict()
    {
        var pc = await Machine("pc");
        var trilogy = "Metroid Prime Trilogy " + _id;
        var deck = await Machine("deck");
        var dolphin = await Add(pc, trilogy, "Dolphin", "Wii", wholeFolder: true);
        var prime = await Add(pc, trilogy + " (PrimeHack)", "PrimeHack", "Wii", wholeFolder: true);
        var other = await Add(pc, trilogy + " (RetroArch)", "RetroArch", "Wii", wholeFolder: true);
        var v = await Push(pc, dolphin.Id, "data/save1.dat", "dolphin", null);
        using var http = Admin();
        var targets = (await http.GetFromJsonAsync<List<CopyTargetDto>>($"/api/games/{dolphin.Id}/versions/{v}/copy-targets"))!;
        Assert.Contains("different formats", targets.Single(t => t.GameId == other.Id).Refusal);
        Assert.Equal("data/save1.dat", Assert.Single(targets.Single(t => t.GameId == prime.Id).Files).To);

        // Two machines diverge on PrimeHack's save: an open conflict there refuses the copy.
        var first = await Push(pc, prime.Id, "data/save1.dat", "pc", null);
        await Push(deck, prime.Id, "data/save1.dat", "deck", null, expect: UploadStatus.Conflict);
        targets = (await http.GetFromJsonAsync<List<CopyTargetDto>>($"/api/games/{dolphin.Id}/versions/{v}/copy-targets"))!;
        Assert.Contains("conflict", targets.Single(t => t.GameId == prime.Id).Refusal);
        _ = first;

        var refused = await http.PostAsync($"/api/games/{dolphin.Id}/versions/{v}/copy-to?target={other.Id}", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, refused.StatusCode);
        // An unrelated game is never a target, whatever its emulator.
        var lone = await Add(pc, "Something else " + _id, "PrimeHack", "Wii", wholeFolder: true);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest,
            (await http.PostAsync($"/api/games/{dolphin.Id}/versions/{v}/copy-to?target={lone.Id}", null)).StatusCode);
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
