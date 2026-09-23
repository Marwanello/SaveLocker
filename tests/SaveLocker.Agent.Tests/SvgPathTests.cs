using System.Numerics;
using SaveLocker.Agent.Linux.Ui;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The Deck's icons stroke lucide's own path strings through <see cref="SvgPath"/>. The failure this
/// guards is the quiet kind: input the parser cannot honour must throw, never draw a plausible wrong
/// shape (a dropped subpath, a curve command silently skipped, arc flags read as one number).
/// </summary>
public class SvgPathTests
{
    private const float Tol = 0.05f;

    private static SvgPath.Subpath Only(string d)
    {
        var subs = SvgPath.Flatten(d);
        Assert.Single(subs);
        return subs[0];
    }

    private static (Vector2 Min, Vector2 Max) Bounds(SvgPath.Subpath sub) =>
        (sub.Points.Aggregate(Vector2.Min), sub.Points.Aggregate(Vector2.Max));

    // ── the three real lucide 0.511.0 icons the Deck draws ─────────────────────────────────────

    [Fact]
    public void LucideCloud_IsOneClosedOutline_WithTheRightExtent()
    {
        var cloud = Only("M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z");
        Assert.True(cloud.Closed);
        // The final arc ends on the start point; a closed run must not repeat it.
        Assert.True(Vector2.Distance(cloud.Points[0], cloud.Points[^1]) > 0.5f);

        // Left lobe: centre (9,12) r=7. Right lobe: centre (17.5,14.5) r=4.5. Flat bottom at y=19.
        var (min, max) = Bounds(cloud);
        Assert.InRange(min.X, 2f - Tol, 2f + Tol);
        Assert.InRange(min.Y, 5f - Tol, 5f + Tol);
        Assert.InRange(max.X, 22f - Tol, 22f + Tol);
        Assert.InRange(max.Y, 19f - Tol, 19f + Tol);
    }

    [Fact]
    public void LucideRefreshCw_IsFourOpenSubpaths()
    {
        var subs = SvgPath.Flatten("M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8");
        Assert.Single(subs);
        Assert.False(subs[0].Closed);
        Assert.Equal(new Vector2(3, 12), subs[0].Points[0]);
        Assert.Equal(new Vector2(21, 8), subs[0].Points[^1]);

        Assert.Equal(new[] { new Vector2(21, 3), new Vector2(21, 8), new Vector2(16, 8) }, Only("M21 3v5h-5").Points);
        Assert.Equal(new[] { new Vector2(8, 16), new Vector2(3, 16), new Vector2(3, 21) }, Only("M8 16H3v5").Points);
    }

    [Fact]
    public void LucideGitBranchArc_QuarterCircleAboutNineNine()
    {
        var arc = Only("M18 9a9 9 0 0 1-9 9");
        Assert.Equal(new Vector2(18, 9), arc.Points[0]);
        Assert.Equal(new Vector2(9, 18), arc.Points[^1]);
        foreach (var p in arc.Points)
            Assert.InRange(Vector2.Distance(p, new Vector2(9, 9)), 9f - Tol, 9f + Tol);
    }

    // ── arcs ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("M0 0a5 5 0 0 1 10 0", -5f)]   // sweep=1 is clockwise on screen: bulges to negative y
    [InlineData("M0 0a5 5 0 0 0 10 0", 5f)]
    public void Arc_SweepFlagPicksTheSide(string d, float extremeY)
    {
        var arc = Only(d);
        Assert.Equal(new Vector2(10, 0), arc.Points[^1]);
        foreach (var p in arc.Points)
            Assert.InRange(Vector2.Distance(p, new Vector2(5, 0)), 5f - Tol, 5f + Tol);
        var (min, max) = Bounds(arc);
        Assert.InRange(extremeY < 0 ? min.Y : max.Y, extremeY - Tol, extremeY + Tol);
    }

    [Fact]
    public void Arc_FlagsMayAbutTheNextNumber()
    {
        // "0110 0" is flag 0, flag 1, x=10, y=0 in SVG's compact form. A number-token scanner reads
        // "0110" as one number and shifts every later argument.
        var spaced = Only("M0 0a5 5 0 0 1 10 0");
        var compact = Only("M0 0a5 5 0 0110 0");
        Assert.Equal(spaced.Points, compact.Points);
    }

    [Fact]
    public void Arc_RadiusTooSmallForTheChord_IsScaledUpNotDropped()
    {
        var arc = Only("M0 0a1 1 0 0 1 10 0");
        Assert.Equal(new Vector2(10, 0), arc.Points[^1]);
        Assert.InRange(Bounds(arc).Min.Y, -5f - Tol, -5f + Tol);
    }

    [Fact]
    public void Arc_ZeroRadiusIsAStraightLine()
    {
        Assert.Equal(new[] { new Vector2(0, 0), new Vector2(10, 0) }, Only("M0 0a0 0 0 0 1 10 0").Points);
    }

    // ── commands and structure ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SecondMoveto_StartsANewSubpath_InsteadOfDroppingTheFirst()
    {
        var subs = SvgPath.Flatten("M1 1L2 2M3 3L4 4");
        Assert.Equal(2, subs.Length);
        Assert.Equal(new Vector2(1, 1), subs[0].Points[0]);
        Assert.Equal(new Vector2(4, 4), subs[1].Points[^1]);
    }

    [Fact]
    public void RelativeAndImplicitRepeats()
    {
        Assert.Equal(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(2, 0) }, Only("M0 0 l1 0 1 0").Points);
        Assert.Equal(new[] { new Vector2(1, 1), new Vector2(3, 1), new Vector2(3, 4) }, Only("m1 1 2 0 0 3").Points);
    }

    [Fact]
    public void ClosePath_ProducesClosedRun_AndTheNextCommandContinuesFromTheStart()
    {
        var subs = SvgPath.Flatten("M0 0L2 0L2 2Zl5 0");
        Assert.Equal(2, subs.Length);
        Assert.True(subs[0].Closed);
        Assert.Equal(3, subs[0].Points.Length);
        Assert.False(subs[1].Closed);
        Assert.Equal(new[] { new Vector2(0, 0), new Vector2(5, 0) }, subs[1].Points);
    }

    [Fact]
    public void NumbersMayAbut_MinusAndSecondDecimalPoint()
    {
        Assert.Equal(new[] { new Vector2(0, 0), new Vector2(6.71f, -9f) }, Only("M0 0L6.71-9").Points);
        Assert.Equal(new[] { new Vector2(0, 0), new Vector2(1.5f, 0.5f) }, Only("M0 0L1.5.5").Points);
    }

    [Theory]
    [InlineData("M0 0c1 2 3 4 5 6")]       // cubic — a regex tokenizer skipped the letter and reused the numbers
    [InlineData("M0 0C1 2 3 4 5 6")]
    [InlineData("M0 0s1 2 3 4")]
    [InlineData("M0 0q1 2 3 4")]
    [InlineData("M0 0t1 2")]
    [InlineData("M0 0a5 6 0 0 1 10 0")]    // elliptical
    [InlineData("M0 0a5 5 30 0 1 10 0")]   // rotated
    public void UnsupportedInput_Throws_NeverDrawsAWrongShape(string d)
    {
        Assert.Throws<NotSupportedException>(() => SvgPath.Flatten(d));
    }

    [Theory]
    [InlineData("5 5")]                    // must start with a command
    [InlineData("M0 0L")]                  // missing arguments
    [InlineData("M0 0Z 5 5")]              // Z takes none, so there is nothing to repeat
    [InlineData("M0 0a5 5 0 2 1 10 0")]    // flags are 0 or 1
    public void MalformedInput_Throws(string d)
    {
        Assert.Throws<FormatException>(() => SvgPath.Flatten(d));
    }

    [Fact]
    public void Flatten_IsCachedPerString()
    {
        const string d = "M1 2L3 4";
        Assert.Same(SvgPath.Flatten(d), SvgPath.Flatten(d));
    }
}
