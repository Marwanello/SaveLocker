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
}
