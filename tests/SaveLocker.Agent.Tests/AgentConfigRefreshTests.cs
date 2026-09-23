using SaveLocker.Shared;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// <see cref="AgentConfig.RefreshFromDisk"/> is how the Deck UI — a separate, long-lived process that
/// loads config.json once — learns what the daemon has since saved: the games it reconciled and the
/// look the console pushed. Each test loads the same file twice, "daemon" writing and "deck" being the
/// UI process that read it at startup.
/// </summary>
public sealed class AgentConfigRefreshTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "savelocker-refresh-" + Guid.NewGuid().ToString("N"));

    private static readonly AppearanceDto Coolant = new("dark", "coolant", "cartridge");

    public AgentConfigRefreshTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string ConfigFile => Path.Combine(_dir, "config.json");

    private (AgentConfig Daemon, AgentConfig Deck) TwoProcesses()
    {
        AgentConfig.Load(ConfigFile);   // creates the file
        return (AgentConfig.Load(ConfigFile), AgentConfig.Load(ConfigFile));
    }

    [Fact]
    public void AdoptsAPushedLook_AndRaisesAppearanceChangedOnce()
    {
        var (daemon, deck) = TwoProcesses();
        var raised = new List<AppearanceDto>();
        deck.AppearanceChanged += raised.Add;

        Assert.True(daemon.ApplyConsoleAppearance(Coolant));
        Assert.NotEqual(Coolant, deck.EffectiveAppearance);   // the UI process has not seen it yet

        deck.RefreshFromDisk();
        Assert.Equal(Coolant, deck.EffectiveAppearance);
        Assert.Equal(new[] { Coolant }, raised);

        deck.RefreshFromDisk();
        Assert.Single(raised);                                 // same look again: nothing to repaint
    }

    [Fact]
    public void StoresAPushedLookWhileNotFollowing_WithoutRepainting()
    {
        var (daemon, deck) = TwoProcesses();
        deck.SetAppearance(follow: false, new AppearanceDto("dark", "ember", "pixel"));
        var raised = new List<AppearanceDto>();
        deck.AppearanceChanged += raised.Add;

        daemon.ApplyConsoleAppearance(Coolant);
        deck.RefreshFromDisk();

        Assert.Empty(raised);
        Assert.Equal("ember", deck.EffectiveAppearance.Accent);

        // Turning "Follow the console" back on shows the console's CURRENT look, not a stale one.
        deck.SetAppearance(follow: true, local: null);
        Assert.Equal(Coolant, deck.EffectiveAppearance);
    }

    [Fact]
    public void AdoptsGamesAddedAndRemovedByTheDaemon()
    {
        var (daemon, deck) = TwoProcesses();
        var id = Guid.NewGuid();

        daemon.SetTracked(id, tracked: true, new TrackedGame { GameId = id, Name = "Hades" });
        Assert.Empty(deck.Games);
        deck.RefreshFromDisk();
        Assert.Equal(id, Assert.Single(deck.Games).GameId);

        daemon.SetTracked(id, tracked: false);
        deck.RefreshFromDisk();
        Assert.Empty(deck.Games);
    }

    [Fact]
    public void SkipsRatherThanWaits_WhenAnotherProcessHoldsTheConfigLock()
    {
        var (daemon, deck) = TwoProcesses();
        daemon.ApplyConsoleAppearance(Coolant);

        using (AgentStateLock.TryAcquire("config", deck.StateDir, TimeSpan.Zero))
        {
            deck.RefreshFromDisk();                            // runs on the render thread: must not block or throw
            Assert.NotEqual(Coolant, deck.EffectiveAppearance);
        }

        deck.RefreshFromDisk();
        Assert.Equal(Coolant, deck.EffectiveAppearance);
    }

    [Fact]
    public void AnUnreadableFile_ChangesNothing()
    {
        var (daemon, deck) = TwoProcesses();
        daemon.ApplyConsoleAppearance(Coolant);
        deck.RefreshFromDisk();

        File.WriteAllText(ConfigFile, "{ not json");
        deck.RefreshFromDisk();

        Assert.Equal(Coolant, deck.EffectiveAppearance);
    }
}
