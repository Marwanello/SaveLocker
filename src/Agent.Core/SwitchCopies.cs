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
/// No profile is copied: a Switch save's files do not name their user — Yuzu forks file one under the profile's folder,
/// Ryujinx under its index entry — so a copy goes under the target emulator's current user, which the page shows.
/// </para>
/// </summary>
public static class SwitchCopies
{
    public const int BackupsKept = 5;

    /// <summary>One emulator's row on the game's Emulator settings page.</summary>
    public sealed record Row(
        string Emulator, string? User, string? Folder, bool Synced, string State, DateTime? WrittenUtc,
        bool KeepInSync, bool CanCopyHere, bool CanMakeSynced, string? Note);

    /// <summary>What a copy is, next to the synced one.</summary>
    public static class States
    {
        public const string Synced = "synced";
        public const string Same = "same";
        public const string Newer = "newer";
        public const string Older = "older";
        public const string Empty = "empty";
        public const string NoSlot = "no-slot";
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
        var syncedHash = Hash(game.Roots(config.StateDir));
        var syncedWritten = game.Roots(config.StateDir).Select(r => SwitchEmulators.Written(r.Directory)).DefaultIfEmpty(DateTime.MinValue).Max();
        var rows = new List<Row>();
        foreach (var s in setups)
        {
            var slot = SwitchEmulators.Slot(s, title);
            var saved = game.EmulatorCopies.FirstOrDefault(c => Is(c.Emulator, s.Emulator));
            if (s == synced)
            {
                rows.Insert(0, new Row(s.Emulator, slot?.UserName, game.SaveDirectory, true, States.Synced,
                    syncedWritten == DateTime.MinValue ? null : syncedWritten, false, false, false, null));
                continue;
            }
            if (slot is null)
            {
                rows.Add(new Row(s.Emulator, null, null, false, States.NoSlot, null, saved?.KeepInSync ?? false, false, false,
                    s.IsRyujinx ? $"Start the game once in {s.Emulator}: it makes the game's save folder the first time." : "No user in this emulator yet."));
                continue;
            }
            var hash = slot.Exists ? Hash(Roots(slot)) : null;
            var state = saved?.Conflict == true ? States.Conflict
                : hash is null ? States.Empty
                : hash == syncedHash ? States.Same
                : slot.WrittenUtc > syncedWritten ? States.Newer : States.Older;
            rows.Add(new Row(s.Emulator, slot.UserName, slot.Dir, false, state, slot.Exists ? slot.WrittenUtc : null,
                saved?.KeepInSync ?? false, state != States.Same, slot.Exists, null));
        }
        return rows;
    }

    /// <summary>Copy the synced save to <paramref name="emulator"/>'s copy now. Null when done, else why not.</summary>
    public static string? CopyHere(AgentConfig config, TrackedGame game, string emulator, Action<string> log,
        IReadOnlyList<SwitchEmulators.Setup>? setups = null) =>
        Transfer(config, game, emulator, toCopy: true, log, setups);

    /// <summary>Keep the side the user chose of a copy marked <see cref="EmulatorCopy.Conflict"/>: <paramref name="keepCopy"/>
    /// takes the copy into the synced folder, otherwise the synced save goes to the copy.</summary>
    public static string? Resolve(AgentConfig config, TrackedGame game, string emulator, bool keepCopy, Action<string> log,
        IReadOnlyList<SwitchEmulators.Setup>? setups = null) =>
        Transfer(config, game, emulator, toCopy: !keepCopy, log, setups);

    /// <summary>Turn keeping <paramref name="emulator"/>'s copy in step on or off.</summary>
    public static void SetKeepInSync(AgentConfig config, TrackedGame game, string emulator, bool keep)
    {
        var copies = game.EmulatorCopies.Select(c => c with { }).ToList();
        var copy = copies.FirstOrDefault(c => Is(c.Emulator, emulator));
        if (copy is null) copies.Add(copy = new EmulatorCopy { Emulator = emulator });
        copy.KeepInSync = keep;
        Save(config, game, copies);
    }

    /// <summary>
    /// Make <paramref name="emulator"/>'s copy the one that syncs: the game's folders here move to it and the server is told;
    /// the old synced copy becomes a copy, kept in step if this one was. Its save is pushed by the next sync like any change.
    /// </summary>
    public static async Task<string?> MakeSyncedAsync(AgentConfig config, TrackedGame game, string emulator, ApiClient? api, Action<string> log,
        IReadOnlyList<SwitchEmulators.Setup>? setups = null)
    {
        if (TitleId(game) is not { } title) return "this is not a Switch game.";
        setups ??= SwitchEmulators.Find();
        var target = setups.FirstOrDefault(s => Is(s.Emulator, emulator));
        if (target is null || SwitchEmulators.Slot(target, title) is not { } slot) return $"{emulator} has no save folder for this game.";
        if (RunningEmulator() is { } running) return $"{running} is running. Close it first.";
        var old = SyncedSetup(game, setups);

        var copies = game.EmulatorCopies.Select(c => c with { }).Where(c => !Is(c.Emulator, emulator)).ToList();
        var was = game.EmulatorCopies.FirstOrDefault(c => Is(c.Emulator, emulator));
        if (old is not null && copies.All(c => !Is(c.Emulator, old.Emulator)))
            copies.Add(new EmulatorCopy { Emulator = old.Emulator, KeepInSync = was?.KeepInSync ?? false, LastHash = was?.LastHash });

        game.SaveDirectory = slot.Dir;
        if (game.ExtraPaths.FirstOrDefault(p => p.Key == SwitchEmulators.DeviceFolderKey) is { } device)
            device.Directory = DeviceDir(target, title, slot);
        config.SaveGameFolders(game);
        Save(config, game, copies);
        var source = new GameSourceDto(GameSourceKinds.Emulator, target.Emulator, target.EmuDeck ? ["Switch", "EmuDeck"] : ["Switch"]);
        config.SaveGameSource(game.GameId, source);
        game.Source = source;
        log($"[{game.Name}] {target.Emulator}'s copy is now the one that syncs ({slot.Dir}).");
        if (api is not null)
        {
            try
            {
                await api.SetMachinePathAsync(game.GameId, slot.Dir);
                if (game.ExtraPaths.FirstOrDefault(p => p.Key == SwitchEmulators.DeviceFolderKey)?.Directory is { } d)
                    await api.SetMachinePathAsync(game.GameId, d, SwitchEmulators.DeviceFolderKey);
                await api.SetGameSourceAsync(game.GameId, source);
            }
            catch (Exception ex) { log($"[{game.Name}] could not tell the server about the new folder: {ex.Message}"); }
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
            var setup = setups.FirstOrDefault(s => Is(s.Emulator, copy.Emulator));
            if (setup is null || setup == SyncedSetup(game, setups) || SwitchEmulators.Slot(setup, title) is not { } slot) continue;
            var syncedRoots = game.Roots(config.StateDir);
            var syncedHash = Hash(syncedRoots);
            var copyHash = slot.Exists ? Hash(Roots(slot)) : null;
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
                why = Copy(config, game, syncedRoots, CopyRoots(setup, title, slot, game), copy.Emulator, log, out copied);
            else if (copyChanged && !syncedChanged)
                why = Copy(config, game, Roots(slot), syncedRoots, "synced", log, out copied);
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
                if (SwitchEmulators.Names.FirstOrDefault(n => name.StartsWith(n, StringComparison.OrdinalIgnoreCase)) is { } emulator)
                    return emulator;
            }
            catch { /* it exited meanwhile */ }
            finally { p.Dispose(); }
        }
        return null;
    }

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
        IReadOnlyList<SwitchEmulators.Setup>? setups)
    {
        if (TitleId(game) is not { } title) return "this is not a Switch game.";
        setups ??= SwitchEmulators.Find();
        var setup = setups.FirstOrDefault(s => Is(s.Emulator, emulator));
        if (setup is null) return $"{emulator} isn't on this machine.";
        if (setup == SyncedSetup(game, setups)) return $"{emulator}'s copy is the one that syncs.";
        if (SwitchEmulators.Slot(setup, title) is not { } slot)
            return setup.IsRyujinx ? $"{emulator} has no save folder for this game yet. Start the game in it once." : $"{emulator} has no user yet.";
        if (!toCopy && !slot.Exists) return $"{emulator} has no save of this game.";
        if (RunningEmulator() is { } running) return $"{running} is running. Close it first.";

        var copies = game.EmulatorCopies.Select(c => c with { }).ToList();
        var copy = copies.FirstOrDefault(c => Is(c.Emulator, emulator));
        if (copy is null) copies.Add(copy = new EmulatorCopy { Emulator = emulator });
        var why = toCopy
            ? Copy(config, game, game.Roots(config.StateDir), CopyRoots(setup, title, slot, game), emulator, log, out var hash)
            : Copy(config, game, Roots(slot), game.Roots(config.StateDir), "synced", log, out hash);
        if (why is not null) return why;
        copy.LastHash = hash;
        copy.Conflict = false;
        Save(config, game, copies);
        return null;
    }

    /// <summary>Make <paramref name="to"/> hold exactly <paramref name="from"/>'s files, folder by folder (by key), after
    /// zipping what <paramref name="to"/> had. Null when done.</summary>
    private static string? Copy(AgentConfig config, TrackedGame game, IReadOnlyList<SaveRoot> from, IReadOnlyList<SaveRoot> to,
        string label, Action<string> log, out string? hash)
    {
        hash = null;
        var source = from.Where(r => r.IsPrimary || Directory.Exists(r.Directory)).ToList();
        var target = to.Where(r => r.IsPrimary || source.Any(s => s.Key == r.Key)).Select(r => r with { IncludeGlobs = null }).ToList();
        var temp = Path.Combine(config.StateDir, "emulator-copies", "tmp");
        try
        {
            Directory.CreateDirectory(temp);
            if (target.Any(r => SwitchEmulators.HasFiles(r.Directory)))
                Backup(config, game, target, label);
            var zip = Path.Combine(temp, $"{game.GameId:N}-{Guid.NewGuid():N}.zip");
            try
            {
                Directory.CreateDirectory(source.Single(r => r.IsPrimary).Directory);
                SaveArchive.CreateArchive(source, zip);
                foreach (var r in target) Directory.CreateDirectory(r.Directory);
                SaveArchive.RestoreArchive(zip, target, temp);
            }
            finally { try { File.Delete(zip); } catch (IOException) { } }
            foreach (var r in target)
                if (RyujinxWorking(r.Directory) is { } working) Mirror(r.Directory, working);
            hash = Hash(target);
            log($"[{game.Name}] copied {Describe(source)} to {Describe(target)}.");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or SaveArchive.UnsafeArchiveException)
        {
            return ex.Message;
        }
    }

    private static string Describe(IReadOnlyList<SaveRoot> roots) => roots.Single(r => r.IsPrimary).Directory;

    private static void Backup(AgentConfig config, TrackedGame game, IReadOnlyList<SaveRoot> roots, string label)
    {
        var dir = Path.Combine(config.StateDir, "emulator-copies", game.GameId.ToString("N"));
        Directory.CreateDirectory(dir);
        var safe = string.Concat(label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'));
        SaveArchive.CreateArchive(roots.Where(r => r.IsPrimary || Directory.Exists(r.Directory)).ToList(),
            Path.Combine(dir, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{safe}.zip"));
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

    private static List<SaveRoot> Roots(SwitchEmulators.TitleSave slot)
    {
        var roots = new List<SaveRoot> { SaveRoot.Primary(slot.Dir) };
        if (slot.DeviceDir is { } device) roots.Add(new SaveRoot(SwitchEmulators.DeviceFolderKey, device));
        return roots;
    }

    /// <summary>A title's device save folder in a setup: a fork's is known in advance, Ryujinx's only once it made one.</summary>
    private static string? DeviceDir(SwitchEmulators.Setup setup, string title, SwitchEmulators.TitleSave slot) =>
        slot.DeviceDir ?? (setup.IsRyujinx ? null : Path.Combine(setup.Saves, "0000000000000000", SwitchEmulators.DeviceProfile, title));

    private static string? Hash(IReadOnlyList<SaveRoot> roots)
    {
        try
        {
            var existing = roots.Where(r => r.IsPrimary || Directory.Exists(r.Directory)).Select(r => r with { IncludeGlobs = null }).ToList();
            return Directory.Exists(existing.Single(r => r.IsPrimary).Directory) ? SaveArchive.HashDirectory(existing) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The setup whose folder for the title is the game's synced folder here.</summary>
    private static SwitchEmulators.Setup? SyncedSetup(TrackedGame game, IReadOnlyList<SwitchEmulators.Setup> setups)
    {
        var dir = Normalize(game.SaveDirectory);
        return setups.FirstOrDefault(s => dir.StartsWith(Normalize(s.Saves) + "/", Comparison))
               ?? setups.FirstOrDefault(s => Is(s.Emulator, game.Source?.Detail));
    }

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
