using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// The single source of truth for how the Game Mode UI looks. Every colour, size, spacing and
/// rounding value in <c>Ui/</c> comes from here — no literal colour may appear anywhere else.
///
/// The palette is the Checkpoint dark set (docs/tasks/checkpoint-ui/plan.md "Tokens"), copied
/// verbatim from <c>web/src/index.css</c>'s <c>@theme</c> block — the Deck is dark-only (plan.md
/// "Surfaces"), so only that half of the console's two palettes applies here. <see cref="Accent"/>
/// is the one token that is NOT fixed: it is re-derived from <see cref="AppearancePalette"/>
/// whenever this machine's effective appearance changes, exactly like the console's inline CSS
/// variable and the tray's <c>MarkIcon</c>. Everything else stays constant — swapping an accent
/// must never make Ember read as "healthy" or "warning", which is why <see cref="Safe"/> and
/// <see cref="Watch"/> are separate, fixed tokens rather than derived from it.
/// </summary>
static class Theme
{
    // ── Palette ──────────────────────────────────────────────────────────────────────────────
    // Straight hex/255, no gamma conversion: the GL backend is not an sRGB framebuffer, so these
    // land on screen as the same values the browser paints. If they look washed out, the
    // framebuffer picked up sRGB somewhere — fix that, do not fudge these constants.

    public static readonly Vector4 Ink   = Rgb(0x0f0f10);   // page
    public static readonly Vector4 Panel = Rgb(0x141416);   // card
    public static readonly Vector4 Raise = Rgb(0x191a1c);   // control / table header
    public static readonly Vector4 Tile  = Rgb(0x1b1c1f);   // inset
    public static readonly Vector4 Hover = Rgb(0x202124);
    public static readonly Vector4 Fg    = Rgb(0xf0eee9);   // text
    public static readonly Vector4 Dim   = Rgb(0xa09d97);   // secondary text
    public static readonly Vector4 Faint = Rgb(0x6b6862);   // tertiary text
    public static readonly Vector4 Line  = Rgb(0x242427);   // border
    public static readonly Vector4 Row   = Rgb(0x1d1d20);   // table rule

    /// <summary>Server and machine agree — plan.md's colour rule. Fixed: never swapped by an accent.</summary>
    public static readonly Vector4 Safe  = Rgb(0x7fa96a);
    /// <summary>Something failed but will retry, or needs attention. Fixed, same reason as <see cref="Safe"/>.</summary>
    public static readonly Vector4 Watch = Rgb(0xd9a63f);

    /// <summary>A decision is waiting. The one token this machine's Appearance setting rewrites —
    /// see <see cref="SetAccent"/>. Ember (the default accent) is the initial value.</summary>
    public static Vector4 Accent { get; private set; } = Rgb(0xe0533c);
    /// <summary>Ink that sits on a filled <see cref="Accent"/> surface (plan.md's "on-accent").</summary>
    public static Vector4 OnAccent { get; private set; } = Rgb(0x160f0e);

    public static Vector4 AccentSoft { get; private set; }
    public static Vector4 AccentLine { get; private set; }
    public static Vector4 AccentInk  { get; private set; }
    public static readonly Vector4 SafeSoft = Lerp(Panel, Safe, 0.14f);
    public static readonly Vector4 SafeLine = Lerp(Panel, Safe, 0.40f);
    public static readonly Vector4 SafeInk  = Lerp(Fg, Safe, 0.82f);
    public static readonly Vector4 WatchSoft = Lerp(Panel, Watch, 0.14f);
    public static readonly Vector4 WatchLine = Lerp(Panel, Watch, 0.40f);
    public static readonly Vector4 WatchInk  = Lerp(Fg, Watch, 0.82f);

    static Theme() => RecomputeAccentDerived();

    /// <summary>
    /// Point <see cref="Accent"/>/<see cref="OnAccent"/> at a different id and rebake ImGui's style
    /// table so every stock-widget colour (checkboxes, sliders, the nav highlight) picks it up too —
    /// the hand-painted widgets in <c>Widgets.cs</c> read the static fields directly every frame, so
    /// they need no rebake, but <c>ApplyStyle</c>'s <c>style.Colors[...]</c> entries are baked once
    /// and must be re-set explicitly. A no-op re-apply (same id) costs one style-table write, which
    /// is cheap enough to not bother guarding against.
    /// </summary>
    public static unsafe void SetAccent(AccentColors colours)
    {
        Accent = Rgb(colours.Dark);
        OnAccent = Rgb(colours.DarkOn);
        RecomputeAccentDerived();
        if (_styled) ApplyStyle();
    }

    private static void RecomputeAccentDerived()
    {
        AccentSoft = Lerp(Panel, Accent, 0.14f);
        AccentLine = Lerp(Panel, Accent, 0.42f);
        AccentInk  = Lerp(Fg, Accent, 0.80f);
    }

    /// <summary>Linear (not oklab — ImGui has no colour-management pipeline to do it in) blend
    /// toward <paramref name="target"/>, matching the console's <c>color-mix(in oklab, X N%, Y)</c>
    /// derivation closely enough at these percentages that the difference is not resolvable.</summary>
    private static Vector4 Lerp(Vector4 baseColour, Vector4 target, float t) =>
        baseColour + (target - baseColour) * t;

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
        public const float RailWidth = 236f;
        public const float HeaderHeight = 64f;
        public const float HintBarHeight = 44f;
        public const float Gutter = 24f;

        /// <summary>A two-line row's fixed height (plan.md "Layout rules": "Two-line rows" /
        /// "Rows are 62px tall" — checkpoint-ui/prototype.html's Deck screen). Fixed rather than
        /// measured from the current font's line height, so it holds exactly regardless of which
        /// TTFs happened to bake (see <see cref="LoadFonts"/>'s fallback-font path).</summary>
        public const float RowHeight = 62f;
        /// <summary>A single-line row's height (the folder browser's directory listing — nothing to
        /// put on a second line there).</summary>
        public const float RowHeightSingle = 46f;

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
    // Inter + JetBrains Mono. The console and agent-ui both self-hosted Archivo in Groups 1/3
    // (docs/tasks/checkpoint-ui/implementation-grouping.md "Group 1"), but that needs real Archivo
    // TTFs embedded as resources, and this environment has none to vendor — the same asset gap
    // Group 1 hit for PNG/ICO rasterization (implementation.md Phase 8). Sizing is Checkpoint's
    // ("16px minimum body text", implementation.md Phase 6 item 1); the face itself is a follow-up
    // once Archivo TTFs are available to embed.

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

    private static bool _styled;

    /// <summary>
    /// Map the palette onto ImGui's style table and replace its cramped debug-tool metrics. This is
    /// most of what separates "looks like a tool" from "looks like an app" — stock ImGui spacing is
    /// tuned for dense inspector panels, not a handheld held at arm's length.
    /// </summary>
    public static unsafe void ApplyStyle()
    {
        _styled = true;
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

        c[(int)ImGuiCol.WindowBg]              = Ink;
        c[(int)ImGuiCol.ChildBg]               = Panel;
        c[(int)ImGuiCol.PopupBg]               = Panel;
        c[(int)ImGuiCol.MenuBarBg]             = Panel;

        c[(int)ImGuiCol.Border]                = Line;
        c[(int)ImGuiCol.BorderShadow]          = Alpha(Ink, 0f);
        c[(int)ImGuiCol.Separator]             = Row;
        c[(int)ImGuiCol.SeparatorHovered]      = Line;
        c[(int)ImGuiCol.SeparatorActive]       = Accent;

        c[(int)ImGuiCol.Text]                  = Fg;
        c[(int)ImGuiCol.TextDisabled]          = Dim;
        c[(int)ImGuiCol.TextSelectedBg]        = Alpha(Accent, 0.35f);

        c[(int)ImGuiCol.FrameBg]               = Raise;
        c[(int)ImGuiCol.FrameBgHovered]        = Alpha(Accent, 0.18f);
        c[(int)ImGuiCol.FrameBgActive]         = Alpha(Accent, 0.28f);

        c[(int)ImGuiCol.Button]                = Raise;
        c[(int)ImGuiCol.ButtonHovered]         = Alpha(Accent, 0.22f);
        c[(int)ImGuiCol.ButtonActive]          = Alpha(Accent, 0.34f);

        c[(int)ImGuiCol.Header]                = Tile;
        c[(int)ImGuiCol.HeaderHovered]         = Alpha(Accent, 0.22f);
        c[(int)ImGuiCol.HeaderActive]          = Alpha(Accent, 0.30f);

        c[(int)ImGuiCol.CheckMark]             = Accent;
        c[(int)ImGuiCol.SliderGrab]            = Accent;
        c[(int)ImGuiCol.SliderGrabActive]      = Accent;

        c[(int)ImGuiCol.TitleBg]               = Panel;
        c[(int)ImGuiCol.TitleBgActive]         = Panel;
        c[(int)ImGuiCol.TitleBgCollapsed]      = Panel;

        c[(int)ImGuiCol.ScrollbarBg]           = Panel;
        c[(int)ImGuiCol.ScrollbarGrab]         = Line;
        c[(int)ImGuiCol.ScrollbarGrabHovered]  = Faint;
        c[(int)ImGuiCol.ScrollbarGrabActive]   = Accent;

        // ImGui.NET 1.90.8 predates the TabSelected/NavCursor renames — these are the old names.
        c[(int)ImGuiCol.Tab]                   = Raise;
        c[(int)ImGuiCol.TabHovered]            = Alpha(Accent, 0.22f);
        c[(int)ImGuiCol.TabActive]             = Tile;

        c[(int)ImGuiCol.TableHeaderBg]         = Raise;
        c[(int)ImGuiCol.TableBorderStrong]     = Line;
        c[(int)ImGuiCol.TableBorderLight]      = Row;
        c[(int)ImGuiCol.TableRowBg]            = Alpha(Panel, 0f);
        c[(int)ImGuiCol.TableRowBgAlt]         = Alpha(Panel, 0.4f);

        // The gamepad focus ring is the ONLY cursor a Deck user has — there is no mouse pointer to
        // fall back on. Make it unmistakable: full-strength accent, thicker than ImGui's hairline.
        // "focus ring is 2px accent + 4px halo" — plan.md "Surfaces" table.
        c[(int)ImGuiCol.NavHighlight]          = Accent;
        c[(int)ImGuiCol.NavWindowingHighlight] = Accent;
        c[(int)ImGuiCol.NavWindowingDimBg]     = Alpha(Ink, 0.6f);
        c[(int)ImGuiCol.ModalWindowDimBg]      = Alpha(Ink, 0.75f);
    }
}
