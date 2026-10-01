using System.Security.Cryptography;
using System.Text.Json;

namespace SaveLocker.Agent.Linux.Art;

/// <summary>
/// Keeps the SaveLocker library art the installer bundles (<c>~/.local/share/SaveLocker/artwork/</c>) in the accent
/// and mark the agent is showing, and repaints it when they change. Steam is never touched — and Steam keeps its
/// own copy of a picture once it is set (Set Custom Artwork hands it the image, not the path), so a repainted file
/// reaches the library only when the person sets it again.
/// <para>
/// The folder is created if it is missing, so every install (release, test, or a tarball unpacked by hand) gets
/// the art without running install.sh; where the installer already put the fixed art, that is what gets replaced. The four files are <c>capsule</c>, <c>capsule-wide</c>, <c>hero</c>
/// and <c>logo</c>, the names the installer gives them.
/// </para>
/// </summary>
public static class SteamArt
{
    private const int MarkerVersion = 2;

    public sealed record Outcome(bool FolderFound, int Written)
    {
        public static readonly Outcome None = new(false, 0);
    }

    /// <summary>
    /// Repaint all four pieces in <paramref name="dir"/>. Idempotent: files that already hold this look are not
    /// rewritten, so calling it at every start and every change costs one read per file.
    /// </summary>
    public static Outcome Apply(string dir, string accent, string mark, Func<string, byte[]> layers)
    {
        Directory.CreateDirectory(dir);

        var colours = AppearancePalette.For(accent);
        var stamp = $"{accent.ToLowerInvariant()}|{mark.ToLowerInvariant()}|{MarkerVersion}";
        var markerPath = Path.Combine(dir, ".savelocker-art.json");
        var marker = ReadMarker(markerPath);
        var files = new Dictionary<string, string>();
        int written = 0;

        foreach (var piece in SteamArtRenderer.Pieces)
        {
            var path = Path.Combine(dir, piece + ".png");
            var current = File.Exists(path) ? Sha(File.ReadAllBytes(path)) : null;
            // Already this look, and untouched since we wrote it.
            if (current is not null && marker.Stamp == stamp && marker.Files.TryGetValue(piece, out var last) && last == current)
            {
                files[piece] = current;
                continue;
            }

            var png = SteamArtRenderer.RenderPng(piece, colours.Dark, colours.DarkOn, mark, layers);
            AtomicFile.WriteAllBytes(path, png);
            files[piece] = Sha(png);
            written++;
        }
        // Nothing written means every piece matched the marker, which therefore already says exactly this.
        if (written > 0) AtomicFile.WriteAllText(markerPath, JsonSerializer.Serialize(new Marker(stamp, files)));
        return new Outcome(true, written);
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
}
