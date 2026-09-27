using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The rules behind OS notifications (tasks/checkpoint-ui/implementation.md, Phase 7): which events
/// fire, that a standing one is announced once, that it comes down when its condition ends, and that
/// a button is only ever a link to a screen of the agent's own UI. All of it is platform-neutral logic
/// in Agent.Core, so none of it needs a toast, a desktop or a D-Bus to prove.
/// </summary>
public class NoticeActionTests
{
    private static readonly Guid Game = Guid.Parse("11111111-2222-3333-4444-555555555555");

    // A button is a link to a screen of the agent's own UI, and nothing else. What the agent UI does
    // with the hash is agent-ui/src/route.ts's business; this is the contract from this side.
    [Theory]
    [InlineData("overview", "http://localhost:5178/#overview")]
    [InlineData("settings", "http://localhost:5178/#settings")]
    [InlineData("conflicts:queue", "http://localhost:5178/#conflicts:queue")]
    public void A_view_becomes_a_link_to_the_agent_uis_own_page(string route, string expected)
    {
        Assert.Equal(expected, NoticeAction.View(route).ToUrl("http://localhost:5178/"));
    }

    [Fact]
    public void The_conflict_chooser_and_a_game_page_have_their_own_routes()
    {
        Assert.Equal("http://127.0.0.1:5188/#conflicts:queue", NoticeAction.Conflicts.ToUrl("http://127.0.0.1:5188"));
        Assert.Equal($"http://127.0.0.1:5188/#game:{Game:D}", NoticeAction.Game(Game).ToUrl("http://127.0.0.1:5188/"));
    }

    [Fact]
    public void A_base_url_with_or_without_its_trailing_slash_gives_the_same_link()
    {
        var a = NoticeAction.Conflicts.ToUrl("http://localhost:5178/");
        var b = NoticeAction.Conflicts.ToUrl("http://localhost:5178");

        Assert.Equal(a, b);
        Assert.DoesNotContain("//#", a);
    }

    [Fact]
    public void A_notice_with_no_action_has_no_link()
    {
        Assert.Null(NoticeAction.None.ToUrl("http://localhost:5178/"));
        Assert.Null(NoticeAction.None.ToOpenUrl("http://localhost:5178/"));
        Assert.Equal("http://localhost:5178/open?view=conflicts:queue", NoticeAction.Conflicts.ToOpenUrl("http://localhost:5178/"));
    }

    // The key is what lets /open raise the tray window; without it the link only shows the screen.
    [Fact]
    public void A_toast_link_carries_the_key_that_lets_it_raise_the_window()
    {
        Assert.Equal($"http://localhost:5178/open?view=game:{Game:D}&key=0123abcd",
            NoticeAction.Game(Game).ToOpenUrl("http://localhost:5178/", "0123abcd"));
        Assert.Null(NoticeAction.None.ToOpenUrl("http://localhost:5178/", "0123abcd"));
    }
}

public class ServerReachabilityTests
{
    // Behind nginx/Caddy/Cloudflare a server that is down still gets an answer back — the proxy's.
    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(520)]
    [InlineData(521)]
    [InlineData(522)]
    [InlineData(523)]
    [InlineData(524)]
    [InlineData(530)]
    public void A_gateway_answering_for_a_missing_server_is_unreachable(int status)
    {
        var ex = new HttpRequestException("gateway", null, (System.Net.HttpStatusCode)status);

        Assert.True(ServerReachability.IsGatewayFailure(ex.StatusCode));
        Assert.True(ServerReachability.IsUnreachable(ex));
    }

    // The server itself answering "no" is a server that is there.
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(413)]
    [InlineData(500)]
    [InlineData(501)]
    public void The_server_answering_no_is_not_unreachable(int status)
    {
        var ex = new HttpRequestException("refused", null, (System.Net.HttpStatusCode)status);

        Assert.False(ServerReachability.IsGatewayFailure(ex.StatusCode));
        Assert.False(ServerReachability.IsUnreachable(ex));
    }

    [Fact]
    public void No_answer_at_all_is_unreachable()
    {
        Assert.True(ServerReachability.IsUnreachable(new HttpRequestException("connection refused")));
        Assert.True(ServerReachability.IsUnreachable(new TaskCanceledException("timed out")));
        Assert.True(ServerReachability.IsUnreachable(
            new IOException("reset", new System.Net.Sockets.SocketException())));
        Assert.False(ServerReachability.IsUnreachable(new InvalidOperationException("a bug")));
        Assert.False(ServerReachability.IsGatewayFailure(null));
    }
}

public class OpenLinkKeyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sl-openkey-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ACL-locked to this user; best effort */ }
    }

    // What a toast link carries in place of the token: unguessable, never the token itself, and the
    // same after a restart so a toast left in the Action Center still raises the window.
    [Fact]
    public void The_link_key_is_derived_from_the_token_and_stable_across_loads()
    {
        var config = Path.Combine(_dir, "config.json");
        var first = LocalAuth.LoadOrCreate(config);
        var second = LocalAuth.LoadOrCreate(config);

        Assert.Matches("^[0-9a-f]{32}$", first.OpenLinkKey);
        Assert.Equal(first.OpenLinkKey, second.OpenLinkKey);
        Assert.DoesNotContain(first.OpenLinkKey, first.Token);
        Assert.True(first.IsValidOpenLinkKey(first.OpenLinkKey));
        Assert.False(first.IsValidOpenLinkKey(first.Token));
        Assert.False(first.IsValidOpenLinkKey(null));
        Assert.False(first.IsValidOpenLinkKey(""));
    }
}

public class NoticeCatalogTests
{
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    // These are the messages SyncEngine and GameActivity really emit, copied as they read in the
    // log. If one of them is reworded the toast copy is what should be reconsidered with it.
    [Fact]
    public void A_rejected_push_points_at_the_game_and_reads_cleanly()
    {
        var n = NoticeCatalog.ForEvent(AgentEventCodes.PushFailed, "Elden Ring", Id,
            "[Elden Ring] push rejected by the server (413): payload too large");

        Assert.NotNull(n);
        Assert.Equal("Elden Ring: push failed", n.Title);
        Assert.Equal("Push rejected by the server (413): payload too large", n.Body);
        Assert.Equal(NoticeAction.Game(Id), n.Primary);
        Assert.Equal("Open game", n.PrimaryLabel);
        Assert.Equal(AgentEventSeverity.Error, n.Severity);
        Assert.True(n.ClearedBySync);
    }

    [Fact]
    public void A_refused_pull_points_at_the_game_and_drops_the_shouting()
    {
        var n = NoticeCatalog.ForEvent(AgentEventCodes.PullBlockedRunning, "Hades", Id,
            "[Hades] REFUSED pull: the game is running (hades.exe). Restoring saves under a live game " +
            "loses them when it writes at exit. Close the game and pull again.");

        Assert.NotNull(n);
        Assert.Equal("Hades: pull refused", n.Title);
        Assert.StartsWith("The game is running (hades.exe).", n.Body);
        Assert.Equal(NoticeAction.Game(Id), n.Primary);
    }

    [Fact]
    public void A_lease_held_elsewhere_is_a_warning_about_that_game()
    {
        var n = NoticeCatalog.ForEvent(AgentEventCodes.LeaseHeldElsewhere, "Baldur's Gate 3", Id,
            "[Baldur's Gate 3] WARNING: saves are checked out by 'WIDEBOY'. " +
            "Launched without pulling — a conflict may occur on exit.");

        Assert.NotNull(n);
        Assert.Equal("Baldur's Gate 3 is checked out elsewhere", n.Title);
        Assert.StartsWith("Saves are checked out by 'WIDEBOY'.", n.Body);
        Assert.Equal(AgentEventSeverity.Warning, n.Severity);
    }

    [Fact]
    public void A_game_name_with_brackets_is_not_cut_at_its_first_one()
    {
        var n = NoticeCatalog.ForEvent(AgentEventCodes.PushFailed, "Game [Deluxe]", Id,
            "[Game [Deluxe]] push rejected by the server (500): boom");

        Assert.NotNull(n);
        Assert.Equal("Push rejected by the server (500): boom", n.Body);
    }

    [Fact]
    public void A_long_message_is_cut_to_something_a_toast_can_show()
    {
        var n = NoticeCatalog.ForEvent(AgentEventCodes.PushFailed, "G", Id,
            "[G] push rejected by the server (500): " + new string('x', 500));

        Assert.NotNull(n);
        Assert.True(n.Body.Length <= 200);
        Assert.EndsWith("…", n.Body);
    }

    // The rules say six things fire. Everything else stays in the log and the console's bell —
    // including several the engine raises constantly, which is exactly why they must not toast.
    [Theory]
    [InlineData(AgentEventCodes.SyncBusy)]
    [InlineData(AgentEventCodes.SettleTimeout)]
    [InlineData(AgentEventCodes.SaveDirMissing)]
    [InlineData(AgentEventCodes.UnsafeSavePath)]
    [InlineData(AgentEventCodes.ServerUnreachable)]   // a queued push retries; only FIVE MINUTES of it is news
    [InlineData(AgentEventCodes.Conflict)]            // the conflict poll owns conflict notices
    [InlineData(AgentEventCodes.LaunchBlocked)]
    [InlineData(AgentEventCodes.UpdateStaged)]
    [InlineData(AgentEventCodes.UpdateFailed)]
    [InlineData(AgentEventCodes.PluginUpdated)]
    [InlineData("something.new")]
    public void Events_outside_the_rules_stay_quiet(string code)
    {
        Assert.Null(NoticeCatalog.ForEvent(code, "G", Id, "[G] whatever happened"));
    }

    [Fact]
    public void The_same_game_and_condition_always_gets_the_same_key()
    {
        var a = NoticeCatalog.ForEvent(AgentEventCodes.PushFailed, "G", Id, "[G] push rejected (1)");
        var b = NoticeCatalog.ForEvent(AgentEventCodes.PushFailed, "G", Id, "[G] push rejected (2)");
        var c = NoticeCatalog.ForEvent(AgentEventCodes.PullBlocked, "G", Id, "[G] BLOCKED pull: x");

        Assert.Equal(a!.Key, b!.Key);
        Assert.NotEqual(a.Key, c!.Key);
        Assert.True(a.Key.Length <= 64, "a Windows toast tag is capped at 64 characters");
    }

    [Fact]
    public void A_conflict_says_what_to_do_and_is_not_ended_by_a_clean_sync()
    {
        var conflict = new ConflictDto(Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ConflictStatus.Open, DateTime.UtcNow, null, null, null);

        var n = NoticeCatalog.ForConflict(conflict, "Cyberpunk 2077");

        Assert.Equal("Cyberpunk 2077 needs a decision", n.Title);
        Assert.Equal("Choose a save", n.PrimaryLabel);
        Assert.Equal(NoticeAction.Conflicts, n.Primary);
        Assert.False(n.ClearedBySync);
    }

    [Fact]
    public void An_escalation_counts_the_hours_and_names_the_machine_that_is_stuck()
    {
        var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
        var e = new ConflictEscalationDto(Id, Guid.NewGuid(), "Hades", "STEAMDECK", now.AddHours(-7).AddMinutes(-10), 2);

        var n = NoticeCatalog.ForEscalation(e, now);

        Assert.Equal("Hades is still waiting on you", n.Title);
        Assert.Contains("7 hours", n.Body);
        Assert.Contains("STEAMDECK cannot sync until then.", n.Body);
        Assert.DoesNotContain("URGENT", n.Body);
    }

    [Fact]
    public void Update_copy_differs_by_what_can_be_done_about_it()
    {
        var windows = NoticeCatalog.UpdateReady("0.5.11", staged: false);
        var linux = NoticeCatalog.UpdateReady("0.5.11", staged: true);

        // Windows: a toast cannot reach into the tray, so it names the menu item instead of offering
        // a button, and carries none.
        Assert.Equal(NoticeAction.None, windows.Primary);
        Assert.Contains("Update to v0.5.11", windows.Body);
        Assert.Equal(NoticeAction.View("settings"), linux.Primary);
        Assert.Contains("next time the agent restarts", linux.Body);
        Assert.Equal(windows.Key, linux.Key);   // one version, one condition, either platform
    }
}

public class NotificationCenterTests
{
    private sealed class FakePresenter : INotificationPresenter
    {
        public List<AgentNotice> Shown { get; } = new();
        public List<string> Withdrawn { get; } = new();
        /// <summary>Non-null makes Show report "could not be shown", like a headless box.</summary>
        public string? Unavailable { get; set; }
        public bool Throws { get; set; }

        public string? Show(AgentNotice notice)
        {
            if (Throws) throw new InvalidOperationException("presenter blew up");
            if (Unavailable is not null) return Unavailable;
            Shown.Add(notice);
            return null;
        }

        public void Withdraw(string key) => Withdrawn.Add(key);
    }

    private readonly FakePresenter _presenter = new();
    private readonly List<string> _log = new();
    private DateTime _now = new(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
    private readonly NotificationCenter _center;

    private static readonly Guid GameA = Guid.NewGuid();
    private static readonly Guid GameB = Guid.NewGuid();

    public NotificationCenterTests() =>
        _center = new NotificationCenter(_presenter, () => _now, _log.Add, TimeSpan.FromMinutes(5));

    private static AgentNotice PushFailed(Guid game) =>
        NoticeCatalog.ForEvent(AgentEventCodes.PushFailed, "G", game, "[G] push rejected (500)")!;

    private static ConflictDto Conflict(Guid id, Guid game) =>
        new(id, game, Guid.NewGuid(), Guid.NewGuid(), ConflictStatus.Open, DateTime.UtcNow, null, null, null);

    [Fact]
    public void A_standing_warning_is_announced_once_not_on_every_repeat()
    {
        _center.Raise(PushFailed(GameA));
        _center.Raise(PushFailed(GameA));
        _center.Raise(PushFailed(GameA));

        Assert.Single(_presenter.Shown);
    }

    [Fact]
    public void Two_games_are_two_conditions()
    {
        _center.Raise(PushFailed(GameA));
        _center.Raise(PushFailed(GameB));

        Assert.Equal(2, _presenter.Shown.Count);
    }

    [Fact]
    public void Clearing_a_condition_takes_it_down_and_lets_it_be_announced_again()
    {
        var n = PushFailed(GameA);
        _center.Raise(n);
        _center.Clear(n.Key);
        _center.Raise(n);

        Assert.Equal(2, _presenter.Shown.Count);
        Assert.Equal([n.Key], _presenter.Withdrawn);
    }

    [Fact]
    public void Clearing_something_that_was_never_announced_withdraws_nothing()
    {
        _center.Clear("push.failed:nobody");

        Assert.Empty(_presenter.Withdrawn);
    }

    [Fact]
    public void A_clean_sync_ends_that_games_faults_and_only_that_games()
    {
        var a = PushFailed(GameA);
        var b = PushFailed(GameB);
        _center.Raise(a);
        _center.Raise(b);

        _center.ClearGame(GameA);

        Assert.Equal([a.Key], _presenter.Withdrawn);
        _center.Raise(b);                         // still standing: not announced again
        Assert.Equal(2, _presenter.Shown.Count);
    }

    [Fact]
    public void A_clean_sync_does_not_end_a_conflict_only_the_server_can_do_that()
    {
        var conflictId = Guid.NewGuid();
        _center.ObserveConflicts([Conflict(conflictId, GameA)], _ => "G");

        _center.ClearGame(GameA);
        _center.ObserveConflicts([Conflict(conflictId, GameA)], _ => "G");

        Assert.Single(_presenter.Shown);
        Assert.Empty(_presenter.Withdrawn);
    }

    [Fact]
    public void A_headless_box_is_told_once_and_asked_again_later()
    {
        _presenter.Unavailable = "no desktop notification daemon reachable";
        var n = PushFailed(GameA);

        _center.Raise(n);
        _center.Raise(n);
        _center.Raise(n);

        Assert.Empty(_presenter.Shown);
        Assert.Single(_log, l => l.Contains("no desktop notification daemon reachable"));

        // A session appears (a Deck switching to Desktop Mode): nothing was announced before, so the
        // same condition is free to be announced now rather than lost.
        _presenter.Unavailable = null;
        _center.Raise(n);
        Assert.Single(_presenter.Shown);
    }

    [Fact]
    public void A_presenter_that_throws_never_takes_the_caller_down()
    {
        _presenter.Throws = true;

        _center.Raise(PushFailed(GameA));

        Assert.Single(_log, l => l.Contains("presenter blew up"));
    }

    [Fact]
    public void A_new_conflict_is_announced_once_and_taken_down_when_it_is_resolved()
    {
        var id = Guid.NewGuid();

        _center.ObserveConflicts([Conflict(id, GameA)], _ => "Hades");
        _center.ObserveConflicts([Conflict(id, GameA)], _ => "Hades");
        Assert.Single(_presenter.Shown);
        Assert.Equal("Hades needs a decision", _presenter.Shown[0].Title);
        Assert.Single(_log, l => l == "conflict notification: 1 new open conflict.");

        // Resolved in the console, on the CLI, or from another machine.
        _center.ObserveConflicts([], _ => "Hades");
        Assert.Equal([NoticeCatalog.ConflictKey(id)], _presenter.Withdrawn);

        // A later, different conflict for the same game is news again.
        _center.ObserveConflicts([Conflict(Guid.NewGuid(), GameA)], _ => "Hades");
        Assert.Equal(2, _presenter.Shown.Count);
    }

    [Fact]
    public void Several_conflicts_at_once_each_get_their_own_notice()
    {
        _center.ObserveConflicts(
            [Conflict(Guid.NewGuid(), GameA), Conflict(Guid.NewGuid(), GameB)], _ => "G");

        Assert.Equal(2, _presenter.Shown.Count);
        Assert.Single(_log, l => l == "conflict notification: 2 new open conflicts.");
    }

    [Fact]
    public void A_conflict_that_could_not_be_shown_is_retried_but_not_logged_every_poll()
    {
        _presenter.Unavailable = "no desktop notification daemon reachable";
        var id = Guid.NewGuid();

        for (var i = 0; i < 5; i++) _center.ObserveConflicts([Conflict(id, GameA)], _ => "G");

        Assert.Empty(_presenter.Shown);
        Assert.Single(_log, l => l.StartsWith("conflict notification:"));
        Assert.Single(_log, l => l.Contains("not shown"));
    }

    [Fact]
    public void An_escalation_is_announced_once_and_goes_when_its_conflict_does()
    {
        var conflictId = Guid.NewGuid();
        var e = new ConflictEscalationDto(conflictId, GameA, "Hades", null, _now.AddHours(-6), 1);
        _center.ObserveConflicts([Conflict(conflictId, GameA)], _ => "Hades");   // this machine's conflict

        _center.RaiseEscalation(e);
        _center.RaiseEscalation(e);   // every heartbeat carries it again
        Assert.Equal(2, _presenter.Shown.Count);   // the conflict, then its escalation — once
        Assert.Equal(NoticeCatalog.EscalationKey(conflictId), _presenter.Shown[1].Key);

        _center.ObserveConflicts([], _ => "Hades");
        Assert.Contains(NoticeCatalog.EscalationKey(conflictId), _presenter.Withdrawn);
    }

    // The heartbeat's escalation list is every overdue conflict in the fleet. Announced on a machine
    // that is not a party to it, the next poll took it straight back down (its conflict is not among
    // this machine's) and its button opened a chooser with nothing in it.
    [Fact]
    public void An_escalation_for_a_conflict_this_machine_is_not_party_to_is_not_announced()
    {
        var elsewhere = Guid.NewGuid();
        _center.ObserveConflicts([], _ => "Hades");

        _center.RaiseEscalation(new ConflictEscalationDto(elsewhere, GameA, "Hades", "STEAMDECK", _now.AddHours(-7), 1));
        _center.ObserveConflicts([], _ => "Hades");

        Assert.Empty(_presenter.Shown);
        Assert.Empty(_presenter.Withdrawn);
    }

    [Fact]
    public void An_escalation_that_could_not_be_shown_is_tried_again_on_the_next_heartbeat()
    {
        var conflictId = Guid.NewGuid();
        var e = new ConflictEscalationDto(conflictId, GameA, "Hades", null, _now.AddHours(-7), 1);
        _presenter.Unavailable = "no desktop notification daemon reachable";
        _center.ObserveConflicts([Conflict(conflictId, GameA)], _ => "Hades");
        _center.RaiseEscalation(e);
        Assert.Empty(_presenter.Shown);

        // A desktop appears; the next poll and heartbeat come round.
        _presenter.Unavailable = null;
        _center.ObserveConflicts([Conflict(conflictId, GameA)], _ => "Hades");
        _center.RaiseEscalation(e);

        Assert.Contains(_presenter.Shown, n => n.Key == NoticeCatalog.EscalationKey(conflictId));
    }

    [Fact]
    public void An_unreachable_server_is_not_news_until_it_has_been_gone_for_five_minutes()
    {
        _center.ObserveServer(false);
        _now += TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59);
        _center.ObserveServer(false);
        Assert.Empty(_presenter.Shown);

        _now += TimeSpan.FromSeconds(1);
        _center.ObserveServer(false);
        _center.ObserveServer(false);   // and every poll after that is the same standing condition

        Assert.Single(_presenter.Shown);
        Assert.Equal("Can't reach the server", _presenter.Shown[0].Title);
        Assert.Contains("5 minutes", _presenter.Shown[0].Body);
    }

    [Fact]
    public void The_server_answering_takes_the_notice_down_and_restarts_the_clock()
    {
        _center.ObserveServer(false);
        _now += TimeSpan.FromMinutes(6);
        _center.ObserveServer(false);
        Assert.Single(_presenter.Shown);

        _center.ObserveServer(true);
        Assert.Equal([NoticeCatalog.ServerUnreachableKey], _presenter.Withdrawn);

        // A blip a minute later is not five minutes of anything.
        _now += TimeSpan.FromMinutes(1);
        _center.ObserveServer(false);
        _now += TimeSpan.FromMinutes(3);
        _center.ObserveServer(false);
        Assert.Single(_presenter.Shown);
    }

    [Fact]
    public void A_reachable_server_never_announces_anything()
    {
        for (var i = 0; i < 100; i++)
        {
            _now += TimeSpan.FromMinutes(1);
            _center.ObserveServer(true);
        }

        Assert.Empty(_presenter.Shown);
        Assert.Empty(_presenter.Withdrawn);
    }
}
