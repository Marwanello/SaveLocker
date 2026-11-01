using SaveLocker.Agent;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>The decisions behind the plugin's update path that need neither Playnite nor a timer.</summary>
public sealed class PlaynitePluginTests
{
    [Fact]
    public void StoppedEdge_FiresOnceWhenARunningProcessStops()
    {
        var edge = new StoppedEdge();

        Assert.False(edge.Sample(running: true));
        Assert.False(edge.Sample(running: true));
        Assert.True(edge.Sample(running: false));
        Assert.False(edge.Sample(running: false));
    }

    [Fact]
    public void StoppedEdge_NeverSeenRunning_DoesNotFire()
    {
        var edge = new StoppedEdge();

        Assert.False(edge.Sample(running: false));
        Assert.False(edge.Sample(running: false));
    }

    [Fact]
    public void StoppedEdge_FiresAgainAfterTheNextRun()
    {
        var edge = new StoppedEdge();
        edge.Sample(running: true);
        edge.Sample(running: false);

        Assert.False(edge.Sample(running: true));
        Assert.True(edge.Sample(running: false));
    }

    [Fact]
    public void Notice_WithAutoUpdateOn_TellsThePlayerToReallyExit()
    {
        var message = PlaynitePlugin.AvailableWhileRunning("0.1.3", "0.1.2", autoUpdate: true);

        Assert.Contains("exit Playnite to install it", message);
        Assert.Contains("the X only hides it", message);
    }

    [Fact]
    public void Notice_WithAutoUpdateOff_DoesNotPromiseAnInstallOnExit()
    {
        var message = PlaynitePlugin.AvailableWhileRunning("0.1.3", "0.1.2", autoUpdate: false);

        Assert.Contains("v0.1.3 available", message);
        Assert.DoesNotContain("exit Playnite", message);
        Assert.Contains("turned off", message);
    }
}
