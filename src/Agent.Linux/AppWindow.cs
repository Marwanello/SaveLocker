using System.Diagnostics;
using SaveLocker.Agent;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Opens the agent UI in its own window on this desktop session — the one place both
/// <c>savelocker open</c> (the KDE-menu launcher, <see cref="TryOpen"/>) and a notification's button
/// (<see cref="OpenFromDaemon"/>) go through, so a click lands in the app window rather than a fresh
/// browser tab. What to run is <see cref="AppWindowPlanner"/>'s; this is the probing and the process start.
/// </summary>
public static class AppWindow
{
    public static AppWindowPlan Plan(string url) =>
        AppWindowPlanner.Choose(url, OnPath, FlatpakInstalled);

    /// <summary>
    /// Start the window. False — without starting anything — when there is no display to draw it on (a
    /// headless box, an SSH shell with no desktop session behind it), so the caller can say so instead
    /// of launching a program that has nowhere to show itself.
    /// <para>
    /// The session that launched this process wins when it has a display of its own — the menu entry
    /// from Desktop Mode's KDE, or the Steam shortcut from Game Mode's gamescope. The user manager's
    /// session values are the fallback for a process that has none (an SSH shell), and they can be a
    /// stale snapshot of the OTHER mode: preferring them unconditionally is what once sent a
    /// Game-Mode-launched window to a compositor that was not running.
    /// </para>
    /// </summary>
    public static bool TryOpen(string url, out AppWindowPlan? plan)
    {
        plan = null;
        if ((DesktopEnvironment.OwnSessionStartInfo() ?? DesktopEnvironment.DesktopStartInfo()) is not { } psi) return false;

        plan = Plan(url);
        psi.FileName = plan.FileName;
        foreach (var a in plan.Args) psi.ArgumentList.Add(a);
        try { return Process.Start(psi) is not null; }
        catch (Exception ex)
        {
            AgentLogger.LogException("AppWindow.TryOpen", ex);
            return false;
        }
    }

    /// <summary>
    /// The daemon's way in, for a notification's button. A browser the daemon started itself would be a
    /// child of <c>savelocker.service</c> — in its cgroup and behind its sandbox, see
    /// <see cref="AppWindowPlanner.ThroughUserManager"/> — so under the unit the app window is started
    /// by the user manager instead. If that cannot be done, or there is no Chromium-family browser, the
    /// link goes to <c>xdg-open</c> with the session's environment: the desktop starts the browser it
    /// hands that to, and that path is the one verified on a Deck. No display check here — a button that
    /// was clicked was on a screen.
    /// </summary>
    public static void OpenFromDaemon(string url)
    {
        var plan = Plan(url);
        if (plan.IsAppWindow && DesktopEnvironment.RunningAsSystemdUnit)
        {
            var detached = AppWindowPlanner.ThroughUserManager(plan);
            var (exit, _, stderr) = ProcessRunner.Run(detached.FileName, detached.Args.ToArray(), TimeSpan.FromSeconds(5));
            if (exit == 0) return;
            AgentLogger.Log($"Could not open the app window through the user manager (systemd-run exit {exit}: " +
                            $"{stderr.Trim()}); using the default browser instead.");
            plan = AppWindowPlanner.DefaultBrowser(url);
        }

        var psi = new ProcessStartInfo(plan.FileName) { UseShellExecute = false };
        foreach (var a in plan.Args) psi.ArgumentList.Add(a);
        DesktopEnvironment.ApplySessionEnv(psi.Environment);
        Process.Start(psi);
    }

    private static bool OnPath(string binary)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, binary);
                if (File.Exists(candidate)) return true;
            }
            catch { /* an unreadable PATH entry is not a browser */ }
        }
        return false;
    }

    /// <summary>`flatpak info` answers 0 only for an installed application; bounded so a wedged flatpak
    /// cannot hang a launcher click.</summary>
    private static bool FlatpakInstalled(string id)
    {
        try
        {
            if (!OnPath("flatpak")) return false;
            var psi = new ProcessStartInfo("flatpak") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            psi.ArgumentList.Add("info");
            psi.ArgumentList.Add(id);
            using var p = Process.Start(psi);
            if (p is null) return false;
            if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
