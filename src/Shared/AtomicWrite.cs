namespace SaveLocker.Shared;

/// <summary>
/// Whole-file writes another process can never observe half-finished — the manifest cache's copy of
/// the same idiom <c>SaveLocker.Agent.AtomicFile</c> uses for agent state, minus the state-dir
/// permission layer (that one is Agent-only, and <c>SaveLocker.Shared</c> cannot reference it).
///
/// The temp name carries the PID and a GUID: the daemon, a one-shot CLI and a second agent process
/// can all rewrite the same cache, and a shared fixed name would let one process rename the other's
/// half-written temp into place.
/// </summary>
public static class AtomicWrite
{
    /// <summary>The working name for a write to <paramref name="path"/> — for a caller that streams
    /// its content into the temp file itself (a bounded download) and then moves it into place.
    /// A failure must delete the temp; <see cref="WriteAllBytes"/> does that automatically.</summary>
    public static string NewTempPath(string path) =>
        Path.Combine(
            Path.GetDirectoryName(path) ?? ".",
            $".{Path.GetFileName(path)}.{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");

    public static void WriteAllText(string path, string contents) =>
        Write(path, temp => File.WriteAllText(temp, contents));

    public static void WriteAllBytes(string path, byte[] contents) =>
        Write(path, temp => File.WriteAllBytes(temp, contents));

    private static void Write(string path, Action<string> write)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = NewTempPath(path);
        try
        {
            write(temp);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }
}
