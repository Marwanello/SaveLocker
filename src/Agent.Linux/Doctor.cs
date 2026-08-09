using SaveLocker.Shared;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Answers "why isn't this working?" on a machine with no UI to look at. A Deck in Game Mode
/// shows the user nothing, so every link in the chain — server, Steam, shortcuts, prefixes,
/// save dirs, permissions — is checked out loud, in the order it has to succeed.
/// </summary>
public static class Doctor
{
    private static int _problems;

    public static async Task<int> RunAsync(AgentConfig config)
    {
        _problems = 0;

        Console.WriteLine("SaveLocker doctor");
        Console.WriteLine("=================");
        Console.WriteLine();

        Section("Agent");
        // A headless box that cannot tell you which version it is running makes every other answer
        // less useful — and it is the first thing to ask for in a bug report.
        Info("version", UpdateChecker.CurrentVersionText);
        Info("config", config.ConfigPath);
        // config.StateDir, not AgentConfig.DefaultDir: with --config the two differ, and reporting
        // (or probing) the default is worse than saying nothing — it declares the wrong directory
        // healthy while the one actually in use may be missing or read-only. The log genuinely does
        // stay in the default directory, so it is labelled rather than silently moved.
        Info("state dir", config.StateDir);
        Info("log", $"{AgentLogger.LogPath} (always the default state dir)");
        Check("state dir is writable", IsWritable(config.StateDir),
            $"cannot write to {config.StateDir} — the agent cannot save its config or queue.");

        Console.WriteLine();
        Section("Server");
        Info("url", config.ServerUrl);
        if (string.IsNullOrEmpty(config.ApiKey))
            Problem("this machine is not registered. Run: savelocker register --name <name>");
        else
            Info("machine", $"{config.MachineName} ({config.MachineId})");
        await CheckServerAsync(config);

        Console.WriteLine();
        Section("Steam");
        var roots = SteamRoots.Find();
        Check("Steam installation found", roots.Count > 0,
            "no Steam root found (looked for ~/.steam/steam, ~/.local/share/Steam and the Flatpak path).");
        foreach (var r in roots) Info("root", r);

        var shortcuts = await ReadShortcutsAsync(roots);
        Check("non-Steam shortcuts found", shortcuts.Count > 0,
            "no shortcuts in shortcuts.vdf. SaveLocker syncs non-Steam games added to Steam; " +
            "Steam-store games already have Steam Cloud.");

        Console.WriteLine();
        Section("Shortcuts and Proton prefixes");
        foreach (var (root, s) in shortcuts)
        {
            if (s.AppId is null)
            {
                Problem($"'{s.AppName}': no AppID in shortcuts.vdf — its Proton prefix cannot be located.");
                continue;
            }

            var prefix = SteamRoots.CompatDataPath(root, s.AppId);
            if (prefix is null)
            {
                Console.WriteLine($"  - {s.AppName}  (appid {s.AppId})");
                Console.WriteLine($"      no prefix at {Path.Combine(root, "steamapps", "compatdata", s.AppId)}");
                Console.WriteLine("      → launch it once through Steam with Proton (Force compatibility tool) to create it.");
                continue;
            }
            Console.WriteLine($"  ✓ {s.AppName}  (appid {s.AppId})");
            Console.WriteLine($"      prefix: {prefix}");
        }

        Console.WriteLine();
        Section("Heroic Games Launcher");
        ReportHeroic();

        Console.WriteLine();
        Section("Tracked games");
        if (config.Games.Count == 0)
            Console.WriteLine("  (none — add one with: savelocker add-game --name <name> --dir <path> --appid <appid>)");

        foreach (var g in config.Games)
        {
            Console.WriteLine($"  {g.Name}");
            if (g.SteamAppId is null)
                Console.WriteLine("    appid: (none — the launch wrapper cannot match this game)\n" +
                                  "           Run 'savelocker scan' to find the correct AppID, then re-add with --appid.");
            else
                Info("    appid", g.SteamAppId);
            if (string.IsNullOrWhiteSpace(g.SaveDirectory))
            {
                Problem($"'{g.Name}' has no save directory. Set one: savelocker add-game --name \"{g.Name}\" --dir <path>");
                continue;
            }
            Info("    save dir", g.SaveDirectory);

            // A non-Steam shortcut's AppID is crc32(exe + name) with the high bit set — stable across
            // launches and Desktop/Game Mode, but RECOMPUTED if the shortcut is renamed, re-pointed, or
            // removed and re-added. Steam then makes a fresh empty prefix, and a save folder mapped to
            // the old compatdata id keeps working perfectly while the game writes somewhere else
            // entirely. Nothing else in the chain notices: the folder exists, is readable, and syncs.
            if (g.SteamAppId is { Length: > 0 } appId &&
                CompatDataIdIn(g.SaveDirectory) is { } mappedId &&
                !mappedId.Equals(appId, StringComparison.Ordinal))
            {
                Problem($"'{g.Name}' syncs prefix {mappedId}, but its shortcut is AppID {appId}. " +
                        $"Steam launches the game into compatdata/{appId}, so saves written now are " +
                        $"NOT being backed up. This happens when a shortcut is renamed or re-pointed. " +
                        $"Check which prefix has recent files, then re-add with --dir under {appId}.");
            }

            if (!Directory.Exists(g.SaveDirectory))
                Problem($"'{g.Name}' save directory does not exist: {g.SaveDirectory}");
            else if (!IsWritable(g.SaveDirectory))
                Problem($"'{g.Name}' save directory is not writable — pull would fail: {g.SaveDirectory}");
            else
            {
                // Symlinks are never archived and never followed (they would drag in — or, on restore,
                // delete — files outside the save folder). Doctor is where the user finds out that a
                // link they placed here is not being synced.
                var links = new List<string>();
                SaveArchive.OnSymlinkSkipped = p => links.Add(p);
                var (bytes, files) = SaveDirSanity.Measure(g.SaveDirectory, g.ExcludeGlobs);
                SaveArchive.OnSymlinkSkipped = null;

                Info("    files", $"{files} ({bytes / 1024.0 / 1024.0:0.#} MB)");
                foreach (var link in links.Take(5))
                    Console.WriteLine($"      ! symlink NOT synced: {link}");
                if (links.Count > 5)
                    Console.WriteLine($"      ! …and {links.Count - 5} more symlinks not synced");

                // A save path that is really the Wine prefix archives gigabytes and is rejected by the
                // upload cap — with an error about the SAVE being too big, which sends the user hunting
                // in the wrong place. Name the actual mistake.
                foreach (var problem in SaveDirSanity.Inspect(g.SaveDirectory, g.ExcludeGlobs))
                    Problem($"'{g.Name}': {problem}");
            }

            if (g.SteamAppId is not null && config.Games.Any(o => o != g && o.SteamAppId == g.SteamAppId))
                Problem($"more than one tracked game claims appid {g.SteamAppId}; " +
                        "the launch wrapper would pick one arbitrarily.");
        }

        Console.WriteLine();
        Section("Platform");
        var probe = FileLockProbe.FirstWriter(AgentConfig.DefaultDir, Array.Empty<string>());
        if (probe.Supported)
            Info("open-file detection", "available (/proc)");
        else
            Console.WriteLine("  ! open-file detection unavailable — the settle gate will wait on the " +
                              "file fingerprint alone. Saves still sync; a game that flushes without " +
                              "changing file size may push slightly early.");

        Console.WriteLine();
        if (_problems == 0)
        {
            Console.WriteLine("No problems found.");
            return 0;
        }
        Console.WriteLine($"{_problems} problem(s) found — see the ✗ lines above.");
        return 1;
    }

    /// <summary>
    /// Heroic, if it is here. Not finding it is not a problem — most machines do not have it — so
    /// this reports rather than complains, right up until a game is found whose prefix is missing.
    /// </summary>
    private static void ReportHeroic()
    {
        var roots = HeroicRoots.Find();
        if (roots.Count == 0)
        {
            Console.WriteLine("  not installed (no config.json under ~/.config/heroic or the Flatpak path).");
            return;
        }

        foreach (var configRoot in roots)
        {
            Info("config", configRoot);
            var games = HeroicLibrary.Read(configRoot);

            if (games.Count == 0)
            {
                Console.WriteLine("  no installed Windows games found. Native-Linux titles are not " +
                                  "listed here on purpose — they have no Wine prefix to sync from.");
                continue;
            }

            foreach (var runner in games.GroupBy(g => g.Runner).OrderBy(g => g.Key, StringComparer.Ordinal))
                Info(runner.Key, $"{runner.Count()} game(s)");

            foreach (var game in games.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase))
            {
                var prefix = HeroicRoots.PrefixFor(configRoot, game.AppName);
                var layout = WinePrefix.Locate(prefix);

                if (layout is null)
                {
                    Console.WriteLine($"  - {game.Title}  ({game.Runner})");
                    Console.WriteLine($"      install: {game.InstallPath}");
                    Console.WriteLine(prefix is null
                        ? "      no Wine prefix recorded — launch it once in Heroic to create one."
                        : $"      {prefix} is not a Wine prefix (no drive_c). Launch it once in Heroic.");
                    continue;
                }

                Console.WriteLine($"  ✓ {game.Title}  ({game.Runner})");
                Console.WriteLine($"      install: {game.InstallPath}");
                // Which shape it is decides where the manifest's Windows tokens resolve, so it is
                // the first thing to know when a Heroic game's save folder comes back empty.
                Console.WriteLine($"      prefix:  {layout.Root} " +
                                  $"({(layout.IsProtonShaped ? "proton" : "wine")}, user {layout.UserName})");

                // A sideloaded game's executable should sit inside the prefix it runs in. When it
                // does not, Heroic is launching an .exe from one prefix inside another — which
                // works, because Wine reaches any Linux path through Z:, so nothing anywhere
                // complains.
                //
                // Seen on a real Deck (2026-08-09): uninstalling left the old prefix and a complete
                // .exe behind, the reinstall went to a different prefix, and Heroic's "pick the
                // executable" step was answered with the leftover copy. Renaming a prefix folder
                // without updating Heroic produces the same shape.
                //
                // Reported as a NOTE, not a problem. Saves follow the prefix the game RUNS in, which
                // is the one above, so SaveLocker is reading the right tree and the machine works —
                // failing doctor's exit code over it would cry wolf. It is still worth saying: it
                // means two copies of the game exist, and <base> — the manifest's most common
                // placeholder — points at the copy the game is not running.
                if (WinePrefix.ContainingPrefix(game.InstallPath) is { } installedIn &&
                    !installedIn.Equals(layout.Root, StringComparison.Ordinal))
                {
                    Console.WriteLine($"      ! its executable is in {installedIn}, a DIFFERENT prefix.");
                    Console.WriteLine("        Saves follow the prefix above — the one it RUNS in — " +
                                      "so that is the one SaveLocker reads, and that part is fine.");
                    Console.WriteLine("        But a save path the manifest anchors to the install " +
                                      "directory would resolve into the other copy. Usually this " +
                                      "means an old prefix was left behind and its leftover .exe was " +
                                      "picked; delete it and re-point the game in Heroic.");
                }
            }
        }
    }

    private static async Task CheckServerAsync(AgentConfig config)
    {
        var api = ApiClient.For(config);

        // Three outcomes, not two. Calling an authenticated route with no key and then reporting the
        // resulting 401 as "server unreachable" sent every unenrolled Deck owner to debug their
        // network, when the server was fine and the machine simply had not registered.
        var status = await api.ProbeAsync("/api/games");
        if (status is null)
        {
            Problem($"server unreachable at {config.ServerUrl} — nothing answered. " +
                    "Check the URL, that the server is running, and this device's network.");
            return;
        }

        Check("server reachable", true, "");

        if (status == System.Net.HttpStatusCode.Unauthorized || status == System.Net.HttpStatusCode.Forbidden)
        {
            Problem(string.IsNullOrEmpty(config.ApiKey)
                ? "the server is reachable, but this machine is not enrolled. " +
                  "Run: savelocker enroll --file <token file>"
                : "the server is reachable, but it rejected this machine's API key. " +
                  "Re-enroll this device, or check that its machine record still exists.");
            return;
        }

        try
        {
            var games = await api.ListGamesAsync();
            Check("authenticated", true, "");
            Info("games on server", games.Count.ToString());
        }
        catch (Exception ex)
        {
            Problem($"server answered {(int)status} but the game list could not be read: {ex.Message}");
        }
    }

    private static async Task<List<(string Root, SteamShortcut Shortcut)>> ReadShortcutsAsync(
        IReadOnlyList<string> roots)
    {
        var all = new List<(string, SteamShortcut)>();
        foreach (var root in roots)
            foreach (var s in await SteamShortcuts.ReadAllAsync(root))
                all.Add((root, s));
        return all;
    }

    /// <summary>Probe writability for real — the permission bits alone don't account for a
    /// read-only mount, and SteamOS mounts plenty of things read-only.</summary>
    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".savelocker-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>The <c>compatdata/&lt;id&gt;</c> segment of a path, or null if it is not in a prefix.</summary>
    private static string? CompatDataIdIn(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var i = Array.FindIndex(parts, p => p.Equals("compatdata", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < parts.Length && parts[i + 1].Length > 0 ? parts[i + 1] : null;
    }

    private static void Section(string name) => Console.WriteLine($"── {name} ──");
    private static void Info(string label, string value) => Console.WriteLine($"  {label}: {value}");

    private static void Check(string label, bool ok, string problem)
    {
        if (ok) Console.WriteLine($"  ✓ {label}");
        else Problem(problem);
    }

    private static void Problem(string message)
    {
        _problems++;
        Console.WriteLine($"  ✗ {message}");
    }
}
