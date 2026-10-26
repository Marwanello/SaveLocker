using System.IO.Compression;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The local copy store of playthroughs (tasks/save-playthroughs Phase 5): a deduplicated blob store plus one snapshot per
/// game and playthrough. What matters is that it never restores something it cannot vouch for, never loses the only copy of a
/// save, and shares a file two playthroughs have in common.
/// </summary>
public sealed class PlaythroughStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-ptstore-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _game = Guid.NewGuid();
    private readonly Guid _main = Guid.NewGuid();
    private readonly Guid _hard = Guid.NewGuid();

    public PlaythroughStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private PlaythroughStore NewStore() => new(Path.Combine(_dir, "state"));

    private string MakeArchive(string name, params (string Path, string Text)[] files)
    {
        var path = Path.Combine(_dir, name + ".zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (entryName, text) in files)
            {
                var entry = zip.CreateEntry(entryName);
                entry.LastWriteTime = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
                entry.Comment = "mtime-utc=2026-10-01T12:00:00.0000000Z";
                using var w = new StreamWriter(entry.Open());
                w.Write(text);
            }
        }
        return path;
    }

    private static Dictionary<string, string> ReadArchive(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); });
    }

    private int BlobCount() =>
        Directory.Exists(Path.Combine(_dir, "state", "playthrough-store", "blobs"))
            ? Directory.EnumerateFiles(Path.Combine(_dir, "state", "playthrough-store", "blobs"), "*", SearchOption.AllDirectories)
                .Count(f => Path.GetFileName(Path.GetDirectoryName(f)) != "tmp")
            : 0;

    [Fact]
    public void A_captured_save_rebuilds_into_the_same_archive_with_its_times_and_comments()
    {
        var store = NewStore();
        var source = MakeArchive("a", ("slot1.sav", "level 12"), ("sub/profile.ini", "name=Sara"));
        var version = Guid.NewGuid();
        store.Capture(_game, _main, "Main", source, version, "hash-a", uploaded: false);

        var found = store.Find(_game, _main);
        Assert.NotNull(found);
        Assert.Equal(version, found!.VersionId);
        Assert.False(found.Uploaded);

        var rebuilt = Path.Combine(_dir, "rebuilt.zip");
        store.BuildArchive(found, rebuilt);
        Assert.Equal(ReadArchive(source), ReadArchive(rebuilt));
        using var was = ZipFile.OpenRead(source);
        using var now = ZipFile.OpenRead(rebuilt);
        Assert.All(now.Entries, e => Assert.Equal("mtime-utc=2026-10-01T12:00:00.0000000Z", e.Comment));
        // A zip keeps a wall-clock time, so the instant is compared as the source archive reads back, not as it was written.
        Assert.All(now.Entries, e => Assert.Equal(was.GetEntry(e.FullName)!.LastWriteTime, e.LastWriteTime));
    }

    [Fact]
    public void A_file_two_playthroughs_share_is_stored_once()
    {
        var store = NewStore();
        store.Capture(_game, _main, "Main", MakeArchive("m", ("slot1.sav", "same bytes"), ("slot2.sav", "main only")), null, "h1", true);
        store.Capture(_game, _hard, "Hard", MakeArchive("h", ("slot1.sav", "same bytes"), ("slot2.sav", "hard only")), null, "h2", true);

        Assert.Equal(3, BlobCount());
    }

    [Fact]
    public void A_stored_file_that_no_longer_matches_aborts_the_rebuild_and_leaves_no_archive()
    {
        var store = NewStore();
        var snapshot = store.Capture(_game, _main, "Main", MakeArchive("a", ("slot1.sav", "intact")), null, "h", true);
        var blob = Directory.EnumerateFiles(Path.Combine(_dir, "state", "playthrough-store", "blobs"), "*", SearchOption.AllDirectories)
            .Single(f => Path.GetFileName(Path.GetDirectoryName(f)) != "tmp");
        File.WriteAllText(blob, "tampered");

        var dest = Path.Combine(_dir, "out.zip");
        Assert.Throws<StoredCopyCorruptException>(() => store.BuildArchive(snapshot, dest));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void A_missing_blob_aborts_the_rebuild()
    {
        var store = NewStore();
        var snapshot = store.Capture(_game, _main, "Main", MakeArchive("a", ("slot1.sav", "x")), null, "h", true);
        foreach (var f in Directory.EnumerateFiles(Path.Combine(_dir, "state", "playthrough-store", "blobs"), "*", SearchOption.AllDirectories))
            File.Delete(f);

        var dest = Path.Combine(_dir, "out.zip");
        Assert.Throws<StoredCopyCorruptException>(() => store.BuildArchive(snapshot, dest));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public void The_sweep_keeps_what_a_snapshot_names_and_what_is_too_young_to_judge()
    {
        var store = NewStore();
        store.Capture(_game, _main, "Main", MakeArchive("a", ("slot1.sav", "kept")), null, "h", true);
        var blobs = Path.Combine(_dir, "state", "playthrough-store", "blobs");
        Directory.CreateDirectory(Path.Combine(blobs, "ab"));
        var oldOrphan = Path.Combine(blobs, "ab", new string('a', 64));
        var youngOrphan = Path.Combine(blobs, "ab", new string('b', 64));
        File.WriteAllText(oldOrphan, "old");
        File.WriteAllText(youngOrphan, "young");
        File.SetLastWriteTimeUtc(oldOrphan, DateTime.UtcNow - PlaythroughStore.SweepGrace - TimeSpan.FromMinutes(5));

        var removed = store.Sweep();

        Assert.Equal(1, removed);
        Assert.False(File.Exists(oldOrphan));
        Assert.True(File.Exists(youngOrphan));
        Assert.Equal(2, BlobCount());   // the named blob and the young orphan
    }

    [Fact]
    public void Clearing_keeps_a_save_that_was_never_uploaded()
    {
        var store = NewStore();
        store.Capture(_game, _main, "Main", MakeArchive("m", ("a.sav", "uploaded")), null, "h1", uploaded: true);
        store.Capture(_game, _hard, "Hard", MakeArchive("h", ("b.sav", "only copy")), null, "h2", uploaded: false);

        var kept = store.Clear();

        var single = Assert.Single(kept);
        Assert.Equal(_hard, single.PlaythroughId);
        Assert.Null(store.Find(_game, _main));
        Assert.NotNull(store.Find(_game, _hard));
        var dest = Path.Combine(_dir, "out.zip");
        store.BuildArchive(store.Find(_game, _hard)!, dest);   // its bytes survived the clear
        Assert.Equal("only copy", ReadArchive(dest)["b.sav"]);
    }

    [Fact]
    public void Marking_a_copy_uploaded_records_the_version_the_server_made()
    {
        var store = NewStore();
        store.Capture(_game, _main, "Main", MakeArchive("a", ("a.sav", "x")), Guid.NewGuid(), "h", uploaded: false);
        var made = Guid.NewGuid();

        store.MarkUploaded(_game, _main, made);

        var found = store.Find(_game, _main)!;
        Assert.True(found.Uploaded);
        Assert.Equal(made, found.VersionId);
        Assert.Empty(store.PendingUploads());
    }

    [Theory]
    [InlineData("profile.dat", true)]
    [InlineData("Saves/Settings.xml", true)]
    [InlineData("game.ini", true)]
    [InlineData("controls.CFG", true)]
    [InlineData("slot1.sav", false)]
    [InlineData("Saves/autosave_3.dat", false)]
    public void Start_fresh_ticks_profile_and_settings_files_only(string name, bool ticked) =>
        Assert.Equal(ticked, AgentApiServer.LooksLikeSettingsFile(name));
}
