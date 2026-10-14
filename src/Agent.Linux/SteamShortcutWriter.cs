using System.Runtime.InteropServices;
using System.Text;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// The one writer for SaveLocker's Steam shortcut entries. The SHIPPED one is "SaveLocker", running
/// <c>savelocker open</c> (the agent UI, controller support included), added and kept in place by
/// <see cref="DeckShortcut"/> at daemon start; the test rig has two fixed entries — "Conflict Game",
/// pointing at <c>savelocker fake-game</c> (<see cref="Ui.UiApp"/>'s FakeGame screen), and
/// "SaveLocker Test", pointing at <c>savelocker ui</c> for the SDL Deck UI or <c>savelocker open</c>
/// for the controller-era one — both with their library art in Steam's grid folder. The dev commands
/// are wired only from <c>tests/testenv-deck.sh</c> and stay behind SAVELOCKER_ALLOW_TEST_COMMANDS;
/// the shipped entry is reached from the daemon and <c>savelocker deck-shortcut</c>.
///
/// <para>
/// <b>Deliberately does NOT parse-and-rewrite the whole file.</b> An earlier version read it with
/// <see cref="SteamVdf.Parse"/>, added a child to the tree, and wrote the whole thing back out with
/// a matching serializer — round-tripping every field this codebase's reader does not itself need
/// to understand is only as safe as that reader is complete, which real files have already proven
/// it is not always. This version only splices bytes in, using
/// <see cref="SteamVdf.AnalyzeShortcuts"/> to find EXACTLY where "shortcuts"'s own children end —
/// not simply "the last byte of the file". A real, live 135-shortcut file was found to end
/// <c>… 0x08 0x08 0x08 0x08</c> (closing the last entry's "tags" object, the entry itself,
/// "shortcuts" itself, and one more from a third-party writer's outer wrapper) — an earlier attempt
/// that inserted before "the last byte" landed the new entry as a SIBLING of the whole shortcuts
/// list rather than a member inside it: syntactically valid VDF, invisible to Steam. Everything
/// between the header and the true insertion point passes through unparsed, unreinterpreted.
/// </para>
///
/// <para>
/// <c>shortcuts.vdf</c> is the SAME file Steam uses for every real non-Steam shortcut on this
/// machine. All writes go through <see cref="AtomicFile"/> so Steam never observes a half-written
/// file. <see cref="Add"/> backs the file's ORIGINAL bytes up once — a second <c>Add</c>
/// (re-running <c>conflict</c>) is a pure no-op once the entry is present, so it never gets a
/// chance to clobber that backup — and <see cref="Remove"/> deletes just our entry's own bytes,
/// leaving shortcuts added meanwhile intact, falling back to the backup only when our entry is
/// present but misshapen (keeping a timestamped pre-restore copy beside the file first, which
/// clean deliberately leaves behind for manual recovery).
/// </para>
/// </summary>
public static class SteamShortcutWriter
{
    /// <summary>One of the fixed entries: its display name and the savelocker subcommand its Exe runs.</summary>
    public sealed record Kind(string Name, string Args);

    /// <summary>The fullscreen fake game the conflict tests launch.</summary>
    public static readonly Kind Conflict = new("Conflict Game", "fake-game");
    /// <summary>The test rig's SDL Deck UI entry, with its art.</summary>
    public static readonly Kind DeckUi = new("SaveLocker Test", "ui");
    /// <summary>The test rig's controller-era entry: the same agent UI a real install opens.</summary>
    public static readonly Kind DeckAppTest = new("SaveLocker Test", "open");
    /// <summary>The SHIPPED entry — what appears in a Deck's library, running the agent UI.</summary>
    public static readonly Kind DeckApp = new("SaveLocker", "open");

    /// <summary>Every entry this class may write; the backup and created-file marker outlive all of them.</summary>
    private static readonly Kind[] AllKinds = [Conflict, DeckUi, DeckAppTest, DeckApp];

    public static Kind? KindNamed(string? name) => name switch
    {
        null or "" or "conflict" => Conflict,
        "ui" => DeckUi,
        "app" => DeckAppTest,
        _ => null,
    };

    private const string ArtListSuffix = ".savelocker-art-files";
    private const string BackupSuffix = ".savelocker-backup";
    // Marks "we created shortcuts.vdf; there was nothing here before" — Remove deletes the file
    // outright in that case instead of restoring a backup that was never taken.
    private const string CreatedMarkerSuffix = ".savelocker-created";

    private static readonly byte[] RootHeader = BuildRootHeader();
    // The Exe field header: the display name alone could belong to a real user shortcut, so an
    // entry only counts as ours when its Exe value points at the savelocker binary.
    private static readonly byte[] ExeKeyMarker =
        Concat([0x01], CStringBytes("Exe"));
    // The appid int field header — retargeting updates the value with the Exe (Valve's AppID is a
    // CRC over the Exe field, so changing the command changes it).
    private static readonly byte[] AppIdKeyMarker =
        Concat([0x02], CStringBytes("appid"));
    // The name field's key, matched in EITHER case (see FindNameMarker): Steam's own files spell it
    // "appname" (measured on a Deck), a Group 10-era change made this writer start spelling it
    // "AppName", and third-party writers use both.
    private static readonly byte[] NameKeyLower = CStringBytes("appname");
    private static readonly byte[] NameKeyUpper = CStringBytes("AppName");
    private static readonly byte[] LaunchOptionsKeyMarker =
        Concat([0x01], CStringBytes("LaunchOptions"));
    // The subcommand pre-controller-era Decks carry, in the Exe field or in LaunchOptions depending
    // on how the entry was made (see RetargetAt).
    private const string LegacyUiArgs = "ui";

    /// <summary>What the file currently holds for one entry kind.</summary>
    public enum ShortcutPresence
    {
        /// <summary>No entry of that name at all.</summary>
        Missing,
        /// <summary>Our entry, running exactly the kind's command.</summary>
        Current,
        /// <summary>Our entry, running something else (e.g. <c>ui</c> before the controller-era change).</summary>
        Stale,
        /// <summary>The name is taken by something that is not ours — never touched.</summary>
        Foreign,
    }

    /// <summary><see cref="InspectAt"/> against the active account's shortcuts.vdf.</summary>
    public static ShortcutPresence Inspect(Kind kind, out string? exeValue) => InspectAt(FindShortcutsVdf(), kind, out exeValue);

    internal static ShortcutPresence InspectAt(string? vdfPath, Kind kind, out string? exeValue)
    {
        exeValue = null;
        if (string.IsNullOrEmpty(vdfPath) || !File.Exists(vdfPath)) return ShortcutPresence.Missing;
        byte[] data;
        try { data = File.ReadAllBytes(vdfPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ShortcutPresence.Foreign; }

        if (FindNameMarker(data, kind) < 0) return ShortcutPresence.Missing;
        if (!TryLocateOwnEntry(data, kind, out var start, out var end)) return ShortcutPresence.Foreign;
        exeValue = FindExeValue(data, start, end);
        if (exeValue is null || !exeValue.Contains("savelocker", StringComparison.OrdinalIgnoreCase)) return ShortcutPresence.Foreign;
        // A hand-made entry can carry the subcommand in LaunchOptions instead of the Exe field
        // (Steam appends LaunchOptions to Exe) — the old install.sh's exact shape. For those the
        // first token there is the command.
        var command = CommandOf(exeValue);
        if (command is null && TryFindLaunchOptionsSpan(data, start, end, out var loStart, out var loEnd))
        {
            var lo = Encoding.UTF8.GetString(data, loStart, loEnd - loStart).Trim();
            if (lo.Length > 0)
            {
                var space = lo.IndexOf(' ');
                command = space < 0 ? lo : lo[..space];
            }
        }
        return command == kind.Args ? ShortcutPresence.Current : ShortcutPresence.Stale;
    }

    /// <summary>
    /// Repairs a STALE entry in place: name, icon, custom LaunchOptions and position all stay, and
    /// only the command — and, when it lives in the Exe field, the appid bytes — is rewritten. All
    /// three shapes an entry can have are handled: our own (subcommand inside the Exe field), the
    /// old install.sh one (bare Exe with <c>ui</c> in Launch Options), and a plain hand-added one
    /// (bare Exe, no options). The four grid pictures are renamed when the AppID changes, so artwork
    /// set by hand follows the entry instead of orphaning under a number nothing maps to any more.
    /// False — nothing written — when there is no stale entry to repair.
    /// </summary>
    internal static bool RetargetAt(string vdfPath, Kind kind)
    {
        if (!File.Exists(vdfPath)) return false;
        if (!OwnedByUs(vdfPath))
        {
            Console.Error.WriteLine($"refusing: '{vdfPath}' is not owned by this user - nothing was written.");
            return false;
        }

        var current = File.ReadAllBytes(vdfPath);
        var currentLength = current.Length;
        var currentMtime = File.GetLastWriteTimeUtc(vdfPath);

        if (!TryLocateOwnEntry(current, kind, out var start, out var end)) return false;
        if (!TryFindExeSpan(current, start, end, out var exeStart, out var exeEnd)) return false;
        var oldExe = Encoding.UTF8.GetString(current, exeStart, exeEnd - exeStart);
        if (!oldExe.Contains("savelocker", StringComparison.OrdinalIgnoreCase)) return false;
        var command = CommandOf(oldExe);
        if (command == kind.Args) return false;

        // Where the change lands depends on how the entry was made. Ours puts the subcommand in the
        // Exe field; the old install.sh told people to put "ui" in Launch Options instead (Steam
        // appends LaunchOptions to Exe, so that entry runs "savelocker ui" too); a shortcut added by
        // hand with neither runs bare "savelocker". All three must end up running kind.Args.
        var launchValue = TryFindLaunchOptionsSpan(current, start, end, out var loStart, out var loEnd)
            ? Encoding.UTF8.GetString(current, loStart, loEnd - loStart)
            : null;
        var launchTrim = launchValue?.Trim();

        var replacements = new List<(int Offset, int Length, byte[] Bytes)>();
        var exeChanged = false;
        string? newExe = null;
        string was;
        if (command is not null)
        {
            newExe = ReplaceCommand(oldExe, kind.Args);
            replacements.Add((exeStart, exeEnd - exeStart, Encoding.UTF8.GetBytes(newExe)));
            exeChanged = true;
            was = command;
        }
        else if (launchTrim == kind.Args)
        {
            return false;   // a manual entry already repaired; Stale is just its shape
        }
        else if (launchTrim == LegacyUiArgs)
        {
            // The LaunchOptions value IS the command here — replacing it is the whole repair, and the
            // AppID (a CRC of the unchanged Exe field and name) keeps applying, so hand-set artwork is
            // untouched.
            replacements.Add((loStart, loEnd - loStart, Encoding.UTF8.GetBytes(kind.Args)));
            was = launchTrim;
        }
        else if (string.IsNullOrEmpty(launchTrim))
        {
            newExe = $"{oldExe} {kind.Args}";
            replacements.Add((exeStart, exeEnd - exeStart, Encoding.UTF8.GetBytes(newExe)));
            exeChanged = true;
            was = "(nothing)";
        }
        else
        {
            Console.Error.WriteLine($"refusing: the '{kind.Name}' entry runs neither '{kind.Args}' nor a command this code can move - leaving shortcuts.vdf untouched.");
            return false;
        }

        var oldAppId = 0;
        var newAppId = 0;
        if (exeChanged)
        {
            oldAppId = ComputeShortcutAppId(oldExe, kind.Name);
            newAppId = ComputeShortcutAppId(newExe!, kind.Name);
            if (TryFindAppIdSpan(current, start, end, out var idStart, out var idEnd) && idEnd - idStart == 4)
                replacements.Add((idStart, 4, BitConverter.GetBytes(newAppId)));
        }

        var updated = new List<byte>(current.Length + 32);
        var pos = 0;
        foreach (var r in replacements.OrderBy(r => r.Offset))
        {
            updated.AddRange(current.AsSpan(pos, r.Offset - pos).ToArray());
            updated.AddRange(r.Bytes);
            pos = r.Offset + r.Length;
        }
        updated.AddRange(current.AsSpan(pos).ToArray());

        if (FileChangedSince(vdfPath, currentLength, currentMtime))
        {
            Console.Error.WriteLine($"refusing: '{vdfPath}' changed under us - nothing was written.");
            return false;
        }
        AtomicFile.WriteAllBytes(vdfPath, updated.ToArray());
        if (exeChanged) RenameArt(vdfPath, oldAppId, newAppId);
        Console.WriteLine($"the '{kind.Name}' shortcut now runs '{kind.Args}' (was '{was}') - restart Steam to see it.");
        return true;
    }

    /// <summary>Splits Steam's shell-style Exe field into the quoted binary and its trailing arguments.</summary>
    private static bool SplitExeField(string exeValue, out string head, out string rest)
    {
        head = "";
        rest = "";
        var open = exeValue.IndexOf('"');
        if (open < 0) return false;
        var close = exeValue.IndexOf('"', open + 1);
        if (close < 0) return false;
        head = exeValue[..(close + 1)];
        rest = exeValue[(close + 1)..].Trim();
        return true;
    }

    private static string? CommandOf(string exeValue)
    {
        if (!SplitExeField(exeValue, out _, out var rest) || rest.Length == 0) return null;
        var space = rest.IndexOf(' ');
        return space < 0 ? rest : rest[..space];
    }

    private static string ReplaceCommand(string exeValue, string command)
    {
        if (!SplitExeField(exeValue, out var head, out var rest)) return exeValue;
        var space = rest.IndexOf(' ');
        var tail = space < 0 ? "" : rest[space..];
        return $"{head} {command}{tail}";
    }

    /// <summary>Renames one entry's four grid pictures old AppID → new, so hand-set artwork follows a repair.</summary>
    private static void RenameArt(string vdfPath, int oldAppId, int newAppId)
    {
        if (oldAppId == newAppId) return;
        var grid = GridDirOf(vdfPath);
        if (!Directory.Exists(grid)) return;
        var oldNames = Art.SteamArt.ShortcutFiles(unchecked((uint)oldAppId)).Select(f => f.Name).ToArray();
        var newNames = Art.SteamArt.ShortcutFiles(unchecked((uint)newAppId)).Select(f => f.Name).ToArray();
        try
        {
            for (var i = 0; i < oldNames.Length; i++)
            {
                var oldPath = Path.Combine(grid, oldNames[i]);
                if (File.Exists(oldPath)) File.Move(oldPath, Path.Combine(grid, newNames[i]), overwrite: true);
            }
            var list = Path.Combine(grid, ArtListSuffix);
            if (!File.Exists(list)) return;
            var lines = File.ReadAllLines(list).Select(n =>
            {
                var i = Array.IndexOf(oldNames, n);
                return i >= 0 ? newNames[i] : n;
            }).ToArray();
            var text = string.Join(Environment.NewLine, lines);
            if (File.ReadAllText(list) != text) AtomicFile.WriteAllText(list, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Adds the shortcut if it is not already present (a second call is a verified no-op, never a
    /// rewrite). Returns the AppID Steam will use for it — per-machine deterministic, since it is
    /// derived from the fixed name and exe (which embeds this machine's prefixDir) — or null if
    /// nothing was found or written.
    /// </summary>
    /// <param name="extraArgs">Appended to the Exe after the kind's own subcommand.</param>
    /// <param name="launchOptions">Written as the entry's LaunchOptions, in place of the launch-gate wrapper.</param>
    public static int? Add(string prefixDir, bool withLaunchCommand = false, Kind? kind = null,
        string? extraArgs = null, string? launchOptions = null) =>
        AddTo(FindShortcutsVdf(), prefixDir, withLaunchCommand, kind ?? Conflict, extraArgs, launchOptions);

    /// <summary><see cref="Add"/> against a given shortcuts.vdf (null: Steam was not found) — what the tests drive.</summary>
    internal static int? AddTo(string? vdfPath, string prefixDir, bool withLaunchCommand, Kind kind, string? extraArgs, string? customLaunchOptions)
    {
        if (string.IsNullOrEmpty(prefixDir) || prefixDir.IndexOf('"') >= 0 || prefixDir.Any(char.IsControl))
        {
            Console.Error.WriteLine("refusing: --prefix must not be empty or contain quotes/control characters - the Exe and StartDir fields are quoted strings with no escaping. Nothing was written.");
            return null;
        }

        if (vdfPath is null)
        {
            Console.Error.WriteLine("no Steam userdata directory found - is Steam installed and has it signed in at least once?");
            return null;
        }

        var exe = Path.Combine(prefixDir, "savelocker");
        // Steam's Exe field is a shell-style string, not just a bare path — the quoted binary
        // followed by its fixed argument is how every other non-Steam-shortcut tool (Boilr, Lutris,
        // etc.) passes a subcommand this way, since shortcuts.vdf has no separate "arguments" key.
        var exeField = string.IsNullOrEmpty(extraArgs) ? $"\"{exe}\" {kind.Args}" : $"\"{exe}\" {kind.Args} {extraArgs}";
        // The AppID stays derived from exeField alone even when a launch command is written — the
        // LaunchOptions wrapper must not change which shortcut Steam maps this entry to.
        var appId = ComputeShortcutAppId(exeField, kind.Name);
        // The one place this invocation string is spelled — see LaunchOptions.cs's own doc comment.
        var launchOptions = customLaunchOptions ?? (withLaunchCommand ? LaunchOptions.Invocation(exe) : string.Empty);

        if (!File.Exists(vdfPath))
        {
            // No existing shortcuts at all: key "0", same as any writer's first entry. A single
            // closing 0x08 here (not the double-plus this class sees on real, tool-edited files) —
            // the minimal structure SteamVdf.Parse itself already expects and this class's own
            // AnalyzeShortcuts will happily read back on the next Add.
            var fresh = Concat(RootHeader, BuildEntryBytes(kind, "0", appId, exeField, prefixDir, launchOptions), [0x08]);
            var createdMarkerPath = vdfPath + CreatedMarkerSuffix;
            File.WriteAllBytes(createdMarkerPath, []);
            AtomicFile.WriteAllBytes(vdfPath, fresh);
            return appId;
        }

        if (!OwnedByUs(vdfPath))
        {
            Console.Error.WriteLine($"refusing: '{vdfPath}' is not owned by this user - running under sudo or as the wrong user must not rewrite Steam's file. Nothing was written.");
            return null;
        }

        var original = File.ReadAllBytes(vdfPath);
        var originalLength = original.Length;
        var originalMtime = File.GetLastWriteTimeUtc(vdfPath);

        if (FindNameMarker(original, kind) >= 0)
        {
            // The fixed display name alone could belong to a real user shortcut — only an entry
            // whose Exe points at savelocker is ours to leave alone.
            if (!TryLocateOwnEntry(original, kind, out var ownStart, out var ownEnd) || !EntryExeLooksOurs(original, ownStart, ownEnd))
            {
                Console.Error.WriteLine($"refusing: a '{kind.Name}' entry is present but it does not point at savelocker - leaving shortcuts.vdf untouched.");
                return null;
            }
            Console.WriteLine($"'{kind.Name}' shortcut already present - leaving shortcuts.vdf untouched.");
            return appId;
        }

        (int insertPos, int nextIndex) analysis;
        try { analysis = SteamVdf.AnalyzeShortcuts(original); }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine(
                $"'{vdfPath}' did not parse the way this code expects a shortcuts.vdf to - refusing to touch it. Nothing was written. ({ex.Message})");
            return null;
        }

        // Steam rewrites this file on exit — refuse rather than splice into (or back up) bytes
        // that changed between our read and our write.
        if (FileChangedSince(vdfPath, originalLength, originalMtime))
        {
            Console.Error.WriteLine($"refusing: '{vdfPath}' changed under us - nothing was written.");
            return null;
        }

        var entryBytes = BuildEntryBytes(kind, analysis.nextIndex.ToString(), appId, exeField, prefixDir, launchOptions);

        var backupPath = vdfPath + BackupSuffix;
        if (!File.Exists(backupPath)) AtomicFile.WriteAllBytes(backupPath, original);

        // The insertion point is the exact offset of the 0x08 that closes "shortcuts" itself —
        // found by AnalyzeShortcuts, NOT assumed to be "the last byte of the file" (see this
        // class's doc comment for why that assumption was wrong). Everything before it — all of a
        // real user's own shortcuts — and everything from it onward (that closing byte, plus
        // whatever else follows) both pass through byte-for-byte, untouched.
        var before = original.AsSpan(0, analysis.insertPos).ToArray();
        var from = original.AsSpan(analysis.insertPos).ToArray();
        var updated = Concat(before, entryBytes, from);

        AtomicFile.WriteAllBytes(vdfPath, updated);
        return appId;
    }

    /// <summary>
    /// Removes one of the fixed shortcuts again (and, for "SaveLocker Test", its art). Deletes just
    /// our entry's own bytes when they can still be located, so shortcuts Steam or the user added
    /// meanwhile survive — the backup
    /// is only restored when our entry is present but no longer structurally intact (e.g. a writer
    /// reordered its fields). Refuses to overwrite rather than silently discarding unknown changes.
    /// Safe to call even if <see cref="Add"/> never ran — it is then a no-op.
    /// Returns true when the file is clean (or there was nothing to do), false on any refusal.
    /// </summary>
    public static bool Remove(Kind? kind = null)
    {
        var vdfPath = FindShortcutsVdf();
        return vdfPath is null || RemoveFrom(vdfPath, kind ?? Conflict);
    }

    /// <summary><see cref="Remove"/> against a given shortcuts.vdf — what the tests drive.</summary>
    internal static bool RemoveFrom(string vdfPath, Kind kind) => RemoveEntry(vdfPath, kind);

    private static bool RemoveEntry(string vdfPath, Kind kind)
    {
        var backupPath = vdfPath + BackupSuffix;
        var createdMarkerPath = vdfPath + CreatedMarkerSuffix;

        if (!File.Exists(vdfPath))
        {
            try { File.Delete(backupPath); } catch { }
            try { File.Delete(createdMarkerPath); } catch { }
            Console.WriteLine("no shortcuts.vdf found - nothing to restore.");
            return true;
        }

        if (!OwnedByUs(vdfPath))
        {
            Console.Error.WriteLine($"refusing: '{vdfPath}' is not owned by this user - running under sudo or as the wrong user must not rewrite Steam's file. Nothing was written.");
            return false;
        }

        var current = File.ReadAllBytes(vdfPath);
        var currentLength = current.Length;
        var currentMtime = File.GetLastWriteTimeUtc(vdfPath);

        // Remove every entry carrying this kind's name whose Exe points at savelocker. Normally
        // there is exactly one; duplicates are what an older case-mismatched writer left behind (a
        // Group 10-era change spelled the name key "AppName", so entries written before it were
        // invisible to Remove and rig cycles piled new ones beside them). A same-named entry that
        // does NOT point at savelocker is a real user shortcut: the whole operation refuses before
        // anything is written.
        var updated = current;
        var removedExes = new List<string>();
        while (true)
        {
            var at = FindNameMarker(updated, kind);
            if (at < 0) break;
            if (!TryLocateEntryAt(updated, kind, at, out var start, out var endExclusive))
                break;   // present but misshapen: fall through to the backup-restore path below
            // Same guard as Add's no-op path: only splice an entry whose Exe points at savelocker,
            // never a real user shortcut sharing the fixed display name.
            if (!EntryExeLooksOurs(updated, start, endExclusive))
            {
                Console.Error.WriteLine($"refusing: the located '{kind.Name}' entry does not point at savelocker - leaving shortcuts.vdf untouched.");
                return false;
            }
            // The entry's own AppID, read before its bytes go: the art files are named by it.
            if (FindExeValue(updated, start, endExclusive) is { } removedExe) removedExes.Add(removedExe);
            updated = Concat(updated.AsSpan(0, start).ToArray(), updated.AsSpan(endExclusive).ToArray());
        }

        if (removedExes.Count > 0)
        {
            if (FileChangedSince(vdfPath, currentLength, currentMtime))
            {
                Console.Error.WriteLine($"refusing: '{vdfPath}' changed under us - nothing was written.");
                return false;
            }
            // Same "leave no trace" rule as the fallback branch below: if SaveLocker created this
            // file from nothing and removing our entries leaves it empty, delete the file outright
            // instead of writing back an empty-but-real shortcuts.vdf where none existed before.
            if (File.Exists(createdMarkerPath) && IsEmptyShortcutsFile(updated))
            {
                File.Delete(vdfPath);
                try { File.Delete(backupPath); } catch { }
                File.Delete(createdMarkerPath);
                foreach (var exe in removedExes) RemoveArtFor(vdfPath, ComputeShortcutAppId(exe, kind.Name));
                Console.WriteLine($"removed {vdfPath} (SaveLocker created it - nothing existed before it).");
                return true;
            }
            AtomicFile.WriteAllBytes(vdfPath, updated);
            foreach (var exe in removedExes) RemoveArtFor(vdfPath, ComputeShortcutAppId(exe, kind.Name));
            // The backup and the created-file marker describe the file before ANY of our entries: kept while
            // another is still here, so its removal can still restore or delete what was there before.
            if (!OtherOwnEntryPresent(updated, kind))
            {
                try { File.Delete(backupPath); } catch { }
                try { File.Delete(createdMarkerPath); } catch { }
            }
            Console.WriteLine($"removed the '{kind.Name}' shortcut from {vdfPath} (other entries untouched).");
            return true;
        }

        // Computed once and reused below — TryLocateOwnEntry already scanned for this same marker
        // internally, but it doesn't report a plain "found at all" answer, so one fresh scan here
        // is the cheapest way to get it without reshaping that method's contract.
        var marker = FindNameMarker(current, kind);
        if (marker < 0)
        {
            // The file differs from the backup because our other entry is in it, not because anything is wrong.
            if (OtherOwnEntryPresent(current, kind))
            {
                Console.WriteLine($"no '{kind.Name}' shortcut in {vdfPath} - nothing to remove.");
                return true;
            }
            if (!File.Exists(backupPath))
            {
                if (File.Exists(createdMarkerPath))
                {
                    // We created this file and our entry is already gone: only delete the file
                    // itself when Steam/the user added nothing meanwhile, never their shortcuts.
                    if (IsEmptyShortcutsFile(current))
                    {
                        File.Delete(vdfPath);
                        Console.WriteLine($"removed {vdfPath} (SaveLocker created it - nothing existed before it).");
                    }
                    else
                    {
                        Console.WriteLine($"our entry is already gone but {vdfPath} now holds other shortcuts - leaving the file, deleting only our marker.");
                    }
                    File.Delete(createdMarkerPath);
                }
                else
                {
                    Console.WriteLine("no SaveLocker shortcuts.vdf backup found - nothing to restore.");
                }
                return true;
            }

            if (current.SequenceEqual(File.ReadAllBytes(backupPath)))
            {
                File.Delete(backupPath);
                Console.WriteLine($"already clean - deleted the stale pre-SaveLocker backup.");
                return true;
            }

            Console.Error.WriteLine(
                $"our entry is already gone from '{vdfPath}' but the file differs from the pre-SaveLocker backup - " +
                "refusing to overwrite, so nothing was written. Delete the backup manually once sure: " + backupPath);
            return false;
        }

        // The name is present but no entry could be located above — never splice blindly here.
        // (A duplicate whose copy is NOT ours already refused in the loop; every duplicate that IS
        // ours was removed there, so reaching this point means the bytes are misshapen.)
        if (!FileContainsOwnExe(current))
        {
            Console.Error.WriteLine($"refusing: a '{kind.Name}' entry is present but no Exe in the file points at savelocker - leaving shortcuts.vdf untouched.");
            return false;
        }

        if (File.Exists(backupPath))
        {
            if (FileChangedSince(vdfPath, currentLength, currentMtime))
            {
                Console.Error.WriteLine($"refusing: '{vdfPath}' changed under us - nothing was written.");
                return false;
            }
            // The restore below discards everything written after the backup was taken, so keep a
            // timestamped copy of the pre-restore file first. Kept deliberately: clean never
            // deletes it, it is the manual-recovery path. Never fails the restore — Warn only.
            string? safetyPath = null;
            try
            {
                safetyPath = vdfPath + ".savelocker-pre-restore-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Copy(vdfPath, safetyPath, false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"warning: could not keep a pre-restore safety copy ({ex.Message}) - continuing with the restore.");
                safetyPath = null;
            }
            var backupBytes = File.ReadAllBytes(backupPath);
            Console.Error.WriteLine(
                $"our entry is present but no longer in its written shape - restoring '{vdfPath}' from the pre-SaveLocker backup; " +
                (safetyPath is null
                    ? "no pre-restore safety copy could be kept. "
                    : $"a pre-restore copy was kept at '{safetyPath}' for manual recovery. ") +
                "shortcuts added meanwhile may be lost. Restart Steam afterwards and check the library.");
            AtomicFile.WriteAllBytes(vdfPath, backupBytes);
            File.Delete(backupPath);
            return true;
        }
        else
        {
            Console.Error.WriteLine(
                $"our entry is present but cannot be located precisely and no backup exists - refusing to touch '{vdfPath}'. " +
                $"Remove the '{kind.Name}' shortcut from Steam itself.");
            return false;
        }
    }

    /// <summary>The grid folder beside the located shortcuts.vdf — where Steam looks for a shortcut's art.</summary>
    private static string GridDirOf(string vdfPath) => Path.Combine(Path.GetDirectoryName(vdfPath)!, "grid");

    /// <summary>
    /// Writes the four library pictures for the shortcut under Steam's own names, and merges them into
    /// the grid's own file list, so removal deletes exactly what was painted — leaving any other
    /// entry's art alone. The shipped entry gets art from the daemon (and repaints it on look changes
    /// through the record below); the rig calls this for its own entries.
    /// </summary>
    /// <param name="stateDir">The agent's state directory: a record left there tells its daemon to
    /// repaint these files whenever the look changes (see <see cref="Art.SteamArtHost"/>).</param>
    public static void WriteArt(int appId, string stateDir, string accent, string mark, Func<string, byte[]> layers)
    {
        var vdfPath = FindShortcutsVdf();
        if (vdfPath is null) return;
        var grid = GridDirOf(vdfPath);
        PaintArt(grid, unchecked((uint)appId), accent, mark, layers);
        Directory.CreateDirectory(stateDir);
        AtomicFile.WriteAllText(Path.Combine(stateDir, ArtRecordFile), System.Text.Json.JsonSerializer.Serialize(new ArtRecord(grid, unchecked((uint)appId))));
        Console.WriteLine($"painted the shortcut's art in {grid}");
    }

    public sealed record ArtRecord(string GridDir, uint AppId);

    /// <summary>In the test agent's state directory: which shortcut's grid art its daemon keeps in step with the look.</summary>
    public const string ArtRecordFile = "test-shortcut-art.json";

    public static ArtRecord? ReadArtRecord(string stateDir)
    {
        try
        {
            var path = Path.Combine(stateDir, ArtRecordFile);
            return File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<ArtRecord>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void ForgetArtRecord(string stateDir)
    {
        try { File.Delete(Path.Combine(stateDir, ArtRecordFile)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Writes the four pieces for the shortcut and merges them into the grid's file list.</summary>
    public static int PaintArt(string grid, uint appId, string accent, string mark, Func<string, byte[]> layers)
    {
        var written = Art.SteamArt.WriteForShortcut(grid, appId, accent, mark, layers);
        var list = Path.Combine(grid, ArtListSuffix);
        var names = Art.SteamArt.ShortcutFiles(appId).Select(f => f.Name).ToList();
        // The list is shared by every entry SaveLocker has painted here: merged, never replaced, or
        // painting a second entry would orphan the first one's files at removal time.
        var existing = File.Exists(list) ? File.ReadAllLines(list).Where(n => n.Length > 0).ToList() : [];
        var merged = existing.Concat(names.Where(n => !existing.Contains(n))).ToList();
        var text = string.Join(Environment.NewLine, merged);
        // The daemon calls this at every start: like the pictures, the list is only written when it would change.
        if (!File.Exists(list) || File.ReadAllText(list) != text) AtomicFile.WriteAllText(list, text);
        return written;
    }

    /// <summary>Deletes only the four files belonging to one entry's AppID, as recorded in the shared list.</summary>
    private static void RemoveArtFor(string vdfPath, int appId)
    {
        var grid = GridDirOf(vdfPath);
        var list = Path.Combine(grid, ArtListSuffix);
        if (!File.Exists(list)) return;
        try
        {
            var mine = Art.SteamArt.ShortcutFiles(unchecked((uint)appId)).Select(f => f.Name).ToHashSet();
            var kept = new List<string>();
            var removedAny = false;
            foreach (var name in File.ReadAllLines(list))
            {
                if (name.Length == 0 || name != Path.GetFileName(name)) continue;
                if (mine.Contains(name)) { File.Delete(Path.Combine(grid, name)); removedAny = true; }
                else kept.Add(name);
            }
            if (!removedAny) return;
            if (kept.Count == 0) File.Delete(list);
            else AtomicFile.WriteAllText(list, string.Join(Environment.NewLine, kept));
            if (!Directory.EnumerateFileSystemEntries(grid).Any()) Directory.Delete(grid);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static bool IsEmptyShortcutsFile(byte[] data)
    {
        try
        {
            var analysis = SteamVdf.AnalyzeShortcuts(data);
            return analysis.NextIndex == 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool TryLocateOwnEntry(byte[] data, Kind kind, out int start, out int endExclusive)
    {
        start = -1;
        endExclusive = -1;
        var marker = FindNameMarker(data, kind);
        if (marker < 0 || FindNameMarker(data, kind, marker + 1) >= 0) return false;
        return TryLocateEntryAt(data, kind, marker, out start, out endExclusive);
    }

    /// <summary>
    /// Locates the entry whose bytes span the given name marker. Deliberately ignores any OTHER
    /// same-named entries: duplicate-aware removal needs each copy located one at a time.
    /// </summary>
    private static bool TryLocateEntryAt(byte[] data, Kind kind, int marker, out int start, out int endExclusive)
    {
        start = -1;
        endExclusive = -1;
        // Order-independent: an entry's fields may come in any order (a third-party writer decides),
        // so instead of assuming appid-then-appname, scan backwards for every plausible numeric
        // entry header near the marker and keep the ones whose children — skipped forward without
        // interpreting values — span the marker exactly.
        var candidateStart = -1;
        var candidateEnd = -1;
        var candidateCount = 0;
        // No arbitrary window: an entry's own header can legitimately sit further back than any
        // fixed distance (a large StartDir/LaunchOptions value written before the name by some other
        // writer's field order), and every candidate is already validated by walking its children
        // forward to confirm it truly spans the marker — the safety here is "exactly one candidate
        // spans it", not the search radius.
        for (var s = marker - 1; s >= 0; s--)
        {
            if (data[s] != 0x00) continue;
            for (var keyLen = 1; keyLen <= 6; keyLen++)
            {
                if (s + 1 + keyLen >= data.Length) break;
                var digits = true;
                for (var i = 0; i < keyLen; i++)
                    if (data[s + 1 + i] is < (byte)'0' or > (byte)'9') { digits = false; break; }
                if (!digits || data[s + 1 + keyLen] != 0x00) continue;
                var pos = s + 1 + keyLen + 1;
                int end;
                try { SkipEntryChildren(data, ref pos); end = pos; }
                catch (InvalidDataException) { continue; }
                if (end < marker + 1 + NameKeyLower.Length + CStringBytes(kind.Name).Length) continue;
                candidateStart = s;
                candidateEnd = end;
                candidateCount++;
                break;
            }
        }
        // Anything but exactly one spanning entry means overlapping or unlistable bytes — not ours
        // to cut.
        if (candidateCount != 1) return false;
        start = candidateStart;
        endExclusive = candidateEnd;
        return true;
    }

    private static bool OtherOwnEntryPresent(byte[] data, Kind kind) =>
        AllKinds.Any(other => other != kind &&
            TryLocateOwnEntry(data, other, out var start, out var end) && EntryExeLooksOurs(data, start, end));

    // Only an entry whose Exe value points at the savelocker binary is ours to keep or splice —
    // the display name alone could belong to a real user shortcut.
    private static bool EntryExeLooksOurs(byte[] data, int start, int endExclusive)
    {
        var value = FindExeValue(data, start, endExclusive);
        return value is not null && value.Contains("savelocker", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindExeValue(byte[] data, int start, int endExclusive) =>
        TryFindExeSpan(data, start, endExclusive, out var valueStart, out var valueEnd)
            ? Encoding.UTF8.GetString(data, valueStart, valueEnd - valueStart)
            : null;

    private static bool TryFindExeSpan(byte[] data, int start, int endExclusive, out int valueStart, out int valueEnd)
    {
        valueStart = -1;
        valueEnd = -1;
        for (var i = start; i + ExeKeyMarker.Length <= endExclusive; i++)
        {
            if (data[i] != ExeKeyMarker[0]) continue;
            var match = true;
            for (var j = 1; j < ExeKeyMarker.Length; j++)
                if (data[i + j] != ExeKeyMarker[j]) { match = false; break; }
            if (!match) continue;
            valueStart = i + ExeKeyMarker.Length;
            valueEnd = valueStart;
            while (valueEnd < endExclusive && data[valueEnd] != 0x00) valueEnd++;
            if (valueEnd >= endExclusive) return false;
            return true;
        }
        return false;
    }

    private static bool TryFindLaunchOptionsSpan(byte[] data, int start, int endExclusive, out int valueStart, out int valueEnd)
    {
        valueStart = -1;
        valueEnd = -1;
        for (var i = start; i + LaunchOptionsKeyMarker.Length <= endExclusive; i++)
        {
            if (data[i] != LaunchOptionsKeyMarker[0]) continue;
            var match = true;
            for (var j = 1; j < LaunchOptionsKeyMarker.Length; j++)
                if (data[i + j] != LaunchOptionsKeyMarker[j]) { match = false; break; }
            if (!match) continue;
            valueStart = i + LaunchOptionsKeyMarker.Length;
            valueEnd = valueStart;
            while (valueEnd < endExclusive && data[valueEnd] != 0x00) valueEnd++;
            if (valueEnd >= endExclusive) return false;
            return true;
        }
        return false;
    }

    private static bool TryFindAppIdSpan(byte[] data, int start, int endExclusive, out int valueStart, out int valueEnd)
    {
        valueStart = -1;
        valueEnd = -1;
        for (var i = start; i + AppIdKeyMarker.Length <= endExclusive; i++)
        {
            if (data[i] != AppIdKeyMarker[0]) continue;
            var match = true;
            for (var j = 1; j < AppIdKeyMarker.Length; j++)
                if (data[i + j] != AppIdKeyMarker[j]) { match = false; break; }
            if (!match) continue;
            valueStart = i + AppIdKeyMarker.Length;
            valueEnd = valueStart + 4;
            return valueEnd <= endExclusive;
        }
        return false;
    }

    // Fallback-restore guard for when entry boundaries are unrecoverable: at least one Exe in the
    // file must point at savelocker before the backup may replace the file.
    private static bool FileContainsOwnExe(byte[] data)
    {
        var offset = 0;
        while (offset + ExeKeyMarker.Length <= data.Length)
        {
            var hit = IndexOf(data, ExeKeyMarker, offset);
            if (hit < 0) return false;
            var valueStart = hit + ExeKeyMarker.Length;
            var valueEnd = valueStart;
            while (valueEnd < data.Length && data[valueEnd] != 0x00) valueEnd++;
            if (valueEnd < data.Length &&
                Encoding.UTF8.GetString(data, valueStart, valueEnd - valueStart)
                    .Contains("savelocker", StringComparison.OrdinalIgnoreCase))
                return true;
            offset = valueEnd + 1;
        }
        return false;
    }

    // statx(2)'s uid fields: unlike libc's struct stat, the layout is the kernel ABI and identical
    // on every architecture, so this needs no per-arch struct. Only st_uid is ever read.
    // Size = 256 is load-bearing, not decorative: the kernel's cp_statx() always copy_to_user's the
    // FULL sizeof(struct statx) — 256 bytes, per the statx(2) ABI — regardless of the requested
    // mask or how many fields this struct declares. Without the explicit Size, the CLR would only
    // reserve the ~28 bytes these six fields need, and the syscall would write ~228 bytes past the
    // end of that stack allocation on every call.
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct StatxMinimal
    {
        public uint stx_mask;
        public uint stx_blksize;
        public ulong stx_attributes;
        public uint stx_nlink;
        public uint stx_uid;
        public uint stx_gid;
    }

    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int statx(int dirfd, string pathname, int flags, uint mask, out StatxMinimal buf);

    [DllImport("libc.so.6")]
    private static extern uint geteuid();

    private const int AtFdcwd = -100;
    private const uint StatxUid = 0x0002u;

    // Steam's file must belong to this user before it is rewritten — under sudo or as the wrong
    // user this refuses instead of shifting ownership on Steam's file. Anything undeterminable
    // (old libc, non-glibc, vanished path) degrades to allow, never blocks.
    private static bool OwnedByUs(string path)
    {
        if (!OperatingSystem.IsLinux()) return true;
        try
        {
            if (statx(AtFdcwd, path, 0, StatxUid, out var st) != 0) return true;
            if ((st.stx_mask & StatxUid) == 0) return true;
            return st.stx_uid == geteuid();
        }
        catch { return true; }
    }

    // Steam rewrites shortcuts.vdf on exit: a size or mtime change since our read means someone
    // else won the race, so refuse rather than clobber. An unreadable path counts as changed.
    private static bool FileChangedSince(string path, long length, DateTime mtimeUtc)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != length) return true;
            return File.GetLastWriteTimeUtc(path) != mtimeUtc;
        }
        catch { return true; }
    }

    // Same bound as SteamVdf's own object walkers, and for the same reason: an unlimited recursion
    // on nested 0x00 markers turns a corrupted or adversarial shortcuts.vdf into an uncatchable
    // StackOverflowException instead of a clean InvalidDataException.
    private const int MaxEntryDepth = 64;

    private static void SkipEntryChildren(byte[] data, ref int pos, int depth = 0)
    {
        if (depth > MaxEntryDepth) throw new InvalidDataException("Entry nests too deep.");
        while (true)
        {
            if (pos >= data.Length) throw new InvalidDataException("Truncated entry.");
            var type = data[pos++];
            if (type == 0x08) return;
            SkipEntryString(data, ref pos);
            switch (type)
            {
                case 0x00: SkipEntryChildren(data, ref pos, depth + 1); break;
                case 0x01: SkipEntryString(data, ref pos); break;
                case 0x02:
                    if (pos + 4 > data.Length) throw new InvalidDataException("Truncated entry.");
                    pos += 4;
                    break;
                default: throw new InvalidDataException($"Unexpected node type 0x{type:X2}.");
            }
        }
    }

    private static void SkipEntryString(byte[] data, ref int pos)
    {
        while (pos < data.Length && data[pos] != 0x00) pos++;
        if (pos >= data.Length) throw new InvalidDataException("Truncated entry.");
        pos++;
    }

    /// <summary>
    /// The active account's <c>shortcuts.vdf</c> path. When more than one <c>userdata</c> account
    /// exists, picks whichever has been written to most recently — the same "no better signal"
    /// heuristic every other "which account is actually active" guess in this codebase falls back
    /// to (see <c>SteamShortcuts.ReadAllAsync</c>'s own doc comment on why there is no reliable
    /// alternative). A single-account Deck, the common case, has nothing to guess.
    /// </summary>
    internal static string? FindShortcutsVdf()
    {
        var steamRoot = SteamRoots.Find().FirstOrDefault();
        if (steamRoot is null) return null;

        var userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata)) return null;

        string[] userDirs;
        try { userDirs = Directory.GetDirectories(userdata); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"could not list '{userdata}' ({ex.Message}) - leaving shortcuts.vdf untouched.");
            return null;
        }
        if (userDirs.Length == 0) return null;

        var chosen = userDirs.Length == 1
            ? userDirs[0]
            : userDirs.OrderByDescending(d =>
            {
                var cfg = Path.Combine(d, "config");
                return Directory.Exists(cfg) ? Directory.GetLastWriteTimeUtc(cfg) : DateTime.MinValue;
            }).First();

        return Path.Combine(chosen, "config", "shortcuts.vdf");
    }

    /// <summary>
    /// Valve's "legacy" non-Steam-shortcut AppID: <c>crc32(exe + appname) | 0x80000000</c>, stored
    /// as a signed int32 the same way <see cref="SteamShortcuts.CompatDataId(int)"/> already reads
    /// one back — the algorithm every third-party shortcut-writing tool (Boilr, SteamGridDB, etc.)
    /// uses, since Valve has never published it officially. Per-machine deterministic for a fixed
    /// name+exe — the exe embeds this machine's prefix path, so another Deck gets another AppID.
    /// </summary>
    private static int ComputeShortcutAppId(string exe, string appName)
    {
        var crc = Crc32.Compute(Encoding.UTF8.GetBytes(exe + appName));
        return unchecked((int)(crc | 0x80000000u));
    }

    // --- Minimal binary-VDF byte encoding, scoped ONLY to the one fixed entry this class ever
    // writes. Not a general writer — see this class's own doc comment for why a general
    // parse-everything/write-everything round trip was tried and abandoned. ---

    private static byte[] BuildRootHeader()
    {
        // 0x00 <NUL-terminated "shortcuts">, exactly what SteamVdf.Parse expects at offset 0.
        return Concat([0x00], CStringBytes("shortcuts"));
    }

    private static byte[] BuildEntryBytes(Kind kind, string key, int appId, string exeField, string startDir, string launchOptions)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x00);
        WriteBytes(ms, CStringBytes(key));

        WriteInt(ms, "appid", appId);
        // Lowercase, the way Steam's own "Add a Non-Steam Game" writes it (measured on a Deck);
        // the finders above accept either case so older capital-spelled entries stay findable.
        WriteString(ms, "appname", kind.Name);
        WriteString(ms, "Exe", exeField);
        WriteString(ms, "StartDir", $"\"{startDir}\"");
        WriteString(ms, "icon", "");
        WriteString(ms, "ShortcutPath", "");
        // Blank for a plain launch straight into the fake game; --with-launch-command sets the
        // launch-gate wrapper ("<exe>" run -- %command%) so Play syncs before/after instead.
        WriteString(ms, "LaunchOptions", launchOptions);
        WriteInt(ms, "IsHidden", 0);
        WriteInt(ms, "AllowDesktopConfig", 1);
        WriteInt(ms, "AllowOverlay", 1);
        WriteInt(ms, "OpenVR", 0);
        WriteInt(ms, "Devkit", 0);
        WriteString(ms, "DevkitGameID", "");
        WriteInt(ms, "DevkitOverrideAppID", 0);
        WriteInt(ms, "LastPlayTime", 0);

        // Empty "tags" object: 0x00 <key> <no children> 0x08.
        ms.WriteByte(0x00);
        WriteBytes(ms, CStringBytes("tags"));
        ms.WriteByte(0x08);

        ms.WriteByte(0x08); // closes this entry
        return ms.ToArray();
    }

    private static void WriteString(MemoryStream ms, string key, string value)
    {
        ms.WriteByte(0x01);
        WriteBytes(ms, CStringBytes(key));
        WriteBytes(ms, CStringBytes(value));
    }

    private static void WriteInt(MemoryStream ms, string key, int value)
    {
        ms.WriteByte(0x02);
        WriteBytes(ms, CStringBytes(key));
        WriteBytes(ms, BitConverter.GetBytes(value));
    }

    private static void WriteBytes(MemoryStream ms, byte[] bytes) => ms.Write(bytes, 0, bytes.Length);

    private static byte[] CStringBytes(string s) => [.. Encoding.UTF8.GetBytes(s), (byte)0x00];

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }

    /// <summary>
    /// Offset of this kind's name field — <c>0x01</c>, the key and the kind's name, all
    /// NUL-terminated — with the KEY matched in either case. Steam's own files spell it "appname",
    /// this writer spelled it that way until a Group 10-era change moved to "AppName", and
    /// third-party writers use both; matching case-sensitively made Remove and the in-place repair
    /// silently miss every entry the other case had written.
    /// </summary>
    private static int FindNameMarker(byte[] data, Kind kind, int startIndex = 0)
    {
        var name = CStringBytes(kind.Name);
        for (var i = Math.Max(0, startIndex); i + 1 + NameKeyLower.Length + name.Length <= data.Length; i++)
        {
            if (data[i] != 0x01) continue;
            if (!EqualsAt(data, i + 1, NameKeyLower) && !EqualsAt(data, i + 1, NameKeyUpper)) continue;
            if (EqualsAt(data, i + 1 + NameKeyLower.Length, name)) return i;
        }
        return -1;
    }

    private static bool EqualsAt(byte[] data, int offset, byte[] value)
    {
        if (offset < 0 || offset + value.Length > data.Length) return false;
        for (var j = 0; j < value.Length; j++)
            if (data[offset + j] != value[j]) return false;
        return true;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int startIndex = 0)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (var i = Math.Max(0, startIndex); i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
