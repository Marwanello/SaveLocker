using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// Flattens an SVG path <c>d</c> string into polylines, so <see cref="Icons"/> can stroke lucide's own
/// path data instead of a hand-guessed copy of the shape. Pure — no ImGui here, so it can be unit-tested
/// and the result cached per string (the strokes are then just a few <c>PathLineTo</c> calls a frame).
///
/// Supports every path command — M/L/H/V/C/S/Q/T/A/Z and their lowercase relative forms, elliptical and rotated
/// arcs too: lucide's icons use M/L/H/V/C/A/Z, and the emulator icons (Arcticons, tasks/emulator-saves Phase 21) smooth
/// curves and elliptical arcs. An unknown command throws <see cref="NotSupportedException"/>: an icon this cannot draw
/// faithfully must fail loudly the first time it is drawn, not render as a plausible wrong shape.
/// </summary>
static class SvgPath
{
    /// <summary>One connected run of points. <see cref="Closed"/> means the last point joins back to
    /// the first, and the first point is not repeated at the end.</summary>
    public readonly record struct Subpath(Vector2[] Points, bool Closed);

    private static readonly ConcurrentDictionary<string, Subpath[]> Cache = new();

    /// <summary>Points are on the path's own coordinate grid (lucide: 24x24), unscaled.</summary>
    public static Subpath[] Flatten(string d) => Cache.GetOrAdd(d, Parse);

    private static Subpath[] Parse(string d)
    {
        var r = new Reader(d);
        var result = new List<Subpath>();
        var pts = new List<Vector2>();
        Vector2 cur = default, start = default;
        char cmd = '\0';
        // The last curve's second control point, for S and T's reflection; null after anything else.
        Vector2? lastCubic = null, lastQuad = null;

        void Flush(bool closed)
        {
            // A closed run whose last point already sits on the first would stroke a zero-length
            // closing segment; drop the duplicate instead.
            if (closed && pts.Count > 1 && Vector2.DistanceSquared(pts[0], pts[^1]) < 1e-6f)
                pts.RemoveAt(pts.Count - 1);
            if (pts.Count > 1) result.Add(new Subpath(pts.ToArray(), closed));
            pts.Clear();
        }

        // After a Z the next drawing command continues from the subpath's start, per the SVG spec.
        void Begun() { if (pts.Count == 0) pts.Add(cur); }

        void LineTo(Vector2 p) { Begun(); pts.Add(p); cur = p; }

        void Cubic(Vector2 c1, Vector2 c2, Vector2 end)
        {
            Begun();
            var from = cur;
            // 12 steps: smooth at the 14-40 px this UI draws icons at, and the result is cached.
            const int steps = 12;
            for (int k = 1; k <= steps; k++)
            {
                var t = k / (float)steps;
                var u = 1f - t;
                pts.Add(u * u * u * from + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * end);
            }
            cur = end;
            lastCubic = c2;
        }

        void Quad(Vector2 c, Vector2 end)
        {
            Begun();
            var from = cur;
            const int steps = 10;
            for (int k = 1; k <= steps; k++)
            {
                var t = k / (float)steps;
                var u = 1f - t;
                pts.Add(u * u * from + 2f * u * t * c + t * t * end);
            }
            cur = end;
            lastQuad = c;
        }

        while (r.More())
        {
            if (r.AtCommand()) cmd = r.ReadCommand();
            else if (cmd == '\0') throw r.Fail("a command letter");
            // else: a bare number repeats the previous command with a fresh argument set.

            // S and T reflect the previous curve's control point only straight after a curve of their own kind.
            var prevCubic = lastCubic;
            var prevQuad = lastQuad;
            lastCubic = lastQuad = null;

            switch (cmd)
            {
                case 'M':
                case 'm':
                {
                    Flush(false);
                    var p = r.ReadPoint();
                    if (cmd == 'm') p += cur;
                    cur = start = p;
                    pts.Add(p);
                    // Extra coordinate pairs after a moveto are implicit lineto's.
                    cmd = cmd == 'M' ? 'L' : 'l';
                    break;
                }
                case 'L': LineTo(r.ReadPoint()); break;
                case 'l': LineTo(cur + r.ReadPoint()); break;
                case 'H': LineTo(new Vector2(r.ReadNumber(), cur.Y)); break;
                case 'h': LineTo(new Vector2(cur.X + r.ReadNumber(), cur.Y)); break;
                case 'V': LineTo(new Vector2(cur.X, r.ReadNumber())); break;
                case 'v': LineTo(new Vector2(cur.X, cur.Y + r.ReadNumber())); break;
                case 'A':
                case 'a':
                {
                    var rx = r.ReadNumber();
                    var ry = r.ReadNumber();
                    var rotation = r.ReadNumber();
                    var largeArc = r.ReadFlag();
                    var sweep = r.ReadFlag();
                    var end = r.ReadPoint();
                    if (cmd == 'a') end += cur;
                    Begun();
                    ArcTo(pts, cur, end, MathF.Abs(rx), MathF.Abs(ry), rotation, largeArc, sweep);
                    cur = end;
                    break;
                }
                case 'C':
                case 'c':
                {
                    // Cubic Bezier: two control points and an end, all relative to the CURRENT point for the
                    // lowercase form (not to each other). lucide's gamepad-2 body is built from these.
                    var c1 = r.ReadPoint();
                    var c2 = r.ReadPoint();
                    var end = r.ReadPoint();
                    if (cmd == 'c') { c1 += cur; c2 += cur; end += cur; }
                    Cubic(c1, c2, end);
                    break;
                }
                case 'S':
                case 's':
                {
                    // Smooth cubic: the first control point is the previous cubic's second, reflected through the
                    // current point — or the current point itself after anything else.
                    var c2 = r.ReadPoint();
                    var end = r.ReadPoint();
                    if (cmd == 's') { c2 += cur; end += cur; }
                    Cubic(prevCubic is { } p ? 2f * cur - p : cur, c2, end);
                    break;
                }
                case 'Q':
                case 'q':
                {
                    var c = r.ReadPoint();
                    var end = r.ReadPoint();
                    if (cmd == 'q') { c += cur; end += cur; }
                    Quad(c, end);
                    break;
                }
                case 'T':
                case 't':
                {
                    var end = r.ReadPoint();
                    if (cmd == 't') end += cur;
                    Quad(prevQuad is { } p ? 2f * cur - p : cur, end);
                    break;
                }
                case 'Z':
                case 'z':
                    Flush(true);
                    cur = start;
                    // Z takes no arguments, so a bare number after it has no command to repeat.
                    cmd = '\0';
                    break;
                default:
                    throw new NotSupportedException($"SvgPath: unsupported command '{cmd}' in \"{d}\"");
            }
        }
        Flush(false);
        return result.ToArray();
    }

    /// <summary>
    /// Endpoint-to-centre arc parameterisation (SVG 1.1 spec appendix F.6.5, with F.6.6's radius correction), elliptical
    /// and rotated. Appends the arc's points, ending exactly at <paramref name="p2"/>.
    /// </summary>
    private static void ArcTo(List<Vector2> pts, Vector2 p1, Vector2 p2, float rx, float ry, float rotationDeg, bool largeArc, bool sweep)
    {
        if (p1 == p2) return;                        // spec: an arc to the current point is omitted
        if (rx == 0f || ry == 0f) { pts.Add(p2); return; }   // spec: a zero radius is a straight line

        var phi = rotationDeg * MathF.PI / 180f;
        var (sin, cos) = MathF.SinCos(phi);
        var dx = (p1.X - p2.X) / 2f;
        var dy = (p1.Y - p2.Y) / 2f;
        var x1p = cos * dx + sin * dy;
        var y1p = -sin * dx + cos * dy;

        // spec: radii too small to span the chord are scaled up
        var lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (lambda > 1f) { var k = MathF.Sqrt(lambda); rx *= k; ry *= k; }

        var num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        var den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        var co = MathF.Sqrt(MathF.Max(0f, num / MathF.Max(den, 1e-9f))) * (largeArc == sweep ? -1f : 1f);
        var cxp = co * rx * y1p / ry;
        var cyp = co * -ry * x1p / rx;
        var centre = new Vector2(cos * cxp - sin * cyp + (p1.X + p2.X) / 2f, sin * cxp + cos * cyp + (p1.Y + p2.Y) / 2f);

        var a1 = MathF.Atan2((y1p - cyp) / ry, (x1p - cxp) / rx);
        var a2 = MathF.Atan2((-y1p - cyp) / ry, (-x1p - cxp) / rx);
        var delta = a2 - a1;
        if (!sweep && delta > 0) delta -= MathF.Tau;
        if (sweep && delta < 0) delta += MathF.Tau;

        // 7.5 degrees a step: smooth at the 14-40 px this UI draws icons at, and the result is cached.
        var segments = Math.Max(4, (int)MathF.Ceiling(MathF.Abs(delta) / (MathF.PI / 24f)));
        for (int k = 1; k < segments; k++)
        {
            var a = a1 + delta * k / segments;
            var (sa, ca) = MathF.SinCos(a);
            pts.Add(centre + new Vector2(cos * rx * ca - sin * ry * sa, sin * rx * ca + cos * ry * sa));
        }
        pts.Add(p2);
    }

    /// <summary>Scanner over a path string: numbers may abut each other ("6.71-9", "1.5.5") and arc
    /// flags are single characters that may abut the next number ("a2 2 0 011 1") — which a
    /// whitespace/regex tokenizer reads as one number and silently shifts every later argument.</summary>
    private sealed class Reader(string s)
    {
        private int _i;

        public bool More()
        {
            while (_i < s.Length && (char.IsWhiteSpace(s[_i]) || s[_i] == ',')) _i++;
            return _i < s.Length;
        }

        public bool AtCommand() => char.IsAsciiLetter(s[_i]);

        public char ReadCommand() => s[_i++];

        public Vector2 ReadPoint() => new(ReadNumber(), ReadNumber());

        public bool ReadFlag()
        {
            if (!More() || (s[_i] != '0' && s[_i] != '1')) throw Fail("an arc flag (0 or 1)");
            return s[_i++] == '1';
        }

        public float ReadNumber()
        {
            if (!More()) throw Fail("a number");
            var begin = _i;
            if (s[_i] is '+' or '-') _i++;

            int digits = 0;
            while (_i < s.Length && char.IsAsciiDigit(s[_i])) { _i++; digits++; }
            if (_i < s.Length && s[_i] == '.')
            {
                _i++;
                while (_i < s.Length && char.IsAsciiDigit(s[_i])) { _i++; digits++; }
            }
            if (digits == 0) { _i = begin; throw Fail("a number"); }

            if (_i < s.Length && s[_i] is 'e' or 'E')
            {
                var mark = _i++;
                if (_i < s.Length && s[_i] is '+' or '-') _i++;
                int expDigits = 0;
                while (_i < s.Length && char.IsAsciiDigit(s[_i])) { _i++; expDigits++; }
                if (expDigits == 0) _i = mark;   // not an exponent after all
            }

            return float.Parse(s.AsSpan(begin, _i - begin), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        public FormatException Fail(string expected) =>
            new($"SvgPath: expected {expected} at offset {_i} in \"{s}\"");
    }
}
