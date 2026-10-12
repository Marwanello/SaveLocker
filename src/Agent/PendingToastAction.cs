using System.Text.Json;

namespace SaveLocker.Agent;

/// <summary>
/// A toast command that arrived while no tray was running, left on disk for the tray to pick up as it
/// starts. The DCOM-launched host writes it and starts the tray; without it the click would be spent
/// waking the agent and the action itself would be lost. Age-limited on the way in: a click from days
/// ago (a machine that was off, a file a failed start left behind) must not fire a sync or an update
/// the moment the agent next comes up.
/// </summary>
internal static class PendingToastAction
{
    /// <summary>One file, so a newer click replaces an older one — the newest decision wins.</summary>
    private static string PathFor(string stateDir) => Path.Combine(stateDir, "toast-action.json");

    private sealed record Payload(string Command, DateTime AtUtc);

    public static void Write(string stateDir, AgentCommand command)
    {
        try
        {
            Directory.CreateDirectory(stateDir);
            File.WriteAllText(PathFor(stateDir),
                JsonSerializer.Serialize(new Payload(command.Arguments, DateTime.UtcNow)));
        }
        catch (Exception ex) { AgentLogger.LogException("PendingToastAction.Write", ex); }
    }

    /// <summary>
    /// Read, delete and return the pending command — null when there is none, it is too old, or it is
    /// unreadable. Consumed exactly once: whatever happens to the action next, it is not re-run by the
    /// start after this one.
    /// </summary>
    public static AgentCommand? Consume(string stateDir, TimeSpan maxAge)
    {
        var path = PathFor(stateDir);
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            File.Delete(path);
            var payload = JsonSerializer.Deserialize<Payload>(json);
            if (payload is null || DateTime.UtcNow - payload.AtUtc > maxAge) return null;
            return AgentCommand.TryParse(payload.Command, out var command) ? command : null;
        }
        catch (Exception ex)
        {
            AgentLogger.LogException("PendingToastAction.Consume", ex);
            try { File.Delete(path); } catch { /* whatever it was, it is not this start's to retry */ }
            return null;
        }
    }
}
