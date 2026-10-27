using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SaveLocker.Shared;

namespace SaveLocker.Agent;

/// <summary>A save this device keeps of one playthrough (<see cref="PlaythroughStore"/>).</summary>
public sealed record StoredCopyDto(Guid PlaythroughId, string? Name, DateTime CapturedAt, bool Uploaded, long Size, int FileCount);

/// <summary>
/// What the game page's playthrough tabs show: the server's list, which one this device is on, and what it has copies of.
/// <paramref name="Offline"/> means the server did not answer: <paramref name="Playthroughs"/> is then empty, and a switch can
/// only go to a playthrough that has a copy here.
/// </summary>
public sealed record GamePlaythroughsDto(
    Guid? SelectedId, string? SelectedName, bool Offline, bool SelectPending, PlaythroughDto[] Playthroughs, StoredCopyDto[] Copies);

public sealed record SwitchPlaythroughResponse(bool Ok, string Message, bool Offline = false, Guid? PlaythroughId = null);

/// <summary>A new playthrough. <paramref name="FromPlaythroughId"/> null means this device's own; <paramref name="StartFresh"/> starts
/// empty instead, leaving <paramref name="KeepFiles"/> (archive names from <c>start-files</c>) in the folder. <paramref name="FromVersionId"/>
/// forks one particular version instead (a row of a playthrough's versions).</summary>
public sealed record NewPlaythroughBody(string Name, Guid? FromPlaythroughId, bool StartFresh, string[]? KeepFiles, bool PlayHere, Guid? FromVersionId = null);

public sealed record RenamePlaythroughBody(string Name);

/// <summary>A save file the <i>Start fresh</i> list offers to keep, ticked by default when it looks like a profile or settings file.</summary>
public sealed record StartFileDto(string Path, long Size, bool KeepByDefault);

public sealed record PlaythroughActionResponse(bool Ok, string Message, PlaythroughDto? Playthrough = null);

public sealed record PlaythroughCopiesDto(PlaythroughCopyMode Mode, long UsedBytes, StoredCopyGameDto[] NotUploaded);

/// <summary>A copy kept here that the server does not have yet — never cleared, since it is the only copy of that save.</summary>
public sealed record StoredCopyGameDto(Guid GameId, string GameName, Guid PlaythroughId, string? PlaythroughName);

public sealed record SetPlaythroughCopiesBody(PlaythroughCopyMode Mode);

public sealed partial class AgentApiServer
{
    private static readonly Regex SettingsLikeName = new(
        @"(profile|setting|config|option|pref)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Files a fresh start keeps unless told otherwise: the ones that carry the player's choices, not their progress.</summary>
    internal static bool LooksLikeSettingsFile(string archiveName)
    {
        var leaf = archiveName[(archiveName.LastIndexOf('/') + 1)..];
        var ext = Path.GetExtension(leaf);
        return SettingsLikeName.IsMatch(leaf)
            || ext.Equals(".ini", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cfg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".conf", StringComparison.OrdinalIgnoreCase);
    }

    private StoredCopyDto[] CopiesOf(Guid gameId) =>
        PlaythroughStore.For(_config).All(gameId)
            .Select(s => new StoredCopyDto(s.PlaythroughId, s.PlaythroughName, s.CapturedAt, s.Uploaded, s.TotalSize, s.Files.Count))
            .ToArray();

    private void MapPlaythroughs(WebApplication app)
    {
        // The tabs under a game: the server's playthroughs (the default first), the one this device follows, and what it keeps
        // copies of. An unreachable server is an answer (offline, no list), not an error.
        app.MapGet("/api/games/{id:guid}/playthroughs",
            async Task<Results<Ok<GamePlaythroughsDto>, NotFound, InternalServerError<ErrorResponse>>> (Guid id, CancellationToken ct) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null) return TypedResults.NotFound();
            try
            {
                var list = await ApiClient.For(_config).ListPlaythroughsAsync(id, ct);
                if (list is null)
                    return TypedResults.Ok(new GamePlaythroughsDto(game.PlaythroughId, game.PlaythroughName, false, false, [], CopiesOf(id)));
                _engine?.Invoke().RememberFollowed(game, list);
                return TypedResults.Ok(new GamePlaythroughsDto(
                    game.PlaythroughId, game.PlaythroughName, false, game.PlaythroughSelectPending, [.. list], CopiesOf(id)));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ServerReachability.IsUnreachable(ex))
            {
                return TypedResults.Ok(new GamePlaythroughsDto(
                    game.PlaythroughId, game.PlaythroughName, true, game.PlaythroughSelectPending, [], CopiesOf(id)));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                return TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
        });

        // Switch this device to another playthrough. Gated with the game page's own sync so the two never run together, and not
        // tied to the request: closing the page must not abort a restore half-way. The answer is the whole result; the activity
        // feed carries the steps while it runs.
        app.MapPost("/api/games/{id:guid}/playthroughs/{playthroughId:guid}/switch",
            async Task<Results<Ok<SwitchPlaythroughResponse>, Conflict<ErrorResponse>, NotFound, InternalServerError<ErrorResponse>>> (Guid id, Guid playthroughId) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || _engine is null) return TypedResults.NotFound();
            var gate = _gameSyncGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0))
                return TypedResults.Conflict(new ErrorResponse("A sync of this game is already running. Watch the activity feed."));
            try
            {
                var result = await _engine().SwitchPlaythroughAsync(game, playthroughId);
                return TypedResults.Ok(new SwitchPlaythroughResponse(result.Ok, result.Message, result.Offline, result.Playthrough?.Id));
            }
            catch (Exception ex)
            {
                AgentLogger.LogException("AgentApiServer.playthrough-switch", ex);
                return TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
            finally { gate.Release(); }
        });

        // A new playthrough, and — with PlayHere — this device switched to it, as one action.
        app.MapPost("/api/games/{id:guid}/playthroughs",
            async Task<Results<Ok<SwitchPlaythroughResponse>, Conflict<ErrorResponse>, NotFound, InternalServerError<ErrorResponse>>> (Guid id, NewPlaythroughBody body) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || _engine is null) return TypedResults.NotFound();
            var gate = _gameSyncGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0))
                return TypedResults.Conflict(new ErrorResponse("A sync of this game is already running. Watch the activity feed."));
            try
            {
                var result = await _engine().NewPlaythroughAsync(
                    game, body.Name, body.FromPlaythroughId, body.StartFresh, body.KeepFiles, body.PlayHere, fromVersionId: body.FromVersionId);
                return TypedResults.Ok(new SwitchPlaythroughResponse(result.Ok, result.Message, result.Offline, result.Playthrough?.Id));
            }
            catch (Exception ex)
            {
                AgentLogger.LogException("AgentApiServer.playthrough-new", ex);
                return TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
            finally { gate.Release(); }
        });

        // The files <i>Start fresh</i> can keep: what a push would carry, with the profile and settings files ticked.
        app.MapGet("/api/games/{id:guid}/playthroughs/start-files",
            Results<Ok<StartFileDto[]>, NotFound, InternalServerError<ErrorResponse>> (Guid id) =>
        {
            var game = _config.Games.FirstOrDefault(g => g.GameId == id);
            if (game is null || !game.IsEnrolledHere) return TypedResults.NotFound();
            try
            {
                var (files, _) = SaveArchive.ComputeManifest(game.Roots(_config.StateDir), game.ExcludeGlobs);
                return TypedResults.Ok(files
                    .Where(f => !SaveArchive.IsMarker(f.Path))
                    .Select(f => new StartFileDto(f.Path, f.Size, LooksLikeSettingsFile(f.Path)))
                    .ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return TypedResults.InternalServerError(new ErrorResponse(ex.Message));
            }
        });

        // Rename and archive sit in a tab's menu. Refused by the server when a device is on the playthrough, when it has an
        // open conflict, when someone is playing it, or when it is the default (which can't be archived).
        app.MapPost("/api/games/{id:guid}/playthroughs/{playthroughId:guid}/rename",
            async Task<Results<Ok<PlaythroughActionResponse>, NotFound, InternalServerError<ErrorResponse>>> (Guid id, Guid playthroughId, RenamePlaythroughBody body) =>
            await ChangePlaythroughAsync(id, playthroughId, "rename", body.Name));
        app.MapPost("/api/games/{id:guid}/playthroughs/{playthroughId:guid}/archive",
            async Task<Results<Ok<PlaythroughActionResponse>, NotFound, InternalServerError<ErrorResponse>>> (Guid id, Guid playthroughId) =>
            await ChangePlaythroughAsync(id, playthroughId, "archive", null));
        app.MapPost("/api/games/{id:guid}/playthroughs/{playthroughId:guid}/unarchive",
            async Task<Results<Ok<PlaythroughActionResponse>, NotFound, InternalServerError<ErrorResponse>>> (Guid id, Guid playthroughId) =>
            await ChangePlaythroughAsync(id, playthroughId, "unarchive", null));

        // Settings › Sync: how much this device keeps, and how much room it takes.
        app.MapGet("/api/settings/playthrough-copies", () => TypedResults.Ok(CopiesState()));
        app.MapPost("/api/settings/playthrough-copies", (SetPlaythroughCopiesBody body) =>
        {
            _config.UpdateSettings(c => c.PlaythroughCopies = body.Mode);
            return TypedResults.Ok(CopiesState());
        });
        // Clear copies: everything except saves never uploaded, which are the only copy of that save. Says which were kept.
        app.MapPost("/api/settings/playthrough-copies/clear", () =>
        {
            try { PlaythroughStore.For(_config).Clear(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AgentLogger.LogException("AgentApiServer.clear-copies", ex); }
            return TypedResults.Ok(CopiesState());
        });
    }

    private PlaythroughCopiesDto CopiesState()
    {
        var store = PlaythroughStore.For(_config);
        return new PlaythroughCopiesDto(
            _config.PlaythroughCopies,
            store.UsedBytes(),
            [.. store.PendingUploads().Select(s => new StoredCopyGameDto(
                s.GameId, _config.Games.FirstOrDefault(g => g.GameId == s.GameId)?.Name ?? "a game", s.PlaythroughId, s.PlaythroughName))]);
    }

    private async Task<Results<Ok<PlaythroughActionResponse>, NotFound, InternalServerError<ErrorResponse>>> ChangePlaythroughAsync(
        Guid gameId, Guid playthroughId, string action, string? name)
    {
        var game = _config.Games.FirstOrDefault(g => g.GameId == gameId);
        if (game is null) return TypedResults.NotFound();
        try
        {
            var (changed, error) = await ApiClient.For(_config).ChangePlaythroughAsync(gameId, playthroughId, action, name);
            if (changed is null) return TypedResults.Ok(new PlaythroughActionResponse(false, error ?? "The server refused."));
            if (game.PlaythroughId == changed.Id && game.PlaythroughName != changed.Name)
            {
                game.PlaythroughName = changed.Name;
                _config.SaveGameSyncState(game);
            }
            return TypedResults.Ok(new PlaythroughActionResponse(true, action switch
            {
                "rename" => $"Renamed to {changed.Name}.",
                "archive" => $"Archived {changed.Name}.",
                _ => $"Unarchived {changed.Name}.",
            }, changed));
        }
        catch (Exception ex) { return TypedResults.InternalServerError(new ErrorResponse(ex.Message)); }
    }
}
