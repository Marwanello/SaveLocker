namespace SaveLocker.Agent;

/// <summary>
/// Whole-file writes that another process can never observe half-finished.
///
/// <c>File.WriteAllText</c> truncates and then writes, so a reader that opens the file in that
/// window sees an empty or partial document. Every one of the agent's state files is read by a
/// second process — the daemon and the Steam launch wrapper share all of them — and every one of
/// them is parsed as JSON, so a torn read is not a glitch: it is a config that fails to
/// deserialize and silently falls back to defaults, losing the machine's API key and game list.
///
/// Writing to a temp file and renaming makes the swap atomic: a reader sees either the whole old
/// file or the whole new one.
/// </summary>
public static class AtomicFile
{
    // Protecting the directory is a full ACL read on Windows, and these writers run constantly
    // (every config save, every health report). The directory only needs it once per process —
    // StateDirSecurity is idempotent, this just avoids asking the filesystem to prove it each time.
    private static readonly HashSet<string> ProtectedDirs = new(StringComparer.OrdinalIgnoreCase);

    // One temp-name shape for every writer here: PID separates two processes racing on the same
    // file, Guid separates two writes from the same process. Same idiom as SyncEngine.TempArchive.
    private static string NewTempPath(string dir, string path) =>
        Path.Combine(dir, $".{Path.GetFileName(path)}.{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");

    public static void WriteAllText(string path, string contents, bool restrictPermissions = false) =>
        WriteAtomic(path, restrictPermissions,
            temp => File.WriteAllText(temp, contents),
            stream => { using var writer = new StreamWriter(stream); writer.Write(contents); });

    public static void WriteAllBytes(string path, byte[] contents, bool restrictPermissions = false) =>
        WriteAtomic(path, restrictPermissions,
            temp => File.WriteAllBytes(temp, contents),
            stream => stream.Write(contents, 0, contents.Length));

    // The atomic-write contract, shared by every writer here so a future fix (the WA-03 ACL logic,
    // temp-cleanup-on-failure) only needs to be made once. Only the actual content write differs
    // between callers: writeUnrestricted for the common File.WriteAllX(temp, ...) path, and
    // writeToStream for the Unix-permission-restricted path, which needs an open FileStream to set
    // UnixCreateMode on creation rather than after the fact.
    private static void WriteAtomic(
        string path, bool restrictPermissions, Action<string> writeUnrestricted, Action<FileStream> writeToStream)
    {
        var dir = Path.GetDirectoryName(path);
        ArgumentException.ThrowIfNullOrEmpty(dir, nameof(path));
        Directory.CreateDirectory(dir);

        // Every caller that asks for restricted permissions is writing agent state, and on Windows
        // the protection that matters is on the DIRECTORY: %PROGRAMDATA% grants all authenticated
        // users read access, it inherits, and config.json holds the machine's server API key. Doing
        // it here rather than at each call site means a state file cannot be added later that
        // quietly misses it. WA-03.
        if (restrictPermissions && OperatingSystem.IsWindows())
        {
            lock (ProtectedDirs)
            {
                if (ProtectedDirs.Add(Path.GetFullPath(dir)))
                    StateDirSecurity.Protect(dir);
            }
        }

        // The temp name carries the PID: two processes racing to rewrite the same file must not
        // collide on the intermediate, or one truncates the other's half-written temp and renames
        // the result into place.
        var temp = NewTempPath(dir, path);

        try
        {
            if (restrictPermissions && !OperatingSystem.IsWindows())
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                };
                using var stream = new FileStream(temp, options);
                writeToStream(stream);
            }
            else
            {
                writeUnrestricted(temp);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }
}
