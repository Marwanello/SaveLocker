using System.Diagnostics;

namespace SaveLocker.Agent;

/// <summary>
/// The process DCOM starts when a toast button is clicked and no tray is running
/// (<c>LocalServer32 … -ToastActivated</c>, written by <see cref="ToastActivation"/>). It serves the
/// activation, leaves the command where the tray will find it and starts the tray — the click asked
/// for something to happen, and the agent that can do it is not up.
/// <para>
/// Short-lived by construction: it registers, waits for the one expected activation, writes and
/// starts, and exits. The wait is bounded so a stray <c>-ToastActivated</c> launch — the shell
/// starting and abandoning an activation, a double click — cannot leave a process sitting around.
/// </para>
/// </summary>
internal static class ToastActivationHost
{
    /// <summary>How long to stay up for the activation. The shell CoCreates within milliseconds of
    /// starting the server; this is only a ceiling.</summary>
    private static readonly TimeSpan WaitForActivation = TimeSpan.FromSeconds(20);

    private sealed record Click(string Aumid, AgentCommand Command);

    public static int Run(string[] args)
    {
        // AgentLogger resolves its file (and AgentConfig its default dir) from the environment at
        // first use — and a DCOM launch has none. A --config argument means this is not the default
        // install (ToastActivation only writes one when it is not), so point the whole process at
        // that root BEFORE anything logs a line or starts anything: without this, a rig host wrote
        // its own lines into the INSTALLED agent's log, and its StartTray started the installed
        // agent (measured 2026-10-11).
        var configPath = RawOption(args, "--config");
        if (configPath is not null &&
            Path.GetDirectoryName(Path.GetFullPath(configPath)) is { } dir &&
            Directory.GetParent(dir) is { } root)
        {
            Environment.SetEnvironmentVariable("SAVELOCKER_STATE_ROOT", root.FullName);
        }

        configPath ??= AgentConfig.DefaultConfigPath;
        var config = AgentConfig.Load(configPath);

        var clicked = ListenForClick(ToastActivation.HostExePath());
        if (clicked is null)
        {
            AgentLogger.Log("toast activation: no registered identity points at this exe; exiting.");
            return 0;
        }

        Click click;
        try { click = clicked.WaitAsync(WaitForActivation).GetAwaiter().GetResult(); }
        catch (TimeoutException)
        {
            AgentLogger.Log("toast activation: nothing arrived; exiting.");
            return 0;
        }

        if (ToastPresenter.PortFromAumid(click.Aumid) is not { } port)
        {
            AgentLogger.Log($"toast activation: '{click.Aumid}' is not an identity of ours");
            return 0;
        }

        AgentLogger.Log($"toast activation: '{click.Command.Arguments}' while no tray was running — " +
                        "leaving it for the tray and starting it.");
        PendingToastAction.Write(config.StateDir, click.Command);
        StartTray(port, configPath, config.StateDir);
        return 0;
    }

    /// <summary>
    /// Serve every identity this exe is registered for — the DCOM launch names no CLSID, and a
    /// machine can have the installed agent's beside a rig's. Null when nothing registered points at
    /// this exe, so there is nothing an activation could reach and no waiting is warranted.
    /// </summary>
    private static Task<Click>? ListenForClick(string exePath)
    {
        var clicked = new TaskCompletionSource<Click>(TaskCreationOptions.RunContinuationsAsynchronously);

        Action<string, string> handler = (aumid, invokedArgs) =>
        {
            if (ToastPresenter.PortFromAumid(aumid) is null) return;
            if (!AgentCommand.TryParse(invokedArgs, out var command)) return;
            clicked.TrySetResult(new Click(aumid, command));
        };

        var served = false;
        foreach (var (_, clsid) in ToastActivation.OwnedClsids(exePath))
        {
            ToastActivation.Serve(clsid, handler);   // lives to process exit
            served = true;
        }

        return served ? clicked.Task : null;
    }

    private static void StartTray(int port, string configPath, string stateDir)
    {
        var psi = new ProcessStartInfo(ToastActivation.HostExePath()) { UseShellExecute = false };
        if (port != ToastPresenter.DefaultPort)
        {
            // Re-create the rig's environment for the tray being started: the port scopes its API,
            // its mutex and its toast identity, and the state root keeps its logs, WebView2 profile
            // and manifest cache inside the rig. Neither survives a DCOM launch on its own.
            psi.ArgumentList.Add("--config");
            psi.ArgumentList.Add(configPath);
            psi.Environment["SAVELOCKER_TRAY_PORT"] = port.ToString();
            if (Directory.GetParent(stateDir) is { } root)
                psi.Environment["SAVELOCKER_STATE_ROOT"] = root.FullName;
        }

        try { Process.Start(psi); }
        catch (Exception ex) { AgentLogger.LogException("ToastActivationHost.StartTray", ex); }
    }

    /// <summary>The option's value from the raw command line — this process is parsed by nobody else
    /// (<c>-ToastActivated</c> is not a command CliArgs would accept), so it reads its own one.</summary>
    private static string? RawOption(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-" + name.TrimStart('-'), StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
