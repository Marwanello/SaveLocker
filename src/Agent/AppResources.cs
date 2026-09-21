using System.Drawing;
using System.Windows.Forms;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>App-wide UI resources: the tray/window icon and a robust clipboard copy.</summary>
internal static class AppResources
{
    /// <summary>The agent icon, loaded once from the embedded SaveLocker.ico.</summary>
    public static Icon Icon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        try
        {
            var asm = typeof(AppResources).Assembly;
            using var stream = asm.GetManifestResourceStream("SaveLocker.Agent.Assets.SaveLocker.ico");
            if (stream is null) return SystemIcons.Application;
            return new Icon(stream);
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    /// <summary>
    /// The agent icon in the given look — the chosen mark, in the chosen accent — as a NEW icon the
    /// caller owns and disposes. <paramref name="large"/> asks for the shell's large-icon size (the
    /// window's Alt-Tab entry) rather than the small one (the tray, the title bar).
    /// <para>
    /// Falls back to a copy of the packaged icon: an icon is never worth failing to show the tray for.
    /// </para>
    /// </summary>
    public static Icon Render(AppearanceDto look, bool large = false)
    {
        try
        {
            var size = large ? SystemInformation.IconSize : SystemInformation.SmallIconSize;
            return MarkIcon.Render(look, size.Width);
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("AppResources.Render", ex);
            return (Icon)Icon.Clone();
        }
    }

    /// <summary>Copy text to the clipboard, tolerating transient OLE failures.</summary>
    public static bool TryCopy(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch
        {
            try { Clipboard.SetDataObject(text, copy: true, retryTimes: 5, retryDelay: 150); return true; }
            catch { return false; }
        }
    }
}
