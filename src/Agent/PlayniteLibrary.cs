using LiteDB;

namespace SaveLocker.Agent;

/// <summary>A game as Playnite's own library database describes it.</summary>
public sealed record PlayniteGame(
    string Name, string InstallDirectory, string? GameId, Guid PluginId);

/// <summary>
/// Reads Playnite's library directly off disk — the structural equivalent of
/// <c>Agent.Linux/HeroicLibrary.cs</c> for a launcher that has no companion "read its files" project
/// on Linux to copy from. <c>GameScanner.ScanAsync</c> calls this as a fourth broad-sweep source
/// (tasks/playnite-plugin/plan.md, Phase 18) so a Playnite-only game shows up in Add Games with zero
/// dependency on the plugin (Phases 8-17) ever being installed — every other Playnite-aware surface
/// in this codebase depends on the plugin running live inside Playnite and calling
/// <c>POST /api/candidates/lookup</c> for one game at a time; this is the only source that can name
/// the whole library with Playnite not even running.
///
/// <para>
/// <b>The version risk this phase's own plan flagged was real, and confirmed on real hardware, not a
/// synthetic fixture.</b> A first pass targeted LiteDB 5.x and a "games" collection — both wrong, and
/// both found by testing against an actual installed Playnite's <c>games.db</c> (2026-09-16): the file
/// opens with LiteDB 5.0.21 refusing it outright (<c>LiteException: File is not a valid LiteDB
/// database format</c>) — its own header bytes literally read <c>** This is a LiteDB file **</c>,
/// LiteDB 4's on-disk marker, not 5's — and its one games collection is named <c>Game</c> (singular,
/// capital), not <c>games</c>. Both are fixed here against that real file. <b>What remains genuinely
/// unverified</b> is reading it while Playnite itself is running and holding it open — the copy tested
/// against was read with Playnite closed. <c>Mode=Shared;ReadOnly=true</c> is LiteDB 4's documented
/// concurrent-read connection mode and two simultaneous shared readers were confirmed to coexist
/// against the same real file, which is the strongest signal available without a live Playnite
/// process to test against. <see cref="SafeRead"/> exists so this — or any other read failure — still
/// degrades to "no Playnite candidates this scan" (the same <c>GameScanner</c> WA-11 contract every
/// other source gets), never a crash.
/// </para>
/// </summary>
public static class PlayniteLibrary
{
    // The OFFICIAL JosefNemec/PlayniteExtensions library plugins' own ids — stable across installs
    // since each is baked into that plugin's own source, not user- or machine-specific. Steam is
    // confirmed against a real games.db (2026-09-16: this machine's real Cyberpunk 2077 entry carries
    // exactly this PluginId); Gog/Epic/Amazon are confirmed by reading each plugin's own source
    // (GogLibrary.cs, EpicLibrary.cs, AmazonGamesLibrary.cs), not from a local install, since this
    // machine has none of those three's OFFICIAL plugin installed.
    //
    // What this map deliberately cannot do: Steam has essentially one plugin that represents it, but
    // Epic/GOG/Amazon do not — several popular COMMUNITY plugins (a legendary-CLI-backed Epic
    // integration among them) represent the same store under a different PluginId this map has no way
    // to enumerate. Confirmed for real on this same machine: three genuinely Epic-owned installed
    // games (Minit, HITMAN 3, Moving Out — their GameId values are legendary app names, not Epic
    // catalog ids) carry PluginId ead65c3b-2f8f-4e37-b4e6-b3de6be540c6, which is NOT the official
    // EpicLibrary id below, and so correctly — not buggily — fall to GameStore.Unknown via
    // <see cref="StoreFor"/>. This is the same gap HeroicLibrary's own fallback already accepts for a
    // runner it doesn't recognize; Store is a UI sub-filter, not something save-path resolution
    // depends on, so under-recognizing a store here costs a filter chip, not correctness.
    private static readonly Guid SteamPluginId = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");
    private static readonly Guid GogPluginId = Guid.Parse("aebe8b7c-6dc3-4a66-af31-e7375c6b5e9e");
    private static readonly Guid EpicPluginId = Guid.Parse("00000002-dbd1-46c6-b5d0-b1ba559d10e4");
    private static readonly Guid AmazonPluginId = Guid.Parse("402674cd-4af6-4886-b6ec-0e695bfa0688");

    /// <summary>
    /// Playnite's per-user data root — <c>%AppData%\Playnite</c> for a normal install. A portable
    /// install keeps its data beside its own executable instead, at a location nothing on the machine
    /// records — there is no registry key or known-folder the way <see cref="GameScanner.FindSteamPath"/>
    /// has for Steam, so it genuinely cannot be auto-discovered the way the rest of this scanner's
    /// sources are. <c>SAVELOCKER_PLAYNITE_PATH</c> is the escape hatch: set it to the Playnite install
    /// root (the folder holding <c>Playnite.DesktopApp.exe</c>) and this reads from there instead.
    /// <c>tests/testenv.ps1 -PlaynitePath</c> sets this automatically for its portable test instance —
    /// confirmed 2026-09-17 that without this override, a portable Playnite is silently invisible to
    /// this whole source, since its library never lives under <c>%AppData%</c> at all.
    /// </summary>
    public static string DataRoot =>
        Environment.GetEnvironmentVariable("SAVELOCKER_PLAYNITE_PATH") is { Length: > 0 } portableRoot
            ? portableRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite");

    private static string DefaultDatabasePath => Path.Combine(DataRoot, "library", "games.db");

    /// <summary>Is Playnite's per-user data folder here at all?</summary>
    public static bool Present => Directory.Exists(DataRoot);

    /// <summary>
    /// The installed, non-uninstalled games in Playnite's library, or empty when the database is
    /// absent or could not be read. Never throws — see this type's own remarks on the one real risk
    /// here.
    /// </summary>
    public static IReadOnlyList<PlayniteGame> SafeRead()
    {
        try
        {
            if (!File.Exists(DefaultDatabasePath)) return Array.Empty<PlayniteGame>();
            return Read(DefaultDatabasePath);
        }
        catch (Exception ex)
        {
            AgentLogger.Log(
                $"Scan: Playnite library at '{DefaultDatabasePath}' could not be read " +
                $"({ex.GetType().Name}: {ex.Message}). Skipping Playnite discovery this scan.");
            return Array.Empty<PlayniteGame>();
        }
    }

    /// <summary>
    /// Reads a specific <c>games.db</c> file. Split out from <see cref="SafeRead"/> so a test can
    /// point it at a small fixture database without touching <see cref="DataRoot"/>.
    /// </summary>
    public static IReadOnlyList<PlayniteGame> Read(string databasePath)
    {
        var games = new List<PlayniteGame>();

        // Mode=Shared: lets a second process (this agent) open the file while Playnite's own process
        // still holds it — LiteDB 4's connection string key, not 5's "Connection=shared" (see this
        // type's own remarks). ReadOnly: this agent must never be the thing that corrupts a user's
        // game library. The collection is "Game" (singular, capital) — confirmed against a real
        // games.db, not assumed from LiteDB's own generic examples.
        using var db = new LiteDatabase($"Filename={databasePath};Mode=Shared;ReadOnly=true");
        var collection = db.GetCollection("Game");

        foreach (var doc in collection.FindAll())
        {
            if (!doc.TryGetValue("IsInstalled", out var installedValue) || !installedValue.AsBoolean)
                continue;

            var installDir = doc.TryGetValue("InstallDirectory", out var dirValue) ? dirValue.AsString : null;
            if (string.IsNullOrWhiteSpace(installDir)) continue;

            var name = doc.TryGetValue("Name", out var nameValue) ? nameValue.AsString : null;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var gameId = doc.TryGetValue("GameId", out var gameIdValue) ? gameIdValue.AsString : null;
            var pluginId = doc.TryGetValue("PluginId", out var pluginIdValue) &&
                Guid.TryParse(pluginIdValue.AsString, out var parsed)
                ? parsed
                : Guid.Empty;

            games.Add(new PlayniteGame(
                name.Trim(), installDir.Trim(),
                string.IsNullOrWhiteSpace(gameId) ? null : gameId.Trim(),
                pluginId));
        }

        return games;
    }

    /// <summary>Which store owns a game, from its Playnite <c>PluginId</c>. Anything this map does
    /// not know (itch.io, a manually-added executable, a third-party library plugin) falls to
    /// <see cref="GameStore.Unknown"/> — mirroring <c>HeroicLibrary</c>'s own fallback exactly.</summary>
    public static GameStore StoreFor(Guid pluginId) =>
        pluginId == SteamPluginId ? GameStore.Steam :
        pluginId == GogPluginId ? GameStore.Gog :
        pluginId == EpicPluginId ? GameStore.Epic :
        pluginId == AmazonPluginId ? GameStore.Amazon :
        GameStore.Unknown;

    public static bool IsSteam(Guid pluginId) => pluginId == SteamPluginId;
}
