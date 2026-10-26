using Microsoft.EntityFrameworkCore;
using SaveLocker.Server.Data;
using SaveLocker.Shared;

namespace SaveLocker.Server.Services;

/// <summary>The game, or the playthrough named, does not exist (or is not that game's).</summary>
public sealed class PlaythroughNotFoundException(string message) : Exception(message);

/// <summary>An archived playthrough is frozen: nothing pushes to, pulls from, selects or leases it.</summary>
public sealed class PlaythroughArchivedException(string name)
    : Exception($"The playthrough '{name}' is archived. Unarchive it to use it.");

public enum PlaythroughFailure { None, NotFound, Invalid, Conflict }

/// <summary>What a playthrough action did: the playthrough as it is now, or why not.</summary>
public readonly record struct PlaythroughOutcome(
    PlaythroughDto? Value, PlaythroughFailure Failure = PlaythroughFailure.None, string? Error = null)
{
    public static PlaythroughOutcome NotFound(string what) => new(null, PlaythroughFailure.NotFound, what);
    public static PlaythroughOutcome Invalid(string why) => new(null, PlaythroughFailure.Invalid, why);
    public static PlaythroughOutcome Conflict(string why) => new(null, PlaythroughFailure.Conflict, why);
}

/// <summary>
/// Playthroughs (tasks/save-playthroughs): a game's several lines of saves. Every game has one or more; each has its own
/// head, versions, conflicts and lease, and each device follows exactly one per game. Everything that syncs asks
/// <see cref="ResolvePlaythroughAsync"/> which line it is on; nothing here moves a save between lines.
/// </summary>
public sealed partial class SyncService
{
    // ----- Which playthrough -----

    /// <summary>
    /// The playthrough a request is about: the one it names, else the one <paramref name="machineId"/> follows, else the
    /// game's default. A machine that follows none yet is put on the default and that is written down at once — the
    /// default can change later, and a device with no record would follow it out from under its own files. This is also
    /// what keeps an agent that predates playthroughs on one line: it names none, so it gets its recorded one.
    /// </summary>
    /// <exception cref="PlaythroughNotFoundException">No such game, or the named playthrough is another game's.</exception>
    /// <exception cref="PlaythroughArchivedException">The playthrough is archived.</exception>
    public async Task<Playthrough> ResolvePlaythroughAsync(
        Guid gameId, Guid? machineId, Guid? requested, CancellationToken ct = default)
    {
        if (requested is { } id)
        {
            var named = await _db.Playthroughs.FirstOrDefaultAsync(p => p.Id == id && p.GameId == gameId, ct)
                        ?? throw new PlaythroughNotFoundException("Unknown playthrough.");
            return named.ArchivedAt is null ? named : throw new PlaythroughArchivedException(named.Name);
        }

        if (machineId is { } machine)
        {
            var selection = await _db.DeviceSelections.FirstOrDefaultAsync(d => d.MachineId == machine && d.GameId == gameId, ct);
            if (selection is not null &&
                await _db.Playthroughs.FirstOrDefaultAsync(p => p.Id == selection.PlaythroughId && p.GameId == gameId, ct) is { } chosen)
                return chosen.ArchivedAt is null ? chosen : throw new PlaythroughArchivedException(chosen.Name);

            var fallback = await DefaultPlaythroughAsync(gameId, ct);
            await RecordSelectionAsync(machine, gameId, fallback.Id, ct);
            return fallback;
        }

        return await DefaultPlaythroughAsync(gameId, ct);
    }

    /// <summary>The game's default playthrough. A game that somehow has none (a database from before this release that
    /// skipped its migration) gets one made here, holding its current head.</summary>
    public async Task<Playthrough> DefaultPlaythroughAsync(Guid gameId, CancellationToken ct = default)
    {
        if (await _db.Playthroughs.FirstOrDefaultAsync(p => p.GameId == gameId && p.IsDefault, ct) is { } found) return found;

        var game = await _db.Games.FindAsync([gameId], ct) ?? throw new PlaythroughNotFoundException("Unknown game.");
        var made = new Playthrough
        {
            Id = Guid.NewGuid(), GameId = gameId, Name = Playthroughs.DefaultName, CreatedAt = DateTime.UtcNow,
            HeadVersionId = game.HeadVersionId, IsDefault = true,
        };
        _db.Playthroughs.Add(made);
        try
        {
            await _db.SaveChangesAsync(ct);
            return made;
        }
        catch (DbUpdateException)
        {
            // Another request made it first.
            _db.Entry(made).State = EntityState.Detached;
            return await _db.Playthroughs.FirstAsync(p => p.GameId == gameId && p.IsDefault, ct);
        }
    }

    /// <summary>Write down that a machine follows a playthrough. Losing the race to another request that wrote the same
    /// row is no failure.</summary>
    private async Task RecordSelectionAsync(Guid machineId, Guid gameId, Guid playthroughId, CancellationToken ct)
    {
        var row = await _db.DeviceSelections.FirstOrDefaultAsync(d => d.MachineId == machineId && d.GameId == gameId, ct);
        if (row is null)
        {
            row = new DeviceSelection { MachineId = machineId, GameId = gameId, PlaythroughId = playthroughId, SelectedAt = DateTime.UtcNow };
            _db.DeviceSelections.Add(row);
        }
        else if (row.PlaythroughId != playthroughId)
        {
            row.PlaythroughId = playthroughId;
            row.SelectedAt = DateTime.UtcNow;
        }
        else return;

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (_db.Entry(row).State == EntityState.Added)
        {
            _db.Entry(row).State = EntityState.Detached;
        }
    }

    // ----- Reading -----

    /// <summary>A game's playthroughs, the default first, then by age with the archived ones last. Null for an unknown game.</summary>
    public async Task<PlaythroughDto[]?> ListPlaythroughsAsync(Guid gameId)
    {
        if (!await _db.Games.AnyAsync(g => g.Id == gameId)) return null;
        // A game from before the migration ran has no playthrough yet; this makes its Main.
        await DefaultPlaythroughAsync(gameId);

        var rows = await _db.Playthroughs.Where(p => p.GameId == gameId).ToListAsync();
        var headIds = rows.Where(p => p.HeadVersionId != null).Select(p => p.HeadVersionId!.Value).ToList();
        var heads = await _db.SaveVersions.Include(v => v.Machine).Where(v => headIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);
        var totals = await _db.SaveVersions.Where(v => v.GameId == gameId)
            .GroupBy(v => v.PlaythroughId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Size = g.Sum(v => v.Size) })
            .ToDictionaryAsync(x => x.Id);
        var conflicted = (await _db.Conflicts.Where(c => c.GameId == gameId && c.Status == ConflictStatus.Open)
            .Select(c => c.PlaythroughId).Distinct().ToListAsync()).ToHashSet();
        var devices = await (from d in _db.DeviceSelections
                             join m in _db.Machines on d.MachineId equals m.Id
                             where d.GameId == gameId
                             orderby m.Name
                             select new { d.PlaythroughId, d.MachineId, m.Name, d.SelectedAt }).ToListAsync();

        return rows
            .OrderBy(p => p.ArchivedAt != null).ThenByDescending(p => p.IsDefault).ThenBy(p => p.CreatedAt)
            .Select(p => new PlaythroughDto(
                p.Id, p.GameId, p.Name, p.IsDefault, p.CreatedAt, p.ForkedFromVersionId, p.ArchivedAt,
                p.HeadVersionId is { } h && heads.TryGetValue(h, out var head) ? head.ToDto() : null,
                totals.TryGetValue(p.Id, out var t) ? t.Count : 0,
                totals.TryGetValue(p.Id, out var t2) ? t2.Size : 0,
                conflicted.Contains(p.Id),
                devices.Where(d => d.PlaythroughId == p.Id)
                    .Select(d => new PlaythroughDeviceDto(d.MachineId, d.Name, d.SelectedAt)).ToArray()))
            .ToArray();
    }

    private async Task<PlaythroughDto?> PlaythroughDtoAsync(Guid gameId, Guid playthroughId) =>
        (await ListPlaythroughsAsync(gameId))?.FirstOrDefault(p => p.Id == playthroughId);

    // ----- Changing -----

    /// <summary>
    /// A new playthrough, starting from a version, from another playthrough's latest save, or empty. Nothing is uploaded or
    /// copied: its head points at the version it was forked from, which stays in the playthrough it was made in. Its first
    /// push (from whichever device selects it) is its first version of its own.
    /// </summary>
    public async Task<PlaythroughOutcome> CreatePlaythroughAsync(
        Guid gameId, CreatePlaythroughRequest req, string by, Guid? machineId = null)
    {
        var game = await _db.Games.FindAsync(gameId);
        if (game is null) return PlaythroughOutcome.NotFound("Unknown game.");
        if (Playthroughs.CleanName(req.Name) is not { } name)
            return PlaythroughOutcome.Invalid($"Give the playthrough a name of 1 to {Playthroughs.MaxNameLength} characters.");
        if (req.FromVersionId is not null && req.FromPlaythroughId is not null)
            return PlaythroughOutcome.Invalid("Fork from a version or from a playthrough, not both.");
        await DefaultPlaythroughAsync(gameId);
        if (await NameTakenAsync(gameId, name, null))
            return PlaythroughOutcome.Conflict($"This game already has a playthrough called '{name}'.");

        Guid? head = null;
        string from = "empty";
        if (req.FromVersionId is { } versionId)
        {
            var version = await _db.SaveVersions.FirstOrDefaultAsync(v => v.Id == versionId && v.GameId == gameId);
            if (version is null) return PlaythroughOutcome.NotFound("Unknown version.");
            if (version.MergedFrom is not null)
                return PlaythroughOutcome.Invalid("That version is history of a merged game; a playthrough cannot start from it.");
            head = version.Id;
            from = $"version {version.Id}";
        }
        else if (req.FromPlaythroughId is { } sourceId)
        {
            var source = await _db.Playthroughs.FirstOrDefaultAsync(p => p.Id == sourceId && p.GameId == gameId);
            if (source is null) return PlaythroughOutcome.NotFound("Unknown playthrough to fork from.");
            if (source.HeadVersionId is null)
                return PlaythroughOutcome.Invalid($"'{source.Name}' has no save yet to start from.");
            head = source.HeadVersionId;
            from = $"'{source.Name}'";
        }

        var created = new Playthrough
        {
            Id = Guid.NewGuid(), GameId = gameId, Name = name, CreatedAt = DateTime.UtcNow,
            ForkedFromVersionId = head, HeadVersionId = head, IsDefault = false,
        };
        _db.Playthroughs.Add(created);
        await Audit(machineId, gameId, "playthrough.create", $"'{name}' from {from} by {by}");
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            _db.Entry(created).State = EntityState.Detached;
            return PlaythroughOutcome.Conflict($"This game already has a playthrough called '{name}'.");
        }
        return new PlaythroughOutcome(await PlaythroughDtoAsync(gameId, created.Id));
    }

    public async Task<PlaythroughOutcome> RenamePlaythroughAsync(Guid gameId, Guid playthroughId, string newName, string by)
    {
        var playthrough = await _db.Playthroughs.FirstOrDefaultAsync(p => p.Id == playthroughId && p.GameId == gameId);
        if (playthrough is null) return PlaythroughOutcome.NotFound("Unknown playthrough.");
        if (Playthroughs.CleanName(newName) is not { } name)
            return PlaythroughOutcome.Invalid($"Give the playthrough a name of 1 to {Playthroughs.MaxNameLength} characters.");
        if (await NameTakenAsync(gameId, name, playthroughId))
            return PlaythroughOutcome.Conflict($"This game already has a playthrough called '{name}'.");

        var old = playthrough.Name;
        playthrough.Name = name;
        await Audit(null, gameId, "playthrough.rename", $"'{old}' → '{name}' by {by}");
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            await _db.Entry(playthrough).ReloadAsync();
            return PlaythroughOutcome.Conflict($"This game already has a playthrough called '{name}'.");
        }
        return new PlaythroughOutcome(await PlaythroughDtoAsync(gameId, playthroughId));
    }

    /// <summary>
    /// Archive a playthrough (hide it and freeze it, keeping every version and with no expiry) or bring it back. The
    /// default cannot be archived; nor can one a device is on, one with an open conflict, or one someone is playing right
    /// now — move them off it first.
    /// </summary>
    public async Task<PlaythroughOutcome> SetPlaythroughArchivedAsync(Guid gameId, Guid playthroughId, bool archived, string by)
    {
        var playthrough = await _db.Playthroughs.FirstOrDefaultAsync(p => p.Id == playthroughId && p.GameId == gameId);
        if (playthrough is null) return PlaythroughOutcome.NotFound("Unknown playthrough.");
        if (archived == (playthrough.ArchivedAt is not null)) return new PlaythroughOutcome(await PlaythroughDtoAsync(gameId, playthroughId));

        if (archived)
        {
            if (playthrough.IsDefault)
                return PlaythroughOutcome.Invalid("The default playthrough can't be archived. Make another one the default first.");
            var onIt = await (from d in _db.DeviceSelections
                              join m in _db.Machines on d.MachineId equals m.Id
                              where d.PlaythroughId == playthroughId
                              orderby m.Name
                              select m.Name).ToListAsync();
            if (onIt.Count > 0)
                return PlaythroughOutcome.Conflict($"{string.Join(", ", onIt)} {(onIt.Count == 1 ? "is" : "are")} on '{playthrough.Name}'. Switch {(onIt.Count == 1 ? "it" : "them")} to another playthrough first.");
            if (await _db.Conflicts.AnyAsync(c => c.PlaythroughId == playthroughId && c.Status == ConflictStatus.Open))
                return PlaythroughOutcome.Conflict($"'{playthrough.Name}' has an open conflict. Resolve it first.");
            if (await ActiveLeaseAsync(gameId, playthroughId) is { } lease)
                return PlaythroughOutcome.Conflict($"{lease.Machine?.Name ?? "A machine"} is playing '{playthrough.Name}' right now.");
        }

        playthrough.ArchivedAt = archived ? DateTime.UtcNow : null;
        await Audit(null, gameId, archived ? "playthrough.archive" : "playthrough.unarchive", $"'{playthrough.Name}' by {by}");
        await _db.SaveChangesAsync();
        return new PlaythroughOutcome(await PlaythroughDtoAsync(gameId, playthroughId));
    }

    /// <summary>Make a playthrough the one new devices (and agents that predate playthroughs) start on. Devices already
    /// on a playthrough stay on it.</summary>
    public async Task<PlaythroughOutcome> SetDefaultPlaythroughAsync(Guid gameId, Guid playthroughId, string by)
    {
        var game = await _db.Games.FindAsync(gameId);
        var playthrough = await _db.Playthroughs.FirstOrDefaultAsync(p => p.Id == playthroughId && p.GameId == gameId);
        if (game is null || playthrough is null) return PlaythroughOutcome.NotFound("Unknown playthrough.");
        if (playthrough.ArchivedAt is not null) return PlaythroughOutcome.Invalid("Unarchive it before making it the default.");
        if (playthrough.IsDefault) return new PlaythroughOutcome(await PlaythroughDtoAsync(gameId, playthroughId));

        await using var tx = await _db.Database.BeginTransactionAsync();
        var old = await _db.Playthroughs.FirstOrDefaultAsync(p => p.GameId == gameId && p.IsDefault);
        // Two steps: the database allows only one default at a time.
        if (old is not null)
        {
            old.IsDefault = false;
            await _db.SaveChangesAsync();
        }
        playthrough.IsDefault = true;
        game.HeadVersionId = playthrough.HeadVersionId;
        await Audit(null, gameId, "playthrough.default", $"'{old?.Name}' → '{playthrough.Name}' by {by}");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return new PlaythroughOutcome(await PlaythroughDtoAsync(gameId, playthroughId));
    }

    /// <summary>A device says which playthrough it now follows (after a switch it carried out). Refuses an archived one.</summary>
    public async Task<PlaythroughOutcome> SelectPlaythroughAsync(Guid machineId, Guid gameId, Guid playthroughId)
    {
        var playthrough = await _db.Playthroughs.FirstOrDefaultAsync(p => p.Id == playthroughId && p.GameId == gameId);
        if (playthrough is null) return PlaythroughOutcome.NotFound("Unknown playthrough.");
        if (playthrough.ArchivedAt is not null) return PlaythroughOutcome.Conflict(new PlaythroughArchivedException(playthrough.Name).Message);

        var before = await _db.DeviceSelections.Where(d => d.MachineId == machineId && d.GameId == gameId)
            .Select(d => (Guid?)d.PlaythroughId).FirstOrDefaultAsync();
        await RecordSelectionAsync(machineId, gameId, playthroughId, default);
        if (before != playthroughId)
        {
            await Audit(machineId, gameId, "playthrough.select", $"'{playthrough.Name}'");
            await _db.SaveChangesAsync();
        }
        return new PlaythroughOutcome(await PlaythroughDtoAsync(gameId, playthroughId));
    }

    private Task<bool> NameTakenAsync(Guid gameId, string name, Guid? exceptId)
    {
        // The column is NOCASE, so the database's own comparison is the case-insensitive one.
        return _db.Playthroughs.AnyAsync(p => p.GameId == gameId && p.Name == name && p.Id != exceptId);
    }

    // ----- The switch command -----

    /// <summary>Whether the machine's last heartbeat reports an agent that can run a <see cref="AgentCommandType.SwitchPlaythrough"/>.</summary>
    private async Task<bool> AgentSupportsPlaythroughsAsync(Guid machineId) =>
        Playthroughs.AgentCanSwitch(await _db.AgentHealth.Where(h => h.MachineId == machineId)
            .Select(h => h.AgentVersion).FirstOrDefaultAsync());

    /// <summary>Null when every switch request names a game, a live playthrough of it and a machine whose agent can run it.</summary>
    private async Task<string?> ValidateSwitchRequestsAsync(IReadOnlyList<EnqueueCommandRequest> reqs)
    {
        foreach (var r in reqs.Where(r => r.Type == AgentCommandType.SwitchPlaythrough))
        {
            if (r.GameId is not { } gameId || r.PlaythroughId is not { } playthroughId)
                return "Switching a playthrough needs a game and a playthrough.";
            var playthrough = await _db.Playthroughs.FirstOrDefaultAsync(p => p.Id == playthroughId && p.GameId == gameId);
            if (playthrough is null) return "Unknown playthrough.";
            if (playthrough.ArchivedAt is not null) return new PlaythroughArchivedException(playthrough.Name).Message;
            if (!await AgentSupportsPlaythroughsAsync(r.MachineId))
                return $"This machine's agent is older than {Playthroughs.MinAgentVersion} (or has not reported a version) and cannot switch playthroughs. Update it first.";
        }
        return null;
    }
}
