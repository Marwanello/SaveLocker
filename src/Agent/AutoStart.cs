using Microsoft.Win32;

namespace SaveLocker.Agent;

/// <summary>
/// "Start with Windows" via the per-user Run registry key
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). Per-user means no admin
/// rights are needed; the tray agent launches automatically when the user logs in.
/// The stored command points at the current executable (the apphost exe).
/// </summary>
public sealed class AutoStart : IAutoStart
{
    private const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SaveLocker";

    /// <summary>
    /// The HKCU subkey holding the login entry. SAVELOCKER_RUNKEY_SUBPATH moves it so a test can
    /// exercise the access-denied branch against a throwaway key — denying writes on the real Run
    /// key to prove a point would be a genuinely destructive thing to do to a working machine.
    /// Test-only and unadvertised, like the other three (Decisions.md).
    /// </summary>
    private static readonly string RunKey =
        Environment.GetEnvironmentVariable("SAVELOCKER_RUNKEY_SUBPATH") is { Length: > 0 } custom
            ? custom : DefaultRunKey;

    /// <summary>
    /// True only when the Run entry actually launches <b>this</b> executable.
    ///
    /// <para>
    /// A non-empty value used to be enough, which made the toggle a report about the registry rather
    /// than about the machine's behaviour. Three states look "enabled" that way and are not: an entry
    /// left by an install that has since been removed, one pointing at a path the agent was moved
    /// away from, and one written by a different SaveLocker install. In each case Windows either
    /// launches nothing or launches something else, while Settings shows the box ticked. WA-10.
    /// </para>
    /// </summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is not string stored || string.IsNullOrWhiteSpace(stored))
                return false;

            var registered = Unquote(stored);
            if (registered.Length == 0 || !File.Exists(registered)) return false;

            // Compare canonical paths: the stored value is quoted, and may differ from
            // ProcessPath only by case or by a short/long name form.
            var current = LauncherPath();
            if (string.IsNullOrEmpty(current)) return false;

            return string.Equals(
                Path.GetFullPath(registered), Path.GetFullPath(current),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            // An unreadable Run key is not an enabled auto-start. Fail toward the honest answer.
            AgentLogger.LogException("AutoStart.IsEnabled", ex);
            return false;
        }
    }

    /// <summary>
    /// Add or remove the login entry, and say what happened. The write is read back, because
    /// <c>SetValue</c> returning without throwing is not the same as the entry being there — group
    /// policy and endpoint-protection products both virtualise or revert this key.
    /// </summary>
    public AutoStartResult SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
                return AutoStartResult.Fail(
                    "Windows would not open the per-user startup registry key " +
                    $@"(HKCU\{RunKey}).");

            if (enabled)
            {
                var exe = LauncherPath();
                if (string.IsNullOrEmpty(exe))
                    return AutoStartResult.Fail(
                        "Could not determine the SaveLocker executable to register for startup.");

                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            AgentLogger.LogException("AutoStart.SetEnabled", ex);
            return AutoStartResult.Fail(
                "Windows denied access to the startup registry key. This is usually group policy " +
                "or security software restricting what may run at login.");
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("AutoStart.SetEnabled", ex);
            return AutoStartResult.Fail("Could not change the startup setting: " + ex.Message);
        }

        // The verification, not a formality: the two failures worth catching here are silent ones.
        if (IsEnabled() != enabled)
            return AutoStartResult.Fail(enabled
                ? "The startup entry did not survive being written. Group policy or security " +
                  "software is most likely reverting it."
                : "The startup entry is still present after being removed. Group policy or " +
                  "security software is most likely restoring it.");

        return AutoStartResult.Success();
    }

    /// <summary>Strip the surrounding quotes a Run value carries, and any trailing arguments.</summary>
    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.StartsWith('"'))
        {
            var close = value.IndexOf('"', 1);
            return close > 0 ? value[1..close] : value[1..];
        }
        // Unquoted values cannot carry arguments unambiguously; treat the whole thing as the path.
        return value;
    }

    /// <summary>
    /// The standalone launcher to register. When running as the published exe
    /// (single-file or apphost) <see cref="Environment.ProcessPath"/> is already the
    /// agent exe. Only the dev path "dotnet Agent.dll" is wrong — there ProcessPath is
    /// dotnet.exe with no dll argument, useless for auto-start — so map it to the
    /// apphost exe next to the app.
    /// </summary>
    private static string? LauncherPath()
    {
        var proc = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(proc) &&
            string.Equals(Path.GetFileName(proc), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var apphost = Path.Combine(AppContext.BaseDirectory, "SaveLocker.Agent.exe");
            if (File.Exists(apphost)) return apphost;
        }
        return proc;
    }
}
