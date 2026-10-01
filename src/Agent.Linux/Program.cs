using System.Diagnostics;
using System.Runtime.InteropServices;
using SaveLocker.Agent;

namespace SaveLocker.Agent.Linux;

static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return 0; }

        // `run` is parsed by hand and BEFORE the option parser: its tail is the game's own command
        // line (`%command%`, which Steam expands into a reaper/proton invocation full of things
        // that look like our flags). Everything after `--` belongs to the game, untouched.
        if (args[0] == "run")
            return await RunWrapperAsync(args[1..]);

        var (command, opts, positionals) = CliArgs.Parse(args);
        var config = AgentConfig.Load(ConfigPath(opts));

        if (command is null) { PrintUsage(); return 0; }

        // Commands shared with the Windows agent (register, push, pull, status, scan, …).
        if (AgentCli.Handles(command))
            return await AgentCli.RunAsync(command, opts, positionals, config,
                new LinuxGameScanner(new Detection(config)));

        switch (command)
        {
            case "daemon":
                // --lan was withdrawn: it bound an unauthenticated management API to every
                // interface. Fail loudly rather than silently ignoring a flag someone's autostart
                // unit or Help-KB notes still carry, so they find out the exposure is gone.
                if (opts.ContainsKey("lan"))
                {
                    Console.Error.WriteLine(
                        "--lan has been removed: it exposed this machine's management API to the whole network without authentication.");
                    Console.Error.WriteLine(
                        $"The daemon serves the UI on localhost only. To reach it from another device, tunnel it:");
                    Console.Error.WriteLine(
                        "  ssh -L 5178:localhost:5178 <user>@<this-machine>   # then browse to http://localhost:5178");
                    return 2;
                }
                await RunDaemonAsync(config, ParsePort(opts));
                return 0;

            case "doctor":
                return await Doctor.RunAsync(config);

            case "open":
                return await OpenWindowAsync(opts, config);

            case "steam-art":
                return SteamArtCommand(opts, config);

            // Test rig only: stale placeholder pictures in the artwork folder, standing in for the fixed art
            // install.sh bundles, so `steam-art` and the daemon's repaint have something to replace.
            case "dev-steam-art-fixture":
            {
                if (!TestCommandsAllowed(out var fixtureDenial)) { Console.Error.WriteLine(fixtureDenial); return 2; }
                var dir = Art.SteamArtHost.ArtworkDir(config);
                Directory.CreateDirectory(dir);
                foreach (var piece in Art.SteamArtRenderer.Pieces)
                    if (!File.Exists(Path.Combine(dir, piece + ".png"))) File.WriteAllText(Path.Combine(dir, piece + ".png"), "old art");
                Console.WriteLine($"fixture ready: {dir}");
                return 0;
            }

            // Exists for the updater's smoke test above all: a staged agent has to be able to prove
            // it can start and say what it is before it is allowed to replace a working one. Useful
            // in its own right — it is the first thing any bug report needs.
            case "version":
                Console.WriteLine(UpdateChecker.CurrentVersionText);
                return 0;

            case "update":
                return await UpdateNowAsync(config);

            // The launch-options command this device needs, and — with --preview — what a game
            // already carrying options would end up with. Phase 2 of tasks/DeckyPlugin.md turns
            // this into a per-game desired-state listing; today it is the rule's only surface, and
            // the answer to "what exactly do I paste into Steam?" without opening the agent UI.
            case "launch-options":
                return LaunchOptionsCommand(opts, config);

            case "plugin-update":
                return await PluginUpdateCommand(opts, config);

            // The unit's ExecStartPre. Runs in the NEW invocation, after the old daemon is gone —
            // which is the whole reason the swap does not happen inside the daemon (Updater.cs).
            case "apply-update":
                return Updater.Apply(config, Console.WriteLine, force: opts.ContainsKey("force"));

            case "ui":
                // Native SDL/GL/ImGui libs load only here, on demand — a headless `daemon` never
                // touches a GPU (Linux-Agent-Streamline.md §3).
                return Ui.UiApp.Run(config,
                    opts.GetValueOrDefault("size"),
                    opts.GetValueOrDefault("screenshot"),
                    opts.ContainsKey("gallery"),
                    opts.GetValueOrDefault("screen"),
                    opts.ContainsKey("autoscan"),
                    opts.GetValueOrDefault("nav"),
                    opts.ContainsKey("nav-debug"),
                    opts.GetValueOrDefault("pointer"),
                    // Where the "Sync now" button reaches the daemon's local API — same default the
                    // daemon itself binds to, overridable for a test daemon on a non-default port
                    // (testenv's Deck target runs one alongside the real install).
                    ParsePort(opts));

            // Test-only stand-in "game": tests/testenv.ps1's "Conflict Game" Steam shortcut points
            // here instead of a real title, so a launch-gate popup and sync-before/after-play can
            // be exercised on real hardware. Two spellings of the same FakeGame screen: `fake-game`
            // is what the seeded shortcut's Exe points at, `ui --screen fakegame` opens it by hand —
            // neither is ever written into a real install's Launch Options.
            // Deliberately NOT gated behind SAVELOCKER_ALLOW_TEST_COMMANDS like the dev-shortcut-*
            // commands below: Steam launches this directly as the shortcut's Exe (DevSteamShortcut
            // leaves LaunchOptions blank, so there is no wrapper to carry the env var), in Steam's
            // own environment, which never has that variable set. Gating it made the seeded
            // shortcut fail to launch from Steam's own library with no visible error — exactly the
            // scenario this command exists for. It has no side effects worth denying outside the
            // test rig (it only opens a UI window), unlike dev-shortcut-add/remove, which mutate a
            // real shortcuts.vdf and stay gated.
            case "fake-game":
                return Ui.UiApp.Run(config, opts.GetValueOrDefault("size"),
                    startScreen: "fakegame", apiPort: ParsePort(opts));

            // Test-only, wired from tests/testenv-deck.sh's cmd_conflict/cmd_clean only. Adds/
            // removes the one fixed "Conflict Game" shortcut - see DevSteamShortcut's own doc
            // comment for the backup/restore guarantee this makes about the real shortcuts.vdf.
            case "dev-shortcut-add":
            {
                if (!TestCommandsAllowed(out var addDenial)) { Console.Error.WriteLine(addDenial); return 2; }
                var prefix = opts.GetValueOrDefault("prefix");
                if (string.IsNullOrEmpty(prefix))
                {
                    Console.Error.WriteLine("dev-shortcut-add needs --prefix <dir>");
                    return 2;
                }
                if (DevSteamShortcut.KindNamed(opts.GetValueOrDefault("kind")) is not { } addKind)
                {
                    Console.Error.WriteLine("dev-shortcut-add --kind must be 'conflict' or 'ui'.");
                    return 2;
                }
                try
                {
                    // The Deck UI entry must open the TEST agent: its daemon port, and its state directory (config, api
                    // token) via XDG_DATA_HOME — without both, `savelocker ui` reads the real install and talks to :5178.
                    string? uiArgs = null, uiLaunch = null;
                    if (addKind == DevSteamShortcut.DeckUi)
                    {
                        var state = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                        if (string.IsNullOrEmpty(state) || state.IndexOf('"') >= 0 || !opts.ContainsKey("port"))
                        {
                            Console.Error.WriteLine("dev-shortcut-add --kind ui needs --port <n> and XDG_DATA_HOME set to the test state directory.");
                            return 2;
                        }
                        uiArgs = $"--port {ParsePort(opts)}";
                        uiLaunch = $"XDG_DATA_HOME=\"{state}\" %command%";
                    }
                    var appId = DevSteamShortcut.Add(prefix, opts.ContainsKey("with-launch-command"), addKind, uiArgs, uiLaunch);
                    if (appId is null) return 1;
                    if (addKind == DevSteamShortcut.DeckUi)
                    {
                        var look = config.EffectiveAppearance;
                        DevSteamShortcut.WriteArt(appId.Value, look.Accent, look.Mark, Art.SteamArtHost.Layer);
                    }
                    Console.WriteLine($"APPID={appId}");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"dev-shortcut-add failed: {ex.Message}");
                    return 1;
                }
            }

            case "dev-shortcut-remove":
                if (!TestCommandsAllowed(out var removeDenial)) { Console.Error.WriteLine(removeDenial); return 2; }
                if (DevSteamShortcut.KindNamed(opts.GetValueOrDefault("kind")) is not { } removeKind)
                {
                    Console.Error.WriteLine("dev-shortcut-remove --kind must be 'conflict' or 'ui'.");
                    return 2;
                }
                try
                {
                    return DevSteamShortcut.Remove(removeKind) ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"dev-shortcut-remove failed: {ex.Message}");
                    return 1;
                }

            case "autostart":
            {
                var autoStart = new SystemdAutoStart();
                if (opts.ContainsKey("disable"))
                {
                    var r = autoStart.SetEnabled(false);
                    if (!r.Ok)
                    {
                        Console.Error.WriteLine("Could not disable auto-start. " + r.Error);
                        return 1;
                    }
                    Console.WriteLine("Auto-start disabled.");
                }
                else if (opts.ContainsKey("enable"))
                {
                    var r = autoStart.SetEnabled(true);
                    if (!r.Ok)
                    {
                        Console.Error.WriteLine("Could not enable auto-start. " + r.Error);
                        return 1;
                    }
                    Console.WriteLine("Auto-start enabled (systemd --user unit savelocker.service).");
                }
                else
                {
                    Console.WriteLine(autoStart.IsEnabled() ? "enabled" : "disabled");
                }
                return 0;
            }

            case "help" or "--help" or "-h":
                PrintUsage();
                return 0;

            default:
                Console.Error.WriteLine($"Unknown command '{command}'.");
                PrintUsage();
                return 2;
        }
    }

    /// <summary>
    /// <c>savelocker launch-options [--preview "&lt;existing&gt;"] [--wrapper &lt;path&gt;]</c>.
    ///
    /// <para>
    /// With no arguments: the launch-options string a game on this device should carry. With
    /// <c>--preview</c>: what a game ALREADY carrying that string would end up with, which is the
    /// question worth asking before letting anything rewrite a working game's options.
    /// <c>--wrapper</c> overrides the resolved binary path so the rule can be exercised without an
    /// installed agent.
    /// </para>
    /// </summary>
    private static int LaunchOptionsCommand(Dictionary<string, string> opts, AgentConfig config)
    {
        var wrapper = opts.GetValueOrDefault("wrapper");
        if (string.IsNullOrWhiteSpace(wrapper))
        {
            wrapper = Daemon.WrapperPath();
            if (wrapper is null)
            {
                Console.Error.WriteLine(
                    "Could not determine the installed path — see install.sh for the exact command.");
                return 1;
            }
        }

        if (opts.TryGetValue("preview", out var existing))
        {
            Console.WriteLine(LaunchOptions.Apply(existing, wrapper));
            return 0;
        }

        var check = opts.ContainsKey("check");
        if (!check && !opts.ContainsKey("list"))
        {
            Console.WriteLine(LaunchOptions.Invocation(wrapper));
            return 0;
        }

        // Only games Steam launches. Nothing can set launch options for a game that has no AppID,
        // so listing them here would be reporting a fault the user cannot act on.
        var games = config.Games.Where(g => SteamShortcuts.UnsignedAppId(g.ResolveSteamAppId()) is not null).ToList();
        if (games.Count == 0)
        {
            Console.WriteLine("No tracked game launches under a Steam AppID.");
            return 0;
        }

        var desired = LaunchOptions.Invocation(wrapper);
        var unconfirmed = 0;
        foreach (var g in games)
        {
            var appId = SteamShortcuts.UnsignedAppId(g.ResolveSteamAppId())!.Value;
            string state;
            if (g.LaunchOptionsError is { Length: > 0 } error) { state = $"ERROR: {error}"; unconfirmed++; }
            else if (g.LaunchOptionsAppliedAt is { } at) state = $"set (confirmed {at:yyyy-MM-dd HH:mm} UTC)";
            else { state = "unknown — nothing has confirmed these"; unconfirmed++; }

            Console.WriteLine($"{g.Name}  (appid {appId})");
            Console.WriteLine($"    {state}");
            Console.WriteLine($"    {desired}");
        }

        // --check is an explicit question, so unknown counts against it; `doctor` treats the same
        // state as merely unknown, because there it sits among things that ARE faults.
        if (!check) return 0;
        Console.WriteLine();
        Console.WriteLine($"{unconfirmed} of {games.Count} game(s) not confirmed.");
        return unconfirmed == 0 ? 0 : 1;
    }

    /// <summary>
    /// <c>savelocker plugin-update [--check]</c> — the Decky plugin's update, by hand.
    ///
    /// <para>
    /// The daemon does this on its own timer; this is for someone who does not want to wait, and it
    /// is the seam the harness drives. <c>--check</c> reports and installs nothing, and exits
    /// non-zero when the installed plugin is behind — which makes it usable from a script, and makes
    /// the "does not apply what it only checked" assertion possible at all.
    /// </para>
    /// </summary>
    private static async Task<int> PluginUpdateCommand(Dictionary<string, string> opts, AgentConfig config)
    {
        var check = opts.ContainsKey("check");

        // AutoUpdate is the fleet-wide "the agent may install things" switch, and the plugin is
        // installed by the same mechanism on the same trust, so it honours the same setting. An
        // explicit `plugin-update` is still refused by it: the point of turning it off is that
        // nothing gets replaced without someone deciding, and this is the report they decide from.
        var apply = !check && config.AutoUpdate;

        var outcome = await DeckyPlugin.CheckAsync(config, Console.WriteLine, apply);

        Console.WriteLine($"plugin: {outcome.Message}");
        if (outcome.State is PluginUpdateState.Available && !check && !config.AutoUpdate)
            Console.WriteLine("Automatic updates are turned off for this machine, so nothing was installed.");

        return outcome.State switch
        {
            PluginUpdateState.Refused or PluginUpdateState.Failed => 1,
            PluginUpdateState.Available when check => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// <c>savelocker run [--config path] -- &lt;game command&gt;</c>. Steam passes the game's command
    /// line where <c>%command%</c> sits, so we split at the first bare <c>--</c> and hand the tail
    /// to the game verbatim.
    /// </summary>
    private static async Task<int> RunWrapperAsync(string[] tail)
    {
        var sep = Array.IndexOf(tail, "--");
        var ourArgs = sep >= 0 ? tail[..sep] : Array.Empty<string>();
        var childCommand = sep >= 0 ? tail[(sep + 1)..] : tail;

        var (_, opts, _) = CliArgs.Parse(ourArgs);
        var config = AgentConfig.Load(ConfigPath(opts));

        return await ProtonRun.ExecuteAsync(config, childCommand);
    }

    /// <summary>
    /// <c>savelocker update</c> — the explicit "do it now" path. The daemon only ever stages and
    /// waits for the next start; this is for someone who does not want to wait.
    /// <para>
    /// Restarting the service from here is safe in a way it is not from the daemon:
    /// <c>systemctl --user stop</c> kills the unit's whole cgroup, and this process is in the user's
    /// shell session, not in the unit. Started from something the daemon spawned, it would be killed
    /// halfway through — which is exactly why the daemon does not do this.
    /// </para>
    /// </summary>
    private static async Task<int> UpdateNowAsync(AgentConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            Console.Error.WriteLine(
                "This machine is not registered, so there is no server to ask. Run: savelocker enroll --file <policy.json>");
            return 1;
        }

        using var checker = new UpdateChecker(config);
        var result = await checker.CheckAsync();

        switch (result)
        {
            case UpdateResult.UpToDate:
                Console.WriteLine($"Already up to date (v{UpdateChecker.CurrentVersionText}).");
                return 0;
            case UpdateResult.Failed f:
                Console.Error.WriteLine($"Could not check for updates: {f.Reason}");
                return 1;
            case UpdateResult.Skipped:
                Console.WriteLine($"v{config.SkipVersion} is available but was skipped. Nothing to do.");
                return 0;
        }

        var available = (UpdateResult.Available)result;
        try
        {
            await Updater.StageAsync(config, available, Console.WriteLine);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Update refused: {ex.Message}");
            return 1;
        }

        // --force: the user asked for this one directly, and refusing because a game is open would
        // leave them with no way to say "yes, now" short of closing it. The file swap survives a
        // running wrapper by design (Updater.cs), so this is a courtesy default, not a safety rail.
        Updater.Apply(config, Console.WriteLine, force: true);
        RestartService();
        return 0;
    }

    /// <summary>
    /// Ask systemd to restart the unit, if it is managing one. Never fatal: a user who runs the
    /// daemon by hand still has a correctly updated install, they just have to restart it — and
    /// saying so is much better than a failed exit code they cannot act on.
    /// </summary>
    private static void RestartService()
    {
        try
        {
            var psi = new ProcessStartInfo("systemctl")
            {
                // --no-block: the restart tears down the unit, and we do not want to sit waiting on
                // a job whose whole point is that the old process goes away.
                ArgumentList = { "--user", "restart", "--no-block", "savelocker.service" },
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) throw new InvalidOperationException("systemctl could not be started");
            p.WaitForExit(10_000);

            if (p.ExitCode == 0)
            {
                Console.WriteLine("Restarted savelocker.service — the new version is running.");
                return;
            }
            Console.WriteLine(
                "The update is installed, but systemd would not restart the service " +
                $"(exit {p.ExitCode}). Start it yourself with:  systemctl --user restart savelocker.service");
        }
        catch
        {
            Console.WriteLine(
                "The update is installed. Restart the agent to run it " +
                "(systemctl --user restart savelocker.service, or restart your 'savelocker daemon').");
        }
    }

    private static int ParsePort(Dictionary<string, string> opts) =>
        opts.TryGetValue("port", out var raw) && int.TryParse(raw, out var port)
            ? port
            : Daemon.DefaultApiPort;

    /// <summary>Test-only hook so an integration test can run the daemon on a sub-second tick instead
    /// of waiting out the real 20s poll. Unset in normal operation, so nothing here changes.</summary>
    private static double? ParsePollMs() =>
        double.TryParse(Environment.GetEnvironmentVariable("SAVELOCKER_POLL_MS"), out var ms)
            ? ms
            : null;

    private static async Task RunDaemonAsync(AgentConfig config, int apiPort)
    {
        using var cts = new CancellationTokenSource();

        // systemd stops a unit with SIGTERM; Ctrl-C in a shell sends SIGINT. Handle both, so the
        // daemon shuts its listeners and watchers down rather than being killed mid-sync.
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;
            cts.Cancel();
        });
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
        {
            ctx.Cancel = true;
            cts.Cancel();
        });

        await using var daemon = new Daemon(config, apiPort, ParsePollMs());
        await daemon.RunAsync(cts.Token);
    }

    /// <summary>Config path from --config, else SAVELOCKER_CONFIG (handy in a systemd unit), else the default.</summary>
    private static string? ConfigPath(Dictionary<string, string> opts) =>
        opts.GetValueOrDefault("config")
        ?? Environment.GetEnvironmentVariable("SAVELOCKER_CONFIG");

    // Gate a command here because of what it DOES, not because it happens to be test-only: this
    // exists to stop a command from mutating real shared state (shortcuts.vdf, a real Steam
    // library) outside the test rig, not to block every command the test rig happens to use.
    // `fake-game` above is test-only too but stays ungated for exactly this reason — it only opens
    // a UI window, and gating it once broke the one real-world path that invokes it (Steam itself).
    // The next test-only command belongs here only if it can change something outside the process.
    private static bool TestCommandsAllowed(out string denial)
    {
        if (Environment.GetEnvironmentVariable("SAVELOCKER_ALLOW_TEST_COMMANDS") == "1")
        {
            denial = "";
            return true;
        }
        denial = "refusing: this is a test-rig command (tests/testenv.ps1 conflict). " +
            "Set SAVELOCKER_ALLOW_TEST_COMMANDS=1 to run it deliberately.";
        return false;
    }

    /// <summary>
    /// `savelocker steam-art [--out DIR] [--accent id] [--mark id]` — repaint the four library pictures in the artwork
    /// folder install.sh bundles, for the look in effect (or the one named), or with --out just write them into DIR.
    /// The daemon does the first by itself at start and on every change; this is for doing it by hand and for seeing it.
    /// </summary>
    private static int SteamArtCommand(Dictionary<string, string> opts, AgentConfig config)
    {
        var look = config.EffectiveAppearance;
        if (!TryLookId(opts, "accent", SaveLocker.Shared.Appearances.Accents, look.Accent, out var accent) ||
            !TryLookId(opts, "mark", SaveLocker.Shared.Appearances.Marks, look.Mark, out var mark))
            return 2;
        if (opts.GetValueOrDefault("out") is { Length: > 0 } dir)
        {
            foreach (var path in Art.SteamArt.Export(dir, accent, mark, Art.SteamArtHost.Layer)) Console.WriteLine(path);
            Console.WriteLine($"({accent}/{mark})");
            return 0;
        }

        var artwork = Art.SteamArtHost.ArtworkDir(config);
        var outcome = Art.SteamArt.Apply(artwork, accent, mark, Art.SteamArtHost.Layer);
        if (!outcome.FolderFound)
        {
            Console.WriteLine($"Could not write to {artwork}. Use --out <dir> to write the pictures elsewhere.");
            return 1;
        }
        Console.WriteLine($"{accent}/{mark}: {outcome.Written} picture(s) written in {artwork}.");
        if (accent != look.Accent || mark != look.Mark)
            Console.WriteLine($"That is not the look in effect ({look.Accent}/{look.Mark}): the agent paints its own again " +
                              "the next time it starts or the look changes.");
        return 0;
    }

    // Without this an id the painter does not know is painted as the default and reported under the name typed.
    private static bool TryLookId(Dictionary<string, string> opts, string name, string[] known, string inEffect, out string id)
    {
        id = opts.GetValueOrDefault(name)?.Trim().ToLowerInvariant() ?? inEffect;
        if (known.Contains(id)) return true;
        Console.Error.WriteLine($"Unknown --{name} '{id}'. Known: {string.Join(", ", known)}.");
        return false;
    }

    /// <summary>
    /// `savelocker open [--port n] [--view route]` — the agent UI in its own window, for Desktop Mode. This
    /// is what the KDE-menu entry install.sh writes runs; Game Mode keeps `savelocker ui`, and the two sit
    /// side by side over the same daemon. It does not start the daemon (systemd does, at login): a launcher
    /// that quietly spawned a second one would race the first over the same state, so it says how to start it.
    /// </summary>
    private static async Task<int> OpenWindowAsync(Dictionary<string, string> opts, AgentConfig config)
    {
        var port = opts.ContainsKey("port") ? ParsePort(opts) : config.DaemonApiPort ?? 5178;
        var origin = $"http://localhost:{port}";

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var up = false;
        try { up = (await http.GetAsync(origin + "/")).IsSuccessStatusCode; } catch { /* not listening */ }
        if (!up)
        {
            Console.Error.WriteLine($"The agent is not answering on {origin}.");
            Console.Error.WriteLine("Start it with:  systemctl --user start savelocker.service");
            return 1;
        }

        var view = opts.GetValueOrDefault("view");
        var url = origin + "/" + (string.IsNullOrWhiteSpace(view) ? "" : "#" + view.TrimStart('#'));
        if (AppWindow.TryOpen(url, out var plan))
        {
            Console.WriteLine($"Opened SaveLocker {plan!.Description}.");
            return 0;
        }

        Console.Error.WriteLine("There is no desktop session to open a window on (this looks like a headless shell).");
        Console.Error.WriteLine($"Open {url} in a browser here, or tunnel it:  ssh -L {port}:localhost:{port} <user>@<this-machine>");
        return 1;
    }

    private static void PrintUsage() => Console.WriteLine(
        """
        savelocker — SaveLocker agent for Linux (Proton / Steam Deck)

        Setup
          enroll --file <policy.json> [--name <name>]      Set up from a console enrollment file (start here)
          register --name <name> [--admin-password <pw>]   Register this machine by hand instead
          set-server --url <url>                           Point the agent at a server
          trust [--accept]                                 Show the pinned server TLS key, or re-pin it
          doctor                                           Diagnose the whole chain

        Games
          scan                                             Find non-Steam shortcuts and their prefixes
          add-game --name <n> [--dir <path>] [--appid <id>] [--manifest <key>] [--prefix <compatdata>]
          list                                             Show tracked games
          status                                           Server head / lease / conflicts
          hash [game] | --dir <path>                       Content hash (what conflict detection compares)

        Sync
          push [game|all] [--force]                        Upload saves
          pull [game|all] [--force]                        Download saves
          run -- %command%                                 Steam launch wrapper: pull, play, push
          launch-options [--list|--check]                  The string to paste into Steam's Launch
                         [--preview "<existing>"]           Options; --list adds per-game state and
                                                            --check exits non-zero if any is
                                                            unconfirmed; --preview shows what a game
                                                            already carrying options would end up with

        Daemon
          daemon [--port <n>]                              Run headless; serves the agent UI on localhost:5178
          open [--port <n>] [--view <route>]               Open the agent UI in its own window (Desktop Mode)
          steam-art [--out <dir>] [--accent <id>] [--mark <id>]
                                                           Repaint the bundled Steam library art in the current look
          autostart --enable | --disable                   systemd --user unit

        Updates
          version                                          Print this agent's version
          update                                           Fetch, verify and install a newer agent now
                                                           (the daemon otherwise stages it and applies
                                                            it the next time the agent starts)
          plugin-update [--check]                          Update the Decky plugin from the server;
                                                            --check only reports, exiting non-zero
                                                            if it is behind

        Game Mode
          ui [--size WxH] [--screenshot <file.png>]        Gamepad-native window for Steam Game Mode (Deck)
                                                           --size tests the layout off-device (default 1280x800)
                                                           --screenshot captures a PNG and exits
                                                           --nav-debug overlays the live nav cursor state
                                                           --port <n> where "Sync now" reaches the daemon's
                                                           local API (default 5178, same as `daemon --port`)

        Add this to a game's Steam launch options to sync it automatically:
          savelocker run -- %command%
        """);
}
