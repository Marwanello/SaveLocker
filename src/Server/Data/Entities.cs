using SaveLocker.Shared;

namespace SaveLocker.Server.Data;

/// <summary>A registered machine (the PC or the laptop).</summary>
public class Machine
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string ApiKeyHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>A tracked game whose saves are synced.</summary>
public class Game
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>
    /// Which region's release of a console game this is ("USA", "Europe, Australia"), <c>""</c> for none. With
    /// <see cref="Name"/>, what tells games apart: the USA and the European Warrior Within are both "Prince of
    /// Persia - Warrior Within", and a disc reads only its own region's saves.
    /// </summary>
    public string Region { get; set; } = "";
    /// <summary>The whole release (<see cref="GameReleaseDto"/>: console, game IDs, languages) as JSON, or null.</summary>
    public string? ReleaseJson { get; set; }
    /// <summary>
    /// The emulator profile the game's saves are under, <c>xenia:&lt;XUID&gt;</c> (tasks/emulator-saves Phase 5c), or null.
    /// Set by the first machine that says, so another machine that finds the game under a different profile of its own
    /// (Xenia makes every new profile's XUID at random) can put it under this one instead.
    /// </summary>
    public string? EmulatorProfile { get; set; }
    /// <summary>
    /// The emulated title the game is, across emulators whose save folders differ (D2, tasks/emulator-saves Phase 6):
    /// <c>switch:&lt;title ID&gt;</c>. Ryujinx files a save under an ID it hands out per install, so neither the name nor
    /// the folder is the same on two machines; this is. Set by the first machine that says, matched before the name.
    /// </summary>
    public string? EmulatorKey { get; set; }
    /// <summary>Key into the Ludusavi manifest, if matched.</summary>
    public string? ManifestKey { get; set; }
    /// <summary>JSON array of user-provided override paths.</summary>
    public string? CustomPathsJson { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Save folder suggested when the game is defined (e.g. in the dashboard).
    /// Propagated to agents, which use it if it exists on that machine, otherwise
    /// the user maps a local folder. The resolved per-machine folder is stored
    /// server-side as a <see cref="MachineSavePath"/>.
    /// </summary>
    public string? SuggestedSaveDir { get; set; }

    // Cached SteamGridDB artwork (served relative URLs under wwwroot/art/, or null).
    public string? GridUrl { get; set; }
    public string? HeroUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? IconUrl { get; set; }

    /// <summary>
    /// The SteamGridDB game hand-picked in the console's picker, or null for "match by name". When set,
    /// every art operation for this game (the picker, Refresh art, the missing-art backfill) reads and
    /// writes <see cref="SteamGridDbId"/>'s assets instead of the first search hit — a game whose name
    /// matches the wrong entry used to have no way out short of renaming it.
    /// </summary>
    public int? SteamGridDbId { get; set; }

    /// <summary>SteamGridDB's own name for <see cref="SteamGridDbId"/>, stored when the id is chosen so
    /// the picker can name the source without asking SteamGridDB on every open. Null when automatic.</summary>
    public string? SteamGridDbName { get; set; }

    /// <summary>How many versions to keep for this game. Null = use the server global default.</summary>
    public int? RetainVersions { get; set; }

    /// <summary>Newline-separated per-game exclude globs (e.g. <c>*.log</c>). Applied on top
    /// of the server global defaults when agents hash + archive this game's saves.</summary>
    public string? ExcludeGlobs { get; set; }

    /// <summary>Newline-separated include globs for the PRIMARY save folder, or null for "the whole
    /// folder". Narrows it to this game's own files when other games share it (an emulator's saves
    /// folder). Set when the game is created; extra folders carry their own on <see cref="GameSavePath"/>.</summary>
    public string? IncludeGlobs { get; set; }

    /// <summary>What the server does when an upload diverges from the current head.</summary>
    public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.Manual;

    /// <summary>The machine whose saves automatically win when <see cref="ConflictPolicy"/> is
    /// <see cref="ConflictPolicy.PreferMachine"/>. Null otherwise.</summary>
    public Guid? PreferredMachineId { get; set; }

    /// <summary>
    /// A mirror of the DEFAULT playthrough's head (<see cref="Playthrough.HeadVersionId"/>), written in the same step as
    /// it (<c>SyncService.MoveHeadAsync</c>) so every reader that has only ever asked "the game's latest" keeps working.
    /// Null until first upload. Code that syncs reads the head of the playthrough it is syncing instead.
    /// </summary>
    public Guid? HeadVersionId { get; set; }

    /// <summary>Newline-separated ids of the games merged into this one (tasks/emulator-saves Phase 17), or null: an agent
    /// that synced one of them moves to this game.</summary>
    public string? MergedGameIds { get; set; }

    /// <summary>The title this game is one emulator's tree of (tasks/multi-emulator-games), or null for a game shown on its own.
    /// Console-side only: nothing syncs differently because of it.</summary>
    public Guid? TitleId { get; set; }
    public GameTitle? Title { get; set; }

    /// <summary>Split out of its title by hand: automatic grouping never takes it back.</summary>
    public bool TitleLocked { get; set; }

    /// <summary>What tells this tree from another of the same emulator in its title ("Rev 0"), or null.</summary>
    public string? TitleEdition { get; set; }

    public List<SaveVersion> Versions { get; set; } = new();
}

/// <summary>
/// One line of a game's saves (tasks/save-playthroughs): its own head, versions, conflicts and lease. Every game has at
/// least one, the <see cref="IsDefault"/> one, "Main". A machine follows exactly one playthrough per game
/// (<see cref="DeviceSelection"/>) and pushes to and pulls from only that.
/// </summary>
public class Playthrough
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>1-60 characters, unique within the game ignoring case.</summary>
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    /// <summary>The version this playthrough was forked from, or null when it started empty. Kept after the playthrough
    /// moves on, as its provenance.</summary>
    public Guid? ForkedFromVersionId { get; set; }

    /// <summary>
    /// The latest save of this playthrough, or null until the first push. A fork's head starts as the version it was forked
    /// from, which belongs to another playthrough (<see cref="SaveVersion.PlaythroughId"/>): no archive is copied.
    /// </summary>
    public Guid? HeadVersionId { get; set; }

    /// <summary>When it was archived. An archived playthrough is frozen — no push, pull, select or lease — and is not pruned.</summary>
    public DateTime? ArchivedAt { get; set; }

    /// <summary>The one playthrough a new device, and an agent that predates playthroughs, follows. Exactly one per game.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Which playthrough one machine follows for one game. Per device, not per game: switching the Deck leaves the PC alone.
/// A row is written the first time a machine syncs a game (the default then), so a later change of the default never moves a
/// device under its own files.
/// </summary>
public class DeviceSelection
{
    public Guid MachineId { get; set; }
    public Guid GameId { get; set; }
    public Guid PlaythroughId { get; set; }
    public DateTime SelectedAt { get; set; }
}

/// <summary>
/// One console entry over the games an emulated title is saved as, one per emulator (tasks/multi-emulator-games). Its
/// name is <see cref="BaseName"/> — the first game's without its "(RetroArch)" suffix — and its art the first game's.
/// </summary>
public class GameTitle
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>The console its games are for ("DS"), or "" when none said.</summary>
    public string PlatformKey { get; set; } = "";
    public string BaseName { get; set; } = "";
    /// <summary>The region its games are, "" for none recorded.</summary>
    public string RegionKey { get; set; } = "";
    /// <summary>The game whose art the title shows: the oldest. When it leaves, the next oldest takes over.</summary>
    public Guid? LeadGameId { get; set; }
}

/// <summary>An uploaded snapshot of a game's saves. Forms a parent chain (lineage).</summary>
public class SaveVersion
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>The playthrough this snapshot was uploaded to (tasks/save-playthroughs). Retention counts versions per
    /// playthrough. A playthrough forked from this version has it as its head without it moving here.</summary>
    public Guid PlaythroughId { get; set; }

    /// <summary>The machine that uploaded this snapshot, or null once that machine has been
    /// deleted. Nullable on purpose: a version outlives its uploader (see
    /// <see cref="MachineName"/>), and a cascade here would destroy save history the console
    /// promises to keep.</summary>
    public Guid? MachineId { get; set; }
    public Machine? Machine { get; set; }

    /// <summary>The uploader's name captured at upload time. The only thing left to name the
    /// uploader once <see cref="MachineId"/> has been nulled by a machine deletion.</summary>
    public string MachineName { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    /// <summary>Stable hash of the save directory contents.</summary>
    public string ContentHash { get; set; } = "";
    public long Size { get; set; }

    /// <summary>The version this snapshot was based on (the head the uploader last knew).</summary>
    public Guid? ParentVersionId { get; set; }

    /// <summary>Exempt from automatic retention until an admin unprotects it.</summary>
    public bool Protected { get; set; }

    /// <summary>The name of the game this version was made under before that game was merged into this one (tasks/emulator-saves
    /// Phase 17): read-only history — its files may be another dump's names — never made Latest. Null for the game's own.</summary>
    public string? MergedFrom { get; set; }

    /// <summary>Made by copying another emulator's save of the same title (tasks/multi-emulator-games Phase 6): that
    /// emulator's name, and the version copied. Null for a version a machine uploaded.</summary>
    public string? CopiedFrom { get; set; }
    public Guid? CopiedFromVersionId { get; set; }

    /// <summary>Relative path of the stored archive within the archive store.</summary>
    public string ArchivePath { get; set; } = "";

    /// <summary>The DLC and title updates the uploading machine had for the game when it made this version
    /// (<see cref="ContentPackageDto"/>[] as JSON), or null when it did not say — tasks/emulator-saves Phase 5c, the DLC
    /// check. Names, IDs and sizes only; never package data.</summary>
    public string? ContentJson { get; set; }
}

/// <summary>
/// One file within a <see cref="SaveVersion"/>'s archive, as it existed at that version — the
/// per-file counterpart to <see cref="SaveVersion.ContentHash"/>. Populated whenever the uploading
/// agent supplied its full manifest (delta-capable pushes; see <c>BeginUploadRequest.Files</c>), so
/// the NEXT push against this version as a base can diff per file instead of re-sending everything.
/// A version with no rows here simply has no stored baseline yet — the next push to it falls back
/// to a full archive, which self-heals the gap once it completes with a manifest.
/// </summary>
public class SaveVersionFile
{
    public Guid VersionId { get; set; }
    public SaveVersion? Version { get; set; }

    /// <summary>Forward-slash relative path, same convention as <see cref="SaveVersion.ArchivePath"/>.</summary>
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>An exclusive checkout of a game's saves by one machine.</summary>
public class Lease
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>The playthrough being played: two devices can hold a lease on two playthroughs of one game at once.</summary>
    public Guid PlaythroughId { get; set; }

    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }

    public DateTime AcquiredAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>A recorded divergence between two save versions awaiting admin resolution.</summary>
public class ConflictFlag
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>The playthrough whose line diverged. Two devices on different playthroughs never conflict.</summary>
    public Guid PlaythroughId { get; set; }

    public Guid VersionAId { get; set; }
    public Guid VersionBId { get; set; }

    /// <summary>
    /// The machine whose push diverged — the one that produced <see cref="VersionBId"/>, and the one
    /// that is stuck. Part of the dedupe key, and it answers "which machine?" without a join; until
    /// now only an agent-reported health event could say.
    /// <para>
    /// Nullable solely because rows written before dedupe existed have no value. Every new conflict
    /// sets it.
    /// </para>
    /// </summary>
    public Guid? MachineId { get; set; }

    /// <summary>
    /// How many divergent pushes this one open conflict represents. A machine that keeps saving while
    /// conflicted bumps this instead of writing a row per push — the same reasoning that makes agent
    /// health events deduplicate rather than append (<c>HealthService</c>). One real incident produced
    /// <b>75 rows for a single unresolved divergence</b>.
    /// </summary>
    public int Count { get; set; } = 1;

    /// <summary>When the most recent divergent push arrived. <see cref="CreatedAt"/> stays put.</summary>
    public DateTime LastSeen { get; set; }

    public ConflictStatus Status { get; set; } = ConflictStatus.Open;
    public DateTime CreatedAt { get; set; }

    public Guid? ResolvedVersionId { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>
/// A queued instruction from the dashboard for an agent to execute on its next
/// poll (the agent command channel). Agents poll for their pending commands,
/// run them, and report the outcome back.
/// </summary>
public class AgentCommand
{
    public Guid Id { get; set; }

    /// <summary>The machine that should run this command.</summary>
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }

    /// <summary>Target game, or null to apply to all of the machine's games.</summary>
    public Guid? GameId { get; set; }

    public AgentCommandType Type { get; set; }
    public bool Force { get; set; }

    /// <summary>The playthrough a <see cref="AgentCommandType.SwitchPlaythrough"/> switches to; null for every other type.</summary>
    public Guid? PlaythroughId { get; set; }
    public CommandStatus Status { get; set; } = CommandStatus.Pending;

    public DateTime CreatedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// When this claim stops being exclusive. A <see cref="CommandStatus.Dispatched"/> command whose
    /// lease has passed is eligible to be handed out again: delivery is a lease, not a one-way door.
    /// An agent that crashed between the poll and the result used to strand the command in
    /// Dispatched forever, with nothing to retry it and nothing to say so.
    /// </summary>
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>
    /// The claim that currently owns this command. Written by the same atomic UPDATE that marks it
    /// Dispatched, so the claimer can then read back exactly the rows it won and two pollers sharing
    /// one machine identity cannot both be handed the same command.
    /// </summary>
    public Guid? ClaimToken { get; set; }

    /// <summary>How many times this command has been handed to an agent. &gt;1 means a lease
    /// expired unacknowledged; the console shows it so a command that keeps failing silently is
    /// visible rather than merely slow.</summary>
    public int ClaimCount { get; set; }

    /// <summary>Human-readable outcome the agent reports (e.g. "pushed 1 game").</summary>
    public string? Result { get; set; }
}

/// <summary>
/// The DLC and title updates one machine has for one game (tasks/emulator-saves Phase 5c, the DLC check), as it last
/// reported them: what lets the console show which machine can pull the latest save safely before anyone tries.
/// </summary>
public class MachineGameContent
{
    public Guid MachineId { get; set; }
    public Guid GameId { get; set; }
    /// <summary><see cref="ContentPackageDto"/>[] as JSON.</summary>
    public string ContentJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; }
    /// <summary>The last launch's Xenia profile check, <see cref="LaunchProfileReport"/> as JSON; null until one ran.</summary>
    public string? LaunchJson { get; set; }
    public DateTime? LaunchAt { get; set; }
}

/// <summary>
/// A DLC hand-over between two machines (tasks/emulator-saves Phase 5c, step 11): packs pass through the server from the
/// machine that has them to the one that asked, and are deleted on delivery or expiry. <see cref="PartsJson"/> holds each
/// pack's progress; the files live under the archive root's <c>_handover</c> folder.
/// </summary>
public class ContentTransfer
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid FromMachineId { get; set; }
    public Guid ToMachineId { get; set; }
    public TransferStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string PartsJson { get; set; } = "[]";
}

/// <summary>
/// A machine's stored save-folder path for a specific game. Keyed on
/// (MachineId, GameId); one row per machine that has mapped a local folder.
/// Injected into the agent's game list for reconcile and surfaced in the dashboard.
/// </summary>
public class MachineSavePath
{
    public Guid MachineId { get; set; }
    public Guid GameId { get; set; }
    /// <summary>Which of the game's save folders: <see cref="SaveRoot.PrimaryKey"/> or a
    /// <see cref="GameSavePath.Key"/>.</summary>
    public string PathKey { get; set; } = SaveRoot.PrimaryKey;
    public string SavePath { get; set; } = "";
    /// <summary>This machine's own names for the game's files, every folder's (<see cref="FileNameDto"/> JSON, tasks/emulator-saves
    /// Phase 17), on the primary folder's row only; null when it uses the server's names. Shown in the console; the agent's
    /// own copy is the one its syncs use.</summary>
    public string? FileNames { get; set; }
}

/// <summary>
/// An EXTRA save folder of a game (tasks/multiple-save-paths). The primary folder lives on
/// <see cref="Game"/> itself (<see cref="Game.SuggestedSaveDir"/>, <see cref="Game.IncludeGlobs"/>), so
/// a single-folder game and every older agent are untouched. The key names the folder's slice of
/// every archive and is identical on every machine; it is never renamed.
/// </summary>
public class GameSavePath
{
    public Guid GameId { get; set; }
    public string Key { get; set; } = "";
    /// <summary>What the console and agent UIs call it ("Save states").</summary>
    public string? Label { get; set; }
    /// <summary>Portable template every machine expands for itself — the extra folder's
    /// <see cref="Game.SuggestedSaveDir"/>.</summary>
    public string? Template { get; set; }
    /// <summary>Newline-separated include globs, or null for the whole folder.</summary>
    public string? IncludeGlobs { get; set; }
    public int SortOrder { get; set; }
    /// <summary>When the folder was removed. A retired row is hidden from every query (a filter on
    /// the context) and kept only so its key is never reused: stored versions may still hold its files.</summary>
    public DateTime? RetiredAt { get; set; }
}

/// <summary>
/// An <b>unconfirmed</b> save folder a machine's scan found for a game it has not mapped. Keyed on
/// (MachineId, GameId) like <see cref="MachineSavePath"/>, and deliberately a separate table:
/// writing a guess into MachineSavePath would push it back to the agent as authority on the next
/// poll. The console offers it; a human promotes it.
/// </summary>
public class MachineScanCandidate
{
    public Guid MachineId { get; set; }
    public Guid GameId { get; set; }
    public string SuggestedPath { get; set; } = "";

    /// <summary>Last heartbeat that still reported this path — a stale guess is worth distrusting.</summary>
    public DateTime LastSeen { get; set; }
}

/// <summary>
/// How one machine found a game: the scanner source that enrolled it there ("Emulator › RetroArch",
/// "Steam › Installed game", …). Per machine, because two machines rarely find the same game the same
/// way. Display only: nothing syncs differently because of it.
/// </summary>
public class MachineGameSource
{
    public Guid MachineId { get; set; }
    public Guid GameId { get; set; }
    /// <summary>One of <see cref="GameSourceKinds"/>.</summary>
    public string Kind { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>Newline-separated extra facts ("SNES", "Flatpak", "AppID 1245620"), or null.</summary>
    public string? Tags { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// A single-use, short-lived token the console mints so a new agent can trade it for a real
/// machine API key (Decisions.md §4). Only the hash is stored — the raw token exists solely in
/// the policy file the admin downloads.
/// </summary>
public class EnrollmentToken
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = "";

    /// <summary>Machine name this token was minted for. When set, it is binding: the enrolling
    /// agent cannot claim a different identity. Null lets the agent supply its own hostname.</summary>
    public string? MachineName { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set the moment the token is spent. Non-null = burnt; a second redeem is refused.</summary>
    public DateTime? RedeemedAt { get; set; }
    public Guid? RedeemedByMachineId { get; set; }
}

/// <summary>
/// The last heartbeat an agent sent (one row per machine). This is what makes an agent's silence
/// readable: without it, a Deck that never started and a Deck with nothing to report look the same.
/// </summary>
public class AgentHealth
{
    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }

    public DateTime LastHeartbeat { get; set; }
    public string? AgentVersion { get; set; }
    public string? Platform { get; set; }

    /// <summary>The heartbeat's <see cref="AgentOsInfo"/>, flattened. Display only; null from an older agent.</summary>
    public string? OsId { get; set; }
    public string? OsName { get; set; }
    public string? OsIdLike { get; set; }
    public string? OsVariantId { get; set; }
    public string? OsDevice { get; set; }

    /// <summary>An update the agent has staged but not applied. Null when none (or an older agent).</summary>
    public string? StagedVersion { get; set; }

    /// <summary>Whether the agent adopts the console's pushed look. Null from an agent that predates the field.</summary>
    public bool? FollowsConsoleAppearance { get; set; }

    /// <summary>The users of the machine's Yuzu-based Switch emulators (<see cref="SwitchUserReport"/>[] as JSON), with
    /// when they were reported. Null until an agent that knows them reports.</summary>
    public string? SwitchUsersJson { get; set; }
    public DateTime? SwitchUsersAt { get; set; }

    /// <summary>The agent's own last successful sync — not the server's view of it.</summary>
    public DateTime? LastSyncTime { get; set; }

    public int TrackedGames { get; set; }
    /// <summary>Tracked games with no save folder on this machine: silently skipped, so worth naming.</summary>
    public int UnmappedGames { get; set; }
    /// <summary>Pushes waiting on the network. A number that only grows means this machine is stranded.</summary>
    public int OfflineQueueDepth { get; set; }
}

/// <summary>
/// A problem an agent reported that the server could not have inferred on its own. Deduplicated on
/// (MachineId, GameId, Code) while open: a condition that persists updates <see cref="LastSeen"/>
/// and <see cref="Count"/> rather than filling the table with a row every poll.
/// </summary>
public class AgentEvent
{
    public Guid Id { get; set; }

    public Guid MachineId { get; set; }
    public Machine? Machine { get; set; }

    public Guid? GameId { get; set; }
    public Game? Game { get; set; }

    /// <summary>Stable condition id from <see cref="SaveLocker.Shared.AgentEventCodes"/>.</summary>
    public string Code { get; set; } = "";
    public AgentEventSeverity Severity { get; set; }
    public string Message { get; set; } = "";

    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    /// <summary>How many times this condition has been reported since it opened.</summary>
    public int Count { get; set; } = 1;

    /// <summary>Set when the condition cleared — either the agent synced that game cleanly, or an
    /// admin dismissed it. Non-null means it no longer shows as an open problem.</summary>
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>A persisted server setting (key/value), e.g. the SteamGridDB API key.
/// DB values override <c>IConfiguration</c> (appsettings/env) so admins can manage
/// settings from the dashboard without editing config files.</summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>Append-only audit trail for visibility in the dashboard.</summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid? MachineId { get; set; }
    public Guid? GameId { get; set; }
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
}

/// <summary>
/// One signed-in dashboard browser. The console used to keep the admin PASSWORD itself in
/// <c>localStorage</c> and send it on every request, so anything able to read that storage (an
/// XSS, a hostile extension) walked away with the real credential. A session is instead a random
/// 256-bit token the server can revoke (Lock, "sign out everywhere", a password change) and that
/// expires on its own; only its SHA-256 is stored, the same treatment machine API keys get.
/// </summary>
public class AdminSession
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }

    /// <summary>Sliding idle expiry, capped at <c>CreatedAt + Security:SessionMaxDays</c>.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Where it was created from (the throttle's client key) — context for the audit trail.</summary>
    public string? ClientAddress { get; set; }
}
