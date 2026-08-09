using SaveLocker.Shared;

namespace SaveLocker.Agent.Linux;

/// <summary>
/// Discovers non-Steam Steam shortcuts and, where possible, their Proton save directories.
///
/// This is deliberately NOT the Windows scanner's shape. Steam-store games are out of scope —
/// they have Steam Cloud (Decisions.md §0) — so there is no <c>libraryfolders.vdf</c> / <c>*.acf</c>
/// scan here. Discovery is <c>shortcuts.vdf</c>, and the shortcut's AppID is what names the
/// Proton prefix we then resolve saves inside.
/// </summary>
public sealed class LinuxGameScanner : IGameScanner
{
    private readonly Detection _detection;

    public LinuxGameScanner(Detection detection) => _detection = detection;

    public async Task<IReadOnlyList<ScanCandidate>> ScanAsync(CancellationToken ct = default)
    {
        var results = new List<ScanCandidate>();

        foreach (var root in SteamRoots.Find())
        {
            foreach (var s in await SteamShortcuts.ReadAllAsync(root, ct))
            {
                ct.ThrowIfCancellationRequested();
                var prefix = s.AppId is null ? null : SteamRoots.CompatDataPath(root, s.AppId);
                var save = await SuggestSaveDirAsync(s, prefix, ct);

                results.Add(new ScanCandidate(
                    Name: s.AppName,
                    SuggestedSaveDir: save,
                    Source: ScanSource.SteamShortcut,
                    HasSteamCloud: false,          // non-Steam shortcuts have no Cloud, by definition
                    ManifestKey: save is null
                        ? null
                        : await _detection.CanonicalNameAsync(s.AppName, ct) ?? s.AppName,
                    InstallDir: s.StartDir,
                    SteamAppId: s.AppId,
                    PrefixPath: prefix,
                    // Carried for parity, and because config is shared between hosts — but Linux
                    // drives lifecycle from the launch wrapper, not from process polling, so
                    // nothing here depends on it (Decisions.md §3).
                    SuggestedProcessName: GameActivity.ProcessNameFromExe(s.Exe)));
            }
        }

        results.AddRange(await ScanHeroicAsync(ct));

        // Normalised, for the same reason as the Windows scanner: one game, one row, however the
        // shortcut happens to be spelled.
        //
        // This also collapses the Heroic double-listing: "Add to Steam" gives a Heroic game a real
        // shortcuts.vdf entry, so it is discovered twice. Preferring the row that HAS a save dir
        // picks the Heroic one — the shortcut's copy cannot resolve, because Steam never made a
        // compatdata prefix for a game it does not launch.
        return results
            .GroupBy(c => ManifestLoader.NormalizeName(c.Name), StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(c => c.SuggestedSaveDir is not null).First())
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Games staged in Heroic Games Launcher.
    ///
    /// <para>
    /// These are worth a second discovery pass rather than being left to the shortcut scan above,
    /// even though most of them also appear in <c>shortcuts.vdf</c> via Heroic's "Add to Steam".
    /// The shortcut carries an AppID, and an AppID names a <c>compatdata</c> prefix — but Heroic
    /// does not launch through Steam, so that prefix does not exist and the game arrives with no
    /// save folder every time. The prefix that does exist is Heroic's, and only Heroic's config
    /// knows where it is.
    /// </para>
    ///
    /// <para>
    /// No Steam Cloud here by definition: Heroic installs Epic, GOG, Amazon and sideloaded games.
    /// </para>
    /// </summary>
    private async Task<List<ScanCandidate>> ScanHeroicAsync(CancellationToken ct)
    {
        var results = new List<ScanCandidate>();

        foreach (var configRoot in HeroicRoots.Find())
        {
            foreach (var game in HeroicLibrary.Read(configRoot))
            {
                ct.ThrowIfCancellationRequested();

                var prefix = HeroicRoots.PrefixFor(configRoot, game.AppName);
                var save = await HeroicSaveDirAsync(game, prefix, ct);

                results.Add(new ScanCandidate(
                    Name: game.Title,
                    SuggestedSaveDir: save,
                    Source: ScanSource.Heroic,
                    HasSteamCloud: false,
                    ManifestKey: save is null
                        ? null
                        : await _detection.CanonicalNameAsync(game.Title, ct) ?? game.Title,
                    InstallDir: game.InstallPath,
                    // Deliberately null. Heroic's app_name is not a Steam AppID, and the Deck's
                    // launch wrapper matches on the AppID Steam passes it — writing Heroic's id
                    // here would make a tracked game the wrapper can never fire for look correct.
                    SteamAppId: null,
                    PrefixPath: prefix,
                    SuggestedProcessName: null));
            }
        }

        return results;
    }

    /// <summary>
    /// Best guess at a Heroic game's save directory: inside its Wine prefix per the manifest, else
    /// portable, beside the executable. The same two shapes as a Steam shortcut, resolved through
    /// Heroic's prefix instead of Steam's — and for a sideloaded GOG game the portable case is the
    /// likelier of the two.
    /// </summary>
    private async Task<string?> HeroicSaveDirAsync(
        HeroicGame game, string? prefix, CancellationToken ct)
    {
        // ONLY the configured prefix. Heroic launches the game in whatever `winePrefix` says, so
        // that is where the game writes — even when the game's own files are somewhere else.
        //
        // Falling back to the prefix the game is INSTALLED in is deliberately not done. The two
        // disagree when a prefix has been renamed on disk without updating Heroic's config: Heroic
        // then creates a fresh empty prefix at the configured path and runs the game there, so the
        // install-side prefix still holds a full set of real save files that nothing writes to any
        // more. Resolving to it would hand back a wrong path that looks more convincing than the
        // right one — see Doctor, which reports the disagreement instead of guessing past it.
        if (prefix is not null)
        {
            var dirs = await _detection.ResolveWineAsync(
                game.Title, prefix, installDir: game.InstallPath, ct);
            if (dirs.FirstOrDefault() is { } inPrefix) return inPrefix;
        }

        return PortableSaveDir(game.InstallPath);
    }

    /// <summary>
    /// Best guess at a shortcut's save directory. Two shapes exist and both are real:
    ///
    /// 1. <b>In-prefix</b> — the game writes to Windows paths inside its Wine prefix. The Ludusavi
    ///    manifest's tokens resolve there, via a resolver built for THIS game's prefix.
    /// 2. <b>Portable</b> — the game writes next to its own .exe. That is a plain Linux path on the
    ///    native filesystem and needs no prefix resolution at all. Common for the standalone builds
    ///    this feature exists for, so it is checked even when a prefix exists.
    ///
    /// Null when neither resolves, which on Linux is the expected case rather than a failure: most
    /// standalone builds are absent from the manifest, so <c>add-game --dir</c> is the primary path.
    /// </summary>
    private async Task<string?> SuggestSaveDirAsync(
        SteamShortcut shortcut, string? compatDataPath, CancellationToken ct)
    {
        if (compatDataPath is not null)
        {
            // StartDir is the game's install directory, which is exactly what <base> means — the
            // most common placeholder in the manifest by a wide margin. Passing it is most of the
            // reason a shortcut now resolves at all.
            var dirs = await _detection.ResolveProtonAsync(
                shortcut.AppName, compatDataPath, installDir: shortcut.StartDir, ct);
            if (dirs.FirstOrDefault() is { } inPrefix) return inPrefix;
        }

        return PortableSaveDir(shortcut);
    }

    /// <summary>
    /// A save folder sitting beside the game's executable. We only claim one when it is
    /// unambiguous — a single conventionally-named directory — because guessing wrong here points
    /// the archiver at the wrong tree, and the user can always say exactly what they mean with
    /// <c>--dir</c>.
    /// </summary>
    private static string? PortableSaveDir(SteamShortcut shortcut) =>
        PortableSaveDir(shortcut.StartDir
                        ?? (shortcut.Exe is { } exe ? Path.GetDirectoryName(exe) : null));

    /// <inheritdoc cref="PortableSaveDir(SteamShortcut)"/>
    private static string? PortableSaveDir(string? installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
            return null;

        string[] names = ["Saves", "Save", "SaveGames", "savegames", "saves", "save"];
        var hits = names
            .Select(n => Path.Combine(installDir, n))
            .Where(Directory.Exists)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return hits.Count == 1 ? Path.GetFullPath(hits[0]) : null;
    }
}
