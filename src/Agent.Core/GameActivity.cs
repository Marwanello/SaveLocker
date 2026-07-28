using System.Diagnostics;

namespace SaveLocker.Agent;

/// <summary>
/// The one answer to "is this game running right now?", shared by every surface that could restore
/// a save directory: tray menu, local API, dashboard commands, CLI, and the automatic lifecycle.
///
/// <para>
/// It exists because Windows has no launch boundary. <see cref="ProcessWatcher"/> polls every four
/// seconds, so by the time it says "launched" the game has been running for up to four seconds and
/// has almost certainly opened its save files. A restore at that point writes underneath a live
/// process — the game then saves over it at exit, and the pulled save is gone. Linux does have a
/// real boundary (<c>savelocker run -- %command%</c> runs before the game), which is why that path
/// can still pull; see Decisions.md.
/// </para>
///
/// <para>
/// Callers check this to give the user an accurate reason. <see cref="SyncEngine.PullAsync"/> checks
/// it again as the enforcing backstop, twice — before downloading and again immediately before the
/// restore — because a game can launch during a multi-minute download.
/// </para>
/// </summary>
public static class GameActivity
{
    /// <summary>
    /// True if any of the game's configured process names is running. A game with no configured
    /// process names returns false: we genuinely cannot tell, and refusing every pull for every
    /// unmapped game would break sync outright. WA-08 is what makes that list non-empty.
    /// </summary>
    public static bool IsActive(TrackedGame game, out string? processName)
    {
        processName = null;
        if (game.ProcessNames.Count == 0) return false;

        var wanted = new HashSet<string>(game.ProcessNames.Select(StripExe), StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (wanted.Contains(name))
                {
                    processName = name;
                    return true;
                }
            }
            catch { /* the process died between enumeration and read — it is not running */ }
            finally { p.Dispose(); }
        }
        return false;
    }

    public static bool IsActive(TrackedGame game) => IsActive(game, out _);

    /// <summary>
    /// The refusal sentence. One wording everywhere, so the console, a toast, and the CLI all say
    /// the same thing about the same condition.
    /// </summary>
    public static string RefusalMessage(TrackedGame game, string? processName) =>
        $"[{game.Name}] REFUSED pull: the game is running" +
        (processName is null ? "" : $" ({processName}.exe)") +
        ". Restoring saves under a live game loses them when it writes at exit. " +
        "Close the game and pull again.";

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
