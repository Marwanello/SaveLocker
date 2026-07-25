using System.Numerics;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// The icon set, drawn as vector paths rather than loaded from an atlas.
///
/// The React surfaces use <c>lucide-react</c>, whose icons are stroked paths on a 24x24 grid with a
/// consistent weight. Those same shapes are reproduced here with ImDrawList calls, which means: no
/// image assets in the tarball, no SVG rasteriser at build time, and — unlike a baked atlas — icons
/// that stay crisp at any size, because they are re-tessellated per frame at the size requested.
///
/// Everything is authored on lucide's 24x24 grid and mapped through <see cref="P"/>, so the shapes
/// can be read against the upstream SVGs. Curves are approximated with short polylines; at the sizes
/// this UI draws (14-40 px) the difference is not resolvable.
/// </summary>
static class Icons
{
    public delegate void Glyph(ImDrawListPtr dl, Vector2 pos, float size, uint col, float stroke);

    /// <summary>lucide's default stroke is 2/24; the React agent UI dials it to 1.75. Match that.</summary>
    public const float StrokeRatio = 1.75f / 24f;

    /// <summary>Map a point on the 24x24 authoring grid into screen space.</summary>
    private static Vector2 P(Vector2 pos, float size, float x, float y) =>
        new(pos.X + x / 24f * size, pos.Y + y / 24f * size);

    /// <summary>Draw <paramref name="glyph"/> as a laid-out ImGui item, so it participates in layout.</summary>
    public static void Draw(Glyph glyph, float size, Vector4 colour)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        glyph(dl, pos, size, ImGui.ColorConvertFloat4ToU32(colour), MathF.Max(1f, size * StrokeRatio));
        ImGui.Dummy(new Vector2(size, size));
    }

    /// <summary>Draw at an explicit screen position, for painting inside a custom widget.</summary>
    public static void DrawAt(ImDrawListPtr dl, Glyph glyph, Vector2 pos, float size, Vector4 colour) =>
        glyph(dl, pos, size, ImGui.ColorConvertFloat4ToU32(colour), MathF.Max(1f, size * StrokeRatio));

    private static void Poly(ImDrawListPtr dl, Vector2 pos, float size, uint col, float stroke,
        bool closed, params float[] xy)
    {
        for (int i = 0; i + 1 < xy.Length; i += 2)
            dl.PathLineTo(P(pos, size, xy[i], xy[i + 1]));
        dl.PathStroke(col, closed ? ImDrawFlags.Closed : ImDrawFlags.None, stroke);
    }

    private static void Line(ImDrawListPtr dl, Vector2 pos, float size, uint col, float stroke,
        float x1, float y1, float x2, float y2) =>
        dl.AddLine(P(pos, size, x1, y1), P(pos, size, x2, y2), col, stroke);

    private static void Rect(ImDrawListPtr dl, Vector2 pos, float size, uint col, float stroke,
        float x1, float y1, float x2, float y2, float radius) =>
        dl.AddRect(P(pos, size, x1, y1), P(pos, size, x2, y2), col, radius / 24f * size,
            ImDrawFlags.None, stroke);

    private static void Dot(ImDrawListPtr dl, Vector2 pos, float size, uint col, float x, float y, float r) =>
        dl.AddCircleFilled(P(pos, size, x, y), MathF.Max(1f, r / 24f * size), col, 12);

    // ── The set ──────────────────────────────────────────────────────────────────────────────
    // Only what the four screens actually use. Add here rather than reaching for a font.

    public static readonly Glyph Monitor = (dl, p, s, c, w) =>
    {
        Rect(dl, p, s, c, w, 2, 3, 22, 17, 2);
        Line(dl, p, s, c, w, 8, 21, 16, 21);
        Line(dl, p, s, c, w, 12, 17, 12, 21);
    };

    public static readonly Glyph Plus = (dl, p, s, c, w) =>
    {
        Line(dl, p, s, c, w, 5, 12, 19, 12);
        Line(dl, p, s, c, w, 12, 5, 12, 19);
    };

    /// <summary>
    /// A gear. lucide's is a single hand-authored path; this generates the tooth ring procedurally.
    /// The teeth must be a real closed outline — an earlier version radiated straight spokes from a
    /// circle, which reads as a sun rather than a gear once it is drawn larger than ~24 px.
    /// </summary>
    public static readonly Glyph Settings = (dl, p, s, c, w) =>
    {
        var centre = P(p, s, 12, 12);
        var rOut = 10.5f / 24f * s;
        var rIn = 7.2f / 24f * s;
        const int teeth = 7;
        var span = MathF.Tau / teeth;

        dl.PathClear();
        for (int i = 0; i < teeth; i++)
        {
            var a = i * span - MathF.PI / 2f;
            // Root, up the flank, across the tooth crown, back down: a trapezoid per tooth.
            foreach (var (offset, radius) in new[]
                     {
                         (-span * 0.30f, rIn), (-span * 0.16f, rOut),
                         (span * 0.16f, rOut), (span * 0.30f, rIn),
                     })
                dl.PathLineTo(centre + new Vector2(
                    MathF.Cos(a + offset), MathF.Sin(a + offset)) * radius);
        }
        dl.PathStroke(c, ImDrawFlags.Closed, w);

        dl.AddCircle(centre, 3.4f / 24f * s, c, 20, w);
    };

    public static readonly Glyph Shield = (dl, p, s, c, w) =>
        Poly(dl, p, s, c, w, true,
            12, 2, 20, 5, 20, 11.5f, 18.5f, 15.5f, 15.5f, 19, 12, 22,
            8.5f, 19, 5.5f, 15.5f, 4, 11.5f, 4, 5);

    public static readonly Glyph Server = (dl, p, s, c, w) =>
    {
        Rect(dl, p, s, c, w, 2, 3, 22, 9.5f, 2);
        Rect(dl, p, s, c, w, 2, 14.5f, 22, 21, 2);
        Dot(dl, p, s, c, 6, 6.25f, 1.1f);
        Dot(dl, p, s, c, 6, 17.75f, 1.1f);
    };

    public static readonly Glyph Cpu = (dl, p, s, c, w) =>
    {
        Rect(dl, p, s, c, w, 4, 4, 20, 20, 2);
        Rect(dl, p, s, c, w, 9, 9, 15, 15, 1);
        foreach (var t in new[] { 9f, 12f, 15f })
        {
            Line(dl, p, s, c, w, t, 1, t, 4);      // top pins
            Line(dl, p, s, c, w, t, 20, t, 23);    // bottom
            Line(dl, p, s, c, w, 1, t, 4, t);      // left
            Line(dl, p, s, c, w, 20, t, 23, t);    // right
        }
    };

    public static readonly Glyph AlertTriangle = (dl, p, s, c, w) =>
    {
        Poly(dl, p, s, c, w, true, 12, 2.5f, 22.5f, 20.5f, 1.5f, 20.5f);
        Line(dl, p, s, c, w, 12, 9, 12, 14);
        Dot(dl, p, s, c, 12, 17.3f, 1.1f);
    };

    public static readonly Glyph Folder = (dl, p, s, c, w) =>
        Poly(dl, p, s, c, w, true, 2, 20, 2, 5, 9, 5, 11.2f, 8, 22, 8, 22, 20);

    public static readonly Glyph Check = (dl, p, s, c, w) =>
        Poly(dl, p, s, c, w, false, 4, 12.5f, 9.5f, 18, 20, 6);

    public static readonly Glyph X = (dl, p, s, c, w) =>
    {
        Line(dl, p, s, c, w, 5.5f, 5.5f, 18.5f, 18.5f);
        Line(dl, p, s, c, w, 18.5f, 5.5f, 5.5f, 18.5f);
    };

    public static readonly Glyph ChevronRight = (dl, p, s, c, w) =>
        Poly(dl, p, s, c, w, false, 9, 5, 16, 12, 9, 19);

    public static readonly Glyph ChevronLeft = (dl, p, s, c, w) =>
        Poly(dl, p, s, c, w, false, 15, 5, 8, 12, 15, 19);

    public static readonly Glyph ChevronUp = (dl, p, s, c, w) =>
        Poly(dl, p, s, c, w, false, 5, 15, 12, 8, 19, 15);

    public static readonly Glyph Copy = (dl, p, s, c, w) =>
    {
        Rect(dl, p, s, c, w, 9, 9, 21, 21, 2);
        Poly(dl, p, s, c, w, false, 5, 15, 3, 15, 3, 3, 15, 3, 15, 5);
    };

    public static readonly Glyph Trash = (dl, p, s, c, w) =>
    {
        Line(dl, p, s, c, w, 3, 6, 21, 6);
        Poly(dl, p, s, c, w, false, 5.5f, 6, 6.5f, 21, 17.5f, 21, 18.5f, 6);
        Poly(dl, p, s, c, w, false, 9, 6, 9, 3, 15, 3, 15, 6);
        Line(dl, p, s, c, w, 10, 10, 10, 17);
        Line(dl, p, s, c, w, 14, 10, 14, 17);
    };

    public static readonly Glyph Search = (dl, p, s, c, w) =>
    {
        dl.AddCircle(P(p, s, 10.5f, 10.5f), 7.5f / 24f * s, c, 24, w);
        Line(dl, p, s, c, w, 16, 16, 21, 21);
    };

    public static readonly Glyph HardDrive = (dl, p, s, c, w) =>
    {
        Line(dl, p, s, c, w, 2, 12, 22, 12);
        Poly(dl, p, s, c, w, false, 5.5f, 4, 18.5f, 4, 22, 12, 22, 19, 2, 19, 2, 12, 5.5f, 4);
        Dot(dl, p, s, c, 6.5f, 15.5f, 1.1f);
    };

    /// <summary>
    /// A spinner. Unlike the others this is time-dependent: it sweeps an arc whose phase comes from
    /// ImGui's frame clock, so it animates without the caller holding any state.
    /// </summary>
    public static void Spinner(ImDrawListPtr dl, Vector2 pos, float size, Vector4 colour, float stroke)
    {
        var centre = pos + new Vector2(size / 2f, size / 2f);
        var radius = size * 0.36f;
        var t = (float)ImGui.GetTime();
        var start = t * 3.2f;

        dl.PathClear();
        const int segments = 28;
        for (int i = 0; i <= segments; i++)
            dl.PathLineTo(centre + new Vector2(
                MathF.Cos(start + i / (float)segments * MathF.PI * 1.45f),
                MathF.Sin(start + i / (float)segments * MathF.PI * 1.45f)) * radius);
        dl.PathStroke(ImGui.ColorConvertFloat4ToU32(colour), ImDrawFlags.None, stroke);
    }
}
