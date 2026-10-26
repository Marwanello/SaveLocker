using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveLocker.Agent;

/// <summary>Which playthrough copies this device keeps (tasks/save-playthroughs Phase 5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlaythroughCopyMode>))]
public enum PlaythroughCopyMode
{
    /// <summary>The outgoing save at a switch, until the server confirms its push. Costs nothing afterwards.</summary>
    UntilUploaded,
    /// <summary>The latest save of every playthrough this device has played, so a switch works with no server.</summary>
    KeepForOffline,
    /// <summary>Nothing. A switch then needs the server.</summary>
    Off
}

/// <summary>One file of a stored save: the zip entry it came from, so the archive can be rebuilt exactly.</summary>
public sealed record StoredFile(string Name, long Size, string Sha256, DateTimeOffset LastWriteTime, string? Comment);

/// <summary>
/// One playthrough's save as this device kept it. <see cref="VersionId"/> is the server version it is based on: the parent
/// of a push that has not happened yet (<see cref="Uploaded"/> false), or the version the push made.
/// </summary>
public sealed class StoredSnapshot
{
    public Guid GameId { get; set; }
    public Guid PlaythroughId { get; set; }
    public string? PlaythroughName { get; set; }
    public Guid? VersionId { get; set; }
    public string ContentHash { get; set; } = "";
    public DateTime CapturedAt { get; set; }
    public bool Uploaded { get; set; }
    public List<StoredFile> Files { get; set; } = new();
    [JsonIgnore] public long TotalSize => Files.Sum(f => f.Size);
}

/// <summary>A stored file whose bytes are gone or no longer hash to what the snapshot says. Nothing was restored.</summary>
public sealed class StoredCopyCorruptException(string message) : Exception(message);

/// <summary>
/// The local copies of playthrough saves (tasks/save-playthroughs Phase 5), deduplicated: <c>blobs/{ab}/{sha256}</c> holds each
/// distinct file once, and <c>snapshots/{game}/{playthrough}.json</c> lists the files of one playthrough's save. A playthrough
/// forked from Main shares every blob with it until it diverges.
/// <para>
/// <b>Write order is the whole safety story.</b> A blob goes to a temp name, is flushed to disk and renamed; the snapshot is
/// written last, atomically. So a snapshot never names a blob that is not on disk, and a crash leaves at worst blobs nothing
/// names, which <see cref="Sweep"/> removes once they are old enough that no write in progress can still be about to name them.
/// <b>Restore verifies every file against the snapshot</b> while it rebuilds the archive, and aborts without touching the save
/// folder on any mismatch.
/// </para>
/// <para>
/// One owner per machine writes here (the daemon on Linux, the tray on Windows), so no reference counts are kept; a
/// cross-process lock only keeps a one-shot CLI command from sweeping while another writes.
/// </para>
/// </summary>
public sealed class PlaythroughStore
{
    /// <summary>A blob younger than this is never swept, so a capture still writing its snapshot cannot lose one.</summary>
    public static readonly TimeSpan SweepGrace = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _stateDir;
    private readonly string _root;

    public PlaythroughStore(string stateDir)
    {
        _stateDir = stateDir;
        _root = Path.Combine(stateDir, "playthrough-store");
    }

    public static PlaythroughStore For(AgentConfig config) => new(config.StateDir);

    private string BlobPath(string sha256) => Path.Combine(_root, "blobs", sha256[..2], sha256);
    private string SnapshotDir(Guid gameId) => Path.Combine(_root, "snapshots", gameId.ToString("N"));
    private string SnapshotPath(Guid gameId, Guid playthroughId) =>
        Path.Combine(SnapshotDir(gameId), playthroughId.ToString("N") + ".json");

    /// <summary>
    /// Keeps the save in <paramref name="archiveZip"/> (an archive of the save folder, as a push builds it) as this playthrough's
    /// copy, replacing the one before. Every file is hashed and stored; the snapshot is written last.
    /// <para>
    /// A copy that replaces one the server never got stays not uploaded, whatever <paramref name="uploaded"/> says: the folder
    /// was restored from that copy (an offline switch back to it), so "unchanged since the last sync" means unchanged since
    /// that restore, and the save it holds still has to reach the server.
    /// </para>
    /// </summary>
    public StoredSnapshot Capture(Guid gameId, Guid playthroughId, string? playthroughName, string archiveZip,
        Guid? versionId, string contentHash, bool uploaded)
    {
        using var guard = AgentStateLock.Acquire("playthrough-store", _stateDir);
        if (Find(gameId, playthroughId) is { Uploaded: false }) uploaded = false;
        var snapshot = new StoredSnapshot
        {
            GameId = gameId, PlaythroughId = playthroughId, PlaythroughName = playthroughName, VersionId = versionId,
            ContentHash = contentHash, CapturedAt = DateTime.UtcNow, Uploaded = uploaded,
        };
        using (var zip = ZipFile.OpenRead(archiveZip))
        {
            foreach (var entry in zip.Entries)
            {
                using var input = entry.Open();
                var (sha, size) = StoreBlob(input);
                snapshot.Files.Add(new StoredFile(entry.FullName, size, sha, entry.LastWriteTime, entry.Comment));
            }
        }
        Directory.CreateDirectory(SnapshotDir(gameId));
        AtomicFile.WriteAllText(SnapshotPath(gameId, playthroughId), JsonSerializer.Serialize(snapshot, Json));
        return snapshot;
    }

    /// <summary>Streams <paramref name="input"/> into the blob store and returns its hash and size. Already stored: only hashed.</summary>
    private (string Sha256, long Size) StoreBlob(Stream input)
    {
        var tmpDir = Path.Combine(_root, "blobs", "tmp");
        Directory.CreateDirectory(tmpDir);
        var tmp = Path.Combine(tmpDir, $"{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
        try
        {
            using var hasher = SHA256.Create();
            long size = 0;
            using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hasher.TransformBlock(buffer, 0, read, null, 0);
                    output.Write(buffer, 0, read);
                    size += read;
                }
                hasher.TransformFinalBlock([], 0, 0);
                output.Flush(flushToDisk: true);
            }
            var sha = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
            var final = BlobPath(sha);
            if (File.Exists(final))
            {
                // Touched, so the sweep's grace period counts from the last time something wanted it.
                File.SetLastWriteTimeUtc(final, DateTime.UtcNow);
                return (sha, size);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(final)!);
            try { File.Move(tmp, final); }
            catch (IOException) when (File.Exists(final)) { /* another writer stored the same bytes first */ }
            return (sha, size);
        }
        finally { try { File.Delete(tmp); } catch { /* already moved, or not ours to worry about */ } }
    }

    public StoredSnapshot? Find(Guid gameId, Guid playthroughId)
    {
        var path = SnapshotPath(gameId, playthroughId);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<StoredSnapshot>(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }

    /// <summary>Every snapshot, optionally of one game.</summary>
    public IReadOnlyList<StoredSnapshot> All(Guid? gameId = null) => ReadAll(gameId, out _);

    /// <param name="complete">False when a snapshot file could not be read (held by a scanner, say): the list may be missing a copy.</param>
    private List<StoredSnapshot> ReadAll(Guid? gameId, out bool complete)
    {
        complete = true;
        var dir = Path.Combine(_root, "snapshots");
        if (!Directory.Exists(dir)) return [];
        var result = new List<StoredSnapshot>();
        var gameDirs = gameId is { } id ? [SnapshotDir(id)] : Directory.EnumerateDirectories(dir);
        foreach (var gameDir in gameDirs)
        {
            if (!Directory.Exists(gameDir)) continue;
            foreach (var file in Directory.EnumerateFiles(gameDir, "*.json"))
            {
                try { if (JsonSerializer.Deserialize<StoredSnapshot>(File.ReadAllText(file)) is { } s) result.Add(s); }
                catch (JsonException) { /* torn or foreign file: not a snapshot */ }
                catch (IOException) { complete = false; }
            }
        }
        return result;
    }

    /// <summary>The blobs every snapshot names, or null when a snapshot could not be read: a sweep must then not run, or it would
    /// delete the files of a copy it merely failed to see — possibly the only copy of a save the server never got.</summary>
    private HashSet<string>? NamedBlobs()
    {
        var all = ReadAll(null, out var complete);
        return complete ? all.SelectMany(s => s.Files).Select(f => f.Sha256.ToLowerInvariant()).ToHashSet() : null;
    }

    /// <summary>Copies that were never uploaded: the only copy of that save this side of the server.</summary>
    public IReadOnlyList<StoredSnapshot> PendingUploads(Guid? gameId = null) => All(gameId).Where(s => !s.Uploaded).ToList();

    /// <summary>Marks a copy as uploaded as <paramref name="versionId"/> — the server now holds it.</summary>
    public void MarkUploaded(Guid gameId, Guid playthroughId, Guid? versionId)
    {
        using var guard = AgentStateLock.Acquire("playthrough-store", _stateDir);
        if (Find(gameId, playthroughId) is not { } snapshot) return;
        snapshot.Uploaded = true;
        snapshot.VersionId = versionId ?? snapshot.VersionId;
        AtomicFile.WriteAllText(SnapshotPath(gameId, playthroughId), JsonSerializer.Serialize(snapshot, Json));
    }

    public void Remove(Guid gameId, Guid playthroughId)
    {
        using var guard = AgentStateLock.Acquire("playthrough-store", _stateDir);
        try { File.Delete(SnapshotPath(gameId, playthroughId)); } catch (IOException) { /* in use; the next sweep tries again */ }
    }

    /// <summary>
    /// Rebuilds the archive a snapshot was made from at <paramref name="destinationZip"/>, checking every file's hash first-hand
    /// as it is copied. A missing blob or a mismatch throws <see cref="StoredCopyCorruptException"/> and leaves no archive, so
    /// nothing downstream can restore half a save.
    /// </summary>
    public void BuildArchive(StoredSnapshot snapshot, string destinationZip)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationZip)!);
        try
        {
            using (var fs = new FileStream(destinationZip, FileMode.Create, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var file in snapshot.Files)
                {
                    var blob = BlobPath(file.Sha256);
                    if (!File.Exists(blob))
                        throw new StoredCopyCorruptException($"the stored copy is missing a file ({file.Name}).");
                    var entry = zip.CreateEntry(file.Name, CompressionLevel.Optimal);
                    entry.LastWriteTime = file.LastWriteTime;
                    if (file.Comment is not null) entry.Comment = file.Comment;
                    using var input = File.OpenRead(blob);
                    using var output = entry.Open();
                    using var hasher = SHA256.Create();
                    var buffer = new byte[81920];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        hasher.TransformBlock(buffer, 0, read, null, 0);
                        output.Write(buffer, 0, read);
                    }
                    hasher.TransformFinalBlock([], 0, 0);
                    if (!string.Equals(Convert.ToHexString(hasher.Hash!), file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new StoredCopyCorruptException($"the stored copy of {file.Name} does not match what was saved.");
                }
            }
        }
        catch
        {
            try { File.Delete(destinationZip); } catch { /* nothing more to do */ }
            throw;
        }
    }

    /// <summary>
    /// Deletes every blob no snapshot names, once it is older than <see cref="SweepGrace"/>; returns how many. Mark and sweep:
    /// the marks are read fresh, so a snapshot written a moment ago is honoured, and a blob a capture wrote moments ago is too old
    /// to be touched.
    /// </summary>
    public int Sweep()
    {
        var blobs = Path.Combine(_root, "blobs");
        if (!Directory.Exists(blobs)) return 0;   // nothing was ever kept: no lock file for a device that never switched
        using var guard = AgentStateLock.Acquire("playthrough-store", _stateDir);
        if (NamedBlobs() is not { } named) return 0;
        var cutoff = DateTime.UtcNow - SweepGrace;
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(blobs))
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var name = Path.GetFileName(file);
                var temp = Path.GetFileName(dir) == "tmp";
                if (!temp && named.Contains(name.ToLowerInvariant())) continue;
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                    File.Delete(file);
                    removed++;
                }
                catch (IOException) { /* in use; the next sweep tries again */ }
            }
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (IOException) { }
        }
        return removed;
    }

    /// <summary>Bytes the store takes on disk (blobs and snapshots).</summary>
    public long UsedBytes()
    {
        if (!Directory.Exists(_root)) return 0;
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; } catch (IOException) { }
        }
        return total;
    }

    /// <summary>
    /// Removes every copy except the ones never uploaded, which are the only copy of a save; returns the names of those, so a
    /// screen can say they were kept. Their blobs stay because their snapshots still name them.
    /// </summary>
    public IReadOnlyList<StoredSnapshot> Clear()
    {
        using (var guard = AgentStateLock.Acquire("playthrough-store", _stateDir))
        {
            foreach (var snapshot in All().Where(s => s.Uploaded))
                try { File.Delete(SnapshotPath(snapshot.GameId, snapshot.PlaythroughId)); } catch (IOException) { }
        }
        // Just-cleared blobs are younger than the grace period, so a clear sweeps with none: nothing is writing in a clear.
        SweepNow();
        return PendingUploads();
    }

    private void SweepNow()
    {
        using var guard = AgentStateLock.Acquire("playthrough-store", _stateDir);
        var blobs = Path.Combine(_root, "blobs");
        if (!Directory.Exists(blobs)) return;
        if (NamedBlobs() is not { } named) return;
        foreach (var file in Directory.EnumerateFiles(blobs, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(Path.GetDirectoryName(file)) == "tmp" || named.Contains(Path.GetFileName(file).ToLowerInvariant())) continue;
            try { File.Delete(file); } catch (IOException) { }
        }
    }
}
