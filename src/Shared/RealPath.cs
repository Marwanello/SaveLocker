namespace SaveLocker.Shared;

/// <summary>
/// The real filesystem path a path designates, with symlinks, junctions and mount points resolved
/// <b>segment by segment</b> — not just the final component, which is all
/// <c>FileSystemInfo.ResolveLinkTarget</c> resolves for a whole path and all
/// <c>SavePathGuard.Canonicalize</c> was built to need.
/// <para>
/// Manifest-expanded save paths must be checked with this: every consumer downstream follows links
/// for real (<see cref="Directory.Exists(string)"/>, the archive walk, the restore's delete pass),
/// so a path that only <i>looks</i> like it sits under a manifest token can still hand a save
/// folder — or a restore — to somewhere else entirely. A component that is a reparse point but not
/// a link (OneDrive's files-on-demand placeholders, for one) is deliberately treated as an ordinary
/// directory; only genuine links are followed.
/// </para>
/// </summary>
public static class RealPath
{
    /// <summary>Enough for any sane directory tree; a cycle is refused rather than chased forever.</summary>
    private const int MaxHops = 40;

    /// <summary>
    /// Resolve every existing link component of <paramref name="path"/>, returning the resolved
    /// path with any not-yet-existing tail appended verbatim. Null when the text is not a usable
    /// absolute path, or when a link chain never settles.
    /// </summary>
    public static string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string full;
        try
        {
            full = Path.GetFullPath(path);
            if (!Path.IsPathRooted(full)) return null;
        }
        catch { return null; } // invalid characters, or a too-long path

        for (var hop = 0; hop <= MaxHops; hop++)
        {
            if (FirstLink(full) is not { } link) return full;

            var rest = full[link.Path.Length..]
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            try
            {
                full = Path.GetFullPath(rest.Length == 0 ? link.Target : Path.Combine(link.Target, rest));
            }
            catch { return null; }
        }

        return null; // more links than any real tree nests: something is cyclical
    }

    /// <summary>
    /// The first existing component of <paramref name="full"/> that is a link (symlink, junction,
    /// mount point), with its target made absolute — or null when there is none.
    /// <para>
    /// A relative link target is resolved against the link's own directory, the same rule the OS
    /// applies. A missing or not-yet-existing component is skipped; so is a reparse point that is
    /// not a link, which is what keeps OneDrive placeholders out of this.
    /// </para>
    /// </summary>
    private static (string Path, string Target)? FirstLink(string full)
    {
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) return null;

        var segments = full[root.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var current = root;

        foreach (var segment in segments)
        {
            var next = Path.Combine(current, segment);

            string? target = null;
            try
            {
                FileSystemInfo? info = Directory.Exists(next)
                    ? new DirectoryInfo(next)
                    : File.Exists(next) ? new FileInfo(next) : null;
                target = info?.LinkTarget;
            }
            catch { target = null; }

            if (target is not null)
            {
                var resolved = Path.IsPathRooted(target)
                    ? target
                    : Path.Combine(Path.GetDirectoryName(next) ?? current, target);
                return (next, resolved);
            }

            current = next;
        }

        return null;
    }
}
