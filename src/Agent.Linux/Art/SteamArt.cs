namespace SaveLocker.Agent.Linux.Art;

/// <summary>
/// Keeps the SaveLocker library art the installer bundles (<c>~/.local/share/SaveLocker/artwork/</c>) in the accent
/// and mark the agent is showing, and repaints it when they change. A real install never touches Steam (only the
/// test rig's own shortcut is painted in Steam's grid folder, <see cref="WriteForShortcut"/>) — and Steam keeps its
/// own copy of a picture once it is set (Set Custom Artwork hands it the image, not the path), so a repainted file
/// reaches the library only when the person sets it again.
/// <para>
/// The folder is created if it is missing, so every install (release, test, or a tarball unpacked by hand) gets
/// the art without running install.sh; where the installer already put the fixed art, that is what gets replaced.
/// The four files are <c>capsule</c>, <c>capsule-wide</c>, <c>hero</c> and <c>logo</c>, the names the installer
/// gives them.
/// </para>
/// </summary>
public static class SteamArt
{
    public sealed record Outcome(bool FolderFound, int Written)
    {
        public static readonly Outcome None = new(false, 0);
    }

    /// <summary>
    /// Repaint all four pieces in <paramref name="dir"/>. Each piece is rendered and compared with the file, so a
    /// picture painted by an older build (a different hero, say) is replaced even when the look itself is unchanged,
    /// and a piece that already matches is not rewritten.
    /// </summary>
    public static Outcome Apply(string dir, string accent, string mark, Func<string, byte[]> layers)
    {
        Directory.CreateDirectory(dir);
        var written = Paint(SteamArtRenderer.Pieces.Select(p => (p, Path.Combine(dir, p + ".png"))), accent, mark, layers);
        // Older builds kept a stamp beside the pictures; the comparison above replaced it.
        try { File.Delete(Path.Combine(dir, ".savelocker-art.json")); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new Outcome(true, written);
    }

    private static int Paint(IEnumerable<(string Piece, string Path)> targets, string accent, string mark, Func<string, byte[]> layers)
    {
        var colours = AppearancePalette.For(accent);
        int written = 0;
        foreach (var (piece, path) in targets)
        {
            var png = SteamArtRenderer.RenderPng(piece, colours.Dark, colours.DarkOn, mark, layers);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(png)) continue;
            AtomicFile.WriteAllBytes(path, png);
            written++;
        }
        return written;
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

    /// <summary>The four pieces under Steam's names for a shortcut's AppID, in its grid folder. Only pieces that differ are written.</summary>
    public static int WriteForShortcut(string gridDir, uint appId, string accent, string mark, Func<string, byte[]> layers)
    {
        Directory.CreateDirectory(gridDir);
        return Paint(ShortcutFiles(appId).Select(f => (f.Piece, Path.Combine(gridDir, f.Name))), accent, mark, layers);
    }

    public static IReadOnlyList<(string Piece, string Name)> ShortcutFiles(uint appId) =>
        [("capsule", $"{appId}p.png"), ("capsule-wide", $"{appId}.png"), ("hero", $"{appId}_hero.png"), ("logo", $"{appId}_logo.png")];
}
