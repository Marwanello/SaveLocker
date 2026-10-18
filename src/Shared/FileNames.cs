namespace SaveLocker.Shared;

/// <summary>
/// One file of a game that this machine names differently from the server (tasks/emulator-saves Phase 17): in save folder
/// <paramref name="Key"/>, the server's <paramref name="Server"/> is this machine's <paramref name="Local"/> — two dumps of
/// one ROM, <c>Chrono Trigger (USA).srm</c> on the server, <c>Chrono Trigger (USA) (Rev 1).srm</c> here. Both are paths
/// relative to the folder, with forward slashes.
/// </summary>
public sealed record FileNameDto(string Key, string Server, string Local);

/// <summary>
/// A machine's own file names for a game, applied on the way in and out of every archive so the server, its hash and its
/// manifest only ever see the server's names (tasks/emulator-saves Phase 17). The list is explicit — one pair per file
/// (maintainer's call, 2026-10-18) — and grows by itself: a file not listed whose name starts with a listed file's name
/// without its extension, followed by a dot, takes the same swap (<c>… (Rev 1).state3</c> → <c>… (USA).state3</c>), and
/// <see cref="Derive"/> says which pairs to add to the list.
/// <para>
/// A file here that already has a server name of the list, without being listed itself, is not this game's: it is another
/// dump's save under the name the server uses, and taking it too would put two files at one name.
/// </para>
/// </summary>
public sealed class FileNames
{
    /// <summary>Most pairs one machine keeps for one game, and the longest name.</summary>
    public const int MaxPairs = 256, MaxNameLength = 260;

    private readonly IReadOnlyList<FileNameDto> _pairs;
    private readonly string _key;

    private FileNames(IReadOnlyList<FileNameDto> pairs, string key)
    {
        _pairs = pairs;
        _key = key;
    }

    /// <summary>The swap for one save folder of a game, or null when the machine keeps the server's names.</summary>
    public static FileNames? For(IReadOnlyList<FileNameDto>? pairs, string key) =>
        pairs is { Count: > 0 } ? new FileNames(pairs, key) : null;

    private static StringComparison Cmp => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The server's name for this machine's <paramref name="localRel"/> in this folder — itself when nothing swaps
    /// it — or null when the file is not this game's (it has a server name of the list without being listed).</summary>
    public string? ToServer(string localRel)
    {
        foreach (var p in _pairs)
            if (p.Key == _key && string.Equals(p.Local, localRel, Cmp)) return p.Server;
        if (_pairs.Any(p => p.Key == _key && string.Equals(p.Server, localRel, Cmp))) return null;
        return Swap(localRel, local: true) ?? localRel;
    }

    /// <summary>This machine's name for the server's <paramref name="serverRel"/> in this folder: the listed one, else the
    /// derived one, else the server's own.</summary>
    public string ToLocal(string serverRel)
    {
        foreach (var p in _pairs)
            if (p.Key == _key && string.Equals(p.Server, serverRel, Cmp)) return p.Local;
        return Swap(serverRel, local: false) ?? serverRel;
    }

    /// <summary>The name with the longest listed stem it starts with swapped for the other side's, or null. Stems come from
    /// every folder's pairs: the states folder has none until its first state, and takes the save's.</summary>
    private string? Swap(string rel, bool local)
    {
        var dir = DirOf(rel);
        var file = rel[dir.Length..];
        (string From, string To)? best = null;
        foreach (var p in _pairs)
        {
            var (from, to) = local ? (Stem(p.Local), Stem(p.Server)) : (Stem(p.Server), Stem(p.Local));
            if (from.Length == 0 || string.Equals(from, to, StringComparison.Ordinal)) continue;
            if (file.Length > from.Length && file.StartsWith(from, Cmp) && file[from.Length] == '.' &&
                (best is null || from.Length > best.Value.From.Length))
                best = (from, to);
        }
        return best is { } b ? dir + b.To + file[b.From.Length..] : null;
    }

    /// <summary>A file name without its folder and its last extension: <c>Chrono Trigger (USA) (Rev 1)</c>.</summary>
    private static string Stem(string rel)
    {
        var file = rel[DirOf(rel).Length..];
        var dot = file.LastIndexOf('.');
        return dot > 0 ? file[..dot] : file;
    }

    private static string DirOf(string rel)
    {
        var slash = rel.LastIndexOf('/');
        return slash >= 0 ? rel[..(slash + 1)] : "";
    }

    /// <summary>The pairs to add to <paramref name="pairs"/> for the files of folder <paramref name="key"/> named
    /// <paramref name="localRels"/>: each one a stem swaps that the list does not have yet.</summary>
    public static IReadOnlyList<FileNameDto> Derive(IReadOnlyList<FileNameDto> pairs, string key, IEnumerable<string> localRels)
    {
        if (For(pairs, key) is not { } names) return [];
        var added = new List<FileNameDto>();
        foreach (var rel in localRels)
        {
            if (pairs.Any(p => p.Key == key && string.Equals(p.Local, rel, Cmp))) continue;
            if (names.Swap(rel, local: true) is { } server && !string.Equals(server, rel, Cmp) &&
                !pairs.Any(p => p.Key == key && string.Equals(p.Server, server, Cmp)))
                added.Add(new FileNameDto(key, server, rel));
        }
        return added;
    }

    /// <summary>
    /// Null when <paramref name="pairs"/> can be a machine's names for a game, else why not: each name a relative path inside
    /// its folder (no <c>..</c>, no drive, no backslash), within the size limits, and each name on either side once per folder.
    /// </summary>
    public static string? Validate(IReadOnlyList<FileNameDto>? pairs)
    {
        if (pairs is null) return null;
        if (pairs.Count > MaxPairs) return $"At most {MaxPairs} file names per game.";
        foreach (var p in pairs)
        {
            if (p.Key != SaveRoot.PrimaryKey && SaveRoot.ValidateExtraKey(p.Key) is { } why) return why;
            foreach (var name in new[] { p.Server, p.Local })
                if (BadName(name) is { } bad) return $"'{name}': {bad}";
        }
        foreach (var folder in pairs.GroupBy(p => p.Key))
        {
            if (folder.GroupBy(p => p.Server, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return $"Two files of the '{folder.Key}' folder have one server name.";
            if (folder.GroupBy(p => p.Local, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                return $"Two server files of the '{folder.Key}' folder have one name on this machine.";
        }
        return null;
    }

    private static string? BadName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "a file name is empty.";
        if (name.Length > MaxNameLength) return $"longer than {MaxNameLength} characters.";
        if (name.Contains('\\') || name.Contains(':') || name.Contains('\0')) return "not a relative path with forward slashes.";
        if (name.StartsWith('/') || name.Split('/').Any(s => s is "" or "." or "..")) return "not a path inside the save folder.";
        return null;
    }
}
