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

    /// <summary>
    /// Is this user's systemd manager kept alive across logout (<c>loginctl enable-linger</c>)?
    /// <para>
    /// It decides what a user has to be told to apply a staged update, and nothing else here cares.
    /// With lingering <b>off</b> — the default, and what <c>install.sh</c> assumes — switching a Deck
    /// to Desktop mode and back is a logout/login, so the user manager and <c>savelocker.service</c>
    /// cycle with it and <c>ExecStartPre</c> performs the swap. With it <b>on</b> the manager
    /// persists across both and the unit is never touched, so only a reboot does it. Three KB
    /// articles recommend <c>enable-linger</c> as the fix for "the agent stops when I log out", so
    /// telling those users to switch modes would be telling them to do nothing.
    /// </para>
    /// <para>
    /// Read from logind's own marker file rather than <c>loginctl show-user</c>: that is what logind
    /// keys on, it costs no process, and it answers on a machine with no session bus at all — the
    /// SSH case the rest of this class already has to survive.
    /// </para>
    /// </summary>
    internal static bool LingerEnabled()
    {
        try
        {
            var user = Environment.UserName;
            return !string.IsNullOrEmpty(user) &&
                   File.Exists(Path.Combine("/var/lib/systemd/linger", user));
        }
        catch { return false; }
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

    /// <summary>
    /// The packaged unit, with the executable path pointed at whatever this agent actually is.
    /// <para>
    /// It is read from the embedded copy of <c>packaging/linux/savelocker.service</c> rather than
    /// written out here, because there are two writers of this file — <c>install.sh</c> drops the
    /// packaged one, this writes its own — and they had drifted: the packaged unit grew an
    /// <c>ExecStartPre</c> update hook and a set of hardening directives that the generated one knew
    /// nothing about, so whether a Deck got them depended on which path had last touched the unit.
    /// </para>
    /// </summary>
    internal static string UnitFile()
    {
        var template = ReadTemplate();
        var exe = AgentExecutable();

        // %h/... is right for a standard install and is what the packaged file says. It is wrong for
        // an agent living anywhere else — a build tree, a second copy under test — so when we know
        // our own path, we use it.
        return string.IsNullOrEmpty(exe)
            ? template
            : template.Replace(PackagedExecPath, exe, StringComparison.Ordinal);
    }

    /// <summary>
    /// This agent's own launcher.
    /// <para>
    /// The apphost beside our assemblies is preferred over <see cref="Environment.ProcessPath"/>,
    /// because under <c>dotnet savelocker.dll</c> the process path is the <b>dotnet host</b> — and a
    /// unit written from that says <c>ExecStart=/usr/bin/dotnet daemon</c>, which starts nothing at
    /// all. The installed agent is a self-contained apphost, so the two agree there; it is running
    /// from a build tree that tells them apart, which is exactly where a broken unit is written and
    /// not noticed.
    /// </para>
    /// </summary>
    private static string? AgentExecutable()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "savelocker");
        if (File.Exists(beside)) return Path.GetFullPath(beside);
        return Environment.ProcessPath;
    }

    /// <summary>The path the packaged unit names, and therefore the string to substitute.</summary>
    private const string PackagedExecPath = "%h/.local/share/SaveLocker/savelocker";

    private static string ReadTemplate()
    {
        using var stream = typeof(SystemdAutoStart).Assembly
            .GetManifestResourceStream("SaveLocker.Agent.Linux.savelocker.service")
            ?? throw new InvalidOperationException(
                "The embedded savelocker.service is missing from this build.");
        using var reader = new StreamReader(stream);
        // systemd does not care about CRLF, but a unit file that round-trips through a Windows
        // checkout and back should still read as the file in packaging/.
        return reader.ReadToEnd().Replace("\r\n", "\n");
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
