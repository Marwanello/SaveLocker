namespace SaveLocker.Agent;

/// <summary>
/// What counts as "the same server", and what a machine's credentials are bound to.
///
/// <para>
/// <c>ApiKey</c>, <c>MachineId</c> and <c>ServerPin</c> are not standalone settings — they are one
/// identity, issued by one origin. Changing <c>ServerUrl</c> while keeping them meant the agent
/// presented server A's machine key, claimed A's machine id, and carried A's TLS pin to server B.
/// B rejects the key (it never issued it), but the agent has still handed a live credential to a
/// host that was never supposed to see it, and the stale pin means the first real TLS identity B
/// presents looks like a mismatch. That is WA-04.
/// </para>
///
/// <para>
/// Comparison is on scheme + host + port, not the raw string: <c>http://Host:5179/</c> and
/// <c>http://host:5179</c> are the same server, and treating them as different would throw away a
/// working enrollment every time someone retyped the URL with a trailing slash.
/// </para>
/// </summary>
public static class ServerOrigin
{
    /// <summary>
    /// The canonical <c>scheme://host:port</c> for a server URL, or null if the text is not an
    /// absolute http/https URL. Null is the answer for every malformed value — <c>htp://x</c>,
    /// <c>C:\servers</c>, <c>ftp://host</c>, a bare hostname, or empty — so a caller that checks for
    /// null cannot be surprised by an exception later from <c>new Uri(...)</c>.
    /// </summary>
    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;

        // Only these two. A file:// or ftp:// URL parses perfectly well and would then be handed to
        // HttpClient, which throws at construction — exactly the crash-on-restart this prevents.
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrEmpty(uri.Host)) return null;

        // GetLeftPart(Authority) already lowercases the host and drops a default port.
        return uri.GetLeftPart(UriPartial.Authority);
    }

    /// <summary>
    /// The URL to store: validated, canonical, and without a trailing slash (the rest of the agent
    /// concatenates paths onto it). Null if the input is not a usable server URL.
    /// </summary>
    public static string? CanonicalUrl(string? url) =>
        Normalize(url) is null ? null : url!.Trim().TrimEnd('/');

    /// <summary>True when both URLs name the same origin. A null or invalid side is never a match.</summary>
    public static bool Same(string? a, string? b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        return na is not null && nb is not null &&
               string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The refusal shown to a user who typed something that is not a server URL.</summary>
    public const string InvalidUrlMessage =
        "That is not a valid server URL. It must be an absolute http:// or https:// address, " +
        "for example http://192.168.1.10:5179 or https://savelocker.example.com.";
}
