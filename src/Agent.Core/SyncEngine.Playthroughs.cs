using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>How a playthrough switch or creation ended. <paramref name="Ok"/> false: the device is still on the playthrough it
/// was on, with its save folder as it was (or put back from the copy taken first).</summary>
/// <param name="Offline">The server could not be reached: the save was switched from this device's own copies, and the push and
/// the selection are sent when the server is back.</param>
public sealed record SwitchResult(bool Ok, string Message, bool Offline = false, PlaythroughDto? Playthrough = null);

/// <summary>
/// Playthroughs on the agent (tasks/save-playthroughs Phase 4): which line of saves a game follows here, and switching between
/// them. A save never moves between playthroughs silently — a switch pushes the outgoing save to its own line first, and only
/// then restores the other one.
/// </summary>
public sealed partial class SyncEngine
{
    private PlaythroughStore Store => PlaythroughStore.For(_config);

    /// <summary>One switch per game at a time in this process, whoever asked (the game page, the console's command, the CLI's
    /// engine). Static: an engine replaced mid-switch must not let a second one start beside it.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> SwitchGates = new();

    /// <summary>
    /// A push on the playthrough whose not-yet-uploaded copy the folder was restored from (an offline switch back to it), made
    /// from that copy's base: the folder holds the copy's save or one played on from it, so this push carried it, and the copy
    /// must not be sent again — from its old parent it would read as a conflict with this very push. Under the push's lock.
    /// </summary>
    private void SupersedeRestoredCopy(TrackedGame game, Guid? parentSent, Guid? newVersionId)
    {
        if (game.PlaythroughId is not { } playthroughId) return;
        try
        {
            if (Store.Find(game.GameId, playthroughId) is not { Uploaded: false } copy || copy.VersionId != parentSent) return;
            FinishCopy(game, playthroughId, newVersionId, _config.PlaythroughCopies);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AgentStateLockException)
        {
            _log($"[{game.Name}] could not update the kept copy: {ex.Message}");
        }
    }

    /// <summary>The store's mark-and-sweep at start (and after each switch): a copy no snapshot names any more goes once it is older
    /// than the grace period. In the background, and best effort: a sweep that cannot run now runs next time.</summary>
    private void SweepPlaythroughStore() => _ = Task.Run(() =>
    {
        try { Store.Sweep(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AgentStateLockException) { /* next time */ }
    });

    /// <summary>" in Hard mode" once a game has more than one playthrough (or while naming the one being switched to); else nothing.</summary>
    private static string InPlaythrough(TrackedGame game, PlaythroughDto? to = null)
    {
        var name = to?.Name ?? game.PlaythroughName;
        return (to is not null || game.PlaythroughCount > 1) && !string.IsNullOrEmpty(name) ? $" in {name}" : "";
    }

    private static void FollowPlaythrough(TrackedGame game, PlaythroughDto playthrough)
    {
        game.PlaythroughId = playthrough.Id;
        game.PlaythroughName = playthrough.Name;
    }

    /// <summary>Whether a conflict is on the line this device follows. A conflict that names no playthrough, or a device that
    /// has never switched, cannot say otherwise.</summary>
    private static bool FollowsConflict(TrackedGame game, ConflictDto conflict) =>
        conflict.PlaythroughId is null || game.PlaythroughId is null || conflict.PlaythroughId == game.PlaythroughId;

    /// <summary>The playthrough this device follows for the game, from the server: the one it recorded, or — for a device that
    /// has never switched and was never recorded — the default. Null when the server predates playthroughs.</summary>
    private async Task<PlaythroughDto?> FollowedAsync(TrackedGame game, CancellationToken ct)
    {
        var list = await _api.ListPlaythroughsAsync(game.GameId, ct);
        if (list is null) return null;
        if (game.PlaythroughId is { } id) return list.FirstOrDefault(p => p.Id == id);
        return list.FirstOrDefault(p => p.Devices.Any(d => d.MachineId == _config.MachineId)) ?? list.FirstOrDefault(p => p.IsDefault);
    }

    private async Task<Guid?> FollowedIdAsync(TrackedGame game) => game.PlaythroughId ?? (await FollowedAsync(game, default))?.Id;

    /// <summary>The latest save of the line this device follows (the state route answers for the calling machine's playthrough,
    /// or the one named); for a server without playthroughs, the game's.</summary>
    private Task<SaveVersionDto?> FollowedHeadAsync(TrackedGame game, CancellationToken ct) =>
        _api.GetPlaythroughHeadAsync(game.GameId, game.PlaythroughId, ct);

    /// <summary>Fills in which playthrough a game that has never switched follows, so the rest of this device (offline copies, the
    /// agent UI) can name it. Called wherever the list has just been read.</summary>
    internal void RememberFollowed(TrackedGame game, IReadOnlyList<PlaythroughDto> list)
    {
        var followed = game.PlaythroughId is { } id
            ? list.FirstOrDefault(p => p.Id == id)
            : list.FirstOrDefault(p => p.Devices.Any(d => d.MachineId == _config.MachineId)) ?? list.FirstOrDefault(p => p.IsDefault);
        if (followed is null) return;
        if (game.PlaythroughId == followed.Id && game.PlaythroughName == followed.Name && game.PlaythroughCount == list.Count) return;
        game.PlaythroughId = followed.Id;
        game.PlaythroughName = followed.Name;
        game.PlaythroughCount = list.Count;
        _config.SaveGameSyncState(game);
    }

    /// <summary>Takes the playthrough count a push or pull answered with, so a line made on another device or in the console shows
    /// here after an ordinary sync, with nothing having read the list first. True when it changed; the caller saves.</summary>
    private static bool TakePlaythroughCount(TrackedGame game, int? count)
    {
        if (count is not { } n || n == game.PlaythroughCount) return false;
        game.PlaythroughCount = n;
        return true;
    }

    /// <summary>
    /// Switch this device to another playthrough of <paramref name="game"/>: refuse while it runs or a session is open here or a
    /// conflict is open; push the outgoing save to its own playthrough (or keep it, to be pushed when the server is back); put the
    /// target's latest in the folder; then record the selection here and on the server.
    /// <para>
    /// A failure at any point leaves the device on the <b>old</b> playthrough. The restore is the only step that touches the
    /// folder, and the game follows the new line only in the same step that records the version it restored (see
    /// <see cref="PullAsync"/>'s <c>switchTo</c>); a restore that fails half-way is put back from the copy taken first, or — with
    /// copies off — from the server, where the outgoing save already is.
    /// </para>
    /// </summary>
    /// <param name="keepPaths">Used only when the target has no save yet (<i>Start fresh</i>): the files, as archive names, to leave in
    /// the otherwise emptied folder. Null or empty keeps nothing.</param>
    public async Task<SwitchResult> SwitchPlaythroughAsync(
        TrackedGame game, Guid targetId, IReadOnlyList<string>? keepPaths = null, CancellationToken ct = default)
    {
        if (IsRetired) return new SwitchResult(false, "This connection has been replaced; try again.");
        var switchGate = SwitchGates.GetOrAdd(game.GameId, _ => new SemaphoreSlim(1, 1));
        if (!await switchGate.WaitAsync(0, ct))
            return new SwitchResult(false, $"A playthrough switch of {game.Name} is already running. Wait for it to finish.");
        try { return await SwitchCoreAsync(game, targetId, keepPaths, ct); }
        finally { switchGate.Release(); }
    }

    private async Task<SwitchResult> SwitchCoreAsync(
        TrackedGame game, Guid targetId, IReadOnlyList<string>? keepPaths, CancellationToken ct)
    {
        using var linked = LinkRetirement(ct);
        ct = linked.Token;

        if (!game.IsEnrolledHere)
            return new SwitchResult(false, $"{game.Name} has no save folder on this device, so there is nothing to switch.");
        if (GameActivity.IsActive(game, out var running))
            return new SwitchResult(false, $"{GameActivity.WhatIsOpen(game, running)} is open, so the playthrough was not switched. Close it, then switch.");
        if (HeldLeases().Contains(game.GameId))
            return new SwitchResult(false, $"A session of {game.Name} is open on this device, so the playthrough was not switched. Close the game, then switch.");

        List<PlaythroughDto>? list = null;
        var serverUp = true;
        try { list = await _api.ListPlaythroughsAsync(game.GameId, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested && ServerReachability.IsUnreachable(ex)) { serverUp = false; }
        if (serverUp && list is null)
            return new SwitchResult(false, "This server does not have playthroughs yet. Update it first.");
        if (list is not null) RememberFollowed(game, list);

        var store = Store;
        var mode = _config.PlaythroughCopies;
        var current = list?.FirstOrDefault(p => p.Id == game.PlaythroughId);
        var currentId = game.PlaythroughId;
        if (currentId is null)
            return new SwitchResult(false, "This device cannot tell which playthrough it is on without the server. Try again when it is reachable.");
        if (currentId == targetId)
            return new SwitchResult(true, $"{game.Name} is already on {game.PlaythroughName ?? "that playthrough"}.");

        PlaythroughDto? target = list?.FirstOrDefault(p => p.Id == targetId);
        if (serverUp)
        {
            if (target is null) return new SwitchResult(false, "That playthrough does not exist.");
            if (target.ArchivedAt is not null)
                return new SwitchResult(false, $"{target.Name} is archived. Unarchive it first.");
            if (await FindOpenConflictAsync(game) is not null)
                return new SwitchResult(false, $"{game.Name} has a conflict in {game.PlaythroughName ?? "this playthrough"}. Resolve it first, so no save is put in the wrong line.");
        }

        var outgoingName = game.PlaythroughName ?? current?.Name ?? "the current playthrough";

        // ---- 1. The outgoing save goes to its own playthrough, or is kept to go later ----
        _log($"[{game.Name}] switching from {outgoingName} to {target?.Name ?? "another playthrough"}…");
        string outgoingHash;
        try { outgoingHash = game.LocalHash(_config.StateDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return new SwitchResult(false, $"The save folder of {game.Name} could not be read ({ex.Message}), so nothing was switched.");
        }
        var changed = outgoingHash != game.LastSyncedHash;
        StoredSnapshot? kept = null;
        if (mode != PlaythroughCopyMode.Off && (changed || mode == PlaythroughCopyMode.KeepForOffline))
        {
            var snapshotZip = TempArchive(game.GameId, "playthrough");
            try
            {
                SaveArchive.CreateArchive(game.Roots(_config.StateDir), snapshotZip, game.ExcludeGlobs);
                kept = store.Capture(game.GameId, currentId.Value, game.PlaythroughName, snapshotZip, game.LastKnownVersionId,
                    outgoingHash, uploaded: !changed);
                _log($"[{game.Name}] kept a copy of {outgoingName} on this device.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SaveArchive.UnsafeArchiveException)
            {
                // A copy that could not be made is not a reason to lose the switch's other protections, but it is one to stop
                // when the copy was the only thing standing between the save and a failed push.
                _log($"[{game.Name}] could not keep a copy of {outgoingName}: {ex.Message}");
            }
            finally { TryDeleteTemp(snapshotZip); }
        }

        var offline = !serverUp;
        var outgoingOnServer = serverUp && !changed;
        if (serverUp && changed)
        {
            var push = await PushAsync(game, force: false, settle: false, ct);
            switch (push?.Status)
            {
                case UploadStatus.Created or UploadStatus.NoChange:
                    outgoingOnServer = true;
                    _log($"[{game.Name}] {outgoingName} is saved on the server.");
                    if (kept is not null) FinishCopy(game, currentId.Value, push.Version?.Id ?? game.LastKnownVersionId, mode);
                    break;
                case UploadStatus.Conflict:
                    DropCopy(game, currentId.Value);
                    return new SwitchResult(false, $"{game.Name} has a conflict in {outgoingName}. Resolve it first, so no save is put in the wrong line.");
                default:
                    if (EmptyFolderHoldStore.For(_config).For(game.GameId) is not null)
                        return new SwitchResult(false, $"Every save file of {game.Name} is gone from this device, so nothing was switched. Delete it everywhere or restore it here first.");
                    if (_offlineQueue?.Contains(game.GameId) != true)
                        return new SwitchResult(false, $"The push of {outgoingName} was refused, so nothing was switched. The activity log says why.");
                    offline = true;   // the server stopped answering mid-way
                    break;
            }
        }
        if (offline)
        {
            if (kept is null)
            {
                return new SwitchResult(false,
                    $"Can't reach the server to save {outgoingName} first, and this device is not keeping copies, so nothing was switched.");
            }
            // The outgoing save goes up from the copy when the server is back. A queued push of the folder would send the
            // NEW playthrough's files to the wrong line, so the generic retry is dropped.
            _offlineQueue?.Remove(game.GameId);
            _log($"[{game.Name}] the server can't be reached; {outgoingName} will be pushed from this device's copy when it is back.");
        }

        // ---- 2. The target's latest goes into the folder ----
        PullOutcome? outcome = null;
        try
        {
            if (!offline)
            {
                outcome = await PullAsync(game, force: true, ct, skipDlcCheck: true, switchTo: target);
                if (outcome == PullOutcome.NothingOnServer)
                {
                    var fresh = await RestoreFreshAsync(game, target!, keepPaths, ct);
                    if (!fresh.Ok) return await UndoAsync(game, currentId.Value, kept, fresh.Message);
                }
                else if (outcome == PullOutcome.Unreachable)
                {
                    offline = true;
                }
                else if (outcome is not (PullOutcome.Restored or PullOutcome.UpToDate))
                {
                    return new SwitchResult(false, $"{game.Name} was not switched: {DescribePull(game, outcome.Value)}");
                }
            }
            if (offline)
            {
                var copy = store.Find(game.GameId, targetId);
                if (copy is null)
                {
                    return await UndoAsync(game, currentId.Value, kept,
                        $"Can't reach the server, and this device has no copy of {target?.Name ?? "that playthrough"} to switch to.");
                }
                target ??= new PlaythroughDto(copy.PlaythroughId, game.GameId, copy.PlaythroughName ?? "playthrough", false,
                    DateTime.UtcNow, null, null, null, 0, 0, false, []);
                var restored = await RestoreFromCopyAsync(game, copy, ct, followAs: target);
                if (!restored.Ok) return await UndoAsync(game, currentId.Value, kept, restored.Message);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
                                   ex is IOException or UnauthorizedAccessException or SaveArchive.UnsafeArchiveException or StoredCopyCorruptException or InvalidDataException)
        {
            return await UndoAsync(game, currentId.Value, kept, $"Restoring the save failed ({ex.Message}).");
        }

        // ---- 3. The selection is recorded: here (above) and on the server ----
        if (list is not null) game.PlaythroughCount = list.Count;
        var told = false;
        if (!offline)
        {
            try
            {
                var (ok, error) = await _api.SelectPlaythroughAsync(game.GameId, targetId, ct);
                told = ok;
                if (!ok) _log($"[{game.Name}] the server did not take the selection: {error}");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ServerReachability.IsUnreachable(ex)) { /* sent later */ }
        }
        game.PlaythroughSelectPending = !told;
        _config.SaveGameSyncState(game);

        try { store.Sweep(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log($"[{game.Name}] could not tidy the copies: {ex.Message}"); }

        var name = game.PlaythroughName ?? target?.Name ?? "the other playthrough";
        var message = offline
            ? $"{game.Name} is now on {name}, from the copy on this device (it may be older than the server's). " +
              (outgoingOnServer ? "The server is told the choice when it is back." : $"{outgoingName} and the choice are sent when the server is back.")
            : $"{game.Name} is now on {name}.";
        _log($"[{game.Name}] {message}");
        return new SwitchResult(true, message, offline, target);
    }

    /// <summary>The server confirmed the push: a copy kept only until then goes; a copy kept for offline use stays, now uploaded.</summary>
    private void FinishCopy(TrackedGame game, Guid playthroughId, Guid? versionId, PlaythroughCopyMode mode)
    {
        try
        {
            if (mode == PlaythroughCopyMode.KeepForOffline) Store.MarkUploaded(game.GameId, playthroughId, versionId);
            else Store.Remove(game.GameId, playthroughId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log($"[{game.Name}] could not update the kept copy: {ex.Message}"); }
    }

    private void DropCopy(TrackedGame game, Guid playthroughId)
    {
        try { Store.Remove(game.GameId, playthroughId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log($"[{game.Name}] could not remove the kept copy: {ex.Message}"); }
    }

    /// <summary>A failed switch: the device stays on the old playthrough. If the restore may have half-written the folder, the
    /// outgoing save is put back — from the copy taken first, else from the server, where the push put it.</summary>
    private async Task<SwitchResult> UndoAsync(TrackedGame game, Guid oldId, StoredSnapshot? kept, string reason)
    {
        _log($"[{game.Name}] switch failed: {reason}");
        var back = "";
        try
        {
            var stillHere = game.LocalHash(_config.StateDir);
            if (kept is not null && stillHere != kept.ContentHash)
            {
                var restored = await RestoreFromCopyAsync(game, kept, default);
                back = restored.Ok ? " Your save was put back." : $" Your save could not be put back from the copy ({restored.Message}); it is in {Path.Combine(_config.StateDir, "playthrough-store")}.";
            }
            else if (kept is null && game.LastSyncedHash is not null && stillHere != game.LastSyncedHash)
            {
                var outcome = await PullAsync(game, force: true);
                back = outcome is PullOutcome.Restored or PullOutcome.UpToDate ? " Your save was put back from the server." : "";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or StoredCopyCorruptException or InvalidDataException)
        {
            _log($"[{game.Name}] could not put the save back: {ex.Message}");
        }
        game.PlaythroughSelectPending = false;
        return new SwitchResult(false, $"{reason}{back} {game.Name} stays on {game.PlaythroughName ?? "its playthrough"}.");
    }

    /// <summary>Puts a kept copy into the save folder. The folder is only touched after every file of the copy verified; the game
    /// then follows the copy's version, so the next push carries it as its parent and any gap to the server is an ordinary
    /// conflict, never a silent overwrite.</summary>
    /// <param name="followAs">The playthrough the copy belongs to when this is a switch to it: the game follows it in the same save that
    /// records the copy's version, and the server is marked as not yet told.</param>
    private async Task<(bool Ok, string Message)> RestoreFromCopyAsync(
        TrackedGame game, StoredSnapshot copy, CancellationToken ct, PlaythroughDto? followAs = null)
    {
        if (RefuseUnsafePath(game, "pull")) return (false, "the save folder is not one SaveLocker may replace.");
        var archive = TempArchive(game.GameId, "copy");
        try
        {
            Store.BuildArchive(copy, archive);
            var (ran, result) = await WithGameLockAsync(game, () =>
            {
                if (GameActivity.IsActive(game)) return Task.FromResult((false, $"{game.Name} started, so its save was not replaced."));
                SaveArchive.RestoreArchive(archive, game.Roots(_config.StateDir), _tempDir, game.ExcludeGlobs);
                var local = game.LocalHash(_config.StateDir);
                game.LastKnownVersionId = copy.VersionId;
                game.LastSyncedHash = local == copy.ContentHash ? copy.ContentHash : local;
                game.ConsecutiveConflicts = 0;
                if (followAs is not null)
                {
                    FollowPlaythrough(game, followAs);
                    game.PlaythroughSelectPending = true;
                }
                _config.SaveGameSyncState(game, touchSyncTime: false);
                return Task.FromResult((true, ""));
            }, TimeSpan.FromSeconds(30), ct);
            return ran ? result : (false, $"A sync of {game.Name} is running. Try again when it is done.");
        }
        finally { TryDeleteTemp(archive); }
    }

    /// <summary>The target playthrough has no save yet (<i>Start fresh</i>): the folder is emptied except the files to keep, through
    /// the same restore a pull uses, so its checks and its delete pass apply. The first push after makes its first version.</summary>
    private async Task<(bool Ok, string Message)> RestoreFreshAsync(
        TrackedGame game, PlaythroughDto target, IReadOnlyList<string>? keepPaths, CancellationToken ct)
    {
        if (RefuseUnsafePath(game, "pull")) return (false, "the save folder is not one SaveLocker may replace.");
        var archive = TempArchive(game.GameId, "fresh");
        try
        {
            var roots = game.Roots(_config.StateDir);
            var keep = new List<string>(keepPaths ?? []);
            foreach (var root in roots.Where(r => !r.IsPrimary && Directory.Exists(r.Directory)))
                keep.Add(SaveArchive.MarkerName(root.Key));
            SaveArchive.CreateArchiveSubset(roots, archive, keep);
            var (ran, result) = await WithGameLockAsync(game, () =>
            {
                if (GameActivity.IsActive(game)) return Task.FromResult((false, $"{game.Name} started, so its save was not replaced."));
                SaveArchive.RestoreArchive(archive, roots, _tempDir, game.ExcludeGlobs);
                game.LastKnownVersionId = null;
                game.LastSyncedHash = game.LocalHash(_config.StateDir);
                game.ConsecutiveConflicts = 0;
                FollowPlaythrough(game, target);
                _config.SaveGameSyncState(game);
                return Task.FromResult((true, ""));
            }, TimeSpan.FromSeconds(30), ct);
            if (!ran) return (false, $"A sync of {game.Name} is running. Try again when it is done.");
            if (result.Item1) _log($"[{game.Name}] {target.Name} starts with {(keep.Count(k => !SaveArchive.IsMarker(k)) is var n and > 0 ? $"{n} kept file(s)" : "an empty save folder")}.");
            return result;
        }
        finally { TryDeleteTemp(archive); }
    }

    /// <summary>
    /// Makes a playthrough and, with <paramref name="playHere"/>, switches this device to it. <i>Current save</i> forks the latest save
    /// of <paramref name="fromPlaythroughId"/> (this device's own playthrough when null) without uploading anything again;
    /// <i>Start fresh</i> starts empty, keeping <paramref name="keepPaths"/>. Either way the current save is pushed first, so what
    /// the new line starts from — or leaves behind — is on the server.
    /// </summary>
    public async Task<SwitchResult> NewPlaythroughAsync(TrackedGame game, string name, Guid? fromPlaythroughId, bool startFresh,
        IReadOnlyList<string>? keepPaths, bool playHere, CancellationToken ct = default, Guid? fromVersionId = null)
    {
        if (CleanedName(name) is not { } cleaned)
            return new SwitchResult(false, $"A playthrough needs a name of 1 to {Playthroughs.MaxNameLength} characters.");
        PlaythroughDto? followed;
        try { followed = await FollowedAsync(game, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested && ServerReachability.IsUnreachable(ex))
        {
            return new SwitchResult(false, "A new playthrough needs the server. Try again when it is reachable.");
        }
        if (followed is null) return new SwitchResult(false, "This server does not have playthroughs yet. Update it first.");

        // What a fork starts from has to be on the server, and a fresh start must not lose what it leaves behind.
        if (game.IsEnrolledHere && fromVersionId is null && (startFresh || fromPlaythroughId is null || fromPlaythroughId == followed.Id))
        {
            var push = await PushAsync(game, force: false, settle: false, ct);
            if (push?.Status is not (UploadStatus.Created or UploadStatus.NoChange))
                return new SwitchResult(false, $"Your current save of {game.Name} could not be saved to the server first. {DescribePush(game, push)}");
        }

        var request = startFresh ? new CreatePlaythroughRequest(cleaned)
            : fromVersionId is { } version ? new CreatePlaythroughRequest(cleaned, FromVersionId: version)
            : new CreatePlaythroughRequest(cleaned, FromPlaythroughId: fromPlaythroughId ?? followed.Id);
        (PlaythroughDto? created, string? error) = await _api.CreatePlaythroughAsync(game.GameId, request, ct);
        if (created is null) return new SwitchResult(false, error ?? "The server refused the new playthrough.");
        _log($"[{game.Name}] made the playthrough {created.Name}.");
        game.PlaythroughCount = Math.Max(game.PlaythroughCount, 1) + 1;
        _config.SaveGameSyncState(game);

        if (!playHere) return new SwitchResult(true, $"Made {created.Name}. This device stays on {game.PlaythroughName ?? followed.Name}.", false, created);
        var switched = await SwitchPlaythroughAsync(game, created.Id, startFresh ? keepPaths : null, ct);
        return switched.Ok
            ? switched with { Message = $"Made {created.Name}. {switched.Message}", Playthrough = created }
            : new SwitchResult(false, $"Made {created.Name}, but this device was not switched to it. {switched.Message}", false, created);
    }

    private static string? CleanedName(string name) => Playthroughs.CleanName(name);

    /// <summary>Work that waits for the server (tasks/save-playthroughs Phase 4): saves kept at an offline switch that were never
    /// pushed, and selections the server was not told. Cheap to ask; called by the drainer's tick.</summary>
    public bool HasPlaythroughWork()
    {
        if (_config.Games.Any(g => g.PlaythroughSelectPending)) return true;
        try { return Store.PendingUploads().Count > 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Sends the saves kept at an offline switch to the playthrough they belong to (parent = the version they were made
    /// from, so a gap is an ordinary conflict), then the selections. Stops at the first sign the server is still away; a copy
    /// that cannot be read is reported and skipped, so it never holds back the others or the selections.</summary>
    public async Task FlushPlaythroughWorkAsync(CancellationToken ct = default)
    {
        if (IsRetired) return;
        foreach (var copy in Store.PendingUploads())
        {
            if (_config.Games.FirstOrDefault(g => g.GameId == copy.GameId) is not { } game) { Store.Remove(copy.GameId, copy.PlaythroughId); continue; }
            try
            {
                // The line this device is on: under the game's lock, so a push of the folder cannot run in between and the
                // device's parent moves with the upload (else its next push would conflict with its own save).
                if (game.PlaythroughId == copy.PlaythroughId)
                {
                    var (ran, _) = await WithGameLockAsync(game, () => FlushCopyAsync(game, copy, ct), GameLockTimeout, ct);
                    if (!ran) _log($"[{game.Name}] the kept save of {copy.PlaythroughName ?? "a playthrough"} waits for the sync in progress.");
                }
                else await FlushCopyAsync(game, copy, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ServerReachability.IsUnreachable(ex))
            {
                _log($"[{game.Name}] the kept save of {copy.PlaythroughName ?? "a playthrough"} was not pushed yet: {ex.Message}");
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is StoredCopyCorruptException or IOException or UnauthorizedAccessException)
            {
                Alert($"[{game.Name}] the save kept on this device for {copy.PlaythroughName ?? "a playthrough"} could not be pushed: {ex.Message} " +
                      $"It stays in {Path.Combine(_config.StateDir, "playthrough-store")}.",
                    AgentEventCodes.PushFailed, AgentEventSeverity.Error, game.GameId);
            }
        }

        foreach (var game in _config.Games.Where(g => g.PlaythroughSelectPending && g.PlaythroughId is not null).ToList())
        {
            try
            {
                var (ok, error) = await _api.SelectPlaythroughAsync(game.GameId, game.PlaythroughId!.Value, ct);
                if (!ok) _log($"[{game.Name}] the server did not take {game.PlaythroughName}: {error}");
                game.PlaythroughSelectPending = false;
                _config.SaveGameSyncState(game);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ServerReachability.IsUnreachable(ex)) { return; }
        }
        try { Store.Sweep(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* next time */ }
    }

    /// <summary>One kept save to the server. When it is the line this device is on (the caller holds the game's lock) and the
    /// folder still stands on the copy's base, the device's parent moves to what the upload made; if the device has moved on
    /// since (a push or pull of the folder), the copy is not this device's newest save of that line, so it is sent as it is
    /// and a gap is an ordinary conflict.</summary>
    private async Task<bool> FlushCopyAsync(TrackedGame game, StoredSnapshot copy, CancellationToken ct)
    {
        var archive = TempArchive(game.GameId, "kept");
        try
        {
            Store.BuildArchive(copy, archive);
            var result = await _api.UploadAsync(game.GameId, copy.ContentHash, copy.VersionId, force: false, archive, null, ct, copy.PlaythroughId);
            switch (result.Status)
            {
                case UploadStatus.Created or UploadStatus.NoChange:
                    _log($"[{game.Name}] pushed the save kept from {copy.PlaythroughName ?? "the other playthrough"}.");
                    if (game.PlaythroughId == copy.PlaythroughId && game.LastKnownVersionId == copy.VersionId && result.Version?.Id is { } made)
                    {
                        game.LastKnownVersionId = made;
                        _config.SaveGameSyncState(game, touchSyncTime: false);
                    }
                    FinishCopy(game, copy.PlaythroughId, result.Version?.Id, _config.PlaythroughCopies);
                    break;
                case UploadStatus.Conflict:
                    Alert($"[{game.Name}] CONFLICT in {copy.PlaythroughName ?? "the other playthrough"}: the save kept from this device diverged from the server's. " +
                          "Resolve it in the dashboard.", AgentEventCodes.Conflict, AgentEventSeverity.Error, game.GameId);
                    Store.MarkUploaded(game.GameId, copy.PlaythroughId, result.Version?.Id);
                    break;
            }
            return true;
        }
        finally { TryDeleteTemp(archive); }
    }
}
