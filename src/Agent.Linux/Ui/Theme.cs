using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// The single source of truth for how the Game Mode UI looks. Every colour, size, spacing and
/// rounding value in <c>Ui/</c> comes from here — no literal colour may appear anywhere else.
///
/// The palette is lifted verbatim from <c>web/src/index.css</c> <c>@theme</c>, which the console and
/// the WinForms agent's React UI both consume, so all three surfaces read as one product. If a token
/// changes there, change it here; they are meant to stay in lockstep.
/// </summary>
static class Theme
{
    // ── Palette ──────────────────────────────────────────────────────────────────────────────
    // Straight hex/255, no gamma conversion: the GL backend is not an sRGB framebuffer, so these
    // land on screen as the same values the browser paints. If they look washed out, the
    // framebuffer picked up sRGB somewhere — fix that, do not fudge these constants.

    public static readonly Vector4 BgGlobal      = Rgb(0x2A3238);
    public static readonly Vector4 BgCard        = Rgb(0x1E252A);
    public static readonly Vector4 BgTableHd     = Rgb(0x222D34);
    public static readonly Vector4 BgRowSep      = Rgb(0x252E35);

    public static readonly Vector4 TextPrimary   = Rgb(0xECEFF1);
    public static readonly Vector4 TextMuted     = Rgb(0x9CA3AF);
    public static readonly Vector4 TextSecondary = Rgb(0x8B9AAA);
    public static readonly Vector4 TextDim       = Rgb(0x556070);
    public static readonly Vector4 TextFaint     = Rgb(0x64748B);

    public static readonly Vector4 AccentGreen   = Rgb(0x129271);
    public static readonly Vector4 AccentAmber   = Rgb(0xF4A60D);
    public static readonly Vector4 AccentAmberLt = Rgb(0xFDCE63);
    // Matches the dashboard/agent-ui conflict card's escalated-border red (ConflictCard.tsx) — the
    // one accent this screen reserves for "overdue," never for an ordinary conflict.
    public static readonly Vector4 AccentRed     = Rgb(0xE5534B);

    public static readonly Vector4 Border        = Rgb(0x494949);

    // Derived tints the React UI already uses. Reproduced rather than reinvented so the two
    // surfaces match exactly — see Sidebar.tsx, StatusHeader.tsx, OverviewView.tsx.
    public static readonly Vector4 NavActiveBg   = Alpha(AccentGreen, 0.14f);
    public static readonly Vector4 NavHoverBg    = Alpha(AccentGreen, 0.07f);
    public static readonly Vector4 ChipBg        = Alpha(AccentGreen, 0.07f);
    public static readonly Vector4 ChipBorder    = Alpha(AccentGreen, 0.20f);
    public static readonly Vector4 WarnBg        = Alpha(AccentAmber, 0.12f);
    public static readonly Vector4 WarnBorder    = Alpha(AccentAmber, 0.45f);

    public static Vector4 Alpha(Vector4 c, float a) => c with { W = a };

    private static Vector4 Rgb(int hex) => new(
        ((hex >> 16) & 0xFF) / 255f,
        ((hex >> 8) & 0xFF) / 255f,
        (hex & 0xFF) / 255f,
        1f);

    // ── Metrics ──────────────────────────────────────────────────────────────────────────────
    // Sized for a 1280x800 handheld held at arm's length, which is a smaller angular size than a
    // desktop monitor at the same pixel count. Validate on the Deck, never on a dev display.

    public static class Space
    {
        public const float Xs = 4f;
        public const float Sm = 8f;
        public const float Md = 14f;
        public const float Lg = 20f;
        public const float Xl = 28f;
    }

    public static class Rounding
    {
        public const float Card = 8f;
        public const float Button = 6f;
        public const float Chip = 5f;
        public const float Pill = 999f;
    }

    public static class Layout
    {
        public const float RailWidth = 220f;
        public const float HeaderHeight = 64f;
        public const float HintBarHeight = 44f;
        public const float Gutter = 24f;

        /// <summary>
        /// Clear space every focusable widget needs on all four sides for its focus ring, which is
        /// drawn OUTSIDE the widget's rect (see <see cref="Widgets.FocusRing"/>).
        ///
        /// This is a contract, not a suggestion. A child window clips its contents, and ImGui gives a
        /// child WindowPadding only when it has Border or AlwaysUseWindowPadding — a child with
        /// neither has ZERO padding, so a full-width row inside it touches both edges and its ring is
        /// clipped away entirely. That shipped in v0.4.0 and was reported from the Deck as rings
        /// getting cut off near the pane boundaries.
        ///
        /// Any child window hosting focusable widgets must therefore reserve at least this much
        /// padding, and any change to the ring's geometry must be reflected here.
        /// </summary>
        public const float FocusClearance = 10f;
    }

    // ── Fonts ────────────────────────────────────────────────────────────────────────────────
    // Inter + JetBrains Mono, matching the console. ImGui bakes one atlas entry per (face, size),
    // so the scale is deliberately short. All six fall back to the built-in font when the TTFs are
    // absent, which keeps a dev build that has not vendored them working.

    public static ImFontPtr Display;
    public static ImFontPtr Title;
    public static ImFontPtr Body;
    public static ImFontPtr BodyStrong;
    public static ImFontPtr Caption;
    public static ImFontPtr Mono;

    /// <summary>True when the real TTFs were found and baked; false when running on ImGui's default font.</summary>
    public static bool FontsLoaded { get; private set; }

    private const string RegularResource    = "SaveLocker.Agent.Linux.Ui.Fonts.Inter-Regular.ttf";
    private const string SemiBoldResource   = "SaveLocker.Agent.Linux.Ui.Fonts.Inter-SemiBold.ttf";
    private const string MonoResource       = "SaveLocker.Agent.Linux.Ui.Fonts.JetBrainsMono-Regular.ttf";

    // ImGui reads font bytes lazily while baking, so the unmanaged copies must outlive the call.
    // We hand ownership to nobody: FontDataOwnedByAtlas is cleared and these are never freed. That
    // is deliberate — ImGui frees with its own allocator, and this is a few hundred KB held for the
    // life of a process the user closes in minutes.
    private static readonly List<IntPtr> _fontBlobs = new();

    /// <summary>
    /// Add the fonts to the atlas. Must run inside <c>ImGuiController</c>'s <c>onConfigureIO</c>
    /// callback: Silk invokes it before building the font device texture, so anything added here is
    /// baked. Adding fonts after the controller is constructed silently does nothing.
    /// </summary>
    public static unsafe void LoadFonts()
    {
        var io = ImGui.GetIO();

        var regular  = ReadResource(RegularResource);
        var semiBold = ReadResource(SemiBoldResource);
        var mono     = ReadResource(MonoResource);

        if (regular is null || semiBold is null || mono is null)
        {
            // No vendored TTFs — keep ImGui's default font for every role so callers can push
            // Theme.Body unconditionally without a null check on every screen.
            var fallback = io.Fonts.AddFontDefault();
            Display = Title = Body = BodyStrong = Caption = Mono = fallback;
            FontsLoaded = false;
            return;
        }

        Body       = Bake(io, regular, 16f);
        BodyStrong = Bake(io, semiBold, 16f);
        Display    = Bake(io, semiBold, 30f);
        Title      = Bake(io, semiBold, 22f);
        Caption    = Bake(io, regular, 13f);
        Mono       = Bake(io, mono, 14f);

        // ImGui draws with Fonts[0] unless something pushes another, so body text must be the
        // default. ImGuiIOPtr.FontDefault is read-only in this binding; go through the native struct.
        io.NativePtr->FontDefault = Body.NativePtr;
        FontsLoaded = true;
    }

    private static unsafe ImFontPtr Bake(ImGuiIOPtr io, byte[] ttf, float pixels)
    {
        var blob = Marshal.AllocHGlobal(ttf.Length);
        Marshal.Copy(ttf, 0, blob, ttf.Length);
        _fontBlobs.Add(blob);

        var cfg = new ImFontConfigPtr(ImGuiNative.ImFontConfig_ImFontConfig())
        {
            FontDataOwnedByAtlas = false,
        };

        return io.Fonts.AddFontFromMemoryTTF(blob, ttf.Length, pixels, cfg);
    }

    private static byte[]? ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    // Push/pop are paired and null-guarded so a screen can reference any role unconditionally,
    // whether or not the TTFs were vendored into this build.
    public static unsafe void PushFont(ImFontPtr font)
    {
        if (font.NativePtr is not null) ImGui.PushFont(font);
    }

    public static unsafe void PopFont(ImFontPtr font)
    {
        if (font.NativePtr is not null) ImGui.PopFont();
    }

    // ── Style ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Map the palette onto ImGui's style table and replace its cramped debug-tool metrics. This is
    /// most of what separates "looks like a tool" from "looks like an app" — stock ImGui spacing is
    /// tuned for dense inspector panels, not a handheld held at arm's length.
    /// </summary>
    public static unsafe void ApplyStyle()
    {
        var style = ImGui.GetStyle();

        style.WindowRounding    = 0f;   // the root window is full-bleed; rounding it would show seams
        style.ChildRounding     = Rounding.Card;
        style.FrameRounding     = Rounding.Button;
        style.PopupRounding     = Rounding.Card;
        style.ScrollbarRounding = Rounding.Pill;
        style.GrabRounding      = Rounding.Pill;
        style.TabRounding       = Rounding.Button;

        style.WindowBorderSize  = 0f;
        style.ChildBorderSize   = 1f;
        style.FrameBorderSize   = 1f;
        style.PopupBorderSize   = 1f;

        style.WindowPadding     = new Vector2(Layout.Gutter, Layout.Gutter);
        style.FramePadding      = new Vector2(Space.Md, Space.Sm + 2f);
        style.ItemSpacing       = new Vector2(Space.Sm + 2f, Space.Sm + 2f);
        style.ItemInnerSpacing  = new Vector2(Space.Sm, Space.Sm);
        style.CellPadding       = new Vector2(Space.Md, Space.Sm);
        style.IndentSpacing     = Space.Lg;

        style.ScrollbarSize     = 12f;
        style.GrabMinSize       = 16f;

        // Touch targets: a 7" panel wants room for a thumb, and gamepad focus rings need room to breathe.
        style.SeparatorTextBorderSize = 1f;
        style.SeparatorTextPadding    = new Vector2(Space.Lg, Space.Xs);

        var c = style.Colors;

        c[(int)ImGuiCol.WindowBg]              = BgGlobal;
        c[(int)ImGuiCol.ChildBg]               = BgCard;
        c[(int)ImGuiCol.PopupBg]               = BgCard;
        c[(int)ImGuiCol.MenuBarBg]             = BgCard;

        c[(int)ImGuiCol.Border]                = Border;
        c[(int)ImGuiCol.BorderShadow]          = Alpha(BgGlobal, 0f);
        c[(int)ImGuiCol.Separator]             = BgRowSep;
        c[(int)ImGuiCol.SeparatorHovered]      = Border;
        c[(int)ImGuiCol.SeparatorActive]       = AccentGreen;

        c[(int)ImGuiCol.Text]                  = TextPrimary;
        c[(int)ImGuiCol.TextDisabled]          = TextMuted;
        c[(int)ImGuiCol.TextSelectedBg]        = Alpha(AccentGreen, 0.35f);

        c[(int)ImGuiCol.FrameBg]               = BgTableHd;
        c[(int)ImGuiCol.FrameBgHovered]        = Alpha(AccentGreen, 0.18f);
        c[(int)ImGuiCol.FrameBgActive]         = Alpha(AccentGreen, 0.28f);

        c[(int)ImGuiCol.Button]                = BgTableHd;
        c[(int)ImGuiCol.ButtonHovered]         = Alpha(AccentGreen, 0.22f);
        c[(int)ImGuiCol.ButtonActive]          = Alpha(AccentGreen, 0.34f);

        c[(int)ImGuiCol.Header]                = NavActiveBg;
        c[(int)ImGuiCol.HeaderHovered]         = Alpha(AccentGreen, 0.22f);
        c[(int)ImGuiCol.HeaderActive]          = Alpha(AccentGreen, 0.30f);

        c[(int)ImGuiCol.CheckMark]             = AccentGreen;
        c[(int)ImGuiCol.SliderGrab]            = AccentGreen;
        c[(int)ImGuiCol.SliderGrabActive]      = AccentGreen;

        c[(int)ImGuiCol.TitleBg]               = BgCard;
        c[(int)ImGuiCol.TitleBgActive]         = BgCard;
        c[(int)ImGuiCol.TitleBgCollapsed]      = BgCard;

        c[(int)ImGuiCol.ScrollbarBg]           = BgCard;
        c[(int)ImGuiCol.ScrollbarGrab]         = Border;
        c[(int)ImGuiCol.ScrollbarGrabHovered]  = TextDim;
        c[(int)ImGuiCol.ScrollbarGrabActive]   = AccentGreen;

        // ImGui.NET 1.90.8 predates the TabSelected/NavCursor renames — these are the old names.
        c[(int)ImGuiCol.Tab]                   = BgTableHd;
        c[(int)ImGuiCol.TabHovered]            = Alpha(AccentGreen, 0.22f);
        c[(int)ImGuiCol.TabActive]             = NavActiveBg;

        c[(int)ImGuiCol.TableHeaderBg]         = BgTableHd;
        c[(int)ImGuiCol.TableBorderStrong]     = Border;
        c[(int)ImGuiCol.TableBorderLight]      = BgRowSep;
        c[(int)ImGuiCol.TableRowBg]            = Alpha(BgCard, 0f);
        c[(int)ImGuiCol.TableRowBgAlt]         = Alpha(BgCard, 0.4f);

        // The gamepad focus ring is the ONLY cursor a Deck user has — there is no mouse pointer to
        // fall back on. Make it unmistakable: full-strength accent, thicker than ImGui's hairline.
        c[(int)ImGuiCol.NavHighlight]          = AccentGreen;
        c[(int)ImGuiCol.NavWindowingHighlight] = AccentGreen;
        c[(int)ImGuiCol.NavWindowingDimBg]     = Alpha(BgGlobal, 0.6f);
        c[(int)ImGuiCol.ModalWindowDimBg]      = Alpha(BgGlobal, 0.75f);
    }
}
