using SaveLocker.Agent.Linux.Art;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// Repainting the bundled artwork folder: created if missing, a piece rewritten only when it differs from a fresh render, never Steam.
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
    public void WithoutTheFolder_ItIsCreatedAndPainted()
    {
        Assert.Equal(new SteamArt.Outcome(true, 4), Apply());
        Assert.True(Directory.Exists(_dir));
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

        Assert.Equal(3, Apply("coolant", "cartridge").Written);   // the hero is background only, the same for every mark
        Assert.NotEqual(after, Read("capsule"));
    }

    [Fact]
    public void AnUnchangedLook_WritesNothing()
    {
        Directory.CreateDirectory(_dir);
        Apply();
        var pieces = SteamArtRenderer.Pieces.Select(p => Path.Combine(_dir, p + ".png")).ToArray();
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var path in pieces) File.SetLastWriteTimeUtc(path, old);

        Assert.Equal(new SteamArt.Outcome(true, 0), Apply());
        Assert.All(pieces, path => Assert.Equal(old, File.GetLastWriteTimeUtc(path)));
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
    public void ArtFromAnOlderBuild_IsReplaced_EvenWithTheSameLook()
    {
        Directory.CreateDirectory(_dir);
        Apply("coolant", "pixel");
        var hero = Path.Combine(_dir, "hero.png");
        var fresh = Read("hero");
        File.WriteAllBytes(hero, SteamArtRenderer.RenderPng("capsule-wide", SaveLocker.Agent.AppearancePalette.For("coolant").Dark, SaveLocker.Agent.AppearancePalette.For("coolant").DarkOn, "pixel", Layer));   // a hero still carrying the logo
        File.WriteAllText(Path.Combine(_dir, ".savelocker-art.json"), "{\"Stamp\":\"coolant|pixel|2\",\"Files\":{}}");

        Assert.Equal(1, Apply("coolant", "pixel").Written);
        Assert.Equal(fresh, Read("hero"));
        Assert.False(File.Exists(Path.Combine(_dir, ".savelocker-art.json")));
    }

    [Fact]
    public void Export_WritesFourPictures_WithoutSteam()
    {
        var files = SteamArt.Export(Path.Combine(_dir, "preview"), "emerald", "memcard", Layer);
        Assert.Equal(4, files.Count);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length > 1000));
    }
}
