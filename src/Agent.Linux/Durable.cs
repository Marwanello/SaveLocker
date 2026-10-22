using System.Runtime.InteropServices;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Writes that are on the disk when they return, for the updater.
/// <para>
/// ext4 with delayed allocation keeps a freshly written file's data in memory for up to ~30 s while
/// the file's existence and size reach the journal within 5. Lose power in that window and the file
/// comes back <b>empty</b>. The agent runs perfectly from the page cache until then, so nothing
/// looks wrong. A Deck that crashed seconds after an update installed came back with every copied
/// file at 0 bytes and no way to roll back (Gotchas.md). Rename-over-an-existing-file is the one
/// pattern ext4 flushes promptly on its own (<c>auto_da_alloc</c>), and the updater cannot use it:
/// it moves the old file aside so a running wrapper keeps its inode.
/// </para>
/// </summary>
internal static class Durable
{
    /// <summary>Copy <paramref name="src"/> to a new file at <paramref name="dst"/> and fsync it.</summary>
    public static void CopyFile(string src, string dst)
    {
        using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    /// <summary>
    /// fsync a directory, which is what makes a create, rename or delete inside it durable. The
    /// file's own fsync covers its data, not its name.
    /// </summary>
    public static void FlushDirectory(string dir)
    {
        var fd = open(dir, ORdOnly | OCloExec);
        if (fd < 0)
            throw new IOException($"Could not open {dir} to flush it to disk (errno {Marshal.GetLastPInvokeError()}).");
        try
        {
            if (fsync(fd) != 0)
                throw new IOException($"Could not flush {dir} to disk (errno {Marshal.GetLastPInvokeError()}).");
        }
        finally { close(fd); }
    }

    /// <summary>
    /// <paramref name="dir"/> and every directory above it up to and including
    /// <paramref name="root"/>: a directory the updater had to create is itself a new name in its
    /// parent.
    /// </summary>
    public static IEnumerable<string> UpTo(string dir, string root)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        if (d != rootFull && !d.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            yield break;
        while (true)
        {
            yield return d;
            if (d == rootFull) yield break;
            d = Path.GetDirectoryName(d)!;
        }
    }

    private const int ORdOnly = 0;
    private const int OCloExec = 0x80000;

    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int open(string pathname, int flags);

    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int fsync(int fd);

    [DllImport("libc.so.6")]
    private static extern int close(int fd);
}
