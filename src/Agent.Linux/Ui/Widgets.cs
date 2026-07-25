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
    public static float Tween(uint id, float target, float speed = 14f)
    {
        var dt = ImGui.GetIO().DeltaTime;
        if (!_tweens.TryGetValue(id, out var current)) current = target;

        // 1 - e^(-speed*dt) is the exponential-decay form; a naive lerp(current, target, speed*dt)
        // overshoots and oscillates once dt gets large (a stalled frame, a window drag).
        current += (target - current) * (1f - MathF.Exp(-speed * dt));
        if (MathF.Abs(target - current) < 0.001f) current = target;

        _tweens[id] = current;
        return current;
    }

    private static Vector4 Mix(Vector4 a, Vector4 b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

    private static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

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

    public static void TextWrapped(string s, Vector4? colour = null, ImFontPtr? font = null)
    {
        ImGui.PushTextWrapPos(0f);
        Text(s, colour, font);
        ImGui.PopTextWrapPos();
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
        ImGui.BeginChild(id, size, ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY);
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

        // The focus ring: a second, offset outline so it reads even against a filled button.
        if (focused)
        {
            var glow = 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 3.5f);
            dl.AddRect(min - new Vector2(3, 3), max + new Vector2(3, 3),
                U32(Theme.Alpha(Theme.AccentGreen, 0.35f + 0.45f * glow)),
                rounding + 3f, ImDrawFlags.None, 2f);
        }

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

        ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Alpha(colour, 0.12f));
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Alpha(colour, 0.45f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Theme.Rounding.Card);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Theme.Space.Md, Theme.Space.Md));
        ImGui.BeginChild("banner", new Vector2(0, 0), ImGuiChildFlags.Border | ImGuiChildFlags.AutoResizeY);

        if (icon is not null)
        {
            Icons.Draw(icon, ImGui.GetTextLineHeight() + 2f, colour);
            ImGui.SameLine(0, Theme.Space.Sm + 2f);
        }

        ImGui.BeginGroup();
        Text(title, colour, Theme.BodyStrong);
        TextWrapped(body, Theme.TextPrimary);
        ImGui.EndGroup();

        if (dismissible)
        {
            ImGui.SameLine();
            var avail = ImGui.GetContentRegionAvail().X;
            if (avail > 30f) ImGui.Dummy(new Vector2(avail - 30f, 0));
            ImGui.SameLine();
            if (IconButton("dismiss", Icons.X, Theme.TextMuted)) dismissed = true;
        }

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
        ImGui.PopID();
        return dismissed;
    }

    /// <summary>A bare icon-only button, for dismiss and row actions.</summary>
    public static bool IconButton(string id, Icons.Glyph glyph, Vector4 colour, float size = 0f)
    {
        if (size <= 0f) size = ImGui.GetTextLineHeight() + 6f;
        var box = size + Theme.Space.Sm;

        var pressed = ImGui.InvisibleButton(id, new Vector2(box, box));
        var min = ImGui.GetItemRectMin();
        var dl = ImGui.GetWindowDrawList();

        bool hot = ImGui.IsItemHovered() || ImGui.IsItemFocused();
        var lift = Tween(ImGui.GetItemID(), hot ? 1f : 0f);

        if (lift > 0.01f)
            dl.AddRectFilled(min, ImGui.GetItemRectMax(),
                U32(Theme.Alpha(Theme.AccentGreen, 0.18f * lift)), Theme.Rounding.Button);

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
        var pressed = ImGui.InvisibleButton("##toggle", new Vector2(width, height));
        if (pressed) value = !value;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        bool focused = ImGui.IsItemFocused();

        var t = Tween(ImGui.GetItemID(), value ? 1f : 0f, 18f);
        var track = Mix(Theme.BgTableHd, Theme.AccentGreen, t);

        dl.AddRectFilled(min, max, U32(track), height / 2f);
        dl.AddRect(min, max, U32(focused ? Theme.AccentGreen : Theme.Border), height / 2f,
            ImDrawFlags.None, focused ? 2f : 1f);

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
        if (!enabled) ImGui.BeginDisabled();
        var pressed = ImGui.InvisibleButton("##row", new Vector2(width, height));
        if (!enabled) ImGui.EndDisabled();

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();

        bool hot = enabled && (ImGui.IsItemHovered() || ImGui.IsItemFocused());
        var lift = Tween(ImGui.GetItemID(), hot ? 1f : 0f);

        if (selected || lift > 0.01f)
        {
            var bg = selected ? Theme.NavActiveBg : Theme.Alpha(Theme.AccentGreen, 0.10f * lift);
            dl.AddRectFilled(min, max, U32(bg), Theme.Rounding.Button);
        }
        // A left accent bar on focus, matching the console sidebar's active-row treatment.
        if (lift > 0.01f || selected)
            dl.AddRectFilled(min, new Vector2(min.X + 3f, max.Y),
                U32(Theme.Alpha(Theme.AccentGreen, selected ? 1f : lift)), 2f);

        dl.AddLine(new Vector2(min.X, max.Y), new Vector2(max.X, max.Y), U32(Theme.BgRowSep), 1f);

        var x = min.X + Theme.Space.Md;
        if (icon is not null)
        {
            Icons.DrawAt(dl, icon, new Vector2(x, min.Y + (height - lineH) / 2f), lineH,
                enabled ? Mix(Theme.TextMuted, Theme.AccentGreen, lift) : Theme.TextDim);
            x += lineH + Theme.Space.Md;
        }

        var textY = subtitle is null ? min.Y + (height - lineH) / 2f : min.Y + Theme.Space.Md;
        Theme.PushFont(Theme.BodyStrong);
        dl.AddText(new Vector2(x, textY), U32(enabled ? Theme.TextPrimary : Theme.TextDim), title);
        Theme.PopFont(Theme.BodyStrong);

        if (subtitle is not null)
        {
            Theme.PushFont(Theme.Caption);
            dl.AddText(new Vector2(x, textY + lineH + Theme.Space.Xs), U32(Theme.TextMuted), subtitle);
            Theme.PopFont(Theme.Caption);
        }

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
        if (!enabled) ImGui.BeginDisabled();
        var pressed = ImGui.InvisibleButton("##check", new Vector2(box, box));
        if (!enabled) ImGui.EndDisabled();
        if (pressed) ticked = !ticked;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        bool hot = enabled && (ImGui.IsItemHovered() || ImGui.IsItemFocused());

        var t = Tween(ImGui.GetItemID(), ticked ? 1f : 0f, 20f);
        var fill = Mix(Theme.BgTableHd, Theme.AccentGreen, t);

        dl.AddRectFilled(min, max, U32(enabled ? fill : Theme.BgRowSep), Theme.Rounding.Button);
        dl.AddRect(min, max, U32(hot ? Theme.AccentGreen : Theme.Border), Theme.Rounding.Button,
            ImDrawFlags.None, hot ? 2f : 1f);

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
    public static bool RailItem(string label, Icons.Glyph icon, bool active)
    {
        var lineH = ImGui.GetTextLineHeight();
        var height = lineH + Theme.Space.Md * 2;
        var width = ImGui.GetContentRegionAvail().X;

        ImGui.PushID(label);
        var pressed = ImGui.InvisibleButton("##rail", new Vector2(width, height));

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();

        bool hot = ImGui.IsItemHovered() || ImGui.IsItemFocused();
        var lift = Tween(ImGui.GetItemID(), hot ? 1f : 0f);
        var on = Tween(ImGui.GetItemID() ^ 0x5A5Au, active ? 1f : 0f);

        var bg = Mix(Theme.Alpha(Theme.AccentGreen, 0f), Theme.NavActiveBg, MathF.Max(on, lift * 0.55f));
        dl.AddRectFilled(min, max, U32(bg), Theme.Rounding.Button);

        if (on > 0.01f || lift > 0.01f)
            dl.AddRectFilled(min, new Vector2(min.X + 3f, max.Y),
                U32(Theme.Alpha(Theme.AccentGreen, MathF.Max(on, lift * 0.6f))), 2f);

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
    /// A gamepad button hint — the filled glyph plus its action, as the hint bar renders them.
    /// A Deck user has no other affordance telling them what A and B do on this screen.
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

        ImGui.Dummy(new Vector2(r * 2, ImGui.GetTextLineHeight()));
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
        ImGui.BeginChild("l", new Vector2(leftW, h), ImGuiChildFlags.None);
        left();
        ImGui.EndChild();

        ImGui.SameLine(0, gutter);

        ImGui.BeginChild("r", new Vector2(0, h), ImGuiChildFlags.None);
        right();
        ImGui.EndChild();
        ImGui.PopID();
    }

    /// <summary>Vertical space, in theme units rather than magic numbers at the call site.</summary>
    public static void Gap(float amount) => ImGui.Dummy(new Vector2(0, amount));
}
