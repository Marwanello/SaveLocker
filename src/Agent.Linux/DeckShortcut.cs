using SaveLocker.Agent;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// The SHIPPED Steam entry — "SaveLocker", running <c>savelocker open</c> (the agent UI, controller
/// support included) — and the policy that adds or repairs it.
///
/// <para>
/// <b>How and when it appears.</b> The daemon runs <see cref="EnsureAtStartup"/> at every start:
/// after install, after an update applies (an update is a restart), and at every boot/login. When
/// the conditions hold it writes the entry straight into Steam's own <c>shortcuts.vdf</c> — the
/// same file Steam's "Add a Non-Steam Game" writes — so it shows up in the library (and therefore
/// Game Mode) with no Desktop Mode work. The conditions are deliberately timid:
/// </para>
/// <list type="bullet">
/// <item>the INSTALLED agent only (a build tree or a <c>--config</c> run must not put an entry in
/// someone's real library),</item>
/// <item>Steam installed and signed in at least once,</item>
/// <item>Steam NOT currently running — it rewrites <c>shortcuts.vdf</c> when it exits, with
/// whatever its memory holds, so an edit made underneath it is silently lost. The right moment is
/// a boot where the agent beats Steam, which on SteamOS it does; anything else tries again at the
/// next start, and <c>savelocker deck-shortcut add</c> is there for doing it by hand,</item>
/// <item>absent AND never offered before — someone who removes the entry has made a choice, and it
/// is kept (the <see cref="MarkerName"/> marker in the state dir, the same pattern as the
/// application-menu entry),</item>
/// <item>an entry that exists but has drifted (an older Deck's shortcut still runs <c>ui</c>) is
/// repaired IN PLACE — name, icon and any custom LaunchOptions stay, the command becomes
/// <c>open</c>, and hand-set artwork is renamed along with the AppID so it keeps applying.</item>
/// </list>
///
/// <para>
/// A shortcut with the same name that does not point at savelocker is never touched; adding by
/// hand with the name taken refuses the same way. Steam only lists a new or changed shortcut after
/// it restarts, so the log line says so.
/// </para>
/// </summary>
public static class DeckShortcut
{
    /// <summary>Left in the state directory once the shortcut has been offered, whether or not it was
    /// written: someone who then removes it from Steam has made a choice, and it is kept.</summary>
    public const string MarkerName = "steam-shortcut-offered";

    /// <summary>What the start-up pass should do, given the file's state and the marker.</summary>
    internal enum Decision { Nothing, Add, Repair }

    /// <summary>The whole policy, pure — so every case is pinned by a test without a Steam library.</summary>
    internal static Decision Decide(SteamShortcutWriter.ShortcutPresence presence, bool offeredBefore) => presence switch
    {
        SteamShortcutWriter.ShortcutPresence.Current => Decision.Nothing,
        SteamShortcutWriter.ShortcutPresence.Foreign => Decision.Nothing,
        SteamShortcutWriter.ShortcutPresence.Stale => Decision.Repair,
        SteamShortcutWriter.ShortcutPresence.Missing => offeredBefore ? Decision.Nothing : Decision.Add,
        _ => Decision.Nothing,
    };

    /// <summary>Only the installed agent may put an entry in a real Steam library — the same rule
    /// the application-menu entry follows (<see cref="DesktopEntry.OfferForInstalledAgent"/>).</summary>
    internal static bool IsInstalledAgent(string baseDir, string stateDir)
    {
        var installed = Path.TrimEndingDirectorySeparator(AgentConfig.DefaultDir);
        return string.Equals(Path.TrimEndingDirectorySeparator(baseDir), installed, StringComparison.Ordinal)
            && string.Equals(Path.TrimEndingDirectorySeparator(stateDir), installed, StringComparison.Ordinal);
    }

    /// <summary>Is Steam running right now? Its exit rewrite is what the start-up pass must not race,
    /// so this gate is about the file, not about performance.</summary>
    public static bool SteamRunning() =>
        ProcessRunner.Run("pgrep", ["-x", "steam"], TimeSpan.FromSeconds(2)).ExitCode == 0;

    /// <summary>
    /// The daemon's start-up pass; see the class doc for how and when. <paramref name="paintArt"/>
    /// receives a new entry's AppID and paints its four library pictures — kept a delegate so this
    /// file stays testable away from the art pipeline (the daemon passes the real one).
    /// </summary>
    public static void EnsureAtStartup(AgentConfig config, Action<string> log, Func<int, bool>? paintArt = null)
    {
        try
        {
            if (!OperatingSystem.IsLinux()) return;
            if (!IsInstalledAgent(AppContext.BaseDirectory, config.StateDir)) return;

            var vdfPath = SteamShortcutWriter.FindShortcutsVdf();
            if (vdfPath is null) return;

            var marker = Path.Combine(config.StateDir, MarkerName);
            var presence = SteamShortcutWriter.InspectAt(vdfPath, SteamShortcutWriter.DeckApp, out _);
            var decision = Decide(presence, File.Exists(marker));
            if (decision == Decision.Nothing) return;

            if (SteamRunning())
            {
                log("Steam is running - the 'SaveLocker' Steam shortcut is added at the next start with Steam closed.");
                return;
            }

            if (decision == Decision.Repair)
            {
                if (SteamShortcutWriter.RetargetAt(vdfPath, SteamShortcutWriter.DeckApp))
                    log("Updated the 'SaveLocker' Steam shortcut to open the agent UI - restart Steam to see it.");
                return;
            }

            var prefix = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            var appId = SteamShortcutWriter.AddTo(vdfPath, prefix, false, SteamShortcutWriter.DeckApp, null, null);
            if (appId is null) return;
            File.WriteAllText(marker, "");
            if (paintArt is not null)
            {
                try { paintArt(appId.Value); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log($"The 'SaveLocker' shortcut was added; its library art was not written: {ex.Message}");
                }
            }
            log("Added 'SaveLocker' to your Steam library - it appears in Game Mode after Steam restarts.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"Could not add the 'SaveLocker' Steam shortcut: {ex.Message}");
        }
    }
}
