using System.Windows.Forms;

namespace SaveLocker.Agent;

/// <summary>The native "Select save folder" dialog, injected into the agent API server.</summary>
internal static class FolderPicker
{
    /// <summary>
    /// Open a Windows folder-picker and return the chosen path.
    ///
    /// <para>
    /// It runs on the tray's own UI thread, not a private STA thread of its own. The old version
    /// read <see cref="Application.OpenForms"/> — a WinForms collection owned by the UI thread — from
    /// that private thread and then handed the resulting <see cref="Form"/> to
    /// <c>ShowDialog</c> as an owner belonging to a different thread. The agent's Main is already
    /// <c>[STAThread]</c>, so there was never anything for a second STA thread to provide. WA-09.
    /// </para>
    /// <para>
    /// The dialog is modal on the tray thread while it is open, which is the ordinary Windows
    /// behaviour for a picker the user just asked for. The Kestrel request awaiting it is not
    /// blocked — only the caller's own continuation waits.
    /// </para>
    /// </summary>
    public static Task<string?> ShowAsync(UiDispatcher ui) => ui.InvokeAsync(() =>
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select save folder",
            UseDescriptionForTitle = true,
        };
        // Parent to the first open form so the dialog appears in front of the agent window.
        var owner = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : null;
    });
}
