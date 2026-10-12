namespace SaveLocker.Agent;

/// <summary>
/// Thread-safe rolling log file at %ProgramData%\SaveLocker\agent.log.
/// Rotates to agent.log.old when the file exceeds 1 MB so it never grows unbounded.
/// All writes are best-effort — logging failures are silently swallowed so they
/// can never crash the agent.
/// </summary>
public static class AgentLogger
{
    public static readonly string LogPath =
        Path.Combine(AgentConfig.DefaultDir, "agent.log");

    private static readonly string OldLogPath = LogPath + ".old";
    private static readonly object Lock = new();
    private const long MaxBytes = 1 * 1024 * 1024;
    private static bool _protected;

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(AgentConfig.DefaultDir);
                // The first write in this process also applies the state directory's protection, so
                // the log is covered on entry paths that never write config.json, and a log an older
                // build left world-readable is tightened in passing.
                if (!_protected)
                {
                    _protected = true;
                    StateDirSecurity.Protect(AgentConfig.DefaultDir);
                }
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                {
                    if (File.Exists(OldLogPath)) File.Delete(OldLogPath);
                    File.Move(LogPath, OldLogPath);
                }
                Append(line + Environment.NewLine);
            }
            catch { /* must never crash the agent */ }
        }
    }

    public static void LogException(string context, Exception ex) =>
        Log($"ERROR [{context}]:{Environment.NewLine}{ex}");

    /// <summary>
    /// Append through a stream created 0600 on Unix (the file may not exist yet — a post-rotation
    /// first line, or a brand-new install), instead of <c>File.AppendAllText</c>'s umask mode.
    /// Windows ignores <c>UnixCreateMode</c>; the directory's ACL is the protection there.
    /// </summary>
    private static void Append(string text)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.Read,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(LogPath, options);
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }
}
