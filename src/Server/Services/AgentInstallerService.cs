using System.Security.Cryptography;
using System.Text.Json;
using SaveLocker.Shared;

namespace SaveLocker.Server.Services;

/// <summary>Thrown when an installer exceeds the configured byte cap, counted while copying.</summary>
public sealed class InstallerTooLargeException(long limitBytes)
    : Exception($"Installer exceeds the {limitBytes / (1024 * 1024)} MB limit.")
{
    public long LimitBytes { get; } = limitBytes;
}

/// <summary>Thrown when an installer is refused before anything on disk is touched.</summary>
public sealed class InstallerRejectedException(string message) : Exception(message);

/// <summary>
/// Manages the agent packages stored on the server so agents can self-update without requiring a
/// separate CDN or GitHub release URL.
/// <para>
/// <b>One slot per platform</b> (<see cref="AgentPlatform"/>): the Windows installer keeps the
/// storage root it has always had, and every later platform gets a subdirectory named for it. That
/// asymmetry is deliberate — it means a server that has been running for months needs no migration
/// and its existing <c>installer-info.json</c> keeps being found exactly where it is.
/// </para>
/// <para>
/// Replacement is <b>staged then published</b>, under a gate shared by every writer — manual
/// upload, manual GitHub fetch, the background poller, and delete. It used to delete every existing
/// <c>.exe</c> before the new stream had produced a single byte, write straight to the final name,
/// and update metadata separately, with nothing serialising the four callers. A failed download or
/// two writers arriving together could leave no installer at all, or metadata describing a file
/// that was not there.
/// </para>
/// </summary>
public class AgentInstallerService
{
    private readonly long _maxBytes;
    private readonly ILogger<AgentInstallerService> _log;
    private const string InfoFileName = "installer-info.json";

    /// <summary>
    /// Where one platform's package lives and what counts as one.
    /// </summary>
    /// <param name="Extension">
    /// Matched with <c>EndsWith</c>, not <c>Path.GetExtension</c>: the Linux package is a
    /// <c>.tar.gz</c>, whose extension by that reckoning is <c>.gz</c>.
    /// </param>
    /// <param name="IsReleaseAsset">Recognises this platform's asset among a GitHub release's.</param>
    /// <param name="Repo">
    /// Which GitHub repository this slot's asset is released from. Per-slot rather than one field on
    /// the service, because the Decky plugin lives in its own repository — Decky's plugin database
    /// tracks plugins as submodules whose root is the plugin, so it could never be a subdirectory of
    /// SaveLocker's.
    /// </param>
    private sealed record Slot(
        string Platform,
        string Root,
        string Extension,
        Func<string, bool> IsReleaseAsset,
        string AssetPattern,
        string Repo);

    private readonly Dictionary<string, Slot> _slots;

    /// <summary>
    /// One writer at a time, across every platform. The service is a singleton, so this genuinely
    /// covers the manual upload, the manual fetch, the poller and delete — the four callers that
    /// used to race. It is deliberately not split per platform: the slots share a storage root and
    /// contend for almost nothing, and one gate is one thing to reason about.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AgentInstallerService(IConfiguration cfg, ILogger<AgentInstallerService> log)
    {
        _log = log;
        var root = cfg["Storage:AgentInstallerRoot"]
            ?? Path.Combine(AppContext.BaseDirectory, "data", "agent-installer");
        var agentRepo = cfg["AgentUpdate:GitHubRepo"] ?? "SkorcherX/SaveLocker";
        var pluginRepo = cfg["AgentUpdate:Plugin:GitHubRepo"] ?? "SkorcherX/SaveLocker-Decky";
        _maxBytes = (long)(cfg.GetValue<int?>("AgentUpdate:MaxInstallerMb") ?? 200) * 1024 * 1024;

        _slots = new Dictionary<string, Slot>(StringComparer.Ordinal)
        {
            [AgentPlatform.Windows] = new(
                AgentPlatform.Windows,
                root,
                ".exe",
                name => name.StartsWith("SaveLocker-Agent-Setup", StringComparison.OrdinalIgnoreCase)
                     && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
                "SaveLocker-Agent-Setup-*.exe",
                agentRepo),

            [AgentPlatform.Linux] = new(
                AgentPlatform.Linux,
                Path.Combine(root, AgentPlatform.Linux),
                ".tar.gz",
                name => name.StartsWith("savelocker-", StringComparison.OrdinalIgnoreCase)
                     && name.EndsWith("-linux-x64.tar.gz", StringComparison.OrdinalIgnoreCase),
                "savelocker-*-linux-x64.tar.gz",
                agentRepo),

            [AgentPlatform.DeckyPlugin] = new(
                AgentPlatform.DeckyPlugin,
                Path.Combine(root, AgentPlatform.DeckyPlugin),
                ".zip",
                name => name.StartsWith("SaveLocker", StringComparison.OrdinalIgnoreCase)
                     && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
                "SaveLocker*.zip",
                pluginRepo),
        };

        foreach (var slot in _slots.Values) Directory.CreateDirectory(slot.Root);
    }

    public long MaxBytes => _maxBytes;

    /// <summary>
    /// Resolves a platform to its slot. Unknown is an exception rather than a default, because the
    /// value names a directory — the routes validate it first, and this is the backstop.
    /// </summary>
    private Slot SlotFor(string platform) =>
        _slots.TryGetValue(platform, out var slot)
            ? slot
            : throw new InstallerRejectedException($"'{platform}' is not a platform this server hosts an agent for.");

    private static string InfoPath(Slot slot) => Path.Combine(slot.Root, InfoFileName);

    public AgentInstallerStatus? GetInfo(string platform = AgentPlatform.Windows)
    {
        var slot = SlotFor(platform);
        var path = InfoPath(slot);
        if (!File.Exists(path)) return null;
        try
        {
            var info = JsonSerializer.Deserialize<AgentInstallerStatus>(File.ReadAllText(path), _json);
            // Metadata written before slots existed carries no platform. It is in the Windows slot's
            // root because that is the only slot that could have written it, so say so.
            return info is null ? null : info with { Platform = slot.Platform };
        }
        catch { return null; }
    }

    public async Task<AgentInstallerStatus> SaveAsync(
        Stream content, string version, string fileName, CancellationToken ct,
        string platform = AgentPlatform.Windows)
    {
        var slot = SlotFor(platform);
        await _gate.WaitAsync(ct);
        try { return await SaveCoreAsync(content, version, fileName, slot, ct, source: "manual"); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Validate, stage, then publish. Nothing that exists is touched until the replacement is
    /// complete on disk, so a cancelled or oversized upload leaves the previous installer serving.
    /// Callers must already hold <see cref="_gate"/>.
    /// </summary>
    private async Task<AgentInstallerStatus> SaveCoreAsync(
        Stream content, string version, string fileName, Slot slot, CancellationToken ct, string source)
    {
        // ---- Validate before touching anything ----
        var safeName = Path.GetFileName(fileName?.Trim() ?? "");
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InstallerRejectedException("The uploaded file has no usable filename.");
        if (!safeName.EndsWith(slot.Extension, StringComparison.OrdinalIgnoreCase))
            throw new InstallerRejectedException(
                $"'{safeName}' is not a {slot.Extension} — the {AgentPlatform.Describe(slot.Platform)} " +
                "agent package must be one.");

        version = version?.Trim() ?? "";
        if (ParseVersion(version) is null)
            throw new InstallerRejectedException(
                $"'{version}' is not a version this server can compare (expected something like 0.4.2).");

        // ---- Stage ----
        var staged = Path.Combine(slot.Root, $".incoming-{Guid.NewGuid():N}.part");
        long written = 0;
        string digest;
        try
        {
            // Hashed while streaming, not by re-reading afterwards: re-reading would hash whatever
            // is on disk at that later moment, which is not necessarily what we just received.
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var fs = new FileStream(
                staged, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    written += read;
                    if (written > _maxBytes) throw new InstallerTooLargeException(_maxBytes);
                    sha.AppendData(buffer.AsSpan(0, read));
                    await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                await fs.FlushAsync(ct);
                fs.Flush(flushToDisk: true);
            }
            if (written == 0) throw new InstallerRejectedException("The uploaded installer is empty.");
            digest = Convert.ToHexStringLower(sha.GetHashAndReset());

            // ---- Publish ----
            // New file into place first, then metadata, and only then remove the old binaries. A
            // crash midway leaves a stray package, which is clutter; the reverse order leaves
            // metadata naming a file that is not there, which is a broken update channel.
            var exePath = Path.Combine(slot.Root, safeName);
            File.Move(staged, exePath, overwrite: true);

            var info = new AgentInstallerStatus(
                version, safeName, DateTime.UtcNow, written, digest, slot.Platform, source);
            await WriteInfoAsync(info, slot, ct);

            foreach (var old in Directory.GetFiles(slot.Root, "*" + slot.Extension))
            {
                if (string.Equals(Path.GetFileName(old), safeName, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(old); }
                catch (Exception ex) { _log.LogWarning(ex, "Could not remove superseded installer {Path}.", old); }
            }

            return info;
        }
        catch
        {
            TryDelete(staged);
            throw;
        }
    }

    /// <summary>Metadata is written aside and renamed, so a reader never sees a half-written file.</summary>
    private async Task WriteInfoAsync(AgentInstallerStatus info, Slot slot, CancellationToken ct)
    {
        var tmp = Path.Combine(slot.Root, $".info-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(info), ct);
        File.Move(tmp, InfoPath(slot), overwrite: true);
    }

    public async Task DeleteAsync(CancellationToken ct = default, string platform = AgentPlatform.Windows)
    {
        var slot = SlotFor(platform);
        await _gate.WaitAsync(ct);
        try
        {
            var info = InfoPath(slot);
            if (File.Exists(info)) File.Delete(info);
            foreach (var f in Directory.GetFiles(slot.Root, "*" + slot.Extension)) TryDelete(f);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Give an installer stored before digests existed one, by hashing what is on disk. Called at
    /// startup, for every platform slot.
    /// <para>
    /// Without this every already-deployed server would keep serving an installer with no digest
    /// until someone happened to upload a new one, and the agent would refuse to verify — so the
    /// fix would arrive for nobody who already had it working. It hashes the file the admin
    /// deliberately put there, which is the same trust decision that stored it in the first place;
    /// it is not a substitute for a digest computed at upload time, and later uploads get that.
    /// </para>
    /// </summary>
    public async Task BackfillDigestAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var slot in _slots.Values)
            {
                try
                {
                    var info = GetInfo(slot.Platform);
                    if (info is null || !string.IsNullOrWhiteSpace(info.Sha256)) continue;

                    var path = Path.Combine(slot.Root, info.FileName);
                    if (!File.Exists(path)) continue;

                    await using var fs = File.OpenRead(path);
                    var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
                    await WriteInfoAsync(info with { Sha256 = digest }, slot, ct);
                    _log.LogInformation(
                        "Computed a SHA-256 for the existing {Platform} agent package {File}; update verification is now active.",
                        slot.Platform, info.FileName);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Could not compute a digest for the stored {Platform} agent package.", slot.Platform);
                }
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Remove staging files left by an upload whose process died. Called at startup.</summary>
    public int SweepIncoming(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var removed = 0;
        foreach (var slot in _slots.Values)
        {
            if (!Directory.Exists(slot.Root)) continue;
            foreach (var file in Directory.EnumerateFiles(slot.Root, ".incoming-*.part")
                         .Concat(Directory.EnumerateFiles(slot.Root, ".info-*.tmp")))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                    File.Delete(file);
                    removed++;
                }
                catch (Exception ex) { _log.LogWarning(ex, "Could not sweep stale installer file {Path}.", file); }
            }
        }
        return removed;
    }

    /// <summary>
    /// Fetches the latest release's asset for <paramref name="platform"/> from the configured GitHub
    /// repo (<c>AgentUpdate:GitHubRepo</c>) and stores it as that platform's hosted package —
    /// automating the otherwise-manual download-from-GitHub-then-upload step.
    /// </summary>
    /// <exception cref="InstallerRejectedException">
    /// The latest release carries no asset for this platform. That is a 400, not a 500: a release
    /// predating the Linux tarball is a normal thing to encounter, and the poller checking both
    /// platforms every interval must be able to tell it apart from a real failure.
    /// </exception>
    public async Task<AgentInstallerStatus> FetchLatestFromGitHubAsync(
        HttpClient http, CancellationToken ct, bool onlyIfNewer = false,
        string platform = AgentPlatform.Windows)
    {
        var slot = SlotFor(platform);

        // The whole fetch is inside the gate, not just the write: the "is this newer?" decision is
        // read-then-write, and a manual upload landing between the two would be silently replaced.
        await _gate.WaitAsync(ct);
        try
        {
            using var meta = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{slot.Repo}/releases/latest");
            meta.Headers.UserAgent.ParseAdd("SaveLocker-Server");
            meta.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var metaResp = await http.SendAsync(meta, ct);
            metaResp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await metaResp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var version = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');

            string? assetName = null, assetUrl = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (slot.IsReleaseAsset(name))
                {
                    assetName = name;
                    assetUrl = a.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
            if (assetUrl is null || assetName is null)
                throw new InstallerRejectedException(
                    $"Latest release of {slot.Repo} has no {slot.AssetPattern} asset.");

            // The scheduled poll still needs to inspect release metadata, but should not
            // repeatedly download the same installer on every interval. Manual fetches
            // retain their original force-refresh behavior.
            var current = GetInfo(slot.Platform);
            if (onlyIfNewer && current is not null && GetInstallerPath(slot.Platform) is not null &&
                !IsNewerVersion(version, current.Version))
                return current;

            using var dl = new HttpRequestMessage(HttpMethod.Get, assetUrl);
            dl.Headers.UserAgent.ParseAdd("SaveLocker-Server");
            using var dlResp = await http.SendAsync(dl, HttpCompletionOption.ResponseHeadersRead, ct);
            dlResp.EnsureSuccessStatusCode();

            // A declared length over the cap is refused before a byte is downloaded; the copy
            // itself is still counted, because a remote server's Content-Length is a claim.
            if (dlResp.Content.Headers.ContentLength is { } declared && declared > _maxBytes)
                throw new InstallerTooLargeException(_maxBytes);

            await using var stream = await dlResp.Content.ReadAsStreamAsync(ct);
            return await SaveCoreAsync(stream, version, assetName, slot, ct, source: "github");
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Compares the hosted package's digest against a <c>SHA256SUMS*.txt</c> release asset from the
    /// same GitHub repo the package was (or could have been) fetched from — the same provenance a
    /// human doing a manual install is told to check by hand (release.yml's own comment). Not every
    /// release publishes one (the Decky plugin repo does not, as of this writing), so "no such file"
    /// is reported as <c>unknown</c>, not <c>mismatch</c> — silence is not evidence of tampering.
    /// </summary>
    public async Task<InstallerHashVerification> VerifyHashAsync(
        string platform, HttpClient http, CancellationToken ct)
    {
        var slot = SlotFor(platform);
        var info = GetInfo(slot.Platform);
        if (info?.Sha256 is null)
            return new(slot.Platform, "unknown", null, null,
                "No hosted package or digest to compare.");

        using var meta = new HttpRequestMessage(
            HttpMethod.Get, $"https://api.github.com/repos/{slot.Repo}/releases/latest");
        meta.Headers.UserAgent.ParseAdd("SaveLocker-Server");
        meta.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var metaResp = await http.SendAsync(meta, ct);
        if (!metaResp.IsSuccessStatusCode)
            return new(slot.Platform, "unknown", null, null, "Could not reach GitHub.");

        // A single release attaches BOTH platforms' checksum files (release.yml's build-linux job
        // adds its own asset to the SAME release the installer job created) — so this cannot stop
        // at the first name matching "SHA256SUMS*.txt". Every candidate is checked for a line
        // naming this platform's file, and the first that actually lists it wins.
        using var doc = JsonDocument.Parse(await metaResp.Content.ReadAsStringAsync(ct));
        var candidates = new List<(string Name, string Url)>();
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            if (name.StartsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var url = a.GetProperty("browser_download_url").GetString();
                if (url is not null) candidates.Add((name, url));
            }
        }
        if (candidates.Count == 0)
            return new(slot.Platform, "unknown", null, null,
                "The latest release of this repo publishes no SHA256SUMS file to check against.");

        string? published = null, matchedSumsName = null;
        var lastNonListingSumsName = candidates[0].Name;
        foreach (var (name, url) in candidates)
        {
            using var dl = new HttpRequestMessage(HttpMethod.Get, url);
            dl.Headers.UserAgent.ParseAdd("SaveLocker-Server");
            using var dlResp = await http.SendAsync(dl, ct);
            if (!dlResp.IsSuccessStatusCode) continue;

            var text = await dlResp.Content.ReadAsStringAsync(ct);
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                // `sha256sum` prefixes the filename with a mode marker (`*`/` `) on some platforms.
                if (parts.Length >= 2 && string.Equals(
                        parts[^1].TrimStart('*'), info.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    published = parts[0].ToLowerInvariant();
                    matchedSumsName = name;
                    break;
                }
            }
            if (published is not null) break;
            lastNonListingSumsName = name;
        }
        if (published is null)
            return new(slot.Platform, "unknown", null, lastNonListingSumsName,
                $"No SHA256SUMS file in the latest release lists {info.FileName} — it may be from a release older than the hosted package.");

        var matches = string.Equals(published, info.Sha256, StringComparison.OrdinalIgnoreCase);
        return new(slot.Platform, matches ? "match" : "mismatch", published, matchedSumsName, null);
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _log.LogWarning(ex, "Could not remove installer file {Path}.", path); }
    }

    private static bool IsNewerVersion(string candidate, string current)
    {
        var candidateVersion = ParseVersion(candidate);
        var currentVersion = ParseVersion(current);
        if (candidateVersion is not null && currentVersion is not null)
            return candidateVersion > currentVersion;

        // Release tags are expected to be semver-like. If either value is not,
        // only an exact match is considered current so a malformed new tag can
        // still be surfaced instead of silently ignored.
        return !string.Equals(
            candidate.Trim().TrimStart('v', 'V'),
            current.Trim().TrimStart('v', 'V'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static Version? ParseVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var suffix = normalized.IndexOfAny(['-', '+']);
        if (suffix >= 0) normalized = normalized[..suffix];
        return Version.TryParse(normalized, out var parsed) ? parsed : null;
    }

    /// <summary>Returns the on-disk path to a platform's hosted package, or null if none is present.</summary>
    public string? GetInstallerPath(string platform = AgentPlatform.Windows)
    {
        var slot = SlotFor(platform);
        var info = GetInfo(slot.Platform);
        if (info is null) return null;
        var path = Path.Combine(slot.Root, info.FileName);
        return File.Exists(path) ? path : null;
    }
}
