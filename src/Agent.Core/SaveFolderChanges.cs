using System.Collections.Concurrent;

namespace SaveLocker.Agent;

/// <summary>
/// When each game's save files here last changed, or were last pushed, pulled or held — what the game page's
/// <i>Save files on this device</i> tree compares against to reload itself (it hashes every file, so it never polls).
/// In memory: the daemon and the tray serve the page from the process that watches the folders.
/// </summary>
public static class SaveFolderChanges
{
    private static readonly ConcurrentDictionary<Guid, DateTime> Changed = new();

    public static void Touch(Guid gameId) => Changed[gameId] = DateTime.UtcNow;

    public static IReadOnlyDictionary<Guid, DateTime> Snapshot() => new Dictionary<Guid, DateTime>(Changed);
}
