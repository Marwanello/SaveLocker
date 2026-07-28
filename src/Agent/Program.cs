namespace SaveLocker.Agent;

static class Program
{
    // With no recognised command we launch the tray UI (STA thread, no prior
    // await); otherwise we run a one-shot CLI command (the manual-override surface).
    // Main is intentionally NON-async: [STAThread] is ignored on an async Main,
    // which leaves the WinForms thread MTA and makes OLE calls (clipboard, file
    // dialogs) throw. A synchronous STA Main runs the tray correctly; CLI commands
    // are bridged to async via GetAwaiter().GetResult().
    [STAThread]
    static int Main(string[] args)
    {
        var (command, opts, positionals) = CliArgs.Parse(args);
        var config = AgentConfig.Load(opts.GetValueOrDefault("config"));

        if (command is null)
        {
            // Single-instance guard: a second tray launch (e.g. auto-start firing while
            // one is already open) just exits. The mutex name is shared with the
            // installer's AppMutex so setup can detect a running agent and prompt the
            // user to close it before replacing files. CLI one-shots are not guarded.
            // A harness tray runs on its own port and must not be blocked by — or block — the
            // installed one, so the override scopes the guard too. Unset in production, where the
            // name and the installer's AppMutex stay exactly as they were.
            var trayPort = Environment.GetEnvironmentVariable("SAVELOCKER_TRAY_PORT");
            var mutexName = string.IsNullOrWhiteSpace(trayPort) ? "SaveLocker.Agent"
                                                               : $"SaveLocker.Agent.{trayPort}";
            using var mutex = new Mutex(initiallyOwned: true, mutexName, out var isNew);
            if (!isNew) return 0;
            TrayApp.Run(config);
            return 0;
        }

        var scanner = new GameScanner(new Detection(config));
        return AgentCli.RunAsync(command, opts, positionals, config, scanner)
            .GetAwaiter().GetResult();
    }
}
