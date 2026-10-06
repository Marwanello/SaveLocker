using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>Where a <see cref="ScanCandidate"/> was discovered.</summary>
public enum ScanSource
{
    /// <summary>A non-Steam game added to Steam (read from shortcuts.vdf).</summary>
    SteamShortcut,
    /// <summary>An installed Steam game (read from appmanifest_*.acf).</summary>
    SteamInstalled,
    /// <summary>A folder under a common save root whose name matches the manifest.</summary>
    SaveRoot,
    /// <summary>
    /// A game installed through Heroic Games Launcher (Linux). Discovered from Heroic's own library
    /// files, not from Steam — Heroic runs its games in prefixes it manages itself, so a Heroic game
    /// added to the Steam library has a shortcut but no compatdata prefix behind it.
    /// </summary>
    Heroic,
    /// <summary>
    /// This is how Playnite told us — either of two different ways. A single, targeted resolve built
    /// from the Playnite plugin's own live data (name, install dir, whichever store hints it has), via
    /// <c>POST /api/candidates/lookup</c> (tasks/playnite-plugin/plan.md, Phase 5); or a broad sweep of
    /// Playnite's own library database read directly off disk, with no plugin involved at all
    /// (<c>GameScanner.ScanPlayniteLibraryAsync</c>, Phase 18). <see cref="GameStore"/> is the finer
    /// axis that tells the two-store cases apart, the same relationship <see cref="Heroic"/> already
    /// has to four different runners.
    /// </summary>
    Playnite,
    /// <summary>
    /// One game's save file inside an emulator's saves folder — found by the save itself, not by a
    /// launcher's library, so it works however the ROM was launched (EmuDeck's ES-DE, Steam ROM
    /// Manager shortcuts, or the emulator's own menu). <see cref="ScanCandidate.EmulatorName"/> says
    /// which emulator; <see cref="ScanCandidate.IncludeGlobs"/> scopes the shared folder to this game.
    /// </summary>
    Emulator
}

/// <summary>
/// Which storefront a candidate came from, when discovery can tell. This is a second axis to
/// <see cref="ScanSource"/>, not a finer one: a source says HOW the game was found, a store says
/// WHO sold it. Heroic is the only source that manages more than one, and its own library files
/// name the runner outright, so the distinction costs nothing to carry and is the only way a user
/// with a large Heroic install base can narrow the list to one store.
/// </summary>
public enum GameStore
{
    /// <summary>Discovery has no store to report — a shortcut, or a save-root match.</summary>
    Unknown,
    Steam,
    Epic,
    Gog,
    Amazon,
    /// <summary>Installed by hand into a launcher that manages it (Heroic's <c>sideload</c>).</summary>
    Sideload
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
    /// <summary>
    /// Unsigned Steam AppID — the compatdata folder name for a non-Steam shortcut, or (Windows,
    /// since tasks/playnite-plugin/plan.md Phase 2) the manifest's own <c>appid</c> for an installed
    /// Steam game, which used to be read only to filter <see cref="GameScanner"/>'s
    /// <c>NonGameAppIds</c>/compat-tool check and then discarded.
    /// </summary>
    string? SteamAppId = null,
    /// <summary>
    /// The game's Wine prefix, when discovery resolved one (Linux only; null on Windows) — Steam's
    /// <c>compatdata/&lt;appid&gt;</c> for a shortcut, or Heroic's own <c>winePrefix</c>. Lets the
    /// path browser open inside the prefix instead of at $HOME when the save-folder guess is null —
    /// the normal case for a game absent from the manifest.
    /// <para>
    /// It is the prefix ROOT, not its <c>drive_c</c>: the two launchers nest it differently, so
    /// anything descending into it must go through <see cref="WinePrefix.Locate"/> rather than
    /// assume Steam's layout.
    /// </para>
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
    string? SuggestedProcessName = null,
    /// <summary>Which storefront sold the game, when discovery knows. See <see cref="GameStore"/>.</summary>
    GameStore Store = GameStore.Unknown,
    /// <summary>
    /// The real Steam AppID a MoonDeck streaming shortcut points at (<c>MOONDECK_STEAM_APP_ID</c>
    /// in its launch options), when this candidate IS such a shortcut. Carried so <c>doctor</c>
    /// can say the pointer streams a genuine install rather than showing a bare shortcut AppID —
    /// the Cyberpunk shape is otherwise a row that looks like any other unresolved shortcut.
    /// Null for every other source.
    /// </summary>
    string? MoonDeckAppId = null,
    /// <summary>The primary folder's include scope, when the scanner knows the game owns only some of
    /// its files — one ROM's save in a folder every ROM shares. Null: the whole folder.</summary>
    IReadOnlyList<string>? IncludeGlobs = null,
    /// <summary>
    /// More save folders the scanner KNOWS belong to this game (tasks/multiple-save-paths plan §8): an
    /// emulator's save states beside its saves. Adopted at enrollment without asking — unlike a
    /// manifest's extra locations, which may just as well be alternatives as companions.
    /// </summary>
    IReadOnlyList<DeclaredSavePath>? ExtraSaveDirs = null,
    /// <summary>
    /// The manifest's other locations for this game that exist here (plan §8): offered as "Also found",
    /// never adopted until the user says so — the manifest cannot tell a second save folder from an
    /// alternative install's, or from a settings folder beside the saves. Null when there are none.
    /// </summary>
    IReadOnlyList<DeclaredSavePath>? AlternateSaveDirs = null,
    /// <summary>"RetroArch", "PCSX2", … — free text, not an enum: which emulators exist changes
    /// faster than this codebase does. Null for every non-emulator source.</summary>
    string? EmulatorName = null,
    /// <summary>The emulated console in EmuDeck/ES-DE's own folder vocabulary ("snes", "psx", …) —
    /// the <c>Emulation/roms/&lt;system&gt;</c> folder the ROM was found in. Null when no ROM was found.</summary>
    string? EmulatorSystem = null,
    /// <summary>The libretro core whose per-core saves folder the file sits in (RetroArch's "Sort
    /// saves by core"), else null. Diagnostic only — not part of the game's identity.</summary>
    string? EmulatorCore = null);

/// <summary>
/// One extra save folder a scanner declares for a candidate. <see cref="Key"/> names the folder on
/// every machine; <see cref="Template"/> is how other machines find theirs, when the scanner can say
/// (otherwise enrollment tries to describe <see cref="Dir"/> itself).
/// </summary>
public sealed record DeclaredSavePath(
    string Key, string Dir, IReadOnlyList<string>? IncludeGlobs = null, string? Template = null);
