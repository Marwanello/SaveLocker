namespace SaveLocker.Shared;

/// <summary>
/// The look a surface draws itself in: which theme, which accent, which app mark. Three short ids —
/// never colours. The palette each id resolves to lives in the front ends (<c>web/src/appearance.ts</c>,
/// <c>agent-ui/src/appearance.ts</c>) and the Deck UI's <c>Theme.cs</c>, because a colour is a
/// rendering decision and the server should not have to know what an accent looks like on a screen
/// it will never see. See <c>docs/tasks/checkpoint-ui/plan.md</c>.
/// </summary>
/// <param name="Theme"><c>"system"</c> (follow the OS), <c>"dark"</c> or <c>"light"</c>.</param>
/// <param name="Accent"><c>"ember"</c>, <c>"coolant"</c>, <c>"arcade"</c>, <c>"cobalt"</c>, <c>"emerald"</c> or <c>"stealth"</c>.</param>
/// <param name="Mark"><c>"pixel"</c> (Pixel lock), <c>"cartridge"</c> or <c>"memcard"</c> (Memory card).</param>
public record AppearanceDto(string Theme, string Accent, string Mark);

/// <summary>
/// The console's appearance setting: the look itself, and whether the server hands it to enrolled
/// agents. <paramref name="PushToAgents"/> off means "each machine keeps its own" — agents are then
/// sent no appearance at all rather than a stale one.
/// </summary>
public record AppearanceSettingsDto(AppearanceDto Look, bool PushToAgents);

/// <summary>
/// Body of <c>POST /api/settings/appearance</c>. Any id the server does not know is a 400.
/// <para>
/// <paramref name="PushToAgents"/> defaults to <b>true</b>, the value an install that never opened the
/// Appearance card has: without the default a body that leaves it out (a script setting only the
/// accent) deserialises to <c>false</c> and silently stops sharing the look with every machine.
/// </para>
/// </summary>
public record SetAppearanceRequest(string Theme, string Accent, string Mark, bool PushToAgents = true);

/// <summary>
/// The closed vocabulary of <see cref="AppearanceDto"/>, and the one place that decides what a value
/// means when it is missing or unrecognised.
/// <para>
/// A receiver <b>normalises, it does not reject</b>: an agent that gets an id it does not know — a
/// newer server that added a sixth accent, a hand-edited <c>config.json</c> — must draw the default
/// rather than nothing, so an unknown id can never leave a window unstyled.
/// </para>
/// </summary>
public static class Appearances
{
    public static readonly string[] Themes = { "system", "dark", "light" };
    public static readonly string[] Accents = { "ember", "coolant", "arcade", "cobalt", "emerald", "stealth" };
    public static readonly string[] Marks = { "pixel", "cartridge", "memcard" };

    /// <summary>Pixel lock on Ember, following the OS theme — what every surface drew before this existed.</summary>
    public static AppearanceDto Default { get; } = new("system", "ember", "pixel");

    /// <summary>True when every field is a known id (case-insensitive).</summary>
    public static bool IsValid(string? theme, string? accent, string? mark) =>
        Has(Themes, theme) && Has(Accents, accent) && Has(Marks, mark);

    /// <summary>
    /// A known-good copy of <paramref name="value"/>: each field lower-cased and, when it is not a
    /// known id, replaced with the default for that field. Never null, never throws.
    /// </summary>
    public static AppearanceDto Normalize(AppearanceDto? value) => new(
        Pick(Themes, value?.Theme, Default.Theme),
        Pick(Accents, value?.Accent, Default.Accent),
        Pick(Marks, value?.Mark, Default.Mark));

    private static bool Has(string[] ids, string? value) =>
        value is not null && ids.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static string Pick(string[] ids, string? value, string fallback) =>
        Has(ids, value) ? value!.Trim().ToLowerInvariant() : fallback;
}
