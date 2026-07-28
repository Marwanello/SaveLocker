using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// Checks the SaveLocker server for a newer agent version and, when requested,
/// downloads the installer and launches it. All network I/O runs off the UI thread;
/// this class never touches WinForms directly.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    private readonly AgentConfig _config;

    // Shared across all checks in one tray session; avoids creating a new socket pool each time.
    private readonly HttpClient _http;

    /// <summary>
    /// The running agent's version.
    /// <para>
    /// <b>The Windows path is the FileVersion resource</b> on the exe: MinVer overrides
    /// AssemblyVersion to 0.0.0.0 when git is inaccessible on CI runners, but the command-line
    /// <c>Version</c> property reliably stamps FileVersion. For single-file self-contained exes
    /// <c>Assembly.Location</c> is empty, so it reads <see cref="Environment.ProcessPath"/>.
    /// </para>
    /// <para>
    /// <b>That yields NOTHING on Linux.</b> The published <c>savelocker</c> is a native ELF apphost,
    /// which has no Win32 version resource — <c>FileVersionInfo.FileVersion</c> is null, and the agent
    /// used to silently fall back to the hard-coded default. Every Deck therefore reported "0.1.0" to
    /// the console forever, no matter which build it was running (the heartbeat sends this). So fall
    /// back to the managed <c>AssemblyFileVersion</c> attribute, which is the same value the Windows
    /// resource is generated from and is readable on every platform.
    /// </para>
    /// </summary>
    public static readonly Version CurrentVersion = ResolveVersion();

    private static Version ResolveVersion()
    {
        // Windows: the PE version resource (proven; leave it first so nothing about Windows changes).
        if (Version.TryParse(FileVersionInfo.GetVersionInfo(Environment.ProcessPath ?? "").FileVersion, out var fromResource))
            return fromResource;

        // Linux (and any host without a version resource): the managed attribute.
        var attr = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
        if (Version.TryParse(attr, out var fromAttribute))
            return fromAttribute;

        return new Version(0, 1, 0);
    }

    /// <summary>The origin this checker was built for. A connection change retires it (see TrayApp).</summary>
    public string ServerUrl { get; }

    /// <summary>Refuse an installer larger than this. The server caps uploads at 200 MB by default.</summary>
    private const long MaxInstallerBytes = 300L * 1024 * 1024;

    public UpdateChecker(AgentConfig config)
    {
        _config = config;
        ServerUrl = config.ServerUrl;

        // Built through the same TLS/pin policy as every other request the agent makes. It used to
        // construct a bare HttpClient, so the update channel — the one that ends in executing a
        // binary — was the single path that ignored the TOFU pin entirely. WA-05.
        _http = ServerHttp.Create(config, withApiKey: true);
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Queries the server for the latest available agent version.
    /// Returns one of: <see cref="UpdateResult.UpToDate"/>, <see cref="UpdateResult.Available"/>,
    /// <see cref="UpdateResult.Skipped"/>, or <see cref="UpdateResult.Failed"/>.
    /// </summary>
    public async Task<UpdateResult> CheckAsync()
    {
        try
        {
            var resp = await _http.GetAsync("/api/agent/latest");

            if (resp.StatusCode == HttpStatusCode.NoContent)
                return new UpdateResult.UpToDate();

            resp.EnsureSuccessStatusCode();
            var info = await resp.Content.ReadFromJsonAsync<AgentVersionInfo>();
            if (info is null || string.IsNullOrWhiteSpace(info.LatestVersion))
                return new UpdateResult.UpToDate();

            if (!Version.TryParse(info.LatestVersion, out var latest))
                return new UpdateResult.Failed($"Server returned unparseable version: {info.LatestVersion}");

            if (latest <= CurrentVersion)
                return new UpdateResult.UpToDate();

            if (!string.IsNullOrEmpty(_config.SkipVersion) &&
                Version.TryParse(_config.SkipVersion, out var skip) && skip == latest)
                return new UpdateResult.Skipped();

            return new UpdateResult.Available(info.LatestVersion, info.DownloadUrl, info.Sha256);
        }
        catch (Exception ex)
        {
            return new UpdateResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Download the installer and prove it is the artifact the configured server authorised, or
    /// throw. Nothing is left on disk on any failure path.
    ///
    /// <para>
    /// Everything here is one defence, because the transport is not one. SaveLocker ships no
    /// certificates and plain http is the default (Decisions.md), so a downloaded executable cannot
    /// be trusted because of <i>how</i> it arrived — only because of what it hashes to.
    /// </para>
    /// </summary>
    /// <param name="expectedSha256">
    /// The digest from the update metadata. Required for a download that leaves the configured
    /// server's origin; optional (with a warning) for the server's own installer endpoint, so a
    /// server that predates digests still updates rather than becoming permanently unupdatable.
    /// </param>
    public async Task<string> DownloadInstallerAsync(
        string version, string downloadUrl, string? expectedSha256 = null, IProgress<int>? progress = null)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(
                $"The server offered an update at '{downloadUrl}', which is not a usable http(s) URL.");

        // Is this our own server, or somewhere else? An off-origin URL is legitimate (an admin can
        // point AgentUpdate:DownloadUrl at a GitHub release) but it is a different trust situation:
        // it gets no credential, no pin, and no benefit of the doubt about integrity.
        var sameOrigin = ServerOrigin.Same(url.GetLeftPart(UriPartial.Authority), _config.ServerUrl);

        if (!sameOrigin && string.IsNullOrWhiteSpace(expectedSha256))
            throw new InvalidOperationException(
                $"Refusing to download an update from {url.Host}: it is not this SaveLocker server, " +
                "and the server supplied no SHA-256 to verify it against. Host the installer on the " +
                "server itself, or set AgentUpdate:Sha256 alongside AgentUpdate:DownloadUrl.");

        if (string.IsNullOrWhiteSpace(expectedSha256))
            AgentLogger.Log(
                "WARNING: the server supplied no SHA-256 for this update, so the downloaded " +
                "installer cannot be verified before it runs. Re-upload the installer in the console " +
                "to record one.");

        // The machine key is a credential for OUR server. The old code reused one client whose
        // DEFAULT HEADERS carried it, so an arbitrary DownloadUrl received this machine's API key
        // simply for being named in the server's response. WA-05.
        var http = sameOrigin ? _http : ServerHttp.CreateForeign();
        try
        {
            // Unique per attempt and created exclusively: the old fixed name in %TEMP% was
            // predictable and world-writable, so another local user could pre-place or swap the file
            // between download and launch.
            var dest = Path.Combine(Path.GetTempPath(), $"SaveLockerSetup-{version}-{Guid.NewGuid():N}.exe");
            try
            {
                var actual = await StreamToFileAsync(http, url, dest, progress);

                if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                    !string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "The downloaded installer does not match the checksum the server published. " +
                        "It was NOT run and has been deleted.\n" +
                        $"  expected: {expectedSha256.Trim().ToLowerInvariant()}\n" +
                        $"  actual:   {actual}");

                VerifyLooksExecutable(dest);
                return dest;
            }
            catch
            {
                // Deleted on EVERY failure, not just the recognised ones. A rejected installer left
                // in %TEMP% is an executable that failed verification sitting next to one that
                // passed, distinguishable only by a GUID.
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                throw;
            }
        }
        finally
        {
            if (!sameOrigin) http.Dispose();
        }
    }

    /// <summary>Stream to disk under a hard size cap, returning the SHA-256 of what was written.</summary>
    private static async Task<string> StreamToFileAsync(
        HttpClient http, Uri url, string dest, IProgress<int>? progress)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();

        // A declared length over the cap is refused before a byte is transferred; the copy is still
        // counted, because Content-Length is a claim by whoever is answering.
        if (resp.Content.Headers.ContentLength is { } declared && declared > MaxInstallerBytes)
            throw new InvalidOperationException(
                $"The update is {declared / (1024 * 1024)} MB, over the " +
                $"{MaxInstallerBytes / (1024 * 1024)} MB limit. Nothing was downloaded.");

        var total = resp.Content.Headers.ContentLength;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using var src = await resp.Content.ReadAsStreamAsync();
        // CreateNew, not Create: this must fail rather than overwrite anything already at the path.
        await using var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        var buf = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await src.ReadAsync(buf)) > 0)
        {
            downloaded += read;
            if (downloaded > MaxInstallerBytes)
                throw new InvalidOperationException(
                    $"The update exceeded the {MaxInstallerBytes / (1024 * 1024)} MB limit while downloading.");

            sha.AppendData(buf.AsSpan(0, read));
            await fs.WriteAsync(buf.AsMemory(0, read));
            if (progress is not null && total > 0)
                progress.Report((int)(downloaded * 100 / total));
        }

        if (downloaded == 0) throw new InvalidOperationException("The server returned an empty update.");
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    /// <summary>
    /// Reject anything that is not a Windows executable. Without this the most likely wrong payload
    /// — a captive-portal login page or a proxy's HTML error page served with 200 — reaches
    /// <c>Process.Start</c>, which hands it to the shell.
    /// <para>
    /// This is a sanity check, not a security control: the digest is what proves authenticity. It
    /// is here because it produces a comprehensible error instead of a baffling one.
    /// </para>
    /// <para>
    /// TODO: verify the Authenticode signature here once releases are signed. That is the one check
    /// that survives an attacker who can rewrite both the artifact and the digest, because it does
    /// not depend on the channel they were delivered over. Code signing is a separate decision with
    /// a real cost; see Decisions.md.
    /// </para>
    /// </summary>
    public void Dispose() => _http.Dispose();

    private static void VerifyLooksExecutable(string path)
    {
        using var fs = File.OpenRead(path);
        var header = new byte[2];
        if (fs.Read(header) != 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
            throw new InvalidOperationException(
                "The downloaded update is not a Windows executable — the server or a proxy in the " +
                "way returned something else. It was NOT run.");
    }
}

/// <summary>Discriminated union result returned by <see cref="UpdateChecker.CheckAsync"/>.</summary>
public abstract record UpdateResult
{
    public sealed record UpToDate : UpdateResult;
    /// <param name="Sha256">Digest the download must match. Null if the server published none.</param>
    public sealed record Available(string Version, string DownloadUrl, string? Sha256 = null) : UpdateResult;
    public sealed record Skipped : UpdateResult;
    public sealed record Failed(string Reason) : UpdateResult;
}
