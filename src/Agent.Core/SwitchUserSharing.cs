using System.IO.Compression;
using System.Text.Json;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// One Switch user on every device (the maintainer's call, 2026-10-16). Animal Crossing links each island resident to the ID
/// of the emulator user who made them, so a Deck and a PC whose Yuzu-based emulators have different user IDs make the game
/// ask to relink on every switch. The user picks one user ID on any device (the server keeps it, and hands it out on the
/// heartbeat); every Yuzu-based install on every device then adopts it:
/// <list type="number">
/// <item>never while a Switch emulator runs, and never over a title whose save is under both users until the user says
/// which to keep;</item>
/// <item><c>profiles.dat</c> and every save about to move or be replaced are zipped first under
/// <c>&lt;state&gt;/switch-users</c>;</item>
/// <item>the server is told every tracked game's new folder before anything moves (its folder for this machine is what the
/// poller applies);</item>
/// <item>the old user's save folder moves to the new ID's, the current user's ID in <c>profiles.dat</c> becomes the new
/// one (its name stays), and an avatar named by the ID is renamed with it;</item>
/// <item>the tracked games follow, and what was done is recorded so “Put back the original user” can undo it.</item>
/// </list>
/// Ryujinx keeps the user ID in its save index too, which SaveLocker never writes, so it is left alone.
/// </summary>
public static class SwitchUserSharing
{
    /// <summary>The folder under a fork's <c>user/save</c> that holds each user's saves.</summary>
    public const string UsersFolder = "0000000000000000";
    public const string Here = "here", Shared = "shared";

    /// <summary>Takes the sync lock of a game around some work: false when a sync holds it.</summary>
    public delegate Task<(bool Ran, string? Result)> GameLock(TrackedGame game, Func<Task<string?>> work);

    /// <summary>One Yuzu-based install here, against the shared user: its current user, whether that is the shared one,
    /// the titles saved under both, whether it can be put back, and why it can't adopt the shared user when it can't.</summary>
    public sealed record InstallState(string Emulator, string Install, string Saves, string? UserId, string? UserName, bool Matches,
        Collision[] Collisions, bool CanUndo, string? Note);

    /// <summary>A title with a save under both this install's user and the shared one.</summary>
    public sealed record Collision(string TitleId, string? GameName, DateTime? HereWritten, DateTime? SharedWritten);

    /// <summary>What an adopt or an undo did, a line each, or why nothing changed.</summary>
    public sealed record Outcome(string? Refusal, IReadOnlyList<string> Done);

    /// <summary>One install's adoption, as recorded for “Put back the original user”.</summary>
    public sealed class Applied
    {
        public DateTime At { get; set; }
        public string Emulator { get; set; } = "";
        public string Install { get; set; } = "";
        public string Saves { get; set; } = "";
        public string Profiles { get; set; } = "";
        public string OldId { get; set; } = "";
        public string NewId { get; set; } = "";
        public string Backup { get; set; } = "";
        /// <summary>The old user's folders and files moved under the new ID (a title kept from here included).</summary>
        public List<string> Moved { get; set; } = new();
        /// <summary>Titles saved under both: which was kept (<see cref="Here"/> or <see cref="Shared"/>).</summary>
        public Dictionary<string, string> Kept { get; set; } = new();
        /// <summary>Avatars renamed with the ID: [from, to] file names.</summary>
        public List<string[]> Avatars { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // One adopt or undo at a time in this process: the poller adopts on its own tick while the page's button can too, and
    // two passes over one install would move its folders and rewrite its profiles.dat at once. A tracked game's sync lock
    // doesn't cover it — an install whose user has no tracked game takes none.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public const string Busy = "The Switch user is already being changed. Try again in a moment.";

    // ---------------------------------------------------------------- what is here

    /// <summary>The users of every Yuzu-based install here, for the heartbeat; the current one marked.</summary>
    public static SwitchUserReport[] Reports(IReadOnlyList<SwitchEmulators.Setup> setups) =>
        [.. Forks(setups).SelectMany(s => SwitchEmulators.Users(s).Select((u, i) => new SwitchUserReport(s.Emulator, InstallOf(s), u.Id, u.Name, i == 0)))
            .Where(u => SwitchUserReport.IsUserId(u.Id))];

    /// <summary>Every Yuzu-based install here against <paramref name="shared"/> (or none chosen).</summary>
    public static IReadOnlyList<InstallState> States(AgentConfig config, SwitchUserDto? shared, IReadOnlyList<SwitchEmulators.Setup> setups)
    {
        var applied = ReadApplied(config);
        var states = new List<InstallState>();
        foreach (var s in Forks(setups))
        {
            var users = SwitchEmulators.Users(s);
            var current = users.FirstOrDefault();
            var canUndo = current is not null && LastApplied(applied, s) is { } a && Is(a.NewId, current.Id);
            if (current is null)
            {
                states.Add(new InstallState(s.Emulator, InstallOf(s), s.Saves, null, null, false, [], false,
                    $"{s.Emulator} has no user yet. Open it once, so it makes one."));
                continue;
            }
            if (shared is null || Is(current.Id, shared.Id))
            {
                states.Add(new InstallState(s.Emulator, InstallOf(s), s.Saves, current.Id, current.Name, shared is not null, [], canUndo, null));
                continue;
            }
            var note = users.Skip(1).FirstOrDefault(u => Is(u.Id, shared.Id)) is { } other
                ? $"{s.Emulator} already has a user with that ID ({other.Name}). Make it {s.Emulator}'s current user instead."
                : !File.Exists(s.Profiles) ? $"{s.Emulator}'s users file isn't where SaveLocker looks for it." : null;
            states.Add(new InstallState(s.Emulator, InstallOf(s), s.Saves, current.Id, current.Name, false,
                note is null ? Collisions(config, s, current.Id, shared.Id) : [], canUndo, note));
        }
        return states;
    }

    private static Collision[] Collisions(AgentConfig config, SwitchEmulators.Setup s, string oldId, string newId)
    {
        var oldDir = UserDir(s, oldId);
        var newDir = UserDir(s, newId);
        if (!Directory.Exists(oldDir) || !Directory.Exists(newDir)) return [];
        return [.. Children(oldDir).Where(c => c.IsDir && SwitchEmulators.IsTitleId(c.Name))
            .Where(c => SwitchEmulators.HasFiles(c.Path) && SwitchEmulators.HasFiles(Path.Combine(newDir, c.Name)))
            .Select(c => new Collision(c.Name.ToUpperInvariant(), GameName(config, c.Name),
                Written(c.Path), Written(Path.Combine(newDir, c.Name))))];
    }

    // ---------------------------------------------------------------- adopting the shared user

    /// <summary>
    /// Make <paramref name="shared"/> the current user of every Yuzu-based install here that has another (see the class).
    /// <paramref name="keep"/> answers the titles saved under both users, title ID → <see cref="Here"/> or
    /// <see cref="Shared"/>; one unanswered refuses everything. Nothing changes when anything refuses.
    /// </summary>
    public static async Task<Outcome> ApplyAsync(AgentConfig config, SwitchUserDto shared, IReadOnlyList<SwitchEmulators.Setup> setups,
        ApiClient? api, GameLock gameLock, IReadOnlyDictionary<string, string>? keep, Action<string> log, Func<string?>? running = null)
    {
        if (!SwitchUserReport.IsUserId(shared.Id)) return new Outcome("That isn't a Switch user ID.", []);
        if (!await Gate.WaitAsync(0)) return new Outcome(Busy, []);
        try { return await ApplyGatedAsync(config, shared, setups, api, gameLock, keep, log, running); }
        finally { Gate.Release(); }
    }

    private static async Task<Outcome> ApplyGatedAsync(AgentConfig config, SwitchUserDto shared, IReadOnlyList<SwitchEmulators.Setup> setups,
        ApiClient? api, GameLock gameLock, IReadOnlyDictionary<string, string>? keep, Action<string> log, Func<string?>? running)
    {
        var newId = shared.Id.ToUpperInvariant();
        var states = States(config, shared, setups);
        var todo = states.Where(s => s.UserId is not null && !s.Matches).ToList();
        if (todo.Count == 0) return new Outcome(null, []);
        if ((running ?? SwitchCopies.RunningEmulator)() is { } open) return new Outcome($"{open} is running. Close it first.", []);
        if (todo.FirstOrDefault(s => s.Note is not null) is { } blocked) return new Outcome(blocked.Note, []);
        var unanswered = todo.SelectMany(s => s.Collisions).Where(c => keep?.GetValueOrDefault(c.TitleId) is not (Here or Shared)).ToList();
        if (unanswered.Count > 0)
            return new Outcome($"Both this device's user and {shared.Name} have saves of {string.Join(", ", unanswered.Select(c => c.GameName ?? c.TitleId))}. " +
                               "Choose which to keep.", []);

        var work = todo.Select(s => (State: s, Setup: Forks(setups).First(x => x.Saves == s.Saves))).ToList();
        var games = config.Games.Where(g => work.Any(w => Moves(g, UserDir(w.Setup, w.State.UserId!)))).ToList();
        if (games.Count > 0 && api is null)
            return new Outcome("This machine isn't connected to a server, which keeps each machine's save folders.", []);

        var (ran, refusal) = await UnderLocks(gameLock, games, async () =>
        {
            var done = new List<string>();
            foreach (var (state, setup) in work)
            {
                var why = await AdoptAsync(config, setup, state.UserId!, newId, keep, api, log);
                if (why is not null) return done.Count == 0 ? why : $"{why} {string.Join(", ", done)} already did.";
                done.Add($"{setup.Emulator} ({InstallOf(setup)})");
            }
            return null;
        });
        if (!ran) return new Outcome($"A sync of {string.Join(", ", games.Select(g => g.Name))} is running. Try again when it's done.", []);
        if (refusal is not null) return new Outcome(refusal, []);
        return new Outcome(null, [.. todo.Select(s => $"{s.Emulator} ({s.Install}) uses {shared.Name} now, the Switch user every device shares.")]);
    }

    private static async Task<string?> AdoptAsync(AgentConfig config, SwitchEmulators.Setup s, string oldId, string newId,
        IReadOnlyDictionary<string, string>? keep, ApiClient? api, Action<string> log)
    {
        var oldDir = UserDir(s, oldId);
        var newDir = UserDir(s, newId);
        var collisions = Directory.Exists(newDir)
            ? Children(oldDir).Where(c => SwitchEmulators.IsTitleId(c.Name) && SwitchEmulators.HasFiles(c.Path) &&
                                          SwitchEmulators.HasFiles(Path.Combine(newDir, c.Name)))
                .ToDictionary(c => c.Name, c => keep![c.Name.ToUpperInvariant()], StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var record = new Applied
        {
            At = DateTime.UtcNow, Emulator = s.Emulator, Install = InstallOf(s), Saves = s.Saves, Profiles = s.Profiles!,
            OldId = oldId.ToUpperInvariant(), NewId = newId, Kept = collisions.ToDictionary(k => k.Key.ToUpperInvariant(), k => k.Value),
        };
        record.Backup = Backup(config, s, oldDir, newDir, collisions.Where(c => c.Value == Here).Select(c => c.Key));

        // The server first: a folder moved only here would be put back by the poller's next tick.
        var moves = Repoint(config, oldDir, newDir);
        var told = new List<(TrackedGame Game, string Key, string From)>();
        try
        {
            foreach (var m in moves)
            {
                await api!.SetMachinePathAsync(m.Game.GameId, m.To, m.Key);
                told.Add((m.Game, m.Key, m.From));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Untell(api!, told, log);
            return $"Could not tell the server about the new folders, so nothing changed: {ex.Message}";
        }

        var profileRewritten = false;
        try
        {
            if (Directory.Exists(oldDir))
            {
                Directory.CreateDirectory(newDir);
                foreach (var c in Children(oldDir))
                {
                    var dest = Path.Combine(newDir, c.Name);
                    if (collisions.TryGetValue(c.Name, out var kept))
                    {
                        if (kept == Shared) { Delete(c.Path); continue; }
                        Delete(dest);
                    }
                    else if (Exists(dest))
                    {
                        // Only one of the two has a save: that one stays (the other is in the backup).
                        if (SwitchEmulators.HasFiles(dest)) { Delete(c.Path); continue; }
                        Delete(dest);
                    }
                    MoveEntry(c.Path, dest);
                    record.Moved.Add(c.Name);
                }
                if (!Children(oldDir).Any()) Directory.Delete(oldDir);
            }
            RewriteProfile(s.Profiles!, oldId, newId);
            profileRewritten = true;
            record.Avatars = RenameAvatars(s.Profiles!, oldId, newId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (profileRewritten)
                try { RewriteProfile(s.Profiles!, newId, oldId); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            foreach (var name in record.Moved.AsEnumerable().Reverse())
                try { MoveEntry(Path.Combine(newDir, name), Path.Combine(oldDir, name)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            // A title saved under both had one side deleted to make way for the other: it comes back from the backup.
            try { RestoreKept(record, oldDir, newDir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                log($"{s.Emulator}: could not restore a replaced save from {record.Backup}: {e.Message}");
            }
            await Untell(api!, told, log);
            return $"Could not move {s.Emulator}'s saves to the new user, and put back what had moved (the backup is {record.Backup}): {ex.Message}";
        }

        foreach (var game in moves.Select(m => m.Game).Distinct())
        {
            Retarget(game, oldDir, newDir);
            config.SaveGameFolders(game);
        }
        WriteApplied(config, [.. ReadApplied(config), record]);
        log($"{s.Emulator} ({record.Install}): user {oldId} is now {newId}; its saves moved to {newDir}. Backup: {record.Backup}");
        return null;
    }

    // ---------------------------------------------------------------- putting the original user back

    /// <summary>Put back the user each install here had before it last adopted a shared one, and stop adopting that one here.</summary>
    public static async Task<Outcome> UndoAsync(AgentConfig config, IReadOnlyList<SwitchEmulators.Setup> setups, ApiClient? api,
        GameLock gameLock, Action<string> log, Func<string?>? running = null)
    {
        if (!await Gate.WaitAsync(0)) return new Outcome(Busy, []);
        try { return await UndoGatedAsync(config, setups, api, gameLock, log, running); }
        finally { Gate.Release(); }
    }

    private static async Task<Outcome> UndoGatedAsync(AgentConfig config, IReadOnlyList<SwitchEmulators.Setup> setups, ApiClient? api,
        GameLock gameLock, Action<string> log, Func<string?>? running)
    {
        var applied = ReadApplied(config);
        var work = Forks(setups)
            .Select(s => (Setup: s, Record: LastApplied(applied, s)))
            .Where(w => w.Record is not null && SwitchEmulators.Users(w.Setup).FirstOrDefault() is { } u && Is(u.Id, w.Record.NewId))
            .Select(w => (w.Setup, Record: w.Record!)).ToList();
        if (work.Count == 0) return new Outcome("Nothing here was changed to a shared user.", []);
        if ((running ?? SwitchCopies.RunningEmulator)() is { } open) return new Outcome($"{open} is running. Close it first.", []);
        var games = config.Games.Where(g => work.Any(w => w.Record.Moved.Any(m => Moves(g, Path.Combine(UserDir(w.Setup, w.Record.NewId), m))))).ToList();
        if (games.Count > 0 && api is null)
            return new Outcome("This machine isn't connected to a server, which keeps each machine's save folders.", []);

        var (ran, refusal) = await UnderLocks(gameLock, games, async () =>
        {
            foreach (var (setup, record) in work)
                if (await PutBackAsync(config, setup, record, api, log) is { } why) return why;
            return null;
        });
        if (!ran) return new Outcome($"A sync of {string.Join(", ", games.Select(g => g.Name))} is running. Try again when it's done.", []);
        if (refusal is not null) return new Outcome(refusal, []);
        var declined = work[0].Record.NewId;
        config.UpdateSettings(c => c.SwitchSharedUserDeclined = declined);
        return new Outcome(null, [.. work.Select(w => $"{w.Setup.Emulator} ({w.Record.Install}) has its original user again.")]);
    }

    private static async Task<string?> PutBackAsync(AgentConfig config, SwitchEmulators.Setup s, Applied record, ApiClient? api, Action<string> log)
    {
        var oldDir = UserDir(s, record.OldId);
        var newDir = UserDir(s, record.NewId);
        var told = new List<(TrackedGame Game, string Key, string From)>();
        var moves = record.Moved.SelectMany(m => Repoint(config, Path.Combine(newDir, m), Path.Combine(oldDir, m))).ToList();
        try
        {
            foreach (var m in moves)
            {
                await api!.SetMachinePathAsync(m.Game.GameId, m.To, m.Key);
                told.Add((m.Game, m.Key, m.From));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Untell(api!, told, log);
            return $"Could not tell the server about the folders, so nothing changed: {ex.Message}";
        }

        try
        {
            Directory.CreateDirectory(oldDir);
            foreach (var name in record.Moved)
                if (Exists(Path.Combine(newDir, name)) && !Exists(Path.Combine(oldDir, name)))
                    MoveEntry(Path.Combine(newDir, name), Path.Combine(oldDir, name));
            RestoreKept(record, oldDir, newDir);
            using (var zip = ZipFile.OpenRead(record.Backup))
            {
                if (zip.GetEntry("profiles.dat") is { } profiles)
                {
                    using var src = profiles.Open();
                    using var buffer = new MemoryStream();
                    src.CopyTo(buffer);
                    AtomicFile.WriteAllBytes(record.Profiles, buffer.ToArray());
                }
            }
            var avatars = Path.GetDirectoryName(record.Profiles)!;
            foreach (var pair in record.Avatars)
                if (File.Exists(Path.Combine(avatars, pair[1])) && !File.Exists(Path.Combine(avatars, pair[0])))
                    File.Move(Path.Combine(avatars, pair[1]), Path.Combine(avatars, pair[0]));
            if (Directory.Exists(newDir) && !Children(newDir).Any()) Directory.Delete(newDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await Untell(api!, told, log);
            return $"Could not put {s.Emulator}'s user back ({ex.Message}). Its backup is {record.Backup}.";
        }

        foreach (var game in moves.Select(m => m.Game).Distinct())
        {
            foreach (var name in record.Moved) Retarget(game, Path.Combine(newDir, name), Path.Combine(oldDir, name));
            config.SaveGameFolders(game);
        }
        WriteApplied(config, [.. ReadApplied(config).Where(a => !(SameSaves(a.Saves, record.Saves) && a.At == record.At))]);
        log($"{s.Emulator} ({record.Install}): user {record.NewId} is {record.OldId} again; its saves are back in {oldDir}.");
        return null;
    }

    // ---------------------------------------------------------------- locks, folders, files

    /// <summary>Run <paramref name="work"/> holding every game's sync lock, taken one after another.</summary>
    public static async Task<(bool Ran, string? Result)> UnderLocks(GameLock gameLock, IReadOnlyList<TrackedGame> games, Func<Task<string?>> work)
    {
        if (games.Count == 0) return (true, await work());
        var inner = true;
        var (ran, result) = await gameLock(games[0], async () =>
        {
            var (r, x) = await UnderLocks(gameLock, games.Skip(1).ToList(), work);
            inner = r;
            return x;
        });
        return (ran && inner, result);
    }

    /// <summary>A sync engine's game lock, waiting a few seconds for a sync in progress.</summary>
    public static GameLock LocksOf(SyncEngine engine) =>
        (game, work) => engine.WithGameLockAsync(game, work, TimeSpan.FromSeconds(5));

    public static string UserDir(SwitchEmulators.Setup s, string userId) => Path.Combine(s.Saves, UsersFolder, userId.ToUpperInvariant());

    private static IEnumerable<SwitchEmulators.Setup> Forks(IReadOnlyList<SwitchEmulators.Setup> setups) =>
        setups.Where(s => !s.IsRyujinx && s.Profiles is not null);

    private static string InstallOf(SwitchEmulators.Setup s) => s.EmuDeck ? "EmuDeck" : "Standalone";

    private static Applied? LastApplied(IEnumerable<Applied> applied, SwitchEmulators.Setup s) =>
        applied.Where(a => SameSaves(a.Saves, s.Saves)).OrderBy(a => a.At).LastOrDefault();

    /// <summary>Each folder of a game under <paramref name="from"/>, and where it goes under <paramref name="to"/>.</summary>
    private static List<(TrackedGame Game, string Key, string From, string To)> Repoint(AgentConfig config, string from, string to)
    {
        var moves = new List<(TrackedGame, string, string, string)>();
        foreach (var g in config.Games)
        {
            if (Under(g.SaveDirectory, from) is { } rest) moves.Add((g, SaveRoot.PrimaryKey, g.SaveDirectory!, to + rest));
            foreach (var p in g.ExtraPaths)
                if (Under(p.Directory, from) is { } r) moves.Add((g, p.Key, p.Directory!, to + r));
        }
        return moves;
    }

    private static void Retarget(TrackedGame game, string from, string to)
    {
        if (Under(game.SaveDirectory, from) is { } rest) game.SaveDirectory = to + rest;
        foreach (var p in game.ExtraPaths)
            if (Under(p.Directory, from) is { } r) p.Directory = to + r;
    }

    private static bool Moves(TrackedGame g, string from) =>
        Under(g.SaveDirectory, from) is not null || g.ExtraPaths.Any(p => Under(p.Directory, from) is not null);

    /// <summary>The rest of <paramref name="path"/> after <paramref name="dir"/> ("" or a separator and more), or null when
    /// it isn't inside it.</summary>
    private static string? Under(string? path, string dir)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var p = Path.TrimEndingDirectorySeparator(path);
        var d = Path.TrimEndingDirectorySeparator(dir);
        if (string.Equals(p, d, Comparison)) return "";
        return p.Length > d.Length && p.StartsWith(d, Comparison) && p[d.Length] is '/' or '\\' ? p[d.Length..] : null;
    }

    private static async Task Untell(ApiClient api, List<(TrackedGame Game, string Key, string From)> told, Action<string> log)
    {
        foreach (var (game, key, from) in told)
            try { await api.SetMachinePathAsync(game.GameId, from, key); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                log($"[{game.Name}] could not put the server's folder back to {from}: {ex.Message}");
            }
    }

    /// <summary>Zip <c>profiles.dat</c>, the old user's saves (<c>old/</c>) and the shared user's saves about to be replaced
    /// (<c>shared/&lt;title&gt;</c>).</summary>
    private static string Backup(AgentConfig config, SwitchEmulators.Setup s, string oldDir, string newDir, IEnumerable<string> replaced)
    {
        var dir = Path.Combine(config.StateDir, "switch-users");
        Directory.CreateDirectory(dir);
        var zipPath = Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{s.Emulator}-{InstallOf(s)}-{Guid.NewGuid():N}"[..48] + ".zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        if (File.Exists(s.Profiles)) zip.CreateEntryFromFile(s.Profiles!, "profiles.dat");
        AddTree(zip, oldDir, "old");
        foreach (var title in replaced) AddTree(zip, Path.Combine(newDir, title), $"shared/{title.ToUpperInvariant()}");
        return zipPath;
    }

    private static void AddTree(ZipArchive zip, string dir, string prefix)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            zip.CreateEntryFromFile(f, prefix + "/" + Path.GetRelativePath(dir, f).Replace('\\', '/'));
    }

    /// <summary>Each title saved under both users back on the side it was taken from: a title kept from here put the shared
    /// user's own save aside, one kept from the shared user this one's.</summary>
    private static void RestoreKept(Applied record, string oldDir, string newDir)
    {
        if (record.Kept.Count == 0) return;
        using var zip = ZipFile.OpenRead(record.Backup);
        foreach (var (title, kept) in record.Kept)
            if (kept == Here) Extract(zip, $"shared/{title}/", Path.Combine(newDir, title));
            else Extract(zip, $"old/{title}/", Path.Combine(oldDir, title));
    }

    private static void Extract(ZipArchive zip, string prefix, string dir)
    {
        var entries = zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && e.Name.Length > 0).ToList();
        if (entries.Count == 0) return;
        Delete(dir);
        var root = Path.GetFullPath(dir);
        foreach (var e in entries)
        {
            var target = Path.GetFullPath(Path.Combine(root, e.FullName[prefix.Length..]));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, Comparison)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>The current user's entry in <c>profiles.dat</c> (0x10 header, 0xC8-byte users: UUID, its copy, …) takes
    /// <paramref name="newId"/>: the save folder name, so its bytes reversed.</summary>
    public static void RewriteProfile(string profilesDat, string oldId, string newId)
    {
        var data = File.ReadAllBytes(profilesDat);
        var oldUuid = Uuid(oldId);
        var newUuid = Uuid(newId);
        for (var at = 0x10; at + 0xC8 <= data.Length; at += 0xC8)
        {
            if (!data.AsSpan(at, 16).SequenceEqual(oldUuid)) continue;
            newUuid.CopyTo(data, at);
            newUuid.CopyTo(data, at + 16);
            AtomicFile.WriteAllBytes(profilesDat, data);
            return;
        }
        throw new IOException($"{Path.GetFileName(profilesDat)} has no user {oldId}.");
    }

    // An avatar is named by the user's UUID, in whichever byte order and dash layout the emulator wrote; renamed in kind.
    private static List<string[]> RenameAvatars(string profilesDat, string oldId, string newId)
    {
        var renamed = new List<string[]>();
        var dir = Path.GetDirectoryName(profilesDat)!;
        var forms = new[] { (Old: oldId, New: newId), (Old: Convert.ToHexString(Uuid(oldId)), New: Convert.ToHexString(Uuid(newId))) };
        foreach (var file in Directory.EnumerateFiles(dir, "*.jpg"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var bare = stem.Replace("-", "");
            if (forms.FirstOrDefault(f => string.Equals(f.Old, bare, StringComparison.OrdinalIgnoreCase)) is not { Old: not null } form) continue;
            var hex = char.IsLower(bare.FirstOrDefault(char.IsLetter)) ? form.New.ToLowerInvariant() : form.New.ToUpperInvariant();
            var name = new System.Text.StringBuilder();
            var i = 0;
            foreach (var ch in stem) name.Append(ch == '-' ? '-' : hex[i++]);
            var to = Path.Combine(dir, name + ".jpg");
            if (File.Exists(to)) continue;
            File.Move(file, to);
            renamed.Add([Path.GetFileName(file), Path.GetFileName(to)]);
        }
        return renamed;
    }

    private static byte[] Uuid(string folderId)
    {
        var bytes = Convert.FromHexString(folderId);
        Array.Reverse(bytes);
        return bytes;
    }

    private static IEnumerable<(string Name, string Path, bool IsDir)> Children(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        return new DirectoryInfo(dir).EnumerateFileSystemInfos().Select(i => (i.Name, i.FullName, i is DirectoryInfo)).ToList();
    }

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    private static void MoveEntry(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }

    private static DateTime? Written(string dir) => SwitchEmulators.Written(dir) is var w && w == DateTime.MinValue ? null : w;

    private static string? GameName(AgentConfig config, string titleId) =>
        config.Games.FirstOrDefault(g => Is(SwitchCopies.TitleId(g), titleId))?.Name;

    private static string AppliedPath(AgentConfig config) => Path.Combine(config.StateDir, "switch-users", "applied.json");

    private static List<Applied> ReadApplied(AgentConfig config)
    {
        try
        {
            var path = AppliedPath(config);
            return File.Exists(path) ? JsonSerializer.Deserialize<List<Applied>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    private static void WriteApplied(AgentConfig config, List<Applied> applied)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppliedPath(config))!);
        AtomicFile.WriteAllText(AppliedPath(config), JsonSerializer.Serialize(applied, JsonOpts));
    }

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool SameSaves(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), Comparison);
    private static bool Is(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
