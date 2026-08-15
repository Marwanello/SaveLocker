using System.IO.Compression;
using System.Text.Json;
using SaveLocker.Shared;

namespace SaveLocker.Agent.Linux;

/// <summary>What a plugin update check concluded. Only <see cref="Updated"/> wrote anything.</summary>
public enum PluginUpdateState
{
    /// <summary>Decky Loader is not on this machine. Not a fault, and not worth a word anywhere.</summary>
    NoDecky,
    /// <summary>Decky is here but SaveLocker's plugin is not. The first install is not ours to do.</summary>
    NotInstalled,
    /// <summary>The installed plugin is current, or the server offers none.</summary>
    UpToDate,
    /// <summary>A newer plugin exists and was deliberately not applied (a check, or AutoUpdate off).</summary>
    Available,
    /// <summary>The plugin's files were replaced. Decky hot-reloads it within about a second.</summary>
    Updated,
    /// <summary>Refused before anything was written — see <see cref="PluginUpdateOutcome.Message"/>.</summary>
    Refused,
    /// <summary>The check itself did not complete (unreachable server, unreadable package.json).</summary>
    Failed,
}

public sealed record PluginUpdateOutcome(
    PluginUpdateState State, string Message, string? InstalledVersion = null, string? LatestVersion = null);

/// <summary>
/// Keeps the SaveLocker Decky plugin up to date, through the channel the agent already uses for its
/// own updates.
///
/// <para>
/// <b>Why the agent does this at all.</b> Decky holds exactly one store URL, so the only way to get
/// update prompts for a plugin outside the official store is to point the store at ours — which
/// replaces the official store while it is set, and nobody leaves it there. In practice that means
/// nobody is ever told an update exists. The agent is already talking to a server that hosts
/// packages, so it does the telling and the installing instead.
/// </para>
///
/// <para>
/// <b>Why writing the files IS the install.</b> Decky chowns a non-<c>_root</c> plugin's directory
/// contents to the desktop user, and its loader runs a watchdog observer over the plugins directory
/// with live reload on by default — so replacing the files makes Decky unload and reload the plugin
/// in about a second. No <c>systemctl</c>, no sudo, no Steam restart. Hot reload does require the
/// <c>debug</c> flag in <c>plugin.json</c>; an installation predating that flag cannot self-update
/// and needs one manual reinstall.
/// </para>
///
/// <para>
/// <b>The constraint everything here is shaped around.</b> The top-level plugin directory itself
/// stays root-owned at 755, so the agent can overwrite files that exist but cannot create new
/// top-level ones — and <c>plugin.json</c> is chowned to root even for a non-<c>_root</c> plugin, so
/// it can never be rewritten at all. Every destination is therefore checked <i>before</i> a single
/// byte is written, and a package that needs something unreachable is refused whole. A half-written
/// plugin is worse than an old one.
/// </para>
/// </summary>
public static class DeckyPlugin
{
    /// <summary>The plugin's directory name, which is also its name in Decky.</summary>
    public const string PluginName = "SaveLocker";

    /// <summary>Where a user pastes to install it the first time — the one step this cannot do.</summary>
    public const string InstallUrl =
        "https://github.com/SkorcherX/SaveLocker-Decky/releases/latest/download/SaveLocker.zip";

    /// <summary>A package with more entries than this is refused before anything is unpacked.</summary>
    private const int MaxEntries = 5_000;

    /// <summary>…and more expanded bytes than this. The plugin is a few hundred KB.</summary>
    private const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The one file the agent must never write, however a package is put together. Decky chowns it to
    /// the effective (root) user, so an attempt would fail — but it is skipped by name rather than
    /// discovered by failing, because failing halfway is the outcome this class exists to prevent.
    /// </summary>
    private const string RootOwnedFile = "plugin.json";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string PluginsRoot => Path.Combine(Home, "homebrew", "plugins");
    public static string PluginDir => Path.Combine(PluginsRoot, PluginName);

    /// <summary>Is Decky Loader on this machine at all?</summary>
    public static bool DeckyPresent => Directory.Exists(PluginsRoot);

    /// <summary>Is our plugin installed? Judged by the file Decky itself reads the version from.</summary>
    public static bool Installed => File.Exists(PackageJsonPath);

    private static string PackageJsonPath => Path.Combine(PluginDir, "package.json");

    /// <summary>
    /// The installed plugin's version, read from the same <c>package.json</c> Decky reports in its own
    /// UI — so what the agent compares and what the user sees cannot disagree.
    /// </summary>
    public static string? InstalledVersion()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(PackageJsonPath));
            var value = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch { return null; }
    }

    /// <summary>
    /// Ask the server what plugin it is offering and, when <paramref name="apply"/> is true, install
    /// it. Never throws: this runs on a timer on a device nobody is watching, and a plugin that could
    /// not be updated must not become a reason the agent stops doing anything else.
    /// </summary>
    public static async Task<PluginUpdateOutcome> CheckAsync(
        AgentConfig config, Action<string> log, bool apply)
    {
        try
        {
            if (!DeckyPresent)
                return new(PluginUpdateState.NoDecky, "Decky Loader is not installed on this machine.");

            if (!Installed)
                return new(PluginUpdateState.NotInstalled,
                    $"Decky Loader is installed but the {PluginName} plugin is not. Install it once " +
                    $"from Decky → Install Plugin from URL: {InstallUrl}");

            var installed = InstalledVersion();
            if (installed is null)
                return new(PluginUpdateState.Failed,
                    $"The plugin's package.json at {PackageJsonPath} could not be read, so its version " +
                    "is unknown. Reinstall the plugin through Decky.");

            if (string.IsNullOrEmpty(config.ApiKey))
                return new(PluginUpdateState.Failed,
                    "This machine is not registered, so there is no server to ask.", installed);

            using var checker = new UpdateChecker(config);
            var info = await checker.FetchLatestAsync(AgentPlatform.DeckyPlugin);
            if (info is null || string.IsNullOrWhiteSpace(info.LatestVersion))
                return new(PluginUpdateState.UpToDate,
                    $"v{installed} installed; the server hosts no plugin package.", installed);

            if (!IsNewer(info.LatestVersion, installed))
                return new(PluginUpdateState.UpToDate,
                    $"up to date (v{installed})", installed, info.LatestVersion);

            if (!apply)
                return new(PluginUpdateState.Available,
                    $"v{info.LatestVersion} available (installed v{installed})", installed, info.LatestVersion);

            return await InstallAsync(config, checker, info, installed, log);
        }
        catch (Exception ex)
        {
            return new(PluginUpdateState.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Download, verify, unpack to a staging directory, prove every destination is reachable, and
    /// only then write. The plan-before-write split is the whole point: the alternative discovers a
    /// path it cannot create partway through, having already replaced the code Decky is running.
    /// </summary>
    private static async Task<PluginUpdateOutcome> InstallAsync(
        AgentConfig config, UpdateChecker checker, AgentVersionInfo info, string installed, Action<string> log)
    {
        var staging = Path.Combine(config.StateDir, "plugin-update");
        DeleteDirectory(staging);
        Directory.CreateDirectory(staging);

        string zip;
        try
        {
            log($"plugin: downloading v{info.LatestVersion}…");
            zip = await checker.DownloadInstallerAsync(
                info.LatestVersion, info.DownloadUrl, info.Sha256, kind: PackageKind.DeckyPlugin);
        }
        catch (Exception ex)
        {
            DeleteDirectory(staging);
            return Fail(config, info.LatestVersion, installed,
                $"the package could not be downloaded or verified. {ex.Message}");
        }

        try
        {
            Extract(zip, staging);
            var payload = ResolvePayloadRoot(staging);

            var plan = new List<(string Rel, string Src, string Dst)>();
            var blocked = new List<string>();
            var skipped = 0;

            foreach (var src in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(payload, src);
                if (rel.Equals(RootOwnedFile, StringComparison.Ordinal)) { skipped++; continue; }

                var dst = Path.Combine(PluginDir, rel);
                if (!CanReplace(dst)) blocked.Add(rel);
                else plan.Add((rel, src, dst));
            }

            if (blocked.Count > 0)
                return Fail(config, info.LatestVersion, installed,
                    $"it needs {string.Join(", ", blocked.Take(5))}" +
                    (blocked.Count > 5 ? $" and {blocked.Count - 5} more" : "") +
                    $", which the agent may not write — the plugin directory itself belongs to root. " +
                    $"Nothing was changed. Reinstall the plugin once from Decky → Install Plugin from " +
                    $"URL ({InstallUrl}) and it will keep itself updated again.");

            if (plan.Count == 0)
                return Fail(config, info.LatestVersion, installed,
                    "the package contains nothing the agent can install.");

            // package.json last, on purpose. Every write trips Decky's watcher, so the plugin may
            // reload partway through — better that it reloads new code still reporting the old
            // version for a second than that it reports the new version while running the old code.
            foreach (var (_, src, dst) in plan.OrderBy(
                         p => p.Rel.Equals("package.json", StringComparison.Ordinal) ? 1 : 0))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }

            var pruned = PruneStaleDistFiles(payload);

            var note = skipped > 0 ? " plugin.json belongs to root and was left as it is." : "";
            log($"plugin: v{info.LatestVersion} installed ({plan.Count} files" +
                (pruned > 0 ? $", {pruned} removed" : "") + $"), was v{installed}.{note}");

            Report(config, AgentEventCodes.PluginUpdated, AgentEventSeverity.Info,
                $"The SaveLocker Decky plugin on this machine was updated from v{installed} to " +
                $"v{info.LatestVersion}.");

            return new(PluginUpdateState.Updated,
                $"updated v{installed} → v{info.LatestVersion}", info.LatestVersion, info.LatestVersion);
        }
        catch (Exception ex)
        {
            return Fail(config, info.LatestVersion, installed, ex.Message);
        }
        finally
        {
            TryDelete(zip);
            DeleteDirectory(staging);
        }
    }

    /// <summary>
    /// Remove files under <c>dist/</c> that the new package does not carry. That directory is
    /// entirely the plugin's and is user-owned, so it can be made to match; nothing outside it is
    /// touched, because anything else might be a file some other version or the user put there.
    /// </summary>
    private static int PruneStaleDistFiles(string payload)
    {
        var installedDist = Path.Combine(PluginDir, "dist");
        var packagedDist = Path.Combine(payload, "dist");
        if (!Directory.Exists(installedDist) || !Directory.Exists(packagedDist)) return 0;

        var keep = Directory.EnumerateFiles(packagedDist, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(packagedDist, f))
            .ToHashSet(StringComparer.Ordinal);

        var removed = 0;
        foreach (var f in Directory.EnumerateFiles(installedDist, "*", SearchOption.AllDirectories))
        {
            if (keep.Contains(Path.GetRelativePath(installedDist, f))) continue;
            try { File.Delete(f); removed++; } catch { /* it stays; harmless */ }
        }
        return removed;
    }

    /// <summary>
    /// Can this exact destination be written? An existing file has to be writable; a new one needs a
    /// writable home, which is what refuses a new <b>top-level</b> file — the plugin directory is
    /// root-owned 755, while <c>dist/</c> below it belongs to the desktop user.
    /// <para>
    /// Probed for real rather than read off the mode bits: SteamOS mounts things read-only, and the
    /// answer that matters is whether a write succeeds.
    /// </para>
    /// </summary>
    private static bool CanReplace(string dst)
    {
        try
        {
            if (File.Exists(dst))
            {
                using var fs = new FileStream(dst, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                return true;
            }

            var dir = Path.GetDirectoryName(dst)!;
            while (!Directory.Exists(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(parent) || parent == dir) return false;
                dir = parent;
            }

            var probe = Path.Combine(dir, $".savelocker-plugin-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Unpack the zip as hostile input. It becomes code running inside Steam's own UI, so it gets the
    /// same treatment as the agent's own tarball: entry and byte caps, and every destination must
    /// resolve inside the staging directory.
    /// </summary>
    private static void Extract(string zipPath, string stagingDir)
    {
        var stagingFull = Path.GetFullPath(stagingDir);
        long written = 0;
        var entries = 0;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (++entries > MaxEntries)
                throw new UpdateRefusedException(
                    $"the package has more than {MaxEntries:N0} entries. Refusing to unpack it.");

            var dst = Path.GetFullPath(Path.Combine(stagingFull, entry.FullName));
            if (!dst.StartsWith(stagingFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new UpdateRefusedException(
                    $"package entry '{entry.FullName}' resolves outside the staging directory. " +
                    "Refusing to unpack it.");

            if (entry.FullName.EndsWith('/') || entry.Name.Length == 0)
            {
                Directory.CreateDirectory(dst);
                continue;
            }

            written += entry.Length;
            if (written > MaxBytes)
                throw new UpdateRefusedException(
                    $"the package expands past {MaxBytes / (1024 * 1024)} MB. Refusing to continue.");

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            entry.ExtractToFile(dst, overwrite: true);
        }

        if (entries == 0) throw new UpdateRefusedException("the package is empty. Refusing to unpack it.");
    }

    /// <summary>
    /// Decky's release zip wraps everything in a single top-level directory named for the plugin.
    /// Detected rather than assumed, so a flat zip works too.
    /// </summary>
    private static string ResolvePayloadRoot(string stagedDir)
    {
        var dirs = Directory.GetDirectories(stagedDir);
        var files = Directory.GetFiles(stagedDir);
        return files.Length == 0 && dirs.Length == 1 ? dirs[0] : stagedDir;
    }

    private static PluginUpdateOutcome Fail(
        AgentConfig config, string latest, string installed, string reason)
    {
        Report(config, AgentEventCodes.PluginUpdateFailed, AgentEventSeverity.Warning,
            $"The SaveLocker Decky plugin on this machine is still v{installed}: v{latest} was not " +
            $"installed because {reason}");
        return new(PluginUpdateState.Refused, reason, installed, latest);
    }

    /// <summary>Put an event where the console will see it — on a Deck that is the only place anyone would.</summary>
    private static void Report(AgentConfig config, string code, AgentEventSeverity severity, string message)
    {
        try { HealthReporter.For(config).Report(code, severity, message); }
        catch { /* reporting a problem must never become one */ }
    }

    /// <summary>
    /// Strictly newer, so a server hosting an older package can never drag a device backwards. An
    /// unparseable version on either side is treated as "not newer": doing nothing is the safe
    /// reading of a value neither side can compare.
    /// </summary>
    private static bool IsNewer(string candidate, string current) =>
        Parse(candidate) is { } a && Parse(current) is { } b && a > b;

    private static Version? Parse(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var suffix = normalized.IndexOfAny(['-', '+']);
        if (suffix >= 0) normalized = normalized[..suffix];
        return Version.TryParse(normalized, out var parsed) ? parsed : null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
