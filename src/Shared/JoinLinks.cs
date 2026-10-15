using System.Text;

namespace SaveLocker.Shared;

/// <summary>
/// The two Linux conveniences a join link hands out besides the <c>curl … | sh</c> one-liner:
/// a <c>savelocker://</c> link that opens an installed agent's Join card, and a downloadable
/// <c>.desktop</c> launcher that runs the one-liner in a terminal. Lives in Shared because the
/// server writes the link and the agent parses it — one shape, two ends.
/// </summary>
public static class JoinLinks
{
    public const string Scheme = "savelocker";

    /// <summary><c>savelocker://enroll?link=&lt;join url&gt;</c> — what the pairing page's
    /// "Open the agent and finish" button points at on Linux.</summary>
    public static string AgentLink(string joinUrl) =>
        $"{Scheme}://enroll?link={Uri.EscapeDataString(joinUrl)}";

    /// <summary>
    /// The join URL inside a <c>savelocker://enroll?link=</c> link, or null for anything else. Any web
    /// page can fire this scheme, so only a well-formed join link gets through — and even that only
    /// pre-fills the Join card; enrolling still takes the person's click.
    /// </summary>
    public static string? JoinUrlFromAgentLink(string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "enroll", StringComparison.OrdinalIgnoreCase))
            return null;

        string? raw = null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair[..eq] == "link") raw = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var join) ||
            join.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(join.Query) || !string.IsNullOrEmpty(join.Fragment))
            return null;

        var segments = join.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || segments[^2] != "join" ||
            JoinCodes.NormalizeJoinCode(segments[^1]) is not { } code)
            return null;

        var serverPath = string.Join('/', segments[..^2]);
        return $"{join.GetLeftPart(UriPartial.Authority)}{(serverPath.Length == 0 ? "" : "/" + serverPath)}/join/{code}";
    }

    /// <summary>
    /// The <c>.desktop</c> launcher behind <c>/join/{code}/install.desktop</c>: double-clicked in a
    /// file manager, it opens a terminal and runs the same <c>install.sh</c> the one-liner does. Null
    /// when the join URL holds a character that would need quoting — the link is pasted into a shell
    /// command, and a URL this plain never needs it.
    /// </summary>
    /// <remarks>
    /// Single quotes around <c>sh -c</c>'s script, as the Decky and EmuDeck installers do: the spec only
    /// defines double quotes, but GLib and KDE both parse single ones, and inside them nothing — not
    /// <c>$</c>, not <c>"</c> — needs an escape. A fetch that fails is repeated without <c>-f</c> so the
    /// server's own "expired / already used" sentence is what the terminal shows.
    /// </remarks>
    public static string? InstallLauncher(string joinUrl, string serverName)
    {
        if (!IsShellPlain(joinUrl)) return null;
        var script = $"{joinUrl}/install.sh".Replace("%", "%%");
        var name = OneLine(serverName);
        return $"""
            [Desktop Entry]
            Type=Application
            Version=1.0
            Name=Install SaveLocker
            Comment=Install the SaveLocker agent and enroll this device with {name}
            Icon=system-software-install
            Terminal=true
            Exec=sh -c 'cd "$(mktemp -d)" || exit 1; if curl -fsS -o install.sh {script}; then sh install.sh; else curl -sS {script}; fi; rm -f install.sh; echo; echo Press Enter to close this window.; read reply'

            """.Replace("\r\n", "\n");
    }

    private static bool IsShellPlain(string url) =>
        url.Length > 0 && url.All(c => c < 128 && (char.IsAsciiLetterOrDigit(c) || "-._~:/[]@%".Contains(c)));

    private static string OneLine(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text) sb.Append(char.IsControl(c) ? ' ' : c);
        return sb.ToString();
    }
}
