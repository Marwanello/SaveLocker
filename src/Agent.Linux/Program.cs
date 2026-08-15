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

            // Exists for the updater's smoke test above all: a staged agent has to be able to prove
            // it can start and say what it is before it is allowed to replace a working one. Useful
            // in its own right — it is the first thing any bug report needs.
            case "version":
                Console.WriteLine(UpdateChecker.CurrentVersionText);
                return 0;

            case "update":
                return await UpdateNowAsync(config);

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
                    opts.GetValueOrDefault("pointer"));

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

        await using var daemon = new Daemon(config, apiPort);
        await daemon.RunAsync(cts.Token);
    }

    /// <summary>Config path from --config, else SAVELOCKER_CONFIG (handy in a systemd unit), else the default.</summary>
    private static string? ConfigPath(Dictionary<string, string> opts) =>
        opts.GetValueOrDefault("config")
        ?? Environment.GetEnvironmentVariable("SAVELOCKER_CONFIG");

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

        Daemon
          daemon [--port <n>]                              Run headless; serves the agent UI on localhost:5178
          autostart --enable | --disable                   systemd --user unit

        Updates
          version                                          Print this agent's version
          update                                           Fetch, verify and install a newer agent now
                                                           (the daemon otherwise stages it and applies
                                                            it the next time the agent starts)

        Game Mode
          ui [--size WxH] [--screenshot <file.png>]        Gamepad-native window for Steam Game Mode (Deck)
                                                           --size tests the layout off-device (default 1280x800)
                                                           --screenshot captures a PNG and exits
                                                           --nav-debug overlays the live nav cursor state

        Add this to a game's Steam launch options to sync it automatically:
          savelocker run -- %command%
        """);
}
