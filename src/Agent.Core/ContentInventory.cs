using System.Text.RegularExpressions;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>
/// What DLC and title updates a tracked game has on this machine (tasks/emulator-saves Phase 5c, the DLC check). Each push
/// sends it so the server knows what the save was made with, and every report tells the console what this machine has.
/// Xenia's packages, and a Switch game's version (<see cref="SwitchGameVersion"/>); other emulators answer null, which turns
/// the check off for their games.
/// </summary>
public static partial class ContentInventory
{
    [GeneratedRegex("^([0-9A-F]{8})/00000001/\\*\\*$", RegexOptions.IgnoreCase)]
    private static partial Regex XeniaSaveScope();

    /// <summary>The packages installed here for the game, or null when its emulator has none this knows about.</summary>
    public static ContentPackageDto[]? For(TrackedGame game) =>
        Home(game) is { } home
            ? [.. XeniaContent.Inventory(home.Content, home.TitleId).Select(p => new ContentPackageDto(p.Kind, p.Id, p.Name, p.Size))]
            : SwitchGameVersion.Inventory(game);

    /// <summary>Can this game's missing packs be handed over from another machine? Xenia's only: a Switch update is
    /// installed into the emulator from the user's own copy.</summary>
    public static bool CanHandOver(TrackedGame game) => Home(game) is not null;

    /// <summary>
    /// Where a Xenia game's packages live: Xenia's <c>content</c> folder, the title ID, and whether the game is in the layout
    /// from before profiles (save folder = content itself), whose builds read packages from <c>content/&lt;TID&gt;</c>.
    /// </summary>
    public static (string Content, string TitleId, bool Legacy)? Home(TrackedGame game)
    {
        if (string.IsNullOrEmpty(game.SaveDirectory)) return null;
        var titleId = game.IncludeGlobs.Select(g => XeniaSaveScope().Match(g.Replace('\\', '/'))).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        if (titleId is null) return null;
        // The save folder is a profile's home (content/<XUID> or content/<name>), or content itself in the layout from
        // before profiles; DLC and updates sit under content either way.
        var dir = EmuDeckRoots.RealPath(Path.TrimEndingDirectorySeparator(game.SaveDirectory));
        var name = Path.GetFileName(dir);
        var parent = Path.GetDirectoryName(dir);
        var profiled = XeniaSaves.Xuid(name) is not null || (XeniaSaves.IsNamedProfile(name) && LooksLikeContent(parent));
        var content = profiled ? parent : dir;
        if (content is null || !Directory.Exists(content)) return null;
        return (content, titleId.ToUpperInvariant(), !profiled);
    }

    // A named profile's home sits in content, beside the device's folder or an XUID's; content itself sits in Xenia's folder.
    private static bool LooksLikeContent(string? dir) =>
        dir is not null && RomSaves.SafeSubdirs(dir).Select(Path.GetFileName)
            .Any(n => n == XeniaSaves.NoProfile || (n is not null && XeniaSaves.Xuid(n) is not null));
}
