using SaveLocker.Agent.Linux;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The application-menu entry for an agent that updated itself (install.sh, which writes it on a fresh
/// install, never runs for those). Offered once: it must appear for an install that lacks it, and must
/// not come back after someone deletes it or replace one that is already there.
/// </summary>
public sealed class DesktopEntryTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "savelocker-desktop-" + Guid.NewGuid().ToString("N"));
    private readonly string _apps;
    private readonly string _state;
    private const string Prefix = "/home/deck/.local/share/SaveLocker";

    public DesktopEntryTests()
    {
        _apps = Path.Combine(_dir, "applications");
        _state = Path.Combine(_dir, "state");
        Directory.CreateDirectory(_state);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Entry => Path.Combine(_apps, DesktopEntry.FileName);

    [Fact]
    public void TheEntry_RunsTheInstalledBinarysOpen_AndLeavesNoPlaceholder()
    {
        var text = DesktopEntry.Render(Prefix);
        Assert.Contains($"Exec={Prefix}/savelocker open\n", text);
        Assert.Contains($"Icon={Prefix}/artwork/icon.png\n", text);
        Assert.StartsWith("[Desktop Entry]\n", text);
        Assert.DoesNotContain("@PREFIX@", text);
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void AnInstallWithoutTheEntry_GetsIt_Once()
    {
        Assert.True(DesktopEntry.OfferOnce(Prefix, _apps, _state, _ => { }));
        Assert.Equal(DesktopEntry.Render(Prefix), File.ReadAllText(Entry));

        // Deleted by its owner: the next start of the daemon must not put it back.
        File.Delete(Entry);
        Assert.False(DesktopEntry.OfferOnce(Prefix, _apps, _state, _ => { }));
        Assert.False(File.Exists(Entry));
    }

    [Fact]
    public void AnEntryThatIsAlreadyThere_IsLeftAlone()
    {
        Directory.CreateDirectory(_apps);
        File.WriteAllText(Entry, "written by install.sh");

        Assert.False(DesktopEntry.OfferOnce(Prefix, _apps, _state, _ => { }));
        Assert.Equal("written by install.sh", File.ReadAllText(Entry));
        Assert.True(File.Exists(Path.Combine(_state, DesktopEntry.MarkerName)));
    }
}
