using SaveLocker.Agent.Linux;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// `savelocker open` and a notification's button both go through <see cref="AppWindowPlanner"/>. What is
/// guarded is the choice — which program, in what form — because a wrong one degrades quietly: a Flatpak
/// run without its `run` verb, or a plain tab where an app window was available, still "opens something".
/// </summary>
public class AppWindowPlannerTests
{
    private const string Url = "http://localhost:5178/#conflicts";

    private static AppWindowPlan Choose(string[]? binaries = null, string[]? flatpaks = null, string url = Url) =>
        AppWindowPlanner.Choose(url, b => binaries?.Contains(b) == true, f => flatpaks?.Contains(f) == true);

    [Fact]
    public void ChromiumOnPath_OpensAnAppWindow_AtTheRoute()
    {
        var plan = Choose(binaries: ["chromium"]);
        Assert.Equal("chromium", plan.FileName);
        Assert.Equal(["--app=http://localhost:5178/?app#conflicts"], plan.Args);
        Assert.True(plan.IsAppWindow);
    }

    [Fact]
    public void AFlatpakBrowser_IsRunThroughFlatpak_NotStartedByItsId()
    {
        var plan = Choose(flatpaks: ["com.google.Chrome"]);
        Assert.Equal("flatpak", plan.FileName);
        Assert.Equal(["run", "com.google.Chrome", "--app=http://localhost:5178/?app#conflicts"], plan.Args);
        Assert.True(plan.IsAppWindow);
    }

    [Fact]
    public void ABinaryOnPath_BeatsAFlatpak()
    {
        var plan = Choose(binaries: ["brave-browser"], flatpaks: ["com.google.Chrome"]);
        Assert.Equal("brave-browser", plan.FileName);
    }

    [Fact]
    public void NoChromiumFamilyBrowser_FallsBackToTheDefaultBrowser_WithThePlainUrl()
    {
        var plan = Choose();
        Assert.Equal("xdg-open", plan.FileName);
        // No ?app: in an ordinary tab the browser draws its own chrome, so the page must not draw a second one.
        Assert.Equal([Url], plan.Args);
        Assert.False(plan.IsAppWindow);
    }

    [Theory]
    [InlineData("http://localhost:5178/", "http://localhost:5178/?app")]
    [InlineData("http://localhost:5178/#game:abc", "http://localhost:5178/?app#game:abc")]
    [InlineData("http://localhost:5178/?x=1#a", "http://localhost:5178/?x=1&app#a")]
    [InlineData("http://localhost:5178/?app#a", "http://localhost:5178/?app#a")]
    public void TheAppFlag_GoesBeforeTheRoute_AndIsNotAddedTwice(string input, string expected) =>
        Assert.Equal(expected, AppWindowPlanner.WithAppFlag(input));

    // The daemon must not start the browser itself: as a child of savelocker.service it would die with
    // the unit's next restart. The command has to arrive intact behind `--`, or systemd-run would read
    // the browser's own `--app=` as one of its options.
    [Fact]
    public void FromTheDaemon_TheUserManagerStartsTheWindow_WithTheCommandIntactAfterTheSeparator()
    {
        var plan = AppWindowPlanner.ThroughUserManager(Choose(flatpaks: ["com.google.Chrome"]));
        Assert.Equal("systemd-run", plan.FileName);
        Assert.Equal("--user", plan.Args[0]);
        var separator = plan.Args.ToList().IndexOf("--");
        Assert.True(separator > 0);
        Assert.Equal(["flatpak", "run", "com.google.Chrome", "--app=http://localhost:5178/?app#conflicts"],
            plan.Args.Skip(separator + 1));
        Assert.True(plan.IsAppWindow);
    }

    // "Is there a desktop to open this on" used to ask for a session bus, which systemd --user keeps
    // alive on a headless box too — so the 409-with-the-path fallback never fired where it was meant to.
    [Theory]
    [InlineData("DISPLAY", ":0", true)]
    [InlineData("WAYLAND_DISPLAY", "wayland-0", true)]
    [InlineData("DISPLAY", "", false)]
    [InlineData("DBUS_SESSION_BUS_ADDRESS", "unix:path=/run/user/1000/bus", false)]
    public void OnlyADisplay_CountsAsADesktop_NotASessionBus(string key, string value, bool expected) =>
        Assert.Equal(expected, AppWindowPlanner.HasDisplay(new Dictionary<string, string?> { [key] = value, ["HOME"] = "/home/deck" }));
}
