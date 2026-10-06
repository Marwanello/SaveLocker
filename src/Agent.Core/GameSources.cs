using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// How this machine found a game, as the console and both agent UIs show it: a kind and which one
/// ("Emulator › RetroArch"), plus tags. Written once, here, so every screen says the same thing.
/// </summary>
public static class GameSources
{
    public const string FolderPickedInAgent = "Folder picked in the agent";
    public const string AddedFromCli = "savelocker add-game";

    public static GameSourceDto From(ScanCandidate c)
    {
        var tags = new List<string>();
        switch (c.Source)
        {
            case ScanSource.Emulator:
                if (SystemLabel(c.EmulatorSystem) is { } system) tags.Add(system);
                var dir = (c.SuggestedSaveDir ?? "").Replace('\\', '/');
                if (dir.Contains("/.var/app/", StringComparison.Ordinal)) tags.Add("Flatpak");
                if (dir.Contains("/saves/retroarch/", StringComparison.OrdinalIgnoreCase)) tags.Add("EmuDeck");
                return Make(GameSourceKinds.Emulator, c.EmulatorName ?? "Emulator", tags);
            case ScanSource.SteamInstalled:
                if (c.SteamAppId is { } appId) tags.Add("AppID " + appId);
                if (c.PrefixPath is not null) tags.Add("Proton");
                return Make(GameSourceKinds.Steam, "Installed game", tags);
            case ScanSource.SteamShortcut:
                if (c.MoonDeckAppId is not null) tags.Add("MoonDeck");
                if (c.PrefixPath is not null) tags.Add("Proton");
                return Make(GameSourceKinds.Steam, "Non-Steam shortcut", tags);
            case ScanSource.Heroic:
                if (c.PrefixPath is not null) tags.Add("Wine prefix");
                return Make(GameSourceKinds.Heroic, StoreLabel(c.Store) ?? "Heroic library", tags);
            case ScanSource.Playnite:
                return Make(GameSourceKinds.Playnite, StoreLabel(c.Store) ?? "Playnite library", tags);
            default:
                var parent = c.SuggestedSaveDir is { } d ? Path.GetFileName(Path.GetDirectoryName(d.TrimEnd('/', '\\'))) : null;
                return Make(GameSourceKinds.SaveFolder, string.IsNullOrEmpty(parent) ? "Save folder" : parent, tags);
        }
    }

    public static GameSourceDto Manual(string detail) => new(GameSourceKinds.Manual, detail);

    private static GameSourceDto Make(string kind, string detail, List<string> tags) =>
        new(kind, detail, tags.Count == 0 ? null : tags.ToArray());

    /// <summary>The first level as people read it. An unknown kind shows as itself.</summary>
    public static string KindLabel(string kind) => kind switch
    {
        GameSourceKinds.Emulator => "Emulator",
        GameSourceKinds.Steam => "Steam",
        GameSourceKinds.Heroic => "Heroic",
        GameSourceKinds.Playnite => "Playnite",
        GameSourceKinds.SaveFolder => "Save-folder scan",
        GameSourceKinds.Manual => "Added by hand",
        _ => kind,
    };

    /// <summary>"Emulator › RetroArch".</summary>
    public static string Describe(GameSourceDto s) => $"{KindLabel(s.Kind)} › {s.Detail}";

    public static string? StoreLabel(GameStore store) => store switch
    {
        GameStore.Steam => "Steam",
        GameStore.Epic => "Epic Games",
        GameStore.Gog => "GOG",
        GameStore.Amazon => "Amazon Games",
        GameStore.Sideload => "Sideloaded",
        _ => null,
    };

    /// <summary>EmuDeck/ES-DE's ROM folder name as the console's own name ("snes" → "SNES").</summary>
    public static string? SystemLabel(string? system) =>
        string.IsNullOrWhiteSpace(system) ? null
        : Systems.TryGetValue(system.Trim(), out var label) ? label
        : system.Trim().ToUpperInvariant();

    private static readonly Dictionary<string, string> Systems = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nes"] = "NES", ["famicom"] = "Famicom", ["fds"] = "Famicom Disk System",
        ["snes"] = "SNES", ["sfc"] = "Super Famicom", ["n64"] = "N64", ["gc"] = "GameCube",
        ["wii"] = "Wii", ["wiiu"] = "Wii U", ["switch"] = "Switch", ["virtualboy"] = "Virtual Boy",
        ["gb"] = "Game Boy", ["gbc"] = "Game Boy Color", ["gba"] = "GBA", ["nds"] = "DS",
        ["n3ds"] = "3DS", ["3ds"] = "3DS",
        ["psx"] = "PS1", ["ps2"] = "PS2", ["ps3"] = "PS3", ["psp"] = "PSP", ["psvita"] = "PS Vita",
        ["genesis"] = "Genesis", ["megadrive"] = "Mega Drive", ["mastersystem"] = "Master System",
        ["gamegear"] = "Game Gear", ["segacd"] = "Sega CD", ["sega32x"] = "32X", ["saturn"] = "Saturn",
        ["dreamcast"] = "Dreamcast", ["pcengine"] = "PC Engine", ["tg16"] = "TurboGrafx-16",
        ["pcenginecd"] = "PC Engine CD", ["neogeo"] = "Neo Geo", ["ngp"] = "Neo Geo Pocket",
        ["ngpc"] = "Neo Geo Pocket Color", ["wonderswan"] = "WonderSwan", ["wonderswancolor"] = "WonderSwan Color",
        ["atari2600"] = "Atari 2600", ["atari7800"] = "Atari 7800", ["lynx"] = "Lynx",
        ["arcade"] = "Arcade", ["mame"] = "Arcade", ["fbneo"] = "Arcade",
    };

    /// <summary>
    /// Emulated games are named by their title alone, so one can share a name with a PC release this
    /// machine also has ("Chrono Trigger"). Scans merge candidates by name, and the server matches games
    /// by name, so a clashing emulated game gets its console appended ("Chrono Trigger (SNES)").
    /// </summary>
    public static IReadOnlyList<ScanCandidate> AvoidNameClashes(IEnumerable<ScanCandidate> emulated,
        IEnumerable<string> otherNames)
    {
        var taken = new HashSet<string>(otherNames.Select(ManifestLoader.NormalizeName), StringComparer.Ordinal);
        return emulated
            .Select(c => taken.Contains(ManifestLoader.NormalizeName(c.Name))
                ? c with { Name = $"{c.Name} ({SystemLabel(c.EmulatorSystem) ?? c.EmulatorName ?? "Emulator"})" }
                : c)
            .ToList();
    }

    /// <summary>
    /// Fill in the source of games enrolled before sources were recorded, from a scan that found them
    /// at the same folder. Returns the games it changed (already saved), for the caller to report.
    /// </summary>
    public static IReadOnlyList<TrackedGame> Backfill(AgentConfig config, IEnumerable<ScanCandidate> candidates)
    {
        var changed = new List<TrackedGame>();
        foreach (var c in candidates)
        {
            if (config.FindGame(c.Name) is not { IsEnrolledHere: true, Source: null } game) continue;
            if (c.SuggestedSaveDir is null ||
                !string.Equals(SavePathGuard.Canonicalize(c.SuggestedSaveDir), SavePathGuard.Canonicalize(game.SaveDirectory),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                continue;
            var source = From(c);
            game.Source = source;
            config.SaveGameSource(game.GameId, source);
            changed.Add(game);
        }
        return changed;
    }

    /// <summary>Tell the server how this machine found <paramref name="game"/>. Best effort: the poller
    /// re-sends a source the server does not have.</summary>
    public static async Task ReportAsync(ApiClient api, TrackedGame game)
    {
        if (game.Source is not { } source) return;
        try { await api.SetGameSourceAsync(game.GameId, source); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AgentLogger.Log($"Could not report how '{game.Name}' was found yet ({ex.Message}); the next poll does.");
        }
    }
}
