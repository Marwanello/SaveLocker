using System.Diagnostics;
using SaveLocker.Shared;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Rung 3 of the Linux desktop/headless escalation ladder
/// (tasks/conflict-resolution-ui/plan.md, Phase 9): a best-effort desktop notification the moment a
/// conflict this machine is party to becomes open, with an action button that opens Phase 6's
/// agent-ui conflicts page in the default browser. Genuinely optional — rungs 1-2 and 5-7 (CLI,
/// <c>doctor</c>, and the safe <c>ConflictFlag.Open</c> terminal state) already guarantee a conflict
/// is discoverable and never silently mishandled whether or not this ever fires. Every failure mode
/// here — a missing <c>notify-send</c>, an unreachable bus, a daemon that ignores the call —
/// collapses to "notification not shown," logged, never a crash and never a hang:
/// <see cref="CommandPoller"/>'s own tick timing depends on this returning promptly, so nothing here
/// ever waits on the child process it starts.
/// <para>
/// <b>Why <c>notify-send --wait</c> and not <c>gdbus call</c>, corrected on real hardware
/// 2026-09-02.</b> plan.md's Phase 9 tooling decision picked <c>gdbus</c>, and the first
/// implementation shipped that way: it reported success, returned a real notification id, and the
/// popup was withdrawn again within a second — a notification carrying <b>actions</b> is owned by the
/// bus connection that sent it, and a notification server closes it when that connection drops,
/// because nobody is left to receive <c>ActionInvoked</c>. <c>gdbus call</c> is one-shot by
/// construction: it sends, takes its reply and exits, so it can never hold a notification open. Not a
/// Deck quirk — bisected against the same daemon on the same bus, where the identical call without
/// actions stayed up and with actions flashed. <c>notify-send --wait</c> keeps its connection alive
/// for as long as the notification is displayed, which fixes the popup AND makes the button work, and
/// it prints the invoked action key straight to stdout — so the separate <c>gdbus monitor</c> the
/// first version needed to catch the click (a second connection, which could never have owned the
/// notification anyway) is gone entirely. <c>gdbus</c> is still what Phase 5's
/// <see cref="DesktopEnvironment"/> probe uses to ask whether a notification daemon is there at all;
/// that part was never in question.
/// </para>
/// <para>
/// Deliberately does NOT bring the Deck's Game Mode screen to the foreground on click — that is
/// Phase 8's screen, which does not exist yet. Only the desktop-session half of the plan's "action
/// button ... opens the chooser" is implemented; the Deck half waits for Phase 8, same as Phase 4
/// left Windows' tray untouched for Phase 7.
/// </para>
/// </summary>
public sealed class ConflictNotifier : IDisposable
{
    /// <summary>The action's key, which <c>notify-send --wait</c> prints on stdout when clicked.</summary>
    private const string ActionKey = "view";

    private readonly string _conflictsUrl;
    private readonly object _lock = new();

    /// <summary>
    /// Conflict ids already notified about. Cleared per id the moment it stops being open, so a fresh
    /// conflict (a new id — conflicts are never reused) always notifies again. Deliberately separate
    /// from <see cref="_live"/>: a notification the user dismissed leaves this set alone, or every
    /// dismissal would be undone by the next 20s tick putting the same popup back.
    /// </summary>
    private readonly HashSet<Guid> _notified = new();

    /// <summary>
    /// The still-running <c>notify-send --wait</c> per conflict, plus the notification id it prints
    /// via <c>--print-id</c> once the daemon assigns one. Withdraw() uses both: the documented
    /// <c>CloseNotification(id)</c> call when an id was captured, and killing the process — the same
    /// ownership rule that broke the first implementation, kept as the always-available fallback —
    /// either way. A conflict resolved from the dashboard, the CLI or another machine takes its
    /// now-stale notification off this screen instead of leaving a button that resolves nothing.
    /// </summary>
    private readonly Dictionary<Guid, LiveNotification> _live = new();

    private sealed class LiveNotification
    {
        public required Process Proc { get; init; }
        public uint? NotificationId;
    }

    public ConflictNotifier(string conflictsUrl) => _conflictsUrl = conflictsUrl;

    /// <summary>
    /// Called once per <see cref="CommandPoller"/> tick with the open conflicts this machine is a
    /// party to (already filtered by machine id — the same "no bystander case" filter <c>doctor</c>
    /// and <c>savelocker conflicts</c> apply). Notifies once per conflict id the first time it's seen
    /// open; ids that closed since the last check are forgotten, not just skipped, so nothing here
    /// grows unbounded over a long-running daemon.
    /// </summary>
    public void CheckConflicts(IReadOnlyList<ConflictDto> openConflicts, Func<Guid, string> gameName)
    {
        var openIds = new HashSet<Guid>(openConflicts.Select(c => c.Id));
        var newlyOpen = new List<ConflictDto>();
        var resolved = new List<LiveNotification>();

        lock (_lock)
        {
            _notified.RemoveWhere(id => !openIds.Contains(id));
            foreach (var id in _live.Keys.Where(id => !openIds.Contains(id)).ToList())
            {
                resolved.Add(_live[id]);
                _live.Remove(id);
            }
            foreach (var c in openConflicts)
                if (!_notified.Contains(c.Id)) newlyOpen.Add(c);
        }

        foreach (var live in resolved) Withdraw(live);
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

        foreach (var c in newlyOpen) Notify(c, gameName(c.GameId));
    }

    /// <summary>
    /// Starts one <c>notify-send --wait</c> and returns immediately — the child is what waits, not
    /// this thread. Arguments go through <c>ArgumentList</c>, so a game name containing an apostrophe
    /// or a quote is just text (the escaping the GVariant-literal version had to do by hand does not
    /// arise here at all).
    /// </summary>
    private void Notify(ConflictDto conflict, string gameName)
    {
        var body = $"'{gameName}' changed on this device and in the cloud since the last " +
            "sync — resolve it before playing again.";

        var psi = new ProcessStartInfo("notify-send")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--app-name=SaveLocker");
        psi.ArgumentList.Add("--icon=dialog-warning");
        // No --expire-time: this is the exact shape confirmed working on real hardware, and a
        // notification carrying actions is already kept until it is answered rather than timed out.
        // Adding an untested flag to a call that has been verified end-to-end buys nothing.
        psi.ArgumentList.Add("--wait");
        // Lets Withdraw() close this exact notification by id via the documented D-Bus method, for a
        // daemon that does not tear a notification down just because the connection that sent it
        // drops (the assumption killing the process alone relies on). Purely additive: printed once,
        // read from the same stdout the action key already comes from, never blocks or changes the
        // --wait behavior this call was verified against.
        psi.ArgumentList.Add("--print-id");
        psi.ArgumentList.Add($"--action={ActionKey}=View conflict");
        psi.ArgumentList.Add("Sync conflict");
        psi.ArgumentList.Add(body);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            AgentLogger.LogException($"ConflictNotifier.Notify '{gameName}'", ex);
            return;
        }

        if (proc is null)
        {
            AgentLogger.Log($"conflict notification: notify-send did not start for '{gameName}'.");
            return;
        }

        // Recorded as notified — and live — only now that the process has actually started: marking
        // it earlier would permanently skip a conflict whose first notify-send attempt failed to
        // launch. Committed together, before EnableRaisingEvents is set below, so a fast-exiting
        // process can never find _live still missing its own entry when Exited fires.
        var live = new LiveNotification { Proc = proc };
        lock (_lock)
        {
            _notified.Add(conflict.Id);
            _live[conflict.Id] = live;
        }

        proc.EnableRaisingEvents = true;
        proc.OutputDataReceived += (_, e) =>
        {
            var line = e.Data?.Trim();
            if (line is null) return;
            // --print-id's one-line id, printed once as soon as the daemon assigns one — everything
            // else on this stream is the action key, printed later and only if the button is clicked.
            if (uint.TryParse(line, out var id)) lock (_lock) live.NotificationId = id;
            else if (string.Equals(line, ActionKey, StringComparison.Ordinal)) OpenConflictsPage();
        };
        proc.Exited += (_, _) => Forget(conflict.Id, proc);
        proc.BeginOutputReadLine();

        AgentLogger.Log($"conflict notification: sent for '{gameName}'.");
    }

    /// <summary>Drop a finished notification's process, without disturbing a newer one for the same
    /// conflict (there should never be one, but identity is cheaper to check than to reason about).</summary>
    private void Forget(Guid conflictId, Process proc)
    {
        // Killing (Withdraw) and a process exiting on its own (this handler) can race for the same
        // Process object from two threads — sharing _lock with Withdraw serializes Kill/Dispose
        // against each other instead of leaving them to run concurrently and unguarded.
        lock (_lock)
        {
            if (_live.TryGetValue(conflictId, out var held) && ReferenceEquals(held.Proc, proc))
                _live.Remove(conflictId);
            try { proc.Dispose(); } catch { /* already gone */ }
        }
    }

    /// <summary>
    /// Take a stale popup off the screen. Tries the documented close-by-id call first — outside the
    /// lock, since it only talks to the session bus and never touches shared state — then always
    /// falls back to dropping the connection that owns it (the mechanism this call was originally
    /// verified against), whether or not an id was ever captured or the close-by-id call succeeded.
    /// </summary>
    private void Withdraw(LiveNotification live)
    {
        uint? id;
        lock (_lock) id = live.NotificationId;
        if (id is { } notificationId) CloseById(notificationId);

        lock (_lock)
        {
            try { if (!live.Proc.HasExited) live.Proc.Kill(); } catch { /* exited on its own; fine */ }
            try { live.Proc.Dispose(); } catch { /* already disposed by Exited */ }
        }
    }

    /// <summary>Best-effort <c>org.freedesktop.Notifications.CloseNotification</c> — the documented
    /// way to withdraw a specific notification, vs. Withdraw()'s connection-drop fallback above.
    /// Never throws (ProcessRunner.Run), and its result is never checked: Withdraw() always also
    /// kills the process regardless, so a missing/erroring gdbus here just means one fewer path did
    /// the job rather than none.</summary>
    private static void CloseById(uint id) =>
        ProcessRunner.Run("gdbus",
            [
                "call", "--session",
                "--dest", "org.freedesktop.Notifications",
                "--object-path", "/org/freedesktop/Notifications",
                "--method", "org.freedesktop.Notifications.CloseNotification",
                id.ToString(),
            ],
            TimeSpan.FromSeconds(2));

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
            psi.ArgumentList.Add(_conflictsUrl);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("ConflictNotifier.OpenConflictsPage", ex);
        }
    }

    public void Dispose()
    {
        List<LiveNotification> live;
        lock (_lock)
        {
            live = _live.Values.ToList();
            _live.Clear();
            _notified.Clear();
        }
        // A notification whose owner is gone can no longer deliver its own button, so leaving it on
        // screen would only offer an action nothing answers.
        foreach (var l in live) Withdraw(l);
    }
}
