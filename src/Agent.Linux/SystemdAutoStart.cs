using System.Diagnostics;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// "Start on login" via a <c>systemd --user</c> unit — the Linux answer to the HKCU Run key.
///
/// The unit lives in the user's home, never <c>/usr</c>: SteamOS's rootfs is immutable and is
/// wiped on every update, so a system-wide install would silently vanish (Decisions.md §5).
/// </summary>
public sealed class SystemdAutoStart : IAutoStart
{
    private const string Unit = "savelocker.service";

    private static string UnitDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "systemd", "user");

    private static string UnitPath => Path.Combine(UnitDir, Unit);

    public bool IsEnabled()
    {
        if (!File.Exists(UnitPath)) return false;
        var (exit, stdout, _) = Run("systemctl", "--user", "is-enabled", Unit);
        return exit == 0 && stdout.Trim() == "enabled";
    }

    public AutoStartResult SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                Directory.CreateDirectory(UnitDir);
                File.WriteAllText(UnitPath, UnitFile());
                Run("systemctl", "--user", "daemon-reload");
                var (exit, _, err) = Run("systemctl", "--user", "enable", "--now", Unit);
                if (exit != 0)
                {
                    AgentLogger.Log($"systemctl --user enable failed (exit {exit}): {err.Trim()}");
                    return AutoStartResult.Fail(Reason("enable", exit, err));
                }
                return AutoStartResult.Success();
            }

            // The exit code is the answer, not a formality. Disabling fails for real — no user bus
            // (a Deck reached over SSH without a login session is the common one), systemd absent —
            // and returning true regardless made both the CLI and the Game Mode toggle report a
            // service as disabled while it was still enabled and still running.
            var (disableExit, _, disableErr) = Run("systemctl", "--user", "disable", "--now", Unit);
            if (disableExit != 0)
            {
                AgentLogger.Log($"systemctl --user disable failed (exit {disableExit}): {disableErr.Trim()}");
                return AutoStartResult.Fail(Reason("disable", disableExit, disableErr));
            }
            return AutoStartResult.Success();
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("SystemdAutoStart.SetEnabled", ex);
            return AutoStartResult.Fail("Could not change the startup setting: " + ex.Message);
        }
    }

    /// <summary>
    /// A reason the user can act on. Exit -1 is <see cref="Run"/>'s "could not even start
    /// systemctl", which on a Deck reached over SSH means no user bus rather than a broken unit —
    /// a distinction worth making, because the two have completely different fixes.
    /// </summary>
    private static string Reason(string verb, int exit, string stderr)
    {
        var detail = stderr.Trim();
        if (exit == -1)
            return $"Could not run systemctl to {verb} the service. If this is a remote shell, " +
                   "there may be no user session bus — try from a desktop session.";
        return detail.Length > 0
            ? $"systemctl could not {verb} the service: {detail}"
            : $"systemctl could not {verb} the service (exit {exit}).";
    }

    private static string UnitFile()
    {
        var exe = Environment.ProcessPath ?? "savelocker";
        return $"""
        [Unit]
        Description=SaveLocker agent
        After=network-online.target

        [Service]
        Type=simple
        ExecStart={exe} daemon
        Restart=on-failure
        RestartSec=10

        [Install]
        WantedBy=default.target

        """;
    }

    /// <summary>
    /// Runs the process (in practice, systemctl) and returns its exit code and both streams. Never
    /// throws — a box without systemd (a container, a minimal chroot) simply reports the toggle as
    /// unavailable.
    /// <para>
    /// Both streams are drained with <b>concurrent</b> async reads before the wait. Reading one to
    /// the end and only then waiting deadlocks the moment the other fills its pipe buffer: the child
    /// blocks writing, the parent blocks reading, and neither moves. systemctl writes its failure
    /// reason to stderr, which is exactly the stream that was redirected and never read.
    /// </para>
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return (-1, "", "");

            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            return (p.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        catch (Exception)
        {
            return (-1, "", "");
        }
    }
}
