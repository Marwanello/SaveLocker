using System.Diagnostics;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// One Switch game in several emulators on the same machine (tasks/emulator-saves Phase 6, the maintainer's design,
/// 2026-10-13): one emulator's copy syncs with the server — the <b>synced copy</b> — and every other Switch emulator here
/// that has the title, or can take it, is one of the game's <b>copies</b>. A copy can be given the synced save once, kept
/// in step, or made the synced copy itself.
/// <para>
/// Keeping a copy in step: whichever side changed since the two were last the same (<see cref="EmulatorCopy.LastHash"/>)
/// is copied over the other, so the emulator played last wins; when both changed it is never decided here — the copy is
/// marked <see cref="EmulatorCopy.Conflict"/> and the user picks. Nothing is copied while any Switch emulator runs, and the
/// side about to be overwritten is zipped first under <c>&lt;state&gt;/emulator-copies</c> (five per game kept). It runs
/// before a push and a pull (a copy played since is taken in first, so a pull still refuses to overwrite it), after them,
/// and on the poller's tick; a copy taken into the synced folder is then pushed by that folder's own watcher.
/// </para>
/// <para>
/// No profile is copied: Yuzu forks file a save under the profile's folder, Ryujinx under its index entry, so a copy goes
/// under the target emulator's current user, which the page shows — a device save in its device save. Most saves don't name their user, but some do: Animal
/// Crossing's island links each resident to a user ID (<c>main.dat</c>, read 2026-10-16), and asks to link them again on an
/// emulator whose user has another ID.
/// </para>
/// </summary>
public static class SwitchCopies
{
    public const int BackupsKept = 5;

    /// <summary>One emulator's row on the game's Emulator settings page. <see cref="TellWhenReady"/>: the user is told once
    /// it can sync the game (<see cref="SetTellWhenReady"/>). <see cref="Saves"/> is the install's saves folder, which tells
    /// two installs of one emulator apart (EmuDeck's and a standalone one), and <see cref="Install"/> says which it is.
    /// <see cref="GameVersion"/>: the version of the game the install runs (<see cref="SwitchGameVersion"/>), when known.</summary>
    public sealed record Row(
        string Emulator, string? User, string? Folder, bool Synced, string State, DateTime? WrittenUtc,
        bool KeepInSync, bool CanCopyHere, bool CanMakeSynced, string? Note, bool TellWhenReady = false,
        string? Saves = null, string? Install = null, string? GameVersion = null);

    /// <summary>What a copy is, next to the synced one.</summary>
    public static class States
    {
        public const string Synced = "synced";
        public const string Same = "same";
        public const string Newer = "newer";
        public const string Older = "older";
        public const string Empty = "empty";
        public const string NoSlot = "no-slot";
        /// <summary>Not on this machine: a row only for one the user is waiting for.</summary>
        public const string NotInstalled = "not-installed";
        public const string Conflict = "conflict";
    }

    /// <summary>The Switch title a game is, or null for any other game.</summary>
    public static string? TitleId(TrackedGame game)
    {
        if (game.EmulatorKey is { } key && EmulatorKeys.Clean(key) is { } clean) return clean[EmulatorKeys.SwitchPrefix.Length..];
        return game.Source is { Kind: GameSourceKinds.Emulator } s && SwitchEmulators.IsSwitch(s.Detail) &&
               game.Release?.GameIds is [var id, ..] && SwitchEmulators.IsTitleId(id) ? id.ToUpperInvariant() : null;
    }

    /// <summary>Every Switch emulator here, for this game: the synced copy first.</summary>
    public static IReadOnlyList<Row> List(AgentConfig config, TrackedGame game, IReadOnlyList<SwitchEmulators.Setup>? setups = null)
    {
        if (TitleId(game) is not { } title) return [];
        setups ??= SwitchEmulators.Find();
        var synced = SyncedSetup(game, setups);
        var syncedWritten = game.Roots(config.StateDir).Select(r => SwitchEmulators.Written(r.Directory)).DefaultIfEmpty(DateTime.MinValue).Max();
        var rows = new List<Row>();
        foreach (var s in setups)
        {
            var slot = SlotFor(s, title, game, setups);
            var saved = game.EmulatorCopies.FirstOrDefault(c => IsCopyOf(c, s));
            if (s == synced)
            {
                rows.Insert(0, new Row(s.Emulator, slot?.UserName, game.SaveDirectory, true, States.Synced,
                    syncedWritten == DateTime.MinValue ? null : syncedWritten, false, false, false, null, false, s.Saves, InstallOf(s),
                    SwitchGameVersion.Of(s, title)));
                continue;
            }
            if (slot is null)
            {
                rows.Add(new Row(s.Emulator, null, null, false, States.NoSlot, null, saved?.KeepInSync ?? false, false, false,
                    s.IsRyujinx ? $"Start the game once in {s.Emulator}: it makes the game's save folder the first time." : "No user in this emulator yet.",
                    saved?.TellWhenReady ?? false, s.Saves, InstallOf(s), SwitchGameVersion.Of(s, title)));
                continue;
            }
            var (mine, theirs) = Pair(game.Roots(config.StateDir), CopyRoots(s, title, slot, game));
            var hash = slot.Exists ? Hash(theirs) : null;
            var state = saved?.Conflict == true ? States.Conflict
                : hash is null ? States.Empty
                : hash == Hash(mine) ? States.Same
                : slot.WrittenUtc > syncedWritten ? States.Newer : States.Older;
            rows.Add(new Row(s.Emulator, slot.UserName, slot.Dir, false, state, slot.Exists ? slot.WrittenUtc : null,
                saved?.KeepInSync ?? false, state != States.Same, slot.Exists, null, false, s.Saves, InstallOf(s), SwitchGameVersion.Of(s, title)));
        }
        foreach (var waiting in game.EmulatorCopies.Where(c => c.TellWhenReady && !setups.Any(s => Is(s.Emulator, c.Emulator))))
            rows.Add(new Row(waiting.Emulator, null, null, false, States.NotInstalled, null, false, false, false, null, true));
        return rows;
    }

    /// <summary>
    /// Ask to be told, or stop, once <paramref name="emulator"/> can sync this game here: installed, and with a save folder
    /// for it (Ryujinx makes one the first time it starts the game). Null when set, else why not.
    /// </summary>
    public static string? SetTellWhenReady(AgentConfig config, TrackedGame game, string emulator, bool tell)
    {
        if (!SwitchEmulators.IsSwitch(emulator)) return $"{emulator} isn't a Switch emulator SaveLocker knows.";
        var name = SwitchEmulators.Names.First(n => Is(n, emulator));
        var copies = game.EmulatorCopies.Select(c => c with { }).ToList();
        var copy = copies.FirstOrDefault(c => Is(c.Emulator, name));
        if (copy is null)
        {
            if (!tell) return null;
            copies.Add(copy = new EmulatorCopy { Emulator = name });
        }
        copy.TellWhenReady = tell;
        // A row kept only for the waiting has nothing left to say.
        if (!tell && !copy.KeepInSync && copy.LastHash is null && !copy.Conflict) copies.Remove(copy);
        Save(config, game, copies);
        return null;
    }

    /// <summary>The emulators the user waits for that can now sync this game; each is told once, so the flag is cleared.</summary>
    public static IReadOnlyList<string> NowReady(AgentConfig config, TrackedGame game, IReadOnlyList<SwitchEmulators.Setup>? setups = null)
    {
        if (TitleId(game) is not { } title || !game.EmulatorCopies.Any(c => c.TellWhenReady)) return [];
        setups ??= SwitchEmulators.Find();
        var ready = new List<string>();
        var copies = game.EmulatorCopies.Select(c => c with { }).ToList();
        foreach (var copy in copies.Where(c => c.TellWhenReady))
            if (setups.FirstOrDefault(s => Is(s.Emulator, copy.Emulator)) is { } setup && SlotFor(setup, title, game, setups) is not null)
            {
                copy.TellWhenReady = false;
                ready.Add(setup.Emulator);
            }
        if (ready.Count > 0) Save(config, game, copies);
        return ready;
    }

    /// <summary>The emulator whose copy syncs, or null when none here holds the game's folder.</summary>
    public static string? SyncedEmulator(TrackedGame game, IReadOnlyList<SwitchEmulators.Setup>? setups = null) =>
        SyncedSetup(game, setups ?? SwitchEmulators.Find())?.Emulator;

    /// <summary>Is <paramref name="emulator"/> (the install whose saves folder is <paramref name="saves"/>, when given) the
    /// one whose copy syncs?</summary>
    public static bool IsSynced(TrackedGame game, string emulator, string? saves, IReadOnlyList<SwitchEmulators.Setup>? setups = null) =>
        SyncedSetup(game, setups ?? SwitchEmulators.Find()) is { } s && Is(s.Emulator, emulator) &&
        (saves is null || string.Equals(Normalize(s.Saves), Normalize(saves), Comparison));

    /// <summary>Zip the synced copy as it is, beside the copies' own backups, before something replaces it.</summary>
    public static void BackupSynced(AgentConfig config, TrackedGame game, string label)
    {
        var roots = game.Roots(config.StateDir).Select(r => r with { IncludeGlobs = null }).ToList();
        if (roots.Any(r => SwitchEmulators.HasFiles(r.Directory))) Backup(config, game, roots, label);
    }

    /// <summary>Copy the synced save to <paramref name="emulator"/>'s copy now. Null when done, else why not.</summary>
    public static string? CopyHere(AgentConfig config, TrackedGame game, string emulator, Action<string> log,
        IReadOnlyList<SwitchEmulators.Setup>? setups = null, string? saves = null) =>
        Transfer(config, game, emulator, toCopy: true, log, setups, saves);

    /// <summary>Keep the side the user chose of a copy marked <see cref="EmulatorCopy.Conflict"/>: <paramref name="keepCopy"/>
    /// takes the copy into the synced folder, otherwise the synced save goes to the copy.</summary>
    public static string? Resolve(AgentConfig config, TrackedGame game, string emulator, bool keepCopy, Action<string> log,
        IReadOnlyList<SwitchEmulators.Setup>? setups = null, string? saves = null) =>
        Transfer(config, game, emulator, toCopy: !keepCopy, log, setups, saves);

    /// <summary>
    /// Turn keeping <paramref name="emulator"/>'s copy in step on or off. Turned on, the two count as last the same as the
    /// side played longer ago, so the next reconcile takes the one played last — what the page promises — rather than
    /// asking about two saves that were never made the same.
    /// </summary>
    public static void SetKeepInSync(AgentConfig config, TrackedGame game, string emulator, bool keep,
        IReadOnlyList<SwitchEmulators.Setup>? setups = null, string? saves = null)
    {
        // Not told which install: the one already recorded for the emulator, not whichever is found first.
        saves ??= game.EmulatorCopies.FirstOrDefault(c => Is(c.Emulator, emulator) && c.Saves is not null)?.Saves;
        setups ??= SwitchEmulators.Find();
        var setup = Pick(setups, emulator, saves);
        var copies = game.EmulatorCopies.Select(c => c with { }).ToList();
        EmulatorCopy copy;
        if (setup is not null) copy = CopyFor(copies, setup);
        else if (copies.FirstOrDefault(c => Is(c.Emulator, emulator)) is { } named) copy = named;
        else copies.Add(copy = new EmulatorCopy { Emulator = emulator, Saves = saves });
        if (keep && !copy.KeepInSync && !copy.Conflict && TitleId(game) is { } title)
        {
            if (setup is not null && SlotFor(setup, title, game, setups) is { Exists: true } slot)
            {
                var (mine, theirs) = Pair(game.Roots(config.StateDir), CopyRoots(setup, title, slot, game));
                var syncedWritten = mine.Select(r => SwitchEmulators.Written(r.Directory)).DefaultIfEmpty(DateTime.MinValue).Max();
                copy.LastHash = slot.WrittenUtc > syncedWritten ? Hash(mine) : Hash(theirs);
            }
        }
        copy.KeepInSync = keep;
        Save(config, game, copies);
    }

    /// <summary>
    /// Make <paramref name="emulator"/>'s copy the one that syncs: the game's folders here move to it and the server is told;
    /// the old synced copy becomes a copy, kept in step if this one was. Its save is pushed by the next sync like any change.
    /// The server is told first and nothing changes here when it can't be: the server's folder for this machine is what the
    /// poller applies, so a move only made here would be undone on its next tick.
    /// </summary>
    public static async Task<string?> MakeSyncedAsync(AgentConfig config, TrackedGame game, string emulator, ApiClient? api, Action<string> log,
        IReadOnlyList<SwitchEmulators.Setup>? setups = null, string? saves = null)
    {
        if (TitleId(game) is not { } title) return "this is not a Switch game.";
        setups ??= SwitchEmulators.Find();
        var target = Pick(setups, emulator, saves);
        if (target is null || SlotFor(target, title, game, setups) is not { } slot) return $"{emulator} has no save folder for this game.";
        var device = game.ExtraPaths.FirstOrDefault(p => p.Key == SwitchEmulators.DeviceFolderKey);
        var deviceDir = DeviceDir(target, title, slot);
        if (device is not null && deviceDir is null)
            return $"{emulator} has no device save for this game yet, and the game keeps one. Start the game in {emulator} once.";
        if (RunningEmulator() is { } running) return $"{running} is running. Close it first.";
        if (api is null) return "this machine isn't connected to a server, which keeps each machine's folder.";
        var source = new GameSourceDto(GameSourceKinds.Emulator, target.Emulator, target.EmuDeck ? ["Switch", "EmuDeck"] : ["Switch"]);
        var movedThere = false;
        try
        {
            await api.SetMachinePathAsync(game.GameId, slot.Dir);
            movedThere = true;
            if (device is not null) await api.SetMachinePathAsync(game.GameId, deviceDir!, SwitchEmulators.DeviceFolderKey);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Half told: put the main folder back, or the poller would move only it.
            if (movedThere)
                try { await api.SetMachinePathAsync(game.GameId, game.SaveDirectory); }
                catch (Exception back) when (back is HttpRequestException or TaskCanceledException)
                {
                    log($"[{game.Name}] could not put the server's folder back to {game.SaveDirectory}: {back.Message}");
                }
            return $"could not tell the server about the new folder, so nothing changed: {ex.Message}";
        }
        var old = SyncedSetup(game, setups);

        var copies = game.EmulatorCopies.Select(c => c with { }).Where(c => !IsCopyOf(c, target)).ToList();
        var was = game.EmulatorCopies.FirstOrDefault(c => IsCopyOf(c, target));
        if (old is not null && copies.All(c => !IsCopyOf(c, old)))
            copies.Add(new EmulatorCopy { Emulator = old.Emulator, Saves = old.Saves, KeepInSync = was?.KeepInSync ?? false, LastHash = was?.LastHash });

        game.SaveDirectory = slot.Dir;
        if (device is not null) device.Directory = deviceDir;
        config.SaveGameFolders(game);
        Save(config, game, copies);
        config.SaveGameSource(game.GameId, source);
        game.Source = source;
        log($"[{game.Name}] {target.Emulator}'s copy is now the one that syncs ({slot.Dir}).");
        // The poller re-sends a source the server's copy differs from, so this one may fail.
        try { await api.SetGameSourceAsync(game.GameId, source); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log($"[{game.Name}] could not tell the server the game's new emulator yet: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Bring every copy kept in step up to date, in whichever direction it needs (see the class). Returns what it did, one
    /// line each; nothing while a Switch emulator runs. Safe to call often: an up-to-date copy costs two hashes.
    /// </summary>
    public static IReadOnlyList<string> Reconcile(AgentConfig config, TrackedGame game, Action<string> log, IReadOnlyList<SwitchEmulators.Setup>? setups = null)
    {
        if (TitleId(game) is not { } title || !game.EmulatorCopies.Any(c => c.KeepInSync && !c.Conflict)) return [];
        if (string.IsNullOrEmpty(game.SaveDirectory) || RunningEmulator() is not null) return [];
        setups ??= SwitchEmulators.Find();
        var done = new List<string>();
        var copies = game.EmulatorCopies.Select(c => c with { }).ToList();
        var changed = false;
        foreach (var copy in copies.Where(c => c.KeepInSync && !c.Conflict))
        {
            var synced = SyncedSetup(game, setups);
            var setup = setups.FirstOrDefault(s => IsCopyOf(copy, s) && s != synced);
            if (setup is null || SlotFor(setup, title, game, setups) is not { } slot) continue;
            var (syncedRoots, copyRoots) = Pair(game.Roots(config.StateDir), CopyRoots(setup, title, slot, game));
            var syncedHash = Hash(syncedRoots);
            var copyHash = slot.Exists ? Hash(copyRoots) : null;
            if (copyHash == syncedHash)
            {
                if (copy.LastHash != syncedHash) { copy.LastHash = syncedHash; changed = true; }
                continue;
            }
            var syncedChanged = syncedHash != copy.LastHash;
            var copyChanged = copyHash is not null && copyHash != copy.LastHash;
            string? why;
            string? copied;
            if (copyHash is null || (syncedChanged && !copyChanged))
                why = Copy(config, game, syncedRoots, copyRoots, copy.Emulator, log, out copied);
            else if (copyChanged && !syncedChanged)
                why = Copy(config, game, copyRoots, syncedRoots, "synced", log, out copied);
            else
            {
                copy.Conflict = true;
                changed = true;
                var line = $"[{game.Name}] {copy.Emulator}'s copy and the synced copy both changed since they were last the same. " +
                           "Pick which to keep on the game's Emulator settings page.";
                log(line);
                done.Add(line);
                continue;
            }
            changed = true;
            if (why is null) copy.LastHash = copied;
            if (why is null) done.Add(copyHash is null || (syncedChanged && !copyChanged)
                ? $"[{game.Name}] copied the synced save to {copy.Emulator}."
                : $"[{game.Name}] took {copy.Emulator}'s newer save into the synced copy.");
            else log($"[{game.Name}] could not keep {copy.Emulator}'s copy in step: {why}");
        }
        if (changed) Save(config, game, copies);
        return done;
    }

    /// <summary>The first Switch emulator running here, or null. A copy is never written while one is: it would load or
    /// write over it.</summary>
    public static string? RunningEmulator()
    {
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (SwitchEmulators.Names.FirstOrDefault(n => IsProcessOf(name, n)) is { } emulator)
                    return emulator;
            }
            catch { /* it exited meanwhile */ }
            finally { p.Dispose(); }
        }
        return null;
    }

    /// <summary>The emulator's own process: its name, or its name and a suffix — <c>yuzu-cmd</c>, <c>Eden.AppImage</c>
    /// (Linux shortens a process name to 15 characters, which keeps the dot).</summary>
    internal static bool IsProcessOf(string process, string emulator) =>
        process.Equals(emulator, StringComparison.OrdinalIgnoreCase) ||
        (process.StartsWith(emulator, StringComparison.OrdinalIgnoreCase) && process.Length > emulator.Length &&
         process[emulator.Length] is '.' or '-' or '_');

    /// <summary>After a pull into Ryujinx's committed copy (<c>&lt;save ID&gt;/0</c>), its working copy (<c>1</c>) is made the
    /// same, so Ryujinx finds the pulled save whichever one it opens.</summary>
    public static void AfterPull(TrackedGame game, Action<string> log)
    {
        foreach (var dir in new[] { game.SaveDirectory }.Concat(game.ExtraPaths.Where(p => p.Key == SwitchEmulators.DeviceFolderKey).Select(p => p.Directory)))
            if (dir is not null && RyujinxWorking(dir) is { } working)
            {
                try { Mirror(dir, working); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log($"[{game.Name}] could not update Ryujinx's working copy {working}: {ex.Message}");
                }
            }
    }

    // ---------------------------------------------------------------- the copying

    private static string? Transfer(AgentConfig config, TrackedGame game, string emulator, bool toCopy, Action<string> log,
        IReadOnlyList<SwitchEmulators.Setup>? setups, string? saves)
    {
        if (TitleId(game) is not { } title) return "this is not a Switch game.";
        saves ??= game.EmulatorCopies.FirstOrDefault(c => Is(c.Emulator, emulator) && c.Saves is not null)?.Saves;
        setups ??= SwitchEmulators.Find();
        var setup = Pick(setups, emulator, saves);
        if (setup is null) return $"{emulator} isn't on this machine.";
        if (setup == SyncedSetup(game, setups)) return $"{emulator}'s copy is the one that syncs.";
        if (SlotFor(setup, title, game, setups) is not { } slot)
            return setup.IsRyujinx ? $"{emulator} has no save folder for this game yet. Start the game in it once." : $"{emulator} has no user yet.";
        if (!toCopy && !slot.Exists) return $"{emulator} has no save of this game.";
        if (RunningEmulator() is { } running) return $"{running} is running. Close it first.";

        var copies = game.EmulatorCopies.Select(c => c with { }).ToList();
        var copy = CopyFor(copies, setup);
        var (syncedRoots, copyRoots) = Pair(game.Roots(config.StateDir), CopyRoots(setup, title, slot, game));
        var why = toCopy
            ? Copy(config, game, syncedRoots, copyRoots, emulator, log, out var hash)
            : Copy(config, game, copyRoots, syncedRoots, "synced", log, out hash);
        if (why is not null) return why;
        copy.LastHash = hash;
        copy.Conflict = false;
        Save(config, game, copies);
        return null;
    }

    /// <summary>Make <paramref name="to"/> hold exactly <paramref name="from"/>'s files, folder by folder (by key — the two
    /// come from <see cref="Pair"/>), after zipping what <paramref name="to"/> had. Null when done.</summary>
    private static string? Copy(AgentConfig config, TrackedGame game, IReadOnlyList<SaveRoot> from, IReadOnlyList<SaveRoot> to,
        string label, Action<string> log, out string? hash)
    {
        hash = null;
        var temp = Path.Combine(config.StateDir, "emulator-copies", "tmp");
        var empty = Path.Combine(temp, $"empty-{Guid.NewGuid():N}");
        try
        {
            // A folder empty on both sides is left alone, so no emulator is handed an empty save folder it never made.
            var keys = from.Where(r => r.IsPrimary || AnyFiles(r.Directory) || to.Any(t => t.Key == r.Key && AnyFiles(t.Directory)))
                .Select(r => r.Key).ToHashSet();
            // One with no folder on the source side is copied as an empty one, so the target's files go too.
            var source = from.Where(r => keys.Contains(r.Key))
                .Select(r => Directory.Exists(r.Directory) ? r : r with { Directory = Path.Combine(empty, r.IsPrimary ? "main" : r.Key) })
                .Select(r => r with { IncludeGlobs = null }).ToList();
            var target = to.Where(r => keys.Contains(r.Key)).Select(r => r with { IncludeGlobs = null }).ToList();
            foreach (var r in source) Directory.CreateDirectory(r.Directory);
            if (target.Any(r => SwitchEmulators.HasFiles(r.Directory)))
                Backup(config, game, target, label);
            var zip = Path.Combine(temp, $"{game.GameId:N}-{Guid.NewGuid():N}.zip");
            try
            {
                SaveArchive.CreateArchive(source, zip);
                foreach (var r in target) Directory.CreateDirectory(r.Directory);
                SaveArchive.RestoreArchive(zip, target, temp);
            }
            finally { try { File.Delete(zip); } catch (IOException) { } }
            foreach (var r in target)
                if (RyujinxWorking(r.Directory) is { } working) Mirror(r.Directory, working);
            hash = Hash(to);
            log($"[{game.Name}] copied {Describe(from)} to {Describe(to)}.");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SaveArchive.UnsafeArchiveException)
        {
            return ex.Message;
        }
        finally { try { if (Directory.Exists(empty)) Directory.Delete(empty, recursive: true); } catch (IOException) { } }
    }

    private static string Describe(IReadOnlyList<SaveRoot> roots) => roots.Single(r => r.IsPrimary).Directory;

    private static void Backup(AgentConfig config, TrackedGame game, IReadOnlyList<SaveRoot> roots, string label)
    {
        var dir = Path.Combine(config.StateDir, "emulator-copies", game.GameId.ToString("N"));
        Directory.CreateDirectory(dir);
        var safe = string.Concat(label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'));
        SaveArchive.CreateArchive(roots.Where(r => r.IsPrimary || Directory.Exists(r.Directory)).ToList(),
            Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{safe}.zip"));
        foreach (var old in new DirectoryInfo(dir).GetFiles("*.zip").OrderByDescending(f => f.Name).Skip(BackupsKept))
            try { old.Delete(); } catch (IOException) { }
    }

    /// <summary>Where a copy's folders are: the slot's, and a device folder when the synced game has one.</summary>
    private static List<SaveRoot> CopyRoots(SwitchEmulators.Setup setup, string title, SwitchEmulators.TitleSave slot, TrackedGame game)
    {
        var roots = new List<SaveRoot> { SaveRoot.Primary(slot.Dir) };
        if (game.ExtraPaths.Any(p => p.Key == SwitchEmulators.DeviceFolderKey) && DeviceDir(setup, title, slot) is { } device)
            roots.Add(new SaveRoot(SwitchEmulators.DeviceFolderKey, device));
        return roots;
    }

    /// <summary>
    /// The synced folders and a copy's, key for key: the main folder and each other one both sides have a place for. Every
    /// comparison and copy goes through this, so a folder only one side can hold (a device save Ryujinx hasn't made yet)
    /// never reads as a change the copy can't carry, and is never copied again on every tick.
    /// </summary>
    private static (List<SaveRoot> Synced, List<SaveRoot> Copy) Pair(IReadOnlyList<SaveRoot> synced, IReadOnlyList<SaveRoot> copy)
    {
        bool Both(SaveRoot r) => r.IsPrimary || (synced.Any(s => s.Key == r.Key) && copy.Any(c => c.Key == r.Key));
        return (synced.Where(Both).Select(r => r with { IncludeGlobs = null }).ToList(), copy.Where(Both).ToList());
    }

    /// <summary>
    /// Where <paramref name="setup"/> keeps this game's synced save: <see cref="SwitchEmulators.Slot"/>'s folder, except for a
    /// game that syncs a device save as its one folder (Animal Crossing's island), which goes in the setup's device save — the
    /// one the emulator opens. Under the user it was never loaded: the Deck, 2026-10-18, Eden started a new island beside it.
    /// </summary>
    private static SwitchEmulators.TitleSave? SlotFor(SwitchEmulators.Setup setup, string title, TrackedGame game,
        IReadOnlyList<SwitchEmulators.Setup> setups)
    {
        var slot = SwitchEmulators.Slot(setup, title);
        if (!SyncsDeviceSave(game, title, setups)) return slot;
        var device = setup.IsRyujinx
            ? slot?.DeviceDir ?? (slot?.UserName == SwitchEmulators.DeviceUser ? slot.Dir : null)
            : Path.Combine(setup.Saves, "0000000000000000", SwitchEmulators.DeviceProfile, title);
        if (device is null) return null;
        var exists = SwitchEmulators.HasFiles(device);
        return new SwitchEmulators.TitleSave(setup, title, device, null, SwitchEmulators.DeviceUser, exists,
            exists ? SwitchEmulators.Written(device) : DateTime.MinValue);
    }

    /// <summary>Is the game's main folder a device save, with no device folder beside it — as the fleet keeps it
    /// (<see cref="EmulatorProfiles.SwitchDevice"/>), or as this machine does?</summary>
    private static bool SyncsDeviceSave(TrackedGame game, string title, IReadOnlyList<SwitchEmulators.Setup> setups)
    {
        if (string.IsNullOrEmpty(game.SaveDirectory) || game.ExtraPaths.Any(p => p.Key == SwitchEmulators.DeviceFolderKey)) return false;
        if (game.EmulatorProfile == EmulatorProfiles.SwitchDevice || SwitchEmulators.IsForkDeviceSave(game.SaveDirectory)) return true;
        return HoldingSetup(game, setups) is { IsRyujinx: true } ryujinx && SwitchEmulators.Slot(ryujinx, title) is
            { DeviceDir: null, UserName: SwitchEmulators.DeviceUser } slot &&
            string.Equals(Normalize(slot.Dir), Normalize(game.SaveDirectory), Comparison);
    }

    /// <summary>A Switch game this machine syncs from a Yuzu fork's device save (with files), which the server doesn't know
    /// is one yet: the poller tells it (<see cref="EmulatorProfiles.SwitchDevice"/>), so every machine keeps it there.</summary>
    public static bool ShouldSayDeviceSave(TrackedGame game, GameDto server) =>
        TitleId(game) is not null && server.EmulatorProfile is null && server.EmulatorKey is not null &&
        !(server.ExtraPaths ?? []).Any(p => p.Key == SwitchEmulators.DeviceFolderKey) &&
        !string.IsNullOrEmpty(game.SaveDirectory) && SwitchEmulators.IsForkDeviceSave(game.SaveDirectory) &&
        SwitchEmulators.HasFiles(game.SaveDirectory);

    /// <summary>
    /// A game the fleet syncs as a device save (<see cref="EmulatorProfiles.SwitchDevice"/>) whose folder here is under a Yuzu
    /// fork's user — where a copy went before 2026-10-18, and where the emulator never opens it — moves to that fork's
    /// device save. The server is told first; the device save, when it holds another save, is zipped first, and so is the
    /// old folder, which is then removed so no scan takes it for the game's save again. Null when there was nothing to do
    /// or it was done, else why not. Ryujinx's folders are its index's, so a Ryujinx folder is left for the page's choice.
    /// </summary>
    public static async Task<string?> MoveToDeviceSaveAsync(AgentConfig config, TrackedGame game, ApiClient api, Action<string> log)
    {
        if (TitleId(game) is not { } title || game.EmulatorProfile != EmulatorProfiles.SwitchDevice ||
            string.IsNullOrEmpty(game.SaveDirectory) || game.ExtraPaths.Any(p => p.Key == SwitchEmulators.DeviceFolderKey) ||
            SwitchEmulators.DeviceSaveBeside(game.SaveDirectory, title) is not { } device) return null;
        if (RunningEmulator() is { } running) return $"{running} is running.";
        var old = game.SaveDirectory;
        try { await api.SetMachinePathAsync(game.GameId, device); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"could not tell the server about the new folder, so nothing changed: {ex.Message}";
        }
        try
        {
            List<SaveRoot> deviceRoot = [SaveRoot.Primary(device)], oldRoot = [SaveRoot.Primary(old)];
            if (SwitchEmulators.HasFiles(old))
            {
                if (SwitchEmulators.HasFiles(device) && Hash(deviceRoot) != Hash(oldRoot))
                    Backup(config, game, deviceRoot, "device-save-before-move");
                Backup(config, game, oldRoot, "user-folder-before-move");
                Mirror(old, device);
                Directory.Delete(old, recursive: true);
            }
            else Directory.CreateDirectory(device);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Half moved: this machine keeps syncing the old folder, and so must the server's record, or the poller would
            // apply the device save on its next tick.
            try { await api.SetMachinePathAsync(game.GameId, old); }
            catch (Exception back) when (back is HttpRequestException or TaskCanceledException)
            {
                log($"[{game.Name}] could not put the server's folder back to {old}: {back.Message}");
            }
            return $"could not move its save from {old} to {device}: {ex.Message}";
        }
        game.SaveDirectory = device;
        config.SaveGameFolders(game);
        log($"[{game.Name}] your other devices sync this game's device save, where the emulator opens it, so its save " +
            $"here moved there from the emulator user's folder: {old} → {device}. Backups: " +
            Path.Combine(config.StateDir, "emulator-copies", game.GameId.ToString("N")));
        return null;
    }

    /// <summary>A title's device save folder in a setup: a fork's is known in advance, Ryujinx's only once it made one.</summary>
    private static string? DeviceDir(SwitchEmulators.Setup setup, string title, SwitchEmulators.TitleSave slot) =>
        slot.DeviceDir ?? (setup.IsRyujinx ? null : Path.Combine(setup.Saves, "0000000000000000", SwitchEmulators.DeviceProfile, title));

    private static string? Hash(IReadOnlyList<SaveRoot> roots)
    {
        try
        {
            if (!Directory.Exists(roots.Single(r => r.IsPrimary).Directory)) return null;
            // Another folder with nothing in it hashes as absent: one side's empty folder is no difference between them.
            return SaveArchive.HashDirectory(roots.Where(r => r.IsPrimary || AnyFiles(r.Directory)).Select(r => r with { IncludeGlobs = null }).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool AnyFiles(string dir)
    {
        try { return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The install whose saves folder holds the game's folder, or null — never one picked by name alone.</summary>
    public static SwitchEmulators.Setup? HoldingSetup(TrackedGame game, IReadOnlyList<SwitchEmulators.Setup> setups)
    {
        var dir = Normalize(game.SaveDirectory);
        return setups.FirstOrDefault(s => dir.StartsWith(Normalize(s.Saves) + "/", Comparison));
    }

    /// <summary>The setup whose folder for the title is the game's synced folder here: the install holding it, else one
    /// of the emulator the game says it comes from.</summary>
    private static SwitchEmulators.Setup? SyncedSetup(TrackedGame game, IReadOnlyList<SwitchEmulators.Setup> setups) =>
        HoldingSetup(game, setups) ?? setups.FirstOrDefault(s => Is(s.Emulator, game.Source?.Detail));

    /// <summary>One install of <paramref name="emulator"/>: the one whose saves folder is <paramref name="saves"/> when
    /// given (two installs of one emulator, EmuDeck's and a standalone one), else the first.</summary>
    private static SwitchEmulators.Setup? Pick(IReadOnlyList<SwitchEmulators.Setup> setups, string emulator, string? saves) =>
        setups.FirstOrDefault(s => Is(s.Emulator, emulator) && (saves is null || string.Equals(Normalize(s.Saves), Normalize(saves), Comparison)));

    /// <summary>Is <paramref name="c"/> the record of <paramref name="s"/>'s copy? One recorded before installs were told apart
    /// (no <see cref="EmulatorCopy.Saves"/>) is any install of the emulator.</summary>
    private static bool IsCopyOf(EmulatorCopy c, SwitchEmulators.Setup s) =>
        Is(c.Emulator, s.Emulator) && (c.Saves is null || string.Equals(Normalize(c.Saves), Normalize(s.Saves), Comparison));

    /// <summary>The record of <paramref name="s"/>'s copy in <paramref name="copies"/>, added when there is none; an old one
    /// with no install learns this one's.</summary>
    private static EmulatorCopy CopyFor(List<EmulatorCopy> copies, SwitchEmulators.Setup s)
    {
        var copy = copies.FirstOrDefault(c => IsCopyOf(c, s) && c.Saves is not null) ?? copies.FirstOrDefault(c => IsCopyOf(c, s));
        if (copy is null) copies.Add(copy = new EmulatorCopy { Emulator = s.Emulator });
        copy.Saves ??= s.Saves;
        return copy;
    }

    private static string InstallOf(SwitchEmulators.Setup s) => s.EmuDeck ? "EmuDeck" : "Standalone";

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static string Normalize(string p) => (p ?? "").Replace('\\', '/').TrimEnd('/');
    private static bool Is(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void Save(AgentConfig config, TrackedGame game, List<EmulatorCopy> copies)
    {
        game.EmulatorCopies = copies;
        try { config.SaveGameEmulatorCopies(game.GameId, copies); }
        catch (AgentStateLockException) { /* retried on the next tick */ }
    }

    /// <summary>Ryujinx's working copy beside a committed one — <c>…/&lt;16 hex&gt;/0</c> → <c>…/1</c> — else null.</summary>
    public static string? RyujinxWorking(string dir)
    {
        var d = dir.TrimEnd('/', '\\');
        var parent = Path.GetDirectoryName(d);
        return Path.GetFileName(d) == "0" && parent is not null && Path.GetFileName(parent) is { Length: 16 } id &&
               id.All(char.IsAsciiHexDigit) && File.Exists(Path.Combine(parent, "ExtraData0"))
            ? Path.Combine(parent, "1") : null;
    }

    /// <summary>Make <paramref name="to"/> a file-for-file copy of <paramref name="from"/>.</summary>
    private static void Mirror(string from, string to)
    {
        Directory.CreateDirectory(to);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(from, file);
            wanted.Add(rel);
            var dest = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
        foreach (var file in Directory.EnumerateFiles(to, "*", SearchOption.AllDirectories).ToList())
            if (!wanted.Contains(Path.GetRelativePath(to, file))) File.Delete(file);
    }
}
