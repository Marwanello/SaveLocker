using System.Diagnostics;
using System.Text.RegularExpressions;
using SaveLocker.Shared;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Rung 3 of the Linux desktop/headless escalation ladder
/// (tasks/conflict-resolution-ui/plan.md, Phase 9): a best-effort desktop notification the moment a
/// conflict this machine is party to becomes open, with an action button that opens Phase 6's
/// agent-ui conflicts page in the default browser. Genuinely optional — rungs 1-2 and 5-7 (CLI,
/// <c>doctor</c>, and the safe <c>ConflictFlag.Open</c> terminal state) already guarantee a conflict
/// is discoverable and never silently mishandled whether or not this ever fires. Every failure mode
/// here — a missing <c>gdbus</c>, an unreachable bus, a daemon that doesn't understand the call —
/// collapses to "notification not shown," logged, never a crash and never a hang: <see cref="CommandPoller"/>'s
/// own tick timing depends on this returning promptly.
/// <para>
/// Deliberately does NOT bring the Deck's Game Mode screen to the foreground on click — that is
/// Phase 8's screen, which does not exist yet. Only the desktop-session half of the plan's "action
/// button ... opens the chooser" is implemented; the Deck half waits for Phase 8, same as Phase 4
/// left Windows' tray untouched for Phase 7.
/// </para>
/// </summary>
public sealed class ConflictNotifier : IDisposable
{
    private readonly Func<string> _conflictsUrl;
    private readonly object _lock = new();

    /// <summary>Conflict ids already notified about. Cleared per id the moment it stops being open,
    /// so a fresh conflict (a new id — conflicts are never reused) always notifies again.</summary>
    private readonly HashSet<Guid> _notified = new();

    /// <summary>D-Bus notification ids sent by this process that are still waiting on a click, so
    /// the monitor below can tell "our notification's action" apart from every other app's.</summary>
    private readonly HashSet<uint> _awaitingAction = new();

    private Process? _monitor;
    private bool _monitorStarted;

    public ConflictNotifier(Func<string> conflictsUrl) => _conflictsUrl = conflictsUrl;

    /// <summary>
    /// Called once per <see cref="CommandPoller"/> tick with the open conflicts this machine is a
    /// party to (already filtered by machine id — the same "no bystander case" filter <c>doctor</c>
    /// and <c>savelocker conflicts</c> apply). Notifies once per conflict id the first time it's seen
    /// open; ids that closed since the last check are forgotten, not just skipped, so nothing here
    /// grows unbounded over a long-running daemon.
    /// </summary>
    public void CheckConflicts(IReadOnlyList<ConflictDto> openConflicts, Func<Guid, string> gameName)
    {
        var newlyOpen = new List<ConflictDto>();
        lock (_lock)
        {
            var openIds = new HashSet<Guid>(openConflicts.Select(c => c.Id));
            _notified.RemoveWhere(id => !openIds.Contains(id));
            foreach (var c in openConflicts)
                if (_notified.Add(c.Id)) newlyOpen.Add(c);
        }
        if (newlyOpen.Count == 0) return;

        // Only worth asking the environment when there is actually something new to say — this is
        // the one point in the whole poll loop that spawns a process, and the overwhelming common
        // case (no new conflict this tick) must cost nothing beyond the set comparison above.
        var env = DesktopEnvironment.Detect();
        if (!env.NotificationDaemonPresent)
        {
            AgentLogger.Log($"conflict notification: {newlyOpen.Count} new open " +
                $"conflict{(newlyOpen.Count == 1 ? "" : "s")}, no desktop notification daemon " +
                "reachable — see `doctor` or the agent UI.");
            return;
        }

        foreach (var c in newlyOpen) Notify(gameName(c.GameId));
    }

    private void Notify(string gameName)
    {
        var body = $"'{gameName}' changed on this device and in the cloud since the last " +
            "sync — resolve it before playing again.";

        var (exit, stdout, _) = ProcessRunner.Run("gdbus",
            [
                "call", "--session",
                "--dest", "org.freedesktop.Notifications",
                "--object-path", "/org/freedesktop/Notifications",
                "--method", "org.freedesktop.Notifications.Notify",
                GVariantString("SaveLocker"),
                "uint32 0",
                GVariantString("dialog-warning"),
                GVariantString("Sync conflict"),
                GVariantString(body),
                "['view', 'View conflict']",
                "@a{sv} {}",
                "0",
            ],
            TimeSpan.FromSeconds(2));

        if (exit != 0)
        {
            AgentLogger.Log($"conflict notification: gdbus call failed for '{gameName}' (exit {exit}).");
            return;
        }
        AgentLogger.Log($"conflict notification: sent for '{gameName}'.");

        if (TryParseNotificationId(stdout) is { } id)
        {
            lock (_lock) _awaitingAction.Add(id);
            EnsureMonitorStarted();
        }
    }

    /// <summary>
    /// A GVariant text-format STRING literal, safe for content this codebase does not otherwise
    /// control — a game's manifest/user-set name commonly contains an apostrophe (Baldur's Gate 3,
    /// Assassin's Creed), which would break a naively single-quoted literal. Double-quoted GVariant
    /// strings use C-style escaping, so only `\` and `"` need escaping here.
    /// </summary>
    private static string GVariantString(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>gdbus prints a successful Notify call's reply as e.g. <c>(uint32 42,)</c>.</summary>
    private static uint? TryParseNotificationId(string stdout)
    {
        var m = Regex.Match(stdout, @"uint32\s+(\d+)");
        return m.Success && uint.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>
    /// One long-lived <c>gdbus monitor</c> for the whole life of the process, started lazily on the
    /// first notification actually sent — most installs never open a conflict, and this is the one
    /// part of the feature that keeps a child process running rather than making a single bounded
    /// call. It never exits on its own, so nothing here waits on it; a line it doesn't recognise
    /// (any other app's notification activity on the same bus) is simply ignored.
    /// </summary>
    private void EnsureMonitorStarted()
    {
        lock (_lock)
        {
            if (_monitorStarted) return;
            _monitorStarted = true;
        }

        try
        {
            var psi = new ProcessStartInfo("gdbus") { RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add("monitor");
            psi.ArgumentList.Add("--session");
            psi.ArgumentList.Add("--dest");
            psi.ArgumentList.Add("org.freedesktop.Notifications");

            var proc = Process.Start(psi);
            if (proc is null) return;
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) OnMonitorLine(e.Data); };
            proc.BeginOutputReadLine();
            _monitor = proc;
        }
        catch (Exception ex)
        {
            // No monitor means a clicked action button silently does nothing — degraded, not broken:
            // the notification itself already told the user a conflict exists, and every other rung
            // (doctor, the agent UI, the CLI) still reaches it.
            AgentLogger.LogException("ConflictNotifier.EnsureMonitorStarted", ex);
        }
    }

    private void OnMonitorLine(string line)
    {
        var m = Regex.Match(line, @"\.(ActionInvoked|NotificationClosed) \(uint32 (\d+)");
        if (!m.Success) return;
        if (!uint.TryParse(m.Groups[2].Value, out var id)) return;

        bool wasOurs;
        lock (_lock) wasOurs = _awaitingAction.Remove(id);
        if (!wasOurs) return;

        if (m.Groups[1].Value == "ActionInvoked") OpenConflictsPage();
    }

    /// <summary>
    /// Opens Phase 6's agent-ui conflicts page in the default browser — the desktop-session half of
    /// the plan's "action button ... opens the chooser." Best-effort: a missing `xdg-open` (a bare
    /// window manager with no default-application handler configured) degrades to nothing happening,
    /// not a crash.
    /// </summary>
    private void OpenConflictsPage()
    {
        try
        {
            var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(_conflictsUrl());
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("ConflictNotifier.OpenConflictsPage", ex);
        }
    }

    public void Dispose()
    {
        try { _monitor?.Kill(entireProcessTree: true); } catch { /* best effort */ }
        _monitor?.Dispose();
    }
}
