using System.Diagnostics;
using System.IO.Compression;
using SaveLocker.Shared;
using YamlDotNet.Serialization;

namespace SaveLocker.Agent;

/// <summary>What a Playnite plugin update check concluded. Only <see cref="Updated"/> wrote anything.
/// A Windows-local mirror of <c>Agent.Linux.PluginUpdateState</c> — that type lives in the Linux-only
/// project and cannot be referenced from here.</summary>
public enum PluginUpdateState
{
    /// <summary>Playnite is not installed on this machine. Not a fault, not worth a word anywhere.</summary>
    NoPlaynite,
    /// <summary>Playnite is here but the SaveLocker plugin is not. <see cref="CheckAsync"/>'s own
    /// background timer never installs one on its own initiative — that stays true after Phase 19 —
    /// but <see cref="InstallFirstTimeAsync"/> now lets an explicit, user-clicked button do it.</summary>
    NotInstalled,
    /// <summary>The installed plugin is current, or the server offers none.</summary>
    UpToDate,
    /// <summary>A newer plugin exists and was deliberately not applied (a check, AutoUpdate off, or
    /// Playnite currently running).</summary>
    Available,
    /// <summary>The plugin's files were replaced. Playnite needs a restart to pick them up.</summary>
    Updated,
    /// <summary>Refused before anything was written — see <see cref="PluginUpdateOutcome.Message"/>.</summary>
    Refused,
    /// <summary>The check itself did not complete (unreachable server, unreadable manifest).</summary>
    Failed,
}

public sealed record PluginUpdateOutcome(
    PluginUpdateState State, string Message, string? InstalledVersion = null, string? LatestVersion = null);

/// <summary>
/// Keeps the SaveLocker Playnite plugin up to date, through the channel the agent already uses for
/// its own updates — the Windows analogue of <c>Agent.Linux/DeckyPlugin.cs</c>
/// (tasks/playnite-plugin/plan.md, Phase 7).
///
/// <para>
/// <b>Why the agent does this at all.</b> Before this plugin is listed in Playnite's official add-on
/// database (Phase 16 — a third party's review timeline), nothing checks a player's installed copy
/// for updates on its own. The agent is already talking to a server that hosts packages, so it does
/// the checking and the installing instead, exactly as it does for the Decky plugin on Linux.
/// </para>
///
/// <para>
/// <b>Genuinely simpler than the Decky plugin.</b> Decky chowns its plugin directory to the desktop
/// user but keeps the top level and <c>plugin.json</c> root-owned, forcing a plan-before-write pass
/// that skips whatever it cannot safely touch. <c>%AppData%\Playnite\Extensions\&lt;id&gt;\</c> is
/// entirely user-owned — every file, including the manifest — so nothing here needs an ownership
/// carve-out. The one thing that constraint's absence does NOT remove: Playnite's own SDK confirms a
/// compiled <c>GenericPlugin</c> extension never hot-reloads, so replacing files while Playnite is
/// running would leave it running old code next to new files (or fail outright — a loaded .NET
/// Framework assembly's file is typically locked by its own host process). <see cref="IsPlayniteRunning"/>
/// is the guard for that: an update while Playnite is open is reported as merely <see
/// cref="PluginUpdateState.Available"/>, not applied, with a message asking the player to close it.
/// </para>
/// </summary>
public static class PlaynitePlugin
{
    /// <summary>The plugin's directory name under Playnite's Extensions folder.</summary>
    public const string PluginName = "SaveLocker";

    /// <summary>Where a user pastes to install it the first time — the one step this cannot do.</summary>
    public const string InstallUrl =
        "https://github.com/Marwanello/SaveLocker-Playnite/releases/latest/download/SaveLocker.zip";

    /// <summary>A package with more entries than this is refused before anything is unpacked.</summary>
    private const int MaxEntries = 5_000;

    /// <summary>…and more expanded bytes than this. The plugin is a few hundred KB.</summary>
    private const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Playnite's own per-user data root — <c>%AppData%\Playnite</c> for a normal (non-portable)
    /// install, distinct from <c>%LocalAppData%\Playnite</c> (where the application binaries
    /// themselves install). Extensions, library data and settings all live here for a normal install
    /// — but NOT "regardless of which mode Playnite runs in," as this comment used to claim: a
    /// portable extraction keeps all of it (Extensions included) beside its own executable instead
    /// (confirmed 2026-09-17 via <c>tests/testenv.ps1</c>'s own portable test instance, and Playnite's
    /// public docs). <c>SAVELOCKER_PLAYNITE_PATH</c> — the same override <see cref="PlayniteLibrary"/>
    /// honors, and the one <c>testenv.ps1 -PlaynitePath</c> sets automatically — points this at that
    /// portable root instead, so a first-install/update test against the disposable portable instance
    /// can never accidentally write into a real, personal Playnite.
    /// </summary>
    public static string PlayniteDataRoot =>
        Environment.GetEnvironmentVariable("SAVELOCKER_PLAYNITE_PATH") is { Length: > 0 } portableRoot
            ? portableRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite");

    public static string ExtensionsRoot => Path.Combine(PlayniteDataRoot, "Extensions");
    public static string PluginDir => Path.Combine(ExtensionsRoot, PluginName);

    /// <summary>Is Playnite on this machine at all?</summary>
    public static bool PlaynitePresent => Directory.Exists(PlayniteDataRoot);

    /// <summary>Is our plugin installed? Judged by the manifest Playnite itself reads to load it.</summary>
    public static bool Installed => File.Exists(ManifestPath);

    private static string ManifestPath => Path.Combine(PluginDir, "extension.yaml");

    /// <summary>
    /// Playnite running with this extension potentially loaded, in either of its two process shapes
    /// (Desktop or Fullscreen mode). Checked before ever writing a file: replacing a compiled
    /// extension's assembly while its host process has it loaded is exactly the half-written-plugin
    /// outcome <see cref="InstallAsync"/> exists to prevent, and — unlike Decky's live-reloading
    /// watchdog — nothing here would recover from that by itself.
    /// </summary>
    public static bool IsPlayniteRunning =>
        Process.GetProcessesByName("Playnite.DesktopApp").Length > 0 ||
        Process.GetProcessesByName("Playnite.FullscreenApp").Length > 0;

    /// <summary>
    /// The installed plugin's version, read from the same <c>extension.yaml</c> Playnite itself reads
    /// to load it — so what the agent compares and what Playnite reports cannot disagree. Tolerant of
    /// whatever else the manifest carries (Id, Name, Author, Module, …); only <c>Version</c> matters
    /// here.
    /// </summary>
    public static string? InstalledVersion()
    {
        try
        {
            // Dictionary<string, object>, not <string, string>: IgnoreUnmatchedProperties only
            // applies to POCO targets, so a Dictionary target already tolerates any key — but a
            // <string, string> target still throws if any OTHER field's value is a YAML mapping or
            // sequence rather than a scalar. Only Version is ever read, so only it needs to be a string.
            var deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
            var doc = deserializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(ManifestPath));
            return doc is not null && doc.TryGetValue("Version", out var v) && v is string s && !string.IsNullOrWhiteSpace(s)
                ? s.Trim()
                : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// GET /api/playnite-plugin's own answer (tasks/playnite-plugin/plan.md, Phase 14) — the Playnite
    /// plugin process itself calling in to ask "is a newer version of me waiting?" Always a
    /// check-only <see cref="CheckAsync"/> (apply: false): a route must never let an external caller
    /// trigger a write, and the plugin cannot safely replace its own currently-loaded assembly anyway
    /// — only the agent's own recurring timer (<c>TrayApp.CheckPlaynitePluginUpdateAsync</c>) installs
    /// anything. Called from inside a running Playnite process, so <see cref="CheckAsync"/>'s own
    /// <see cref="IsPlayniteRunning"/> guard always takes its "close Playnite first" branch when a
    /// newer version genuinely exists — <see cref="PluginUpdateOutcome.Message"/> already reads
    /// exactly like the restart notice Phase 14 wants, with nothing extra to compose here.
    /// </summary>
    public static async Task<PlaynitePluginStatusDto> StatusAsync(AgentConfig config, Action<string> log)
    {
        var outcome = await CheckAsync(config, log, apply: false);
        return new PlaynitePluginStatusDto(
            outcome.State.ToString(), outcome.Message, outcome.InstalledVersion, outcome.LatestVersion);
    }

    /// <summary>
    /// GET /api/playnite-plugin/status's own answer (tasks/playnite-plugin/plan.md, Phase 19) — the
    /// agent-ui suggest/install card's data source, shaped like Agent.Core's <c>DeckyStatusDto</c> on
    /// purpose: same three-state card pattern (not present / present without the plugin / installed),
    /// different launcher. Unlike <see cref="StatusAsync"/> above (the plugin's OWN self-check, called
    /// from inside a running Playnite process, which stops at "not installed" because Phase 14 has
    /// nothing to offer there), this always tries to learn the latest available version too — a card
    /// that suggests installing the plugin in the first place has nowhere else to get one.
    /// </summary>
    public static async Task<PlaynitePluginCardStatusDto> CardStatusAsync(AgentConfig config)
    {
        if (!PlaynitePresent) return new(false, false, null, null, InstallUrl);

        var installed = Installed ? InstalledVersion() : null;
        string? latest = null;
        if (!string.IsNullOrEmpty(config.ApiKey))
        {
            try
            {
                using var checker = new UpdateChecker(config);
                var info = await checker.FetchLatestAsync(AgentPlatform.PlaynitePlugin);
                latest = info?.LatestVersion;
            }
            catch { /* best effort — a card that can't reach the server just omits the version */ }
        }

        return new(true, Installed, installed, latest, InstallUrl);
    }

    /// <summary>
    /// POST /api/playnite-plugin/install's own answer (Phase 19) — an explicit, user-clicked first
    /// install onto a machine that has Playnite but not yet this plugin. This reverses this file's own
    /// earlier default (see <see cref="PluginUpdateState.NotInstalled"/>'s doc comment, "the first
    /// install isn't ours to do"): unlike Decky, which root-owns its plugin directory's top level,
    /// <see cref="PluginDir"/> carries no ownership constraint that makes a first-time write
    /// technically impossible, so the only thing stopping it before now was policy, not capability —
    /// mirroring Decky's UX regardless of that difference. What stays intentional is the consent
    /// boundary: this only ever runs from an explicit button click (see <c>PlaynitePluginCard.tsx</c>),
    /// never from the background timer that drives an ordinary update.
    /// </summary>
    public static async Task<PlaynitePluginStatusDto> InstallFirstTimeAsync(AgentConfig config, Action<string> log)
    {
        var outcome = await InstallFirstTimeCoreAsync(config, log);
        return new PlaynitePluginStatusDto(
            outcome.State.ToString(), outcome.Message, outcome.InstalledVersion, outcome.LatestVersion);
    }

    private static async Task<PluginUpdateOutcome> InstallFirstTimeCoreAsync(AgentConfig config, Action<string> log)
    {
        try
        {
            if (!PlaynitePresent)
                return new(PluginUpdateState.NoPlaynite, "Playnite is not installed on this machine.");

            if (Installed)
            {
                var current = InstalledVersion();
                return new(PluginUpdateState.UpToDate,
                    $"the plugin is already installed (v{current}).", current);
            }

            if (string.IsNullOrEmpty(config.ApiKey))
                return new(PluginUpdateState.Failed,
                    "This machine is not registered, so there is no server to ask.");

            using var checker = new UpdateChecker(config);
            var info = await checker.FetchLatestAsync(AgentPlatform.PlaynitePlugin);
            if (info is null || string.IsNullOrWhiteSpace(info.LatestVersion))
                return new(PluginUpdateState.Failed, "the server hosts no plugin package to install.");

            if (IsPlayniteRunning)
                return new(PluginUpdateState.Refused,
                    "Close Playnite first — a first install cannot be applied while it is running.");

            return await InstallAsync(config, checker, info, installed: null, log);
        }
        catch (Exception ex)
        {
            return new(PluginUpdateState.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Ask the server what plugin it is offering and, when <paramref name="apply"/> is true, install
    /// it. Never throws: this runs on a timer nobody is necessarily watching, and a plugin that could
    /// not be updated must not become a reason the agent stops doing anything else.
    /// </summary>
    public static async Task<PluginUpdateOutcome> CheckAsync(
        AgentConfig config, Action<string> log, bool apply)
    {
        try
        {
            if (!PlaynitePresent)
                return new(PluginUpdateState.NoPlaynite, "Playnite is not installed on this machine.");

            if (!Installed)
                return new(PluginUpdateState.NotInstalled,
                    $"Playnite is installed but the {PluginName} plugin is not. Install it once from a " +
                    $"downloaded .pext, or from Playnite's Add-ons browser once listed: {InstallUrl}");

            var installed = InstalledVersion();
            if (installed is null)
                return new(PluginUpdateState.Failed,
                    $"The plugin's manifest at {ManifestPath} could not be read, so its version is " +
                    "unknown. Reinstall the plugin.");

            if (string.IsNullOrEmpty(config.ApiKey))
                return new(PluginUpdateState.Failed,
                    "This machine is not registered, so there is no server to ask.", installed);

            using var checker = new UpdateChecker(config);
            var info = await checker.FetchLatestAsync(AgentPlatform.PlaynitePlugin);
            if (info is null || string.IsNullOrWhiteSpace(info.LatestVersion))
                return new(PluginUpdateState.UpToDate,
                    $"v{installed} installed; the server hosts no plugin package.", installed);

            if (!IsNewer(info.LatestVersion, installed))
                return new(PluginUpdateState.UpToDate,
                    $"up to date (v{installed})", installed, info.LatestVersion);

            if (IsPlayniteRunning)
                return new(PluginUpdateState.Available,
                    $"v{info.LatestVersion} available (installed v{installed}) — close Playnite first, " +
                    "an update cannot be applied while it is running.", installed, info.LatestVersion);

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
    /// Download, verify, unpack to a staging directory, and only then write. Unlike Decky's plan-
    /// before-write pass, nothing under <see cref="PluginDir"/> is root-owned or otherwise off-limits
    /// — the whole directory is replaced to match the package exactly, including files the old version
    /// had that the new one doesn't. <paramref name="installed"/> is null for a genuine first install
    /// (<see cref="InstallFirstTimeCoreAsync"/>) — there is no prior version to name in that case.
    /// </summary>
    private static async Task<PluginUpdateOutcome> InstallAsync(
        AgentConfig config, UpdateChecker checker, AgentVersionInfo info, string? installed, Action<string> log)
    {
        var staging = Path.Combine(config.StateDir, "playnite-plugin-update");
        DeleteDirectory(staging);
        Directory.CreateDirectory(staging);

        string zip;
        try
        {
            log($"playnite plugin: downloading v{info.LatestVersion}…");
            zip = await checker.DownloadInstallerAsync(
                info.LatestVersion, info.DownloadUrl, info.Sha256, kind: PackageKind.PlaynitePlugin);
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

            var newFiles = Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories)
                .Select(src => (Rel: Path.GetRelativePath(payload, src), Src: src))
                .ToList();
            if (newFiles.Count == 0)
                return Fail(config, info.LatestVersion, installed,
                    "the package contains nothing to install.");

            // One last check right before writing: Playnite starting during the download/extract
            // above must not still let a write proceed into a now-loaded assembly.
            if (IsPlayniteRunning)
                return Fail(config, info.LatestVersion, installed,
                    "Playnite was started during the update — close it and check again. Nothing was " +
                    "changed.");

            foreach (var (rel, src) in newFiles)
            {
                var dst = Path.Combine(PluginDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
            }

            // The whole directory is ours, unlike Decky's dist/-only prune — remove anything the old
            // version left behind that the new package no longer carries.
            var keep = newFiles.Select(f => f.Rel).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pruned = 0;
            if (Directory.Exists(PluginDir))
            {
                foreach (var existing in Directory.EnumerateFiles(PluginDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(PluginDir, existing);
                    if (keep.Contains(rel)) continue;
                    try { File.Delete(existing); pruned++; } catch { /* stays; harmless */ }
                }
            }

            var fromClause = installed is null ? "" : $", was v{installed}";
            log($"playnite plugin: v{info.LatestVersion} installed ({newFiles.Count} files" +
                (pruned > 0 ? $", {pruned} removed" : "") + $"){fromClause}. Restart Playnite to " +
                "finish " + (installed is null ? "installing." : "updating."));

            Report(config, AgentEventCodes.PluginUpdated, AgentEventSeverity.Info,
                installed is null
                    ? $"The SaveLocker Playnite plugin was installed on this machine (v{info.LatestVersion}). " +
                      "Restart Playnite to finish installing."
                    : $"The SaveLocker Playnite plugin on this machine was updated from v{installed} to " +
                      $"v{info.LatestVersion}. Restart Playnite to finish updating.");

            return new(PluginUpdateState.Updated,
                installed is null
                    ? $"installed v{info.LatestVersion}"
                    : $"updated v{installed} → v{info.LatestVersion}",
                info.LatestVersion, info.LatestVersion);
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
    /// Unpack the zip as hostile input — it becomes code running inside Playnite's own process, so it
    /// gets the same treatment as the agent's own tarball and the Decky plugin's zip: entry and byte
    /// caps, and every destination must resolve inside the staging directory.
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
                throw new InvalidOperationException(
                    $"the package has more than {MaxEntries:N0} entries. Refusing to unpack it.");

            var dst = Path.GetFullPath(Path.Combine(stagingFull, entry.FullName));
            if (!dst.StartsWith(stagingFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"package entry '{entry.FullName}' resolves outside the staging directory. " +
                    "Refusing to unpack it.");

            if (entry.FullName.EndsWith('/') || entry.Name.Length == 0)
            {
                Directory.CreateDirectory(dst);
                continue;
            }

            written += entry.Length;
            if (written > MaxBytes)
                throw new InvalidOperationException(
                    $"the package expands past {MaxBytes / (1024 * 1024)} MB. Refusing to continue.");

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            entry.ExtractToFile(dst, overwrite: true);
        }

        if (entries == 0) throw new InvalidOperationException("the package is empty. Refusing to unpack it.");
    }

    /// <summary>A release zip wrapping everything in a single top-level directory unwraps cleanly too;
    /// detected rather than assumed, so a flat zip works either way.</summary>
    private static string ResolvePayloadRoot(string stagedDir)
    {
        var dirs = Directory.GetDirectories(stagedDir);
        var files = Directory.GetFiles(stagedDir);
        return files.Length == 0 && dirs.Length == 1 ? dirs[0] : stagedDir;
    }

    private static PluginUpdateOutcome Fail(
        AgentConfig config, string latest, string? installed, string reason)
    {
        var stateClause = installed is null ? "still not installed" : $"still v{installed}";
        Report(config, AgentEventCodes.PluginUpdateFailed, AgentEventSeverity.Warning,
            $"The SaveLocker Playnite plugin on this machine is {stateClause}: v{latest} was not " +
            $"installed because {reason}");
        return new(PluginUpdateState.Refused, reason, installed, latest);
    }

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
