namespace SaveLocker.Agent;

/// <summary>
/// The Steam launch-options string a tracked game should carry, and how to get there from whatever
/// it carries now.
///
/// <para>
/// Pure: no I/O, no Steam knowledge, no platform knowledge. That is deliberate. The agent cannot
/// write launch options itself — Steam holds <c>localconfig.vdf</c>/<c>shortcuts.vdf</c> in memory
/// and rewrites them on exit, so an agent-side edit is discarded — which means the writer is
/// somewhere else (today, a Decky plugin: <c>tasks/DeckyPlugin.md</c>). Keeping the RULE here means
/// the writer holds no SaveLocker knowledge and the rule stays testable without Steam or hardware.
/// </para>
///
/// <para>
/// The rule substitutes into <c>%command%</c> rather than appending to the string. Users run
/// mangohud and gamemoderun, set env vars, and pass per-game arguments; appending breaks all three,
/// and each of them has a position Steam expects it in.
/// </para>
/// </summary>
public static class LaunchOptions
{
    /// <summary>Steam's placeholder for the game's own command line.</summary>
    public const string CommandToken = "%command%";

    /// <summary>What sits between the wrapper path and the token in a wrapper invocation.</summary>
    private const string RunSeparator = " run -- ";

    /// <summary>The file name the wrapper binary has, with the Windows spelling for completeness.</summary>
    private static readonly string[] WrapperNames = ["savelocker", "savelocker.exe"];

    /// <summary>
    /// The canonical wrapper invocation for an installed agent — the whole of a launch-options
    /// string for a game that has nothing else set. The one place this string is spelled.
    /// </summary>
    public static string Invocation(string wrapperPath) =>
        Quote(wrapperPath) + RunSeparator + CommandToken;

    /// <summary>
    /// True when <paramref name="existing"/> already runs the wrapper by an <b>absolute</b> path —
    /// any absolute one, not just the path we would write. A relative one reads as false: that is
    /// the case Steam cannot resolve in Game Mode, and the one <see cref="Apply"/> repairs.
    /// </summary>
    public static bool IsApplied(string? existing, string wrapperPath) =>
        TryFindWrapper(existing, wrapperPath, out _, out _, out var foundPath)
        && Path.IsPathRooted(foundPath);

    /// <summary>
    /// The launch-options string this game should have. Idempotent — it runs on a timer, not once,
    /// so <c>Apply(Apply(x)) == Apply(x)</c> must hold for every shape below.
    ///
    /// <list type="bullet">
    /// <item>empty → the wrapper invocation alone.</item>
    /// <item>already wrapped by any <b>absolute</b> savelocker path → returned unchanged, even when
    /// that is not the path we would have written. <c>~/.local/bin/savelocker</c> is a symlink to
    /// the real binary and is what <c>install.sh</c> puts on a Deck; rewriting it to the link's
    /// target changes nothing about how the game runs, so it is churn on a working config rather
    /// than a repair.</item>
    /// <item>wrapped by a <b>relative</b> savelocker → replaced with the absolute path. This is the
    /// repair that matters, and the only one: Game Mode does not put <c>~/.local/bin</c> on PATH, so
    /// a hand-typed bare <c>savelocker run -- %command%</c> silently stops the game launching at
    /// all.</item>
    /// <item>contains <c>%command%</c> → the token is replaced by the invocation, which leaves outer
    /// wrappers (<c>mangohud %command%</c>), leading <c>VAR=x</c> assignments and trailing game
    /// arguments each where they were.</item>
    /// <item>anything else → the invocation, then the existing string. Steam appends a
    /// <c>%command%</c>-less string to the game's own argv, which is exactly where it lands.</item>
    /// </list>
    /// </summary>
    public static string Apply(string? existing, string wrapperPath)
    {
        var trimmed = existing?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return Invocation(wrapperPath);

        if (TryFindWrapper(trimmed, wrapperPath, out var pathStart, out var pathLength, out var foundPath))
        {
            return Path.IsPathRooted(foundPath)
                ? trimmed
                : string.Concat(
                    trimmed.AsSpan(0, pathStart),
                    Quote(wrapperPath),
                    trimmed.AsSpan(pathStart + pathLength));
        }

        var token = trimmed.IndexOf(CommandToken, StringComparison.Ordinal);
        if (token >= 0)
            return string.Concat(
                trimmed.AsSpan(0, token),
                Invocation(wrapperPath),
                trimmed.AsSpan(token + CommandToken.Length));

        return Invocation(wrapperPath) + " " + trimmed;
    }

    /// <summary>
    /// Locate an existing <c>&lt;path&gt; run -- %command%</c>, reporting where the path sits so a
    /// caller can replace just that much. The path may be quoted; an unquoted one runs back to the
    /// previous whitespace, which is what leaves <c>mangohud …</c> in front of it untouched.
    ///
    /// <para>
    /// An occurrence counts as ours when the path is <b>either</b> named <c>savelocker</c> — which is
    /// what finds a stale one worth repairing — <b>or</b> exactly the wrapper we are applying. The
    /// second half is what keeps <see cref="Apply"/> idempotent for a wrapper whose file is named
    /// something else, and that is not hypothetical: run from a build tree the agent is
    /// <c>dotnet savelocker.dll</c>, so the resolved path is the <i>dotnet host</i>. Keyed on the
    /// name alone, Apply would not recognise its own output and would wrap it again every pass.
    /// </para>
    /// </summary>
    private static bool TryFindWrapper(
        string? value, string wrapperPath, out int pathStart, out int pathLength, out string? path)
    {
        pathStart = 0;
        pathLength = 0;
        path = null;
        if (string.IsNullOrEmpty(value)) return false;

        var run = value.IndexOf(RunSeparator + CommandToken, StringComparison.Ordinal);
        if (run <= 0) return false;

        int start;
        if (value[run - 1] == '"')
        {
            var open = value.LastIndexOf('"', run - 2);
            if (open < 0) return false;
            start = open;
            path = value.Substring(open + 1, run - 1 - (open + 1));
        }
        else
        {
            start = run;
            while (start > 0 && !char.IsWhiteSpace(value[start - 1])) start--;
            path = value[start..run];
        }

        if (path.Length == 0 ||
            !(IsWrapper(path) || string.Equals(path, wrapperPath, StringComparison.Ordinal)))
        {
            path = null;
            return false;
        }

        pathStart = start;
        pathLength = run - start;
        return true;
    }

    /// <summary>True when a path names the agent binary, however it was written.</summary>
    private static bool IsWrapper(string path)
    {
        var slash = path.LastIndexOfAny(['/', '\\']);
        var name = slash >= 0 ? path[(slash + 1)..] : path;
        return WrapperNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Quote a path that needs it. A non-default install prefix can contain spaces, and an unquoted
    /// one would have Steam run its first word and pass the rest as arguments.
    /// </summary>
    private static string Quote(string path) =>
        path.Any(char.IsWhiteSpace) ? $"\"{path}\"" : path;
}
