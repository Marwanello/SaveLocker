using System.Text.RegularExpressions;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

// OS notifications (tasks/checkpoint-ui/implementation.md, Phase 7). Everything here is
// platform-neutral: WHAT is worth interrupting someone for, in what words, and how often. HOW it
// reaches the screen is a host's business — a Windows toast (ToastPresenter, src/Agent) or a
// freedesktop notification (DesktopNotifier, src/Agent.Linux) — behind INotificationPresenter.
//
// A headless box with no desktop session has nothing to show a notification on; those events reach
// the console's bell and the audit log through HealthReporter, exactly as before (Decisions.md §2).
// Nothing in this file replaces that channel — it is a second, louder one for the few events that
// deserve it.

public enum NoticeActionKind { None, OpenView }

/// <summary>
/// What a notification's primary button does: open the agent UI at one exact screen. That is all it
/// ever is, and deliberately so. Each platform turns the <see cref="Route"/> into a link to the
/// agent's own loopback server and lets the OS open it — a freedesktop action runs <c>xdg-open</c>, a
/// Windows toast button is a plain <c>http://localhost</c> link the shell hands to the default browser,
/// whose page asks the tray to raise its own window at the route (<see cref="ToOpenUrl"/>).
/// <para>
/// A richer button ("Retry now", "Install now") needs a way back into the running agent, and on
/// Windows every way tried failed or was not worth its surface. A custom URL scheme registered by the
/// agent at runtime is not resolvable for a toast's button on Windows 11 25H2 — the shell's toast host
/// launched the schemes installed apps had registered (Discord, Steam) and answered every new one,
/// including an exact copy of Discord's key layout, with "Get an app to open this link" — and even if
/// it worked, a registered scheme is a door any web page can knock on. A link to a page that already
/// exists needs no registration, opens the screen the whole design says a click should open, and is
/// what the Linux side has always done.
/// </para>
/// </summary>
public readonly record struct NoticeAction(NoticeActionKind Kind, string? Route = null)
{
    public static NoticeAction None => default;
    public static NoticeAction View(string route) => new(NoticeActionKind.OpenView, route);
    /// <summary>Straight to the one-at-a-time chooser, the same one the status header's Sync all raises.</summary>
    public static NoticeAction Conflicts => View("conflicts:queue");
    public static NoticeAction Game(Guid id) => View($"game:{id:D}");

    /// <summary>The link a button carries: the agent UI's root plus this route as its hash
    /// (<c>agent-ui/src/route.ts</c> is what reads it). Null for <see cref="None"/>.</summary>
    public string? ToUrl(string uiBaseUrl) =>
        Kind == NoticeActionKind.OpenView ? uiBaseUrl.TrimEnd('/') + "/#" + Route : null;

    /// <summary>The link a Windows toast button carries: the agent's own <c>/open</c> route, which raises
    /// the tray window at this screen instead of leaving the page to the default browser.
    /// <paramref name="key"/> is <see cref="LocalAuth.OpenLinkKey"/>: without it <c>/open</c> only
    /// shows the screen in the browser and never raises the window, so a web page cannot do that.
    /// Routes and the key are ASCII letters, digits, <c>:</c> and <c>-</c> (<c>/open</c> refuses anything
    /// else), so nothing here needs escaping.</summary>
    public string? ToOpenUrl(string uiBaseUrl, string? key = null) =>
        Kind == NoticeActionKind.OpenView
            ? uiBaseUrl.TrimEnd('/') + "/open?view=" + Route + (key is null ? "" : "&key=" + key)
            : null;
}

/// <summary>
/// One thing worth interrupting someone for.
/// </summary>
/// <param name="Key">Identity: the same key is the same condition. A key already on screen is never
/// announced again until <see cref="NotificationCenter.Clear"/> says the condition ended.</param>
/// <param name="ClearedBySync">Whether a clean sync of <paramref name="GameId"/> ends this condition.
/// True for the engine's own faults (a failed push stops being true once a push works); false for a
/// conflict, which ends only when the server says it is resolved, and for an update.</param>
public sealed record AgentNotice(
    string Key,
    Guid? GameId,
    string Title,
    string Body,
    AgentEventSeverity Severity,
    NoticeAction Primary,
    string PrimaryLabel = "",
    string SecondaryLabel = "Later",
    bool ClearedBySync = true);

/// <summary>Delivery, per host. Implementations must not block and must not throw.</summary>
public interface INotificationPresenter
{
    /// <summary>Show it. Null when it was shown; otherwise a short reason it could not be — no
    /// desktop session, no notification daemon — which the center logs once and, because nothing was
    /// announced, lets a later attempt try again.</summary>
    string? Show(AgentNotice notice);

    /// <summary>Take a notification off the screen (and out of any history) because the condition it
    /// announced has ended, so its button no longer offers something that would do nothing.</summary>
    void Withdraw(string key);
}

/// <summary>
/// Which events fire, and what they say. The copy follows the plan's voice — plain, specific, cause
/// first and fix second, no exclamation marks — and the rules follow its "Rules" table:
/// conflict opened, lease held elsewhere, push failed for good, pull refused, update ready, server
/// gone for more than five minutes. Everything else returns null and stays where it always was: the
/// log and the console.
/// </summary>
public static class NoticeCatalog
{
    // The engine words its log lines for a reader of logs — "[Game] BLOCKED pull: …". A toast has the
    // game in its title already and reads better without the shouting. These are the exact verbs
    // SyncEngine and GameActivity emit; a message that carries none of them is used as it stands.
    private static readonly Regex LogVerb = new(
        @"^(WARNING|CONFLICT|BLOCKED pull|BLOCKED launch|REFUSED pull):\s*",
        RegexOptions.CultureInvariant);

    private const int MaxBody = 200;

    /// <summary>The notice for a coded engine event, or null when that event is not worth one.</summary>
    public static AgentNotice? ForEvent(string code, string gameName, Guid gameId, string message)
    {
        var body = Tidy(message, gameName);
        return code switch
        {
            AgentEventCodes.PushFailed => new AgentNotice(
                $"{code}:{gameId:D}", gameId, $"{gameName}: push failed", body,
                AgentEventSeverity.Error, NoticeAction.Game(gameId), "Open game", "Dismiss"),

            AgentEventCodes.PullBlocked or AgentEventCodes.PullBlockedRunning => new AgentNotice(
                $"{code}:{gameId:D}", gameId, $"{gameName}: pull refused", body,
                AgentEventSeverity.Error, NoticeAction.Game(gameId), "Open game", "Dismiss"),

            AgentEventCodes.LeaseHeldElsewhere => new AgentNotice(
                $"{code}:{gameId:D}", gameId, $"{gameName} is checked out elsewhere", body,
                AgentEventSeverity.Warning, NoticeAction.Game(gameId), "Open game", "Dismiss"),

            _ => null,
        };
    }

    /// <summary>A conflict this machine is a party to has opened.</summary>
    public static AgentNotice ForConflict(ConflictDto conflict, string gameName) => new(
        ConflictKey(conflict.Id), conflict.GameId,
        $"{gameName} needs a decision",
        "Both copies changed since the last sync. Nothing syncs for this game until you keep one.",
        AgentEventSeverity.Error, NoticeAction.Conflicts, "Choose a save", "Later",
        ClearedBySync: false);

    /// <summary>The server's own "this has been open too long" signal — a second, deliberate nudge for
    /// someone who let the first one go.</summary>
    public static AgentNotice ForEscalation(ConflictEscalationDto escalation, DateTime utcNow)
    {
        var hours = Math.Max(1, (int)(utcNow - escalation.CreatedAt).TotalHours);
        var stuck = string.IsNullOrWhiteSpace(escalation.StuckMachineName)
            ? ""
            : $" {escalation.StuckMachineName} cannot sync until then.";
        return new AgentNotice(
            EscalationKey(escalation.ConflictId), escalation.GameId,
            $"{escalation.GameName} is still waiting on you",
            $"Unresolved for {hours} hour{(hours == 1 ? "" : "s")}.{stuck} Keep one copy to unblock it.",
            AgentEventSeverity.Error, NoticeAction.Conflicts, "Choose a save", "Later",
            ClearedBySync: false);
    }

    public static AgentNotice ServerUnreachable(TimeSpan down)
    {
        var minutes = Math.Max(1, (int)down.TotalMinutes);
        return new AgentNotice(
            ServerUnreachableKey, null,
            "Can't reach the server",
            $"Offline for {minutes} minute{(minutes == 1 ? "" : "s")}. Saves stay on this device and " +
            "are pushed as soon as the server answers.",
            AgentEventSeverity.Warning, NoticeAction.View("overview"), "Open SaveLocker", "Dismiss",
            ClearedBySync: false);
    }

    /// <summary>A newer agent is here. <paramref name="staged"/> is the Linux shape — already
    /// downloaded and verified, installing on the next start — against Windows, where it is on offer
    /// and installing is the tray menu's "Update to …" item. There is no button for that: a toast
    /// cannot reach into the tray (see <see cref="NoticeAction"/>), so it says where to go instead.</summary>
    public static AgentNotice UpdateReady(string version, bool staged) => staged
        ? new AgentNotice(
            $"update:{version}", null, $"SaveLocker {version} is ready",
            "Downloaded and verified. It installs the next time the agent restarts.",
            AgentEventSeverity.Info, NoticeAction.View("settings"), "Open SaveLocker", "Later",
            ClearedBySync: false)
        : new AgentNotice(
            $"update:{version}", null, $"SaveLocker {version} is available",
            $"Choose \"Update to v{version}\" in the tray menu to install it. The agent restarts when it finishes.",
            AgentEventSeverity.Info, NoticeAction.None, "", "",
            ClearedBySync: false);

    /// <summary>A plain, action-less notice — what a tray menu item answers with.</summary>
    public static AgentNotice Message(string body) => new(
        $"message:{Guid.NewGuid():N}", null, "SaveLocker", body, AgentEventSeverity.Info,
        NoticeAction.None, "", "", ClearedBySync: false);

    public const string ServerUnreachableKey = "server.unreachable";
    public const string ConflictKeyPrefix = "conflict:";
    public const string EscalationKeyPrefix = "conflict.escalated:";
    public static string ConflictKey(Guid id) => ConflictKeyPrefix + id.ToString("D");
    public static string EscalationKey(Guid id) => EscalationKeyPrefix + id.ToString("D");

    private static string Tidy(string message, string gameName)
    {
        var original = message.Trim();
        var text = original;
        // Stripped by the game's own name rather than a pattern: a title like "Game [Deluxe]" would
        // otherwise be cut at its first bracket.
        var prefix = $"[{gameName}]";
        if (text.StartsWith(prefix, StringComparison.Ordinal)) text = text[prefix.Length..].TrimStart();
        text = LogVerb.Replace(text, "");
        if (text.Length == 0) return original;
        text = char.ToUpperInvariant(text[0]) + text[1..];
        return text.Length <= MaxBody ? text : text[..(MaxBody - 1)].TrimEnd() + "…";
    }
}

/// <summary>
/// The rules, in one place: a notification is announced <b>once</b> per standing condition, taken
/// down when the condition ends, and never repeated because something polled again. It owns no
/// screen — that is the <see cref="INotificationPresenter"/>'s — and it owns no clock of its own
/// beyond the one rule that needs time ("server unreachable past five minutes").
/// <para>
/// State is in memory and per process, like everything else about notifications: a restart
/// announces whatever is still true, once, which is what someone who was away would want.
/// </para>
/// </summary>
public sealed class NotificationCenter
{
    private sealed record Standing(Guid? GameId, bool ClearedBySync);

    private readonly INotificationPresenter _presenter;
    private readonly Func<DateTime> _utcNow;
    private readonly Action<string> _log;
    private readonly TimeSpan _unreachableAfter;
    private readonly object _lock = new();
    private readonly Dictionary<string, Standing> _standing = new();
    // Keys whose "could not be shown" has already been logged, so a poll that retries every twenty
    // seconds does not write the same line every twenty seconds.
    private readonly HashSet<string> _undeliveredLogged = new();
    // The open conflicts this machine is a party to, as the last ObserveConflicts saw them. The
    // server's escalations cover the whole fleet; only these are this machine's to announce.
    private HashSet<Guid> _ownConflicts = new();
    private DateTime? _downSince;

    /// <param name="unreachableAfter">How long the server must be gone before it is worth saying so.
    /// Five minutes; <c>SAVELOCKER_UNREACHABLE_NOTICE_SECONDS</c> shortens it so a test does not have
    /// to wait that long — test-only and unadvertised, like the other two timing overrides.</param>
    public NotificationCenter(
        INotificationPresenter presenter,
        Func<DateTime>? utcNow = null,
        Action<string>? log = null,
        TimeSpan? unreachableAfter = null)
    {
        _presenter = presenter;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _log = log ?? AgentLogger.Log;
        _unreachableAfter = unreachableAfter ?? ResolveUnreachableAfter();
    }

    private static TimeSpan ResolveUnreachableAfter() =>
        int.TryParse(Environment.GetEnvironmentVariable("SAVELOCKER_UNREACHABLE_NOTICE_SECONDS"), out var s) && s > 0
            ? TimeSpan.FromSeconds(Math.Clamp(s, 1, 3600))
            : TimeSpan.FromMinutes(5);

    /// <summary>Announce it, unless that condition is already on screen.</summary>
    public void Raise(AgentNotice notice)
    {
        lock (_lock)
        {
            if (!_standing.TryAdd(notice.Key, new Standing(notice.GameId, notice.ClearedBySync))) return;
        }

        string? reason;
        try { reason = _presenter.Show(notice); }
        catch (Exception ex) { reason = ex.Message; }
        if (reason is null) return;

        // Nothing was announced, so the condition is not "standing" — the next attempt (the next
        // poll, say) must be free to try again, for instance once a desktop session appears.
        bool first;
        lock (_lock)
        {
            _standing.Remove(notice.Key);
            first = _undeliveredLogged.Add(notice.Key);
        }
        if (first) _log($"notification '{notice.Title}' not shown: {reason}");
    }

    /// <summary>The condition behind <paramref name="key"/> ended: take it down, and let it be
    /// announced again if it comes back.</summary>
    public void Clear(string key)
    {
        bool wasStanding;
        lock (_lock)
        {
            wasStanding = _standing.Remove(key);
            _undeliveredLogged.Remove(key);
        }
        if (wasStanding) Withdraw(key);
    }

    /// <summary>A game synced cleanly, so the faults the engine raised for it are over. A conflict is
    /// deliberately not among them — it ends when the server says so, in <see cref="ObserveConflicts"/>.</summary>
    public void ClearGame(Guid gameId)
    {
        List<string> keys;
        lock (_lock)
        {
            keys = _standing.Where(kv => kv.Value.ClearedBySync && kv.Value.GameId == gameId)
                .Select(kv => kv.Key).ToList();
            foreach (var key in keys) _standing.Remove(key);
            _undeliveredLogged.RemoveWhere(k => keys.Contains(k));
        }
        foreach (var key in keys) Withdraw(key);
    }

    /// <summary>
    /// The conflicts this machine is a party to that are open right now, from the server. New ones
    /// are announced, and any whose conflict has closed — resolved here, in the console, on the CLI,
    /// or from another machine — are taken down. This is the single source of conflict
    /// notifications, so a conflict found by this machine's own push and one found by the poll are
    /// one condition with one key, never two toasts.
    /// </summary>
    public void ObserveConflicts(IReadOnlyList<ConflictDto> open, Func<Guid, string> gameName)
    {
        var openIds = open.Select(c => c.Id).ToHashSet();

        List<string> closed;
        List<ConflictDto> fresh;
        int unannounced;
        lock (_lock)
        {
            _ownConflicts = openIds;
            closed = _standing.Keys.Where(k => IsConflictKey(k, out var id) && !openIds.Contains(id)).ToList();
            foreach (var key in closed) _standing.Remove(key);
            _undeliveredLogged.RemoveWhere(k => IsConflictKey(k, out var id) && !openIds.Contains(id));
            fresh = open.Where(c => !_standing.ContainsKey(NoticeCatalog.ConflictKey(c.Id))).ToList();
            // Counted before Raise, so a conflict that could not be shown last poll — and was logged
            // as such then — is not announced in the log a second time.
            unannounced = fresh.Count(c => !_undeliveredLogged.Contains(NoticeCatalog.ConflictKey(c.Id)));
        }
        foreach (var key in closed) Withdraw(key);

        if (unannounced > 0)
            _log($"conflict notification: {unannounced} new open conflict{(unannounced == 1 ? "" : "s")}.");

        foreach (var c in fresh) Raise(NoticeCatalog.ForConflict(c, gameName(c.GameId)));
    }

    /// <summary>
    /// The server's own escalation ("open too long") — announced once per conflict, and only for a
    /// conflict this machine is a party to. The heartbeat's list is every overdue conflict in the
    /// fleet: announced anywhere else, the next <see cref="ObserveConflicts"/> would take it straight
    /// back down (its conflict is not among this machine's), and its button would open a chooser with
    /// nothing in it. Meant to be called with every heartbeat's list, not only new entries — the key
    /// keeps a standing one from repeating, and one that could not be shown is tried again.
    /// </summary>
    public void RaiseEscalation(ConflictEscalationDto escalation)
    {
        bool ours;
        lock (_lock) ours = _ownConflicts.Contains(escalation.ConflictId);
        if (ours) Raise(NoticeCatalog.ForEscalation(escalation, _utcNow()));
    }

    /// <summary>
    /// Called on every poll with whether the server answered. One dropped push is not news — the
    /// queue retries it — but a server that has been gone for five minutes is, and it stops being
    /// news the moment it answers.
    /// </summary>
    public void ObserveServer(bool reachable)
    {
        TimeSpan down;
        lock (_lock)
        {
            if (reachable) _downSince = null;
            else _downSince ??= _utcNow();
            down = _downSince is { } since ? _utcNow() - since : TimeSpan.Zero;
        }

        if (reachable) Clear(NoticeCatalog.ServerUnreachableKey);
        else if (down >= _unreachableAfter) Raise(NoticeCatalog.ServerUnreachable(down));
    }

    private void Withdraw(string key)
    {
        try { _presenter.Withdraw(key); }
        catch (Exception ex) { _log($"notification withdraw '{key}': {ex.Message}"); }
    }

    private static bool IsConflictKey(string key, out Guid id)
    {
        id = default;
        var tail = key.StartsWith(NoticeCatalog.EscalationKeyPrefix, StringComparison.Ordinal)
            ? key[NoticeCatalog.EscalationKeyPrefix.Length..]
            : key.StartsWith(NoticeCatalog.ConflictKeyPrefix, StringComparison.Ordinal)
                ? key[NoticeCatalog.ConflictKeyPrefix.Length..]
                : null;
        return tail is not null && Guid.TryParse(tail, out id);
    }
}
