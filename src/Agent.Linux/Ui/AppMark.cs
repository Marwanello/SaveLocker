using System.Numerics;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// Draws the chosen app mark (Pixel lock, Cartridge, Memory card) straight into the ImGui draw list,
/// in the live accent — the header's replacement for the pre-Checkpoint <c>logo-96.png</c>, which was
/// a fixed-colour raster that never followed an accent change and stayed the old brand mark after
/// Group 6 repainted everything around it.
/// <para>
/// The geometry is the exact 32-unit grid of <c>web/src/appearance.ts</c>'s <c>MARKS</c> and
/// <c>src/Agent/MarkIcon.cs</c> (the Windows tray icon, the other C# port of the same shapes). Vector,
/// not a texture: no embedded PNG to keep in step with the palette, no rasterizer (the gap
/// <c>implementation.md</c> Phase 8 already flags), and it repaints for free when the accent or the
/// mark changes since nothing is baked.
/// </para>
/// <para>
/// Body takes the accent and the punched detail takes on-accent — agent-ui's own topbar draws
/// <c>&lt;Mark/&gt;</c> the same way (<c>agent-ui/src/App.tsx</c>'s <c>.sl-brand</c>), directly on the
/// panel background with no colour tile behind it. That is a different rule from the Windows tray
/// icon's real cut-out transparency (brand-kit.html's "one colour, no punch" mono variant) — this
/// surface, like the agent window, is not fighting an arbitrary host background.
/// </para>
/// </summary>
static class AppMark
{
    public static void Draw(ImDrawListPtr dl, Vector2 pos, float size, string? markId, Vector4 body, Vector4 punch)
    {
        var bodyCol = Widgets.U32(body);
        var punchCol = Widgets.U32(punch);
        switch (markId)
        {
            case "cartridge": Cartridge(dl, pos, size, body, bodyCol, punchCol); break;
            case "memcard": MemoryCard(dl, pos, size, bodyCol, punchCol); break;
            default: PixelLock(dl, pos, size, bodyCol, punchCol); break;
        }
    }

    private static Vector2 P(Vector2 pos, float size, float x, float y) =>
        new(pos.X + x / 32f * size, pos.Y + y / 32f * size);

    private static float S(float size, float v) => v / 32f * size;

    private static void Rect(ImDrawListPtr dl, Vector2 pos, float size, uint col,
        float x, float y, float w, float h, float rounding = 0f) =>
        dl.AddRectFilled(P(pos, size, x, y), P(pos, size, x + w, y + h), col, S(size, rounding));

    private static void PixelLock(ImDrawListPtr dl, Vector2 pos, float size, uint body, uint punch)
    {
        Rect(dl, pos, size, body, 12, 3, 8, 4);
        Rect(dl, pos, size, body, 8, 7, 4, 5);
        Rect(dl, pos, size, body, 20, 7, 4, 5);
        Rect(dl, pos, size, body, 5, 12, 22, 16);
        Rect(dl, pos, size, punch, 14, 16, 4, 4);
        Rect(dl, pos, size, punch, 15, 20, 2, 5);
    }

    private static void Cartridge(ImDrawListPtr dl, Vector2 pos, float size, Vector4 bodyV, uint body, uint punch)
    {
        Rect(dl, pos, size, body, 5, 5, 22, 22, 4);
        Rect(dl, pos, size, punch, 8.5f, 8.5f, 15, 8, 2);
        dl.AddCircleFilled(P(pos, size, 16, 12.5f), S(size, 2.1f), body, 16);

        // The three label pins: on-accent at 65% over the body, flattened to a solid colour since
        // this surface has no real transparency to punch through (see the header on Draw above).
        var punchV = ImGui.ColorConvertU32ToFloat4(punch);
        var pinCol = Widgets.U32(Vector4.Lerp(bodyV, punchV, 0.65f));
        var pinStroke = MathF.Max(1f, S(size, 2f));
        foreach (var x in stackalloc float[] { 10.5f, 16f, 21.5f })
            dl.AddLine(P(pos, size, x, 27f), P(pos, size, x, 23.6f), pinCol, pinStroke);
    }

    private static void MemoryCard(ImDrawListPtr dl, Vector2 pos, float size, uint body, uint punch)
    {
        // Chamfered top-left, three rounded corners — the same path as MarkIcon.cs's MemoryCard.
        dl.PathClear();
        dl.PathLineTo(P(pos, size, 11f, 3.6f));
        dl.PathLineTo(P(pos, size, 24.4f, 3.6f));
        dl.PathArcTo(P(pos, size, 24.4f, 7.2f), S(size, 3.6f), Deg(270), Deg(360), 8);
        dl.PathLineTo(P(pos, size, 28f, 24.8f));
        dl.PathArcTo(P(pos, size, 24.4f, 24.8f), S(size, 3.6f), Deg(0), Deg(90), 8);
        dl.PathLineTo(P(pos, size, 7.6f, 28.4f));
        dl.PathArcTo(P(pos, size, 7.6f, 24.8f), S(size, 3.6f), Deg(90), Deg(180), 8);
        dl.PathLineTo(P(pos, size, 4f, 10.6f));
        dl.PathFillConvex(body);

        Rect(dl, pos, size, punch, 7.4f, 11.4f, 5.6f, 12.6f, 1.6f);
        dl.AddCircleFilled(P(pos, size, 20.6f, 15.4f), S(size, 2.6f), punch, 16);
        var keyhole = new[]
        {
            P(pos, size, 19.3f, 17.4f), P(pos, size, 21.9f, 17.4f),
            P(pos, size, 22.8f, 22.8f), P(pos, size, 18.4f, 22.8f),
        };
        dl.AddConvexPolyFilled(ref keyhole[0], keyhole.Length, punch);

        var lineStroke = MathF.Max(1f, S(size, 1.5f));
        foreach (var y in stackalloc float[] { 15.2f, 18.6f, 22f })
            dl.AddLine(P(pos, size, 7.4f, y), P(pos, size, 13f, y), body, lineStroke);
    }

    private static float Deg(float degrees) => degrees * MathF.PI / 180f;
}
