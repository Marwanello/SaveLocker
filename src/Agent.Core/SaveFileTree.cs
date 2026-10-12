using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// The save files of one game on this machine right now, file by file against the server's head
/// (tasks/save-file-trees Phase 3) — what the agent UI's "Save files on this PC" tree draws. Reads and
/// hashes every file, the same cost as a push's own hash, so it is asked for on demand, never on a timer.
/// </summary>
public static class SaveFileTree
{
    public const string Same = "same";
    /// <summary>A sync would push it: changed or new here, or deleted here (<see cref="LocalFileDto.Missing"/>).</summary>
    public const string Here = "here";
    /// <summary>A sync would pull it: only on the server, or changed (or removed) there while this copy
    /// was not touched.</summary>
    public const string Server = "server";

    public const string OtherGame = "otherGame";
    public const string Excluded = "excluded";

    /// <summary>How many of a folder's other files are listed; the rest are only counted.</summary>
    public const int MaxOther = 200;

    public static async Task<GameFilesDto> BuildAsync(AgentConfig config, TrackedGame game, ApiClient api,
        CancellationToken ct = default)
    {
        // Every folder, shadows included, so the content hash is the one a push compares; only the real
        // ones are shown — a shadow is SaveLocker's own copy, not a folder of this PC.
        var roots = game.Roots(config.StateDir);
        var real = game.RealRoots(config.StateDir);
        var (manifest, contentHash, onDisk, unsynced) = await Task.Run(() =>
        {
            var (files, hash) = SaveArchive.ComputeManifest(roots, game.ExcludeGlobs);
            var paths = SaveArchive.ListSaveFiles(roots, game.ExcludeGlobs)
                .ToDictionary(f => f.ArchiveName, f => f.FullPath, StringComparer.Ordinal);
            return (files, hash, paths, SaveArchive.ListUnsyncedFiles(real, game.ExcludeGlobs));
        }, ct);

        // A 404 is a server older than this route (or one that no longer has the game): nothing to compare
        // with, which is not the same as an empty head — that would call every file here a push.
        HeadFilesDto? head = null;
        try { head = await api.GetHeadFilesAsync(game.GameId, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { }

        return Compare(game, real, manifest, contentHash, onDisk, unsynced, head);
    }

    /// <summary>
    /// The comparison itself, apart from the disk and the network. <paramref name="head"/> null: the server
    /// did not answer (or has no such route), so no file has a state. A file that differs from the head is the server's to give
    /// only when the head moved past what this machine last synced AND nothing changed here since —
    /// otherwise it is this machine's change, which a sync pushes (or, if both moved, takes to a conflict).
    /// </summary>
    public static GameFilesDto Compare(TrackedGame game, IReadOnlyList<SaveRoot> realRoots,
        IReadOnlyList<FileManifestEntry> manifest, string contentHash, IReadOnlyDictionary<string, string> onDisk,
        IReadOnlyList<SaveArchive.UnsyncedFile> unsynced, HeadFilesDto? head)
    {
        var headMoved = head?.Head is { } h && h.Id != game.LastKnownVersionId;
        var untouchedHere = game.LastSyncedHash is { } synced &&
                            string.Equals(synced, contentHash, StringComparison.OrdinalIgnoreCase);
        var differs = headMoved && untouchedHere ? Server : Here;
        var gone = headMoved || game.LastKnownVersionId is null ? Server : Here;

        // Last one wins, like the server's own listing of an archive that holds a name twice.
        var remote = new Dictionary<(string Key, string Path), (long Size, string Sha256)>();
        foreach (var f in head?.Folders ?? Array.Empty<HeadFolderDto>())
        foreach (var x in f.Files)
            remote[(f.Key, x.Path)] = (x.Size, x.Sha256);

        var folders = new List<GameFolderFilesDto>();
        foreach (var root in realRoots)
        {
            var files = new List<LocalFileDto>();
            var mine = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in manifest)
            {
                if (SaveArchive.SplitArchiveName(entry.Path) is not { } split || split.Key != root.Key) continue;
                mine.Add(split.Path);
                DateTime? modified = onDisk.TryGetValue(entry.Path, out var full) ? WriteTime(full) : null;
                var inHead = remote.TryGetValue((root.Key, split.Path), out var r);
                string? state = head is null ? null
                    : inHead && string.Equals(r.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase) ? Same
                    // Not in the head at all goes the same way: a new file here, or one the head removed.
                    : differs;
                files.Add(new LocalFileDto(split.Path, entry.Size, modified, state, NotInHead: head is not null && !inHead));
            }
            foreach (var ((key, path), r) in remote)
                if (key == root.Key && !mine.Contains(path))
                    files.Add(new LocalFileDto(path, r.Size, null, gone, Missing: true));

            var others = unsynced.Where(u => u.Key == root.Key).ToList();
            folders.Add(new GameFolderFilesDto(
                root.Key,
                Label(game, root.Key),
                root.Directory,
                files.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray(),
                others.Take(MaxOther).Select(u => new OtherFileDto(u.Path, Size(u.FullPath), u.Excluded ? Excluded : OtherGame)).ToArray(),
                others.Count));
        }

        var headDto = head?.Head is { } v ? new GameFilesHeadDto(v.Id, v.CreatedAt, v.MachineName) : null;
        return new GameFilesDto(folders.ToArray(), headDto, head is not null);
    }

    private static string Label(TrackedGame game, string key) =>
        key == SaveRoot.PrimaryKey
            ? "Save folder"
            : game.ExtraPaths.FirstOrDefault(p => p.Key == key) is { Label: { Length: > 0 } label } ? label : key;

    /// <summary>Null for a file gone since it was listed — not the 1601 a missing file reports.</summary>
    private static DateTime? WriteTime(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static long Size(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}

/// <param name="Folders">One per save folder that is a real folder on this machine, the primary first.</param>
/// <param name="Head">The server's latest version, or null when it has none or did not answer.</param>
/// <param name="Reachable">False: the server did not answer (or is older than this route), and no file
/// carries a state.</param>
public sealed record GameFilesDto(GameFolderFilesDto[] Folders, GameFilesHeadDto? Head, bool Reachable);

public sealed record GameFilesHeadDto(Guid VersionId, DateTime When, string Machine);

/// <param name="Key"><c>main</c>, or the extra folder's key.</param>
/// <param name="Path">This machine's folder.</param>
/// <param name="Files">The files a push archives, plus the head's files missing here.</param>
/// <param name="Other">Files in the folder this game does not sync, at most <see cref="SaveFileTree.MaxOther"/>.</param>
/// <param name="OtherCount">Every such file, listed or not.</param>
public sealed record GameFolderFilesDto(string Key, string Label, string Path, LocalFileDto[] Files,
    OtherFileDto[] Other, int OtherCount);

/// <param name="Path">Relative to its folder, with forward slashes.</param>
/// <param name="State"><c>same</c>, <c>here</c> or <c>server</c>; null when the server did not answer.</param>
/// <param name="Missing">Not on this machine: the head has it. <c>server</c> — a pull writes it; <c>here</c>
/// — it was deleted here, and a push removes it.</param>
/// <param name="NotInHead">On this machine but not in the head: with <c>here</c> a new file a push adds, with
/// <c>server</c> one the head removed, which a pull deletes.</param>
public sealed record LocalFileDto(string Path, long Size, DateTime? ModifiedUtc, string? State, bool Missing = false,
    bool NotInHead = false);

/// <param name="Why"><c>otherGame</c>: outside the folder's include scope. <c>excluded</c>: an exclude pattern drops it.</param>
public sealed record OtherFileDto(string Path, long Size, string Why);
