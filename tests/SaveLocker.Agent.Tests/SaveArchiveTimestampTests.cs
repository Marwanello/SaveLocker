using System.IO.Compression;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// A version's "newest change" (<see cref="SaveArchive.GetArchiveStats"/>) must be the real UTC instant the
/// file was written. Zip's own timestamp has no zone — it is the uploader's local wall clock — so read as UTC
/// it came out shifted by the uploader's offset (+3 h on the UTC+3 box it was found on, 2026-09-28). On a
/// machine at UTC+0 the old code happened to be right, so the first test only fails before the fix off UTC.
/// </summary>
public sealed class SaveArchiveTimestampTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sl-ts-" + Guid.NewGuid().ToString("N"));

    public SaveArchiveTimestampTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static readonly DateTime Written = new(2026, 9, 28, 7, 38, 12, DateTimeKind.Utc);

    private string SaveWith(params (string Rel, DateTime WrittenUtc)[] files)
    {
        var dir = Path.Combine(_root, "save");
        foreach (var (rel, at) in files)
        {
            var path = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "data " + rel);
            File.SetLastWriteTimeUtc(path, at);
        }
        return dir;
    }

    [Fact]
    public void Newest_change_is_the_real_utc_write_time_whatever_the_uploaders_timezone()
    {
        var zip = Path.Combine(_root, "v.zip");
        SaveArchive.CreateArchive(SaveWith(("slot1.sav", Written), ("sub/slot2.sav", Written.AddHours(-2))), zip);

        var stats = SaveArchive.GetArchiveStats(zip);

        Assert.Equal(2, stats.FileCount);
        Assert.Equal(DateTimeKind.Utc, stats.NewestFileWriteUtc!.Value.Kind);
        Assert.Equal(Written, stats.NewestFileWriteUtc);
    }

    [Fact]
    public void The_zip_timestamp_other_tools_show_stays_the_local_wall_clock()
    {
        var zip = Path.Combine(_root, "v.zip");
        SaveArchive.CreateArchive(SaveWith(("slot1.sav", Written)), zip);

        using var archive = ZipFile.OpenRead(zip);
        var local = Written.ToLocalTime();
        var stamped = archive.Entries.Single().LastWriteTime.DateTime;
        // DOS time has two-second resolution.
        Assert.InRange((stamped - local).TotalSeconds, -2, 2);
    }

    [Fact]
    public void A_delta_subset_carries_the_same_utc_time()
    {
        var dir = SaveWith(("a.sav", Written), ("b.sav", Written.AddMinutes(-5)));
        var zip = Path.Combine(_root, "delta.zip");
        SaveArchive.CreateArchiveSubset(dir, zip, ["b.sav"]);

        Assert.Equal(Written.AddMinutes(-5), SaveArchive.GetArchiveStats(zip).NewestFileWriteUtc);
    }

    [Fact]
    public void Copying_an_entry_keeps_its_utc_time()
    {
        var src = Path.Combine(_root, "src.zip");
        SaveArchive.CreateArchive(SaveWith(("slot1.sav", Written)), src);

        var dst = Path.Combine(_root, "dst.zip");
        using (var from = ZipFile.OpenRead(src))
        using (var to = ZipFile.Open(dst, ZipArchiveMode.Create))
        {
            var e = from.Entries.Single();
            var copy = to.CreateEntry(e.FullName);
            SaveArchive.CopyWriteTime(e, copy);
            using var s = e.Open();
            using var d = copy.Open();
            s.CopyTo(d);
        }

        Assert.Equal(Written, SaveArchive.GetArchiveStats(dst).NewestFileWriteUtc);
    }

    // An archive written before the fix has no UTC record, only the uploader's wall clock, and nothing in it
    // says which timezone that was. It keeps reading as it always did: the wall clock, labelled UTC.
    [Fact]
    public void An_archive_from_before_the_fix_reads_as_it_always_did()
    {
        var zip = Path.Combine(_root, "old.zip");
        var wallClock = new DateTime(2026, 9, 28, 10, 38, 12);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("slot1.sav");
            e.LastWriteTime = wallClock;
            using var w = new StreamWriter(e.Open());
            w.Write("old");
        }

        var newest = SaveArchive.GetArchiveStats(zip).NewestFileWriteUtc!.Value;
        Assert.Equal(DateTimeKind.Utc, newest.Kind);
        Assert.Equal(wallClock, DateTime.SpecifyKind(newest, DateTimeKind.Unspecified));
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("mtime-utc=not a date")]
    [InlineData("mtime-utc=2026-09-28T07:38:12")]
    public void A_comment_that_is_not_a_utc_record_is_ignored(string comment)
    {
        var zip = Path.Combine(_root, "odd.zip");
        var wallClock = new DateTime(2026, 9, 28, 10, 38, 12);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var e = archive.CreateEntry("slot1.sav");
            e.LastWriteTime = wallClock;
            e.Comment = comment;
            using var w = new StreamWriter(e.Open());
            w.Write("x");
        }

        Assert.Equal(wallClock, DateTime.SpecifyKind(SaveArchive.GetArchiveStats(zip).NewestFileWriteUtc!.Value, DateTimeKind.Unspecified));
    }

    [Fact]
    public void The_content_hash_ignores_timestamps()
    {
        var dir = SaveWith(("slot1.sav", Written));
        var before = SaveArchive.HashDirectory(dir);
        File.SetLastWriteTimeUtc(Path.Combine(dir, "slot1.sav"), Written.AddDays(-30));

        Assert.Equal(before, SaveArchive.HashDirectory(dir));
        Assert.Equal(before, SaveArchive.ComputeManifest(dir).ContentHash);
    }
}
