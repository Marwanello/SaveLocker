using System.Diagnostics;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Runs a process and returns its exit code and both streams. Never throws — a missing binary or
/// an unschedulable child (a container, a minimal chroot) simply reports the exit code as -1.
/// <para>
/// Both streams are drained with <b>concurrent</b> async reads before the wait. Reading one to the
/// end and only then waiting deadlocks the moment the other fills its pipe buffer: the child blocks
/// writing, the parent blocks reading, and neither moves.
/// </para>
/// <para>
/// The default overload waits indefinitely, matching <c>systemctl</c>'s own behaviour. The timed
/// overload exists for a process that can accept a connection and then never actually finish —
/// <c>gdbus</c> probing a stale D-Bus session bus is the one caller that needs it — and kills the
/// child rather than let a caller like <c>doctor</c> hang on something outside this process's
/// control.
/// </para>
/// </summary>
internal static class ProcessRunner
{
    public static (int ExitCode, string StdOut, string StdErr) Run(string file, params string[] args) =>
        Run(file, args, Timeout.InfiniteTimeSpan);

    public static (int ExitCode, string StdOut, string StdErr) Run(string file, string[] args, TimeSpan timeout)
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
            if (!p.WaitForExit(timeout))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return (-1, "", "");
            }
            return (p.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        catch (Exception)
        {
            return (-1, "", "");
        }
    }
}
