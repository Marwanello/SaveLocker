using System.Text.RegularExpressions;

namespace SaveLocker.Shared;

/// <summary>
/// One console entry per game, one save tree per emulator (tasks/multi-emulator-games). A game saved with two emulators is
/// two server games — "Metroid Prime - Hunters" and "Metroid Prime - Hunters (RetroArch)" — and stays two for sync. This
/// is the rule that sees they are one title: the name with the emulator's suffix and the region tag taken off, and what
/// is left in the suffix ("Rev 0") as the edition that tells two trees of one emulator apart. Pure, so the server and
/// the tests read it alike.
/// </summary>
public static partial class TitleNames
{
    /// <summary>A game's name as titles group it: <see cref="BaseName"/>, and the edition its suffix named, or null.</summary>
    public readonly record struct Parsed(string BaseName, string? Edition);

    /// <summary>
    /// <c>"Metroid Prime - Hunters (RetroArch, Rev 0)"</c> → <c>("Metroid Prime - Hunters", "Rev 0")</c>. A trailing
    /// group whose first entry is one of <paramref name="emulators"/> is the server's suffix; then trailing region tags
    /// equal to <paramref name="region"/> (any region tag when none is recorded) are dropped, and revision or
    /// pre-release tags join the edition. A name that would be emptied is kept whole.
    /// </summary>
    public static Parsed Parse(string name, IEnumerable<string> emulators, string? region)
    {
        var text = name.Trim();
        var edition = new List<string>();
        var emus = emulators.Select(e => e.Trim()).Where(e => e.Length > 0).ToList();
        var regionKey = region?.Trim() ?? "";

        if (TrailingGroup().Match(text) is { Success: true } suffix)
        {
            var parts = suffix.Groups[1].Value.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (parts.Count > 0 && emus.Any(e => string.Equals(e, parts[0], StringComparison.OrdinalIgnoreCase)))
            {
                text = text[..suffix.Index].TrimEnd();
                edition.AddRange(parts.Skip(1).Where(p => !IsRegion(p)));
            }
        }

        // "Metroid Prime - Hunters (USA) (Rev 1)": the save file's own name, as earlier agents named a game.
        while (TrailingGroup().Match(text) is { Success: true } tag)
        {
            var value = tag.Groups[1].Value.Trim();
            if (IsRegion(value) && (regionKey.Length == 0 || string.Equals(value, regionKey, StringComparison.OrdinalIgnoreCase))) { }
            else if (Revision().IsMatch(value) || PreRelease().IsMatch(value)) edition.Insert(0, value);
            else break;
            text = text[..tag.Index].TrimEnd();
        }

        if (text.Length == 0) return new Parsed(name.Trim(), null);
        return new Parsed(text, edition.Count == 0 ? null : string.Join(", ", edition.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// The console a game is for, as titles compare it: the first emulator source's console tag ("DS", "Wii" — the tags
    /// that are not how it was installed), else its release's console. Null when neither says, and an unknown platform
    /// matches any (<see cref="SamePlatform"/>).
    /// </summary>
    public static string? Platform(IEnumerable<string[]?> emulatorSourceTags, string? releaseConsole)
    {
        foreach (var tags in emulatorSourceTags)
            if (tags?.Select(t => t.Trim()).FirstOrDefault(t => t.Length > 0 && !NotAConsole.Contains(t)) is { } console)
                return console;
        return string.IsNullOrWhiteSpace(releaseConsole) ? null : releaseConsole.Trim();
    }

    /// <summary>Equal ignoring case, or either unknown.</summary>
    public static bool SamePlatform(string? a, string? b) =>
        a is null || b is null || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Equal ignoring case, or either unrecorded: a game made before releases were recorded joins any region's.</summary>
    public static bool SameRegion(string? a, string? b) =>
        string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) ||
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Is <paramref name="tag"/> a No-Intro or Redump region list ("USA", "Europe, Australia")?</summary>
    public static bool IsRegion(string tag) =>
        tag.Split(',').Select(p => p.Trim()).All(p => p.Length > 0 && Regions.Contains(p));

    // How a save was installed, which GameSources also writes as tags; never a console.
    private static readonly HashSet<string> NotAConsole = new(StringComparer.OrdinalIgnoreCase)
        { "Flatpak", "EmuDeck", "Steam shortcut", "Proton", "MoonDeck", "Wine prefix" };

    // No-Intro's and Redump's region names: the same list as the agent's RomNames.
    private static readonly HashSet<string> Regions = new(StringComparer.OrdinalIgnoreCase)
    {
        "World", "USA", "Europe", "Japan", "Asia", "Australia", "Brazil", "Canada", "China", "France", "Germany", "Greece",
        "Hong Kong", "India", "Italy", "Korea", "Latin America", "Mexico", "Netherlands", "New Zealand", "Norway", "Poland",
        "Portugal", "Russia", "Scandinavia", "Spain", "Sweden", "Taiwan", "UK", "Austria", "Belgium", "Denmark", "Finland",
        "Switzerland", "Ireland", "Israel", "South Africa", "Turkey", "United Kingdom",
    };

    [GeneratedRegex(@"\(([^()]*)\)\s*$")]
    private static partial Regex TrailingGroup();

    [GeneratedRegex(@"^(?:Rev\s+[\w.]+|v\d[\w.]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Revision();

    [GeneratedRegex(@"^(?:Beta|Proto|Demo|Sample|Prototype)(?:\s*\d+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PreRelease();
}
