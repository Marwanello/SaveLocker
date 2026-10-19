using System.IO.Compression;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// tasks/emulator-saves Phase 17: a machine's own names for a game's files (<see cref="FileNames"/>) — two dumps of one ROM,
/// <c>Chrono Trigger (USA).srm</c> on the server and <c>Chrono Trigger (USA) (Rev 1).srm</c> here — swapped on the way into
/// and out of every archive, so the server, the hash and the manifest only see the server's names.
/// </summary>
public sealed class FileNamesTests : IDisposable
{
    private const string Server = "Chrono Trigger (USA)", Local = "Chrono Trigger (USA) (Rev 1)";
    private static readonly FileNameDto[] Pairs = [new(SaveRoot.PrimaryKey, Server + ".srm", Local + ".srm")];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-names-" + Guid.NewGuid().ToString("N"));

    public FileNamesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Write(string rel, string content)
    {
        var path = Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Listed_names_swap_both_ways_and_new_slots_take_the_saves_swap()
    {
        var main = FileNames.For(Pairs, SaveRoot.PrimaryKey)!;
        Assert.Equal(Server + ".srm", main.ToServer(Local + ".srm"));
        Assert.Equal(Local + ".srm", main.ToLocal(Server + ".srm"));
        Assert.Equal(Server + ".rtc", main.ToServer(Local + ".rtc"));

        // The states folder has no pair of its own: it takes the save's.
        var states = FileNames.For(Pairs, RomSaves.StatesKey)!;
        Assert.Equal(Server + ".state3", states.ToServer(Local + ".state3"));
        Assert.Equal(Server + ".state.auto", states.ToServer(Local + ".state.auto"));
        Assert.Equal(Local + ".state1.png", states.ToLocal(Server + ".state1.png"));

        // Another game's file, and a name that only starts with the stem, are left as they are.
        Assert.Equal("Zelda (USA).srm", main.ToServer("Zelda (USA).srm"));
        Assert.Equal(Local + " Extra.srm", main.ToServer(Local + " Extra.srm"));
    }

    [Fact]
    public void A_file_here_under_the_servers_name_is_not_this_games()
    {
        var main = FileNames.For(Pairs, SaveRoot.PrimaryKey)!;
        Assert.Null(main.ToServer(Server + ".srm"));
    }

    [Fact]
    public void Another_dumps_state_under_the_swaps_server_name_is_not_this_games()
    {
        // The USA dump's state beside the Rev 1 dump's: both would be "… (USA).state1" on the server.
        var states = FileNames.For(Pairs, RomSaves.StatesKey)!;
        Assert.Equal(Server + ".state1", states.ToServer(Local + ".state1"));
        Assert.Null(states.ToServer(Server + ".state1"));
        Assert.Empty(FileNames.Derive(Pairs, RomSaves.StatesKey, [Server + ".state1"]));

        var dir = Path.Combine(_root, "states");
        Write($"states/{Local}.state1", "rev1-slot");
        Write($"states/{Server}.state1", "usa-slot");
        Write($"states/{Server}.state2", "usa-slot2");
        Write($"saves/{Local}.srm", "rev1-save");
        var roots = new[]
        {
            new SaveRoot(SaveRoot.PrimaryKey, Path.Combine(_root, "saves"), [Server + ".srm"], FileNames.For(Pairs, SaveRoot.PrimaryKey)),
            new SaveRoot(RomSaves.StatesKey, dir, [Server + ".state*"], states),
        };
        var zip = Path.Combine(_root, "states.zip");
        SaveArchive.CreateArchive(roots, zip);
        using (var archive = ZipFile.OpenRead(zip))
        {
            var entry = Assert.Single(archive.Entries, e => e.Name == Server + ".state1");
            using var reader = new StreamReader(entry.Open());
            Assert.Equal("rev1-slot", reader.ReadToEnd());
            Assert.DoesNotContain(archive.Entries, e => e.Name == Server + ".state2");
        }

        // A restore of that archive leaves the other dump's states alone, the slot the server lacks too.
        SaveArchive.RestoreArchive(zip, roots, Path.Combine(_root, "staging"));
        Assert.Equal("usa-slot", File.ReadAllText(Path.Combine(dir, Server + ".state1")));
        Assert.Equal("usa-slot2", File.ReadAllText(Path.Combine(dir, Server + ".state2")));
        Assert.Equal("rev1-slot", File.ReadAllText(Path.Combine(dir, Local + ".state1")));
    }

    [Fact]
    public void Derive_lists_the_files_the_swap_reaches_once()
    {
        var added = FileNames.Derive(Pairs, RomSaves.StatesKey, [Local + ".state3", Local + ".state3.png", "Zelda.state"]);
        Assert.Equal([new FileNameDto(RomSaves.StatesKey, Server + ".state3", Local + ".state3"),
                      new FileNameDto(RomSaves.StatesKey, Server + ".state3.png", Local + ".state3.png")], added);
        Assert.Empty(FileNames.Derive([.. Pairs, .. added], RomSaves.StatesKey, [Local + ".state3"]));
    }

    [Theory]
    [InlineData("../x.srm")]
    [InlineData("/abs.srm")]
    [InlineData("C:x.srm")]
    [InlineData("a\\b.srm")]
    [InlineData("")]
    public void Names_must_be_paths_inside_the_folder(string bad) =>
        Assert.NotNull(FileNames.Validate([new FileNameDto(SaveRoot.PrimaryKey, bad, "ok.srm")]));

    [Fact]
    public void Two_files_on_one_name_are_refused()
    {
        Assert.NotNull(FileNames.Validate([new(SaveRoot.PrimaryKey, "a.srm", "x.srm"), new(SaveRoot.PrimaryKey, "a.srm", "y.srm")]));
        Assert.NotNull(FileNames.Validate([new(SaveRoot.PrimaryKey, "a.srm", "x.srm"), new(SaveRoot.PrimaryKey, "b.srm", "x.srm")]));
        Assert.Null(FileNames.Validate(Pairs));
    }

    [Fact]
    public void Two_machines_with_different_names_hash_the_same_and_archive_the_servers_names()
    {
        var pc = Path.Combine(_root, "pc");
        var deck = Path.Combine(_root, "deck");
        Write($"pc/{Server}.srm", "save");
        Write($"deck/{Local}.srm", "save");
        Write("deck/Zelda (USA).srm", "zelda");
        var pcRoots = new[] { SaveRoot.Primary(pc, [Server + ".srm", Server + ".rtc"]) };
        var deckRoots = new[] { new SaveRoot(SaveRoot.PrimaryKey, deck, [Server + ".srm", Server + ".rtc"], FileNames.For(Pairs, SaveRoot.PrimaryKey)) };

        Assert.Equal(SaveArchive.HashDirectory(pcRoots), SaveArchive.HashDirectory(deckRoots));
        Assert.Equal(SaveArchive.ComputeManifest(pcRoots).Files.Select(f => f.Path), SaveArchive.ComputeManifest(deckRoots).Files.Select(f => f.Path));

        var zip = Path.Combine(_root, "deck.zip");
        SaveArchive.CreateArchive(deckRoots, zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal([Server + ".srm"], archive.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void A_restore_writes_this_machines_names_and_leaves_other_games_alone()
    {
        var pc = Path.Combine(_root, "pc", "saves");
        var deck = Path.Combine(_root, "deck", "saves");
        Write($"pc/saves/{Server}.srm", "pc-save");
        Write($"pc/states/{Server}.state1", "pc-slot1");
        var pcRoots = new[]
        {
            SaveRoot.Primary(pc, [Server + ".srm"]),
            new SaveRoot(RomSaves.StatesKey, Path.Combine(_root, "pc", "states"), [Server + ".state*"]),
        };
        var zip = Path.Combine(_root, "pc.zip");
        SaveArchive.CreateArchive(pcRoots, zip);

        Write($"deck/saves/{Local}.srm", "deck-save");
        Write($"deck/states/{Local}.state2", "deck-slot2");
        Write("deck/states/Zelda (USA).state1", "zelda-slot");
        var deckRoots = new[]
        {
            new SaveRoot(SaveRoot.PrimaryKey, deck, [Server + ".srm"], FileNames.For(Pairs, SaveRoot.PrimaryKey)),
            new SaveRoot(RomSaves.StatesKey, Path.Combine(_root, "deck", "states"), [Server + ".state*"], FileNames.For(Pairs, RomSaves.StatesKey)),
        };
        SaveArchive.RestoreArchive(zip, deckRoots, Path.Combine(_root, "staging"));

        Assert.Equal("pc-save", File.ReadAllText(Path.Combine(deck, Local + ".srm")));
        Assert.Equal("pc-slot1", File.ReadAllText(Path.Combine(_root, "deck", "states", Local + ".state1")));
        Assert.False(File.Exists(Path.Combine(deck, Server + ".srm")));
        // The slot the server's save does not have goes, as any in-scope file would; another game's state stays.
        Assert.False(File.Exists(Path.Combine(_root, "deck", "states", Local + ".state2")));
        Assert.Equal("zelda-slot", File.ReadAllText(Path.Combine(_root, "deck", "states", "Zelda (USA).state1")));
        Assert.Equal(SaveArchive.HashDirectory(pcRoots), SaveArchive.HashDirectory(deckRoots));
    }
}
