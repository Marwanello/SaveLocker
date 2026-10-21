namespace SaveLocker.Shared;

/// <summary>
/// Copy this save to another emulator (tasks/multi-emulator-games Phase 6): which pairs of emulators keep one title's save
/// in the same format, and what each file is called on the other side. A fixed table on purpose — a pair is added only
/// once someone has checked the formats really match; a wrong one would hand an emulator a file it cannot read.
/// </summary>
public static class EmulatorSaveCopy
{
    /// <summary>How a pair converts: <see cref="FromExtension"/> files become <see cref="ToExtension"/> files with the same
    /// bytes, or (both null) every file is copied under its own path.</summary>
    public sealed record Rule(string How, string? FromExtension = null, string? ToExtension = null);

    private sealed record Pair(string From, string To, Rule Rule);

    // melonDS writes the DS cartridge save raw as .sav; RetroArch's DS cores (melonDS DS, DeSmuME) write the same raw
    // bytes as .srm. PrimeHack is a Dolphin fork and keeps a Wii or GameCube save in Dolphin's own layout.
    private static readonly Pair[] Pairs =
    [
        new("melonDS", "RetroArch", new Rule("Same bytes. The .sav file is renamed .srm.", ".sav", ".srm")),
        new("RetroArch", "melonDS", new Rule("Same bytes. The .srm file is renamed .sav.", ".srm", ".sav")),
        new("Dolphin", "PrimeHack", new Rule("Same save folder layout. Copied as is.")),
        new("PrimeHack", "Dolphin", new Rule("Same save folder layout. Copied as is.")),
    ];

    /// <summary>The rule from one emulator's save to another's, or null when copying between them is not offered.</summary>
    public static Rule? For(string? fromEmulator, string? toEmulator) =>
        fromEmulator is null || toEmulator is null ? null
        : Pairs.FirstOrDefault(p => string.Equals(p.From, fromEmulator.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(p.To, toEmulator.Trim(), StringComparison.OrdinalIgnoreCase))?.Rule;

    /// <summary>
    /// Each source file's name on the target side, or why the save cannot be copied. <paramref name="sourceFiles"/> are the
    /// source version's primary-folder files; <paramref name="targetFiles"/> the target's latest version's, when it has
    /// one — a renamed save takes the target's own file name, which is its ROM's and may differ from the source's; and
    /// <paramref name="targetInclude"/> the target game's include scope, which every copied file must fall in, or the
    /// target's machines would never see it.
    /// </summary>
    public static (IReadOnlyList<(string From, string To)> Files, string? Refusal) Map(
        Rule rule, IReadOnlyList<string> sourceFiles, IReadOnlyList<string>? targetFiles, IReadOnlyList<string>? targetInclude)
    {
        if (sourceFiles.Count == 0) return ([], "That version has no save files to copy.");
        var include = targetInclude is { Count: > 0 } ? targetInclude : null;
        bool InScope(string path) => include is null || SaveArchive.FilterIncluded([path], include).Count == 1;

        if (rule.FromExtension is null || rule.ToExtension is null)
        {
            var outside = sourceFiles.FirstOrDefault(f => !InScope(f));
            return outside is null
                ? (sourceFiles.Select(f => (f, f)).ToList(), null)
                : ([], $"'{outside}' is not one of the other emulator's save files for this game.");
        }

        var saves = sourceFiles.Where(f => f.EndsWith(rule.FromExtension, StringComparison.OrdinalIgnoreCase)).ToList();
        if (saves.Count != 1)
            return ([], saves.Count == 0
                ? $"That version has no {rule.FromExtension} file to copy."
                : $"That version has {saves.Count} {rule.FromExtension} files; copying only works for one.");
        var from = saves[0];

        var candidates = new List<string>();
        if (targetFiles?.Where(f => f.EndsWith(rule.ToExtension, StringComparison.OrdinalIgnoreCase)).ToList() is [var own])
            candidates.Add(own);
        candidates.Add(from[..^rule.FromExtension.Length] + rule.ToExtension);
        // The scope may name the save outright: RetroArch's "<ROM>.srm".
        candidates.AddRange(include?.Where(g => g.IndexOfAny(['*', '?', '[']) < 0 &&
                                                g.EndsWith(rule.ToExtension, StringComparison.OrdinalIgnoreCase)) ?? []);
        var to = candidates.FirstOrDefault(InScope);
        return to is null
            ? ([], $"No {rule.ToExtension} name in the other emulator's save scope fits '{from}'.")
            : ([(from, to)], null);
    }
}
