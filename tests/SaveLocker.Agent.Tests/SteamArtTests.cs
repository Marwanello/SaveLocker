using SaveLocker.Agent.Linux.Art;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Repainting the bundled artwork folder: only an existing folder, only when the look changed, never Steam.
/// </summary>
public sealed class SteamArtTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "savelocker-art-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static byte[] Layer(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "packaging"))) dir = dir.Parent;
        return File.ReadAllBytes(Path.Combine(dir!.FullName, "src", "Agent.Linux", "Art", "layers", name));
    }

    private SteamArt.Outcome Apply(string accent = "ember", string mark = "pixel") =>
        SteamArt.Apply(_dir, accent, mark, Layer);

    private byte[] Read(string piece) => File.ReadAllBytes(Path.Combine(_dir, piece + ".png"));

    [Fact]
    public void WithoutTheFolder_NothingIsCreated()
    {
        Assert.Equal(SteamArt.Outcome.None, Apply());
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void ReplacesTheOldPictures_InPlace()
    {
        Directory.CreateDirectory(_dir);
        foreach (var piece in SteamArtRenderer.Pieces) File.WriteAllText(Path.Combine(_dir, piece + ".png"), "old art");

        Assert.Equal(new SteamArt.Outcome(true, 4), Apply());
        foreach (var piece in SteamArtRenderer.Pieces) Assert.True(Read(piece).Length > 1000, piece);
    }

    [Fact]
    public void RepaintsOnlyWhenTheLookChanged()
    {
        Directory.CreateDirectory(_dir);
        Apply("ember", "pixel");
        var before = Read("capsule");

        Assert.Equal(0, Apply("ember", "pixel").Written);

        Assert.Equal(4, Apply("coolant", "pixel").Written);
        var after = Read("capsule");
        Assert.NotEqual(before, after);

        Assert.Equal(4, Apply("coolant", "cartridge").Written);
        Assert.NotEqual(after, Read("capsule"));
    }

    [Fact]
    public void ReinstalledFixedArt_IsPaintedOverAgain()
    {
        Directory.CreateDirectory(_dir);
        Apply("coolant", "pixel");
        File.WriteAllText(Path.Combine(_dir, "hero.png"), "orange again");   // install.sh copied the fixed art back

        Assert.Equal(1, Apply("coolant", "pixel").Written);
        Assert.True(Read("hero").Length > 1000);
    }

    [Fact]
    public void Export_WritesFourPictures_WithoutSteam()
    {
        var files = SteamArt.Export(Path.Combine(_dir, "preview"), "emerald", "memcard", Layer);
        Assert.Equal(4, files.Count);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length > 1000));
    }
}
