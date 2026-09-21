using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>One accent's colours as 0xRRGGBB: the value on a dark surface, the value on a light one,
/// and the ink that sits on top of each (what plan.md calls <c>on-accent</c>).</summary>
public readonly record struct AccentColors(int Dark, int DarkOn, int Light, int LightOn);

/// <summary>
/// What each accent id looks like, for the surfaces that are drawn in C# rather than CSS: the Windows
/// tray icon today, the Deck UI's <c>Theme.cs</c> once Group 6 gives it Checkpoint tokens.
/// <para>
/// A <b>hand-kept copy</b> of <c>ACCENTS</c> in <c>web/src/appearance.ts</c> and
/// <c>agent-ui/src/appearance.ts</c>, which are themselves copies of the prototype's table — three
/// packages with no shared build to import from. Change all three together;
/// <c>tests/run-local-api-tests.ps1</c> checks that this one names exactly the ids in
/// <see cref="Appearances.Accents"/>.
/// </para>
/// </summary>
public static class AppearancePalette
{
    private static readonly Dictionary<string, AccentColors> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ember"]   = new(0xE0533C, 0x160F0E, 0xC0432C, 0xFFF8F6),
        ["coolant"] = new(0x35A5BD, 0x08171B, 0x12768D, 0xF2FBFD),
        ["arcade"]  = new(0xD4589B, 0x1A0D15, 0xB23C7C, 0xFFF5FA),
        ["cobalt"]  = new(0x5B81D6, 0x0A0F1C, 0x3A5CBE, 0xF6F8FF),
        ["emerald"] = new(0x2FBF71, 0x06170E, 0x1A7F4B, 0xF2FDF6),
        ["stealth"] = new(0xDCD7CC, 0x141416, 0x2E2B28, 0xFAF8F5),
    };

    /// <summary>The ids this palette knows, for the check that it has not drifted from
    /// <see cref="Appearances.Accents"/>.</summary>
    public static IReadOnlyCollection<string> Ids => Table.Keys;

    /// <summary>The colours for an accent id; Ember for anything unrecognised.</summary>
    public static AccentColors For(string? accent) =>
        accent is not null && Table.TryGetValue(accent, out var colours) ? colours : Table["ember"];
}
