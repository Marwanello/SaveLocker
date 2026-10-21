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
    /// True if any of the game's configured process names is running, or — for an emulator's game — that emulator
    /// is (tasks/emulator-pull-safety Phase 3): PCSX2, Dolphin and DuckStation keep the whole memory card in memory, so
    /// a save restored under them is not seen and is overwritten when they write the card back. Every game of a running
    /// emulator waits (Q3). A game with neither returns false: we genuinely cannot tell, and refusing every pull for
    /// every unmapped game would break sync outright. WA-08 is what makes the process list non-empty.
    /// <para>The emulator is never put in <see cref="TrackedGame.ProcessNames"/>: <see cref="ProcessWatcher"/> would then
    /// take "PCSX2 closed" as "every PS2 game exited" and push them all.</para>
    /// </summary>
    public static bool IsActive(TrackedGame game, out string? processName)
    {
        processName = null;
        var emulators = EmulatorProcesses.EmulatorsOf(game);
        if (game.ProcessNames.Count == 0 && emulators.Count == 0) return false;
        return IsActive(game, EmulatorProcesses.Snapshot(), out processName);
    }

    /// <summary><see cref="IsActive(TrackedGame, out string?)"/> against a list of running process names.</summary>
    internal static bool IsActive(TrackedGame game, IReadOnlyCollection<string> running, out string? processName)
    {
        processName = null;
        var wanted = new HashSet<string>(game.ProcessNames.Select(StripExe), StringComparer.OrdinalIgnoreCase);
        if (running.FirstOrDefault(wanted.Contains) is { } own)
        {
            processName = own;
            return true;
        }
        if (EmulatorProcesses.Running(EmulatorProcesses.EmulatorsOf(game), running) is { } emulator)
        {
            processName = emulator.Process;
            return true;
        }
        return false;
    }

    public static bool IsActive(TrackedGame game) => IsActive(game, out _);

    /// <summary>What is open, for a sentence: the emulator holding <paramref name="processName"/> (<c>PCSX2</c>), else
    /// <c>the game</c>.</summary>
    public static string WhatIsOpen(TrackedGame game, string? processName) =>
        processName is not null && !game.ProcessNames.Any(n => StripExe(n).Equals(processName, StringComparison.OrdinalIgnoreCase)) &&
        EmulatorProcesses.EmulatorsOf(game).FirstOrDefault(e => EmulatorProcesses.IsProcessOf(processName, e)) is { } emulator
            ? emulator
            : "the game";

    /// <summary>
    /// The refusal sentence. One wording everywhere, so the console, a toast, and the CLI all say
    /// the same thing about the same condition.
    /// </summary>
    public static string RefusalMessage(TrackedGame game, string? processName) =>
        processName is not null && !game.ProcessNames.Any(n => StripExe(n).Equals(processName, StringComparison.OrdinalIgnoreCase)) &&
        EmulatorProcesses.EmulatorsOf(game).FirstOrDefault(e => EmulatorProcesses.IsProcessOf(processName, e)) is { } emulator
            ? $"[{game.Name}] REFUSED pull: {emulator} is open. It can hold the save in memory, so a save restored now is not " +
              $"seen and can be overwritten. Close {emulator} and pull again."
            : $"[{game.Name}] REFUSED pull: the game is running" +
              (processName is null ? "" : $" ({processName}.exe)") +
              ". Restoring saves under a live game loses them when it writes at exit. " +
              "Close the game and pull again.";

    /// <summary>
    /// The process name a launcher path implies — <c>D:\Games\Foo\foo.exe</c> becomes <c>foo</c>.
    /// Null when there is nothing usable to derive. Shared by both scanners so a shortcut produces
    /// the same answer on either OS.
    /// </summary>
    public static string? ProcessNameFromExe(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        string name;
        try { name = Path.GetFileNameWithoutExtension(exePath.Trim().Trim('"')); }
        catch { return null; } // invalid path characters — not something to guess at
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string StripExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
