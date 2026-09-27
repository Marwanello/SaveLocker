using System.Diagnostics;
using SaveLocker.Shared;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Linux delivery for <see cref="NotificationCenter"/>: a best-effort freedesktop notification with
/// one action button that opens the agent UI at the exact screen in the default browser. It began as
/// the conflict notifier (rung 3 of the desktop/headless escalation ladder,
/// tasks/conflict-resolution-ui/plan.md, Phase 9) and is that same code, generalised — what to say and
/// when to say it is now <see cref="NotificationCenter"/>'s and <see cref="NoticeCatalog"/>'s, shared
/// with Windows. Genuinely optional: the console's bell, <c>doctor</c> and the CLI already guarantee
/// every one of these events is discoverable whether or not this ever fires. Every failure mode here
/// — a missing <c>notify-send</c>, an unreachable bus, a daemon that ignores the call — collapses to
/// "notification not shown," logged, never a crash and never a hang:
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
/// it prints the invoked action key straight to stdout — so no separate <c>gdbus monitor</c> is
/// needed to catch the click (a second connection, which could never have owned the notification
/// anyway). <c>gdbus</c> is still what <see cref="DesktopEnvironment"/>'s probe uses to ask whether a
/// notification daemon is there at all; that part was never in question.
/// </para>
/// <para>
/// Deliberately ONE action, where the design's GNOME mock-up draws two ("Install now / Later"). The
/// single-action call is the exact shape confirmed working on real hardware, a second button only
/// duplicates the notification's own dismiss, and a call verified end to end is not the place to add
/// an untested flag. Deliberately does NOT bring the Deck's Game Mode screen to the foreground on
/// click either — that is Phase 8's screen of the conflict-resolution plan, which does not exist yet.
/// </para>
/// </summary>
public sealed class DesktopNotifier : INotificationPresenter, IDisposable
{
    /// <summary>The action's key, which <c>notify-send --wait</c> prints on stdout when clicked.</summary>
    private const string ActionKey = "view";

    private readonly string _uiBaseUrl;
    private readonly object _lock = new();

    /// <summary>
    /// The still-running <c>notify-send --wait</c> per notice key, plus the notification id it prints
    /// via <c>--print-id</c> once the daemon assigns one. <see cref="Withdraw"/> uses both: the
    /// documented <c>CloseNotification(id)</c> call when an id was captured, and killing the process —
    /// the same ownership rule that broke the first implementation, kept as the always-available
    /// fallback — either way. A conflict resolved from the dashboard, the CLI or another machine takes
    /// its now-stale notification off this screen instead of leaving a button that resolves nothing.
    /// Whether something is still <i>announced</i> is <see cref="NotificationCenter"/>'s to know, not
    /// this dictionary's: a notification the user dismissed leaves the center's state alone, so the
    /// next poll cannot put the same popup back.
    /// </summary>
    private readonly Dictionary<string, LiveNotification> _live = new();

    private sealed class LiveNotification
    {
        public required Process Proc { get; init; }
        public uint? NotificationId;
    }

    /// <param name="uiBaseUrl">The agent UI's root, e.g. <c>http://127.0.0.1:5178/</c> — a button
    /// opens a hash route on it.</param>
    public DesktopNotifier(string uiBaseUrl) => _uiBaseUrl = uiBaseUrl;

    /// <summary>
    /// How long a probe that found no notification daemon is believed. A notice nothing could show is
    /// tried again by whatever raises it next — every poll for a conflict, and an engine fault each
    /// time it recurs, which includes the pre-launch check the Decky plugin asks the daemon for — and
    /// each retry would re-run the probe, a <c>gdbus</c> process bounded at 2 s, on that game's launch.
    /// Only "no" is remembered: "yes" is re-checked every time, so a daemon that has gone away is never
    /// assumed, and a desktop that appears is noticed within this window.
    /// </summary>
    private static readonly TimeSpan NoDaemonRecheck = TimeSpan.FromSeconds(30);
    private DateTime _noDaemonUntil = DateTime.MinValue; // under _lock

    /// <summary>
    /// Starts one <c>notify-send --wait</c> and returns immediately — the child is what waits, not
    /// this thread. Arguments go through <c>ArgumentList</c>, so a game name containing an apostrophe
    /// or a quote is just text (the escaping the GVariant-literal version had to do by hand does not
    /// arise here at all). The one thing that can hold the caller is the environment probe, which is
    /// bounded to 2 s by <see cref="DesktopEnvironment"/>, only reaches a subprocess when a session
    /// bus actually accepted a connection, and is skipped for <see cref="NoDaemonRecheck"/> after it
    /// found nothing — the callers include the daemon's pre-launch route, so a game's launch must not
    /// pay for it over and over.
    /// </summary>
    public string? Show(AgentNotice notice)
    {
        const string noDaemon = "no desktop notification daemon reachable — see `doctor` or the agent UI.";
        // Only worth asking the environment when there is actually something to say — this is the one
        // point that spawns a process, and the overwhelming common case (nothing new) never gets here.
        lock (_lock)
        {
            if (DateTime.UtcNow < _noDaemonUntil) return noDaemon;
        }
        if (!DesktopEnvironment.Detect().NotificationDaemonPresent)
        {
            lock (_lock) _noDaemonUntil = DateTime.UtcNow + NoDaemonRecheck;
            return noDaemon;
        }

        var psi = new ProcessStartInfo("notify-send")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("--app-name=SaveLocker");
        psi.ArgumentList.Add($"--icon={IconFor(notice.Severity)}");
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
        var hasAction = notice.Primary.Kind != NoticeActionKind.None && notice.PrimaryLabel.Length > 0;
        if (hasAction) psi.ArgumentList.Add($"--action={ActionKey}={notice.PrimaryLabel}");
        psi.ArgumentList.Add(notice.Title);
        psi.ArgumentList.Add(notice.Body);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            AgentLogger.LogException($"DesktopNotifier.Show '{notice.Title}'", ex);
            return $"notify-send would not start ({ex.Message})";
        }

        if (proc is null) return "notify-send did not start.";

        // Recorded as live only now that the process has actually started, and before
        // EnableRaisingEvents is set below, so a fast-exiting process can never find _live still
        // missing its own entry when Exited fires.
        var live = new LiveNotification { Proc = proc };
        lock (_lock) _live[notice.Key] = live;

        proc.EnableRaisingEvents = true;
        proc.OutputDataReceived += (_, e) =>
        {
            var line = e.Data?.Trim();
            if (line is null) return;
            // --print-id's one-line id, printed once as soon as the daemon assigns one — everything
            // else on this stream is the action key, printed later and only if the button is clicked.
            if (uint.TryParse(line, out var id)) lock (_lock) live.NotificationId = id;
            else if (string.Equals(line, ActionKey, StringComparison.Ordinal)) Open(notice.Primary);
        };
        proc.Exited += (_, _) => Forget(notice.Key, proc);
        proc.BeginOutputReadLine();

        AgentLogger.Log($"notification sent: '{notice.Title}'.");
        return null;
    }

    private static string IconFor(AgentEventSeverity severity) => severity switch
    {
        AgentEventSeverity.Error => "dialog-error",
        AgentEventSeverity.Warning => "dialog-warning",
        _ => "dialog-information",
    };

    /// <summary>Drop a finished notification's process, without disturbing a newer one for the same
    /// key (there should never be one, but identity is cheaper to check than to reason about).</summary>
    private void Forget(string key, Process proc)
    {
        // Killing (Withdraw) and a process exiting on its own (this handler) can race for the same
        // Process object from two threads — sharing _lock with Withdraw serializes Kill/Dispose
        // against each other instead of leaving them to run concurrently and unguarded.
        lock (_lock)
        {
            if (_live.TryGetValue(key, out var held) && ReferenceEquals(held.Proc, proc))
                _live.Remove(key);
            try { proc.Dispose(); } catch { /* already gone */ }
        }
    }

    /// <summary>
    /// Take a stale popup off the screen. Tries the documented close-by-id call first — outside the
    /// lock, since it only talks to the session bus and never touches shared state — then always
    /// falls back to dropping the connection that owns it (the mechanism this call was originally
    /// verified against), whether or not an id was ever captured or the close-by-id call succeeded.
    /// </summary>
    public void Withdraw(string key)
    {
        LiveNotification? live;
        lock (_lock)
        {
            if (!_live.Remove(key, out live)) return;
        }
        Withdraw(live);
    }

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

    /// <summary>
    /// Best-effort <c>org.freedesktop.Notifications.CloseNotification</c> — the documented way to
    /// withdraw a specific notification, vs. Withdraw()'s connection-drop fallback above. Never
    /// throws (ProcessRunner.Run), and its result is never checked: Withdraw() always also kills the
    /// process regardless, so a missing/erroring gdbus here just means one fewer path did the job
    /// rather than none.
    /// <para>
    /// <b>Confirmed on real hardware 2026-09-02, same session as the Phase 9 fix above.</b> The
    /// worry was that a "kill the connection" withdraw relies on undocumented, daemon-specific
    /// behavior — tested directly on the Deck: a backgrounded <c>notify-send --wait --print-id</c>
    /// stayed visible for a deliberate 5s pause (ruling out a race with a too-fast first attempt),
    /// then this exact call made it vanish at that moment and the waiting <c>notify-send</c> exited
    /// on its own (status 0), no <c>Kill()</c> needed. Kept as a fallback rather than a replacement
    /// regardless — this only proves the documented path also works, not that the connection-drop
    /// path (already verified above) stops being necessary on some other daemon.
    /// </para>
    /// </summary>
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
    /// Opens the agent UI at the notice's screen in the default browser — the desktop-session half of
    /// the plan's "action button ... opens the chooser." The browser is the Linux agent's UI, so the
    /// link is the hash route itself (<see cref="NoticeAction.ToUrl"/>); Windows' toast goes through
    /// <c>/open</c> instead, to raise its tray window. Best-effort: a missing `xdg-open` (a bare window
    /// manager with no default-application handler configured) degrades to nothing happening, not a crash.
    /// </summary>
    private void Open(NoticeAction action)
    {
        if (action.ToUrl(_uiBaseUrl) is not { } url) return;

        try
        {
            var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(url);
            // See DesktopEnvironment.ApplySessionEnv: this process's own display and session
            // variables can be missing or left over from Game Mode even while a desktop session is
            // running. xdg-open gets the session's current values from the systemd user manager.
            DesktopEnvironment.ApplySessionEnv(psi.Environment);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("DesktopNotifier.Open", ex);
        }
    }

    public void Dispose()
    {
        List<LiveNotification> live;
        lock (_lock)
        {
            live = _live.Values.ToList();
            _live.Clear();
        }
        // A notification whose owner is gone can no longer deliver its own button, so leaving it on
        // screen would only offer an action nothing answers.
        foreach (var l in live) Withdraw(l);
    }
}
