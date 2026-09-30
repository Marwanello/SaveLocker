using SaveLocker.Agent.Linux.Art;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Painting SaveLocker's own Steam shortcut: which shortcut, which files, when they are rewritten, and — the one
/// that matters to a person — that art they chose themselves is never overwritten.
/// </summary>
public sealed class SteamArtTests : IDisposable
{
    private const int SignedId = -1234567890;
    private static readonly uint Id = unchecked((uint)SignedId);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "savelocker-art-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static byte[] Layer(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "packaging"))) dir = dir.Parent;
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "src", "Agent.Linux", "Art", "layers", name));
    }

    private string Grid => Path.Combine(_root, "userdata", "10001", "config", "grid");

    private void Shortcut(string name, string exe)
    {
        var cfg = Path.Combine(_root, "userdata", "10001", "config");
        Directory.CreateDirectory(cfg);
        File.WriteAllBytes(Path.Combine(cfg, "shortcuts.vdf"), SteamArt.BuildShortcutsVdf(name, exe, SignedId));
    }

    private SteamArt.Outcome Apply(string accent = "ember", string mark = "pixel") =>
        SteamArt.Apply([_root], accent, mark, Layer);

    [Theory]
    [InlineData("SaveLocker", "\"/opt/x/savelocker\" ui")]
    [InlineData("savelocker", "/home/deck/notes")]
    [InlineData("Whatever I Named It", "\"/home/deck/.local/share/SaveLocker/savelocker\"")]
    [InlineData("Whatever I Named It", "/home/deck/SaveLocker/savelocker")]
    public void FindsTheShortcut_ByNameOrByProgram(string name, string exe)
    {
        Shortcut(name, exe);
        var target = Assert.Single(SteamArt.FindTargets([_root]));
        Assert.Equal(Id, target.AppId);
        Assert.Equal(Grid, target.GridDir);
    }

    [Fact]
    public void IgnoresOtherShortcuts_AndWritesNothingWithoutOne()
    {
        Shortcut("Hades", "\"/games/hades\"");
        Assert.Empty(SteamArt.FindTargets([_root]));
        Assert.Equal(SteamArt.Outcome.None, Apply());
        Assert.False(Directory.Exists(Grid));
    }

    [Fact]
    public void WritesTheFourPieces_UnderSteamsOwnNames()
    {
        Shortcut("SaveLocker", "\"/opt/x/savelocker\" ui");
        var outcome = Apply();

        Assert.Equal(new SteamArt.Outcome(1, 4, 0), outcome);
        foreach (var file in new[] { $"{Id}p.png", $"{Id}.png", $"{Id}_hero.png", $"{Id}_logo.png" })
            Assert.True(File.Exists(Path.Combine(Grid, file)), file);
    }

    [Fact]
    public void RepaintsOnlyWhenTheLookChanged()
    {
        Shortcut("SaveLocker", "\"/opt/x/savelocker\" ui");
        Apply("ember", "pixel");
        var before = File.ReadAllBytes(Path.Combine(Grid, $"{Id}p.png"));

        Assert.Equal(0, Apply("ember", "pixel").Written);

        Assert.Equal(4, Apply("coolant", "pixel").Written);
        var after = File.ReadAllBytes(Path.Combine(Grid, $"{Id}p.png"));
        Assert.NotEqual(before, after);

        Assert.Equal(4, Apply("coolant", "cartridge").Written);
        Assert.NotEqual(after, File.ReadAllBytes(Path.Combine(Grid, $"{Id}p.png")));
    }

    [Fact]
    public void ArtThePersonChose_IsNeverOverwritten()
    {
        Shortcut("SaveLocker", "\"/opt/x/savelocker\" ui");
        Directory.CreateDirectory(Grid);
        var mine = Path.Combine(Grid, $"{Id}p.png");
        File.WriteAllBytes(mine, [1, 2, 3]);                       // set by hand before we ever ran

        var first = Apply();
        Assert.Equal(new SteamArt.Outcome(1, 3, 1), first);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(mine));

        // And one they set after we had painted it.
        var hero = Path.Combine(Grid, $"{Id}_hero.png");
        File.WriteAllBytes(hero, [9, 9]);
        var second = Apply("arcade", "memcard");
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(hero));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(mine));
        Assert.Equal(2, second.LeftAlone);
        Assert.Equal(2, second.Written);
    }

    [Fact]
    public void Export_WritesFourPictures_WithoutSteam()
    {
        var files = SteamArt.Export(Path.Combine(_root, "preview"), "emerald", "memcard", Layer);
        Assert.Equal(4, files.Count);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length > 1000));
    }
}
