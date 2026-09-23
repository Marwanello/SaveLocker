using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// Flattens an SVG path <c>d</c> string into polylines, so <see cref="Icons"/> can stroke lucide's own
/// path data instead of a hand-guessed copy of the shape. Pure — no ImGui here, so it can be unit-tested
/// and the result cached per string (the strokes are then just a few <c>PathLineTo</c> calls a frame).
///
/// Supports M/L/H/V/A/Z and their lowercase relative forms, and only circular arcs (rx == ry, no
/// rotation) — which is all the lucide icons this UI draws use. Anything else throws
/// <see cref="NotSupportedException"/>: an icon this cannot draw faithfully must fail loudly the first
/// time it is drawn, not render as a plausible wrong shape.
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

        while (r.More())
        {
            if (r.AtCommand()) cmd = r.ReadCommand();
            else if (cmd == '\0') throw r.Fail("a command letter");
            // else: a bare number repeats the previous command with a fresh argument set.

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
                    if (rx != ry || rotation != 0f)
                        throw new NotSupportedException(
                            $"SvgPath: only circular, unrotated arcs are supported (in \"{d}\")");
                    Begun();
                    ArcTo(pts, cur, end, MathF.Abs(rx), largeArc, sweep);
                    cur = end;
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
    /// Circular-arc endpoint-to-centre parameterisation (SVG 1.1 spec appendix F.6.5), specialised to
    /// rx == ry and no rotation. Appends the arc's points, ending exactly at <paramref name="p2"/>.
    /// </summary>
    private static void ArcTo(List<Vector2> pts, Vector2 p1, Vector2 p2, float r, bool largeArc, bool sweep)
    {
        if (p1 == p2) return;                        // spec: an arc to the current point is omitted
        if (r == 0f) { pts.Add(p2); return; }        // spec: a zero radius is a straight line

        var dist = (p2 - p1).Length();
        if (dist > 2f * r) r = dist / 2f;            // spec: radii too small to span the chord are scaled up

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

        // 7.5 degrees a step: smooth at the 14-40 px this UI draws icons at, and the result is cached.
        var segments = Math.Max(4, (int)MathF.Ceiling(MathF.Abs(delta) / (MathF.PI / 24f)));
        for (int k = 1; k < segments; k++)
        {
            var a = a1 + delta * k / segments;
            pts.Add(centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r);
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
