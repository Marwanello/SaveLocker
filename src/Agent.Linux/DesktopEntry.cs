using SaveLocker.Agent;

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
    /// Write the entry if it is missing and has never been offered from this state directory. True
    /// when it was written. Never throws: a menu entry is a bonus, like the auto-start unit.
    /// </summary>
    public static bool OfferOnce(string prefix, string applicationsDir, string stateDir, Action<string> log)
    {
        try
        {
            var marker = Path.Combine(stateDir, MarkerName);
            if (File.Exists(marker)) return false;

            var entry = Path.Combine(applicationsDir, FileName);
            var wrote = false;
            if (!File.Exists(entry))
            {
                Directory.CreateDirectory(applicationsDir);
                File.WriteAllText(entry, Render(prefix));
                wrote = true;
                log($"Added SaveLocker to the application menu ({entry}).");
            }
            File.WriteAllText(marker, "");
            return wrote;
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
        OfferOnce(prefix, Path.Combine(Path.GetDirectoryName(prefix)!, "applications"), config.StateDir, log);
    }
}
