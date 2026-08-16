using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using SaveLocker.Shared;

namespace SaveLocker.Agent.Linux;

/// <summary>Thrown when a staged update is refused. Nothing has been applied when this is raised.</summary>
public sealed class UpdateRefusedException(string message) : Exception(message);

/// <summary>
/// The Linux agent's self-update: stage now, apply at the next start.
///
/// <para>
/// <b>Why it is split in two.</b> Replacing the agent's files is not something a running daemon can
/// do to itself. <c>systemctl --user stop</c> kills every process in the unit's cgroup, so an
/// updater the daemon spawned dies together with the unit it just stopped — halfway through the
/// swap. So the daemon only ever <i>stages</i>: it downloads, verifies, unpacks and smoke-tests into
/// its own state directory and stops there. The swap runs from <c>ExecStartPre</c> of the *next*
/// invocation (<c>savelocker apply-update</c>), which is a fresh process in the new cgroup, with the
/// old daemon already gone.
/// </para>
///
/// <para>
/// <b>Why it copies file-by-file instead of swapping a directory.</b> On Linux the install prefix
/// and the agent's state directory are the same place (<c>~/.local/share/SaveLocker</c>), so
/// <c>config.json</c> — this machine's server API key — lives inside the tree an update replaces.
/// Renaming the tree away would take the enrollment with it. Only paths the tarball actually carries
/// are touched; everything else is left exactly where it is.
/// </para>
///
/// <para>
/// <b>Why the old files are moved rather than deleted.</b> A rename inside the same directory is
/// free, it gives the replacement a NEW inode (so a process still executing the old file keeps
/// running from the one it mapped — the same reason <c>install.sh</c> uses
/// <c>cp --remove-destination</c>), and it leaves a complete previous version on disk to roll back
/// to. The rollback is the point: an agent that cannot start after an update, on a headless device
/// nobody is watching, is the worst outcome this feature can produce.
/// </para>
/// </summary>
public static class Updater
{
    /// <summary>A tarball with more entries than this is refused before anything is written.</summary>
    private const int MaxEntries = 20_000;

    /// <summary>…and more expanded bytes than this. The published agent is ~90 MB.</summary>
    private const long MaxBytes = 500L * 1024 * 1024;

    // ── Layout, all under the agent's state directory ──────────────────────────────────────────
    private static string UpdateRoot(AgentConfig c) => Path.Combine(c.StateDir, "update");
    private static string StagedDir(AgentConfig c) => Path.Combine(UpdateRoot(c), "staged");
    private static string PreviousDir(AgentConfig c) => Path.Combine(UpdateRoot(c), "previous");
    private static string StagedMarker(AgentConfig c) => Path.Combine(UpdateRoot(c), "apply.json");
    private static string AppliedMarker(AgentConfig c) => Path.Combine(UpdateRoot(c), "applied.json");

    /// <summary>Written by <see cref="StageAsync"/>; read by <see cref="Apply"/>.</summary>
    private sealed record StagedPayload(string Version, string PayloadDir, DateTime StagedAt);

    /// <summary>
    /// Written by <see cref="Apply"/> and cleared by <see cref="Commit"/>. Its survival into the
    /// next start is the signal that the version we installed did not manage to run.
    /// </summary>
    /// <param name="Added">
    /// Paths the new version introduced, which by definition have no copy in <c>previous/</c>.
    /// Recorded so a rollback can remove them: restoring only what was replaced would leave the
    /// failed version's new files scattered through the install of the version we just went back to.
    /// </param>
    private sealed record AppliedUpdate(
        string Version, string FromVersion, DateTime AppliedAt, string[] Added);

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    /// <summary>
    /// Where the agent's own files live. <see cref="AppContext.BaseDirectory"/>, never
    /// <see cref="Environment.ProcessPath"/>: under <c>dotnet savelocker.dll</c> the process path is
    /// the <c>dotnet</c> host, and an update that "installed itself" over a .NET installation would
    /// be a spectacular way to break a machine.
    /// </summary>
    public static string InstallPrefix => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>Is there a staged update waiting to be applied at the next start?</summary>
    public static string? PendingVersion(AgentConfig config) => ReadStaged(config)?.Version;

    /// <summary>
    /// The staged update, and whether restarting right now would actually install it.
    /// <para>
    /// This is what <c>/api/agent-version</c> publishes, and the reason it exists separately from
    /// the update <i>check</i>: "the server is offering v0.5.8" and "v0.5.8 is on this disk, verified
    /// and smoke-tested" are different states, and only the second can be acted on offline, quickly,
    /// and without a way to fail. Anything offering to install now must read this one.
    /// </para>
    /// </summary>
    public static StagedUpdateInfo? StagedUpdate(AgentConfig config) =>
        ReadStaged(config) is { } staged
            ? new StagedUpdateInfo(staged.Version, BlockedReason(config))
            : null;

    /// <summary>
    /// Why a restart would install nothing, phrased for a user, or null when it would work.
    /// <para>
    /// <see cref="Apply"/> defers a staged update while a game is running under the launch wrapper,
    /// so a restart in that state succeeds and changes nothing — which from a button looks exactly
    /// like a bug. The sentence is built here rather than by each surface so the Game Mode UI, the
    /// agent UI and the Decky plugin cannot word the same condition three different ways.
    /// </para>
    /// </summary>
    private static string? BlockedReason(AgentConfig config) =>
        RunningGame(config) is { } game
            ? $"{game} is running — the update will install when you close it."
            : null;

    /// <summary>
    /// How to make a staged update happen now, in words that are true on <b>this</b> device.
    /// <para>
    /// The honest answer is not "restart SaveLocker". The swap runs from the unit's
    /// <c>ExecStartPre</c>, so what has to cycle is the <c>savelocker.service</c> systemd
    /// <c>--user</c> unit — which nothing on a Game Mode screen says, so the user guesses. A reboot
    /// always does it. Switching to Desktop mode and back only does it when lingering is off (see
    /// <see cref="SystemdAutoStart.LingerEnabled"/>), which is why this is probed rather than
    /// promised.
    /// </para>
    /// <para>
    /// "Restart Steam" is named because it is the control that is actually on screen and the first
    /// thing anyone reaches for, and it is not it: that restarts Steam's own processes, not a
    /// systemd user unit. Saying so costs a clause and saves the one wrong guess everybody makes.
    /// </para>
    /// </summary>
    public static string ApplyInstruction() =>
        SystemdAutoStart.LingerEnabled()
            ? "Restart your device to install it. Restarting Steam will not."
            : "Restart your device to install it, or switch to Desktop mode and back. " +
              "Restarting Steam will not.";

    // ── Stage ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Download, verify, unpack and smoke-test <paramref name="update"/> into the state directory.
    /// Applies nothing. Returns the staged version.
    /// <para>
    /// The smoke test is the load-bearing step: it runs the staged binary and requires it to report
    /// the version we expect. A tarball that unpacked perfectly but cannot execute — wrong
    /// architecture, a glibc too new, a truncated payload that still hashed correctly because the
    /// server hashed the same truncation — is otherwise indistinguishable from a good one right up
    /// until the moment the agent is replaced and never comes back.
    /// </para>
    /// </summary>
    public static async Task<string> StageAsync(
        AgentConfig config, UpdateResult.Available update, Action<string> log)
    {
        var root = UpdateRoot(config);
        Directory.CreateDirectory(root);
        SetPrivate(root);

        // Any previous attempt is rubble: it was either applied (and committed) or abandoned.
        DeleteDirectory(StagedDir(config));
        TryDelete(StagedMarker(config));

        log($"update: downloading v{update.Version}…");
        using var checker = new UpdateChecker(config);
        var tarball = await checker.DownloadInstallerAsync(update.Version, update.DownloadUrl, update.Sha256);

        try
        {
            var staged = StagedDir(config);
            Directory.CreateDirectory(staged);
            Extract(tarball, staged);

            var payload = ResolvePayloadRoot(staged);
            var binary = Path.Combine(payload, "savelocker");
            if (!File.Exists(binary))
                throw new UpdateRefusedException(
                    "The downloaded package contains no 'savelocker' binary. Nothing was staged.");
            File.SetUnixFileMode(binary,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            SmokeTest(binary, update.Version);

            WriteJson(StagedMarker(config), new StagedPayload(update.Version, payload, DateTime.UtcNow));
            log($"update: v{update.Version} staged and verified — it will be applied the next time " +
                "the agent starts.");
            Report(config, AgentEventCodes.UpdateStaged, AgentEventSeverity.Info,
                $"SaveLocker v{update.Version} is downloaded and verified, and will be installed the " +
                $"next time this machine's agent starts (currently running v{UpdateChecker.CurrentVersionText}).");
            return update.Version;
        }
        catch (Exception ex)
        {
            DeleteDirectory(StagedDir(config));
            TryDelete(StagedMarker(config));
            // The machine keeps working, so nothing else will ever look wrong — it just stops
            // updating, quietly and forever. On a device with no UI that is invisible without this.
            Report(config, AgentEventCodes.UpdateFailed, AgentEventSeverity.Warning,
                $"SaveLocker v{update.Version} could not be prepared, so this machine is still on " +
                $"v{UpdateChecker.CurrentVersionText}. Nothing was replaced. {ex.Message}");
            throw;
        }
        finally
        {
            TryDelete(tarball);
        }
    }

    /// <summary>
    /// Unpack the tarball, treating it as hostile input — it becomes the code this machine runs, so
    /// it gets the same treatment as a pulled save archive and then some. Entry counts and expanded
    /// bytes are capped, every destination must resolve inside the staging directory, and links are
    /// refused outright rather than resolved: the published tarball contains none, so a link in one
    /// is either a corrupt build or someone trying to write outside this directory.
    /// </summary>
    private static void Extract(string tarballPath, string stagingDir)
    {
        var stagingFull = Path.GetFullPath(stagingDir);
        long written = 0;
        var entries = 0;

        using var file = File.OpenRead(tarballPath);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        while (tar.GetNextEntry() is { } entry)
        {
            if (++entries > MaxEntries)
                throw new UpdateRefusedException(
                    $"The package has more than {MaxEntries:N0} entries. Refusing to unpack it.");

            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                throw new UpdateRefusedException(
                    $"The package contains a link ('{entry.Name}'). The published agent tarball has " +
                    "none, so this is either a corrupt build or an attempt to write outside the " +
                    "staging directory. Refusing to unpack it.");

            var dst = Path.GetFullPath(Path.Combine(stagingFull, entry.Name));
            if (!dst.StartsWith(stagingFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new UpdateRefusedException(
                    $"Package entry '{entry.Name}' resolves outside the staging directory. " +
                    "Refusing to unpack it.");

            if (entry.EntryType is TarEntryType.Directory)
            {
                Directory.CreateDirectory(dst);
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                continue;   // devices, fifos and the like: nothing the agent ships, nothing we write

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            using var src = entry.DataStream ?? Stream.Null;
            using var outFile = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            int read;
            while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                written += read;
                if (written > MaxBytes)
                    throw new UpdateRefusedException(
                        $"The package expanded past {MaxBytes / (1024 * 1024)} MB. Refusing to continue.");
                outFile.Write(buffer, 0, read);
            }
            outFile.Dispose();

            // The execute bit is the only mode that matters here, and tar carries it.
            if ((entry.Mode & UnixFileMode.UserExecute) != 0)
                File.SetUnixFileMode(dst, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        if (entries == 0)
            throw new UpdateRefusedException("The package is empty. Refusing to unpack it.");
    }

    /// <summary>
    /// The tarball wraps everything in a single top-level directory (<c>SaveLocker/</c>). Detected
    /// rather than assumed, so a differently-rolled package still works.
    /// </summary>
    private static string ResolvePayloadRoot(string stagedDir)
    {
        var dirs = Directory.GetDirectories(stagedDir);
        var files = Directory.GetFiles(stagedDir);
        return files.Length == 0 && dirs.Length == 1 ? dirs[0] : stagedDir;
    }

    /// <summary>
    /// Run the staged binary and require it to say what it is. Anything else — a non-zero exit, no
    /// output, the wrong version, or not starting at all — means it does not get to replace a
    /// working agent.
    /// </summary>
    private static void SmokeTest(string binary, string expectedVersion)
    {
        string output;
        int exitCode;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(binary)
            {
                ArgumentList = { "version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }) ?? throw new UpdateRefusedException("The staged agent could not be started at all.");

            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30_000))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                throw new UpdateRefusedException(
                    "The staged agent did not exit when asked for its version. Refusing to install it.");
            }
            exitCode = p.ExitCode;
            output = (stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult()).Trim();
        }
        catch (UpdateRefusedException) { throw; }
        catch (Exception ex)
        {
            throw new UpdateRefusedException(
                $"The staged agent could not be run ({ex.Message}). This is what an update for the " +
                "wrong architecture, or built against a newer glibc than this device has, looks " +
                "like. Refusing to install it.");
        }

        if (exitCode != 0)
            throw new UpdateRefusedException(
                $"The staged agent exited {exitCode} when asked for its version. Refusing to install it." +
                (output.Length > 0 ? $"\n  it said: {output}" : ""));

        // Compared on Major.Minor.Patch: the server's version string and the binary's stamped
        // AssemblyFileVersion are produced by different parts of the build and need only agree on
        // what they mean, not on how many components they print.
        if (!SameVersion(output, expectedVersion))
            throw new UpdateRefusedException(
                $"The staged agent reports version '{output}', but the server offered " +
                $"'{expectedVersion}'. Refusing to install a package that is not what it claimed.");
    }

    private static bool SameVersion(string a, string b) =>
        Version.TryParse(a.Trim(), out var va) && Version.TryParse(b.Trim(), out var vb) &&
        va.Major == vb.Major &&
        Math.Max(va.Minor, 0) == Math.Max(vb.Minor, 0) &&
        Math.Max(va.Build, 0) == Math.Max(vb.Build, 0);

    // ── Apply, roll back, commit ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>ExecStartPre</c> hook. Exactly one of three things happens, and usually it is nothing:
    /// <list type="number">
    /// <item>an apply from last time never reached a working agent → roll it back;</item>
    /// <item>a staged update is waiting → install it;</item>
    /// <item>neither → return immediately.</item>
    /// </list>
    /// It must never prevent the agent starting: every failure is reported and swallowed, because a
    /// broken update is a problem and a device that will not start at all is a much bigger one.
    /// </summary>
    public static int Apply(AgentConfig config, Action<string> log, bool force = false)
    {
        try
        {
            if (ReadApplied(config) is { } unconfirmed)
            {
                log($"update: v{unconfirmed.Version} was installed but never started successfully. " +
                    $"Rolling back to v{unconfirmed.FromVersion}.");
                RollBack(config, unconfirmed, log);
                return 0;
            }

            if (ReadStaged(config) is not { } staged) return 0;

            if (!force && RunningGame(config) is { } game)
            {
                log($"update: {game} is running, so v{staged.Version} stays staged. " +
                    "It will be applied the next time the agent starts.");
                return 0;
            }

            InstallStaged(config, staged, log);
            return 0;
        }
        catch (Exception ex)
        {
            // Reported loudly and never fatal. The agent that is about to start is either the old
            // one (nothing was swapped) or a rolled-back one; both work.
            log($"update: FAILED to apply — {ex.Message}");
            return 0;
        }
    }

    private static void InstallStaged(AgentConfig config, StagedPayload staged, Action<string> log)
    {
        var payload = staged.PayloadDir;
        if (!Directory.Exists(payload))
            throw new UpdateRefusedException(
                $"The staged update at {payload} is gone. Nothing was changed.");

        var prefix = InstallPrefix;
        var previous = PreviousDir(config);
        DeleteDirectory(previous);
        Directory.CreateDirectory(previous);

        var from = UpdateChecker.CurrentVersionText;
        var moved = 0;
        var copied = 0;
        var added = new List<string>();

        foreach (var src in Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(payload, src);
            var dst = Path.Combine(prefix, rel);

            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            // Move the incumbent aside rather than overwriting it. The replacement then lands on a
            // NEW inode, so anything still executing the old file — a `savelocker run` wrapper
            // supervising a game right now — keeps running from the copy it already mapped instead
            // of taking a SIGBUS partway through.
            if (File.Exists(dst))
            {
                var keep = Path.Combine(previous, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(keep)!);
                File.Move(dst, keep);
                moved++;
            }
            else added.Add(rel);

            File.Copy(src, dst);
            CopyExecutableBit(src, dst);
            copied++;
        }

        WriteJson(AppliedMarker(config),
            new AppliedUpdate(staged.Version, from, DateTime.UtcNow, [.. added]));
        TryDelete(StagedMarker(config));
        DeleteDirectory(StagedDir(config));

        log($"update: v{staged.Version} installed ({copied} files, {moved} replaced). " +
            $"The previous version is kept until it starts successfully.");
    }

    /// <summary>
    /// Put back everything <see cref="InstallStaged"/> moved aside. Only files that were genuinely
    /// replaced are in there, so this restores the old version without touching state.
    /// </summary>
    private static void RollBack(AgentConfig config, AppliedUpdate applied, Action<string> log)
    {
        var previous = PreviousDir(config);
        var prefix = InstallPrefix;
        var restored = 0;

        // Files the failed version introduced have nothing to restore over them, so they have to be
        // removed by name or they stay behind in an install that has otherwise gone back a version.
        foreach (var rel in applied.Added ?? [])
        {
            try
            {
                var stray = Path.Combine(prefix, rel);
                if (File.Exists(stray)) File.Delete(stray);
            }
            catch (Exception ex) { log($"update: could not remove {rel} — {ex.Message}"); }
        }

        if (Directory.Exists(previous))
        {
            foreach (var src in Directory.EnumerateFiles(previous, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(previous, src);
                var dst = Path.Combine(prefix, rel);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    if (File.Exists(dst)) File.Delete(dst);   // unlink: the replacement gets no say
                    File.Move(src, dst);
                    restored++;
                }
                catch (Exception ex)
                {
                    log($"update: could not restore {rel} — {ex.Message}");
                }
            }
        }

        // Cleared whatever happened. A rollback that half-worked must not be retried on every start;
        // the agent says so once and the user is left with a machine that runs.
        TryDelete(AppliedMarker(config));
        DeleteDirectory(previous);
        log($"update: rolled back ({restored} files restored).");

        // The machine now syncs perfectly on the old version, so nothing else reports a problem —
        // but a build that cannot start on this device is one an admin should hear about before it
        // reaches the rest of the fleet.
        Report(config, AgentEventCodes.UpdateRolledBack, AgentEventSeverity.Error,
            $"SaveLocker v{applied.Version} was installed on this machine and did not start. " +
            $"v{applied.FromVersion} has been restored and the agent is running normally. " +
            "That version should not be rolled out further until this is understood.");
    }

    /// <summary>
    /// Called once the agent is genuinely up. This is what turns "installed" into "kept": until it
    /// runs, the next start treats the applied version as suspect and reverts it.
    /// </summary>
    public static void Commit(AgentConfig config, Action<string> log)
    {
        try
        {
            if (ReadApplied(config) is not { } applied) return;
            TryDelete(AppliedMarker(config));
            DeleteDirectory(PreviousDir(config));
            log($"update: v{applied.Version} started successfully (was v{applied.FromVersion}).");
        }
        catch (Exception ex)
        {
            log($"update: could not confirm the applied version — {ex.Message}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The name of a game currently being wrapped by <c>savelocker run</c>, or null.
    /// <para>
    /// Read from <c>/proc</c> rather than from a lease or a pidfile: the wrapper holds no local lock
    /// while the game is actually running, and this has to work on a device that is offline and
    /// mid-boot. Its own process is skipped, and so is the daemon — only <c>run</c> means a game.
    /// </para>
    /// <para>
    /// <paramref name="config"/> is what turns a pid into a name. Steam hands the wrapper
    /// <c>SteamAppId</c> in its environment, which is the same key everything else matches games on,
    /// so the tracked game is one lookup away — and "Khazan is running" is an answer a user can act
    /// on, where "pid 4131 is running" is one they cannot. Falls back to the generic phrase whenever
    /// the environment cannot be read or names nothing tracked, which is not a failure: the only
    /// thing the caller needs is whether a game is there.
    /// </para>
    /// </summary>
    private static string? RunningGame(AgentConfig? config = null)
    {
        var self = Environment.ProcessId;
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid) || pid == self) continue;
            try
            {
                var raw = File.ReadAllText(Path.Combine(dir, "cmdline"));
                var argv = raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (argv.Length >= 2 &&
                    Path.GetFileName(argv[0]).Equals("savelocker", StringComparison.Ordinal) &&
                    argv[1].Equals("run", StringComparison.Ordinal))
                    return TrackedGameOf(dir, config) ?? "A game";
            }
            catch { /* the process exited, or is not ours to read */ }
        }
        return null;
    }

    /// <summary>
    /// Which tracked game a live wrapper process is playing, from its own environment, or null.
    /// Readable because the wrapper runs as the same user as everything else here.
    /// </summary>
    private static string? TrackedGameOf(string procDir, AgentConfig? config)
    {
        if (config is null) return null;
        try
        {
            var appId = File.ReadAllText(Path.Combine(procDir, "environ"))
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(v => v.StartsWith("SteamAppId=", StringComparison.Ordinal))?["SteamAppId=".Length..];
            if (SteamShortcuts.UnsignedAppId(appId) is not { } wanted) return null;

            return config.Games.FirstOrDefault(
                g => SteamShortcuts.UnsignedAppId(g.ResolveSteamAppId()) == wanted)?.Name;
        }
        catch { return null; }
    }

    /// <summary>
    /// Put an event where the console will see it. Durable by design: <c>apply-update</c> runs from
    /// <c>ExecStartPre</c> and exits long before anything could send a heartbeat, so the event is
    /// written to the shared events file and the daemon starting immediately afterwards drains it.
    /// That is the same path the launch wrapper already uses (Decisions.md §2 — the console is the
    /// Deck's UI), and it is why a rollback that happened at boot is still reportable.
    /// </summary>
    private static void Report(AgentConfig config, string code, AgentEventSeverity severity, string message)
    {
        try { HealthReporter.For(config).Report(code, severity, message); }
        catch { /* reporting a problem must never become one */ }
    }

    private static void CopyExecutableBit(string src, string dst)
    {
        try
        {
            var mode = File.GetUnixFileMode(src);
            if ((mode & UnixFileMode.UserExecute) != 0)
                File.SetUnixFileMode(dst, mode);
        }
        catch { /* best effort: the binary is chmod'ed explicitly at stage time */ }
    }

    /// <summary>0700. The staged tree becomes executable code; nobody else on the box needs it.</summary>
    private static void SetPrivate(string dir)
    {
        try
        {
            File.SetUnixFileMode(dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch { /* best effort */ }
    }

    private static StagedPayload? ReadStaged(AgentConfig c) => ReadJson<StagedPayload>(StagedMarker(c));
    private static AppliedUpdate? ReadApplied(AgentConfig c) => ReadJson<AppliedUpdate>(AppliedMarker(c));

    private static T? ReadJson<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null; }
        catch { return null; }
    }

    private static void WriteJson<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, _json));
        File.Move(tmp, path, overwrite: true);
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
