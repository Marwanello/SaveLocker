using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;

namespace SaveLocker.Agent;

/// <summary>
/// How the server's certificate is vouched for, recorded with the pin. <see cref="Public"/> means it
/// carries embedded Certificate Transparency SCTs — a public CA issued it and logged it where anyone
/// can audit it. <see cref="Private"/> is everything else that still validates on this machine: a
/// self-signed cert or a home CA the user installed, or a TLS-intercepting proxy's root.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ServerCertTrust>))]
public enum ServerCertTrust { Public, Private }

/// <summary>What the TLS handshake presented: the pin, how the cert is vouched for, and the platform's verdict.</summary>
public readonly record struct ObservedCert(string Pin, ServerCertTrust Trust, SslPolicyErrors Errors);

public enum PinVerdict
{
    /// <summary>The key is the pinned one.</summary>
    Match,
    /// <summary>A new key on a publicly-logged cert that validates for this host: a routine renewal. Re-pinned quietly.</summary>
    Renewed,
    /// <summary>A new key that a renewal does not explain. Warn.</summary>
    Changed,
}

/// <summary>
/// Trust-on-first-use pinning of the server's TLS identity (Decisions.md §4).
/// <para>
/// The enrollment policy is unsigned, so the agent's protection against being pointed at a malicious
/// server is: the user got the link from their own console, HTTPS, and this pin — recorded at
/// enrollment, checked on every later connection.
/// </para>
/// <para>
/// <b>The pin only adds anything for a private certificate.</b> Every connection already has to pass
/// normal CA validation for the configured host name. Behind Cloudflare or Let's Encrypt the key
/// rotates on every renewal (Cloudflare's edge: ~60 days), and a CA-valid, CT-logged renewal is
/// indistinguishable from — and no riskier than — any other cert the public PKI would accept. Warning
/// on it fired on every machine, every renewal, and taught users to ignore the one warning that
/// matters. So a publicly-logged renewal re-pins quietly; a private cert's new key, a public cert
/// replaced by one nobody logged, or a cert that fails validation still warns.
/// </para>
/// It <b>warns</b> rather than blocks. A pin that hard-failed would take the agent offline for a
/// reason the user cannot diagnose on a headless Deck; a pin that warns still surfaces that the
/// server is not who it was.
/// </summary>
public static class ServerTrust
{
    // RFC 6962 §3.3: the X.509v3 extension carrying the SCTs a CA embeds at issuance.
    private const string EmbeddedSctOid = "1.3.6.1.4.1.11129.2.4.2";

    /// <summary>SHA-256 of the certificate's SubjectPublicKeyInfo, base64. Null when there is no cert.</summary>
    public static string? Fingerprint(X509Certificate2? cert)
    {
        if (cert is null) return null;
        var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
        return Convert.ToBase64String(SHA256.HashData(spki));
    }

    /// <summary>
    /// Public when the cert carries embedded SCTs. Presence is a classifier, not a verification: the
    /// SCTs are not checked against the CT logs' keys. Forging one takes a root this machine already
    /// trusts, and whoever can plant one of those owns the machine anyway.
    /// </summary>
    public static ServerCertTrust Classify(X509Certificate2 cert) =>
        cert.Extensions[EmbeddedSctOid] is not null ? ServerCertTrust.Public : ServerCertTrust.Private;

    public static ObservedCert Observe(X509Certificate2 cert, SslPolicyErrors errors) =>
        new(Fingerprint(cert)!, Classify(cert), errors);

    /// <summary>
    /// True when this URL can be pinned at all. Plain http:// has no identity to pin — on a trusted
    /// LAN that is a legitimate setup, so it is not an error, but nothing is recorded and the
    /// warning must not fire for it later.
    /// </summary>
    public static bool IsPinnable(string serverUrl) =>
        Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// <paramref name="pinnedTrust"/> null is an agent that pinned before the trust was recorded; it
    /// is judged by what the server presents now, which is what lets those agents stop warning.
    /// </summary>
    public static PinVerdict Judge(string pinned, ServerCertTrust? pinnedTrust, ObservedCert seen)
    {
        if (seen.Pin == pinned) return PinVerdict.Match;
        // None already means: chains to a trusted root, in date, and valid for the configured host.
        return seen.Errors == SslPolicyErrors.None
               && seen.Trust == ServerCertTrust.Public
               && pinnedTrust != ServerCertTrust.Private
            ? PinVerdict.Renewed
            : PinVerdict.Changed;
    }

    /// <summary>
    /// The policy every connection to the configured server runs: nothing without a pin, a quiet
    /// re-pin for a renewal, the warning for anything else.
    /// </summary>
    public static PinVerdict? Check(AgentConfig config, ObservedCert seen)
    {
        var pinned = config.ServerPin;
        if (string.IsNullOrEmpty(pinned)) return null;

        var verdict = Judge(pinned, config.ServerPinTrust, seen);
        // A long-lived host judges against the pin it loaded at boot. Before calling a key changed, look
        // at the file: `trust --accept` in another process may already have accepted this one.
        if (verdict == PinVerdict.Changed)
        {
            bool adopted;
            lock (RenewLock) adopted = config.AdoptPinFromDisk();
            if (adopted)
            {
                pinned = config.ServerPin;
                if (string.IsNullOrEmpty(pinned)) return null;
                verdict = Judge(pinned, config.ServerPinTrust, seen);
            }
        }

        if (verdict == PinVerdict.Renewed) AcceptRenewal(config, pinned, seen);
        else if (verdict == PinVerdict.Changed) WarnMismatch(pinned, config.ServerPinTrust, seen);
        else if (config.ServerPinTrust is null && seen.Errors == SslPolicyErrors.None) LearnTrust(config, pinned, seen);
        return verdict;
    }

    private static readonly object RenewLock = new();

    private static void AcceptRenewal(AgentConfig config, string pinned, ObservedCert seen)
    {
        lock (RenewLock)
        {
            // Another handshake got here first, and logged and recorded this renewal.
            if (config.ServerPin != pinned) return;
            config.ServerPin = seen.Pin;
            config.ServerPinTrust = seen.Trust;
        }

        AgentLogger.Log($"The server's certificate was renewed with a new key (publicly logged, valid for " +
                        $"{config.ServerUrl}); pin updated: {pinned} -> {seen.Pin}");

        // Best-effort: in memory is already right for this process, and a failure here only means the
        // next process re-pins again. Never worth failing a handshake over.
        try { config.RecordHandshakePin(pinned, seen.Pin, seen.Trust); }
        catch (Exception ex) { AgentLogger.LogException("Recording the renewed server pin", ex); }
    }

    /// <summary>
    /// A pin recorded before the trust was gets it from the first matching handshake. Left unknown, a
    /// private server's agent would take a public cert for its host as a renewal, forever.
    /// </summary>
    private static void LearnTrust(AgentConfig config, string pinned, ObservedCert seen)
    {
        lock (RenewLock)
        {
            if (config.ServerPin != pinned || config.ServerPinTrust is not null) return;
            config.ServerPinTrust = seen.Trust;
        }

        try { config.RecordHandshakePin(pinned, pinned, seen.Trust); }
        catch (Exception ex) { AgentLogger.LogException("Recording the server pin's trust", ex); }
    }

    public static string MismatchWarning(string expected, ServerCertTrust? pinnedTrust, ObservedCert seen)
    {
        var why = seen.Errors != SslPolicyErrors.None
            ? $"  The new certificate also fails validation ({seen.Errors}), so this connection was refused.\n"
            : pinnedTrust switch
            {
                ServerCertTrust.Public =>
                    "  This machine enrolled against a publicly-trusted certificate, and this one was not publicly\n" +
                    "  logged. That is what a TLS-intercepting proxy or a different server looks like.\n",
                ServerCertTrust.Private =>
                    "  This server uses a private certificate (self-signed or a home CA). Its key only changes when\n" +
                    "  someone replaces that certificate.\n",
                // Pinned before the trust was recorded, so all that is known is the new cert.
                _ => "  The new certificate was not publicly logged. A private certificate (self-signed or a home CA)\n" +
                     "  that was replaced looks like this, and so does a TLS-intercepting proxy or a different server.\n",
            };
        return "WARNING: the server's TLS identity has CHANGED since this machine enrolled.\n" +
               $"  pinned:   {expected}\n" +
               $"  observed: {seen.Pin}\n" +
               why +
               "  A pull writes files into your save folders, so stop and check before syncing. If you replaced\n" +
               "  the certificate yourself, accept the new one with: savelocker trust --accept";
    }

    private static int _warned;

    /// <summary>
    /// Raise the mismatch once per process, to the log <i>and</i> to stderr. The log alone is not
    /// enough: a user running a one-shot command would never see it, and the daemon would otherwise
    /// repeat it on every 20-second poll.
    /// </summary>
    private static void WarnMismatch(string expected, ServerCertTrust? pinnedTrust, ObservedCert seen)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 1) return;
        var warning = MismatchWarning(expected, pinnedTrust, seen);
        AgentLogger.Log(warning);
        Console.Error.WriteLine(warning);
    }

    /// <summary>One line for <c>doctor</c> and <c>trust</c>: what this agent does if the key changes.</summary>
    public static string Describe(AgentConfig config) =>
        !IsPinnable(config.ServerUrl) ? "plain http — no TLS identity to pin"
        : string.IsNullOrEmpty(config.ServerPin) ? "not pinned — enroll, or run: savelocker trust --accept"
        : config.ServerPinTrust switch
        {
            ServerCertTrust.Public => "public certificate — a renewal re-pins by itself; anything else warns",
            ServerCertTrust.Private => "private certificate — any new key warns",
            _ => "pinned — re-pinned by itself on the next publicly-logged renewal",
        };
}
