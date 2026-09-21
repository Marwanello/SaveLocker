using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// Draws the chosen app mark (Pixel lock, Cartridge, Memory card) as a Windows <see cref="Icon"/>, in
/// the chosen accent, at the size the shell actually asks for.
/// <para>
/// Drawn at runtime instead of shipped as one <c>.ico</c> per mark per accent — that would be
/// fifteen files to keep in step with three shape tables, and the build environment has no SVG
/// rasterizer to make them with (<c>implementation.md</c> Phase 8). The shapes are the exact geometry
/// of <c>web/src/assets/marks/*.svg</c>, in the same 32-unit box.
/// </para>
/// <para>
/// The mark's inner detail (the lock's keyhole, the cartridge's label, the card's contacts) is cut out
/// as real transparency rather than painted in an "on-accent" colour: in a notification area the
/// background is whatever the user's taskbar is, and a hole is correct on both. That is the
/// brand-kit's own rule for a tray-sized mark — one colour, no punch fill.
/// </para>
/// </summary>
internal static class MarkIcon
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// A new icon the caller owns and must dispose. <paramref name="pixels"/> is the edge length —
    /// pass the shell's own small-icon size so the mark is drawn 1:1 instead of being rescaled by
    /// the taskbar, which is what turns a 4 px grid into mush.
    /// </summary>
    public static Icon Render(AppearanceDto look, int pixels)
    {
        var colours = AppearancePalette.For(look.Accent);
        var lightTaskbar = TaskbarIsLight();
        var body = Rgb(lightTaskbar ? colours.Light : colours.Dark);

        using var bmp = new Bitmap(pixels, pixels, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            // The pixel mark is drawn on a 4-unit grid on purpose; anti-aliasing would smear its edges
            // across two device pixels at 24 px. The other two are curves and want it.
            g.SmoothingMode = look.Mark == "pixel" ? SmoothingMode.None : SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.ScaleTransform(pixels / 32f, pixels / 32f);

            switch (look.Mark)
            {
                case "cartridge": Cartridge(g, body); break;
                case "memcard": MemoryCard(g, body); break;
                default: PixelLock(g, body); break;
            }
        }

        var handle = bmp.GetHicon();
        try
        {
            // FromHandle does not own the handle; Clone makes an icon that does, so the original can
            // be destroyed without leaking one GDI object per render.
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void PixelLock(Graphics g, Color body)
    {
        using var fill = new SolidBrush(body);
        g.FillRectangle(fill, 12, 3, 8, 4);
        g.FillRectangle(fill, 8, 7, 4, 5);
        g.FillRectangle(fill, 20, 7, 4, 5);
        g.FillRectangle(fill, 5, 12, 22, 16);

        Punch(g, () =>
        {
            using var hole = new SolidBrush(Color.Transparent);
            g.FillRectangle(hole, 14, 16, 4, 4);
            g.FillRectangle(hole, 15, 20, 2, 5);
        });
    }

    private static void Cartridge(Graphics g, Color body)
    {
        using var fill = new SolidBrush(body);
        FillRounded(g, fill, 5, 5, 22, 22, 4);

        Punch(g, () =>
        {
            using var hole = new SolidBrush(Color.Transparent);
            FillRounded(g, hole, 8.5f, 8.5f, 15, 8, 2);

            // The three edge pins are the on-accent colour at 65 % over the body, i.e. the body seen
            // through a 65 % hole. SourceCopy writes alpha directly, so that is alpha 35 %.
            // Clipped to the body: the SVG's round caps run a unit past its bottom edge, which on a
            // dark page is an invisible dark stub but here would be faint accent pixels outside it.
            using var pin = new Pen(Color.FromArgb(89, body), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.SetClip(new RectangleF(0, 0, 32, 27));
            foreach (var x in new[] { 10.5f, 16f, 21.5f })
                g.DrawLine(pin, x, 27f, x, 23.6f);
            g.ResetClip();
        });

        // The keyhole's dot sits inside the label cut-out, so it goes back on top in the body colour.
        g.FillEllipse(fill, 16f - 2.1f, 12.5f - 2.1f, 4.2f, 4.2f);
    }

    private static void MemoryCard(Graphics g, Color body)
    {
        using var fill = new SolidBrush(body);
        using (var outline = new GraphicsPath())
        {
            // Chamfered top-left, three rounded corners: M11 3.6 h13.4 A3.6 … V10.6 z in the SVG.
            outline.AddLine(11f, 3.6f, 24.4f, 3.6f);
            outline.AddArc(20.8f, 3.6f, 7.2f, 7.2f, 270, 90);
            outline.AddLine(28f, 7.2f, 28f, 24.8f);
            outline.AddArc(20.8f, 21.2f, 7.2f, 7.2f, 0, 90);
            outline.AddLine(24.4f, 28.4f, 7.6f, 28.4f);
            outline.AddArc(4f, 21.2f, 7.2f, 7.2f, 90, 90);
            outline.AddLine(4f, 24.8f, 4f, 10.6f);
            outline.CloseFigure();
            g.FillPath(fill, outline);
        }

        Punch(g, () =>
        {
            using var hole = new SolidBrush(Color.Transparent);
            FillRounded(g, hole, 7.4f, 11.4f, 5.6f, 12.6f, 1.6f);
            g.FillEllipse(hole, 20.6f - 2.6f, 15.4f - 2.6f, 5.2f, 5.2f);
            g.FillPolygon(hole, new[] { new PointF(19.3f, 17.4f), new PointF(21.9f, 17.4f), new PointF(22.8f, 22.8f), new PointF(18.4f, 22.8f) });
        });

        // The three contact lines cross the cut-out in the body colour.
        using var line = new Pen(body, 1.5f);
        foreach (var y in new[] { 15.2f, 18.6f, 22f })
            g.DrawLine(line, 7.4f, y, 13f, y);
    }

    /// <summary>Run drawing that must REPLACE pixels (alpha included) instead of blending over them.</summary>
    private static void Punch(Graphics g, Action draw)
    {
        var previous = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        try { draw(); }
        finally { g.CompositingMode = previous; }
    }

    private static void FillRounded(Graphics g, Brush brush, float x, float y, float w, float h, float r)
    {
        using var path = new GraphicsPath();
        var d = r * 2;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    private static Color Rgb(int rgb) => Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>Whether the taskbar (where the tray lives) is currently light. Dark unless the user's
    /// own personalisation says otherwise, which is also what a Windows without the setting does.</summary>
    private static bool TaskbarIsLight()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }
}
