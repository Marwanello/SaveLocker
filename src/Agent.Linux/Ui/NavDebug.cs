using System.Numerics;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// `savelocker ui --nav-debug` — a live read-out of where the nav cursor actually is.
///
/// Every diagnosis of this UI's navigation so far has been inference from source, and the previous
/// two attempts at it were spent trying things and measuring the result on a Deck. This shows the
/// three pieces of state that matter — the focused item, the child window that owns it, and whether
/// a focus request is armed — so a symptom can be read rather than deduced.
///
/// It draws on the FOREGROUND draw list and submits no items and no window of its own. That is not a
/// convenience: an overlay that participated in layout or nav would change the thing being measured.
/// </summary>
static class NavDebug
{
    public static bool Enabled;

    /// <summary>Nested child windows the cursor is currently inside, outermost first.</summary>
    private static readonly List<string> _scopes = new();

    private static uint _focusedId;
    private static string _focusedLabel = "";
    private static string _focusedScope = "";
    private static bool _seenThisFrame;

    private static readonly List<string> _trail = new();
    private const int TrailLength = 7;

    private static string _keys = "";

    public static void BeginFrame()
    {
        if (!Enabled) return;
        _seenThisFrame = false;
        _container = "";
        // Pairs are balanced by the callers, but an early-out inside a screen would leak a scope
        // into the next frame and quietly mislabel everything after it.
        _scopes.Clear();
    }

    public static void PushScope(string name)
    {
        if (Enabled) _scopes.Add(name);
    }

    public static void PopScope()
    {
        if (Enabled && _scopes.Count > 0) _scopes.RemoveAt(_scopes.Count - 1);
    }

    private static string _container = "";

    /// <summary>
    /// Call immediately after an <c>EndChild</c>. A child window that ImGui treats as a nav
    /// *container* submits itself as an item, so <c>IsItemFocused</c> right after EndChild is a
    /// public-API answer to "is the cursor parked on the pane itself rather than on a control" —
    /// the state that draws a highlight around the whole pane with nothing selected inside it.
    /// </summary>
    public static void NoteContainer(string name)
    {
        if (!Enabled) return;
        if (ImGui.IsItemFocused()) _container = name;
    }

    /// <summary>The nav keys pulsed into ImGui this frame, as UiApp fed them.</summary>
    public static void NoteKeys(IEnumerable<ImGuiKey> keys)
    {
        if (!Enabled) return;
        var names = keys.Select(k => k.ToString().Replace("Gamepad", "")).ToArray();
        if (names.Length > 0) _keys = string.Join(" ", names);
    }

    /// <summary>
    /// Called by <see cref="Widgets"/> for whichever item reports focus. First one wins: ImGui only
    /// ever focuses one item, so a second report in the same frame would itself be a finding.
    /// </summary>
    public static void NoteFocused(uint id, string label)
    {
        if (!Enabled || _seenThisFrame) return;
        _seenThisFrame = true;

        var scope = _scopes.Count == 0 ? "<root>" : string.Join(" > ", _scopes);
        bool changed = id != _focusedId || scope != _focusedScope;

        _focusedId = id;
        _focusedLabel = label;
        _focusedScope = scope;

        if (changed)
        {
            _trail.Add($"{(_keys.Length > 0 ? _keys : "-"),-12} {Describe(label),-22} {scope}");
            if (_trail.Count > TrailLength) _trail.RemoveAt(0);
        }
    }

    private static string Describe(string label) =>
        string.IsNullOrEmpty(label) ? "<unlabelled>" : label;

    /// <summary>
    /// Everything the overlay needs that lives on <see cref="UiApp"/> rather than in here.
    /// </summary>
    public readonly record struct Frame(
        string Screen, string Zone, uint ActiveRailId, uint BestContentId, int PendingCrossFrames);

    /// <summary>Whether the mono face baked, guarded the same way <see cref="Theme.PushFont"/> is.</summary>
    private static unsafe bool FontReady(ImFontPtr font) => font.NativePtr is not null;

    public static void Draw(Frame frame)
    {
        if (!Enabled) return;

        // ImGui's own answer, independent of our bookkeeping. When this is true and _seenThisFrame
        // is false, the cursor is on something we do not instrument; when BOTH are false the cursor
        // is on no item at all — parked on a child window as a nav container. That distinction is
        // the whole reason this overlay exists.
        bool imguiHasItem = ImGui.IsAnyItemFocused();

        var lines = new List<(string Label, string Value, Vector4 Colour)>
        {
            ("key",     _keys.Length > 0 ? _keys : "-",                       Theme.TextPrimary),
            ("screen",  frame.Screen,                                          Theme.TextMuted),
            ("zone",    frame.Zone,                                            Theme.AccentGreen),
            ("focus",   _seenThisFrame
                            ? $"{Describe(_focusedLabel)}  0x{_focusedId:X8}"
                            : imguiHasItem ? "<uninstrumented item>" : "<NO ITEM>",
                        _seenThisFrame ? Theme.TextPrimary : Theme.AccentAmber),
            ("window",  _seenThisFrame ? _focusedScope : "-",                  Theme.TextMuted),
            ("container", _container.Length > 0 ? _container + "  <- CURSOR ON PANE" : "-",
                        _container.Length > 0 ? Theme.AccentAmber : Theme.TextDim),
        };

        if (Widgets.FocusRequestFrames > 0)
        {
            var landed = _seenThisFrame && _focusedId == Widgets.FocusTargetId;
            lines.Add(("request",
                $"0x{Widgets.FocusTargetId:X8}  {Widgets.FocusRequestFrames} frames left" +
                (landed ? "  LANDED" : ""),
                landed ? Theme.AccentGreen : Theme.AccentAmber));
        }
        else
        {
            lines.Add(("request", "none", Theme.TextDim));
        }

        // The rail's NoNav gate is suspended while a request is armed, which is what lets a Down
        // press cross panes. Name that state outright rather than leaving it to be inferred from
        // the frame counter.
        lines.Add(("gate", Widgets.FocusRequestFrames > 0
            ? "BOTH PANES NAVIGABLE (request armed)"
            : "one pane navigable",
            Widgets.FocusRequestFrames > 0 ? Theme.AccentAmber : Theme.TextDim));

        lines.Add(("nav api", ImGuiInternal.Available ? "cimgui internal" : "fallback",
            ImGuiInternal.Available ? Theme.AccentGreen : Theme.AccentAmber));
        lines.Add(("rail id", $"0x{frame.ActiveRailId:X8}", Theme.TextDim));
        lines.Add(("best content", $"0x{frame.BestContentId:X8}", Theme.TextDim));
        if (frame.PendingCrossFrames > 0)
            lines.Add(("pending cross", $"{frame.PendingCrossFrames} frames", Theme.AccentAmber));

        var dl = ImGui.GetForegroundDrawList();
        var font = Theme.Mono;
        if (!FontReady(font)) return;
        const float fontSize = 13f;
        const float pad = 10f;
        const float lineH = 17f;
        const float labelW = 96f;
        const float width = 460f;

        var height = pad * 2 + lineH * (lines.Count + 2 + _trail.Count) + lineH;
        var origin = new Vector2(ImGui.GetIO().DisplaySize.X - width - 12f, 12f);

        // Deliberately NOT routed through Widgets.U32: that folds in the global Alpha so widgets
        // participate in the screen cross-fade, and an instrument that fades out mid-transition is
        // useless exactly when a transition is what is being watched.
        uint Col(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

        dl.AddRectFilled(origin, origin + new Vector2(width, height),
            Col(Theme.Alpha(Theme.BgGlobal, 0.92f)), Theme.Rounding.Card);
        dl.AddRect(origin, origin + new Vector2(width, height),
            Col(Theme.AccentAmber), Theme.Rounding.Card, ImDrawFlags.None, 1f);

        var y = origin.Y + pad;
        void Row(string label, string value, Vector4 colour)
        {
            dl.AddText(font, fontSize, new Vector2(origin.X + pad, y), Col(Theme.TextDim), label);
            dl.AddText(font, fontSize, new Vector2(origin.X + pad + labelW, y), Col(colour), value);
            y += lineH;
        }

        Row("NAV DEBUG", "--nav-debug", Theme.AccentAmber);
        y += lineH * 0.4f;
        foreach (var (label, value, colour) in lines) Row(label, value, colour);

        y += lineH * 0.6f;
        Row("trail", "key          item                   window", Theme.TextDim);
        foreach (var entry in _trail) Row("", entry, Theme.TextMuted);
    }
}
