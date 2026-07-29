using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SaveLocker.Agent;

/// <summary>
/// The SaveLocker agent window: a 900×600 WinForms shell hosting a WebView2 control
/// that navigates to the local AgentApiServer (React UI).
/// </summary>
internal sealed class AgentWindow : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private readonly int _port;
    private bool _navigated;
    // Where to go once WebView2 finishes initializing. WebView2 comes up asynchronously, so on the
    // FIRST open there is no CoreWebView2 yet and a Navigate call had nowhere to go — it was
    // silently dropped and OnLoad then went to "/" regardless. That is the whole of WA-12: accepting
    // the first-run prompt asked for Settings and landed on Overview.
    //
    // Only ever touched on the UI thread: every caller goes through the tray's dispatcher (WA-09),
    // and OnLoad's continuation resumes there too. No lock is needed, and adding one would hide that
    // requirement rather than enforce it.
    private string? _pendingUrl;
    private bool _coreReady;

    private string HomeUrl => $"http://localhost:{_port}/";

    /// <param name="view">Optional initial view (the React app's hash route), applied as soon as
    /// WebView2 is ready rather than being lost to the startup race.</param>
    public AgentWindow(int port, string? view = null)
    {
        _port = port;
        _pendingUrl = UrlForView(port, view);
        Text = "SaveLocker";
        Icon = AppResources.Icon;
        // WinForms ClientSize units are physical pixels even when DeviceDpi > 96.
        // WebView2 divides physical px by devicePixelRatio (= DeviceDpi/96) to get CSS px.
        // So to get 900×600 CSS pixels we need 900*(DeviceDpi/96) × 600*(DeviceDpi/96) physical px.
        var dpiScale = DeviceDpi / 96f;
        ClientSize = new Size((int)(900 * dpiScale), (int)(600 * dpiScale));
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x0d, 0x11, 0x14);
        Controls.Add(_webView);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_navigated) return;
        _navigated = true;

        try
        {
            AgentLogger.Log("WebView2: initializing…");
            // Explicit user-data folder avoids silent failures when the default
            // location (next to the exe) isn't writable or is locked by a prior run.
            var udFolder = Path.Combine(AgentConfig.DefaultDir, "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: udFolder);
            await _webView.EnsureCoreWebView2Async(env);
            _coreReady = true;

            // The route requested while WebView2 was still starting wins over the home page. A
            // later deep link replaces an earlier one, so what opens is the last thing asked for.
            var target = _pendingUrl ?? HomeUrl;
            _pendingUrl = null;
            AgentLogger.Log($"WebView2: ready — navigating to {target}");
            _webView.CoreWebView2.Navigate(target);
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("WebView2 init", ex);
            MessageBox.Show(
                $"The SaveLocker UI failed to load:\n\n{ex.Message}\n\n" +
                $"Log: {AgentLogger.LogPath}",
                "SaveLocker — UI Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Navigate to a URL (e.g. to deep-link to a view via hash). Before WebView2 is ready the
    /// request is <b>queued</b> rather than dropped, and applied the moment it comes up.
    /// </summary>
    public void Navigate(string url)
    {
        if (_coreReady && _webView.CoreWebView2 is { } wv2) wv2.Navigate(url);
        else _pendingUrl = url;
    }

    /// <summary>Show a specific view, whether or not WebView2 has finished starting.</summary>
    public void NavigateToView(string? view)
    {
        if (UrlForView(_port, view) is { } url) Navigate(url);
    }

    /// <summary>The React app routes on the hash, so a view is a fragment on the home URL.</summary>
    private static string? UrlForView(int port, string? view) =>
        string.IsNullOrWhiteSpace(view) ? null : $"http://localhost:{port}/#{view.Trim()}";

    // ─── DWM title bar theming ────────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Attribute IDs (Windows 10 20H1+ for dark mode; Windows 11 22000+ for color)
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR           = 35;
    private const int DWMWA_TEXT_COLOR              = 36;

    // COLORREF = 0x00BBGGRR  (blue in high byte, red in low byte)
    // #2A3238 → R=0x2A G=0x32 B=0x38 → 0x0038322A
    // #ECEFF1 → R=0xEC G=0xEF B=0xF1 → 0x00F1EFEC
    private const int ColorCaption = 0x0038322A;
    private const int ColorText    = 0x00F1EFEC;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        // Windows 11 only — silently ignored on Windows 10
        int caption = ColorCaption;
        DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        int text = ColorText;
        DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Hide instead of close so the window can be re-shown from the tray.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
