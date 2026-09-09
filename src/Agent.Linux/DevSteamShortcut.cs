using System.Runtime.InteropServices;
using System.Text;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Test-only: adds or removes ONE hardcoded non-Steam shortcut, "Conflict Game", pointing at
/// <c>savelocker fake-game</c> (<see cref="Ui.UiApp"/>'s FakeGame screen). Wired only from
/// <c>tests/testenv.ps1</c>'s <c>conflict</c>/<c>clean</c> commands (via testenv-deck.sh) — never
/// reachable from the shipped agent's normal command surface, and never anything but this one
/// fixed entry (see tasks/conflict-resolution-ui/plan.md).
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
public static class DevSteamShortcut
{
    private const string ShortcutName = "Conflict Game";
    private const string BackupSuffix = ".savelocker-backup";
    // Marks "we created shortcuts.vdf; there was nothing here before" — Remove deletes the file
    // outright in that case instead of restoring a backup that was never taken.
    private const string CreatedMarkerSuffix = ".savelocker-created";

    private static readonly byte[] RootHeader = BuildRootHeader();
    // Identifies our entry by its AppName field rather than a fixed key: the key is now assigned
    // sequentially (see AnalyzeShortcuts), same as every real writer does, so it isn't stable
    // enough on its own to search for.
    private static readonly byte[] AppNameMarker =
        Concat([0x01], CStringBytes("AppName"), CStringBytes(ShortcutName));
    // The Exe field header: the display name alone could belong to a real user shortcut, so an
    // entry only counts as ours when its Exe value points at the savelocker binary.
    private static readonly byte[] ExeKeyMarker =
        Concat([0x01], CStringBytes("Exe"));

    /// <summary>
    /// Adds the shortcut if it is not already present (a second call is a verified no-op, never a
    /// rewrite). Returns the AppID Steam will use for it — per-machine deterministic, since it is
    /// derived from the fixed name and exe (which embeds this machine's prefixDir) — or null if
    /// nothing was found or written.
    /// </summary>
    public static int? Add(string prefixDir, bool withLaunchCommand = false)
    {
        if (string.IsNullOrEmpty(prefixDir) || prefixDir.IndexOf('"') >= 0 || prefixDir.Any(char.IsControl))
        {
            Console.Error.WriteLine("refusing: --prefix must not be empty or contain quotes/control characters - the Exe and StartDir fields are quoted strings with no escaping. Nothing was written.");
            return null;
        }

        var vdfPath = FindShortcutsVdf();
        if (vdfPath is null)
        {
            Console.Error.WriteLine("no Steam userdata directory found - is Steam installed and has it signed in at least once?");
            return null;
        }

        var exe = Path.Combine(prefixDir, "savelocker");
        // Steam's Exe field is a shell-style string, not just a bare path — the quoted binary
        // followed by its fixed argument is how every other non-Steam-shortcut tool (Boilr, Lutris,
        // etc.) passes a subcommand this way, since shortcuts.vdf has no separate "arguments" key.
        var exeField = $"\"{exe}\" fake-game";
        // The AppID stays derived from exeField alone even when a launch command is written — the
        // LaunchOptions wrapper must not change which shortcut Steam maps this entry to.
        var appId = ComputeShortcutAppId(exeField, ShortcutName);
        var launchOptions = withLaunchCommand ? $"\"{exe}\" run -- %command%" : string.Empty;

        if (!File.Exists(vdfPath))
        {
            // No existing shortcuts at all: key "0", same as any writer's first entry. A single
            // closing 0x08 here (not the double-plus this class sees on real, tool-edited files) —
            // the minimal structure SteamVdf.Parse itself already expects and this class's own
            // AnalyzeShortcuts will happily read back on the next Add.
            var fresh = Concat(RootHeader, BuildEntryBytes("0", appId, exeField, prefixDir, launchOptions), [0x08]);
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

        if (IndexOf(original, AppNameMarker) >= 0)
        {
            // The fixed display name alone could belong to a real user shortcut — only an entry
            // whose Exe points at savelocker is ours to leave alone.
            if (!TryLocateOwnEntry(original, out var ownStart, out var ownEnd) || !EntryExeLooksOurs(original, ownStart, ownEnd))
            {
                Console.Error.WriteLine("refusing: a 'Conflict Game' entry is present but it does not point at savelocker - leaving shortcuts.vdf untouched.");
                return null;
            }
            Console.WriteLine("'Conflict Game' shortcut already present - leaving shortcuts.vdf untouched.");
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

        var entryBytes = BuildEntryBytes(analysis.nextIndex.ToString(), appId, exeField, prefixDir, launchOptions);

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
    /// Removes the "Conflict Game" shortcut again. Deletes just our entry's own bytes when they
    /// can still be located, so shortcuts Steam or the user added meanwhile survive — the backup
    /// is only restored when our entry is present but no longer structurally intact (e.g. a writer
    /// reordered its fields). Refuses to overwrite rather than silently discarding unknown changes.
    /// Safe to call even if <see cref="Add"/> never ran — it is then a no-op.
    /// Returns true when the file is clean (or there was nothing to do), false on any refusal.
    /// </summary>
    public static bool Remove()
    {
        var vdfPath = FindShortcutsVdf();
        if (vdfPath is null) return true;

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

        if (TryLocateOwnEntry(current, out var start, out var endExclusive))
        {
            // Same guard as Add's no-op path: only splice an entry whose Exe points at savelocker,
            // never a real user shortcut sharing the fixed display name.
            if (!EntryExeLooksOurs(current, start, endExclusive))
            {
                Console.Error.WriteLine("refusing: the located 'Conflict Game' entry does not point at savelocker - leaving shortcuts.vdf untouched.");
                return false;
            }
            if (FileChangedSince(vdfPath, currentLength, currentMtime))
            {
                Console.Error.WriteLine($"refusing: '{vdfPath}' changed under us - nothing was written.");
                return false;
            }
            var updated = Concat(current.AsSpan(0, start).ToArray(), current.AsSpan(endExclusive).ToArray());
            AtomicFile.WriteAllBytes(vdfPath, updated);
            try { File.Delete(backupPath); } catch { }
            try { File.Delete(createdMarkerPath); } catch { }
            Console.WriteLine($"removed the 'Conflict Game' shortcut from {vdfPath} (other entries untouched).");
            return true;
        }

        if (IndexOf(current, AppNameMarker) < 0)
        {
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

        // The name is present but the entry is no longer locatable — never splice blindly here.
        // A duplicate display name (one of them the user's) must not trigger a backup restore.
        var firstMarker = IndexOf(current, AppNameMarker);
        if (IndexOf(current, AppNameMarker, firstMarker + 1) >= 0)
        {
            Console.Error.WriteLine("refusing: more than one 'Conflict Game' entry is present - remove the unwanted one from Steam itself. Nothing was written.");
            return false;
        }
        if (!FileContainsOwnExe(current))
        {
            Console.Error.WriteLine("refusing: a 'Conflict Game' entry is present but no Exe in the file points at savelocker - leaving shortcuts.vdf untouched.");
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
                "Remove the 'Conflict Game' shortcut from Steam itself.");
            return false;
        }
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

    private static bool TryLocateOwnEntry(byte[] data, out int start, out int endExclusive)
    {
        start = -1;
        endExclusive = -1;
        var marker = IndexOf(data, AppNameMarker);
        if (marker < 0 || IndexOf(data, AppNameMarker, marker + 1) >= 0) return false;

        // Order-independent: an entry's fields may come in any order (a third-party writer decides),
        // so instead of assuming appid-then-AppName, scan backwards for every plausible numeric
        // entry header near the marker and keep the ones whose children — skipped forward without
        // interpreting values — span the marker exactly.
        var candidateStart = -1;
        var candidateEnd = -1;
        var candidateCount = 0;
        var windowStart = Math.Max(0, marker - 4096);
        for (var s = marker - 1; s >= windowStart; s--)
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
                if (end < marker + AppNameMarker.Length) continue;
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

    // Only an entry whose Exe value points at the savelocker binary is ours to keep or splice —
    // the display name alone could belong to a real user shortcut.
    private static bool EntryExeLooksOurs(byte[] data, int start, int endExclusive)
    {
        var value = FindExeValue(data, start, endExclusive);
        return value is not null && value.Contains("savelocker", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindExeValue(byte[] data, int start, int endExclusive)
    {
        for (var i = start; i + ExeKeyMarker.Length <= endExclusive; i++)
        {
            if (data[i] != ExeKeyMarker[0]) continue;
            var match = true;
            for (var j = 1; j < ExeKeyMarker.Length; j++)
                if (data[i + j] != ExeKeyMarker[j]) { match = false; break; }
            if (!match) continue;
            var valueStart = i + ExeKeyMarker.Length;
            var valueEnd = valueStart;
            while (valueEnd < endExclusive && data[valueEnd] != 0x00) valueEnd++;
            if (valueEnd >= endExclusive) return null;
            return Encoding.UTF8.GetString(data, valueStart, valueEnd - valueStart);
        }
        return null;
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
    [StructLayout(LayoutKind.Sequential)]
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

    private static void SkipEntryChildren(byte[] data, ref int pos)
    {
        while (true)
        {
            if (pos >= data.Length) throw new InvalidDataException("Truncated entry.");
            var type = data[pos++];
            if (type == 0x08) return;
            SkipEntryString(data, ref pos);
            switch (type)
            {
                case 0x00: SkipEntryChildren(data, ref pos); break;
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
    private static string? FindShortcutsVdf()
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
        var crc = Crc32(Encoding.UTF8.GetBytes(exe + appName));
        return unchecked((int)(crc | 0x80000000u));
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    // --- Minimal binary-VDF byte encoding, scoped ONLY to the one fixed entry this class ever
    // writes. Not a general writer — see this class's own doc comment for why a general
    // parse-everything/write-everything round trip was tried and abandoned. ---

    private static byte[] BuildRootHeader()
    {
        // 0x00 <NUL-terminated "shortcuts">, exactly what SteamVdf.Parse expects at offset 0.
        return Concat([0x00], CStringBytes("shortcuts"));
    }

    private static byte[] BuildEntryBytes(string key, int appId, string exeField, string startDir, string launchOptions)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x00);
        WriteBytes(ms, CStringBytes(key));

        WriteInt(ms, "appid", appId);
        WriteString(ms, "AppName", ShortcutName);
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
