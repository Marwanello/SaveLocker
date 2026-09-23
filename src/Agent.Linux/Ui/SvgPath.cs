using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// A tiny SVG path tessellator — just enough to stroke lucide's icons from their own <c>d</c> strings
/// (copied verbatim out of <c>agent-ui/node_modules/lucide-react</c>) instead of hand-guessing the
/// shape. Two prior hand-authored attempts each at <see cref="Icons.Cloud"/> and <see cref="Icons.Sync"/>
/// produced a visibly wrong outline; porting the real path data fixes the class of bug at the source
/// rather than eyeballing a curve a third time.
///
/// lucide's icons only ever use M/L/H/V/A/Z (plus their lowercase relative forms), and every arc this
/// UI actually needs is a true circle (rx == ry, no rotation) — so that is all this supports.
/// Anything else throws rather than silently drawing the wrong shape.
/// </summary>
static class SvgPath
{
    private static readonly Regex Token =
        new(@"[MLHVAZmlhvaz]|-?\d*\.?\d+(?:[eE]-?\d+)?", RegexOptions.Compiled);

    /// <summary>Stroke one or more lucide <c>d</c> strings (authored on lucide's 24x24 grid) into
    /// <paramref name="dl"/>, mapped into <paramref name="pos"/>/<paramref name="size"/> the same way
    /// every other glyph in <see cref="Icons"/> is.</summary>
    public static void Stroke(ImDrawListPtr dl, Vector2 pos, float size, uint col, float stroke,
        params string[] paths)
    {
        foreach (var d in paths)
            StrokeOne(dl, pos, size, col, stroke, d);
    }

    private static void StrokeOne(ImDrawListPtr dl, Vector2 pos, float size, uint col, float stroke, string d)
    {
        var tokens = Token.Matches(d);
        int i = 0;
        string Next() => tokens[i++].Value;
        float NextNum() => float.Parse(Next(), CultureInfo.InvariantCulture);
        Vector2 Map(Vector2 v) => pos + v / 24f * size;

        Vector2 cur = default, start = default;
        char cmd = ' ';
        bool open = false;

        void Begin(Vector2 p) { dl.PathClear(); dl.PathLineTo(Map(p)); open = true; cur = p; }
        void LineTo(Vector2 p) { dl.PathLineTo(Map(p)); cur = p; }
        void End() { if (open) { dl.PathStroke(col, ImDrawFlags.None, stroke); open = false; } }

        while (i < tokens.Count)
        {
            if (tokens[i].Value.Length == 1 && "MLHVAZmlhvaz".IndexOf(tokens[i].Value[0]) >= 0)
                cmd = tokens[i++].Value[0];
            // else: a bare number repeats the previous command with a fresh argument set.

            switch (cmd)
            {
                case 'M': start = new Vector2(NextNum(), NextNum()); Begin(start); cmd = 'L'; break;
                case 'm': start = cur + new Vector2(NextNum(), NextNum()); Begin(start); cmd = 'l'; break;
                case 'L': LineTo(new Vector2(NextNum(), NextNum())); break;
                case 'l': LineTo(cur + new Vector2(NextNum(), NextNum())); break;
                case 'H': LineTo(new Vector2(NextNum(), cur.Y)); break;
                case 'h': LineTo(cur + new Vector2(NextNum(), 0)); break;
                case 'V': LineTo(new Vector2(cur.X, NextNum())); break;
                case 'v': LineTo(cur + new Vector2(0, NextNum())); break;
                case 'A':
                case 'a':
                {
                    var r = NextNum();
                    NextNum();               // ry — always == rx for the arcs this parser handles
                    NextNum();               // x-axis-rotation — always 0 for the arcs this parser handles
                    var largeArc = NextNum() != 0;
                    var sweep = NextNum() != 0;
                    var end = cmd == 'a' ? cur + new Vector2(NextNum(), NextNum())
                                         : new Vector2(NextNum(), NextNum());
                    ArcTo(dl, pos, size, cur, end, r, largeArc, sweep);
                    cur = end;
                    break;
                }
                case 'Z':
                case 'z':
                    LineTo(start);
                    End();
                    break;
                default:
                    throw new NotSupportedException($"SvgPath: unsupported command '{cmd}' in \"{d}\"");
            }
        }
        End();
    }

    /// <summary>
    /// Circular-arc endpoint-to-centre parameterisation (SVG 1.1 spec appendix F.6.5), specialised to
    /// rx == ry and no rotation — true for every arc lucide's icons use. Appends the arc's points to
    /// the ImDrawList's currently open path, in the same authoring-grid-to-screen mapping as the
    /// straight segments around it, so it can sit inline in one continuous stroke.
    /// </summary>
    private static void ArcTo(ImDrawListPtr dl, Vector2 pos, float size, Vector2 p1, Vector2 p2,
        float r, bool largeArc, bool sweep)
    {
        var dist = (p2 - p1).Length();
        if (dist > 2f * r) r = dist / 2f; // degenerate input safety net, per spec

        var x1p = (p1.X - p2.X) / 2f;
        var y1p = (p1.Y - p2.Y) / 2f;
        var d2 = x1p * x1p + y1p * y1p;
        var ratio = MathF.Max(0f, (r * r - d2) / MathF.Max(d2, 1e-6f));
        var co = MathF.Sqrt(ratio) * (largeArc == sweep ? -1f : 1f);
        var cxp = co * y1p;
        var cyp = co * -x1p;
        var centre = new Vector2(cxp + (p1.X + p2.X) / 2f, cyp + (p1.Y + p2.Y) / 2f);

        var a1 = MathF.Atan2((y1p - cyp) / r, (x1p - cxp) / r);
        var a2 = MathF.Atan2((-y1p - cyp) / r, (-x1p - cxp) / r);
        var delta = a2 - a1;
        if (!sweep && delta > 0) delta -= MathF.Tau;
        if (sweep && delta < 0) delta += MathF.Tau;

        const int segments = 24;
        for (int k = 1; k <= segments; k++)
        {
            var a = a1 + delta * k / segments;
            var pt = centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            dl.PathLineTo(pos + pt / 24f * size);
        }
    }
}
