using System.Numerics;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// The component vocabulary the screens are built from. Most of these are painted with
/// <see cref="ImDrawListPtr"/> over an <c>InvisibleButton</c> rather than using ImGui's stock
/// widgets: the stock ones carry a debug-inspector look that no amount of style-table tuning
/// removes, and going through InvisibleButton keeps full gamepad-nav participation for free
/// (it is a real ImGui item, so focus, activation and clipping all behave normally).
///
/// Everything here reads its colours and metrics from <see cref="Theme"/>.
/// </summary>
static class Widgets
{
    // ── Motion ───────────────────────────────────────────────────────────────────────────────

    // Immediate mode keeps no per-widget state, so tweens live here, keyed by ImGui's own item ID.
    // The map is small and bounded by the number of animated widgets ever drawn, which for four
    // screens is dozens — not worth evicting.
    private static readonly Dictionary<uint, float> _tweens = new();

    /// <summary>
    /// Ease <paramref name="id"/>'s stored value toward <paramref name="target"/> and return it.
    /// Frame-rate independent: <paramref name="speed"/> is "fraction of the remaining distance per
    /// second", so the motion looks the same whether the loop runs at 30 or 60.
    /// </summary>
    /// <param name="initial">
    /// Where the value starts the first time this id is seen. Left as NaN it starts AT the target,
    /// so a widget does not animate in from nowhere on its first frame — which is what steady-state
    /// hover and focus want. Pass 0 for something that should animate on appearance.
    /// </param>
    public static float Tween(uint id, float target, float speed = 14f, float initial = float.NaN)
    {
        var dt = ImGui.GetIO().DeltaTime;
        if (!_tweens.TryGetValue(id, out var current))
            current = float.IsNaN(initial) ? target : initial;

        // 1 - e^(-speed*dt) is the exponential-decay form; a naive lerp(current, target, speed*dt)
        // overshoots and oscillates once dt gets large (a stalled frame, a window drag).
        current += (target - current) * (1f - MathF.Exp(-speed * dt));
        if (MathF.Abs(target - current) < 0.001f) current = target;

        _tweens[id] = current;
        return current;
    }

    private static Vector4 Mix(Vector4 a, Vector4 b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    // ── Audio feedback ───────────────────────────────────────────────────────────────────────

    // ImGui exposes no public "nav focus changed" event, so focus movement is detected by watching
    // which item reports itself focused. Only one item can be, so tracking the last one seen is
    // enough — and it costs nothing on the frames where focus has not moved.
    private static uint _lastFocused;

    /// <summary>The item that currently holds nav focus.</summary>
    internal static uint CurrentFocusId => _lastFocused;

    // Crossing between panes is driven explicitly, not by ImGui's geometry.
    //
    // ImGui scores a directional move purely on position, so Left out of the content landed on
    // whichever rail entry happened to sit nearest vertically -- "Scan for games" jumped to
    // *Overview* -- and refused outright when no entry was close. Neither is what a rail means.
    //
    // Instead every enabled focusable item reports itself as it draws. That yields two things: the
    // topmost-leftmost candidate in a pane (where Right should land) and the ability to focus one
    // specific item by id (where Left should land: the entry for the screen you are on).
    private static uint _focusTargetId;
    private static int _focusTargetFrames;
    private const int FocusRequestLifetimeFrames = 45;

    private static bool _scanning;
    private static bool _haveBest;
    private static uint _bestId;
    private static Vector2 _bestPos;

    /// <summary>Start collecting focusable candidates. Pair with <see cref="EndFocusScan"/>.</summary>
    public static void BeginFocusScan()
    {
        _scanning = true;
        _haveBest = false;
    }

    /// <summary>The topmost (then leftmost) focusable item drawn during the scan; 0 if none.</summary>
    public static uint EndFocusScan()
    {
        _scanning = false;
        return _haveBest ? _bestId : 0u;
    }

    /// <summary>Ask a specific item to take focus as soon as it next draws.</summary>
    public static void RequestFocus(uint id)
    {
        if (id == 0) return;
        _focusTargetId = id;
        _focusTargetFrames = FocusRequestLifetimeFrames;
    }

    internal static bool FocusRequestPending => _focusTargetFrames > 0;

    /// <summary>
    /// Called by every focusable widget immediately before it submits its item, with the id that
    /// item will have.
    /// </summary>
    /// <param name="enabled">
    /// Disabled widgets pass false: focusing one leaves the cursor nowhere useful and ImGui falls
    /// back to highlighting the container, which is the "broken highlight" this work removed.
    /// </param>
    private static void ClaimFocus(uint id, bool enabled = true)
    {
        if (!enabled) return;

        if (_scanning)
        {
            var pos = ImGui.GetCursorScreenPos();
            // Topmost wins; ties on the same visual row go to the leftmost.
            if (!_haveBest || pos.Y < _bestPos.Y - 2f ||
                (MathF.Abs(pos.Y - _bestPos.Y) <= 2f && pos.X < _bestPos.X))
            {
                _haveBest = true;
                _bestId = id;
                _bestPos = pos;
            }
        }

        if (_focusTargetFrames > 0 && id == _focusTargetId)
        {
            _focusTargetFrames = 0;
            ImGui.SetKeyboardFocusHere();
        }
    }

    /// <summary>Age an unclaimed request so it cannot sit armed forever.</summary>
    public static void AgeFocusRequest()
    {
        if (_focusTargetFrames > 0) _focusTargetFrames--;
    }

    /// <summary>
    /// THE focus cursor. On a Deck this is the only pointer that exists, so it has to be
    /// unmistakable and identical everywhere.
    ///
    /// The first version leaned on a tinted fill alone, which for a rail entry worked out to about
    /// 7% alpha over the panel — technically present, invisible in practice, and reported from
    /// hardware as "a broken highlight you only notice once you realise it is there". A solid
    /// accent outline plus a soft outer glow is legible at arm's length and, unlike a fill, cannot
    /// be confused with a widget's own selected/active state.
    /// </summary>
    public static void FocusRing(ImDrawListPtr dl, Vector2 min, Vector2 max, float rounding,
        float strength = 1f)
    {
        if (strength <= 0.01f) return;

        var pulse = 0.75f + 0.25f * MathF.Sin((float)ImGui.GetTime() * 4f);
        var glow = Theme.Alpha(Theme.AccentGreen, 0.30f * strength * pulse);
        var edge = Theme.Alpha(Theme.AccentGreen, strength);

        dl.AddRect(min - new Vector2(4, 4), max + new Vector2(4, 4), U32(glow), rounding + 4f,
            ImDrawFlags.None, 3f);
        dl.AddRect(min - new Vector2(1, 1), max + new Vector2(1, 1), U32(edge), rounding + 1f,
            ImDrawFlags.None, 2f);
    }

    /// <summary>
    /// Play the navigate cue when focus arrives on this item, and an activation cue when it fires.
    /// Every interactive widget calls this immediately after its <c>InvisibleButton</c>.
    /// </summary>
    private static void Feedback(uint id, bool pressed, Sound.Cue activate = Sound.Cue.Activate)
    {
        if (ImGui.IsItemFocused() && id != _lastFocused)
        {
            _lastFocused = id;
            Sound.Play(Sound.Cue.Navigate);
        }
        if (pressed) Sound.Play(activate);
    }

    /// <summary>
    /// Colour to packed U32, honouring ImGui's global <c>style.Alpha</c>.
    ///
    /// This matters more here than in a normal ImGui app: <c>PushStyleVar(ImGuiStyleVar.Alpha, …)</c>
    /// is applied by ImGui's own widget code, but every widget in this file paints through
    /// ImDrawList with explicit colours, which ImGui never touches. Without folding Alpha in here,
    /// a fade would silently do nothing to the parts of the UI that are actually drawn by hand.
    /// </summary>
    internal static uint U32(Vector4 c) =>
        ImGui.ColorConvertFloat4ToU32(c with { W = c.W * ImGui.GetStyle().Alpha });

    // ── Text ─────────────────────────────────────────────────────────────────────────────────
    // ImGui.NET's Text* helpers treat their argument as a printf FORMAT string, so any dynamic text
    // containing '%' (the launch command's "%command%") corrupts the output. Everything routes
    // through TextUnformatted, which never formats.

    public static void Text(string s, Vector4? colour = null, ImFontPtr? font = null)
    {
        if (font.HasValue) Theme.PushFont(font.Value);
        if (colour.HasValue) ImGui.PushStyleColor(ImGuiCol.Text, colour.Value);
        ImGui.TextUnformatted(s);
        if (colour.HasValue) ImGui.PopStyleColor();
        if (font.HasValue) Theme.PopFont(font.Value);
    }

    /// <summary>
    /// Wrapped text. <paramref name="wrapPosX"/> is a window-local X to wrap at; 0 means the
    /// window's right edge. Pass an explicit value when something else has to sit to the right of
    /// the text, or the text claims the full width and pushes it off screen.
    /// </summary>
    public static void TextWrapped(string s, Vector4? colour = null, ImFontPtr? font = null,
        float wrapPosX = 0f)
    {
        ImGui.PushTextWrapPos(wrapPosX);
        Text(s, colour, font);
        ImGui.PopTextWrapPos();
    }

    /// <summary>
    /// Shorten <paramref name="s"/> to fit <paramref name="maxWidth"/>, adding an ellipsis.
    /// ImDrawList's AddText neither wraps nor clips, so every hand-painted widget drawing
    /// variable-length text must do this itself or the text runs off the edge of the window.
    ///
    /// <paramref name="middle"/> elides the centre rather than the tail, which is what save paths
    /// want: the leaf folder identifies the game, the long compatdata/pfx/drive_c middle does not.
    /// </summary>
    public static string Elide(string s, float maxWidth, bool middle = false)
    {
        if (string.IsNullOrEmpty(s) || maxWidth <= 0f) return "";
        if (ImGui.CalcTextSize(s).X <= maxWidth) return s;

        const string Ellipsis = "...";
        var ellipsisW = ImGui.CalcTextSize(Ellipsis).X;
        if (ellipsisW > maxWidth) return "";

        if (!middle)
        {
            var keep = FitCount(s, maxWidth - ellipsisW);
            return keep <= 0 ? Ellipsis : s[..keep] + Ellipsis;
        }

        // Split the budget: the tail is the informative half, so it gets the larger share.
        var budget = maxWidth - ellipsisW;
        var tailKeep = FitCountFromEnd(s, budget * 0.6f);
        var headBudget = budget - (tailKeep > 0 ? ImGui.CalcTextSize(s[^tailKeep..]).X : 0f);
        var headKeep = FitCount(s, headBudget);
        if (headKeep <= 0 && tailKeep <= 0) return Ellipsis;
        return s[..Math.Max(0, headKeep)] + Ellipsis + s[^Math.Max(0, tailKeep)..];
    }

    private static int FitCount(string s, float width)
    {
        int lo = 0, hi = s.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(s[..mid]).X <= width) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    private static int FitCountFromEnd(string s, float width)
    {
        int lo = 0, hi = s.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (ImGui.CalcTextSize(s[^mid..]).X <= width) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>A small uppercase tracked label, matching the console's "AGENT STATUS" treatment.</summary>
    public static void EyebrowLabel(string s)
    {
        Theme.PushFont(Theme.Caption);
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var col = U32(Theme.TextMuted);

        // ImGui has no letter-spacing, so tracking is done by drawing character by character.
        // Worth it: the wide-tracked caps label is a signature of the console's visual language.
        float x = 0f;
        const float tracking = 1.6f;
        foreach (var ch in s.ToUpperInvariant())
        {
            var str = ch.ToString();
            dl.AddText(pos + new Vector2(x, 0), col, str);
            x += ImGui.CalcTextSize(str).X + tracking;
        }

        ImGui.Dummy(new Vector2(x, ImGui.GetTextLineHeight()));
        Theme.PopFont(Theme.Caption);
    }

    public static void SectionHeader(string s)
    {
        ImGui.Spacing();
        EyebrowLabel(s);
        var dl = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var w = ImGui.GetContentRegionAvail().X;
        dl.AddLine(p + new Vector2(0, 2), p + new Vector2(w, 2), U32(Theme.BgRowSep), 1f);
        ImGui.Dummy(new Vector2(0, Theme.Space.Sm));
    }

    // ── Containers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A card: rounded fill plus hairline border, drawn behind a child region. Returns the child's
    /// content width. Pair with <see cref="EndCard"/>.
    /// </summary>
    public static void BeginCard(string id, Vector2 size, Vector4? fill = null, Vector4? border = null)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, fill ?? Theme.BgCard);
        ImGui.PushStyleColor(ImGuiCol.Border, border ?? Theme.Border);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Theme.Rounding.Card);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Theme.Space.Lg, Theme.Space.Md));
        ImGui.BeginChild(id, size, ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY,
            ImGuiWindowFlags.NavFlattened);
    }

    public static void EndCard()
    {
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    // ── Buttons ──────────────────────────────────────────────────────────────────────────────

    public enum ButtonKind { Primary, Secondary, Danger, Ghost }

    /// <summary>
    /// A pill button. Hover, focus and press are tweened, and the gamepad focus ring is drawn as a
    /// full outline rather than ImGui's hairline — on a Deck it is the only cursor there is.
    /// </summary>
    public static bool PillButton(string label, ButtonKind kind = ButtonKind.Secondary,
        Icons.Glyph? icon = null, float minWidth = 0f, bool enabled = true)
    {
        var padX = Theme.Space.Lg;
        var padY = Theme.Space.Sm + 2f;
        var iconSize = enabled || true ? ImGui.GetTextLineHeight() : 0f;

        Theme.PushFont(Theme.BodyStrong);
        var textSize = ImGui.CalcTextSize(label);
        Theme.PopFont(Theme.BodyStrong);

        var width = MathF.Max(minWidth,
            textSize.X + padX * 2 + (icon is null ? 0f : iconSize + Theme.Space.Sm));
        var height = textSize.Y + padY * 2;

        ClaimFocus(ImGui.GetID(label), enabled);
        if (!enabled) ImGui.BeginDisabled();
        var pressed = ImGui.InvisibleButton(label, new Vector2(width, height));
        if (!enabled) ImGui.EndDisabled();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var id = ImGui.GetItemID();
        var dl = ImGui.GetWindowDrawList();

        bool hovered = enabled && ImGui.IsItemHovered();
        bool active = enabled && ImGui.IsItemActive();
        bool focused = enabled && ImGui.IsItemFocused();

        if (enabled) Feedback(id, pressed);

        var lift = Tween(id, (hovered || focused ? 1f : 0f) + (active ? 0.6f : 0f));

        var (baseFill, baseText, borderColour) = kind switch
        {
            ButtonKind.Primary   => (Theme.AccentGreen, Theme.TextPrimary, Theme.AccentGreen),
            ButtonKind.Danger    => (Theme.Alpha(Theme.AccentAmber, 0.16f), Theme.AccentAmber, Theme.WarnBorder),
            ButtonKind.Ghost     => (Theme.Alpha(Theme.BgCard, 0f), Theme.TextMuted, Theme.Alpha(Theme.Border, 0f)),
            _                    => (Theme.BgTableHd, Theme.TextPrimary, Theme.Border),
        };

        var fill = Mix(baseFill, kind == ButtonKind.Primary
            ? Mix(baseFill, Theme.TextPrimary, 0.18f)
            : Theme.Alpha(Theme.AccentGreen, 0.22f), lift * 0.6f);
        var text = enabled ? baseText : Theme.TextDim;

        // A disabled Primary must lose its accent fill, not just dim its label — a full-green
        // button with grey text still reads as pressable, and on a gamepad the user finds out only
        // by pressing A and having nothing happen.
        if (!enabled)
        {
            fill = Theme.BgTableHd;
            borderColour = Theme.Border;
        }

        var rounding = height / 2f;
        dl.AddRectFilled(min, max, U32(fill), rounding);
        dl.AddRect(min, max, U32(Mix(borderColour, Theme.AccentGreen, lift * 0.8f)), rounding,
            ImDrawFlags.None, 1f);

        FocusRing(dl, min, max, rounding, focused ? 1f : 0f);

        var contentW = textSize.X + (icon is null ? 0f : iconSize + Theme.Space.Sm);
        var cursor = new Vector2(min.X + (width - contentW) / 2f, min.Y + padY);

        if (icon is not null)
        {
            Icons.DrawAt(dl, icon, new Vector2(cursor.X, min.Y + (height - iconSize) / 2f), iconSize, text);
            cursor.X += iconSize + Theme.Space.Sm;
        }

        Theme.PushFont(Theme.BodyStrong);
        dl.AddText(cursor, U32(text), label);
        Theme.PopFont(Theme.BodyStrong);

        return pressed && enabled;
    }

    // ── Display ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A stat tile: big tabular number over a muted caption. Mirrors the console's StatCard, which
    /// is the agent UI's headline element.
    /// </summary>
    public static void StatTile(string value, string label, Vector4 valueColour, float width, float height)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);

        dl.AddRectFilled(min, max, U32(Theme.BgCard), Theme.Rounding.Card);
        dl.AddRect(min, max, U32(Theme.Border), Theme.Rounding.Card, ImDrawFlags.None, 1f);

        // A thin accent rule along the top edge ties the tile to the value it carries.
        dl.AddLine(min + new Vector2(Theme.Rounding.Card, 1f),
                   new Vector2(max.X - Theme.Rounding.Card, min.Y + 1f),
                   U32(Theme.Alpha(valueColour, 0.75f)), 2f);

        Theme.PushFont(Theme.Display);
        var vs = ImGui.CalcTextSize(value);
        dl.AddText(new Vector2(min.X + (width - vs.X) / 2f, min.Y + height * 0.28f - vs.Y / 2f),
            U32(valueColour), value);
        Theme.PopFont(Theme.Display);

        Theme.PushFont(Theme.Caption);
        var ls = ImGui.CalcTextSize(label);
        dl.AddText(new Vector2(min.X + (width - ls.X) / 2f, max.Y - ls.Y - Theme.Space.Md),
            U32(Theme.TextMuted), label);
        Theme.PopFont(Theme.Caption);

        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>A small rounded chip — the server-URL pill and inline state markers.</summary>
    public static void Badge(string text, Vector4 colour, Icons.Glyph? icon = null, bool mono = false)
    {
        var font = mono ? Theme.Mono : Theme.Caption;
        Theme.PushFont(font);
        var ts = ImGui.CalcTextSize(text);
        Theme.PopFont(font);

        var iconSize = icon is null ? 0f : ts.Y;
        var padX = Theme.Space.Sm + 2f;
        var padY = Theme.Space.Xs + 1f;
        var width = ts.X + padX * 2 + (icon is null ? 0f : iconSize + Theme.Space.Xs + 2f);
        var height = ts.Y + padY * 2;

        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(width, height);

        dl.AddRectFilled(min, max, U32(Theme.Alpha(colour, 0.09f)), Theme.Rounding.Chip);
        dl.AddRect(min, max, U32(Theme.Alpha(colour, 0.30f)), Theme.Rounding.Chip, ImDrawFlags.None, 1f);

        var x = min.X + padX;
        if (icon is not null)
        {
            Icons.DrawAt(dl, icon, new Vector2(x, min.Y + padY), iconSize, colour);
            x += iconSize + Theme.Space.Xs + 2f;
        }

        Theme.PushFont(font);
        dl.AddText(new Vector2(x, min.Y + padY), U32(colour), text);
        Theme.PopFont(font);

        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>A pulsing status dot with a glow, as in the console's StatusHeader.</summary>
    public static void StatusDot(Vector4 colour, float diameter = 9f)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var centre = pos + new Vector2(diameter / 2f, ImGui.GetTextLineHeight() / 2f);

        var pulse = 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 2.2f);
        dl.AddCircleFilled(centre, diameter / 2f + 3f * pulse, U32(Theme.Alpha(colour, 0.18f)), 16);
        dl.AddCircleFilled(centre, diameter / 2f, U32(colour), 16);

        ImGui.Dummy(new Vector2(diameter, ImGui.GetTextLineHeight()));
    }

    /// <summary>
    /// A full-width notice. Returns true when its dismiss control was activated.
    /// </summary>
    public static bool Banner(string id, string title, string body, Vector4 colour,
        Icons.Glyph? icon = null, bool dismissible = false)
    {
        bool dismissed = false;
        ImGui.PushID(id);

        // Animate in. A conflict banner appears without warning while the user is looking at
        // something else, so it fades and settles downward rather than popping into place — the
        // movement is what draws the eye to it.
        var enter = Tween(ImGui.GetID("##enter"), 1f, 16f, initial: 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * enter);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (1f - enter) * 10f);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Alpha(colour, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Alpha(colour, 0.45f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Theme.Rounding.Card);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Theme.Space.Md, Theme.Space.Md));
        ImGui.BeginChild("banner", new Vector2(0, 0),
            ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY, ImGuiWindowFlags.NavFlattened);

        if (icon is not null)
        {
            Icons.Draw(icon, ImGui.GetTextLineHeight() + 2f, colour);
            ImGui.SameLine(0, Theme.Space.Sm + 2f);
        }

        // Reserve the dismiss button's width BEFORE the body wraps: text wrapping to the window
        // edge leaves nothing for the button, which then wraps out of the banner entirely.
        var reserve = dismissible ? ImGui.GetTextLineHeight() + Theme.Space.Lg : 0f;
        var wrapX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - reserve;

        ImGui.BeginGroup();
        Text(title, colour, Theme.BodyStrong);
        TextWrapped(body, Theme.TextPrimary, wrapPosX: wrapX);
        ImGui.EndGroup();

        if (dismissible)
        {
            ImGui.SameLine();
            var avail = ImGui.GetContentRegionAvail().X;
            if (avail > reserve) { ImGui.Dummy(new Vector2(avail - reserve, 0)); ImGui.SameLine(); }
            if (IconButton("dismiss", Icons.X, Theme.TextMuted)) dismissed = true;
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();   // enter alpha
        ImGui.PopID();
        return dismissed;
    }

    /// <summary>A bare icon-only button, for dismiss and row actions.</summary>
    public static bool IconButton(string id, Icons.Glyph glyph, Vector4 colour, float size = 0f)
    {
        if (size <= 0f) size = ImGui.GetTextLineHeight() + 6f;
        var box = size + Theme.Space.Sm;

        ClaimFocus(ImGui.GetID(id));
        var pressed = ImGui.InvisibleButton(id, new Vector2(box, box));
        var min = ImGui.GetItemRectMin();
        var dl = ImGui.GetWindowDrawList();

        bool hot = ImGui.IsItemHovered() || ImGui.IsItemFocused();
        var lift = Tween(ImGui.GetItemID(), hot ? 1f : 0f);
        Feedback(ImGui.GetItemID(), pressed, Sound.Cue.Back);

        if (lift > 0.01f)
            dl.AddRectFilled(min, ImGui.GetItemRectMax(),
                U32(Theme.Alpha(Theme.AccentGreen, 0.18f * lift)), Theme.Rounding.Button);
        FocusRing(dl, min, ImGui.GetItemRectMax(), Theme.Rounding.Button,
            ImGui.IsItemFocused() ? 1f : 0f);

        Icons.DrawAt(dl, glyph, min + new Vector2(Theme.Space.Xs, Theme.Space.Xs), size,
            Mix(colour, Theme.AccentGreen, lift));

        return pressed;
    }

    // ── Input ────────────────────────────────────────────────────────────────────────────────

    /// <summary>An iOS-style switch. Gamepad-activatable, and the knob slides rather than snapping.</summary>
    public static bool Toggle(string label, ref bool value)
    {
        var height = ImGui.GetTextLineHeight() + 6f;
        var width = height * 1.9f;

        ImGui.PushID(label);
        ClaimFocus(ImGui.GetID("##toggle"));
        var pressed = ImGui.InvisibleButton("##toggle", new Vector2(width, height));
        if (pressed) value = !value;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        bool focused = ImGui.IsItemFocused();

        Feedback(ImGui.GetItemID(), pressed, Sound.Cue.Toggle);

        var t = Tween(ImGui.GetItemID(), value ? 1f : 0f, 18f);
        var track = Mix(Theme.BgTableHd, Theme.AccentGreen, t);

        dl.AddRectFilled(min, max, U32(track), height / 2f);
        dl.AddRect(min, max, U32(Theme.Border), height / 2f, ImDrawFlags.None, 1f);
        FocusRing(dl, min, max, height / 2f, focused ? 1f : 0f);

        var r = height / 2f - 3f;
        var cx = min.X + 3f + r + t * (width - height);
        dl.AddCircleFilled(new Vector2(cx, min.Y + height / 2f), r, U32(Theme.TextPrimary), 20);

        ImGui.SameLine(0, Theme.Space.Md);
        ImGui.AlignTextToFramePadding();
        Text(label, Theme.TextPrimary);
        ImGui.PopID();

        return pressed;
    }

    /// <summary>
    /// A minus/value/plus stepper. This is how a number gets edited without a text field — Game Mode
    /// has no keyboard, and <c>UiApp</c> deliberately suppresses the on-screen one.
    /// </summary>
    public static bool Stepper(string label, ref int value, int min, int max, int step = 1)
    {
        bool changed = false;
        ImGui.PushID(label);

        if (PillButton("-", ButtonKind.Secondary, minWidth: 52f, enabled: value > min))
        {
            value = Math.Max(min, value - step);
            changed = true;
        }

        ImGui.SameLine(0, Theme.Space.Sm);

        var boxW = 88f;
        var boxH = ImGui.GetItemRectSize().Y;
        var dl = ImGui.GetWindowDrawList();
        var bmin = ImGui.GetCursorScreenPos();
        dl.AddRectFilled(bmin, bmin + new Vector2(boxW, boxH), U32(Theme.BgTableHd), Theme.Rounding.Button);
        dl.AddRect(bmin, bmin + new Vector2(boxW, boxH), U32(Theme.Border), Theme.Rounding.Button,
            ImDrawFlags.None, 1f);

        Theme.PushFont(Theme.BodyStrong);
        var text = value.ToString();
        var ts = ImGui.CalcTextSize(text);
        dl.AddText(bmin + new Vector2((boxW - ts.X) / 2f, (boxH - ts.Y) / 2f), U32(Theme.TextPrimary), text);
        Theme.PopFont(Theme.BodyStrong);
        ImGui.Dummy(new Vector2(boxW, boxH));

        ImGui.SameLine(0, Theme.Space.Sm);
        if (PillButton("+", ButtonKind.Secondary, minWidth: 52f, enabled: value < max))
        {
            value = Math.Min(max, value + step);
            changed = true;
        }

        ImGui.SameLine(0, Theme.Space.Md);
        ImGui.AlignTextToFramePadding();
        Text(label, Theme.TextMuted);
        ImGui.PopID();

        return changed;
    }

    /// <summary>
    /// A tappable list row: optional leading icon, title, subtitle, trailing text and chevron.
    /// The whole row is one focus target, which is what makes a long list navigable by D-pad.
    /// </summary>
    public static bool ListRow(string id, string title, string? subtitle = null,
        Icons.Glyph? icon = null, string? trailing = null, Vector4? trailingColour = null,
        bool chevron = true, bool selected = false, bool enabled = true)
    {
        var lineH = ImGui.GetTextLineHeight();
        var height = subtitle is null
            ? lineH + Theme.Space.Md * 2
            : lineH * 2 + Theme.Space.Xs + Theme.Space.Md * 2;
        var width = ImGui.GetContentRegionAvail().X;

        ImGui.PushID(id);
        ClaimFocus(ImGui.GetID("##row"), enabled);
        if (!enabled) ImGui.BeginDisabled();
        var pressed = ImGui.InvisibleButton("##row", new Vector2(width, height));
        if (!enabled) ImGui.EndDisabled();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();

        bool hot = enabled && (ImGui.IsItemHovered() || ImGui.IsItemFocused());
        var lift = Tween(ImGui.GetItemID(), hot ? 1f : 0f);
        if (enabled) Feedback(ImGui.GetItemID(), pressed);

        if (selected || lift > 0.01f)
        {
            var bg = selected ? Theme.NavActiveBg : Theme.Alpha(Theme.AccentGreen, 0.16f * lift);
            dl.AddRectFilled(min, max, U32(bg), Theme.Rounding.Button);
        }
        // A left accent bar marks selection, matching the console sidebar's active-row treatment.
        if (selected)
            dl.AddRectFilled(min, new Vector2(min.X + 3f, max.Y), U32(Theme.AccentGreen), 2f);

        FocusRing(dl, min, max, Theme.Rounding.Button, lift);

        dl.AddLine(new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), U32(Theme.BgRowSep), 1f);

        var x = min.X + Theme.Space.Md;
        if (icon is not null)
        {
            Icons.DrawAt(dl, icon, new Vector2(x, min.Y + (height - lineH) / 2f), lineH,
                enabled ? Mix(Theme.TextMuted, Theme.AccentGreen, lift) : Theme.TextDim);
            x += lineH + Theme.Space.Md;
        }

        // The right-hand furniture is measured first: the text budget is whatever it leaves, and
        // AddText does not clip, so an un-elided save path simply runs off the window.
        var rightEdge = max.X - Theme.Space.Md;
        if (chevron)
        {
            Icons.DrawAt(dl, Icons.ChevronRight,
                new Vector2(rightEdge - lineH, min.Y + (height - lineH) / 2f), lineH,
                Mix(Theme.TextDim, Theme.AccentGreen, lift));
            rightEdge -= lineH + Theme.Space.Sm;
        }

        if (trailing is not null)
        {
            Theme.PushFont(Theme.Caption);
            var ts = ImGui.CalcTextSize(trailing);
            dl.AddText(new Vector2(rightEdge - ts.X, min.Y + (height - ts.Y) / 2f),
                U32(trailingColour ?? Theme.TextMuted), trailing);
            rightEdge -= ts.X + Theme.Space.Md;
            Theme.PopFont(Theme.Caption);
        }

        var budget = MathF.Max(0f, rightEdge - x);
        var textY = subtitle is null ? min.Y + (height - lineH) / 2f : min.Y + Theme.Space.Md;

        Theme.PushFont(Theme.BodyStrong);
        dl.AddText(new Vector2(x, textY), U32(enabled ? Theme.TextPrimary : Theme.TextDim),
            Elide(title, budget));
        Theme.PopFont(Theme.BodyStrong);

        if (subtitle is not null)
        {
            Theme.PushFont(Theme.Caption);
            dl.AddText(new Vector2(x, textY + lineH + Theme.Space.Xs), U32(Theme.TextMuted),
                Elide(subtitle, budget, middle: true));
            Theme.PopFont(Theme.Caption);
        }

        ImGui.PopID();
        return pressed && enabled;
    }

    /// <summary>
    /// A checkbox drawn to match the rest of the set, returning whether it changed. Used by the
    /// add-game candidate list, where the tick is the primary action rather than a row tap.
    /// </summary>
    public static bool CheckRow(string id, ref bool ticked, bool enabled = true)
    {
        var box = ImGui.GetTextLineHeight() + 4f;
        ImGui.PushID(id);
        ClaimFocus(ImGui.GetID("##check"), enabled);
        if (!enabled) ImGui.BeginDisabled();
        var pressed = ImGui.InvisibleButton("##check", new Vector2(box, box));
        if (!enabled) ImGui.EndDisabled();
        if (pressed) ticked = !ticked;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        bool hot = enabled && (ImGui.IsItemHovered() || ImGui.IsItemFocused());
        if (enabled) Feedback(ImGui.GetItemID(), pressed, Sound.Cue.Toggle);

        var t = Tween(ImGui.GetItemID(), ticked ? 1f : 0f, 20f);
        var fill = Mix(Theme.BgTableHd, Theme.AccentGreen, t);

        dl.AddRectFilled(min, max, U32(enabled ? fill : Theme.BgRowSep), Theme.Rounding.Button);
        dl.AddRect(min, max, U32(Theme.Border), Theme.Rounding.Button, ImDrawFlags.None, 1f);
        FocusRing(dl, min, max, Theme.Rounding.Button, hot ? 1f : 0f);

        if (t > 0.05f)
            Icons.DrawAt(dl, Icons.Check, min + new Vector2(2, 2), box - 4f,
                Theme.Alpha(Theme.TextPrimary, t));

        ImGui.PopID();
        return pressed;
    }

    /// <summary>
    /// A left-rail navigation row: icon, label, and the console sidebar's active treatment — tinted
    /// fill plus a 3 px accent bar down the leading edge (see <c>agent-ui/.../Sidebar.tsx</c>).
    /// </summary>
    /// <summary>Id of the most recently drawn rail entry, so the caller can target it.</summary>
    internal static uint LastRailItemId { get; private set; }

    public static bool RailItem(string label, Icons.Glyph icon, bool active)
    {
        var lineH = ImGui.GetTextLineHeight();
        var height = lineH + Theme.Space.Md * 2;
        var width = ImGui.GetContentRegionAvail().X;

        ImGui.PushID(label);
        LastRailItemId = ImGui.GetID("##rail");
        ClaimFocus(LastRailItemId);
        var pressed = ImGui.InvisibleButton("##rail", new Vector2(width, height));

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();

        bool hot = ImGui.IsItemHovered() || ImGui.IsItemFocused();
        var lift = Tween(ImGui.GetItemID(), hot ? 1f : 0f);
        var on = Tween(ImGui.GetItemID() ^ 0x5A5Au, active ? 1f : 0f);
        Feedback(ImGui.GetItemID(), pressed);

        // Active (this is the current screen) and focused (the cursor is here) are DIFFERENT states
        // and must look different: you can stand on "Settings" while still viewing "Overview".
        var bg = Mix(Theme.Alpha(Theme.AccentGreen, 0f), Theme.NavActiveBg, MathF.Max(on, lift));
        dl.AddRectFilled(min, max, U32(bg), Theme.Rounding.Button);

        if (on > 0.01f)
            dl.AddRectFilled(min, new Vector2(min.X + 3f, max.Y),
                U32(Theme.Alpha(Theme.AccentGreen, on)), 2f);

        FocusRing(dl, min, max, Theme.Rounding.Button, lift);

        var tint = Mix(Theme.TextMuted, Theme.AccentGreen, MathF.Max(on, lift));
        var textCol = Mix(Theme.TextPrimary, Theme.AccentGreen, on);

        var x = min.X + Theme.Space.Md;
        Icons.DrawAt(dl, icon, new Vector2(x, min.Y + (height - lineH) / 2f), lineH, tint);

        var font = active ? Theme.BodyStrong : Theme.Body;
        Theme.PushFont(font);
        dl.AddText(new Vector2(x + lineH + Theme.Space.Md, min.Y + (height - lineH) / 2f),
            U32(textCol), label);
        Theme.PopFont(font);

        ImGui.PopID();
        return pressed;
    }

    /// <summary>
    /// A face-button hint — a lettered circle plus its action, matching how A/B/X/Y are moulded on
    /// the hardware. A Deck user has no other affordance telling them what the buttons do here.
    /// </summary>
    public static void GamepadHint(string button, string action)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var r = ImGui.GetTextLineHeight() * 0.55f;
        var centre = pos + new Vector2(r, ImGui.GetTextLineHeight() / 2f);

        dl.AddCircleFilled(centre, r, U32(Theme.BgTableHd), 20);
        dl.AddCircle(centre, r, U32(Theme.Border), 20, 1f);

        Theme.PushFont(Theme.Caption);
        var bs = ImGui.CalcTextSize(button);
        dl.AddText(centre - bs / 2f, U32(Theme.TextPrimary), button);
        Theme.PopFont(Theme.Caption);

        HintLabel(r * 2, action);
    }

    /// <summary>
    /// A hint for a control that is a shape rather than a letter — the D-pad above all. Drawing it
    /// as a circled "D" was wrong: there is no D button on a Deck.
    /// </summary>
    public static void GamepadHintIcon(Icons.Glyph glyph, string action)
    {
        var dl = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var lineH = ImGui.GetTextLineHeight();

        // Sized to the face-button circle's diameter so the row reads as one set of controls.
        var size = lineH * 1.1f;
        Icons.DrawAt(dl, glyph, pos + new Vector2(0, (lineH - size) / 2f), size, Theme.TextPrimary);

        HintLabel(size, action);
    }

    private static void HintLabel(float glyphWidth, string action)
    {
        ImGui.Dummy(new Vector2(glyphWidth, ImGui.GetTextLineHeight()));
        ImGui.SameLine(0, Theme.Space.Sm);
        ImGui.AlignTextToFramePadding();
        Text(action, Theme.TextMuted, Theme.Caption);
    }

    // ── Layout ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two side-by-side columns with a gutter. ImGui has no layout engine, so this exists once here
    /// rather than as repeated cursor arithmetic on every screen.
    /// </summary>
    public static void TwoColumn(string id, float leftFraction, Action left, Action right,
        float height = 0f)
    {
        var avail = ImGui.GetContentRegionAvail();
        var gutter = Theme.Space.Lg;
        var leftW = (avail.X - gutter) * leftFraction;
        var h = height > 0f ? height : avail.Y;

        ImGui.PushID(id);
        ImGui.BeginChild("l", new Vector2(leftW, h), ImGuiChildFlags.None,
            ImGuiWindowFlags.NavFlattened);
        left();
        ImGui.EndChild();

        ImGui.SameLine(0, gutter);

        ImGui.BeginChild("r", new Vector2(0, h), ImGuiChildFlags.None,
            ImGuiWindowFlags.NavFlattened);
        right();
        ImGui.EndChild();
        ImGui.PopID();
    }

    /// <summary>Vertical space, in theme units rather than magic numbers at the call site.</summary>
    public static void Gap(float amount) => ImGui.Dummy(new Vector2(0, amount));
}
