using System.Collections.Frozen;
using System.IO.Compression;

namespace SaveLocker.Shared;

/// <summary>
/// A Switch game's eShop name and icon by its title ID, from blawar/titledb (MIT, <c>SwitchTitles/NOTICE.md</c>; rebuilt
/// by <c>SwitchTitles/Update-SwitchTitles.ps1</c>). The agent names a Switch save from it when the machine itself has no
/// name for it (no Ryujinx record, no ROM named after it); the server takes the icon as the game's.
/// </summary>
public static class SwitchTitles
{
    public sealed record Title(string Name, string? IconHash);

    /// <summary>Where an icon hash is served from. The server can point it elsewhere (its tests' stand-in host).</summary>
    public const string EshopImageBase = "https://img-eshop.cdn.nintendo.net/i/";

    private static readonly Lazy<FrozenDictionary<string, Title>> Table = new(Load);

    /// <summary>The eShop's entry for a 16-digit title ID, or null for one the table has no game of.</summary>
    public static Title? For(string? titleId) =>
        titleId is { Length: 16 } ? Table.Value.GetValueOrDefault(titleId.ToUpperInvariant()) : null;

    /// <summary>The title ID a <c>switch:</c> emulator key names, or null for any other key.</summary>
    public static string? TitleIdOf(string? emulatorKey) =>
        EmulatorKeys.Clean(emulatorKey) is { } key && key.StartsWith(EmulatorKeys.SwitchPrefix, StringComparison.Ordinal)
            ? key[EmulatorKeys.SwitchPrefix.Length..]
            : null;

    /// <summary>The name a Switch game takes when nobody knows a better one: <c>Switch 0100F2C0115B6000</c>.</summary>
    public static string Placeholder(string titleId) => $"Switch {titleId.ToUpperInvariant()}";

    private static FrozenDictionary<string, Title> Load()
    {
        using var stream = typeof(SwitchTitles).Assembly.GetManifestResourceStream("SaveLocker.Shared.switch-titles.tsv.gz");
        if (stream is null) return FrozenDictionary<string, Title>.Empty;
        using var reader = new StreamReader(new GZipStream(stream, CompressionMode.Decompress));
        var titles = new Dictionary<string, Title>(StringComparer.Ordinal);
        // title ID, name, icon hash
        while (reader.ReadLine() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length < 2 || f[0].Length != 16 || f[1].Length == 0) continue;
            titles[f[0]] = new Title(f[1], f.Length > 2 && f[2].Length == 64 ? f[2] : null);
        }
        return titles.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
