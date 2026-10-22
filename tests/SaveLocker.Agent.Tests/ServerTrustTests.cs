using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace SaveLocker.Agent.Tests;

/// <summary>
/// The TOFU pin's rule (Decisions.md §4): a CA-valid, publicly-logged renewal re-pins quietly; any other
/// new key warns. Every case runs through the handler's own validation callback — the code a real
/// handshake reaches — with certs built here, one carrying the embedded-SCT extension a public CA adds.
/// </summary>
public sealed class ServerTrustTests : IDisposable
{
    private const string Server = "https://saves.example.com";

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "savelocker-trust-" + Guid.NewGuid().ToString("N"));

    public ServerTrustTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static X509Certificate2 Cert(bool publiclyLogged)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=saves.example.com", key, HashAlgorithmName.SHA256);
        if (publiclyLogged)
            // The payload is never parsed — presence is the classifier — so an empty SCT list will do.
            req.CertificateExtensions.Add(
                new X509Extension(new Oid("1.3.6.1.4.1.11129.2.4.2"), [0x04, 0x02, 0x00, 0x00], critical: false));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60));
    }

    private AgentConfig Enrolled(X509Certificate2 cert, ServerCertTrust? trust)
    {
        var config = AgentConfig.Load(Path.Combine(_dir, "config.json"));
        Assert.True(config.TrySetServerUrl(Server, out _));
        config.ServerPin = ServerTrust.Fingerprint(cert);
        config.ServerPinTrust = trust;
        config.Save();
        return config;
    }

    private static PinVerdict? Handshake(AgentConfig config, X509Certificate2 cert,
        SslPolicyErrors errors = SslPolicyErrors.None)
    {
        PinVerdict? verdict = null;
        using var handler = ServerHttp.CreateHandler(seen => verdict = ServerTrust.Check(config, seen));
        using var request = new HttpRequestMessage(HttpMethod.Get, Server);
        var accepted = handler.ServerCertificateCustomValidationCallback!(request, cert, null, errors);
        // The pin never overrides the platform: only a clean chain is ever accepted.
        Assert.Equal(errors == SslPolicyErrors.None, accepted);
        return verdict;
    }

    private AgentConfig OnDisk() => AgentConfig.Load(Path.Combine(_dir, "config.json"));

    [Fact]
    public void Classify_reads_the_embedded_SCT_extension()
    {
        Assert.Equal(ServerCertTrust.Public, ServerTrust.Classify(Cert(publiclyLogged: true)));
        Assert.Equal(ServerCertTrust.Private, ServerTrust.Classify(Cert(publiclyLogged: false)));
    }

    [Fact]
    public void Public_ca_renewal_with_a_new_key_repins_in_memory_and_on_disk()
    {
        var config = Enrolled(Cert(publiclyLogged: true), ServerCertTrust.Public);
        var renewed = Cert(publiclyLogged: true);

        Assert.Equal(PinVerdict.Renewed, Handshake(config, renewed));

        var pin = ServerTrust.Fingerprint(renewed);
        Assert.Equal(pin, config.ServerPin);
        Assert.Equal(pin, OnDisk().ServerPin);
        Assert.Equal(ServerCertTrust.Public, OnDisk().ServerPinTrust);
        // And the next connection in this process is simply a match.
        Assert.Equal(PinVerdict.Match, Handshake(config, renewed));
    }

    [Fact]
    public void A_pin_recorded_before_trust_existed_repins_on_a_public_renewal()
    {
        // Every agent enrolled before this change, the Deck behind Cloudflare among them.
        var config = Enrolled(Cert(publiclyLogged: true), trust: null);
        var renewed = Cert(publiclyLogged: true);

        Assert.Equal(PinVerdict.Renewed, Handshake(config, renewed));
        Assert.Equal(ServerTrust.Fingerprint(renewed), OnDisk().ServerPin);
        Assert.Equal(ServerCertTrust.Public, OnDisk().ServerPinTrust);
    }

    [Fact]
    public void Self_signed_key_change_warns_and_keeps_the_pin()
    {
        var original = Cert(publiclyLogged: false);
        var config = Enrolled(original, ServerCertTrust.Private);
        var pin = config.ServerPin;

        Assert.Equal(PinVerdict.Changed, Handshake(config, Cert(publiclyLogged: false)));
        Assert.Equal(pin, config.ServerPin);
        Assert.Equal(pin, OnDisk().ServerPin);
    }

    [Fact]
    public void Self_signed_server_moving_to_a_public_cert_still_warns()
    {
        var config = Enrolled(Cert(publiclyLogged: false), ServerCertTrust.Private);
        Assert.Equal(PinVerdict.Changed, Handshake(config, Cert(publiclyLogged: true)));
    }

    [Fact]
    public void Public_server_presenting_an_unlogged_cert_warns()
    {
        // What a TLS-intercepting proxy with its root installed on this machine looks like.
        var config = Enrolled(Cert(publiclyLogged: true), ServerCertTrust.Public);
        var pin = config.ServerPin;

        Assert.Equal(PinVerdict.Changed, Handshake(config, Cert(publiclyLogged: false)));
        Assert.Equal(pin, OnDisk().ServerPin);
    }

    [Fact]
    public void Public_cert_that_fails_validation_warns_and_is_refused()
    {
        var config = Enrolled(Cert(publiclyLogged: true), ServerCertTrust.Public);
        var pin = config.ServerPin;

        Assert.Equal(PinVerdict.Changed,
            Handshake(config, Cert(publiclyLogged: true), SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.Equal(pin, OnDisk().ServerPin);
    }

    [Fact]
    public void Renewal_never_overwrites_a_pin_changed_on_disk_since()
    {
        var original = Cert(publiclyLogged: true);
        var config = Enrolled(original, ServerCertTrust.Public);

        // Another process ran trust --accept (or re-enrolled) after this one loaded its config.
        var other = OnDisk();
        other.ServerPin = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=";
        other.Save();

        Assert.Equal(PinVerdict.Renewed, Handshake(config, Cert(publiclyLogged: true)));
        Assert.Equal("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=", OnDisk().ServerPin);
        // And this process adopts it, so its own next Save() cannot write the renewal over it.
        Assert.Equal("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=", config.ServerPin);
        config.Save();
        Assert.Equal("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=", OnDisk().ServerPin);
    }

    [Fact]
    public void A_stale_hosts_save_keeps_a_pin_another_process_accepted()
    {
        // Found on the Deck: the daemon's next Save() wrote its boot-time pin back over `trust --accept`.
        var daemon = Enrolled(Cert(publiclyLogged: false), ServerCertTrust.Private);
        var cli = OnDisk();
        var accepted = Cert(publiclyLogged: false);
        cli.SetPin(ServerTrust.Observe(accepted, SslPolicyErrors.None));
        cli.Save();

        daemon.SettleQuietSeconds += 1;   // any unrelated setting the daemon persists
        daemon.Save();

        Assert.Equal(ServerTrust.Fingerprint(accepted), OnDisk().ServerPin);
        Assert.Equal(ServerTrust.Fingerprint(accepted), daemon.ServerPin);
    }

    [Fact]
    public void A_stale_hosts_save_keeps_a_renewal_another_process_recorded()
    {
        var daemon = Enrolled(Cert(publiclyLogged: true), ServerCertTrust.Public);
        var ui = OnDisk();
        var renewed = Cert(publiclyLogged: true);

        Assert.Equal(PinVerdict.Renewed, Handshake(daemon, renewed));
        ui.Save();

        Assert.Equal(ServerTrust.Fingerprint(renewed), OnDisk().ServerPin);
    }

    [Fact]
    public void A_pin_changed_in_this_process_is_what_its_save_writes()
    {
        var config = Enrolled(Cert(publiclyLogged: false), ServerCertTrust.Private);
        var repinned = Cert(publiclyLogged: false);
        config.SetPin(ServerTrust.Observe(repinned, SslPolicyErrors.None));
        config.Save();
        Assert.Equal(ServerTrust.Fingerprint(repinned), OnDisk().ServerPin);
    }

    [Fact]
    public void Nothing_is_judged_without_a_pin()
    {
        var config = Enrolled(Cert(publiclyLogged: true), null);
        config.ServerPin = null;
        Assert.Null(Handshake(config, Cert(publiclyLogged: false)));
        Assert.Null(config.ServerPin);
    }

    [Fact]
    public void The_warning_says_why_the_new_key_is_not_a_renewal()
    {
        var seen = new ObservedCert("new", ServerCertTrust.Private, SslPolicyErrors.None);
        Assert.Contains("private certificate", ServerTrust.MismatchWarning("old", ServerCertTrust.Private, seen));
        Assert.Contains("not publicly", ServerTrust.MismatchWarning("old", ServerCertTrust.Public, seen));
        Assert.Contains("fails validation", ServerTrust.MismatchWarning("old", ServerCertTrust.Public,
            seen with { Errors = SslPolicyErrors.RemoteCertificateChainErrors }));
        Assert.All(new[] { ServerCertTrust.Private, ServerCertTrust.Public }, trust =>
            Assert.Contains("TLS identity has CHANGED", ServerTrust.MismatchWarning("old", trust, seen)));
    }
}

/// <summary>Plain http has no identity: a real connection records nothing and judges nothing.</summary>
public sealed class ServerTrustHttpTests(ServerProcess server) : IClassFixture<ServerProcess>
{
    [Fact]
    public async Task Plain_http_stays_unpinned()
    {
        var config = AgentConfig.Load(Path.Combine(server.State, "agent-config-" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.True(config.TrySetServerUrl(server.Url, out _));
        Assert.False(ServerTrust.IsPinnable(config.ServerUrl));

        var api = ApiClient.For(config);
        Assert.NotNull(await api.ProbeAsync("/api/games"));

        Assert.Null(api.Observed);
        Assert.Null(config.ServerPin);
        Assert.Null(config.ServerPinTrust);
        Assert.StartsWith("plain http", ServerTrust.Describe(config));
    }
}
