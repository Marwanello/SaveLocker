using System.Text.RegularExpressions;

namespace SaveLocker.Agent;

/// <summary>
/// A human title from a ROM or save file name. Emulator games have no store, no manifest entry and
/// usually no library file a scan can rely on, so the file name is the title. It is no longer the
/// identity: a save is matched to its server game by the files it names (Enroller.ServerNameFor, D1), so
/// a title only has to read well — and only the machine that enrolls a game first ever picks it.
/// </summary>
public static partial class RomNames
{
    /// <summary>
    /// <c>"Chrono Trigger (USA) (Rev 1) [!]"</c> → <c>"Chrono Trigger"</c>: every parenthesised or
    /// bracketed tag dropped (No-Intro/Redump region, revision and dump-status tags), whitespace
    /// collapsed. A name that is nothing but tags is returned unchanged rather than emptied.
    /// </summary>
    public static string CleanTitle(string fileBaseName)
    {
        var cleaned = Whitespace().Replace(Tags().Replace(fileBaseName, " "), " ").Trim();
        return cleaned.Length == 0 ? fileBaseName.Trim() : cleaned;
    }

    /// <summary>
    /// A save's title: the cleaned file name, except an arcade ROM's (<c>sf2</c>, <c>scud</c>), which is a set name
    /// and not a title — that takes <paramref name="known"/> (an emulator's own set table), else what ES-DE's
    /// gamelist calls it, cleaned the same way. Either may exist on one machine and not another; that is safe
    /// because an emulator game is found by its files, not its name (Enroller.ServerNameFor, D1).
    /// </summary>
    public static string TitleFor(string fileBaseName, string? system, GamelistXml gamelists, string? known = null) =>
        known is { Length: > 0 } ? known
        : gamelists.ArcadeTitle(system, fileBaseName) is { Length: > 0 } listed ? CleanTitle(listed)
        : CleanTitle(fileBaseName);

    /// <summary>
    /// The tags that tell one dump of a ROM from another (tasks/emulator-pull-safety Phase 8): the region
    /// (<c>USA</c>, <c>Europe, Australia</c>), the revision or version (<c>Rev 1</c>, <c>v1.1</c>) and a pre-release
    /// (<c>Beta</c>, <c>Proto 2</c>, <c>Demo</c>, <c>Sample</c>), in the file's order. Language lists (<c>En,Fr,De</c>) and
    /// dump marks (<c>[!]</c>, <c>[b]</c>) are not editions and are left out.
    /// </summary>
    public static IReadOnlyList<string> EditionTags(string romBase) =>
        Paren().Matches(romBase).Select(m => m.Groups[1].Value.Trim())
            .Where(t => IsRegion(t) || Revision().IsMatch(t) || PreRelease().IsMatch(t))
            .ToList();

    /// <summary>Is <paramref name="tag"/> a revision or version (<c>Rev 1</c>, <c>Rev A</c>, <c>v1.02</c>)?</summary>
    public static bool IsRevision(string tag) => Revision().IsMatch(tag);

    /// <summary>
    /// Are two ROM names the same release once revision, version and dump marks are taken off — another revision of
    /// one game (<c>Metroid Prime - Hunters (USA)</c> and <c>… (USA) (Rev 1)</c>)? The region and everything else must
    /// match, ignoring case.
    /// </summary>
    public static bool SameRelease(string a, string b) =>
        string.Equals(ReleaseKey(a), ReleaseKey(b), StringComparison.OrdinalIgnoreCase);

    private static string ReleaseKey(string romBase)
    {
        var text = Brackets().Replace(romBase, " ");
        text = Paren().Replace(text, m => Revision().IsMatch(m.Groups[1].Value.Trim()) ? " " : m.Value);
        return Whitespace().Replace(text, " ").Trim();
    }

    private static bool IsRegion(string tag) =>
        tag.Split(',').Select(p => p.Trim()).All(p => p.Length > 0 && Regions.Contains(p));

    /// <summary>No-Intro's and Redump's region names.</summary>
    private static readonly HashSet<string> Regions = new(StringComparer.OrdinalIgnoreCase)
    {
        "World", "USA", "Europe", "Japan", "Asia", "Australia", "Brazil", "Canada", "China", "France", "Germany", "Greece",
        "Hong Kong", "India", "Italy", "Korea", "Latin America", "Mexico", "Netherlands", "New Zealand", "Norway", "Poland",
        "Portugal", "Russia", "Scandinavia", "Spain", "Sweden", "Taiwan", "UK", "Austria", "Belgium", "Denmark", "Finland",
        "Switzerland", "Ireland", "Israel", "South Africa", "Turkey", "United Kingdom",
    };

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex Paren();

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"^(?:Rev\s*[\w.]+|v\d[\w.]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Revision();

    [GeneratedRegex(@"^(?:Beta|Proto|Demo|Sample|Prototype)(?:\s*\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PreRelease();

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
