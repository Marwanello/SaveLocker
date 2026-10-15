using SaveLocker.Agent;
using SaveLocker.Shared;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// The application-menu entry that runs <c>savelocker open</c>.
/// <para>
/// <c>install.sh</c> writes it, but an agent that updates itself never runs install.sh — so every
/// install older than the entry would go without it for good. The daemon therefore offers it once per
/// install. Both write the same text: <c>packaging/linux/savelocker.desktop</c>, embedded here and
/// rendered by install.sh with <c>sed</c>, with only the install prefix substituted — the rule the
/// systemd unit already follows (<see cref="SystemdAutoStart"/>), for the same reason.
/// </para>
/// </summary>
public static class DesktopEntry
{
    public const string FileName = "savelocker.desktop";

    /// <summary>Left in the state directory once the entry has been offered, whether or not it was
    /// written: someone who then deletes the menu entry has made a choice, and it is kept.</summary>
    public const string MarkerName = "desktop-entry-offered";

    private const string Resource = "SaveLocker.Agent.Linux.savelocker.desktop";

    /// <summary>The entry's text for an agent installed at <paramref name="prefix"/>.</summary>
    public static string Render(string prefix)
    {
        using var stream = typeof(DesktopEntry).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException("The embedded savelocker.desktop is missing from this build.");
        using var reader = new StreamReader(stream);
        // LF whatever the checkout the build ran in used: this file is read by a Linux desktop.
        return reader.ReadToEnd().Replace("\r\n", "\n").Replace("@PREFIX@", prefix);
    }

    /// <summary>
    /// Write the entry if it is missing and has never been offered from this state directory, or
    /// bring one this agent wrote up to date (the entry learned <c>savelocker://</c> after it first
    /// shipped). One that does not run this install's <c>open</c> is someone else's and is left alone.
    /// True when it was written. Never throws: a menu entry is a bonus, like the auto-start unit.
    /// </summary>
    public static bool OfferOnce(string prefix, string applicationsDir, string stateDir, Action<string> log)
    {
        try
        {
            var marker = Path.Combine(stateDir, MarkerName);
            var entry = Path.Combine(applicationsDir, FileName);
            var current = Render(prefix);

            if (File.Exists(entry))
            {
                var existing = File.ReadAllText(entry);
                if (!File.Exists(marker)) File.WriteAllText(marker, "");
                if (existing == current || !existing.Contains($"Exec={prefix}/savelocker open", StringComparison.Ordinal))
                    return false;
                File.WriteAllText(entry, current);
                log($"Updated SaveLocker's application-menu entry ({entry}).");
                return true;
            }

            if (File.Exists(marker)) return false;
            Directory.CreateDirectory(applicationsDir);
            File.WriteAllText(entry, current);
            File.WriteAllText(marker, "");
            log($"Added SaveLocker to the application menu ({entry}).");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"Could not add SaveLocker to the application menu: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The daemon's call. Only the installed agent gets a menu entry: a build tree, a test rig's copy
    /// or a <c>--config</c> run is not what a menu should open, and must not leave one behind.
    /// </summary>
    public static void OfferForInstalledAgent(AgentConfig config, Action<string> log)
    {
        if (!OperatingSystem.IsLinux()) return;
        var prefix = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var installed = Path.TrimEndingDirectorySeparator(AgentConfig.DefaultDir);
        if (!string.Equals(prefix, installed, StringComparison.Ordinal) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(config.StateDir), installed, StringComparison.Ordinal))
            return;

        // Beside the install prefix: ~/.local/share/SaveLocker -> ~/.local/share/applications, which is
        // where install.sh puts it.
        var applications = Path.Combine(Path.GetDirectoryName(prefix)!, "applications");
        if (OfferOnce(prefix, applications, config.StateDir, log)) RegisterLinkHandler(applications);
    }

    /// <summary>
    /// Make this entry the opener for <c>savelocker://</c> links, as install.sh does: refresh the
    /// desktop's MIME cache and name it the scheme's default. Both tools are optional extras of a
    /// desktop install; without them a browser asks which program to use, which still works.
    /// </summary>
    public static void RegisterLinkHandler(string applicationsDir)
    {
        ProcessRunner.Run("update-desktop-database", [applicationsDir], TimeSpan.FromSeconds(10));
        ProcessRunner.Run("xdg-mime", ["default", FileName, "x-scheme-handler/" + JoinLinks.Scheme], TimeSpan.FromSeconds(10));
    }
}
