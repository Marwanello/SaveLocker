using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SaveLocker.Agent.Linux.Art;

/// <summary>
/// Puts SaveLocker's own library art on SaveLocker's own Steam shortcut, in the accent and mark the agent is
/// showing, and repaints it when they change.
/// <para>
/// Steam reads a non-Steam shortcut's art from <c>userdata/&lt;id&gt;/config/grid/</c>, by the shortcut's AppID:
/// <c>&lt;id&gt;p.png</c> the portrait capsule, <c>&lt;id&gt;.png</c> the wide one, <c>_hero</c> and <c>_logo</c>.
/// The shortcut is the one the installer's step 4 has the user add and name "SaveLocker" (or whose program is the
/// <c>savelocker</c> binary) — nothing is created here, and with no such shortcut nothing is written.
/// </para>
/// <para>
/// Only art SaveLocker wrote is ever replaced. A slot a person filled by hand (Set Custom Artwork, SteamGridDB)
/// holds a file this never wrote, so it is left alone; a slot that is empty or still holds our last picture is
/// painted. Steam shows new art after it restarts.
/// </para>
/// </summary>
public static class SteamArt
{
    public const string ShortcutName = "SaveLocker";
    private const int MarkerVersion = 1;

    public sealed record Target(string GridDir, uint AppId);

    public sealed record Outcome(int Shortcuts, int Written, int LeftAlone)
    {
        public static readonly Outcome None = new(0, 0, 0);
    }

    /// <summary>(piece, file name for a shortcut's AppID) — Steam's own naming.</summary>
    private static IEnumerable<(string Piece, string File)> Slots(uint id) =>
    [
        ("capsule", $"{id}p.png"),
        ("capsule-wide", $"{id}.png"),
        ("hero", $"{id}_hero.png"),
        ("logo", $"{id}_logo.png"),
    ];

    /// <summary>Every Steam account's SaveLocker shortcut under the given roots.</summary>
    public static IReadOnlyList<Target> FindTargets(IEnumerable<string> steamRoots)
    {
        var found = new List<Target>();
        foreach (var root in steamRoots)
        {
            var userdata = Path.Combine(root, "userdata");
            if (!Directory.Exists(userdata)) continue;
            foreach (var user in SafeDirs(userdata))
            {
                var vdf = Path.Combine(user, "config", "shortcuts.vdf");
                byte[] bytes;
                try { if (!File.Exists(vdf)) continue; bytes = File.ReadAllBytes(vdf); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                foreach (var s in SteamShortcuts.Parse(bytes))
                {
                    if (!IsSaveLocker(s) || !uint.TryParse(s.AppId, out var id)) continue;
                    found.Add(new Target(Path.Combine(user, "config", "grid"), id));
                }
            }
        }
        return found;
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static bool IsSaveLocker(SteamShortcut s)
    {
        if (string.Equals(s.AppName, ShortcutName, StringComparison.OrdinalIgnoreCase)) return true;
        // The Exe field is a shell-style string: the (maybe quoted) binary, then arguments.
        var exe = s.Exe?.Trim();
        if (string.IsNullOrEmpty(exe)) return false;
        var program = exe.StartsWith('"') ? exe.Split('"', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : exe.Split(' ')[0];
        return string.Equals(Path.GetFileName(program ?? ""), "savelocker", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Paint all four pieces for every SaveLocker shortcut found. Idempotent: a slot that already holds this
    /// look is not rewritten, so calling it at every start and every change costs one read per file.
    /// </summary>
    public static Outcome Apply(IEnumerable<string> steamRoots, string accent, string mark, Func<string, byte[]> layers)
    {
        var targets = FindTargets(steamRoots);
        if (targets.Count == 0) return Outcome.None;

        var colours = AppearancePalette.For(accent);
        var stamp = $"{accent.ToLowerInvariant()}|{mark.ToLowerInvariant()}|{MarkerVersion}";
        int written = 0, left = 0;

        foreach (var t in targets)
        {
            Directory.CreateDirectory(t.GridDir);
            var markerPath = Path.Combine(t.GridDir, $".savelocker-art-{t.AppId}.json");
            var marker = ReadMarker(markerPath);
            var files = new Dictionary<string, string>(marker.Files);
            byte[]? cache = null;

            foreach (var (piece, name) in Slots(t.AppId))
            {
                var path = Path.Combine(t.GridDir, name);
                var current = File.Exists(path) ? Sha(File.ReadAllBytes(path)) : null;
                var ours = marker.Files.TryGetValue(name, out var last) && last == current;

                // Someone else's file: never touched.
                if (current is not null && !ours) { left++; continue; }
                // Ours and already this look.
                if (ours && marker.Stamp == stamp) continue;

                cache = SteamArtRenderer.RenderPng(piece, colours.Dark, colours.DarkOn, mark, layers);
                AtomicFile.WriteAllBytes(path, cache);
                files[name] = Sha(cache);
                written++;
            }
            AtomicFile.WriteAllText(markerPath, JsonSerializer.Serialize(new Marker(stamp, files)));
        }
        return new Outcome(targets.Count, written, left);
    }

    /// <summary>Render the four pieces into a plain folder — no Steam involved — for a look at the result.</summary>
    public static IReadOnlyList<string> Export(string dir, string accent, string mark, Func<string, byte[]> layers)
    {
        Directory.CreateDirectory(dir);
        var colours = AppearancePalette.For(accent);
        var written = new List<string>();
        foreach (var piece in SteamArtRenderer.Pieces)
        {
            var path = Path.Combine(dir, piece + ".png");
            File.WriteAllBytes(path, SteamArtRenderer.RenderPng(piece, colours.Dark, colours.DarkOn, mark, layers));
            written.Add(path);
        }
        return written;
    }

    private sealed record Marker(string Stamp, Dictionary<string, string> Files);

    private static Marker ReadMarker(string path)
    {
        try
        {
            if (File.Exists(path) &&
                JsonSerializer.Deserialize<Marker>(File.ReadAllText(path)) is { Files: not null } m)
                return m;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new Marker("", new Dictionary<string, string>());
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// A minimal binary <c>shortcuts.vdf</c> holding one entry, for tests and the test rig's fake Steam root —
    /// the same shape <c>SteamShortcuts.Parse</c> reads.
    /// </summary>
    public static byte[] BuildShortcutsVdf(string appName, string exe, int appId)
    {
        using var ms = new MemoryStream();
        void Bytes(params byte[] b) => ms.Write(b);
        void Cstr(string s) { Bytes(Encoding.UTF8.GetBytes(s)); Bytes(0); }
        Bytes(0); Cstr("shortcuts");
        Bytes(0); Cstr("0");
        Bytes(2); Cstr("appid"); Bytes(BitConverter.GetBytes(appId));
        Bytes(1); Cstr("AppName"); Cstr(appName);
        Bytes(1); Cstr("Exe"); Cstr(exe);
        Bytes(1); Cstr("StartDir"); Cstr("\"/\"");
        Bytes(8);   // entry
        Bytes(8);   // shortcuts
        Bytes(8);   // root
        return ms.ToArray();
    }
}
