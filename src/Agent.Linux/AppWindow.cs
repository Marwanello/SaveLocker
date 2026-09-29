using System.Diagnostics;
using SaveLocker.Agent;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Opens the agent UI in its own window on this desktop session — the one place both
/// <c>savelocker open</c> (the KDE-menu launcher) and a notification's button go through, so a click
/// lands in the app window rather than a fresh browser tab. What to run is
/// <see cref="AppWindowPlanner"/>'s; this is the probing and the process start.
/// </summary>
public static class AppWindow
{
    public static AppWindowPlan Plan(string url) =>
        AppWindowPlanner.Choose(url, OnPath, FlatpakInstalled);

    /// <summary>
    /// Start the window. False — without starting anything — when there is no desktop session to draw it
    /// on (a headless box, an SSH shell), so the caller can say so instead of launching a program that has
    /// nowhere to show itself.
    /// </summary>
    public static bool TryOpen(string url, out AppWindowPlan? plan)
    {
        plan = null;
        if (!DesktopEnvironment.Detect().HasSessionBus) return false;

        plan = Plan(url);
        var psi = new ProcessStartInfo(plan.FileName) { UseShellExecute = false };
        foreach (var a in plan.Args) psi.ArgumentList.Add(a);
        // This process's own display variables can be missing or left over from Game Mode even while a
        // desktop session is running; see DesktopEnvironment.ApplySessionEnv.
        DesktopEnvironment.ApplySessionEnv(psi.Environment);
        try { return Process.Start(psi) is not null; }
        catch (Exception ex)
        {
            AgentLogger.LogException("AppWindow.TryOpen", ex);
            return false;
        }
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
