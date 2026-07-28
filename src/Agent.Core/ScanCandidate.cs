namespace SaveLocker.Agent;

/// <summary>Where a <see cref="ScanCandidate"/> was discovered.</summary>
public enum ScanSource
{
    /// <summary>A non-Steam game added to Steam (read from shortcuts.vdf).</summary>
    SteamShortcut,
    /// <summary>An installed Steam game (read from appmanifest_*.acf).</summary>
    SteamInstalled,
    /// <summary>A folder under a common save root whose name matches the manifest.</summary>
    SaveRoot
}

/// <summary>
/// A discovered game the user might want to enroll. <see cref="SuggestedSaveDir"/>
/// is our best guess at the local save folder (may be null if we couldn't resolve
/// one yet — the user can fill it in).
/// </summary>
public sealed record ScanCandidate(
    string Name,
    string? SuggestedSaveDir,
    ScanSource Source,
    bool HasSteamCloud,
    string? ManifestKey = null,
    string? InstallDir = null,
    /// <summary>Unsigned Steam AppID for a non-Steam shortcut — the compatdata folder name.</summary>
    string? SteamAppId = null,
    /// <summary>
    /// The game's Proton compatdata prefix, when discovery resolved one (Linux only; null on
    /// Windows). Lets the path browser open inside the prefix instead of at $HOME when the
    /// save-folder guess is null — the normal case for a game absent from the manifest.
    /// </summary>
    string? PrefixPath = null,
    /// <summary>
    /// The process name (no <c>.exe</c>) that means this game is running, when discovery can know
    /// it unambiguously — which in practice means a non-Steam shortcut, where Steam records the
    /// exact executable the user chose. Null for an installed Steam game or a save-root match: the
    /// folder name is not an executable name and guessing would be worse than admitting ignorance.
    /// <para>
    /// On Windows this is what drives the whole process lifecycle — lease, exit-push, and the
    /// running-game pull refusal (WA-01). A game enrolled without it is not merely missing a
    /// nicety; <see cref="ProcessWatcher"/> excludes it entirely. WA-08.
    /// </para>
    /// </summary>
    string? SuggestedProcessName = null);
