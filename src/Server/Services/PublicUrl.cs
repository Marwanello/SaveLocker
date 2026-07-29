namespace SaveLocker.Server.Services;

/// <summary>
/// Works out the base URL an <b>agent on another machine</b> should use to reach this server, for
/// the two places the server hands a URL to somebody else: the enrollment policy file and the
/// hosted-installer download link.
/// <para>
/// Both used to be built straight from the request's scheme and host, which is right whenever the
/// admin browsed to the server the way an agent would — and wrong the moment they did not. Opening
/// the console on the server box itself (unRAID's own WebUI button will do it) mints an enrollment
/// file saying <c>http://localhost:5080</c>, and a Deck that enrolls with it spends its time trying
/// to reach itself.
/// </para>
/// <para>
/// Deliberately NOT a forwarded-header story. This deployment is LAN-only over plain HTTP with no
/// reverse proxy and no tunnel, so there is nothing to trust `X-Forwarded-*` for; adding that
/// machinery would be config surface protecting against a topology that does not exist. If a proxy
/// is ever put in front, <c>Server:PublicBaseUrl</c> is the answer — set it once and every URL the
/// server hands out is correct regardless of what the request looked like.
/// </para>
/// </summary>
public static class PublicUrl
{
    public const string ConfigKey = "Server:PublicBaseUrl";

    /// <summary>
    /// The configured public base URL if there is one, otherwise the origin this request arrived on.
    /// Never has a trailing slash.
    /// </summary>
    public static string For(HttpContext http, IConfiguration config)
    {
        var configured = config[ConfigKey];
        if (!string.IsNullOrWhiteSpace(configured) && IsUsableAbsolute(configured, out var normalized))
            return normalized;

        return $"{http.Request.Scheme}://{http.Request.Host}".TrimEnd('/');
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is an absolute http/https URL with a host. Returns it
    /// without a trailing slash. Anything else — a bare host, a path, a typo — is refused rather
    /// than written into a policy file that will fail later on somebody else's machine.
    /// </summary>
    public static bool IsUsableAbsolute(string? candidate, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        if (!Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrWhiteSpace(uri.Host)) return false;

        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }

    /// <summary>
    /// True when this URL names the machine asking — <c>localhost</c>, a loopback literal, or
    /// <c>0.0.0.0</c>.
    /// <para>
    /// Only worth refusing when it was <b>inferred</b> from the request origin. An agent running on
    /// the same box as the server is a real setup (it is what the enrollment suite does), so a
    /// loopback URL an admin typed, or put in <c>Server:PublicBaseUrl</c>, is a statement of intent
    /// and is honoured. What is refused is the accident: reaching the console at
    /// <c>http://localhost:5080</c> and minting a file for a Deck that will read it elsewhere.
    /// </para>
    /// </summary>
    public static bool IsLoopback(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.IsLoopback) return true;                       // localhost, 127.x, ::1
        return uri.Host is "0.0.0.0" or "[::]";                // "all interfaces" is not an address
    }
}
