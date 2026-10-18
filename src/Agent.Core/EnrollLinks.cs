using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// Which server game an emulator save joins when it is added, and what else the user may pick instead
/// (tasks/emulator-saves Phase 16, "Linking by hand", version 2 of the mockup). The automatic choice is
/// <see cref="Enroller.ServerNameFor"/>'s. By hand the user may keep the save as its own game, or join
/// another server game that keeps exactly the same files — or, since Phase 17, a same-titled game of the same
/// emulator that keeps other files (another dump of the ROM), with this machine keeping its own file names for it
/// (<see cref="Rename"/>, <see cref="FileNames"/>). Another emulator's save of the game stays apart.
/// </summary>
public static class EnrollLinks
{
    public const string Auto = "auto";
    public const string Separate = "separate";
    public const string Game = "game";
    /// <summary>Join a game of the same emulator that keeps other file names, with this machine's own names for them (Phase 17).</summary>
    public const string Rename = "rename";

    /// <summary>The options for one emulator candidate, the automatic one first. Empty for any other candidate.</summary>
    public static IReadOnlyList<LinkOption> For(IReadOnlyList<GameDto> server, ScanCandidate c)
    {
        if (c.Source == ScanSource.Emulator && c.NotSyncable is null && SwitchTitles.TitleIdOf(c.EmulatorKey) is { } titleId)
            return SwitchOptions(server, c, titleId);
        if (c.Source != ScanSource.Emulator || c.IncludeGlobs is not { Count: > 0 } || c.NotSyncable is not null) return [];
        var extras = c.ExtraSaveDirs ?? Array.Empty<DeclaredSavePath>();
        var options = new List<LinkOption>();
        var auto = Enroller.ServerNameFor(server, c, extras);
        var joined = auto is null ? null : server.FirstOrDefault(g => Is(g, auto) && Enroller.SameFiles(g, c));
        var file = c.IncludeGlobs[0];

        if (c.UntouchedSeed && joined is null)
            options.Add(new LinkOption(Auto, LinkKind.Blocked, c.Name, null, SeedDetail, "Not played"));
        else if (auto is null)
            options.Add(new LinkOption(Auto, LinkKind.Blocked, c.Name, null,
                "Every name this save could take on the server is already another game’s.", "Not available"));
        else if (joined is not null)
            options.Add(new LinkOption(Auto, LinkKind.Join, joined.Name, joined.Id,
                $"The server’s “{joined.Name}” keeps {Files(joined)}: the same save files.", "Same save files"));
        else
            options.Add(new LinkOption(Auto, LinkKind.New, auto, null,
                $"No server game keeps {file} yet.", "New"));

        if (joined is not null && !c.UntouchedSeed && SeparateName(server, c) is { } own)
            options.Add(new LinkOption(Separate, LinkKind.New, own, null,
                $"Creates “{own}”. It never syncs with “{joined.Name}”.", "New"));

        foreach (var g in server.Where(g => g.Id != joined?.Id && Enroller.SameFiles(g, c))
                     .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            options.Add(new LinkOption(Game, LinkKind.Join, g.Name, g.Id,
                $"Also keeps {Files(g)}: the same save files.", "Same save files"));

        // Same title, other files: shown so the user sees why it is not linked, never pickable here.
        var names = Enroller.NamesFor(c);
        foreach (var g in server.Where(g => !Enroller.SameFiles(g, c) && (SameTitle(g, c, names) || OtherEmulator(g, c)))
                     .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            options.Add(RenamePairs(g, c, out var why) is { } pairs
                ? new LinkOption(Rename, LinkKind.Join, g.Name, g.Id,
                    $"Keeps {pairs[0].Server}. This device keeps its own name, {pairs[0].Local}, for it and its states. " +
                    "The first sync asks which of the two saves to keep.", "Different file names")
                : why is not null
                ? new LinkOption(Game, LinkKind.Blocked, g.Name, g.Id, why, "Different file names")
                : OtherEmulator(g, c)
                ? new LinkOption(Game, LinkKind.Blocked, g.Name, g.Id,
                    $"Keeps the same files, saved with {string.Join(", ", g.Emulators!)}. Saves from different emulators are kept as separate games.",
                    "Another emulator")
                : Enroller.OtherRegion(g, c)
                ? new LinkOption(Game, LinkKind.Blocked, g.Name, g.Id,
                    $"The {g.Release!.Region} release. A disc reads only its own region’s saves, so each region is its own game.",
                    "Another region")
                : new LinkOption(Game, LinkKind.Blocked, g.Name, g.Id,
                    g.IncludeGlobs is { Length: > 0 }
                        ? $"Keeps {Files(g)}, not {file}, and another emulator saved it, or its files have no counterpart here. " +
                          "Only another dump's save from the same emulator can be linked under its own names."
                        : "A game that keeps a whole save folder, like a PC game. Only another save of this game can be linked.",
                    g.IncludeGlobs is { Length: > 0 } ? "Different file names" : "Whole folder"));
        return options;
    }

    /// <summary>
    /// A Switch save's options (D2): a title is one game on the server whichever emulator saved it, so it joins the game
    /// keyed to its title, and is never kept apart from it. With none yet, it is a new game, or it may join a same-titled
    /// game no title was recorded for (one added by hand, or by an agent from before D2), which then takes this title.
    /// A same-titled game of another title is listed greyed out.
    /// </summary>
    private static List<LinkOption> SwitchOptions(IReadOnlyList<GameDto> server, ScanCandidate c, string titleId)
    {
        var options = new List<LinkOption>();
        if (server.FirstOrDefault(g => SameKey(g, c)) is { } keyed)
        {
            var name = Enroller.KeyedName(server, keyed, c);
            options.Add(new LinkOption(Auto, LinkKind.Join, name, keyed.Id,
                $"The server’s “{keyed.Name}” is this Switch title ({titleId}), whichever emulator saved it." +
                (name == keyed.Name ? "" : $" It takes the name “{name}”."), "Same title"));
            return options;
        }
        var auto = Enroller.ServerNameFor(server, c, c.ExtraSaveDirs ?? Array.Empty<DeclaredSavePath>());
        options.Add(auto is null
            ? new LinkOption(Auto, LinkKind.Blocked, c.Name, null,
                "Every name this save could take on the server is already another game’s.", "Not available")
            : new LinkOption(Auto, LinkKind.New, auto, null, $"No server game is this Switch title ({titleId}) yet.", "New"));
        var names = Enroller.NamesFor(c);
        foreach (var g in server.Where(g => SameTitle(g, c, names)).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            options.Add(TakesSwitchTitle(g)
                ? new LinkOption(Game, LinkKind.Join, g.Name, g.Id,
                    $"No Switch title was recorded for it. Joining makes it {titleId}, the game every Switch emulator’s save of it joins.",
                    "Takes this title")
                : g.EmulatorKey is { } other
                ? new LinkOption(Game, LinkKind.Blocked, g.Name, g.Id,
                    $"Another Switch title ({SwitchTitles.TitleIdOf(other) ?? other}). Each title is its own game.", "Another title")
                : new LinkOption(Game, LinkKind.Blocked, g.Name, g.Id,
                    $"Keeps {Files(g)}. Only a game that keeps a whole save folder can take a Switch title.", "Different files"));
        return options;
    }

    /// <summary>
    /// The pairs that link save <paramref name="c"/> to server game <paramref name="g"/> under this machine's own names (Phase 17),
    /// or null with <paramref name="why"/> when it cannot be (null too when it is simply not that kind of game). Same emulator
    /// only (maintainer's call, 2026-10-18), the same region, and every file of the save's scope paired with the game's file
    /// of the same extension: <c>Chrono Trigger (USA) (Rev 1).srm</c> here is the server's <c>Chrono Trigger (USA).srm</c>.
    /// States are not listed: they take the swap of the save's name (<see cref="FileNames"/>). A file of the server's name
    /// already here is refused — a second dump's save under that name; both would land on one file.
    /// </summary>
    public static IReadOnlyList<FileNameDto>? RenamePairs(GameDto g, ScanCandidate c, out string? why)
    {
        why = null;
        if (c.Source != ScanSource.Emulator || c.EmulatorName is null || c.IncludeGlobs is not { Count: > 0 } local ||
            g.IncludeGlobs is not { Length: > 0 } server || c.EmulatorKey is not null)
            return null;
        if (g.Emulators is not { Length: > 0 } emulators || !emulators.Contains(c.EmulatorName, StringComparer.OrdinalIgnoreCase) ||
            Enroller.OtherRegion(g, c))
            return null;
        if (local.Concat(server).Any(n => n.Contains('*') || n.Contains('?') || n.Contains('/')))
            return null;
        var serverKeys = (g.ExtraPaths ?? []).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        if ((c.ExtraSaveDirs ?? []).Any(e => !serverKeys.Contains(e.Key)))
        {
            why = "It keeps other save folders than this save does.";
            return null;
        }
        var pairs = new List<FileNameDto>();
        foreach (var name in local)
            if (server.FirstOrDefault(s => string.Equals(Path.GetExtension(s), Path.GetExtension(name), StringComparison.OrdinalIgnoreCase)) is { } match &&
                !string.Equals(match, name, StringComparison.Ordinal))
                pairs.Add(new FileNameDto(SaveRoot.PrimaryKey, match, name));
        if (pairs.Count == 0 || !string.Equals(pairs[0].Local, local[0], StringComparison.Ordinal))
        {
            why = $"Keeps {Files(g)}: no file of it matches {local[0]}.";
            return null;
        }
        if (c.SuggestedSaveDir is { } dir && pairs.FirstOrDefault(p => File.Exists(Path.Combine(dir, p.Server))) is { } clash)
        {
            why = $"This device also has {clash.Server}, another save under the server's name: linking would put two saves on one file.";
            return null;
        }
        return FileNames.Validate(pairs) is null ? pairs : null;
    }

    private static bool SameKey(GameDto g, ScanCandidate c) =>
        c.EmulatorKey is not null && string.Equals(g.EmulatorKey, c.EmulatorKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>A game a Switch save may join by hand: no title recorded, and its whole folder is the save.</summary>
    private static bool TakesSwitchTitle(GameDto g) =>
        g.EmulatorKey is null && g.IncludeGlobs is not { Length: > 0 } && (g.ExtraPaths ?? []).Length == 0;

    /// <summary>The name a save kept as its own game takes: the first of <see cref="Enroller.NamesFor"/> no
    /// server game has. Null when every one is taken.</summary>
    public static string? SeparateName(IReadOnlyList<GameDto> server, ScanCandidate c) =>
        Enroller.NamesFor(c).FirstOrDefault(n => !Enroller.NameTaken(server, n, c));

    /// <summary>
    /// The server name a candidate takes under the user's <paramref name="choice"/>, or the reason it cannot
    /// be added. A pick is checked again here, against the server as it is now: the list the user chose from
    /// may be minutes old. EmuDeck's untouched preinstalled file (<see cref="ScanCandidate.UntouchedSeed"/>) only
    /// ever joins a game that keeps it: as a game of its own it would publish a save nobody made.
    /// </summary>
    public static (string? Name, string? Refusal) Resolve(IReadOnlyList<GameDto> server, ScanCandidate c,
        IReadOnlyList<DeclaredSavePath> extras, LinkChoice? choice)
    {
        switch (choice?.Choice)
        {
            case Separate when c.UntouchedSeed:
                return (null, SeedRefusal);
            case Separate:
                return SeparateName(server, c) is { } own
                    ? (own, null)
                    : (null, "every name it could take as its own game is already another game’s.");
            case Rename:
                if (server.FirstOrDefault(g => g.Id == choice.GameId) is not { } target)
                    return (null, "the server game you picked is no longer on the server.");
                return RenamePairs(target, c, out var whyNot) is not null
                    ? (target.Name, null)
                    : (null, whyNot is null ? $"the server's '{target.Name}' cannot be linked under this device's own names."
                        : char.ToLowerInvariant(whyNot[0]) + whyNot[1..]);
            case Game:
                if (server.FirstOrDefault(g => g.Id == choice.GameId) is not { } picked)
                    return (null, "the server game you picked is no longer on the server.");
                if (SwitchTitles.TitleIdOf(c.EmulatorKey) is not null)
                    return SameKey(picked, c) || TakesSwitchTitle(picked) ? (picked.Name, null)
                        : picked.EmulatorKey is not null ? (null, $"the server's '{picked.Name}' is another Switch title.")
                        : (null, $"the server's '{picked.Name}' keeps only some files of its folder, so it cannot take a Switch title.");
                return Enroller.SameFiles(picked, c) ? (picked.Name, null)
                    : OtherEmulator(picked, c) ? (null, $"the server's '{picked.Name}' holds another emulator's saves; saves from different emulators are kept as separate games.")
                    : Enroller.OtherRegion(picked, c) ? (null, $"the server's '{picked.Name}' is the {picked.Release!.Region} release; each region is its own game.")
                    : (null, $"the server’s '{picked.Name}' keeps different save files, so it cannot be linked yet.");
            default:
                if (Enroller.ServerNameFor(server, c, extras) is not { } named)
                    return (null, "every name it could take on the server is already another game's.");
                return !c.UntouchedSeed || server.Any(g => Is(g, named) && Enroller.SameFiles(g, c))
                    ? (named, null)
                    : (null, SeedRefusal);
        }
    }

    private const string SeedDetail =
        "EmuDeck’s preinstalled file, never played here. It can only join a server game that keeps it.";
    private const string SeedRefusal =
        "it is EmuDeck's preinstalled file, never played here: it can only join a server game that keeps it.";

    private static bool Is(GameDto g, string name) => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>The same files, but another emulator's save made the game (<see cref="Enroller.SameEmulator"/>).</summary>
    private static bool OtherEmulator(GameDto g, ScanCandidate c) =>
        c.IncludeGlobs is { Count: > 0 } && Enroller.SameScope(g.IncludeGlobs, c.IncludeGlobs) && !Enroller.SameEmulator(g, c);

    /// <summary>"Chrono Trigger" is the same title as "Chrono Trigger", "Chrono Trigger (RetroArch)" or
    /// "Chrono Trigger (Japan) (melonDS)": the name, or the name followed by a bracket.</summary>
    private static bool SameTitle(GameDto g, ScanCandidate c, IReadOnlyList<string> names) =>
        names.Any(n => Is(g, n)) ||
        (g.Name.StartsWith(c.Name + " (", StringComparison.OrdinalIgnoreCase));

    private static string Files(GameDto g)
    {
        var all = (g.IncludeGlobs ?? []).Concat((g.ExtraPaths ?? []).SelectMany(p => p.IncludeGlobs ?? [])).ToList();
        return all.Count switch
        {
            0 => "its whole save folder",
            1 => all[0],
            _ => $"{all[0]} and {all.Count - 1} more",
        };
    }
}

public static class LinkKind
{
    public const string Join = "join";
    public const string New = "new";
    public const string Blocked = "blocked";
}

/// <summary>One choice on an Add games row. <paramref name="Choice"/> is what the enroll request sends back
/// (<see cref="EnrollLinks.Auto"/>, <see cref="EnrollLinks.Separate"/>, <see cref="EnrollLinks.Game"/> with
/// <paramref name="GameId"/>); <paramref name="Kind"/> says what it does (<see cref="LinkKind"/>).</summary>
public sealed record LinkOption(string Choice, string Kind, string Name, Guid? GameId, string Detail, string Badge);

/// <summary>The user's pick for one candidate, from the enroll request.</summary>
public sealed record LinkChoice(string Choice, Guid? GameId = null);
