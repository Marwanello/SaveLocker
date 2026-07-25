using System.Runtime.InteropServices;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// The handful of <c>imgui_internal</c> entry points this UI needs, bound directly.
///
/// WHY THIS EXISTS. Moving the nav cursor between the rail and the content pane is not expressible
/// through ImGui.NET's public surface. <c>SetKeyboardFocusHere</c> is implemented as a *tabbing*
/// request and only acts within the active focus scope (ocornut/imgui#7226), so it cannot take the
/// cursor from a widget in a different child — which is precisely the hand-off this shell is built
/// on. Eight approaches were tried and measured against that wall (2026-07-25) before anyone checked
/// whether the primitive was reachable at all.
///
/// It is. ImGui.NET's managed assembly binds only <c>igSetKeyboardFocusHere</c> /
/// <c>igSetItemDefaultFocus</c> / <c>igSetWindowFocus</c>, but the <c>libcimgui.so</c> it ships
/// **exports the whole internal API**, in both 1.90.8.1 and 1.91.6.1. The gap was in the binding,
/// never in ImGui. So this is a binding, not a patch: same library, same version, no upgrade — the
/// ImGui.NET upgrade is separately known to break the working Right cross and must not be retried.
///
/// <c>igSetFocusID</c> is the one that matters. Called immediately after the target item is
/// submitted, it sets <c>NavId</c> and <c>NavWindow</c> and derives the nav rect from that item —
/// it is what ImGui itself uses internally to place the cursor, with none of the scope restrictions.
///
/// <c>igSetNavID</c> is deliberately NOT bound: it takes <c>ImRect</c> by value, which is ABI surface
/// with no upside while <c>igSetFocusID</c> does the job.
/// </summary>
static class ImGuiInternal
{
    // Must match the name ImGui.NET itself binds, so the already-loaded module is reused rather than
    // a second copy mapped into the process.
    private const string Lib = "cimgui";

    /// <summary>Sets <c>NavId</c>/<c>NavWindow</c> to the last-submitted item. Call right after it.</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igSetFocusID(uint id, IntPtr window);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr igGetCurrentWindow();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr igFindWindowByName(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void igFocusWindow(IntPtr window, int flags);

    private static readonly string[] RequiredExports =
    {
        "igSetFocusID", "igGetCurrentWindow", "igFindWindowByName", "igFocusWindow",
    };

    private static bool? _available;
    private static string _status = "not probed";

    /// <summary>
    /// Whether every entry point resolved. Callers MUST branch on this and keep their existing
    /// behaviour for the false case: a Deck has to degrade to the old half-working navigation, never
    /// die on a missing symbol. A future ImGui.NET could ship a cimgui built without the internal
    /// API and this would be the only warning.
    /// </summary>
    public static bool Available => _available ??= Probe();

    /// <summary>One line describing which path is live, for `--nav-debug` and `doctor`.</summary>
    public static string Status
    {
        get { _ = Available; return _status; }
    }

    /// <summary>
    /// Resolved by symbol lookup rather than by calling something and catching: the guard has to run
    /// before any of these are invoked, and several are only meaningful inside a live frame.
    /// </summary>
    private static bool Probe()
    {
        try
        {
            // Resolve through ImGui.NET's own assembly so the native asset is found the same way its
            // DllImports find it — a RID-agnostic build maps runtimes/ differently from a
            // self-contained publish, and this must agree with ImGui.NET in both shapes.
            if (!NativeLibrary.TryLoad(Lib, typeof(ImGui).Assembly, null, out var handle))
            {
                _status = $"{Lib} not loadable — using SetKeyboardFocusHere fallback";
                return false;
            }

            var missing = RequiredExports
                .Where(name => !NativeLibrary.TryGetExport(handle, name, out _))
                .ToArray();

            if (missing.Length > 0)
            {
                _status = $"{Lib} lacks {string.Join(", ", missing)} — using SetKeyboardFocusHere fallback";
                return false;
            }

            _status = $"{Lib} internal nav API available ({RequiredExports.Length} exports)";
            return true;
        }
        catch (Exception ex)
        {
            _status = $"{Lib} probe failed ({ex.GetType().Name}: {ex.Message}) — using fallback";
            return false;
        }
    }

    /// <summary>
    /// Place the nav cursor on the item that was just submitted, in the window currently being
    /// drawn. Returns false if the internal API is unavailable, so the caller can fall back.
    /// </summary>
    public static bool FocusLastItem(uint id)
    {
        if (!Available) return false;

        var window = igGetCurrentWindow();
        if (window == IntPtr.Zero) return false;

        igSetFocusID(id, window);
        return true;
    }

    /// <summary>
    /// Make a named window the focused one. Only needed when the cursor has to move to a window that
    /// is not the one being drawn; <see cref="FocusLastItem"/> is the normal path.
    /// </summary>
    public static bool FocusWindow(string name)
    {
        if (!Available) return false;

        var window = igFindWindowByName(name);
        if (window == IntPtr.Zero) return false;

        igFocusWindow(window, 0);   // ImGuiFocusRequestFlags_None
        return true;
    }
}
