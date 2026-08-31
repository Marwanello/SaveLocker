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
            RunningAsSystemdUnit: HasEnv("INVOCATION_ID"));
    }

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
}
