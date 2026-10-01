using SaveLocker.Agent.Linux.Art;
using SaveLocker.Shared;
using StbImageSharp;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The agent repaints the Steam art for the accent and mark in effect. The fixed pictures the installer bundles
/// are drawn by a different renderer (resvg, in <c>export-art.mjs</c>), so the first thing held here is that the
/// two agree for the default look; the rest is that the look actually reaches the pixels.
/// </summary>
public class SteamArtRendererTests
{
    private const int Ember = 0xE0533C, EmberInk = 0x160F0E;
    private const int Coolant = 0x35A5BD, CoolantInk = 0x08171B;

    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "packaging", "linux", "artwork")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found");
    }

    private static byte[] Layer(string name) =>
        File.ReadAllBytes(Path.Combine(Root, "src", "Agent.Linux", "Art", "layers", name));

    private static (byte[] Rgba, int W, int H) Render(string piece, int accent, int ink, string mark = "pixel") =>
        SteamArtRenderer.Render(piece, accent, ink, mark, Layer);

    private static int At((byte[] Rgba, int W, int H) img, int x, int y)
    {
        var o = (y * img.W + x) * 4;
        return img.Rgba[o] << 16 | img.Rgba[o + 1] << 8 | img.Rgba[o + 2];
    }

    [Theory]
    [InlineData("capsule")]
    [InlineData("capsule-wide")]
    [InlineData("hero")]
    [InlineData("logo")]
    public void DefaultLook_MatchesTheBundledPicture(string piece)
    {
        var mine = Render(piece, Ember, EmberInk);
        var theirs = ImageResult.FromMemory(
            File.ReadAllBytes(Path.Combine(Root, "packaging", "linux", "artwork", "dist", piece + ".png")),
            ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((theirs.Width, theirs.Height), (mine.W, mine.H));

        long sum = 0; int worst = 0, far = 0;
        for (int i = 0; i < mine.Rgba.Length; i++)
        {
            // Compare colour only where something is drawn: a fully transparent pixel's RGB is meaningless.
            if (i % 4 != 3 && theirs.Data[i / 4 * 4 + 3] == 0 && mine.Rgba[i / 4 * 4 + 3] == 0) continue;
            var d = Math.Abs(mine.Rgba[i] - theirs.Data[i]);
            sum += d; worst = Math.Max(worst, d); if (d > 12) far++;
        }
        var mean = (double)sum / mine.Rgba.Length;
        Assert.True(mean < 1.0, $"{piece}: mean channel difference {mean:F2}");
        Assert.True(far < mine.Rgba.Length / 500, $"{piece}: {far} channels differ by more than 12 (worst {worst})");
    }

    [Fact]
    public void AnotherAccent_ChangesTheWash_AndKeepsTheSize()
    {
        var ember = Render("capsule", Ember, EmberInk);
        var cool = Render("capsule", Coolant, CoolantInk);
        Assert.Equal((ember.W, ember.H), (cool.W, cool.H));
        // Near the top-left, where the wash is strongest, the tint follows the accent.
        var e = At(ember, 20, 20); var c = At(cool, 20, 20);
        Assert.True(((e >> 16) & 0xff) > (e & 0xff), "Ember wash should lean red");
        Assert.True((c & 0xff) > ((c >> 16) & 0xff), "Coolant wash should lean blue");
    }

    [Fact]
    public void TheMarkIsPaintedInTheAccent_AndItsInkInTheOnColour()
    {
        // Pixel lock in the vertical capsule sits at (64,72) size 170: its body is the big block, its keyhole ink.
        var img = Render("capsule", Coolant, CoolantInk);
        float s = 170 / 32f;
        int bodyX = (int)(64 + 8 * s), bodyY = (int)(72 + 14 * s);   // inside rect 5,12 22x16, clear of the keyhole
        int inkX = (int)(64 + 15.5f * s), inkY = (int)(72 + 17.5f * s); // the keyhole's square
        Assert.Equal(Coolant, At(img, bodyX, bodyY));
        Assert.Equal(CoolantInk, At(img, inkX, inkY));
    }

    [Fact]
    public void DifferentMarks_DrawDifferentShapes()
    {
        var pixel = Render("capsule", Ember, EmberInk, "pixel");
        var cart = Render("capsule", Ember, EmberInk, "cartridge");
        var card = Render("capsule", Ember, EmberInk, "memcard");
        Assert.NotEqual(pixel.Rgba, cart.Rgba);
        Assert.NotEqual(cart.Rgba, card.Rgba);
    }

    [Fact]
    public void TheHero_IsBackgroundOnly_TheSameForEveryMark()
    {
        // Steam lays the logo piece over the hero, so the hero carries no mark or wordmark of its own.
        Assert.Equal(Render("hero", Ember, EmberInk, "pixel").Rgba, Render("hero", Ember, EmberInk, "memcard").Rgba);
    }

    [Fact]
    public void TheLogo_StaysTransparentAroundItsArtwork()
    {
        var img = Render("logo", Coolant, CoolantInk);
        Assert.Equal(0, img.Rgba[3]);                                   // top-left corner
        Assert.Equal(0, img.Rgba[(img.W * img.H - 1) * 4 + 3]);         // bottom-right corner
    }

    [Fact]
    public void AnUnknownMark_FallsBackToThePixelLock_AndAnUnknownPieceThrows()
    {
        Assert.Equal(Render("logo", Ember, EmberInk, "pixel").Rgba, Render("logo", Ember, EmberInk, "nonsense").Rgba);
        Assert.Throws<ArgumentException>(() => Render("banner", Ember, EmberInk));
    }

    [Fact]
    public void EncodedPng_ReadsBackToTheSamePixels()
    {
        var img = Render("capsule-wide", Coolant, CoolantInk, "memcard");
        var png = SteamArtRenderer.RenderPng("capsule-wide", Coolant, CoolantInk, "memcard", Layer);
        var back = ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
        Assert.Equal((img.W, img.H), (back.Width, back.Height));
        Assert.Equal(img.Rgba, back.Data);
    }

    [Fact]
    public void EveryMarkTheAppOffers_HasALayer_AndEveryAccentRenders()
    {
        // A new mark or accent added to the app must not silently fall back to the default in Steam.
        Assert.Equal(Appearances.Marks.OrderBy(x => x), SteamArtRenderer.Marks.OrderBy(x => x));
        foreach (var piece in SteamArtRenderer.Pieces)
            foreach (var mark in SteamArtRenderer.Marks)
                Assert.NotEmpty(Layer($"{piece}.mark-{mark}.png"));
        foreach (var accent in Appearances.Accents)
        {
            var c = SaveLocker.Agent.AppearancePalette.For(accent);
            Assert.NotNull(SteamArtRenderer.RenderPng("logo", c.Dark, c.DarkOn, "pixel", Layer));
        }
    }
}
