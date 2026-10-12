using SaveLocker.Agent;
using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The buttons that act (tasks/notification-follow-ups): the command an AgentNotice may carry, its
/// wire form through a toast's COM activation, the XML a toast is built with, the per-port identity
/// and CLSID split, and the pending-action file a DCOM-launched host leaves for the tray.
/// </summary>
public class AgentCommandTests
{
    private static readonly Guid Game = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void A_retry_round_trips_through_its_wire_form()
    {
        var command = AgentCommand.Retry(Game);

        Assert.Equal("Retry now", command.Label);
        Assert.Equal($"retry:{Game:D}", command.Arguments);
        Assert.True(AgentCommand.TryParse(command.Arguments, out var parsed));
        Assert.Equal(command, parsed);
    }

    [Fact]
    public void An_install_round_trips_through_its_wire_form()
    {
        var command = AgentCommand.Install("0.6.1");

        Assert.Equal("Install now", command.Label);
        Assert.Equal("install:0.6.1", command.Arguments);
        Assert.True(AgentCommand.TryParse(command.Arguments, out var parsed));
        Assert.Equal(command, parsed);
    }

    // The string arrives from a COM call anything local can make, so only the two exact shapes are
    // accepted — in particular a version is digits/dots/dashes/plus and nothing else.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("retry:")]
    [InlineData("retry:not-a-guid")]
    [InlineData("retry:11111111222233334444555555555555")]
    [InlineData("install:")]
    [InlineData("install:1.0 beta")]
    [InlineData("install:C:\\evil.exe")]
    [InlineData("install:../../x")]
    [InlineData("reboot:now")]
    public void Anything_but_the_two_exact_shapes_is_refused(string? arguments)
    {
        Assert.False(AgentCommand.TryParse(arguments, out _));
    }

    [Fact]
    public void A_version_is_confined_to_characters_a_version_can_carry()
    {
        Assert.True(AgentCommand.TryParse("install:9.9.9-ci+build.7", out var ok));
        Assert.Equal("9.9.9-ci+build.7", ok.Version);

        // 64 is the ceiling the parser allows; one past it is refused rather than truncated.
        Assert.True(AgentCommand.TryParse("install:" + new string('1', 64), out _));
        Assert.False(AgentCommand.TryParse("install:" + new string('1', 65), out _));
    }
}

public class ToastIdentityTests
{
    [Fact]
    public void The_product_and_a_rig_have_distinct_identities()
    {
        Assert.Equal("SaveLocker", ToastPresenter.AumidForPort(ToastPresenter.DefaultPort));
        Assert.Equal("SaveLocker.Test.5188", ToastPresenter.AumidForPort(5188));

        Assert.Equal(ToastPresenter.DefaultPort, ToastPresenter.PortFromAumid("SaveLocker"));
        Assert.Equal(5188, ToastPresenter.PortFromAumid("SaveLocker.Test.5188"));
        Assert.Null(ToastPresenter.PortFromAumid("SomeOtherApp"));
        Assert.Null(ToastPresenter.PortFromAumid("SaveLocker.Test."));
        Assert.Null(ToastPresenter.PortFromAumid("SaveLocker.Test.0"));
        Assert.Null(ToastPresenter.PortFromAumid(null));
    }

    // A rig's CLSID can never collide with — and so can never overwrite the registration of — the
    // installed agent's, which is what lets both live on one machine.
    [Fact]
    public void The_product_and_each_rig_have_their_own_clsid()
    {
        var product = ToastActivation.ClsidForPort(ToastPresenter.DefaultPort);
        var a = ToastActivation.ClsidForPort(5188);
        var b = ToastActivation.ClsidForPort(5189);

        Assert.Equal(ToastActivation.ProductClsid, product);
        Assert.NotEqual(product, a);
        Assert.NotEqual(a, b);
        Assert.Equal(a, ToastActivation.ClsidForPort(5188));   // deterministic, not per-process
        Assert.Matches(@"^\{[0-9A-F]{8}-([0-9A-F]{4}-){3}[0-9A-F]{12}\}$", ToastActivation.ClsidText(a));
    }
}

public class ToastXmlTests
{
    private static readonly Guid Game = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static string XmlFor(AgentNotice notice)
    {
        // The presenter writes nothing at construction; without UseLook there is no logo path and the
        // XML is exactly the buttons-and-text shape this test is about.
        var presenter = new ToastPresenter(5188, _ => { });
        return presenter.BuildXml(notice);
    }

    // The acting button must be a plain foreground action — no activationType="protocol" — because
    // that is what routes the click to the COM activator instead of a browser.
    [Fact]
    public void A_push_failure_carries_retry_that_acts_and_the_link_that_opens()
    {
        var notice = NoticeCatalog.ForEvent(
            AgentEventCodes.PushFailed, "Elden Ring", Game, "[Elden Ring] push rejected (413)")!;

        var xml = XmlFor(notice);

        Assert.Contains("content=\"Retry now\"", xml);
        Assert.Contains($"arguments=\"retry:{Game:D}\"", xml);
        Assert.DoesNotContain($"arguments=\"retry:{Game:D}\" activationType", xml);
        Assert.Contains("content=\"Open game\"", xml);
        Assert.Contains("activationType=\"protocol\"", xml);
        Assert.Contains("content=\"Dismiss\"", xml);
        Assert.Contains("arguments=\"dismiss\" activationType=\"system\"", xml);
    }

    [Fact]
    public void The_update_toast_offers_install_now_with_no_link_and_a_later_dismiss()
    {
        var notice = NoticeCatalog.UpdateReady("0.5.11", staged: false);

        var xml = XmlFor(notice);

        Assert.Contains("content=\"Install now\"", xml);
        Assert.Contains("arguments=\"install:0.5.11\"", xml);
        Assert.DoesNotContain("activationType=\"protocol\"", xml);   // no link on this notice
        Assert.Contains("content=\"Later\"", xml);
        // A button that acts is not a toast the shell should keep on screen forever: Info severity
        // keeps the ordinary timeout.
        Assert.DoesNotContain("scenario=\"reminder\"", xml);
    }

    [Fact]
    public void A_conflict_toast_has_its_link_and_no_command()
    {
        var conflict = new ConflictDto(Guid.NewGuid(), Game, Guid.NewGuid(), Guid.NewGuid(),
            ConflictStatus.Open, DateTime.UtcNow, null, null, null);

        var xml = XmlFor(NoticeCatalog.ForConflict(conflict, "Cyberpunk 2077"));

        Assert.Contains("content=\"Choose a save\"", xml);
        Assert.Contains("activationType=\"protocol\"", xml);
        Assert.DoesNotContain("retry:", xml);
        Assert.DoesNotContain("install:", xml);
    }

    [Fact]
    public void A_plain_message_has_no_actions_at_all()
    {
        var xml = XmlFor(NoticeCatalog.Message("You're up to date."));

        Assert.DoesNotContain("<actions>", xml);
    }
}

public class PendingToastActionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sl-toast-action-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_written_command_is_consumed_exactly_once()
    {
        var command = AgentCommand.Retry(Guid.NewGuid());

        PendingToastAction.Write(_dir, command);

        Assert.Equal(command, PendingToastAction.Consume(_dir, TimeSpan.FromMinutes(10)));
        Assert.Null(PendingToastAction.Consume(_dir, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Nothing_pending_consumes_as_nothing()
    {
        Assert.Null(PendingToastAction.Consume(_dir, TimeSpan.FromMinutes(10)));
    }

    // A click from before the machine was switched off must not fire the moment the agent next
    // starts — the file is deleted either way, so it is spent at most once.
    [Fact]
    public void An_old_action_is_dropped_not_run()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "toast-action.json");
        File.WriteAllText(path,
            $$"""{"Command":"retry:{{Guid.NewGuid():D}}","AtUtc":"2020-01-01T00:00:00Z"}""");

        Assert.Null(PendingToastAction.Consume(_dir, TimeSpan.FromMinutes(10)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_malformed_file_is_dropped_not_run()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "toast-action.json");
        File.WriteAllText(path, "not json at all");

        Assert.Null(PendingToastAction.Consume(_dir, TimeSpan.FromMinutes(10)));
        Assert.False(File.Exists(path));
    }
}
