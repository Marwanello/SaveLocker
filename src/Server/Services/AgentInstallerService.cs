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
/// Manages the agent installer binary stored on the server so agents can
/// self-update without requiring a separate CDN or GitHub release URL.
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
    private readonly string _root;
    private readonly string _githubRepo;
    private readonly long _maxBytes;
    private readonly ILogger<AgentInstallerService> _log;
    private const string InfoFileName = "installer-info.json";

    /// <summary>
    /// One writer at a time. The service is a singleton, so this genuinely covers the manual
    /// upload, the manual fetch, the poller and delete — the four callers that used to race.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public AgentInstallerService(IConfiguration cfg, ILogger<AgentInstallerService> log)
    {
        _log = log;
        _root = cfg["Storage:AgentInstallerRoot"]
            ?? Path.Combine(AppContext.BaseDirectory, "data", "agent-installer");
        _githubRepo = cfg["AgentUpdate:GitHubRepo"] ?? "SkorcherX/SaveLocker";
        _maxBytes = (long)(cfg.GetValue<int?>("AgentUpdate:MaxInstallerMb") ?? 200) * 1024 * 1024;
        Directory.CreateDirectory(_root);
    }

    public long MaxBytes => _maxBytes;

    public AgentInstallerStatus? GetInfo()
    {
        var path = Path.Combine(_root, InfoFileName);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<AgentInstallerStatus>(File.ReadAllText(path), _json); }
        catch { return null; }
    }

    public async Task<AgentInstallerStatus> SaveAsync(
        Stream content, string version, string fileName, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await SaveCoreAsync(content, version, fileName, ct); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Validate, stage, then publish. Nothing that exists is touched until the replacement is
    /// complete on disk, so a cancelled or oversized upload leaves the previous installer serving.
    /// Callers must already hold <see cref="_gate"/>.
    /// </summary>
    private async Task<AgentInstallerStatus> SaveCoreAsync(
        Stream content, string version, string fileName, CancellationToken ct)
    {
        // ---- Validate before touching anything ----
        var safeName = Path.GetFileName(fileName?.Trim() ?? "");
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InstallerRejectedException("The uploaded file has no usable filename.");
        if (!safeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InstallerRejectedException($"'{safeName}' is not a .exe — the agent installer must be one.");

        version = version?.Trim() ?? "";
        if (ParseVersion(version) is null)
            throw new InstallerRejectedException(
                $"'{version}' is not a version this server can compare (expected something like 0.4.2).");

        // ---- Stage ----
        var staged = Path.Combine(_root, $".incoming-{Guid.NewGuid():N}.part");
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
            // crash midway leaves a stray .exe, which is clutter; the reverse order leaves metadata
            // naming a file that does not exist, which is a broken update channel.
            var exePath = Path.Combine(_root, safeName);
            File.Move(staged, exePath, overwrite: true);

            var info = new AgentInstallerStatus(version, safeName, DateTime.UtcNow, written, digest);
            await WriteInfoAsync(info, ct);

            foreach (var old in Directory.GetFiles(_root, "*.exe"))
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
    private async Task WriteInfoAsync(AgentInstallerStatus info, CancellationToken ct)
    {
        var tmp = Path.Combine(_root, $".info-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(info), ct);
        File.Move(tmp, Path.Combine(_root, InfoFileName), overwrite: true);
    }

    public async Task DeleteAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var info = Path.Combine(_root, InfoFileName);
            if (File.Exists(info)) File.Delete(info);
            foreach (var f in Directory.GetFiles(_root, "*.exe")) TryDelete(f);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Give an installer stored before digests existed one, by hashing what is on disk. Called at
    /// startup.
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
            var info = GetInfo();
            if (info is null || !string.IsNullOrWhiteSpace(info.Sha256)) return;

            var path = Path.Combine(_root, info.FileName);
            if (!File.Exists(path)) return;

            await using var fs = File.OpenRead(path);
            var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
            await WriteInfoAsync(info with { Sha256 = digest }, ct);
            _log.LogInformation(
                "Computed a SHA-256 for the existing agent installer {File}; update verification is now active.",
                info.FileName);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not compute a digest for the stored agent installer.");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Remove staging files left by an upload whose process died. Called at startup.</summary>
    public int SweepIncoming(TimeSpan olderThan)
    {
        if (!Directory.Exists(_root)) return 0;
        var cutoff = DateTime.UtcNow - olderThan;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(_root, ".incoming-*.part")
                     .Concat(Directory.EnumerateFiles(_root, ".info-*.tmp")))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                File.Delete(file);
                removed++;
            }
            catch (Exception ex) { _log.LogWarning(ex, "Could not sweep stale installer file {Path}.", file); }
        }
        return removed;
    }

    /// <summary>
    /// Fetches the latest release's agent installer asset from the configured GitHub repo
    /// (<c>AgentUpdate:GitHubRepo</c>) and stores it as the hosted installer — automating the
    /// otherwise-manual download-from-GitHub-then-upload step. Throws if no matching asset exists.
    /// </summary>
    public async Task<AgentInstallerStatus> FetchLatestFromGitHubAsync(
        HttpClient http, CancellationToken ct, bool onlyIfNewer = false)
    {
        // The whole fetch is inside the gate, not just the write: the "is this newer?" decision is
        // read-then-write, and a manual upload landing between the two would be silently replaced.
        await _gate.WaitAsync(ct);
        try
        {
            using var meta = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{_githubRepo}/releases/latest");
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
                if (name.StartsWith("SaveLocker-Agent-Setup", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    assetName = name;
                    assetUrl = a.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
            if (assetUrl is null || assetName is null)
                throw new InvalidOperationException(
                    $"Latest release of {_githubRepo} has no SaveLocker-Agent-Setup-*.exe asset.");

            // The scheduled poll still needs to inspect release metadata, but should not
            // repeatedly download the same installer on every interval. Manual fetches
            // retain their original force-refresh behavior.
            var current = GetInfo();
            if (onlyIfNewer && current is not null && GetInstallerPath() is not null &&
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
            return await SaveCoreAsync(stream, version, assetName, ct);
        }
        finally { _gate.Release(); }
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

    /// <summary>Returns the on-disk path to the hosted installer, or null if none is present.</summary>
    public string? GetInstallerPath()
    {
        var info = GetInfo();
        if (info is null) return null;
        var path = Path.Combine(_root, info.FileName);
        return File.Exists(path) ? path : null;
    }
}
