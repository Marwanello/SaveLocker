using System.Drawing.Imaging;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SaveLocker.Shared;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace SaveLocker.Agent;

/// <summary>
/// Windows delivery for <see cref="NotificationCenter"/>: a real toast with a title, a body, the
/// brand mark and up to two buttons, in place of the tray balloon.
/// <para>
/// <b>What a button does.</b> A plain notice's primary button is a
/// <c>http://localhost:&lt;port&gt;/open?view=route&amp;key=…</c> link (<see cref="NoticeAction.ToOpenUrl"/>):
/// the shell hands it to the default browser, and the page it lands on asks the tray to raise its own
/// window at that exact screen. The key (<see cref="LocalAuth.OpenLinkKey"/>) is what lets it raise the
/// window; a web page, which cannot know it, gets only the screen in a tab. It works from the banner and
/// from the Action Center, needs nothing registered, and cannot be aimed at anything but the agent's own
/// page. The alternatives were tried on Windows 11 25H2 and rejected — see <see cref="NoticeAction"/>
/// for the measurements.
/// <br>A notice carrying an <see cref="AgentCommand"/> gets one more button, first: <b>Retry now</b> or
/// <b>Install now</b>, with no <c>activationType</c> so Windows routes the click to the COM activator
/// (<see cref="ToastActivation"/>) — in-process while the tray runs, else the exe is started with
/// <c>-ToastActivated</c>. That click does the thing itself; the link stays as the second button, and a
/// system-level dismiss comes last.
/// </para>
/// <para>
/// <b>What names the toast.</b> Its header takes an app name and icon from a Start-menu shortcut
/// carrying the toast's AUMID (<see cref="ProductAumid"/>) — the installer's shortcut does
/// (installer/SaveLocker.iss). Verified: with such a shortcut the header read the shortcut's name with
/// the app's icon; without one it read the bare AUMID string and showed no icon. An HKCU
/// <c>AppUserModelId</c> registration with a DisplayName and IconUri, and setting the process's own
/// AppUserModelID, were both tried and changed nothing. So a build with no shortcut — a test rig, a
/// dev run — shows the AUMID itself as its name, which is why it is chosen to be worth reading, and
/// the mark in the toast's own logo slot works either way.
/// </para>
/// </summary>
internal sealed class ToastPresenter : INotificationPresenter
{
    /// <summary>The installed agent's toast identity. Also stamped on its Start-menu shortcut by the
    /// installer; <c>run-appearance-consistency-tests</c> fails if the two drift.</summary>
    public const string ProductAumid = "SaveLocker";

    private const string ToastGroup = "SaveLocker";
    private const int MaxTagLength = 64;
    internal const int DefaultPort = 5178;
    private const int LogoPixels = 96;

    /// <summary>The toast identity a port runs under — the product's on :5178, a distinguishable test
    /// rig's elsewhere. Also what <see cref="ToastActivation"/> registers per port, and what a click's
    /// reported AUMID is read back with (<see cref="PortFromAumid"/>).</summary>
    public static string AumidForPort(int port) =>
        port == DefaultPort ? ProductAumid : $"{ProductAumid}.Test.{port}";

    /// <summary>The port an AUMID belongs to; null for an AUMID this agent never issues. The product's
    /// own identity has no port in its text, so the default is the answer for it.</summary>
    public static int? PortFromAumid(string? aumid)
    {
        if (string.Equals(aumid, ProductAumid, StringComparison.Ordinal)) return DefaultPort;
        const string prefix = ProductAumid + ".Test.";
        if (aumid is not null && aumid.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(aumid[prefix.Length..], out var port) && port is > 0 and <= 65535)
            return port;
        return null;
    }

    // True once the mark has been written, so a toast is never made to point at a file that is not there.
    private volatile bool _hasLogo;

    private readonly string _aumid;
    private readonly string _uiBaseUrl;
    private readonly string _logoBase;
    private readonly Regex _ownLogoName;
    private volatile string _logoPath;
    // Null until the agent's API server exists (UseOpenLinkKey); a link without it still opens the
    // screen, in the browser, and only the window raise waits for it.
    private volatile string? _openLinkKey;
    private readonly Action<string> _fallback;

    /// <param name="fallback">What to do if Windows will not take a toast at all — a locked-down
    /// policy, a broken Runtime. Given "title: body"; the tray shows it as a balloon, so a machine
    /// that could always be told still can.</param>
    public ToastPresenter(int port, Action<string> fallback)
    {
        _fallback = fallback;
        _uiBaseUrl = $"http://localhost:{port}/";
        // A test rig's identity is tellable apart from the installed agent's on the very toast it
        // raises — see the class remarks for why this string is what a shortcut-less build displays.
        _aumid = AumidForPort(port);
        // In %TEMP%, and nowhere else that was tried. The shell draws a toast in an AppContainer
        // process, which can read only where "ALL APPLICATION PACKAGES" has been granted access — and
        // of the user's own folders that is %TEMP%. The agent's state directory is out (it is ACL-
        // locked to the user, SYSTEM and Administrators on purpose: StateDirSecurity, WA-03), and so
        // is a plain folder under %LOCALAPPDATA%: in both cases the file was there, the right size and
        // format, and the toast silently had no picture. A mark is not sensitive, and it is rewritten
        // on every start and every look change, so Temp being cleaned now and then costs nothing.
        _logoBase = Path.Combine(Path.GetTempPath(),
            port == DefaultPort ? "savelocker-toast-logo" : $"savelocker-toast-logo-{port}");
        _logoPath = _logoBase + ".png";
        // This presenter's own marks only — one per look, plus the old fixed name. The installed
        // agent's base is a prefix of every test rig's ("savelocker-toast-logo-5188-…"), so the
        // directory's own prefix match would have the installed agent delete a running rig's mark.
        _ownLogoName = new Regex(
            "^" + Regex.Escape(Path.GetFileName(_logoBase)) + @"(-[0-9a-f]{10})?\.png$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>The identity this presenter's toasts carry. What <see cref="ToastActivation"/>
    /// registers under, and the header a shortcut-less build shows.</summary>
    internal string Aumid => _aumid;

    /// <summary>The key a button's link needs to raise the tray window — the API server's.</summary>
    public void UseOpenLinkKey(string key) => _openLinkKey = key;

    /// <summary>
    /// Draw the toast's mark in the current look — the same mark and accent as the tray. It is shown in
    /// the toast's own logo slot (see <see cref="BuildXml"/>) at 48 dip, so it is drawn at twice that:
    /// the tray's 32 px icon upscaled would be soft on a high-DPI screen. Rewriting the file is all a
    /// look change needs; the next toast reads it.
    /// <para>
    /// Each look gets its own file name. One fixed name was unreliable: the shell keeps the picture it
    /// already has for a path (and may hold the file open while a toast is up), so a rewrite sometimes
    /// showed and sometimes did not. A new path is always read fresh; the old look's file is removed
    /// once nothing is using it.
    /// </para>
    /// </summary>
    public void UseLook(AppearanceDto look)
    {
        try
        {
            var path = $"{_logoBase}-{LookId(look)}.png";
            if (!File.Exists(path))
            {
                using var icon = MarkIcon.Render(look, LogoPixels);
                using var bitmap = icon.ToBitmap();
                bitmap.Save(path, ImageFormat.Png);
            }
            _logoPath = path;
            _hasLogo = true;
            foreach (var old in Directory.EnumerateFiles(Path.GetDirectoryName(_logoBase)!, Path.GetFileName(_logoBase) + "*.png"))
                if (_ownLogoName.IsMatch(Path.GetFileName(old)) &&
                    !string.Equals(old, path, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(old); } catch { /* still on screen in a toast; the next look change retries */ }
        }
        catch (Exception ex) { AgentLogger.LogException("ToastPresenter.UseLook", ex); }
    }

    public string? Show(AgentNotice notice)
    {
        try
        {
            var notifier = ToastNotificationManager.CreateToastNotifier(_aumid);
            // Off for this app, for this user or by policy: nothing would appear. Saying so — once,
            // by the center — beats a toast that was "sent" and never seen. Asked in its own try:
            // until Windows has seen this app show a toast once it has no settings entry for it, and
            // the property throws "element not found" (0x80070490) rather than answering — which is
            // "unknown", not "off", and must not stop the first toast from ever being tried.
            if (SettingOrNull(notifier) is { } setting && setting != NotificationSetting.Enabled)
                return $"Windows notifications are turned off for SaveLocker ({setting})";

            var xml = new XmlDocument();
            xml.LoadXml(BuildXml(notice));
            notifier.Show(new ToastNotification(xml) { Tag = TagFor(notice.Key), Group = ToastGroup });
            // Logged like every other outcome here: Focus Assist and the user's notification settings
            // can swallow a toast without a word, and "it said it showed it" is the only account that
            // always survives — the same reason CheckForUpdateAsync logs an update check that found nothing.
            AgentLogger.Log($"notification shown: '{notice.Title}'.");
            return null;
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("ToastPresenter.Show", ex);
            _fallback($"{notice.Title}: {notice.Body}");
            return null;
        }
    }

    private static string LookId(AppearanceDto look) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{look.Theme}|{look.Accent}|{look.Mark}")))[..10].ToLowerInvariant();

    private static NotificationSetting? SettingOrNull(ToastNotifier notifier)
    {
        try { return notifier.Setting; }
        catch { return null; }
    }

    public void Withdraw(string key)
    {
        try { ToastNotificationManager.History.Remove(TagFor(key), ToastGroup, _aumid); }
        catch (Exception ex) { AgentLogger.LogException("ToastPresenter.Withdraw", ex); }
    }

    /// <summary>
    /// The toast, as Windows' XML. A notice that needs a decision (an Error) stays until dismissed —
    /// the <c>reminder</c> scenario — instead of sliding into the Action Center after five seconds.
    /// </summary>
    internal string BuildXml(AgentNotice notice)
    {
        var link = notice.Primary.ToOpenUrl(_uiBaseUrl, _openLinkKey);

        var xml = new StringBuilder("<toast");
        if (link is not null)
        {
            xml.Append($" launch=\"{Esc(link)}\" activationType=\"protocol\"");
            if (notice.Severity == AgentEventSeverity.Error) xml.Append(" scenario=\"reminder\"");
        }
        xml.Append("><visual><binding template=\"ToastGeneric\">");
        // The brand mark, in the logo slot, in the current accent. A plain path, as verified: a local
        // file is allowed for a desktop app (see the constructor for where it has to live).
        if (_hasLogo)
            xml.Append($"<image placement=\"appLogoOverride\" src=\"{Esc(_logoPath)}\"/>");
        xml.Append($"<text>{Esc(notice.Title)}</text><text>{Esc(notice.Body)}</text>")
           .Append("</binding></visual>");

        if (link is not null || notice.Command is not null)
        {
            xml.Append("<actions>");
            // The button that acts, when there is one. No activationType: "foreground" is the COM
            // activator (ToastActivation), which is what lands the click in the tray rather than a
            // browser. The label and arguments are both ours, but escape them like everything else.
            if (notice.Command is { } command)
                xml.Append($"<action content=\"{Esc(command.Label)}\" arguments=\"{Esc(command.Arguments)}\"/>");
            if (link is not null)
                xml.Append($"<action content=\"{Esc(notice.PrimaryLabel)}\" arguments=\"{Esc(link)}\" activationType=\"protocol\"/>");
            if (notice.SecondaryLabel.Length > 0)
                xml.Append($"<action content=\"{Esc(notice.SecondaryLabel)}\" arguments=\"dismiss\" activationType=\"system\"/>");
            xml.Append("</actions>");
        }
        return xml.Append("</toast>").ToString();
    }

    private static string Esc(string text) => SecurityElement.Escape(text) ?? "";

    /// <summary>A toast's tag is capped at 64 characters. Ours are far shorter, but a key that ever
    /// isn't must still map to the same tag every time or it could never be withdrawn.</summary>
    private static string TagFor(string key) =>
        key.Length <= MaxTagLength
            ? key
            : Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
}
