using System.Net.Security;

namespace SaveLocker.Agent;

/// <summary>
/// One place that builds an <see cref="HttpClient"/> for talking to the SaveLocker server, so the
/// TLS policy cannot differ between callers.
///
/// <para>
/// It exists because it already did differ. <see cref="ApiClient"/> observed and pinned the server's
/// key; <see cref="UpdateChecker"/> constructed a bare <c>HttpClient</c> and ignored the pin
/// entirely — meaning the one channel that ends in <b>executing a downloaded binary</b> was the only
/// channel with no identity check on it at all (WA-05).
/// </para>
///
/// <para>
/// Note what this does and does not buy on the default configuration: SaveLocker ships no
/// certificates and plain http is supported (Decisions.md), so over http there is no identity to
/// pin and this adds nothing. That is precisely why the installer digest is a separate, mandatory
/// control rather than a nicety layered on top of TLS.
/// </para>
/// </summary>
public static class ServerHttp
{
    /// <summary>
    /// A handler that <i>observes</i> the server's TLS identity without weakening validation — the
    /// callback still returns the platform's own verdict. Returning true unconditionally here is the
    /// classic way this hook gets misused into disabling TLS validation.
    /// </summary>
    public static HttpClientHandler CreateHandler(
        string? expectedPin, Action<string>? onObserved = null, Action<string>? onMismatch = null)
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
        {
            if (ServerTrust.Fingerprint(cert) is { } pin)
            {
                onObserved?.Invoke(pin);
                if (!string.IsNullOrEmpty(expectedPin) && pin != expectedPin)
                    onMismatch?.Invoke(pin);
            }
            return errors == SslPolicyErrors.None;
        };
        return handler;
    }

    /// <summary>
    /// A client aimed at the configured server, carrying the recorded pin policy and — only when
    /// asked — the machine API key.
    /// </summary>
    /// <param name="withApiKey">
    /// False for a request to a host that is not the configured server. The machine key is a
    /// credential for <i>this</i> server and must never be attached to a request going anywhere
    /// else; a <c>DownloadUrl</c> pointing at another origin used to receive it in a default header.
    /// </param>
    public static HttpClient Create(AgentConfig config, bool withApiKey)
    {
        var handler = CreateHandler(
            config.ServerPin,
            onMismatch: observed => ServerTrust.WarnMismatch(config.ServerPin!, observed));

        var http = new HttpClient(handler) { BaseAddress = new Uri(config.ServerUrl) };
        if (withApiKey && !string.IsNullOrEmpty(config.ApiKey))
            http.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);
        return http;
    }

    /// <summary>
    /// A client for an origin that is <b>not</b> the configured server: no base address, no
    /// credential, and no pin (the pin belongs to the server, not to this host).
    /// </summary>
    public static HttpClient CreateForeign() => new(CreateHandler(expectedPin: null));
}
