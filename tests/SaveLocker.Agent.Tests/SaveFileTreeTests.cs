using System.Net;
using System.Net.Http.Json;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/save-file-trees Phases 1 and 3 without a server: the CRC-32 the console's diff compares, the
/// diff itself, and which files of a save folder a push leaves out.
/// </summary>
public sealed class SaveFileTreeArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-filetree-" + Guid.NewGuid().ToString("N"));

    public SaveFileTreeArchiveTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string P(params string[] parts) => Path.Combine(_root, Path.Combine(parts));

    private string Dir(string name, params (string Rel, string Content)[] files)
    {
        var dir = P(name.Split('/'));
        Directory.CreateDirectory(dir);
        foreach (var (rel, content) in files)
        {
            var path = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        return dir;
    }

    private IReadOnlyList<SaveArchive.ArchiveFolder> Zip(string name, params SaveRoot[] roots)
    {
        var zip = P(name + ".zip");
        SaveArchive.CreateArchive(roots, zip);
        return SaveArchive.ListArchiveFolders(zip, int.MaxValue);
    }

    [Fact]
    public void Each_file_carries_its_crc_and_an_untouched_file_keeps_it()
    {
        var dir = Dir("crc", ("keep.sav", "same bytes"), ("edit.sav", "before"));
        var first = Zip("crc1", SaveRoot.Primary(dir)).Single().Files.ToDictionary(f => f.Path);
        File.WriteAllText(Path.Combine(dir, "edit.sav"), "after!");
        var second = Zip("crc2", SaveRoot.Primary(dir)).Single().Files.ToDictionary(f => f.Path);

        Assert.NotEqual(0u, first["keep.sav"].Crc32);
        Assert.Equal(first["keep.sav"].Crc32, second["keep.sav"].Crc32);
        Assert.NotEqual(first["edit.sav"].Crc32, second["edit.sav"].Crc32);
        Assert.Equal(first["edit.sav"].Size, second["edit.sav"].Size);   // same size: only the CRC tells
    }

    [Fact]
    public void The_diff_counts_added_changed_removed_and_a_move_between_folders()
    {
        var parent = Zip("p",
            SaveRoot.Primary(Dir("p/main", ("a.sav", "a1"), ("b.sav", "b"), ("keep.sav", "k"))),
            new SaveRoot("states", Dir("p/states", ("x.state", "x"))));
        var current = Zip("c",
            SaveRoot.Primary(Dir("c/main", ("a.sav", "a2"), ("c.sav", "new"), ("keep.sav", "k"))),
            new SaveRoot("states", Dir("c/states", ("b.sav", "b"))));

        var diff = SaveArchive.DiffArchiveFolders(parent, current);

        Assert.Equal((2, 1, 2, 1), (diff.Added, diff.Changed, diff.Removed, diff.Unchanged));
        Assert.Equal(new[]
        {
            ("main", "a.sav", FileChange.Changed, 2L),
            ("main", "b.sav", FileChange.Removed, 1L),
            ("main", "c.sav", FileChange.Added, 3L),
            ("states", "b.sav", FileChange.Added, 1L),
            ("states", "x.state", FileChange.Removed, 1L),
        }, diff.Files.Select(f => (f.Key, f.Path, f.Change, f.Size)));
    }

    [Fact]
    public void The_first_version_is_all_added_and_the_list_is_capped_but_not_the_counts()
    {
        var first = Zip("first", SaveRoot.Primary(Dir("first", ("one.sav", "1"), ("sub/two.sav", "2"), ("three.sav", "3"))));

        var all = SaveArchive.DiffArchiveFolders(null, first);
        Assert.Equal((3, 0, 0, 0), (all.Added, all.Changed, all.Removed, all.Unchanged));
        Assert.Contains(all.Files, f => f.Path == "sub/two.sav" && f.Change == FileChange.Added);

        var capped = SaveArchive.DiffArchiveFolders(null, first, maxFiles: 1);
        Assert.Equal(3, capped.Added);
        Assert.Single(capped.Files);
    }

    [Fact]
    public void Archive_hashes_are_per_folder_and_match_the_files()
    {
        var main = Dir("h/main", ("slot.sav", "slot bytes"));
        var states = Dir("h/states", ("q.state", "state bytes"));
        var zip = P("h.zip");
        SaveArchive.CreateArchive(new[] { SaveRoot.Primary(main), new SaveRoot("states", states) }, zip);

        var hashed = SaveArchive.HashArchiveFiles(zip);

        Assert.Equal(new[] { "main", "states" }, hashed.Select(f => f.Key));
        Assert.Equal(SaveArchive.HashFile(Path.Combine(main, "slot.sav")), Assert.Single(hashed[0].Files).Sha256);
        Assert.Equal(SaveArchive.HashFile(Path.Combine(states, "q.state")), Assert.Single(hashed[1].Files).Sha256);
    }

    [Fact]
    public void Unsynced_files_are_split_into_other_games_and_excluded_ones()
    {
        var shared = Dir("shared", ("Chrono.srm", "c"), ("Chrono.rtc", "r"), ("Zelda.srm", "z"), ("deep/Mana.srm", "m"));
        var roots = new[] { SaveRoot.Primary(shared, new[] { "Chrono.srm", "Chrono.rtc" }) };

        var unsynced = SaveArchive.ListUnsyncedFiles(roots, new[] { "*.rtc" });

        Assert.Equal(new[] { ("Chrono.rtc", true), ("Zelda.srm", false), ("deep/Mana.srm", false) },
            unsynced.Select(u => (u.Path, u.Excluded)).OrderBy(u => u.Path, StringComparer.Ordinal));
        Assert.All(unsynced, u => Assert.Equal("main", u.Key));

        // Two scoped folders of one game in one directory: a file neither takes is listed once.
        var both = new[] { SaveRoot.Primary(shared, new[] { "Chrono.srm" }), new SaveRoot("clock", shared, new[] { "Chrono.rtc" }) };
        var once = SaveArchive.ListUnsyncedFiles(both);
        Assert.Equal(2, once.Count);
        Assert.DoesNotContain(once, u => u.Path is "Chrono.srm" or "Chrono.rtc");
    }

    [Fact]
    public void Archive_names_split_into_folder_and_path()
    {
        Assert.Equal(("main", "sub/a.sav"), SaveArchive.SplitArchiveName("sub/a.sav"));
        Assert.Equal(("states", "x/q.state"), SaveArchive.SplitArchiveName(".savelocker/paths/states/x/q.state"));
        Assert.Null(SaveArchive.SplitArchiveName(SaveArchive.MarkerName("states")));
    }
}

/// <summary>
/// tasks/save-file-trees against a real server: the console's per-version changes (Phase 1), the head's
/// file hashes an agent compares with, and the agent's own file tree (Phase 3) for a RetroArch game whose
/// save and states folders other ROMs share.
/// </summary>
public sealed class SaveFileTreeServerTests : IClassFixture<ServerProcess>, IDisposable
{
    private const string Chrono = "Chrono Trigger (USA)";
    private const string Zelda = "Zelda (USA)";
    private const string AdminPassword = "file-tree-admin-pw";

    private readonly ServerProcess _server;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-filetree-" + Guid.NewGuid().ToString("N"));

    public SaveFileTreeServerTests(ServerProcess server)
    {
        _server = server;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private HttpClient Admin()
    {
        var http = new HttpClient { BaseAddress = new Uri(_server.Url) };
        http.DefaultRequestHeaders.Add("X-Admin-Password", AdminPassword);
        return http;
    }

    /// <summary>Sets the console password the first time; afterwards the header already carries it.</summary>
    private async Task LockConsole()
    {
        using var http = Admin();
        (await http.PostAsJsonAsync("/api/admin/password", new SetAdminPasswordRequest(AdminPassword))).EnsureSuccessStatusCode();
    }

    private async Task<AgentConfig> Machine(string name)
    {
        var config = AgentConfig.Load(Path.Combine(_dir, name, "state", "config.json"));
        config.ServerUrl = _server.Url;
        var reg = await new ApiClient(config.ServerUrl, null).RegisterAsync(name + Guid.NewGuid().ToString("N")[..6]);
        config.ApiKey = reg.ApiKey;
        config.MachineId = reg.MachineId;
        config.MachineName = name;
        config.Save();
        return config;
    }

    private string Emulation(string machine) => Path.Combine(_dir, machine, "Emulation");
    private string Saves(string machine) => Path.Combine(Emulation(machine), "saves", "retroarch", "saves");
    private string States(string machine) => Path.Combine(Emulation(machine), "saves", "retroarch", "states");

    private static void Write(string dir, string name, string content)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), content);
    }

    /// <summary>Enrolls <paramref name="title"/>'s ROM. The class shares one server, and a title is one
    /// game there, so each test plays a title of its own.</summary>
    private async Task<TrackedGame> Enroll(AgentConfig config, string machine, string title = "Chrono Trigger")
    {
        var found = RetroArchSaves.Scan(RetroArchConfig.Folders(new[] { Emulation(machine) }, Array.Empty<string>()),
            Array.Empty<string>()).ToList();
        var id = found.FindIndex(c => c.Name == title);
        Assert.Equal((1, 0), await Enroller.EnrollAsync(config, found, new[] { id }));
        return Reload(config, title);
    }

    private static TrackedGame Reload(AgentConfig config, string title = "Chrono Trigger") =>
        AgentConfig.Load(config.ConfigPath).FindGame(title)!;

    private static Dictionary<(string Key, string Path), LocalFileDto> Files(GameFilesDto tree) =>
        tree.Folders.SelectMany(f => f.Files.Select(x => (f.Key, x))).ToDictionary(x => (x.Key, x.x.Path), x => x.x);

    [Fact]
    public async Task Each_version_lists_what_it_changed_and_only_the_console_may_ask()
    {
        const string Mana = "Secret of Mana (USA)";
        Write(Saves("v"), Mana + ".srm", "sram-1");
        Write(States("v"), Mana + ".state1", "slot-1");
        var v = await Machine("v");
        var game = await Enroll(v, "v", "Secret of Mana");
        await using var engine = new SyncEngine(v, ApiClient.For(v));

        Assert.NotNull(await engine.PushAsync(game, force: true));
        Write(Saves("v"), Mana + ".srm", "sram-2");
        Assert.NotNull(await engine.PushAsync(game = Reload(v, "Secret of Mana")));
        Write(States("v"), Mana + ".state2", "slot-2");
        Assert.NotNull(await engine.PushAsync(game = Reload(v, "Secret of Mana")));
        File.Delete(Path.Combine(States("v"), Mana + ".state1"));
        Assert.NotNull(await engine.PushAsync(game = Reload(v, "Secret of Mana")));

        await LockConsole();
        using var admin = Admin();
        var changes = (await admin.GetFromJsonAsync<VersionChangesDto[]>($"/api/games/{game.GameId}/versions/changes"))!;

        // Newest first: removed a state, added a state, changed the save, the first push.
        Assert.Equal(4, changes.Length);
        Assert.Equal((0, 0, 1, 2), (changes[0].Added, changes[0].Changed, changes[0].Removed, changes[0].Unchanged));
        Assert.Equal(("states", Mana + ".state1", FileChange.Removed), Only(changes[0]));
        Assert.Equal(("states", Mana + ".state2", FileChange.Added), Only(changes[1]));
        Assert.Equal(("main", Mana + ".srm", FileChange.Changed), Only(changes[2]));
        Assert.Equal((2, 0, 0, 0), (changes[3].Added, changes[3].Changed, changes[3].Removed, changes[3].Unchanged));
        Assert.All(changes, c => Assert.False(c.BaseMissing));

        // The full listing carries the CRC the diff compares.
        var folders = (await admin.GetFromJsonAsync<VersionFolderDto[]>($"/api/games/{game.GameId}/versions/{changes[0].VersionId}/folders"))!;
        Assert.All(folders.SelectMany(f => f.Files), f => Assert.NotNull(f.Crc32));

        // A machine key is not a console session: the admin route refuses it.
        using var asMachine = new HttpClient { BaseAddress = new Uri(_server.Url) };
        asMachine.DefaultRequestHeaders.Add("X-Api-Key", v.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await asMachine.GetAsync($"/api/games/{game.GameId}/versions/changes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/games/{Guid.NewGuid()}/versions/changes")).StatusCode);

        static (string, string, FileChange) Only(VersionChangesDto c)
        {
            var f = Assert.Single(c.Files);
            return (f.Key, f.Path, f.Change);
        }
    }

    [Fact]
    public async Task The_head_files_route_hashes_the_stored_archive_for_a_machine_key_only()
    {
        const string Earth = "EarthBound (USA)";
        Write(Saves("h"), Earth + ".srm", "head-sram");
        Write(States("h"), Earth + ".state", "head-state");
        var h = await Machine("h");
        var game = await Enroll(h, "h", "EarthBound");
        await using var engine = new SyncEngine(h, ApiClient.For(h));
        Assert.NotNull(await engine.PushAsync(game, force: true));

        var files = (await ApiClient.For(h).GetHeadFilesAsync(game.GameId))!;
        Assert.Equal(Reload(h, "EarthBound").LastKnownVersionId, files.Head?.Id);
        Assert.Equal(SaveArchive.HashFile(Path.Combine(Saves("h"), Earth + ".srm")),
            Assert.Single(files.Folders.Single(f => f.Key == "main").Files).Sha256);
        Assert.Equal(SaveArchive.HashFile(Path.Combine(States("h"), Earth + ".state")),
            Assert.Single(files.Folders.Single(f => f.Key == "states").Files).Sha256);

        using var anonymous = new HttpClient { BaseAddress = new Uri(_server.Url) };
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/agent/games/{game.GameId}/head/files")).StatusCode);
        Assert.Null(await ApiClient.For(h).GetHeadFilesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_file_tree_marks_each_file_against_the_head_and_folds_the_neighbours()
    {
        Write(Saves("a"), Chrono + ".srm", "a-sram");
        Write(States("a"), Chrono + ".state1", "a-slot1");
        Write(Saves("a"), Zelda + ".srm", "zelda-sram");
        Write(States("a"), Zelda + ".state", "zelda-state");
        Write(Saves("b"), Chrono + ".srm", "b-old");

        var a = await Machine("a");
        var b = await Machine("b");
        var gameA = await Enroll(a, "a");
        var gameB = await Enroll(b, "b");
        await using var engineA = new SyncEngine(a, ApiClient.For(a));
        await using var engineB = new SyncEngine(b, ApiClient.For(b));
        Assert.NotNull(await engineA.PushAsync(gameA, force: true));
        await engineB.PullAsync(gameB, force: true);

        async Task<GameFilesDto> Tree(AgentConfig config, Action<TrackedGame>? tweak = null)
        {
            var game = Reload(config);
            tweak?.Invoke(game);
            return await SaveFileTree.BuildAsync(config, game, ApiClient.For(config));
        }

        // Just pushed: every file of the game in sync; the other ROM's files folded under each folder.
        var tree = await Tree(a);
        Assert.True(tree.Reachable);
        Assert.Equal(new[] { "main", "states" }, tree.Folders.Select(f => f.Key));
        Assert.Equal(Saves("a"), tree.Folders[0].Path);
        Assert.All(Files(tree).Values, f => Assert.Equal(SaveFileTree.Same, f.State));
        Assert.Equal(new[] { (Zelda + ".srm", SaveFileTree.OtherGame) }, tree.Folders[0].Other.Select(o => (o.Path, o.Why)));
        Assert.Equal(new[] { (Zelda + ".state", SaveFileTree.OtherGame) }, tree.Folders[1].Other.Select(o => (o.Path, o.Why)));

        // A changes its save: that file, and only it, is a push.
        Write(Saves("a"), Chrono + ".srm", "a-sram-edited");
        tree = await Tree(a);
        Assert.Equal(SaveFileTree.Here, Files(tree)[("main", Chrono + ".srm")].State);
        Assert.Equal(SaveFileTree.Same, Files(tree)[("states", Chrono + ".state1")].State);

        // Back as it was; B then changes the save and adds a slot. A's untouched copy is the server's to update.
        Write(Saves("a"), Chrono + ".srm", "a-sram");
        Write(Saves("b"), Chrono + ".srm", "b-sram");
        Write(States("b"), Chrono + ".state2", "b-slot2");
        Assert.NotNull(await engineB.PushAsync(Reload(b)));
        tree = await Tree(a);
        Assert.Equal(SaveFileTree.Server, Files(tree)[("main", Chrono + ".srm")].State);
        var slot2 = Files(tree)[("states", Chrono + ".state2")];
        Assert.Equal((SaveFileTree.Server, true), (slot2.State, slot2.Missing));
        Assert.Equal(SaveFileTree.Same, Files(tree)[("states", Chrono + ".state1")].State);

        // A edits too: its own change is what a sync carries, never mistaken for the server's.
        Write(Saves("a"), Chrono + ".srm", "a-sram-again");
        Assert.Equal(SaveFileTree.Here, Files(await Tree(a))[("main", Chrono + ".srm")].State);

        // A pull makes everything agree again.
        Write(Saves("a"), Chrono + ".srm", "a-sram");
        await engineA.PullAsync(Reload(a));
        Assert.All(Files(await Tree(a)).Values, f => Assert.Equal(SaveFileTree.Same, f.State));

        // An exclude pattern turns a file of the game into an excluded one; a wider scope takes in a neighbour.
        Write(Saves("a"), Chrono + ".rtc", "clock");
        tree = await Tree(a, g => g.ExcludeGlobs = new() { "*.rtc" });
        Assert.Contains(tree.Folders[0].Other, o => o.Path == Chrono + ".rtc" && o.Why == SaveFileTree.Excluded);
        Assert.DoesNotContain(Files(tree).Keys, k => k.Path == Chrono + ".rtc");
        tree = await Tree(a, g => g.IncludeGlobs.Add(Zelda + ".srm"));
        Assert.Equal(SaveFileTree.Here, Files(tree)[("main", Zelda + ".srm")].State);
        Assert.DoesNotContain(tree.Folders[0].Other, o => o.Path == Zelda + ".srm");
    }

    [Fact]
    public async Task An_unreachable_server_still_lists_the_files_with_no_state()
    {
        const string Metroid = "Super Metroid (USA)";
        Write(Saves("u"), Metroid + ".srm", "u-sram");
        var u = await Machine("u");
        var game = await Enroll(u, "u", "Super Metroid");
        u.ServerUrl = "http://127.0.0.1:1";

        var tree = await SaveFileTree.BuildAsync(u, game, ApiClient.For(u));

        Assert.False(tree.Reachable);
        Assert.Null(tree.Head);
        var file = Assert.Single(Files(tree).Values);
        Assert.Equal((Metroid + ".srm", (string?)null), (file.Path, file.State));
    }
}
