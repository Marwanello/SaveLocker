namespace SaveLocker.Server.Services;

/// <summary>
/// Thrown when an upload exceeds the configured byte cap. Counted while copying rather than trusted
/// from request metadata: a chunked upload declares no length, so the only honest place to find out
/// is the copy itself.
/// </summary>
public sealed class ArchiveTooLargeException(long limitBytes)
    : Exception($"Archive exceeds the {limitBytes / (1024 * 1024)} MB upload limit.")
{
    public long LimitBytes { get; } = limitBytes;
}

/// <summary>
/// Stores save-game archive files on disk (an unRAID share volume in production).
/// Layout: {root}/{gameId}/{versionId}.zip
/// <para>
/// Nothing appears at its final path until it is complete. Uploads are staged in
/// <see cref="IncomingDirName"/> on the same volume and published with a rename, so a caller that
/// sees the file sees all of it — an aborted upload used to leave a truncated archive sitting at the
/// exact path a <c>SaveVersion</c> row would name.
/// </para>
/// </summary>
public sealed class ArchiveStore
{
    /// <summary>
    /// Staging directory, deliberately inside the archive root: a rename is only atomic within one
    /// volume, and the whole point of staging is that publishing cannot half-happen.
    /// </summary>
    public const string IncomingDirName = ".incoming";

    private readonly string _root;
    private readonly ILogger<ArchiveStore> _log;

    public ArchiveStore(IConfiguration config, ILogger<ArchiveStore> log)
    {
        _log = log;
        _root = config["Storage:ArchiveRoot"]
                ?? Path.Combine(AppContext.BaseDirectory, "data", "archives");
        Directory.CreateDirectory(_root);
    }

    private string IncomingDir => Path.Combine(_root, IncomingDirName);

    /// <summary>
    /// The store path persisted in <c>SaveVersion.ArchivePath</c>. Always '/'-separated, never
    /// Path.Combine: this string goes into the DATABASE, so it outlives the OS that wrote it.
    /// A Windows-hosted server writing "gameid\versionid.zip" produces rows that the Linux
    /// (Docker) server cannot resolve — a backslash is a legal filename character there, not a
    /// separator — and every archive silently becomes undownloadable, which the agent reports
    /// as the very convincing lie "server has no saves yet".
    /// </summary>
    public string RelativePath(Guid gameId, Guid versionId) =>
        $"{gameId:N}/{versionId:N}.zip";

    /// <summary>
    /// Resolve a stored path against the archive root, accepting either separator: rows written
    /// by an older Windows-hosted server still carry backslashes.
    /// </summary>
    public string FullPath(string relativePath) =>
        Path.Combine(_root, relativePath.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Persist an uploaded archive stream and return its relative store path.
    /// <para>
    /// Staged, size-checked, then published atomically. If the copy is cancelled, the client
    /// disconnects, or the cap is exceeded, the partial file is removed and nothing is published —
    /// so the caller can never commit a database row pointing at half an archive.
    /// </para>
    /// </summary>
    /// <param name="maxBytes">Hard cap; exceeded uploads raise <see cref="ArchiveTooLargeException"/>.
    /// Non-positive disables the check.</param>
    public async Task<(string relativePath, long size)> SaveAsync(
        Guid gameId, Guid versionId, Stream content, long maxBytes, CancellationToken ct = default)
    {
        var rel = RelativePath(gameId, versionId);
        var full = FullPath(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        Directory.CreateDirectory(IncomingDir);

        // The random suffix keeps two attempts at the same version id from sharing a staging file.
        var staged = Path.Combine(IncomingDir, $"{versionId:N}-{Guid.NewGuid():N}.part");
        long written = 0;

        try
        {
            await using (var fs = new FileStream(
                staged, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    written += read;
                    if (maxBytes > 0 && written > maxBytes) throw new ArchiveTooLargeException(maxBytes);
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                // Durable before it is visible: publishing a name that outlives the bytes is the
                // failure this whole method exists to prevent.
                await fs.FlushAsync(ct);
                fs.Flush(flushToDisk: true);
            }

            File.Move(staged, full, overwrite: true);
            return (rel, written);
        }
        catch
        {
            TryDeleteFile(staged);
            throw;
        }
    }

    public Stream OpenRead(string relativePath) => File.OpenRead(FullPath(relativePath));

    public bool Exists(string relativePath) => File.Exists(FullPath(relativePath));

    /// <summary>
    /// Delete a stored archive. Returns false if the file is still there afterwards, so a caller
    /// that has already committed the database can record the cleanup debt instead of assuming.
    /// </summary>
    public bool TryDelete(string relativePath)
    {
        var full = FullPath(relativePath);
        try
        {
            if (File.Exists(full)) File.Delete(full);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not delete archive {Path}; it is now orphaned on disk.", relativePath);
            return false;
        }
    }

    /// <summary>
    /// Remove staging files older than <paramref name="olderThan"/>. Anything left in staging is by
    /// definition an upload that never completed — but only once it is old enough that it cannot be
    /// an upload still in flight.
    /// </summary>
    public int SweepIncoming(TimeSpan olderThan)
    {
        if (!Directory.Exists(IncomingDir)) return 0;

        var cutoff = DateTime.UtcNow - olderThan;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(IncomingDir, "*.part"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                File.Delete(file);
                removed++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not sweep stale upload {Path}.", file);
            }
        }
        if (removed > 0) _log.LogInformation("Swept {Count} stale upload file(s) from staging.", removed);
        return removed;
    }

    private void TryDeleteFile(string fullPath)
    {
        try { if (File.Exists(fullPath)) File.Delete(fullPath); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not remove staging file {Path}.", fullPath); }
    }
}
