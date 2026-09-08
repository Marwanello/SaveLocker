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
/// present but misshapen.
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

    /// <summary>
    /// Adds the shortcut if it is not already present (a second call is a verified no-op, never a
    /// rewrite). Returns the AppID Steam will use for it — deterministic, since it is derived from
    /// the fixed name and exe — or null if nothing was found or written.
    /// </summary>
    public static int? Add(string prefixDir)
    {
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
        var appId = ComputeShortcutAppId(exeField, ShortcutName);

        if (!File.Exists(vdfPath))
        {
            // No existing shortcuts at all: key "0", same as any writer's first entry. A single
            // closing 0x08 here (not the double-plus this class sees on real, tool-edited files) —
            // the minimal structure SteamVdf.Parse itself already expects and this class's own
            // AnalyzeShortcuts will happily read back on the next Add.
            var fresh = Concat(RootHeader, BuildEntryBytes("0", appId, exeField, prefixDir), [0x08]);
            var createdMarkerPath = vdfPath + CreatedMarkerSuffix;
            File.WriteAllBytes(createdMarkerPath, []);
            AtomicFile.WriteAllBytes(vdfPath, fresh);
            return appId;
        }

        var original = File.ReadAllBytes(vdfPath);

        if (IndexOf(original, AppNameMarker) >= 0)
        {
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

        var entryBytes = BuildEntryBytes(analysis.nextIndex.ToString(), appId, exeField, prefixDir);

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
    /// </summary>
    public static void Remove()
    {
        var vdfPath = FindShortcutsVdf();
        if (vdfPath is null) return;

        var backupPath = vdfPath + BackupSuffix;
        var createdMarkerPath = vdfPath + CreatedMarkerSuffix;

        if (!File.Exists(vdfPath))
        {
            try { File.Delete(backupPath); } catch { }
            try { File.Delete(createdMarkerPath); } catch { }
            Console.WriteLine("no shortcuts.vdf found - nothing to restore.");
            return;
        }

        var current = File.ReadAllBytes(vdfPath);
        if (TryLocateOwnEntry(current, out var start, out var endExclusive))
        {
            var updated = Concat(current.AsSpan(0, start).ToArray(), current.AsSpan(endExclusive).ToArray());
            AtomicFile.WriteAllBytes(vdfPath, updated);
            try { File.Delete(backupPath); } catch { }
            try { File.Delete(createdMarkerPath); } catch { }
            Console.WriteLine($"removed the 'Conflict Game' shortcut from {vdfPath} (other entries untouched).");
            return;
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
                return;
            }

            if (current.SequenceEqual(File.ReadAllBytes(backupPath)))
            {
                File.Delete(backupPath);
                Console.WriteLine($"already clean - deleted the stale pre-SaveLocker backup.");
                return;
            }

            Console.Error.WriteLine(
                $"our entry is already gone from '{vdfPath}' but the file differs from the pre-SaveLocker backup - " +
                "refusing to overwrite, so nothing was written. Delete the backup manually once sure: " + backupPath);
            return;
        }

        if (File.Exists(backupPath))
        {
            Console.Error.WriteLine(
                $"our entry is present but no longer in its written shape - restoring '{vdfPath}' from the pre-SaveLocker backup; " +
                "shortcuts added meanwhile may be lost. Restart Steam afterwards and check the library.");
            AtomicFile.WriteAllBytes(vdfPath, File.ReadAllBytes(backupPath));
            File.Delete(backupPath);
        }
        else
        {
            Console.Error.WriteLine(
                $"our entry is present but cannot be located precisely and no backup exists - refusing to touch '{vdfPath}'. " +
                "Remove the 'Conflict Game' shortcut from Steam itself.");
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

        for (var keyLen = 1; keyLen <= 6; keyLen++)
        {
            var s = marker - (1 + (keyLen + 1) + 1 + 6 + 4);
            if (s < 0 || data[s] != 0x00) continue;
            var digits = true;
            for (var i = 0; i < keyLen; i++)
                if (data[s + 1 + i] is < (byte)'0' or > (byte)'9') { digits = false; break; }
            if (!digits || data[s + 1 + keyLen] != 0x00 || data[s + 1 + keyLen + 1] != 0x02) continue;
            if (Encoding.UTF8.GetString(data, s + 1 + keyLen + 2, 6) != "appid\0") continue;

            var pos = s + 1 + (keyLen + 1);
            try { SkipEntryChildren(data, ref pos); }
            catch (InvalidDataException) { continue; }
            start = s;
            endExclusive = pos;
            return true;
        }
        return false;
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

        var userDirs = Directory.GetDirectories(userdata);
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
    /// uses, since Valve has never published it officially. Deterministic for a fixed name+exe.
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

    private static byte[] BuildEntryBytes(string key, int appId, string exeField, string startDir)
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
        // Left empty on purpose for now — no launch-gate wrapper yet, just a plain launch straight
        // into the fake game. Set to "<exe> run -- %command%" once ready to test the gate itself.
        WriteString(ms, "LaunchOptions", "");
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
