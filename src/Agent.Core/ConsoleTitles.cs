using System.Collections.Frozen;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace SaveLocker.Agent;

/// <summary>
/// A disc game's title from the serial its save is filed under (<c>BASLUS-21287…</c> → SLUS-21287 → "Prince of
/// Persia - The Two Thrones (USA)"), from Redump's dats as libretro-database ships them (CC BY-SA 4.0,
/// <c>ConsoleTitles/NOTICE.md</c>; rebuilt by <c>ConsoleTitles/Update-ConsoleTitles.ps1</c>). What a game writes
/// into its own save is no title to go by: PS2 saves carry the series on the first line of <c>icon.sys</c>, so
/// Warrior Within and The Two Thrones were both "Prince of Persia". The region stays in the name — each region's
/// disc has its own serial and reads only its own saves, so a USA and a European save are two games.
/// </summary>
public static partial class ConsoleTitles
{
    private static readonly Lazy<FrozenDictionary<string, string>> Table = new(Load);

    /// <summary>
    /// The title for <paramref name="serial"/> on <paramref name="system"/> (<c>psx</c>, <c>ps2</c>, <c>ps3</c>:
    /// <c>SLUS-21287</c>, <c>SLUS21287</c> or the disc's <c>SLUS_212.87</c>; <c>gc</c>, <c>wii</c>: the game ID, of
    /// which the four-letter game code counts — <c>GALE01</c>, <c>R3ME</c>), or null for one Redump has no disc of.
    /// </summary>
    public static string? For(string system, string? serial)
    {
        if (string.IsNullOrEmpty(serial)) return null;
        string? key = system is "gc" or "wii"
            ? serial.Length >= 4 ? serial[..4].ToUpperInvariant() : null
            : SonySerial().Match(serial) is { Success: true } m ? $"{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value}{m.Groups[3].Value}" : null;
        return key is null ? null : Table.Value.GetValueOrDefault($"{system}\t{key}");
    }

    private static FrozenDictionary<string, string> Load()
    {
        using var stream = typeof(ConsoleTitles).Assembly.GetManifestResourceStream("SaveLocker.Agent.console-titles.tsv.gz");
        if (stream is null) return FrozenDictionary<string, string>.Empty;
        using var reader = new StreamReader(new GZipStream(stream, CompressionMode.Decompress));
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var title = line.LastIndexOf('\t');
            if (title > 0) titles[line[..title]] = line[(title + 1)..];
        }
        return titles.ToFrozenDictionary(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^([A-Za-z]{4})[-_ ]?(\d{3})\.?(\d{2})")]
    private static partial Regex SonySerial();
}
