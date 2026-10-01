using System.Net.Sockets;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// What kind of session this process is running in right now — Deck Game Mode, a KDE Desktop Mode
/// session, a plain desktop Linux box, an SSH shell, or the <c>systemd --user</c> unit itself. Every
/// field answers one narrow question; nothing here decides anything by itself
/// (tasks/conflict-resolution-ui/plan.md, Phase 5) — it exists so a later caller (the launch wrapper
/// bringing a UI to the foreground, an optional desktop notification) can tell whether a richer
/// surface than the CLI is actually reachable before trying to use one.
/// </summary>
/// <param name="HasGraphicalSession">A Wayland or X11 display is set — <i>some</i> graphical session
/// exists (true in both Game Mode and Desktop Mode; false over a bare SSH shell).</param>
/// <param name="HasSessionBus">A D-Bus session bus is set <b>and</b> its socket actually accepts a
/// connection — not just that the environment variable is present.</param>
/// <param name="NotificationDaemonPresent">Something currently owns
/// <c>org.freedesktop.Notifications</c> on that bus. Deliberately distinct from
/// <see cref="HasSessionBus"/>: a bus can exist with nothing listening for notifications on it — two
/// different reasons the same feature can't fire.
/// <para>
/// <b>Correction, confirmed on real hardware 2026-08-31:</b> this doc comment used to claim "Game
/// Mode has no session bus at all." That was never actually verified and turned out to be wrong — a
/// `doctor` run over SSH while the Deck was sitting in Game Mode (Gamescope) reported both
/// <see cref="HasSessionBus"/> and this field as true. SteamOS keeps one persistent per-user D-Bus
/// bus alive via <c>systemd --user</c> regardless of which shell (or which graphical mode) reaches
/// it, so an SSH login shares the same bus a running Gamescope session already has, with something
/// already claiming <c>org.freedesktop.Notifications</c> ownership on it. What that claimant actually
/// is, and whether a real <c>Notify</c> call renders anything visible in Game Mode rather than being
/// silently accepted and dropped, is still unconfirmed — see Phase 9 in
/// tasks/conflict-resolution-ui/plan.md.</para></param>
/// <param name="IsInteractiveTty">This process has a real terminal attached, as opposed to running
/// under a service manager with its standard streams redirected.</param>
/// <param name="RunningAsSystemdUnit">This process is the <c>systemd --user</c> unit's own main
/// process, not an interactive CLI invocation — REPO_MAP already notes the daemon runs under
/// <c>systemd --user</c> exclusively, so this is really "am I the unit or a human at a
/// terminal."</param>
public readonly record struct DesktopSessionInfo(
    bool HasGraphicalSession,
    bool HasSessionBus,
    bool NotificationDaemonPresent,
    bool IsInteractiveTty,
    bool RunningAsSystemdUnit);

/// <summary>
/// Rung 1 of the Linux desktop/headless escalation ladder
/// (tasks/conflict-resolution-ui/reference/03-platform-ux-flows.md). Cheap enough to call fresh
/// per-decision rather than caching — a Deck switching between Game Mode and Desktop Mode must never
/// be read stale, and every check here is either an environment-variable read or one short-lived
/// subprocess.
/// </summary>
public static class DesktopEnvironment
{
    /// <summary>
    /// A start-info carrying the desktop session's environment (<see cref="ApplySessionEnv"/>), for a
    /// program that shows something there — or null when that environment names no display, so a
    /// caller can say "there is no desktop here" instead of starting a program with nowhere to draw.
    /// The caller sets <c>FileName</c> and the arguments.
    /// </summary>
    public static System.Diagnostics.ProcessStartInfo? DesktopStartInfo()
    {
        var psi = new System.Diagnostics.ProcessStartInfo { UseShellExecute = false };
        ApplySessionEnv(psi.Environment);
        return AppWindowPlanner.HasDisplay(psi.Environment) ? psi : null;
    }

    /// <summary>
    /// Hand a file to the desktop's default handler. False — without trying — when there is no
    /// display to show it on (a headless box, an SSH tunnel into the agent UI), so the caller can
    /// show the path instead.
    /// </summary>
    public static bool TryOpenFile(string path)
    {
        if (DesktopStartInfo() is not { } psi) return false;
        psi.FileName = "xdg-open";
        psi.ArgumentList.Add(path);
        return System.Diagnostics.Process.Start(psi) is not null;
    }

    public static DesktopSessionInfo Detect()
    {
        var hasGraphicalSession = HasEnv("WAYLAND_DISPLAY") || HasEnv("DISPLAY");
        var hasSessionBus = SessionBusSocketConnectable();
        return new DesktopSessionInfo(
            HasGraphicalSession: hasGraphicalSession,
            HasSessionBus: hasSessionBus,
            // Only worth asking the bus a question if a bus actually answered above — an
            // unreachable/absent bus can't own anything, and this skips spawning gdbus for nothing.
            NotificationDaemonPresent: hasSessionBus && NotificationsNameOwned(),
            IsInteractiveTty: IsInteractiveTtySafe(),
            RunningAsSystemdUnit: RunningAsSystemdUnit);
    }

    /// <summary>systemd sets <c>INVOCATION_ID</c> for a unit's processes and for nothing a person starts.</summary>
    public static bool RunningAsSystemdUnit => HasEnv("INVOCATION_ID");

    /// <summary>
    /// Console.IsInputRedirected can throw when stdin is in an unusual state (e.g. its file
    /// descriptor closed outright rather than redirected) — exactly the kind of process context
    /// doctor may be invoked from. Collapses to "no", matching every other probe in this class.
    /// </summary>
    private static bool IsInteractiveTtySafe()
    {
        try { return !Console.IsInputRedirected; }
        catch { return false; }
    }

    private static bool HasEnv(string name) =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));

    /// <summary>
    /// <c>$DBUS_SESSION_BUS_ADDRESS</c> set is not the same claim as "a bus is actually there" — a
    /// stale value from a dead session, or a variable inherited across an SSH hop into a different
    /// machine, both set the variable while nothing is listening. Only the common
    /// <c>unix:path=...</c> form (what a <c>systemd --user</c>-managed session bus — the only kind
    /// this daemon, or a modern KDE/GNOME desktop session, ever sets up — actually uses) is probed;
    /// an abstract-socket or <c>tcp:</c> address degrades to "not connectable" rather than guessing
    /// at a form nothing in this project's real target environments produces.
    /// </summary>
    private static bool SessionBusSocketConnectable()
    {
        var address = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (string.IsNullOrEmpty(address)) return false;

        var path = ExtractUnixPath(address);
        if (path is null) return false;

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch { return false; }
    }

    private static string? ExtractUnixPath(string busAddress)
    {
        if (!busAddress.StartsWith("unix:", StringComparison.Ordinal)) return null;
        foreach (var part in busAddress["unix:".Length..].Split(','))
        {
            if (part.StartsWith("path=", StringComparison.Ordinal))
                return part["path=".Length..];
        }
        return null;
    }

    /// <summary>
    /// Whether <c>org.freedesktop.Notifications</c> currently has an owner, via <c>gdbus</c> (part
    /// of glib2, present alongside the session bus on every real target here — see the Phase 9 D-Bus
    /// tooling decision in plan.md for why <c>gdbus</c> was picked over <c>dbus-send</c> or a NuGet
    /// D-Bus client). <c>gdbus</c> missing, erroring, or printing anything unexpected all collapse to
    /// "can't confirm a notification daemon" — this check exists only to decide whether attempting a
    /// notification is worth it at all, so an inconclusive answer is treated the same as "no."
    /// <para>
    /// Bounded to a short timeout: a session bus can accept a connection and then never complete the
    /// D-Bus handshake (a stale or half-started bus, not merely a test contrivance), and
    /// <c>gdbus</c> would then hang waiting on it forever. This detection exists specifically so
    /// callers — <c>doctor</c> today, the launch wrapper and an optional notification later — never
    /// block on something outside this process's control, the same rule `plan.md`'s "must never
    /// hang" section already holds the wrapper to.
    /// </para>
    /// </summary>
    private static bool NotificationsNameOwned()
    {
        var (exit, stdout, _) = ProcessRunner.Run("gdbus",
            [
                "call", "--session",
                "--dest", "org.freedesktop.DBus",
                "--object-path", "/org/freedesktop/DBus",
                "--method", "org.freedesktop.DBus.NameHasOwner",
                "org.freedesktop.Notifications",
            ],
            TimeSpan.FromSeconds(2));
        // gdbus prints "(true,)" or "(false,)" to stdout on success.
        return exit == 0 && stdout.Contains("(true", StringComparison.Ordinal);
    }

    /// <summary>
    /// What an app launched from the desktop session would be started with, and this daemon usually
    /// lacks: display, X authority, and the session's identity and search paths.
    /// <c>XDG_DATA_DIRS</c> carries the flatpak export directories, which hold a flatpak browser's
    /// <c>.desktop</c> file. <c>XDG_CURRENT_DESKTOP</c>/<c>KDE_*</c> pick the desktop's own opener.
    /// </summary>
    private static readonly string[] SessionKeys =
    [
        "DISPLAY", "WAYLAND_DISPLAY", "XAUTHORITY",
        "XDG_DATA_DIRS", "XDG_CONFIG_DIRS", "XDG_MENU_PREFIX",
        "XDG_CURRENT_DESKTOP", "XDG_SESSION_DESKTOP", "XDG_SESSION_TYPE", "DESKTOP_SESSION",
        "KDE_FULL_SESSION", "KDE_SESSION_VERSION", "KDE_SESSION_UID", "KDE_APPLICATIONS_AS_SCOPE",
    ];

    /// <summary>
    /// The desktop session's environment (<see cref="SessionKeys"/>) as the <c>systemd --user</c>
    /// manager holds it right now. Never taken from this process's own environment, which is a
    /// snapshot from whenever systemd started it. Every real SaveLocker daemon here is a
    /// <c>systemd --user</c> unit pulled in by <c>default.target</c>. That target is reached before
    /// SteamOS's desktop session imports these variables into the manager, and systemd never pushes
    /// later imports into a running unit. On real hardware (2026-09-27), the test rig's daemon had
    /// none of them. The installed daemon had Game Mode's values (<c>XDG_CURRENT_DESKTOP=gamescope</c>)
    /// while the Deck sat in Desktop Mode. Sending a notification still worked, because D-Bus only
    /// needs the session bus. <c>xdg-open</c> failed in two ways:
    /// <list type="bullet">
    /// <item>With no display, it fell through to looking for a text browser.</item>
    /// <item>With a display but without <c>XDG_DATA_DIRS</c>, KDE could not see the flatpak Chrome
    /// that was set as the default browser. It opened the only other https handler: SteamOS's
    /// Firefox placeholder, whose <c>Exec</c> line is broken.</item>
    /// </list>
    /// Handing <c>xdg-open</c> the session's own values opened Chrome. Re-read on every call, not
    /// cached, because a session can start well after the daemon. Empty if <c>systemctl</c> fails;
    /// the caller then keeps what it has. Values that systemd prints in escaped <c>$'…'</c> form are
    /// skipped, since none of these keys should need escaping. <see cref="ApplySessionEnv"/> is how a
    /// child process gets them.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ResolveSessionEnv()
    {
        var result = new Dictionary<string, string>();
        var (exit, stdout, _) = ProcessRunner.Run("systemctl", ["--user", "show-environment"], TimeSpan.FromSeconds(2));
        if (exit != 0) return result;

        foreach (var line in stdout.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq];
            if (Array.IndexOf(SessionKeys, key) < 0) continue;
            var value = line[(eq + 1)..].TrimEnd('\r');
            if (value.Length > 0 && !value.StartsWith("$'", StringComparison.Ordinal)) result[key] = value;
        }
        return result;
    }

    /// <summary>The search paths among <see cref="SessionKeys"/>: they only add places to look, so a
    /// value the session does not set is harmless to keep. Every other key says which session this is.</summary>
    private static readonly string[] SearchPathKeys = ["XDG_DATA_DIRS", "XDG_CONFIG_DIRS"];

    /// <summary>
    /// Give a child process (<paramref name="environment"/> is its <c>ProcessStartInfo.Environment</c>,
    /// a copy of this process's own) the desktop session's values from <see cref="ResolveSessionEnv"/>.
    /// When the session has a display, what it says about <i>which</i> session this is is the whole
    /// truth: a display or identity key it does not set is removed rather than left at this process's
    /// value, which may be Game Mode's — a gamescope <c>WAYLAND_DISPLAY</c> left beside Desktop Mode's
    /// X11 <c>DISPLAY</c> sends a Wayland-first browser to a compositor that is not running. When it
    /// has no display (nothing imported a session into the manager, as under WSL), this process's own
    /// values are all there is, and are kept.
    /// </summary>
    public static void ApplySessionEnv(IDictionary<string, string?> environment)
    {
        var session = ResolveSessionEnv();
        var authoritative = session.ContainsKey("DISPLAY") || session.ContainsKey("WAYLAND_DISPLAY");
        foreach (var key in SessionKeys)
        {
            if (session.TryGetValue(key, out var value)) environment[key] = value;
            else if (authoritative && Array.IndexOf(SearchPathKeys, key) < 0) environment.Remove(key);
        }
    }
}
