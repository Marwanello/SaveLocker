using System.Net;
using System.Net.Http.Json;
using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/save-file-trees Phases 1 and 3 without a server: the CRC-32 the console's diff compares, the
/// diff itself.
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

}

/// <summary>
/// tasks/save-file-trees against a real server: the console's per-version changes (Phase 1) and the head's
/// file hashes an agent compares with, for a RetroArch game whose save and states folders other ROMs share.
/// </summary>
public sealed class SaveFileTreeServerTests : IClassFixture<ServerProcess>, IDisposable
{
    private const string Chrono = "Chrono Trigger (USA)";
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
}
